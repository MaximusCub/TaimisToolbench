namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure column-edge arithmetic (Blish-free, unit-testable) for the
    /// Required Recipes table: Recipe (flex) | Discipline | Cost | Sold By
    /// | Status, every row one line at
    /// PlanContentHeightMath.RecipeRowHeight.
    /// <para>
    /// The Recipe column reserves what its own longest name needs off the
    /// left and the data columns DISTRIBUTE over equal tracks across the
    /// rest - the module's shared law, see
    /// <see cref="JustifiedColumnTracks"/>. Below the width that supports
    /// that they pack right-to-left off the pinned edge, which is how the
    /// whole table was anchored before. Discipline and Sold By are
    /// LEFT-ruled (their values are words, the same choice the Shopping
    /// List's Source column makes); Cost and Status right-align. Each
    /// optional column is reserved only when some row fills it.
    /// </para>
    /// <para>Why the discipline is a column rather than a second caption
    /// line: docs/ARCHITECTURE.md, "Services Q-Z: relocated design
    /// narrative".</para>
    /// </summary>
    internal static class RecipesColumnMath
    {
        /// <summary>
        /// Gap between two adjacent bands. Shared with the Shopping List's
        /// own between-columns gap so two tables in the same view do not
        /// rule their columns at two different rhythms.
        /// </summary>
        public const int ColumnGap = ShoppingColumnMath.ColumnGap;

        /// <summary>
        /// Gap the recipe name's ellipsis budget keeps before the first
        /// data column - the name-to-column gap every other plan table
        /// reserves.
        /// </summary>
        public const int NameToDisciplineGap = 12;

        /// <summary>
        /// Slack past the longest recipe name in the Recipe column's
        /// reserve. It has to cover <see cref="ColumnGap"/>-scale breathing
        /// room AND <see cref="NameToDisciplineGap"/>, or the longest name
        /// would ellipsize inside a column reserved for it.
        /// </summary>
        public const int NameHeadroom = 24;

        /// <summary>
        /// Floor for the Recipe column's reserve, and the floor that
        /// decides the packed fallback: below the width that can hold this
        /// plus a full track per data column there is nothing to
        /// distribute. Same number and same role as
        /// <see cref="ShoppingColumnMath.NameMinWidth"/>.
        /// </summary>
        public const int NameMinWidth = ShoppingColumnMath.NameMinWidth;

        /// <summary>
        /// Cap on the Sold By band, past which the merchant phrase
        /// ellipsizes and its full text rides the cell's own hover.
        /// <para>
        /// A cap is load-bearing rather than cosmetic. Every band feeds
        /// <see cref="ComputeEdges"/>'s widest-band term, which is what
        /// decides whether the table distributes at all, and a merchant
        /// phrase is unbounded: "Scholar Pashsa and 4 other merchants"
        /// already runs wider than the whole Status column. Uncapped, one
        /// long phrase drops the table into the packed fallback and
        /// crushes the Recipe column with it.
        /// </para>
        /// <para>
        /// 220 is the widest band four data columns can carry at the
        /// module's minimum panel and still distribute: 1252px of panel
        /// leaves a 1186px span from nameX, the Recipe column's floor
        /// takes <see cref="NameMinWidth"/> of it, and the 986px left over
        /// gives each of the four tracks 246px.
        /// </para>
        /// </summary>
        public const int SoldByMaxWidth = 220;

        public readonly struct ColumnEdges
        {
            public readonly int StatusRightEdge;
            public readonly int DisciplineX;

            /// <summary>
            /// Right edge the Sheet cost cell's run right-aligns on.
            /// Meaningless when the column is not reserved this render;
            /// read <see cref="HasSheetCost"/> first.
            /// </summary>
            public readonly int SheetCostRightEdge;

            /// <summary>
            /// Left rule the Sold By cell's words start on. Meaningless
            /// when the column is not reserved this render; read
            /// <see cref="HasSoldBy"/> first.
            /// </summary>
            public readonly int SoldByX;

            public readonly int NameMaxWidth;

            public readonly bool HasDiscipline;
            public readonly bool HasSheetCost;
            public readonly bool HasSoldBy;

            /// <summary>
            /// Whether the data columns are DISTRIBUTED over equal tracks
            /// or packed right-to-left off the pinned edge. False is the
            /// narrow-panel fallback - see <see cref="ComputeEdges"/>.
            /// </summary>
            public readonly bool Distributed;

            /// <summary>The distributed span, from
            /// <see cref="DataStartX"/> to the pinned right edge; 0 when
            /// packed.</summary>
            public readonly int TrackSpan;

            /// <summary>
            /// Left edge of the data tracks - the Recipe column's reserve
            /// past its own nameX, and so where that column's header CELL
            /// ends. 0 when packed, where the Recipe column has no reserve
            /// of its own and absorbs whatever the right-hand stack leaves.
            /// </summary>
            public readonly int DataStartX;

            /// <summary>Number of data tracks this render: Status, plus
            /// each optional column that is reserved.</summary>
            public readonly int DataColumnCount;

            internal ColumnEdges(
                int statusRightEdge, int disciplineX, int sheetCostRightEdge, int soldByX,
                int nameMaxWidth, bool hasDiscipline, bool hasSheetCost, bool hasSoldBy,
                bool distributed, int trackSpan, int dataStartX, int dataColumnCount)
            {
                StatusRightEdge = statusRightEdge;
                DisciplineX = disciplineX;
                SheetCostRightEdge = sheetCostRightEdge;
                SoldByX = soldByX;
                NameMaxWidth = nameMaxWidth;
                HasDiscipline = hasDiscipline;
                HasSheetCost = hasSheetCost;
                HasSoldBy = hasSoldBy;
                Distributed = distributed;
                TrackSpan = trackSpan;
                DataStartX = dataStartX;
                DataColumnCount = dataColumnCount;
            }
        }

        /// <summary>
        /// Every edge of one render of the table, from the panel width, the
        /// data-derived band widths (each of which the caller has already
        /// floored at its own header label - a header at the ColumnHeader
        /// tier routinely out-measures the data under it) and the widest
        /// recipe name. The single entry point the header row, every data
        /// row, and all of their resize closures call, so no two of them
        /// can anchor the table differently.
        /// <para>
        /// The band widths are the columns' widest values, never one row's
        /// own: a row whose status is blank still must not let its name run
        /// under the widest "Auto-learned" beside it. A zero band means the
        /// column is not drawn at all this render.
        /// </para>
        /// </summary>
        public static ColumnEdges ComputeEdges(
            int panelWidth, int statusColumnWidth, int disciplineColumnWidth,
            int sheetCostColumnWidth, int soldByColumnWidth, int maxNameWidth, int nameX)
        {
            int pinnedRightEdge = PlanRelayoutMath.PinnedRightEdge(panelWidth);
            bool hasDiscipline = disciplineColumnWidth > 0;
            bool hasSheetCost = sheetCostColumnWidth > 0;
            bool hasSoldBy = soldByColumnWidth > 0;
            int dataColumnCount =
                1 + (hasDiscipline ? 1 : 0) + (hasSheetCost ? 1 : 0) + (hasSoldBy ? 1 : 0);

            int widestBand = Max(
                statusColumnWidth,
                Max(disciplineColumnWidth, Max(sheetCostColumnWidth, soldByColumnWidth)));
            int fullSpan = pinnedRightEdge - nameX;
            int nameBand = fullSpan - (dataColumnCount * (widestBand + ColumnGap));
            int wanted = EffectiveNameColumnWidth(maxNameWidth);
            if (nameBand > wanted)
            {
                nameBand = wanted;
            }

            if (nameBand >= NameMinWidth)
            {
                int dataStartX = nameX + nameBand;
                int trackSpan = pinnedRightEdge - dataStartX;

                // Track indices are handed out left to right over the
                // columns actually reserved, so a table with no Discipline
                // column closes the gap instead of leaving an empty track
                // where that column would have been.
                int nextIndex = 0;
                int disciplineIndex = hasDiscipline ? nextIndex++ : 0;
                int sheetIndex = hasSheetCost ? nextIndex++ : 0;
                int soldByIndex = hasSoldBy ? nextIndex : 0;

                // Status keeps pinnedRightEdge, which by the span's own
                // construction IS its track's right edge: it is the band
                // that genuinely pins to the panel (see
                // JustifiedColumnTracks), and a table that stopped half a
                // track short of its own margin would strand the space
                // distribution exists to spend.
                return new ColumnEdges(
                    pinnedRightEdge,
                    hasDiscipline
                        ? TrackBandX(
                            dataStartX, trackSpan, dataColumnCount, disciplineIndex,
                            disciplineColumnWidth)
                        : dataStartX,
                    hasSheetCost
                        ? TrackBandX(
                            dataStartX, trackSpan, dataColumnCount, sheetIndex, sheetCostColumnWidth)
                            + sheetCostColumnWidth
                        : dataStartX,
                    hasSoldBy
                        ? TrackBandX(
                            dataStartX, trackSpan, dataColumnCount, soldByIndex, soldByColumnWidth)
                        : dataStartX,
                    PlanRelayoutMath.NameMaxWidthBeforeColumn(
                        dataStartX, 0, NameToDisciplineGap, nameX),
                    hasDiscipline, hasSheetCost, hasSoldBy, true, trackSpan, dataStartX,
                    dataColumnCount);
            }

            // Packed fallback, stacked right to left off the pinned edge:
            // Status, then Sold By, then Cost, then Discipline. A column
            // that is not reserved costs no band and no gap, so the stack
            // closes over it.
            int cursor = pinnedRightEdge - statusColumnWidth;
            int soldByX = cursor - ColumnGap - soldByColumnWidth;
            if (hasSoldBy)
            {
                cursor = soldByX;
            }

            int sheetCostRightEdge = cursor - ColumnGap;
            if (hasSheetCost)
            {
                cursor = sheetCostRightEdge - sheetCostColumnWidth;
            }

            int disciplineX = cursor - ColumnGap - disciplineColumnWidth;
            if (hasDiscipline)
            {
                cursor = disciplineX;
            }

            return new ColumnEdges(
                pinnedRightEdge, disciplineX, sheetCostRightEdge, soldByX,
                PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    cursor, 0, NameToDisciplineGap, nameX),
                hasDiscipline, hasSheetCost, hasSoldBy, false, 0, 0, dataColumnCount);
        }

        /// <summary>
        /// The Recipe column's reserve: its longest name plus
        /// <see cref="NameHeadroom"/>, never below
        /// <see cref="NameMinWidth"/>. <see cref="ComputeEdges"/> caps it
        /// again at whatever full data tracks leave, so a list of very long
        /// names gives up headroom before the data columns give up
        /// legibility.
        /// </summary>
        public static int EffectiveNameColumnWidth(int maxNameWidth)
        {
            int wanted = maxNameWidth + NameHeadroom;
            return wanted > NameMinWidth ? wanted : NameMinWidth;
        }

        /// <summary>
        /// Where each right-hand header may sit: from the column on its
        /// left to the column on its right, gutters split - never its own
        /// band, which is floored at the header label itself and so pins a
        /// header that should be centred over shorter values. Status's
        /// right-hand neighbour is the table's pinned edge, and the recipe
        /// name flexes, so what precedes the first data column is that
        /// name's ellipsis budget rather than a measured string. A column
        /// that is not reserved this render yields an empty room and is
        /// skipped in the neighbour chain.
        /// </summary>
        public static void HeaderRooms(
            ColumnEdges edges, int disciplineInk, int sheetCostInk, int soldByInk, int statusInk,
            out JustifiedColumnTracks.HeaderRoom discipline,
            out JustifiedColumnTracks.HeaderRoom sheetCost,
            out JustifiedColumnTracks.HeaderRoom soldBy,
            out JustifiedColumnTracks.HeaderRoom status)
        {
            int nameBudgetRight =
                FirstColumnInkLeft(edges, sheetCostInk, statusInk) - NameToDisciplineGap;
            int disciplineInkRight = edges.DisciplineX + disciplineInk;
            int sheetCostInkX = edges.SheetCostRightEdge - sheetCostInk;
            int soldByInkRight = edges.SoldByX + soldByInk;
            int statusInkX = edges.StatusRightEdge - statusInk;

            int beforeSheetCost = edges.HasDiscipline ? disciplineInkRight : nameBudgetRight;
            int beforeSoldBy = edges.HasSheetCost ? edges.SheetCostRightEdge : beforeSheetCost;
            int beforeStatus = edges.HasSoldBy ? soldByInkRight : beforeSoldBy;

            int afterSheetCost = edges.HasSoldBy ? edges.SoldByX : statusInkX;
            int afterDiscipline = edges.HasSheetCost ? sheetCostInkX : afterSheetCost;

            discipline = edges.HasDiscipline
                ? JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(nameBudgetRight, edges.DisciplineX),
                    JustifiedColumnTracks.RoomRightBound(disciplineInkRight, afterDiscipline))
                : JustifiedColumnTracks.HeaderRoom.Between(edges.DisciplineX, edges.DisciplineX);
            sheetCost = edges.HasSheetCost
                ? JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(beforeSheetCost, sheetCostInkX),
                    JustifiedColumnTracks.RoomRightBound(edges.SheetCostRightEdge, afterSheetCost))
                : JustifiedColumnTracks.HeaderRoom.Between(
                    edges.SheetCostRightEdge, edges.SheetCostRightEdge);
            soldBy = edges.HasSoldBy
                ? JustifiedColumnTracks.HeaderRoom.Between(
                    JustifiedColumnTracks.RoomLeftBound(beforeSoldBy, edges.SoldByX),
                    JustifiedColumnTracks.RoomRightBound(soldByInkRight, statusInkX))
                : JustifiedColumnTracks.HeaderRoom.Between(edges.SoldByX, edges.SoldByX);
            status = JustifiedColumnTracks.HeaderRoom.Between(
                JustifiedColumnTracks.RoomLeftBound(beforeStatus, statusInkX),
                edges.StatusRightEdge);
        }

        /// <summary>
        /// Left edge of the ink the leftmost data column draws, which is
        /// what the recipe name's ellipsis budget stops short of.
        /// </summary>
        private static int FirstColumnInkLeft(
            ColumnEdges edges, int sheetCostInk, int statusInk)
        {
            if (edges.HasDiscipline)
            {
                return edges.DisciplineX;
            }

            if (edges.HasSheetCost)
            {
                return edges.SheetCostRightEdge - sheetCostInk;
            }

            return edges.HasSoldBy ? edges.SoldByX : edges.StatusRightEdge - statusInk;
        }

        /// <summary>
        /// Left edge of data column <paramref name="dataIndex"/>'s band,
        /// centred on the track it owns - the module's shared distribution
        /// law, see <see cref="JustifiedColumnTracks"/>.
        /// </summary>
        private static int TrackBandX(
            int dataStartX, int trackSpan, int dataColumnCount, int dataIndex, int bandWidth)
        {
            return JustifiedColumnTracks.CenteredX(
                dataStartX, trackSpan, dataColumnCount, dataIndex, bandWidth);
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }
    }
}
