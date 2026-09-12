namespace TaimisToolbench.Services
{
    /// <summary>
    /// THE distribution law every justified table in the module shares: a
    /// row's span is divided into N EQUAL tracks and each data column is
    /// CENTRED on its own track, so the columns spread across the panel
    /// instead of stacking against its right-hand edge.
    /// <para>
    /// TRACK COUNT is one per data column plus ONE for the label, which is
    /// the leftmost and the only flexing element. Data column i (0-based)
    /// right-aligns on <c>RightEdge(..., i + 1)</c>; the label fills from
    /// startX up to the first data column's reserved band, so it absorbs
    /// whatever that column does not need.
    /// </para>
    /// <para>
    /// A column's header and its cells centre on the same track, which is
    /// what makes a header sit over the values it names.
    /// <see cref="RightEdge"/> is kept for the bands that genuinely pin to
    /// an edge (buttons, coin runs that must not ragged-right against each
    /// other). Derivation: docs/ARCHITECTURE.md section S1.2.
    /// </para>
    /// </summary>
    internal static class JustifiedColumnTracks
    {
        /// <summary>
        /// Right edge of track <paramref name="index"/> (0-based) of
        /// <paramref name="trackCount"/> equal tracks spanning
        /// <paramref name="trackSpan"/> px from <paramref name="startX"/>.
        /// Integer-exact off the span rather than accumulated from a
        /// rounded track width, so the last track's edge lands exactly on
        /// the span's own end instead of a rounding pixel short of it.
        /// </summary>
        public static int RightEdge(int startX, int trackSpan, int trackCount, int index)
        {
            if (trackCount <= 0)
            {
                return startX;
            }

            return startX + (int)((long)trackSpan * (index + 1) / trackCount);
        }

        /// <summary>
        /// Left edge of track <paramref name="index"/> - the right edge of
        /// the track before it, so adjacent tracks share an edge exactly
        /// and no rounding gap opens between them.
        /// </summary>
        public static int LeftEdge(int startX, int trackSpan, int trackCount, int index)
        {
            if (trackCount <= 0)
            {
                return startX;
            }

            return startX + (int)((long)trackSpan * index / trackCount);
        }

        /// <summary>Width of track <paramref name="index"/>.</summary>
        public static int Width(int startX, int trackSpan, int trackCount, int index)
        {
            int left = LeftEdge(startX, trackSpan, trackCount, index);
            int right = RightEdge(startX, trackSpan, trackCount, index);
            return right > left ? right - left : 0;
        }

        /// <summary>
        /// X at which content of <paramref name="contentWidth"/> centres in
        /// track <paramref name="index"/>. Content wider than its track is
        /// pinned to the track's left edge rather than allowed to overhang
        /// symmetrically into both neighbours, so an overlong cell collides
        /// in one direction only and the column to its left stays readable.
        /// </summary>
        public static int CenteredX(
            int startX, int trackSpan, int trackCount, int index, int contentWidth)
        {
            int left = LeftEdge(startX, trackSpan, trackCount, index);
            int width = Width(startX, trackSpan, trackCount, index);
            if (contentWidth >= width)
            {
                return left;
            }

            return left + (width - contentWidth) / 2;
        }

        /// <summary>
        /// X at which content centres in a column's own reserved BAND rather
        /// than in an equal track: the same law, for a column whose band -
        /// not an equal share of the row - is the thing to centre in.
        /// Content wider than its band pins left, exactly as it does in a
        /// track. NOT the rule for a header, which centres over the cells'
        /// own extent instead - see <see cref="CenteredOverContent"/>; use
        /// this only where the band IS the content.
        /// </summary>
        public static int CenteredInBand(int bandX, int bandWidth, int contentWidth)
        {
            if (contentWidth >= bandWidth)
            {
                return bandX;
            }

            return bandX + (bandWidth - contentWidth) / 2;
        }

        /// <summary>
        /// Clearance kept between two headers that back off from the same
        /// boundary line. Each gives up half of it, so the pair can close
        /// to this and no further.
        /// </summary>
        public const int HeaderGutter = 6;

        /// <summary>
        /// The x range a header may occupy: bounded by the COLUMNS EITHER
        /// SIDE of it, never by its own reserved band. A band is sized to
        /// hold one column's widest cell and a header is routinely wider
        /// than that, so clamping a header into its band right-aligns the
        /// very header the centring was meant to move - the defect
        /// <see cref="CenteredOverContent"/> exists to remove. Columns
        /// hundreds of pixels apart have nothing to collide with and no
        /// clamp fires at all.
        /// <para>
        /// Build the bounds with <see cref="RoomLeftBound"/> /
        /// <see cref="RoomRightBound"/> where a neighbouring column exists
        /// and from the table's own edge where none does. Derivation:
        /// docs/ARCHITECTURE.md section S1.2.
        /// </para>
        /// </summary>
        public readonly struct HeaderRoom
        {
            public readonly int Left;
            public readonly int Right;

            private HeaderRoom(int left, int right)
            {
                Left = left;
                Right = right;
            }

            /// <summary>
            /// Room between two bounds. A right bound left of the left one
            /// - a table too narrow to hold the column at all - collapses
            /// onto the left bound rather than inverting.
            /// </summary>
            public static HeaderRoom Between(int left, int right)
            {
                return new HeaderRoom(left, right < left ? left : right);
            }

            /// <summary>Width of the room; never negative.</summary>
            public int Width
            {
                get { return Right - Left; }
            }
        }

        /// <summary>
        /// Leftmost x a header may reach: the middle of the gap between the
        /// column on its left and its own cells, plus half a
        /// <see cref="HeaderGutter"/>, so the two headers meeting over that
        /// gap keep a whole one between them.
        /// <paramref name="leftNeighborInkRight"/> is where that column
        /// stops drawing - its widest cell, or the ellipsis budget a
        /// flexing name column may fill. Never past
        /// <paramref name="ownInkLeft"/>: a column's own cells are always
        /// inside its header's room, however little gap precedes them.
        /// </summary>
        public static int RoomLeftBound(int leftNeighborInkRight, int ownInkLeft)
        {
            int bound = leftNeighborInkRight
                + ((ownInkLeft - leftNeighborInkRight) / 2) + (HeaderGutter / 2);
            return bound > ownInkLeft ? ownInkLeft : bound;
        }

        /// <summary>
        /// <see cref="RoomLeftBound"/> mirrored: the rightmost x a header's
        /// right edge may reach before the column on its right, and never
        /// short of <paramref name="ownInkRight"/>.
        /// </summary>
        public static int RoomRightBound(int ownInkRight, int rightNeighborInkLeft)
        {
            int bound = ownInkRight
                + ((rightNeighborInkLeft - ownInkRight) / 2) - (HeaderGutter / 2);
            return bound < ownInkRight ? ownInkRight : bound;
        }

        /// <summary>
        /// X at which a header centres over the extent its column's CELLS
        /// actually cover - <paramref name="contentX"/> to contentX +
        /// <paramref name="contentWidth"/> - rather than over the reserved
        /// band those cells sit in. THE header law of this module: a band
        /// is invisible to a reader, so centring in one drifts the word off
        /// the ink it names by half of however much the band exceeds it,
        /// and a band routinely does.
        /// <para>
        /// Cells keep their own justification, so the CALLER derives
        /// contentX from it: the column's left rule for a left-ruled
        /// column, rightEdge - contentWidth for a right-aligned one. The
        /// header is free to overhang its own band symmetrically; only
        /// <paramref name="room"/> stops it, and a header wider than the
        /// room pins to the room's left bound and overhangs rightward only
        /// - the one direction <see cref="CenteredX"/> already spills in.
        /// Derivation: docs/ARCHITECTURE.md section S1.2.
        /// </para>
        /// </summary>
        public static int CenteredOverContent(
            int contentX, int contentWidth, int headerWidth, HeaderRoom room)
        {
            int x = contentX + ((contentWidth - headerWidth) / 2);
            int rightmost = room.Right - headerWidth;
            if (x > rightmost)
            {
                x = rightmost;
            }

            return x < room.Left ? room.Left : x;
        }

        /// <summary>
        /// <see cref="CenteredOverContent"/> for a column whose cells
        /// right-align on <paramref name="contentRightEdge"/> - the shape
        /// every numeric and coin column in the module has.
        /// </summary>
        public static int CenteredOverContentRightAligned(
            int contentRightEdge, int contentWidth, int headerWidth, HeaderRoom room)
        {
            return CenteredOverContent(
                contentRightEdge - contentWidth, contentWidth, headerWidth, room);
        }

        /// <summary>
        /// X at which a header takes the same right edge its column's cells
        /// take, instead of centring over their ink. For a column whose
        /// header should read as part of the edge the values rule on: the
        /// word and the numbers under it then end on one line, which
        /// centring over a band narrower than the header does not give.
        /// <para>
        /// Clamped into <paramref name="room"/> exactly as
        /// <see cref="CenteredOverContent"/> is, so a header too wide for
        /// the gap beside it pins to the room's left bound and overhangs
        /// rightward only.
        /// </para>
        /// </summary>
        public static int RightAlignedOverContent(
            int contentRightEdge, int headerWidth, HeaderRoom room)
        {
            return CenteredOverContent(
                contentRightEdge - headerWidth, headerWidth, headerWidth, room);
        }

        /// <summary>
        /// Whether a span is wide enough to distribute at all. A track has
        /// to hold its own reserved band plus the gap that keeps a wide
        /// value out of the column to its left; below that width there is
        /// nothing to distribute and the caller falls back to the packed
        /// right-to-left stack, which fits in less. On a narrow panel a
        /// legible cramped table beats an evenly spaced illegible one.
        /// </summary>
        public static bool FitsDistributed(int trackSpan, int trackCount, int widestBand, int gap)
        {
            return trackCount > 0 && trackSpan / trackCount >= widestBand + gap;
        }
    }
}
