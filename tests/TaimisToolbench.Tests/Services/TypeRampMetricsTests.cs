using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The ramp's own invariants. These do not re-measure the font (the
    /// XNB parse is recorded in TypeRampMetrics' doc comment); they pin the RELATIONSHIPS that make
    /// the ramp a hierarchy rather than four sizes in a row, so a future
    /// tier swap that breaks one fails here instead of on a screenshot.
    /// </summary>
    public class TypeRampMetricsTests
    {
        [Fact]
        public void PromotedTiers_AreARealStepOverBody_NotTheFlatRampTheyReplaced()
        {
            // The rule the old 14/16/18-regular ramp failed, stated as the
            // RELATION it actually is. Deliberately not "at least 1.25x
            // over 16pt": that gate reads as an invariant while really
            // encoding one of the two candidate tier seats, and it fails
            // by construction on the 18/22 retreat
            // that was kept one commit away.
            Assert.True(
                TypeRampMetrics.SectionTitlePointSize > TypeRampMetrics.ColumnHeaderPointSize,
                "section titles must outrank column headers");
            Assert.True(
                TypeRampMetrics.SectionTitleInk.CapHeight > TypeRampMetrics.ColumnHeaderInk.CapHeight,
                "the title/header step has to survive in ink, not only in nominal size");
            Assert.True(
                TypeRampMetrics.ColumnHeaderInk.CapHeight > TypeRampMetrics.BodyInk.CapHeight,
                "the body/header step has to survive in ink, not only in nominal size");
        }

        [Fact]
        public void EachTierSeat_CarriesTheInkMeasuredForItsOwnPointSize()
        {
            // The half of a tier swap with no other alarm: move
            // ColumnHeaderPointSize to 18 and leave ColumnHeaderInk on
            // Bold20, and every height constant derived from it is derived
            // from a font the view has stopped drawing in. Nothing on
            // screen says so, and nothing else here would.
            Assert.Equal(
                BoldInkFor(TypeRampMetrics.ColumnHeaderPointSize), TypeRampMetrics.ColumnHeaderInk);
            Assert.Equal(
                BoldInkFor(TypeRampMetrics.SectionTitlePointSize), TypeRampMetrics.SectionTitleInk);
            Assert.Equal(
                BoldInkFor(TypeRampMetrics.StatusPointSize), TypeRampMetrics.StatusInk);
        }

        [Fact]
        public void TheOneRegularWeightSeat_SitsOnAFaceThatCanBeDrawnWith()
        {
            // SmallHeading is the ramp's only regular-weight promoted
            // role, so this is the only seat that can land on one of the
            // two measured font defects. At 18 the plan header's
            // " x 42 needed" would render with 4px word gaps; at 22 it
            // would silently render at 24. UiFonts.Regular throws on both,
            // which is a runtime alarm - this is the one that fires first.
            Assert.True(
                TypeRampMetrics.HasUsableRegularFace(TypeRampMetrics.SmallHeadingPointSize),
                $"{TypeRampMetrics.SmallHeadingPointSize}pt has no usable regular face");
        }

        [Fact]
        public void CaptionIsTheFloor_AndBodyIsTheOnlyReadingSizeAboveIt()
        {
            Assert.True(TypeRampMetrics.CaptionInk.CapHeight < TypeRampMetrics.BodyInk.CapHeight);
            Assert.True(TypeRampMetrics.BodyInk.CapHeight < TypeRampMetrics.ColumnHeaderInk.CapHeight);
        }

        [Fact]
        public void StatusSitsBetweenBodyAndTheSectionTitles()
        {
            // A transient line must read above the rows and below the
            // headings that structure the page.
            Assert.True(TypeRampMetrics.StatusInk.CapHeight > TypeRampMetrics.BodyInk.CapHeight);
            Assert.True(TypeRampMetrics.StatusInk.CapHeight < TypeRampMetrics.SectionTitleInk.CapHeight);
        }

        [Theory]
        [InlineData(14)]
        [InlineData(16)]
        [InlineData(18)]
        [InlineData(20)]
        [InlineData(22)]
        [InlineData(24)]
        [InlineData(32)]
        public void EveryMeasuredEntry_IsInternallyConsistent(int pointSize)
        {
            var ink = InkFor(pointSize);

            // Cap ink sits inside the line box, above the baseline; the
            // lowest ink is a descender, at or below the baseline. A typo
            // in the measured table almost always breaks one of these.
            Assert.True(ink.CapTopY >= 0);
            Assert.True(ink.CapTopY + ink.CapHeight <= ink.BaselineY);
            Assert.True(ink.LowestInk >= ink.BaselineY);
            Assert.True(ink.LineHeight > ink.CapHeight);
        }

        [Fact]
        public void TheCoinDigits_MatchTheGamesWidthAndNeverGoUnderTheFloor()
        {
            var digits = TypeRampMetrics.CoinDigitInk;

            // MEASURED beside the game's own coin row in one screenshot, so
            // no interface scale is assumed: at this face the digits ink
            // "841" 19 pixels wide against the game's 19, and a step down
            // inks 17. So the digits sit at Body, and a step down is a
            // regression rather than a refinement.
            Assert.Equal(TypeRampMetrics.BodyInk.CapHeight, digits.CapHeight);
            Assert.True(digits.LineHeight >= TypeRampMetrics.CaptionInk.LineHeight);
        }

        [Fact]
        public void InkBottom_IsWhereADividerHasToClear()
        {
            var header = TypeRampMetrics.ColumnHeaderInk;

            Assert.Equal(header.LowestInk + 4, TypeRampMetrics.InkBottom(header, 4));
        }

        [Fact]
        public void BaselineAlignedY_PutsTwoTiersOnOneReadingLine()
        {
            // The section header's caret (Body) against its title.
            var title = TypeRampMetrics.SectionTitleInk;
            var caret = TypeRampMetrics.BodyInk;

            int titleY = 3;
            int baseline = titleY + title.BaselineY;
            int caretY = TypeRampMetrics.BaselineAlignedY(caret, baseline);

            Assert.Equal(baseline, caretY + caret.BaselineY);
        }

        private static TypeRampMetrics.FontInk InkFor(int pointSize)
        {
            switch (pointSize)
            {
                case 14: return TypeRampMetrics.Regular14;
                case 16: return TypeRampMetrics.Regular16;
                case 20: return TypeRampMetrics.Regular20;
                case 32: return TypeRampMetrics.Regular32;
                default: return BoldInkFor(pointSize);
            }
        }

        /// <summary>
        /// The measured bold ink at a size, by size - every promoted tier
        /// seat is bold, so this is what a seat's ink is checked against.
        /// </summary>
        private static TypeRampMetrics.FontInk BoldInkFor(int pointSize)
        {
            switch (pointSize)
            {
                case 18: return TypeRampMetrics.Bold18;
                case 20: return TypeRampMetrics.Bold20;
                case 22: return TypeRampMetrics.Bold22;
                default: return TypeRampMetrics.Bold24;
            }
        }
    }
}
