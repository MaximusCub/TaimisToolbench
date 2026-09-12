using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The inline loading spinner trails an auto-width status label inside a
    /// fixed-height row, so the two things that can actually go wrong are
    /// "it stops tracking the label's right edge" and "it grows out of the
    /// row". Both are arithmetic, so both are pinned here.
    /// </summary>
    public class InlineSpinnerLayoutTests
    {
        [Fact]
        public void Place_PutsTheSpinnerOneGapRightOfTheLabel()
        {
            var placement = InlineSpinnerLayout.Place(
                labelX: 0, labelY: 81, labelWidth: 140, labelHeight: 19,
                spinnerSize: 18, gap: 6);

            Assert.Equal(146, placement.X);
        }

        [Fact]
        public void Place_TracksTheLabelWideningByExactlyTheWidthDelta()
        {
            // The label is AutoSizeWidth and its text changes several times
            // a second during a generation; the spinner must follow it
            // rather than sit at a position captured once at build time.
            var narrow = InlineSpinnerLayout.Place(0, 81, 100, 19, 18, 6);
            var wide = InlineSpinnerLayout.Place(0, 81, 260, 19, 18, 6);

            Assert.Equal(160, wide.X - narrow.X);
            Assert.Equal(narrow.Y, wide.Y);
        }

        [Fact]
        public void Place_CentersTheSpinnerOnATallerLabel()
        {
            var placement = InlineSpinnerLayout.Place(0, 100, 50, 24, 18, 6);

            Assert.Equal(103, placement.Y);
        }

        [Fact]
        public void Place_NeverStartsAboveTheLabelTopWhenTheSpinnerIsTaller()
        {
            // The label is top-aligned in its row, so a spinner centered on
            // a shorter label would hang above the row band. Clamping to the
            // label's own top keeps it inside the row for any spinner that
            // fits the row at all.
            var placement = InlineSpinnerLayout.Place(0, 100, 50, 12, 20, 6);

            Assert.Equal(100, placement.Y);
        }

        [Fact]
        public void Place_HonorsANonZeroLabelOrigin()
        {
            var placement = InlineSpinnerLayout.Place(
                labelX: 12, labelY: 2, labelWidth: 80, labelHeight: 19,
                spinnerSize: 18, gap: 6);

            Assert.Equal(98, placement.X);
            Assert.Equal(2, placement.Y);
        }

        [Fact]
        public void PlanStripSpinnerFitsThePlanTabsStatusRow()
        {
            // The plan strip's status row is exactly the gap between the
            // status label's Y and the separator beneath it. A spinner
            // taller than that would paint over the separator.
            Assert.True(
                InlineSpinnerLayout.PlanStripSize <= TopRegionLayoutMath.StatusToSeparatorGap,
                "Plan-strip spinner must fit inside the status row it sits in.");
        }

        [Fact]
        public void SnapshotStatusSpinnerFitsTheSnapshotTabsStatusRow()
        {
            Assert.True(
                InlineSpinnerLayout.SnapshotStatusSize <= SnapshotHeaderLayout.StatusRowHeight,
                "Snapshot-tab spinner must fit inside the status panel it sits in.");
        }

        [Fact]
        public void LabelWidthForText_TakesTheTextWidthNotTheBudget()
        {
            Assert.Equal(120, InlineSpinnerLayout.LabelWidthForText(textWidth: 120, budget: 400));
        }

        [Fact]
        public void SpinnerSitsBesideTheTextRatherThanAtTheEndOfTheBudget()
        {
            // The Ranker toolbar reserved 400 pixels for a status line that
            // measured 120. Sizing the label to the reserve seated the
            // spinner 280 pixels right of the text it belongs to.
            const int budget = 400;
            const int textWidth = 120;

            var stranded = InlineSpinnerLayout.Place(
                labelX: 0, labelY: 8, labelWidth: budget, labelHeight: 19,
                spinnerSize: InlineSpinnerLayout.SnapshotStatusSize,
                gap: InlineSpinnerLayout.LabelGap);

            int seated = InlineSpinnerLayout.LabelWidthForText(textWidth, budget);
            var beside = InlineSpinnerLayout.Place(
                0, 8, seated, 19,
                InlineSpinnerLayout.SnapshotStatusSize, InlineSpinnerLayout.LabelGap);

            Assert.Equal(textWidth + InlineSpinnerLayout.LabelGap, beside.X);
            Assert.Equal(280, stranded.X - beside.X);
        }

        [Fact]
        public void LabelWidthForText_CapsAtTheBudgetSoTheSpinnerStaysInItsBand()
        {
            // Ellipsizing measures the shortened string; rounding can put
            // it a pixel past the budget the band reserved for it.
            Assert.Equal(400, InlineSpinnerLayout.LabelWidthForText(textWidth: 401, budget: 400));
        }

        [Fact]
        public void LabelWidthForText_TreatsNegativeInputsAsZero()
        {
            Assert.Equal(0, InlineSpinnerLayout.LabelWidthForText(textWidth: 40, budget: -1));
            Assert.Equal(0, InlineSpinnerLayout.LabelWidthForText(textWidth: -1, budget: 400));
        }

        [Fact]
        public void RankerAndSnapshotDrawTheSameSizeSpinner()
        {
            // The Ranker's spinner reads small next to the Snapshot tab's
            // only because it was stranded alone at the right hand end.
            // Both tabs build one at this constant, so seating it correctly
            // changes no size.
            Assert.Equal(22, InlineSpinnerLayout.SnapshotStatusSize);
        }
    }
}
