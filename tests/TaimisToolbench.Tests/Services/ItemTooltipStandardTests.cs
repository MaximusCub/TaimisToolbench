using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Every item icon in the module shows the same box for the same item.
    /// A caller passes an id and a size bucket, so there is one input and
    /// one answer, and these compare answers rather than call sites.
    /// </summary>
    public class ItemTooltipStandardTests
    {
        private const int Mithril = 19700;
        private const int RuneId = 24836;
        private const int HelmId = 48074;

        private static ItemMetadata Metadata()
        {
            return new ItemMetadata
            {
                ItemId = Mithril,
                Name = "Mithril Ore",
                IconUrl = "mithril.png",
                Rarity = "Basic",
            };
        }

        private static ItemStatBlock Stats()
        {
            return new ItemStatBlock
            {
                ItemId = Mithril,
                Name = "Mithril Ore",
                IconUrl = "mithril.png",
                Rarity = "Basic",
                ItemType = "CraftingMaterial",
                VendorValue = 7,
            };
        }

        private static TooltipContent Compose(ItemTooltipFacts facts)
        {
            return ItemRowTooltipComposer.BuildRowContent(
                ItemStatTooltipComposer.BuildContent(facts.Stats, facts.Sockets, facts.Skin),
                facts.Identity,
                SecondTooltipBox.Compose(
                    (TooltipContent)null, IconWikiTarget.ItemPage(facts.Name).Hint));
        }

        [Fact]
        public void TheFirstBoxIsTheItemsOwnContent()
        {
            var content = Compose(ItemTooltipFacts.ForItemId(Mithril, Metadata(), Stats()));

            Assert.Equal("Mithril Ore", content.ToPlainLines()[0]);
            Assert.Equal(TooltipLineKind.Header, content.Lines[0].Kind);
            Assert.Equal("mithril.png", content.Lines[0].IconUrl);
        }

        [Fact]
        public void TheSecondBoxEndsWithTheWikiLine()
        {
            var content = Compose(ItemTooltipFacts.ForItemId(Mithril, Metadata(), Stats()));

            Assert.True(content.HasExtra);
            Assert.Equal(new[] { IconWikiTarget.HintText }, content.ToExtraLines());
        }

        /// <summary>
        /// The plan tab reads the plan's metadata dictionary. The Ranker
        /// and Plan History read their own captures. Feed each surface's
        /// own inputs to the facts and the boxes agree.
        /// </summary>
        [Fact]
        public void EverySurfaceShowsTheSameBoxForTheSameItem()
        {
            var fromStore = Compose(ItemTooltipFacts.ForItemId(Mithril, Metadata(), Stats()));
            var fromCapture = Compose(ItemTooltipFacts.ForCapturedItem(
                "Mithril Ore", "mithril.png", "Basic", Stats()));

            Assert.Equal(fromStore.ToPlainLines(), fromCapture.ToPlainLines());
            Assert.Equal(fromStore.ToExtraLines(), fromCapture.ToExtraLines());
        }

        /// <summary>
        /// The stat block arrives from the API after the row is drawn.
        /// Until it does, the box is the icon and the name, never a blank
        /// or a guess.
        /// </summary>
        [Fact]
        public void BeforeTheStatBlockArrivesTheBoxIsTheIconAndTheName()
        {
            var facts = ItemTooltipFacts.ForItemId(Mithril, Metadata(), null);

            Assert.Null(facts.Stats);
            Assert.Equal("Mithril Ore", facts.Name);
            Assert.Equal("mithril.png", facts.IconUrl);
            Assert.Equal("Basic", facts.Rarity);

            var content = Compose(facts);
            Assert.Equal(new[] { "Mithril Ore" }, content.ToPlainLines());
        }

        [Fact]
        public void AnUnnamedSubjectHeadsNothing()
        {
            var facts = ItemTooltipFacts.Unnamed();

            Assert.Null(facts.Name);
            Assert.False(facts.Identity.HasSubject);
        }

        /// <summary>
        /// Rarity goes through the module's one resolution policy, so an
        /// item nobody looked up draws the neutral frame rather than a
        /// guessed colour.
        /// </summary>
        [Fact]
        public void AnUnresolvedRarityStaysNull()
        {
            Assert.Null(ItemTooltipFacts.ForItemId(Mithril, null, null).Rarity);
        }

        // --- the equipped character drives the rune count ---
        private static SnapshotItemEntry Worn(string source, params int[] upgrades)
        {
            return new SnapshotItemEntry
            {
                ItemId = HelmId,
                Count = 1,
                Source = source,
                Upgrades = new List<int>(upgrades),
            };
        }

        /// <summary>
        /// The character is the input. A named one gives a count; a null
        /// one gives none, which draws the bonuses unlit rather than
        /// claiming a wrong number.
        /// </summary>
        [Fact]
        public void ANamedCharacterCountsItsOwnWornPieces()
        {
            var index = new EquippedRuneSetIndex(new List<SnapshotItemEntry>
            {
                Worn("Equipped:Taimi", RuneId, RuneId),
            });

            Assert.Equal("Equipped:Taimi", index.EquippedBy(HelmId));
            Assert.Equal(2, index.WornCopiesOf("Equipped:Taimi", RuneId));
        }

        [Fact]
        public void AnUnknownCharacterCountsNothingRatherThanGuessing()
        {
            var index = new EquippedRuneSetIndex(new List<SnapshotItemEntry>
            {
                Worn("Equipped:Taimi", RuneId, RuneId),
            });

            Assert.Equal(0, index.WornCopiesOf(null, RuneId));
            Assert.Equal(0, index.WornCopiesOf("Equipped:Someone Else", RuneId));
        }

        /// <summary>
        /// A stack held in more than one place has no single wearer, so the
        /// index names no character and the tooltip counts nothing.
        /// </summary>
        [Fact]
        public void AStackHeldInTwoPlacesNamesNoCharacter()
        {
            var index = new EquippedRuneSetIndex(new List<SnapshotItemEntry>
            {
                Worn("Equipped:Taimi", RuneId),
                new SnapshotItemEntry
                {
                    ItemId = HelmId,
                    Count = 1,
                    Source = "Bank",
                    Upgrades = new List<int>(),
                },
            });

            Assert.Null(index.EquippedBy(HelmId));
            Assert.Equal(0, index.WornCopies(HelmId, RuneId));
        }

        /// <summary>
        /// The count reaches the reader as a counter on the component's
        /// name, and the bonus tiers under it light up to that number.
        /// </summary>
        [Fact]
        public void TheCountReachesTheBonusLadder()
        {
            var rune = new ItemStatBlock
            {
                ItemId = RuneId,
                Name = "Superior Rune of the Scholar",
                UpgradeBonuses = new List<string> { "+25 Power", "+35 Ferocity" },
            };

            var lit = new TooltipContentBuilder();
            UpgradeEffectLines.AppendSocketedBlock(lit, rune, 2);
            var unlit = new TooltipContentBuilder();
            UpgradeEffectLines.AppendSocketedBlock(unlit, rune, 0);

            var litLines = lit.Build().ToPlainLines();
            var unlitLines = unlit.Build().ToPlainLines();

            // The count rides the component's NAME. The tier ladder under
            // it is drawn either way; only its active tiers differ.
            Assert.Contains("2", litLines[0]);
            Assert.Equal("Superior Rune of the Scholar", unlitLines[0]);
            Assert.NotEqual(unlitLines[0], litLines[0]);

            Assert.Equal(litLines.Count, unlitLines.Count);
            Assert.Contains("+25 Power", string.Join("\n", litLines));
            Assert.Contains("+25 Power", string.Join("\n", unlitLines));
        }
    }
}
