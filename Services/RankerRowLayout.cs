using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure column arithmetic for one Crafting Ranker row, in the shape of
    /// LogRowLayout / ShoppingColumnMath.
    ///
    /// Same law as every other table in the module: the name column is the
    /// only flexing element, it consumes every pixel the pinned right-hand
    /// block does not, and at no width is there empty space to the right of
    /// the action buttons.
    /// </summary>
    public static class RankerRowLayout
    {
        public const int Inset = 16;

        // Tier 1 of the module's two-tier icon system: the
        // Ranker's rows carry in-game bag-slot-sized item art, like the
        // Snapshot grid and the plan heading.
        public const int IconSize = ItemIconTiers.BagSlotIconSize;
        public const int IconBorder = 1;
        public const int IconTotal = IconSize + 2 * IconBorder;
        public const int IconGap = 8;
        public const int CellGap = 12;
        public const int ButtonGap = 4;

        // 60: the 54px tier-1 icon frame plus 3px of clearance each side.
        public const int RowHeight = 60;

        /// <summary>A text-only sub-line: a note.</summary>
        public const int SubLineHeight = 20;

        /// <summary>
        /// The gate strip's own pitch. Taller than a text sub-line because
        /// each cell now carries a painted bar rather than a bare number,
        /// and the bar has to hold the percentage's line box with plate
        /// left around it.
        /// </summary>
        public const int GateLineHeight = 22;

        /// <summary>
        /// Height of a gate cell's bar inside <see cref="GateLineHeight"/>.
        /// The figure inside draws at Caption, one ramp tier below the gate
        /// NAME beside it, so its 18px line box leaves a pixel of bar top
        /// and bottom - at Body it filled the 20 exactly and the meter read
        /// as a number with paint behind it. Seat:
        /// Views/RankerTabContent's GateValueY.
        /// </summary>
        public const int GateBarHeight = 20;

        /// <summary>Gap between a gate's label band and its bar.</summary>
        public const int GateLabelGap = 8;

        /// <summary>
        /// A currency line carries a wallet-LIST-tier icon (the game's own
        /// 32px), so it cannot sit in a text line's pitch. 4px of clearance,
        /// against the main line's 3.
        /// </summary>
        public const int CurrencyLineHeight = CurrencyIconTiers.WalletListIconSize + 4;

        // RHYTHM. Grouping is carried by the gaps BETWEEN blocks rather than
        // by uniform padding everywhere: the headline, the gate strip and the
        // currency detail are three different kinds of statement, and a
        // reader should see three groups instead of one wall of lines. Inside
        // a block, lines keep their own pitch and take no gap at all.
        public const int GateTopGap = 2;
        public const int CurrencyTopGap = 8;
        public const int NoteTopGap = 8;

        /// <summary>
        /// The row's three actions, at the box Blish's own window close
        /// control draws in rather than at the module's on-tab button
        /// height (Services/GlyphButtonMetrics). Not square.
        /// </summary>
        public const int ButtonWidth = GlyphButtonMetrics.RowActionWidth;

        /// <summary>The other axis of <see cref="ButtonWidth"/>.</summary>
        public const int ButtonHeight = GlyphButtonMetrics.RowActionHeight;

        /// <summary>Room for "25." at UiFonts.Caption plus clearance.</summary>
        public const int RankWidth = 26;

        // The three right-hand cells each reserve enough width for BOTH
        // their widest cell text ("100%", "999d", a coin amount) and their
        // own column-header label at the header band's bold ColumnHeader
        // font. Header and cells centre on the same track, so a track
        // narrower than its header spills into the columns on BOTH sides of
        // it - the live sandbox check caught the right-aligned form of that
        // ("ReadhyDaining") when an empty table let the coin band collapse
        // to the width of a dash.

        /// <summary>
        /// Fits the bold "Ready" header (~50px), which is now the binding
        /// term: the bar's centred "100%" was derived against bold 18 at
        /// ~46px and draws one tier down at Body, so it is narrower than the
        /// header above it. This is the cell's floor, not its width: under
        /// distribution the bar takes its whole track.
        /// </summary>
        public const int ReadyCellWidth = 66;

        /// <summary>
        /// Height of the headline readiness bar. 24, and the percentage
        /// inside it is a Body line box (20), leaving 2px of plate above and
        /// below so the bar reads around the figure instead of being filled
        /// by it (Views/RankerTabContent's ReadyLineY). The bar itself
        /// centres in <see cref="RowHeight"/>.
        /// </summary>
        public const int ReadyBarHeight = 24;

        /// <summary>
        /// Floor for the headline bar. Below this the centred "100%" it
        /// carries has no plate left around it - derived when that figure
        /// was bold 18 at ~46px, and clear by more since it dropped a tier.
        /// The packed fallback's ReadyCellWidth - CellGap is 54, so nothing
        /// at a supported width goes under it.
        /// </summary>
        public const int MinReadinessBarWidth = 50;

        /// <summary>
        /// Floor for the affordability chip's column, applied inside
        /// Compute: fits the bold "Status" header (~62px) and the narrower
        /// of the two chips. Rows may measure wider; never narrower.
        /// </summary>
        public const int MinStatusCellWidth = 120;

        /// <summary>Fits bold "Days" (~46px) and body "999d".</summary>
        public const int DaysCellWidth = 54;

        /// <summary>
        /// Floor for the coin cell band, applied inside Compute: fits bold
        /// "Remaining" (~92px). Rows may measure wider; never narrower.
        /// </summary>
        public const int MinRemainingCellWidth = 100;

        /// <summary>
        /// The six gate cells of the breakdown sub-line.
        /// <para>
        /// MEASURED at the narrowest real width the suite drives
        /// (a 1378px screen, a 1232px row, an 1112px sub-line band): six
        /// cells leave an 85px bar past an 80px label band, against 122px
        /// with five. The figure a cell draws is Caption-tier and well
        /// inside that, so the sixth cell costs bar length and no
        /// legibility.
        /// </para>
        /// </summary>
        public const int GateCellCount = 6;

        /// <summary>Below this the pinned block cannot fit and the name band collapses to zero.</summary>
        public const int MinNameWidth = 40;

        public readonly struct Bands
        {
            public readonly int RowWidth;
            public readonly int RankX;
            public readonly int IconX;
            public readonly int NameX;
            public readonly int NameWidth;

            /// <summary>Left edge of the Status column's chip - the one cell that is left-aligned.</summary>
            public readonly int StatusX;

            /// <summary>Width the Status chip may fill before it runs into Ready.</summary>
            public readonly int StatusWidth;

            /// <summary>Left edge of the headline readiness bar.</summary>
            public readonly int ReadyBarX;

            /// <summary>Width of that bar, or 0 at a width that cannot hold one.</summary>
            public readonly int ReadyBarWidth;

            /// <summary>Right edge of the Ready track - the bar's right edge.</summary>
            public readonly int ReadyRightEdge;

            /// <summary>Left edge of the Days track.</summary>
            public readonly int DaysTrackX;

            /// <summary>Right edge of the Days track.</summary>
            public readonly int DaysRightEdge;

            /// <summary>Left edge of the Remaining track.</summary>
            public readonly int RemainingTrackX;

            /// <summary>Right edge of the Remaining track.</summary>
            public readonly int RemainingRightEdge;

            /// <summary>
            /// Whether the four data columns are DISTRIBUTED over equal
            /// tracks or packed right-to-left. False is the narrow-panel
            /// fallback; see <see cref="Compute"/>.
            /// </summary>
            public readonly bool Distributed;

            /// <summary>Left edge of the move-up button, or -1 when the row has none.</summary>
            public readonly int UpX;

            /// <summary>Left edge of the move-down button, or -1 when the row has none.</summary>
            public readonly int DownX;

            public readonly int RemoveX;

            /// <summary>Left edge of the sub-lines, aligned under the item name.</summary>
            public readonly int SubLineX;

            /// <summary>Width available to a sub-line, out to the row's one right edge.</summary>
            public readonly int SubLineWidth;

            public Bands(
                int rowWidth, int rankX, int iconX, int nameX, int nameWidth,
                int statusX, int statusWidth,
                int readyBarX, int readyBarWidth, int readyRightEdge,
                int daysTrackX, int daysRightEdge,
                int remainingTrackX, int remainingRightEdge, bool distributed,
                int upX, int downX, int removeX,
                int subLineX, int subLineWidth)
            {
                DaysTrackX = daysTrackX;
                RemainingTrackX = remainingTrackX;
                RowWidth = rowWidth;
                RankX = rankX;
                IconX = iconX;
                NameX = nameX;
                NameWidth = nameWidth;
                StatusX = statusX;
                StatusWidth = statusWidth;
                ReadyBarX = readyBarX;
                ReadyBarWidth = readyBarWidth;
                ReadyRightEdge = readyRightEdge;
                DaysRightEdge = daysRightEdge;
                RemainingRightEdge = remainingRightEdge;
                Distributed = distributed;
                UpX = upX;
                DownX = downX;
                RemoveX = removeX;
                SubLineX = subLineX;
                SubLineWidth = subLineWidth;
            }

            /// <summary>
            /// The band data column <paramref name="index"/> owns - the one
            /// track its header and its cell content both centre in, which
            /// is what puts a header over the values it names. Ready's track
            /// is its bar: the bar is a gauge, so it FILLS the track and
            /// only the percentage inside it centres.
            /// <para>
            /// An index outside 0..<see cref="DataColumnCount"/>-1 returns a
            /// zero-width band at the first column's left edge, the same
            /// shape <see cref="GateCell"/> uses.
            /// </para>
            /// </summary>
            public void DataTrack(int index, out int x, out int width)
            {
                switch (index)
                {
                    case StatusColumn:
                        x = StatusX;
                        width = Math.Max(0, StatusWidth);
                        return;
                    case ReadyColumn:
                        x = ReadyBarX;
                        width = Math.Max(0, ReadyBarWidth);
                        return;
                    case DaysColumn:
                        x = DaysTrackX;
                        width = Math.Max(0, DaysRightEdge - DaysTrackX);
                        return;
                    case RemainingColumn:
                        x = RemainingTrackX;
                        width = Math.Max(0, RemainingRightEdge - RemainingTrackX);
                        return;
                    default:
                        x = StatusX;
                        width = 0;
                        return;
                }
            }
        }

        /// <summary>Data column indices for <see cref="Bands.DataTrack"/>, left to right.</summary>
        public const int StatusColumn = 0;
        public const int ReadyColumn = 1;
        public const int DaysColumn = 2;
        public const int RemainingColumn = 3;

        /// <summary>
        /// Columns the row is divided into between the item name's left edge
        /// and the last data column's right edge: the name takes
        /// <see cref="NameTrackSpan"/> of them, then Status, Ready, Days and
        /// Remaining take one each.
        /// <para>
        /// This is SummarySectionLayoutMath's currency-table idiom, applied
        /// here because the four data columns used to huddle against the
        /// buttons at the right edge, leaving the centre of a 2400px row
        /// empty and the eye with nothing to follow from a row's name to
        /// its numbers.
        /// </para>
        /// <para>
        /// The name gets two tracks rather than one because it is the row's
        /// subject and now draws one tier up the ramp (bold 18); a single
        /// track at the 1378px window floor leaves it about 200px, which
        /// ellipsizes ordinary legendary names, and two leaves it about 320.
        /// </para>
        /// </summary>
        public const int TrackCount = 6;

        /// <summary>
        /// Tracks the item name spans; see <see cref="TrackCount"/>. The two
        /// constants are COUPLED - Compute reads the four data columns off
        /// tracks NameTrackSpan..TrackCount, so TrackCount has to stay
        /// NameTrackSpan plus <see cref="DataColumnCount"/>. Asserted rather
        /// than left to be noticed.
        /// </summary>
        public const int NameTrackSpan = 2;

        /// <summary>Status, Ready, Days, Remaining - one track each.</summary>
        public const int DataColumnCount = 4;

        /// <summary>
        /// rowWidth is the SCROLLING panel's width minus
        /// WindowSizing.ScrollbarAllowance, never the container's width.
        /// <para>
        /// The affordability chip is a COLUMN here, with a header of its
        /// own. It used to trail the item name, which put it at a different
        /// x on every row and made the one badge the table exists to be
        /// scanned for the only thing in it that could not be scanned.
        /// </para>
        /// <para>
        /// statusCellWidth and remainingCellWidth are the widest measured
        /// cell across the WHOLE table, not this row's - the columns are
        /// table-wide or the header labels nothing.
        /// </para>
        /// </summary>
        public static Bands Compute(
            int rowWidth, int remainingCellWidth, int statusCellWidth = 0, bool showReorder = true)
        {
            rowWidth = Math.Max(0, rowWidth);
            remainingCellWidth = Math.Max(MinRemainingCellWidth, remainingCellWidth);
            statusCellWidth = Math.Max(MinStatusCellWidth, statusCellWidth);

            int rightEdge = Math.Max(0, rowWidth - Inset);

            // Independent mode has no reorder buttons at all - the order it
            // displays is its own answer, not something to drag. Their rails
            // are not left empty: every band to their left widens into the
            // space, which is what keeps the table justified to the panel
            // rather than stranding 64px of nothing under the header.
            int removeX = rightEdge - ButtonWidth;
            int downX = showReorder ? removeX - ButtonGap - ButtonWidth : -1;
            int upX = showReorder ? downX - ButtonGap - ButtonWidth : -1;

            int rankX = Inset;
            int iconX = rankX + RankWidth;
            int nameX = iconX + IconTotal + IconGap;

            int dataRightEdge = (showReorder ? upX : removeX) - CellGap;
            int trackSpan = dataRightEdge - nameX;

            // A track has to hold the widest cell any of the four data
            // columns will draw, plus the gap that keeps it off its
            // neighbour. Below that there is nothing to distribute and the
            // row falls back to the packed right-to-left stack, which fits
            // in less: on a narrow panel a legible cramped table beats an
            // evenly spaced illegible one. Same trade, and the same test,
            // as SummarySectionLayoutMath.EdgesFromRightEdge.
            int widestCell = Math.Max(
                Math.Max(statusCellWidth, remainingCellWidth),
                Math.Max(ReadyCellWidth, DaysCellWidth));
            bool distributed = JustifiedColumnTracks.FitsDistributed(
                trackSpan, TrackCount, widestCell, CellGap);

            int statusX;
            int statusWidth;
            int readyRightEdge;
            int daysTrackX;
            int daysRightEdge;
            int remainingTrackX;
            int remainingRightEdge;
            int readyTrackWidth;

            if (distributed)
            {
                // Data column i sits on track NameTrackSpan + i; the name
                // takes the tracks before them.
                statusX = JustifiedColumnTracks.LeftEdge(
                    nameX, trackSpan, TrackCount, NameTrackSpan + StatusColumn);
                statusWidth = JustifiedColumnTracks.Width(
                    nameX, trackSpan, TrackCount, NameTrackSpan + StatusColumn) - CellGap;
                readyTrackWidth = JustifiedColumnTracks.Width(
                    nameX, trackSpan, TrackCount, NameTrackSpan + ReadyColumn);
                readyRightEdge = JustifiedColumnTracks.RightEdge(
                    nameX, trackSpan, TrackCount, NameTrackSpan + ReadyColumn);
                daysTrackX = JustifiedColumnTracks.LeftEdge(
                    nameX, trackSpan, TrackCount, NameTrackSpan + DaysColumn);
                daysRightEdge = JustifiedColumnTracks.RightEdge(
                    nameX, trackSpan, TrackCount, NameTrackSpan + DaysColumn);
                remainingTrackX = JustifiedColumnTracks.LeftEdge(
                    nameX, trackSpan, TrackCount, NameTrackSpan + RemainingColumn);
                remainingRightEdge = JustifiedColumnTracks.RightEdge(
                    nameX, trackSpan, TrackCount, NameTrackSpan + RemainingColumn);
            }
            else
            {
                // Packed: a column's track is the band it reserves, so the
                // centring the view does is identical in both regimes.
                remainingRightEdge = dataRightEdge;
                remainingTrackX = remainingRightEdge - remainingCellWidth;
                daysRightEdge = remainingTrackX - CellGap;
                daysTrackX = daysRightEdge - DaysCellWidth;
                readyRightEdge = daysTrackX - CellGap;
                statusX = readyRightEdge - ReadyCellWidth - CellGap - statusCellWidth;
                statusWidth = statusCellWidth;
                readyTrackWidth = ReadyCellWidth;
            }

            // The bar FILLS its track. A capped bar would strand the rest of
            // it, which is the dead space distribution exists to retire, and
            // the clearance to the Status chip on its left is already paid
            // for out of that column's own band.
            int readyBarWidth = Math.Max(0, readyTrackWidth);
            int readyBarX = readyRightEdge - readyBarWidth;

            // The name band ends a gap short of the Status chip's left edge:
            // the chip is left-aligned there, so a name allowed to run to it
            // would touch it.
            int nameWidth = statusX - CellGap - nameX;

            // A window narrow enough to squeeze the name out clamps rather
            // than emitting a negative width the view would hand to a
            // measure call.
            if (nameWidth < MinNameWidth)
            {
                nameWidth = Math.Max(0, Math.Min(MinNameWidth, rightEdge - nameX));
            }

            int subLineX = nameX;
            int subLineWidth = Math.Max(0, rightEdge - subLineX);

            return new Bands(
                rowWidth, rankX, iconX, nameX, nameWidth,
                statusX, Math.Max(0, statusWidth),
                readyBarX, readyBarWidth, readyRightEdge,
                daysTrackX, daysRightEdge,
                remainingTrackX, remainingRightEdge, distributed,
                upX, downX, removeX,
                subLineX, subLineWidth);
        }

        /// <summary>
        /// Y of a line box of <paramref name="lineHeight"/> centred in the
        /// main line. The row's height is set by its tier-1 item icon
        /// (<see cref="RowHeight"/>), so every font that draws on the main
        /// line - the bold-18 name and readiness bar, the body-16 days and
        /// coin cells, the caption rank - centres against the ICON rather
        /// than sharing a top edge with a taller neighbour.
        /// </summary>
        public static int MainLineY(int lineHeight)
        {
            return Math.Max(0, (RowHeight - lineHeight) / 2);
        }

        /// <summary>
        /// One cell of the gate-breakdown sub-line. The cells divide the
        /// sub-line's full width evenly so the strip is justified to the panel
        /// rather than left-packed with dead space on the right.
        /// </summary>
        public static void GateCell(in Bands bands, int index, out int x, out int width)
        {
            if (index < 0 || index >= GateCellCount || bands.SubLineWidth <= 0)
            {
                x = bands.SubLineX;
                width = 0;
                return;
            }

            // Integer-exact edges: the last cell absorbs the remainder rather
            // than leaving a rounding gap at the right edge.
            int left = bands.SubLineX + (int)((long)bands.SubLineWidth * index / GateCellCount);
            int right = bands.SubLineX + (int)((long)bands.SubLineWidth * (index + 1) / GateCellCount);
            x = left;
            width = Math.Max(0, right - left);
        }

        /// <summary>
        /// The painted bar inside gate cell <paramref name="index"/>: it
        /// starts past a label band wide enough for the widest of the
        /// gate names (measured by the caller, since that is a
        /// MeasureString) and runs to a gap short of the cell's own end.
        /// <para>
        /// The label band is one width for every cell rather than each
        /// cell's own label width, so the bars start at the same offset in
        /// every cell and the strip reads as a row of gauges rather than a
        /// row of sentences. The gap it fills is exactly the dead space between a
        /// gate's name and its right-aligned percentage.
        /// </para>
        /// </summary>
        public static void GateBar(
            in Bands bands, int index, int labelBandWidth, out int barX, out int barWidth)
        {
            GateCell(bands, index, out int cellX, out int cellWidth);
            barX = cellX + Math.Max(0, labelBandWidth) + GateLabelGap;
            barWidth = Math.Max(0, cellX + cellWidth - CellGap - barX);
        }

        /// <summary>
        /// Where each block of a row's sub-lines starts, relative to the row
        /// panel's top, and how tall the row ends up. One place, so the row's
        /// HEIGHT and the y its labels are drawn at cannot disagree - they are
        /// the same arithmetic read twice.
        /// <para>
        /// A block with no lines takes no height AND no gap, so a row with
        /// nothing below its headline is exactly RowHeight tall - which is
        /// what the table shows with both display toggles off.
        /// </para>
        /// </summary>
        public readonly struct SubLineBlock
        {
            /// <summary>Y of the gate strip, or -1 when the row has none.</summary>
            public readonly int GateY;

            /// <summary>Y of the first currency line, or -1 when there are none.</summary>
            public readonly int CurrencyY;

            /// <summary>Y of the first note line, or -1 when there are none.</summary>
            public readonly int NoteY;

            /// <summary>Total height of the row, sub-lines included.</summary>
            public readonly int TotalHeight;

            public SubLineBlock(int gateY, int currencyY, int noteY, int totalHeight)
            {
                GateY = gateY;
                CurrencyY = currencyY;
                NoteY = noteY;
                TotalHeight = totalHeight;
            }
        }

        public static SubLineBlock SubLines(bool hasGates, int currencyLines, int noteLines)
        {
            currencyLines = Math.Max(0, currencyLines);
            noteLines = Math.Max(0, noteLines);

            int y = RowHeight;
            int gateY = -1;
            int currencyY = -1;
            int noteY = -1;

            if (hasGates)
            {
                y += GateTopGap;
                gateY = y;
                y += GateLineHeight;
            }

            if (currencyLines > 0)
            {
                y += CurrencyTopGap;
                currencyY = y;
                y += currencyLines * CurrencyLineHeight;
            }

            if (noteLines > 0)
            {
                y += NoteTopGap;
                noteY = y;
                y += noteLines * SubLineHeight;
            }

            // The breath a stack of sub-lines needs so the next row's
            // headline does not sit on this row's last detail line.
            if (y > RowHeight)
            {
                y += GateTopGap;
            }

            return new SubLineBlock(gateY, currencyY, noteY, y);
        }

        /// <summary>
        /// The Analyze button's plate. It carries TWO labels - "Analyze" at
        /// rest and "Analyzing..." while a run is in flight - and must not
        /// resize between them, so this fits the WIDER one with room to
        /// spare. Interpolated from the module's hand-fitted button widths
        /// (Buy All 70 at 7 characters, Clear Overrides 124 at 15): ~6.75px
        /// per character plus ~23px of plate, so the 12-character label
        /// wants ~104 and this leaves ~14px of padding either side of it.
        /// Never fed status text - see <see cref="Toolbar"/> for the run's
        /// progress line, which belongs to the status band.
        /// </summary>
        public const int AnalyzeButtonWidth = 132;

        public readonly struct ToolbarSlots
        {
            /// <summary>Left edge of the right-anchored Analyze button.</summary>
            public readonly int AnalyzeX;

            /// <summary>Left edge of the first display toggle, seated left of the second.</summary>
            public readonly int FirstToggleX;

            /// <summary>Left edge of the second display toggle, seated left of Refresh.</summary>
            public readonly int SecondToggleX;

            /// <summary>Left edge of the status line's band.</summary>
            public readonly int StatusX;

            /// <summary>
            /// Width the status label may fill. Text longer than this is
            /// ellipsized by the view, never allowed to run under the button.
            /// </summary>
            public readonly int StatusWidth;

            public ToolbarSlots(
                int analyzeX, int firstToggleX, int secondToggleX, int statusX, int statusWidth)
            {
                AnalyzeX = analyzeX;
                FirstToggleX = firstToggleX;
                SecondToggleX = secondToggleX;
                StatusX = statusX;
                StatusWidth = statusWidth;
            }
        }

        /// <summary>
        /// The toolbar row: one full-width status band on the left, the
        /// Analyze button pinned right, the two display toggles between them
        /// in reading order, the inline spinner after the status text.
        /// The run-progress text renders in the status band and ONLY
        /// there - in game, status-length text stamped onto the
        /// fixed-width button spilled past its edges.
        /// <para>
        /// A toggle whose width is zero takes no slot at all, so the status
        /// band keeps the space rather than a rail of nothing sitting in it.
        /// </para>
        /// </summary>
        public static ToolbarSlots Toolbar(
            int barWidth, int spinnerSize, int labelGap,
            int firstToggleWidth = 0, int secondToggleWidth = 0)
        {
            int analyzeX = Math.Max(0, barWidth - AnalyzeButtonWidth);
            int secondX = secondToggleWidth <= 0
                ? analyzeX
                : Math.Max(Inset, analyzeX - CellGap - secondToggleWidth);
            int firstX = firstToggleWidth <= 0
                ? secondX
                : Math.Max(Inset, secondX - CellGap - firstToggleWidth);
            int statusRight = firstX - spinnerSize - 2 * labelGap;
            return new ToolbarSlots(
                analyzeX, firstX, secondX, Inset, Math.Max(0, statusRight - Inset));
        }

        public readonly struct ModeStripSlots
        {
            /// <summary>Left edge of the "Compare:" caption, or -1 when it does not fit.</summary>
            public readonly int LabelX;

            /// <summary>Left edge of the first option's indicator.</summary>
            public readonly int FirstX;

            /// <summary>Left edge of the second option's indicator.</summary>
            public readonly int SecondX;

            public ModeStripSlots(int labelX, int firstX, int secondX)
            {
                LabelX = labelX;
                FirstX = firstX;
                SecondX = secondX;
            }
        }

        /// <summary>
        /// The right-anchored comparison-mode strip: caption, then the two
        /// options in reading order, laid out from the right edge so the
        /// last one ends where the table does.
        /// <para>
        /// The caption is the only droppable part. At a width where it
        /// would run under whatever sits to its left (<paramref name="minX"/>
        /// is that control's right edge) it is dropped rather than
        /// overlapped - the two options are self-describing, the word
        /// "Compare:" is not load-bearing, and BOTH options staying legible
        /// is the whole point of the control.
        /// </para>
        /// </summary>
        public static ModeStripSlots ModeStrip(
            int barWidth, int labelWidth, int firstWidth, int secondWidth, int gap, int minX)
        {
            barWidth = Math.Max(0, barWidth);
            minX = Math.Max(0, minX);

            int secondX = Math.Max(minX, barWidth - Inset - secondWidth);
            int firstX = Math.Max(minX, secondX - gap - firstWidth);
            int labelX = firstX - gap - labelWidth;

            return new ModeStripSlots(labelX < minX ? -1 : labelX, firstX, secondX);
        }

        /// <summary>
        /// Currency shortfalls deliberately do NOT share the gate strip's
        /// rails any more. On the shared grid an entry rendered directly
        /// under whichever gate column its index landed on ("Ascalonian
        /// Tear" under Materials, an essence under Disciplines), which read
        /// in game as children of unrelated gates. Their own
        /// indented, icon-led grid makes them parse as one currency list
        /// owned by the row.
        /// </summary>
        public const int CurrenciesPerLine = 3;
        public const int MaxCurrencyLines = 3;

        /// <summary>Indent of the currency grid under the sub-line band.</summary>
        public const int CurrencyIndent = 16;

        /// <summary>
        /// The breakdown's currency entries draw at the game's wallet LIST
        /// tier, deliberately the larger icons, which is why a currency line
        /// has a pitch of its own rather than sharing the text sub-line's
        /// 20px. The
        /// Remaining cell's inline gold/silver/copper run stays on the BAR
        /// tier: that one really is an inline coin run inside a sentence,
        /// which is what the bar tier is for.
        /// </summary>
        public const int CurrencyIconSize = CurrencyIconTiers.WalletListIconSize;

        /// <summary>Gap between a currency icon's frame and its name.</summary>
        public const int CurrencyIconGap = 6;

        /// <summary>
        /// One cell of the currency grid: CurrenciesPerLine equal cells
        /// across the indented band, remainder to the last cell, same
        /// integer-exact rule as GateCell.
        /// </summary>
        public static void CurrencyCell(in Bands bands, int index, out int x, out int width)
        {
            int bandX = bands.SubLineX + CurrencyIndent;
            int bandWidth = Math.Max(0, bands.SubLineWidth - CurrencyIndent);
            if (index < 0 || index >= CurrenciesPerLine || bandWidth <= 0)
            {
                x = bandX;
                width = 0;
                return;
            }

            int left = bandX + (int)((long)bandWidth * index / CurrenciesPerLine);
            int right = bandX + (int)((long)bandWidth * (index + 1) / CurrenciesPerLine);
            x = left;
            width = Math.Max(0, right - left);
        }

        /// <summary>
        /// How many currency shortfall sub-lines a row needs. Deliberately
        /// independent of width: a width-dependent count would change a
        /// row's HEIGHT mid-drag.
        /// </summary>
        public static int CurrencyLineCount(int currencyCount)
        {
            return (CurrenciesShown(currencyCount) + CurrenciesPerLine - 1) / CurrenciesPerLine;
        }

        /// <summary>
        /// How many currency shortfalls a row actually draws. The grid holds
        /// CurrenciesPerLine by MaxCurrencyLines and the rest are dropped.
        /// The Currencies gate averages over all of them, so a row can score
        /// against currencies it never listed. RankerTabContent pairs this
        /// with CurrencyOverflowNote so the drop is stated.
        /// </summary>
        public static int CurrenciesShown(int currencyCount)
        {
            if (currencyCount <= 0)
            {
                return 0;
            }

            return Math.Min(currencyCount, CurrenciesPerLine * MaxCurrencyLines);
        }

        /// <summary>
        /// The line that owns up to the currencies the grid could not fit,
        /// or null when it fit them all. Same disclosure the discipline note
        /// and the recipe tree's overflow pill already make.
        /// </summary>
        public static string CurrencyOverflowNote(int currencyCount)
        {
            int hidden = Math.Max(0, currencyCount - CurrenciesShown(currencyCount));
            if (hidden == 0)
            {
                return null;
            }

            return StatusText.Count(hidden, "more currency", "more currencies") + " not shown";
        }
    }
}
