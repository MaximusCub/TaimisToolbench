using System;
using System.Collections.Generic;
using System.Globalization;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// Settings tab content: lets the user set the coin value of the
    /// non-coin currencies and untradeable barter items a vendor takes
    /// (see Models/CurrencyValuation.cs), used to compare vendor offers
    /// and persisted through ModuleSettings. Plan-level
    /// defaults (price basis, own materials) remain on the Crafting Plan
    /// tab - only informational text about them is shown here.
    /// </summary>
    internal class SettingsTabContent
    {
        // Curated list of common plan currencies: Karma, Laurels, Spirit
        // Shards, the Rift Essence tiers, and Astral Acclaim. The coin
        // currency itself (Gw2Constants.CoinCurrencyId) is never listed here
        // - it is already directly comparable and CurrencyValuation rejects
        // coin-keyed entries outright.
        //
        // Astral Acclaim (63) - dev/proposals/addendum-astral-acclaim.md P1: added so a
        // user CAN value it if they choose to, but with no suggested rate
        // (see the info line in BuildCurrencyValuationsSection below) - a
        // single implied copper-per-AA
        // anchor was rejected, since AA's per-item
        // deal quality varies across the Wizard's Vault and any one implied
        // rate would misrepresent that. Leaving this row blank (the
        // default) keeps AA out of price comparisons entirely, same as any
        // other unset currency.
        // Ids with no
        // CurrencyDecisionDefaults entry but still worth surfacing for a
        // user to value by hand - see CurrencyDecisionDefaults' own doc
        // comment for why each is absent from that table: Astral Acclaim's
        // per-item deal quality varies too much for any single suggested
        // rate, and the three Rift Essence tiers have no row at all in
        // gw2efficiency's own source table.
        private static readonly int[] CuratedCurrencyIdsWithoutDefault =
        {
            63, // Astral Acclaim
            78, // Fine Rift Essence
            79, // Rare Rift Essence
            80,  // Masterwork Rift Essence
        };

        // ModuleSettings.GetEffectiveCurrencyValuation applies EVERY entry
        // in that table to every real solve regardless of whether a
        // Settings row exists for it, so a defaulted currency with no row
        // here was invisible and unclearable - no way to inspect it, know
        // it was silently tipping a vendor-vs-TP comparison, or turn it
        // off (Feature 1's own three-state requirement: set / default /
        // cleared, each visible and each reachable). CurrencyDecisionDefaults
        // is now the single source of truth for which defaulted ids get a
        // row - adding a new default there automatically gets a Settings
        // row here too, with no second list to remember to keep in sync.
        private static readonly int[] CuratedCurrencyIds = BuildCuratedCurrencyIds();

        private static int[] BuildCuratedCurrencyIds()
        {
            var ids = new SortedSet<int>(CurrencyDecisionDefaults.DefaultCopperPerUnit.Keys);
            foreach (int id in CuratedCurrencyIdsWithoutDefault)
            {
                ids.Add(id);
            }

            var result = new int[ids.Count];
            ids.CopyTo(result);
            return result;
        }

        // BarterItemDecisionDefaults is the single source of truth for
        // which barter items get a row, exactly as CurrencyDecisionDefaults
        // is for currencies: every defaulted item must be inspectable and
        // clearable, and adding an entry there adds a row here with no
        // second list to keep in sync. Sorted by NAME, not id - an id the
        // user never sees is a meaningless sort key, and these rows arrive
        // with their names already curated beside their values.
        private static readonly int[] CuratedBarterItemIds = BuildCuratedBarterItemIds();

        private static int[] BuildCuratedBarterItemIds()
        {
            var ids = new List<int>(BarterItemDecisionDefaults.Defaults.Keys);
            ids.Sort((a, b) => string.Compare(
                BarterItemDecisionDefaults.Defaults[a].Name,
                BarterItemDecisionDefaults.Defaults[b].Name,
                StringComparison.Ordinal));
            return ids.ToArray();
        }

        /// <summary>
        /// The item ids whose icons this tab draws, for whoever warms the
        /// item metadata that resolves them (Module). Exposed rather than
        /// re-derived at the caller so the fetch cannot ask for a different
        /// set than the grid shows.
        /// </summary>
        internal static IReadOnlyList<int> BarterItemIconIds
        {
            get { return Array.AsReadOnly(CuratedBarterItemIds); }
        }

        private static readonly Color InfoTextColor = new Color(170, 170, 170);
        private static readonly Color SectionDividerColor = new Color(130, 130, 130);
        private static readonly Color ErrorTextColor = new Color(255, 100, 100);
        private static readonly Color SuccessTextColor = new Color(150, 200, 150);
        private static readonly Color WarningTextColor = new Color(255, 200, 60);

        private const int SaveBarHeight = 40;

        // Every horizontal constant on this tab now comes from
        // SettingsFormLayout, which derives them from the plan tables' own
        // pinned-right-edge rule; these are compile-time aliases so the call
        // sites below read as geometry rather than as lookups.
        private const int RowHeight = SettingsFormLayout.SettingsRowHeight;
        private const int NameColumnX = SettingsFormLayout.CellLeftPad;
        private const int InputWidth = SettingsFormLayout.InputWidth;
        private const int RowGap = SettingsFormLayout.SettingsRowGap;

        // PlanContentHeightMath's SectionTitle band, aliased rather than
        // re-derived: these headings are the same tier and rule.
        private const int SectionHeaderRowHeight = PlanContentHeightMath.SectionHeaderRowHeight;
        private const int SectionHeaderTitleY = PlanContentHeightMath.SectionHeaderTitleY;

        // 22, not 20: an info line sits at y=2 and its lowest Font16 ink is
        // y=23, so 22 leaves the same 1px overhang 20 left Font14's y=21.
        private const int InfoRowHeight = SettingsFormLayout.DescriptionLineHeight;

        // Y of a Body-16 label inside a RowHeight row, and of a 26px input
        // inside the same row - both unchanged from the flat form this board
        // replaces.
        private const int RowLabelY = 7;
        private const int RowInputY = 3;
        private const int InputHeight = 26;

        private static readonly Logger Logger = Logger.GetLogger<SettingsTabContent>();

        /// <summary>
        /// What a settings row's control cluster is, and therefore what
        /// <see cref="LayoutFormRow"/> has to pin to the column's right
        /// edge. Three shapes cover the tab.
        /// </summary>
        private enum FormRowKind
        {
            /// <summary>Text box plus the shared unit/error tag slot.</summary>
            Input,

            /// <summary>Slider, live readout, Test button.</summary>
            Volume,

            /// <summary>A Checkbox standing in for the name, no cluster.</summary>
            Checkbox,

            /// <summary>A Dropdown over a fixed option list, no tag slot.</summary>
            Dropdown,
        }

        /// <summary>
        /// One row of the section board: a flexing name and a cluster pinned
        /// to the column's right edge, optionally with its own wrapped
        /// description line beneath it.
        /// </summary>
        private sealed class FormRow
        {
            public FormRowKind Kind;
            public Panel Panel;

            public Label NameLabel;
            public string NameText;

            public TextBox Input;

            public Dropdown Selector;

            // The row's ONE tag slot, shared by two labels at the same spot:
            // the unit hint, replaced by the validation error while the
            // typed value will not parse. Banded at max(widest unit, widest
            // error) across the section so the column cannot move when a row
            // fails - see SettingsFormLayout.TagX.
            public Label Tag;
            public Label Error;
            public string UnitText;
            public string ErrorText;
            public int TagBandWidth;

            public TrackBar Slider;
            public Label Readout;
            public Control TestButton;

            public Checkbox Checkbox;

            // A row-level counterpart to SectionBlock.Chip, for the one row
            // whose save behaviour differs from its section's - see
            // AddLogDiagnosticsRow. Pinned to the same right edge the tag
            // slot above uses, so the two read as one column.
            public Panel Chip;
            public int ChipWidth;

            public string DescriptionText;
            public Label DescriptionLabel;

            /// <summary>Width of the pinned cluster, which is what the name
            /// column's budget is taken against.</summary>
            public int ClusterWidth;
        }

        /// <summary>
        /// One block on the section board: a title band with an optional
        /// right-pinned chip, optional section-level prose, and its rows.
        /// </summary>
        private sealed class SectionBlock
        {
            public Panel Panel;
            public Label TitleLabel;
            public Panel Rule;
            public Panel Chip;
            public int ChipWidth;
            public int ChipY;

            public readonly List<string> Prose = new List<string>();
            public readonly List<Label> ProseLabels = new List<Label>();
            public readonly List<FormRow> Rows = new List<FormRow>();
        }

        private class CurrencyRow
        {
            // A wallet currency id, or - when IsBarterItem - a GW2 item id.
            // The two are different id spaces that collide numerically, so
            // every lookup below has to pick its table off IsBarterItem
            // rather than off the number alone.
            public int Id;
            public bool IsBarterItem;
            public bool HasDefault;
            public long DefaultCopperPerUnit;

            // Full, un-ellipsized - the cell's name column flexes with the
            // column width, so the text it was shortened from is needed
            // again on every resize.
            public string Name;

            // The row's own one-line cell inside the currency grid, and the
            // rule along its bottom - both driven by ApplyCurrencyFilter.
            public Panel Cell;
            public Panel Divider;

            // The cell's leading icon, built only once this session's
            // metadata for this row's own id space has resolved - see
            // EnsureCurrencyRowIcon. Null until then, and null for the whole
            // session when the fetch never succeeds; the cell reserves the
            // band either way, so nothing moves when it appears.
            public Panel Icon;

            public Label NameLabel;
            public TextBox Input;

            // The cell's single tag slot, shared by two labels at the same
            // spot: DefaultLabel carries the persisted default/cleared state
            // (Feature 1 requires it VISIBLE, not hover-only), ErrorLabel
            // replaces it while the typed amount is unparseable. Only one is
            // ever shown - see SetCurrencyRowError.
            public Label DefaultLabel;
            public Label ErrorLabel;

            // Null for a currency with no CurrencyDecisionDefaults entry
            // (nothing to clear - see AddCurrencyRow). Holds the user's
            // in-memory intent for the NEXT Save (see SaveValuations); the
            // persisted state is shown by DefaultLabel.
            public Checkbox ClearCheckbox;
        }

        // One row per Homestead Refinement material
        // family. MaterialItemId is internal-only bookkeeping (never
        // displayed - see MaterialLabel) used solely to route the chosen
        // tier back to the right ModuleSettings entry.
        private class HomesteadTierRow
        {
            public int MaterialItemId;
            public string MaterialLabel;
            public Dropdown Selector;
        }

        private readonly ModuleSettings _settings;
        private readonly ModalDialog _modalDialog;

        // The session item-stat cache the barter rows' hovers read, from
        // the module's one ItemMetadataService. Never a fetch.
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;

        private readonly List<CurrencyRow> _rows = new List<CurrencyRow>();

        // Row names in _rows order, held so the filter keystroke path does
        // not rebuild a 47-entry list per character typed.
        private readonly List<string> _currencyNames = new List<string>();

        // This session's currency name/icon list, pushed in by Module once
        // the one /v2/currencies fetch resolves (SetCurrencyMetadata). Held
        // across Build cycles - the instance outlives its control tree - so
        // re-opening the tab does not blank the icons until a refetch. Null
        // means "not resolved yet", which is not the same as "no icon".
        private IReadOnlyDictionary<int, CurrencyMetadata> _currencyMetadata;

        // The barter-item half of the same thing, from the module's shared
        // ItemMetadataService (SetBarterItemMetadata). Held on the same terms
        // as the currency list above, and separate from it because a GW2
        // item id and a currency id are different id spaces - see
        // CurrencyRow.Id.
        private IReadOnlyDictionary<int, ItemMetadata> _barterItemMetadata;
        private readonly List<HomesteadTierRow> _homesteadRows = new List<HomesteadTierRow>();

        private FlowPanel _rootPanel;

        // The four short sections, packed into as many min-width columns as
        // the panel holds (see LayoutSectionBoard). Vendor Cost Valuations
        // is NOT one of them - it is a full-width grid below the board.
        private Panel _boardPanel;
        private readonly List<SectionBlock> _sections = new List<SectionBlock>();

        // One status label for the whole tab, next to the one Save button in
        // the header bar (see BuildSaveBar) - the four per-section Save rows
        // and their four status labels this replaced are recorded in
        // KNOWN-ISSUES #55.
        private Label _statusLabel;

        // The save bar's own controls. The dirty chip and Discard are hidden
        // entirely at zero unsaved changes - see SettingsSaveBarLayout.
        private Panel _saveBarPanel;
        private Label _dirtyChipLabel;
        private StandardButton _discardButton;
        private StandardButton _saveButton;
        private int _saveBarWidth;

        // Set while LoadAll is rewriting every control, so the change
        // handlers below do not run a full-form dirty check 50+ times for
        // one load.
        private bool _suspendDirtyRefresh;

        // The currency list's absolutely-positioned grid: one Panel holding
        // every cell, repacked by ApplyCurrencyFilter as rows are hidden and
        // held at its unfiltered height throughout (SetCurrencyGridHeight).
        private Panel _currencyGridPanel;
        private TextBox _currencyFilterInput;
        private Label _currencyCountLabel;

        // Column header over that grid: one "Currency"/"Copper per unit"
        // pair per grid column, repositioned with the columns themselves.
        // The unit belongs here rather than inside each 70px box - as a
        // placeholder it read as a label naming the box, not as a prompt to
        // type a number into it (bug 2, reported in game).
        private Panel _currencyHeaderPanel;
        private readonly List<Label> _currencyHeaderNames = new List<Label>();
        private readonly List<Label> _currencyHeaderUnits = new List<Label>();

        // Reused per filter pass (one entry per row, in _rows order) rather
        // than reallocated per keystroke: the rows whose amount did not
        // parse, which stay on screen through any filter.
        private bool[] _currencyForceVisible = new bool[0];

        // Every direct child of _rootPanel that spans the panel width, plus
        // the section-header rules inside them - re-widened together when
        // the window is resized (see ApplyPanelWidth). Without this the
        // controls keep the width the tab was first opened at, and a
        // narrowed window pushes the grid's right-hand column off-panel.
        private readonly List<Panel> _fullWidthPanels = new List<Panel>();

        // Width the content is currently laid out at (see ApplyPanelWidth).
        private int _panelWidth;

        // Holds the wrap/ellipsize half of a resize until the drag stops -
        // see ApplyPanelWidth. Lives as long as the view, so Teardown drops
        // it rather than leaving a waiter pointed at a disposed tree.
        private readonly ResizeSettleDebounce _resizeSettle;

        // The ONE "Diagnostics" checkbox + the two log-file
        // policy rows (max size / retention) - dev/proposals/d2-log-system.md Section 5.
        // No separate
        // ScrollDiagnosticsEnabled checkbox is surfaced here - see
        // ModuleSettings' own doc comment on that setting's backward-compat
        // read.
        private Checkbox _logDiagnosticsCheckbox;
        private TextBox _logMaxSizeInput;
        private FormRow _logMaxSizeRow;
        private TextBox _logRetentionDaysInput;
        private FormRow _logRetentionDaysRow;

        // Plan History's one setting rides in the Logging section rather
        // than a section of its own, so the board's column count does not
        // change for a single row.
        private TextBox _planHistoryMaxEntriesInput;
        private FormRow _planHistoryMaxEntriesRow;

        // Held only to be disposed - see DisposeClickVolumeSlider.
        private TrackBar _clickVolumeSlider;

        private Label _clickVolumeReadout;

        // Standalone
        // "Snapshot" section, its own new section (not folded into "Plan
        // Defaults", which is about per-plan choices - a different
        // concern). TextBox+Save+error-label idiom, same shape as the
        // Homestead tier rows above.
        private TextBox _snapshotRefreshIntervalInput;
        private FormRow _snapshotRefreshIntervalRow;

        // The control values as of the last load or successful save - what
        // an edit is measured against (see UnsavedChangeCount). Null until
        // the tab has been built once, which SettingsFormState reads as
        // "nothing to compare", not as "everything changed".
        private SettingsFormState _baseline;

        // False while Build is midway through replacing the row lists.
        // Blish runs Build off the UI thread (WindowBase2.ShowView does
        // view.DoLoad(...).ContinueWith(BuildView) with no scheduler),
        // while UnsavedChangeCount is called from the main thread's tab
        // handler - so without this, a tab switch landing during a build
        // would enumerate _rows while AddCurrencyRow appends to it and
        // throw "Collection was modified" out of Blish's input dispatch.
        // Volatile so the reader that sees true also sees the finished
        // lists. Same philosophy as the null-baseline early-out above:
        // a half-built form has nothing to compare, not everything.
        private volatile bool _buildComplete;

        /// <param name="getItemStatBlock">
        /// The session stat cache the grid's barter rows hover from. Never
        /// a fetch (ItemMetadataService.GetCachedStatBlock); null degrades
        /// those hovers to their icon+name header.
        /// </param>
        /// <param name="modalDialog">
        /// Raises the Discard confirm. Null degrades to discarding without
        /// one rather than losing the affordance - the confirm matrix is a
        /// UX rule, not a correctness gate.
        /// </param>
        public SettingsTabContent(
            ModuleSettings settings,
            Func<int, ItemStatBlock> getItemStatBlock,
            ModalDialog modalDialog = null)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _getItemStatBlock = getItemStatBlock;
            _modalDialog = modalDialog;

            _resizeSettle = new ResizeSettleDebounce(
                RefitTextAfterResizeSettle,
                MainThreadMarshal.Run,
                ResizeSettleDebounce.DefaultSettleMs,
                ex =>
                {
                    Logger.Warn(ex, "Settings text re-fit wait failed");
                    ModuleLog.Shared.Write(ModuleLogLevel.Warn, "settings",
                        $"Settings text re-fit wait failed: {ex.GetType().Name} - {ex.Message}");
                });

            // Lifetime subscription, dropped in Teardown.
            _settings.ClickSoundVolumePercent.SettingChanged += OnClickVolumeSettingChanged;
        }

        /// <summary>
        /// Announces, on the caller's thread, that a rebuild has been
        /// committed to. <see cref="Build"/> clears the same flag, but Blish
        /// only queues Build (ShowView does
        /// <c>view.DoLoad(...).ContinueWith(BuildView)</c>) after the main
        /// thread has already switched tabs - so between the switch and
        /// Build's first statement the flag would still read true from the
        /// PREVIOUS build, and a dirty check in that gap would enumerate the
        /// row lists the queued Build is about to clear. Called from the
        /// Settings tab's view factory, which TabbedWindow2.OnTabChanged
        /// evaluates on the main thread before either of those.
        /// </summary>
        public void BeginRebuild()
        {
            _buildComplete = false;
        }

        /// <summary>
        /// Releases what outlives this tab's own control tree. Called from
        /// Module.Unload; safe to call when the tab was never opened, and
        /// safe to call twice.
        /// </summary>
        public void Teardown()
        {
            _resizeSettle.Cancel();
            _settings.ClickSoundVolumePercent.SettingChanged -= OnClickVolumeSettingChanged;
            DisposeClickVolumeSlider();
            _clickVolumeReadout = null;
        }

        /// <summary>
        /// Keeps the slider and readout current when the setting changes
        /// elsewhere (Blish's Manage Modules slider - see Module.Initialize).
        /// Terminates rather than ping-pongs: the write is skipped when the
        /// slider already rounds to this percent, and an unchanged setting
        /// is not re-announced.
        /// </summary>
        private void OnClickVolumeSettingChanged(object sender, ValueChangedEventArgs<int> e)
        {
            int percent = ClickSoundVolume.Clamp(e.NewValue);

            var slider = _clickVolumeSlider;
            if (slider != null
                && ClickSoundVolume.TryPercentFromSliderValue(slider.Value, out int shown)
                && shown != percent)
            {
                slider.Value = percent;
            }

            if (_clickVolumeReadout != null)
            {
                _clickVolumeReadout.Text = ClickSoundVolume.FormatPercent(percent);
            }
        }

        /// <summary>
        /// Disposed, not just dropped: TrackBar hooks the static
        /// Control.Input.Mouse.LeftMouseButtonReleased in its constructor
        /// and unhooks only in DisposeControl, which no Blish teardown
        /// reaches (ViewContainer clears its children before the dispose
        /// walk visits them - measured, 1.3.0). Left alone, each rebuild
        /// and unload strands a live slider, and this whole object with it,
        /// on that handler; the tab's other control types take no such
        /// static hooks.
        /// </summary>
        private void DisposeClickVolumeSlider()
        {
            _clickVolumeSlider?.Dispose();
            _clickVolumeSlider = null;
        }

        public void Build(Container container)
        {
            _buildComplete = false;

            _rows.Clear();
            _currencyNames.Clear();
            _currencyForceVisible = new bool[0];
            _fullWidthPanels.Clear();

            // Same hazard as the stale _homesteadRows below: these point at
            // the previous Build cycle's already-disposed controls until the
            // currency section is rebuilt further down.
            _currencyGridPanel = null;
            _currencyFilterInput = null;
            _currencyCountLabel = null;
            _currencyHeaderPanel = null;
            _currencyHeaderNames.Clear();
            _currencyHeaderUnits.Clear();
            _statusLabel = null;
            _statusFullText = "";

            // Same hazard: the previous cycle's board blocks and save-bar
            // controls are disposed with their container.
            _sections.Clear();
            _boardPanel = null;
            _dirtyChipLabel = null;
            _discardButton = null;
            _saveButton = null;

            // The previous cycle's slider; Teardown handles the last one.
            DisposeClickVolumeSlider();
            _clickVolumeReadout = null;

            // Dropped before the controls it describes are replaced: a
            // baseline left over from the previous Build cycle would be
            // compared against a freshly loaded form and report the
            // difference between two sessions as unsaved edits. LoadAll
            // below takes the new one.
            _baseline = null;

            // Module.cs's Settings tab reuses this
            // SAME SettingsTabContent instance across every tab re-open
            // (unlike the Log tab's "new instance per open" factory), so
            // without clearing here, re-opening Settings more than once
            // per session would accumulate stale HomesteadTierRow entries
            // pointing at controls the previous Build() cycle's container
            // already disposed - LoadCurrentHomesteadTiers/
            // SaveHomesteadTiers would then read/write through them
            // alongside the current cycle's real rows.
            _homesteadRows.Clear();

            // The one content frame this tab places against: the container
            // less the scrollbar strip. The save bar uses the SAME width
            // even though it does not scroll, so its buttons land on the
            // vertical line the scrolling content's right edge holds below
            // them.
            int panelWidth = ContentWidth(container);
            _panelWidth = panelWidth;

            BuildSaveBar(container);

            _rootPanel = new FlowPanel()
            {
                Size = ContentSizeBelowSaveBar(container),
                Location = new Point(0, SaveBarHeight),
                FlowDirection = ControlFlowDirection.SingleTopToBottom,
                CanScroll = true,
                Parent = container,
            };

            container.Resized += (_, __) =>
            {
                _saveBarPanel.Size = new Point(container.ContentRegion.Width, SaveBarHeight);
                _rootPanel.Size = ContentSizeBelowSaveBar(container);
                ApplySaveBarWidth(ContentWidth(container));
                ApplyPanelWidth(ContentWidth(container));
            };

            _boardPanel = new Panel()
            {
                Size = new Point(panelWidth, 0),
                Parent = _rootPanel,
            };
            _fullWidthPanels.Add(_boardPanel);

            BuildSoundSection();
            BuildHomesteadRefinementSection();
            BuildLoggingSection();
            BuildSnapshotSection();
            LayoutSectionBoard(measureText: true);

            BuildCurrencyValuationsSection(panelWidth);

            LoadAll();
            RefreshDirtyState();
        }

        private static int ContentWidth(Container container)
        {
            int width = container.ContentRegion.Width - WindowSizing.ScrollbarAllowance;
            return width > 0 ? width : 0;
        }

        /// <summary>
        /// Loads every section from persisted settings and takes the
        /// baseline the dirty check compares against. Shared by Build and
        /// DiscardChanges so a discard restores exactly the state a fresh
        /// build would show.
        /// </summary>
        private void LoadAll()
        {
            // One dirty check at the end rather than one per control the
            // load rewrites - the handlers each run a whole-form capture.
            _suspendDirtyRefresh = true;
            try
            {
                LoadCurrentValuations();
                LoadCurrentHomesteadTiers();
                LoadCurrentLoggingSettings();
                LoadCurrentSnapshotSettings();
            }
            finally
            {
                _suspendDirtyRefresh = false;
            }

            // LoadCurrentValuations clears the per-row error tags, so the
            // rows a failed Save forced past the filter have to be
            // re-evaluated - otherwise a discard leaves them pinned.
            ApplyCurrencyFilter();

            _baseline = CaptureFormState();

            // Last line, deliberately: it publishes everything above it.
            _buildComplete = true;
        }

        /// <summary>
        /// Every save-gated control value on the tab, as the Blish-free
        /// SettingsFormState. The Diagnostics checkbox is absent by
        /// design - see that type's own doc comment.
        /// </summary>
        private SettingsFormState CaptureFormState()
        {
            var state = new SettingsFormState();

            foreach (var row in _rows)
            {
                state.AddText(
                    row.IsBarterItem
                        ? SettingsFormState.BarterItemAmountKey(row.Id)
                        : SettingsFormState.CurrencyAmountKey(row.Id),
                    row.Input?.Text);
                state.AddFlag(
                    row.IsBarterItem
                        ? SettingsFormState.BarterItemIgnoreKey(row.Id)
                        : SettingsFormState.CurrencyIgnoreKey(row.Id),
                    row.ClearCheckbox != null && row.ClearCheckbox.Checked);
            }

            foreach (var row in _homesteadRows)
            {
                state.AddText(
                    SettingsFormState.HomesteadTierKey(row.MaterialItemId),
                    row.Selector?.SelectedItem);
            }

            // Captured through null-conditionals rather than skipped when
            // the control is missing: the key set has to be identical
            // between baseline and capture, or an absent control would
            // itself read as a change.
            state.AddText(SettingsFormState.LogMaxSizeMbKey, _logMaxSizeInput?.Text);
            state.AddText(SettingsFormState.LogRetentionDaysKey, _logRetentionDaysInput?.Text);
            state.AddText(SettingsFormState.PlanHistoryMaxEntriesKey, _planHistoryMaxEntriesInput?.Text);
            state.AddText(
                SettingsFormState.SnapshotRefreshIntervalMinutesKey,
                _snapshotRefreshIntervalInput?.Text);

            return state;
        }

        /// <summary>
        /// How many fields differ from the last load or successful save.
        /// Returns the count rather than the changed keys because those
        /// keys carry currency and item ids, which are internal-only and
        /// must never reach a caller that might display them.
        ///
        /// <para>
        /// Zero until the tab has finished building once - see
        /// _buildComplete for the cross-thread reason.
        /// </para>
        /// </summary>
        public int UnsavedChangeCount()
        {
            if (!_buildComplete)
            {
                return 0;
            }

            return CaptureFormState().ChangedKeys(_baseline).Count;
        }

        /// <summary>
        /// Restores the last loaded/saved values into the controls and
        /// clears the save bar's status line, which would otherwise still
        /// report the outcome of a save the user has just walked back.
        /// </summary>
        public void DiscardChanges()
        {
            LoadAll();

            SetStatusText("");
        }

        private static Point ContentSizeBelowSaveBar(Container container)
        {
            int height = container.ContentRegion.Height - SaveBarHeight;
            return new Point(container.ContentRegion.Width, height > 0 ? height : 0);
        }

        /// <summary>
        /// The RESIZE entry point. Every row/header panel is built at the
        /// width the tab happened to open at, and the currency grid
        /// additionally derives its column count, column width and cell X
        /// positions from it - so without this, narrowing the window leaves
        /// the second column of cells beyond the panel's right edge,
        /// unreachable until the tab is closed and re-opened.
        /// <para>
        /// Positions and widths track the drag; the work that MEASURES text
        /// does not. Re-wrapping this tab's ten paragraphs and re-ellipsizing
        /// its fifty-odd names is hundreds of MeasureString calls, and a
        /// window drag delivers resize events at frame rate, so that half
        /// runs once at drag settle instead - the module's standing split
        /// (CraftingPlanView's re-ellipsis registry, MainView's row re-fit).
        /// </para>
        /// </summary>
        private void ApplyPanelWidth(int panelWidth)
        {
            // Resized fires on height-only changes too, and re-widening
            // every row re-flows the scrolling FlowPanel once per row - so
            // do nothing unless the width actually moved.
            if (panelWidth <= 0 || panelWidth == _panelWidth)
            {
                return;
            }

            _panelWidth = panelWidth;
            Relayout(measureText: false);
            _resizeSettle.Schedule();
        }

        /// <summary>
        /// The trailing half of a resize: the wraps and ellipses the live
        /// pass left at the previous width, re-fitted once the drag has
        /// stopped. Skipped while a rebuild is in flight, whose own Build
        /// pass measures everything anyway.
        /// </summary>
        private void RefitTextAfterResizeSettle()
        {
            if (!_buildComplete || _panelWidth <= 0)
            {
                return;
            }

            Relayout(measureText: true);
        }

        private void Relayout(bool measureText)
        {
            foreach (var panel in _fullWidthPanels)
            {
                panel.Width = _panelWidth;
            }

            LayoutSectionBoard(measureText);

            if (_currencyGridPanel == null)
            {
                return;
            }

            _currencyGridPanel.Width = _panelWidth;
            LayoutCurrencyFilterRow();
            LayoutCurrencyGridHeader();

            int columnWidth = SettingsCurrencyGridLayout.ComputeColumnWidth(_panelWidth);
            foreach (var row in _rows)
            {
                row.Cell.Width = columnWidth;
                row.Divider.Width = columnWidth;
                LayoutCurrencyCell(row, columnWidth, measureText);
            }

            SetCurrencyGridHeight();
            ApplyCurrencyFilter();
        }

        /// <summary>
        /// Holds the grid panel at its UNFILTERED height. Blish's Scrollbar
        /// resets the scroll position to the top whenever the scrolling
        /// container's content height changes (RecalculateLayout compares
        /// the previous scrollbar percent against the freshly recomputed one
        /// and zeroes ScrollDistance/TargetScrollDistance when they differ -
        /// decompiled from the shipped 1.3.0 binary), and it does so a frame
        /// later, so a resize cannot simply be undone in place. Sizing the
        /// grid to the filtered list would therefore snap the tab to the top
        /// on every filter keystroke that changes the match count; the cost
        /// of the fixed height is trailing blank space below a filtered
        /// list, which is why nothing but the section's two-line footnote
        /// follows the grid.
        /// </summary>
        private void SetCurrencyGridHeight()
        {
            if (_currencyGridPanel == null)
            {
                return;
            }

            _currencyGridPanel.Height = SettingsCurrencyGridLayout.ComputeHeight(
                _rows.Count, _panelWidth, CurrencyRowHeight);
        }

        // ---- Section board ----
        //
        // Four short sections packed into as many
        // SettingsFormLayout.SettingsFormMinColumnWidth columns as the panel
        // holds. Every block is measured at the resolved column width first
        // (a description wraps, so a block's height is a function of that
        // width) and placed second; ColumnBoardLayout owns the packing.
        private SectionBlock BeginSection(string title, params string[] prose)
        {
            var section = new SectionBlock
            {
                Panel = new Panel()
                {
                    Size = new Point(SettingsFormLayout.SettingsFormMinColumnWidth, SectionHeaderRowHeight),
                    Parent = _boardPanel,
                },
            };

            section.TitleLabel = new Label()
            {
                Text = title,
                Font = UiFonts.SectionTitle,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(NameColumnX, SectionHeaderTitleY),
                Parent = section.Panel,
            };

            // Same header rule as every CraftingPlanView section: 2px in
            // SectionDividerColor, bottom-anchored with 1px clearance (see
            // LabelHelpers.CreateRowDivider for why 1px lines and flush
            // anchoring are unsafe here). It spans the COLUMN now, and the
            // title sits at the column's own inset, so the two share a left
            // edge instead of the title floating inside its own rule.
            section.Rule = new Panel()
            {
                Size = new Point(SettingsFormLayout.SettingsFormMinColumnWidth, 2),
                Location = new Point(0, SectionHeaderRowHeight - 3),
                BackgroundColor = SectionDividerColor,
                Parent = section.Panel,
            };

            foreach (string line in prose)
            {
                section.Prose.Add(line);
                section.ProseLabels.Add(CreateWrappedLabel(section.Panel));
            }

            _sections.Add(section);
            return section;
        }

        /// <summary>
        /// The tab's one chip word. Only immediate-apply controls carry it:
        /// a standing "Save needed" on everything else is a colour that says
        /// nothing, and the save bar already carries the dirty state.
        /// </summary>
        private const string ImmediateApplyTagText = "Applies immediately";

        /// <summary>
        /// A right-pinned tag in a section's title band, for a section whose
        /// every control applies immediately.
        /// </summary>
        private static void AddSectionChip(SectionBlock section, string text, string tooltip)
        {
            PillColors.GetPillColors(PillKind.Locked, false, out Color border, out Color fill);
            section.ChipWidth = LabelHelpers.MeasureSmallTagWidth(text);

            // Centred in the band ABOVE its rule, so the tag clears the rule
            // the way the band's title does.
            section.ChipY = PlanRelayoutMath.CenterX(
                SectionHeaderRowHeight - 3, LabelHelpers.SmallTagHeight);
            section.Chip = LabelHelpers.CreateSmallTag(
                section.Panel, text, 0, section.ChipY, border, fill);
            LabelHelpers.ApplyTagTooltip(section.Chip, tooltip);
        }

        /// <summary>
        /// The same tag on ONE row, for a control whose save behaviour
        /// differs from its section's. Same chrome and same right edge as
        /// the section band's, so the two read as one vocabulary rather than
        /// two. The row's name column is budgeted against the tag through
        /// ClusterWidth, exactly as an input row is budgeted against its
        /// box.
        /// </summary>
        private static void AddRowChip(FormRow row, string text, string tooltip)
        {
            PillColors.GetPillColors(PillKind.Locked, false, out Color border, out Color fill);

            row.ChipWidth = LabelHelpers.MeasureSmallTagWidth(text);
            row.ClusterWidth = row.ChipWidth;
            row.Chip = LabelHelpers.CreateSmallTag(
                row.Panel, text, 0,
                PlanRelayoutMath.CenterX(RowHeight, LabelHelpers.SmallTagHeight),
                border, fill);
            LabelHelpers.ApplyTagTooltip(row.Chip, tooltip);
        }

        private static Label CreateWrappedLabel(Panel parent)
        {
            return new Label()
            {
                Font = UiFonts.Body,
                Text = "",
                AutoSizeWidth = false,
                AutoSizeHeight = false,
                TextColor = InfoTextColor,
                Parent = parent,
            };
        }

        /// <summary>
        /// Wraps one paragraph into an already-created label at (x, y) and
        /// returns the height it took: one
        /// <see cref="SettingsFormLayout.DescriptionLineHeight"/> row per
        /// physical line, which is NotesSectionLayoutMath's own precedent.
        /// <para>
        /// At <paramref name="measureText"/> false the paragraph keeps the
        /// wrap it already has and only its box moves - see
        /// <see cref="ApplyPanelWidth"/> for why. The label's own Height is
        /// the cache: it is written explicitly here, never auto-sized.
        /// </para>
        /// </summary>
        private static int LayoutWrappedLabel(
            Label label, string text, int x, int y, int budget, bool measureText)
        {
            if (budget < 20)
            {
                budget = 20;
            }

            if (measureText)
            {
                var wrapped = TextWrapMath.Wrap(
                    text, budget, budget, LabelHelpers.MeasureWith(UiFonts.Body));
                string joined = string.Join("\n", wrapped.Lines);

                if (!string.Equals(label.Text, joined, StringComparison.Ordinal))
                {
                    label.Text = joined;
                }

                label.Size = new Point(budget, wrapped.Lines.Count * InfoRowHeight);

                // The wrap only drops text at the line cap, and then the
                // full paragraph is the hover - the module's one overflow
                // idiom.
                TooltipFacility.ApplyPlain(label, wrapped.Truncated ? text : null);
            }
            else
            {
                label.Width = budget;
            }

            label.Location = new Point(x, y);
            return label.Height;
        }

        private void LayoutSectionBoard(bool measureText)
        {
            if (_boardPanel == null || _sections.Count == 0)
            {
                return;
            }

            int boardWidth = _panelWidth;
            int columnCount = ColumnBoardLayout.ComputeColumnCount(
                boardWidth, SettingsFormLayout.SettingsFormMinColumnWidth, _sections.Count);
            int columnWidth = ColumnBoardLayout.ComputeColumnWidth(boardWidth, columnCount);

            var heights = new List<int>(_sections.Count);
            foreach (var section in _sections)
            {
                heights.Add(LayoutSection(section, columnWidth, measureText));
            }

            var board = ColumnBoardLayout.Compute(
                heights, boardWidth, SettingsFormLayout.SettingsFormMinColumnWidth, SettingsFormLayout.SectionGap);

            for (int i = 0; i < _sections.Count; i++)
            {
                var placement = board.Blocks[i];
                _sections[i].Panel.Location = new Point(placement.X, placement.Y);
                _sections[i].Panel.Size = new Point(placement.Width, heights[i]);
            }

            _boardPanel.Height = board.Height;
        }

        private static int LayoutSection(SectionBlock section, int columnWidth, bool measureText)
        {
            section.Rule.Size = new Point(columnWidth, 2);
            if (section.Chip != null)
            {
                section.Chip.Location = new Point(
                    PlanRelayoutMath.RightAlignedX(
                        PlanRelayoutMath.PinnedRightEdge(columnWidth), section.ChipWidth),
                    section.ChipY);
            }

            int y = SectionHeaderRowHeight + SettingsFormLayout.TitleToContentGap;

            for (int i = 0; i < section.Prose.Count; i++)
            {
                y += LayoutWrappedLabel(
                    section.ProseLabels[i], section.Prose[i], NameColumnX, y,
                    SettingsFormLayout.SectionProseMaxWidth(columnWidth), measureText);
            }

            if (section.Prose.Count > 0)
            {
                y += RowGap;
            }

            for (int i = 0; i < section.Rows.Count; i++)
            {
                var row = section.Rows[i];
                row.Panel.Location = new Point(0, y);
                row.Panel.Size = new Point(columnWidth, RowHeight);
                LayoutFormRow(row, columnWidth, measureText);
                y += RowHeight;

                if (row.DescriptionLabel != null)
                {
                    // No gap: a description belongs to the row above it.
                    y += LayoutWrappedLabel(
                        row.DescriptionLabel, row.DescriptionText, NameColumnX, y,
                        SettingsFormLayout.DescriptionMaxWidth(columnWidth, row.ClusterWidth),
                        measureText);
                }

                if (i < section.Rows.Count - 1)
                {
                    y += RowGap;
                }
            }

            return y;
        }

        private static void LayoutFormRow(FormRow row, int columnWidth, bool measureText)
        {
            switch (row.Kind)
            {
                case FormRowKind.Input:
                    row.Input.Location = new Point(
                        SettingsFormLayout.InputX(columnWidth, row.TagBandWidth), RowInputY);
                    int tagX = SettingsFormLayout.TagX(columnWidth, row.TagBandWidth);
                    row.Tag.Location = new Point(tagX, RowLabelY);
                    row.Error.Location = new Point(tagX, RowLabelY);
                    break;

                case FormRowKind.Dropdown:
                    row.Selector.Location = new Point(
                        SettingsFormLayout.DropdownX(columnWidth),
                        PlanRelayoutMath.CenterX(RowHeight, SettingsFormLayout.DropdownHeight));
                    break;

                case FormRowKind.Volume:
                    row.Slider.Location =
                        new Point(SettingsFormLayout.VolumeSliderX(columnWidth), RowLabelY);
                    row.Readout.Location =
                        new Point(SettingsFormLayout.VolumeReadoutX(columnWidth), RowLabelY);
                    row.TestButton.Location =
                        new Point(SettingsFormLayout.TestButtonX(columnWidth), 1);
                    break;
            }

            if (row.Chip != null)
            {
                row.Chip.Location = new Point(
                    PlanRelayoutMath.RightAlignedX(
                        PlanRelayoutMath.PinnedRightEdge(columnWidth), row.ChipWidth),
                    PlanRelayoutMath.CenterX(RowHeight, LabelHelpers.SmallTagHeight));
            }

            if (row.NameLabel == null)
            {
                return;
            }

            int budget = SettingsFormLayout.NameMaxWidth(columnWidth, row.ClusterWidth);
            row.NameLabel.Width = budget;
            if (!measureText)
            {
                return;
            }

            string shortName = LabelHelpers.EllipsizeToWidth(UiFonts.Body, row.NameText, budget);
            if (!string.Equals(row.NameLabel.Text, shortName, StringComparison.Ordinal))
            {
                row.NameLabel.Text = shortName;
            }

            // Blish resolves a tooltip on the control under the cursor and
            // never bubbles, so the row panel carries it too.
            string full = string.Equals(shortName, row.NameText, StringComparison.Ordinal)
                ? null
                : row.NameText;
            TooltipFacility.ApplyPlain(row.NameLabel, full);
            TooltipFacility.ApplyPlain(row.Panel, full);
        }

        /// <summary>
        /// The tag slot's two labels share one spot, so only one is ever
        /// shown: the red error takes it while a value will not parse, and
        /// the unit hint comes back when it does.
        /// </summary>
        private static void SetRowError(FormRow row, string text)
        {
            if (row == null)
            {
                return;
            }

            row.Error.Text = text ?? "";
            row.Tag.Visible = row.Error.Text.Length == 0;
        }

        /// <summary>
        /// Bands a section's tag slot at max(widest unit, widest error)
        /// across its rows - the header-floored band rule, applied to a form
        /// - so the column cannot move when one row fails validation.
        /// Font-derived, so it is measured once at build and never again.
        /// </summary>
        private static void BandSectionTagSlot(SectionBlock section)
        {
            var font = UiFonts.Body;
            int band = 0;
            foreach (var row in section.Rows)
            {
                if (row.Kind != FormRowKind.Input)
                {
                    continue;
                }

                band = Math.Max(band, (int)Math.Ceiling(font.MeasureString(row.UnitText ?? "").Width));
                band = Math.Max(band, (int)Math.Ceiling(font.MeasureString(row.ErrorText ?? "").Width));
            }

            foreach (var row in section.Rows)
            {
                if (row.Kind != FormRowKind.Input)
                {
                    continue;
                }

                row.TagBandWidth = band;
                row.ClusterWidth = SettingsFormLayout.InputClusterWidth(band);
            }
        }

        /// <summary>
        /// One settings row: a flexing name label and a text box plus its
        /// shared unit/error tag slot, both pinned to the column's right
        /// edge. The caller bands the slot once the section's rows all
        /// exist (see <see cref="BandSectionTagSlot"/>).
        /// </summary>
        private FormRow AddInputRow(
            SectionBlock section, string name, string unitText, string errorText, string description)
        {
            var row = new FormRow
            {
                Kind = FormRowKind.Input,
                NameText = name,
                UnitText = unitText,
                ErrorText = errorText,
                DescriptionText = description,
            };

            row.Panel = new Panel()
            {
                Size = new Point(SettingsFormLayout.SettingsFormMinColumnWidth, RowHeight),
                Parent = section.Panel,
            };

            row.NameLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = name,
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Location = new Point(NameColumnX, RowLabelY),
                Parent = row.Panel,
            };

            row.Input = new TextBox()
            {
                Size = new Point(InputWidth, InputHeight),
                Parent = row.Panel,
            }.ReleaseOnDispose().ReleaseOnEnter();
            row.Input.TextChanged += (_, __) => RefreshDirtyState();

            row.Tag = new Label()
            {
                Font = UiFonts.Body,
                Text = unitText,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                TextColor = InfoTextColor,
                Parent = row.Panel,
            };

            row.Error = new Label()
            {
                Font = UiFonts.Body,
                Text = "",
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                TextColor = ErrorTextColor,
                Parent = row.Panel,
            };

            if (description != null)
            {
                row.DescriptionLabel = CreateWrappedLabel(section.Panel);
            }

            section.Rows.Add(row);
            return row;
        }

        /// <summary>
        /// One settings row whose value is picked from a fixed list: a
        /// flexing name and a dropdown pinned to the column's right edge.
        /// A dropdown cannot hold a value that will not parse, so the row
        /// has no tag slot and no error label.
        /// </summary>
        private FormRow AddDropdownRow(SectionBlock section, string name, IEnumerable<string> options)
        {
            var row = new FormRow
            {
                Kind = FormRowKind.Dropdown,
                NameText = name,
                ClusterWidth = SettingsFormLayout.DropdownWidth,
            };

            row.Panel = new Panel()
            {
                Size = new Point(SettingsFormLayout.SettingsFormMinColumnWidth, RowHeight),
                Parent = section.Panel,
            };

            row.NameLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = name,
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Location = new Point(NameColumnX, RowLabelY),
                Parent = row.Panel,
            };

            row.Selector = new Dropdown()
            {
                Size = new Point(
                    SettingsFormLayout.DropdownWidth, SettingsFormLayout.DropdownHeight),
                Parent = row.Panel,
            };

            foreach (string option in options)
            {
                row.Selector.Items.Add(option);
            }

            row.Selector.ValueChanged += (_, __) => RefreshDirtyState();

            section.Rows.Add(row);
            return row;
        }

        private void BuildCurrencyValuationsSection(int panelWidth)
        {
            AddSectionHeader(
                "Vendor Cost Valuations", panelWidth,
                "Price basis and both \"own materials\" choices are set per plan in the Crafting Plan tab.");
            AddInfoLine(
                "Coin value per unit of each currency and barter item a vendor takes, used to compare vendor offers.",
                panelWidth);
            // The one sentence that names the interaction. Bug 2, reported
            // in game: with the unit inside the box and only a grey "default N"
            // beside it, the row read as three read-only labels - nothing
            // said an amount could be typed over the default at all.
            AddInfoLine("Type a whole number of copper in a row's box and press Save to override its default.", panelWidth);
            // "Leave a currency unset..." and "Some currencies show a
            // default estimate..." used to sit here. Both are carried by the
            // hovers of the controls they describe (the amount box's own
            // tooltip and the default tag's, in AddCurrencyRow), reworded to
            // suit the control rather than repeated verbatim - so on the
            // panel they were a second copy of a sentence the reader already
            // has where it applies. Note the amount box states the
            // leave-it-blank clause on BOTH of its branches: the default
            // tag, which is the other place that clause lives, does not
            // exist on the rows with no default. The price-basis pointer
            // moved to this section's title hover: it points at another tab
            // rather than instructing about a control here.
            AddCurrencyFilterRow(panelWidth);
            AddCurrencyGridHeader(panelWidth);

            _currencyGridPanel = new Panel()
            {
                Size = new Point(panelWidth, 0),
                Parent = _rootPanel,
            };

            int columnWidth = SettingsCurrencyGridLayout.ComputeColumnWidth(panelWidth);
            foreach (int currencyId in CuratedCurrencyIds)
            {
                AddCurrencyRow(currencyId, columnWidth);
            }

            // Barter items share the grid rather than getting a second one:
            // to the user these rows do the same job (what is one unit of
            // this worth), the filter box searches both with one keystroke,
            // and a second grid would need its own header, filter and
            // count. They follow the currencies rather than interleaving
            // with them so the two id spaces stay visibly separate.
            foreach (int itemId in CuratedBarterItemIds)
            {
                AddCurrencyRow(itemId, columnWidth, isBarterItem: true);
            }

            _currencyForceVisible = new bool[_rows.Count];
            SetCurrencyGridHeight();
            ApplyCurrencyFilter();

            // The two families the grid deliberately does not price, as one
            // footnote under it. Astral Acclaim is untradable and earned via
            // capped seasonal play; the Black Lion family is gem-store
            // RNG-chest currency. Neither has a rate this module can
            // honestly suggest, so Astral Acclaim ships unset - which simply
            // keeps it out of price comparisons, like any other unset
            // currency - and the Black Lion rows are left out of the grid
            // entirely: an unlisted item is unvalued, and its vendor offers
            // still appear, just unranked.
            // dev/proposals/addendum-astral-acclaim.md P1.
            AddInfoLine(
                "Astral Acclaim is untradable and earned via capped play - its value is personal, so no rate is suggested here.\n"
                + "Black Lion tickets, statuettes and vouchers come from gem-store chests - their value is personal too, so they are not listed.",
                panelWidth);
        }

        // The slider stays FIXED at this width whatever the column does:
        // only the name flexes, exactly as in a plan table, because a 700px
        // volume slider on a wide column is a worse artefact than the space
        // it fills. Its cluster's own geometry is SettingsFormLayout's.
        private const int SliderWidth = SettingsFormLayout.SliderWidth;
        private const int SliderHeight = 16;
        private const int ReadoutWidth = SettingsFormLayout.ReadoutWidth;
        private const int TestButtonWidth = SettingsFormLayout.TestButtonWidth;

        /// <summary>
        /// The click-volume row: label, slider, live readout, Test button.
        /// Immediate-apply, unlike the save-gated sections (recorded in
        /// KNOWN-ISSUES #52) - a volume is tuned by ear - so the row must stay
        /// out of CaptureFormState, or every drag would count as an unsaved
        /// change. The section's band says so with a tag. The tab's only
        /// other immediate-apply control is the Diagnostics checkbox, which
        /// sits in a save-gated section and so carries the same tag on its
        /// own row; nothing else on the tab is tagged.
        /// </summary>
        private void BuildSoundSection()
        {
            var section = BeginSection("Sound");
            AddSectionChip(
                section, ImmediateApplyTagText,
                "Changes in this section take effect as you make them. Nothing here waits for Save.");

            AddClickVolumeRow(section);
        }

        private void AddClickVolumeRow(SectionBlock section)
        {
            var row = new FormRow
            {
                Kind = FormRowKind.Volume,
                NameText = "Click volume",
                ClusterWidth = SettingsFormLayout.WidestClusterWidth,
                DescriptionText =
                    "Volume of this module's own click, played whenever you press one of its buttons, rows or pills. Drag to 0 to turn it off.",
            };

            var rowPanel = new Panel()
            {
                Size = new Point(SettingsFormLayout.SettingsFormMinColumnWidth, RowHeight),
                Parent = section.Panel,
            };
            row.Panel = rowPanel;
            row.DescriptionLabel = CreateWrappedLabel(section.Panel);

            row.NameLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = row.NameText,
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Location = new Point(NameColumnX, RowLabelY),
                Parent = rowPanel,
            };

            int percent = _settings.GetClampedClickSoundVolumePercent();

            // Assigning MinValue/MaxValue is load-bearing even though they
            // match TrackBar's defaults: the setters are what fill the
            // snap table Ctrl+drag aggregates over, and on a TrackBar that
            // never had either assigned, Ctrl+drag throws (measured, 1.3.0).
            _clickVolumeSlider = new TrackBar()
            {
                MinValue = ClickSoundVolume.MinPercent,
                MaxValue = ClickSoundVolume.MaxPercent,
                Value = percent,
                Size = new Point(SliderWidth, SliderHeight),
                BasicTooltipText = "How loud this module's click plays. 0 turns it off. Does not change the checkbox click, which is Blish HUD's own.",
                Parent = rowPanel,
            };
            row.Slider = _clickVolumeSlider;

            _clickVolumeReadout = new Label()
            {
                Font = UiFonts.Body,
                Text = ClickSoundVolume.FormatPercent(percent),
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Width = ReadoutWidth,
                HorizontalAlignment = HorizontalAlignment.Right,
                Parent = rowPanel,
            };
            row.Readout = _clickVolumeReadout;

            // Subscribed after the initial Value assignment so it does not
            // fire during Build. The setting is all this writes; readout and
            // player hang off SettingChanged instead, so both sliders drive
            // them by one path (see Module.Initialize).
            _clickVolumeSlider.ValueChanged += (_, e) =>
            {
                if (!ClickSoundVolume.TryPercentFromSliderValue(e.Value, out int newPercent))
                {
                    return;
                }

                _settings.ClickSoundVolumePercent.Value = newPercent;
            };

            // No Click handler: a FeedbackButton's own press feedback plays
            // the click at the slider's current value, which IS the audition.
            // Its hover carries the checkbox footnote - the one fact about
            // this control that is Blish's behaviour rather than the
            // module's.
            row.TestButton = new FeedbackButton()
            {
                Text = "Test",
                Size = new Point(TestButtonWidth, UiMetrics.ButtonHeight),
                BasicTooltipText = "Play the click at the volume set here. Checkboxes are the exception: they play Blish HUD's own click, which this does not change.",
                Parent = rowPanel,
            };

            section.Rows.Add(row);
        }

        /// <summary>
        /// Three per-material rows (Fiber/Metal/Wood), each a dropdown over
        /// the three upgrade states and saved with the rest of the tab. The
        /// setting is entered by hand because no account endpoint reports
        /// which of these upgrades have been bought. Labels name the
        /// material family only - no raw item/vendor ids are ever displayed
        /// (repo invariant).
        /// </summary>
        private void BuildHomesteadRefinementSection()
        {
            var section = BeginSection(
                "Homestead Refinement",
                "Which Homestead Refinement efficiency upgrades you have bought.",
                "Raises how much Refined Homestead material each trade produces.");

            AddHomesteadTierRow(section, Gw2Constants.RefinedHomesteadFiberItemId, "Fiber (Farm)");
            AddHomesteadTierRow(section, Gw2Constants.RefinedHomesteadMetalItemId, "Metal (Metal Forge)");
            AddHomesteadTierRow(section, Gw2Constants.RefinedHomesteadWoodItemId, "Wood (Lumber Mill)");
        }

        private void AddHomesteadTierRow(SectionBlock section, int materialItemId, string materialLabel)
        {
            var form = AddDropdownRow(section, materialLabel, HomesteadTierOptions.All);

            _homesteadRows.Add(new HomesteadTierRow
            {
                MaterialItemId = materialItemId,
                MaterialLabel = materialLabel,
                Selector = form.Selector,
            });
        }

        private void LoadCurrentHomesteadTiers()
        {
            var tiers = _settings.GetHomesteadEfficiencyTiers();

            foreach (var row in _homesteadRows)
            {
                row.Selector.SelectedItem =
                    HomesteadTierOptions.TextForTier(tiers.GetTier(row.MaterialItemId));
            }
        }

        /// <summary>
        /// Writes the three dropdowns to their settings entries. Every
        /// option maps to a tier in range, so this section has no rejected
        /// rows and reports no invalid count.
        /// </summary>
        private void SaveHomesteadTiers()
        {
            foreach (var row in _homesteadRows)
            {
                int tier = HomesteadTierOptions.TierForText(row.Selector.SelectedItem);

                if (row.MaterialItemId == Gw2Constants.RefinedHomesteadFiberItemId)
                {
                    _settings.HomesteadFiberTier.Value = tier;
                }
                else if (row.MaterialItemId == Gw2Constants.RefinedHomesteadMetalItemId)
                {
                    _settings.HomesteadMetalTier.Value = tier;
                }
                else if (row.MaterialItemId == Gw2Constants.RefinedHomesteadWoodItemId)
                {
                    _settings.HomesteadWoodTier.Value = tier;
                }
            }
        }

        /// <summary>
        /// One "Diagnostics" checkbox (idiom (a),
        /// immediate-apply - matches ValueOwnMaterials above) plus two
        /// TextBox+Save rows (idiom (b) - matches the Vendor Cost
        /// Valuations rows) for the log file's size cap and retention
        /// window. This is
        /// the ONE diagnostics toggle for the whole module per the
        /// tab-roadmap-proposal synthesis (Section 2.1) - no separate
        /// ScrollDiagnosticsEnabled checkbox is added alongside it.
        /// </summary>
        private void BuildLoggingSection()
        {
            var section = BeginSection(
                "Logging",
                "Controls the module's own log file (data/module_log.jsonl), separate from Blish HUD's own log.",
                "The Log tab always shows the current session regardless of these settings.");

            AddLogDiagnosticsRow(section);

            _logMaxSizeRow = AddInputRow(
                section, "Log max size", "MB (1-1000)", "Must be 1-1000", null);
            _logMaxSizeInput = _logMaxSizeRow.Input;

            _logRetentionDaysRow = AddInputRow(
                section, "Log retention", "days (1-365)", "Must be 1-365", null);
            _logRetentionDaysInput = _logRetentionDaysRow.Input;

            _planHistoryMaxEntriesRow = AddInputRow(
                section, "Plan history kept", "plans (5-200)", "Must be 5-200",
                "How many previously-generated plans the Plan History tab keeps. Pinned entries are never removed.");
            _planHistoryMaxEntriesInput = _planHistoryMaxEntriesRow.Input;

            BandSectionTagSlot(section);
        }

        /// <summary>
        /// A Checkbox row: the box IS the name control, and its 92-character
        /// explanation is the row's own wrapped description line rather than
        /// a fourth left column at x=186 matching nothing.
        /// <para>
        /// It is also the tab's one immediate-apply control inside a
        /// save-gated section, so it carries the "Applies immediately" tag
        /// at ROW level that Sound carries at section level. Without it the
        /// chip vocabulary lies by omission: the tab teaches that an
        /// untagged section waits for Save, and this box does not - ticking
        /// it writes the setting there and then, and Discard cannot take it
        /// back (LoadCurrentLoggingSettings never touches the checkbox,
        /// because CaptureFormState deliberately excludes it).
        /// </para>
        /// </summary>
        private void AddLogDiagnosticsRow(SectionBlock section)
        {
            var row = new FormRow
            {
                Kind = FormRowKind.Checkbox,
                DescriptionText =
                    "Log fine-grained diagnostic events (including scroll machinery) to the Log tab and file.",
            };

            row.Panel = new Panel()
            {
                Size = new Point(SettingsFormLayout.SettingsFormMinColumnWidth, RowHeight),
                Parent = section.Panel,
            };
            row.DescriptionLabel = CreateWrappedLabel(section.Panel);

            AddRowChip(
                row, ImmediateApplyTagText,
                "This box takes effect the moment you tick it - it does not wait for Save, "
                    + "and Discard does not undo it. The two boxes below it do wait for Save.");

            _logDiagnosticsCheckbox = new Checkbox()
            {
                Text = "Diagnostics logging",
                Checked = _settings.LogDiagnosticsEnabled.Value,
                Location = new Point(NameColumnX, RowLabelY),
                Parent = row.Panel,
            };
            row.Checkbox = _logDiagnosticsCheckbox;

            _logDiagnosticsCheckbox.CheckedChanged += (_, e) =>
            {
                _settings.LogDiagnosticsEnabled.Value = e.Checked;
            };

            section.Rows.Add(row);
        }

        private void LoadCurrentLoggingSettings()
        {
            if (_logMaxSizeInput != null)
            {
                long mb = _settings.LogMaxSizeBytes.Value / (1024 * 1024);
                _logMaxSizeInput.Text = mb.ToString(CultureInfo.InvariantCulture);
            }

            SetRowError(_logMaxSizeRow, "");

            if (_logRetentionDaysInput != null)
            {
                _logRetentionDaysInput.Text = _settings.LogRetentionDays.Value.ToString(CultureInfo.InvariantCulture);
            }

            SetRowError(_logRetentionDaysRow, "");

            if (_planHistoryMaxEntriesInput != null)
            {
                _planHistoryMaxEntriesInput.Text =
                    _settings.PlanHistoryMaxEntries.Value.ToString(CultureInfo.InvariantCulture);
            }

            SetRowError(_planHistoryMaxEntriesRow, "");
        }

        private int SaveLoggingSettings()
        {
            int invalidCount = 0;

            SetRowError(_logMaxSizeRow, "");
            if (SettingsInputParser.TryParseLogMaxSizeMb(_logMaxSizeInput?.Text, out long maxSizeBytes))
            {
                _settings.LogMaxSizeBytes.Value = (int)maxSizeBytes;
            }
            else if (_logMaxSizeRow != null)
            {
                SetRowError(_logMaxSizeRow, _logMaxSizeRow.ErrorText);
                invalidCount++;
            }

            SetRowError(_logRetentionDaysRow, "");
            if (SettingsInputParser.TryParseRetentionDays(_logRetentionDaysInput?.Text, out int retentionDays))
            {
                // Retention is only enforced once per session at
                // Module.Initialize (age-based pruning does not need
                // per-write cost - dev/proposals/d2-log-system.md Section 4.2), so a
                // saved value here intentionally takes effect next session,
                // not immediately - nothing holds a live copy of it to keep
                // current, unlike the size cap above.
                _settings.LogRetentionDays.Value = retentionDays;
            }
            else if (_logRetentionDaysRow != null)
            {
                SetRowError(_logRetentionDaysRow, _logRetentionDaysRow.ErrorText);
                invalidCount++;
            }

            SetRowError(_planHistoryMaxEntriesRow, "");
            if (SettingsInputParser.TryParsePlanHistoryMaxEntries(_planHistoryMaxEntriesInput?.Text, out int maxEntries))
            {
                // Enforced at the next capture, not retroactively: a
                // lowered cap does not delete rows until a Generate next
                // runs the retention pass - nothing holds a live copy.
                _settings.PlanHistoryMaxEntries.Value = maxEntries;
            }
            else if (_planHistoryMaxEntriesRow != null)
            {
                SetRowError(_planHistoryMaxEntriesRow, _planHistoryMaxEntriesRow.ErrorText);
                invalidCount++;
            }

            return invalidCount;
        }

        /// <summary>
        /// One TextBox+Save
        /// row for SnapshotRefreshIntervalMinutes - replaces Module.cs's
        /// previously-hardcoded StaleThreshold constant, so the Snapshot
        /// tab's own staleness indicator and Module's auto-refresh trigger
        /// read the same number and can never silently disagree about what
        /// "stale" means.
        /// </summary>
        private void BuildSnapshotSection()
        {
            var section = BeginSection("Snapshot");

            _snapshotRefreshIntervalRow = AddInputRow(
                section, "Refresh interval", "minutes (1-120)", "Must be 1-120",
                "How long a cached account snapshot may sit before an automatic background refresh runs.");
            _snapshotRefreshIntervalInput = _snapshotRefreshIntervalRow.Input;

            BandSectionTagSlot(section);
        }

        private void LoadCurrentSnapshotSettings()
        {
            if (_snapshotRefreshIntervalInput != null)
            {
                _snapshotRefreshIntervalInput.Text = _settings.SnapshotRefreshIntervalMinutes.Value.ToString(CultureInfo.InvariantCulture);
            }

            SetRowError(_snapshotRefreshIntervalRow, "");
        }

        private int SaveSnapshotSettings()
        {
            int invalidCount = 0;

            SetRowError(_snapshotRefreshIntervalRow, "");
            if (SettingsInputParser.TryParseRefreshIntervalMinutes(_snapshotRefreshIntervalInput?.Text, out int minutes))
            {
                _settings.SnapshotRefreshIntervalMinutes.Value = minutes;
            }
            else if (_snapshotRefreshIntervalRow != null)
            {
                SetRowError(_snapshotRefreshIntervalRow, _snapshotRefreshIntervalRow.ErrorText);
                invalidCount++;
            }

            return invalidCount;
        }

        private void AddSectionHeader(string title, int panelWidth, string tooltip = null)
        {
            var headerPanel = new Panel()
            {
                Size = new Point(panelWidth, SectionHeaderRowHeight),
                Parent = _rootPanel,
            };
            _fullWidthPanels.Add(headerPanel);

            var titleLabel = new Label()
            {
                Text = title,
                Font = UiFonts.SectionTitle,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(NameColumnX, SectionHeaderTitleY),
                Parent = headerPanel,
            };
            TooltipFacility.ApplyPlain(titleLabel, tooltip);

            // Same header rule as every CraftingPlanView section: 2px in
            // SectionDividerColor, bottom-anchored with 1px clearance
            // (see LabelHelpers.CreateRowDivider for why 1px lines and
            // flush anchoring are unsafe here).
            _fullWidthPanels.Add(new Panel()
            {
                Size = new Point(panelWidth, 2),
                Location = new Point(0, SectionHeaderRowHeight - 3),
                BackgroundColor = SectionDividerColor,
                Parent = headerPanel,
            });
        }

        /// <summary>
        /// One note of the full-width section's own prose: its header notes
        /// and its closing footnote. Each embedded newline starts a new
        /// line and nothing else breaks - the strings are written short
        /// enough to sit on one line at
        /// <see cref="WindowSizing.MinWindowWidth"/>, so a wrap budget here
        /// would only strand the width the section itself uses. The label
        /// auto-sizes, so a resize moves nothing.
        /// </summary>
        private void AddInfoLine(string text, int panelWidth)
        {
            int lines = 1;
            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '\n')
                {
                    lines++;
                }
            }

            var rowPanel = new Panel()
            {
                Size = new Point(panelWidth, (lines * InfoRowHeight) + 2),
                Parent = _rootPanel,
            };
            _fullWidthPanels.Add(rowPanel);

            new Label()
            {
                Font = UiFonts.Body,
                Text = text,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(NameColumnX, 2),
                TextColor = InfoTextColor,
                Parent = rowPanel,
            };
        }

        // One line per currency: icon, name, input, Ignore, and one tag slot
        // that shows either the default/cleared state or an "Invalid"
        // warning. The cell's geometry lives in SettingsCurrencyGridLayout
        // so its SettingsCurrencyMinColumnWidth (the one/two-column
        // threshold) and its row height are derived from the same numbers
        // the controls are placed with, not hand-copied from them; these are
        // aliases, not a second copy.
        private const int CurrencyRowHeight = SettingsCurrencyGridLayout.CurrencyRowHeight;
        private const int CellNameX = SettingsCurrencyGridLayout.CellNameX;

        // The cell's icon is part of the name column, so its header rules
        // on the icon rather than on the name beside it.
        private static readonly int CellHeaderX = ColumnHeaderLabelMath.LabelX(
            SettingsCurrencyGridLayout.CellNameX, SettingsCurrencyGridLayout.CellIconX);

        private const int CellInputWidth = SettingsCurrencyGridLayout.CellInputWidth;
        private const int CellDividerClearance = SettingsCurrencyGridLayout.CellDividerClearance;
        private static readonly int CellTextY = SettingsCurrencyGridLayout.CellTextY;
        private static readonly int CellInputY = SettingsCurrencyGridLayout.CellControlY(InputHeight);
        private const int CurrencyFilterWidth = 200;

        // The filter row is an ordinary RowHeight form row, not a grid cell.
        // It borrowed the cell's own Y's while the two heights were within a
        // pixel of each other; the cell is 42 now, so it centres in its own
        // height instead.
        private static readonly int FilterInputY = (RowHeight - InputHeight) / 2;
        private static readonly int FilterTextY =
            (RowHeight - TypeRampMetrics.BodyInk.LineHeight) / 2;

        private void AddCurrencyFilterRow(int panelWidth)
        {
            var rowPanel = new Panel()
            {
                Size = new Point(panelWidth, RowHeight),
                Parent = _rootPanel,
            };
            _fullWidthPanels.Add(rowPanel);

            _currencyFilterInput = new TextBox()
            {
                Size = new Point(CurrencyFilterWidth, InputHeight),
                // The section's own left inset, not the cell's name x: this
                // row sits above the grid, not inside a cell, and lines up
                // with the section title and the cells' icon column.
                Location = new Point(SettingsFormLayout.CellLeftPad, FilterInputY),
                // "Search {scope}..." - the one placeholder shape the
                // module's other three search boxes use; this box was the
                // lone "Filter ..." spelling.
                PlaceholderText = "Search valuations...",
                Parent = rowPanel,
            }.ReleaseOnDispose().ReleaseOnEnter();
            _currencyFilterInput.TextChanged += (_, __) => ApplyCurrencyFilter();

            // Right-pinned, like every other count on a justified row: the
            // box states the query, the count states the result, and the
            // row uses the width between them.
            _currencyCountLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = "",
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                TextColor = InfoTextColor,
                Location = new Point(
                    SettingsFormLayout.CellLeftPad + CurrencyFilterWidth + 12, FilterTextY),
                Parent = rowPanel,
            };

            LayoutCurrencyFilterRow();
        }

        // Measured rather than read off the label: AutoSizeWidth resolves on
        // Blish's next layout pass, so Label.Width is still the PREVIOUS
        // text's width in the same call that assigned the new one.
        private void LayoutCurrencyFilterRow()
        {
            if (_currencyCountLabel == null)
            {
                return;
            }

            int width = (int)Math.Ceiling(
                UiFonts.Body.MeasureString(_currencyCountLabel.Text ?? "").Width);
            _currencyCountLabel.Location = new Point(
                PlanRelayoutMath.RightAlignedX(
                    PlanRelayoutMath.PinnedRightEdge(_panelWidth), width),
                FilterTextY);
        }

        // The plan tables' column-header band, aliased: same tier over the
        // same kind of data columns.
        private const int CurrencyHeaderTextY = PlanContentHeightMath.ColumnHeaderLabelY;

        /// <summary>
        /// One "Currency"/"Copper per unit" pair per grid column, sitting on
        /// the same X's as the cells below it - repositioned in the same
        /// pass the cells are, so a header cannot drift off the column it
        /// names. The pairs are built once and hidden past the live column
        /// count: the count only changes with the panel width, and building
        /// labels costs more than hiding them per resize tick.
        /// </summary>
        private void AddCurrencyGridHeader(int panelWidth)
        {
            _currencyHeaderPanel = HeaderBands.CreateColumnHeaderBand(_rootPanel, panelWidth);
            _fullWidthPanels.Add(_currencyHeaderPanel);

            LayoutCurrencyGridHeader();
        }

        private const string NameHeaderText = "Currency or item";
        private const string UnitHeaderText = "Copper per unit";

        // Both memos are of compile-time strings in fonts that never change
        // at runtime, and both are read on every resize tick.
        private int _unitHeaderWidth;
        private int _widestCurrencyTagWidth;

        private int UnitHeaderWidth()
        {
            if (_unitHeaderWidth <= 0)
            {
                _unitHeaderWidth = LabelHelpers.MeasureWith(HeaderBands.Font)(UnitHeaderText);
            }

            return _unitHeaderWidth;
        }

        /// <summary>
        /// Widest string the cell's one tag slot can ink, over both curated
        /// defaults tables - what
        /// SettingsCurrencyGridLayout.UnitHeaderX centres the unit header
        /// over the right-hand end of.
        /// <para>
        /// Derived from the shipped tables rather than from the rows'
        /// CURRENT text: the tag follows what the user types and ticks, and
        /// a header re-centred on that would move while it was being read.
        /// The two families measured here are the widest the slot shows -
        /// see SettingsCurrencyGridLayout.CellTagWidth.
        /// </para>
        /// </summary>
        private int WidestCurrencyTagWidth()
        {
            if (_widestCurrencyTagWidth > 0)
            {
                return _widestCurrencyTagWidth;
            }

            var measure = LabelHelpers.MeasureWith(UiFonts.Body);
            int widest = 0;
            foreach (long value in AllCuratedDefaults())
            {
                string amount = value.ToString(CultureInfo.InvariantCulture);
                widest = Math.Max(widest, measure("default " + amount));
                widest = Math.Max(widest, measure("was " + amount));
            }

            _widestCurrencyTagWidth = widest;
            return widest;
        }

        private static IEnumerable<long> AllCuratedDefaults()
        {
            foreach (var kvp in CurrencyDecisionDefaults.DefaultCopperPerUnit)
            {
                yield return kvp.Value;
            }

            foreach (var kvp in BarterItemDecisionDefaults.Defaults)
            {
                yield return kvp.Value.CopperPerUnit;
            }
        }

        private Label CreateCurrencyHeaderLabel(string text)
        {
            return new Label()
            {
                Font = HeaderBands.Font,
                TextColor = HeaderBands.LabelColor,
                Text = text,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(CellHeaderX, CurrencyHeaderTextY),
                Parent = _currencyHeaderPanel,
            };
        }

        private void LayoutCurrencyGridHeader()
        {
            if (_currencyHeaderPanel == null)
            {
                return;
            }

            int columnCount = SettingsCurrencyGridLayout.ComputeColumnCount(_panelWidth);
            int columnWidth = SettingsCurrencyGridLayout.ComputeColumnWidth(_panelWidth);

            // Grown, never shrunk: the count is uncapped now, so a wide
            // window needs pairs a narrow one did not, and a pair built once
            // is cheaper to hide than to rebuild on the next resize tick.
            while (_currencyHeaderNames.Count < columnCount)
            {
                _currencyHeaderNames.Add(CreateCurrencyHeaderLabel(NameHeaderText));
                _currencyHeaderUnits.Add(CreateCurrencyHeaderLabel(UnitHeaderText));
            }

            int unitX = SettingsCurrencyGridLayout.UnitHeaderX(
                columnWidth, UnitHeaderWidth(), WidestCurrencyTagWidth());
            for (int i = 0; i < _currencyHeaderNames.Count; i++)
            {
                bool visible = i < columnCount;
                _currencyHeaderNames[i].Visible = visible;
                _currencyHeaderUnits[i].Visible = visible;
                if (!visible)
                {
                    continue;
                }

                _currencyHeaderNames[i].Location =
                    new Point((i * columnWidth) + CellHeaderX, CurrencyHeaderTextY);
                _currencyHeaderUnits[i].Location =
                    new Point((i * columnWidth) + unitX, CurrencyHeaderTextY);
            }
        }

        private void AddCurrencyRow(int id, int columnWidth, bool isBarterItem = false)
        {
            string name = isBarterItem
                ? BarterItemDecisionDefaults.ResolveName(id)
                : Gw2Constants.ResolveCurrencyName(id);

            var cellPanel = new Panel()
            {
                Size = new Point(columnWidth, CurrencyRowHeight),
                Parent = _currencyGridPanel,
            };

            // Width and text are resolved by LayoutCurrencyCell, which the
            // resize path calls too - the name is the only flexing part of
            // the cell, so build and relayout must go through one function.
            var nameLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = name,
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Location = new Point(CellNameX, CellTextY),
                Parent = cellPanel,
            };

            bool hasDefault = isBarterItem
                ? BarterItemDecisionDefaults.TryGetDefault(id, out long defaultCopperPerUnit)
                : CurrencyDecisionDefaults.TryGetDefault(id, out defaultCopperPerUnit);

            // One noun for both kinds of row. "Currency" would be wrong on
            // an item row and "item" wrong on a currency one, and the boxes
            // behave identically.
            string kindNoun = isBarterItem ? "item" : "currency";

            var input = new TextBox()
            {
                Size = new Point(CellInputWidth, InputHeight),
                Location = new Point(CellNameX, CellInputY),
                // Digits, not the unit: "copper" named what the box HELD
                // and so read as a unit label on a read-only field (field
                // test, bug 2). The greyed default is the number currently
                // in effect for this currency, which both prompts the shape
                // of the input and states what typing over it replaces. A
                // currency with no default has nothing to suggest and shows
                // an empty box; "Copper per unit" over the column carries
                // the unit for both. The 70px box leaves ~50px of text
                // region (Blish's TextBox insets the placeholder by 10px a
                // side and does not truncate it), which every value in
                // CurrencyDecisionDefaults - 3600 is the largest - fits.
                // Set once here for the pre-load state;
                // RefreshCurrencyRowDefaultState owns it from then on, and
                // clears it while the currency is ignored.
                PlaceholderText = hasDefault
                    ? defaultCopperPerUnit.ToString(CultureInfo.InvariantCulture)
                    : "",
                Parent = cellPanel,
            }.ReleaseOnDispose().ReleaseOnEnter();
            // Feature 1 spec: the estimate is labeled as such, with
            // attribution/editable/clearable spelled out on hover.
            // The "leave it blank" clause is on BOTH branches deliberately.
            // It used to be a panel info line; it is the only statement that
            // unset is a supported state rather than an unfinished one, and
            // the four currencies it matters most for (the ones with no
            // default, so no default tag and no default-tag hover) would
            // otherwise carry it nowhere at all.
            // A currency default is adapted from gw2efficiency; a barter
            // item's is derived here from a vendor exchange, so the two
            // rows cite different sources rather than one wrong one.
            string defaultSource = isBarterItem
                ? "derived from the cheapest vendor exchange we can price"
                : "adapted from gw2efficiency";
            TooltipFacility.ApplyPlain(input, hasDefault
                ? $"Default estimate {defaultCopperPerUnit} copper per unit, {defaultSource} (decision-only). Type your own amount here and press Save to override it, or tick Ignore to suppress it. Left blank and not ignored, it keeps the default."
                : $"Coin value of one unit, in copper. Type an amount here and press Save, or leave it blank to keep this {kindNoun} out of price comparisons.");

            var defaultLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = "",
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                TextColor = InfoTextColor,
                Location = new Point(CellNameX, CellTextY),
                Parent = cellPanel,
            };
            TooltipFacility.ApplyPlain(defaultLabel, hasDefault
                ? $"This {kindNoun} is valued automatically at its default estimate unless you type your own amount or tick Ignore."
                : null);

            var errorLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = "",
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                TextColor = ErrorTextColor,
                BasicTooltipText = "Enter a positive whole number of copper, or leave the box blank.",
                Location = new Point(CellNameX, CellTextY),
                Parent = cellPanel,
            };

            Checkbox clearCheckbox = null;
            if (hasDefault)
            {
                // "Clear" named an ACTION this control does not perform:
                // it is a persistent three-state flag that suppresses the
                // curated default, not a button that empties the box beside
                // it. "Ignore" names the state.
                //
                // Not the longer "Ignore default": the cell reserves
                // SettingsCurrencyGridLayout.CellClearWidth (74px) for this
                // control, and widening that widens
                // SettingsCurrencyMinColumnWidth with it.
                // That was load-bearing at the old 930px window minimum,
                // whose panel could not hold two columns at all; the 1378px
                // minimum clears the two-column threshold by ~344px, so the
                // budget now has slack (see CellInputToClearGap). The name
                // stays short anyway - the tag slot immediately right of it
                // ("default 3600" / "ignored") and the tooltip carry the
                // rest of the meaning.
                clearCheckbox = new Checkbox()
                {
                    Text = "Ignore",
                    Location = new Point(CellNameX, CellTextY),
                    Parent = cellPanel,
                };
                clearCheckbox.CheckedChanged += (_, __) => RefreshDirtyState();
                TooltipFacility.ApplyPlain(
                    clearCheckbox,
                    $"Ignore this {kindNoun}'s default estimate - it will not be valued unless you enter your own amount.");
            }

            // Appended in the same step as the row it names - the filter
            // maps grid.Cells[i] back onto _rows[i] by index.
            _currencyNames.Add(name);
            var row = new CurrencyRow
            {
                Id = id,
                IsBarterItem = isBarterItem,
                Name = name,
                HasDefault = hasDefault,
                DefaultCopperPerUnit = defaultCopperPerUnit,
                Cell = cellPanel,
                // Shown/hidden per filter pass: the cells on the last
                // populated grid row carry no rule (see ApplyCurrencyFilter).
                Divider = LabelHelpers.CreateRowDivider(
                    cellPanel, columnWidth, CurrencyRowHeight, CellDividerClearance),
                Input = input,
                NameLabel = nameLabel,
                DefaultLabel = defaultLabel,
                ErrorLabel = errorLabel,
                ClearCheckbox = clearCheckbox,
            };

            input.TextChanged += (_, __) => RefreshDirtyState();

            EnsureCurrencyRowIcon(row);
            LayoutCurrencyCell(row, columnWidth, measureText: true);
            _rows.Add(row);
        }

        /// <summary>
        /// Hands the tab the session's currency metadata, on the main
        /// thread. Called once per session by Module after the one
        /// /v2/currencies fetch resolves, and again for nothing after that:
        /// the service caches the whole list for the session, so a second
        /// call would carry the same icons.
        /// <para>
        /// A null or empty dictionary is ignored rather than stored - a
        /// failed fetch must not overwrite icons a previous call already
        /// resolved, and it is the "not known yet" state the rows already
        /// render.
        /// </para>
        /// </summary>
        public void SetCurrencyMetadata(IReadOnlyDictionary<int, CurrencyMetadata> metadata)
        {
            if (metadata == null || metadata.Count == 0)
            {
                return;
            }

            _currencyMetadata = metadata;

            // Every tab re-open runs Build again and rebuilds these rows,
            // which pick the metadata up themselves; this pass is for the
            // rows already on screen when the fetch lands.
            foreach (var row in _rows)
            {
                EnsureCurrencyRowIcon(row);
            }
        }

        /// <summary>
        /// The barter-item twin of <see cref="SetCurrencyMetadata"/>, on the
        /// same thread and the same terms, fed by the module's shared
        /// <see cref="ItemMetadataService"/> rather than a second fetch of
        /// its own.
        /// </summary>
        public void SetBarterItemMetadata(IReadOnlyDictionary<int, ItemMetadata> metadata)
        {
            if (metadata == null || metadata.Count == 0)
            {
                return;
            }

            _barterItemMetadata = metadata;

            foreach (var row in _rows)
            {
                EnsureCurrencyRowIcon(row);
            }
        }

        /// <summary>
        /// Builds one cell's currency icon, once the icon is knowable.
        /// <para>
        /// Nothing is drawn while the row's metadata is unresolved: that is
        /// "not fetched yet", not "this has no icon", and IconControls'
        /// empty-slot placeholder states the second. Once the currency list has
        /// resolved, every currency row gets an icon control. A barter item is
        /// held to the id it resolved rather than to the fetch having happened.
        /// </para>
        /// <para>
        /// The band is reserved by the cell's geometry
        /// (SettingsCurrencyGridLayout.CellNameX is past the icon whether or
        /// not one is drawn), so an icon arriving mid-session moves no other
        /// control. Built at most once per row per Build cycle.
        /// </para>
        /// Why the two readiness tests differ: docs/ARCHITECTURE.md, "Views:
        /// relocated design narrative".
        /// </summary>
        private void EnsureCurrencyRowIcon(CurrencyRow row)
        {
            if (row.Icon != null)
            {
                return;
            }

            if (row.IsBarterItem)
            {
                ItemMetadata item;
                if (_barterItemMetadata == null ||
                    !_barterItemMetadata.TryGetValue(row.Id, out item) ||
                    item == null)
                {
                    return;
                }

                // These rows ARE items, unlike their currency
                // neighbours, so they go to the item entry point.
                row.Icon = IconControls.DrawItemIcon(
                    row.Cell,
                    row.Id,
                    SettingsCurrencyGridLayout.CellIconX,
                    SettingsCurrencyGridLayout.CellIconY,
                    ItemIconTier.CurrencyListRow,
                    ItemFactsFor);
                return;
            }

            if (_currencyMetadata == null)
            {
                return;
            }

            row.Icon = IconControls.CreateCurrencyIcon(
                row.Cell,
                row.Id,
                SettingsCurrencyGridLayout.CellIconX,
                SettingsCurrencyGridLayout.CellIconY,
                ItemIconTier.CurrencyListRow,
                CurrencyFactsFor);
        }

        /// <summary>Everything one barter item's icon draws and its
        /// tooltip shows. The tab's own /v2/items entry leads, and the
        /// session stat cache fills the body.</summary>
        private ItemTooltipFacts ItemFactsFor(int itemId)
        {
            ItemMetadata item = null;
            if (_barterItemMetadata != null)
            {
                _barterItemMetadata.TryGetValue(itemId, out item);
            }

            return ItemTooltipFacts.ForItemId(
                itemId,
                item,
                _getItemStatBlock == null || itemId <= 0 ? null : _getItemStatBlock(itemId));
        }

        /// <summary>No balance: this tab reads no wallet snapshot, and
        /// null is "not known", which drops the line rather than claiming
        /// the player holds none.</summary>
        private CurrencyTooltipFacts CurrencyFactsFor(int currencyId)
        {
            return CurrencyTooltipFacts.ForCurrencyId(
                currencyId, _currencyMetadata, (int?)null);
        }

        /// <summary>
        /// Places one cell's pinned control block against the cell's own
        /// right edge and re-fits the name to whatever is left. Called from
        /// the build and from ApplyPanelWidth, so the two cannot drift.
        /// </summary>
        private static void LayoutCurrencyCell(CurrencyRow row, int columnWidth, bool measureText)
        {
            row.Input.Location = new Point(
                SettingsCurrencyGridLayout.CellInputX(columnWidth), CellInputY);

            int tagX = SettingsCurrencyGridLayout.CellTagX(columnWidth);
            row.DefaultLabel.Location = new Point(tagX, CellTextY);
            row.ErrorLabel.Location = new Point(tagX, CellTextY);

            if (row.ClearCheckbox != null)
            {
                row.ClearCheckbox.Location = new Point(
                    SettingsCurrencyGridLayout.CellClearX(columnWidth), CellTextY);
            }

            int budget = SettingsCurrencyGridLayout.CellNameMaxWidth(columnWidth);
            row.NameLabel.Width = budget;
            if (!measureText)
            {
                return;
            }

            string shortName = LabelHelpers.EllipsizeToWidth(UiFonts.Body, row.Name, budget);
            if (!string.Equals(row.NameLabel.Text, shortName, StringComparison.Ordinal))
            {
                row.NameLabel.Text = shortName;
            }

            // Only when the name did not fit - an always-on tooltip
            // repeating the visible text is noise.
            string full = string.Equals(shortName, row.Name, StringComparison.Ordinal) ? null : row.Name;
            TooltipFacility.ApplyPlain(row.NameLabel, full);
            TooltipFacility.ApplyPlain(row.Cell, full);
        }

        /// <summary>
        /// Packs the cells matching the filter box two-up (one-up on a
        /// narrow panel) and hides the rest. The grid panel keeps its
        /// unfiltered height throughout - see SetCurrencyGridHeight.
        /// </summary>
        private void ApplyCurrencyFilter()
        {
            if (_currencyGridPanel == null)
            {
                return;
            }

            for (int i = 0; i < _rows.Count && i < _currencyForceVisible.Length; i++)
            {
                _currencyForceVisible[i] = _rows[i].ErrorLabel.Text.Length > 0;
            }

            var grid = SettingsCurrencyGridLayout.Compute(
                _currencyNames, _currencyFilterInput?.Text, _panelWidth, CurrencyRowHeight,
                _currencyForceVisible);

            for (int i = 0; i < _rows.Count; i++)
            {
                var row = _rows[i];
                var placement = grid.Cells[i];

                row.Cell.Visible = placement.Visible;
                if (placement.Visible)
                {
                    row.Cell.Location = new Point(placement.X, placement.Y);
                }

                // Hidden cells report Row = -1, so the guard below also
                // keeps their rule off.
                row.Divider.Visible = placement.Row >= 0 && placement.Row < grid.RowCount - 1;
            }

            if (_currencyCountLabel != null)
            {
                _currencyCountLabel.Text = grid.VisibleCount == _rows.Count
                    ? StatusText.Count(_rows.Count, "row", "rows")
                    : $"{grid.VisibleCount} of {_rows.Count} shown";
                LayoutCurrencyFilterRow();
            }
        }

        /// <summary>
        /// Refreshes one row's Clear checkbox, its input placeholder and its
        /// default/cleared tag
        /// from the given already-loaded or just-saved valuation - shared by
        /// LoadCurrentValuations and SaveValuations so the two can never
        /// disagree about how to render the same state. The tag is a label,
        /// not the input's placeholder: the placeholder is clipped to ~50px
        /// of text region by the 70px box (Blish's TextInputBase draws it
        /// untruncated inside the control's own scissor), which cuts the
        /// number off the default state entirely, and it would vanish behind
        /// any typed override besides.
        /// </summary>
        private static void RefreshCurrencyRowDefaultState(CurrencyRow row, CurrencyValuation valuation)
        {
            if (!row.HasDefault)
            {
                return;
            }

            bool isCleared = row.IsBarterItem
                ? valuation.IsItemCleared(row.Id)
                : valuation.IsCleared(row.Id);
            bool hasOverride = row.IsBarterItem
                ? valuation.TryGetItemCopperValue(row.Id, out _)
                : valuation.TryGetCopperValue(row.Id, out _);

            row.ClearCheckbox.Checked = isCleared;

            // The placeholder states the number in effect (see
            // AddCurrencyRow), so it has to follow the same state the tag
            // does: an ignored currency has NO number in effect, and a
            // greyed default left in the box there would contradict the
            // "ignored" tag beside it.
            row.Input.PlaceholderText = isCleared
                ? ""
                : row.DefaultCopperPerUnit.ToString(CultureInfo.InvariantCulture);
            row.DefaultLabel.Text = isCleared
                ? "ignored"
                : hasOverride
                    ? $"was {row.DefaultCopperPerUnit}"
                    : $"default {row.DefaultCopperPerUnit}";
            row.DefaultLabel.TextColor = isCleared ? WarningTextColor : InfoTextColor;
        }

        /// <summary>
        /// The cell's two tags share one slot (see CurrencyRow.DefaultLabel):
        /// the red warning takes it while an amount will not parse, and the
        /// default/cleared state comes back when it does.
        /// </summary>
        private static void SetCurrencyRowError(CurrencyRow row, string text)
        {
            row.ErrorLabel.Text = text ?? "";
            row.DefaultLabel.Visible = row.ErrorLabel.Text.Length == 0;
        }

        /// <summary>
        /// The tab's one Save button and its one status label, in a bar that
        /// is a sibling of the scrolling content (not a row inside it), so
        /// Save stays reachable from any scroll position. Anchored at the
        /// TOP rather than as a bottom footer: LogTabContent already builds
        /// a fixed toolbar this way above its own CanScroll FlowPanel, and a
        /// top bar needs only ContentRegion.Width to place correctly - a
        /// bottom footer would additionally depend on ContentRegion.Height
        /// being final at Build time, whose failure mode is a Save bar
        /// floating over the rows.
        /// </summary>
        private void BuildSaveBar(Container container)
        {
            _saveBarPanel = new Panel()
            {
                Size = new Point(container.ContentRegion.Width, SaveBarHeight),
                Parent = container,
            };

            // The bar's own state, on the surface that owns it: how many
            // fields differ from the last save used to be visible ONLY
            // inside Module's tab-switch prompt, so the user could not see
            // their own dirty state while looking at the form.
            _dirtyChipLabel = new Label()
            {
                Font = UiFonts.Body,
                Text = "",
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                TextColor = WarningTextColor,
                Location = new Point(SettingsSaveBarLayout.SettingsSaveBarInset, 10),
                Visible = false,
                Parent = _saveBarPanel,
            };
            TooltipFacility.ApplyPlain(
                _dirtyChipLabel, "Fields edited since the last load or save. Press Save to persist them.");

            // Status tier, like every other tab's: 18 BOLD, which is not a
            // style choice at this size (TypeRampMetrics on 18-regular's
            // collapsed word gaps). y=9 re-centres the taller 23px line box.
            // Explicit width, not AutoSizeWidth: a long status used to run
            // straight under the buttons.
            _statusLabel = new Label()
            {
                Font = UiFonts.Status,
                Text = "",
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Location = new Point(SettingsSaveBarLayout.SettingsSaveBarInset, 9),
                Parent = _saveBarPanel,
            };

            // Destructive - it throws away the user's typed edits - so it
            // goes through the confirm matrix, and it is hidden entirely
            // while there is nothing to discard.
            _discardButton = new FeedbackButton()
            {
                Text = "Discard",
                Size = new Point(DiscardButtonWidth, UiMetrics.ButtonHeight),
                Location = new Point(SettingsSaveBarLayout.SettingsSaveBarInset, SaveBarButtonY),
                BasicTooltipText = "Throw away every unsaved edit on this tab and restore the last saved values.",
                Visible = false,
                Parent = _saveBarPanel,
            };
            _discardButton.Click += (_, __) => ConfirmDiscardChanges();

            // Always enabled, even at zero changes: a disabled primary
            // invites "why is this disabled?", the reasoning already
            // recorded for the tree chips.
            _saveButton = new FeedbackButton()
            {
                Text = "Save",
                Size = new Point(SaveButtonWidth, UiMetrics.ButtonHeight),
                Location = new Point(SettingsSaveBarLayout.SettingsSaveBarInset, SaveBarButtonY),
                BasicTooltipText = "Save every section on this tab.",
                Parent = _saveBarPanel,
            };
            _saveButton.Click += (_, __) => SaveAll();

            ApplySaveBarWidth(ContentWidth(container));
        }

        private const int SaveButtonWidth = 80;
        private const int DiscardButtonWidth = 90;

        // Centred in the 40px bar, derived rather than written down.
        private const int SaveBarButtonY = (SaveBarHeight - UiMetrics.ButtonHeight) / 2;

        /// <summary>
        /// Places the bar's four slots. The bar does not scroll, but it is
        /// measured against the SAME content width the scrolling panel below
        /// it uses, so Save lands on the vertical line the content's right
        /// edge holds.
        /// </summary>
        private void ApplySaveBarWidth(int barWidth)
        {
            // Resized fires on height-only drags too; the bar's slots are a
            // function of width alone.
            if (barWidth <= 0 || barWidth == _saveBarWidth)
            {
                return;
            }

            _saveBarWidth = barWidth;
            LayoutSaveBar();
        }

        private void LayoutSaveBar()
        {
            if (_saveButton == null)
            {
                return;
            }

            int chipWidth = _dirtyChipLabel != null && _dirtyChipLabel.Visible
                ? (int)Math.Ceiling(UiFonts.Body.MeasureString(_dirtyChipLabel.Text ?? "").Width)
                : 0;
            int discardWidth = _discardButton != null && _discardButton.Visible ? DiscardButtonWidth : 0;

            var slots = SettingsSaveBarLayout.Compute(
                _saveBarWidth, chipWidth, discardWidth, SaveButtonWidth);

            if (_dirtyChipLabel != null)
            {
                _dirtyChipLabel.Location = new Point(slots.ChipX, 10);
            }

            if (_statusLabel != null)
            {
                _statusLabel.Location = new Point(slots.StatusX, 9);
                _statusLabel.Width = slots.StatusMaxWidth;
                ApplyStatusText();
            }

            if (_discardButton != null)
            {
                _discardButton.Location = new Point(slots.DiscardX, SaveBarButtonY);
            }

            _saveButton.Location = new Point(slots.SaveX, SaveBarButtonY);
        }

        // The status line's full text, so the ellipsis is re-taken from the
        // original on every resize rather than compounded onto an already-
        // shortened string.
        private string _statusFullText = "";

        private void SetStatusText(string text)
        {
            _statusFullText = text ?? "";
            ApplyStatusText();
        }

        private void ApplyStatusText()
        {
            if (_statusLabel == null)
            {
                return;
            }

            int budget = _statusLabel.Width;
            string shown = LabelHelpers.EllipsizeToWidth(UiFonts.Status, _statusFullText, budget);
            if (!string.Equals(_statusLabel.Text, shown, StringComparison.Ordinal))
            {
                _statusLabel.Text = shown;
            }

            TooltipFacility.ApplyPlain(
                _statusLabel,
                string.Equals(shown, _statusFullText, StringComparison.Ordinal) ? null : _statusFullText);
        }

        /// <summary>
        /// Recomputes the dirty chip and the Discard button from the live
        /// form. Both are hidden entirely at zero - see
        /// SettingsSaveBarLayout.
        /// </summary>
        private void RefreshDirtyState()
        {
            if (_suspendDirtyRefresh || _dirtyChipLabel == null)
            {
                return;
            }

            int unsaved = UnsavedChangeCount();
            _dirtyChipLabel.Text = StatusText.Count(unsaved, "unsaved change");
            _dirtyChipLabel.Visible = unsaved > 0;
            if (_discardButton != null)
            {
                _discardButton.Visible = unsaved > 0;
            }

            LayoutSaveBar();
        }

        private void ConfirmDiscardChanges()
        {
            if (_modalDialog == null)
            {
                DiscardChanges();
                RefreshDirtyState();
                return;
            }

            int unsaved = UnsavedChangeCount();
            string changeWord = unsaved == 1 ? "change" : "changes";
            _modalDialog.Show(
                $"Discard {unsaved} unsaved {changeWord} on this tab and restore the last saved values?",
                () =>
                {
                    DiscardChanges();
                    RefreshDirtyState();
                },
                null,
                confirmText: "Discard");
        }

        /// <summary>
        /// What a SaveAll actually got to disk. The in-tab Save button
        /// ignores it and reads the status label instead; a caller saving
        /// from OUTSIDE the tab has no status label on screen (the save
        /// bar is unparented the moment the view is torn down), so it has
        /// to be told in the return value or the failure is silent.
        /// </summary>
        public readonly struct SaveOutcome
        {
            public SaveOutcome(int invalidCount, bool writeFailed)
            {
                InvalidCount = invalidCount;
                WriteFailed = writeFailed;
            }

            /// <summary>Entries rejected by their section's parser and left at their persisted value.</summary>
            public int InvalidCount { get; }

            /// <summary>The currency valuation write itself failed - see the module log.</summary>
            public bool WriteFailed { get; }

            public bool AllSaved => !WriteFailed && InvalidCount == 0;
        }

        /// <summary>
        /// Persists every section - currency valuations, Homestead tiers,
        /// logging policy, snapshot refresh interval - in place of the four
        /// per-section Save buttons. Each text-entry section keeps its own
        /// per-row error labels and its own "invalid rows are left as
        /// previously persisted" contract; only the confirmation is shared.
        /// </summary>
        public SaveOutcome SaveAll()
        {
            bool valuationsSaved = SaveValuations(out int invalidCount);
            SaveHomesteadTiers();
            invalidCount += SaveLoggingSettings();
            invalidCount += SaveSnapshotSettings();

            if (valuationsSaved)
            {
                // Rebased on the CONTROLS, not on what reached disk. An
                // entry that would not parse keeps its previously
                // persisted value but its text stays in the box, so a
                // baseline taken from persisted state would leave the tab
                // permanently dirty and re-prompt on every later tab
                // switch to save a value that can never be saved. The
                // status line below already tells the user those entries
                // were not saved. A failed valuation write is the one case
                // that does NOT rebase - there the edits really are still
                // unsaved, and the next prompt should say so.
                _baseline = CaptureFormState();
            }

            // The chip reads the freshly-rebased baseline, so a clean save
            // clears it and a failed one leaves it standing.
            RefreshDirtyState();

            var outcome = new SaveOutcome(invalidCount, !valuationsSaved);

            if (_statusLabel == null)
            {
                return outcome;
            }

            if (!valuationsSaved)
            {
                // Defensive branch (see SaveValuations' catch): the other
                // three sections did persist, but a failed write is the
                // headline and the per-row errors stay on screen.
                SetStatusText("Save failed - see log");
                _statusLabel.TextColor = ErrorTextColor;
                return outcome;
            }

            if (invalidCount == 0)
            {
                SetStatusText(StatusText.Stamp("Saved", DateTime.Now));
                _statusLabel.TextColor = SuccessTextColor;
            }
            else
            {
                string entryWord = invalidCount == 1 ? "entry" : "entries";
                SetStatusText($"Saved - {invalidCount} invalid {entryWord} not saved");
                _statusLabel.TextColor = WarningTextColor;
            }

            return outcome;
        }

        private void LoadCurrentValuations()
        {
            var valuation = _settings.GetCurrencyValuation();

            foreach (var row in _rows)
            {
                bool hasValue = row.IsBarterItem
                    ? valuation.TryGetItemCopperValue(row.Id, out long copperPerUnit)
                    : valuation.TryGetCopperValue(row.Id, out copperPerUnit);
                row.Input.Text = hasValue
                    ? copperPerUnit.ToString(CultureInfo.InvariantCulture)
                    : "";
                SetCurrencyRowError(row, "");
                RefreshCurrencyRowDefaultState(row, valuation);
            }
        }

        /// <summary>
        /// Returns false when the valuation could not be persisted at all;
        /// invalidCount counts rows whose text did not parse (those keep
        /// their previously-persisted value either way).
        /// </summary>
        private bool SaveValuations(out int invalidCount)
        {
            // Seeded from the currently-persisted valuation (not empty) so
            // an invalid row is left untouched below rather than silently
            // dropped: the status label tells the user invalid entries are
            // "not saved", which must mean unchanged, not cleared. Only a
            // row the user deliberately blanks is removed. currency-ux-
            // package (Feature 1): cleared is seeded the same way, for the
            // same reason - a row nobody touched this Save must keep
            // whatever cleared/default state it already had persisted.
            var persisted = _settings.GetCurrencyValuation();
            // .NET Framework 4.8's Dictionary<TKey,TValue> has no
            // constructor overload accepting IReadOnlyDictionary<TKey,
            // TValue> (only IDictionary<TKey,TValue>) - CopperPerUnit is
            // exposed as the former, so this is a manual copy rather than
            // a one-line constructor call.
            var entries = new Dictionary<int, long>();
            foreach (var kvp in persisted.CopperPerUnit)
            {
                entries[kvp.Key] = kvp.Value;
            }

            var cleared = new HashSet<int>(persisted.ClearedCurrencyIds);

            // The barter-item side of the same seeded-from-persisted
            // treatment, kept in its own pair of collections because item
            // and currency ids share no id space.
            var itemEntries = new Dictionary<int, long>();
            foreach (var kvp in persisted.ItemCopperPerUnit)
            {
                itemEntries[kvp.Key] = kvp.Value;
            }

            var itemCleared = new HashSet<int>(persisted.ClearedItemIds);

            invalidCount = 0;

            foreach (var row in _rows)
            {
                SetCurrencyRowError(row, "");

                string text = row.Input.Text;
                if (string.IsNullOrWhiteSpace(text))
                {
                    // Blank box = no explicit override. Feature 1: this no
                    // longer means "excluded from comparison" outright - a
                    // currency with a curated default is still valued via
                    // that default unless the Clear checkbox is checked,
                    // which is the ONLY thing that persists a genuine
                    // suppression (see CurrencyValuation's own doc comment
                    // on the three-state precedence).
                    var rowEntries = row.IsBarterItem ? itemEntries : entries;
                    var rowCleared = row.IsBarterItem ? itemCleared : cleared;
                    rowEntries.Remove(row.Id);
                    if (row.HasDefault && row.ClearCheckbox != null && row.ClearCheckbox.Checked)
                    {
                        rowCleared.Add(row.Id);
                    }
                    else
                    {
                        rowCleared.Remove(row.Id);
                    }

                    continue;
                }

                if (SettingsInputParser.TryParseCopperValue(text, out long copperPerUnit))
                {
                    var rowEntries = row.IsBarterItem ? itemEntries : entries;
                    var rowCleared = row.IsBarterItem ? itemCleared : cleared;
                    rowEntries[row.Id] = copperPerUnit;
                    // An explicit value always wins over a stale cleared
                    // marker - CurrencyValuation's constructor rejects an
                    // id that is both valued and cleared at once.
                    rowCleared.Remove(row.Id);
                }
                else
                {
                    // Left out of this row's changes entirely - whatever
                    // was previously persisted for this currency (if
                    // anything) is preserved, matching "not saved" below.
                    // Short enough to stay inside a half-width cell; the
                    // label's tooltip carries the full rule.
                    SetCurrencyRowError(row, "Invalid");
                    invalidCount++;
                }
            }

            // A row the filter is hiding still counts towards the save bar's
            // "N invalid entries not saved", so re-run the filter with those
            // rows forced visible - a warning whose tag is off screen points
            // the user at nothing.
            ApplyCurrencyFilter();

            CurrencyValuation saved;
            try
            {
                saved = new CurrencyValuation(entries, cleared, itemEntries, itemCleared);
                _settings.SetCurrencyValuation(saved);
            }
            catch (Exception ex)
            {
                // Defensive: entries/cleared are seeded from the already-
                // valid persisted valuation and only ever updated with
                // SettingsInputParser-validated positive values (removing
                // the same id from `cleared` in the same step) on non-coin
                // currency ids, so CurrencyValuation's constructor should
                // never actually reject this. Still guarded so a future
                // change to either side degrades to a visible status
                // message instead of an unhandled exception on the UI
                // thread.
                Logger.Warn(ex, "Failed to save currency valuations");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "settings", $"Failed to save currency valuations: {ex.GetType().Name} - {ex.Message}");
                return false;
            }

            foreach (var row in _rows)
            {
                RefreshCurrencyRowDefaultState(row, saved);
            }

            return true;
        }
    }
}
