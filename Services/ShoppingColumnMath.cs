using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure column-edge arithmetic (Blish-free, unit-testable) for the shopping
    /// list's Item/Source/Amount/Each/Total table columns. Each column
    /// reserves a band, sized per render from the widest actual string in it
    /// (measured in the view via BitmapFont.MeasureString, which is
    /// Blish-bound and so not tested here) and clamped to a fixed minimum;
    /// see ShoppingListSectionRenderer.Render for the pre-scan.
    /// <para>
    /// The Item column takes its reserve off the left
    /// (<see cref="EffectiveNameColumnWidth"/>) and the four data columns
    /// DISTRIBUTE over equal tracks across the rest; below the width that
    /// supports that they pack right-to-left as they always did. Cells keep
    /// their own rule inside their band - badges left, numbers and coin runs
    /// right - and each HEADER is bounded by the columns either side of it
    /// (<see cref="HeaderRoomsFor"/>): Source and Amount centre over their
    /// cells' INK, Each and Total take the right edge their money rules
    /// against, and Item's stays on the left rule its names keep.
    /// Why: docs/ARCHITECTURE.md, "Services Q-Z: relocated design narrative".
    /// </para>
    /// </summary>
    internal static class ShoppingColumnMath
    {
        public const int TotalMinWidth = 150;
        public const int EachMinWidth = 110;
        public const int ColumnGap = 20;

        /// <summary>Left x of the row's item icon.</summary>
        public const int IconX = 8;

        /// <summary>
        /// Left x of the item name column, past the row's tier-2 icon
        /// frame plus a gap - and the left end of the track span, which is
        /// why it lives here rather than in the renderer that draws it.
        /// </summary>
        public const int NameX = IconX + PlanContentHeightMath.RowIconFrameSize + 8;

        /// <summary>
        /// Source, Amount, Each, Total - one equal track each, spanning
        /// everything between the Item column's reserve and the Total
        /// column's pinned right edge. Only the first three CENTRE on their
        /// track; Total right-aligns on its track's right edge, which is
        /// the panel's own pinned edge.
        /// <para>
        /// The Item column used to be two tracks of six, so it grew with
        /// the panel whatever its names measured - a third of the row for a
        /// column of "Copper Ore"s - while a mixed coin-and-currency Each
        /// or Total ("4g 36s 20c" plus two currency segments) was left in a
        /// sixth. It reserves what its own longest name needs now, and the
        /// four data columns divide the rest.
        /// </para>
        /// </summary>
        public const int DataColumnCount = 4;

        /// <summary>
        /// Slack past the longest item name in the Item column's reserve.
        /// It has to cover <see cref="ColumnGap"/>-scale breathing room AND
        /// the gap the row's own ellipsis budget keeps before the Source
        /// column (ShoppingListSectionRenderer.NameToQtyGap, 12), or the
        /// longest name would ellipsize inside a column reserved for it.
        /// </summary>
        public const int NameHeadroom = 24;

        /// <summary>
        /// Floor for the Item column's reserve: a list of short names must
        /// not collapse the row's subject to a stub, and the column's own
        /// header sits on the same rule. Also the floor that decides the
        /// packed fallback - below the width that can hold this plus four
        /// full data tracks there is nothing to distribute.
        /// </summary>
        public const int NameMinWidth = 200;

        public readonly struct ColumnEdges
        {
            public readonly int TotalRightEdge;
            public readonly int EachRightEdge;
            public readonly int QtyRightEdge;

            /// <summary>
            /// LEFT edge of the source-badge column. Left, not right:
            /// badges are words of different lengths, and a column of them
            /// reads as a column because their left edges rule - the same
            /// choice Required Recipes' Discipline column makes.
            /// </summary>
            public readonly int SourceX;

            /// <summary>
            /// Each column's reserved BAND width - what its CELLS grow
            /// inside, not what its header centres over or is bounded by
            /// (<see cref="HeaderRoomsFor"/>). Carried on the edges rather
            /// than re-derived by the caller: the header row and the data
            /// rows both read them off one instance, so neither can reserve
            /// a band the other did not.
            /// </summary>
            public readonly int SourceBandWidth;
            public readonly int QtyBandWidth;
            public readonly int EachBandWidth;
            public readonly int TotalBandWidth;

            public ColumnEdges(
                int totalRightEdge, int eachRightEdge, int qtyRightEdge, int sourceX,
                int sourceBandWidth, int qtyBandWidth, int eachBandWidth, int totalBandWidth,
                bool distributed, int trackSpan, int dataStartX, int nameColumnWidth)
            {
                TotalRightEdge = totalRightEdge;
                EachRightEdge = eachRightEdge;
                QtyRightEdge = qtyRightEdge;
                SourceX = sourceX;
                SourceBandWidth = sourceBandWidth;
                QtyBandWidth = qtyBandWidth;
                EachBandWidth = eachBandWidth;
                TotalBandWidth = totalBandWidth;
                Distributed = distributed;
                TrackSpan = trackSpan;
                DataStartX = dataStartX;
                NameColumnWidth = nameColumnWidth;
            }

            /// <summary>
            /// Whether the four data columns are DISTRIBUTED over equal
            /// tracks or packed right-to-left off the pinned edge. False is
            /// the narrow-panel fallback - see <see cref="ComputeEdges"/>.
            /// </summary>
            public readonly bool Distributed;

            /// <summary>The distributed span, from
            /// <see cref="DataStartX"/> to the Total column's pinned right
            /// edge; 0 when packed.</summary>
            public readonly int TrackSpan;

            /// <summary>
            /// Left edge of the four data tracks - the Item column's
            /// reserve past <see cref="NameX"/>, and so where that column's
            /// header CELL ends. 0 when packed, where the Item column has
            /// no reserve of its own and simply absorbs whatever the
            /// right-hand stack leaves.
            /// </summary>
            public readonly int DataStartX;

            /// <summary>The Item column's reserve this render; 0 when
            /// packed.</summary>
            public readonly int NameColumnWidth;

            /// <summary>Left edge of the band each right-aligned column's
            /// cells grow leftward into.</summary>
            public int QtyBandX => QtyRightEdge - QtyBandWidth;

            public int EachBandX => EachRightEdge - EachBandWidth;

            public int TotalBandX => TotalRightEdge - TotalBandWidth;
        }

        /// <summary>
        /// Every edge of one render of the table, in whichever of the two
        /// regimes the width supports: the four data columns DISTRIBUTED
        /// over equal tracks, or - on a panel too narrow for that - packed
        /// right-to-left off totalRightEdge as they always were. Header and
        /// data rows stay in lockstep by construction either way, because
        /// both are handed the same ColumnEdges instance for a given
        /// render. Total's band width is max(TotalMinWidth, maxTotalWidth);
        /// Each's band width is max(EachMinWidth, maxEachWidth).
        /// <para>
        /// The source badge used to be glued to the name's right edge, so
        /// its x moved with every row's own name length and no two rows'
        /// badges lined up. It is a column in the right-hand block now:
        /// maxQtyWidth and sourceColumnWidth are BAND widths (the widest
        /// value each column draws this render), never one row's own, so a
        /// short "1x" row cannot let its name run under the widest "429750x"
        /// beside it.
        /// </para>
        /// </summary>
        public static ColumnEdges ComputeEdges(
            int totalRightEdge, int maxEachWidth, int maxTotalWidth,
            int maxQtyWidth = 0, int sourceColumnWidth = 0, int maxNameWidth = 0)
        {
            int totalBand = EffectiveTotalWidth(maxTotalWidth);
            int eachBand = EffectiveEachWidth(maxEachWidth);

            // A track has to hold the widest band any of the four data
            // columns reserves, plus the gap that keeps it off its
            // neighbour, and the Item column has to keep at least its own
            // floor. Below that there is nothing to distribute and the
            // table falls back to the packed right-to-left stack, which
            // fits in less: on a narrow panel a legible cramped table beats
            // an evenly spaced illegible one. Same trade, same test, as
            // RankerRowLayout.Compute and SummarySectionLayoutMath.
            int widestBand = Max(Max(sourceColumnWidth, maxQtyWidth), Max(eachBand, totalBand));
            int fullSpan = totalRightEdge - NameX;
            int nameBand = fullSpan - (DataColumnCount * (widestBand + ColumnGap));
            int wanted = EffectiveNameColumnWidth(maxNameWidth);
            if (nameBand > wanted)
            {
                nameBand = wanted;
            }

            if (nameBand >= NameMinWidth)
            {
                int dataStartX = NameX + nameBand;
                int trackSpan = totalRightEdge - dataStartX;

                // Total keeps totalRightEdge, which by the span's own
                // construction IS its track's right edge: it is the band
                // that genuinely pins to the panel (see
                // JustifiedColumnTracks), and a table that stopped half a
                // track short of its own margin would strand the space
                // distribution exists to spend.
                return new ColumnEdges(
                    totalRightEdge,
                    TrackBandX(dataStartX, trackSpan, 2, eachBand) + eachBand,
                    TrackBandX(dataStartX, trackSpan, 1, maxQtyWidth) + maxQtyWidth,
                    TrackBandX(dataStartX, trackSpan, 0, sourceColumnWidth),
                    sourceColumnWidth, maxQtyWidth, eachBand, totalBand,
                    true, trackSpan, dataStartX, nameBand);
            }

            int eachRightEdge = totalRightEdge - totalBand - ColumnGap;
            int qtyRightEdge = eachRightEdge - eachBand - ColumnGap;
            int packedSourceX = qtyRightEdge - maxQtyWidth - ColumnGap - sourceColumnWidth;

            return new ColumnEdges(
                totalRightEdge, eachRightEdge, qtyRightEdge, packedSourceX,
                sourceColumnWidth, maxQtyWidth, eachBand, totalBand,
                false, 0, 0, 0);
        }

        /// <summary>
        /// The Item column's reserve: its longest name plus
        /// <see cref="NameHeadroom"/>, never below
        /// <see cref="NameMinWidth"/>. <see cref="ComputeEdges"/> caps it
        /// again at whatever four full data tracks leave, so a list of very
        /// long names gives up headroom before the data columns give up
        /// legibility.
        /// </summary>
        public static int EffectiveNameColumnWidth(int maxNameWidth)
        {
            int wanted = maxNameWidth + NameHeadroom;
            return wanted > NameMinWidth ? wanted : NameMinWidth;
        }

        /// <summary>
        /// Left edge of data column <paramref name="dataIndex"/>'s band,
        /// centred on the track it owns - the module's shared distribution
        /// law, see <see cref="JustifiedColumnTracks"/>. Data column 0 is
        /// Source, on the first track past the Item column's reserve.
        /// </summary>
        private static int TrackBandX(int dataStartX, int trackSpan, int dataIndex, int bandWidth)
        {
            return JustifiedColumnTracks.CenteredX(
                dataStartX, trackSpan, DataColumnCount, dataIndex, bandWidth);
        }

        /// <summary>
        /// Left edge of the track data column <paramref name="dataIndex"/>
        /// owns - where the column before it stops, and so the boundary
        /// between their two header cells.
        /// </summary>
        private static int TrackX(int dataStartX, int trackSpan, int dataIndex)
        {
            return JustifiedColumnTracks.LeftEdge(
                dataStartX, trackSpan, DataColumnCount, dataIndex);
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }

        /// <summary>
        /// Where each of the four data headers may sit: from the column on
        /// its left to the column on its right, gutters split - never the
        /// column's own band, which is floored at its own header label and
        /// at <see cref="TotalMinWidth"/>/<see cref="EachMinWidth"/>, so
        /// clamping into one right-aligns the header on its own values.
        /// </summary>
        public readonly struct HeaderRooms
        {
            public readonly JustifiedColumnTracks.HeaderRoom Source;
            public readonly JustifiedColumnTracks.HeaderRoom Amount;
            public readonly JustifiedColumnTracks.HeaderRoom Each;
            public readonly JustifiedColumnTracks.HeaderRoom Total;

            internal HeaderRooms(
                JustifiedColumnTracks.HeaderRoom source, JustifiedColumnTracks.HeaderRoom amount,
                JustifiedColumnTracks.HeaderRoom each, JustifiedColumnTracks.HeaderRoom total)
            {
                Source = source;
                Amount = amount;
                Each = each;
                Total = total;
            }
        }

        /// <summary>
        /// <see cref="HeaderRooms"/> for one render. Total's right-hand
        /// neighbour is the table's own pinned edge, which a header may not
        /// cross whatever its width.
        /// </summary>
        public static HeaderRooms HeaderRoomsFor(
            ColumnEdges edges, int nameGap, int sourceInk, int qtyInk, int eachInk, int totalInk)
        {
            int nameBudgetRight = edges.SourceX - nameGap;
            int sourceInkRight = edges.SourceX + sourceInk;
            int qtyInkX = edges.QtyRightEdge - qtyInk;
            int eachInkX = edges.EachRightEdge - eachInk;
            int totalInkX = edges.TotalRightEdge - totalInk;

            return new HeaderRooms(
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(nameBudgetRight, edges.SourceX),
                    JustifiedColumnTracks.RoomRightBound(sourceInkRight, qtyInkX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(sourceInkRight, qtyInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.QtyRightEdge, eachInkX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(edges.QtyRightEdge, eachInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.EachRightEdge, totalInkX)),
                JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(edges.EachRightEdge, totalInkX),
                    edges.TotalRightEdge));
        }

        /// <summary>
        /// Where each of the five header CELLS ends, in the header row's own
        /// left-to-right order (Total closes the band, so four are written).
        /// These are COLUMN edges: "Item" is a 40px word over a name column
        /// hundreds of pixels wide, so a label-derived boundary would sort a
        /// click above the item NAMES by Source.
        /// <para>
        /// nameGap is the Item column's own gap before the next column, and
        /// is used only by the packed fallback; distributed, the cells are
        /// the tracks themselves.
        /// </para>
        /// </summary>
        public static void HeaderCellBoundaries(ColumnEdges edges, int nameGap, int[] into)
        {
            if (into == null || into.Length < 4)
            {
                return;
            }

            if (edges.Distributed)
            {
                // Each cell is its column's whole TRACK, which is already a
                // partition of the row: no gap to split, and no cell that
                // stops short of the column beside it. The Item cell is
                // everything before the first track, i.e. its own reserve.
                into[0] = TrackX(edges.DataStartX, edges.TrackSpan, 0);
                into[1] = TrackX(edges.DataStartX, edges.TrackSpan, 1);
                into[2] = TrackX(edges.DataStartX, edges.TrackSpan, 2);
                into[3] = TrackX(edges.DataStartX, edges.TrackSpan, 3);
                return;
            }

            into[0] = edges.SourceX - (nameGap / 2);
            into[1] = edges.SourceX + edges.SourceBandWidth + (ColumnGap / 2);
            into[2] = edges.QtyRightEdge + (ColumnGap / 2);
            into[3] = edges.EachRightEdge + (ColumnGap / 2);
        }

        private static int EffectiveTotalWidth(int maxTotalWidth)
        {
            return maxTotalWidth > TotalMinWidth ? maxTotalWidth : TotalMinWidth;
        }

        private static int EffectiveEachWidth(int maxEachWidth)
        {
            return maxEachWidth > EachMinWidth ? maxEachWidth : EachMinWidth;
        }

        /// <summary>
        /// <see cref="ComputeEdges"/> from the panel width instead of a
        /// right edge: the Total column's right edge is
        /// PlanRelayoutMath.PinnedRightEdge, so the table justifies to the
        /// panel at every width. The single entry point the header row,
        /// every data row, and both of their relayout closures call, so no
        /// two of them can anchor the table differently.
        /// </summary>
        public static ColumnEdges ComputeEdgesForPanel(
            int panelWidth, int maxEachWidth, int maxTotalWidth,
            int maxQtyWidth = 0, int sourceColumnWidth = 0, int maxNameWidth = 0)
        {
            return ComputeEdges(
                PlanRelayoutMath.PinnedRightEdge(panelWidth),
                maxEachWidth, maxTotalWidth, maxQtyWidth, sourceColumnWidth, maxNameWidth);
        }

        /// <summary>
        /// Total width of a horizontal run of "label, gap, icon, gap"
        /// segments - the same layout convention CraftingPlanView's coin
        /// AND currency segments both use. Callers pass
        /// their own already-measured (Blish-bound BitmapFont.MeasureString)
        /// per-segment text widths plus their own iconSize/labelIconGap/
        /// segmentGap constants, so this arithmetic can never drift from
        /// what the view actually lays out - it does not bake in any of
        /// CraftingPlanView's specific pixel values itself. Empty/null
        /// input is 0 width (no trailing gap to subtract).
        /// </summary>
        public static int SegmentRunWidth(
            IReadOnlyList<int> segmentTextWidths, int iconSize, int labelIconGap, int segmentGap)
        {
            if (segmentTextWidths == null || segmentTextWidths.Count == 0)
            {
                return 0;
            }

            int width = 0;
            foreach (var textWidth in segmentTextWidths)
            {
                width += textWidth + labelIconGap + iconSize + segmentGap;
            }

            return width - segmentGap;
        }

        /// <summary>
        /// int[] twin of the IReadOnlyList&lt;int&gt; overload above, same
        /// formula. Every per-frame relayout closure (replayed on
        /// every OnPanelResized drag tick) calls this with
        /// SegmentLayoutHandle.TextWidths, which is always a concrete
        /// int[]; without this overload the compiler binds those hot-path
        /// calls to the IReadOnlyList&lt;int&gt; overload instead, and on
        /// .NET Framework foreaching an array through IEnumerable&lt;T&gt;/
        /// IReadOnlyList&lt;T&gt; allocates a heap enumerator per call. This
        /// overload lets the compiler lower the foreach to a plain indexed
        /// loop, so the resize hot path stays allocation-free as documented
        /// on its callers (RepositionValueCellRightAligned, the cost-tile
        /// relayout closure).
        /// </summary>
        public static int SegmentRunWidth(
            int[] segmentTextWidths, int iconSize, int labelIconGap, int segmentGap)
        {
            if (segmentTextWidths == null || segmentTextWidths.Length == 0)
            {
                return 0;
            }

            int width = 0;
            for (int i = 0; i < segmentTextWidths.Length; i++)
            {
                width += segmentTextWidths[i] + labelIconGap + iconSize + segmentGap;
            }

            return width - segmentGap;
        }
    }
}
