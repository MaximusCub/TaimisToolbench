using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // The Recipe Tree section renderer AND the interactive override loop it
    // drives (Best Path/Craft All/Buy All presets, the per-node
    // craft/tp/vendor decision pills, the Ignore pill), plus every field
    // that loop owns: TreeNodeState, _treeNodeStates, _treeRoots/_treeFlow
    // (this render pass's tree bookkeeping), _nodeOverrides/_ignoredItemIds/
    // _nodeExpansion (session-persistent decision/ignore/expansion state),
    // and _lastResult (the solve context the override loop re-resolves
    // against).
    //
    // Alone among the section renderers this one owns application state,
    // not just presentation: those fields survive every local re-solve (a
    // pill click never rebuilds them) and reset only on a genuinely new
    // Generate. What it needs from the view beyond relayout registration -
    // PreserveScrollAcross (KNOWN-ISSUES #39), SetStatus, the
    // post-re-solve render entry point, the current panel width, the
    // current plan, and the shared section chrome - arrives as one named
    // ITreePlanHost rather than a reference to CraftingPlanView, which
    // would reopen the private surface this class exists to stay out of.
    //
    // See docs/ARCHITECTURE.md section 5 for the state-ownership
    // rationale and the scroll/resize/wheel controller cut decision.
    internal sealed class TreeSectionController
    {
        private readonly ISectionRelayoutSink _sink;
        private readonly ITreePlanHost _host;
        private readonly Func<PlanSolveContext, IReadOnlyDictionary<int, AcquisitionSource>, ISet<int>, CraftingPlanResult> _resolveOverridesSync;
        private readonly PlanViewModelBuilder _vmBuilder;

        // This session's item stat block for an item id, or null. Null is
        // routine, not exceptional: a synthesized cost-component leaf is
        // not a real item at all, and a plan restored from disk has no
        // stats until something re-fetches (KNOWN-ISSUES #40). Either way
        // the row falls back to the tooltip it had before this feature
        // existed.
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;

        // Registers one row control under a stable scroll-anchor key, so a
        // re-solve can put the row the user was looking at back under
        // their cursor instead of merely restoring the scroll offset (see
        // Services/ScrollAnchorMath). Optional - a null one simply leaves
        // the view anchoring at section granularity.
        private readonly Action<int, Control> _registerRowScrollAnchor;

        private static readonly Logger Logger = Logger.GetLogger<TreeSectionController>();

        internal TreeSectionController(
            ISectionRelayoutSink sink,
            ITreePlanHost host,
            PlanViewModelBuilder vmBuilder,
            Func<PlanSolveContext, IReadOnlyDictionary<int, AcquisitionSource>, ISet<int>, CraftingPlanResult> resolveOverridesSync,
            Func<int, ItemStatBlock> getItemStatBlock = null,
            Action<int, Control> registerRowScrollAnchor = null)
        {
            // resolveOverridesSync is deliberately NOT null-guarded - the
            // sole production call site (CraftingPlanView's own
            // constructor) accepts it as an optional parameter defaulting
            // to null, and every reader below already treats a null value
            // as "override re-solve unavailable" (ApplyOverridesAndResolve
            // bails out; RenderDecisionPills renders its pills
            // non-interactive) rather than a construction-time error.
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _vmBuilder = vmBuilder ?? throw new ArgumentNullException(nameof(vmBuilder));
            _resolveOverridesSync = resolveOverridesSync;

            // Optional and NOT null-guarded, matching resolveOverridesSync
            // above: null simply means "no stat tooltips this session",
            // which every reader below already treats as the fallback.
            _getItemStatBlock = getItemStatBlock;
            _registerRowScrollAnchor = registerRowScrollAnchor;
        }

        // Per-node user decision overrides (keyed by solver NodeId) and
        // explicit tree expansion state; both survive local re-solves and
        // reset on a fresh Generate.
        private readonly Dictionary<int, AcquisitionSource> _nodeOverrides =
            new Dictionary<int, AcquisitionSource>();

        // Item ids manually marked "Ignore" this session (gw2e
        // parity) - keyed by ItemId (not NodeId), matching gw2e's own
        // "Ignore marks every occurrence of that item id, tree-wide"
        // semantics (see PlanSolver.Solve's ignoredItemIds parameter).
        // Independent of _nodeOverrides: neither "Best Path" nor "Craft
        // All"/"Buy All" clears this (gw2e's bulk actions are documented as
        // unrelated to ownership); it is only ever
        // toggled per item id (the pill click) or on a fresh Generate.
        private readonly HashSet<int> _ignoredItemIds = new HashSet<int>();
        private readonly Dictionary<int, bool> _nodeExpansion =
            new Dictionary<int, bool>();

        // Solve context the override loop re-resolves against - the result
        // of the last full Generate or the last local override re-solve,
        // whichever happened most recently.
        private CraftingPlanResult _lastResult;

        private class TreeNodeState
        {
            public bool ChildrenBuilt;
            public bool IsExpanded;
            public FlowPanel ChildContainer;
            public Label ArrowLabel;
            public CraftingTreeNode Node;
            public int Depth;

            // PanelWidth removed - a captured build-time width
            // would go stale once resize no longer triggers a full rebuild
            // (see GetCurrentPanelWidth, which every remaining reader of
            // "current tree width" uses instead).

            // Whether lazily-built children (built on first expand) should
            // render dimmed - computed once from this node's own dimmed
            // state and decision, so it stays correct however many frames
            // later the user actually expands the node.
            public bool ChildDimmed;
        }

        // States for the current render pass; rebuilt with the tree itself.
        private readonly List<TreeNodeState> _treeNodeStates = new List<TreeNodeState>();

        /// <summary>
        /// Everything an in-place refresh of one already-built tree row
        /// needs to reach - see <see cref="TryRefreshInPlace"/>. Held per
        /// BUILT row, in build order, which is exactly the pre-order the
        /// refresh walk re-derives.
        /// <para>
        /// The row's own relayout and re-ellipsis closures read their
        /// mutable state (the pill list, the cost cell, the qty width)
        /// through this handle rather than capturing it, so a refresh that
        /// replaces a row's pills does not leave a closure repositioning
        /// controls that no longer exist.
        /// </para>
        /// </summary>
        private sealed class TreeRowHandle
        {
            internal CraftingTreeNode Node;
            internal int Depth;
            internal bool Dimmed;
            internal string CaptionText;
            internal string FullName;

            internal Panel RowPanel;
            internal Label QtyLabel;
            internal Label NameLabel;
            internal Panel IconFrame;
            internal Panel IconScrim;

            /// <summary>
            /// The SAME instance for the row's whole life. The row's click
            /// guard closes over it to ask whether a pill is under the
            /// cursor, so a refresh must refill it, never replace it.
            /// Holds Controls, not Panels: the IGNORE toggle is the module's
            /// FeedbackButton, not a pill panel.
            /// </summary>
            internal readonly List<Control> Pills = new List<Control>();

            /// <summary>
            /// Each pill's x measured from the pill column's own left edge,
            /// in step with <see cref="Pills"/>. The resize pass replays
            /// these rather than re-flowing the run. The ignore button is
            /// carried the same way: every offset in a tree row's grid is
            /// fixed relative to PillColX
            /// (PlanRelayoutMath.ComputeTreeColumnEdges), so replaying its
            /// own puts it back in its column at any window width.
            /// </summary>
            internal readonly List<int> PillOffsets = new List<int>();

            internal CoinCurrencyRenderer.ValueCellHandle CostCell;
            internal bool RowDrawsCurrency;

            internal int NameX;
            internal int QtyWidth;
            internal int PillColumnWidth;
            internal int CostColumnWidth;
            internal TreeCostColumnMath.CostColumnWidths ColumnWidths;

            /// <summary>Null for a row with no children.</summary>
            internal TreeNodeState State;
        }

        // Every built row of the current render pass, keyed by the solver
        // NodeId its row draws. A MAP rather than the build-order list it
        // started as: rows are appended in pre-order by the initial build,
        // but a later expand APPENDS its children at the end, so build
        // order stops being tree order the first time anyone expands
        // anything - and a refresh that walked the list positionally would
        // have quietly stopped matching from then on.
        private readonly Dictionary<int, TreeRowHandle> _treeRowsByNodeId =
            new Dictionary<int, TreeRowHandle>();

        // Set when two rows of one render claim the same NodeId, which
        // would make the map above ambiguous. Never observed - the solver
        // numbers its nodes uniquely - so this is a guard that declines to
        // refresh rather than a case with a handling strategy.
        private bool _treeRowIdsAmbiguous;

        // Node count of the pre-scan that titled this render's section
        // header ("Recipe Tree (N)"). A refresh that would change it has to
        // decline: the header is preserved across an in-place refresh, and
        // a title that no longer counts the tree under it is worse than a
        // rebuild.
        private int _scannedNodeCount;

        // Root nodes + top-level content FlowPanel for the current render's
        // Recipe Tree section (null when the plan has no tree). Held so
        // RefreshTreeContainerHeights - called from the tree row toggle
        // handler deep inside RenderTreeNode's recursion, as well as from
        // CreateTreeSection itself - can recompute treeFlow's own explicit
        // Height without threading both through every recursive call.
        // A single-item plan still populates this with exactly one root,
        // so every consumer
        // below is unchanged in that case (see MultiRootTreeFlowHeight's
        // own doc comment for the "N==1 is byte-identical" guarantee).
        private List<CraftingTreeNode> _treeRoots;
        private FlowPanel _treeFlow;

        // Per-render-pass widest value per coin denomination (plus the
        // widest whole currency run) across the ENTIRE tree, so every
        // row's cost cell lands in the same sub-columns and the coin icons
        // form straight vertical rules - see Services/TreeCostColumnMath,
        // including why this covers every node rather than only the rows
        // currently expanded. Scanned once in CreateTreeSection and read
        // by every RenderTreeNode call of that pass, including the ones a
        // later expand click builds lazily.
        private TreeCostColumnMath.CostColumnWidths _costColumnWidths =
            TreeCostColumnMath.CostColumnWidths.Empty;

        // The widest sub-columns any render of THIS plan has already
        // reserved. Unlike _costColumnWidths it survives the per-pass
        // reset, and only a fresh Generate clears it, so an in-session
        // ignore cannot narrow the cost column and slide every row's pills
        // sideways under the cursor - see Services/TreeCostColumnFloor.
        private TreeCostColumnMath.CostColumnWidths _planCostColumnFloor =
            TreeCostColumnMath.CostColumnWidths.Empty;

        // This render pass's decision-pill column width, and the widest
        // run any render of THIS plan has ever REQUIRED. The ratchet is on
        // the ink, not on the granted width, for the reason
        // TreePillColumnMath.Resolve gives: an ignore click shrinks the
        // required run, and that is what must not narrow under the cursor
        // (Services/TreeCostColumnFloor), while the granted width answers
        // to the window on screen.
        private int _pillColumnWidth = PlanRelayoutMath.TreePillColumnWidth;
        private int _planPillRequiredFloor;

        // How much of THIS render's pill column was claimed from the cost
        // column's reserve above what its rows draw
        // (TreePillColumnMath.RightClaim). EffectiveCostColumnWidth hands
        // the claim back out of the cost column's reserved width, which is
        // what extends the pills toward the cost ink without moving
        // PillColX; zero whenever the column is at its floor.
        private int _pillColumnCostClaim;

        // The column-header row's own relayout closure, held so the
        // "Source" header can be re-placed the moment a newly built row
        // widens the pill ink under it - see NoteSourceHeaderInk. Withdrawn
        // by the per-pass reset with the controls it moves.
        private Action<int> _treeHeaderRelayout;

        // Widest pill run any row of THIS plan has drawn, measured from the
        // pill column's own left edge, and the thing the "Source" header
        // centres over (Services/TreePillRunLayout.HeaderX). Rows are built
        // lazily, so this is only ever known for the rows built so far;
        // it is therefore a one-way high-water mark cleared by a fresh
        // Generate, exactly as the cost column's reserve is
        // (Services/TreeCostColumnFloor) - an expand may widen it, a
        // collapse or a narrower re-solve may not move it back and slide
        // the header out from under the reader's eye.
        private int _sourceHeaderInkWidth;

        /// <summary>
        /// Per-render-pass reset, called from
        /// CraftingPlanView.RenderPlan before it disposes/rebuilds the
        /// content panel's children - moved verbatim from RenderPlan's own
        /// top-of-method _treeNodeStates.Clear()/_treeRoots = null/
        /// _treeFlow = null (a plan without a tree section must not retain
        /// disposed controls from the previous render). Deliberately
        /// separate from ResetForNewPlan below: this runs on EVERY
        /// RenderPlan call (a fresh Generate AND an override-resolve
        /// re-render both go through RenderPlan), while ResetForNewPlan
        /// only runs once per genuinely new Generate.
        /// </summary>
        internal void ResetTreeRenderState()
        {
            _treeNodeStates.Clear();
            _treeRowsByNodeId.Clear();
            _treeRowIdsAmbiguous = false;
            _scannedNodeCount = 0;
            _treeRoots = null;
            _treeFlow = null;
            _treeHeaderRelayout = null;
            _costColumnWidths = TreeCostColumnMath.CostColumnWidths.Empty;
            _pillColumnWidth = PlanRelayoutMath.TreePillColumnWidth;
            _pillColumnCostClaim = 0;

            // Withdrawn with the render pass that published them: the tree
            // actions operate on the controls this reset is about to
            // discard, and the next plan may have no tree at all.
            _host.SetTreeToolbar(null);
        }

        /// <summary>
        /// Fresh-generation reset, called from
        /// CraftingPlanView.TriggerGenerate right before its own RenderPlan
        /// call - moved verbatim from TriggerGenerate's own
        /// _nodeOverrides.Clear()/_ignoredItemIds.Clear()/
        /// _nodeExpansion.Clear()/_lastResult = result. A brand new
        /// Generate discards every prior override/ignore/expansion
        /// decision and adopts the new solve result as the override loop's
        /// baseline.
        /// </summary>
        internal void ResetForNewPlan(CraftingPlanResult result)
        {
            _nodeOverrides.Clear();
            _ignoredItemIds.Clear();
            _nodeExpansion.Clear();

            // The cost column's no-narrowing floor is a property of the
            // plan on screen; a new one re-derives it from its own scan.
            // The pill column's ink high-water mark is the same kind of
            // fact and is cleared with it.
            _planCostColumnFloor = TreeCostColumnMath.CostColumnWidths.Empty;
            _planPillRequiredFloor = 0;
            _pillColumnCostClaim = 0;
            _sourceHeaderInkWidth = 0;
            _lastResult = result;
        }

        /// <summary>
        /// Re-seeds the override loop's decision/ignore state from a persisted
        /// plan, called from CraftingPlanView.ApplyRestoredPlan immediately
        /// after ResetForNewPlan(result). Without it, a restored session's
        /// <see cref="_nodeOverrides"/>/<see cref="_ignoredItemIds"/> start
        /// empty even though the restored <paramref name="result"/> already
        /// reflects the user's prior overrides, and the very next pill click
        /// re-solves with only that ONE new override applied - silently
        /// discarding every override the user set before restarting.
        /// <para>
        /// <paramref name="nodeOverrides"/>/<paramref name="ignoredItemIds"/>
        /// are copied, not aliased: this instance owns its two collections for
        /// their entire lifetime (every other mutator below assumes that), and
        /// PersistedPlan's own copies must stay independent so a later pill
        /// click's mutation here can never reach back into the object
        /// PlanStoreHelpers just deserialized.
        /// </para>
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        internal void RestoreOverrides(
            IReadOnlyDictionary<int, AcquisitionSource> nodeOverrides,
            IReadOnlyList<int> ignoredItemIds)
        {
            if (nodeOverrides != null)
            {
                foreach (var kvp in nodeOverrides)
                {
                    _nodeOverrides[kvp.Key] = kvp.Value;
                }
            }

            if (ignoredItemIds != null)
            {
                foreach (int itemId in ignoredItemIds)
                {
                    _ignoredItemIds.Add(itemId);
                }
            }
        }

        /// <summary>
        /// Renders the Recipe Tree section's single shared content
        /// FlowPanel: one root per requested item, stacked - N top-level
        /// trees for a multi-item batch (the synthetic wrapper root never
        /// surfacing - see CraftingPlanResult.MultiItemRoots' own doc
        /// comment), or the familiar single tree when treeRoots has one
        /// element. Each root node already carries its own full icon/name/
        /// quantity/pill/cost row (RenderTreeNode), so no separate
        /// per-root header row is needed - gw2e's own "N independent
        /// top-level recipe trees" look falls out for free.
        /// </summary>
        internal void CreateTreeSection(IReadOnlyList<CraftingTreeNode> treeRoots, int panelWidth)
        {
            _treeNodeStates.Clear();
            _treeRowsByNodeId.Clear();
            _treeRowIdsAmbiguous = false;

            // The five action buttons this header used to carry now live in
            // CraftingPlanView's non-scrolling top strip - see
            // TreeToolbarCommands. Nothing interactive is left in the header,
            // so the suppressToggle guard (and the press-time hover flag it
            // read) went with them.
            _treeRoots = treeRoots as List<CraftingTreeNode> ?? new List<CraftingTreeNode>(treeRoots);

            // Column pre-scan: ONE walk of the whole tree per render
            // pass, before any row is built, so every row (including the
            // ones an expand click builds later) anchors to the same
            // sub-columns. Never re-run per row draw or per resize tick -
            // the result is data-derived, not panelWidth-derived.
            //
            // Hoisted above the header because the header's title now
            // carries the tree's node count, which comes out of this same
            // walk. It reads nothing the header
            // produces, so the move is ordering only.
            var scan = ScanTreeColumns(_treeRoots);
            _costColumnWidths = TreeCostColumnFloor.Widen(_planCostColumnFloor, scan.CostWidths);
            _planCostColumnFloor = _costColumnWidths;
            _scannedNodeCount = scan.NodeCount;

            // Second data-derived column, after the cost one because it
            // spends what the cost column leaves - see ScannedPillColumn.
            // The claim is what it spent of the cost column's own slack,
            // which EffectiveCostColumnWidth hands back below.
            var pillColumn = ScannedPillColumn(_treeRoots, panelWidth);
            _pillColumnWidth = pillColumn.Width;
            _pillColumnCostClaim = pillColumn.CostClaim;
            _planPillRequiredFloor = pillColumn.RequiredFloor;

            // Parenthesised count, like every other countable section
            // ("Used Materials (12)", "Shopping List (7)"). The number is
            // every node at every depth - the rows Expand All reveals -
            // not the currently visible ones, which would change under the
            // reader on every caret click. A tree with no roots renders no
            // section body at all, so it keeps the bare title rather than
            // advertising "(0)".
            string title = scan.NodeCount > 0
                ? $"Recipe Tree ({scan.NodeCount})"
                : "Recipe Tree";
            var header = _host.CreateTreeSectionHeader(
                title, PlanSectionType.RecipeTree, panelWidth, true, null);
            var treeFlow = header.ContentFlow;
            _treeFlow = treeFlow;

            // Column headers over the two columns a tree row's right-hand
            // side actually has. Both track the panel width (the pill+cost
            // block's x is width-derived), hence middleXForWidth/
            // rightXForWidth rather than build-time x's, and both centre
            // over the INK their cells cover rather than over the bands
            // reserved for them, bounded only by each other and by the
            // table's edge (JustifiedColumnTracks.HeaderRoom).
            // Counted by PlanContentHeightMath.MultiRootTreeFlowHeight,
            // which every treeFlow height assignment goes through, and
            // guarded on the same "is there a tree at all" condition that
            // math counts the header under: a header over zero roots would
            // be a row the section's own height math reserves nothing for.
            if (_treeRoots.Count > 0)
            {
                int headerCostColumnWidth = EffectiveCostColumnWidth();

                // Captured by value beside headerCostColumnWidth: an
                // in-place refresh that would change either one bails out
                // and rebuilds (TryRefreshInPlace), so a live field read
                // here could only ever disagree with the rows.
                var headerCostWidths = _costColumnWidths;
                int sourceHeaderWidth = MeasureHeaderLabel(SourceHeaderText);
                int costHeaderWidth = MeasureHeaderLabel(CostHeaderText);

                // _sourceHeaderInkWidth is read LIVE, unlike everything
                // else this closure holds: no row exists yet, so the ink
                // the header centres over is not knowable until the loop
                // below has run and NoteSourceHeaderInk has re-placed the
                // header behind it.
                int headerPillColumnWidth = _pillColumnWidth;
                Func<int, PlanRelayoutMath.TreeColumnEdges> headerEdgesFor =
                    w => PlanRelayoutMath.ComputeTreeColumnEdges(
                        w, 0, 0, headerPillColumnWidth, headerCostColumnWidth, TreeRightMargin);

                _treeHeaderRelayout = ColumnHeaderRowRenderer.CreateColumnHeaderRow(
                    treeFlow, panelWidth, "Item", PlanRelayoutMath.TableLeftHeaderX, CostHeaderText, _sink,
                    middleLabel: SourceHeaderText,
                    middleXForWidth: w =>
                    {
                        var edges = headerEdgesFor(w);
                        PlanRelayoutMath.ComputeTreeHeaderRooms(
                            edges, _sourceHeaderInkWidth, headerCostWidths.WidestRowRunWidth,
                            out var sourceRoom, out _);
                        return TreePillRunLayout.HeaderX(
                            edges.PillColX, _sourceHeaderInkWidth, sourceHeaderWidth, sourceRoom);
                    },
                    rightXForWidth: w => headerEdgesFor(w).CostRightEdge,
                    rightLabelXForWidth: w =>
                    {
                        var edges = headerEdgesFor(w);
                        PlanRelayoutMath.ComputeTreeHeaderRooms(
                            edges, _sourceHeaderInkWidth, headerCostWidths.WidestRowRunWidth,
                            out _, out var costRoom);
                        return TreeCostColumnMath.HeaderX(
                            edges.CostRightEdge, headerCostWidths, costHeaderWidth, costRoom);
                    },
                    // The flow's own settled height less the header row,
                    // which is how MultiRootTreeFlowHeight composes it.
                    // Live, because expanding a node changes it without
                    // rebuilding the header.
                    rowsHeight: () => treeFlow.Height - HeaderBands.RowHeight);
            }

#if DEBUG
            int relayoutCountBeforeTree = _sink.RelayoutCount;
#endif
            // A thin gap
            // between consecutive roots so N stacked full item trees read
            // as N distinct blocks (PlanContentHeightMath.
            // MultiRootDividerHeight) - never inserted for a single root,
            // which keeps that case's rendered rows byte-identical to the
            // single-item render.
            for (int i = 0; i < _treeRoots.Count; i++)
            {
                if (i > 0)
                {
                    var rootDivider = new ClippedPanel()
                    {
                        Size = new Point(panelWidth, PlanContentHeightMath.MultiRootDividerHeight),
                        Parent = treeFlow,
                    };
                    _sink.AddRelayout(w => rootDivider.Size = new Point(w, PlanContentHeightMath.MultiRootDividerHeight));
                }

                RenderTreeNode(_treeRoots[i], treeFlow, panelWidth, 0, dimmed: false);
            }
#if DEBUG
            // Every RenderTreeNode call registers its
            // own relayout closure (see the field comment on
            // _relayoutActions) - a single root node still yields at least
            // one. Zero growth here would mean that mechanism itself
            // silently broke.
            if (_sink.RelayoutCount == relayoutCountBeforeTree)
            {
                Logger.Warn("Recipe Tree root rendered but registered no relayout closures - it will not track live window resize.");
            }
#endif

            // Every container this initial build
            // populated (treeFlow plus every childFlow created for a
            // default-expanded node) still reads its construction-time
            // Size.Y of 0 at this point - one synchronous pass now finalizes
            // every one of them from the same PlanContentHeightMath
            // arithmetic the rows above were just laid out with, before
            // this method returns to RenderPlan/PreserveScrollAcross.
            RefreshTreeContainerHeights();

            // Published last: every action below reads state this method
            // just finished building.
            _host.SetTreeToolbar(new TreeToolbarCommands
            {
                BestPath = ApplyBestPathPreset,
                CraftAll = () => ApplyPreset(AcquisitionSource.Craft),
                BuyAll = () => ApplyPreset(AcquisitionSource.BuyFromTp),
                ExpandAll = ExpandAll,
                CollapseAll = CollapseAll,
                ClearOverrides = ClearOverrides,
                ClearIgnored = ClearIgnored,
                GetOverrideCount = () => _nodeOverrides.Count,
                GetIgnoredCount = () => _ignoredItemIds.Count,
                CanReSolve = () => _lastResult?.SolveContext != null,
                CraftAllWouldChange = () => PresetWouldChange(AcquisitionSource.Craft),
                BuyAllWouldChange = () => PresetWouldChange(AcquisitionSource.BuyFromTp),
            });
        }

        // Decision preset: clear every manual override and re-solve for the
        // solver's own cheapest plan. The Count == 0 early return is the
        // belt to the view's braces - the view gates this behind its own
        // would-change predicate now, but the command is still directly
        // invokable with no dialog wiring at all.
        private void ApplyBestPathPreset()
        {
            if (_nodeOverrides.Count == 0)
            {
                return;
            }

            _nodeOverrides.Clear();
            ApplyOverridesAndResolve(isBestPathPreset: true);
        }

        /// <summary>
        /// The Overrides chip's clear action: back to the solver's own
        /// choices. MEASURED: identical work to
        /// <see cref="ApplyBestPathPreset"/> - clear the same dictionary,
        /// re-solve - and it differs only in writing the ordinary
        /// "Plan updated" event rather than claiming "Best path restored",
        /// which is a preset's label and not a description of clearing.
        /// See KNOWN-ISSUES #59: the two buttons being one action is a
        /// recorded finding, not something this seam invents a difference
        /// to hide.
        /// </summary>
        private void ClearOverrides()
        {
            if (_nodeOverrides.Count == 0)
            {
                return;
            }

            _nodeOverrides.Clear();
            ApplyOverridesAndResolve();
        }

        /// <summary>
        /// The Ignored chip's clear action. Runs the SAME re-solve path any
        /// ignore-pill click runs - no bespoke status string, because
        /// nothing bespoke happened.
        /// </summary>
        private void ClearIgnored()
        {
            if (_ignoredItemIds.Count == 0)
            {
                return;
            }

            _ignoredItemIds.Clear();
            ApplyOverridesAndResolve();
        }

        /// <summary>
        /// Whether applying a preset would actually change the override
        /// map - the same key set with the same values re-solves to the
        /// identical plan (ignore marks are untouched by a preset), so the
        /// click is a no-op the view reports instead of performing.
        /// Answered at click time: it walks the solver tree to build the
        /// preset, which is bounded but not free.
        /// <para>
        /// NULL, not false, with no solve context. A persisted plan whose
        /// Result deserialises without one restores a renderable tree
        /// (PlanStructuralValidator accepts a null SolveContext) and a
        /// visible toolbar, and every re-solve on it is unavailable rather
        /// than unnecessary. See <see cref="TreeToolbarCommands"/>.
        /// </para>
        /// </summary>
        private bool? PresetWouldChange(AcquisitionSource source)
        {
            if (_lastResult?.SolveContext == null)
            {
                return null;
            }

            var preset = CraftingPlanPipeline.BuildPresetOverrides(_lastResult.SolveContext, source);
            if (preset.Count != _nodeOverrides.Count)
            {
                return true;
            }

            foreach (var kvp in preset)
            {
                if (!_nodeOverrides.TryGetValue(kvp.Key, out var current) || current != kvp.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private void ExpandAll()
        {
            _host.PreserveScrollAcross(() =>
            {
                // Building children appends to _treeNodeStates; index loop
                // deliberately walks the growing list.
                for (int i = 0; i < _treeNodeStates.Count; i++)
                {
                    var s = _treeNodeStates[i];
                    if (!s.ChildrenBuilt)
                    {
                        int captionSplitIndex = ReceiptCaptionHelper.ComputeCaptionSplitIndex(s.Node);
                        for (int childIndex = 0; childIndex < s.Node.Children.Count; childIndex++)
                        {
                            string childCaption = ReceiptCaptionHelper.CaptionForChildIndex(captionSplitIndex, childIndex);
                            RenderTreeNode(
                                s.Node.Children[childIndex], s.ChildContainer, _host.PanelWidth,
                                s.Depth + 1, s.ChildDimmed, childCaption);
                        }

                        s.ChildrenBuilt = true;
                    }

                    s.IsExpanded = true;
                    _nodeExpansion[s.Node.NodeId] = true;
                    s.ChildContainer.Visible = true;
                    s.ArrowLabel.Text = UiGlyphs.ExpandCaret(true, UiFonts.GlyphsAvailable);
                }

                RefreshTreeContainerHeights();
            });
            HoverChainResync.AfterRebuild();
        }

        private void CollapseAll()
        {
            _host.PreserveScrollAcross(() =>
            {
                foreach (var s in _treeNodeStates)
                {
                    s.IsExpanded = false;
                    _nodeExpansion[s.Node.NodeId] = false;
                    s.ChildContainer.Visible = false;
                    s.ArrowLabel.Text = ">";
                }

                RefreshTreeContainerHeights();
            });

            HoverChainResync.AfterRebuild();
        }

        private void ApplyPreset(AcquisitionSource source)
        {
            if (_lastResult?.SolveContext == null)
            {
                return;
            }

            _nodeOverrides.Clear();
            // Walk the full solver tree (not the display tree, which hides
            // children under bought nodes) so one click reaches every level.
            var preset = CraftingPlanPipeline.BuildPresetOverrides(
                _lastResult.SolveContext, source);
            foreach (var kvp in preset)
            {
                _nodeOverrides[kvp.Key] = kvp.Value;
            }

            ApplyOverridesAndResolve();
        }

        // IsBestPathPreset must come from which
        // control fired this call, not be inferred from the resulting
        // _nodeOverrides count - see StatusText.ForOverrideResolve for why.
        //
        // anchorNodeId names the row a pill click landed on, so the
        // restore can hold that row still even at scroll offset zero,
        // where the host will not anchor on the cursor alone. Null for
        // the toolbar presets, which act on the whole tree at once and
        // name no row.
        private void ApplyOverridesAndResolve(bool isBestPathPreset = false, int? anchorNodeId = null)
        {
            // Edit since the move: this used to return silently on a
            // missing solve context, which made EVERY local change - a pill
            // click, a preset, either chip's clear - a dead click on a plan
            // restored without one. The click is still refused; it now says
            // so. Unwired _resolveOverridesSync stays silent: that is a
            // build-time wiring fault, not a state the user is in.
            if (_lastResult?.SolveContext == null)
            {
                _host.SetStatus(StatusText.ReSolveUnavailable);
                return;
            }

            if (_resolveOverridesSync == null)
            {
                return;
            }

            try
            {
                var result = _resolveOverridesSync(_lastResult.SolveContext, _nodeOverrides, _ignoredItemIds);
                _lastResult = result;
                _host.SetLastDebugLog(result.DebugLog);
                var vm = _vmBuilder.Build(result);
                _host.CurrentPlan = vm;
                _host.PreserveScrollAcross(() => _host.RenderPlanAfterResolve(vm), anchorNodeId);
                // The click that got us here came from a cursor that has
                // not moved, and the render just replaced controls under
                // it - see HoverChainResync.
                HoverChainResync.AfterRebuild();
                _host.SetStatus(StatusText.ForOverrideResolve(isBestPathPreset));
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Override re-solve failed");
                _host.SetStatus(StatusText.ForUpdateFailure(ex.Message));
            }
        }

        private const int TreeRowHeight = PlanContentHeightMath.TreeRowHeight;

        // Row-local vertical anchors. The tier-2 icon resize grew the row
        // 40 -> 48, so the icon frame's center moved down 4px; each anchor
        // keeps its pre-tier-2 offset from that center (caret/qty/name/cost
        // 12 -> 16, pills 10 -> 14). The icon itself stays top-padded by
        // PlanContentHeightMath.TreeRowIconPad, which is the height law's
        // own term.
        private const string SourceHeaderText = "Source";
        private const string CostHeaderText = "Cost";

        private const int TreeRowTextY = 16;
        private const int TreeRowPillY = 14;
        private const int TreeCostColumnWidth = 150;
        private const int TreeRightMargin = 8;

        // Decision-pill chrome. TightPillPadding is the first thing tried
        // when a row's pills do not fit: 3px of side padding instead of 6
        // still reads as a pill, and squeezing beats hiding a real option
        // (PlanRelayoutMath.ComputePillFit).
        // 24, not 20. A pill's label sits at y=2 inside an inset fill panel
        // of PillHeight - 2, and the Font14 label's lowest ink is y=21; the
        // old 18px interior clipped it. 24 gives the same 1px of interior
        // slack the Font12 label had.
        private const int PillHeight = 24;
        private const int PillGap = 6;
        private const int PillPadding = 12;
        private const int TightPillPadding = 6;

        // What a dead click on a dimmed pill means, and the one action
        // that makes it live again. Every dimmed row is somewhere under a
        // node the solver decided to buy, so switching that node to CRAFT
        // is always the answer, however deep the row sits.
        private const string DimmedPillTooltip =
            "Under a bought item - switch the parent to CRAFT to change this";

        /// <summary>
        /// A header label's width at the header band's own font - measured
        /// from the string, because a Blish Label's Width is not settled
        /// until its next layout pass and the centring closures run before
        /// then.
        /// </summary>
        private static int MeasureHeaderLabel(string text)
        {
            return (int)Math.Ceiling(HeaderBands.Font.MeasureString(text ?? "").Width);
        }

        /// <summary>
        /// Width the cost column actually gets this render: its reserve
        /// (TreeCostColumnMath.Reserve) less whatever the decision-pill
        /// column claimed from it (ScannedPillColumn's claim), never below
        /// the leftmost ink any row draws. Widening it pushes the decision
        /// pills and the name budget left - which is the point: before, a
        /// wide cost run silently overprinted the pills.
        /// </summary>
        private int EffectiveCostColumnWidth()
        {
            return TreeCostColumnMath.WidthAfterClaim(
                _costColumnWidths, TreeCostColumnWidth, _pillColumnCostClaim);
        }

        /// <summary>
        /// Blish-bound half of the column pre-scan: the pure walk lives in
        /// TreeCostColumnMath.ScanColumns, this supplies the measurements
        /// it cannot make itself. Strings are memoised because a tree
        /// repeats them heavily (number strings like "00"/"42", and one
        /// item name recurs across the tree), so MeasureString runs once
        /// per DISTINCT string rather than once per node; the currency
        /// callback only fires for the handful of vendor nodes that draw a
        /// currency run at all.
        /// </summary>
        private TreeCostColumnMath.TreeColumnScan ScanTreeColumns(IReadOnlyList<CraftingTreeNode> roots)
        {
            var font = UiFonts.Body;
            var measured = new Dictionary<string, int>();
            var metadata = _host.CurrentPlan?.CurrencyMetadata;

            Func<string, int> measure = text =>
            {
                if (!measured.TryGetValue(text, out int width))
                {
                    width = (int)Math.Ceiling(font.MeasureString(text).Width);
                    measured[text] = width;
                }

                return width;
            };

            return TreeCostColumnMath.ScanColumns(
                roots,
                measure,
                node => CoinCurrencyRenderer.TotalCurrencySegmentsWidth(
                    CoinCurrencyRenderer.BuildCurrencySegments(
                        CurrencyDisplayResolver.ResolveAmounts(node.VendorCurrencyCosts, metadata), font)));
        }

        /// <summary>
        /// Blish-bound half of the pill-column scan: the walk and the whole
        /// width/claim rule live in TreePillColumnMath.Resolve, this
        /// supplies the measurements they cannot make and the plan's ink
        /// high-water mark.
        /// <para>
        /// Returns rather than writing fields, because TryRefreshInPlace
        /// asks this as a pure gate question: an in-place refresh is only
        /// safe when the answer is the one already on screen, and the
        /// CLAIM is half of that answer (EffectiveCostColumnWidth nets it
        /// out of the cost column's reserve, so a stale claim moves
        /// PillColX against a full render at the same window size).
        /// </para>
        /// </summary>
        private TreePillColumnMath.ColumnResolution ScannedPillColumn(
            IReadOnlyList<CraftingTreeNode> roots, int panelWidth)
        {
            var font = UiFonts.Caption;
            var plan = _host.CurrentPlan;
            var leading = new List<int>(4);
            int toggleSlot = ReservedIgnorePillWidth();

            // Memoised for the reason ScanTreeColumns memoises: a tree
            // repeats its pill texts heavily (every craftable row draws
            // CRAFT/TP/VENDOR), and this walk also runs on the ignore-click
            // path, where TryRefreshInPlace asks it whether the column
            // moved.
            var measured = new Dictionary<string, int>();
            Func<string, int> measure = text =>
            {
                if (!measured.TryGetValue(text, out int measuredWidth))
                {
                    measuredWidth = (int)Math.Ceiling(font.MeasureString(text).Width);
                    measured[text] = measuredWidth;
                }

                return measuredWidth;
            };

            int required = TreePillColumnMath.Scan(roots, node =>
            {
                var specs = DecisionPillPlanner.BuildPillSpecs(
                    node, plan?.CurrencyPlanTotals, plan?.OwnedCurrencyAmounts,
                    plan?.OwnedStockDrawnItemIds);
                int leadingCount = FlowedPillCount(specs);

                leading.Clear();
                for (int i = 0; i < leadingCount; i++)
                {
                    leading.Add(MeasuredPillWidth(specs[i], measure, toggleSlot));
                }

                return TreePillColumnMath.RequiredWidth(leading, PillGap);
            });

            // Room the cost column can give back, off _costColumnWidths
            // rather than through EffectiveCostColumnWidth, because the
            // latter already nets out the PREVIOUS render's claim.
            int costSlack = TreeCostColumnMath.RightSlack(_costColumnWidths, TreeCostColumnWidth);

            return TreePillColumnMath.Resolve(
                required,
                _planPillRequiredFloor,
                PlanRelayoutMath.TreePillColumnWidth,
                panelWidth,
                WindowSizing.TabPanelWidthFor(WindowSizing.MinWindowWidth),
                costSlack);
        }

        /// <summary>
        /// Recomputes and re-assigns the explicit Height of every tree childFlow
        /// container plus the top-level treeFlow, from the SAME
        /// PlanContentHeightMath arithmetic used to build the rows.
        /// <para>
        /// Setting each container's Size fires its own Resized event, which
        /// FlowPanel already wires to reflow its own parent's sibling positions
        /// (ChangedChildOnResized in the vendored FlowPanel source) - so this
        /// call alone both resizes and repositions every affected row,
        /// synchronously, with no separate Invalidate() needed.
        /// </para>
        /// <para>
        /// It recomputes every node currently in _treeNodeStates rather than
        /// walking only the toggled node's ancestor chain. Each computation is a
        /// pure function of that node's own structure plus the shared
        /// _nodeExpansion map, so recomputing unaffected containers is harmless,
        /// and this runs once per user toggle, never per frame.
        /// </para>
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        private void RefreshTreeContainerHeights()
        {
            int panelWidth = _host.PanelWidth;
            foreach (var state in _treeNodeStates)
            {
                state.ChildContainer.Size = new Point(
                    panelWidth,
                    PlanContentHeightMath.ChildrenHeight(
                        state.Node.Children, state.Depth + 1, state.ChildDimmed, _nodeExpansion));
            }

            if (_treeRoots != null && _treeRoots.Count > 0 && _treeFlow != null)
            {
                _treeFlow.Size = new Point(
                    panelWidth, PlanContentHeightMath.MultiRootTreeFlowHeight(_treeRoots, _nodeExpansion));
            }
        }

        // UI-bundle milestone: captionText is the sanctioned tooltip
        // fallback for Feature C (receipt/what-if captions) - see
        // ReceiptCaptionHelper's own doc comment for why a real extra ROW
        // is not used (frozen PlanContentHeightMath tree-height math counts
        // exactly node.Children.Count rows per level). null for every node
        // except the first child of each group under a node whose Children
        // stack cost-component leaves + a reference branch - see the three
        // call sites that compute it via ReceiptCaptionHelper.
        private void RenderTreeNode(
            CraftingTreeNode node, FlowPanel parent, int panelWidth, int depth, bool dimmed, string captionText = null)
        {
            // Every column origin, the caret glyph, the quantity prefix and
            // the dimmed-branch flag come from the Blish-free planner (see
            // its doc comment and docs/ARCHITECTURE.md section 5's STANDING
            // RULE); only control construction and the palette stay here.
            var shape = TreeRowShapePlanner.Plan(node, depth, dimmed, _nodeExpansion);
            int indent = shape.Indent;
            bool hasChildren = shape.HasChildren;

            var rowPanel = new ClippedPanel()
            {
                Size = new Point(panelWidth, TreeRowHeight),
                BackgroundColor = Color.Transparent,
                Parent = parent,
            };

            // Hover wash (pattern per SuggestionPanel row highlighting).
            // Color.White * 0.07f premultiplies alpha; a raw
            // Color(255,255,255,18) renders as near-opaque white in XNA's
            // premultiplied pipeline (verified via screenshot loop).
            rowPanel.MouseEntered += (_, __) =>
            {
                rowPanel.BackgroundColor = Color.White * 0.07f;
            };
            rowPanel.MouseLeft += (_, __) =>
            {
                rowPanel.BackgroundColor = Color.Transparent;
            };

            // Caret column: fixed width even for leaf rows (no children ->
            // no glyph, but the icon column still starts at the same x as
            // every sibling), so caret state is scannable at a glance.
            Label arrowLabel = null;
            if (hasChildren)
            {
                Color arrowColor = dimmed ? Color.White * 0.35f : Color.White;
                arrowLabel = new Label()
                {
                    Font = UiFonts.BodyGlyphs,
                    Text = UiGlyphs.ExpandCaret(shape.IsExpanded, UiFonts.GlyphsAvailable),
                    TextColor = arrowColor,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(indent, TreeRowTextY),
                    Parent = rowPanel,
                };
            }

            // Icon column: rarity-framed, dimmed reference branches get a
            // neutral frame plus a dark scrim over the icon itself (Blish
            // panels have no tint/filter property, so a translucent black
            // overlay approximates gw2e's grayscale+opacity filter).
            int iconX = shape.IconX;
            ItemIconFrame frame = dimmed
                ? ItemIconFrame.Explicit(new Color(60, 60, 60))
                : ItemIconFrame.ForRarity(node.Rarity);
            var hover = TreeRowHover(node, captionText);
            var iconFrame = IconControls.CreateItemIcon(
                rowPanel, node.IconUrl, frame, iconX, PlanContentHeightMath.TreeRowIconPad,
                ItemIconTier.BagSidebar, hover);
            Panel iconScrim = null;
            if (dimmed)
            {
                iconScrim = new ClippedPanel()
                {
                    Size = new Point(TreeRowShapePlanner.IconSize, TreeRowShapePlanner.IconSize),
                    Location = new Point(
                        iconX + TreeRowShapePlanner.IconBorder,
                        PlanContentHeightMath.TreeRowIconPad + TreeRowShapePlanner.IconBorder),
                    BackgroundColor = Color.Black * 0.5f,
                    Parent = rowPanel,
                };
            }

            // Name column: fixed x regardless of depth's remaining width;
            // clipped with an ellipsis against the pill column so long names
            // never collide with the fixed-position columns to its right.
            // PillColX/costRightEdge/nameMaxWidth now come from
            // PlanRelayoutMath.ComputeTreeColumnEdges - the SAME pure
            // function RegisterRowResizeHandlers' closures call, so the
            // build and every later resize tick can never disagree about
            // these columns.
            int nameX = shape.NameX;

            var nameFont = UiFonts.Body;
            string qtyPrefix = shape.QuantityPrefix;
            int qtyWidth = qtyPrefix.Length > 0
                ? (int)System.Math.Ceiling(nameFont.MeasureString(qtyPrefix).Width)
                : 0;

            // Snapshot, not a live field read: every closure below outlives
            // this call, and the next render pass resets _costColumnWidths
            // before rebuilding. Capturing the value keeps a row's build-time
            // columns and its own relayout arithmetic identical by
            // construction.
            var columnWidths = _costColumnWidths;
            int costColumnWidth = EffectiveCostColumnWidth();
            int pillColumnWidth = _pillColumnWidth;
            var edges = PlanRelayoutMath.ComputeTreeColumnEdges(
                panelWidth, nameX, qtyWidth, pillColumnWidth, costColumnWidth, TreeRightMargin);
            int pillColX = edges.PillColX;

            string fullName = node.Name ?? "";

            // Registered before the row's controls exist so every closure
            // below reads its mutable state (pills, cost cell, qty width)
            // from ONE place an in-place refresh can rewrite.
            var handle = new TreeRowHandle
            {
                Node = node,
                Depth = depth,
                Dimmed = dimmed,
                CaptionText = captionText,
                FullName = fullName,
                RowPanel = rowPanel,
                IconFrame = iconFrame,
                IconScrim = iconScrim,
                NameX = nameX,
                QtyWidth = qtyWidth,
                PillColumnWidth = pillColumnWidth,
                CostColumnWidth = costColumnWidth,
                ColumnWidths = columnWidths,
            };
            if (_treeRowsByNodeId.ContainsKey(node.NodeId))
            {
                _treeRowIdsAmbiguous = true;
            }
            else
            {
                _treeRowsByNodeId[node.NodeId] = handle;
                // Same NodeId identity, same ambiguity guard: a duplicated
                // id cannot say which row the user was on either.
                _registerRowScrollAnchor?.Invoke(node.NodeId, rowPanel);
            }

            string displayName = LabelHelpers.EllipsizeToWidth(nameFont, fullName, edges.NameMaxWidth);

            Color qtyColor = new Color(170, 170, 170);
            Color nameColor = RarityColors.GetRarityNameColor(node.Rarity);
            if (dimmed)
            {
                qtyColor *= 0.45f;
                // Lift dark hues toward readable before dimming (premultiplied-
                // correct: Lerp opaque colors first, then apply alpha via *).
                nameColor = Color.Lerp(nameColor, Color.White, 0.30f) * 0.50f;
            }

            Label qtyLabel = null;
            if (qtyPrefix.Length > 0)
            {
                // Same baseline as the name label below it - both boxes get
                // the descender clearance so the two halves of "12x <name>"
                // can never sit on different lines.
                qtyLabel = LabelHelpers.WithDescenderClearance(
                    new Label()
                    {
                        Text = qtyPrefix,
                        Font = nameFont,
                        TextColor = qtyColor,
                        AutoSizeWidth = true,
                        AutoSizeHeight = true,
                        Location = new Point(nameX, TreeRowTextY),
                        Parent = rowPanel,
                    });
            }

            var nameLabel = LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = displayName,
                    Font = nameFont,
                    TextColor = nameColor,
                    ShowShadow = true,
                    ShadowColor = dimmed ? Color.Black * 0.4f : Color.Black * 0.8f,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(nameX + qtyWidth, TreeRowTextY),
                    Parent = rowPanel,
                });
            handle.QtyLabel = qtyLabel;
            handle.NameLabel = nameLabel;

            WireWikiLinkContextAction(rowPanel, node);

            StampRowIcon(iconFrame, iconScrim, hover);

            // Decision pill column: one pill per feasible source (direct
            // selection - click sets the override and re-solves), or a
            // single locked/HAVE/CURRENCY pill when there is no choice.
            RenderDecisionPills(handle, node, pillColX, edges.ActionButtonX, TreeRowPillY, dimmed);

            // Cost column: four right-aligned sub-columns (gold, silver,
            // copper, then any non-coin currency), each sized by this
            // render's pre-scan of the whole tree, so the coin ICONS land
            // on the same x on every row drawing the same bands - see
            // Services/TreeCostColumnMath, whose ComputeRowEdges also
            // covers why a row with no currency of its own ends on the
            // column's right edge rather than short of it.
            // Only rendered
            // when this node has a real committed decision with a cost
            // figure at all (SubtreeCost.HasValue) - HAVE/CURRENCY/UNKNOWN
            // nodes carry no SubtreeCost and keep the column blank exactly
            // as before (their own pill already communicates "no price").
            // Within that: a BuyFromVendor node priced wholly or partly in
            // a non-coin currency renders currency segments alongside/
            // instead of coin (sibling site to the shopping list's #16
            // fix, same CoinCurrencyRenderer.RenderValueCellRightAligned entry point); a
            // decision whose real cost is genuinely zero-and-uncosted
            // renders a dash instead of an invented "0".
            //
            // A node whose children are the new synthesized cost-
            // component leaves (see CraftingTreeBuilder.
            // BuildVendorCostComponentLeaves - every child of such a node is
            // a component leaf, never mixed with a reference branch or a
            // real craft child) shows ONLY the compact gold total here -
            // the breakdown lives one expand-click away as real child
            // rows, instead of one very long segmented row colliding with
            // the layout.
            RenderCostCell(handle, node, edges.CostRightEdge, dimmed);

            FlowPanel childFlow = RenderChildContainer(
                node, parent, panelWidth, depth, shape, rowPanel, arrowLabel, handle);

            RegisterRowResizeHandlers(handle, rowPanel, childFlow, nameFont);
        }

        /// <summary>
        /// The row's right-click-to-wiki context action, wired only for
        /// names that can resolve to a page - see WikiLinkBuilder's
        /// SentinelNames for the ones that never can.
        /// </summary>
        private static void WireWikiLinkContextAction(Panel rowPanel, CraftingTreeNode node)
        {
            // This module's only external-URL launch - a context action
            // (right-click), not a visible icon. Every tree
            // row gets this, item leaf or internal node alike - a wiki page
            // that does not exist for an internal-only concept (e.g. a
            // synthesized cost-component "currency" name) just 404s rather
            // than crashing anything; WikiLinkBuilder.HasWikiPage/
            // BuildItemPageUrl additionally suppress the affordance
            // entirely for the known placeholder names (see
            // WikiLinkBuilder's SentinelNames), which never resolve to a
            // real page at all.
            //
            // Fix-pass (render-path allocation): HasWikiPage is a cheap
            // non-whitespace + not-a-placeholder-name check - the actual
            // URL (Trim + Replace + Uri.EscapeDataString, a closure, and a
            // delegate) is built lazily inside the press/release handlers
            // below instead of eagerly for every tree row on every build
            // and every lazy expand, since most rows are never
            // right-clicked at all.
            //
            // Fix-pass (right-click-as-camera-drag): GW2's own right-drag
            // is the camera-rotate gesture, and firing on button-DOWN alone
            // (the previous behavior) meant a drag begun over this row -
            // input Blish otherwise swallows here today - opened the
            // browser and yanked focus out of a fullscreen game the
            // instant the button went down, with no way to abort. Firing
            // on RightMouseButtonReleased alone is NOT a fix: Blish routes
            // the release event to whichever row is under the cursor at
            // release time, so a drag that started on a DIFFERENT row
            // would open THIS row's page instead. Pairing press+release on
            // this SAME rowPanel closes that: press arms a per-row flag,
            // and only this row's own Released handler (which only fires
            // when the release also lands on this row) can consume it.
            // MouseLeft additionally disarms the flag the moment the
            // cursor leaves this row after a press, so a drag that starts
            // here, wanders off, and is released back over this row later
            // (from an unrelated gesture) cannot replay a stale arm.
            //
            // Unlike RenderChildContainer's caret handler, this one does
            // not exclude clicks landing on a pill (this method never sees
            // them). Intentional and harmless: decision pills carry no
            // right-click meaning, so a right-click that lands on one
            // still falls through to this row's wiki-link handler rather
            // than doing nothing.
            if (WikiLinkBuilder.HasWikiPage(node.Name))
            {
                // Which page a row opens is decided by
                // TreeRowTooltipComposer.BuildWikiUrl, the same Blish-free
                // class that writes the tooltip line naming the affordance.
                string wikiUrl = TreeRowTooltipComposer.BuildWikiUrl(node);
                bool wikiLinkArmed = false;
                rowPanel.RightMouseButtonPressed += (_, __) => wikiLinkArmed = true;
                rowPanel.MouseLeft += (_, __) => wikiLinkArmed = false;
                rowPanel.RightMouseButtonReleased += (_, __) =>
                {
                    if (wikiLinkArmed)
                    {
                        wikiLinkArmed = false;
                        WikiLinkLauncher.Open(wikiUrl);
                    }
                };
        }
        }

        /// <summary>
        /// The row's child container and its expand/collapse caret
        /// handler, or null when the row is a leaf. Recurses into
        /// <see cref="RenderTreeNode"/> for children visible now; a
        /// collapsed node's children are built on first expand instead.
        /// </summary>
        private FlowPanel RenderChildContainer(
            CraftingTreeNode node, FlowPanel parent, int panelWidth, int depth, TreeRowShape shape,
            Panel rowPanel, Label arrowLabel, TreeRowHandle handle)
        {
            bool hasChildren = shape.HasChildren;
            bool isExpanded = shape.IsExpanded;

            // Child container. Children of a non-Craft decision are this
            // module's own informational reference branch (gw2e has no
            // equivalent ".not-crafted" concept; this dimmed "what it would
            // cost to craft instead" branch is a module original).
            FlowPanel childFlow = null;
            if (hasChildren)
            {
                bool childDimmed = shape.ChildDimmed;

                // Standard (explicit) height, same
                // as the section header's contentFlow - see that
                // construction site's comment. Starts at 0; the caller that
                // ultimately owns this build pass (CreateTreeSection's
                // initial call, or the caret handler below) finalizes the
                // real height via RefreshTreeContainerHeights before
                // control returns to PreserveScrollAcross's caller.
                childFlow = new ClippedFlowPanel()
                {
                    Size = new Point(panelWidth, 0),
                    FlowDirection = ControlFlowDirection.SingleTopToBottom,
                    Parent = parent,
                };

                var state = new TreeNodeState
                {
                    Node = node,
                    Depth = depth,
                    ChildContainer = childFlow,
                    ArrowLabel = arrowLabel,
                    ChildDimmed = childDimmed,
                };
                _treeNodeStates.Add(state);
                handle.State = state;
                if (isExpanded)
                {
                    // UI-bundle milestone, Feature C: caption split computed
                    // once per node, reused for every child index - see
                    // ReceiptCaptionHelper's own doc comment.
                    int captionSplitIndex = ReceiptCaptionHelper.ComputeCaptionSplitIndex(node);
                    for (int childIndex = 0; childIndex < node.Children.Count; childIndex++)
                    {
                        string childCaption = ReceiptCaptionHelper.CaptionForChildIndex(captionSplitIndex, childIndex);
                        RenderTreeNode(node.Children[childIndex], childFlow, panelWidth, depth + 1, childDimmed, childCaption);
                    }

                    state.ChildrenBuilt = true;
                    state.IsExpanded = true;
                    childFlow.Visible = true;
                }
                else
                {
                    state.IsExpanded = false;
                    childFlow.Visible = false;
                }

                EventHandler<MouseEventArgs> toggleHandler = (_, __) =>
                {
                    // Pills have their own click actions; do not also treat
                    // a pill click as an expand/collapse toggle.
                    if (CursorOverPill(rowPanel, handle.Pills))
                    {
                        return;
                    }

                    _host.PreserveScrollAcross(() =>
                    {
                        if (!state.ChildrenBuilt)
                        {
                            // Read the LIVE width rather than the
                            // (possibly long-stale, since resize no longer
                            // triggers a rebuild) width this node itself was
                            // built at - see GetCurrentPanelWidth.
                            int currentWidth = _host.PanelWidth;
                            int captionSplitIndex = ReceiptCaptionHelper.ComputeCaptionSplitIndex(state.Node);
                            for (int childIndex = 0; childIndex < state.Node.Children.Count; childIndex++)
                            {
                                string childCaption = ReceiptCaptionHelper.CaptionForChildIndex(captionSplitIndex, childIndex);
                                RenderTreeNode(
                                    state.Node.Children[childIndex], state.ChildContainer, currentWidth,
                                    state.Depth + 1, state.ChildDimmed, childCaption);
                            }

                            state.ChildrenBuilt = true;
                        }

                        state.IsExpanded = !state.IsExpanded;
                        _nodeExpansion[state.Node.NodeId] = state.IsExpanded;
                        state.ChildContainer.Visible = state.IsExpanded;
                        state.ArrowLabel.Text =
                            UiGlyphs.ExpandCaret(state.IsExpanded, UiFonts.GlyphsAvailable);
                        RefreshTreeContainerHeights();
                    });
                    // A caret click builds or hides the rows directly
                    // under the cursor - see HoverChainResync.
                    HoverChainResync.AfterRebuild();
                };
                // Same pill guard as toggleHandler, for the same reason: a
                // press on a pill reaches this row panel too, and the row
                // must not answer a click it is about to ignore.
                PressFeedback.Wire(rowPanel, () => CursorOverPill(rowPanel, handle.Pills));
                rowPanel.Click += toggleHandler;
            }

            return childFlow;
        }

        /// <summary>
        /// The row's two resize responses: reposition on every drag
        /// tick, re-ellipsize the name only once the drag settles.
        /// </summary>
        private void RegisterRowResizeHandlers(
            TreeRowHandle handle, Panel rowPanel, FlowPanel childFlow, BitmapFont nameFont)
        {
            // Pills/cost cell reposition every drag tick (no
            // MeasureString - pill widths are already-known control Width,
            // CoinCurrencyRenderer.RepositionValueCellRightAligned uses only cached segment text
            // widths); childFlow's width tracks panelWidth with its Height
            // preserved exactly (never perturbs scroll - every
            // row/container height is explicit). The name label is
            // untouched by the relayout pass; it only re-ellipsizes at settle.
            _sink.AddRelayout(w =>
            {
                rowPanel.Size = new Point(w, TreeRowHeight);
                var e = RowEdges(handle, w);

                int placed = handle.Pills.Count < handle.PillOffsets.Count
                    ? handle.Pills.Count
                    : handle.PillOffsets.Count;
                for (int i = 0; i < placed; i++)
                {
                    handle.Pills[i].Location = new Point(e.PillColX + handle.PillOffsets[i], TreeRowPillY);
                }

                if (handle.CostCell != null)
                {
                    CoinCurrencyRenderer.RepositionValueCellInSubColumns(
                        handle.CostCell,
                        TreeCostColumnMath.ComputeRowEdges(
                            e.CostRightEdge, handle.ColumnWidths, handle.RowDrawsCurrency),
                        TreeRowTextY);
                }

                if (childFlow != null)
                {
                    childFlow.Size = new Point(w, childFlow.Height);
                }
            });
            _sink.AddReellipsis(w =>
            {
                string newDisplayName = LabelHelpers.EllipsizeToWidth(
                    nameFont, handle.FullName, RowEdges(handle, w).NameMaxWidth);
                // No tooltip re-stamp: the deferred builder reads the
                // label's CURRENT text when the box is drawn.
                if (handle.NameLabel.Text != newDisplayName)
                {
                    handle.NameLabel.Text = newDisplayName;
                }
            });
        }

        /// <summary>
        /// One row's column grid at a given panel width, read entirely off
        /// its handle - the single place build, relayout, re-ellipsis and
        /// refresh all derive it, so a refresh that changes a row's qty
        /// prefix moves every one of them together.
        /// </summary>
        private static PlanRelayoutMath.TreeColumnEdges RowEdges(TreeRowHandle handle, int panelWidth)
        {
            return PlanRelayoutMath.ComputeTreeColumnEdges(
                panelWidth, handle.NameX, handle.QtyWidth,
                handle.PillColumnWidth, handle.CostColumnWidth, TreeRightMargin);
        }

        /// <summary>
        /// Builds (or rebuilds) the row's cost cell into its handle. Only
        /// a node with a real committed decision AND a cost figure gets one
        /// - HAVE/CURRENCY/UNKNOWN nodes carry no SubtreeCost and keep the
        /// column blank, their own pill already saying "no price".
        /// </summary>
        private void RenderCostCell(
            TreeRowHandle handle, CraftingTreeNode node, int costRightEdge, bool dimmed)
        {
            handle.CostCell = null;
            handle.RowDrawsCurrency = false;
            if (!node.SubtreeCost.HasValue)
            {
                return;
            }

            // TreeCostColumnMath.ShowsCurrencySegments, not a
            // hand-repeated cost-component check: the pre-scan reserves
            // the currency sub-column from that same predicate, so a
            // second copy here could reserve for rows that never draw
            // and vice versa.
            var currencyAmounts = TreeCostColumnMath.ShowsCurrencySegments(node)
                ? CurrencyDisplayResolver.ResolveAmounts(
                    node.VendorCurrencyCosts, _host.CurrentPlan?.CurrencyMetadata)
                : null;
            handle.RowDrawsCurrency = currencyAmounts != null && currencyAmounts.Count > 0;
            handle.CostCell = CoinCurrencyRenderer.RenderValueCellInSubColumns(
                handle.RowPanel, node.SubtreeCost.Value, currencyAmounts,
                TreeCostColumnMath.ComputeRowEdges(
                    costRightEdge, handle.ColumnWidths, handle.RowDrawsCurrency),
                TreeRowTextY, UiFonts.Body, dimmed ? 0.35f : 1f);
        }

        /// <summary>
        /// Updates the already-built tree to a fresh solve WITHOUT disposing a
        /// single row, returning false when it cannot - in which case the caller
        /// renders the plan from scratch as before. Shortening the rebuild frame
        /// is what stops rapid IGNORE toggling from dropping clicks; it is not a
        /// complete fix, because <see cref="RepaintRow"/> still rebuilds a
        /// matched row's pills, so no pill INSTANCE survives a re-solve.
        /// <para>
        /// The gate is deliberately strict, and every rejection is a correct
        /// full rebuild rather than a wrong cheap one: the new tree must present
        /// the SAME built rows, in the same order, at the same depth and dim
        /// state, each still passing <see cref="TreeRowIdentity"/> against the
        /// node its row was built from, and the cost sub-column widths and the
        /// header's node count must be unchanged (both are chrome this refresh
        /// preserves rather than redraws). Ignoring a LEAF material satisfies
        /// all of that; ignoring a node with children does not, because an
        /// ignored node is built as a leaf, so that click pays for a full rebuild.
        /// </para>
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        internal bool TryRefreshInPlace(IReadOnlyList<CraftingTreeNode> newRoots)
        {
            if (_treeRoots == null || _treeFlow == null || newRoots == null)
            {
                return false;
            }

            if (_treeRowIdsAmbiguous || _treeRowsByNodeId.Count == 0)
            {
                return false;
            }

            if (newRoots.Count != _treeRoots.Count)
            {
                return false;
            }

            var scan = ScanTreeColumns(newRoots);
            if (scan.NodeCount != _scannedNodeCount)
            {
                return false;
            }

            // Against the floored widths, not the raw scan: a re-solve
            // that only REMOVES the tree's widest cost run keeps the
            // reservation it already made, so that click no longer forces
            // a full rebuild either.
            if (!TreeCostColumnFloor.Equal(
                TreeCostColumnFloor.Widen(_planCostColumnFloor, scan.CostWidths), _costColumnWidths))
            {
                return false;
            }

            // Same gate for the pill column, and for the same reason: the
            // rows this refresh preserves were placed against the width
            // already on screen. The CLAIM is gated too - it is netted out
            // of the cost column (EffectiveCostColumnWidth), so keeping a
            // stale one places these rows where a full render at this same
            // window size would not.
            var pillColumn = ScannedPillColumn(newRoots, _host.PanelWidth);
            if (pillColumn.Width != _pillColumnWidth
                || pillColumn.CostClaim != _pillColumnCostClaim)
            {
                return false;
            }

            var plan = new List<KeyValuePair<TreeRowHandle, CraftingTreeNode>>(_treeRowsByNodeId.Count);
            if (!MatchRows(newRoots, 0, false, plan))
            {
                return false;
            }

            // Every built row has to be accounted for. A shorter plan means
            // the new tree reaches fewer rows than are on screen, which is
            // a structural change the walk cannot see from the top.
            if (plan.Count != _treeRowsByNodeId.Count)
            {
                return false;
            }

            int panelWidth = _host.PanelWidth;
            foreach (var pair in plan)
            {
                RepaintRow(pair.Key, pair.Value, panelWidth);
            }

            _treeRoots = newRoots as List<CraftingTreeNode> ?? new List<CraftingTreeNode>(newRoots);
            RefreshTreeContainerHeights();
            return true;
        }

        /// <summary>
        /// Walks the new tree, pairing each node that HAS a row with that
        /// row and refusing the moment the two disagree about identity or
        /// structure. Only a node whose children were actually built
        /// descends - a collapsed subtree has no rows, so its shape is free
        /// to differ and is simply adopted along with the node.
        /// <para>
        /// A matching NodeId is where the pairing STARTS, not where it is
        /// settled: a synthetic cost-component id names a position in a
        /// vendor offer rather than an item, so identity is asked of
        /// TreeRowIdentity, which owns the argument.
        /// </para>
        /// </summary>
        private bool MatchRows(
            IReadOnlyList<CraftingTreeNode> newSiblings, int depth, bool dimmed,
            List<KeyValuePair<TreeRowHandle, CraftingTreeNode>> plan)
        {
            for (int i = 0; i < newSiblings.Count; i++)
            {
                var newNode = newSiblings[i];
                if (!_treeRowsByNodeId.TryGetValue(newNode.NodeId, out var handle))
                {
                    return false;
                }

                if (handle.Depth != depth || handle.Dimmed != dimmed)
                {
                    return false;
                }

                if (!TreeRowIdentity.SameRow(handle.Node, newNode))
                {
                    return false;
                }

                plan.Add(new KeyValuePair<TreeRowHandle, CraftingTreeNode>(handle, newNode));

                if (handle.State == null || !handle.State.ChildrenBuilt)
                {
                    continue;
                }

                bool childDimmed = dimmed || newNode.Decision != CraftingDecision.Craft;
                if (!MatchRows(newNode.Children, depth + 1, childDimmed, plan))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Re-renders the parts of one row a re-solve can change - the qty
        /// prefix, the pill column, the cost cell and the tooltip - into
        /// the controls the row already has. Everything
        /// <see cref="TreeRowIdentity"/> has just proved unchanged (icon,
        /// name text, rarity colour, caret, dim chrome) is left alone,
        /// which is most of the row and all of its texture work.
        /// <para>
        /// Unconditional rather than gated on a per-row "did anything
        /// change" test: a pill's own text, colour, tooltip and click
        /// wiring all derive from the node AND from plan-scope facts
        /// (currency totals, owned amounts, subduing results), so a
        /// cheaper test would have to re-derive nearly all of it to be
        /// correct - and a wrong skip leaves a stale, still-clickable pill.
        /// </para>
        /// </summary>
        private void RepaintRow(TreeRowHandle handle, CraftingTreeNode newNode, int panelWidth)
        {
            var nameFont = UiFonts.Body;

            if (handle.QtyLabel != null)
            {
                string newQtyPrefix = $"{newNode.Quantity}x ";
                if (handle.QtyLabel.Text != newQtyPrefix)
                {
                    handle.QtyLabel.Text = newQtyPrefix;
                    handle.QtyWidth = (int)Math.Ceiling(nameFont.MeasureString(newQtyPrefix).Width);
                    handle.NameLabel.Location = new Point(handle.NameX + handle.QtyWidth, TreeRowTextY);
                }
            }

            var edges = RowEdges(handle, panelWidth);

            // Only when the budget actually moved: the name TEXT is one of
            // the facts TreeRowIdentity proved unchanged, so an unchanged
            // qty prefix leaves an unchanged ellipsis, and this is the
            // row's only MeasureString loop.
            string displayName = LabelHelpers.EllipsizeToWidth(nameFont, handle.FullName, edges.NameMaxWidth);
            if (handle.NameLabel.Text != displayName)
            {
                handle.NameLabel.Text = displayName;
            }

            DisposePills(handle);
            RenderDecisionPills(
                handle, newNode, edges.PillColX, edges.ActionButtonX, TreeRowPillY, handle.Dimmed);

            DisposeValueCell(handle.CostCell);
            RenderCostCell(handle, newNode, edges.CostRightEdge, handle.Dimmed);

            StampRowIcon(handle.IconFrame, handle.IconScrim, TreeRowHover(newNode, handle.CaptionText));

            handle.Node = newNode;
            if (handle.State != null)
            {
                handle.State.Node = newNode;
                handle.State.ChildDimmed = handle.Dimmed || newNode.Decision != CraftingDecision.Craft;
            }
        }

        /// <summary>
        /// Drops the row's pills and the offsets that placed them, which
        /// are refilled together by the next RenderDecisionPills call and
        /// must never be left describing controls that no longer exist.
        /// </summary>
        private static void DisposePills(TreeRowHandle handle)
        {
            foreach (var pill in handle.Pills)
            {
                pill.Dispose();
            }

            handle.Pills.Clear();
            handle.PillOffsets.Clear();
        }

        /// <summary>
        /// Disposes every control a value cell put on its row - the dash,
        /// or both halves of the coin/currency segment run. A cell is
        /// parented straight to the row panel rather than to a wrapper of
        /// its own, so there is no single control to drop.
        /// </summary>
        private static void DisposeValueCell(CoinCurrencyRenderer.ValueCellHandle cell)
        {
            if (cell == null)
            {
                return;
            }

            cell.DashLabel?.Dispose();
            DisposeSegments(cell.CoinSegments);
            DisposeSegments(cell.CurrencySegments);
        }

        private static void DisposeSegments(CoinCurrencyRenderer.SegmentLayoutHandle segments)
        {
            if (segments.Controls == null)
            {
                return;
            }

            for (int i = 0; i < segments.Controls.Length; i++)
            {
                segments.Controls[i].Item1?.Dispose();
                segments.Controls[i].Item2?.Dispose();
            }
        }

        /// <summary>
        /// Rebuilds a tree row's tooltip from its (possibly re-ellipsized)
        /// display name plus its width-invariant extra lines - shared by
        /// RenderTreeNode's initial build and its settle re-ellipsis
        /// closure so the two can never disagree about tooltip content.
        /// <para>
        /// Rich, not <c>BasicTooltipText</c>: a row's unit-price line
        /// carries a gold figure, which only the rich surface can draw with
        /// coin icons - see <see cref="TooltipFacility"/>.
        /// </para>
        /// </summary>
        private ItemIconTooltip TreeRowHover(CraftingTreeNode node, string captionText)
        {
            // ExtraTooltipLines never depends on panelWidth (unit
            // price / acquisition hint text is fixed), so it is computed
            // once and reused verbatim by the settle re-ellipsis pass.
            //
            // The line-building itself (unit price, price-side-fallback
            // caveat, acquisition hint, caption, wiki-link line) lives in
            // the pure, unit-tested
            // Services/TreeRowTooltipComposer.cs - see that class's own doc
            // comment and docs/ARCHITECTURE.md section 5's STANDING RULE.
            var extraContent = TreeRowTooltipComposer.BuildExtraTooltipContent(
                node, captionText, _host.CurrentPlan);
            var identity = ItemTooltipIdentity.ForItem(node.Name ?? "", node.IconUrl, node.Rarity);
            var getStatBlock = _getItemStatBlock;

            // Composed at HOVER time, not here: a plan restored from disk
            // fills its stat cache in the background (Q13), so a snapshot
            // taken at render time could never show what lands after it.
            // The lookup itself is a session cache read - see
            // ItemMetadataService.GetCachedStatBlock, which never fetches.
            return ItemIconTooltip.Composed(
                identity,
                () => ItemRowTooltipComposer.BuildRowContent(
                    TreeRowTooltipComposer.BuildStatTooltipContent(node, getStatBlock),
                    identity,
                    extraContent));
        }

        /// <summary>
        /// The row's icon, and the scrim a dimmed row lays over the top of
        /// it - Blish resolves a tooltip on the deepest control under the
        /// cursor, so the scrim rather than the icon beneath it is what the
        /// cursor finds. Nothing else on the row is stamped: the item's
        /// hover belongs to its picture
        /// (<see cref="ItemIconTooltip.StampOnIconTree"/>).
        /// <para>
        /// The icon is re-stamped rather than left as CreateItemIcon set
        /// it, because the in-place re-render swaps the row's NODE under a
        /// reused icon control and the old node's hover would otherwise
        /// survive the swap.
        /// </para>
        /// </summary>
        private static void StampRowIcon(Panel iconFrame, Panel iconScrim, ItemIconTooltip hover)
        {
            hover.StampOnIconTree(iconFrame);
            hover.StampOnIconOverlay(iconScrim);
        }

        // --- Decision pills ---
        //
        // PillKind/PillSpec/BuildPillSpecs (the decision -> pill mapping,
        // gw2e's multi-pill model, KNOWN-ISSUES #18) live in
        // Services/DecisionPillPlanner.cs - Blish-free and directly unit
        // tested (DecisionPillPlannerTests) - so only the actual
        // Panel/Label rendering below stays here.

        /// <summary>
        /// Whether the cursor is over one of this row's decision pills. The
        /// row panel receives every mouse event its pills do (measured -
        /// Container.TriggerMouseInput raises the container's own events
        /// before walking its children), so both the row's click handler and
        /// its press feedback have to defer to the pill under the cursor.
        /// <para>
        /// Answered from the pills' own rectangles against the live cursor
        /// (Services/TreeRowPillHitTest), NOT from Control.MouseOver, which
        /// is frozen against whatever the hover chain last resolved and is
        /// therefore wrong for every control this module builds under a
        /// stationary cursor. RelativeMousePosition is derived from the
        /// row's live AbsoluteBounds, so it reads the same layout the click
        /// was dispatched against - the pattern SortableHeaderCells already
        /// uses to pick a header cell out of its band.
        /// </para>
        /// </summary>
        private static bool CursorOverPill(Control rowPanel, List<Control> pillPanels)
        {
            if (rowPanel == null || pillPanels == null)
            {
                return false;
            }

            // Collected rather than tested in place, so the ONE fold over a
            // row's pills is the one the tests drive. Read live off the
            // controls: the resize pass moves them, so a list built once
            // and kept would be stale at the first drag tick. One small
            // list per PRESS - a user gesture, not a frame.
            var boxes = new List<TreeRowPillHitTest.PillBox>(pillPanels.Count);
            foreach (var pill in pillPanels)
            {
                boxes.Add(new TreeRowPillHitTest.PillBox(
                    pill.Location.X, pill.Location.Y, pill.Width, pill.Height));
            }

            var cursor = rowPanel.RelativeMousePosition;
            return TreeRowPillHitTest.AnyCovers(boxes, cursor.X, cursor.Y);
        }

        /// <summary>
        /// Renders the pill column into the caller's own list, which the row's
        /// expand/collapse click handler closes over to exclude pills from its
        /// hit-test. The list is REFILLED rather than replaced so an in-place
        /// refresh can rebuild a row's pills without invalidating that closure.
        /// <para>
        /// The column is as wide as the widest row needs within what the
        /// panel can spare (Services/TreePillColumnMath), but a realistic
        /// run can exceed it (a measured "HAVE n/m NEEDED" run reaches
        /// 436px) and the row has no second line. ComputePillFit then picks
        /// normal padding, else tightened, else as many tightened pills as
        /// fit beside a trailing "+N".
        /// </para>
        /// <para>
        /// The budget is width-INVARIANT for the plan's life: both endpoints
        /// move together and the column settles once per render, which is why
        /// the fit is resolved at build time and resize only repositions.
        /// docs/ARCHITECTURE.md section V.33.
        /// </para>
        /// </summary>
        private void RenderDecisionPills(
            TreeRowHandle handle, CraftingTreeNode node,
            int pillColX, int actionButtonX, int pillY, bool dimmed)
        {
            var rowPanel = handle.RowPanel;
            var pillPanels = handle.Pills;
            var pillOffsets = handle.PillOffsets;

            // Plan-scope currency facts
            // for the new HAVE/TOTAL pill - see PlanViewModel.
            // CurrencyPlanTotals/OwnedCurrencyAmounts' own doc comments.
            var plan = _host.CurrentPlan;
            var specs = DecisionPillPlanner.BuildPillSpecs(
                node, plan?.CurrencyPlanTotals, plan?.OwnedCurrencyAmounts,
                plan?.OwnedStockDrawnItemIds);
            var font = UiFonts.Caption;
            pillPanels.Clear();
            pillOffsets.Clear();
            int x = pillColX;

            int maxRightEdge = pillColX + handle.PillColumnWidth - TreePillColumnMath.TrailingClearance;

            // The ignore button is not in this column: it draws in the
            // row's own action column, at actionButtonX. The run therefore
            // gets the whole decision column.
            int leadingCount = FlowedPillCount(specs);
            int anchoredIndex = leadingCount < specs.Count ? specs.Count - 1 : -1;
            int keyWidth = ReservedIgnorePillWidth();

            Func<string, int> measure = text => (int)Math.Ceiling(font.MeasureString(text).Width);
            var pillWidths = new List<int>(leadingCount);
            for (int specIndex = 0; specIndex < leadingCount; specIndex++)
            {
                pillWidths.Add(MeasuredPillWidth(specs[specIndex], measure, keyWidth));
            }

            var fit = PlanRelayoutMath.ComputePillFit(
                pillWidths, PillPadding - TightPillPadding, PillGap, pillColX,
                maxRightEdge, MeasureOverflowPillWidth);

            int chosenPadding = PillPadding - fit.WidthReduction;

            // Where every pill goes, decided before any is built: the
            // flowed run, then the ignore button in its own column. The
            // overflow chip is rendered after the loop, from the x the run
            // left.
            var placements = new List<PillPlacement>(fit.VisibleCount + 1);
            for (int specIndex = 0; specIndex < fit.VisibleCount; specIndex++)
            {
                int width = PlanRelayoutMath.ReducedWidth(pillWidths[specIndex], fit.WidthReduction);
                placements.Add(new PillPlacement(specIndex, x, width, width - chosenPadding));
                x += width + PillGap;
            }

            // Right edge of this row's flowed run, including the "+N" chip
            // the loop below draws from the same x - the ink the "Source"
            // header centres over.
            int runRightEdge = pillColX;
            if (fit.VisibleCount > 0)
            {
                runRightEdge = x - PillGap;
            }

            if (fit.HiddenCount > 0)
            {
                runRightEdge = x + fit.OverflowPillWidth;
            }

            if (anchoredIndex >= 0)
            {
                placements.Add(new PillPlacement(anchoredIndex, actionButtonX, keyWidth, 0));
            }

            foreach (var placement in placements)
            {
                var spec = specs[placement.SpecIndex];
                int pillWidth = placement.Width;
                int textWidth = placement.TextWidth;

                PillColors.GetPillColors(spec.Kind, node.IsIgnored, out Color borderColor, out Color fillColor);

                bool isToggle = spec.Kind == PillKind.Ignore;
                Control outer;
                Panel inner = null;
                Label label = null;
                CloseKeyButton toggle = null;
                if (isToggle)
                {
                    // The toggle is Blish's own window close control, not a
                    // pill panel and not a plate carrying a mark
                    // (Views/Rendering/CloseKeyButton). It fills its slot
                    // exactly, so the anchored rectangle below IS the hit
                    // target in both states.
                    toggle = new CloseKeyButton
                    {
                        // The width comes from ReservedIgnorePillWidth and
                        // the height from the same metrics, because the two
                        // axes of this control differ and PillHeight is
                        // neither of them.
                        Size = new Point(placement.Width, GlyphButtonMetrics.RowActionHeight),
                        Location = new Point(placement.X, pillY),
                        Parent = rowPanel,
                    };
                    if (node.IsIgnored)
                    {
                        toggle.Tint = fillColor;
                    }

                    // No wash of its own on a dimmed row. ignoreInteractive
                    // below requires !dimmed, so a dimmed row's toggle is
                    // always disabled, and CloseKeyButton fades a disabled
                    // key by exactly PillColors.DimmedPillFactor - the pills
                    // beside it get the same fraction, and a second wash
                    // here would multiply the two. Dimmed, a plain key's
                    // mark reads 6.28:1 against its own plate and an ignored
                    // key's reads 2.34:1 - the art's ratios rather than
                    // colours anyone here picked: docs/KNOWN-ISSUES.md,
                    // DEFERRED, "Dimmed IGNORE toggle's mark".
                    outer = toggle;
                }
                else
                {
                    // White, not borderColor: Selected/Available fills expose the
                    // border hue behind the label, so border-colored text has zero
                    // contrast against its own backdrop.
                    Color textColor = Color.White;
                    // Chrome (UNKNOWN/UNRECOGNIZED/CURRENCY/GUILD UPGRADE/the
                    // sole-source badge) reads one tier below a pill you can
                    // act on, matching the recessed ring PillColors gives it.
                    if (PillColors.IsNonInteractiveChrome(spec.Kind))
                    {
                        textColor *= PillColors.NonInteractiveTextAlpha;
                    }

                    if (dimmed)
                    {
                        // PillColors.DimmedPillFactor, not the 0.35 this row's
                        // name/quantity/cost use - see that constant's own doc
                        // comment for why a pill needs a higher floor than the
                        // text around it.
                        borderColor *= PillColors.DimmedPillFactor;
                        fillColor *= PillColors.DimmedPillFactor;
                        textColor *= PillColors.DimmedPillFactor;
                    }

                    outer = CreatePillPanel(
                        rowPanel, spec.Text, font, pillWidth, textWidth, placement.X, pillY, PillLabelY,
                        borderColor, fillColor, textColor, out inner, out label);
                }

                // The dimmed-only difference between this and the two flags
                // below is exactly what the dead-click tooltip at the
                // bottom of this loop has to explain.
                bool clickableWhenActive = DecisionPillPlanner.IsInteractive(spec);
                bool interactive = !dimmed && spec.Source.HasValue && _resolveOverridesSync != null;
                bool ignoreInteractive = !dimmed && spec.Kind == PillKind.Ignore && _resolveOverridesSync != null;

                // The pill went inert on a dimmed row by never being wired;
                // a button is born armed, so Enabled is the switch: Blish
                // gates Click on it and PressFeedback.Wire checks it before
                // dimming or sounding. The hover state goes with it, because
                // an inert key that lights under the cursor is a promise it
                // will not keep.
                if (toggle != null && !ignoreInteractive)
                {
                    toggle.Enabled = false;
                }

                // Built outside the interactive arm below: a decisively-
                // losing pill owes the reader its "why it loses" text
                // whether or not this row's clicks are wired, and a dimmed
                // row's pills are exactly the ones that are not. Pure text
                // derived from the spec, so it costs nothing to resolve
                // here and null for every other kind.
                TooltipContent subduingContent = spec.Kind == PillKind.Subdued
                    ? PillSubduingTooltipBuilder.BuildContent(
                        spec.SubduingResult, plan?.ItemMetadata, plan?.CurrencyMetadata)
                    : null;

                // The pill's head prose, and whether the subduing block
                // belongs after it. Both are pure text decisions, so both
                // live in the Blish-free PillTooltipTextComposer (see
                // CONTRIBUTING.md's STANDING RULE); only the
                // click wiring below and the rich composition at the bottom
                // of this loop need a control. The head is stamped onto
                // outer/inner/label rather than outer alone because the
                // inner fill panel and its label cover almost the whole
                // pill, so a tooltip on outer alone is swallowed by
                // whichever child is under the cursor (labels capture
                // mouse). Click/MouseEntered/MouseLeft stay on outer only -
                // unlike tooltip lookup, those already work correctly.
                var headPlan = PillTooltipTextComposer.Compose(
                    spec, node, interactive, ignoreInteractive,
                    plan?.CurrencyPlanTotals, plan?.OwnedCurrencyAmounts);
                string tooltipText = headPlan.Text;
                bool appendSubduing = headPlan.AppendSubduing && subduingContent != null;

                if (interactive)
                {
                    var source = spec.Source.Value;
                    int anchorNodeId = node.NodeId;
                    outer.Click += (_, __) =>
                    {
                        _nodeOverrides[anchorNodeId] = source;
                        ApplyOverridesAndResolve(anchorNodeId: anchorNodeId);
                    };
                    Color restingBorder = borderColor;
                    outer.MouseEntered += (_, __) => outer.BackgroundColor = Color.White;
                    outer.MouseLeft += (_, __) => outer.BackgroundColor = restingBorder;
                    PressFeedback.Wire(outer);
                }
                else if (ignoreInteractive)
                {
                    // Toggles this ITEM id (not just this node) in
                    // or out of _ignoredItemIds, matching gw2e's own
                    // tree-wide-by-item-id "Ignore" semantics.
                    int itemId = node.ItemId;
                    int anchorNodeId = node.NodeId;
                    outer.Click += (_, __) =>
                    {
                        if (!_ignoredItemIds.Remove(itemId))
                        {
                            _ignoredItemIds.Add(itemId);
                        }

                        ApplyOverridesAndResolve(anchorNodeId: anchorNodeId);
                    };

                    // No hand-rolled wash-and-restore here: ignoreInteractive
                    // implies PillKind.Ignore, so this arm only ever holds the
                    // toggle, whose press came from CloseKeyButton's own
                    // PressFeedback wiring and whose hover is the texture
                    // swap. The pill arm above keeps the pair.
                }

                // Appends the value-detail
                // hover (real gold vs. decision-only optimization price,
                // plus a vendor cap line when applicable) onto the
                // committed CRAFT/VENDOR pill's existing tooltip, only when
                // ValueDetailTooltipBuilder finds a real divergence -
                // Selected (multi-option winner) and Locked (sole option)
                // are the only two kinds a committed CRAFT/VENDOR pill can
                // ever have (see BuildPillSpecs' own "the selected pill
                // always matches node.Decision" guarantee), so gating on
                // node.Decision here (rather than re-checking spec.Text)
                // cannot accidentally attach this to an unrelated pill -
                // every other Kind's node.Decision is never Craft/
                // BuyFromVendor (Currency/GuildUpgrade/Have/etc. all use
                // their own distinct CraftingDecision values).
                TooltipContent valueDetailContent = null;
                if ((spec.Kind == PillKind.Selected || spec.Kind == PillKind.Locked) &&
                    (node.Decision == CraftingDecision.Craft || node.Decision == CraftingDecision.BuyFromVendor))
                {
                    ValueDetailTooltipBuilder.TryBuildContent(
                        node, plan?.VendorCapsByItemId, out valueDetailContent);
                }

                // A dimmed row's would-be-clickable pills are inert: the
                // reference branch under a bought item is a "what it would
                // cost to craft instead" preview, not a live decision, so
                // the click handlers above are never wired. They still draw
                // a full pill set, so the only honest thing left is to say
                // why the click did nothing and what to change to make it
                // work. Appended, never assigned over: a dimmed Subdued
                // pill carries its "why it loses" text (resolved in its own
                // arm above, which exists precisely because the interactive
                // arm never runs on a dimmed row), and a dimmed committed
                // pill can carry the value-detail hover - neither may be
                // clobbered.
                //
                // Composed as CONTENT, not by string concatenation: the
                // value-detail block and a Weighted pill's margin both
                // carry gold figures the rich surface draws with coin
                // icons, and a "\n\n" join would have flattened them back
                // into text. Separator() is the blank line that join used
                // to produce, and is a no-op when nothing precedes it -
                // which is what the old "tooltipText == null ? x : y"
                // ternaries were doing by hand.
                var pillTooltip = new TooltipContentBuilder();
                pillTooltip.Text(tooltipText);
                if (appendSubduing)
                {
                    pillTooltip.Separator().Append(subduingContent);
                }

                if (valueDetailContent != null)
                {
                    pillTooltip.Separator().Append(valueDetailContent);
                }

                if (dimmed && clickableWhenActive)
                {
                    pillTooltip.Separator().Text(DimmedPillTooltip);
                }

                // All three controls point at the ONE shared rich surface
                // - see TooltipFacility for why there is one instance for
                // the whole module rather than one per tooltip'd control.
                var pillContent = pillTooltip.Build();
                if (!pillContent.IsEmpty)
                {
                    TooltipFacility.ApplyRich(outer, pillContent);
                    if (inner != null)
                    {
                        // The toggle has no children to fan out to; a pill's
                        // inner fill and label cover almost the whole pill,
                        // so a tooltip on outer alone is swallowed by
                        // whichever child is under the cursor.
                        TooltipFacility.ApplyRich(inner, pillContent);
                        TooltipFacility.ApplyRich(label, pillContent);
                    }
                }

                pillPanels.Add(outer);
                pillOffsets.Add(placement.X - pillColX);
            }

            if (fit.HiddenCount > 0)
            {
                pillPanels.Add(
                    RenderOverflowPill(rowPanel, specs, fit, font, x, pillY, dimmed));
                pillOffsets.Add(x - pillColX);
            }

            NoteSourceHeaderInk(TreePillRunLayout.HeaderInkWidth(pillColX, runRightEdge));
        }

        /// <summary>
        /// Records a freshly built row's pill-run width and re-places the
        /// "Source" header when it widens the plan's high-water mark (see
        /// <see cref="_sourceHeaderInkWidth"/>). Called from the ONE place
        /// every row's pills are laid out, so a lazy expand, an Expand All
        /// and an in-place refresh all reach it without each remembering
        /// to.
        /// <para>
        /// Bounded work: only a strict increase re-places anything, and
        /// the widths are monotone, so a whole tree costs at most a
        /// handful of header moves. The re-place is the header row's own
        /// relayout closure, which is position-only by contract
        /// (ISectionRelayoutSink.AddRelayout).
        /// </para>
        /// </summary>
        private void NoteSourceHeaderInk(int inkRunWidth)
        {
            if (inkRunWidth <= _sourceHeaderInkWidth)
            {
                return;
            }

            _sourceHeaderInkWidth = inkRunWidth;

            // A width of 0 is the "no content panel" answer
            // (CraftingPlanView.GetCurrentPanelWidth); placing the header
            // off that would strand it until the next resize, and the
            // field above is already updated for whoever asks next.
            int panelWidth = _host.PanelWidth;
            if (panelWidth > 0)
            {
                _treeHeaderRelayout?.Invoke(panelWidth);
            }
        }

        /// <summary>
        /// One pill's resolved geometry: which spec it draws, where it
        /// sits, and how wide its slot and its text are. Text width is
        /// carried separately because a pill's slot includes padding the
        /// label must not count; the IGNORE toggle carries none, since
        /// its FeedbackButton centres its own mark.
        /// </summary>
        private readonly struct PillPlacement
        {
            internal readonly int SpecIndex;
            internal readonly int X;
            internal readonly int Width;
            internal readonly int TextWidth;

            internal PillPlacement(int specIndex, int x, int width, int textWidth)
            {
                SpecIndex = specIndex;
                X = x;
                Width = width;
                TextWidth = textWidth;
            }
        }

        /// <summary>
        /// How many of <paramref name="specs"/> flow in the pill column:
        /// all of them, less a TRAILING ignore button, which draws in the
        /// row's own action column instead. Only a trailing one is taken
        /// out - that is where DecisionPillPlanner emits it, and removing
        /// one from the middle would leave the "+N" chip naming the wrong
        /// entries, which index the run by position.
        /// </summary>
        private static int FlowedPillCount(IReadOnlyList<PillSpec> specs)
        {
            return specs.Count > 0 && specs[specs.Count - 1].Kind == PillKind.Ignore
                ? specs.Count - 1
                : specs.Count;
        }

        /// <summary>
        /// One entry's slot width. The ignore button draws no text in
        /// either state, so it answers from its own control width rather
        /// than from a word; only a button left in the run by
        /// <see cref="FlowedPillCount"/> ever reaches that arm.
        /// <para>
        /// Takes the measurement as a delegate rather than a font because
        /// its two callers need different ones: a row builds one closure
        /// over its own face, and the column pre-scan memoises across a
        /// whole tree (ScannedPillColumn).
        /// </para>
        /// </summary>
        private static int MeasuredPillWidth(PillSpec spec, Func<string, int> measureText, int keyWidth)
        {
            return spec.Kind == PillKind.Ignore
                ? keyWidth
                : measureText(spec.Text) + PillPadding;
        }

        /// <summary>
        /// The ignore button's width, which is the control itself:
        /// Blish's own window close control at its measured box
        /// (Services/GlyphButtonMetrics). Nothing is padded on top of that
        /// edge, because padding outside a filled control is dead space the
        /// row's expand/collapse click would answer.
        /// <para>
        /// The width is state-independent by construction: the button draws
        /// the same mark plain or amber (CloseKeyButton.Tint), so ignoring
        /// an item re-renders it at the size it was clicked at, in the
        /// column it was clicked in.
        /// </para>
        /// </summary>
        private static int ReservedIgnorePillWidth()
        {
            return PlanRelayoutMath.TreeActionColumnWidth;
        }

        private const int PillLabelY = 2;

        /// <summary>
        /// The trailing "+N" pill: the row admitting that N of its pills did not
        /// fit, instead of the column simply ending early. Styled as
        /// non-interactive chrome, because it is - clicking it does nothing, and
        /// its tooltip names what is missing.
        /// <para>
        /// The tooltip must not suggest widening the window: the pill
        /// column's width is settled once per render (see
        /// ScannedPillColumn), so dragging the window wider does not
        /// move this chip and the advice would be false until the next
        /// Generate.
        /// </para>
        /// Why it is not wired to a popup offering the hidden options:
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        private static Panel RenderOverflowPill(
            Panel rowPanel, IReadOnlyList<PillSpec> specs, PlanRelayoutMath.PillFitPlan fit,
            BitmapFont font, int x, int pillY, bool dimmed)
        {
            string text = OverflowPillText(fit.HiddenCount);
            int textWidth = (int)System.Math.Ceiling(font.MeasureString(text).Width);

            PillColors.GetPillColors(PillKind.Locked, false, out Color borderColor, out Color fillColor);
            Color textColor = Color.White * PillColors.NonInteractiveTextAlpha;
            if (dimmed)
            {
                borderColor *= PillColors.DimmedPillFactor;
                fillColor *= PillColors.DimmedPillFactor;
                textColor *= PillColors.DimmedPillFactor;
            }

            // Bounded by the fit, not by specs.Count: the IGNORE key is
            // the last spec and was never part of the run this fit was
            // computed over, so it is not one of the hidden ones.
            var hiddenTexts = new List<string>(fit.HiddenCount);
            for (int i = fit.VisibleCount; i < fit.VisibleCount + fit.HiddenCount; i++)
            {
                hiddenTexts.Add(specs[i].Text);
            }

            string tooltipText = $"No room to show: {string.Join(", ", hiddenTexts)}";

            var outer = CreatePillPanel(
                rowPanel, text, font, fit.OverflowPillWidth, textWidth, x, pillY, PillLabelY,
                borderColor, fillColor, textColor, out Panel inner, out Label label);

            TooltipFacility.ApplyPlain(outer, tooltipText);
            TooltipFacility.ApplyPlain(inner, tooltipText);
            TooltipFacility.ApplyPlain(label, tooltipText);

            return outer;
        }

        private static string OverflowPillText(int hiddenCount)
        {
            return "+" + hiddenCount;
        }

        /// <summary>
        /// Width of the "+N" pill. A method group, not a lambda over the
        /// row's font local: RenderDecisionPills runs once per tree row, so
        /// a capturing closure would be one allocation per row on the
        /// render path for a callback most rows never invoke.
        /// </summary>
        private static int MeasureOverflowPillWidth(int hiddenCount)
        {
            var font = UiFonts.Caption;
            return (int)System.Math.Ceiling(
                font.MeasureString(OverflowPillText(hiddenCount)).Width) + TightPillPadding;
        }

        /// <summary>
        /// One pill's three nested controls (border panel, inset fill
        /// panel, centered label) - shared by the decision pills and the
        /// trailing "+N" pill so the two can never disagree about pill
        /// chrome. Border simulated as an outer colored panel with a
        /// 1px-inset fill panel, the same nesting technique
        /// IconControls.CreateItemIcon uses.
        /// </summary>
        private static Panel CreatePillPanel(
            Panel rowPanel, string text, BitmapFont font, int pillWidth, int textWidth,
            int x, int pillY, int labelY, Color borderColor, Color fillColor, Color textColor,
            out Panel inner, out Label label)
        {
            var outer = new ClippedPanel()
            {
                Size = new Point(pillWidth, PillHeight),
                Location = new Point(x, pillY),
                BackgroundColor = borderColor,
                Parent = rowPanel,
            };
            inner = new ClippedPanel()
            {
                Size = new Point(pillWidth - 2, PillHeight - 2),
                Location = new Point(1, 1),
                BackgroundColor = fillColor,
                Parent = outer,
            };
            // Clamped: a decision pill's width is its text plus padding, so
            // the offset is always positive there, but the "+N" pill's
            // width was reserved before its final N was known - a
            // digit-count change would otherwise start its label left of
            // its own pill.
            int labelX = (pillWidth - 2 - textWidth) / 2;
            if (labelX < 0)
            {
                labelX = 0;
            }

            label = new Label()
            {
                Text = text,
                Font = font,
                TextColor = textColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(labelX, labelY),
                Parent = inner,
            };
            return outer;
        }
    }
}
