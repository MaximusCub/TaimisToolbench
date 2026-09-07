using System;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The upgrade-slot rule /v2/items has no field for, and the flag that
    /// picks one of the game's infusion glyphs.
    /// </summary>
    public class ItemSlotFactsTests
    {
        [Theory]
        [InlineData("Greatsword", 2)]
        [InlineData("Hammer", 2)]
        [InlineData("LongBow", 2)]
        [InlineData("Rifle", 2)]
        [InlineData("ShortBow", 2)]
        [InlineData("Staff", 2)]
        [InlineData("Harpoon", 2)]
        [InlineData("Speargun", 2)]
        [InlineData("Trident", 2)]
        [InlineData("Axe", 1)]
        [InlineData("Dagger", 1)]
        [InlineData("Mace", 1)]
        [InlineData("Pistol", 1)]
        [InlineData("Scepter", 1)]
        [InlineData("Sword", 1)]
        [InlineData("Focus", 1)]
        [InlineData("Shield", 1)]
        [InlineData("Torch", 1)]
        [InlineData("Warhorn", 1)]
        public void EveryWeaponTypeTakesOneSigilPerHand(string subType, int expected)
        {
            Assert.Equal(expected, ItemSlotFacts.UpgradeSlotCount("Weapon", subType, false));
        }

        [Theory]
        [InlineData("LargeBundle")]
        [InlineData("SmallBundle")]
        [InlineData("Toy")]
        [InlineData("ToyTwoHanded")]
        public void TheWeaponTypesNoSigilIsFlaggedForTakeNoUpgrade(string subType)
        {
            Assert.Equal(0, ItemSlotFacts.UpgradeSlotCount("Weapon", subType, false));
        }

        [Theory]
        [InlineData("Helm")]
        [InlineData("Shoulders")]
        [InlineData("Coat")]
        [InlineData("Gloves")]
        [InlineData("Leggings")]
        [InlineData("Boots")]
        [InlineData("HelmAquatic")]
        public void EveryArmorPieceTakesExactlyOneRune(string subType)
        {
            Assert.Equal(1, ItemSlotFacts.UpgradeSlotCount("Armor", subType, false));
        }

        [Theory]
        [InlineData("Trinket", "Amulet")]
        [InlineData("Trinket", "Ring")]
        [InlineData("Trinket", "Accessory")]
        [InlineData("Back", null)]
        public void ATrinketOrBackItemTakesOneJewel(string itemType, string subType)
        {
            Assert.Equal(1, ItemSlotFacts.UpgradeSlotCount(itemType, subType, false));
        }

        [Fact]
        public void NotUpgradeableRemovesTheSlotWhateverTheTypeSays()
        {
            // The ascended cliff, and the Bloodbound/Dreambound weapons at
            // ordinary rarities, both express themselves through this flag
            // rather than through rarity.
            Assert.Equal(0, ItemSlotFacts.UpgradeSlotCount("Trinket", "Ring", true));
            Assert.Equal(0, ItemSlotFacts.UpgradeSlotCount("Back", null, true));
            Assert.Equal(0, ItemSlotFacts.UpgradeSlotCount("Weapon", "Greatsword", true));
            Assert.Equal(0, ItemSlotFacts.UpgradeSlotCount("Armor", "Coat", true));
        }

        [Theory]
        [InlineData("UpgradeComponent")]
        [InlineData("CraftingMaterial")]
        [InlineData("Consumable")]
        [InlineData("Trophy")]
        [InlineData(null)]
        public void NothingElseHasAnUpgradeSlot(string itemType)
        {
            Assert.Equal(0, ItemSlotFacts.UpgradeSlotCount(itemType, "Default", false));
        }

        [Fact]
        public void AWeaponWithNoSubTypeReportsNoSlotRatherThanGuessingOneHanded()
        {
            Assert.Equal(0, ItemSlotFacts.UpgradeSlotCount("Weapon", null, false));
        }

        [Fact]
        public void AWeaponTypeTheRuleHasNeverSeenReadsAsOneHanded()
        {
            // No such value exists today - land spears report Harpoon - so
            // this pins which way an added one would fall, not a live case.
            Assert.Equal(1, ItemSlotFacts.UpgradeSlotCount("Weapon", "Spear", false));
        }

        [Fact]
        public void TheEnrichmentFlagIsTheOnlyThingThatChangesTheSlotKind()
        {
            Assert.Equal(
                ItemSlotKind.Enrichment,
                ItemSlotFacts.InfusionSlotKind(new[] { "Enrichment" }));
            Assert.Equal(
                ItemSlotKind.Infusion,
                ItemSlotFacts.InfusionSlotKind(new[] { "Infusion" }));
        }

        [Fact]
        public void AnUnflaggedSlotIsStillASlotAndReadsAsAPlainInfusionOne()
        {
            Assert.Equal(ItemSlotKind.Infusion, ItemSlotFacts.InfusionSlotKind(null));
            Assert.Equal(ItemSlotKind.Infusion, ItemSlotFacts.InfusionSlotKind(new string[0]));
            Assert.Equal(
                ItemSlotKind.Infusion, ItemSlotFacts.InfusionSlotKind(new[] { "Somethingelse" }));
        }

        [Fact]
        public void EverySlotKindNamesAGlyphAndNoTwoKindsShareOne()
        {
            var art = Enum.GetValues(typeof(ItemSlotKind))
                .Cast<ItemSlotKind>()
                .Select(ItemSlotFacts.SlotArtAssetId)
                .ToArray();

            Assert.All(art, id => Assert.True(id > 0));
            Assert.Equal(art.Length, art.Distinct().Count());
        }
    }
}
