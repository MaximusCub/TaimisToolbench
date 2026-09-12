using System;
using System.Collections.Generic;
using System.IO;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Gear a character is wearing and gear parked in one of its other
    /// saved equipment templates are two places, not one. The capture tells
    /// them apart on the wire's own "location" word, which
    /// EquipmentLocationPolicy reads; everything below here works from the
    /// source key that decision produces.
    /// </summary>
    public class EquipmentTemplatePlaceTests : IDisposable
    {
        private const string Divineaxe = "Divineaxe";

        private readonly string _tempDir;
        private readonly SnapshotStore _store;

        public EquipmentTemplatePlaceTests()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _store = new SnapshotStore(_tempDir);
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

        private static string Worn(string name) =>
            AccountItemIndex.CharacterEquipmentSourcePrefix + name;

        private static string Stored(string name) =>
            AccountItemIndex.CharacterTemplateSourcePrefix + name;

        private static string Bags(string name) =>
            AccountItemIndex.CharacterSourcePrefix + name;

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

        // Saves through the real store and reads back what a later session
        // would, so the line under test is built from a snapshot.json rather
        // than from objects the test still holds.
        private string LineFor(int itemId, List<SnapshotItemEntry> items, SnapshotSourceFilter filter = null)
        {
            _store.Save(new AccountSnapshot { CapturedAt = DateTime.UtcNow, Items = items });
            var loaded = _store.LoadLatest();
            Assert.NotNull(loaded);

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(loaded.Items),
                new AccountItemIndex(loaded.Items),
                "",
                filter ?? new SnapshotSourceFilter(),
                null);

            foreach (var row in rows)
            {
                if (row.ItemId == itemId)
                {
                    return SnapshotHoldLine.Format(row.Breakdown);
                }
            }

            return "";
        }

        // ---- What the wire word decides ----
        [Theory]
        [InlineData("Armory", true)]
        [InlineData("armory", true)]
        [InlineData("Equipped", false)]
        [InlineData("EquippedFromLegendaryArmory", false)]
        [InlineData("LegendaryArmory", false)]
        public void IsStoredInTemplate_ReadsTheLiteralLocation(string location, bool stored)
        {
            Assert.Equal(stored, EquipmentLocationPolicy.IsStoredInTemplate(location));
        }

        // A slot whose location the capture cannot read is not guessed at.
        // It is neither worn nor stored, and IsHeldByCharacter already keeps
        // it out of the snapshot, so no row claims a place for it.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("SomeFutureLocation")]
        public void UnreadableLocation_IsNeitherWornNorStored(string location)
        {
            Assert.False(EquipmentLocationPolicy.IsStoredInTemplate(location));
            Assert.False(EquipmentLocationPolicy.IsHeldByCharacter(location));
        }

        // ---- What the source key means ----
        [Fact]
        public void TemplateKey_NamesItsCharacterLikeTheOtherTwoEncodings()
        {
            Assert.True(AccountItemIndex.TryGetCharacterName(Stored(Divineaxe), out string name));
            Assert.Equal(Divineaxe, name);
        }

        [Fact]
        public void TemplateKey_IsATemplatePlaceAndNotAWornOne()
        {
            string stored = Stored(Divineaxe);
            string socketedInStored = AccountItemIndex.SocketedSource(101544, stored);

            Assert.True(AccountItemIndex.IsTemplateGearPlace(stored));
            Assert.True(AccountItemIndex.IsTemplateGearPlace(socketedInStored));
            Assert.False(AccountItemIndex.IsWornGearPlace(stored));
            Assert.False(AccountItemIndex.IsWornGearPlace(socketedInStored));

            Assert.False(AccountItemIndex.IsTemplateGearPlace(Worn(Divineaxe)));
            Assert.False(AccountItemIndex.IsTemplateGearPlace(Bags(Divineaxe)));
        }

        // EquippedRuneSetIndex counts pieces a character is wearing, and
        // reads IsEquipmentSource to find them. A spare set is not worn.
        [Fact]
        public void StoredGear_DoesNotCountTowardsTheWornRuneSet()
        {
            var items = new List<SnapshotItemEntry>
            {
                new SnapshotItemEntry
                {
                    ItemId = 200, Count = 1, Source = Worn(Divineaxe),
                    Upgrades = new List<int> { 24839 },
                },
                new SnapshotItemEntry
                {
                    ItemId = 201, Count = 1, Source = Stored(Divineaxe),
                    Upgrades = new List<int> { 24839 },
                },
            };

            var index = new EquippedRuneSetIndex(items);

            Assert.Equal(1, index.WornCopiesOf(Worn(Divineaxe), 24839));
            Assert.Equal(0, index.WornCopiesOf(Stored(Divineaxe), 24839));
        }

        // Bags first, then the spare set, then what is on the character's
        // back. Stripping a template costs the player nothing right now.
        [Fact]
        public void SpendOrder_TakesTheSpareSetBeforeTheWornOne()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 1, Worn(Divineaxe)),
                Entry(100, "Iron Ore", 1, Stored(Divineaxe)),
                Entry(100, "Iron Ore", 1, Bags(Divineaxe)),
            };

            var sources = AccountItemIndex.GetPrioritizedSources(
                100, new AccountItemIndex(items), Divineaxe);

            Assert.Equal(
                new[] { Bags(Divineaxe), Stored(Divineaxe), Worn(Divineaxe) },
                sources);
        }

        // ---- What the player reads ----
        [Fact]
        public void WornAndStored_ReadAsTwoDifferentPlaces()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(101955, "Relic of Zakiros", 1, Worn(Divineaxe)),
                Entry(101955, "Relic of Zakiros", 1, Stored(Divineaxe)),
            };

            string line = LineFor(101955, items);

            Assert.Contains("Equipped: Divineaxe", line);
            Assert.Contains("Equipment Templates: Divineaxe", line);
        }

        [Fact]
        public void StoredAlone_NeverReadsAsEquipped()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(99965, "Relic of the Flock", 1, Stored(Divineaxe)),
            };

            string line = LineFor(99965, items);

            Assert.Equal("Equipment Templates: Divineaxe", line);
        }

        // A rune socketed into a piece sitting in a spare template is still
        // named under that piece, and still not called equipped.
        [Fact]
        public void RuneSocketedIntoStoredGear_NamesThePieceUnderTheTemplateLabel()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(101544, "Obsidian Heavy Helmet", 1, Stored(Divineaxe)),
                Entry(24839, "Superior Rune of the Water", 1,
                    AccountItemIndex.SocketedSource(101544, Stored(Divineaxe))),
            };

            string line = LineFor(24839, items);

            Assert.Equal(
                "Equipment Templates: Divineaxe (in Obsidian Heavy Helmet)", line);
        }

        // The capture reports one entry per physical item and names every
        // tab it is reused in, so a set worn across three templates is one
        // row. The total follows the rows, and three rows would mean three
        // pieces.
        [Fact]
        public void OneRowPerPhysicalItem_TotalsOneHoweverManyTemplatesShareIt()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(101544, "Obsidian Heavy Helmet", 1, Stored(Divineaxe)),
            };

            var index = new AccountItemIndex(items);

            Assert.Equal(1, index.GetQuantity(101544, Stored(Divineaxe)));
            Assert.Single(index.GetSources(101544));
        }

        // A legendary the armory already counts makes no row of its own, and
        // the place it is drawn into is named under the armory instead. A
        // spare template is one such place, and calling it equipped was the
        // same mislabel the rows above fix.
        [Fact]
        public void LegendaryRuneInStoredGear_IsNamedUnderTheTemplateLabel()
        {
            const int LegendaryRune = 100000;
            const int Helm = 101544;

            var items = new List<SnapshotItemEntry>
            {
                Entry(LegendaryRune, "Legendary Rune", 6, AccountItemIndex.SourceLegendaryArmory),
                Entry(Helm, "Obsidian Heavy Helmet", 1, Stored(Divineaxe)),
            };
            SocketedItemRows.AddFor(
                items, Helm, Stored(Divineaxe), 1, new[] { LegendaryRune }, null);

            var snapshot = new AccountSnapshot { CapturedAt = DateTime.UtcNow, Items = items };
            SocketedItemRows.SettleArmoryOwned(
                snapshot.Items, snapshot.LegendaryArmoryEquipped,
                new HashSet<int> { LegendaryRune });

            _store.Save(snapshot);
            var loaded = _store.LoadLatest();

            // The armory's own count is unchanged by a template drawing on it.
            Assert.Equal(
                6,
                new AccountItemIndex(loaded.Items).GetQuantity(
                    LegendaryRune, AccountItemIndex.SourceLegendaryArmory));

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(loaded.Items),
                new AccountItemIndex(loaded.Items),
                "Legendary Rune",
                new SnapshotSourceFilter(),
                null,
                null,
                SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(loaded));

            Assert.Equal(
                "Legendary Armory - Equipment Templates: Divineaxe (in Obsidian Heavy Helmet)",
                SnapshotHoldLine.Format(Assert.Single(rows).Breakdown));
        }

        // ---- The filter ----
        [Fact]
        public void UncheckingEquipmentTemplates_HidesStoredGearAndNothingElse()
        {
            var filter = new SnapshotSourceFilter { EquipmentTemplates = false };

            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Stored(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(
                AccountItemIndex.SocketedSource(101544, Stored(Divineaxe)), filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(Worn(Divineaxe), filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(Bags(Divineaxe), filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(
                AccountItemIndex.SourceBank, filter));
        }

        [Fact]
        public void UncheckingTheCharacter_StillHidesThatCharactersTemplates()
        {
            var filter = new SnapshotSourceFilter();
            filter.UncheckedCharacters.Add(Divineaxe);

            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Stored(Divineaxe), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(Worn(Divineaxe), filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(Stored("Apoyu"), filter));
        }

        [Fact]
        public void UncheckedTemplates_DropTheStoredHalfOfARowAndKeepTheRest()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(101955, "Relic of Zakiros", 1, Worn(Divineaxe)),
                Entry(101955, "Relic of Zakiros", 1, Stored(Divineaxe)),
            };

            string line = LineFor(
                101955, items, new SnapshotSourceFilter { EquipmentTemplates = false });

            Assert.Equal("Equipped: Divineaxe", line);
        }

        // A snapshot written before the split carries stored gear under the
        // worn key, so it reads as equipped until the next refresh rewrites
        // every row. Nothing rejects it and nothing crashes on it.
        [Fact]
        public void SnapshotFromBeforeTheSplit_ReadsAsEquippedAndLoadsCleanly()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(99965, "Relic of the Flock", 1, Worn(Divineaxe)),
            };

            string line = LineFor(99965, items);

            Assert.Equal("Equipped: Divineaxe", line);
        }
    }
}
