using System;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class RankerRowLayoutTests
    {
        // The module's supported window widths, minus the tab-panel chrome
        // and the scrollbar allowance the view subtracts before calling in.
        public static readonly object[][] RealWidths =
        {
            new object[] { WindowSizing.TabPanelWidthFor(1378) - WindowSizing.ScrollbarAllowance },
            new object[] { WindowSizing.TabPanelWidthFor(1638) - WindowSizing.ScrollbarAllowance },
            new object[] { WindowSizing.TabPanelWidthFor(1836) - WindowSizing.ScrollbarAllowance },
            new object[] { WindowSizing.TabPanelWidthFor(2406) - WindowSizing.ScrollbarAllowance },
            new object[] { WindowSizing.TabPanelWidthFor(2560) - WindowSizing.ScrollbarAllowance },
        };

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AtEveryRealWidth_TheNameBandIsPositiveAndClearsTheReadyCell(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, remainingCellWidth: 120);

            Assert.True(bands.NameWidth > 0);
            Assert.True(bands.NameX + bands.NameWidth <= bands.ReadyRightEdge);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AtEveryRealWidth_TheLastButtonEndsExactlyOnTheRowsOneRightEdge(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, remainingCellWidth: 120);

            Assert.Equal(rowWidth - RankerRowLayout.Inset,
                bands.RemoveX + RankerRowLayout.ButtonWidth);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AtEveryRealWidth_ThePinnedBlockNeverOverlapsLeftToRight(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, remainingCellWidth: 120);

            Assert.True(bands.NameX + bands.NameWidth <= bands.StatusX);
            Assert.True(bands.StatusX + bands.StatusWidth <= bands.ReadyRightEdge);
            Assert.True(bands.ReadyRightEdge <= bands.DaysRightEdge - RankerRowLayout.DaysCellWidth);
            Assert.True(bands.DaysRightEdge <= bands.RemainingRightEdge - 120);
            Assert.True(bands.RemainingRightEdge <= bands.UpX);
            Assert.True(bands.UpX + RankerRowLayout.ButtonWidth <= bands.DownX);
            Assert.True(bands.DownX + RankerRowLayout.ButtonWidth <= bands.RemoveX);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-5000)]
        [InlineData(120)]
        [InlineData(300)]
        public void DegenerateWidths_ClampRatherThanEmittingNegativeWidths(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, remainingCellWidth: 120);

            Assert.True(bands.NameWidth >= 0);
            Assert.True(bands.SubLineWidth >= 0);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void TheGateStripFillsTheSubLineBandExactly(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 120);

            int previousRight = bands.SubLineX;
            for (int i = 0; i < RankerRowLayout.GateCellCount; i++)
            {
                RankerRowLayout.GateCell(bands, i, out int x, out int width);
                Assert.Equal(previousRight, x);
                Assert.True(width > 0);
                previousRight = x + width;
            }

            // Justified to the panel: no rounding gap stranded on the right.
            Assert.Equal(bands.SubLineX + bands.SubLineWidth, previousRight);
        }

        [Fact]
        public void TheGateStripCarriesAllFiveGates()
        {
            // Field issue 7: the strip gained a Recipes cell; the cell count
            // is what the view's render loop truncates against.
            Assert.Equal(5, RankerRowLayout.GateCellCount);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(RankerRowLayout.GateCellCount)]
        [InlineData(99)]
        public void GateCell_OutOfRange_ReturnsZeroWidth(int index)
        {
            var bands = RankerRowLayout.Compute(1200, 120);

            RankerRowLayout.GateCell(bands, index, out _, out int width);

            Assert.Equal(0, width);
        }

        [Fact]
        public void ARowWithNothingBelowItsHeadline_IsExactlyTheBaseRowTall()
        {
            // Both display toggles off, and an unmeasured row's height.
            var empty = RankerRowLayout.SubLines(hasGates: false, currencyLines: 0, noteLines: 0);

            Assert.Equal(RankerRowLayout.RowHeight, empty.TotalHeight);
            Assert.Equal(-1, empty.GateY);
            Assert.Equal(-1, empty.CurrencyY);
            Assert.Equal(-1, empty.NoteY);
        }

        [Fact]
        public void ARowIsShorterByExactlyTheDetailItsTogglesDrop()
        {
            var categoriesOnly = RankerRowLayout.SubLines(hasGates: true, currencyLines: 0, noteLines: 0);
            var full = RankerRowLayout.SubLines(hasGates: true, currencyLines: 2, noteLines: 1);

            Assert.True(categoriesOnly.TotalHeight < full.TotalHeight);
            Assert.Equal(-1, categoriesOnly.CurrencyY);
            Assert.Equal(-1, categoriesOnly.NoteY);

            // The category strip sits at the same y either way: dropping the
            // currency list below it must not move the strip above it.
            Assert.Equal(categoriesOnly.GateY, full.GateY);
        }

        [Fact]
        public void EachBlockStartsBelowTheOneBeforeIt_AndInsideTheRow()
        {
            var block = RankerRowLayout.SubLines(hasGates: true, currencyLines: 3, noteLines: 2);

            Assert.True(block.GateY >= RankerRowLayout.RowHeight);
            Assert.True(block.CurrencyY >= block.GateY + RankerRowLayout.SubLineHeight);
            Assert.True(block.NoteY
                >= block.CurrencyY + 3 * RankerRowLayout.CurrencyLineHeight);
            Assert.True(block.NoteY + 2 * RankerRowLayout.SubLineHeight <= block.TotalHeight);
        }

        [Fact]
        public void CurrencyLinesUseTheirOwnPitch_BecauseTheirIconIsTallerThanText()
        {
            var one = RankerRowLayout.SubLines(hasGates: true, currencyLines: 1, noteLines: 0);
            var two = RankerRowLayout.SubLines(hasGates: true, currencyLines: 2, noteLines: 0);

            Assert.Equal(RankerRowLayout.CurrencyLineHeight, two.TotalHeight - one.TotalHeight);
            Assert.True(RankerRowLayout.CurrencyLineHeight > RankerRowLayout.SubLineHeight);
            Assert.True(RankerRowLayout.CurrencyLineHeight
                >= RankerRowLayout.CurrencyIconSize);
        }

        [Fact]
        public void NegativeLineCountsAreClamped()
        {
            var block = RankerRowLayout.SubLines(hasGates: false, currencyLines: -3, noteLines: -1);

            Assert.Equal(RankerRowLayout.RowHeight, block.TotalHeight);
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(6, 2)]
        [InlineData(7, 3)]
        [InlineData(9, 3)]
        [InlineData(20, 3)]
        public void CurrencyLineCount_IsMonotonicAndCappedAtThreeLines(int currencies, int expected)
        {
            Assert.Equal(expected, RankerRowLayout.CurrencyLineCount(currencies));
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void TheCurrencyGridIsIndentedAndFillsItsOwnBandExactly(int rowWidth)
        {
            // Field issue 6: currency entries no longer sit on the gate
            // rails - their grid starts CurrencyIndent inside the sub-line
            // band so they read as one owned list, not gate children.
            var bands = RankerRowLayout.Compute(rowWidth, 120);

            int previousRight = bands.SubLineX + RankerRowLayout.CurrencyIndent;
            for (int i = 0; i < RankerRowLayout.CurrenciesPerLine; i++)
            {
                RankerRowLayout.CurrencyCell(bands, i, out int x, out int width);
                Assert.Equal(previousRight, x);
                Assert.True(width > 0);
                previousRight = x + width;
            }

            Assert.Equal(bands.SubLineX + bands.SubLineWidth, previousRight);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AtEveryRealWidth_ACurrencyCellFitsIconNameAndValue(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 120);
            RankerRowLayout.CurrencyCell(bands, 0, out _, out int cellWidth);

            // Icon frame + gap + a usable name run + a right-aligned value.
            Assert.True(cellWidth >=
                RankerRowLayout.CurrencyIconSize + 2 + RankerRowLayout.CurrencyIconGap + 150);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(RankerRowLayout.CurrenciesPerLine)]
        [InlineData(99)]
        public void CurrencyCell_OutOfRange_ReturnsZeroWidth(int index)
        {
            var bands = RankerRowLayout.Compute(1200, 120);

            RankerRowLayout.CurrencyCell(bands, index, out _, out int width);

            Assert.Equal(0, width);
        }

        // ---------------------------------------------------------------
        // The header minimum-width floor - the live sandbox check's Fix 1.
        // The header labels right-align at the same edges the cells do, so
        // every band must stay at least as wide as its own header text even
        // when the table is empty and no cell has been measured.
        // ---------------------------------------------------------------
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(8)] // a dash-only remaining cell, the empty-table case
        [InlineData(99)]
        public void TheRemainingBandNeverDropsBelowItsHeaderFloor(int measuredCellWidth)
        {
            var bands = RankerRowLayout.Compute(1200, measuredCellWidth);

            Assert.True(bands.RemainingRightEdge - bands.DaysRightEdge
                >= RankerRowLayout.MinRemainingCellWidth + RankerRowLayout.CellGap);
        }

        [Fact]
        public void InThePackedRegime_AWiderMeasuredCellStillBeatsTheFloor()
        {
            // 700 is narrow enough that no coin band distributes, so both
            // rows are laid out by the packed right-to-left stack, which is
            // the regime this reserve arithmetic belongs to.
            var floored = RankerRowLayout.Compute(700, 8);
            var wide = RankerRowLayout.Compute(700, 180);

            Assert.False(floored.Distributed);
            Assert.False(wide.Distributed);
            Assert.Equal(RankerRowLayout.MinRemainingCellWidth + RankerRowLayout.CellGap,
                floored.RemainingRightEdge - floored.DaysRightEdge);
            Assert.Equal(180 + RankerRowLayout.CellGap,
                wide.RemainingRightEdge - wide.DaysRightEdge);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AnEmptyTablesHeaderCellsDoNotOverlap(int rowWidth)
        {
            // Exactly the shape the sandbox check photographed as
            // "ReadhyDaining": zero rows, so a dash-width coin band. Each
            // right-aligned header must clear the cell to its left even then.
            var bands = RankerRowLayout.Compute(rowWidth, 8);

            int statusCellRight = bands.StatusX + RankerRowLayout.MinStatusCellWidth;
            int readyCellLeft = bands.ReadyRightEdge - RankerRowLayout.ReadyCellWidth;
            int daysCellLeft = bands.DaysRightEdge - RankerRowLayout.DaysCellWidth;
            int remainingCellLeft = bands.RemainingRightEdge - RankerRowLayout.MinRemainingCellWidth;

            Assert.True(bands.NameX + bands.NameWidth <= bands.StatusX);
            Assert.True(statusCellRight <= readyCellLeft);
            Assert.True(bands.ReadyRightEdge <= daysCellLeft);
            Assert.True(bands.DaysRightEdge <= remainingCellLeft);
            Assert.True(bands.RemainingRightEdge < bands.UpX);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void TheNameBandStopsAGapShortOfTheStatusColumn(int rowWidth)
        {
            // The Status chip is LEFT-aligned on its own rail, so the name
            // that used to reserve the chip's width out of its own budget
            // now simply ends before it.
            var bands = RankerRowLayout.Compute(rowWidth, 120);

            Assert.Equal(bands.StatusX - RankerRowLayout.CellGap, bands.NameX + bands.NameWidth);
        }

        // ---------------------------------------------------------------
        // The toolbar row - field bug: refresh-progress text stamped onto
        // the fixed-width button spilled past its edges. The status band
        // and the button band must never overlap, at any width, with any
        // progress string.
        // ---------------------------------------------------------------
        private const int SpinnerSize = 20;
        private const int SpinnerGap = 6;

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AtEveryRealWidth_TheStatusBandAndButtonNeverOverlap(int barWidth)
        {
            var slots = RankerRowLayout.Toolbar(barWidth, SpinnerSize, SpinnerGap);

            // Status text, its trailing spinner and the gap after it all end
            // before the button starts.
            Assert.True(slots.StatusX + slots.StatusWidth + SpinnerGap + SpinnerSize + SpinnerGap
                <= slots.AnalyzeX);
            Assert.Equal(barWidth, slots.AnalyzeX + RankerRowLayout.AnalyzeButtonWidth);
            Assert.Equal(RankerRowLayout.Inset, slots.StatusX);
            Assert.True(slots.StatusWidth > 0);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void ALongProgressString_EllipsizesInsideTheStatusBand(int barWidth)
        {
            // The view feeds status text through TextWrapMath.Ellipsize
            // against ToolbarSlots.StatusWidth; proven here with the same
            // Blish-free arithmetic and a synthetic 8px-per-char measure.
            var slots = RankerRowLayout.Toolbar(barWidth, SpinnerSize, SpinnerGap);
            string progress = "Analyzing 17 of 25 - The Legendary Item With An Extremely " +
                "Long Name That Keeps Going. The first refresh of a session downloads " +
                "recipe data and can take a while, and this string is longer than any band.";
            System.Func<string, int> measure = s => 8 * (s ?? "").Length;

            string shown = TextWrapMath.Ellipsize(progress, slots.StatusWidth, measure);

            Assert.True(measure(shown) <= slots.StatusWidth);
            Assert.True(slots.StatusX + measure(shown) + SpinnerGap + SpinnerSize
                <= slots.AnalyzeX);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(50)]
        [InlineData(-10)]
        public void ToolbarDegenerateWidths_ClampRatherThanGoingNegative(int barWidth)
        {
            var slots = RankerRowLayout.Toolbar(barWidth, SpinnerSize, SpinnerGap);

            Assert.True(slots.AnalyzeX >= 0);
            Assert.True(slots.StatusWidth >= 0);
        }

        // The toolbar seats TWO display toggles now - the category strip and
        // the currency list are separate choices, and one checkbox cannot
        // carry two. These stand in for the real checkbox art plus label,
        // which the view measures.
        private const int FirstToggleWidth = 120;
        private const int SecondToggleWidth = 130;

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AtEveryRealWidth_BothTogglesSeatBetweenTheStatusBandAndRefresh(int barWidth)
        {
            var slots = RankerRowLayout.Toolbar(
                barWidth, SpinnerSize, SpinnerGap, FirstToggleWidth, SecondToggleWidth);

            Assert.True(slots.StatusX + slots.StatusWidth + SpinnerGap + SpinnerSize + SpinnerGap
                <= slots.FirstToggleX);
            Assert.True(slots.FirstToggleX + FirstToggleWidth <= slots.SecondToggleX);
            Assert.True(slots.SecondToggleX + SecondToggleWidth <= slots.AnalyzeX);
            Assert.Equal(barWidth, slots.AnalyzeX + RankerRowLayout.AnalyzeButtonWidth);
            Assert.True(slots.StatusWidth > 0);
        }

        [Fact]
        public void ToggleSlotsCostTheStatusBandExactlyTheirOwnWidth()
        {
            // No rail of nothing: a toolbar with no toggles hands the space
            // back to the status band rather than reserving it anyway.
            var none = RankerRowLayout.Toolbar(1200, SpinnerSize, SpinnerGap);
            var both = RankerRowLayout.Toolbar(
                1200, SpinnerSize, SpinnerGap, FirstToggleWidth, SecondToggleWidth);

            Assert.Equal(none.AnalyzeX, none.FirstToggleX);
            Assert.Equal(none.AnalyzeX, none.SecondToggleX);
            Assert.Equal(
                none.StatusWidth - FirstToggleWidth - SecondToggleWidth
                    - 2 * RankerRowLayout.CellGap,
                both.StatusWidth);
        }

        [Fact]
        public void AtAnAbsurdlyNarrowWidth_NeitherToggleIsSeatedOutsideTheRow()
        {
            var slots = RankerRowLayout.Toolbar(
                100, SpinnerSize, SpinnerGap, FirstToggleWidth, SecondToggleWidth);

            Assert.True(slots.FirstToggleX >= RankerRowLayout.Inset);
            Assert.True(slots.SecondToggleX >= RankerRowLayout.Inset);
            Assert.Equal(0, slots.StatusWidth);
        }

        // The comparison-mode radio strip. Measured footprints: the dot,
        // its gap and the widest of the two option labels at UiFonts.Body,
        // which the view measures for real; these stand in for them.
        private const int RadioLabelWidth = 44;
        private const int FirstOptionWidth = 130;
        private const int SecondOptionWidth = 120;
        private const int OptionGap = 16;
        private const int AddButtonRight = 380;

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AtEveryRealWidth_BothModeOptionsFitInsideTheRowAndNeverOverlap(int rowWidth)
        {
            var slots = RankerRowLayout.ModeStrip(
                rowWidth, RadioLabelWidth, FirstOptionWidth, SecondOptionWidth,
                OptionGap, AddButtonRight);

            Assert.True(slots.FirstX >= AddButtonRight);
            Assert.True(slots.FirstX + FirstOptionWidth <= slots.SecondX);
            Assert.True(slots.SecondX + SecondOptionWidth <= rowWidth - RankerRowLayout.Inset);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AtEveryRealWidth_TheCaptionClearsTheControlToItsLeft(int rowWidth)
        {
            var slots = RankerRowLayout.ModeStrip(
                rowWidth, RadioLabelWidth, FirstOptionWidth, SecondOptionWidth,
                OptionGap, AddButtonRight);

            // Shown or dropped, never overlapped: -1 is the view's cue to
            // hide it.
            Assert.True(slots.LabelX == -1 || slots.LabelX >= AddButtonRight);
            if (slots.LabelX >= 0)
            {
                Assert.True(slots.LabelX + RadioLabelWidth <= slots.FirstX);
            }
        }

        [Fact]
        public void WhenTheRowIsTooNarrowForTheCaption_ItIsDroppedRatherThanOverlapped()
        {
            var slots = RankerRowLayout.ModeStrip(
                AddButtonRight + FirstOptionWidth + OptionGap + SecondOptionWidth + RankerRowLayout.Inset,
                RadioLabelWidth, FirstOptionWidth, SecondOptionWidth, OptionGap, AddButtonRight);

            Assert.Equal(-1, slots.LabelX);
            Assert.Equal(AddButtonRight, slots.FirstX);
        }

        [Fact]
        public void AtAnAbsurdlyNarrowWidth_NothingIsPlacedLeftOfTheControlBeforeIt()
        {
            var slots = RankerRowLayout.ModeStrip(
                120, RadioLabelWidth, FirstOptionWidth, SecondOptionWidth, OptionGap, AddButtonRight);

            Assert.Equal(-1, slots.LabelX);
            Assert.Equal(AddButtonRight, slots.FirstX);
            Assert.Equal(AddButtonRight, slots.SecondX);
        }

        // WHY THE COLUMN HEADER HAS TO BE RE-SEATED WHENEVER THE COIN BAND
        // CHANGES. Ready and Days are derived by walking LEFT from the coin
        // band, so a table that has not been refreshed yet (coin band at its
        // MinRemainingCellWidth floor) puts those two rails in a different
        // place than the same table does once a real coin cell has been
        // measured. The header labels are right-aligned on the very same
        // rails, so a view that re-renders its rows without re-seating its
        // header leaves the two disagreeing by exactly this difference -
        // the reported "Ready/Days/Remaining are poorly aligned with the
        // content below" (measured at 37px in the 2026-08-27 capture).
        [Theory]
        [MemberData(nameof(RealWidths))]
        public void UnderDistribution_AWiderCoinBandMovesNoRailAtAll(int rowWidth)
        {
            // The reported "Ready/Days/Remaining are poorly aligned with the
            // content below" was this: the rails were walked LEFT from the
            // coin band, so the first refresh's real coin cell moved them by
            // 37px while the header stayed put. Equal tracks retire the
            // mechanism rather than re-fixing it - a track's width is a
            // fraction of the row, not a function of the widest cell in it.
            // The coin band now decides only WHETHER the row distributes.
            var narrow = RankerRowLayout.Compute(rowWidth, RankerRowLayout.MinRemainingCellWidth);
            var wide = RankerRowLayout.Compute(rowWidth, RankerRowLayout.MinRemainingCellWidth + 37);

            Assert.True(narrow.Distributed);
            Assert.True(wide.Distributed);
            Assert.Equal(narrow.RemainingRightEdge, wide.RemainingRightEdge);
            Assert.Equal(narrow.DaysRightEdge, wide.DaysRightEdge);
            Assert.Equal(narrow.ReadyRightEdge, wide.ReadyRightEdge);
            Assert.Equal(narrow.StatusX, wide.StatusX);
            Assert.Equal(narrow.NameWidth, wide.NameWidth);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void ACellTooWideForATrack_FallsBackToThePackedStack(int rowWidth)
        {
            // The documented narrow-panel escape: on a row where one column
            // needs more than its share, an evenly spaced illegible table is
            // worse than a cramped legible one.
            var packed = RankerRowLayout.Compute(rowWidth, rowWidth);

            Assert.False(packed.Distributed);
            Assert.True(packed.DaysRightEdge < packed.RemainingRightEdge);
            Assert.True(packed.ReadyRightEdge < packed.DaysRightEdge);
            Assert.True(packed.StatusX < packed.ReadyRightEdge);
            Assert.True(packed.NameWidth >= 0);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void TheSameBandWidth_GivesTheHeaderAndTheCellsOneSetOfRails(int rowWidth)
        {
            // The view derives both from this one call; anything that
            // recomputes the band has to recompute both sides of it.
            var first = RankerRowLayout.Compute(rowWidth, 137);
            var second = RankerRowLayout.Compute(rowWidth, 137);

            Assert.Equal(first.ReadyRightEdge, second.ReadyRightEdge);
            Assert.Equal(first.DaysRightEdge, second.DaysRightEdge);
            Assert.Equal(first.RemainingRightEdge, second.RemainingRightEdge);
        }

        // Independent mode shows no reorder arrows: its order IS its answer.
        // The two rails they leave behind are reclaimed rather than stranded,
        // which is the module's standing rule about dead space.
        [Theory]
        [MemberData(nameof(RealWidths))]
        public void WithNoReorderButtons_TheRowStillEndsOnItsOneRightEdge(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 137, showReorder: false);

            Assert.Equal(rowWidth - RankerRowLayout.Inset,
                bands.RemoveX + RankerRowLayout.ButtonWidth);
            Assert.Equal(-1, bands.UpX);
            Assert.Equal(-1, bands.DownX);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void WithNoReorderButtons_TheReclaimedWidthIsSpreadAcrossTheTracks(int rowWidth)
        {
            // The rails the arrows leave behind are not stranded. Under
            // distribution the whole span widens by their width, so every
            // track - the name's two included - takes a share of it rather
            // than the last column taking all of it.
            var withArrows = RankerRowLayout.Compute(rowWidth, 137, showReorder: true);
            var without = RankerRowLayout.Compute(rowWidth, 137, showReorder: false);

            int reclaimed = 2 * (RankerRowLayout.ButtonWidth + RankerRowLayout.ButtonGap);
            Assert.Equal(withArrows.RemoveX, without.RemoveX);
            Assert.Equal(withArrows.RemainingRightEdge + reclaimed, without.RemainingRightEdge);
            Assert.True(without.DaysRightEdge > withArrows.DaysRightEdge);
            Assert.True(without.ReadyRightEdge > withArrows.ReadyRightEdge);
            Assert.True(without.StatusX > withArrows.StatusX);
            Assert.True(without.NameWidth > withArrows.NameWidth);

            // Every gain is a share of the same reclaimed span, so none of
            // them can exceed it.
            Assert.True(without.NameWidth - withArrows.NameWidth <= reclaimed);
        }

        // ---------------------------------------------------------------
        // THE DISTRIBUTED TRACKS. The four data columns divide the span
        // from the item name's left edge to the last column's right edge
        // into TrackCount equal tracks, the name taking NameTrackSpan of
        // them - SummarySectionLayoutMath's currency-table idiom, applied
        // because the four columns used to huddle against the buttons and
        // leave the middle of a wide row empty.
        //
        // A display toggle changes a row's HEIGHT and nothing else, so the
        // horizontal sweeps below are density-independent by construction and
        // the vertical ones are swept over both densities.
        // ---------------------------------------------------------------
        public static readonly object[][] RealWidthsBothOrderings =
            Cross(RealWidths, new object[] { true, false });

        public static readonly object[][] RealWidthsBothDensities =
            Cross(RealWidths, new object[] { true, false });

        private static object[][] Cross(object[][] left, object[] right)
        {
            var rows = new object[left.Length * right.Length][];
            int at = 0;
            foreach (var l in left)
            {
                foreach (var r in right)
                {
                    rows[at++] = new[] { l[0], r };
                }
            }

            return rows;
        }

        [Theory]
        [MemberData(nameof(RealWidthsBothOrderings))]
        public void AtEveryRealWidth_TheFourDataColumnsAreEqualTracks(int rowWidth, bool showReorder)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130, showReorder);
            Assert.True(bands.Distributed);

            // Each track's width, read off the edges the view actually uses.
            // Status is the left-aligned one, so its track is its band plus
            // the gap that keeps the chip off the bar beside it.
            int status = bands.StatusWidth + RankerRowLayout.CellGap;
            int ready = bands.ReadyRightEdge - bands.StatusX - status;
            int days = bands.DaysRightEdge - bands.ReadyRightEdge;
            int remaining = bands.RemainingRightEdge - bands.DaysRightEdge;

            // Integer-exact off the span, so no two tracks differ by more
            // than the one pixel a remainder can leave.
            Assert.True(Math.Abs(status - ready) <= 1, status + " vs " + ready);
            Assert.True(Math.Abs(ready - days) <= 1, ready + " vs " + days);
            Assert.True(Math.Abs(days - remaining) <= 1, days + " vs " + remaining);

            // And the name really does take NameTrackSpan of them.
            int nameSpan = bands.StatusX - bands.NameX;
            Assert.True(Math.Abs(nameSpan - (RankerRowLayout.NameTrackSpan * days)) <= RankerRowLayout.NameTrackSpan);
        }

        [Theory]
        [MemberData(nameof(RealWidthsBothOrderings))]
        public void AtEveryRealWidth_TheLastTrackEndsExactlyOnTheDataSpansOwnEnd(int rowWidth, bool showReorder)
        {
            // What integer-exact track edges buy: the Remaining column's
            // rail lands on the pixel the pinned block starts at, never a
            // rounding pixel short of it.
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130, showReorder);
            int pinned = showReorder ? bands.UpX : bands.RemoveX;

            Assert.Equal(pinned - RankerRowLayout.CellGap, bands.RemainingRightEdge);
        }

        [Theory]
        [MemberData(nameof(RealWidthsBothOrderings))]
        public void AtEveryRealWidth_EveryTrackHoldsItsOwnWidestCell(int rowWidth, bool showReorder)
        {
            const int WidestStatusChip = 130;
            var bands = RankerRowLayout.Compute(rowWidth, 137, WidestStatusChip, showReorder);

            Assert.True(bands.StatusWidth >= WidestStatusChip);
            Assert.True(bands.DaysRightEdge - bands.ReadyRightEdge >= RankerRowLayout.ReadyCellWidth);
            Assert.True(bands.RemainingRightEdge - bands.DaysRightEdge >= 137);
        }

        [Theory]
        [MemberData(nameof(RealWidthsBothOrderings))]
        public void AtEveryRealWidth_TheReadinessBarFitsInsideItsOwnTrack(int rowWidth, bool showReorder)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130, showReorder);

            // Wide enough for the centred "100%" it carries at bold 18, and
            // it FILLS its track rather than leaving part of it stranded.
            Assert.True(bands.ReadyBarWidth >= RankerRowLayout.MinReadinessBarWidth);
            Assert.Equal(bands.ReadyRightEdge, bands.ReadyBarX + bands.ReadyBarWidth);
            Assert.Equal(bands.StatusX + bands.StatusWidth + RankerRowLayout.CellGap, bands.ReadyBarX);
            Assert.True(bands.ReadyBarWidth > RankerRowLayout.ReadyCellWidth);
        }

        [Fact]
        public void ThePackedFallbackStillLeavesRoomForABar()
        {
            var packed = RankerRowLayout.Compute(700, 137);

            Assert.False(packed.Distributed);
            Assert.True(packed.ReadyBarWidth >= RankerRowLayout.MinReadinessBarWidth);
            Assert.Equal(packed.ReadyRightEdge, packed.ReadyBarX + packed.ReadyBarWidth);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(120)]
        public void DegenerateWidths_LeaveNoNegativeBarOrStatusBand(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 120);

            Assert.True(bands.ReadyBarWidth >= 0);
            Assert.True(bands.StatusWidth >= 0);
            Assert.True(bands.NameWidth >= 0);
        }

        // ---------------------------------------------------------------
        // ONE TRACK PER DATA COLUMN, and the header centres on the same one
        // its cells do. Right-aligning both lines them up only at that edge,
        // so a short header over wide cells reads as belonging to the column
        // on its right - reported twice against this table ("Status" nowhere
        // near the chips, "Remaining" nowhere near the gold).
        // ---------------------------------------------------------------
        [Theory]
        [MemberData(nameof(RealWidthsBothOrderings))]
        public void EachDataColumnsTrackIsTheBandItsHeaderAndItsCellsShare(
            int rowWidth, bool showReorder)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130, showReorder);

            bands.DataTrack(RankerRowLayout.StatusColumn, out int statusX, out int statusWidth);
            bands.DataTrack(RankerRowLayout.ReadyColumn, out int readyX, out int readyWidth);
            bands.DataTrack(RankerRowLayout.DaysColumn, out int daysX, out int daysWidth);
            bands.DataTrack(RankerRowLayout.RemainingColumn, out int coinX, out int coinWidth);

            // Each track is the published band, so nothing can read the
            // column's edges two ways.
            Assert.Equal(bands.StatusX, statusX);
            Assert.Equal(bands.StatusWidth, statusWidth);
            Assert.Equal(bands.ReadyBarX, readyX);
            Assert.Equal(bands.ReadyBarWidth, readyWidth);
            Assert.Equal(bands.DaysTrackX, daysX);
            Assert.Equal(bands.DaysRightEdge, daysX + daysWidth);
            Assert.Equal(bands.RemainingTrackX, coinX);
            Assert.Equal(bands.RemainingRightEdge, coinX + coinWidth);

            // Left to right, in order, never overlapping.
            Assert.True(statusX + statusWidth <= readyX);
            Assert.True(readyX + readyWidth <= daysX);
            Assert.True(daysX + daysWidth <= coinX);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void UnderDistribution_TheFourTracksTileTheDataSpanWithNoGapButTheirOwn(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130);
            Assert.True(bands.Distributed);

            // Status's track is its band plus the one CellGap that keeps a
            // chip off the bar beside it; the other three meet edge to edge.
            bands.DataTrack(RankerRowLayout.StatusColumn, out int statusX, out int statusWidth);
            bands.DataTrack(RankerRowLayout.ReadyColumn, out int readyX, out _);
            bands.DataTrack(RankerRowLayout.DaysColumn, out int daysX, out _);
            bands.DataTrack(RankerRowLayout.RemainingColumn, out int coinX, out _);

            Assert.Equal(statusX + statusWidth + RankerRowLayout.CellGap, readyX);
            Assert.Equal(bands.ReadyRightEdge, daysX);
            Assert.Equal(bands.DaysRightEdge, coinX);
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AHeaderAndTheCellUnderIt_ShareOneCentreRatherThanOneEdge(int rowWidth)
        {
            // The reported miss, in arithmetic: a bold "Status" is ~62px and
            // an "Affordable now" chip ~130, so the two agree on a centre and
            // on nothing else. Both are placed by the ONE shared law.
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130);
            const int HeaderWidth = 62;
            const int CellWidth = 130;

            for (int column = 0; column < RankerRowLayout.DataColumnCount; column++)
            {
                bands.DataTrack(column, out int trackX, out int trackWidth);
                int header = JustifiedColumnTracks.CenteredInBand(
                    trackX, trackWidth, HeaderWidth);
                int cell = JustifiedColumnTracks.CenteredInBand(trackX, trackWidth, CellWidth);

                // Integer halving can leave one pixel between two centres.
                Assert.True(
                    Math.Abs((header + (HeaderWidth / 2)) - (cell + (CellWidth / 2))) <= 1,
                    "column " + column + ": " + header + " vs " + cell);

                // And both sit inside the track they name.
                Assert.True(header >= trackX);
                Assert.True(header + HeaderWidth <= trackX + trackWidth);
                Assert.True(cell >= trackX);
                Assert.True(cell + CellWidth <= trackX + trackWidth);
            }
        }

        [Fact]
        public void InThePackedFallback_ATrackIsTheBandThatColumnReserves()
        {
            // Centring is regime-independent: the packed stack hands the view
            // the same shape of track, just one measured off reserved widths
            // instead of off equal shares.
            var packed = RankerRowLayout.Compute(700, 180, 150);
            Assert.False(packed.Distributed);

            packed.DataTrack(RankerRowLayout.StatusColumn, out _, out int statusWidth);
            packed.DataTrack(RankerRowLayout.ReadyColumn, out _, out int readyWidth);
            packed.DataTrack(RankerRowLayout.DaysColumn, out _, out int daysWidth);
            packed.DataTrack(RankerRowLayout.RemainingColumn, out _, out int coinWidth);

            Assert.Equal(150, statusWidth);
            Assert.Equal(RankerRowLayout.ReadyCellWidth, readyWidth);
            Assert.Equal(RankerRowLayout.DaysCellWidth, daysWidth);
            Assert.Equal(180, coinWidth);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(RankerRowLayout.DataColumnCount)]
        [InlineData(99)]
        public void DataTrack_OutOfRange_ReturnsZeroWidth(int column)
        {
            var bands = RankerRowLayout.Compute(1200, 137, 130);

            bands.DataTrack(column, out _, out int width);

            Assert.Equal(0, width);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(120)]
        public void DegenerateWidths_LeaveNoNegativeTrack(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 120);

            for (int column = 0; column < RankerRowLayout.DataColumnCount; column++)
            {
                bands.DataTrack(column, out _, out int width);
                Assert.True(width >= 0);
            }
        }

        // ---------------------------------------------------------------
        // The gate strip's bars. Each cell is a fixed label band, then a
        // bar filling the rest of the cell - the dead space between a
        // gate's name and its right-aligned percentage.
        // ---------------------------------------------------------------
        private const int GateLabelBand = 84;

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void EveryGateBarStartsAtTheSameOffsetInsideItsOwnCell(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130);

            for (int i = 0; i < RankerRowLayout.GateCellCount; i++)
            {
                RankerRowLayout.GateCell(bands, i, out int cellX, out int cellWidth);
                RankerRowLayout.GateBar(bands, i, GateLabelBand, out int barX, out int barWidth);

                Assert.Equal(cellX + GateLabelBand + RankerRowLayout.GateLabelGap, barX);
                Assert.True(barWidth > 0);
                Assert.Equal(cellX + cellWidth - RankerRowLayout.CellGap, barX + barWidth);
            }
        }

        [Theory]
        [MemberData(nameof(RealWidths))]
        public void AGateBarNeverRunsIntoTheCellBesideIt(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130);

            for (int i = 0; i < RankerRowLayout.GateCellCount - 1; i++)
            {
                RankerRowLayout.GateBar(bands, i, GateLabelBand, out int barX, out int barWidth);
                RankerRowLayout.GateCell(bands, i + 1, out int nextX, out _);
                Assert.True(barX + barWidth <= nextX);
            }
        }

        [Fact]
        public void AGateBarCannotGoNegativeWhenTheLabelBandSwallowsTheCell()
        {
            var bands = RankerRowLayout.Compute(400, 137);

            RankerRowLayout.GateBar(bands, 0, 9999, out _, out int barWidth);
            Assert.Equal(0, barWidth);

            RankerRowLayout.GateBar(bands, 0, -50, out int barX, out _);
            RankerRowLayout.GateCell(bands, 0, out int cellX, out _);
            Assert.Equal(cellX + RankerRowLayout.GateLabelGap, barX);
        }

        // ---------------------------------------------------------------
        // The row's vertical rhythm, derived from the tier-1 icon that sets
        // RowHeight rather than listed as five literals. Both densities:
        // the sparse one is the same main line with the detail blocks gone.
        // ---------------------------------------------------------------
        [Theory]
        [MemberData(nameof(RealWidthsBothDensities))]
        public void EveryMainLineBoxIsCentredOnTheIconThatSetsTheRowHeight(int rowWidth, bool headlineOnly)
        {
            var bands = RankerRowLayout.Compute(rowWidth, 137, 130);
            Assert.True(bands.RowWidth > 0);

            foreach (int lineHeight in new[]
            {
                TypeRampMetrics.CaptionInk.LineHeight,
                TypeRampMetrics.BodyInk.LineHeight,
                TypeRampMetrics.StatusInk.LineHeight,
                RankerRowLayout.ReadyBarHeight,
            })
            {
                int y = RankerRowLayout.MainLineY(lineHeight);
                Assert.Equal(RankerRowLayout.RowHeight - y - lineHeight, y + ((RankerRowLayout.RowHeight - lineHeight) % 2));
                Assert.True(y >= 0);
                Assert.True(y + lineHeight <= RankerRowLayout.RowHeight);
            }

            var block = RankerRowLayout.SubLines(
                hasGates: true,
                currencyLines: headlineOnly ? 0 : 2,
                noteLines: headlineOnly ? 0 : 1);
            Assert.Equal(RankerRowLayout.RowHeight + RankerRowLayout.GateTopGap, block.GateY);
            Assert.True(block.TotalHeight >= block.GateY + RankerRowLayout.GateLineHeight);
        }

        [Fact]
        public void TheGateStripsPitchHoldsItsBar()
        {
            // The strip grew a painted bar per cell, so its pitch is no
            // longer a text sub-line's.
            Assert.True(RankerRowLayout.GateLineHeight >= RankerRowLayout.GateBarHeight);
            Assert.True(TypeRampMetrics.BodyInk.LineHeight <= RankerRowLayout.GateBarHeight);
            Assert.True(TypeRampMetrics.StatusInk.LineHeight <= RankerRowLayout.ReadyBarHeight);
        }

        [Fact]
        public void TheNameSpanAndTheTrackCountAreOneDecision()
        {
            // Compute reads Status, Ready, Days and Remaining off tracks
            // NameTrackSpan..TrackCount. Widening the name band by moving
            // NameTrackSpan alone would silently drop the Remaining column
            // off the end of the span rather than fail to build.
            Assert.Equal(RankerRowLayout.TrackCount,
                RankerRowLayout.NameTrackSpan + RankerRowLayout.DataColumnCount);
        }

        [Fact]
        public void MainLineY_ClampsRatherThanGoingNegative()
        {
            Assert.Equal(0, RankerRowLayout.MainLineY(RankerRowLayout.RowHeight + 100));
        }

        // --- The currencies the grid cannot fit ---
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(9, 9)]
        [InlineData(10, 9)]
        [InlineData(40, 9)]
        public void CurrenciesShown_StopsAtTheGridSize(int currencies, int expected)
        {
            Assert.Equal(expected, RankerRowLayout.CurrenciesShown(currencies));
        }

        [Fact]
        public void CurrencyLineCount_AgreesWithCurrenciesShown()
        {
            // The row's height and the number of chips it draws must come
            // from one count, or a chip lands outside its own row.
            for (int currencies = 0; currencies <= 40; currencies++)
            {
                int shown = RankerRowLayout.CurrenciesShown(currencies);
                int expectedLines =
                    (shown + RankerRowLayout.CurrenciesPerLine - 1) / RankerRowLayout.CurrenciesPerLine;
                Assert.Equal(expectedLines, RankerRowLayout.CurrencyLineCount(currencies));
            }
        }

        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        [InlineData(9)]
        public void CurrencyOverflowNote_IsAbsentWhenEveryCurrencyFits(int currencies)
        {
            Assert.Null(RankerRowLayout.CurrencyOverflowNote(currencies));
        }

        [Fact]
        public void CurrencyOverflowNote_CountsWhatTheGridDropped()
        {
            // The Currencies gate averages over all twelve, so the three the
            // row never lists still move its percentage.
            Assert.Equal("3 more currencies not shown", RankerRowLayout.CurrencyOverflowNote(12));
            Assert.Equal("1 more currency not shown", RankerRowLayout.CurrencyOverflowNote(10));
        }
    }
}
