using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Contracts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// The Crafting Ranker: a persisted, priority-ordered list of items the
    /// user is working toward, each row answering "how close am I, given that
    /// everything above this has first claim on my materials?".
    ///
    /// Structurally a LogTabContent-shaped tab (fixed chrome siblings plus one
    /// scrolling FlowPanel), but held for the module's lifetime like
    /// AboutTabContent, because the watchlist, the ephemeral per-row metrics
    /// and the in-flight refresh token all have to survive a tab switch.
    /// </summary>
    internal class RankerTabContent
    {
        private static readonly Logger Logger = Logger.GetLogger<RankerTabContent>();

        private const int AddRowHeight = 40;
        private const int ToolbarHeight = 40;
        private const int ColumnHeaderRowHeight = PlanContentHeightMath.ColumnHeaderRowHeight;
        private const int ColumnHeaderLabelY = PlanContentHeightMath.ColumnHeaderLabelY;

        // No section band: this tab is named once, by the title band
        // Views/ViewAdapter draws above every tab's content.
        private const int TopChromeHeight =
            AddRowHeight + ToolbarHeight + ColumnHeaderRowHeight;

        private const int ScrollbarAllowance = WindowSizing.ScrollbarAllowance;
        private const int CaptionLineHeight = 18;
        private const int CaptionsPadding = 10;
        private const int SearchBoxWidth = 260;
        private const int QuantityBoxWidth = 56;
        private const int AddButtonWidth = 72;
        /// <summary>Clearance between the mode strip and the Add button to its left.</summary>
        private const int ModeGap = 8;

        // Blish's Checkbox draws its 32px box at x-9 and its label at x+20
        // (measured, decompiled 1.3.0), so its true footprint is wider than
        // its Location suggests and starts left of it.
        private const int CheckboxArtOverhang = 9;
        private const int CheckboxTextInset = 20;
        private const int BannerHeight = 30;

        // Vertical rhythm of the 60px main line, DERIVED rather than
        // listed. The row's height is set by its tier-1 item icon
        // (RankerRowLayout.RowHeight), and every face and every box on the
        // line centres against that icon - which is what makes the type ramp
        // a choice this row can change without re-picking five literals.
        private static int MainLineTextY => RankerRowLayout.MainLineY(TypeRampMetrics.BodyInk.LineHeight);

        private static int MainLineNameY => RankerRowLayout.MainLineY(TypeRampMetrics.StatusInk.LineHeight);

        private static int MainLineRankY => RankerRowLayout.MainLineY(TypeRampMetrics.CaptionInk.LineHeight);

        private static int MainLineChipY => RankerRowLayout.MainLineY(LabelHelpers.SmallTagHeight);

        private static int MainLineButtonY => RankerRowLayout.MainLineY(RankerRowLayout.ButtonHeight);

        private static int MainLineBarY => RankerRowLayout.MainLineY(RankerRowLayout.ReadyBarHeight);

        private const int MainLineIconY = 3;

        /// <summary>
        /// The readiness percentage, centred inside its own bar. One ramp
        /// tier below the row's own Status text: Bold18's 23px line box in a
        /// 24px bar left the number filling the paint edge to edge, and a
        /// meter reads as a meter only if the bar is visible around its
        /// figure. Body's 20 leaves 2px of plate above and below the box,
        /// 5px above and below the ink.
        /// </summary>
        private static int ReadyLineY =>
            MainLineBarY + ((RankerRowLayout.ReadyBarHeight - TypeRampMetrics.BodyInk.LineHeight) / 2);

        /// <summary>A gate's label and its bar, centred in the gate strip's pitch.</summary>
        private static int GateTextY =>
            (RankerRowLayout.GateLineHeight - TypeRampMetrics.BodyInk.LineHeight) / 2;

        private static int GateBarOffsetY =>
            (RankerRowLayout.GateLineHeight - RankerRowLayout.GateBarHeight) / 2;

        /// <summary>
        /// The percentage centred inside a gate's bar, one ramp tier below
        /// the gate NAME beside it for the reason <see cref="ReadyLineY"/>
        /// gives: Body's 20px line box exactly equalled GateBarHeight, so
        /// the figure had no plate above or below it at all. Caption's 18
        /// leaves a pixel of box either side and 3px of ink clearance.
        /// </summary>
        private static int GateValueY =>
            GateBarOffsetY
                + ((RankerRowLayout.GateBarHeight - TypeRampMetrics.CaptionInk.LineHeight) / 2);

        // Muted grey is reserved for content meant to leave the user's
        // focus: the footer captions and the empty-state onboarding prose
        // (matching EmptyPlanStateRenderer). Reported in game: primary row data at
        // this grey on the grey window read "as if disabled".
        private static readonly Color DimColor = new Color(150, 150, 150);

        // Primary sub-line data matches the Crafting Plan's currency table
        // rows, the direct analogue: names in white, figures at 220 grey.
        private static readonly Color ValueTextColor = new Color(220, 220, 220);

        private static readonly Color StatusColor = new Color(200, 200, 200);
        private static readonly Color ErrorColor = new Color(255, 100, 100);

        // The affordability chip reuses SummarySectionRenderer's
        // full-coverage tag colors (PillKind.Selected's darkened green,
        // 4.21:1 against CreateSmallTag's white label) - in game, white
        // text on RankerReadinessColors' pale #7EBA7E was
        // unreadable. The readiness TEXT bands keep their own palette; only
        // the pill chrome borrows the proven badge combination.
        private static readonly Color AffordableChipBorder = new Color(31, 143, 12);
        private static readonly Color AffordableChipFill = AffordableChipBorder * 0.15f;

        // The comparison-mode radio indicator: 157330 is the small green dot
        // the game uses for "on"; its "-cantint" twin is the grey dot for
        // "off". Art, not a U+25CF/U+25CB pair - neither exists in the font.
        private const int RadioOnAssetId = 157330;
        private const string RadioOffTextureName = "157330-cantint";
        private const int RadioIndicatorSize = 16;
        private const int RadioIndicatorGap = 6;
        private const int RadioOptionGap = 16;

        private readonly CraftingPlanPipeline _pipeline;
        private readonly IItemSearchProvider _itemSearchProvider;
        private readonly ModuleSettings _settings;
        private readonly RankerStore _store;
        private readonly Func<AccountSnapshot> _getSnapshot;
        private readonly Func<string> _getActiveCharacterName;
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;
        private readonly ItemStatWarmer _statWarmer;
        private readonly ResizeSettleDebounce _resizeSettle;

        private readonly RankerWatchlist _watchlist;

        // Ephemeral, session-scoped, one answer set per comparison mode.
        // Never persisted: a readiness number goes stale the moment Trading
        // Post prices move. RankerResultCache owns the invalidation rules.
        private readonly RankerResultCache _results = new RankerResultCache();

        private readonly List<RenderedRow> _rows = new List<RenderedRow>();

        private Panel _addPanel;
        private Panel _toolbarPanel;
        private Panel _columnHeaderPanel;
        private FlowPanel _contentPanel;
        private Panel _captionsPanel;
        private readonly List<Label> _captionLabels = new List<Label>();

        private AutocompleteTextBox _searchBox;
        private SuggestionPanel _suggestionPanel;
        private TextBox _quantityBox;
        private FeedbackButton _addButton;
        private FeedbackButton _refreshButton;
        private Label _modeLabel;
        private readonly List<ModeRadio> _modeRadios = new List<ModeRadio>();
        private Label _statusLabel;
        private Checkbox _categoriesCheckbox;
        private Checkbox _currenciesCheckbox;
        private LoadingSpinner _spinner;
        private Panel _bannerPanel;
        private Label _bannerLabel;

        private readonly List<Label> _columnHeaderLabels = new List<Label>();

        private int? _pendingItemId;
        private string _pendingItemName;
        private string _pendingItemIconUrl;

        private volatile bool _buildComplete;
        private int _lastLayoutWidth = -1;

        // Table-wide, not per row: the Ready and Days cells sit to the LEFT
        // of the coin cell, so letting each row size its own coin band
        // would put those columns in a different place on every row and
        // leave the header labelling nothing.
        private int _remainingBandWidth;
        private int _statusBandWidth;
        private int _refreshGeneration;
        private CancellationTokenSource _refreshCts;
        private bool _isRefreshing;
        private bool _firstRefreshDone;
        private readonly RankerSnapshotWatch _snapshotWatch = new RankerSnapshotWatch();
        private bool _rarityDirty;
        private DateTime? _lastRefreshLocal;
        private string _statusOverride;
        private bool _statusIsError;

        public RankerTabContent(
            CraftingPlanPipeline pipeline,
            IItemSearchProvider itemSearchProvider,
            ModuleSettings settings,
            RankerStore store,
            Func<AccountSnapshot> getSnapshot,
            Func<string> getActiveCharacterName,
            Func<int, ItemStatBlock> getItemStatBlock = null,
            Func<IReadOnlyList<int>, Task<int>> warmItemStatsAsync = null)
        {
            _pipeline = pipeline;
            _itemSearchProvider = itemSearchProvider;
            _settings = settings;
            _store = store;
            _getSnapshot = getSnapshot ?? (() => null);
            _getActiveCharacterName = getActiveCharacterName ?? (() => null);
            _getItemStatBlock = getItemStatBlock;
            _statWarmer = new ItemStatWarmer(warmItemStatsAsync, "ranker");
            _watchlist = store?.Load() ?? new RankerWatchlist();

            _resizeSettle = new ResizeSettleDebounce(
                RefitAfterResizeSettle,
                MainThreadMarshal.Run,
                ResizeSettleDebounce.DefaultSettleMs,
                ex => Logger.Warn(ex, "Ranker row re-fit wait failed"));
        }

        /// <summary>
        /// The last Refresh's owned solve per item id, for a future
        /// next-action classifier. Session-scoped and never persisted.
        /// </summary>
        public IReadOnlyDictionary<int, CraftingPlanResult> LastOwnedResults => _results.OwnedResults(Mode);

        /// <summary>Main thread, immediately before Blish queues the off-thread Build.</summary>
        public void BeginRebuild()
        {
            _buildComplete = false;

            // Without this the watchlist's hovers degrade to the item's name
            // and nothing else: GetCachedStatBlock is a pure read, so a stat
            // block only exists here if some other tab already fetched it.
            _statWarmer.Start(
                Entries.Select(e => e.ItemId).Where(id => id > 0).Distinct().ToList());
        }

        public void Build(Container container)
        {
            _buildComplete = false;
            _rows.Clear();
            _captionLabels.Clear();
            _columnHeaderLabels.Clear();
            _lastLayoutWidth = -1;

            int w = container.ContentRegion.Width;

            BuildAddRow(container, w);
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

            _captionsPanel = new Panel
            {
                Size = new Point(w, 0),
                Location = new Point(0, TopChromeHeight),
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

            // Build() runs on a ThreadPool thread; every control touch below
            // this point has to land on the main thread, and _buildComplete
            // must be set inside the same queued callback so no entry point
            // can observe a half-built tab.
            MainThreadMarshal.Run(() =>
            {
                RebuildRows();

                // A tab switch during a run rebuilds the chrome from scratch,
                // so the in-flight state has to be restamped onto it - which
                // includes the button's "Analyzing..." label, since the
                // rebuild constructs it at rest. Its plate holds either word
                // and never carries the progress text, which belongs to the
                // status band (field bug: status-length text on the 132px
                // button spilled past its edges).
                if (_isRefreshing)
                {
                    _spinner.Visible = true;
                    SetControlsEnabled(false);
                }

                _buildComplete = true;
            });
        }

        /// <summary>View-only refresh on tab switch. Never triggers a solve.</summary>
        public void Refresh()
        {
            if (!_buildComplete || !IsLive)
            {
                return;
            }

            UpdateStatusLine();
            UpdateBanner();
        }

        public void CancelRefresh()
        {
            try
            {
                _refreshCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        public void Teardown()
        {
            CancelRefresh();
            _resizeSettle.Cancel();
            // SpriteScreen-parented, so disposing the row never disposes it.
            _suggestionPanel?.Dispose();
            _suggestionPanel = null;
        }

        private bool IsLive => _contentPanel != null && _contentPanel.Parent != null;

        private List<RankerWatchlistEntry> Entries => _watchlist.Entries;

        private RankerMode Mode => _watchlist.Mode;

        /// <summary>
        /// The two display toggles - see RenderSubLines. Persisted beside the
        /// mode, because they are the same kind of choice: how the user wants
        /// to read the table, not what the table says. Both off is the
        /// default, and is the headline row alone.
        /// </summary>
        private bool ShowCategories => _watchlist.ShowCategories;

        private bool ShowCurrencies => _watchlist.ShowCurrencies;

        // ---------------------------------------------------------------
        // Comparison mode
        // ---------------------------------------------------------------
        private const string CascadeModeItem = "In priority order";
        private const string IndependentModeItem = "Each on its own";

        private static string ModeItem(RankerMode mode)
        {
            return mode == RankerMode.Independent ? IndependentModeItem : CascadeModeItem;
        }

        /// <summary>
        /// What each option DOES, on the caption and on both halves of each
        /// option - Blish resolves a tooltip on the deepest control under the
        /// cursor and never bubbles to the parent (KNOWN-ISSUES #57), so the
        /// dot and its label each need their own.
        /// </summary>
        private const string ModeStripTooltip =
            "How the table measures each row. \"" + CascadeModeItem +
            "\" measures every row against what the rows above it leave behind; \"" +
            IndependentModeItem + "\" measures every row against your whole account.";

        private static string ModeTooltip(RankerMode mode)
        {
            return mode == RankerMode.Independent
                ? "Every row is measured against your full account, ignoring the other rows - which is closest to done right now? Closest sorts to the top; your priority order is kept and restored when you switch back."
                : "Rows are measured top down, each one against what the rows above it leave behind: higher rows have first claim on your materials, currencies, coin and daily crafts. The table stays in your own priority order, and the arrows move a row up or down it.";
        }

        /// <summary>
        /// A mode toggle never re-solves: metrics computed under the other
        /// mode go stale via MetricsAreCurrent (and revive if the user
        /// toggles straight back), exactly the staleness a reorder causes.
        /// </summary>
        private void OnModeChanged(RankerMode mode)
        {
            if (_isRefreshing || mode == _watchlist.Mode)
            {
                return;
            }

            _watchlist.Mode = mode;
            Persist();
            UpdateModeRadios();
            RebuildRows();

            // Toggling IS the request. A mode this session has already
            // computed displays instantly from its own answer set above;
            // one it has not is computed now, for the rows that need it,
            // rather than parked behind a Refresh press the user has no
            // reason to expect.
            if (Entries.Count > 0 && !_results.IsComplete(mode, Entries))
            {
                StartRefresh(mode, recomputeAll: false);
                return;
            }

            _statusOverride = null;
            UpdateStatusLine();
        }

        /// <summary>
        /// Priority indices in the order the table displays them: the
        /// stored order in Cascade mode, readiness-descending in
        /// Independent mode. The stored order itself is never touched.
        /// </summary>
        private List<int> DisplayOrder()
        {
            if (Mode != RankerMode.Independent)
            {
                var order = new List<int>(Entries.Count);
                for (int i = 0; i < Entries.Count; i++)
                {
                    order.Add(i);
                }

                return order;
            }

            return RankerPriorityOrdering.IndependentDisplayOrder(Entries, entry =>
            {
                var metrics = _results.Metrics(Mode, entry.ItemId);
                return RankerPriorityOrdering.MetricsAreCurrent(
                    metrics, Entries.IndexOf(entry), Mode) ? metrics : null;
            });
        }

        // ---------------------------------------------------------------
        // Chrome
        // ---------------------------------------------------------------
        private void BuildAddRow(Container container, int width)
        {
            _addPanel = new Panel
            {
                Size = new Point(width, AddRowHeight),
                Parent = container,
            };

            // SpriteScreen-parented and holding a global mouse subscription,
            // so disposing the old container never reaches it - a tab revisit
            // would otherwise leak one popup per visit.
            _suggestionPanel?.Dispose();

            _searchBox = new AutocompleteTextBox
            {
                PlaceholderText = "Search for an item to track...",
                Size = new Point(SearchBoxWidth, UiMetrics.ButtonHeight),
                Location = new Point(RankerRowLayout.Inset, 6),
                Parent = _addPanel,
            }.ReleaseOnDispose().ReleaseOnEnter();

            _suggestionPanel = new SuggestionPanel(_searchBox, _itemSearchProvider);
            _suggestionPanel.ItemSelected += (_, args) =>
            {
                _pendingItemId = args.ItemId;
                _pendingItemName = args.Name;
                _pendingItemIconUrl = args.IconUrl;
                UpdateAddButtonState();
            };

            _searchBox.TextChanged += (_, __) =>
            {
                // A pick is the only thing that resolves the field; editing it
                // afterwards has to drop that resolution, or Add would queue a
                // different item than the box reads.
                if (ItemRowSelection.SelectionIsStale(_pendingItemId, _pendingItemName, _searchBox.Text))
                {
                    _pendingItemId = null;
                    _pendingItemName = null;
                    _pendingItemIconUrl = null;
                }

                UpdateAddButtonState();
            };

            _quantityBox = new TextBox
            {
                PlaceholderText = "Qty",
                Text = "1",
                Size = new Point(QuantityBoxWidth, UiMetrics.ButtonHeight),
                Location = new Point(RankerRowLayout.Inset + SearchBoxWidth + 8, 6),
                Parent = _addPanel,
            }.ReleaseOnDispose().ReleaseOnEnter();

            _addButton = new FeedbackButton
            {
                Text = "Add",
                Size = new Point(AddButtonWidth, UiMetrics.ButtonHeight),
                Location = new Point(RankerRowLayout.Inset + SearchBoxWidth + QuantityBoxWidth + 16, 6),
                Enabled = false,
                Parent = _addPanel,
            };
            TooltipFacility.ApplyPlain(_addButton, "Add this item to the bottom of your priority list.");
            _addButton.Click += (_, __) => AddPendingItem();

            // The comparison mode is a two-option, mutually exclusive
            // choice and BOTH options should read at all times, which a
            // dropdown cannot do - it hides the alternative behind a click.
            // Blish ships no radio control, so
            // this is the smallest honest one: the game's own indicator dot
            // plus a label, both clickable, both always visible. The dot is
            // art rather than a U+25CF/U+25CB pair, neither of which exists
            // in the bitmap font (Services/UiGlyphs).
            _modeLabel = new Label
            {
                Font = UiFonts.Body,
                Text = "Compare:",
                TextColor = Color.White,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(0, 10),
                Parent = _addPanel,
            };
            TooltipFacility.ApplyPlain(_modeLabel, ModeStripTooltip);

            _modeRadios.Clear();
            _modeRadios.Add(CreateModeRadio(RankerMode.Cascade));
            _modeRadios.Add(CreateModeRadio(RankerMode.Independent));
            UpdateModeRadios();
        }

        /// <summary>One option of the comparison-mode radio pair.</summary>
        private sealed class ModeRadio
        {
            public RankerMode Mode;
            public Image Indicator;
            public Label Text;

            /// <summary>Indicator, gap and label - what the strip has to fit.</summary>
            public int Width;
        }

        private ModeRadio CreateModeRadio(RankerMode mode)
        {
            var indicator = new Image(AsyncTexture2D.FromAssetId(RadioOnAssetId))
            {
                Size = new Point(RadioIndicatorSize, RadioIndicatorSize),
                Location = new Point(0, 12),
                Parent = _addPanel,
            };

            var label = new Label
            {
                Font = UiFonts.Body,
                Text = ModeItem(mode),
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(0, 10),
                Parent = _addPanel,
            };

            // The label is as clickable as the dot: a 16px target is not a
            // control, it is a dare.
            indicator.Click += (_, __) => OnModeChanged(mode);
            label.Click += (_, __) => OnModeChanged(mode);

            string tooltip = ModeTooltip(mode);
            TooltipFacility.ApplyPlain(indicator, tooltip);
            TooltipFacility.ApplyPlain(label, tooltip);

            return new ModeRadio
            {
                Mode = mode,
                Indicator = indicator,
                Text = label,
                Width = RadioIndicatorSize + RadioIndicatorGap
                    + LabelHelpers.MeasureWith(UiFonts.Body)(ModeItem(mode)),
            };
        }

        /// <summary>
        /// Selection is carried by BOTH the dot and the label: the dot alone
        /// is a 16px difference in a row of text, which read in game as no
        /// difference at all on the tab's other indicators.
        /// </summary>
        private void UpdateModeRadios()
        {
            foreach (var radio in _modeRadios)
            {
                bool selected = radio.Mode == Mode;
                bool enabled = radio.Indicator.Enabled;
                radio.Indicator.Tint = selected
                    ? (enabled ? Color.White : Color.White * 0.4f)
                    : new Color(255, 255, 255) * (enabled ? 0.25f : 0.15f);
                radio.Text.TextColor = selected
                    ? (enabled ? Color.White : DimColor)
                    : (enabled ? ValueTextColor * 0.8f : DimColor);
            }
        }

        /// <summary>
        /// Seats the mode strip against the row's right edge, never left of
        /// the Add button. A hidden caption is moved off-panel rather than
        /// left where it would be overlapped.
        /// </summary>
        private void PositionModeStrip(int barWidth)
        {
            if (_modeRadios.Count < 2)
            {
                return;
            }

            int addButtonRight = RankerRowLayout.Inset + SearchBoxWidth + QuantityBoxWidth + 16 + AddButtonWidth;
            var slots = RankerRowLayout.ModeStrip(
                barWidth, _modeLabel.Width, _modeRadios[0].Width, _modeRadios[1].Width,
                RadioOptionGap, addButtonRight + ModeGap);

            _modeLabel.Visible = slots.LabelX >= 0;
            if (slots.LabelX >= 0)
            {
                _modeLabel.Location = new Point(slots.LabelX, _modeLabel.Location.Y);
            }

            PlaceModeRadio(_modeRadios[0], slots.FirstX);
            PlaceModeRadio(_modeRadios[1], slots.SecondX);
        }

        private static void PlaceModeRadio(ModeRadio radio, int x)
        {
            radio.Indicator.Location = new Point(x, radio.Indicator.Location.Y);
            radio.Text.Location = new Point(
                x + RadioIndicatorSize + RadioIndicatorGap, radio.Text.Location.Y);
        }

        private void BuildToolbar(Container container, int width)
        {
            _toolbarPanel = new Panel
            {
                Size = new Point(width, ToolbarHeight),
                Location = new Point(0, AddRowHeight),
                Parent = container,
            };

            _statusLabel = new Label
            {
                Font = UiFonts.Status,
                Text = "",
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                TextColor = StatusColor,
                Location = new Point(RankerRowLayout.Inset, 8),
                Parent = _toolbarPanel,
            };

            _spinner = InlineSpinner.Create(_toolbarPanel, InlineSpinnerLayout.SnapshotStatusSize);

            // Blish's own Checkbox, art and all - the module's established
            // shape for a persisted on/off, and no glyph anywhere near it.
            // Two of them, in the order the detail they reveal appears down
            // the row: the category strip, then the currency list under it.
            _categoriesCheckbox = new Checkbox
            {
                Text = CategoriesToggleText,
                Checked = ShowCategories,
                Location = new Point(0, 12),
                Parent = _toolbarPanel,
            };
            TooltipFacility.ApplyPlain(_categoriesCheckbox, CategoriesTooltip);
            _categoriesCheckbox.CheckedChanged += (_, e) => OnShowCategoriesChanged(e.Checked);

            _currenciesCheckbox = new Checkbox
            {
                Text = CurrenciesToggleText,
                Checked = ShowCurrencies,
                Location = new Point(0, 12),
                Parent = _toolbarPanel,
            };
            TooltipFacility.ApplyPlain(_currenciesCheckbox, CurrenciesTooltip);
            _currenciesCheckbox.CheckedChanged += (_, e) => OnShowCurrenciesChanged(e.Checked);

            // Size is assigned ONCE, here. The label swaps between the two
            // words below and the plate must not move with it, which holds
            // because FeedbackButton.RecalculateLayout only re-seats the
            // text and icon inside the bounds it is given - it never writes
            // Size the way an autosizing LabelBase would.
            _refreshButton = new FeedbackButton
            {
                Text = AnalyzeButtonText,
                Size = new Point(RankerRowLayout.AnalyzeButtonWidth, UiMetrics.ButtonHeight),
                Location = new Point(0, 6),
                Parent = _toolbarPanel,
            };
            _refreshButton.Click += (_, __) => OnRefreshClicked();
        }

        /// <summary>What the button says at rest.</summary>
        private const string AnalyzeButtonText = "Analyze";

        /// <summary>
        /// What it says while a run is in flight. The width both are drawn
        /// on is RankerRowLayout.AnalyzeButtonWidth, derived from THIS one.
        /// </summary>
        private const string AnalyzeRunningButtonText = "Analyzing...";

        private const string CategoriesToggleText = "Show Categories";
        private const string CurrenciesToggleText = "Show Currencies";

        private const string CategoriesTooltip =
            "Show the five categories under each row - materials, currencies, time gates, disciplines and recipes - as the bars the Ready figure is blended from, along with the notes that explain them. Off by default so more rows fit on screen.";

        private const string CurrenciesTooltip =
            "List the currencies each row is still short of, and by how much. The Currencies category says how close you are; this says which currency.";

        /// <summary>
        /// A display choice, not a measurement one: nothing is recomputed and
        /// no answer changes, so both modes' answer sets survive it untouched.
        /// </summary>
        private void OnShowCategoriesChanged(bool show)
        {
            if (show == _watchlist.ShowCategories)
            {
                return;
            }

            _watchlist.ShowCategories = show;
            Persist();
            RebuildRows();
        }

        /// <summary>See <see cref="OnShowCategoriesChanged"/>.</summary>
        private void OnShowCurrenciesChanged(bool show)
        {
            if (show == _watchlist.ShowCurrencies)
            {
                return;
            }

            _watchlist.ShowCurrencies = show;
            Persist();
            RebuildRows();
        }

        private void BuildColumnHeader(Container container, int width)
        {
            _columnHeaderPanel = HeaderBands.CreateColumnHeaderBand(
                container, width, 0, AddRowHeight + ToolbarHeight);

            foreach (string text in ColumnHeaders)
            {
                _columnHeaderLabels.Add(new Label
                {
                    Font = HeaderBands.Font,
                    TextColor = HeaderBands.LabelColor,
                    Text = text,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(0, ColumnHeaderLabelY),
                    Parent = _columnHeaderPanel,
                });
            }
        }

        /// <summary>
        /// The table's columns, left to right. Status is a column of its own
        /// rather than a badge trailing the item name: trailing the name put
        /// it at a different x on every row, so the one mark the table exists
        /// to be scanned for was the only thing in it that could not be
        /// scanned.
        /// </summary>
        private static readonly string[] ColumnHeaders =
        {
            "#", "Item", "Status", "Ready", "Days", "Remaining",
        };

        /// <summary>
        /// One short sentence per column, saying what its number MEANS. The
        /// "#" entry is the only mode-dependent one and is written in
        /// <see cref="UpdateColumnHeaderTooltips"/>; the rest are fixed.
        /// </summary>
        private static readonly string[] ColumnHeaderTooltips =
        {
            null,
            "The item you are working toward, and how many of it - hover for its full details.",
            "Whether you can afford everything this item still needs right now, or how much coin you are short of it.",
            "How close this item is to finished: the five barriers under the row, blended into one figure.",
            "The shortest possible wait in days, set by once-per-day crafts that no amount of coin can shorten.",
            "The coin still to spend, for the materials this item needs that you do not already hold.",
        };

        private void PositionChrome(Container container, int width)
        {
            int height = container.ContentRegion.Height;
            int barWidth = Math.Max(0, width - ScrollbarAllowance);

            _addPanel.Size = new Point(width, AddRowHeight);
            _toolbarPanel.Size = new Point(width, ToolbarHeight);
            _columnHeaderPanel.Size = new Point(width, ColumnHeaderRowHeight);

            // The checkbox's art hangs 9px left of its own Location (Blish
            // draws it at x-9), so the width the toolbar reserves for it
            // includes that overhang and the control is seated 9px inside.
            var toolbar = RankerRowLayout.Toolbar(
                barWidth, InlineSpinnerLayout.SnapshotStatusSize, InlineSpinnerLayout.LabelGap,
                ToggleFootprint(_categoriesCheckbox), ToggleFootprint(_currenciesCheckbox));
            _refreshButton.Location = new Point(toolbar.AnalyzeX, _refreshButton.Location.Y);
            _categoriesCheckbox.Location = new Point(
                toolbar.FirstToggleX + CheckboxArtOverhang, _categoriesCheckbox.Location.Y);
            _currenciesCheckbox.Location = new Point(
                toolbar.SecondToggleX + CheckboxArtOverhang, _currenciesCheckbox.Location.Y);
            _statusLabel.Width = toolbar.StatusWidth;
            InlineSpinner.PlaceAfter(_spinner, _statusLabel, InlineSpinnerLayout.LabelGap);

            PositionModeStrip(barWidth);

            PositionColumnHeader(barWidth);

            int captionsHeight = MeasureCaptionsHeight(barWidth);
            _captionsPanel.Size = new Point(width, captionsHeight);
            _captionsPanel.Location = new Point(0, Math.Max(TopChromeHeight, height - captionsHeight));

            _contentPanel.Size = new Point(
                width, Math.Max(0, height - TopChromeHeight - captionsHeight));
            _contentPanel.Location = new Point(0, TopChromeHeight);
        }

        /// <summary>
        /// What one toggle costs the toolbar: Blish's checkbox art, the inset
        /// it draws its label at, and the label itself.
        /// </summary>
        private static int ToggleFootprint(Checkbox checkbox)
        {
            return CheckboxArtOverhang + CheckboxTextInset
                + LabelHelpers.MeasureWith(UiFonts.Caption)(checkbox.Text);
        }

        private void PositionColumnHeader(int barWidth)
        {
            // The header labels sit on the columns they name because every
            // row shares these same table-wide band widths.
            var bands = BandsFor(barWidth);

            SetHeaderLabel(0, bands.RankX);

            // The rank column is a column of its own and stays out of
            // Item's span: Item begins at the icon its rows open with, not
            // at the band's left edge (Services/ColumnHeaderLabelMath).
            SetHeaderLabel(1, ColumnHeaderLabelMath.LabelX(bands.NameX, bands.IconX));

            // The four data columns centre their header on the same track
            // their cells centre on, which is what puts a header over the
            // values it names. The rank and the item name stay left-aligned:
            // they are the row's index and its subject, and both read down
            // the table from one rail.
            for (int column = 0; column < RankerRowLayout.DataColumnCount; column++)
            {
                var label = _columnHeaderLabels.Count > FirstDataColumnHeader + column
                    ? _columnHeaderLabels[FirstDataColumnHeader + column]
                    : null;
                if (label != null)
                {
                    label.Location = new Point(
                        CenteredInColumn(bands, column, label.Width), ColumnHeaderLabelY);
                }
            }
        }

        /// <summary>Index of the Status header in <see cref="ColumnHeaders"/>.</summary>
        private const int FirstDataColumnHeader = 2;

        /// <summary>
        /// X at which content of <paramref name="contentWidth"/> centres in
        /// data column <paramref name="column"/>. One track, one centring
        /// law (Services/JustifiedColumnTracks) for the header and for the
        /// cells under it - a second copy is how the two drift apart, which
        /// is the drift this replaced.
        /// </summary>
        private static int CenteredInColumn(
            in RankerRowLayout.Bands bands, int column, int contentWidth)
        {
            bands.DataTrack(column, out int trackX, out int trackWidth);
            return Math.Max(0, JustifiedColumnTracks.CenteredInBand(
                trackX, trackWidth, contentWidth));
        }

        /// <summary>
        /// The right edge that centres this row's coin run in the Remaining
        /// column. CoinCurrencyRenderer lays a value cell out from its right
        /// edge, and RemainingCellWidth is the very measurement it lays out
        /// from (MeasureRowCells), so the two cannot disagree about where
        /// the run starts.
        /// </summary>
        private static int RemainingCellRightEdge(in RankerRowLayout.Bands bands, RenderedRow row)
        {
            return CenteredInColumn(
                bands, RankerRowLayout.RemainingColumn, row.RemainingCellWidth)
                + row.RemainingCellWidth;
        }

        /// <summary>
        /// The "#" column means a different thing in each mode - a priority
        /// the user set, or a ranking the tab worked out - and a bare number
        /// cannot say which. The other four headers are mode-independent.
        /// </summary>
        private void UpdateColumnHeaderTooltips()
        {
            if (_columnHeaderLabels.Count == 0)
            {
                return;
            }

            TooltipFacility.ApplyPlain(_columnHeaderLabels[0], Mode == RankerMode.Independent
                ? "Rank by readiness, worked out from the numbers on the right. Your own priority order is kept, and comes back when you switch to \"" + CascadeModeItem + "\"."
                : "Your priority order. The row above has first claim on your materials, currencies, coin and daily crafts - use the arrows to change it.");

            for (int i = 1; i < _columnHeaderLabels.Count && i < ColumnHeaderTooltips.Length; i++)
            {
                TooltipFacility.ApplyPlain(_columnHeaderLabels[i], ColumnHeaderTooltips[i]);
            }
        }

        private void SetHeaderLabel(int index, int x)
        {
            if (index < _columnHeaderLabels.Count)
            {
                _columnHeaderLabels[index].Location = new Point(x, ColumnHeaderLabelY);
            }
        }

        // ---------------------------------------------------------------
        // Captions - the standing honesty text, below the list so it never
        // scrolls out of view
        // ---------------------------------------------------------------
        private static readonly string[] Captions =
        {
            "In priority order, each item is measured against what the items above it leave behind - higher rows have first claim on your materials, currencies, coin and daily crafts. Each on its own measures every item against your full account, ignoring the other rows, and sorts the closest-to-done to the top.",
            "Ready blends five separate barriers - materials at buy-order prices, account currencies, time-gated daily crafts, crafting disciplines and recipe unlocks - and counts only the ones this item actually has. Hover it for the breakdown.",
        };

        private int MeasureCaptionsHeight(int barWidth)
        {
            if (Entries.Count == 0)
            {
                return 0;
            }

            int usable = Math.Max(1, barWidth - 2 * RankerRowLayout.Inset);
            var measure = LabelHelpers.MeasureWith(UiFonts.Caption);
            int lines = 0;
            foreach (string caption in Captions)
            {
                lines += TextWrapMath.Wrap(caption, usable, usable, measure).Lines.Count;
            }

            return lines * CaptionLineHeight + CaptionsPadding;
        }

        private void RebuildCaptions(int barWidth)
        {
            foreach (var label in _captionLabels)
            {
                label.Parent = null;
                label.Dispose();
            }

            _captionLabels.Clear();

            if (Entries.Count == 0)
            {
                return;
            }

            int usable = Math.Max(1, barWidth - 2 * RankerRowLayout.Inset);
            var measure = LabelHelpers.MeasureWith(UiFonts.Caption);
            int y = 4;
            foreach (string caption in Captions)
            {
                foreach (string line in TextWrapMath.Wrap(caption, usable, usable, measure).Lines)
                {
                    _captionLabels.Add(new Label
                    {
                        Font = UiFonts.Caption,
                        Text = line,
                        TextColor = DimColor,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Width = usable,
                        Location = new Point(RankerRowLayout.Inset, y),
                        Parent = _captionsPanel,
                    });
                    y += CaptionLineHeight;
                }
            }
        }

        // ---------------------------------------------------------------
        // Rows
        // ---------------------------------------------------------------
        private class RenderedRow
        {
            public int ItemId;

            /// <summary>Index in the STORED priority list - what a move or a removal acts on.</summary>
            public int Index;

            /// <summary>Where the row sits in the table right now; see ReorderVisible.</summary>
            public int DisplayPosition;
            public string FullName;
            public Panel Panel;
            public Label RankLabel;
            public IconNameRowHelpers.IconNameHandle IconName;
            /// <summary>The percentage centred in the bar, or the measured non-numeric verdict.</summary>
            public Label ReadyLabel;

            /// <summary>The bar's plate, and the painted part inside it. Null on an unmeasured row.</summary>
            public Panel ReadyBarTrack;
            public Panel ReadyBarFill;

            /// <summary>Readiness the bar was painted at, so a resize can repaint without re-solving.</summary>
            public double ReadyFraction;

            public Panel StatusChip;
            public Label StatusPlaceholder;
            public int StatusCellWidth;
            public Label DaysLabel;
            public CoinCurrencyRenderer.ValueCellHandle RemainingCell;
            public Label RemainingDash;
            public int RemainingCellWidth;
            public CaretKeyButton Up;
            public CaretKeyButton Down;
            public CloseKeyButton Remove;
            public readonly List<Label> GateNameLabels = new List<Label>();
            public readonly List<Label> GateValueLabels = new List<Label>();
            public readonly List<Panel> GateBarTracks = new List<Panel>();
            public readonly List<Panel> GateBarFills = new List<Panel>();

            /// <summary>
            /// Each gate's completion, or -1 for a gate this item does not
            /// have. Kept beside the controls so a width change repaints the
            /// bars from the same numbers rather than re-reading metrics
            /// that may have been replaced under it.
            /// </summary>
            public readonly List<double> GateFractions = new List<double>();
            public readonly List<Panel> CurrencyIconFrames = new List<Panel>();
            public readonly List<Label> CurrencyNameLabels = new List<Label>();
            public readonly List<string> CurrencyNameFulls = new List<string>();
            /// <summary>
            /// A shortfall's amount as a Label, or full coverage as
            /// LabelHelpers' shared marker panel - so this is typed at the
            /// Control the layout pass actually needs (a Location and a
            /// Width), not at the one the common case happens to be.
            /// </summary>
            public readonly List<Control> CurrencyValues = new List<Control>();
            public readonly List<Label> NoteLabels = new List<Label>();
            public RankerRowMetrics Metrics;
        }

        private void RebuildRows()
        {
            if (_contentPanel == null)
            {
                return;
            }

            // Dispose, not ClearChildren: ClearChildren only detaches
            // (docs/ARCHITECTURE.md - "a tab switch detaches, it does not
            // dispose"), leaving every orphaned row tree to the GC and any
            // control type that hooks a static event in its constructor
            // (TrackBar does) rooted forever. Same idiom as
            // RichTooltipSurface.DisposeContent.
            foreach (var child in _contentPanel.Children.ToArray())
            {
                child.Dispose();
            }

            _rows.Clear();
            _bannerPanel = null;
            _bannerLabel = null;

            InvalidateOnSnapshotChange();
            AdoptRarityFromStatCache();

            int barWidth = Math.Max(0, _contentPanel.Width - ScrollbarAllowance);
            _lastLayoutWidth = _contentPanel.Width;

            BuildBanner(barWidth);

            if (Entries.Count == 0)
            {
                BuildEmptyState(barWidth);
            }
            else
            {
                var order = DisplayOrder();
                for (int position = 0; position < order.Count; position++)
                {
                    _rows.Add(CreateRow(Entries[order[position]], order[position], position, barWidth));
                }

                // Every row's cells are measured before any is rendered, so
                // the whole table shares one column geometry.
                RecomputeBandWidths();
                foreach (var row in _rows)
                {
                    RenderRowContent(row, Entries[row.Index], barWidth);
                }
            }

            _refreshButton.Enabled = !_isRefreshing && Entries.Count > 0;
            TooltipFacility.ApplyPlain(_refreshButton, Entries.Count > 0
                ? "Recalculate every row. Each item is solved twice, so the first analysis of a session can take a while."
                : "Add an item to your list first.");

            UpdateColumnHeaderTooltips();
            RebuildCaptions(barWidth);
            if (_contentPanel.Parent is Container container)
            {
                PositionChrome(container, container.ContentRegion.Width);
            }

            UpdateStatusLine();
        }

        /// <summary>
        /// Colours rows whose rarity this session already knows from some
        /// other tab's work, before their own first refresh has run. Cheap
        /// (one dictionary read per uncoloured row) and saves only when
        /// something actually changed, so the common case writes nothing.
        /// </summary>
        private void AdoptRarityFromStatCache()
        {
            if (_getItemStatBlock == null)
            {
                return;
            }

            if (RankerRarityAdoption.AdoptFromStatCache(Entries, _getItemStatBlock))
            {
                Persist();
            }
        }

        /// <summary>
        /// Every number in either answer set was measured against the
        /// holdings of one account snapshot; a newer one makes all of them
        /// claims about an account that no longer exists.
        /// <para>
        /// The rows on screen are NOT re-rendered here: blanking a table the
        /// user is reading - possibly mid-hover - to announce a background
        /// event is worse than leaving the numbers they were already reading
        /// until the rebuild replaces them row by row.
        /// </para>
        /// <para>
        /// A table that never had results does not ask for one. The first
        /// run of a session is the user's to start: it is up to
        /// RankerWatchlistLimits.MaxEntries rows at two plan solves each,
        /// and nothing on screen is waiting on it.
        /// </para>
        /// </summary>
        private void InvalidateOnSnapshotChange()
        {
            if (_snapshotWatch.Observe(_getSnapshot()?.CapturedAt, _results.HasAnyResults))
            {
                _results.InvalidateEverything();
            }
        }

        /// <summary>
        /// The snapshot poll, on the same terms as the Log tab's own: a
        /// nullable-DateTime compare per tick, run by Module.Update ONLY
        /// while this tab is the selected tab of a window the user can see.
        /// <para>
        /// That gate is the whole design, and the cost it guards plus the
        /// coalescing rule are stated on
        /// <see cref="RankerSnapshotWatch"/>: a change that lands while the
        /// tab is hidden is remembered there and spent on the first tick
        /// after it is next shown.
        /// </para>
        /// </summary>
        public void PollForSnapshotChange()
        {
            if (!_buildComplete || !IsLive)
            {
                return;
            }

            InvalidateOnSnapshotChange();
            if (!_snapshotWatch.TryTakeRebuild(_isRefreshing, Entries.Count > 0))
            {
                return;
            }

            // Says why the numbers are about to move. The run's own
            // per-row progress replaces it as soon as the first solve
            // reports, and the spinner and the Analyzing... button carry it
            // from here on.
            SetStatus("Account snapshot changed - recalculating.", isError: false);

            // recomputeAll, for the reason the button passes it: every
            // number in the set was measured against holdings that have
            // moved, so none of them can be replayed from cache.
            StartRefresh(Mode, recomputeAll: true);
        }

        private void BuildBanner(int barWidth)
        {
            if (_getSnapshot() != null)
            {
                return;
            }

            _bannerPanel = new Panel
            {
                Size = new Point(barWidth, BannerHeight),
                Parent = _contentPanel,
            };
            _bannerLabel = new Label
            {
                Font = UiFonts.Body,
                Text = "No account snapshot available - every item will read 0% until you fetch one from the Snapshot tab.",
                TextColor = StatusColor,
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Width = Math.Max(0, barWidth - RankerRowLayout.Inset),
                Location = new Point(RankerRowLayout.Inset, 6),
                Parent = _bannerPanel,
            };
        }

        private static readonly string[] EmptyStateLines =
        {
            "Nothing on your priority list yet.",
            "",
            "Add the items you are working toward, in the order you want to finish them. The Ranker then answers a question the Crafting Plan tab cannot: given that everything above it already has first claim on your materials, your currencies and your daily crafts, how close is each one really?",
            "",
            "Every row scores five separate barriers - materials, account currencies, time-gated daily crafts, crafting disciplines and recipe unlocks - and combines only the ones that item actually has into one Ready percentage you can rank by.",
            "",
            "Search above to add your first item, then press Analyze.",
        };

        private void BuildEmptyState(int barWidth)
        {
            var panel = new Panel
            {
                Size = new Point(barWidth, 0),
                Parent = _contentPanel,
            };

            int usable = Math.Max(1, barWidth - 2 * RankerRowLayout.Inset);
            var measure = LabelHelpers.MeasureWith(UiFonts.Body);
            int y = 8;
            foreach (string paragraph in EmptyStateLines)
            {
                if (paragraph.Length == 0)
                {
                    y += CaptionLineHeight / 2;
                    continue;
                }

                foreach (string line in TextWrapMath.Wrap(paragraph, usable, usable, measure).Lines)
                {
                    new Label
                    {
                        Font = UiFonts.Body,
                        Text = line,
                        TextColor = DimColor,
                        AutoSizeWidth = false,
                        AutoSizeHeight = true,
                        Width = usable,
                        Location = new Point(8, y),
                        Parent = panel,
                    };
                    y += 20;
                }
            }

            panel.Size = new Point(barWidth, y + 8);
        }

        /// <summary>
        /// Widest coin cell across the whole table, so every row shares one
        /// column geometry and the header labels sit on the columns they
        /// name. Returns true when it changed.
        /// </summary>
        private bool RecomputeBandWidths()
        {
            int remaining = MeasureDashWidth();
            int status = 0;
            foreach (var row in _rows)
            {
                if (row.RemainingCellWidth > remaining)
                {
                    remaining = row.RemainingCellWidth;
                }

                if (row.StatusCellWidth > status)
                {
                    status = row.StatusCellWidth;
                }
            }

            bool changed = remaining != _remainingBandWidth || status != _statusBandWidth;
            _remainingBandWidth = remaining;
            _statusBandWidth = status;
            return changed;
        }

        /// <summary>
        /// Re-seats EVERYTHING the coin band's width moves, which is the
        /// rows AND the column header over them.
        /// <para>
        /// The header is the half that was missed, and it is the reported
        /// "Ready/Days/Remaining are poorly aligned with the content below".
        /// A table with no results yet measures its coin band at
        /// RankerRowLayout.MinRemainingCellWidth; the first refresh replaces
        /// that with a real coin cell, and since the Ready and Days rails
        /// are derived by walking LEFT from the coin band, both move by the
        /// difference (37px in the 2026-08-27 capture) while the header
        /// labels stayed where the empty table had put them. Fixing the
        /// rails' arithmetic - the previous attempt - could not fix that,
        /// because the header was simply never asked again.
        /// </para>
        /// </summary>
        private void RelayoutTable(int barWidth)
        {
            foreach (var each in _rows)
            {
                if (each.Index < Entries.Count)
                {
                    RenderRowContent(each, Entries[each.Index], barWidth);
                }
            }

            PositionColumnHeader(barWidth);
        }

        private RankerRowLayout.Bands BandsFor(int barWidth)
        {
            return RankerRowLayout.Compute(
                barWidth, _remainingBandWidth, _statusBandWidth, ReorderVisible);
        }

        /// <summary>
        /// THE INDEPENDENT-MODE RANK MODEL, in one place.
        /// <list type="bullet">
        /// <item><description>The STORED list is always the user's priority
        /// order and is never touched by independent mode - it is what
        /// cascade mode goes back to, and what persists.</description></item>
        /// <item><description>Independent mode DISPLAYS by readiness, so the
        /// number in the "#" column is that ranking, not a priority; the
        /// column header says which on hover.</description></item>
        /// <item><description>There is therefore nothing to reorder while it
        /// is displayed, and the arrows are not shown at all - a disabled
        /// arrow invites a click that can never do anything. Remove stays:
        /// it is about the list, not about the order.</description></item>
        /// <item><description>A row added while independent mode is
        /// displayed still goes to the bottom of the stored priority order,
        /// and shows wherever its readiness puts it - which is last until it
        /// has been measured.</description></item>
        /// </list>
        /// </summary>
        private bool ReorderVisible => Mode == RankerMode.Cascade;

        private RenderedRow CreateRow(
            RankerWatchlistEntry entry, int index, int displayPosition, int barWidth)
        {
            var row = new RenderedRow
            {
                ItemId = entry.ItemId,
                Index = index,
                DisplayPosition = displayPosition,
                FullName = BuildDisplayName(entry),
            };
            var metrics = _results.Metrics(Mode, entry.ItemId);
            row.Metrics = RankerPriorityOrdering.MetricsAreCurrent(metrics, index, Mode) ? metrics : null;

            row.Panel = new Panel
            {
                Size = new Point(barWidth, RankerRowLayout.RowHeight),
                Parent = _contentPanel,
            };

            MeasureRowCells(row);
            return row;
        }

        private void RenderRowContent(RenderedRow row, RankerWatchlistEntry entry, int barWidth)
        {
            // Dispose, not ClearChildren - see RebuildRows. This runs per
            // row per band-width recompute (a Refresh over a full
            // watchlist re-renders every row at least once, often twice),
            // so a detach-only clear orphans hundreds of controls per
            // click.
            foreach (var child in row.Panel.Children.ToArray())
            {
                child.Dispose();
            }

            row.GateNameLabels.Clear();
            row.GateValueLabels.Clear();
            row.CurrencyIconFrames.Clear();
            row.CurrencyNameLabels.Clear();
            row.CurrencyNameFulls.Clear();
            row.CurrencyValues.Clear();
            row.NoteLabels.Clear();
            row.GateBarTracks.Clear();
            row.GateBarFills.Clear();
            row.GateFractions.Clear();
            row.StatusChip = null;
            row.StatusPlaceholder = null;
            row.ReadyBarTrack = null;
            row.ReadyBarFill = null;
            row.ReadyLabel = null;
            row.RemainingCell = null;
            row.RemainingDash = null;

            var metrics = row.Metrics;
            string chipText = ChipText(metrics);
            var bands = BandsFor(barWidth);

            row.RankLabel = new Label
            {
                Font = UiFonts.Caption,
                Text = (row.DisplayPosition + 1).ToString(CultureInfo.InvariantCulture) + ".",
                TextColor = ValueTextColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(bands.RankX, MainLineRankY),
                Parent = row.Panel,
            };

            // The name band is the name's alone now that the chip has a
            // column: it runs to the Status column's own left edge.
            // ONE resolved rarity feeds the frame, the name colour and the
            // hover header - resolving it three times is how they drift.
            string rarity = ItemRarityResolution.Resolve(entry.Rarity, StatRarityFor(entry.ItemId));
            var hover = ItemHover(row, entry, rarity);
            row.IconName = IconNameRowHelpers.CreateIconAndEllipsizedName(
                row.Panel, entry.IconUrl, rarity,
                bands.IconX, MainLineIconY, row.FullName, UiFonts.Status,
                bands.NameX + bands.NameWidth, 0, 0, bands.NameX, MainLineNameY,
                hover, iconSize: RankerRowLayout.IconSize);

            // Chip and placeholder are both exactly StatusCellWidth wide
            // (MeasureRowCells measures whichever of the two this row has),
            // so one centred x serves both.
            int statusX = CenteredInColumn(
                bands, RankerRowLayout.StatusColumn, row.StatusCellWidth);
            if (chipText != null)
            {
                ChipColors(metrics, out Color chipBorder, out Color chipFill);
                row.StatusChip = LabelHelpers.CreateSmallTag(
                    row.Panel, chipText, statusX, MainLineChipY, chipBorder, chipFill);
                LabelHelpers.ApplyTagTooltip(row.StatusChip, ChipTooltip(metrics));
            }
            else
            {
                row.StatusPlaceholder = CreateUnknownCell(
                    row.Panel, statusX, MainLineTextY, StatusPlaceholderTooltip(metrics));
            }

            RenderReadyCell(row, bands, metrics);

            // Measured absences render at ValueTextColor and as a real zero;
            // only a row that has never been solved gets the Neutral dash.
            // That is the whole distinction: an unmeasured cell says "-" in
            // grey and hovers "not yet calculated", and a measured cell
            // always shows a number, even when the number is nothing.
            row.DaysLabel = new Label
            {
                Font = UiFonts.Body,
                Text = metrics == null ? RankerReadinessCalculator.DashText : RankerReadinessCalculator.FormatDays(metrics),
                TextColor = metrics == null
                    ? RankerReadinessColors.Neutral
                    : metrics.DaysRemaining <= 0
                        ? ValueTextColor
                        : RankerReadinessColors.ForDays(metrics.DaysRemaining),
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(0, MainLineTextY),
                Parent = row.Panel,
            };
            TooltipFacility.ApplyPlain(row.DaysLabel, DaysTooltip(metrics));

            if (metrics == null)
            {
                row.RemainingDash = CreateUnknownCell(
                    row.Panel, 0, MainLineTextY, "Not yet calculated - press Analyze.");
            }
            else if (metrics.RemainingCoinCost <= 0)
            {
                // A solved row that owes nothing has a real answer, so it
                // draws the game's own zero rather than a dash. The coin
                // renderer's plain zero-value cell is reserved for the
                // gw2e-style "not sold or craftable" case and would claim
                // something this row is not saying.
                row.RemainingCell = CoinCurrencyRenderer.RenderZeroValueCellRightAligned(
                    row.Panel, RemainingCellRightEdge(bands, row), MainLineTextY, UiFonts.Body);
                foreach (var segment in row.RemainingCell.CoinSegments.Controls)
                {
                    // The NUMBER only. The coin beside it already carries its
                    // denomination's own hover from the renderer, and Blish
                    // resolves a tooltip on the deepest control under the
                    // cursor, so overwriting it would trade a fact for a
                    // sentence the number is already saying.
                    TooltipFacility.ApplyPlain(segment.Label, ZeroRemainingTooltip);
                }
            }
            else
            {
                row.RemainingCell = CoinCurrencyRenderer.RenderValueCellRightAligned(
                    row.Panel, metrics.RemainingCoinCost, null,
                    RemainingCellRightEdge(bands, row), MainLineTextY, UiFonts.Body);
            }

            row.Up = null;
            row.Down = null;
            int rowIndex = row.Index;

            if (ReorderVisible)
            {
                row.Up = CreateCaretButton(
                    row.Panel, UiGlyphs.CaretUp, bands.UpX, MoveUpTooltip());
                row.Down = CreateCaretButton(
                    row.Panel, UiGlyphs.CaretDown, bands.DownX, MoveDownTooltip());
                row.Up.Enabled = CanReorder && RankerPriorityOrdering.CanMoveUp(row.Index, Entries.Count);
                row.Down.Enabled = CanReorder && RankerPriorityOrdering.CanMoveDown(row.Index, Entries.Count);
                row.Up.Click += (_, __) => MoveRow(rowIndex, up: true);
                row.Down.Click += (_, __) => MoveRow(rowIndex, up: false);
            }

            row.Remove = CreateRemoveButton(
                row.Panel, bands.RemoveX, "Remove this item from your list.");
            row.Remove.Enabled = !_isRefreshing;
            row.Remove.Click += (_, __) => RemoveRow(rowIndex);

            var subLines = RenderSubLines(row, bands);
            row.Panel.Size = new Point(barWidth, subLines.TotalHeight);

            LayoutRow(row, bands, measureText: true);
        }

        /// <summary>Cell widths only - no controls built, so it is safe before the table's bands are known.</summary>
        private static void MeasureRowCells(RenderedRow row)
        {
            string chipText = ChipText(row.Metrics);
            row.StatusCellWidth = chipText == null
                ? MeasureDashWidth()
                : LabelHelpers.MeasureSmallTagWidth(chipText);
            row.RemainingCellWidth = row.Metrics == null
                ? MeasureDashWidth()
                : row.Metrics.RemainingCoinCost <= 0
                    ? CoinCurrencyRenderer.MeasureZeroValueWidth(UiFonts.Body)
                    : CoinCurrencyRenderer.MeasureValueWidth(row.Metrics.RemainingCoinCost, null, UiFonts.Body);
        }

        /// <summary>
        /// Seats the readiness cell at the table's current bands, and
        /// repaints the fill: the bar's WIDTH is a table-wide band, so a
        /// resize changes how many pixels a given percentage is worth.
        /// </summary>
        private static void LayoutReadyCell(RenderedRow row, in RankerRowLayout.Bands bands)
        {
            if (row.ReadyBarTrack != null)
            {
                int barX = Math.Max(0, bands.ReadyBarX);
                row.ReadyBarTrack.Location = new Point(barX, MainLineBarY);
                row.ReadyBarTrack.Size = new Point(bands.ReadyBarWidth, RankerRowLayout.ReadyBarHeight);
                row.ReadyBarFill.Size = new Point(
                    RankerReadinessRamp.FillWidth(bands.ReadyBarWidth, row.ReadyFraction),
                    RankerRowLayout.ReadyBarHeight);

                row.ReadyLabel.Location = new Point(
                    barX + Math.Max(0, (bands.ReadyBarWidth - row.ReadyLabel.Width) / 2), ReadyLineY);
                return;
            }

            // No bar: the unmeasured dash and the non-numeric verdict both
            // centre on the column's track, where a bar's own percentage
            // would have been.
            row.ReadyLabel.Location = new Point(
                CenteredInColumn(bands, RankerRowLayout.ReadyColumn, row.ReadyLabel.Width),
                MainLineTextY);
        }

        /// <summary>
        /// A reorder action, on the same key its neighbouring X is cut
        /// from (Views/Rendering/CaretKeyButton). It takes no explicit
        /// Size for the reason the remove action does not.
        /// </summary>
        private static CaretKeyButton CreateCaretButton(
            Panel parent, string glyph, int x, string tooltip)
        {
            var button = new CaretKeyButton(glyph)
            {
                Location = new Point(x, MainLineButtonY),
                Parent = parent,
            };
            TooltipFacility.ApplyPlain(button, tooltip);
            return button;
        }

        /// <summary>
        /// The remove action, which is Blish's own window close control and
        /// not a plate carrying a mark - the two carets beside it keep the
        /// same box (Views/Rendering/CloseKeyButton). It takes no explicit
        /// Size: the control is born at that box, and a row that named its
        /// own would be a second place the X could change size.
        /// </summary>
        private static CloseKeyButton CreateRemoveButton(
            Panel parent, int x, string tooltip)
        {
            var button = new CloseKeyButton
            {
                Location = new Point(x, MainLineButtonY),
                Parent = parent,
            };
            TooltipFacility.ApplyPlain(button, tooltip);
            return button;
        }

        /// <summary>
        /// The headline readiness cell: a painted bar with the percentage
        /// centred inside it in white.
        /// <para>
        /// THREE STATES, and they must not be confusable. An unmeasured row
        /// draws no bar at all - a grey dash, and a hover that says to press
        /// Refresh. A measured row with nothing scoreable draws its verdict
        /// as text. Only a measured percentage draws a bar, so a 0% bar (a
        /// full plate with nothing painted on it and a white "0%") can never
        /// be mistaken for a row that has not been solved.
        /// </para>
        /// </summary>
        private void RenderReadyCell(
            RenderedRow row, in RankerRowLayout.Bands bands, RankerRowMetrics metrics)
        {
            row.ReadyFraction = -1;

            if (metrics == null)
            {
                row.ReadyLabel = CreateUnknownCell(
                    row.Panel, 0, MainLineTextY, "Not yet calculated - press Analyze.");
                return;
            }

            if (metrics.Kind != RankerReadinessKind.Measured)
            {
                row.ReadyLabel = new Label
                {
                    Font = UiFonts.Body,
                    Text = RankerReadinessCalculator.FormatReadiness(metrics),
                    TextColor = ValueTextColor,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(0, MainLineTextY),
                    Parent = row.Panel,
                };
                TooltipFacility.ApplyPlain(row.ReadyLabel, ReadyTooltip(metrics));
                return;
            }

            row.ReadyFraction = metrics.Readiness;
            row.ReadyBarTrack = CreateBar(
                row.Panel, Math.Max(0, bands.ReadyBarX), MainLineBarY, bands.ReadyBarWidth,
                RankerRowLayout.ReadyBarHeight, metrics.Readiness, out row.ReadyBarFill);

            row.ReadyLabel = new Label
            {
                // Seated with ReadyLineY, which is derived from this tier.
                Font = UiFonts.Body,
                Text = RankerReadinessCalculator.FormatReadiness(metrics),
                TextColor = Color.White,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(0, ReadyLineY),
                Parent = row.Panel,
            };

            string tooltip = ReadyTooltip(metrics);
            TooltipFacility.ApplyPlain(row.ReadyLabel, tooltip);
            TooltipFacility.ApplyPlain(row.ReadyBarTrack, tooltip);
            TooltipFacility.ApplyPlain(row.ReadyBarFill, tooltip);
        }

        /// <summary>
        /// A bar: a dark plate with the ramp painted across part of it. The
        /// fill is a CHILD of the plate, so a relayout that moves the plate
        /// moves both and only the fill's width is ever rewritten.
        /// </summary>
        private static Panel CreateBar(
            Panel parent, int x, int y, int width, int height, double fraction, out Panel fill)
        {
            var track = new Panel
            {
                Size = new Point(Math.Max(0, width), height),
                Location = new Point(x, y),
                BackgroundColor = RankerReadinessColors.BarTrack,
                Parent = parent,
            };
            fill = new Panel
            {
                Size = new Point(RankerReadinessRamp.FillWidth(Math.Max(0, width), fraction), height),
                Location = new Point(0, 0),
                BackgroundColor = RankerReadinessColors.BarFill(fraction),
                Parent = track,
            };
            return track;
        }

        /// <summary>
        /// The one placeholder every column uses for "this row has never
        /// been solved": the module's dash at the standing neutral grey,
        /// hovering the reason. A MEASURED emptiness is never drawn like
        /// this - it draws a real 0, 0% or zero coin value in the row's own
        /// value colour, which is what keeps the two apart at a glance
        /// rather than only on hover.
        /// </summary>
        private static Label CreateUnknownCell(Panel parent, int x, int y, string tooltip)
        {
            var label = new Label
            {
                Font = UiFonts.Body,
                Text = RankerReadinessCalculator.DashText,
                TextColor = RankerReadinessColors.Neutral,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(x, y),
                Parent = parent,
            };
            TooltipFacility.ApplyPlain(label, tooltip);
            return label;
        }

        private const string ZeroRemainingTooltip =
            "Nothing left to buy - the materials you hold cover this item's coin cost.";

        private static string StatusPlaceholderTooltip(RankerRowMetrics metrics)
        {
            return metrics == null
                ? "Not yet calculated - press Analyze."
                : "Your account snapshot has not loaded, so what you can afford is not known yet.";
        }

        /// <summary>
        /// The row's breakdown, under its headline. Returns the block the
        /// row's height is taken from.
        /// <para>
        /// TWO TOGGLES, both off by default: the headline row is the
        /// comparison and everything here is the explanation, and a user
        /// comparing twenty rows wants the comparison on screen at once.
        /// Nothing is lost - each half is one
        /// toggle away, and the headline itself still hovers with the
        /// breakdown. The NOTES travel with the category strip because that
        /// is what they explain: a discipline gap, a contested claim, a
        /// vendor cap on one of the five categories.
        /// </para>
        /// </summary>
        private RankerRowLayout.SubLineBlock RenderSubLines(
            RenderedRow row, in RankerRowLayout.Bands bands)
        {
            var metrics = row.Metrics;
            if (metrics == null)
            {
                return RankerRowLayout.SubLines(false, 0, 0);
            }

            int currencyLines = ShowCurrencies
                ? RankerRowLayout.CurrencyLineCount(metrics.CurrencyShortfalls.Count)
                : 0;
            var notes = ShowCategories ? BuildNotes(metrics) : EmptyNotes;

            // A measured row always has gates; one that somehow has none
            // must not reserve a line for a strip it will not draw.
            bool hasGates = ShowCategories && metrics.Gates != null && metrics.Gates.Count > 0;
            var block = RankerRowLayout.SubLines(hasGates, currencyLines, notes.Count);

            // The gate breakdown, justified across the full sub-line band so
            // the five barriers read as one strip rather than a left-packed
            // sentence with dead space to its right.
            int gateY = block.GateY;
            int gateCount = hasGates ? metrics.Gates.Count : 0;
            int labelBand = GateLabelBandWidth();
            for (int i = 0; i < gateCount && i < RankerRowLayout.GateCellCount; i++)
            {
                var gate = metrics.Gates[i];
                row.GateNameLabels.Add(new Label
                {
                    Font = UiFonts.Body,
                    Text = RankerReadinessCalculator.GateLabel(gate.Gate),
                    TextColor = Color.White,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(0, gateY + GateTextY),
                    Parent = row.Panel,
                });

                RankerRowLayout.GateBar(bands, i, labelBand, out int barX, out int barWidth);

                // A gate this item does not have draws a FULL bar: there is
                // nothing outstanding behind a barrier that is not there, so
                // it reads 100% like any other finished gate
                // (RankerReadinessCalculator.FormatGate, which is also where
                // the caveat about the headline lives).
                double fraction = gate.Applies ? gate.Completion : 1.0;
                row.GateFractions.Add(fraction);
                Panel fill;
                Panel track = CreateBar(
                    row.Panel, barX, gateY + GateBarOffsetY, barWidth,
                    RankerRowLayout.GateBarHeight, fraction, out fill);

                row.GateBarTracks.Add(track);
                row.GateBarFills.Add(fill);
                row.GateValueLabels.Add(new Label
                {
                    // Seated with GateValueY, which is derived from this tier.
                    Font = UiFonts.Caption,

                    // White over the bar, at 5.07:1 or better at every point
                    // on the ramp (Services/RankerReadinessRamp) - which is
                    // the constraint that made the ramp as deep as it is.
                    Text = RankerReadinessCalculator.FormatGate(gate),
                    TextColor = Color.White,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(0, gateY + GateValueY),
                    Parent = row.Panel,
                });
            }

            int shown = currencyLines == 0 ? 0 : Math.Min(
                metrics.CurrencyShortfalls.Count,
                RankerRowLayout.CurrenciesPerLine * RankerRowLayout.MaxCurrencyLines);
            for (int i = 0; i < shown; i++)
            {
                var shortfall = metrics.CurrencyShortfalls[i];
                int y = block.CurrencyY
                    + (i / RankerRowLayout.CurrenciesPerLine) * RankerRowLayout.CurrencyLineHeight;

                // The wallet-tier icon is taller than its own caption text,
                // so the text centres on the ICON rather than the icon
                // sitting on the text's line box.
                int textY = y + (RankerRowLayout.CurrencyIconSize - UiFonts.Caption.LineHeight) / 2;

                string fullName = CurrencyName(shortfall);
                row.CurrencyNameFulls.Add(fullName);
                row.CurrencyIconFrames.Add(IconControls.CreateItemIcon(
                    row.Panel, CurrencyIconUrl(shortfall), ItemIconFrame.Currency(),
                    0, y, ItemIconTier.CurrencyListRow,
                    CurrencyHover(shortfall.CurrencyId, fullName)));
                row.CurrencyNameLabels.Add(new Label
                {
                    Font = UiFonts.Caption,
                    Text = fullName,
                    TextColor = ValueTextColor,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(0, textY),
                    Parent = row.Panel,
                });

                // Fully covered says the same thing here as it does in the
                // plan Summary's currency table, so it says it with the same
                // control (LabelHelpers.CreateFullCoverageMarker) rather than
                // a green word only this tab uses. Seated on the ICON like
                // the text beside it, but off the tag's own height.
                row.CurrencyValues.Add(shortfall.Short > 0
                    ? (Control)new Label
                    {
                        Font = UiFonts.Caption,
                        Text = ShortfallText(shortfall),
                        TextColor = ValueTextColor,
                        AutoSizeWidth = true,
                        AutoSizeHeight = true,
                        Location = new Point(0, textY),
                        Parent = row.Panel,
                    }
                    : LabelHelpers.CreateFullCoverageMarker(
                        row.Panel,
                        0,
                        y + ((RankerRowLayout.CurrencyIconSize - LabelHelpers.SmallTagHeight) / 2)));
            }

            for (int i = 0; i < notes.Count; i++)
            {
                row.NoteLabels.Add(new Label
                {
                    Font = UiFonts.Caption,
                    Text = notes[i],
                    TextColor = DimColor,
                    AutoSizeWidth = false,
                    AutoSizeHeight = true,
                    Width = Math.Max(0, bands.SubLineWidth),
                    Location = new Point(
                        bands.SubLineX, block.NoteY + i * RankerRowLayout.SubLineHeight),
                    Parent = row.Panel,
                });
            }

            return block;
        }

        private static readonly IReadOnlyList<string> EmptyNotes = new List<string>();

        private static int _gateLabelBandWidth = -1;

        /// <summary>
        /// Width reserved for a gate's NAME inside its cell - the widest of
        /// the five, so every bar in the strip starts at the same offset in
        /// its own cell and the five read as one rack of gauges. Measured
        /// once: the five labels are fixed strings and the face does not
        /// change while the module is loaded.
        /// </summary>
        private static int GateLabelBandWidth()
        {
            if (_gateLabelBandWidth >= 0)
            {
                return _gateLabelBandWidth;
            }

            var measure = LabelHelpers.MeasureWith(UiFonts.Body);
            int widest = 0;
            foreach (RankerGate gate in Enum.GetValues(typeof(RankerGate)))
            {
                int width = measure(RankerReadinessCalculator.GateLabel(gate));
                if (width > widest)
                {
                    widest = width;
                }
            }

            _gateLabelBandWidth = widest;
            return widest;
        }

        private void LayoutRow(RenderedRow row, in RankerRowLayout.Bands bands, bool measureText)
        {
            row.Panel.Size = new Point(bands.RowWidth, row.Panel.Height);
            row.RankLabel.Location = new Point(bands.RankX, MainLineRankY);

            if (measureText)
            {
                // The rich deferred tooltip already carries the full name,
                // so a truncation change needs no re-stamp here.
                IconNameRowHelpers.ReellipsizeName(row.IconName, UiFonts.Status,
                    bands.NameX + bands.NameWidth, 0, 0);
            }

            row.IconName.IconFrame.Location = new Point(bands.IconX, row.IconName.IconFrame.Location.Y);
            row.IconName.NameLabel.Location = new Point(bands.NameX, row.IconName.NameLabel.Location.Y);

            if (row.StatusChip != null)
            {
                row.StatusChip.Location = new Point(
                    CenteredInColumn(bands, RankerRowLayout.StatusColumn, row.StatusChip.Width),
                    MainLineChipY);
            }

            if (row.StatusPlaceholder != null)
            {
                row.StatusPlaceholder.Location = new Point(
                    CenteredInColumn(
                        bands, RankerRowLayout.StatusColumn, row.StatusPlaceholder.Width),
                    MainLineTextY);
            }

            LayoutReadyCell(row, bands);

            row.DaysLabel.Location = new Point(
                CenteredInColumn(bands, RankerRowLayout.DaysColumn, row.DaysLabel.Width),
                MainLineTextY);

            if (row.RemainingDash != null)
            {
                row.RemainingDash.Location = new Point(
                    CenteredInColumn(
                        bands, RankerRowLayout.RemainingColumn, row.RemainingDash.Width),
                    MainLineTextY);
            }
            else if (row.RemainingCell != null)
            {
                // The coin run is laid out from its RIGHT edge, so centring
                // it means handing the renderer the right edge a centred run
                // of this row's measured width would have.
                CoinCurrencyRenderer.RepositionValueCellRightAligned(
                    row.RemainingCell, RemainingCellRightEdge(bands, row), MainLineTextY);
            }

            if (row.Up != null)
            {
                row.Up.Location = new Point(bands.UpX, MainLineButtonY);
                row.Down.Location = new Point(bands.DownX, MainLineButtonY);
            }

            row.Remove.Location = new Point(bands.RemoveX, MainLineButtonY);

            int labelBand = GateLabelBandWidth();
            for (int i = 0; i < row.GateNameLabels.Count; i++)
            {
                RankerRowLayout.GateCell(bands, i, out int cellX, out _);
                row.GateNameLabels[i].Location = new Point(cellX, row.GateNameLabels[i].Location.Y);

                RankerRowLayout.GateBar(bands, i, labelBand, out int barX, out int barWidth);
                var track = row.GateBarTracks[i];
                track.Location = new Point(barX, track.Location.Y);
                track.Size = new Point(barWidth, RankerRowLayout.GateBarHeight);
                row.GateBarFills[i].Size = new Point(
                    RankerReadinessRamp.FillWidth(barWidth, row.GateFractions[i]),
                    RankerRowLayout.GateBarHeight);

                var value = row.GateValueLabels[i];
                value.Location = new Point(
                    barX + Math.Max(0, (barWidth - value.Width) / 2), value.Location.Y);
            }

            for (int i = 0; i < row.CurrencyNameLabels.Count; i++)
            {
                // The currency list's own indented grid - deliberately NOT
                // the gate rails; see RankerRowLayout.CurrenciesPerLine.
                RankerRowLayout.CurrencyCell(bands, i % RankerRowLayout.CurrenciesPerLine,
                    out int cellX, out int cellWidth);

                var icon = row.CurrencyIconFrames[i];
                var name = row.CurrencyNameLabels[i];
                var value = row.CurrencyValues[i];
                // No border term: CurrencyIconSize is the FRAMED box at the
                // wallet-list tier, art inset inside it (ItemIconTiers).
                int nameX = cellX + RankerRowLayout.CurrencyIconSize
                    + RankerRowLayout.CurrencyIconGap;
                int valueX = Math.Max(nameX, cellX + cellWidth - value.Width - RankerRowLayout.CellGap);

                icon.Location = new Point(cellX, icon.Location.Y);
                name.Location = new Point(nameX, name.Location.Y);
                value.Location = new Point(valueX, value.Location.Y);

                if (measureText)
                {
                    // A long currency name must clear the value in its own
                    // cell rather than running under it.
                    string full = row.CurrencyNameFulls[i];
                    string shown = LabelHelpers.EllipsizeToWidth(
                        UiFonts.Caption, full, Math.Max(0, valueX - nameX - RankerRowLayout.IconGap));
                    if (!string.Equals(name.Text, shown, StringComparison.Ordinal))
                    {
                        name.Text = shown;
                    }
                }
            }

            foreach (var note in row.NoteLabels)
            {
                note.Location = new Point(bands.SubLineX, note.Location.Y);
                note.Width = Math.Max(0, bands.SubLineWidth);
            }
        }

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
            RebuildCaptions(Math.Max(0, _contentPanel.Width - ScrollbarAllowance));
        }

        private void RefitEveryRow(bool measureText)
        {
            int barWidth = Math.Max(0, _contentPanel.Width - ScrollbarAllowance);
            _lastLayoutWidth = _contentPanel.Width;

            _contentPanel.SuspendLayout();
            try
            {
                if (_bannerPanel != null)
                {
                    _bannerPanel.Size = new Point(barWidth, BannerHeight);
                    _bannerLabel.Width = Math.Max(0, barWidth - RankerRowLayout.Inset);
                }

                var bands = BandsFor(barWidth);
                foreach (var row in _rows)
                {
                    LayoutRow(row, bands, measureText);
                }
            }
            finally
            {
                _contentPanel.ResumeLayout(false);
            }

            PositionColumnHeader(barWidth);
        }

        // ---------------------------------------------------------------
        // Row text
        // ---------------------------------------------------------------
        private static string BuildDisplayName(RankerWatchlistEntry entry)
        {
            string name = string.IsNullOrEmpty(entry.Name) ? "Unknown item" : entry.Name;
            return entry.Quantity > 1
                ? name + " x" + entry.Quantity.ToString(CultureInfo.InvariantCulture)
                : name;
        }

        private static string ChipText(RankerRowMetrics metrics)
        {
            if (metrics == null || !metrics.HasSnapshot)
            {
                return null;
            }

            return metrics.AffordableNow
                ? "Affordable now"
                : "Short " + CoinSegmentMath.GameStyleText(metrics.ShortfallCoin);
        }

        /// <summary>
        /// Affordable reuses the module's proven badge combination (the
        /// decision pills' darkened CRAFT green, already reused by the
        /// summary's full-coverage "OK" tag); Short takes the shopping
        /// source tags' recessed Locked plate. Both carry CreateSmallTag's
        /// white label at field-proven contrast - the pale readiness green
        /// behind white text was the reported unreadable case.
        /// </summary>
        private static void ChipColors(RankerRowMetrics metrics, out Color border, out Color fill)
        {
            if (metrics != null && metrics.AffordableNow)
            {
                border = AffordableChipBorder;
                fill = AffordableChipFill;
                return;
            }

            PillColors.GetPillColors(PillKind.Locked, false, out border, out fill);
        }

        /// <summary>
        /// The standard rich item hover for one watchlist row: the item's
        /// icon+name header either way, plus the session stat block when it
        /// has one.
        /// <para>
        /// Its caller stamps it on the row PANEL and the rank as well,
        /// because Blish resolves a tooltip on the deepest control under
        /// the cursor and never bubbles to the parent (KNOWN-ISSUES #57):
        /// every control the cursor can land on is its own hover, and the
        /// panel is what it lands on between them.
        /// </para>
        /// </summary>
        private ItemIconTooltip ItemHover(RenderedRow row, RankerWatchlistEntry entry, string rarity)
        {
            int itemId = entry.ItemId;
            return ItemIconTooltip.ForItem(
                ItemTooltipIdentity.ForItem(row.FullName, entry.IconUrl, rarity),
                _getItemStatBlock == null || itemId <= 0 ? (Func<ItemStatBlock>)null
                    : () => _getItemStatBlock(itemId));
        }

        /// <summary>The rarity the session stat cache knows for an item, or
        /// null - the fallback behind an entry's own captured value.</summary>
        private string StatRarityFor(int itemId)
        {
            if (_getItemStatBlock == null || itemId <= 0)
            {
                return null;
            }

            var block = _getItemStatBlock(itemId);
            return block == null ? null : block.Rarity;
        }

        private bool CanReorder => !_isRefreshing && Mode == RankerMode.Cascade;

        private string MoveUpTooltip()
        {
            return Mode == RankerMode.Independent
                ? "Priority order applies in \"" + CascadeModeItem + "\" mode - switch back to change it."
                : "Raise this item's priority. It then has first claim on materials the row above it was using.";
        }

        private string MoveDownTooltip()
        {
            return Mode == RankerMode.Independent
                ? "Priority order applies in \"" + CascadeModeItem + "\" mode - switch back to change it."
                : "Lower this item's priority.";
        }

        private string ChipTooltip(RankerRowMetrics metrics)
        {
            if (metrics == null)
            {
                return null;
            }

            bool independent = metrics.Mode == RankerMode.Independent;
            if (metrics.AffordableNow)
            {
                return independent
                    ? "You have enough coin for what is left of this item, measured against your full account."
                    : "You have enough coin for what is left of this item, after paying for everything above it on the list.";
            }

            return "You are " + CoinSegmentMath.GameStyleText(metrics.ShortfallCoin) +
                (independent
                    ? " short of what is left of this item, measured against your full account."
                    : " short of what is left of this item, counting coin that the higher-priority items above it would already have spent.");
        }

        private static int MeasureDashWidth()
        {
            return LabelHelpers.MeasureWith(UiFonts.Body)(RankerReadinessCalculator.DashText);
        }

        private string CurrencyName(RankerCurrencyShortfall shortfall)
        {
            return CurrencyDisplayResolver.ResolveName(shortfall.CurrencyId, CurrencyMetadataFor(shortfall.CurrencyId));
        }

        /// <summary>
        /// A currency shortfall chip's hover: the game's own currency box.
        /// <para>
        /// With NO wallet balance, deliberately.
        /// <see cref="RankerCurrencyShortfall.Held"/> is what the wallet has
        /// left once the higher-priority rows above this one have claimed
        /// theirs, not what the account holds, so drawing it as "N in
        /// Wallet" would state something untrue. Null is "not known", which
        /// drops the line rather than guessing at it.
        /// </para>
        /// </summary>
        private ItemIconTooltip CurrencyHover(int currencyId, string fullName)
        {
            return ItemIconTooltip.ForCurrency(fullName, () =>
            {
                var metadata = CurrencyMetadataFor(currencyId);
                return CurrencyTooltipFacts.For(
                    fullName,
                    CurrencyDisplayResolver.ResolveIconUrl(currencyId, metadata),
                    CurrencyDisplayResolver.ResolveDescription(currencyId, metadata),
                    null);
            });
        }

        private string CurrencyIconUrl(RankerCurrencyShortfall shortfall)
        {
            var metadata = CurrencyMetadataFor(shortfall.CurrencyId);
            return metadata != null && metadata.TryGetValue(shortfall.CurrencyId, out var entry)
                ? entry?.IconUrl
                : null;
        }

        private IReadOnlyDictionary<int, CurrencyMetadata> CurrencyMetadataFor(int currencyId)
        {
            foreach (var result in _results.EnumerateOwned(Mode))
            {
                if (result?.CurrencyMetadata != null && result.CurrencyMetadata.ContainsKey(currencyId))
                {
                    return result.CurrencyMetadata;
                }
            }

            return null;
        }

        /// <summary>
        /// What a currency line still owes. Full coverage never reaches here
        /// - it draws the shared marker instead of a word - so this only
        /// ever formats a real shortfall.
        /// </summary>
        private static string ShortfallText(RankerCurrencyShortfall shortfall)
        {
            return shortfall.Short.ToString("N0", CultureInfo.InvariantCulture) + " short";
        }

        private IReadOnlyList<string> BuildNotes(RankerRowMetrics metrics)
        {
            var notes = new List<string>();

            var missing = metrics.DisciplineGaps
                .Where(g => g.BestRating < g.RequiredRating)
                .ToList();
            if (missing.Count > 0)
            {
                var gap = missing[0];
                string text = gap.BestRating <= 0
                    ? "Needs " + gap.Discipline + " at " + gap.RequiredRating.ToString(CultureInfo.InvariantCulture) +
                      " - no character has learned it"
                    : "Needs " + gap.Discipline + " at " + gap.RequiredRating.ToString(CultureInfo.InvariantCulture) +
                      " - your best is " + gap.BestRating.ToString(CultureInfo.InvariantCulture);
                if (missing.Count > 1)
                {
                    text += " (and " + (missing.Count - 1).ToString(CultureInfo.InvariantCulture) + " more)";
                }

                notes.Add(text);
            }

            if (metrics.ContestedItemCount > 0 || metrics.ContestedCurrencyCount > 0)
            {
                var parts = new List<string>();
                if (metrics.ContestedItemCount > 0)
                {
                    parts.Add(StatusText.Count(metrics.ContestedItemCount, "material"));
                }

                if (metrics.ContestedCurrencyCount > 0)
                {
                    parts.Add(StatusText.Count(metrics.ContestedCurrencyCount, "currency", "currencies"));
                }

                notes.Add("Claimed by higher priority: " + string.Join(", ", parts));
            }

            foreach (var capped in metrics.VendorCappedItems)
            {
                string name = VendorCappedName(capped.ItemId);
                if (name == null)
                {
                    // Never render an id. No name means no note.
                    continue;
                }

                // Same wording as the plan tab's TimegatedNotice rows, and
                // named for what it is - a vendor purchase limit, not an
                // earning cooldown (the calculator already drops caps on
                // TP-liquid items, where the cap is coin rather than time).
                notes.Add(name + " is timegated - vendor " + CapLabel(capped.CapType) +
                    " limit: " + capped.CapValue.ToString(CultureInfo.InvariantCulture) +
                    " (plan needs " + capped.NeededCount.ToString(CultureInfo.InvariantCulture) + ")");
                break;
            }

            return notes;
        }

        private static string CapLabel(TimegatedCapType capType)
        {
            switch (capType)
            {
                case TimegatedCapType.Daily: return "Daily";
                case TimegatedCapType.Weekly: return "Weekly";
                default: return "Season";
            }
        }

        private string VendorCappedName(int itemId)
        {
            foreach (var result in _results.EnumerateOwned(Mode))
            {
                if (result?.ItemMetadata != null &&
                    result.ItemMetadata.TryGetValue(itemId, out var meta) &&
                    !string.IsNullOrEmpty(meta.Name))
                {
                    return meta.Name;
                }
            }

            return null;
        }

        private static string ReadyTooltip(RankerRowMetrics metrics)
        {
            if (metrics == null)
            {
                return "Not yet calculated - press Analyze.";
            }

            if (metrics.Kind != RankerReadinessKind.Measured)
            {
                return "This item has no measurable barrier left that the Ranker can score. Read the lines under the row for what is actually outstanding.";
            }

            var lines = new List<string>
            {
                "Ready blends the barriers this item actually has, each measured only against itself - nothing is converted into coin.",
                "",
            };
            foreach (var gate in metrics.Gates)
            {
                string label = RankerReadinessCalculator.GateLabel(gate.Gate);
                lines.Add(gate.Applies
                    ? label + ": " + RankerReadinessCalculator.FormatPercent(gate.Completion) +
                      " at weight " + gate.Weight.ToString("0.00", CultureInfo.InvariantCulture)
                    : label + ": this item has none, so it is not part of the blend");
            }

            lines.Add("");
            lines.Add("Weights are renormalised over the barriers that apply, so an item with only materials scores exactly its materials figure.");
            return string.Join("\n", lines);
        }

        private static string DaysTooltip(RankerRowMetrics metrics)
        {
            if (metrics == null)
            {
                return "Not yet calculated - press Analyze.";
            }

            if (metrics.DaysRemaining <= 0)
            {
                return "Nothing in this item's plan is a once-per-day craft.";
            }

            string text = "The earliest day this could finish, counting from today. Its plan needs " +
                StatusText.Count(metrics.DaysRemaining, "day") +
                " of once-per-day crafts, which no amount of coin shortens.";
            int queued = metrics.DaysRemaining - metrics.DaysAlone;
            if (queued > 0)
            {
                text += " " + StatusText.Count(queued, "day") +
                    " of that is spent on higher-priority items first - move this row up to take those days back.";
            }

            return text;
        }

        // ---------------------------------------------------------------
        // Mutations
        // ---------------------------------------------------------------
        private void AddPendingItem()
        {
            if (_isRefreshing || !_pendingItemId.HasValue)
            {
                return;
            }

            int quantity = ItemRowRequestBuilder.NormalizeQuantity(_quantityBox.Text);
            int existing = RankerPriorityOrdering.IndexOfItem(Entries, _pendingItemId.Value);

            if (existing >= 0)
            {
                // Updating in place rather than re-prioritising: the list is a
                // priority order, and silently moving an item because the user
                // re-searched it is the surprising outcome.
                Entries[existing].Quantity = quantity;
                InvalidateAfterChangeAt(existing);
                SetStatus($"{Entries[existing].Name} is already on your list - quantity updated to {quantity}.", isError: false);
            }
            else if (Entries.Count >= RankerWatchlistLimits.MaxEntries)
            {
                SetStatus($"Your list is full ({RankerWatchlistLimits.MaxEntries} items). Remove one to add another.", isError: false);
                return;
            }
            else
            {
                Entries.Add(new RankerWatchlistEntry
                {
                    ItemId = _pendingItemId.Value,
                    Quantity = quantity,
                    Name = _pendingItemName,
                    IconUrl = _pendingItemIconUrl,
                });
                SetStatus("Added " + _pendingItemName, isError: false);
            }

            _pendingItemId = null;
            _pendingItemName = null;
            _pendingItemIconUrl = null;
            _searchBox.Text = "";
            _quantityBox.Text = "1";
            _suggestionPanel?.HidePanel();

            Persist();
            RebuildRows();
            UpdateAddButtonState();
        }

        private void MoveRow(int index, bool up)
        {
            if (!CanReorder)
            {
                return;
            }

            int invalidatedFrom = up
                ? RankerPriorityOrdering.MoveUp(Entries, index)
                : RankerPriorityOrdering.MoveDown(Entries, index);

            if (invalidatedFrom == RankerPriorityOrdering.NoInvalidation)
            {
                return;
            }

            // Order matters to the cascade and to nothing else, so the
            // independent answers survive a reorder untouched.
            _results.InvalidateCascadeFrom(Entries, invalidatedFrom);
            Persist();
            RebuildRows();
            SetStatus("Order changed - press Analyze to recalculate the rows below it.", isError: false);
        }

        private void RemoveRow(int index)
        {
            if (_isRefreshing || index < 0 || index >= Entries.Count)
            {
                return;
            }

            string name = Entries[index].Name;
            int invalidatedFrom = RankerPriorityOrdering.RemoveAt(Entries, index);

            // The removed row leaves both sets; the survivors' independent
            // numbers are position-free and stand, while the cascade below
            // the gap no longer has the right claims above it.
            _results.KeepOnly(Entries);
            _results.InvalidateCascadeFrom(Entries, invalidatedFrom);

            Persist();
            RebuildRows();
            SetStatus("Removed " + name, isError: false);
            UpdateAddButtonState();
        }

        /// <summary>
        /// The entry at index changed in place (a quantity edit): the row
        /// itself is wrong under BOTH modes, and under the cascade so is
        /// everything below it. See RankerResultCache for the rules.
        /// </summary>
        private void InvalidateAfterChangeAt(int index)
        {
            if (index < 0 || index >= Entries.Count)
            {
                return;
            }

            _results.InvalidateItem(Entries[index].ItemId);
            _results.InvalidateCascadeFrom(Entries, index);
        }

        private void Persist()
        {
            if (_store != null && !_store.Save(_watchlist))
            {
                SetStatus("Your list could not be saved - see the Log tab.", isError: true);
            }
        }

        private void UpdateAddButtonState()
        {
            if (_addButton == null)
            {
                return;
            }

            _addButton.Enabled = !_isRefreshing && _pendingItemId.HasValue;
        }

        // ---------------------------------------------------------------
        // Refresh
        // ---------------------------------------------------------------
        private void OnRefreshClicked()
        {
            // Disabled while a run is in flight (the module's standing
            // long-run pattern - see the plan tab's Generate button), so no
            // cancel path hangs off this click.
            if (_isRefreshing || Entries.Count == 0)
            {
                return;
            }

            // A press means "these numbers are old" - prices move even when
            // the list does not - so it recomputes the displayed mode whole
            // rather than trusting anything already cached for it.
            StartRefresh(Mode, recomputeAll: true);
        }

        /// <summary>
        /// One row of a run's work plan, decided on the MAIN thread where the
        /// cache lives. A row the mode's set already answers is not re-solved;
        /// under the cascade its cached solve is still replayed, because the
        /// rows below it are measured against what it claims.
        /// </summary>
        private sealed class RefreshRow
        {
            public RankerWatchlistEntry Entry;
            public int Slot;
            public bool Solve;
            public CraftingPlanResult CachedOwned;
        }

        /// <summary>
        /// Starts a run for one mode. <paramref name="recomputeAll"/> drops
        /// that mode's set first (the Refresh button); otherwise only the rows
        /// the cache cannot answer are solved, which is what makes a toggle to
        /// a mode computed earlier in the session cost nothing.
        /// </summary>
        private void StartRefresh(RankerMode mode, bool recomputeAll)
        {
            if (recomputeAll)
            {
                _results.InvalidateMode(mode);
            }

            int firstStale = _results.FirstStaleIndex(mode, Entries);
            if (firstStale < 0)
            {
                // Every row already answered under this mode.
                return;
            }

            int myGen = ++_refreshGeneration;
            var cts = new CancellationTokenSource();
            _refreshCts = cts;
            _isRefreshing = true;
            SetControlsEnabled(false);
            _spinner.Visible = true;

            // Read HERE, on the main thread, and once: every row in a run is
            // measured against the same account state, and the stamp the
            // cache is judged against has to be the one the run actually
            // used - not whatever a background re-fetch has replaced it with
            // by the time the run ends.
            var snapshot = _getSnapshot();
            _snapshotWatch.MeasuredAgainst(snapshot?.CapturedAt);

            var work = new List<RefreshRow>(Entries.Count);
            for (int i = 0; i < Entries.Count; i++)
            {
                var entry = Entries[i];
                bool cached = mode == RankerMode.Cascade
                    ? i < firstStale
                    : RankerPriorityOrdering.MetricsAreCurrent(
                        _results.Metrics(mode, entry.ItemId), i, mode);

                work.Add(new RefreshRow
                {
                    // Copied: the run reads these off the main thread, and
                    // the user can edit the list while it is in flight.
                    Entry = new RankerWatchlistEntry
                    {
                        ItemId = entry.ItemId,
                        Quantity = entry.Quantity,
                        Name = entry.Name,
                        IconUrl = entry.IconUrl,
                        Rarity = entry.Rarity,
                    },
                    Slot = i,
                    Solve = !cached,
                    CachedOwned = cached ? _results.Owned(mode, entry.ItemId) : null,
                });
            }

            string activeCharacter = _getActiveCharacterName();
            Task.Run(() => RunRefreshAsync(work, snapshot, activeCharacter, mode, myGen, cts.Token));
        }

        private async Task RunRefreshAsync(
            List<RefreshRow> work, AccountSnapshot snapshot, string activeCharacter,
            RankerMode mode, int myGen, CancellationToken ct)
        {
            int updated = 0;
            string failure = null;
            bool cancelled = false;

            try
            {
                var valuation = _settings?.GetEffectiveCurrencyValuation();
                var homesteadTiers = _settings?.GetHomesteadEfficiencyTiers();
                var cascade = new RankerPriorityCascade(snapshot);

                // Independent mode is slot 1 semantics for EVERY row: one
                // unconsumed availability (the full account), no Consume
                // threading between rows. The mode only changes which
                // snapshot the owned solve sees, which is why each mode
                // keeps its own answer set rather than staling the other's.
                var fullAvailability = mode == RankerMode.Independent
                    ? cascade.CurrentAvailability
                    : null;

                int total = work.Count(w => w.Solve);
                int position = 0;

                foreach (var row in work)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!row.Solve)
                    {
                        // Answered already. Under the cascade the row still
                        // has to claim what it claimed last time, or every
                        // row below it would be measured against materials
                        // this one is already spending.
                        if (mode == RankerMode.Cascade && row.CachedOwned != null)
                        {
                            cascade.Consume(row.CachedOwned);
                        }

                        continue;
                    }

                    var entry = row.Entry;
                    int slot = row.Slot;
                    string name = entry.Name;
                    int reportPosition = ++position;
                    int reportTotal = total;
                    MainThreadMarshal.Run(() => ReportProgress(myGen, reportPosition, reportTotal, name));

                    var availability = fullAvailability ?? cascade.CurrentAvailability;

                    var baseline = await _pipeline.GenerateStructuredAsync(
                        entry.ItemId, entry.Quantity, null, ct, null,
                        activeCharacterName: null,
                        priceBasis: PriceBasis.BuyOrder,
                        currencyValuation: valuation,
                        ownMaterialsMode: OwnMaterialsMode.Free,
                        homesteadTiers: homesteadTiers,
                        phaseProgress: null,
                        characterDisciplines: snapshot?.CharacterDisciplines).ConfigureAwait(false);

                    if (myGen != _refreshGeneration)
                    {
                        return;
                    }

                    ct.ThrowIfCancellationRequested();

                    var owned = await _pipeline.GenerateStructuredAsync(
                        entry.ItemId, entry.Quantity, availability.Snapshot, ct, null,
                        activeCharacterName: activeCharacter,
                        priceBasis: PriceBasis.BuyOrder,
                        currencyValuation: valuation,
                        ownMaterialsMode: OwnMaterialsMode.Free,
                        homesteadTiers: homesteadTiers,
                        phaseProgress: null,
                        characterDisciplines: snapshot?.CharacterDisciplines).ConfigureAwait(false);

                    if (myGen != _refreshGeneration)
                    {
                        return;
                    }

                    ct.ThrowIfCancellationRequested();

                    var metrics = RankerReadinessCalculator.Compute(baseline, owned, availability, slot, mode);
                    if (mode == RankerMode.Cascade)
                    {
                        cascade.Consume(owned);
                    }

                    updated++;

                    int itemId = entry.ItemId;
                    MainThreadMarshal.Run(() => ApplyRowMetrics(myGen, mode, itemId, metrics, owned));
                }
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                failure = ex.Message;
                Logger.Warn(ex, "Ranker refresh failed");
            }

            int finalUpdated = updated;
            string finalFailure = failure;
            bool wasCancelled = cancelled;
            MainThreadMarshal.Run(() => FinishRefresh(myGen, mode, finalUpdated, finalFailure, wasCancelled));
        }

        private void ReportProgress(int myGen, int position, int total, string name)
        {
            if (myGen != _refreshGeneration || !_buildComplete || !IsLive)
            {
                return;
            }

            string text = $"Analyzing {position} of {total} - {name}";
            if (!_firstRefreshDone)
            {
                text += ". The first analysis of a session downloads recipe data and can take a while.";
            }

            SetStatus(text, isError: false);
        }

        private void ApplyRowMetrics(
            int myGen, RankerMode mode, int itemId, RankerRowMetrics metrics, CraftingPlanResult owned)
        {
            if (myGen != _refreshGeneration)
            {
                return;
            }

            var entry = Entries.FirstOrDefault(e => e.ItemId == itemId);

            // Adopted BEFORE the liveness gate below: the solve's metadata
            // knows the rarity the Add-time search result never carried, and
            // that is a fact about the item rather than about the view, so a
            // run the user tabbed away from must not lose it. Persisted once
            // per run, in FinishRefresh, which is not gated either.
            if (RankerRarityAdoption.AdoptFromMetadata(entry, owned?.ItemMetadata))
            {
                _rarityDirty = true;
            }

            if (!_buildComplete || !IsLive)
            {
                return;
            }

            _results.Store(mode, itemId, metrics, owned);

            var row = _rows.FirstOrDefault(r => r.ItemId == itemId);
            if (row == null || entry == null)
            {
                return;
            }

            row.Metrics = metrics;
            MeasureRowCells(row);

            int barWidth = Math.Max(0, _contentPanel.Width - ScrollbarAllowance);
            if (RecomputeBandWidths())
            {
                RelayoutTable(barWidth);
                return;
            }

            RenderRowContent(row, entry, barWidth);
        }

        private void FinishRefresh(int myGen, RankerMode mode, int updated, string failure, bool cancelled)
        {
            if (myGen != _refreshGeneration)
            {
                return;
            }

            _isRefreshing = false;
            _firstRefreshDone = true;
            _lastRefreshLocal = DateTime.Now;

            if (_rarityDirty)
            {
                // Rarity adopted from the run's solves (see ApplyRowMetrics)
                // is worth keeping across sessions; one save per run.
                _rarityDirty = false;
                Persist();
            }

            if (!_buildComplete || !IsLive)
            {
                return;
            }

            _spinner.Visible = false;
            SetControlsEnabled(true);

            // Independent mode's display order is the refresh's answer -
            // re-sort now that the metrics are in.
            if (Mode == RankerMode.Independent)
            {
                RebuildRows();
            }

            if (failure != null)
            {
                SetStatus(StatusText.ForGenerationFailure(failure), isError: true);
            }
            else if (cancelled)
            {
                SetStatus("Analysis cancelled - " + StatusText.Count(updated, "item") + " updated", isError: false);
            }
            else
            {
                _statusOverride = null;
                UpdateStatusLine();
            }
        }

        private void SetControlsEnabled(bool enabled)
        {
            if (!IsLive)
            {
                return;
            }

            _addButton.Enabled = enabled && _pendingItemId.HasValue;
            _searchBox.Enabled = enabled;
            _quantityBox.Enabled = enabled;
            foreach (var radio in _modeRadios)
            {
                radio.Indicator.Enabled = enabled;
                radio.Text.Enabled = enabled;
            }

            UpdateModeRadios();
            _refreshButton.Enabled = enabled && Entries.Count > 0;

            // Off the FIELD, not off the parameter: every caller sets
            // _isRefreshing first, and the label has to survive a rebuild
            // that re-enters here with a run still going.
            _refreshButton.Text = _isRefreshing
                ? AnalyzeRunningButtonText
                : AnalyzeButtonText;

            foreach (var row in _rows)
            {
                bool reorder = enabled && Mode == RankerMode.Cascade;
                // A row action draws its own disabled state - the whole key
                // fades, plate and mark together - so this is a click gate
                // only, not a click gate plus a hand-rolled repaint the way
                // the Image pair needed.
                if (row.Up != null)
                {
                    row.Up.Enabled = reorder && RankerPriorityOrdering.CanMoveUp(row.Index, Entries.Count);
                    row.Down.Enabled = reorder && RankerPriorityOrdering.CanMoveDown(row.Index, Entries.Count);
                }

                row.Remove.Enabled = enabled;
            }
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

            if (Entries.Count == 0)
            {
                ApplyStatusText("Add the items you are working toward, in priority order.", isError: false);
                return;
            }

            if (!_lastRefreshLocal.HasValue)
            {
                ApplyStatusText("Not yet calculated - press Analyze.", isError: false);
                return;
            }

            string text = StatusText.Stamp("Analyzed", _lastRefreshLocal.Value);
            var snapshot = _getSnapshot();
            if (snapshot != null)
            {
                text += " (" + StatusText.ForSnapshotAgeSuffix(DateTime.UtcNow - snapshot.CapturedAt) + ")";
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
            TooltipFacility.ApplyPlain(_statusLabel, string.Equals(shown, text, StringComparison.Ordinal) ? null : text);
            InlineSpinner.PlaceAfter(_spinner, _statusLabel, InlineSpinnerLayout.LabelGap);
        }

        private void UpdateBanner()
        {
            bool wantsBanner = _getSnapshot() == null;
            if (wantsBanner == (_bannerPanel != null))
            {
                return;
            }

            RebuildRows();
        }
    }
}
