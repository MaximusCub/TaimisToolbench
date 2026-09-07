using System;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // The structured tooltip model - TooltipContentBuilder and
    // TooltipContent.FromText are the subjects here. Assertions read
    // through the test-side plain projection (Helpers/
    // TooltipContentPlainText) because comparing wording is far more
    // legible than walking spans; what is being pinned is what the builder
    // PUT in the model, not the projection itself.
    public class TooltipContentTests
    {
        [Fact]
        public void CoinSpan_KeepsTheCallersOwnPlainText()
        {
            var content = new TooltipContentBuilder()
                .Text("Crafting gold price: ")
                .Coin(12345, "1g 23s 45c")
                .Build();

            Assert.Equal("Crafting gold price: 1g 23s 45c", content.ToPlainText());
        }

        [Fact]
        public void DifferentCoinFormatsCoexist()
        {
            // The two composers deliberately format coin differently
            // (always three units vs leading units omitted). The span
            // carries the text, so the model never imposes one on both.
            var content = new TooltipContentBuilder()
                .Text("A ").Coin(500, "0g 5s 0c").EndLine()
                .Text("B ").Coin(500, "5s 0c")
                .Build();

            Assert.Equal("A 0g 5s 0c\nB 5s 0c", content.ToPlainText());
        }

        [Fact]
        public void Text_EmbeddedHardBreaks_BecomeLines()
        {
            var content = TooltipContent.FromText("one\ntwo\nthree");

            Assert.Equal(3, content.Lines.Count);
            Assert.Equal(new[] { "one", "two", "three" }, content.ToPlainLines());
        }

        [Fact]
        public void Text_BlankSeparatorLine_KeptAsAnEmptyLine()
        {
            var content = TooltipContent.FromText("head\n\ntail");

            Assert.Equal(3, content.Lines.Count);
            Assert.Empty(content.Lines[1].Spans);
            Assert.Equal("head\n\ntail", content.ToPlainText());
        }

        [Fact]
        public void Text_CarriageReturnsNormalized()
        {
            Assert.Equal("a\nb\nc", TooltipContent.FromText("a\r\nb\rc").ToPlainText());
        }

        [Fact]
        public void Separator_OnEmptyBuilder_AddsNothing()
        {
            var builder = new TooltipContentBuilder();
            builder.Separator();

            Assert.True(builder.Build().IsEmpty);
        }

        [Fact]
        public void Separator_BetweenBlocks_IsOneBlankLine()
        {
            // The structural replacement for the pill tooltip's old
            // "\n\n" concatenation - same rendered result, no string math.
            var content = new TooltipContentBuilder()
                .Text("Switch to CRAFT")
                .Separator()
                .Text("More expensive")
                .Build();

            Assert.Equal("Switch to CRAFT\n\nMore expensive", content.ToPlainText());
        }

        [Fact]
        public void Append_JoinsTwoComposersResultsWithoutFlattening()
        {
            var first = new TooltipContentBuilder().Text("head").Build();
            var second = new TooltipContentBuilder()
                .Text("cost ").Coin(100, "0g 1s 0c").Build();

            var joined = new TooltipContentBuilder().Append(first).Separator().Append(second).Build();

            Assert.Equal("head\n\ncost 0g 1s 0c", joined.ToPlainText());
            // The coin span survives the join - the whole point of
            // composing content instead of strings.
            Assert.Contains(joined.Lines.SelectMany(l => l.Spans), s => s.IsCoin && s.CoinCopper == 100);
        }

        [Fact]
        public void Append_NullOrEmpty_IsANoOp()
        {
            var builder = new TooltipContentBuilder().Text("only");
            builder.Append(null).Append(TooltipContent.Empty);

            Assert.Equal("only", builder.Build().ToPlainText());
        }

        [Fact]
        public void Text_NullOrEmpty_ContributesNothing()
        {
            Assert.True(TooltipContent.FromText(null).IsEmpty);
            Assert.True(TooltipContent.FromText("").IsEmpty);
        }

        [Fact]
        public void OrText_KeepsRealContentAndFallsBackToTheNoteWhenThereIsNone()
        {
            var real = new TooltipContentBuilder().Text("Mithril Ore").Build();

            Assert.Same(real, TooltipContent.OrText(real, "No icon available for this entry."));
            Assert.Equal(
                "No icon available for this entry.",
                TooltipContent.OrText(TooltipContent.Empty, "No icon available for this entry.").ToPlainText());
            Assert.Equal(
                "No icon available for this entry.",
                TooltipContent.OrText(null, "No icon available for this entry.").ToPlainText());
            Assert.True(TooltipContent.OrText(TooltipContent.Empty, null).IsEmpty);
        }

        [Fact]
        public void OrText_CoversTheRestoredPlanRowThatComposesNothingYet()
        {
            // The case the icon tree actually hits: a row with no item
            // identity at all - a synthesized cost-component leaf - and no
            // stat block, so the deferred builder is empty and the icon's
            // own note has to survive.
            var composed = ItemRowTooltipComposer.BuildRowContent(
                (ItemStatBlock)null, ItemTooltipIdentity.Unnamed(), extraLines: null);

            Assert.True(composed.IsEmpty);
            Assert.Equal(
                "No icon available for this entry.",
                TooltipContent.OrText(composed, "No icon available for this entry.").ToPlainText());
        }

        [Fact]
        public void WithExtra_LeavesTheFirstBoxAloneAndHangsTheSecondOffIt()
        {
            var first = new TooltipContentBuilder().Text("Mithril Ore").Build();
            var second = new TooltipContentBuilder().Text("Right-click to open the wiki page.").Build();

            var stacked = first.WithExtra(second);

            Assert.True(stacked.HasExtra);
            Assert.Equal(new[] { "Mithril Ore" }, stacked.ToPlainLines());
            Assert.Equal(new[] { "Right-click to open the wiki page." }, stacked.ToExtraLines());

            // The original is untouched, so content handed to two surfaces
            // cannot grow a second box on one of them.
            Assert.False(first.HasExtra);
        }

        [Fact]
        public void WithExtra_RefusesToPutASecondBoxUnderAnEmptyOne()
        {
            var second = new TooltipContentBuilder().Text("A hint.").Build();

            Assert.False(TooltipContent.Empty.WithExtra(second).HasExtra);
            Assert.False(new TooltipContentBuilder().Text("x").Build().WithExtra(null).HasExtra);
            Assert.False(
                new TooltipContentBuilder().Text("x").Build().WithExtra(TooltipContent.Empty).HasExtra);
        }

        [Fact]
        public void Append_RefusesContentThatCarriesASecondBox()
        {
            // A builder has one box. Flattening the second one into it
            // would put the module's own lines inside the game's block,
            // which is the distinction the second box exists to draw.
            var stacked = new TooltipContentBuilder().Text("first").Build()
                .WithExtra(new TooltipContentBuilder().Text("second").Build());
            Assert.True(stacked.HasExtra);

            var builder = new TooltipContentBuilder().Text("head");

            Assert.Throws<ArgumentException>(() => builder.Append(stacked));

            // The rejected call left the builder as it was.
            Assert.Equal(new[] { "head" }, builder.Build().ToPlainLines());
        }

        [Fact]
        public void Append_TakesContentThatIsAllOneBox()
        {
            var builder = new TooltipContentBuilder().Text("head");
            builder.Append(new TooltipContentBuilder().Text("body").Build());

            Assert.Equal(new[] { "head", "body" }, builder.Build().ToPlainLines());
        }
    }
}
