using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The Snapshot tab shows two filter sets. Locations say which places
    /// to show. Characters say whose bags, worn gear and equipment
    /// templates to show. These tests pin how the two combine, and what a
    /// shift-click does inside one set.
    /// </summary>
    public class FilterSetTests
    {
        private const string Divineaxe = "Divineaxe";
        private const string Apoyu = "Apoyu";

        private static string Bags(string name) =>
            AccountItemIndex.CharacterSourcePrefix + name;

        private static string Worn(string name) =>
            AccountItemIndex.CharacterEquipmentSourcePrefix + name;

        private static string Templates(string name) =>
            AccountItemIndex.CharacterTemplateSourcePrefix + name;

        private static SnapshotItemEntry Entry(int itemId, string name, int count, string source)
        {
            return new SnapshotItemEntry
            {
                ItemId = itemId,
                Name = name,
                Count = count,
                Source = source,
            };
        }

        // Every location off except the one named, which is how the tab
        // hands over a set with a single box ticked.
        private static SnapshotSourceFilter OnlyLocation(string location)
        {
            var filter = new SnapshotSourceFilter
            {
                Bank = false,
                Bags = false,
                MaterialStorage = false,
                LegendaryArmory = false,
                EquipmentTemplates = false,
                Equipped = false,
                SharedInventory = false,
            };

            switch (location)
            {
                case "Bank": filter.Bank = true; break;
                case "Bags": filter.Bags = true; break;
                case "MaterialStorage": filter.MaterialStorage = true; break;
                case "LegendaryArmory": filter.LegendaryArmory = true; break;
                case "EquipmentTemplates": filter.EquipmentTemplates = true; break;
                case "Equipped": filter.Equipped = true; break;
                case "SharedInventory": filter.SharedInventory = true; break;
            }

            return filter;
        }

        // ---- The location set on its own ----
        // The reported failure: ticking Equipment Templates and nothing
        // else returned no rows at all.
        [Fact]
        public void EquipmentTemplatesAlone_ShowsEveryCharactersTemplateGear()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 5, AccountItemIndex.SourceBank),
                Entry(101, "Berserker Helm", 1, Templates(Divineaxe)),
                Entry(102, "Viper Helm", 1, Templates(Apoyu)),
                Entry(103, "Worn Axe", 1, Worn(Divineaxe)),
                Entry(104, "Bagged Axe", 1, Bags(Divineaxe)),
            };

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                "",
                OnlyLocation("EquipmentTemplates"),
                null);

            Assert.Equal(
                new[] { 101, 102 },
                rows.ConvertAll(r => r.ItemId).ToArray());
        }

        [Fact]
        public void BagsAlone_ShowsBagContentsAndNeitherWornNorTemplateGear()
        {
            var filter = OnlyLocation("Bags");

            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(Bags(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Worn(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Templates(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(
                AccountItemIndex.SourceBank, filter));
        }

        [Fact]
        public void EquippedAlone_ShowsWornGearAndNeitherBagsNorTemplateGear()
        {
            var filter = OnlyLocation("Equipped");

            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(Worn(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Bags(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Templates(Divineaxe), filter));
        }

        // A socket answers to the box its container answers to.
        [Fact]
        public void SocketedGear_FollowsTheLocationOfThePieceItSitsIn()
        {
            var bagsOnly = OnlyLocation("Bags");

            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(
                AccountItemIndex.SocketedSource(101544, Bags(Divineaxe)), bagsOnly));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(
                AccountItemIndex.SocketedSource(101544, Worn(Divineaxe)), bagsOnly));
        }

        [Fact]
        public void UncheckingBags_LeavesWornAndTemplateGearAlone()
        {
            var filter = new SnapshotSourceFilter { Bags = false };

            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Bags(Divineaxe), filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(Worn(Divineaxe), filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(Templates(Divineaxe), filter));
        }

        // ---- How the two sets combine ----
        [Fact]
        public void TheCharacterSetNarrowsOnlyCharacterHeldPlaces()
        {
            var filter = new SnapshotSourceFilter();
            filter.UncheckedCharacters.Add(Divineaxe);

            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Bags(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Worn(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Templates(Divineaxe), filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(Bags(Apoyu), filter));
        }

        // Unchecking every character empties the character-held places and
        // leaves the account-wide ones showing, so a set with nothing
        // ticked never zeroes the whole result.
        [Fact]
        public void EveryCharacterUnchecked_StillShowsTheAccountWideLocations()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 5, AccountItemIndex.SourceBank),
                Entry(101, "Mithril Ore", 3, AccountItemIndex.SourceMaterialStorage),
                Entry(102, "Shared Salvage Kit", 1, AccountItemIndex.SourceSharedInventory),
                Entry(103, "Aurene", 1, AccountItemIndex.SourceLegendaryArmory),
                Entry(104, "Bagged Axe", 1, Bags(Divineaxe)),
                Entry(105, "Worn Axe", 1, Worn(Apoyu)),
            };

            var filter = new SnapshotSourceFilter();
            filter.UncheckedCharacters.Add(Divineaxe);
            filter.UncheckedCharacters.Add(Apoyu);

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                "",
                filter,
                null);

            Assert.Equal(
                new[] { 100, 101, 102, 103 },
                SortedIds(rows));
        }

        // Both sets must pass. One character ticked plus Equipment
        // Templates ticked shows that character's stored gear only.
        [Fact]
        public void TheTwoSetsAnd_OneLocationAndOneCharacter()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(101, "Berserker Helm", 1, Templates(Divineaxe)),
                Entry(102, "Viper Helm", 1, Templates(Apoyu)),
                Entry(103, "Worn Axe", 1, Worn(Divineaxe)),
            };

            var filter = OnlyLocation("EquipmentTemplates");
            filter.UncheckedCharacters.Add(Apoyu);

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                "",
                filter,
                null);

            Assert.Equal(new[] { 101 }, SortedIds(rows));
        }

        // ---- Armory wearers ----
        // A wearer is worn gear. Unchecking Equipped stops the armory row
        // naming one, the same way unchecking that character does.
        [Fact]
        public void UncheckingEquipped_StopsTheArmoryRowNamingItsWearers()
        {
            const int Aurene = 100000;

            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    Entry(Aurene, "Aurene's Claw", 1, AccountItemIndex.SourceLegendaryArmory),
                },
                LegendaryArmoryEquipped = new List<SnapshotArmoryEquip>
                {
                    new SnapshotArmoryEquip { ItemId = Aurene, CharacterName = Divineaxe },
                },
            };

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(snapshot.Items),
                new AccountItemIndex(snapshot.Items),
                "",
                new SnapshotSourceFilter { Equipped = false },
                null,
                null,
                SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(snapshot));

            var row = Assert.Single(rows);
            Assert.Null(Assert.Single(row.Breakdown).EquippedBy);
        }

        // ---- The source key classifier ----
        [Fact]
        public void CharacterPlaceOf_NamesThePlaceAndWhereTheNameStarts()
        {
            AssertPlace(Bags(Divineaxe), CharacterPlaceKind.Bags);
            AssertPlace(Worn(Divineaxe), CharacterPlaceKind.Equipped);
            AssertPlace(Templates(Divineaxe), CharacterPlaceKind.TemplateGear);
            AssertPlace(AccountItemIndex.SourceBank, CharacterPlaceKind.None);
            AssertPlace(AccountItemIndex.SourceMaterialStorage, CharacterPlaceKind.None);
            AssertPlace(null, CharacterPlaceKind.None);
        }

        private static void AssertPlace(string source, CharacterPlaceKind expected)
        {
            int nameOffset;
            Assert.Equal(expected, AccountItemIndex.CharacterPlaceOf(source, out nameOffset));

            if (expected == CharacterPlaceKind.None)
            {
                Assert.Equal(-1, nameOffset);
            }
            else
            {
                Assert.Equal(Divineaxe, source.Substring(nameOffset));
            }
        }

        [Fact]
        public void CharacterPlaceOf_ReadsThroughASocketToItsContainer()
        {
            int nameOffset;
            string socketed = AccountItemIndex.SocketedSource(101544, Templates(Divineaxe));

            Assert.Equal(
                CharacterPlaceKind.TemplateGear,
                AccountItemIndex.CharacterPlaceOf(socketed, out nameOffset));
            Assert.Equal(Divineaxe, socketed.Substring(nameOffset));
        }

        // ---- The short-query character hint ----
        [Fact]
        public void ShortQueryHint_OffersAMatchWhileACharacterPlaceIsShown()
        {
            Assert.Equal(
                SnapshotSearchResultBuilder.ShortQueryCharacterHintText,
                SnapshotSearchResultBuilder.ShortQueryCharacterHint(
                    "D", new[] { Divineaxe }, null, true));
        }

        // With bags, worn gear and templates all unticked, another letter
        // would surface nothing, so the hint is withheld.
        [Fact]
        public void ShortQueryHint_WithheldWhenNoCharacterPlaceIsShown()
        {
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint(
                "D", new[] { Divineaxe }, null, false));
        }

        private static int[] SortedIds(List<SnapshotSearchRow> rows)
        {
            var ids = rows.ConvertAll(r => r.ItemId);
            ids.Sort();
            return ids.ToArray();
        }
    }
}
