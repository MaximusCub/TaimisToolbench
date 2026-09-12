using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Where a popout's parts sit, and how big one opens. Blish-free: every
    /// number here is derived from the plan tab's own layout constants, so
    /// the table inside a popout is the table the plan tab draws.
    /// </summary>
    internal static class PopoutLayout
    {
        /// <summary>
        /// Width of the tick column down the left of the table. The table
        /// itself is rendered into a flow inset by this much, so the shared
        /// row renderers draw exactly what they draw on the plan tab and
        /// the column is beside their rows rather than inside them.
        /// </summary>
        public const int CheckColumnWidth = 32;

        /// <summary>
        /// Side of the square a text-less Blish Checkbox is given. Bigger
        /// than the box it paints, because this is the hit area and the
        /// player is clicking it mid-game.
        /// </summary>
        public const int CheckboxSize = 20;

        /// <summary>Left x of the box inside its column.</summary>
        public const int CheckboxX = (CheckColumnWidth - CheckboxSize) / 2;

        /// <summary>
        /// The width the section renderer lays its table out at, given the
        /// popout's content box. Named once because two callers need it and
        /// they must not answer differently: the window replays every
        /// relayout closure at this width, and those closures are the
        /// renderers' own, which read it as the panel width their columns
        /// justify to. Feeding one the content box instead puts the tick
        /// column's width into every column edge on the row.
        /// </summary>
        public static int TableWidth(int contentWidth)
        {
            int width = contentWidth - CheckColumnWidth - WindowSizing.ScrollbarAllowance;
            return width > 0 ? width : 0;
        }

        /// <summary>Height of the toolbar strip above the table.</summary>
        public const int ToolbarHeight = 34;

        /// <summary>Gap between the toolbar and the status line's line box.</summary>
        public const int StatusTextY = 4;

        /// <summary>
        /// Gap between the status line's lowest ink and the rule under it.
        /// The row used to be 24px against a line whose descenders reach
        /// 23, so the text sat 3px INTO the rule.
        /// </summary>
        public const int StatusBottomPadding = UiSpacing.ButtonGap;

        /// <summary>
        /// Height of the status line under the toolbar. Not a const:
        /// TypeRampMetrics reports its measured ink from a static field.
        /// </summary>
        public static readonly int StatusRowHeight =
            StatusTextY + TypeRampMetrics.StatusInk.LowestInk + StatusBottomPadding;

        /// <summary>Rule between the chrome and the scrolling table.</summary>
        public const int SeparatorHeight = 2;

        /// <summary>
        /// Everything above the table inside the content box.
        /// </summary>
        public static readonly int ChromeHeight =
            ToolbarHeight + StatusRowHeight + SeparatorHeight;

        /// <summary>
        /// Clearance kept below the last row, inside the content box. The
        /// window's own art already leaves
        /// <see cref="WindowSizing.WindowContentBottomMargin"/> under that
        /// box; this is the inner pad a tab's content keeps on top of it,
        /// so a popout's last icon does not sit on the frame the way the
        /// plan tab's never does.
        /// </summary>
        public const int ContentBottomPadding = WindowSizing.TabPanelInnerPadding;

        /// <summary>
        /// How many rows a popout opens tall. Ten icon-led rows plus a
        /// column header band is 532px of table; with the chrome and the
        /// window's own frame that is a window of about 665px, which is
        /// under two thirds of a 1080p client's height and leaves the game
        /// visible around it. A list longer than ten rows scrolls.
        /// </summary>
        public const int DefaultVisibleRows = 10;

        /// <summary>
        /// Narrowest content box the Shopping List table stays a
        /// five-column table in, tick column and scrollbar strip included:
        /// the table is laid out in what is left after both, so a floor
        /// that counted neither would be short by their width. Below the
        /// width <see cref="ShoppingColumnMath"/> can distribute three data
        /// tracks over, the table packs its columns right to left instead,
        /// which is a different picture from the one the plan tab shows.
        /// <para>
        /// The Amount band ahead of the name is taken at
        /// <see cref="SnapshotItemGridLayout.AmountColumnFloor"/>, the
        /// measured width the same header word wants in the module's other
        /// minimum-width derivation. A floor, not a guarantee: a list whose
        /// widest count out-measures it wants a wider window than this.
        /// </para>
        /// </summary>
        public static int MinShoppingContentWidth()
        {
            int widestBand = ShoppingColumnMath.TotalMinWidth > ShoppingColumnMath.EachMinWidth
                ? ShoppingColumnMath.TotalMinWidth
                : ShoppingColumnMath.EachMinWidth;

            int table = AmountLedRowMath.NameX(SnapshotItemGridLayout.AmountColumnFloor)
                + ShoppingColumnMath.NameMinWidth
                + (ShoppingColumnMath.DataColumnCount * (widestBand + ShoppingColumnMath.ColumnGap))
                + PlanRelayoutMath.TableRightMargin;

            return table + CheckColumnWidth + WindowSizing.ScrollbarAllowance;
        }

        /// <summary>
        /// Narrowest content box the Crafting Steps table reads in. Its rows
        /// have no columns to distribute, so the floor is its own fixed run
        /// (badge, icon, "Craft Nx ") plus the same item-name floor the
        /// Shopping List keeps for a name column, plus room for the
        /// right-aligned discipline sublabel.
        /// </summary>
        public static int MinCraftStepsContentWidth()
        {
            return CraftStepTextX
                + ShoppingColumnMath.NameMinWidth
                + CraftStepSublabelBand
                + PlanRelayoutMath.TableRightMargin
                + CheckColumnWidth
                + WindowSizing.ScrollbarAllowance;
        }

        /// <summary>
        /// Left x of the text run on a Crafting Steps row, mirroring
        /// Views/Rendering/CraftStepsSectionRenderer's own TextX. Named here
        /// because a width floor cannot be derived without it and this file
        /// may not reference a renderer.
        /// </summary>
        private const int CraftStepTextX = 52 + PlanContentHeightMath.RowIconFrameSize + 8;

        /// <summary>
        /// Room kept for a Crafting Steps row's right-aligned sublabel. The
        /// widest one the plan emits is a discipline name and a rating, and
        /// the Shopping List's own Each column floor is the module's
        /// nearest measured band of that order.
        /// </summary>
        private const int CraftStepSublabelBand = ShoppingColumnMath.EachMinWidth;

        /// <summary>
        /// The narrowest content box for one section's table.
        /// </summary>
        public static int MinContentWidth(PlanSectionType sectionType)
        {
            return sectionType == PlanSectionType.CraftingSteps
                ? MinCraftStepsContentWidth()
                : MinShoppingContentWidth();
        }

        /// <summary>
        /// Content-box height a popout opens at for a table of this many
        /// rows: the chrome, plus as much of the table as
        /// <see cref="DefaultVisibleRows"/> allows. A short list opens short
        /// rather than opening with empty space under it.
        /// </summary>
        public static int DefaultContentHeight(
            PlanSectionType sectionType, IReadOnlyList<PlanRowViewModel> rows)
        {
            var shown = Head(rows, DefaultVisibleRows);
            return ChromeHeight
                + PlanContentHeightMath.SectionBodyHeight(sectionType, shown)
                + ContentBottomPadding;
        }

        private static IReadOnlyList<PlanRowViewModel> Head(
            IReadOnlyList<PlanRowViewModel> rows, int count)
        {
            var head = new List<PlanRowViewModel>();
            if (rows == null)
            {
                return head;
            }

            for (int i = 0; i < rows.Count && i < count; i++)
            {
                head.Add(rows[i]);
            }

            return head;
        }

        /// <summary>
        /// The y of each row's tick box inside the tick column, in the order
        /// the table draws its rows. Derived from the same per-row heights
        /// <see cref="PlanContentHeightMath.SectionBodyHeight"/> sums, so a
        /// box cannot land off the row it belongs to.
        /// <para>
        /// A row that carries no tick at all - a Crafting Steps notice line,
        /// which is prose rather than a step - reports -1.
        /// </para>
        /// </summary>
        public static IReadOnlyList<int> CheckboxYOffsets(
            PlanSectionType sectionType, IReadOnlyList<PlanRowViewModel> rows)
        {
            var offsets = new List<int>();
            if (rows == null)
            {
                return offsets;
            }

            int y = sectionType == PlanSectionType.CraftingSteps
                ? 0
                : PlanContentHeightMath.ColumnHeaderRowHeight;

            foreach (var row in rows)
            {
                bool notice = sectionType == PlanSectionType.CraftingSteps
                    && PlanRowKinds.IsCraftingStepsNotice(row.RowType);

                int rowHeight = notice
                    ? PlanContentHeightMath.FallbackTextRowHeight
                    : RowHeightFor(sectionType);

                offsets.Add(notice ? -1 : y + CentredOffset());
                y += rowHeight;
            }

            return offsets;
        }

        private static int RowHeightFor(PlanSectionType sectionType)
        {
            return sectionType == PlanSectionType.CraftingSteps
                ? PlanContentHeightMath.CraftStepRowHeight
                : PlanContentHeightMath.ShoppingRowHeight;
        }

        /// <summary>
        /// Centres the box on the band a reader sees, not on the row's full
        /// height: an icon-led row spends its last pixels on a rule and its
        /// clearance, and counting those sets the box low. Only icon-led
        /// rows carry a box at all, so there is one band to centre on.
        /// </summary>
        private static int CentredOffset()
        {
            return (PlanContentHeightMath.IconLedRowVisibleHeight - CheckboxSize) / 2;
        }
    }
}
