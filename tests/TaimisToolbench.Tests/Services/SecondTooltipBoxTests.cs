using System.Linq;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The second box every icon hover shows: this surface's own tips, then
    /// the right-click affordance last. The owner stated the ordering rule
    /// exactly, so it is pinned exactly - the line is always last, it opens
    /// on a fresh line with one blank line above it, and there is no blank
    /// line when it is the only thing in the box.
    /// </summary>
    public class SecondTooltipBoxTests
    {
        private static TooltipContent Tips(params string[] lines)
        {
            return SecondTooltipBox.Compose(lines, null);
        }

        [Fact]
        public void TheWikiLineIsLast_AfterOneBlankLine()
        {
            var box = SecondTooltipBox.Compose(
                Tips("Unit price: 1s 0c", "A caveat."), IconWikiTarget.HintText);

            Assert.Equal(
                new[] { "Unit price: 1s 0c", "A caveat.", "", IconWikiTarget.HintText },
                box.ToPlainLines());
        }

        [Fact]
        public void TheWikiLineAloneOpensTheBox_WithNoBlankLineAboveIt()
        {
            var box = SecondTooltipBox.Compose((TooltipContent)null, IconWikiTarget.HintText);

            Assert.Equal(new[] { IconWikiTarget.HintText }, box.ToPlainLines());
        }

        [Fact]
        public void WithNoPageTheBoxIsJustTheTips()
        {
            var box = SecondTooltipBox.Compose(Tips("A caveat."), null);

            Assert.Equal(new[] { "A caveat." }, box.ToPlainLines());
        }

        [Fact]
        public void WithNeitherTipsNorAPageTheBoxIsEmpty()
        {
            Assert.True(SecondTooltipBox.Compose((TooltipContent)null, null).IsEmpty);
            Assert.True(SecondTooltipBox.Compose(new string[0], null).IsEmpty);
        }

        [Fact]
        public void ABlankOrNullTipIsDroppedRatherThanDrawnAsABlankRow()
        {
            var box = SecondTooltipBox.Compose(
                new[] { null, "A caveat.", "" }, IconWikiTarget.HintText);

            Assert.Equal(
                new[] { "A caveat.", "", IconWikiTarget.HintText }, box.ToPlainLines());
        }

        /// <summary>
        /// A tip carrying a coin amount keeps its coin span, which is the
        /// whole reason the box is content rather than a joined string.
        /// </summary>
        [Fact]
        public void ACoinTipKeepsItsCoinSpan()
        {
            var tips = TooltipContent.FromLines(new[]
            {
                TooltipContent.Line(
                    TooltipSpan.FromText("Unit price: "),
                    TooltipSpan.FromCoin(1234, "12s 34c")),
            });

            var box = SecondTooltipBox.Compose(tips, IconWikiTarget.HintText);

            Assert.Equal(new long[] { 1234 }, box.CoinValues());
            Assert.Equal(IconWikiTarget.HintText, box.ToPlainLines().Last());
        }

        [Fact]
        public void Attach_HangsTheSecondBoxUnderTheFirst()
        {
            var first = TooltipContent.FromText("Mithril Ore");
            var stacked = SecondTooltipBox.Attach(
                first, SecondTooltipBox.Compose((TooltipContent)null, IconWikiTarget.HintText));

            Assert.True(stacked.HasExtra);
            Assert.Equal(new[] { "Mithril Ore" }, stacked.ToPlainLines());
            Assert.Equal(new[] { IconWikiTarget.HintText }, stacked.ToExtraLines());
        }

        /// <summary>
        /// A second box under an empty first one would render as a blank
        /// frame above the only lines there are.
        /// </summary>
        [Fact]
        public void Attach_PromotesTheSecondBoxWhenThereIsNoFirstOne()
        {
            var only = SecondTooltipBox.Attach(
                TooltipContent.Empty,
                SecondTooltipBox.Compose((TooltipContent)null, IconWikiTarget.HintText));

            Assert.False(only.HasExtra);
            Assert.Equal(new[] { IconWikiTarget.HintText }, only.ToPlainLines());
        }

        [Fact]
        public void Attach_WithNothingToAddLeavesTheFirstBoxAlone()
        {
            var first = TooltipContent.FromText("Mithril Ore");

            Assert.False(SecondTooltipBox.Attach(first, null).HasExtra);
            Assert.False(SecondTooltipBox.Attach(first, TooltipContent.Empty).HasExtra);
            Assert.True(SecondTooltipBox.Attach(null, null).IsEmpty);
        }
    }
}
