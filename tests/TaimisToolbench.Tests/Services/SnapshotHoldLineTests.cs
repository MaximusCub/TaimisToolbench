using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class SnapshotHoldLineTests
    {
        private static SnapshotHoldLocation Place(
            SnapshotHoldCategory category, int count, string characterName = "")
        {
            return new SnapshotHoldLocation
            {
                Category = category,
                Count = count,
                CharacterName = characterName,
            };
        }

        // --- The three lines the module was asked for, verbatim ---
        [Fact]
        public void EveryPlacePrintsItsCountWhenOnePlaceHoldsMoreThanOne()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.SharedInventory, 3),
                Place(SnapshotHoldCategory.Bags, 2, "Apoyu"),
                Place(SnapshotHoldCategory.Bags, 2, "Divineaxe"),
                Place(SnapshotHoldCategory.Equipped, 1, "Apoyu"),
                Place(SnapshotHoldCategory.Bank, 1),
                Place(SnapshotHoldCategory.MaterialStorage, 1),
            });

            Assert.Equal(
                "Shared Inventory (3)  Bags: Apoyu (2) Divineaxe (2)  Equipped: Apoyu (1)  Bank (1)  Material Storage (1)",
                line);
        }

        [Fact]
        public void OnePlaceHoldingOnePrintsNoCount()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.Bags, 1, "Apoyu"),
            });

            Assert.Equal("Bags: Apoyu", line);
        }

        [Fact]
        public void OneAccountWidePlaceHoldingTheWholeTotalPrintsNoCount()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.MaterialStorage, 28),
            });

            Assert.Equal("Material Storage", line);
        }

        [Fact]
        public void OneCharacterHoldingTheWholeTotalPrintsNoCount()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.Bags, 4, "Divineaxe"),
            });

            Assert.Equal("Bags: Divineaxe", line);
        }

        [Fact]
        public void TwoPlacesEachHoldingOnePrintNoCounts()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.Bags, 1, "Apoyu"),
                Place(SnapshotHoldCategory.Bags, 1, "Divineaxe"),
            });

            Assert.Equal("Bags: Apoyu, Divineaxe", line);
        }

        // --- The rest of the rules ---
        [Fact]
        public void AccountWidePlaceHoldingOneNamesNoCountAndNoCharacter()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.Bank, 1),
            });

            Assert.Equal("Bank", line);
        }

        [Fact]
        public void AccountWidePlacesEachHoldingOnePrintNoCounts()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.Bank, 1),
                Place(SnapshotHoldCategory.MaterialStorage, 1),
            });

            Assert.Equal("Bank  Material Storage", line);
        }

        [Fact]
        public void CategoriesRunInReadingOrderWhateverOrderTheyArriveIn()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.MaterialStorage, 40),
                Place(SnapshotHoldCategory.Equipped, 1, "Apoyu"),
                Place(SnapshotHoldCategory.Bank, 5),
                Place(SnapshotHoldCategory.Bags, 2, "Apoyu"),
                Place(SnapshotHoldCategory.SharedInventory, 1),
            });

            Assert.Equal(
                "Shared Inventory (1)  Bags: Apoyu (2)  Equipped: Apoyu (1)  Bank (5)  Material Storage (40)",
                line);
        }

        [Fact]
        public void LegendaryArmoryIsAccountWideAndReadsLastOfTheKnownPlaces()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.LegendaryArmory, 1),
                Place(SnapshotHoldCategory.Bank, 2),
                Place(SnapshotHoldCategory.Equipped, 1, "Divineaxe"),
            });

            Assert.Equal("Equipped: Divineaxe (1)  Bank (2)  Legendary Armory (1)", line);
        }

        [Fact]
        public void LegendaryArmoryHoldingOnePrintsNoCountLikeTheOtherAccountWidePlaces()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                SnapshotHoldLine.FromSource(AccountItemIndex.SourceLegendaryArmory, 1),
            });

            Assert.Equal("Legendary Armory", line);
        }

        // --- Who is wearing an account-wide Legendary Armory item ---
        [Fact]
        public void TheArmoryNamesTheCharactersWearingItsCopy()
        {
            var armory = Place(SnapshotHoldCategory.LegendaryArmory, 1);
            armory.EquippedBy = new List<string> { "Divineaxe", "Apoyu" };

            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation> { armory });

            // The dash keeps these apart from the colon that introduces the
            // characters holding a category's stock. They hold none: they
            // draw the one copy this place already counts.
            Assert.Equal("Legendary Armory - Equipped: Divineaxe, Apoyu", line);
        }

        [Fact]
        public void WearersNeverAddToTheCount()
        {
            var armory = Place(SnapshotHoldCategory.LegendaryArmory, 2);
            armory.EquippedBy = new List<string> { "Divineaxe", "Apoyu", "Zoe" };

            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.Bank, 1),
                armory,
            });

            Assert.Equal(
                "Bank (1)  Legendary Armory (2) - Equipped: Divineaxe, Apoyu, Zoe", line);
        }

        [Fact]
        public void WearersDoNotTurnAOnePlaceLineIntoACountedOne()
        {
            var armory = Place(SnapshotHoldCategory.LegendaryArmory, 3);
            armory.EquippedBy = new List<string> { "Apoyu" };

            Assert.Equal(
                "Legendary Armory - Equipped: Apoyu",
                SnapshotHoldLine.Format(new List<SnapshotHoldLocation> { armory }));
        }

        [Fact]
        public void AnArmoryNobodyIsWearingReadsAsItAlwaysDid()
        {
            foreach (var wearers in new[] { null, new List<string>(), new List<string> { "" } })
            {
                var armory = Place(SnapshotHoldCategory.LegendaryArmory, 1);
                armory.EquippedBy = wearers;

                Assert.Equal(
                    "Legendary Armory",
                    SnapshotHoldLine.Format(new List<SnapshotHoldLocation> { armory }));
            }
        }

        [Fact]
        public void CharactersKeepTheOrderTheCallerSupplied()
        {
            // GetPrioritizedSources puts the active character first, and the
            // line must not re-sort that away.
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                Place(SnapshotHoldCategory.Bags, 1, "Zoe"),
                Place(SnapshotHoldCategory.Bags, 1, "Abel"),
            });

            Assert.Equal("Bags: Zoe, Abel", line);
        }

        [Fact]
        public void NothingHoldingTheItemIsAnEmptyLine()
        {
            Assert.Equal("", SnapshotHoldLine.Format(null));
            Assert.Equal("", SnapshotHoldLine.Format(new List<SnapshotHoldLocation>()));
        }

        // --- Reading a raw source key ---
        [Fact]
        public void BagsAndWornGearReadAsDifferentPlacesOnTheSameCharacter()
        {
            var bags = SnapshotHoldLine.FromSource(
                AccountItemIndex.CharacterSourcePrefix + "Divineaxe", 4);
            var equipped = SnapshotHoldLine.FromSource(
                AccountItemIndex.CharacterEquipmentSourcePrefix + "Divineaxe", 1);

            Assert.Equal(SnapshotHoldCategory.Bags, bags.Category);
            Assert.Equal("Divineaxe", bags.CharacterName);
            Assert.Equal(4, bags.Count);

            Assert.Equal(SnapshotHoldCategory.Equipped, equipped.Category);
            Assert.Equal("Divineaxe", equipped.CharacterName);
            Assert.Equal(1, equipped.Count);
        }

        [Fact]
        public void StorageKeysReadAsTheAccountWidePlaces()
        {
            Assert.Equal(
                SnapshotHoldCategory.SharedInventory,
                SnapshotHoldLine.FromSource(AccountItemIndex.SourceSharedInventory, 1).Category);
            Assert.Equal(
                SnapshotHoldCategory.Bank,
                SnapshotHoldLine.FromSource(AccountItemIndex.SourceBank, 1).Category);
            Assert.Equal(
                SnapshotHoldCategory.MaterialStorage,
                SnapshotHoldLine.FromSource(AccountItemIndex.SourceMaterialStorage, 1).Category);
            Assert.Equal(
                SnapshotHoldCategory.LegendaryArmory,
                SnapshotHoldLine.FromSource(AccountItemIndex.SourceLegendaryArmory, 1).Category);
        }

        [Fact]
        public void AnUnrecognizedKeyStillShowsItsOwnText()
        {
            var location = SnapshotHoldLine.FromSource("SomeFutureSource", 7);

            Assert.Equal(SnapshotHoldCategory.Unknown, location.Category);
            Assert.Equal(
                "SomeFutureSource",
                SnapshotHoldLine.Format(new List<SnapshotHoldLocation> { location }));
        }

        [Fact]
        public void TwoUnrecognizedKeysAreBothNamed()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                SnapshotHoldLine.FromSource("FutureVault", 1),
                SnapshotHoldLine.FromSource("FutureLocker", 1),
            });

            Assert.Equal("FutureVault  FutureLocker", line);
        }

        [Fact]
        public void TwoUnrecognizedKeysEachPrintTheirOwnBracketedCount()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                SnapshotHoldLine.FromSource("FutureVault", 1),
                SnapshotHoldLine.FromSource("FutureLocker", 2),
            });

            Assert.Equal("FutureVault (1)  FutureLocker (2)", line);
        }

        [Fact]
        public void UnrecognizedKeysReadAfterEveryKnownPlace()
        {
            var line = SnapshotHoldLine.Format(new List<SnapshotHoldLocation>
            {
                SnapshotHoldLine.FromSource("FutureVault", 1),
                SnapshotHoldLine.FromSource(AccountItemIndex.SourceBank, 1),
            });

            Assert.Equal("Bank  FutureVault", line);
        }

        [Fact]
        public void ANullKeyReadsAsUnknownAndNeverThrows()
        {
            var location = SnapshotHoldLine.FromSource(null, 1);

            Assert.Equal(SnapshotHoldCategory.Unknown, location.Category);
            Assert.Equal(
                "Unknown",
                SnapshotHoldLine.Format(new List<SnapshotHoldLocation> { location }));
        }
    }
}
