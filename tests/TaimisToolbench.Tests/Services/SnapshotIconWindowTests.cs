using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Which result cells are near enough to the viewport for their icon
    /// picture to be worth asking Blish for. The numbers are the Snapshot
    /// tab's own: 56px item rows, two columns at the 1252px grid the 1378px
    /// window minimum leaves.
    /// </summary>
    public class SnapshotIconWindowTests
    {
        private const int RowHeight = 56;
        private const int Columns = 2;
        private const int ViewHeight = 500;

        private static SnapshotIconWindow.Span Compute(
            int cellCount, int viewTop, int margin = 0, int gridTop = 0, int columns = Columns)
        {
            return SnapshotIconWindow.Compute(
                cellCount, columns, RowHeight, gridTop, viewTop, ViewHeight, margin);
        }

        [Fact]
        public void AtTheTop_TakesTheRowsTheViewportShows_AndNoMore()
        {
            // 500px of viewport over 56px rows covers rows 0..8 (row 8 spans
            // 448..504), so 9 rows of 2 columns.
            var span = Compute(cellCount: 1000, viewTop: 0);

            Assert.Equal(0, span.Start);
            Assert.Equal(18, span.Count);
        }

        [Fact]
        public void ScrolledDown_StartsAtTheFirstRowStillShowing()
        {
            // viewTop 560 is exactly the top of row 10.
            var span = Compute(cellCount: 1000, viewTop: 560);

            Assert.Equal(20, span.Start);
            Assert.Equal(18, span.Count);
        }

        [Fact]
        public void APartlyShownRowIsIncluded()
        {
            // viewTop 570 cuts row 10 (560..616) ten pixels down, so row 10
            // is still on screen and must still be asked for.
            var span = Compute(cellCount: 1000, viewTop: 570);

            Assert.Equal(20, span.Start);
        }

        [Fact]
        public void TheMarginReachesRowsAboveAndBelowTheViewport()
        {
            var withoutMargin = Compute(cellCount: 1000, viewTop: 1120);
            var withMargin = Compute(cellCount: 1000, viewTop: 1120, margin: 240);

            Assert.Equal(40, withoutMargin.Start);
            // 240px is four whole 56px rows plus a slice of a fifth, above
            // and below.
            Assert.Equal(30, withMargin.Start);
            Assert.True(withMargin.End > withoutMargin.End);
        }

        [Fact]
        public void NeverReachesPastTheLastCell()
        {
            // 5 cells over 2 columns is 3 rows; the last row holds one cell.
            var span = Compute(cellCount: 5, viewTop: 0);

            Assert.Equal(0, span.Start);
            Assert.Equal(5, span.Count);
            Assert.Equal(5, span.End);
        }

        [Fact]
        public void NeverStartsBeforeTheFirstCell()
        {
            // Scrolled above the section, which happens to the wallet run
            // while the reader is still in the items above it.
            var span = Compute(cellCount: 1000, viewTop: -2000, margin: 240);

            Assert.Equal(0, span.Start);
            Assert.Equal(0, span.Count);
        }

        [Fact]
        public void ASectionScrolledPastIsEmpty()
        {
            var span = Compute(cellCount: 10, viewTop: 4000, margin: 240);

            Assert.Equal(0, span.Count);
        }

        [Fact]
        public void ASectionScrolledOffTheTopIsEmpty()
        {
            // The section's rows end at 10 * 56 = 560; the viewport starts
            // at 900, so nothing of it is left even with the margin.
            var span = Compute(cellCount: 20, viewTop: 900, margin: 240);

            Assert.Equal(0, span.Count);
        }

        [Fact]
        public void ASectionBelowTheItemsIsOffsetByItsOwnTop()
        {
            // The wallet run starts 4000px down the grid. Scrolled to it,
            // the first of its cells is the one to ask for.
            var span = Compute(cellCount: 60, viewTop: 4000, gridTop: 4000);

            Assert.Equal(0, span.Start);
            Assert.Equal(18, span.Count);
        }

        [Fact]
        public void AWiderWindowAsksForMoreCellsPerRow()
        {
            var two = Compute(cellCount: 1000, viewTop: 0, columns: 2);
            var three = Compute(cellCount: 1000, viewTop: 0, columns: 3);

            Assert.Equal(18, two.Count);
            Assert.Equal(27, three.Count);
        }

        [Fact]
        public void ItAsksForFarLessThanTheWholeList()
        {
            // The owner's live snapshot: 910 distinct items plus 53
            // currencies. This is the whole point of the class.
            var span = Compute(cellCount: 910, viewTop: 0, margin: 240);

            Assert.True(span.Count < 100, $"asked for {span.Count} of 910");
        }

        [Fact]
        public void NoCellsMeansNoRequests()
        {
            Assert.Equal(0, Compute(cellCount: 0, viewTop: 0).Count);
        }

        [Fact]
        public void AViewportWithNoHeightYetAsksForNothing()
        {
            // The first rebuild can land before the panel has been laid out.
            var span = SnapshotIconWindow.Compute(
                cellCount: 1000, columnCount: Columns, rowHeight: RowHeight, gridTop: 0,
                viewTop: 0, viewHeight: 0, marginPx: 240);

            Assert.Equal(0, span.Count);
        }

        [Fact]
        public void DegenerateGeometryAsksForNothingRatherThanThrowing()
        {
            Assert.Equal(0, SnapshotIconWindow.Compute(10, 0, RowHeight, 0, 0, ViewHeight, 0).Count);
            Assert.Equal(0, SnapshotIconWindow.Compute(10, Columns, 0, 0, 0, ViewHeight, 0).Count);
            Assert.Equal(0, SnapshotIconWindow.Compute(-5, Columns, RowHeight, 0, 0, ViewHeight, 0).Count);
        }

        [Fact]
        public void ANegativeMarginIsTreatedAsNone()
        {
            var none = SnapshotIconWindow.Compute(10, Columns, RowHeight, 0, 0, ViewHeight, 0);
            var negative = SnapshotIconWindow.Compute(10, Columns, RowHeight, 0, 0, ViewHeight, -50);

            Assert.Equal(none.Start, negative.Start);
            Assert.Equal(none.Count, negative.Count);
        }

        [Fact]
        public void PrimeFrames_CoversEveryPictureIncludingAPartialLastFrame()
        {
            int perFrame = SnapshotIconWindow.PrimePerFrame;

            Assert.Equal(0, SnapshotIconWindow.PrimeFrames(0));
            Assert.Equal(1, SnapshotIconWindow.PrimeFrames(1));
            Assert.Equal(1, SnapshotIconWindow.PrimeFrames(perFrame));
            Assert.Equal(2, SnapshotIconWindow.PrimeFrames(perFrame + 1));
            Assert.Equal(3, SnapshotIconWindow.PrimeFrames(perFrame * 3));
        }

        [Fact]
        public void PrimeFrames_IsNeverShortOfWhatTheWalkNeeds()
        {
            // The estimate is what the tab tells the log, so it must not
            // under-report: every picture has to fit inside the frames it
            // claims.
            for (int pictures = 0; pictures <= 2000; pictures++)
            {
                int frames = SnapshotIconWindow.PrimeFrames(pictures);

                Assert.True(
                    frames * SnapshotIconWindow.PrimePerFrame >= pictures,
                    $"{frames} frames cannot cover {pictures} pictures");
            }
        }

        [Fact]
        public void PrimeFrames_NeverGoesBackwards()
        {
            int previous = 0;
            for (int pictures = 0; pictures <= 2000; pictures++)
            {
                int frames = SnapshotIconWindow.PrimeFrames(pictures);

                Assert.True(frames >= previous, $"{pictures} pictures reported fewer frames");
                previous = frames;
            }
        }

        [Fact]
        public void PrimeFrames_NegativeCountIsNoWork()
        {
            Assert.Equal(0, SnapshotIconWindow.PrimeFrames(-1));
            Assert.Equal(0, SnapshotIconWindow.PrimeFrames(int.MinValue));
        }

        [Fact]
        public void EveryCellTheViewportShowsIsInTheSpan()
        {
            // Drives the whole scroll of a 963-cell list past a 500px
            // viewport and asserts the span covers every cell that overlaps
            // it - the property the class exists to hold.
            const int cellCount = 963;
            for (int viewTop = -300; viewTop < 30000; viewTop += 37)
            {
                var span = SnapshotIconWindow.Compute(
                    cellCount, Columns, RowHeight, 0, viewTop, ViewHeight, 0);

                for (int i = 0; i < cellCount; i++)
                {
                    int top = (i / Columns) * RowHeight;
                    bool onScreen = top < viewTop + ViewHeight && top + RowHeight > viewTop;
                    if (onScreen)
                    {
                        Assert.True(
                            i >= span.Start && i < span.End,
                            $"cell {i} shows at viewTop {viewTop} but the span is [{span.Start}, {span.End})");
                    }
                }
            }
        }
    }
}
