using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure width-dependent layout arithmetic (Blish-free, unit-testable)
    /// shared by CraftingPlanView's initial section builders and its
    /// live in-place resize relayout registry. Every formula here is
    /// called from BOTH the build path (CreateX... methods) and the
    /// relayout/re-ellipsis closures registered alongside them, so the two
    /// paths cannot drift apart - mirrors ShoppingColumnMath's "one source
    /// of truth" shape for the remaining width-dependent geometry m2's
    /// resize-path research identified (tree column anchors, cost-tile
    /// geometry, generic centering/right-alignment/name-column budgeting).
    /// <para>See docs/ARCHITECTURE.md section 4.</para>
    /// </summary>
    internal static class PlanRelayoutMath
    {
        /// <summary>
        /// Left edge that centers a contentWidth-wide block inside a
        /// containerWidth-wide space, clamped to never go negative (a block
        /// wider than its container starts flush left rather than at a
        /// negative x). Used by the plan header's title centering and the
        /// cost-tile row's own row centering.
        /// </summary>
        public static int CenterX(int containerWidth, int contentWidth)
        {
            int x = (containerWidth - contentWidth) / 2;
            return x > 0 ? x : 0;
        }

        /// <summary>
        /// Left edge that right-aligns a width-wide control to rightEdge.
        /// </summary>
        public static int RightAlignedX(int rightEdge, int width)
        {
            return rightEdge - width;
        }

        /// <summary>
        /// Max width available for a name label sitting before a fixed-
        /// width trailing column (e.g. a right-aligned quantity), reserving
        /// gapBeforeColumn px between the name and that column and starting
        /// at nameX. Clamped to a 20px floor so a very narrow panel never
        /// yields a negative/zero ellipsis width. Shared by the Used
        /// Materials and Shopping List name columns - both use exactly this
        /// shape, just with a different columnRightXBeforeGap
        /// (panelWidth-8 vs. the shopping edges' QtyRightEdge).
        /// </summary>
        public static int NameMaxWidthBeforeColumn(
            int columnRightXBeforeGap, int trailingColumnWidth, int gapBeforeColumn, int nameX)
        {
            int width = columnRightXBeforeGap - trailingColumnWidth - gapBeforeColumn - nameX;
            return width > 20 ? width : 20;
        }

        /// <summary>
        /// Boundary between a flexing name column's HEADER cell and the band
        /// pinned to its right: <see cref="NameMaxWidthBeforeColumn"/>'s own
        /// three terms with the gap split, because a boundary derived from
        /// the header WORDS sits far left of it (HeaderCellMath.LabelExtent).
        /// </summary>
        public static int HeaderSplitBeforeColumn(
            int columnRightXBeforeGap, int trailingColumnWidth, int gapBeforeColumn)
        {
            return columnRightXBeforeGap - trailingColumnWidth - (gapBeforeColumn / 2);
        }

        /// <summary>
        /// Gap every plan table keeps between its right-hand block and the
        /// panel's right edge.
        /// </summary>
        public const int TableRightMargin = UiSpacing.SectionRightPad;

        /// <summary>
        /// Right edge of every plan table's right-hand block, at every
        /// panel width: the panel edge less
        /// <see cref="TableRightMargin"/>, and nothing else.
        /// <para>
        /// The invariant this expresses: a table justifies to the width it
        /// is given. Its rightmost column's right edge is a function of
        /// panelWidth alone, the NAME column is the only one that flexes,
        /// and a name too long for what is left of the row ellipsizes with
        /// its full text on a tooltip. The previous model pulled the whole
        /// right-hand block LEFT to sit just past the widest name a table
        /// rendered, which left the recovered space stranded to the right
        /// of the block instead of inside the name column.
        /// </para>
        /// </summary>
        public static int PinnedRightEdge(int panelWidth)
        {
            return panelWidth - TableRightMargin;
        }

        /// <summary>
        /// The one rail every plan table's left-hand header word sits on:
        /// <see cref="ColumnHeaderLabelMath"/> applied to the icon gutter
        /// the tables open their rows with
        /// (<see cref="ShoppingColumnMath.IconX"/>), which Required Recipes
        /// duplicates as its own IconX/NameX pair. Used Materials opens
        /// with an Amount column instead, so its icon rule is that band's
        /// right edge (<see cref="UsedMaterialsColumnMath"/>).
        /// <para>
        /// A rail rather than each table's own answer because the Recipe
        /// Tree's grid differs from the tables stacked under it - a caret
        /// column sits before its icon, so its depth-0 gutter
        /// (TreeRowShapePlanner.CaretColumnWidth) is 10px right of theirs -
        /// and a reader sees the two identical words, not the two grids.
        /// The rail stays inside the tree's own Item column, which starts
        /// at its caret.
        /// </para>
        /// </summary>
        public static int TableLeftHeaderX =>
            ColumnHeaderLabelMath.LabelX(ShoppingColumnMath.NameX, ShoppingColumnMath.IconX);

        /// <summary>
        /// FLOOR width of the recipe tree's decision column - the narrowest
        /// pillColumnWidth a tree caller ever passes
        /// <see cref="ComputeTreeColumnEdges"/>, and the width every tree
        /// gets at the module's minimum window. Above it the column may
        /// claim more (Services/TreePillColumnMath). Lives here so the
        /// Blish-free width tests assert against the shipped column.
        /// <para>
        /// The ignore button is NOT in this column - it has one of its own
        /// at the far right of the row - so the widest run the floor has to
        /// hold is CRAFT / TP / VENDOR, 171px at the Menomonia 14 the
        /// markers draw in; 231 leaves it a budget of 227. 231 is also the
        /// old 256 less exactly what the action column costs, so the two
        /// together take what this column alone used to, and no window
        /// width paid for the new one: docs/ARCHITECTURE.md section V.33.
        /// </para>
        /// </summary>
        public const int TreePillColumnWidth = 231;

        /// <summary>
        /// Width of the tree's trailing action column, which closes every
        /// row after Cost and carries the ignore button. That button IS
        /// Blish's own window close control at its measured box
        /// (Services/GlyphButtonMetrics), so the column is exactly as wide
        /// as the control in it.
        /// </summary>
        public const int TreeActionColumnWidth = GlyphButtonMetrics.RowActionWidth;

        /// <summary>
        /// Clearance between the cost values and the ignore button beside
        /// them. Its own number, chosen to read the same as the clearance
        /// the decision column keeps before the cost column, and
        /// deliberately not derived from it: tuning one column's internal
        /// padding must not move a column two places away.
        /// </summary>
        public const int TreeActionColumnGap = 4;

        public readonly struct TreeColumnEdges
        {
            public readonly int PillColX;
            public readonly int CostRightEdge;
            public readonly int NameMaxWidth;

            public TreeColumnEdges(int pillColX, int costRightEdge, int nameMaxWidth)
            {
                PillColX = pillColX;
                CostRightEdge = costRightEdge;
                NameMaxWidth = nameMaxWidth;
            }

            /// <summary>
            /// x of the row's ignore button, one gap right of where the
            /// cost values stop. Derived rather than stored: the action
            /// column is what pushed <see cref="CostRightEdge"/> left in the
            /// first place, so the two can never be given different
            /// answers.
            /// </summary>
            public int ActionButtonX => CostRightEdge + TreeActionColumnGap;
        }

        /// <summary>
        /// Recipe tree's fixed right-anchored column grid (pills, cost,
        /// action) plus the resulting name-column budget, entirely as a
        /// function of panelWidth plus the row's own indent-derived nameX
        /// and the qty-prefix text width (font-only, invariant to
        /// panelWidth). Both the initial build and the relayout/
        /// re-ellipsis closures call this same function, so a tree row's
        /// columns and its build-time counterpart cannot drift apart.
        /// <para>
        /// The ignore button takes the row's right edge and the data
        /// columns end short of it, the shape RankerRowLayout.Compute
        /// already uses. The whole block is pinned to the panel edge (see
        /// <see cref="PinnedRightEdge"/>), so every offset between the four
        /// columns is width-invariant: a source marker that fits at one
        /// window width fits at every other, the caller settles the
        /// decision column's width once per render so a resize drag never
        /// refits, and a relayout can replay any of the four from PillColX
        /// alone.
        /// </para>
        /// </summary>
        public static TreeColumnEdges ComputeTreeColumnEdges(
            int panelWidth, int nameX, int qtyPrefixWidth,
            int pillColumnWidth, int costColumnWidth, int rightMargin)
        {
            int rightEdge = panelWidth - rightMargin;
            int costRightEdge = rightEdge - TreeActionColumnWidth - TreeActionColumnGap;
            int pillColX = costRightEdge - costColumnWidth - pillColumnWidth;

            int nameMaxWidth = pillColX - nameX - TreeNameGap;
            if (nameMaxWidth < 20)
            {
                nameMaxWidth = 20;
            }

            int nameAvailWidth = nameMaxWidth - qtyPrefixWidth;
            if (nameAvailWidth < 10)
            {
                nameAvailWidth = 10;
            }

            return new TreeColumnEdges(pillColX, costRightEdge, nameAvailWidth);
        }

        /// <summary>
        /// Room for the header of a right-aligned column that closes its
        /// table, with only a flexing name column before it - the Used
        /// Materials Amount column's shape. Bounded by the table's own edge
        /// on the right and, on the left, by half the gap
        /// <see cref="NameMaxWidthBeforeColumn"/> keeps before the column.
        /// That is a narrow room: this column reserves nothing beyond its
        /// widest value, so a header wider than the two together cannot
        /// centre at all and degrades the way every over-wide header does
        /// (JustifiedColumnTracks.CenteredOverContent) - left bound,
        /// spilling rightward.
        /// </summary>
        public static JustifiedColumnTracks.HeaderRoom TrailingColumnHeaderRoom(
            int rightEdge, int columnWidth, int gapBeforeColumn)
        {
            int inkX = rightEdge - columnWidth;
            return JustifiedColumnTracks.HeaderRoom.Between(
                JustifiedColumnTracks.RoomLeftBound(inkX - gapBeforeColumn, inkX), rightEdge);
        }

        /// <summary>Gap a tree row's name budget keeps before the pill
        /// column, and so where the name column stops drawing.</summary>
        public const int TreeNameGap = 8;

        /// <summary>
        /// Where the recipe tree's two data headers may sit: "Source" over
        /// the pill runs, "Cost" over the coin runs, each bounded by the
        /// column beside it rather than by its own reserve. Both reserves
        /// overstate their ink badly - the pill column is at least
        /// <see cref="TreePillColumnWidth"/> whatever a row's badges
        /// measure, and the cost column's is a per-denomination sum no one
        /// row draws together - so a header clamped into one right-aligns
        /// on values it should sit over. Cost's own right-hand neighbour is
        /// the table's pinned edge.
        /// </summary>
        public static void ComputeTreeHeaderRooms(
            TreeColumnEdges edges, int sourceInk, int costInk,
            out JustifiedColumnTracks.HeaderRoom source,
            out JustifiedColumnTracks.HeaderRoom cost)
        {
            int sourceInkRight = edges.PillColX + sourceInk;
            int costInkX = edges.CostRightEdge - costInk;
            source = JustifiedColumnTracks.HeaderRoom.Between(
                JustifiedColumnTracks.RoomLeftBound(edges.PillColX - TreeNameGap, edges.PillColX),
                JustifiedColumnTracks.RoomRightBound(sourceInkRight, costInkX));
            cost = JustifiedColumnTracks.HeaderRoom.Between(
                JustifiedColumnTracks.RoomLeftBound(sourceInkRight, costInkX),
                edges.CostRightEdge);
        }

        /// <summary>
        /// How many of the recipe tree's decision pills (already-measured
        /// widths, in DecisionPillPlanner.BuildPillSpecs emission order) fit
        /// left-to-right from startX before the next one would cross
        /// maxRightEdge, the boundary before the right-aligned cost column.
        /// The tree row has no wrap or second line - TreeRowHeight is one
        /// fixed height shared by every scroll/layout calculation in
        /// CraftingPlanView - so a pill that would overlap the cost column
        /// cannot be rendered.
        /// <para>
        /// Always returns at least 1 when pillWidths is non-empty, even if
        /// that first pill alone exceeds the budget, and every pill after the
        /// first is dropped strictly once it would not entirely fit.
        /// </para>
        /// <para>
        /// This is the primitive, not the policy: <see cref="ComputePillFit"/>
        /// calls it up to three times and is what the renderer actually uses.
        /// Derivation: docs/ARCHITECTURE.md section S1.6.
        /// </para>
        /// </summary>
        public static int ComputeVisiblePillCount(
            IReadOnlyList<int> pillWidths, int gap, int startX, int maxRightEdge)
        {
            return ComputeVisiblePillCount(pillWidths, 0, gap, startX, maxRightEdge);
        }

        /// <summary>
        /// <see cref="ComputeVisiblePillCount"/> with every pill narrowed by
        /// widthReduction - the tightened-padding pass, without a second
        /// measured-width list existing for the tree to allocate per row.
        /// A pill can never narrow below 1px however large the reduction.
        /// </summary>
        private static int ComputeVisiblePillCount(
            IReadOnlyList<int> pillWidths, int widthReduction, int gap, int startX, int maxRightEdge)
        {
            if (pillWidths == null || pillWidths.Count == 0)
            {
                return 0;
            }

            int x = startX;
            int count = 0;
            for (int i = 0; i < pillWidths.Count; i++)
            {
                int width = ReducedWidth(pillWidths[i], widthReduction);
                if (i > 0 && x + width > maxRightEdge)
                {
                    break;
                }

                x += width + gap;
                count++;
            }

            return count;
        }

        /// <summary>
        /// One pill's width at a given padding reduction, floored at 1px.
        /// The renderer calls this too, so build and fit cannot disagree
        /// about how wide a tightened pill is.
        /// </summary>
        public static int ReducedWidth(int fullWidth, int widthReduction)
        {
            int width = fullWidth - widthReduction;
            return width > 1 ? width : 1;
        }

        /// <summary>
        /// How <see cref="ComputePillFit"/> resolved one tree row's pill
        /// column: how many pills to draw, how much narrower to draw them
        /// (0 = their measured width), how many were left out, and how wide
        /// the trailing "+N" pill announcing them must be (0 when nothing
        /// was left out).
        /// </summary>
        public readonly struct PillFitPlan
        {
            public readonly int VisibleCount;
            public readonly int HiddenCount;
            public readonly int WidthReduction;
            public readonly int OverflowPillWidth;

            public PillFitPlan(int visibleCount, int hiddenCount, int widthReduction, int overflowPillWidth)
            {
                VisibleCount = visibleCount;
                HiddenCount = hiddenCount;
                WidthReduction = widthReduction;
                OverflowPillWidth = overflowPillWidth;
            }
        }

        /// <summary>
        /// Full pill-column plan for one tree row, in the order the fix
        /// escalates:
        /// <list type="number">
        /// <item><description>Every pill fits at its normal padding - draw
        /// them all.</description></item>
        /// <item><description>They fit once every pill is narrowed by
        /// <paramref name="widthReduction"/> - draw them all that narrow.</description></item>
        /// <item><description>They still do not fit - reserve room for a
        /// trailing "+N" pill and draw as many tightened pills as fit before
        /// it, so the row states that options exist rather than dropping
        /// them silently.</description></item>
        /// </list>
        /// <paramref name="overflowPillWidthForHidden"/> measures "+N" for a
        /// given N; null degrades to dropping silently rather than throwing,
        /// as does a non-positive <paramref name="widthReduction"/>. Like
        /// <see cref="ComputeVisiblePillCount"/>, at least one pill is always
        /// drawn. Derivation: docs/ARCHITECTURE.md section S1.6.
        /// </summary>
        public static PillFitPlan ComputePillFit(
            IReadOnlyList<int> pillWidths,
            int widthReduction,
            int gap,
            int startX,
            int maxRightEdge,
            Func<int, int> overflowPillWidthForHidden)
        {
            if (pillWidths == null || pillWidths.Count == 0)
            {
                return new PillFitPlan(0, 0, 0, 0);
            }

            int count = pillWidths.Count;
            int fullFit = ComputeVisiblePillCount(pillWidths, 0, gap, startX, maxRightEdge);
            if (fullFit >= count)
            {
                return new PillFitPlan(fullFit, 0, 0, 0);
            }

            int reduction = widthReduction > 0 ? widthReduction : 0;
            int reducedFit = reduction > 0
                ? ComputeVisiblePillCount(pillWidths, reduction, gap, startX, maxRightEdge)
                : fullFit;
            if (reducedFit >= count)
            {
                return new PillFitPlan(reducedFit, 0, reduction, 0);
            }

            if (overflowPillWidthForHidden == null)
            {
                return new PillFitPlan(reducedFit, 0, reduction, 0);
            }

            int hidden = count - reducedFit;
            int visible = reducedFit;
            int overflowWidth = 0;
            for (int i = 0; i < 4; i++)
            {
                int candidateWidth = overflowPillWidthForHidden(hidden);
                if (candidateWidth < 0)
                {
                    candidateWidth = 0;
                }

                int fit = ComputeVisiblePillCount(
                    pillWidths, reduction, gap, startX, maxRightEdge - candidateWidth - gap);
                int nextHidden = count - fit;

                overflowWidth = candidateWidth;
                visible = fit;
                if (nextHidden == hidden)
                {
                    break;
                }

                hidden = nextHidden;
            }

            return new PillFitPlan(visible, count - visible, reduction, overflowWidth);
        }

        public readonly struct CostTileGeometry
        {
            public readonly int TileWidth;
            public readonly int StartX;

            public CostTileGeometry(int tileWidth, int startX)
            {
                TileWidth = tileWidth;
                StartX = startX;
            }
        }

        /// <summary>
        /// Equal-width stat-tile row geometry (Summary section's cost
        /// tiles): tileWidth clamped to a minimum, then the row of tiles is
        /// centered as a unit within panelWidth. Used by
        /// SummarySectionRenderer.CreateFormulaBand.
        /// tileCount &lt;= 0 returns a zero-width, zero-offset geometry
        /// (caller already skips rendering the row entirely in that case).
        /// </summary>
        public static CostTileGeometry ComputeCostTileGeometry(
            int panelWidth, int tileCount, int totalMargin, int minTileWidth)
        {
            if (tileCount <= 0)
            {
                return new CostTileGeometry(0, 0);
            }

            int tileWidth = (panelWidth - totalMargin) / tileCount;
            if (tileWidth < minTileWidth)
            {
                tileWidth = minTileWidth;
            }

            int rowContentWidth = tileWidth * tileCount;

            return new CostTileGeometry(tileWidth, CenterX(panelWidth, rowContentWidth));
        }
    }
}
