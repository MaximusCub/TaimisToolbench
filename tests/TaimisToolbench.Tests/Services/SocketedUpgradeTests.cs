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
    /// The socketed-upgrade path end to end: which stacks may report their
    /// sockets at all, and what the tooltip reads once they do. Every stat
    /// block here comes from verbatim live /v2/items JSON through the real
    /// parser and the real factory, so the assertions are about the game's
    /// own data rather than about a hand-built stand-in.
    /// </summary>
    public class SocketedUpgradeTests
    {
        /// <summary>A composed tooltip plus the projections the assertions
        /// need: its wording, and the structure behind a named line.</summary>
        private sealed class Rendered
        {
            public Rendered(TooltipContent content)
            {
                Content = content;
                Lines = content.ToPlainLines().ToArray();
            }

            public TooltipContent Content { get; }

            public string[] Lines { get; }

            public TooltipLine LineWith(string text)
            {
                return Content.Lines.First(l => string.Concat(l.Spans.Select(s => s.Text)) == text);
            }

            public TooltipSpanRole RoleOf(string text)
            {
                return LineWith(text).Spans[0].Role;
            }
        }

        private static SnapshotItemEntry Stack(
            int itemId, string source, List<int> infusions = null, List<int> upgrades = null)
        {
            return new SnapshotItemEntry
            {
                ItemId = itemId,
                Count = 1,
                Source = source,
                Infusions = infusions,
                Upgrades = upgrades,
            };
        }

        /// <summary>
        /// The host item's tooltip with those components socketed into it.
        /// A component whose details.type is Rune or Sigil goes in the
        /// upgrade list and everything else in the infusion list, which is
        /// the same split /v2/account/inventory reports.
        /// </summary>
        private static async Task<Rendered> Composed(int hostId, params string[] itemJson)
        {
            var raws = await RealItemFixtures.ParseAsync(itemJson);
            var blocks = raws.Values.ToDictionary(r => r.Id, ItemStatBlockFactory.Build);

            var infusions = new List<int>();
            var upgrades = new List<int>();
            foreach (var block in blocks.Values.Where(b => b.ItemId != hostId))
            {
                (block.SubType == "Rune" || block.SubType == "Sigil" ? upgrades : infusions)
                    .Add(block.ItemId);
            }

            var view = SocketedUpgradeView.Resolve(
                new SocketedUpgradeIds(infusions, upgrades),
                id => blocks.TryGetValue(id, out var b) ? b : null);
            return new Rendered(ItemStatTooltipComposer.BuildContent(blocks[hostId], view));
        }

        [Fact]
        public void Build_ReportsTheSocketsOfASingleStack()
        {
            var index = SocketedUpgradeIndex.Build(new List<SnapshotItemEntry>
            {
                Stack(48074, "Bank", new List<int> { 49424 }, new List<int> { 24836 }),
            });

            Assert.Equal(new[] { 49424 }, index[48074].Infusions);
            Assert.Equal(new[] { 24836 }, index[48074].Upgrades);
        }

        [Fact]
        public void Build_OmitsAnItemWhoseStacksCarryDifferentUpgrades()
        {
            var index = SocketedUpgradeIndex.Build(new List<SnapshotItemEntry>
            {
                Stack(48074, "Bank", upgrades: new List<int> { 24836 }),
                Stack(48074, "Character:Zaeed", upgrades: new List<int> { 24838 }),
            });

            Assert.False(index.ContainsKey(48074));
        }

        [Fact]
        public void Build_OmitsAnItemWhereOneStackIsSocketedAndAnotherIsBare()
        {
            // The row sums both stacks, so naming the rune would describe
            // an object the row also covers and does not match.
            var index = SocketedUpgradeIndex.Build(new List<SnapshotItemEntry>
            {
                Stack(48074, "Bank", upgrades: new List<int> { 24836 }),
                Stack(48074, "Bank"),
            });

            Assert.False(index.ContainsKey(48074));
        }

        [Fact]
        public void Build_KeepsAnItemWhoseStacksAgree()
        {
            var index = SocketedUpgradeIndex.Build(new List<SnapshotItemEntry>
            {
                Stack(48074, "Bank", upgrades: new List<int> { 24836 }),
                Stack(48074, "SharedInventory", upgrades: new List<int> { 24836 }),
            });

            Assert.Equal(new[] { 24836 }, index[48074].Upgrades);
        }

        [Fact]
        public void Build_SkipsStacksWithNothingSocketed()
        {
            // Material storage rows carry neither field, and nor does the
            // large majority of bag rows.
            var index = SocketedUpgradeIndex.Build(new List<SnapshotItemEntry>
            {
                Stack(19700, "MaterialStorage"),
                Stack(48074, "Bank", upgrades: new List<int> { 24836 }),
            });

            Assert.Equal(new[] { 48074 }, index.Keys.ToArray());
        }

        [Fact]
        public void Build_ToleratesANullItemList()
        {
            Assert.Empty(SocketedUpgradeIndex.Build(null));
        }

        [Fact]
        public void ItemIdsToResolve_NamesTheHostsAndTheComponentsOnce()
        {
            var index = SocketedUpgradeIndex.Build(new List<SnapshotItemEntry>
            {
                Stack(48074, "Bank", new List<int> { 49424 }, new List<int> { 24836 }),
                Stack(30699, "Bank", new List<int> { 49424 }),
            });

            var ids = SocketedUpgradeIndex.ItemIdsToResolve(index);

            Assert.Equal(4, ids.Count);
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Contains(48074, ids);
            Assert.Contains(30699, ids);
            Assert.Contains(49424, ids);
            Assert.Contains(24836, ids);
        }

        [Fact]
        public async Task AscendedArmor_ReadsLikeTheGameWithItsInfusionAndRune()
        {
            var t = await Composed(
                48074,
                RealItemJson.ZojjasWarfists,
                RealItemJson.AgonyInfusion,
                RealItemJson.RuneOfTheScholar);

            Assert.Equal(new[]
            {
                "Zojja's Warfists",
                "Defense: 191",
                "+47 Power",
                "+34 Precision",
                "+34 Ferocity",
                "",
                "+1 Agony Infusion",
                "+1 Agony Resistance",
                "",
                "Superior Rune of the Scholar",
                "(1): +25 Power",
                "(2): +35 Ferocity",
                "(3): +50 Power",
                "(4): +65 Ferocity",
                "(5): +100 Power",
                "(6): +125 Ferocity",
                "",
                "Ascended",
                "Heavy",
                "Gloves Armor",
                "Required Level: 80",
                "Crafted in the style of the renowned asuran genius, Zojja.",
                "Account Bound",
                "2s 40c",
            }, t.Lines);
        }

        [Fact]
        public async Task SocketedComponentNamesAreTheBonusBlueRatherThanTheirRarity()
        {
            // The infusion is Ascended and the rune Exotic; neither name
            // takes a rarity colour.
            var t = await Composed(
                48074,
                RealItemJson.ZojjasWarfists,
                RealItemJson.AgonyInfusion,
                RealItemJson.RuneOfTheScholar);

            Assert.Equal(TooltipSpanRole.Bonus, t.RoleOf("+1 Agony Infusion"));
            Assert.Equal(TooltipSpanRole.Bonus, t.RoleOf("Superior Rune of the Scholar"));
            Assert.Equal(TooltipSpanRole.Bonus, t.RoleOf("+1 Agony Resistance"));
        }

        [Fact]
        public async Task ASocketedComponentDrawsItsOwnIconBesideItsName()
        {
            var t = await Composed(48074, RealItemJson.ZojjasWarfists, RealItemJson.RuneOfTheScholar);
            var name = t.LineWith("Superior Rune of the Scholar");

            Assert.Equal(TooltipLineKind.Effect, name.Kind);
            Assert.Contains("220736.png", name.IconUrl);

            // Only the name line carries the icon; the ladder under it runs
            // flush left, as the game draws it.
            var tier = t.LineWith("(1): +25 Power");
            Assert.Equal(TooltipLineKind.Text, tier.Kind);
            Assert.Null(tier.IconUrl);
        }

        [Fact]
        public async Task ASocketedRunesTiersAreAllInactive()
        {
            var t = await Composed(48074, RealItemJson.ZojjasWarfists, RealItemJson.RuneOfTheScholar);

            foreach (var line in t.Lines.Where(l => l.StartsWith("(")))
            {
                Assert.Equal(TooltipSpanRole.BonusInactive, t.RoleOf(line));
            }
        }

        [Fact]
        public async Task ASocketedSigilKeepsItsCooldownOnItsOwnGreyLine()
        {
            var t = await Composed(30699, RealItemJson.Bolt, RealItemJson.SigilOfEarth);

            Assert.Contains("Superior Sigil of Earth", t.Lines);
            Assert.Contains(t.Lines, l => l.StartsWith("Inflict bleeding for 6 seconds"));
            Assert.Equal(TooltipSpanRole.Reminder, t.RoleOf("(Cooldown: 2 Seconds)"));

            // The weapon's own infusion slot has no reading, so it still
            // reports as a slot.
            Assert.Contains("Unused Infusion Slot", t.Lines);
        }

        [Fact]
        public async Task AKnownInfusionSpendsASlotAndTheRestStayUnread()
        {
            // Sunrise carries two infusion slots; one reading leaves one.
            var t = await Composed(30703, RealItemJson.Sunrise, RealItemJson.AgonyInfusion);

            Assert.Contains("+1 Agony Infusion", t.Lines);
            Assert.Single(t.Lines.Where(l => l == "Unused Infusion Slot"));
        }

        [Fact]
        public async Task AComponentWithNoStatBlockYetIsDroppedRatherThanNamed()
        {
            var host = ItemStatBlockFactory.Build(
                await RealItemFixtures.ParseOneAsync(RealItemJson.ZojjasWarfists));

            var view = SocketedUpgradeView.Resolve(
                new SocketedUpgradeIds(new List<int> { 49424 }, null), id => null);
            var lines = ItemStatTooltipComposer.BuildContent(host, view).ToPlainLines();

            Assert.True(view.IsEmpty);
            Assert.DoesNotContain("+1 Agony Infusion", lines);
            Assert.Contains("Unused Infusion Slot", lines);
        }

        [Fact]
        public async Task ARunesBonusMarkupIsSanitizedOnTheLooseItemToo()
        {
            // details.bonuses carries the same <c=@reminder> runs the buff
            // description does, and neither may reach the reader raw.
            var raw = await RealItemFixtures.ParseOneAsync(RealItemJson.RuneOfTheWater);
            var lines = ItemStatTooltipComposer.BuildContent(ItemStatBlockFactory.Build(raw))
                .ToPlainLines();

            Assert.Contains(
                "(4): Remove a condition when you are struck. (Cooldown: 30 Seconds)", lines);
            Assert.DoesNotContain(lines, l => l.Contains("<c="));
        }

        [Fact]
        public async Task AnEnrichmentRendersFromItsDescriptionAloneItsAttributesBeingEmpty()
        {
            var block = ItemStatBlockFactory.Build(
                await RealItemFixtures.ParseOneAsync(RealItemJson.KarmicEnrichment));

            var builder = new TooltipContentBuilder();
            UpgradeEffectLines.AppendSocketedBlock(builder, block);

            Assert.Empty(block.Attributes);
            Assert.Equal(
                new[] { "Karmic Enrichment", "+15% Karma" },
                builder.Build().ToPlainLines().ToArray());
        }
    }
}
