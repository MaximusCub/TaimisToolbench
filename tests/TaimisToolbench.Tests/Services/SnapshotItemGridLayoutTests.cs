using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class SnapshotItemGridLayoutTests
    {
        // MainView renders straight into its tab's content region and adds
        // no right-edge padding of its own, so the last (scrollbar) term of
        // WindowSizing's chain is the one SnapshotItemGridLayout applies
        // itself - i.e. the grid width at the window minimum is exactly the
        // panel width the rest of the module's layout math is derived
        // against. That last step is a fact about the VIEW and so cannot be
        // asserted from a Blish-free test; step 1 of the sandbox check checks
        // it live (the rightmost column's text stopping clear of the
        // scrollbar). What IS asserted here is the arithmetic hanging off
        // it: move MinColumnWidth or WindowToTabPanelChrome and the
        // threshold test below fails with the documented window widths.
        private static int ContentPanelWidthFor(int windowWidth)
        {
            return WindowSizing.TabPanelWidthFor(windowWidth)
                + SnapshotItemGridLayout.ScrollbarAllowance;
        }

        private static int ColumnCountAtWindow(int windowWidth)
        {
            return SnapshotItemGridLayout.ComputeColumnCount(
                SnapshotItemGridLayout.ComputeGridWidth(ContentPanelWidthFor(windowWidth)));
        }

        private static readonly int GridWidthAtWindowMinimum =
            SnapshotItemGridLayout.ComputeGridWidth(ContentPanelWidthFor(WindowSizing.MinWindowWidth));

        [Fact]
        public void ColumnThresholds_ThroughTheChromeChain_AreTheDocumentedWindowWidths()
        {
            // The window widths the table in KNOWN-ISSUES #50 quotes,
            // derived rather than copied: N columns need N * MinColumnWidth
            // of grid, and the chrome between the window
            // and the grid is WindowToTabPanelChrome (this tab spends on the
            // scrollbar the 20px the chain's last term spends on padding).
            int windowForTwoColumns = 2 * SnapshotItemGridLayout.SnapshotMinColumnWidth
                + WindowSizing.WindowToTabPanelChrome;
            int windowForThreeColumns = 3 * SnapshotItemGridLayout.SnapshotMinColumnWidth
                + WindowSizing.WindowToTabPanelChrome;

            // Re-pinned when the Amount header gained its persistent sort
            // indicator (AmountColumnFloor 95, was 79), when the
            // indicator's gap doubled (floor 99), and when the Amount
            // column took the same gap as its own left inset (CellAmountX
            // 8, was 2): each widened the column and moved the thresholds
            // right, and the two-column floor still clears the enforced
            // window minimum.
            Assert.Equal(1310, windowForTwoColumns);
            Assert.Equal(1902, windowForThreeColumns);

            // The enforced minimum sits between them, which is the whole
            // claim: every client that can hold the minimum is at least
            // two-up, and the one-column fallback is only reachable below it.
            Assert.True(windowForTwoColumns < WindowSizing.MinWindowWidth);
            Assert.True(windowForThreeColumns > WindowSizing.MinWindowWidth);

            Assert.Equal(1, ColumnCountAtWindow(windowForTwoColumns - 1));
            Assert.Equal(2, ColumnCountAtWindow(windowForTwoColumns));
            Assert.Equal(2, ColumnCountAtWindow(windowForThreeColumns - 1));
            Assert.Equal(3, ColumnCountAtWindow(windowForThreeColumns));
        }

        [Fact]
        public void TheAmountColumnsLeftInset_MatchesTheGapAfterItsHeaderWord()
        {
            // The A in "Amount" sat 2px from the edge of its cell and read
            // as touching it. The inset is the gap between the header word
            // and its sort indicator, so the space before the word matches
            // the space after it.
            Assert.Equal(SortIndicatorLayout.Gap, SnapshotItemGridLayout.CellAmountX);

            // A header block at least as wide as the band it centres in
            // pins to that inset, which is the case every column the tab
            // actually draws: "Amount" plus its indicator out-measures the
            // digits under it.
            Assert.Equal(
                SnapshotItemGridLayout.CellAmountX,
                SnapshotItemGridLayout.CellAmountTextX(40, 96));
        }

        [Fact]
        public void MinColumnWidth_FitsTwoColumnsAtTheWindowMinimum()
        {
            Assert.Equal(2, SnapshotItemGridLayout.ComputeColumnCount(GridWidthAtWindowMinimum));
            Assert.True(2 * SnapshotItemGridLayout.SnapshotMinColumnWidth <= GridWidthAtWindowMinimum);
            Assert.True(3 * SnapshotItemGridLayout.SnapshotMinColumnWidth > GridWidthAtWindowMinimum);
        }

        [Fact]
        public void ColumnWidth_AtWindowMinimum_LeavesTheNameRunRoomToSpare()
        {
            int columnWidth = SnapshotItemGridLayout.ComputeColumnWidth(GridWidthAtWindowMinimum);

            // Budgeted the way the cell is actually built: the name starts
            // past the Amount column and the icon gutter.
            int nameRunBudget = SnapshotItemGridLayout.CellTextMaxWidth(
                columnWidth, SnapshotItemGridLayout.AmountColumnFloor);

            Assert.True(
                nameRunBudget >= SnapshotItemGridLayout.SnapshotNameRunChars * SnapshotItemGridLayout.MaxCharWidthPx,
                "a full-length name must not ellipsize in a column at the window minimum");
        }

        [Fact]
        public void TheNameIsTheOnlyPartOfTheCellThatFlexes()
        {
            // The plan view's rule on one grid cell: a wider cell must not
            // strand the recovered space, and everything left of the name is
            // fixed for the run.
            const int band = 60;
            int narrow = SnapshotItemGridLayout.CellTextMaxWidth(600, band);
            int wide = SnapshotItemGridLayout.CellTextMaxWidth(800, band);

            Assert.Equal(200, wide - narrow);
            Assert.Equal(
                800 - SnapshotItemGridLayout.CellTextRightPad,
                SnapshotItemGridLayout.CellContentRightEdge(800));
        }

        [Fact]
        public void TheAmountColumnLeadsTheCellAndTheIconFollowsIt()
        {
            const int band = 79;

            // Amount, gap, icon gutter, then the text: the reading order the
            // cell is built in.
            Assert.Equal(
                SnapshotItemGridLayout.CellAmountX + band + SnapshotItemGridLayout.CellAmountGap,
                SnapshotItemGridLayout.CellIconX(band));
            Assert.Equal(
                SnapshotItemGridLayout.CellIconX(band) + SnapshotItemGridLayout.IconGutterWidth,
                SnapshotItemGridLayout.CellTextX(band));

            // A wider band pushes the name right by exactly its own growth.
            Assert.Equal(
                20,
                SnapshotItemGridLayout.CellTextX(band + 20) - SnapshotItemGridLayout.CellTextX(band));
        }

        [Fact]
        public void AnAmountCentresInItsBand()
        {
            const int band = 80;

            Assert.Equal(
                SnapshotItemGridLayout.CellAmountX + 30,
                SnapshotItemGridLayout.CellAmountTextX(band, 20));

            // The widest amount in the run fills the band exactly.
            Assert.Equal(
                SnapshotItemGridLayout.CellAmountX,
                SnapshotItemGridLayout.CellAmountTextX(band, band));

            // Nothing is ever placed left of the band, whatever it measures.
            Assert.Equal(
                SnapshotItemGridLayout.CellAmountX,
                SnapshotItemGridLayout.CellAmountTextX(band, band + 40));
            Assert.Equal(
                SnapshotItemGridLayout.CellAmountX,
                SnapshotItemGridLayout.CellAmountTextX(0, 0));
        }

        // The Amount column IS everything left of the icon gutter, so its
        // cell reaches the gutter rather than stopping between two words.
        [Fact]
        public void HeaderCellSplit_SitsInTheGapBetweenTheAmountBandAndTheIcon()
        {
            const int band = 79;

            int split = SnapshotItemGridLayout.CellHeaderSplitX(band);
            int amountRightEdge = SnapshotItemGridLayout.CellAmountX + band;

            Assert.InRange(split, amountRightEdge, SnapshotItemGridLayout.CellIconX(band));

            // Each pixel answers the header of the column it is in.
            Assert.True(split > SnapshotItemGridLayout.CellAmountX);
        }

        [Fact]
        public void HeaderCellSplit_MovesWithTheBandAndNotWithTheColumn()
        {
            const int band = 79;

            // Everything left of the split is fixed-width, so a wider window
            // spends every recovered pixel on the name.
            Assert.Equal(
                20,
                SnapshotItemGridLayout.CellHeaderSplitX(band + 20)
                    - SnapshotItemGridLayout.CellHeaderSplitX(band));
        }

        [Fact]
        public void AmountBand_IsFlooredAtItsOwnHeaderLabel()
        {
            // The header out-measures its digits, and a name budgeted
            // against the digits alone would run under it.
            Assert.Equal(79, SnapshotItemGridLayout.CellAmountBandWidth(32, 79));
            Assert.Equal(140, SnapshotItemGridLayout.CellAmountBandWidth(140, 79));
            Assert.Equal(0, SnapshotItemGridLayout.CellAmountBandWidth(-5, 0));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-100, 1)]
        [InlineData(1, 1)]
        [InlineData(591, 1)]
        [InlineData(1183, 1)]
        [InlineData(1184, 2)]
        [InlineData(1775, 2)]
        [InlineData(1776, 3)]
        [InlineData(2960, 5)]
        public void ComputeColumnCount_AddsAColumnPerWholeMinColumnWidth(int gridWidth, int expected)
        {
            Assert.Equal(expected, SnapshotItemGridLayout.ComputeColumnCount(gridWidth));
        }

        [Fact]
        public void ComputeColumnCount_IsNotCappedAtTwo()
        {
            // The count is derived from the width the player gave the
            // window, not written down: an ultrawide window gets more
            // columns, and every one of them still clears MinColumnWidth.
            int columns = SnapshotItemGridLayout.ComputeColumnCount(3800);

            Assert.True(columns >= 4);
            Assert.True(SnapshotItemGridLayout.ComputeColumnWidth(3800) >= SnapshotItemGridLayout.SnapshotMinColumnWidth);
        }

        [Fact]
        public void ComputeGridWidth_NeverGoesNegative()
        {
            Assert.Equal(0, SnapshotItemGridLayout.ComputeGridWidth(0));
            Assert.Equal(0, SnapshotItemGridLayout.ComputeGridWidth(SnapshotItemGridLayout.ScrollbarAllowance));
            Assert.Equal(0, SnapshotItemGridLayout.ComputeGridWidth(-500));
            Assert.Equal(80, SnapshotItemGridLayout.ComputeGridWidth(100));
        }

        [Fact]
        public void ComputeColumnWidth_ZeroOrNegativeWidth_IsZero()
        {
            Assert.Equal(0, SnapshotItemGridLayout.ComputeColumnWidth(0));
            Assert.Equal(0, SnapshotItemGridLayout.ComputeColumnWidth(-40));
        }

        [Fact]
        public void Compute_PacksInReadingOrder()
        {
            var grid = SnapshotItemGridLayout.Compute(5, 1200, 52);

            Assert.Equal(2, grid.ColumnCount);
            Assert.Equal(600, grid.ColumnWidth);
            Assert.Equal(3, grid.RowCount);
            Assert.Equal(156, grid.Height);

            // Left-to-right first, then down - NOT column-major, so the
            // wallet run still reads after the item run above it.
            Assert.Equal((0, 0, 0, 0), Cell(grid, 0));
            Assert.Equal((600, 0, 1, 0), Cell(grid, 1));
            Assert.Equal((0, 52, 0, 1), Cell(grid, 2));
            Assert.Equal((600, 52, 1, 1), Cell(grid, 3));
            Assert.Equal((0, 104, 0, 2), Cell(grid, 4));
        }

        [Fact]
        public void Compute_NarrowPanel_IsOneCellPerRow()
        {
            var grid = SnapshotItemGridLayout.Compute(3, 600, 36);

            Assert.Equal(1, grid.ColumnCount);
            Assert.Equal(600, grid.ColumnWidth);
            Assert.Equal(3, grid.RowCount);
            Assert.Equal(108, grid.Height);

            Assert.Equal((0, 0, 0, 0), Cell(grid, 0));
            Assert.Equal((0, 36, 0, 1), Cell(grid, 1));
            Assert.Equal((0, 72, 0, 2), Cell(grid, 2));
        }

        [Fact]
        public void Compute_ThreeColumns_FillsTheLastRowPartially()
        {
            var grid = SnapshotItemGridLayout.Compute(4, 1800, 52);

            Assert.Equal(3, grid.ColumnCount);
            Assert.Equal(600, grid.ColumnWidth);
            Assert.Equal(2, grid.RowCount);
            Assert.Equal(104, grid.Height);
            Assert.Equal((0, 52, 0, 1), Cell(grid, 3));
        }

        [Fact]
        public void WalletRunIsOffsetBelowTheItemRun_AtTheSameColumnCount()
        {
            // How MainView composes the two runs into one grid panel: the
            // items first, the wallet at the item run's own height, so the
            // reading order of the single-column list survives the repack.
            var items = SnapshotItemGridLayout.Compute(3, 1310, 52);
            var wallet = SnapshotItemGridLayout.Compute(3, 1310, 36, items.Height);

            Assert.Equal(2, items.ColumnCount);
            Assert.Equal(items.ColumnCount, wallet.ColumnCount);
            Assert.Equal(items.ColumnWidth, wallet.ColumnWidth);

            // Items occupy two rows of 52; the wallet run starts under them.
            Assert.Equal(104, items.Height);
            Assert.Equal(104, wallet.Cells[0].Y);
            Assert.Equal(104, wallet.Cells[1].Y);
            Assert.Equal(140, wallet.Cells[2].Y);

            // The offset never leaks into the section's own height, which is
            // what the grid panel's total is summed from.
            Assert.Equal(72, wallet.Height);
            Assert.Equal(176, items.Height + wallet.Height);

            // Highest cell bottom edge stays inside that total.
            Assert.Equal(176, wallet.Cells[2].Y + 36);
        }

        [Fact]
        public void EmptyItemRun_LeavesTheWalletRunAtTheTop()
        {
            var items = SnapshotItemGridLayout.Compute(0, 1310, 52);
            var wallet = SnapshotItemGridLayout.Compute(2, 1310, 36, items.Height);

            Assert.Equal(0, items.Height);
            Assert.Equal(0, wallet.Cells[0].Y);
            Assert.Equal(0, wallet.Cells[1].Y);
            Assert.Equal(655, wallet.Cells[1].X);
        }

        [Fact]
        public void Compute_EmptyOrNegativeCount_IsAnEmptyZeroHeightGrid()
        {
            foreach (int count in new[] { 0, -3 })
            {
                var grid = SnapshotItemGridLayout.Compute(count, 1310, 52);

                Assert.Empty(grid.Cells);
                Assert.Equal(0, grid.RowCount);
                Assert.Equal(0, grid.Height);
            }
        }

        [Fact]
        public void Compute_ZeroRowHeight_PlacesEveryRowAtZero()
        {
            var grid = SnapshotItemGridLayout.Compute(4, 1200, 0);

            Assert.Equal(0, grid.Height);
            Assert.Equal(0, grid.Cells[3].Y);
            Assert.Equal(1, grid.Cells[3].Row);
        }

        [Fact]
        public void Compute_DegenerateWidth_StillPlacesOneCellPerRow()
        {
            // A window dragged to nothing must not divide by zero or stack
            // every cell on top of the first.
            var grid = SnapshotItemGridLayout.Compute(2, 0, 52);

            Assert.Equal(1, grid.ColumnCount);
            Assert.Equal(0, grid.ColumnWidth);
            Assert.Equal(2, grid.RowCount);
            Assert.Equal((0, 52, 0, 1), Cell(grid, 1));
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 52)]
        [InlineData(2, 52)]
        [InlineData(3, 104)]
        [InlineData(4, 104)]
        [InlineData(5, 156)]
        public void ComputeHeight_MatchesTheGridItPlaces(int count, int expected)
        {
            Assert.Equal(expected, SnapshotItemGridLayout.ComputeHeight(count, 1200, 52));
            Assert.Equal(expected, SnapshotItemGridLayout.Compute(count, 1200, 52).Height);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(2, 1)]
        [InlineData(3, 2)]
        public void ColumnDividerCount_LeavesTheLastColumnUnruled(int columnCount, int expected)
        {
            Assert.Equal(expected, SnapshotItemGridLayout.ColumnDividerCount(columnCount));
        }

        [Fact]
        public void ColumnDividerX_ClearsBothCellsItSitsBetween()
        {
            const int ColumnWidth = 626;

            int x = SnapshotItemGridLayout.ColumnDividerX(0, ColumnWidth);
            int right = x + SnapshotItemGridLayout.ColumnDividerWidth;

            // Left of it, the first cell's text ends; right of it, the
            // second cell's Amount column begins. The rule touches neither.
            Assert.True(x >= SnapshotItemGridLayout.CellContentRightEdge(ColumnWidth));
            Assert.True(right <= ColumnWidth + SnapshotItemGridLayout.CellAmountX);
        }

        [Fact]
        public void ColumnDividerX_SitsOnEveryColumnBoundary()
        {
            const int ColumnWidth = 626;

            int first = SnapshotItemGridLayout.ColumnDividerX(0, ColumnWidth);
            int second = SnapshotItemGridLayout.ColumnDividerX(1, ColumnWidth);

            Assert.Equal(ColumnWidth, second - first);
        }

        private static (int, int, int, int) Cell(SnapshotItemGridLayout.Grid grid, int index)
        {
            var cell = grid.Cells[index];
            return (cell.X, cell.Y, cell.Column, cell.Row);
        }
    }
}
