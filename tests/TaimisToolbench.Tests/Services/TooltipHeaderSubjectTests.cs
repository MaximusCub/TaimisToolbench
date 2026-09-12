using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// What a tooltip header row says about its own subject, composer by
    /// composer. The rich surface reads exactly this to decide whether the
    /// header icon gets a filled frame or a ring, and currency art is
    /// mostly transparent: a currency that reaches the surface stating
    /// anything else draws the grey plate behind its icon that the field
    /// reported on the Snapshot tab.
    /// </summary>
    public class TooltipHeaderSubjectTests
    {
        // The composer never reads the id; it is the key the
        // facts are resolved by.
        private const int TestCurrencyId = 23;

        private const string ItemIcon = "https://render.guildwars2.com/file/AAA/1.png";
        private const string CurrencyIcon = "https://render.guildwars2.com/file/BBB/2.png";

        private static TooltipHeaderSubject SubjectOfFirstLine(TooltipContent content)
        {
            return content.Lines[0].HeaderSubject;
        }

        private static TooltipContent CurrencyContent(string description = "Earned in the mists.")
        {
            return CurrencyTooltipComposer.BuildContent(
                CurrencyTooltipFacts.ForCurrencyEntry(
                TestCurrencyId,
                new CurrencyMetadata
                {
                    CurrencyId = TestCurrencyId,
                    Name = "Spirit Shards",
                    IconUrl = CurrencyIcon,
                    Description = description,
                },
                412));
        }

        [Fact]
        public void ACurrencyHeaderSaysSo()
        {
            Assert.True(SubjectOfFirstLine(CurrencyContent()).IsCurrency);
        }

        [Fact]
        public void ACurrencyNameTakesItsOwnColourRoleRatherThanARarityOne()
        {
            // The game colours a currency's name in a warm tan of its own.
            // An item's name is its rarity colour - white for Basic - so an
            // item whose rarity nobody resolved still asks for the rarity
            // role and its neutral fallback, not for the currency colour.
            Assert.Equal(
                TooltipSpanRole.CurrencyName,
                CurrencyContent().Lines[0].Spans.Single().Role);

            var item = ItemRowTooltipComposer.BuildRowContent(
                null,
                ItemTooltipIdentity.ForItem("Unlooked Thing", ItemIcon, null),
                null);

            Assert.Equal(TooltipSpanRole.Rarity, item.Lines[0].Spans.Single().Role);
        }

        [Fact]
        public void ACurrencyAndARaritylessItemAreStillToldApart()
        {
            // Before the subject existed both reached the icon layer as a
            // null rarity string - which is how the currency ended up in an
            // item's frame.
            var currency = SubjectOfFirstLine(CurrencyContent());
            var item = SubjectOfFirstLine(ItemRowTooltipComposer.BuildRowContent(
                null,
                ItemTooltipIdentity.ForItem("Unlooked Thing", ItemIcon, null),
                null));

            Assert.Null(currency.RarityKey);
            Assert.Null(item.RarityKey);
            Assert.True(currency.IsCurrency);
            Assert.False(item.IsCurrency);
        }

        [Fact]
        public void AnItemRowHeaderCarriesTheRarityTheRowResolved()
        {
            var subject = SubjectOfFirstLine(ItemRowTooltipComposer.BuildRowContent(
                null,
                ItemTooltipIdentity.ForItem("Mithril Ore", ItemIcon, "Basic"),
                null));

            Assert.False(subject.IsCurrency);
            Assert.Equal("Basic", subject.RarityKey);
        }

        [Fact]
        public void AStatBlockHeaderCarriesTheStatBlocksRarity()
        {
            var subject = SubjectOfFirstLine(ItemStatTooltipComposer.BuildContent(
                new ItemStatBlock
                {
                    ItemId = 30684,
                    Name = "Bolt",
                    Rarity = "Legendary",
                    IconUrl = ItemIcon,
                    ItemType = "Weapon",
                }));

            Assert.False(subject.IsCurrency);
            Assert.Equal("Legendary", subject.RarityKey);
        }

        [Fact]
        public void EveryHiddenPlanItemHeaderIsAnItem()
        {
            var items = new List<PlanHeaderItem>
            {
                new PlanHeaderItem { ItemId = 1, Name = "Shown", IconUrl = ItemIcon, Rarity = "Fine" },
                new PlanHeaderItem { ItemId = 2, Name = "Hidden", IconUrl = ItemIcon, Rarity = "Rare" },
                new PlanHeaderItem { ItemId = 3, Name = "Nameless" },
            };

            var content = MultiItemHeaderTooltipComposer.BuildHiddenItemsContent(items, 1);

            Assert.Equal(2, content.Lines.Count);
            Assert.All(content.Lines, line => Assert.False(line.HeaderSubject.IsCurrency));
            Assert.Equal(
                new[] { "Rare", null },
                content.Lines.Select(line => line.HeaderSubject.RarityKey).ToArray());
        }

        [Fact]
        public void TheStatementSurvivesLayoutOntoTheRowTheSurfaceDraws()
        {
            // The surface never sees the content, only the laid-out rows -
            // so the statement has to survive the wrap to be worth making.
            var layout = TooltipLayoutMath.LayoutContent(
                CurrencyContent("A long enough description to wrap over more than one row."),
                140, 20, text => text.Length * 10, copper => 60,
                headerRowHeight: 34, headerIndent: 39);

            var iconRows = layout.Rows.Where(row => row.IconUrl != null).ToArray();

            Assert.Equal(CurrencyIcon, Assert.Single(iconRows).IconUrl);
            Assert.True(iconRows[0].HeaderSubject.IsCurrency);
        }
    }
}
