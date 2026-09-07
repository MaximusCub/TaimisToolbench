using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The rune set counter and the lit bonus tiers, from a snapshot's own
    /// stacks through to the composed tooltip. Every stat block comes from
    /// verbatim live /v2/items JSON through the real parser and the real
    /// factory, and the rows go through the real socket index, the real
    /// equipped-set index and the real composer.
    /// </summary>
    public class EquippedRuneSetTests
    {
        private const int Warfists = 48074;
        private const int Boots = 48075;
        private const int Coat = 48076;
        private const int Leggings = 48077;
        private const int Helm = 48078;
        private const int Shoulders = 48079;
        private const int Scholar = 24836;
        private const int Water = 24838;

        private const string Apoyu = AccountItemIndex.CharacterEquipmentSourcePrefix + "Apoyu";
        private const string Divineaxe = AccountItemIndex.CharacterEquipmentSourcePrefix + "Divineaxe";
        private const string ApoyuBags = AccountItemIndex.CharacterSourcePrefix + "Apoyu";

        private static SnapshotItemEntry Stack(int itemId, string source, params int[] upgrades)
        {
            return new SnapshotItemEntry
            {
                ItemId = itemId,
                Count = 1,
                Source = source,
                Upgrades = upgrades.Length == 0 ? null : upgrades.ToList(),
            };
        }

        /// <summary>
        /// One character wearing <paramref name="pieces"/> armour pieces,
        /// each socketed with <paramref name="runeId"/>. The first piece is
        /// the one the tooltip is built for.
        /// </summary>
        private static List<SnapshotItemEntry> WornSet(int runeId, int pieces, string source)
        {
            var ids = new[] { Warfists, Boots, Coat, Leggings, Helm, Shoulders };
            var items = new List<SnapshotItemEntry>();
            for (int i = 0; i < pieces; i++)
            {
                items.Add(Stack(ids[i], source, runeId));
            }

            return items;
        }

        /// <summary>
        /// The tooltip for <paramref name="hostId"/> as the Snapshot tab
        /// builds it: the socket index decides what the row may report, the
        /// equipped-set index decides how many pieces the wearer has on,
        /// and the composer draws both.
        /// </summary>
        private static async Task<TooltipContent> HoverAsync(
            IReadOnlyList<SnapshotItemEntry> items, int hostId, string runeJson)
        {
            var raws = await RealItemFixtures.ParseAsync(RealItemJson.ZojjasWarfists, runeJson);
            var blocks = raws.Values.ToDictionary(r => r.Id, ItemStatBlockFactory.Build);

            // The armour fixture is the only host stat block on hand, so it
            // stands in for every piece of the set. Only the hovered row's
            // block reaches the tooltip.
            var host = blocks[Warfists];
            var sockets = SocketedUpgradeIndex.Build(items);
            var runeSets = new EquippedRuneSetIndex(items);

            var view = sockets.TryGetValue(hostId, out var ids)
                ? SocketedUpgradeView.Resolve(
                    ids,
                    id => blocks.TryGetValue(id, out var b) ? b : null,
                    runeId => runeSets.WornCopies(hostId, runeId))
                : SocketedUpgradeView.None;

            return ItemStatTooltipComposer.BuildContent(host, view);
        }

        private static TooltipSpanRole RoleOf(TooltipContent content, string text)
        {
            return content.Lines
                .First(l => string.Concat(l.Spans.Select(s => s.Text)) == text)
                .Spans[0].Role;
        }

        [Fact]
        public async Task AFullSetOnOneCharacterLightsEveryTierAndPrintsTheCounter()
        {
            var tooltip = await HoverAsync(
                WornSet(Scholar, 6, Apoyu), Warfists, RealItemJson.RuneOfTheScholar);
            var lines = tooltip.ToPlainLines();

            Assert.Contains("Superior Rune of the Scholar (6/6)", lines);
            Assert.Equal(TooltipSpanRole.Bonus, RoleOf(tooltip, "(1): +25 Power"));
            Assert.Equal(TooltipSpanRole.Bonus, RoleOf(tooltip, "(6): +125 Ferocity"));
        }

        [Fact]
        public async Task APartialSetLightsOnlyTheTiersTheWearerHasReached()
        {
            var tooltip = await HoverAsync(
                WornSet(Scholar, 4, Apoyu), Warfists, RealItemJson.RuneOfTheScholar);

            Assert.Contains("Superior Rune of the Scholar (4/6)", tooltip.ToPlainLines());
            Assert.Equal(TooltipSpanRole.Bonus, RoleOf(tooltip, "(4): +65 Ferocity"));
            Assert.Equal(TooltipSpanRole.BonusInactive, RoleOf(tooltip, "(5): +100 Power"));
            Assert.Equal(TooltipSpanRole.BonusInactive, RoleOf(tooltip, "(6): +125 Ferocity"));
        }

        [Fact]
        public async Task ASinglePieceLightsTheFirstTierAlone()
        {
            var tooltip = await HoverAsync(
                WornSet(Scholar, 1, Apoyu), Warfists, RealItemJson.RuneOfTheScholar);

            Assert.Contains("Superior Rune of the Scholar (1/6)", tooltip.ToPlainLines());
            Assert.Equal(TooltipSpanRole.Bonus, RoleOf(tooltip, "(1): +25 Power"));
            Assert.Equal(TooltipSpanRole.BonusInactive, RoleOf(tooltip, "(2): +35 Ferocity"));
        }

        [Fact]
        public async Task MorePiecesThanTiersAreCountedButLightNothingExtra()
        {
            // A Major rune's ladder is four entries long (24838), and the
            // wiki's Rune article says the tooltip shows the total number of
            // identical runes rather than capping it at the ladder.
            var tooltip = await HoverAsync(
                WornSet(Water, 6, Apoyu), Warfists, RealItemJson.RuneOfTheWater);
            var lines = tooltip.ToPlainLines();

            Assert.Contains("Major Rune of the Water (6/4)", lines);
            Assert.Equal(TooltipSpanRole.Bonus, RoleOf(tooltip, "(3): +30 Healing"));
            Assert.DoesNotContain(lines, l => l.StartsWith("(5):"));
        }

        [Fact]
        public async Task AnItemAlsoHeldInTheBankSaysNothingAboutASet()
        {
            var items = WornSet(Scholar, 6, Apoyu);
            items.Add(Stack(Warfists, AccountItemIndex.SourceBank, Scholar));

            var tooltip = await HoverAsync(items, Warfists, RealItemJson.RuneOfTheScholar);

            Assert.Contains("Superior Rune of the Scholar", tooltip.ToPlainLines());
            Assert.DoesNotContain(tooltip.ToPlainLines(), l => l.Contains("(6/6)"));
            Assert.Equal(TooltipSpanRole.BonusInactive, RoleOf(tooltip, "(1): +25 Power"));
        }

        [Fact]
        public async Task AnItemWornByTwoCharactersSaysNothingAboutASet()
        {
            var items = WornSet(Scholar, 6, Apoyu);
            items.Add(Stack(Warfists, Divineaxe, Scholar));

            var tooltip = await HoverAsync(items, Warfists, RealItemJson.RuneOfTheScholar);

            Assert.DoesNotContain(tooltip.ToPlainLines(), l => l.Contains("/6)"));
            Assert.Equal(TooltipSpanRole.BonusInactive, RoleOf(tooltip, "(1): +25 Power"));
        }

        [Fact]
        public async Task AnItemInACharactersBagsSaysNothingAboutASet()
        {
            // Bags are the same character, and still not worn gear: nothing
            // in a bag contributes to a set bonus.
            var items = WornSet(Scholar, 6, Apoyu);
            items[0] = Stack(Warfists, ApoyuBags, Scholar);

            var tooltip = await HoverAsync(items, Warfists, RealItemJson.RuneOfTheScholar);

            Assert.DoesNotContain(tooltip.ToPlainLines(), l => l.Contains("/6)"));
            Assert.Equal(TooltipSpanRole.BonusInactive, RoleOf(tooltip, "(1): +25 Power"));
        }

        [Fact]
        public void OnlyTheWearersOwnPiecesAreCounted()
        {
            var items = WornSet(Scholar, 6, Apoyu);
            var other = new[] { 60001, 60002, 60003 };
            foreach (int id in other)
            {
                items.Add(Stack(id, Divineaxe, Scholar));
            }

            var index = new EquippedRuneSetIndex(items);

            Assert.Equal(6, index.WornCopies(Warfists, Scholar));
            Assert.Equal(3, index.WornCopies(other[0], Scholar));
        }

        [Fact]
        public void ARuneTheWearerHasNoneOfCountsZero()
        {
            var index = new EquippedRuneSetIndex(WornSet(Scholar, 6, Apoyu));

            Assert.Equal(0, index.WornCopies(Warfists, Water));
        }

        [Fact]
        public void AnEmptySnapshotCountsNothing()
        {
            Assert.Equal(0, new EquippedRuneSetIndex(null).WornCopies(Warfists, Scholar));
            Assert.Equal(0, EquippedRuneSetIndex.Empty.WornCopies(Warfists, Scholar));
        }
    }
}
