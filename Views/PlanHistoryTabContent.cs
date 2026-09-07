using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// The Plan History tab: a list of previously-generated plans, newest
    /// first with pinned rows on top. A row expands in place from its
    /// caret, its icon or its name (free, frozen summary) and offers Open
    /// in Planner (restore the exact saved plan, pills live, no network)
    /// and Re-Solve in Planner (run the same request again at today's
    /// prices).
    ///
    /// Structurally a RankerTabContent-shaped tab: fixed chrome siblings
    /// plus one scrolling FlowPanel, held for the module's lifetime with
    /// BeginRebuild because the expanded-row selection and the in-flight
    /// re-solve state must survive a tab switch. All data flows through
    /// Module-owned delegates - this view never touches a store directly.
    /// </summary>
    internal class PlanHistoryTabContent
    {
        private static readonly Logger Logger = Logger.GetLogger<PlanHistoryTabContent>();

        private const int ToolbarHeight = 40;
        private const int ColumnHeaderRowHeight = PlanContentHeightMath.ColumnHeaderRowHeight;
        private const int ColumnHeaderLabelY = PlanContentHeightMath.ColumnHeaderLabelY;

        // No section band: this tab is named once, by the title band
        // Views/ViewAdapter draws above every tab's content.
        private const int TopChromeHeight = ToolbarHeight + ColumnHeaderRowHeight;

        private const int ScrollbarAllowance = WindowSizing.ScrollbarAllowance;
        private const int ClearButtonWidth = 120;

        // The one row seat that cannot live in PlanHistoryRowLayout beside
        // the others: the module's button height is Views-layer geometry,
        // and Services does not reach into Views. Same centring rule.
        private static readonly int MainLineButtonY =
            (PlanHistoryRowLayout.RowHeight - UiMetrics.ButtonHeight) / 2;

        // The remove X is a compact square, not an on-tab button, so it
        // centres on its own height rather than sharing the seat of the
        // text buttons beside it.
        private static readonly int RemoveButtonY =
            (PlanHistoryRowLayout.RowHeight - PlanHistoryRowLayout.IconButtonHeight) / 2;

        // Not a row: one prose line with no icon column, so it does not
        // follow the rows' frame-driven height. It does share their left
        // edge - it stands where the list would.
        private const int EmptyStateHeight = 44;

        private static readonly Color DimColor = new Color(150, 150, 150);

        /// <summary>The expand caret's ink, as the Recipe Tree draws it.</summary>
        private static readonly Color CaretColor = Color.White;

        private static readonly Color StatusColor = new Color(200, 200, 200);

        /// <summary>The expanded row's detail lines, at the same white
        /// every other standard label in the module uses.</summary>
        private static readonly Color DetailTextColor = Color.White;
        private static readonly Color ErrorColor = new Color(255, 100, 100);

        private const string EmptyStateText =
            "No plans generated yet. Generate a plan from the Crafting Plan tab and it will appear here.";

        private readonly Func<IReadOnlyList<PlanHistoryEntry>> _snapshotEntries;
        private readonly Action<Action<PlanHistoryIndex>> _mutateIndex;
        private readonly Func<PlanHistoryEntry, bool> _openEntry;

        // Returns null on success, an error message on failure; throws
        // OperationCanceledException on cancellation.
        private readonly Func<PlanHistoryEntry, CancellationToken, Task<string>> _resolveEntryAsync;
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;
        private readonly ItemStatWarmer _statWarmer;
        private readonly ModalDialog _modalDialog;
        private readonly ModuleSettings _settings;
        private readonly ResizeSettleDebounce _resizeSettle;

        private readonly List<RenderedRow> _rows = new List<RenderedRow>();
        private readonly List<SortableHeaderBlock> _columnHeaderLabels =
            new List<SortableHeaderBlock>();

        // Session-sticky, like the plan tables' own: a rebuild after a pin,
        // a delete or a fresh capture keeps whatever order the user chose,
        // and only a module reload clears it. Sorting overrides the
        // pin-first default (PlanHistoryTableSorter says why).
        private readonly TableSortState<PlanHistoryTableColumn> _sortState =
            new TableSortState<PlanHistoryTableColumn>();

        private HeaderCellPlan _headerCellPlan;

        private Panel _toolbarPanel;
        private Panel _columnHeaderPanel;
        private FlowPanel _contentPanel;
        private Label _statusLabel;
        private LoadingSpinner _spinner;
        private FeedbackButton _clearButton;

        private volatile bool _buildComplete;
        private int _lastLayoutWidth = -1;

        // Table-wide, so every row shares one column geometry and the
        // header labels sit on the columns they name.
        private int _costBandWidth;
        private int _whenBandWidth;

        private string _expandedEntryId;
        private bool _isResolving;
        private CancellationTokenSource _resolveCts;
        private string _statusOverride;
        private bool _statusIsError;

        public PlanHistoryTabContent(
            Func<IReadOnlyList<PlanHistoryEntry>> snapshotEntries,
            Action<Action<PlanHistoryIndex>> mutateIndex,
            Func<PlanHistoryEntry, bool> openEntry,
            Func<PlanHistoryEntry, CancellationToken, Task<string>> resolveEntryAsync,
            ModalDialog modalDialog,
            ModuleSettings settings,
            Func<int, ItemStatBlock> getItemStatBlock = null,
            // The twin of getItemStatBlock, and the reason this tab's
            // hovers are not identity-only: the accessor above is a pure
            // cache read that never fetches, so without this a history row
            // could only show a full item tooltip for an item some earlier
            // plan happened to touch. See ItemStatWarmer.
            Func<IReadOnlyList<int>, Task<int>> warmItemStatsAsync = null)
        {
            _snapshotEntries = snapshotEntries ?? (() => new List<PlanHistoryEntry>());
            _mutateIndex = mutateIndex ?? (_ => { });
            _openEntry = openEntry ?? (_ => false);
            _resolveEntryAsync = resolveEntryAsync;
            _modalDialog = modalDialog;
            _settings = settings;
            _getItemStatBlock = getItemStatBlock;
            _statWarmer = new ItemStatWarmer(warmItemStatsAsync, "history");

            _resizeSettle = new ResizeSettleDebounce(
                RefitAfterResizeSettle,
                MainThreadMarshal.Run,
                ResizeSettleDebounce.DefaultSettleMs,
                ex => Logger.Warn(ex, "Plan History row re-fit wait failed"));
        }

        /// <summary>Main thread, immediately before Blish queues the off-thread Build.</summary>
        public void BeginRebuild()
        {
            _buildComplete = false;
            if (!_isResolving)
            {
                // A transient status ("Entry deleted") should not outlive
                // the visit that caused it; the in-flight re-solve status
                // must.
                _statusOverride = null;
            }

            // Off-thread, and deliberately not awaited: the rows render
            // from their own persisted name/icon/rarity either way, and
            // their hovers are deferred, so whatever this fills in shows on
            // the next hover without a re-render. Started here rather than
            // in Build because Build runs off the main thread and may be
            // skipped entirely when nothing changed.
            // Bounded by the retention cap, so this is one batched request
            // on the first visit and nothing at all on later ones -
            // WarmStatBlocksAsync skips every id the cache already holds.
            _statWarmer.Start(PlanHistoryItemIds.ForEntries(_snapshotEntries()));
        }

        public void Build(Container container)
        {
            _buildComplete = false;
            _rows.Clear();
            _columnHeaderLabels.Clear();
            _lastLayoutWidth = -1;

            int w = container.ContentRegion.Width;

            BuildToolbar(container, w);
            BuildColumnHeader(container, w);

            _contentPanel = new FlowPanel
            {
                Size = new Point(w, Math.Max(0, container.ContentRegion.Height - TopChromeHeight)),
                Location = new Point(0, TopChromeHeight),
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                CanScroll = true,
                Parent = container,
            };

            PositionChrome(container, w);

            container.Resized += (_, __) =>
            {
                if (!_buildComplete)
                {
                    return;
                }

                PositionChrome(container, container.ContentRegion.Width);
                RefitRows();
            };

            // Build() runs on a ThreadPool thread; every control touch
            // below lands on the main thread, and _buildComplete is set
            // inside the same queued callback so no entry point can
            // observe a half-built tab.
            MainThreadMarshal.Run(() =>
            {
                RebuildRows();

                // A tab switch during a re-solve rebuilds the chrome from
                // scratch, so the in-flight state has to be restamped.
                if (_isResolving)
                {
                    _spinner.Visible = true;
                    SetControlsEnabled(false);
                }

                _buildComplete = true;
            });
        }

        /// <summary>
        /// Re-reads the index and rebuilds the list. Called on tab switch
        /// and (via MainThreadMarshal) after a capture lands while the tab
        /// is live. No-ops until Build has completed.
        /// </summary>
        public void Refresh()
        {
            if (!_buildComplete || !IsLive)
            {
                return;
            }

            RebuildRows();

            if (_isResolving)
            {
                _spinner.Visible = true;
                SetControlsEnabled(false);
            }
        }

        public void Teardown()
        {
            try
            {
                _resolveCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }

            _resizeSettle.Cancel();
        }

        private bool IsLive => _contentPanel != null && _contentPanel.Parent != null;

        // ---------------------------------------------------------------
        // Chrome
        // ---------------------------------------------------------------
        private void BuildToolbar(Container container, int width)
        {
            _toolbarPanel = new Panel
            {
                Size = new Point(width, ToolbarHeight),
                Parent = container,
            };

            _statusLabel = new Label
            {
                Font = UiFonts.Status,
                Text = "",
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                TextColor = StatusColor,
                Location = new Point(PlanHistoryRowLayout.Inset, 8),
                Parent = _toolbarPanel,
            };

            _spinner = InlineSpinner.Create(_toolbarPanel, InlineSpinnerLayout.SnapshotStatusSize);

            _clearButton = new FeedbackButton
            {
                Text = "Clear History",
                Size = new Point(ClearButtonWidth, UiMetrics.ButtonHeight),
                Location = new Point(0, 6),
                Parent = _toolbarPanel,
            };
            TooltipFacility.ApplyPlain(_clearButton, "Remove every unpinned entry. Pinned entries are kept.");
            _clearButton.Click += (_, __) => OnClearHistoryClicked();
        }

        /// <summary>
        /// The three columns, labelled and clickable. All three sort: a
        /// history list is read to answer "which plan", "how much did it
        /// cost" and "when did I run it", and only the last of those is the
        /// order the list already arrives in.
        /// </summary>
        private void BuildColumnHeader(Container container, int width)
        {
            _columnHeaderPanel = HeaderBands.CreateColumnHeaderBand(
                container, width, 0, ToolbarHeight);

            var cells = new SortableHeaderCells(_columnHeaderPanel);
            _headerCellPlan = new HeaderCellPlan(HeaderColumns.Length, cells);
            for (int i = 0; i < HeaderColumns.Length; i++)
            {
                var column = HeaderColumns[i];
                var block = SortableHeaderBlock.Create(
                    _columnHeaderPanel, HeaderBands.Font, HeaderBands.LabelColor,
                    ColumnHeaderLabelY, HeaderTitles[i], _sortState.DirectionFor(column));
                SortableHeaderLabel.MarkSortable(block.Title);
                SortableHeaderLabel.MarkSortable(block.IndicatorLabel);
                _columnHeaderLabels.Add(block);
                _headerCellPlan.Set(
                    i, block.Title, block.Width, () => SortBy(column), block.IndicatorLabel);
            }
        }

        private static readonly PlanHistoryTableColumn[] HeaderColumns =
        {
            PlanHistoryTableColumn.Plan,
            PlanHistoryTableColumn.Cost,
            PlanHistoryTableColumn.Generated,
        };

        private static readonly string[] HeaderTitles = { "Plan", "Cost", "Generated" };

        /// <summary>Cycles one column and rebuilds the list in the new
        /// order. A full rebuild, not a re-place: these rows carry an
        /// expansion panel whose height the flow owns, and the expanded row
        /// is restored by id.</summary>
        private void SortBy(PlanHistoryTableColumn column)
        {
            _sortState.Cycle(column);
            for (int i = 0; i < _columnHeaderLabels.Count; i++)
            {
                _columnHeaderLabels[i].SetDirection(_sortState.DirectionFor(HeaderColumns[i]));
            }

            RebuildRows();
        }

        private void PositionChrome(Container container, int width)
        {
            int height = container.ContentRegion.Height;
            int barWidth = Math.Max(0, width - ScrollbarAllowance);

            _toolbarPanel.Size = new Point(width, ToolbarHeight);
            _columnHeaderPanel.Size = new Point(width, ColumnHeaderRowHeight);

            _clearButton.Location = new Point(
                Math.Max(0, barWidth - PlanHistoryRowLayout.Inset - ClearButtonWidth),
                _clearButton.Location.Y);

            int statusRight = _clearButton.Location.X - InlineSpinnerLayout.SnapshotStatusSize
                - 2 * InlineSpinnerLayout.LabelGap;
            _statusLabel.Width = Math.Max(0, statusRight - PlanHistoryRowLayout.Inset);
            InlineSpinner.PlaceAfter(_spinner, _statusLabel, InlineSpinnerLayout.LabelGap);

            PositionColumnHeader(barWidth);

            _contentPanel.Size = new Point(width, Math.Max(0, height - TopChromeHeight));
            _contentPanel.Location = new Point(0, TopChromeHeight);
        }

        private void PositionColumnHeader(int barWidth)
        {
            var bands = BandsFor(barWidth);

            // Plan is the flexing label column and reads from its left
            // edge; the two data columns centre on their own axes, so a
            // short header sits over the values it names instead of only
            // meeting them at one edge (JustifiedColumnTracks).
            SetHeaderLabel(0, bands.CaretX);
            SetHeaderLabelCentered(1, bands.CostCenterX);
            SetHeaderLabelCentered(2, bands.WhenCenterX);

            // Each cell owns its column, so a click anywhere in it sorts -
            // the split every other table in the module uses.
            if (_headerCellPlan != null)
            {
                _headerCellPlan.SetBoundary(0, bands.CostCenterX - (_costBandWidth / 2));
                _headerCellPlan.SetBoundary(1, bands.WhenCenterX - (_whenBandWidth / 2));
                _headerCellPlan.Sync(_columnHeaderPanel.Width);
            }
        }

        private void SetHeaderLabel(int index, int x)
        {
            if (index < _columnHeaderLabels.Count)
            {
                _columnHeaderLabels[index].MoveTo(x);
            }
        }

        private void SetHeaderLabelCentered(int index, int centerX)
        {
            if (index >= _columnHeaderLabels.Count)
            {
                return;
            }

            // The whole BLOCK centres, indicator included: centring the word
            // alone would leave the pair sitting right of its own axis.
            var block = _columnHeaderLabels[index];
            block.MoveTo(Math.Max(0, centerX - (block.Width / 2)));
        }

        // ---------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------
        private class RenderedRow
        {
            public PlanHistoryEntry Entry;
            public string FullLabel;
            public Panel Panel;
            public IconNameRowHelpers.IconNameHandle IconName;
            public CoinCurrencyRenderer.ValueCellHandle CostCell;
            public Label WhenLabel;
            public Label Caret;
            public Panel CaretHit;
            public FeedbackButton Open;
            public FeedbackButton Resolve;
            public Checkbox Pin;
            public CloseKeyButton Delete;
            public Panel DetailPanel;
            public readonly List<Label> DetailFlexLabels = new List<Label>();
            public readonly List<string> DetailFlexFulls = new List<string>();

            /// <summary>Parallel to the two above: a detail line that is an
            /// ITEM NAME carries no tooltip of its own at any width - the
            /// item's icon beside it is the one control that answers for it
            /// (ItemIconTooltip.StampOnIconTree). The settings, note and
            /// timestamp lines keep their own truncation text.</summary>
            public readonly List<bool> DetailFlexSilent = new List<bool>();
        }

        private void RebuildRows()
        {
            if (_contentPanel == null)
            {
                return;
            }

            // Dispose, not ClearChildren: ClearChildren only detaches
            // (docs/ARCHITECTURE.md), so every rebuild - which fires on
            // every tab switch and after every capture - would orphan up
            // to PlanHistoryMaxEntries row trees to the GC undisposed.
            // Same idiom as RichTooltipSurface.DisposeContent.
            foreach (var child in _contentPanel.Children.ToArray())
            {
                child.Dispose();
            }

            _rows.Clear();

            int barWidth = Math.Max(0, _contentPanel.Width - ScrollbarAllowance);
            _lastLayoutWidth = _contentPanel.Width;

            var entries = PlanHistoryTableSorter.Sort(
                PlanHistoryRetention.SortForDisplay(_snapshotEntries()), _sortState);

            // Drop a stale expansion whose row is gone (deleted/evicted).
            if (_expandedEntryId != null
                && !entries.Any(e => string.Equals(e.EntryId, _expandedEntryId, StringComparison.Ordinal)))
            {
                _expandedEntryId = null;
            }

            MeasureBandWidths(entries);

            if (entries.Count == 0)
            {
                BuildEmptyState();
            }
            else
            {
                var bands = BandsFor(barWidth);
                foreach (var entry in entries)
                {
                    _rows.Add(CreateRow(entry, barWidth, bands));
                }
            }

            // The band widths this pass measured are what the header
            // centres on and what its click cells are split at, so the
            // header is re-placed here rather than left on the previous
            // list's measurements until the next resize.
            PositionColumnHeader(barWidth);

            _clearButton.Enabled = !_isResolving && entries.Count > 0;
            UpdateStatusLine();
        }

        private void BuildEmptyState()
        {
            // Wrapped in a Panel because the FlowPanel owns its direct
            // children's positions - a bare Label's (8, 8) would be
            // overridden by the flow.
            int barWidth = Math.Max(0, _contentPanel.Width - ScrollbarAllowance);
            var panel = new Panel
            {
                Size = new Point(barWidth, EmptyStateHeight),
                Parent = _contentPanel,
            };

            new Label
            {
                Font = UiFonts.Body,
                Text = EmptyStateText,
                TextColor = DimColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(PlanHistoryRowLayout.Inset, 8),
                Parent = panel,
            };
        }

        /// <summary>
        /// Widest coin cell and widest timestamp across the whole table.
        /// Pure measurement, no controls built.
        /// </summary>
        private void MeasureBandWidths(IReadOnlyList<PlanHistoryEntry> entries)
        {
            var measure = LabelHelpers.MeasureWith(UiFonts.Body);
            int cost = 0;
            int when = 0;
            foreach (var entry in entries)
            {
                int costWidth = CoinCurrencyRenderer.MeasureValueWidth(
                    entry.TotalCoinCostAtGeneration, null, UiFonts.Body);
                if (costWidth > cost)
                {
                    cost = costWidth;
                }

                int whenWidth = measure(WhenText(entry));
                if (whenWidth > when)
                {
                    when = whenWidth;
                }
            }

            _costBandWidth = cost;
            _whenBandWidth = when;
        }

        private PlanHistoryRowLayout.Bands BandsFor(int barWidth)
        {
            return PlanHistoryRowLayout.Compute(barWidth, _costBandWidth, _whenBandWidth);
        }

        private static string WhenText(PlanHistoryEntry entry)
        {
            return entry.LastGeneratedAtUtc.ToLocalTime()
                .ToString(StatusText.TimestampFormat, CultureInfo.InvariantCulture);
        }

        private RenderedRow CreateRow(PlanHistoryEntry entry, int barWidth, in PlanHistoryRowLayout.Bands bands)
        {
            var row = new RenderedRow
            {
                Entry = entry,
                FullLabel = PlanHistoryLabels.RowLabel(entry),
            };

            row.Panel = new Panel
            {
                Size = new Point(barWidth, PlanHistoryRowLayout.RowHeight),
                Parent = _contentPanel,
            };

            var firstSummary = FirstSummary(entry);
            string firstRarity = ResolvedRarity(firstSummary);
            var hover = RowHover(entry, firstSummary, firstRarity);

            bool expanded = string.Equals(_expandedEntryId, entry.EntryId, StringComparison.Ordinal);
            CreateCaret(row, expanded, bands);

            row.IconName = IconNameRowHelpers.CreateIconAndEllipsizedName(
                row.Panel, firstSummary?.IconUrl, firstRarity,
                bands.IconX, PlanHistoryRowLayout.IconY, row.FullLabel, UiFonts.Body,
                bands.NameX + bands.NameWidth, 0, 0, bands.NameX, PlanHistoryRowLayout.MainLineTextY,
                ItemIconTier.BagSlot, hover);

            row.CostCell = CoinCurrencyRenderer.RenderValueCellRightAligned(
                row.Panel, entry.TotalCoinCostAtGeneration, null, bands.CostRightEdge,
                PlanHistoryRowLayout.MainLineTextY, UiFonts.Body);

            row.WhenLabel = LabelHelpers.CreateRightAlignedLabel(
                row.Panel, WhenText(entry), UiFonts.Body, StatusColor,
                bands.WhenRightEdge, PlanHistoryRowLayout.MainLineTextY);

            // The caret, the icon and the name are one affordance: all
            // three toggle the same panel, so the biggest targets on the
            // row do what the caret advertises. Wired per control rather
            // than on row.Panel because Blish delivers a click to the
            // deepest control under the cursor and never bubbles - a
            // handler on the panel alone would answer only the gaps.
            string entryId = entry.EntryId;
            WireExpandTarget(row.CaretHit, entryId);
            WireExpandTarget(row.IconName.IconFrame, entryId);
            WireExpandTarget(row.IconName.NameLabel, entryId);

            // Omitted, not disabled, when the saved plan is gone. The
            // cluster is right-anchored, so the empty slot reads as
            // whitespace to the left of Re-Solve, not as a dead button.
            if (entry.BlobPresent)
            {
                row.Open = CreateActionButton(row.Panel, "Open in Planner", bands.OpenX,
                    "Load this exact saved plan into the Crafting Plan tab, with its decision pills, "
                        + "at the prices it was generated with. Replaces the plan currently shown there.");
                row.Open.Click += (_, __) => OnOpenClicked(entry);
            }

            row.Resolve = CreateActionButton(row.Panel, "Re-Solve in Planner", bands.ResolveX,
                "Run this same request again at current prices and show it in the Crafting Plan tab. "
                    + "Replaces the plan currently shown there. Manual decision overrides are not restored.");
            row.Resolve.Click += (_, __) => OnResolveClicked(entry);

            // The pin seat used to carry U+25CF/U+25CB, which the shipped
            // font does not have - they draw nothing and advance zero
            // pixels, so the pinned state had no representation at all
            // (KNOWN-ISSUES #64). A Checkbox rather than a button wearing
            // an icon, because a Checkbox brings its own state art.
            row.Pin = CreatePinToggle(row.Panel, entry.Pinned, bands.PinX);
            row.Pin.CheckedChanged += (_, __) => OnPinClicked(entry);

            row.Delete = CreateRemoveButton(row.Panel, bands.DeleteX);
            row.Delete.Click += (_, __) => OnDeleteClicked(entry);

            if (_isResolving)
            {
                SetRowEnabled(row, false);
            }

            if (expanded)
            {
                row.DetailPanel = BuildDetailPanel(row, entry, barWidth, bands);
            }

            return row;
        }

        private static PlanHistoryItemSummary FirstSummary(PlanHistoryEntry entry)
        {
            if (entry.ItemSummaries == null)
            {
                return null;
            }

            foreach (var summary in entry.ItemSummaries)
            {
                if (summary != null)
                {
                    return summary;
                }
            }

            return null;
        }

        /// <summary>
        /// One history row's hover: the icon+name header the row already
        /// draws - which is the FIRST item's, quantity and all - then the
        /// rest of the plan's items and its override/ignored counts.
        /// <para>
        /// The stat body belongs to a ONE-ITEM entry only, where the header
        /// item IS the request. A multi-item entry keeps none: claiming one
        /// item's stats for a three-item request would be a lie the icon
        /// does not tell.
        /// </para>
        /// </summary>
        private ItemIconTooltip RowHover(
            PlanHistoryEntry entry, PlanHistoryItemSummary firstSummary, string firstRarity)
        {
            var itemLines = PlanHistoryLabels.ItemLineTexts(entry);
            var extras = new List<string>();
            for (int i = 1; i < itemLines.Count; i++)
            {
                extras.Add(itemLines[i]);
            }

            if (entry.OverrideCountAtGeneration > 0)
            {
                extras.Add(StatusText.ForOverridesChip(entry.OverrideCountAtGeneration));
            }

            if (entry.IgnoredCountAtGeneration > 0)
            {
                extras.Add(StatusText.ForIgnoredChip(entry.IgnoredCountAtGeneration));
            }

            // An entry whose summaries were never captured heads nothing
            // rather than inventing a subject: there is no item to name and
            // no icon to show, and the chips below are the whole hover.
            var identity = itemLines.Count > 0
                ? ItemTooltipIdentity.ForItem(itemLines[0], firstSummary?.IconUrl, firstRarity)
                : ItemTooltipIdentity.Unnamed();

            // Without this a one-item entry composed to the header alone:
            // no stats, and no extras either, because its one item IS the
            // header and most entries carry no chips.
            int singleItemId = PlanHistoryLabels.SingleItemId(entry);
            Func<ItemStatBlock> stats = _getItemStatBlock == null || singleItemId <= 0
                ? (Func<ItemStatBlock>)null
                : () => _getItemStatBlock(singleItemId);

            return ItemIconTooltip.ForItem(identity, stats, () => extras);
        }

        /// <summary>
        /// The ONE rarity a summary renders at - its captured value, else
        /// whatever this session's stat cache holds. Fed to the icon frame,
        /// the name colour and the hover header alike, so the three cannot
        /// disagree.
        /// </summary>
        private string ResolvedRarity(PlanHistoryItemSummary summary)
        {
            if (summary == null)
            {
                return null;
            }

            var block = _getItemStatBlock == null || summary.ItemId <= 0
                ? null
                : _getItemStatBlock(summary.ItemId);
            return ItemRarityResolution.Resolve(summary.Rarity, block?.Rarity);
        }

        private FeedbackButton CreateActionButton(Panel parent, string text, int x, string tooltip)
        {
            var button = new FeedbackButton
            {
                Text = text,
                Size = new Point(PlanHistoryRowLayout.ActionButtonWidth, UiMetrics.ButtonHeight),
                Location = new Point(x, MainLineButtonY),
                Parent = parent,
            };
            TooltipFacility.ApplyPlain(button, tooltip);
            return button;
        }

        // Blish's own window close control, which is what the Ranker's
        // identical action draws (Views/Rendering/CloseKeyButton). No
        // explicit Size: the control is born at that box.
        private CloseKeyButton CreateRemoveButton(Panel parent, int x)
        {
            var button = new CloseKeyButton
            {
                Location = new Point(x, RemoveButtonY),
                Parent = parent,
            };
            TooltipFacility.ApplyPlain(button, "Remove this entry from the history.");
            return button;
        }

        // The Recipe Tree's own expand affordance, from the module's glyph
        // page, degrading to ASCII exactly where that page does. The glyph
        // rides inside a transparent hit plate rather than being clicked
        // directly: a caret glyph is ~7px wide on a 60px row, and the strip
        // between it and the icon answered nothing at all
        // (PlanHistoryRowLayout.CaretHitPadX). The plate carries the hover
        // as well as the click, because Blish resolves a tooltip on the
        // deepest control under the cursor and never bubbles.
        private static void CreateCaret(
            RenderedRow row, bool expanded, in PlanHistoryRowLayout.Bands bands)
        {
            string tooltip = expanded
                ? "Collapse this plan."
                : "Expand this plan to see what it cost when it was generated. "
                    + "Nothing is recalculated.";

            row.CaretHit = new Panel
            {
                Size = new Point(bands.CaretHitWidth, PlanHistoryRowLayout.CaretHitHeight),
                Location = new Point(bands.CaretHitX, PlanHistoryRowLayout.CaretHitY),
                Parent = row.Panel,
            };
            TooltipFacility.ApplyPlain(row.CaretHit, tooltip);

            row.Caret = new Label
            {
                Font = UiFonts.BodyGlyphs,
                Text = UiGlyphs.ExpandCaret(expanded, UiFonts.GlyphsAvailable),
                TextColor = CaretColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(
                    bands.CaretX - bands.CaretHitX,
                    PlanHistoryRowLayout.MainLineTextY - PlanHistoryRowLayout.CaretHitY),
                Parent = row.CaretHit,
            };
            TooltipFacility.ApplyPlain(row.Caret, tooltip);
        }

        /// <summary>
        /// Makes one control - and everything nested inside it, since Blish
        /// resolves a click on the deepest control under the cursor and
        /// never bubbles - toggle the row's detail panel.
        /// </summary>
        private void WireExpandTarget(Control control, string entryId)
        {
            if (control == null)
            {
                return;
            }

            control.Click += (_, __) => ToggleDetail(entryId);

            var container = control as Container;
            if (container == null)
            {
                return;
            }

            foreach (var child in container.Children.ToArray())
            {
                WireExpandTarget(child, entryId);
            }
        }

        // Checkbox centres its 32px state art on Height/2, so handing it
        // the button height sits it on the same line as the buttons beside
        // it. Checked is set here and the handler wired by the caller, so
        // construction cannot fire a pin toggle back at the store.
        private Checkbox CreatePinToggle(Panel parent, bool pinned, int x)
        {
            var toggle = new Checkbox
            {
                Text = "Pin",
                Checked = pinned,
                Size = new Point(PlanHistoryRowLayout.PinToggleWidth, UiMetrics.ButtonHeight),
                Location = new Point(x, MainLineButtonY),
                Parent = parent,
            };
            TooltipFacility.ApplyPlain(
                toggle,
                "Keep this entry. Pinned entries are never trimmed automatically, "
                    + "and Clear History leaves them alone.");
            return toggle;
        }

        // ---------------------------------------------------------------
        // Detail panel
        // ---------------------------------------------------------------
        private Panel BuildDetailPanel(
            RenderedRow row, PlanHistoryEntry entry, int barWidth, in PlanHistoryRowLayout.Bands bands)
        {
            var itemLines = PlanHistoryLabels.ItemLineTexts(entry);
            bool hasChips = entry.OverrideCountAtGeneration > 0 || entry.IgnoredCountAtGeneration > 0;
            bool hasBlobNote = !entry.BlobPresent;
            bool hasOverridesNote = entry.OverrideCountAtGeneration > 0;
            long sampleDelta = SampleDelta(entry, out DateTime previousSampleAtUtc);
            bool hasSampleLine = sampleDelta != 0;

            int itemLineCount = PlanHistoryRowLayout.DetailItemLineCount(itemLines.Count);
            int height = PlanHistoryRowLayout.DetailHeight(
                itemLineCount, hasChips, hasSampleLine, hasBlobNote, hasOverridesNote);

            // A direct sibling inserted after the row inside the same
            // FlowPanel, so the flow handles the reflow.
            var panel = new Panel
            {
                Size = new Point(barWidth, height),
                Parent = _contentPanel,
            };

            int rightEdge = Math.Max(0, barWidth - PlanHistoryRowLayout.Inset);
            int x = bands.NameX;
            int y = PlanHistoryRowLayout.DetailPadding / 2;

            // Empty for a single-item plan, which the row above already
            // headlines - PlanHistoryRowLayout.DetailItemLineCount, which
            // the panel's height was measured from.
            var summaries = itemLineCount > 0 && entry.ItemSummaries != null
                ? entry.ItemSummaries
                : new List<PlanHistoryItemSummary>();
            int line = 0;
            foreach (var summary in summaries)
            {
                if (summary == null)
                {
                    continue;
                }

                string rarity = ResolvedRarity(summary);
                int summaryItemId = summary.ItemId;
                string full = line < itemLines.Count ? itemLines[line] : "";

                // A detail line IS one item, so it gets the standard item
                // hover in full - the icon+name header either way, and this
                // session's stat block underneath it when there is one.
                var lineHover = ItemIconTooltip.ForItem(
                    ItemTooltipIdentity.ForItem(full, summary.IconUrl, rarity),
                    _getItemStatBlock == null || summaryItemId <= 0 ? (Func<ItemStatBlock>)null
                        : () => _getItemStatBlock(summaryItemId));

                IconControls.CreateItemIcon(
                    panel, summary.IconUrl, ItemIconFrame.ForRarity(rarity),
                    x, y + PlanHistoryRowLayout.IconPad, ItemIconTier.BagSidebar, lineHover);

                int textX = x + PlanHistoryRowLayout.DetailIconTotal + PlanHistoryRowLayout.IconGap;

                // The item's own rarity colour, as the row above it and
                // every other item name in the module takes: an unknown
                // rarity resolves to the same 200-grey these lines used to
                // be pinned at, so nothing dims - a KNOWN rarity stops
                // being thrown away.
                AddFlexLabel(
                    row, panel, full, UiFonts.Body, textX,
                    y + PlanHistoryRowLayout.DetailItemTextY, rightEdge,
                    RarityColors.GetRarityNameColor(rarity), silent: true);
                y += PlanHistoryRowLayout.DetailItemLineHeight;
                line++;
            }

            AddFlexLabel(
                row, panel,
                PlanHistoryLabels.SettingsLine(entry.UseOwnMaterials, entry.PriceBasis, entry.ValueOwnMaterials),
                UiFonts.Caption, x, y + 3, rightEdge);
            y += PlanHistoryRowLayout.DetailSettingsLineHeight;

            if (hasChips)
            {
                int chipX = x;
                if (entry.OverrideCountAtGeneration > 0)
                {
                    string chipText = StatusText.ForOverridesChip(entry.OverrideCountAtGeneration);
                    var chip = LabelHelpers.CreateSmallTag(panel, chipText, chipX, y, DimColor, DimColor * 0.15f);
                    chipX += LabelHelpers.MeasureSmallTagWidth(chipText) + TreeChipStripLayout.ChipGap;
                }

                if (entry.IgnoredCountAtGeneration > 0)
                {
                    LabelHelpers.CreateSmallTag(
                        panel, StatusText.ForIgnoredChip(entry.IgnoredCountAtGeneration),
                        chipX, y, DimColor, DimColor * 0.15f);
                }

                y += PlanHistoryRowLayout.DetailChipsLineHeight;
            }

            AddFlexLabel(
                row, panel,
                StatusText.Stamp("Generated", entry.CreatedAtUtc.ToLocalTime()),
                UiFonts.Caption, x, y + 2, rightEdge);
            y += PlanHistoryRowLayout.DetailCaptionLineHeight;

            if (hasSampleLine)
            {
                BuildSampleLine(panel, sampleDelta, previousSampleAtUtc, x, y + 2);
                y += PlanHistoryRowLayout.DetailNoteLineHeight;
            }

            if (hasBlobNote)
            {
                AddFlexLabel(
                    row, panel, "Not saved in full - Re-solve to rebuild it.",
                    UiFonts.Caption, x, y + 2, rightEdge);
                y += PlanHistoryRowLayout.DetailNoteLineHeight;
            }

            if (hasOverridesNote)
            {
                AddFlexLabel(
                    row, panel,
                    StatusText.ForOverridesChip(entry.OverrideCountAtGeneration)
                        + " - restored by Open, not by Re-solve.",
                    UiFonts.Caption, x, y + 2, rightEdge);
            }

            return panel;
        }

        /// <summary>
        /// A detail line that re-ellipsizes on resize. color overrides the
        /// default, which is DetailTextColor for every line the panel owns.
        /// </summary>
        private void AddFlexLabel(
            RenderedRow row, Panel panel, string full, BitmapFont font,
            int x, int y, int rightEdge, Color? color = null, bool silent = false)
        {
            string shown = LabelHelpers.EllipsizeToWidth(font, full, Math.Max(0, rightEdge - x));
            var label = new Label
            {
                Font = font,
                Text = shown,
                TextColor = color ?? DetailTextColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(x, y),
                Parent = panel,
            };
            if (!silent)
            {
                TooltipFacility.ApplyPlain(
                    label, string.Equals(shown, full, StringComparison.Ordinal) ? null : full);
            }

            row.DetailFlexLabels.Add(label);
            row.DetailFlexFulls.Add(full);
            row.DetailFlexSilent.Add(silent);
        }

        /// <summary>
        /// Newest sample minus the one before it; 0 when there are fewer
        /// than two samples or the costs match (the line is omitted).
        /// </summary>
        private static long SampleDelta(PlanHistoryEntry entry, out DateTime previousSampleAtUtc)
        {
            previousSampleAtUtc = default(DateTime);
            var samples = entry.CostSamples;
            if (samples == null || samples.Count < 2)
            {
                return 0;
            }

            var newest = samples[samples.Count - 1];
            var previous = samples[samples.Count - 2];
            if (newest == null || previous == null)
            {
                return 0;
            }

            previousSampleAtUtc = previous.TimestampUtc;
            return newest.TotalCoinCost - previous.TotalCoinCost;
        }

        private void BuildSampleLine(Panel panel, long delta, DateTime previousSampleAtUtc, int x, int y)
        {
            long magnitude = Math.Abs(delta);
            var segments = CoinCurrencyRenderer.BuildCoinSegments(magnitude, UiFonts.Caption);
            CoinCurrencyRenderer.LayoutCoinSegments(panel, segments, x, y, UiFonts.Caption);
            int coinWidth = CoinCurrencyRenderer.TotalCoinSegmentsWidth(segments);

            string suffix = (delta < 0 ? " cheaper than " : " dearer than ")
                + StatusText.ForAgeAgo(DateTime.UtcNow - previousSampleAtUtc);
            new Label
            {
                Font = UiFonts.Caption,
                Text = suffix,
                TextColor = DetailTextColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(x + coinWidth + 4, y),
                Parent = panel,
            };
        }

        // ---------------------------------------------------------------
        // Refit
        // ---------------------------------------------------------------
        private void RefitRows()
        {
            if (!IsLive || _contentPanel.Width == _lastLayoutWidth)
            {
                return;
            }

            RefitEveryRow(measureText: false);
            _resizeSettle.Schedule();
        }

        private void RefitAfterResizeSettle()
        {
            if (!IsLive)
            {
                return;
            }

            RefitEveryRow(measureText: true);
        }

        private void RefitEveryRow(bool measureText)
        {
            int barWidth = Math.Max(0, _contentPanel.Width - ScrollbarAllowance);
            _lastLayoutWidth = _contentPanel.Width;

            var bands = BandsFor(barWidth);
            int rightEdge = Math.Max(0, barWidth - PlanHistoryRowLayout.Inset);

            _contentPanel.SuspendLayout();
            try
            {
                foreach (var row in _rows)
                {
                    LayoutRow(row, bands, rightEdge, measureText);
                }
            }
            finally
            {
                _contentPanel.ResumeLayout(false);
            }

            PositionColumnHeader(barWidth);
        }

        private void LayoutRow(
            RenderedRow row, in PlanHistoryRowLayout.Bands bands, int rightEdge, bool measureText)
        {
            row.Panel.Size = new Point(bands.RowWidth, row.Panel.Height);

            if (measureText)
            {
                // Re-ellipsis only. The row's hover is deferred and says
                // the same at any width, so nothing is re-stamped here.
                IconNameRowHelpers.ReellipsizeName(row.IconName, UiFonts.Body,
                    bands.NameX + bands.NameWidth, 0, 0);
            }

            // The plate moves; the glyph keeps its offset inside it.
            row.CaretHit.Location = new Point(
                bands.CaretHitX, PlanHistoryRowLayout.CaretHitY);
            row.CaretHit.Size = new Point(
                bands.CaretHitWidth, PlanHistoryRowLayout.CaretHitHeight);
            row.IconName.IconFrame.Location = new Point(bands.IconX, row.IconName.IconFrame.Location.Y);
            row.IconName.NameLabel.Location = new Point(bands.NameX, row.IconName.NameLabel.Location.Y);

            CoinCurrencyRenderer.RepositionValueCellRightAligned(
                row.CostCell, bands.CostRightEdge, PlanHistoryRowLayout.MainLineTextY);
            row.WhenLabel.Location = new Point(
                Math.Max(0, bands.WhenRightEdge - row.WhenLabel.Width),
                PlanHistoryRowLayout.MainLineTextY);

            if (row.Open != null)
            {
                row.Open.Location = new Point(bands.OpenX, MainLineButtonY);
            }

            row.Resolve.Location = new Point(bands.ResolveX, MainLineButtonY);
            row.Pin.Location = new Point(bands.PinX, MainLineButtonY);
            row.Delete.Location = new Point(bands.DeleteX, RemoveButtonY);

            if (row.DetailPanel != null)
            {
                row.DetailPanel.Size = new Point(bands.RowWidth, row.DetailPanel.Height);
                if (measureText)
                {
                    for (int i = 0; i < row.DetailFlexLabels.Count; i++)
                    {
                        var label = row.DetailFlexLabels[i];
                        string full = row.DetailFlexFulls[i];
                        string shown = LabelHelpers.EllipsizeToWidth(
                            label.Font, full, Math.Max(0, rightEdge - label.Location.X));
                        if (!string.Equals(label.Text, shown, StringComparison.Ordinal))
                        {
                            label.Text = shown;
                            if (!row.DetailFlexSilent[i])
                            {
                                TooltipFacility.ApplyPlain(
                                    label,
                                    string.Equals(shown, full, StringComparison.Ordinal) ? null : full);
                            }
                        }
                    }
                }
            }
        }

        // ---------------------------------------------------------------
        // Actions
        // ---------------------------------------------------------------
        // Guarded like every other row action. The gate lives here rather
        // than on the controls because the expand targets are a Label, an
        // icon frame and a name - none of them buttons, so SetRowEnabled
        // has nothing on them to switch off.
        private void ToggleDetail(string entryId)
        {
            if (_isResolving)
            {
                return;
            }

            _expandedEntryId = string.Equals(_expandedEntryId, entryId, StringComparison.Ordinal)
                ? null
                : entryId;
            RebuildRows();
        }

        private void OnOpenClicked(PlanHistoryEntry entry)
        {
            if (_isResolving)
            {
                return;
            }

            bool opened = _openEntry(entry);
            if (!opened)
            {
                SetStatus(
                    "That saved plan could not be loaded - use Re-solve to rebuild it at current prices.",
                    isError: false);

                // The failed load cleared BlobPresent on the row - rebuild
                // so Open disappears and the note shows.
                RebuildRows();
            }
        }

        private void OnResolveClicked(PlanHistoryEntry entry)
        {
            if (_isResolving || _resolveEntryAsync == null)
            {
                return;
            }

            _isResolving = true;
            var cts = new CancellationTokenSource();
            _resolveCts = cts;

            SetControlsEnabled(false);
            _spinner.Visible = true;
            SetStatus("Re-solving...", isError: false);

            Task.Run(async () =>
            {
                string failure = null;
                bool cancelled = false;
                try
                {
                    failure = await _resolveEntryAsync(entry, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
                catch (Exception ex)
                {
                    failure = ex.Message;
                    Logger.Warn(ex, "Plan History re-solve failed");
                }

                MainThreadMarshal.Run(() => FinishResolve(failure, cancelled));
            });
        }

        private void FinishResolve(string failure, bool cancelled)
        {
            _isResolving = false;

            if (cancelled)
            {
                _statusOverride = null;
            }
            else if (failure != null)
            {
                _statusOverride = StatusText.ForGenerationFailure(failure);
                _statusIsError = true;
            }
            else
            {
                _statusOverride = "Re-solved - see the Crafting Plan tab";
                _statusIsError = false;
            }

            if (!_buildComplete || !IsLive)
            {
                return;
            }

            _spinner.Visible = false;

            // The re-solve's capture bumped the row (cost, stamp, sample),
            // so rebuild rather than merely re-enabling.
            RebuildRows();
        }

        private void OnPinClicked(PlanHistoryEntry entry)
        {
            if (_isResolving)
            {
                return;
            }

            string entryId = entry.EntryId;
            bool saved = MutateEntries(index =>
            {
                var target = index.Entries.Find(
                    e => e != null && string.Equals(e.EntryId, entryId, StringComparison.Ordinal));
                if (target != null)
                {
                    target.Pinned = !target.Pinned;
                }
            });

            RebuildRows();
            if (!saved)
            {
                SetStatus("Plan History could not be saved - see the Log tab.", isError: true);
            }
        }

        private void OnDeleteClicked(PlanHistoryEntry entry)
        {
            if (_isResolving)
            {
                return;
            }

            // No dialog: single-row deletion is low-friction and
            // recoverable by re-generating.
            string entryId = entry.EntryId;
            bool saved = MutateEntries(index => index.Entries.RemoveAll(
                e => e != null && string.Equals(e.EntryId, entryId, StringComparison.Ordinal)));

            RebuildRows();
            SetStatus(saved ? "Entry deleted" : "Plan History could not be saved - see the Log tab.",
                isError: !saved);
        }

        private void OnClearHistoryClicked()
        {
            if (_isResolving || _modalDialog == null)
            {
                return;
            }

            _modalDialog.Show(
                "This removes every unpinned entry from Plan History. Continue?",
                ClearUnpinnedEntries,
                null,
                "Clear");
        }

        private void ClearUnpinnedEntries()
        {
            int removed = 0;
            int pinnedKept = 0;
            bool saved = MutateEntries(index =>
            {
                removed = index.Entries.RemoveAll(e => e == null || !e.Pinned);
                pinnedKept = index.Entries.Count;
            });

            RebuildRows();

            if (!saved)
            {
                SetStatus("Plan History could not be saved - see the Log tab.", isError: true);
                return;
            }

            string text = StatusText.Count(removed, "plan") + " removed";
            if (pinnedKept > 0)
            {
                text += " - " + StatusText.Count(pinnedKept, "pinned plan") + " kept";
            }

            SetStatus(text, isError: false);
        }

        /// <summary>
        /// Runs one index mutation through the Module-owned delegate.
        /// Returns false when the mutation could not be persisted (the
        /// in-memory index keeps the change either way, so the UI never
        /// lies about what the user just did).
        /// </summary>
        private bool MutateEntries(Action<PlanHistoryIndex> mutation)
        {
            try
            {
                _mutateIndex(mutation);
                return true;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Plan History index mutation failed");
                return false;
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (_clearButton != null)
            {
                _clearButton.Enabled = enabled && _rows.Count > 0;
            }

            foreach (var row in _rows)
            {
                SetRowEnabled(row, enabled);
            }
        }

        private static void SetRowEnabled(RenderedRow row, bool enabled)
        {
            // The one row affordance that is not a button, and so takes no
            // disabled plate: it says so in its own ink instead.
            row.Caret.TextColor = enabled ? CaretColor : DimColor;

            if (row.Open != null)
            {
                row.Open.Enabled = enabled;
            }

            row.Resolve.Enabled = enabled;
            row.Pin.Enabled = enabled;
            row.Delete.Enabled = enabled;
        }

        // ---------------------------------------------------------------
        // Status line
        // ---------------------------------------------------------------
        private void SetStatus(string text, bool isError)
        {
            _statusOverride = text;
            _statusIsError = isError;
            ApplyStatusText(text, isError);
        }

        private void UpdateStatusLine()
        {
            if (_statusOverride != null)
            {
                ApplyStatusText(_statusOverride, _statusIsError);
                return;
            }

            int shown = _rows.Count;
            if (shown == 0)
            {
                ApplyStatusText("", isError: false);
                return;
            }

            int pinned = 0;
            foreach (var row in _rows)
            {
                if (row.Entry.Pinned)
                {
                    pinned++;
                }
            }

            int cap = _settings?.GetClampedPlanHistoryMaxEntries() ?? 25;
            string text;
            if (shown >= cap)
            {
                text = StatusText.Count(shown, "plan")
                    + " kept (limit " + cap.ToString(CultureInfo.InvariantCulture)
                    + ") - oldest unpinned entries are removed automatically";
            }
            else
            {
                text = StatusText.Count(shown, "plan") + " kept";
                if (pinned > 0)
                {
                    text += " - " + pinned.ToString(CultureInfo.InvariantCulture) + " pinned";
                }
            }

            ApplyStatusText(text, isError: false);
        }

        private void ApplyStatusText(string text, bool isError)
        {
            if (_statusLabel == null)
            {
                return;
            }

            string shown = LabelHelpers.EllipsizeToWidth(UiFonts.Status, text, Math.Max(0, _statusLabel.Width));
            _statusLabel.Text = shown;
            _statusLabel.TextColor = isError ? ErrorColor : StatusColor;
            TooltipFacility.ApplyPlain(
                _statusLabel, string.Equals(shown, text, StringComparison.Ordinal) ? null : text);
            InlineSpinner.PlaceAfter(_spinner, _statusLabel, InlineSpinnerLayout.LabelGap);
        }
    }
}
