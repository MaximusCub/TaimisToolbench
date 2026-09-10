using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// What a rune, sigil or infusion sitting in worn gear is worth to the
    /// account: a row of its own, searchable, counted, and consumable by a
    /// plan. Drives the shipped row builder and then the shipped search,
    /// hold-line and reducer over what it produced.
    /// </summary>
    public class SocketedItemRowsTests : IDisposable
    {
        private const int Helm = 101544;
        private const int Coat = 101521;
        private const int Rune = 24836;
        private const int Sigil = 24615;
        private const int Infusion = 49431;
        private const int LegendaryRune = 100147;

        private readonly string _tempDir;

        public SocketedItemRowsTests()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
        }

        // ---- One slot's sockets ----
        [Fact]
        public void AddFor_GivesEachSocketedItemItsOwnRowUnderTheHostsSource()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(
                rows, Helm, "Divineaxe", 1, new[] { Rune }, new[] { Infusion });

            Assert.Equal(2, rows.Count);
            Assert.Equal(new[] { Rune, Infusion }, rows.Select(r => r.ItemId).ToArray());
            Assert.All(rows, r => Assert.Equal(1, r.Count));
            Assert.All(rows, r => Assert.Equal("Socketed:101544:Divineaxe", r.Source));
        }

        [Fact]
        public void AddFor_AddsNothingWhenTheSlotHasNoSockets()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(rows, Helm, "Divineaxe", 1, null, null);
            SocketedItemRows.AddFor(rows, Helm, "Divineaxe", 1, new int[0], new int[0]);

            // A slot whose sockets could not be read must make no claim at
            // all, rather than a claim that it is empty.
            Assert.Empty(rows);
        }

        [Fact]
        public void AddFor_ReadsTwoOfTheSameInfusionAsOneRowOfTwo()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(
                rows, Helm, "Divineaxe", 1, null, new[] { Infusion, Infusion });

            Assert.Single(rows);
            Assert.Equal(2, rows[0].Count);
        }

        [Fact]
        public void AddFor_SkipsAnIdTheCaptureCouldNotRead()
        {
            var rows = new List<SnapshotItemEntry>();

            SocketedItemRows.AddFor(rows, Helm, "Divineaxe", 1, new[] { 0, Rune, -1 }, null);

            Assert.Single(rows);
            Assert.Equal(Rune, rows[0].ItemId);
        }

        [Fact]
        public void AddFor_LeavesTheHostRowAloneWhenItIsAlreadyInTheList()
        {
            var rows = new List<SnapshotItemEntry>
            {
                Row(Helm, 1, AccountItemIndex.CharacterEquipmentSourcePrefix + "Divineaxe"),
            };

            SocketedItemRows.AddFor(rows, Helm, "Divineaxe", 1, new[] { Helm }, null);

            // The scan that merges repeated sockets must not reach back into
            // rows this call did not add, or a socket would be counted as
            // another copy of the gear.
            Assert.Equal(2, rows.Count);
            Assert.Equal(1, rows[0].Count);
            Assert.Equal(1, rows[1].Count);
            Assert.True(AccountItemIndex.IsSocketedSource(rows[1].Source));
        }

        // ---- What the account then holds ----
        [Fact]
        public void SixArmourSlotsCarryingOneRuneAreSixRunes()
        {
            var rows = SixPieceSet("Divineaxe");
            var index = new AccountItemIndex(rows);

            Assert.Equal(6, Total(index, Rune));
        }

        [Fact]
        public void FourCharactersInThatSetAreTwentyFourRunes()
        {
            var rows = new List<SnapshotItemEntry>();
            foreach (string name in new[] { "Divineaxe", "Apoyu", "Zoe", "Taimi" })
            {
                rows.AddRange(SixPieceSet(name));
            }

            var index = new AccountItemIndex(rows);

            Assert.Equal(24, Total(index, Rune));
        }

        // ---- Where the Snapshot tab says they are ----
        [Fact]
        public void ASocketedRuneIsSearchableAndNamesTheGearItSitsIn()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1,
                    AccountItemIndex.CharacterEquipmentSourcePrefix + "Divineaxe"),
            };
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var row = SearchOneRow(items, "Scholar");

            Assert.Equal(Rune, row.ItemId);
            Assert.Equal(1, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe (Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ASlotDrawingALegendaryFromTheArmoryStillReportsItsSockets()
        {
            // The gear itself is the armory's row. Only its sockets come
            // from the character, which is the whole of what used to be
            // lost.
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1, AccountItemIndex.SourceLegendaryArmory),
            };
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { Sigil }, null);
            Name(items, Sigil, "Superior Sigil of Force");

            var row = SearchOneRow(items, "Force");

            Assert.Equal(1, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe (Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void TwoPiecesOnOneCharacterAreTwoPlacesOnTheLine()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(Helm, "Obsidian Heavy Helmet", 1,
                    AccountItemIndex.CharacterEquipmentSourcePrefix + "Divineaxe"),
                Named(Coat, "Obsidian Heavy Breastplate", 1,
                    AccountItemIndex.CharacterEquipmentSourcePrefix + "Divineaxe"),
            };
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { Rune }, null);
            SocketedItemRows.AddFor(items, Coat, "Divineaxe", 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var row = SearchOneRow(items, "Scholar");

            Assert.Equal(2, row.TotalCount);
            Assert.Equal(
                "Equipped: Divineaxe (Obsidian Heavy Breastplate), Divineaxe (Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void TheWearerIsNamedWithoutTheGearWhenTheCaptureCannotNameIt()
        {
            // The host id resolves to nothing, and an id is never shown
            // (repo invariant), so the place names the character alone.
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var row = SearchOneRow(items, "Scholar");

            Assert.Equal("Equipped: Divineaxe", SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void ADamagedSocketKeyNamesNoIdOnTheLine()
        {
            // A key too damaged to read a host or a wearer out of still
            // reaches the line, and the line still shows no id.
            var items = new List<SnapshotItemEntry>
            {
                Named(Rune, "Superior Rune of the Scholar", 1, "Socketed:not-an-id"),
            };

            var row = SearchOneRow(items, "Scholar");

            Assert.Equal("Equipped", SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void UncheckingACharacterHidesWhatIsSocketedIntoItsGear()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var filter = new SnapshotSourceFilter();
            filter.UncheckedCharacters.Add("Divineaxe");

            Assert.Empty(SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                "",
                filter,
                null));
        }

        [Fact]
        public void SearchingTheWearersNameFindsWhatIsSocketedIntoItsGear()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { Rune }, null);
            Name(items, Rune, "Superior Rune of the Scholar");

            var row = SearchOneRow(items, "Divineaxe");

            Assert.Equal(Rune, row.ItemId);
        }

        // ---- The armory's own copies ----
        [Fact]
        public void ALegendaryRuneMakesNoRowAndNamesItsWearerOnTheArmoryRow()
        {
            var items = new List<SnapshotItemEntry>
            {
                Named(LegendaryRune, "Legendary Rune", 6, AccountItemIndex.SourceLegendaryArmory),
            };
            foreach (int host in new[] { Helm, Coat })
            {
                SocketedItemRows.AddFor(items, host, "Divineaxe", 1, new[] { LegendaryRune }, null);
            }

            SocketedItemRows.AddFor(items, Helm, "Apoyu", 1, new[] { LegendaryRune }, null);

            var snapshot = new AccountSnapshot { Items = items };
            SocketedItemRows.SettleArmoryOwned(
                snapshot.Items, snapshot.LegendaryArmoryEquipped,
                new HashSet<int> { LegendaryRune });

            // One armory row, still carrying the armory's own count.
            Assert.Single(snapshot.Items);
            Assert.Equal(6, snapshot.Items[0].Count);

            var row = SearchOneRow(snapshot.Items, "Legendary Rune", snapshot);
            Assert.Equal(6, row.TotalCount);
            Assert.Equal(
                "Legendary Armory - Equipped: Divineaxe, Apoyu",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        [Fact]
        public void SettleArmoryOwned_LeavesEveryOtherRowAlone()
        {
            var items = new List<SnapshotItemEntry>
            {
                Row(LegendaryRune, 2, AccountItemIndex.SourceBank),
                Row(Rune, 3, AccountItemIndex.SourceBank),
            };
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { Rune }, null);

            SocketedItemRows.SettleArmoryOwned(items, null, new HashSet<int> { LegendaryRune });

            // A bank stack is the account's own copy however the armory
            // reports the same id, and an ordinary rune is nothing to do
            // with the armory at all.
            Assert.Equal(3, items.Count);
            Assert.Equal(2, new AccountItemIndex(items).GetQuantity(
                LegendaryRune, AccountItemIndex.SourceBank));
            Assert.Equal(4, Total(new AccountItemIndex(items), Rune));
        }

        [Fact]
        public void SettleArmoryOwned_DoesNothingWithoutAnArmoryReading()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { LegendaryRune }, null);

            SocketedItemRows.SettleArmoryOwned(items, null, new HashSet<int>());
            SocketedItemRows.SettleArmoryOwned(items, null, null);
            SocketedItemRows.SettleArmoryOwned(null, null, new HashSet<int> { LegendaryRune });

            Assert.Single(items);
        }

        // ---- What already read the socket lists ----
        [Fact]
        public void TheRowsChangeNeitherTheSocketIndexNorTheRuneSetCount()
        {
            var gearOnly = new List<SnapshotItemEntry>
            {
                Sockets(
                    Named(Helm, "Obsidian Heavy Helmet", 1,
                        AccountItemIndex.CharacterEquipmentSourcePrefix + "Divineaxe"),
                    new List<int> { Rune }),
            };

            var withRows = new List<SnapshotItemEntry>(gearOnly);
            SocketedItemRows.AddFor(withRows, Helm, "Divineaxe", 1, new[] { Rune }, null);

            var before = SocketedUpgradeIndex.Build(gearOnly);
            var after = SocketedUpgradeIndex.Build(withRows);
            Assert.Equal(before.Count, after.Count);
            Assert.Equal(before[Helm].Upgrades, after[Helm].Upgrades);

            Assert.Equal(
                new EquippedRuneSetIndex(gearOnly).WornCopies(Helm, Rune),
                new EquippedRuneSetIndex(withRows).WornCopies(Helm, Rune));
        }

        // ---- What a plan does with them ----
        [Fact]
        public void APlanDiscountsARuneItFindsInWornGear()
        {
            var items = new List<SnapshotItemEntry>();
            SocketedItemRows.AddFor(items, Helm, "Divineaxe", 1, new[] { Rune }, null);

            var node = new RecipeNode { Id = Rune, Quantity = 1, IngredientType = "Item" };
            var root = new RecipeNode
            {
                Id = 9000,
                Quantity = 1,
                IngredientType = "Item",
                Recipes =
                {
                    new RecipeOption
                    {
                        RecipeId = 1,
                        OutputCount = 1,
                        CraftsNeeded = 1,
                        Ingredients = { node },
                    },
                },
            };
            RecipeNodeIds.Assign(root);

            var result = new InventoryReducer().Reduce(
                root, new AccountItemIndex(items), "Divineaxe");

            var used = Assert.Single(result.UsedMaterials);
            Assert.Equal(Rune, used.ItemId);
            Assert.Equal(1, used.QuantityUsed);
            Assert.Equal("Socketed:101544:Divineaxe", used.Sources[0].Source);
        }

        // ---- Through a real store ----
        [Fact]
        public void TheRowsSurviveASaveAndLoad()
        {
            var store = new SnapshotStore(_tempDir);
            var snapshot = new AccountSnapshot
            {
                CapturedAt = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc),
                Items = new List<SnapshotItemEntry>
                {
                    Named(Helm, "Obsidian Heavy Helmet", 1,
                        AccountItemIndex.CharacterEquipmentSourcePrefix + "Divineaxe"),
                },
            };
            SocketedItemRows.AddFor(
                snapshot.Items, Helm, "Divineaxe", 1, new[] { Rune }, new[] { Infusion });
            Name(snapshot.Items, Rune, "Superior Rune of the Scholar");
            Name(snapshot.Items, Infusion, "+7 Agony Infusion");

            store.Save(snapshot);
            var loaded = store.LoadLatest();

            var index = new AccountItemIndex(loaded.Items);
            Assert.Equal(1, Total(index, Rune));
            Assert.Equal(1, Total(index, Infusion));

            var row = SearchOneRow(loaded.Items, "Agony");
            Assert.Equal(
                "Equipped: Divineaxe (Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(row.Breakdown));
        }

        // ---- Helpers ----
        private static List<SnapshotItemEntry> SixPieceSet(string characterName)
        {
            var rows = new List<SnapshotItemEntry>();
            for (int piece = 0; piece < 6; piece++)
            {
                rows.Add(Row(Helm + piece, 1,
                    AccountItemIndex.CharacterEquipmentSourcePrefix + characterName));
                SocketedItemRows.AddFor(
                    rows, Helm + piece, characterName, 1, new[] { Rune }, null);
            }

            return rows;
        }

        private static int Total(AccountItemIndex index, int itemId)
        {
            return index.GetSources(itemId).Sum(s => index.GetQuantity(itemId, s));
        }

        private static SnapshotSearchRow SearchOneRow(
            IReadOnlyList<SnapshotItemEntry> items, string search, AccountSnapshot snapshot = null)
        {
            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(items),
                new AccountItemIndex(items),
                search,
                null,
                null,
                null,
                SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(snapshot));
            return Assert.Single(rows);
        }

        private static SnapshotItemEntry Row(int itemId, int count, string source)
        {
            return new SnapshotItemEntry { ItemId = itemId, Count = count, Source = source };
        }

        private static SnapshotItemEntry Named(
            int itemId, string name, int count, string source)
        {
            var entry = Row(itemId, count, source);
            entry.Name = name;
            return entry;
        }

        private static SnapshotItemEntry Sockets(SnapshotItemEntry entry, List<int> upgrades)
        {
            entry.Upgrades = upgrades;
            return entry;
        }

        /// <summary>
        /// Stands in for the resolve pass that names every row after the
        /// capture, which is what the Snapshot tab searches on.
        /// </summary>
        private static void Name(IEnumerable<SnapshotItemEntry> items, int itemId, string name)
        {
            foreach (var entry in items)
            {
                if (entry.ItemId == itemId)
                {
                    entry.Name = name;
                }
            }
        }
    }
}
