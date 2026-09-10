using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class CurrencyTooltipComposerTests
    {
        // The composer never reads the id; it is the key the
        // facts are resolved by.
        private const int TestCurrencyId = 23;

        private static CurrencyTooltipFacts SpiritShards(int? wallet = 412)
        {
            return CurrencyTooltipFacts.ForCurrencyEntry(
                TestCurrencyId,
                new CurrencyMetadata
                {
                    CurrencyId = TestCurrencyId,
                    Name = "Spirit Shards",
                    IconUrl = "https://render.guildwars2.com/file/spirit_shard.png",
                    Description = "Gained after reaching level 80 and by completing map events.",
                },
                wallet);
        }

        [Fact]
        public void BuildContent_ShowsTheGamesFourParts_InTheGamesOrder()
        {
            var content = CurrencyTooltipComposer.BuildContent(SpiritShards());

            Assert.Equal(
                new[]
                {
                    "Spirit Shards",
                    "412 in Wallet",
                    "Gained after reaching level 80 and by completing map events.",
                    "Currency",
                },
                content.ToPlainLines().ToArray());
        }

        [Fact]
        public void BuildContent_OpensOnTheIconAndNameHeader_NotABareTextLine()
        {
            // The same header row the item tooltip opens with: the icon is
            // carried by the LINE KIND, so a currency hover cannot end up
            // as the name-only box this composer exists to replace.
            var header = CurrencyTooltipComposer.BuildContent(SpiritShards()).Lines[0];

            Assert.Equal(TooltipLineKind.Header, header.Kind);
            Assert.Equal("https://render.guildwars2.com/file/spirit_shard.png", header.IconUrl);
            Assert.Equal("Spirit Shards", header.Spans.Single().Text);
        }

        [Fact]
        public void BuildContent_HeaderCarriesNoRarity_ACurrencyHasNone()
        {
            var header = CurrencyTooltipComposer.BuildContent(SpiritShards()).Lines[0];

            Assert.Null(header.Spans.Single().RarityKey);
        }

        [Fact]
        public void BuildContent_ThousandsSeparatesTheWalletBalance()
        {
            // Wallet holdings run to seven figures where an item count does
            // not - the reason this is not a bare ToString().
            var content = CurrencyTooltipComposer.BuildContent(
                CurrencyTooltipFacts.ForCurrencyEntry(
                TestCurrencyId,
                new CurrencyMetadata
                {
                    CurrencyId = TestCurrencyId,
                    Name = "Karma",
                    IconUrl = "k.png",
                    Description = null,
                },
                1234567));

            Assert.Contains("1,234,567 in Wallet", content.ToPlainText());
        }

        [Fact]
        public void BuildContent_NoWalletSnapshot_DropsTheBalanceLineRatherThanClaimingZero()
        {
            // Null is "nobody read the wallet", which is a different
            // statement from a holding of zero and must not render as one.
            var content = CurrencyTooltipComposer.BuildContent(SpiritShards(wallet: null));

            Assert.DoesNotContain("in Wallet", content.ToPlainText());
            Assert.Equal(
                new[]
                {
                    "Spirit Shards",
                    "Gained after reaching level 80 and by completing map events.",
                    "Currency",
                },
                content.ToPlainLines().ToArray());
        }

        [Fact]
        public void BuildContent_WalletHoldsNone_StillSaysSo()
        {
            var content = CurrencyTooltipComposer.BuildContent(SpiritShards(wallet: 0));

            Assert.Contains("0 in Wallet", content.ToPlainText());
        }

        [Fact]
        public void BuildContent_NoDescriptionYet_DropsTheParagraphRatherThanInventingOne()
        {
            // /v2/currencies has not landed for this session yet. Inventing
            // prose here would violate the no-invented-data invariant.
            var content = CurrencyTooltipComposer.BuildContent(
                CurrencyTooltipFacts.ForCurrencyEntry(
                TestCurrencyId,
                new CurrencyMetadata
                {
                    CurrencyId = TestCurrencyId,
                    Name = "Karma",
                    IconUrl = "k.png",
                    Description = null,
                },
                900));

            Assert.Equal(
                new[] { "Karma", "900 in Wallet", "Currency" },
                content.ToPlainLines().ToArray());
        }

        [Fact]
        public void BuildContent_DescriptionMarkup_KeepsItsRoles_AndIsNeverShownRaw()
        {
            var content = CurrencyTooltipComposer.BuildContent(
                CurrencyTooltipFacts.ForCurrencyEntry(
                TestCurrencyId,
                new CurrencyMetadata
                {
                    CurrencyId = TestCurrencyId,
                    Name = "Karma",
                    IconUrl = "k.png",
                    Description = "Spend it at karma merchants. <c=@flavor>A warm glow.</c>",
                },
                5));

            Assert.DoesNotContain("<c=", content.ToPlainText());

            var descriptionSpans = content.Lines
                .First(l => l.Spans.Any(s => s.Text.Contains("karma merchants")))
                .Spans;
            Assert.Contains(descriptionSpans, s => s.Role == TooltipSpanRole.Default);
            Assert.Contains(descriptionSpans, s => s.Role == TooltipSpanRole.Flavor);
        }

        [Fact]
        public void BuildContent_MultiParagraphDescription_BreaksOnItsOwnHardBreaks()
        {
            var content = CurrencyTooltipComposer.BuildContent(
                CurrencyTooltipFacts.ForCurrencyEntry(
                TestCurrencyId,
                new CurrencyMetadata
                {
                    CurrencyId = TestCurrencyId,
                    Name = "Karma",
                    IconUrl = "k.png",
                    Description = "First line.\nSecond line.",
                },
                null));

            Assert.Equal(
                new[] { "Karma", "First line.", "Second line.", "Currency" },
                content.ToPlainLines().ToArray());
        }

        [Fact]
        public void BuildContent_AnUnknownCurrencyIsStillNamed()
        {
            // Facts are resolved from an id, and the name falls back to the
            // module's own id table, so a /v2/currencies reply that never
            // arrived leaves a named header rather than an empty box.
            var content = CurrencyTooltipComposer.BuildContent(
                CurrencyTooltipFacts.ForCurrencyEntry(TestCurrencyId, null, null));

            Assert.False(content.IsEmpty);
            Assert.NotEmpty(content.ToPlainLines()[0]);
        }

        [Fact]
        public void BuildContent_NoIconYet_StillHeadsWithTheHeaderRow()
        {
            // HeaderLine normalises a null url to empty, so the row draws
            // the neutral empty-slot square and the name stays in the
            // column every other tooltip's name sits in.
            var header = CurrencyTooltipComposer.BuildContent(
                CurrencyTooltipFacts.ForCurrencyEntry(
                TestCurrencyId,
                new CurrencyMetadata
                {
                    CurrencyId = TestCurrencyId,
                    Name = "Karma",
                    IconUrl = null,
                    Description = null,
                },
                null)).Lines[0];

            Assert.Equal(TooltipLineKind.Header, header.Kind);
            Assert.Equal("", header.IconUrl);
        }

        [Fact]
        public void BuildContent_CarriesNoIdAnywhere_IdsAreInternalOnly()
        {
            // The type takes no id at all, which is the construction that
            // enforces it - this asserts the composed prose too.
            string text = CurrencyTooltipComposer.BuildContent(SpiritShards()).ToPlainText();

            Assert.DoesNotContain("23", text);
        }
    }
}
