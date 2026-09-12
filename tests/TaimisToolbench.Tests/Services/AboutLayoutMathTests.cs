using System.Linq;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class AboutLayoutMathTests
    {
        // Panel widths the tab actually resolves to: the window minimum, the
        // narrow-screen client, and a very wide window.
        private static readonly int FloorPanelWidth =
            WindowSizing.TabPanelWidthFor(WindowSizing.MinWindowWidth)
                - WindowSizing.ScrollbarAllowance;

        private const int WidePanelWidth = 2540;

        // These pin the RESOLVED numbers, not the sums that produce them: a
        // test that restates the const expression is checked by the compiler
        // and can never go red, so any term moving would be invisible. Here
        // a term moving forces a human to look at the new value and at the
        // boundary tests below, which are written in literal pixels and so
        // have an oracle independent of the constants under test.
        [Fact]
        public void FactsMinWidthAndTwoColumnThreshold_ResolveToTheirShippedPixels()
        {
            Assert.Equal(362, AboutLayoutMath.FactsMinWidth);
            Assert.Equal(954, AboutLayoutMath.TwoColumnThreshold);
        }

        [Fact]
        public void ColumnCount_TurnsOverBetween953And954Pixels()
        {
            Assert.Equal(1, AboutLayoutMath.ColumnCount(953));
            Assert.Equal(2, AboutLayoutMath.ColumnCount(954));
        }

        [Fact]
        public void LabelFloor_HoldsEveryFactLabelTheTabShips()
        {
            Assert.Equal(5, AboutLayoutMath.FactLabels.Count);

            foreach (string label in AboutLayoutMath.FactLabels)
            {
                Assert.True(
                    label.Length <= AboutLayoutMath.LabelRunChars,
                    label + " is wider than the label band's floor");
            }

            // The floor is not merely sufficient, it is sized to the widest
            // label the tab ships: shipping a wider one reds this.
            Assert.Equal(
                AboutLayoutMath.LabelRunChars,
                AboutLayoutMath.FactLabels.Max(label => label.Length));
            Assert.Equal(126, AboutLayoutMath.LabelFloor);
        }

        [Fact]
        public void ProseMeasure_IsInsideTheReadingBand()
        {
            // 66 characters at the module's own measured ~8.4px Body-16
            // average. The band is 45-75; a measure outside it is the defect
            // this constant exists to avoid.
            Assert.InRange(AboutLayoutMath.ProseTargetChars, 45, 75);
            Assert.InRange(AboutLayoutMath.ProseMeasure, 45 * 8, 75 * 9);
        }

        [Fact]
        public void ColumnCount_IsTwoAtEveryPanelWidthTheWindowActuallyReaches()
        {
            Assert.Equal(2, AboutLayoutMath.ColumnCount(WidePanelWidth));
            Assert.Equal(2, AboutLayoutMath.ColumnCount(FloorPanelWidth));
        }

        [Fact]
        public void ColumnCount_NeverGoesToThree_ADocumentIsNotAGrid()
        {
            // Fixed two-column assignment, not a ColumnBoardLayout: About's
            // blocks have fixed roles (facts vs prose) and a reader expects
            // the identity card on the left.
            Assert.Equal(2, AboutLayoutMath.ColumnCount(10000));
        }

        [Fact]
        public void ColumnWidth_SplitsThePanelLessItsGutter()
        {
            Assert.Equal(
                (WidePanelWidth - AboutLayoutMath.ColumnGutter) / 2,
                AboutLayoutMath.ColumnWidth(WidePanelWidth));
            Assert.Equal(
                AboutLayoutMath.ColumnWidth(WidePanelWidth) + AboutLayoutMath.ColumnGutter,
                AboutLayoutMath.SecondColumnX(WidePanelWidth));

            // Both columns plus the gutter fit inside the panel.
            Assert.True(
                (2 * AboutLayoutMath.ColumnWidth(WidePanelWidth)) + AboutLayoutMath.ColumnGutter
                    <= WidePanelWidth);
        }

        [Fact]
        public void ColumnWidth_OneColumnTakesTheWholePanel()
        {
            int narrow = AboutLayoutMath.TwoColumnThreshold - 1;

            Assert.Equal(narrow, AboutLayoutMath.ColumnWidth(narrow));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void ColumnWidth_NonPositivePanel_IsZero(int panelWidth)
        {
            Assert.Equal(0, AboutLayoutMath.ColumnWidth(panelWidth));
        }

        [Fact]
        public void TextBudget_IsCappedAtTheMeasureHoweverWideTheColumn()
        {
            // The declared divergence: past roughly a 1100px panel the tab
            // stops using its width, because a 280-character line is a worse
            // artefact than white space.
            Assert.Equal(
                AboutLayoutMath.ProseMeasure,
                AboutLayoutMath.TextBudget(AboutLayoutMath.ColumnWidth(WidePanelWidth)));
            Assert.Equal(AboutLayoutMath.ProseMeasure, AboutLayoutMath.TextBudget(10000));
        }

        [Fact]
        public void TextBudget_BelowTheMeasure_TakesWhatTheColumnHas()
        {
            const int NarrowColumn = 400;

            Assert.Equal(
                PlanRelayoutMath.PinnedRightEdge(NarrowColumn) - AboutLayoutMath.AboutInset,
                AboutLayoutMath.TextBudget(NarrowColumn));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-100)]
        public void TextBudget_FloorsRatherThanGoingNegative(int columnWidth)
        {
            Assert.True(AboutLayoutMath.TextBudget(columnWidth) >= 20);
        }

        [Fact]
        public void ValueColumnIsDerivedFromTheMeasuredLabelBand()
        {
            const int MeasuredBand = 150;

            Assert.Equal(
                AboutLayoutMath.AboutInset + MeasuredBand + AboutLayoutMath.LabelToValueGap,
                AboutLayoutMath.ValueX(MeasuredBand));
        }

        [Fact]
        public void LabelFloorIsRespectedWhenTheMeasuredLabelsAreNarrower()
        {
            Assert.Equal(
                AboutLayoutMath.ValueX(AboutLayoutMath.LabelFloor),
                AboutLayoutMath.ValueX(10));
        }

        [Fact]
        public void ValueMaxWidth_FlexesToTheColumnsPinnedRightEdge()
        {
            const int Band = 150;
            int narrow = AboutLayoutMath.ValueMaxWidth(600, Band);
            int wide = AboutLayoutMath.ValueMaxWidth(1200, Band);

            Assert.Equal(600, wide - narrow);
            Assert.Equal(
                PlanRelayoutMath.PinnedRightEdge(600) - AboutLayoutMath.ValueX(Band), narrow);
        }

        [Fact]
        public void CopyBoxWidth_IsCappedAtTheMeasureToo()
        {
            // A 2300px box holding a URL is the same defect as a 2300px
            // paragraph.
            Assert.Equal(
                AboutLayoutMath.ProseMeasure, AboutLayoutMath.CopyBoxWidth(2000, 150));
        }

        [Fact]
        public void CopyBoxWidth_NeverDropsBelowItsFloor()
        {
            foreach (int columnWidth in new[] { 0, 100, 300 })
            {
                Assert.True(AboutLayoutMath.CopyBoxWidth(columnWidth, 150) >= AboutLayoutMath.ValueFloor);
            }
        }

        [Fact]
        public void TwoColumnsFitAtTheWindowMinimumAndStackOnANarrowScreenClient()
        {
            Assert.Equal(2, AboutLayoutMath.ColumnCount(FloorPanelWidth));

            int narrowScreenPanel =
                WindowSizing.TabPanelWidthFor(WindowSizing.NarrowScreenFloorWidth)
                    - WindowSizing.ScrollbarAllowance;

            Assert.Equal(1, AboutLayoutMath.ColumnCount(narrowScreenPanel));

            // Stacked, the facts column still clears its own minimum, so
            // nothing clips there either.
            Assert.True(AboutLayoutMath.ColumnWidth(narrowScreenPanel) >= AboutLayoutMath.FactsMinWidth);
        }

        // The longest bulleted line the Credits section ships, measured by
        // summing xAdvance over the shipped Menomonia 16 regular glyph table
        // (Content/fonts/menomonia/menomonia-16-regular.xnb). The
        // wrapper has no hanging indent, so a bullet that wraps loses its
        // alignment; this is the number the measure has to clear.
        private const int LongestCreditBulletPx = 355;

        [Fact]
        public void TheMeasureClearsTheLongestCreditBullet_AtEveryWidthTheTabReaches()
        {
            int narrowScreenPanel =
                WindowSizing.TabPanelWidthFor(WindowSizing.NarrowScreenFloorWidth)
                    - WindowSizing.ScrollbarAllowance;

            Assert.Equal(784, narrowScreenPanel);
            Assert.Equal(1232, FloorPanelWidth);

            // Stacked on the narrowest client, and in two columns at the
            // window minimum, the prose still gets the whole measure.
            Assert.Equal(
                AboutLayoutMath.ProseMeasure,
                AboutLayoutMath.TextBudget(AboutLayoutMath.ColumnWidth(narrowScreenPanel)));
            Assert.Equal(
                AboutLayoutMath.ProseMeasure,
                AboutLayoutMath.TextBudget(AboutLayoutMath.ColumnWidth(FloorPanelWidth)));

            Assert.True(
                LongestCreditBulletPx < AboutLayoutMath.ProseMeasure,
                $"the longest bullet is {LongestCreditBulletPx}px against a "
                    + $"{AboutLayoutMath.ProseMeasure}px measure");
        }

        [Fact]
        public void SubheadingGeometry_ResolvesToItsShippedPixels()
        {
            Assert.Equal(32, AboutLayoutMath.SubheadingBandHeight);
            Assert.Equal(4, AboutLayoutMath.SubheadingTitleY);
            Assert.Equal(20, AboutLayoutMath.SectionGap);
            Assert.Equal(8, AboutLayoutMath.SubsectionGap);
        }

        [Fact]
        public void TheSubheadingBand_SeatsTheDescendersOfItsOwn20ptFace()
        {
            int lowestInk =
                AboutLayoutMath.SubheadingTitleY + TypeRampMetrics.ColumnHeaderInk.LowestInk;

            Assert.True(
                lowestInk <= AboutLayoutMath.SubheadingBandHeight,
                $"20pt ink reaches y={lowestInk} in a {AboutLayoutMath.SubheadingBandHeight}px band");
        }

        [Fact]
        public void TheSubheadingBand_IsShorterThanASectionBand_ButStillTallerThanItsFace()
        {
            Assert.True(
                AboutLayoutMath.SubheadingBandHeight
                    < PlanContentHeightMath.SectionHeaderRowHeight);
            Assert.True(
                TypeRampMetrics.ColumnHeaderInk.LineHeight
                    <= AboutLayoutMath.SubheadingBandHeight);
        }

        // The whole point of the Credits group: three subsections separated
        // by less than the tab puts between sections read as one section.
        // Equal gaps would make them three peers.
        [Fact]
        public void SubsectionsSitCloserTogetherThanSectionsDo()
        {
            Assert.True(AboutLayoutMath.SubsectionGap < AboutLayoutMath.SectionGap);
            Assert.True(AboutLayoutMath.SubsectionGap > 0);
        }
    }
}
