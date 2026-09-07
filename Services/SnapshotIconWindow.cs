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
