using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure content-height arithmetic (Blish-free, unit-testable) for the
    /// plan view's collapsible section bodies and recipe-tree child
    /// containers. Every row height in the plan view is a fixed constant, so
    /// the total height of any section body or tree subtree is knowable
    /// synchronously from row counts/types and expansion state alone, with
    /// no need to wait for Blish layout to converge.
    /// <para>
    /// Still true after Plan Notes learned to wrap: a wrapped note renders
    /// one FallbackTextRowHeight row per LINE, so only the row COUNT became
    /// width-dependent, which is why Notes is sized by
    /// NotesSectionLayoutMath and its renderer rather than by
    /// SectionBodyHeight below - the same split Summary already has.
    /// </para>
    /// <para>
    /// CraftingPlanView uses these SAME constants both to size the
    /// AutoSize-replacement containers and to size the individual row Panels
    /// it creates, so the two cannot drift. See docs/ARCHITECTURE.md section 4.2.
    /// </para>
    /// </summary>
    internal static class PlanContentHeightMath
    {
        // --- Section row-height constants (mirrored 1:1 by CraftingPlanView's
        // row builders - CreateUsedMaterialRow, CreateShoppingRow, etc. use
        // these same constants rather than their own local copies). ---
        //
        // Every height below was re-derived against the +2pt body font
        // (Views/Rendering/UiFonts): measured Menomonia line heights are 13
        // at 12pt, 18 at 14pt and 20 at 16pt, and the lowest ASCII ink sits
        // 1px past the line box at 14pt and 16pt, 3px past it at 12pt. A
        // row keeps its height only where that ink still clears whatever
        // sits under it (the 2px divider, or the row's own bottom edge).
        //
        // The icon-led rows are ICON-driven, not text-driven: a
        // RowIconFrameSize rarity frame plus a 2px divider already exceeds
        // the tallest text run in them, so their heights are derived below
        // from the frame rather than pinned by ink arithmetic.
        // The craft-step row's body text was already Font16 before the
        // bump, so only its Font12 -> Font14 sublabel moved, and that
        // sublabel's ink clears its divider with the same margin it had at
        // the pre-tier-2 heights. The two 28px rows put a single line at
        // y=4 (ink 25) and y=7 (ink 26) with no divider beneath either.

        // --- Tier-2 icon-row geometry: every
        // row-level item icon in the Crafting Plan tab renders at the
        // in-game bag-SIDEBAR size, ItemIconTiers.BagSidebarIconSize. ---

        /// <summary>Rarity-frame border of a plan-tab row icon - the
        /// borderThickness the row builders pass to
        /// IconControls.CreateItemIcon.</summary>
        public const int RowIconBorder = 1;

        /// <summary>Overall edge of a plan-tab row's rarity-framed icon:
        /// tier-2 art plus the frame on both sides (42).</summary>
        public const int RowIconFrameSize =
            ItemIconTiers.BagSidebarIconSize + 2 * RowIconBorder;

        /// <summary>Height of the 2px row divider quad
        /// LabelHelpers.CreateRowDivider draws (2, never 1 - see that
        /// method's scissor-defect derivation).</summary>
        public const int RowDividerHeight = 2;

        /// <summary>
        /// bottomClearance every icon-led table row passes to
        /// LabelHelpers.CreateRowDivider. 1, not 0: the tier-2 flush-fit
        /// heights (RowIconFrameSize + RowDividerHeight = 44) are in the
        /// Container.Paint round-trip defect's vulnerable class, so the
        /// clearance pixel is part of the height derivation itself, and
        /// rowHeight absorbs it. Proven immune at every GW2 UI scale by the
        /// executable re-derivation in
        /// tests/.../RowDividerScissorSimulationTests.cs.
        /// </summary>
        public const int IconRowDividerClearance = 1;

        /// <summary>
        /// Clear space an icon-led list row keeps between its icon frame
        /// and the divider rule on either side of it. Reported in game: the
        /// rows were flush, so the frame's bottom border and the rule's top
        /// edge shared a y and the rule read as drawing over the icon.
        /// </summary>
        public const int IconRowFramePadding = 3;

        /// <summary>
        /// y of the icon frame in an icon-led list row. One less than the
        /// padding, because the row above ends on its clearance pixel: the
        /// gap a reader sees over the frame is this plus that pixel.
        /// </summary>
        public const int IconRowIconY = IconRowFramePadding - 1;

        // Padded fit: the frame sits at IconRowIconY, the divider lands at
        // rowHeight - RowDividerHeight - IconRowDividerClearance, and the
        // terms above put exactly IconRowFramePadding between the two.
        // 2 + 42 + 3 + 2 + 1 = 50.
        public const int IconLedRowHeight =
            IconRowIconY + RowIconFrameSize + IconRowFramePadding
            + RowDividerHeight + IconRowDividerClearance;

        public const int UsedMaterialRowHeight = IconLedRowHeight;
        public const int ShoppingRowHeight = IconLedRowHeight;
        public const int CraftStepRowHeight = IconLedRowHeight;

        /// <summary>
        /// Height of the band a reader sees in an icon-led row (47). The
        /// row draws its rule over its own last RowDividerHeight plus
        /// IconRowDividerClearance pixels, and none of those are space.
        /// Centre a control on this rather than on IconLedRowHeight.
        /// Centring on the full height counts the rule as visible space
        /// and sets the control too low.
        /// </summary>
        public const int IconLedRowVisibleHeight =
            IconLedRowHeight - RowDividerHeight - IconRowDividerClearance;

        /// <summary>Edge of the numbered badge a Crafting Steps row draws
        /// to the left of its icon.</summary>
        public const int CraftStepBadgeSize = 36;

        /// <summary>y of that badge. Centred on the band, which puts the
        /// same gap above it and below it.</summary>
        public const int CraftStepBadgeY =
            (IconLedRowVisibleHeight - CraftStepBadgeSize) / 2;

        // 32, not the 28 a Body-16 header band needed: column headers moved
        // to the ColumnHeader tier (TypeRampMetrics.ColumnHeaderInk), whose
        // lowest ink is 26 rather than 21. ColumnHeaderLabelY 4 reproduces
        // the band the 16pt header drew - cap top 8px down, ink bottom 2px
        // clear of the band's own bottom edge - at the taller font.
        public const int ColumnHeaderRowHeight = 32;

        // Baseline y of every column-header label inside that band. Lives
        // here rather than with the chrome that draws it
        // (Views/Rendering/HeaderBands, which aliases this) because it
        // is half of the arithmetic above: a label y and a band height that
        // move independently are how a header's descenders end up on the
        // row under them.
        public const int ColumnHeaderLabelY = 4;

        // --- Section header band (drawn by CraftingPlanView.
        // CreateSectionHeader, which aliases all three). ---

        // 38, not the 32 an 18pt title needed: section titles moved to the
        // SectionTitle tier (TypeRampMetrics.SectionTitleInk), lowest ink
        // 30 rather than 23. The divider is bottom-anchored at
        // height - 3, so its top is y=35 and the title's ink bottom
        // (SectionHeaderTitleY 3 + 30 = 33) clears it by 2px - the
        // clearance LabelHelpers.CreateRowDivider's scissor-defect note
        // requires, never 1px.
        public const int SectionHeaderRowHeight = 38;

        public const int SectionHeaderTitleY = 3;

        // The caret is Body, not SectionTitle - two fonts on one reading
        // line, so it is BASELINE-aligned to the title rather than
        // top-aligned or centred in the band, with the same 1px optical
        // lift the 18pt header gave it. Ink bottom 10 + 21 = 31, also clear
        // of the divider top at 35.
        public const int SectionHeaderCaretY = 10;

        // --- Tab title band (one per tab, drawn by
        // Views/Rendering/HeaderBands.CreateTabTitleBand, which aliases
        // both constants below). ---

        // 44. Blish_HUD.Controls.Panel draws a 36px band for a Panel.Title
        // and prints it at DefaultFont16; that height, that font and the
        // band's text bounds are literals inside Panel.RecalculateLayout
        // and Panel.PaintBeforeChildren, and every layout rectangle they
        // write is private, so the band can be neither retargeted nor
        // subclassed - the module draws its own instead. Sized for the
        // SectionTitle tier: that ink block spans TabTitleY + capTopY 4 to
        // TabTitleY + lowestInk 30, i.e. 26px, and centring 26 in 44 leaves
        // 9px of air above and below. It also seats a UiMetrics.ButtonHeight
        // control - the Snapshot tab's two actions ride this band.
        public const int TabTitleBandHeight = 44;

        // Top y of the tab title label inside that band: (44 - 26) / 2 - 4.
        // The two are one piece of arithmetic, for the reason
        // ColumnHeaderLabelY gives above.
        public const int TabTitleY = 5;

        // 36, not 32: this row's two labels sit at y=7 and y=9, whose
        // Font16/Font14 ink (28) landed on the 32px row's divider top (29).
        // NOT moved to the tier-2 icon-row height: this is the plan tab's
        // one text-only table row (no item icon), so the icon-size rule
        // does not reach it, and 36 is on
        // LabelHelpers.CreateRowDivider's proven-immune list.
        public const int DisciplineRowHeight = 36;

        // The same padded shape as UsedMaterialRowHeight and
        // ShoppingRowHeight.
        //
        // EVERY recipe row, since the discipline became a column
        // (Services/RecipesColumnMath) rather than a second line under the
        // name. The 48px twin this section used to need for a sublabel row
        // is gone with the sublabel.
        public const int RecipeRowHeight = IconLedRowHeight;

        // Reserved height of a formula band's amount run: the taller of the
        // amount text's own line box (Regular16, 20 - TypeRampMetrics) and
        // the coin icon beside it, which is what SummarySectionRenderer.
        // AmountBlockHeight measures at render time.
        //
        // Spelled as CoinSegmentMath.CoinIconSize alone until the wallet BAR
        // tier made the icon the SHORTER of the pair: that only ever agreed
        // with the text by coincidence, and a reserve named after the icon
        // now under-reserves by 2px.
        public const int AmountTextLineHeight = 20;
        public const int AmountRunHeight =
            CoinSegmentMath.CoinIconSize > AmountTextLineHeight
                ? CoinSegmentMath.CoinIconSize
                : AmountTextLineHeight;

        // Caption y and amount bottom pad of an UNHIGHLIGHTED formula band
        // (the profit band; a highlighted one uses
        // SummarySectionLayoutMath's box-derived pair instead). Here rather
        // than with the renderer that draws them because they are two of
        // CostTileRowHeight's five terms.
        public const int CostTileCaptionY = 4;
        public const int CostTileAmountBottomPad = 6;

        /// <summary>
        /// The ONE distance between a formula tile's caption line box and
        /// the top of the amount run under it, in BOTH Total Cost bands
        /// (the cost band's boxed tiles and the profit band's plain ones).
        /// Anchoring the amount UNDER the caption, rather than bottom-
        /// anchoring it inside a fixed row height, is what makes the gap
        /// this number at every band, so the two bands cannot drift apart.
        /// <para>
        /// 8, from the caption tier's own metrics rather than by eye: a
        /// label and the value it names read as one group only while the
        /// space between them stays under about one cap height
        /// (ColumnHeaderInk.CapHeight is 17), and reads as touching much
        /// below half of it. 8 is the 4pt-scale step at half that cap
        /// height, and leaves 7px of clear air under the caption's
        /// descenders. Derivation: docs/ARCHITECTURE.md section 4.2.
        /// </para>
        /// </summary>
        public const int CostTileLabelToValueGap = 8;

        /// <summary>
        /// Height a formula band reserves for one caption line.
        /// Deliberately above what the font measures: the renderer places
        /// the caption from real font metrics and hangs the amount below
        /// it, so a reserve under the real line box makes the band clip its
        /// own amount. 29 = ColumnHeaderInk's measured 25px line box plus
        /// one 4pt step of slack for a font Blish resolved differently
        /// than this module measured.
        /// </summary>
        public const int CostTileCaptionLineHeight = 29;

        // 67 = 4 caption y + 29 caption line + 8 label-to-value gap + a 20px
        // amount run + 6 bottom pad. Was 58 while the amount was
        // BOTTOM-anchored and the gap under the caption was whatever was
        // left (1px) - see CostTileLabelToValueGap. There is no
        // CostTileAmountY constant any more for the same reason: the band
        // hangs its run under the caption it actually measured, so the run's
        // y is not a constant of the row at all.
        //
        // AmountRunHeight, never CoinSegmentMath.CoinIconSize: the run is as
        // tall as the taller of its text and its icon, and at the 18px
        // wallet BAR tier that is the text. Naming the icon here would
        // under-reserve the row by 2px.
        //
        // This band carries no divider, so no RowDividerScissorSimulation
        // pair moves with it.
        public const int CostTileRowHeight =
            CostTileCaptionY
            + CostTileCaptionLineHeight
            + CostTileLabelToValueGap
            + AmountRunHeight
            + CostTileAmountBottomPad;

        // The Summary section's COST formula band is no longer a taller
        // twin of this row: its result tile shares the band's one amount
        // font and is highlighted with a tinted box instead, so its height
        // is the box's own geometry - Services/SummarySectionLayoutMath.
        // CostBandHeight. The profit band still uses CostTileRowHeight.
        //
        // The Summary's currency table mirrors the in-game wallet list, so
        // its row geometry is measured from the same capture as the icon it
        // carries: the list's row band is 42px with a 32px icon box centred
        // in it, i.e. 5px of clearance above and below
        // (Services/CurrencyIconTiers). Icon-plus-pad, not the old literal
        // 28, which predated the tier and could not hold a list-tier icon.
        public const int CurrencyRowIconPad = 5;
        public const int CurrencyRowHeight =
            CurrencyIconTiers.WalletListIconSize + (2 * CurrencyRowIconPad);

        public const int FallbackTextRowHeight = 28;

        /// <summary>Clearance above and below a tree row's icon frame. The
        /// tree draws indent guidelines instead of row dividers, so its
        /// height is frame-plus-breathing-room - the same 3px-each-side law
        /// the Ranker's tier-1 rows use (RankerRowLayout.RowHeight).</summary>
        public const int TreeRowIconPad = 3;

        // 42 + 3 + 3 = 48.
        public const int TreeRowHeight = RowIconFrameSize + 2 * TreeRowIconPad;

        /// <summary>
        /// Total height of a collapsible section's AutoSize-replacement
        /// content FlowPanel, computed purely from its rows (no Blish
        /// objects touched). CraftingPlanView.CreateCollapsibleSection
        /// assigns this to contentFlow.Height synchronously right after
        /// dispatching to the section's CreateXBody builder, so the
        /// container's true height is valid before the next paint - no
        /// multi-frame AutoSize convergence window remains to race.
        /// </summary>
        public static int SectionBodyHeight(PlanSectionType sectionType, IReadOnlyList<PlanRowViewModel> rows)
        {
            rows = rows ?? Array.Empty<PlanRowViewModel>();
            switch (sectionType)
            {
                // Both of these draw a ColumnHeaderRowHeight band, as the
                // two column-header tables below do. Counted
                // unconditionally, exactly as the two column-header tables
                // below are, because all four renderers emit
                // the header before looking at the row count.
                case PlanSectionType.UsedMaterials:
                    return ColumnHeaderRowHeight + rows.Count * UsedMaterialRowHeight;
                case PlanSectionType.ShoppingList:
                    return ColumnHeaderRowHeight + rows.Count * ShoppingRowHeight;
                case PlanSectionType.CraftingSteps:
                    return CraftingStepsBodyHeight(rows);
                case PlanSectionType.RequiredDisciplines:
                    return ColumnHeaderRowHeight + rows.Count * DisciplineRowHeight;
                case PlanSectionType.RequiredRecipes:
                    return ColumnHeaderRowHeight + rows.Count * RecipeRowHeight;
                default:
                    // Defensive fallback mirrors CreateCollapsibleSection's own
                    // default branch (CreateTextRow, one fixed-height row per
                    // PlanRowViewModel).
                    return rows.Count * FallbackTextRowHeight;
            }
        }

        /// <summary>
        /// A Crafting Steps section can now mix numbered
        /// CraftStep rows (CraftStepRowHeight, via
        /// Views/Rendering/CraftStepsSectionRenderer.CreateCraftStepRow) with
        /// plain TimegatedNotice info rows (FallbackTextRowHeight, via the
        /// shared Views/Rendering/TextRowRenderer.CreateTextRow helper - see
        /// Views/Rendering/CraftStepsSectionRenderer.Render),
        /// so height is summed per-row rather than assumed uniform.
        /// </summary>
        private static int CraftingStepsBodyHeight(IReadOnlyList<PlanRowViewModel> rows)
        {
            int height = 0;
            foreach (var row in rows)
            {
                height += row.RowType == PlanRowType.TimegatedNotice
                    ? FallbackTextRowHeight
                    : CraftStepRowHeight;
            }

            return height;
        }

        /// <summary>
        /// Whether a tree node is expanded: an explicit user override
        /// (persisted per NodeId across local re-solves) wins; otherwise
        /// falls back to the same default CraftingPlanView.RenderTreeNode
        /// has always used - non-dimmed (real crafting-path) nodes default
        /// open through depth 1, dimmed reference branches always default
        /// closed. Both the view's row builder and this height arithmetic
        /// call this same method so the two can never disagree about which
        /// nodes are expanded.
        /// </summary>
        public static bool IsNodeExpanded(
            int nodeId, int depth, bool dimmed, IReadOnlyDictionary<int, bool> expansionOverrides)
        {
            if (expansionOverrides != null && expansionOverrides.TryGetValue(nodeId, out bool overrideValue))
            {
                return overrideValue;
            }

            return !dimmed && depth < 2;
        }

        /// <summary>
        /// Total height of one tree node's own row plus (if expanded) every
        /// descendant currently expanded beneath it - the value
        /// CraftingPlanView assigns to the Recipe Tree section's top-level
        /// contentFlow.Height (node = TreeRoot, depth = 0, dimmed = false).
        /// </summary>
        public static int TreeNodeHeight(
            CraftingTreeNode node, int depth, bool dimmed, IReadOnlyDictionary<int, bool> expansionOverrides)
        {
            if (node == null)
            {
                return 0;
            }

            return TreeRowHeight + TreeChildFlowHeight(node, depth, dimmed, expansionOverrides);
        }

        /// <summary>
        /// Total height of a single node's OWN childFlow container - the
        /// sum of every child's TreeNodeHeight, or 0 when the node has no
        /// children or is not currently expanded. This is exactly what
        /// CraftingPlanView.RenderTreeNode's childFlow.Height must be set
        /// to right after (lazily or eagerly) populating it.
        /// </summary>
        public static int TreeChildFlowHeight(
            CraftingTreeNode node, int depth, bool dimmed, IReadOnlyDictionary<int, bool> expansionOverrides)
        {
            if (node?.Children == null || node.Children.Count == 0)
            {
                return 0;
            }

            if (!IsNodeExpanded(node.NodeId, depth, dimmed, expansionOverrides))
            {
                return 0;
            }

            bool childDimmed = dimmed || node.Decision != CraftingDecision.Craft;
            return ChildrenHeight(node.Children, depth + 1, childDimmed, expansionOverrides);
        }

        /// <summary>
        /// Sum of TreeNodeHeight over a set of sibling nodes that share the
        /// same depth and dimmed flag. Exposed separately from
        /// TreeChildFlowHeight so a toggle handler that already has a
        /// node's own ChildDimmed cached (RenderTreeNode computes it once
        /// per node and stores it on TreeNodeState) can recompute that
        /// node's childFlow height directly, without re-deriving dimmed
        /// state from scratch.
        /// </summary>
        public static int ChildrenHeight(
            IReadOnlyList<CraftingTreeNode> children, int childDepth, bool childDimmed,
            IReadOnlyDictionary<int, bool> expansionOverrides)
        {
            if (children == null)
            {
                return 0;
            }

            int total = 0;
            foreach (var child in children)
            {
                total += TreeNodeHeight(child, childDepth, childDimmed, expansionOverrides);
            }

            return total;
        }

        // Thin visual gap
        // CraftingPlanView draws between two consecutive top-level trees in
        // a multi-item batch, so N stacked full item trees read as N
        // distinct blocks rather than blending into one continuous list of
        // rows. Never inserted for a single root (roots.Count == 1), which
        // is what keeps that case byte-identical to the single-item height.
        public const int MultiRootDividerHeight = 12;

        /// <summary>
        /// Total height of the Recipe Tree section's single shared content
        /// FlowPanel when it holds N top-level trees stacked rather than
        /// one: the same per-root arithmetic TreeNodeHeight does, summed
        /// across every root, plus one MultiRootDividerHeight gap between
        /// each pair of consecutive roots - never before the first or after
        /// the last, so a one-element list has no divider at all.
        /// <para>
        /// One ColumnHeaderRowHeight column header precedes the roots
        /// whenever there is a tree at all
        /// (TreeSectionController.CreateTreeSection builds it into the same
        /// FlowPanel), so a single-item plan differs from
        /// TreeNodeHeight(roots[0], 0, false, ...) by exactly that header.
        /// An empty/absent roots list renders no tree and therefore no
        /// header either, and still measures 0.
        /// </para>
        /// <para>Derivation: docs/ARCHITECTURE.md section 4.2.</para>
        /// </summary>
        public static int MultiRootTreeFlowHeight(
            IReadOnlyList<CraftingTreeNode> roots, IReadOnlyDictionary<int, bool> expansionOverrides)
        {
            if (roots == null || roots.Count == 0)
            {
                return 0;
            }

            int total = ColumnHeaderRowHeight;
            for (int i = 0; i < roots.Count; i++)
            {
                if (i > 0)
                {
                    total += MultiRootDividerHeight;
                }

                total += TreeNodeHeight(roots[i], 0, false, expansionOverrides);
            }

            return total;
        }
    }
}
