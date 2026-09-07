using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The Snapshot header's shared formulas, plus the two properties that
    /// motivated them: a source-filter run that fits beside the search box
    /// costs the header no height at all, and a run that does NOT fit there
    /// drops to its own full-width row rather than being squeezed into half
    /// the width and hidden behind the cap's scrollbar. Exercises the real
    /// SnapshotHeaderLayout and the real SourceFilterFlowLayout together -
    /// the width handed to the flow is the only thing that changed about it,
    /// so what matters is what the flow does when handed each one.
    /// </summary>
    public class SnapshotHeaderLayoutTests
    {
        // MainView's own constants for the rows this shares.
        private const int SearchRowHeight = 35;
        private const int SourceFilterX = 470;
        private const int SearchToFilterGapY = 3;
        private const int CellHeight = 25;
        private const int CellGapX = 10;
        private const int RowGapY = 4;
        private const int TopPad = 3;
        private const int BottomPad = 2;

        private static int FilterHeight(SourceFilterFlowResult flow)
        {
            return TopPad + flow.TotalHeight + BottomPad;
        }

        private static SnapshotHeaderLayout.SourceFilterPlacement Place(int panelWidth, bool shares)
        {
            return SnapshotHeaderLayout.PlaceSourceFilterRun(
                panelWidth, SourceFilterX, SearchRowHeight, SearchToFilterGapY, shares);
        }

        private static SourceFilterFlowResult Flow(IReadOnlyList<int> widths, int availableWidth)
        {
            return SourceFilterFlowLayout.Layout(widths, availableWidth, CellHeight, CellGapX, RowGapY);
        }

        /// <summary>
        /// The two-pass resolution MainView.ApplyTopRegionLayout runs: flow
        /// beside the search box, and re-flow on the run's own row when that
        /// wrapped it.
        /// </summary>
        private static (SnapshotHeaderLayout.SourceFilterPlacement Placement, SourceFilterFlowResult Flow) Resolve(
            IReadOnlyList<int> widths, int panelWidth)
        {
            var placement = Place(panelWidth, shares: true);
            var flow = Flow(widths, placement.Width);
            if (SnapshotHeaderLayout.SharesSearchRow(flow.RowCount))
            {
                return (placement, flow);
            }

            placement = Place(panelWidth, shares: false);
            return (placement, Flow(widths, placement.Width));
        }

        [Fact]
        public void SourceFilterWidth_EndsOnTheChromeRightEdgeNotTheRawPanel()
        {
            // Was panelWidth - startX (414 here), which ran the run past
            // the edge every other chrome element on this tab pins to and
            // moved its own wrap threshold with it.
            Assert.Equal(
                SnapshotHeaderLayout.ChromeRightEdge(884) - SourceFilterX,
                SnapshotHeaderLayout.SourceFilterWidth(884, SourceFilterX));
        }

        [Fact]
        public void SourceFilterWidth_PanelNarrowerThanTheOffset_FloorsAtZero()
        {
            Assert.Equal(0, SnapshotHeaderLayout.SourceFilterWidth(300, SourceFilterX));
        }

        [Fact]
        public void SearchBandHeight_FilterFitsBesideTheSearchBox_CostsNothing()
        {
            // One row of checkboxes (30px) is shorter than the search row it
            // shares, so the band - and therefore every row below it - is
            // exactly where it would be with no filter row at all.
            Assert.Equal(
                SearchRowHeight,
                SnapshotHeaderLayout.SearchBandHeight(SearchRowHeight, 30, Place(1400, shares: true)));
        }

        [Fact]
        public void SearchBandHeight_RunOnItsOwnRow_CostsTheSearchRowPlusTheGapPlusItself()
        {
            // The pre-share layout, reproduced exactly: the run starts one
            // gap below the search row and the band covers both.
            Assert.Equal(
                SearchRowHeight + SearchToFilterGapY + 30,
                SnapshotHeaderLayout.SearchBandHeight(SearchRowHeight, 30, Place(1400, shares: false)));
        }

        [Fact]
        public void PlaceSourceFilterRun_OwnRow_SitsInsideTheTabsFrameBelowTheSearchRow()
        {
            var placement = Place(884, shares: false);

            Assert.False(placement.SharesSearchRow);

            // Was x=0 spanning the raw panel - sixteen pixels left of the
            // search box directly above it, and past the shared right edge.
            Assert.Equal(SnapshotHeaderLayout.SnapshotHeaderInset, placement.X);
            Assert.Equal(
                SnapshotHeaderLayout.ChromeRightEdge(884),
                placement.X + placement.Width);
            Assert.Equal(SearchRowHeight + SearchToFilterGapY, placement.OffsetY);
        }

        [Fact]
        public void SingleRowRoster_SharesTheSearchRowForFree()
        {
            // Three storage checkboxes and one character: fits in the right
            // half at a wide window, so the header spends zero extra pixels
            // on the filter.
            var resolved = Resolve(new List<int> { 70, 150, 150, 110 }, 1400);

            Assert.True(resolved.Placement.SharesSearchRow);
            Assert.Equal(1, resolved.Flow.RowCount);
            Assert.Equal(
                SearchRowHeight,
                SnapshotHeaderLayout.SearchBandHeight(
                    SearchRowHeight, FilterHeight(resolved.Flow), resolved.Placement));
        }

        [Fact]
        public void RosterThatWouldWrapBesideTheSearchBox_TakesItsOwnRowInstead()
        {
            // The 15-character account the audit measured: 19 cells at the
            // 884px panel the audit ran at. 884 was never the minimum tab
            // panel - it is the window's content region, before the
            // ViewAdapter's own 60px of padding (see
            // SettingsCurrencyGridLayoutTests' chrome derivation) - and the
            // minimum panel is 1310px since the window minimum moved to
            // 1436. Kept as the narrow sample the audit's cell counts were
            // taken at; the assertions below are about the wrap decision at
            // whatever width they are handed. Beside the search box that is
            // several rows deep; on its own row it is far fewer, which is the
            // whole point - the same cells, visible rather than scrolled.
            var widths = new List<int> { 70, 150, 150, 140 };
            for (int i = 0; i < 15; i++)
            {
                widths.Add(124);
            }

            var beside = Flow(widths, Place(884, shares: true).Width);
            var resolved = Resolve(widths, 884);

            Assert.False(SnapshotHeaderLayout.SharesSearchRow(beside.RowCount));
            Assert.False(resolved.Placement.SharesSearchRow);
            Assert.True(resolved.Flow.RowCount < beside.RowCount);
            Assert.Equal(widths.Count, resolved.Flow.Cells.Count);
        }

        [Fact]
        public void OwnRowFallback_FitsInsideTheFourRowCapWhereSharingWouldNot()
        {
            // The failure the fallback exists for: at the shared width this
            // roster runs past the 4-row cap, so a third of the filters end
            // up behind a scrollbar inside a 117px box. At full width it fits
            // the cap outright.
            const int maxRows = 4;
            var widths = new List<int> { 70, 150, 150, 140 };
            for (int i = 0; i < 15; i++)
            {
                widths.Add(124);
            }

            var beside = Flow(widths, Place(884, shares: true).Width);
            var resolved = Resolve(widths, 884);

            Assert.True(beside.RowCount > maxRows);
            Assert.True(resolved.Flow.RowCount <= maxRows);
        }

        [Fact]
        public void LargeRoster_StillWrapsAndNothingIsDropped()
        {
            var widths = new List<int> { 70, 150, 150, 140 };
            for (int i = 0; i < 12; i++)
            {
                widths.Add(120);
            }

            var resolved = Resolve(widths, 884);

            // Every cell still placed (nothing dropped), and the band grows
            // by exactly the run's own height plus the row it now owns.
            Assert.Equal(widths.Count, resolved.Flow.Cells.Count);
            int band = SnapshotHeaderLayout.SearchBandHeight(
                SearchRowHeight, FilterHeight(resolved.Flow), resolved.Placement);
            Assert.Equal(
                SearchRowHeight + SearchToFilterGapY + FilterHeight(resolved.Flow), band);
        }

        [Fact]
        public void ZeroWidth_PlacesEveryCellOnItsOwnRowRatherThanDroppingAny()
        {
            // The degenerate window: SourceFilterWidth floors at 0, and the
            // flow degrades to one cell per row - wrapped and then scrolled,
            // never clipped away.
            var flow = Flow(
                new List<int> { 70, 150, 150 },
                SnapshotHeaderLayout.SourceFilterWidth(300, SourceFilterX));

            Assert.Equal(3, flow.RowCount);
            Assert.Equal(3, flow.Cells.Count);
        }

        [Fact]
        public void StatusRow_HoldsTheStatusTierItIsDrawnIn()
        {
            // The Status tier's ink runs 2px deeper than Body's, and a band
            // left at the old height clips a descender - so this asserts
            // against the measured ink, not the old literal.
            const int labelY = 2;

            Assert.True(
                TypeRampMetrics.InkBottom(TypeRampMetrics.StatusInk, labelY)
                    < SnapshotHeaderLayout.StatusRowHeight,
                "the status row must clear its own tier's lowest ink");
        }

        [Fact]
        public void NoCellsYet_SharesTheSearchRow()
        {
            // The state before the first snapshot lands: nothing to flow, so
            // nothing to move off the search row.
            var flow = Flow(new List<int>(), Place(884, shares: true).Width);

            Assert.Equal(0, flow.RowCount);
            Assert.True(SnapshotHeaderLayout.SharesSearchRow(flow.RowCount));
        }

        // ---- One right edge for the whole tab ----
        private static readonly int ContainerAtWindowMinimum =
            WindowSizing.TabPanelWidthFor(WindowSizing.MinWindowWidth);

        [Theory]
        [InlineData(1252)]
        [InlineData(1632)]
        [InlineData(2540)]
        public void ChromeRightEdge_IsTheSameEdgeTheGridsLastColumnEndsOn(int containerWidth)
        {
            // The whole point of the change. The grid is laid out inside
            // ComputeGridWidth(container) and its rightmost column's own
            // right edge is PinnedRightEdge of that - so the header buttons,
            // the coin block and the last column land on ONE line.
            int gridWidth = SnapshotItemGridLayout.ComputeGridWidth(containerWidth);
            int columnCount = SnapshotItemGridLayout.ComputeColumnCount(gridWidth);
            int columnWidth = SnapshotItemGridLayout.ComputeColumnWidth(gridWidth);

            int lastColumnRightEdge =
                ((columnCount - 1) * columnWidth)
                + SnapshotItemGridLayout.CellContentRightEdge(columnWidth);

            Assert.Equal(
                PlanRelayoutMath.PinnedRightEdge(gridWidth),
                SnapshotHeaderLayout.ChromeRightEdge(containerWidth));

            // Integer column division can leave a remainder the grid does
            // not use; the chrome edge is never LEFT of the last column.
            Assert.True(SnapshotHeaderLayout.ChromeRightEdge(containerWidth) >= lastColumnRightEdge);
            Assert.True(
                SnapshotHeaderLayout.ChromeRightEdge(containerWidth) - lastColumnRightEdge < columnCount);
        }

        [Fact]
        public void ChromeRightEdge_IsNotTheContainersOwnEdge()
        {
            // It used to be: the buttons pinned to containerWidth - 10 while
            // the grid ended at containerWidth - 28, eighteen pixels apart
            // on the same tab at every width.
            Assert.Equal(
                ContainerAtWindowMinimum
                    - WindowSizing.ScrollbarAllowance - PlanRelayoutMath.TableRightMargin,
                SnapshotHeaderLayout.ChromeRightEdge(ContainerAtWindowMinimum));
        }

        [Fact]
        public void CoinBlockIsRightPinnedAsAUnit()
        {
            const int BlockWidth = 200;

            Assert.Equal(
                SnapshotHeaderLayout.ChromeRightEdge(ContainerAtWindowMinimum),
                SnapshotHeaderLayout.CoinBlockX(ContainerAtWindowMinimum, BlockWidth) + BlockWidth);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-40)]
        public void CoinBlockX_AbsentBlock_SitsOnTheEdgeItself(int blockWidth)
        {
            Assert.Equal(
                SnapshotHeaderLayout.ChromeRightEdge(ContainerAtWindowMinimum),
                SnapshotHeaderLayout.CoinBlockX(ContainerAtWindowMinimum, blockWidth));
        }

        [Fact]
        public void ResultLineStopsBeforeTheCoinBlockWithTheModulesOwnGap()
        {
            const int BlockWidth = 200;

            int budget = SnapshotHeaderLayout.ResultLineMaxWidth(ContainerAtWindowMinimum, BlockWidth);

            Assert.Equal(
                SnapshotHeaderLayout.CoinBlockX(ContainerAtWindowMinimum, BlockWidth)
                    - SnapshotHeaderLayout.ResultLineToCoinGap - SnapshotHeaderLayout.SnapshotHeaderInset,
                budget);
            Assert.Equal(
                PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    SnapshotHeaderLayout.ChromeRightEdge(ContainerAtWindowMinimum),
                    BlockWidth,
                    SnapshotHeaderLayout.ResultLineToCoinGap,
                    SnapshotHeaderLayout.SnapshotHeaderInset),
                budget);
        }

        [Fact]
        public void ResultLineTakesEveryPixelAWiderWindowAdds()
        {
            const int BlockWidth = 200;

            int narrow = SnapshotHeaderLayout.ResultLineMaxWidth(1252, BlockWidth);
            int wide = SnapshotHeaderLayout.ResultLineMaxWidth(2252, BlockWidth);

            Assert.Equal(1000, wide - narrow);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-500)]
        public void ResultLineMaxWidth_FloorsRatherThanGoingNegative(int containerWidth)
        {
            Assert.True(SnapshotHeaderLayout.ResultLineMaxWidth(containerWidth, 200) >= 20);
        }

        [Fact]
        public void StatusLineReservesRoomForTheSpinnerTrailingIt()
        {
            int reserve = InlineSpinnerLayout.SnapshotStatusSize + InlineSpinnerLayout.LabelGap;

            Assert.Equal(
                SnapshotHeaderLayout.ChromeRightEdge(ContainerAtWindowMinimum)
                    - SnapshotHeaderLayout.SnapshotHeaderInset - reserve,
                SnapshotHeaderLayout.StatusMaxWidth(ContainerAtWindowMinimum, reserve));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-500)]
        public void StatusMaxWidth_FloorsRatherThanGoingNegative(int containerWidth)
        {
            Assert.True(SnapshotHeaderLayout.StatusMaxWidth(containerWidth, 26) >= 20);
        }

        [Fact]
        public void HeaderButtonGapIsTheModulesOwnButtonGap_NotTheTwentyItWas()
        {
            Assert.Equal(8, SnapshotHeaderLayout.HeaderButtonGap);
        }

        [Fact]
        public void SearchRowControlsShareOneOpticalCentre()
        {
            // The same rule the Log toolbar states, against this tab's own
            // 35px row - one implementation, not two.
            Assert.Equal(4, PlanRelayoutMath.CenterX(SearchRowHeight, 26));
            Assert.Equal(2, PlanRelayoutMath.CenterX(SearchRowHeight, 30));
            Assert.Equal(
                LogToolbarLayout.CenteredY(26),
                PlanRelayoutMath.CenterX(LogToolbarLayout.BarHeight, 26));
        }

        [Theory]
        [InlineData(1378)]
        [InlineData(1920)]
        [InlineData(930)]
        public void SourceFilterRun_StartsAtTheGutterAndEndsOnTheSharedRightEdge(int containerWidth)
        {
            // The run is the only content-driven width on this tab, so it
            // is the one that used to escape the tab's frame: own-row mode
            // began at x=0 (sixteen pixels left of the search box directly
            // above it) and both modes ran past the edge every other chrome
            // element pins to, which also moved the run's own wrap point.
            int chromeRight = SnapshotHeaderLayout.ChromeRightEdge(containerWidth);

            var ownRow = SnapshotHeaderLayout.PlaceSourceFilterRun(
                containerWidth, startX: 200, searchRowHeight: SearchRowHeight,
                rowGap: 6, sharesSearchRow: false);

            Assert.Equal(SnapshotHeaderLayout.SnapshotHeaderInset, ownRow.X);
            Assert.Equal(chromeRight, ownRow.X + ownRow.Width);

            var shared = SnapshotHeaderLayout.PlaceSourceFilterRun(
                containerWidth, startX: 200, searchRowHeight: SearchRowHeight,
                rowGap: 6, sharesSearchRow: true);

            Assert.Equal(200, shared.X);
            Assert.Equal(chromeRight, shared.X + shared.Width);
        }

        [Fact]
        public void BandControlY_CentresAControlInTheTabTitleBand()
        {
            // The tab's two actions moved out of an in-view header row that
            // repeated the tab's name and onto the title band itself, so
            // the band has to seat a button as well as its own title. 28 is
            // Views/Rendering/UiMetrics.ButtonHeight, which the Views layer
            // owns and this layer may not name.
            const int buttonHeight = 28;
            int y = SnapshotHeaderLayout.BandControlY(
                PlanContentHeightMath.TabTitleBandHeight, buttonHeight);

            Assert.Equal(
                PlanContentHeightMath.TabTitleBandHeight - buttonHeight - y,
                y);
            Assert.True(y > 0, "a button seated at the band's very top edge is not centred");
        }

        [Fact]
        public void BandControlY_FloorsAtZeroForAControlTallerThanItsBand()
        {
            // Control.Size ignores a negative component and a negative
            // Location would park the control above the band and be
            // clipped, so the degenerate case has to land inside the band
            // rather than outside it.
            Assert.Equal(0, SnapshotHeaderLayout.BandControlY(20, 40));
            Assert.Equal(0, SnapshotHeaderLayout.BandControlY(0, 28));
        }
    }
}
