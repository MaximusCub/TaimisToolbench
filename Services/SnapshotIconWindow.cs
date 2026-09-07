namespace TaimisToolbench.Services
{
    /// <summary>
    /// Which cells of one result grid are close enough to the viewport for
    /// their icon art to be worth requesting now.
    /// <para>
    /// The Snapshot tab draws one row per distinct item. The owner's live
    /// snapshot holds 1039 item entries over 910 distinct item ids, plus 53
    /// wallet currencies: 963 rows, and 927 distinct picture urls, asked for
    /// in one burst the moment the unfiltered list is built. This picks the
    /// near ones so the rest can wait until they are scrolled toward.
    /// </para>
    /// <para>
    /// Cells are numbered in PLACEMENT order, the order
    /// <see cref="SnapshotItemGridLayout.Grid.Cells"/> is indexed in, so
    /// index / columnCount is the cell's row and every cell in one row shares
    /// a top edge. That makes the answer a contiguous span, which is why this
    /// returns two integers instead of a set.
    /// </para>
    /// </summary>
    internal static class SnapshotIconWindow
    {
        /// <summary>
        /// Pixels above and below the viewport still counted as near. 240px
        /// is four 56px item rows, so an ordinary wheel notch lands on art
        /// that was already asked for.
        /// </summary>
        public const int MarginPx = 240;

        /// <summary>
        /// Pictures one frame of the background prime asks for, once the
        /// viewport's own are on their way.
        /// <para>
        /// MEASURED: the frame-thread half of one
        /// request - the <c>Directory.CreateDirectory</c> and
        /// <c>File.Exists</c> Blish runs before its work goes async - costs
        /// 0.015ms over 1000 probes of the real asset cache, hit or miss. 16
        /// of those is 0.24ms, under 1.5% of a 16.67ms frame, which leaves
        /// room for the rest of the call that was not measured: the texture
        /// allocation and starting the load task.
        /// </para>
        /// <para>
        /// Asking faster would not finish sooner. Warm, every request queues
        /// a decode that takes one process-wide graphics device lock at low
        /// priority, so the lock is the limit. Cold, the transport is: 16 a
        /// frame issues a 927-picture prime inside a second, and the
        /// connection pool takes tens of seconds to drain it.
        /// </para>
        /// </summary>
        public const int PrimePerFrame = 16;

        /// <summary>
        /// Pictures one frame asks for while the Snapshot tab is NOT the tab
        /// on screen, so its icons are already asked for the first time it
        /// is opened. Asked for, not necessarily arrived: on a cold asset
        /// cache the answers come back at the rate
        /// <see cref="IconAssetConnectionLimit.ConnectionsPerServer"/>
        /// allows, which is slower than this.
        /// <para>
        /// A quarter of <see cref="PrimePerFrame"/>, because this spends
        /// frames the player is giving to some other tab. At the 0.015ms per
        /// request measured above that is 0.06ms, under 0.4% of a 16.67ms
        /// frame. The 927 pictures of the owner's snapshot take about 232
        /// frames at this rate, or four seconds at 60fps - less than it
        /// takes to open the window and reach the tab.
        /// </para>
        /// </summary>
        public const int BackgroundPrimePerFrame = 4;

        /// <summary>Frames a prime of <paramref name="pictures"/> takes at
        /// <see cref="PrimePerFrame"/>.</summary>
        public static int PrimeFrames(int pictures)
        {
            return PrimeFrames(pictures, PrimePerFrame);
        }

        /// <summary>Frames a prime of <paramref name="pictures"/> takes at
        /// <paramref name="perFrame"/> pictures a frame.</summary>
        public static int PrimeFrames(int pictures, int perFrame)
        {
            if (pictures <= 0 || perFrame <= 0)
            {
                return 0;
            }

            return ((pictures - 1) / perFrame) + 1;
        }

        /// <summary>A contiguous run of placement indices, or an empty
        /// one.</summary>
        public readonly struct Span
        {
            /// <summary>First placement index in the run. 0 when the run is
            /// empty; read <see cref="Count"/> first.</summary>
            public readonly int Start;

            public readonly int Count;

            public Span(int start, int count)
            {
                Start = start;
                Count = count;
            }

            /// <summary>One past the last placement index in the run.</summary>
            public int End
            {
                get { return Start + Count; }
            }
        }

        /// <summary>
        /// The placement indices whose cells intersect the viewport grown by
        /// <paramref name="marginPx"/> on both edges.
        /// </summary>
        /// <param name="gridTop">Y of the section's first row, in the same
        /// coordinates as <paramref name="viewTop"/>. This is
        /// <see cref="SnapshotItemGridLayout.Grid.Top"/>.</param>
        /// <param name="viewTop">Y of the viewport's top edge in those same
        /// coordinates. It grows as the reader scrolls down.</param>
        /// <returns>An empty span when the section has no cells, when the
        /// viewport has no height yet, or when the section is entirely off
        /// screen.</returns>
        public static Span Compute(
            int cellCount, int columnCount, int rowHeight, int gridTop,
            int viewTop, int viewHeight, int marginPx)
        {
            if (cellCount <= 0 || columnCount <= 0 || rowHeight <= 0 || viewHeight <= 0)
            {
                return new Span(0, 0);
            }

            int margin = marginPx > 0 ? marginPx : 0;
            long nearTop = (long)viewTop - margin;
            long nearBottom = (long)viewTop + viewHeight + margin;

            // A cell spans [top, top + rowHeight). It is near when that
            // half-open run overlaps [nearTop, nearBottom), so the last near
            // row is the one holding nearBottom - 1.
            long firstRow = FloorDiv(nearTop - gridTop, rowHeight);
            long lastRow = FloorDiv(nearBottom - 1 - gridTop, rowHeight);

            int rowCount = GridLayout.RowCount(cellCount, columnCount);
            if (firstRow < 0)
            {
                firstRow = 0;
            }

            if (lastRow > rowCount - 1)
            {
                lastRow = rowCount - 1;
            }

            if (firstRow > lastRow)
            {
                return new Span(0, 0);
            }

            int start = (int)(firstRow * columnCount);
            long endExclusive = (lastRow + 1) * (long)columnCount;
            if (endExclusive > cellCount)
            {
                endExclusive = cellCount;
            }

            return new Span(start, (int)(endExclusive - start));
        }

        /// <summary>
        /// Integer division that rounds toward negative infinity. C# rounds
        /// toward zero, which would put a cell straddling y=0 in row 0 and a
        /// cell just above it in row 0 as well.
        /// </summary>
        private static long FloorDiv(long numerator, long denominator)
        {
            long quotient = numerator / denominator;
            if (numerator % denominator != 0 && (numerator < 0) != (denominator < 0))
            {
                quotient--;
            }

            return quotient;
        }
    }
}
