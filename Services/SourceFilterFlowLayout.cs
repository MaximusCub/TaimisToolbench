using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Placement of one cell in a <see cref="SourceFilterFlowLayout"/> run,
    /// relative to the containing row panel's own origin.
    /// </summary>
    internal class FlowCellPlacement
    {
        public int X { get; set; }

        public int Y { get; set; }
    }

    /// <summary>
    /// The full placement run: one <see cref="FlowCellPlacement"/> per input
    /// width, in input order, plus the height the container must reserve.
    /// </summary>
    internal class SourceFilterFlowResult
    {
        public List<FlowCellPlacement> Cells { get; } = new List<FlowCellPlacement>();

        public int RowCount { get; set; }

        public int TotalHeight { get; set; }
    }

    /// <summary>
    /// Wrapping left-to-right placement for the Snapshot tab's source-filter
    /// checkbox row, whose cell count is account-driven (one checkbox per
    /// character, 1 to 15+) and so cannot use the fixed X positions the row
    /// carried while it was four fixed checkboxes. Blish-free by
    /// construction: callers measure their own label widths and apply the
    /// returned offsets (see Views/MainView.cs).
    /// <para>
    /// The tab shows two filter sets, locations and characters.
    /// <see cref="LayoutGroups"/> starts each set on a row of its own, which
    /// is what makes the two read as separate sets at every width.
    /// </para>
    /// </summary>
    internal static class SourceFilterFlowLayout
    {
        /// <summary>
        /// A cell wider than <paramref name="availableWidth"/> still gets
        /// placed at the start of its own row rather than being dropped or
        /// looping forever - overflowing one oversized label is strictly
        /// better than hiding a filter the user cannot then re-enable.
        /// </summary>
        public static SourceFilterFlowResult Layout(
            IReadOnlyList<int> cellWidths,
            int availableWidth,
            int cellHeight,
            int horizontalGap,
            int verticalGap)
        {
            var result = new SourceFilterFlowResult();

            if (cellWidths == null || cellWidths.Count == 0)
            {
                return result;
            }

            int height = cellHeight > 0 ? cellHeight : 0;
            int gapX = horizontalGap > 0 ? horizontalGap : 0;
            int gapY = verticalGap > 0 ? verticalGap : 0;

            int x = 0;
            int rowIndex = 0;
            PlaceRun(result, cellWidths, availableWidth, height, gapX, gapY, ref x, ref rowIndex);
            Finish(result, rowIndex, height, gapY);
            return result;
        }

        /// <summary>
        /// The same placement, run over several groups of cells, with every
        /// group after the first starting on a fresh row. Cells come back
        /// flattened in group order, so a caller reads them off against its
        /// own controls in the order it passed them. An empty group takes no
        /// row. A null group list returns an empty result.
        /// </summary>
        public static SourceFilterFlowResult LayoutGroups(
            IReadOnlyList<IReadOnlyList<int>> groups,
            int availableWidth,
            int cellHeight,
            int horizontalGap,
            int verticalGap)
        {
            var result = new SourceFilterFlowResult();

            if (groups == null || groups.Count == 0)
            {
                return result;
            }

            int height = cellHeight > 0 ? cellHeight : 0;
            int gapX = horizontalGap > 0 ? horizontalGap : 0;
            int gapY = verticalGap > 0 ? verticalGap : 0;

            int x = 0;
            int rowIndex = 0;
            bool anyPlaced = false;

            foreach (var group in groups)
            {
                if (group == null || group.Count == 0)
                {
                    continue;
                }

                if (anyPlaced)
                {
                    rowIndex++;
                    x = 0;
                }

                PlaceRun(result, group, availableWidth, height, gapX, gapY, ref x, ref rowIndex);
                anyPlaced = true;
            }

            if (!anyPlaced)
            {
                return result;
            }

            Finish(result, rowIndex, height, gapY);
            return result;
        }

        private static void PlaceRun(
            SourceFilterFlowResult result,
            IReadOnlyList<int> cellWidths,
            int availableWidth,
            int height,
            int gapX,
            int gapY,
            ref int x,
            ref int rowIndex)
        {
            foreach (int rawWidth in cellWidths)
            {
                int width = rawWidth > 0 ? rawWidth : 0;

                if (x > 0 && x + width > availableWidth)
                {
                    rowIndex++;
                    x = 0;
                }

                result.Cells.Add(new FlowCellPlacement { X = x, Y = rowIndex * (height + gapY) });
                x += width + gapX;
            }
        }

        private static void Finish(
            SourceFilterFlowResult result, int lastRowIndex, int height, int gapY)
        {
            result.RowCount = lastRowIndex + 1;
            result.TotalHeight = (result.RowCount * height) + (lastRowIndex * gapY);
        }
    }
}
