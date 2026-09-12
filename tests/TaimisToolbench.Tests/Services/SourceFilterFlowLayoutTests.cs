using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class SourceFilterFlowLayoutTests
    {
        private const int CellHeight = 25;
        private const int GapX = 10;
        private const int GapY = 4;

        private static SourceFilterFlowResult Layout(int availableWidth, params int[] widths)
        {
            return SourceFilterFlowLayout.Layout(widths, availableWidth, CellHeight, GapX, GapY);
        }

        [Fact]
        public void Layout_NullWidths_NoCellsNoHeight()
        {
            var result = SourceFilterFlowLayout.Layout(null, 500, CellHeight, GapX, GapY);

            Assert.Empty(result.Cells);
            Assert.Equal(0, result.RowCount);
            Assert.Equal(0, result.TotalHeight);
        }

        [Fact]
        public void Layout_EmptyWidths_NoCellsNoHeight()
        {
            var result = SourceFilterFlowLayout.Layout(new List<int>(), 500, CellHeight, GapX, GapY);

            Assert.Empty(result.Cells);
            Assert.Equal(0, result.RowCount);
            Assert.Equal(0, result.TotalHeight);
        }

        [Fact]
        public void Layout_CellsThatFit_StayOnOneRowSeparatedByTheGap()
        {
            var result = Layout(500, 70, 170, 170);

            Assert.Equal(1, result.RowCount);
            Assert.Equal(CellHeight, result.TotalHeight);
            Assert.Equal(0, result.Cells[0].X);
            Assert.Equal(80, result.Cells[1].X);
            Assert.Equal(260, result.Cells[2].X);
            Assert.All(result.Cells, c => Assert.Equal(0, c.Y));
        }

        [Fact]
        public void Layout_CellEndingExactlyAtTheEdge_DoesNotWrap()
        {
            var result = Layout(250, 70, 170);

            Assert.Equal(1, result.RowCount);
            Assert.Equal(80, result.Cells[1].X);
        }

        [Fact]
        public void Layout_CellOverflowingByOnePixel_WrapsToNextRow()
        {
            var result = Layout(249, 70, 170);

            Assert.Equal(2, result.RowCount);
            Assert.Equal(0, result.Cells[1].X);
            Assert.Equal(CellHeight + GapY, result.Cells[1].Y);
            Assert.Equal((2 * CellHeight) + GapY, result.TotalHeight);
        }

        [Fact]
        public void Layout_ManyCells_WrapAcrossSeveralRowsAndHeightMatchesRowCount()
        {
            // 8 x 100px cells (+10px gaps) into a 340px row: 3 per row.
            var result = Layout(340, 100, 100, 100, 100, 100, 100, 100, 100);

            Assert.Equal(3, result.RowCount);
            Assert.Equal((3 * CellHeight) + (2 * GapY), result.TotalHeight);
            Assert.Equal(0, result.Cells[3].X);
            Assert.Equal(CellHeight + GapY, result.Cells[3].Y);
            Assert.Equal(0, result.Cells[6].X);
            Assert.Equal(2 * (CellHeight + GapY), result.Cells[6].Y);
        }

        [Fact]
        public void Layout_CellWiderThanTheRow_PlacedAtRowStartAndFollowerWraps()
        {
            var result = Layout(100, 300, 50);

            Assert.Equal(2, result.RowCount);
            Assert.Equal(0, result.Cells[0].X);
            Assert.Equal(0, result.Cells[0].Y);
            Assert.Equal(0, result.Cells[1].X);
            Assert.Equal(CellHeight + GapY, result.Cells[1].Y);
        }

        [Fact]
        public void Layout_NonPositiveAvailableWidth_OneCellPerRow()
        {
            var result = Layout(0, 70, 70, 70);

            Assert.Equal(3, result.RowCount);
            Assert.All(result.Cells, c => Assert.Equal(0, c.X));
        }

        [Fact]
        public void Layout_NegativeWidth_TreatedAsZeroAndStillPlaced()
        {
            var result = Layout(500, -50, 70);

            Assert.Equal(2, result.Cells.Count);
            Assert.Equal(1, result.RowCount);
            Assert.Equal(0, result.Cells[0].X);
            Assert.Equal(GapX, result.Cells[1].X);
        }

        [Fact]
        public void Layout_NegativeHeightAndGaps_ClampedToZero()
        {
            var result = SourceFilterFlowLayout.Layout(new[] { 70, 70 }, 100, -25, -10, -4);

            Assert.Equal(2, result.RowCount);
            Assert.Equal(0, result.TotalHeight);
            Assert.Equal(0, result.Cells[1].Y);
        }

        // ---- LayoutGroups ----
        private static SourceFilterFlowResult LayoutGroups(
            int availableWidth, params int[][] groups)
        {
            var asLists = new List<IReadOnlyList<int>>();
            foreach (var group in groups)
            {
                asLists.Add(group);
            }

            return SourceFilterFlowLayout.LayoutGroups(
                asLists, availableWidth, CellHeight, GapX, GapY);
        }

        [Fact]
        public void LayoutGroups_NullGroups_NoCellsNoHeight()
        {
            var result = SourceFilterFlowLayout.LayoutGroups(null, 500, CellHeight, GapX, GapY);

            Assert.Empty(result.Cells);
            Assert.Equal(0, result.RowCount);
            Assert.Equal(0, result.TotalHeight);
        }

        // Two sets that would share a row if flowed as one run do not, which
        // is what makes them read as two sets.
        [Fact]
        public void LayoutGroups_SecondGroup_StartsOnItsOwnRow()
        {
            var result = LayoutGroups(500, new[] { 70, 70 }, new[] { 70 });

            Assert.Equal(2, result.RowCount);
            Assert.Equal(0, result.Cells[0].X);
            Assert.Equal(80, result.Cells[1].X);
            Assert.Equal(0, result.Cells[2].X);
            Assert.Equal(CellHeight + GapY, result.Cells[2].Y);
            Assert.Equal((2 * CellHeight) + GapY, result.TotalHeight);
        }

        [Fact]
        public void LayoutGroups_AGroupThatWraps_CarriesTheNextGroupPastItsLastRow()
        {
            var result = LayoutGroups(150, new[] { 70, 70, 70 }, new[] { 70 });

            Assert.Equal(3, result.RowCount);
            Assert.Equal(0, result.Cells[0].Y);
            Assert.Equal(0, result.Cells[1].Y);
            Assert.Equal(CellHeight + GapY, result.Cells[2].Y);
            Assert.Equal(2 * (CellHeight + GapY), result.Cells[3].Y);
            Assert.Equal(0, result.Cells[3].X);
        }

        [Fact]
        public void LayoutGroups_EmptyOrNullGroups_TakeNoRow()
        {
            var result = SourceFilterFlowLayout.LayoutGroups(
                new List<IReadOnlyList<int>> { new int[0], null, new[] { 70, 70 } },
                500, CellHeight, GapX, GapY);

            Assert.Equal(2, result.Cells.Count);
            Assert.Equal(1, result.RowCount);
            Assert.Equal(0, result.Cells[0].Y);
        }

        [Fact]
        public void LayoutGroups_AllGroupsEmpty_NoCellsNoHeight()
        {
            var result = SourceFilterFlowLayout.LayoutGroups(
                new List<IReadOnlyList<int>> { new int[0], null },
                500, CellHeight, GapX, GapY);

            Assert.Empty(result.Cells);
            Assert.Equal(0, result.RowCount);
            Assert.Equal(0, result.TotalHeight);
        }

        // One group behaves exactly like the ungrouped run.
        [Fact]
        public void LayoutGroups_OneGroup_MatchesLayout()
        {
            var grouped = LayoutGroups(250, new[] { 70, 170, 70 });
            var flat = Layout(250, 70, 170, 70);

            Assert.Equal(flat.RowCount, grouped.RowCount);
            Assert.Equal(flat.TotalHeight, grouped.TotalHeight);
            for (int i = 0; i < flat.Cells.Count; i++)
            {
                Assert.Equal(flat.Cells[i].X, grouped.Cells[i].X);
                Assert.Equal(flat.Cells[i].Y, grouped.Cells[i].Y);
            }
        }

        // The narrowest realistic case: every cell on a row of its own, and
        // still one row per cell plus nothing lost between the groups.
        [Fact]
        public void LayoutGroups_TooNarrowForAnyCell_KeepsEveryCellPlaced()
        {
            var result = LayoutGroups(0, new[] { 70, 70 }, new[] { 70, 70 });

            Assert.Equal(4, result.Cells.Count);
            Assert.Equal(4, result.RowCount);
            Assert.All(result.Cells, c => Assert.Equal(0, c.X));
        }
    }
}
