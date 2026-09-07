using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class AccountItemIndexTests
    {
        private static SnapshotItemEntry Entry(int itemId, int count, string source)
        {
            return new SnapshotItemEntry
            {
                ItemId = itemId,
                Count = count,
                Source = source,
            };
        }

        // Character sources must use the same encodings production writes
        // (Gw2AccountSnapshotService): bags and worn gear each get their own.
        private static string CharSource(string name)
        {
            return AccountItemIndex.CharacterSourcePrefix + name;
        }

        private static string EquipSource(string name)
        {
            return AccountItemIndex.CharacterEquipmentSourcePrefix + name;
        }

        [Fact]
        public void EmptyItems_AllQueriesReturnZero()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>());

            Assert.Equal(0, index.GetQuantity(1, AccountItemIndex.SourceBank));
            Assert.Empty(index.GetSources(1));
        }

        [Fact]
        public void NullItems_AllQueriesReturnZero()
        {
            var index = new AccountItemIndex(null);

            Assert.Equal(0, index.GetQuantity(1, AccountItemIndex.SourceBank));
            Assert.Empty(index.GetSources(1));
        }

        [Fact]
        public void SingleSourceSingleItem_CorrectQuantity()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 25, AccountItemIndex.SourceMaterialStorage),
            });

            Assert.Equal(25, index.GetQuantity(100, AccountItemIndex.SourceMaterialStorage));
            Assert.Equal(0, index.GetQuantity(100, AccountItemIndex.SourceBank));
            Assert.Equal(0, index.GetQuantity(999, AccountItemIndex.SourceMaterialStorage));
        }

        [Fact]
        public void MultipleSourcesSameItem_EachSourceCorrect()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 10, AccountItemIndex.SourceMaterialStorage),
                Entry(100, 5, AccountItemIndex.SourceBank),
                Entry(100, 3, CharSource("Alice")),
            });

            Assert.Equal(10, index.GetQuantity(100, AccountItemIndex.SourceMaterialStorage));
            Assert.Equal(5, index.GetQuantity(100, AccountItemIndex.SourceBank));
            Assert.Equal(3, index.GetQuantity(100, CharSource("Alice")));
        }

        [Fact]
        public void DuplicateEntries_QuantitiesSummed()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 10, AccountItemIndex.SourceBank),
                Entry(100, 7, AccountItemIndex.SourceBank),
            });

            Assert.Equal(17, index.GetQuantity(100, AccountItemIndex.SourceBank));
        }

        [Fact]
        public void GetSources_ReturnsAllSourcesForItem()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 10, AccountItemIndex.SourceMaterialStorage),
                Entry(100, 5, AccountItemIndex.SourceBank),
                Entry(200, 3, AccountItemIndex.SourceSharedInventory),
            });

            var sources100 = index.GetSources(100);
            Assert.Equal(2, sources100.Count);
            Assert.Contains(AccountItemIndex.SourceMaterialStorage, sources100);
            Assert.Contains(AccountItemIndex.SourceBank, sources100);

            var sources200 = index.GetSources(200);
            Assert.Single(sources200);
            Assert.Equal(AccountItemIndex.SourceSharedInventory, sources200[0]);
        }

        [Fact]
        public void GetSources_UnknownItem_ReturnsEmpty()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 10, AccountItemIndex.SourceBank),
            });

            Assert.Empty(index.GetSources(999));
        }

        [Fact]
        public void GetQuantity_NullSource_ReturnsZero()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 10, AccountItemIndex.SourceBank),
            });

            Assert.Equal(0, index.GetQuantity(100, null));
        }

        [Fact]
        public void ZeroCountEntries_Excluded()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 0, AccountItemIndex.SourceBank),
                Entry(100, 5, AccountItemIndex.SourceMaterialStorage),
            });

            Assert.Equal(0, index.GetQuantity(100, AccountItemIndex.SourceBank));
            Assert.Equal(5, index.GetQuantity(100, AccountItemIndex.SourceMaterialStorage));
            var sources = index.GetSources(100);
            Assert.Single(sources);
            Assert.Equal(AccountItemIndex.SourceMaterialStorage, sources[0]);
        }

        [Fact]
        public void GetPrioritizedSources_RespectsPriorityOrder()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 1, AccountItemIndex.SourceBank),
                Entry(100, 2, AccountItemIndex.SourceSharedInventory),
                Entry(100, 3, AccountItemIndex.SourceMaterialStorage),
                Entry(100, 4, CharSource("Alice")),
            });

            var prioritized = AccountItemIndex.GetPrioritizedSources(
                100, index, "Alice");

            Assert.Equal(4, prioritized.Count);
            Assert.Equal(AccountItemIndex.SourceMaterialStorage, prioritized[0]);
            Assert.Equal(CharSource("Alice"), prioritized[1]);
            Assert.Equal(AccountItemIndex.SourceSharedInventory, prioritized[2]);
            Assert.Equal(AccountItemIndex.SourceBank, prioritized[3]);
        }

        [Fact]
        public void GetPrioritizedSources_ActiveCharBagsBeforeOwnWornGear()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 1, AccountItemIndex.SourceBank),
                Entry(100, 2, EquipSource("Alice")),
                Entry(100, 3, CharSource("Alice")),
            });

            var prioritized = AccountItemIndex.GetPrioritizedSources(
                100, index, "Alice");

            Assert.Equal(3, prioritized.Count);
            Assert.Equal(CharSource("Alice"), prioritized[0]);
            Assert.Equal(EquipSource("Alice"), prioritized[1]);
            Assert.Equal(AccountItemIndex.SourceBank, prioritized[2]);
        }

        [Fact]
        public void GetPrioritizedSources_LegendaryArmoryRanksAfterBank()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 1, AccountItemIndex.SourceLegendaryArmory),
                Entry(100, 2, AccountItemIndex.SourceBank),
                Entry(100, 3, CharSource("Zed")),
            });

            var prioritized = AccountItemIndex.GetPrioritizedSources(100, index, null);

            Assert.Equal(3, prioritized.Count);
            Assert.Equal(AccountItemIndex.SourceBank, prioritized[0]);
            Assert.Equal(AccountItemIndex.SourceLegendaryArmory, prioritized[1]);
            Assert.Equal(CharSource("Zed"), prioritized[2]);
        }

        [Fact]
        public void CharacterNameOffset_ReadsBothCharacterEncodingsAndNothingElse()
        {
            Assert.Equal("Alice", NameAt(CharSource("Alice")));
            Assert.Equal("Alice", NameAt(EquipSource("Alice")));

            Assert.Equal(-1, AccountItemIndex.CharacterNameOffset(AccountItemIndex.SourceBank));
            Assert.Equal(-1, AccountItemIndex.CharacterNameOffset(null));

            Assert.True(AccountItemIndex.IsEquipmentSource(EquipSource("Alice")));
            Assert.False(AccountItemIndex.IsEquipmentSource(CharSource("Alice")));
        }

        private static string NameAt(string source)
        {
            Assert.True(AccountItemIndex.TryGetCharacterName(source, out string name));
            return name;
        }

        [Fact]
        public void GetPrioritizedSources_NullActiveChar_SkipsCharPriority()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 1, AccountItemIndex.SourceBank),
                Entry(100, 2, AccountItemIndex.SourceMaterialStorage),
                Entry(100, 3, CharSource("Bob")),
            });

            var prioritized = AccountItemIndex.GetPrioritizedSources(
                100, index, null);

            Assert.Equal(3, prioritized.Count);
            Assert.Equal(AccountItemIndex.SourceMaterialStorage, prioritized[0]);
            Assert.Equal(AccountItemIndex.SourceBank, prioritized[1]);
            Assert.Equal(CharSource("Bob"), prioritized[2]);
        }

        [Fact]
        public void GetPrioritizedSources_OtherChars_SortedAlphabetically()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 1, CharSource("Charlie")),
                Entry(100, 2, CharSource("Alice")),
                Entry(100, 3, CharSource("Bob")),
            });

            var prioritized = AccountItemIndex.GetPrioritizedSources(
                100, index, null);

            Assert.Equal(3, prioritized.Count);
            Assert.Equal(CharSource("Alice"), prioritized[0]);
            Assert.Equal(CharSource("Bob"), prioritized[1]);
            Assert.Equal(CharSource("Charlie"), prioritized[2]);
        }

        [Fact]
        public void GetPrioritizedSources_UnknownItem_ReturnsEmpty()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>());

            var prioritized = AccountItemIndex.GetPrioritizedSources(
                999, index, "Alice");

            Assert.Empty(prioritized);
        }

        [Fact]
        public void GetPrioritizedSources_ActiveCharNotInSources_Skipped()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 5, AccountItemIndex.SourceBank),
                Entry(100, 3, AccountItemIndex.SourceMaterialStorage),
            });

            var prioritized = AccountItemIndex.GetPrioritizedSources(
                100, index, "NonexistentChar");

            Assert.Equal(2, prioritized.Count);
            Assert.Equal(AccountItemIndex.SourceMaterialStorage, prioritized[0]);
            Assert.Equal(AccountItemIndex.SourceBank, prioritized[1]);
        }

        [Fact]
        public void NullSource_Excluded()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 5, null),
                Entry(100, 3, AccountItemIndex.SourceBank),
            });

            Assert.Equal(3, index.GetQuantity(100, AccountItemIndex.SourceBank));
            var sources = index.GetSources(100);
            Assert.Single(sources);
            Assert.Equal(AccountItemIndex.SourceBank, sources[0]);
        }

        [Fact]
        public void EmptySource_Excluded()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 5, ""),
                Entry(100, 3, AccountItemIndex.SourceBank),
            });

            Assert.Equal(3, index.GetQuantity(100, AccountItemIndex.SourceBank));
            Assert.Equal(0, index.GetQuantity(100, ""));
            var sources = index.GetSources(100);
            Assert.Single(sources);
        }

        [Fact]
        public void WhitespaceSource_Excluded()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 5, "  "),
                Entry(100, 3, AccountItemIndex.SourceBank),
            });

            Assert.Equal(3, index.GetQuantity(100, AccountItemIndex.SourceBank));
            var sources = index.GetSources(100);
            Assert.Single(sources);
            Assert.Equal(AccountItemIndex.SourceBank, sources[0]);
        }

        [Fact]
        public void GetSources_ReturnsDeterministicOrder()
        {
            // Insert sources in non-alphabetical order
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 1, CharSource("Charlie")),
                Entry(100, 2, CharSource("Alice")),
                Entry(100, 3, AccountItemIndex.SourceBank),
            });

            var sources1 = index.GetSources(100);
            var sources2 = index.GetSources(100);

            Assert.Equal(3, sources1.Count);
            Assert.Equal(sources1, sources2);
            // Ordinal sorted: Bank < Character:Alice < Character:Charlie
            Assert.Equal(AccountItemIndex.SourceBank, sources1[0]);
            Assert.Equal(CharSource("Alice"), sources1[1]);
            Assert.Equal(CharSource("Charlie"), sources1[2]);
        }

        [Fact]
        public void GetPrioritizedSources_AllSourceTypes_FullPriorityChain()
        {
            // Every source type present: MaterialStorage, active char, SharedInventory,
            // Bank, plus two other characters
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 1, CharSource("Zara")),
                Entry(100, 2, AccountItemIndex.SourceBank),
                Entry(100, 3, AccountItemIndex.SourceSharedInventory),
                Entry(100, 4, AccountItemIndex.SourceMaterialStorage),
                Entry(100, 5, CharSource("ActiveHero")),
                Entry(100, 6, CharSource("Alice")),
            });

            var prioritized = AccountItemIndex.GetPrioritizedSources(
                100, index, "ActiveHero");

            Assert.Equal(6, prioritized.Count);
            Assert.Equal(AccountItemIndex.SourceMaterialStorage, prioritized[0]);
            Assert.Equal(CharSource("ActiveHero"), prioritized[1]);
            Assert.Equal(AccountItemIndex.SourceSharedInventory, prioritized[2]);
            Assert.Equal(AccountItemIndex.SourceBank, prioritized[3]);
            Assert.Equal(CharSource("Alice"), prioritized[4]);
            Assert.Equal(CharSource("Zara"), prioritized[5]);
        }

        [Fact]
        public void SourceConstants_MatchExpectedValues()
        {
            Assert.Equal("MaterialStorage", AccountItemIndex.SourceMaterialStorage);
            Assert.Equal("SharedInventory", AccountItemIndex.SourceSharedInventory);
            Assert.Equal("Bank", AccountItemIndex.SourceBank);
            Assert.Equal("Character:", AccountItemIndex.CharacterSourcePrefix);
        }

        [Fact]
        public void GetPrioritizedSources_BareNameSource_NotTreatedAsActiveCharacter()
        {
            // Regression guard: sources must be matched in the production
            // "Character:<name>" encoding. A bare-name source (which production
            // never writes) must not be promoted to the active-character slot.
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(100, 1, AccountItemIndex.SourceBank),
                Entry(100, 2, "Alice"),
            });

            var prioritized = AccountItemIndex.GetPrioritizedSources(
                100, index, "Alice");

            Assert.Equal(2, prioritized.Count);
            Assert.Equal(AccountItemIndex.SourceBank, prioritized[0]);
            Assert.Equal("Alice", prioritized[1]);
        }
    }
}
