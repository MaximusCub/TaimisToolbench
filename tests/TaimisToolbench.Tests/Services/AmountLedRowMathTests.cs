using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The Used Materials table's column order: Amount first, Item after it.
    /// <para>
    /// Widths come from BitmapFont.MeasureString, which is Blish-bound, so
    /// the band is passed in. 99 is the shipped floor for it: the "Amount"
    /// header block at the ColumnHeader tier, which
    /// SnapshotItemGridLayout.AmountColumnFloor states term by term as 79px
    /// of word plus the sort indicator's gap and slot.
    /// </para>
    /// </summary>
    public class AmountLedRowMathTests
    {
        private const int AmountBand = SnapshotItemGridLayout.AmountColumnFloor;

        // The module's minimum window (1378px) leaves the plan panel this
        // wide, so it is the width every column below has least room at.
        private const int MinimumPanelWidth = 1252;

        [Fact]
        public void TheInsetsAreTheSnapshotCells_NotNumbersOfTheirOwn()
        {
            // The Snapshot tab settled this column order first. A second
            // set of numbers here would drift from it silently.
            Assert.Equal(SnapshotItemGridLayout.CellAmountX, AmountLedRowMath.AmountX);
            Assert.Equal(
                SnapshotItemGridLayout.CellAmountGap, AmountLedRowMath.AmountToNameGap);
            Assert.Equal(
                SnapshotItemGridLayout.CellIconX(AmountBand), AmountLedRowMath.IconX(AmountBand));
        }

        [Fact]
        public void TheIconOpensPastTheAmountBand_AndTheNamePastTheIcon()
        {
            // 8 inset, a 99px band, the 12px gap: the icon frame rules at
            // 119 and the name at 169, past the 42px tier-2 frame and the
            // 8px gap the Shopping List's rows keep.
            Assert.Equal(119, AmountLedRowMath.IconX(AmountBand));
            Assert.Equal(169, AmountLedRowMath.NameX(AmountBand));
            Assert.Equal(
                PlanRelayoutMath.IconLedRowNameX - PlanRelayoutMath.IconLedRowIconX,
                AmountLedRowMath.NameX(AmountBand) - AmountLedRowMath.IconX(AmountBand));
        }

        [Fact]
        public void TheItemColumnRunsToThePinnedEdge_SoItKeepsWhatTheMoveCostIt()
        {
            // The name gave up 111px on its left and took exactly that
            // back on its right, because the band and its gap left the row
            // rather than moved within it. Same budget as when the
            // quantities closed the row: 1075px at the minimum window.
            int budget = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                PlanRelayoutMath.PinnedRightEdge(MinimumPanelWidth), 0, 0,
                AmountLedRowMath.NameX(AmountBand));

            Assert.Equal(1075, budget);
            Assert.Equal(
                PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    PlanRelayoutMath.PinnedRightEdge(MinimumPanelWidth), AmountBand,
                    AmountLedRowMath.AmountToNameGap, PlanRelayoutMath.IconLedRowNameX),
                budget);
        }

        [Fact]
        public void AWiderPanelGoesEntirelyToTheName_BecauseNoOtherColumnReadsTheWidth()
        {
            // Every x on the row is data-derived now, which is why the row
            // registers no reposition closure: 200px more panel is 200px
            // more name and nothing else.
            Assert.Equal(
                200,
                PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    PlanRelayoutMath.PinnedRightEdge(MinimumPanelWidth + 200), 0, 0,
                    AmountLedRowMath.NameX(AmountBand))
                - PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    PlanRelayoutMath.PinnedRightEdge(MinimumPanelWidth), 0, 0,
                    AmountLedRowMath.NameX(AmountBand)));
        }

        [Fact]
        public void TheQuantitiesCentreInTheBand_AndTheHeaderTakesTheSameSeat()
        {
            // A short "1x" and a long "4250x" read as one column because
            // both centre, and the word sits on their centre line rather
            // than beside them.
            Assert.Equal(
                AmountLedRowMath.AmountX + ((AmountBand - 32) / 2),
                AmountLedRowMath.AmountTextX(AmountBand, 32));
            Assert.Equal(
                AmountLedRowMath.AmountX,
                AmountLedRowMath.AmountTextX(AmountBand, AmountBand));
        }

        [Fact]
        public void AValueWiderThanTheBandPinsLeft_RatherThanRunningUnderTheIcon()
        {
            // The band is floored at the header block and grown by the
            // pre-scan, so this cannot happen in the shipped table. It is
            // the degradation if one ever measures wider than the band it
            // was scanned into.
            Assert.Equal(
                AmountLedRowMath.AmountX,
                AmountLedRowMath.AmountTextX(AmountBand, AmountBand + 40));
        }

        [Fact]
        public void TheHeaderSplitSitsInTheGap_NotOnEitherColumnsInk()
        {
            int split = AmountLedRowMath.HeaderSplitX(AmountBand);

            Assert.Equal(113, split);
            Assert.True(split > AmountLedRowMath.AmountX + AmountBand);
            Assert.True(split < AmountLedRowMath.IconX(AmountBand));
        }

        [Fact]
        public void AnEmptyBandCollapsesTheColumn_RatherThanIndentingByANegative()
        {
            // No rows, so nothing was measured. The icon falls back onto
            // the inset plus the gap, and nothing lands at a negative x.
            Assert.Equal(
                AmountLedRowMath.AmountX + AmountLedRowMath.AmountToNameGap,
                AmountLedRowMath.IconX(0));
            Assert.Equal(
                AmountLedRowMath.AmountX + AmountLedRowMath.AmountToNameGap,
                AmountLedRowMath.IconX(-40));
            Assert.True(AmountLedRowMath.AmountTextX(0, 20) >= 0);
        }
    }
}
