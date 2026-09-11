using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class SnapshotSearchResultBuilderTests
    {
        private static SnapshotItemEntry Entry(
            int itemId, string name, int count, string source, string iconUrl = "", string rarity = "")
        {
            return new SnapshotItemEntry
            {
                ItemId = itemId,
                Name = name,
                Count = count,
                Source = source,
                IconUrl = iconUrl,
                Rarity = rarity,
            };
        }

        private static string CharSource(string name) => AccountItemIndex.CharacterSourcePrefix + name;

        private static string EquipSource(string name) => AccountItemIndex.CharacterEquipmentSourcePrefix + name;

        // The filter shape MainView hands the builder once the user
        // unchecks one or more per-character boxes: everything else stays
        // checked, and any character not named here is visible.
        private static SnapshotSourceFilter Unchecked(params string[] characterNames)
        {
            var filter = new SnapshotSourceFilter();
            foreach (var name in characterNames)
            {
                filter.UncheckedCharacters.Add(name);
            }

            return filter;
        }

        // Builds the itemId -> representative-entry map the way MainView
        // does once per snapshot (see SnapshotSearchResultBuilder.
        // BuildRepresentativeIndex) - every BuildItemRows test below feeds
        // its raw items list through this helper first, exactly like the
        // real caller, rather than passing the raw list to BuildItemRows
        // directly (BuildItemRows now takes the already-deduped map, not
        // the raw per-source entry list).
        private static IReadOnlyDictionary<int, SnapshotItemEntry> ItemsById(IReadOnlyList<SnapshotItemEntry> items) =>
            SnapshotSearchResultBuilder.BuildRepresentativeIndex(items);

        // ---- BuildRepresentativeIndex ----
        [Fact]
        public void BuildRepresentativeIndex_NullItems_ReturnsEmpty()
        {
            var result = SnapshotSearchResultBuilder.BuildRepresentativeIndex(null);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildRepresentativeIndex_EmptyItems_ReturnsEmpty()
        {
            var result = SnapshotSearchResultBuilder.BuildRepresentativeIndex(new List<SnapshotItemEntry>());

            Assert.Empty(result);
        }

        [Fact]
        public void BuildRepresentativeIndex_NullEntryInList_Skipped()
        {
            var items = new List<SnapshotItemEntry> { Entry(100, "Iron Ore", 5, AccountItemIndex.SourceBank), null };

            var result = SnapshotSearchResultBuilder.BuildRepresentativeIndex(items);

            Assert.Single(result);
            Assert.True(result.ContainsKey(100));
        }

        [Fact]
        public void BuildRepresentativeIndex_DuplicateItemId_FirstSeenEntryWins()
        {
            var first = Entry(100, "Iron Ore", 10, AccountItemIndex.SourceBank);
            var second = Entry(100, "Iron Ore", 7, AccountItemIndex.SourceMaterialStorage);
            var items = new List<SnapshotItemEntry> { first, second };

            var result = SnapshotSearchResultBuilder.BuildRepresentativeIndex(items);

            Assert.Single(result);
            Assert.Same(first, result[100]);
        }

        [Fact]
        public void BuildRepresentativeIndex_DistinctItemIds_OneEntryPerId()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, AccountItemIndex.SourceBank),
                Entry(2, "Linen Scrap", 5, AccountItemIndex.SourceBank),
            };

            var result = SnapshotSearchResultBuilder.BuildRepresentativeIndex(items);

            Assert.Equal(2, result.Count);
        }

        // ---- BuildItemRows ----
        [Fact]
        public void BuildItemRows_NullItemsById_ReturnsEmpty()
        {
            var index = new AccountItemIndex(null);
            var result = SnapshotSearchResultBuilder.BuildItemRows(null, index, "", new SnapshotSourceFilter(), null);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildItemRows_NullIndex_ReturnsEmpty()
        {
            var items = new List<SnapshotItemEntry> { Entry(1, "Iron Ore", 5, "Bank") };
            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), null, "", new SnapshotSourceFilter(), null);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildItemRows_EmptyItems_ReturnsEmpty()
        {
            var index = new AccountItemIndex(new List<SnapshotItemEntry>());
            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(new List<SnapshotItemEntry>()), index, "", new SnapshotSourceFilter(), null);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildItemRows_SingleItemSingleSource_TotalMatchesAndSingleBreakdownEntry()
        {
            var items = new List<SnapshotItemEntry> { Entry(100, "Iron Ore", 40, AccountItemIndex.SourceBank) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal(100, result[0].ItemId);
            Assert.Equal("Iron Ore", result[0].Name);
            Assert.Equal(40, result[0].TotalCount);
            Assert.Single(result[0].Breakdown);
            Assert.Equal(SnapshotHoldCategory.Bank, result[0].Breakdown[0].Category);
            Assert.Equal(40, result[0].Breakdown[0].Count);
        }

        [Fact]
        public void BuildItemRows_CarriesTheCapturedRarityOntoTheRow()
        {
            // The row is what the Snapshot tab draws a rarity frame and a
            // rarity-coloured name from. Dropping the field here is exactly
            // how the tab rendered a neutral frame for every item the
            // session's stat cache had never seen.
            var items = new List<SnapshotItemEntry>
            {
                Entry(24358, "Ancient Bone", 250, AccountItemIndex.SourceMaterialStorage, rarity: "Basic"),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Equal("Basic", Assert.Single(result).Rarity);
        }

        [Fact]
        public void BuildItemRows_ItemWithNoCapturedRarity_LeavesTheRowEmptyRatherThanNull()
        {
            // A row read out of a pre-rarity snapshot.json. The view resolves
            // "" through ItemRarityResolution, which reads it as unknown; a
            // null here would be indistinguishable but is not what the model
            // promises, and the view's fallback path is the one being kept
            // alive for exactly these rows.
            var items = new List<SnapshotItemEntry> { Entry(100, "Iron Ore", 40, AccountItemIndex.SourceBank) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Equal("", Assert.Single(result).Rarity);
            Assert.Null(ItemRarityResolution.Resolve(result[0].Rarity, null));
        }

        [Fact]
        public void BuildItemRows_ItemAcrossMultipleSources_TotalsAndBreaksDownEachSource()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 150, AccountItemIndex.SourceMaterialStorage),
                Entry(100, "Iron Ore", 100, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal(250, result[0].TotalCount);
            Assert.Equal(2, result[0].Breakdown.Count);
            // MaterialStorage outranks Bank in GetPrioritizedSources.
            Assert.Equal(SnapshotHoldCategory.MaterialStorage, result[0].Breakdown[0].Category);
            Assert.Equal(150, result[0].Breakdown[0].Count);
            Assert.Equal(SnapshotHoldCategory.Bank, result[0].Breakdown[1].Category);
            Assert.Equal(100, result[0].Breakdown[1].Count);
        }

        [Fact]
        public void BuildItemRows_SearchText_MatchesNameCaseInsensitiveSubstring()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, AccountItemIndex.SourceBank),
                Entry(2, "Linen Scrap", 5, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "iron", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal("Iron Ore", result[0].Name);
        }

        [Fact]
        public void BuildItemRows_SearchText_NoMatch_ReturnsEmpty()
        {
            var items = new List<SnapshotItemEntry> { Entry(1, "Iron Ore", 10, AccountItemIndex.SourceBank) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "linen", new SnapshotSourceFilter(), null);

            Assert.Empty(result);
        }

        // ---- BuildItemRows: character-label search ----
        [Fact]
        public void BuildItemRows_SearchText_MatchesCharacterNameCaseInsensitive()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, CharSource("Zaeed")),
                Entry(2, "Linen Scrap", 5, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "zaeed", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal("Iron Ore", result[0].Name);
        }

        [Fact]
        public void BuildItemRows_CharacterMatch_ReportsAccountWideTotalNotJustThatCharacter()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, CharSource("Zaeed")),
                Entry(1, "Iron Ore", 500, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "Zaeed", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal(510, result[0].TotalCount);
            Assert.Equal(2, result[0].Breakdown.Count);
            Assert.Contains(result[0].Breakdown, b => b.Category == SnapshotHoldCategory.Bags && b.CharacterName == "Zaeed");
        }

        [Fact]
        public void BuildItemRows_SearchText_DoesNotMatchTheCharacterEncodingPrefix()
        {
            // "Character:" is an internal encoding token, not part of any
            // character's name - searching it must not surface everything.
            var items = new List<SnapshotItemEntry> { Entry(1, "Iron Ore", 10, CharSource("Zaeed")) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "character", new SnapshotSourceFilter(), null);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildItemRows_SearchText_DoesNotMatchStorageLocationLabels()
        {
            var items = new List<SnapshotItemEntry> { Entry(1, "Iron Ore", 10, AccountItemIndex.SourceBank) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "bank", new SnapshotSourceFilter(), null);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildItemRows_CharacterSearch_HonorsThatCharactersUncheckedBox()
        {
            // AND-composition: an unchecked character stays hidden even when
            // its own name is what was typed.
            var items = new List<SnapshotItemEntry> { Entry(1, "Iron Ore", 10, CharSource("Zaeed")) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "Zaeed", Unchecked("Zaeed"), null);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildItemRows_CharacterSearch_UncheckedOtherCharacterStillDropsFromTheMatchedRow()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, CharSource("Zaeed")),
                Entry(1, "Iron Ore", 4, CharSource("Bob")),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "Zaeed", Unchecked("Bob"), null);

            Assert.Single(result);
            Assert.Equal(10, result[0].TotalCount);
            Assert.Single(result[0].Breakdown);
            Assert.Equal(SnapshotHoldCategory.Bags, result[0].Breakdown[0].Category);
            Assert.Equal("Zaeed", result[0].Breakdown[0].CharacterName);
        }

        [Fact]
        public void BuildItemRows_CharacterSearch_SurfacesEveryItemThatCharacterHolds()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, CharSource("Zaeed")),
                Entry(2, "Linen Scrap", 3, CharSource("Zaeed")),
                Entry(3, "Ancient Wood", 7, CharSource("Bob")),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "zae", new SnapshotSourceFilter(), null);

            Assert.Equal(2, result.Count);
            Assert.Equal("Iron Ore", result[0].Name);
            Assert.Equal("Linen Scrap", result[1].Name);
        }

        [Fact]
        public void BuildItemRows_CharacterSearch_ItemNameMatchStillWinsOnItsOwn()
        {
            // A name match needs no character behind it, and a row matched
            // both ways appears exactly once.
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Ore of Zaeed", 10, AccountItemIndex.SourceBank),
                Entry(2, "Iron Ore", 3, CharSource("Zaeed")),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "zaeed", new SnapshotSourceFilter(), null);

            Assert.Equal(2, result.Count);
            Assert.Equal("Iron Ore", result[0].Name);
            Assert.Equal("Ore of Zaeed", result[1].Name);
        }

        [Fact]
        public void BuildItemRows_OneCharacterQuery_MatchesItemNamesButNotCharacterHoldings()
        {
            // Minimum query length for character labels only: a single
            // letter must not surface everything a character whose name
            // contains it happens to hold, or the first keystrokes of an
            // item search widen the list instead of narrowing it. Item
            // names still match from the first letter.
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, CharSource("Aria")),
                Entry(2, "Ancient Wood", 7, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "a", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal("Ancient Wood", result[0].Name);
        }

        [Fact]
        public void BuildItemRows_TwoCharacterQuery_MatchesCharacterName()
        {
            // Exact boundary: two characters is the shortest query allowed
            // to match a character label, and the same item that stayed
            // hidden at one letter surfaces here.
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, CharSource("Aria")),
                Entry(2, "Ancient Wood", 7, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "ar", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal("Iron Ore", result[0].Name);
        }

        [Fact]
        public void BuildItemRows_PaddedOneCharacterQuery_StaysBelowTheCharacterMinimum()
        {
            // The length that counts is the trimmed one - surrounding
            // whitespace must not buy a one-letter query character matching.
            // The bank item is the control: it proves the padded query still
            // reaches item names, so an empty character result means the
            // minimum held rather than that the query matched nothing at all.
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, CharSource("Aria")),
                Entry(2, "Ancient Wood", 7, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "  a  ", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal("Ancient Wood", result[0].Name);
        }

        [Fact]
        public void BuildItemRows_PaddedTwoCharacterQuery_MatchesCharacterName()
        {
            // The padded counterpart of the boundary test: whitespace must
            // not cost a two-letter query its character matching either.
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Iron Ore", 10, CharSource("Aria")),
                Entry(2, "Ancient Wood", 7, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "  ar  ", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal("Iron Ore", result[0].Name);
        }

        [Fact]
        public void FilterWallet_CharacterName_LeavesCurrenciesUnaffected()
        {
            // Currencies have no per-character holding at all, so a
            // character-name search must not start listing them.
            var wallet = new List<SnapshotWalletEntry>
            {
                new SnapshotWalletEntry { CurrencyId = 2, CurrencyName = "Karma", Value = 100 },
            };

            Assert.Empty(SnapshotSearchResultBuilder.FilterWallet(wallet, "Zaeed"));
        }

        [Fact]
        public void BuildItemRows_SourceFilter_ExcludedSourceDropsFromBreakdownAndTotal()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 150, AccountItemIndex.SourceMaterialStorage),
                Entry(100, "Iron Ore", 100, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);
            var filter = new SnapshotSourceFilter { Bank = false };

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", filter, null);

            Assert.Single(result);
            Assert.Equal(150, result[0].TotalCount);
            Assert.Single(result[0].Breakdown);
            Assert.Equal(SnapshotHoldCategory.MaterialStorage, result[0].Breakdown[0].Category);
        }

        [Fact]
        public void BuildItemRows_SourceFilter_AllSourcesExcluded_ItemDropsEntirely()
        {
            var items = new List<SnapshotItemEntry> { Entry(100, "Iron Ore", 40, AccountItemIndex.SourceBank) };
            var index = new AccountItemIndex(items);
            var filter = new SnapshotSourceFilter { Bank = false, MaterialStorage = false, SharedInventory = false };

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", filter, null);

            Assert.Empty(result);
        }

        // ---- BuildItemRows: worn gear ----
        [Fact]
        public void BuildItemRows_BagsAndWornGear_AreSeparatePlacesOnOneCharacter()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Black Ice Band", 2, CharSource("Divineaxe")),
                Entry(100, "Black Ice Band", 1, EquipSource("Divineaxe")),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", new SnapshotSourceFilter(), "Divineaxe");

            Assert.Single(result);
            Assert.Equal(3, result[0].TotalCount);
            Assert.Equal(2, result[0].Breakdown.Count);
            Assert.Equal(SnapshotHoldCategory.Bags, result[0].Breakdown[0].Category);
            Assert.Equal("Divineaxe", result[0].Breakdown[0].CharacterName);
            Assert.Equal(SnapshotHoldCategory.Equipped, result[0].Breakdown[1].Category);
            Assert.Equal("Divineaxe", result[0].Breakdown[1].CharacterName);
        }

        [Fact]
        public void BuildItemRows_UncheckedCharacter_HidesItsBagsAndWornGearTogether()
        {
            // One checkbox per character covers both of that character's
            // source encodings.
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Black Ice Band", 2, CharSource("Alice")),
                Entry(100, "Black Ice Band", 1, EquipSource("Alice")),
                Entry(100, "Black Ice Band", 4, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", Unchecked("Alice"), null);

            Assert.Single(result);
            Assert.Equal(4, result[0].TotalCount);
            Assert.Single(result[0].Breakdown);
            Assert.Equal(SnapshotHoldCategory.Bank, result[0].Breakdown[0].Category);
        }

        [Fact]
        public void BuildItemRows_SearchText_MatchesACharacterNameInAWornGearSource()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Black Ice Band", 1, EquipSource("Divineaxe")),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "divine", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal(SnapshotHoldCategory.Equipped, result[0].Breakdown[0].Category);
        }

        [Fact]
        public void CollectCharacterNames_IncludesACharacterKnownOnlyFromWornGear()
        {
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    Entry(100, "Black Ice Band", 1, EquipSource("Divineaxe")),
                },
            };

            Assert.Equal(new[] { "Divineaxe" }, SnapshotSearchResultBuilder.CollectCharacterNames(snapshot));
        }

        [Fact]
        public void BuildItemRows_LegendaryArmory_IsItsOwnAccountWidePlace()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Bolt", 1, AccountItemIndex.SourceLegendaryArmory),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal(SnapshotHoldCategory.LegendaryArmory, result[0].Breakdown[0].Category);
            Assert.Equal("", result[0].Breakdown[0].CharacterName);
        }

        [Fact]
        public void BuildItemRows_LegendaryArmoryUnchecked_DropsThoseItems()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Bolt", 1, AccountItemIndex.SourceLegendaryArmory),
            };
            var index = new AccountItemIndex(items);
            var filter = new SnapshotSourceFilter { LegendaryArmory = false };

            Assert.Empty(SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", filter, null));
        }

        // ---- Who is wearing an armory item ----
        private static AccountSnapshot ArmorySnapshot(params (int ItemId, string Character)[] worn)
        {
            var snapshot = new AccountSnapshot();
            foreach (var entry in worn)
            {
                snapshot.LegendaryArmoryEquipped.Add(new SnapshotArmoryEquip
                {
                    ItemId = entry.ItemId,
                    CharacterName = entry.Character,
                });
            }

            return snapshot;
        }

        [Fact]
        public void BuildArmoryEquippedIndex_KeepsCaptureOrderAndNamesEachWearerOnce()
        {
            // One character wearing the same legendary in two slots is two
            // captured pairings and one name.
            var index = SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(
                ArmorySnapshot(
                    (100, "Divineaxe"), (100, "Apoyu"), (100, "Divineaxe"), (200, "Zoe")));

            Assert.Equal(
                new[] { "Divineaxe", "Apoyu" },
                index[100].Select(d => d.CharacterName).ToArray());
            Assert.Equal(
                new[] { "Zoe" }, index[200].Select(d => d.CharacterName).ToArray());
        }

        [Fact]
        public void BuildArmoryEquippedIndex_SkipsUnusablePairingsAndNeverReturnsNull()
        {
            Assert.Empty(SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(null));
            Assert.Empty(SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(new AccountSnapshot()));
            Assert.Empty(SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(
                ArmorySnapshot((0, "Divineaxe"), (100, ""), (100, null))));
        }

        [Fact]
        public void BuildItemRows_ArmoryRow_NamesTheCharactersWearingIt()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Bolt", 1, AccountItemIndex.SourceLegendaryArmory),
            };
            var index = new AccountItemIndex(items);
            var armory = SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(
                ArmorySnapshot((100, "Divineaxe"), (100, "Apoyu")));

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", new SnapshotSourceFilter(), null, null, armory);

            // Named, not counted: the account holds one copy however many
            // characters are drawing it.
            Assert.Equal(1, result[0].TotalCount);
            Assert.Equal(
                new[] { "Divineaxe", "Apoyu" }, result[0].Breakdown[0].EquippedBy);
            Assert.Equal(
                "Legendary Armory - Equipped: Divineaxe, Apoyu",
                SnapshotHoldLine.Format(result[0].Breakdown));
        }

        [Fact]
        public void BuildItemRows_OnlyTheArmoryPlaceCarriesWearers()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Bolt", 1, AccountItemIndex.SourceLegendaryArmory),
                Entry(100, "Bolt", 1, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);
            var armory = SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(
                ArmorySnapshot((100, "Divineaxe")));

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", new SnapshotSourceFilter(), null, null, armory);

            Assert.Equal(2, result[0].TotalCount);
            foreach (var place in result[0].Breakdown)
            {
                if (place.Category != SnapshotHoldCategory.LegendaryArmory)
                {
                    Assert.Null(place.EquippedBy);
                }
            }

            // Two places each holding one still print no counts; the
            // wearer clause is not a place and does not change that.
            Assert.Equal(
                "Bank  Legendary Armory - Equipped: Divineaxe",
                SnapshotHoldLine.Format(result[0].Breakdown));
        }

        [Fact]
        public void BuildItemRows_UncheckedCharacter_StopsNamingItUnderTheArmory()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Bolt", 1, AccountItemIndex.SourceLegendaryArmory),
            };
            var index = new AccountItemIndex(items);
            var armory = SnapshotSearchResultBuilder.BuildArmoryEquippedIndex(
                ArmorySnapshot((100, "Divineaxe"), (100, "Apoyu")));

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", Unchecked("Divineaxe"), null, null, armory);

            Assert.Equal(new[] { "Apoyu" }, result[0].Breakdown[0].EquippedBy);

            // Unchecking every wearer leaves the place itself, which is
            // account-wide and was never that character's to hide.
            var noneVisible = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", Unchecked("Divineaxe", "Apoyu"), null, null, armory);

            Assert.Null(noneVisible[0].Breakdown[0].EquippedBy);
            Assert.Equal("Legendary Armory", SnapshotHoldLine.Format(noneVisible[0].Breakdown));
        }

        [Fact]
        public void BuildItemRows_NoArmoryIndex_ReadsExactlyAsItDidBefore()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Bolt", 1, AccountItemIndex.SourceLegendaryArmory),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Null(result[0].Breakdown[0].EquippedBy);
            Assert.Equal("Legendary Armory", SnapshotHoldLine.Format(result[0].Breakdown));
        }

        // ---- BuildItemRows: per-character source filtering ----
        [Fact]
        public void BuildItemRows_UncheckedCharacter_HidesOnlyThatCharactersContribution()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 5, CharSource("Alice")),
                Entry(100, "Iron Ore", 3, CharSource("Bob")),
                Entry(100, "Iron Ore", 10, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);
            var filter = Unchecked("Alice");

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", filter, null);

            Assert.Single(result);
            Assert.Equal(13, result[0].TotalCount);
            Assert.DoesNotContain(result[0].Breakdown, b => b.Category == SnapshotHoldCategory.Bags && b.CharacterName == "Alice");
            Assert.Contains(result[0].Breakdown, b => b.Category == SnapshotHoldCategory.Bags && b.CharacterName == "Bob");
        }

        [Fact]
        public void BuildItemRows_CharacterAbsentFromUncheckedSet_DefaultsVisible()
        {
            // A character seen for the first time in a fresh snapshot is not
            // in the exclusion set, so it shows without anyone having to
            // seed a checked flag for it.
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 5, CharSource("Alice")),
                Entry(100, "Iron Ore", 7, CharSource("Newcomer")),
            };
            var index = new AccountItemIndex(items);
            var filter = Unchecked("Alice");

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", filter, null);

            Assert.Single(result);
            Assert.Equal(7, result[0].TotalCount);
            Assert.Single(result[0].Breakdown);
            Assert.Equal(SnapshotHoldCategory.Bags, result[0].Breakdown[0].Category);
            Assert.Equal("Newcomer", result[0].Breakdown[0].CharacterName);
        }

        [Fact]
        public void BuildItemRows_ItemHeldOnlyByUncheckedCharacter_DropsEntirely()
        {
            var items = new List<SnapshotItemEntry> { Entry(100, "Iron Ore", 5, CharSource("Alice")) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", Unchecked("Alice"), null);

            Assert.Empty(result);
        }

        [Fact]
        public void BuildItemRows_EveryCharacterUnchecked_LeavesStorageSourcesOnly()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 5, CharSource("Alice")),
                Entry(100, "Iron Ore", 3, CharSource("Bob")),
                Entry(100, "Iron Ore", 10, AccountItemIndex.SourceMaterialStorage),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", Unchecked("Alice", "Bob"), null);

            Assert.Single(result);
            Assert.Equal(10, result[0].TotalCount);
            Assert.Single(result[0].Breakdown);
            Assert.Equal(SnapshotHoldCategory.MaterialStorage, result[0].Breakdown[0].Category);
        }

        [Fact]
        public void BuildItemRows_UncheckedCharacterName_MatchedOrdinally()
        {
            // Character names come from the same snapshot strings the index
            // encodes its source keys from, so only an exact match hides a
            // character - a differently-cased name is a different character.
            var items = new List<SnapshotItemEntry> { Entry(100, "Iron Ore", 5, CharSource("Zaeed")) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", Unchecked("zaeed"), null);

            Assert.Single(result);
            Assert.Equal(5, result[0].TotalCount);
        }

        [Fact]
        public void BuildItemRows_NullSourceFilter_TreatedAsShowEverything()
        {
            var items = new List<SnapshotItemEntry> { Entry(100, "Iron Ore", 40, AccountItemIndex.SourceBank) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", null, null);

            Assert.Single(result);
            Assert.Equal(40, result[0].TotalCount);
        }

        [Fact]
        public void BuildItemRows_MultipleItems_SortedByNameOrdinalCaseInsensitive()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "zinc Ore", 1, AccountItemIndex.SourceBank),
                Entry(2, "Ancient Wood", 1, AccountItemIndex.SourceBank),
                Entry(3, "bronze Ingot", 1, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Equal(3, result.Count);
            Assert.Equal("Ancient Wood", result[0].Name);
            Assert.Equal("bronze Ingot", result[1].Name);
            Assert.Equal("zinc Ore", result[2].Name);
        }

        [Fact]
        public void BuildItemRows_SameNameDifferentItemIds_TieBrokenByItemIdAscending()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(200, "Recipe: Iron Ingot", 1, AccountItemIndex.SourceBank),
                Entry(100, "Recipe: Iron Ingot", 1, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Equal(2, result.Count);
            Assert.Equal(100, result[0].ItemId);
            Assert.Equal(200, result[1].ItemId);
        }

        [Fact]
        public void BuildItemRows_DuplicateEntriesSameItemIdAndSource_CountsSummed()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 10, AccountItemIndex.SourceBank),
                Entry(100, "Iron Ore", 7, AccountItemIndex.SourceBank),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal(17, result[0].TotalCount);
        }

        [Fact]
        public void BuildItemRows_BlankName_FallsBackToUnknownItem()
        {
            var items = new List<SnapshotItemEntry> { Entry(100, "", 5, AccountItemIndex.SourceBank) };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Single(result);
            Assert.Equal("Unknown Item", result[0].Name);
        }

        [Fact]
        public void BuildItemRows_NullEntryInList_SkippedViaRepresentativeIndex()
        {
            var items = new List<SnapshotItemEntry> { Entry(100, "Iron Ore", 5, AccountItemIndex.SourceBank), null };
            var index = new AccountItemIndex(items.Where(i => i != null).ToList());

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Single(result);
        }

        [Fact]
        public void BuildItemRows_ActiveCharacter_PrioritizedAheadOfSharedAndBank()
        {
            var items = new List<SnapshotItemEntry>
            {
                Entry(100, "Iron Ore", 1, AccountItemIndex.SourceBank),
                Entry(100, "Iron Ore", 2, AccountItemIndex.SourceSharedInventory),
                Entry(100, "Iron Ore", 3, CharSource("Zaeed")),
            };
            var index = new AccountItemIndex(items);

            var result = SnapshotSearchResultBuilder.BuildItemRows(ItemsById(items), index, "", new SnapshotSourceFilter(), "Zaeed");

            Assert.Single(result);
            Assert.Equal(SnapshotHoldCategory.Bags, result[0].Breakdown[0].Category);
            Assert.Equal("Zaeed", result[0].Breakdown[0].CharacterName);
            Assert.Equal(SnapshotHoldCategory.SharedInventory, result[0].Breakdown[1].Category);
            Assert.Equal(SnapshotHoldCategory.Bank, result[0].Breakdown[2].Category);
        }

        // ---- FilterWallet ----
        [Fact]
        public void FilterWallet_Null_ReturnsEmpty()
        {
            Assert.Empty(SnapshotSearchResultBuilder.FilterWallet(null, ""));
        }

        [Fact]
        public void FilterWallet_EmptySearch_ReturnsAllEntries()
        {
            var wallet = new List<SnapshotWalletEntry>
            {
                new SnapshotWalletEntry { CurrencyId = 2, CurrencyName = "Karma", Value = 100 },
                new SnapshotWalletEntry { CurrencyId = 3, CurrencyName = "Gems", Value = 5 },
            };

            var result = SnapshotSearchResultBuilder.FilterWallet(wallet, "");

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void FilterWallet_SearchText_MatchesCurrencyNameCaseInsensitive()
        {
            var wallet = new List<SnapshotWalletEntry>
            {
                new SnapshotWalletEntry { CurrencyId = 2, CurrencyName = "Karma", Value = 100 },
                new SnapshotWalletEntry { CurrencyId = 3, CurrencyName = "Gems", Value = 5 },
            };

            var result = SnapshotSearchResultBuilder.FilterWallet(wallet, "KARMA");

            Assert.Single(result);
            Assert.Equal("Karma", result[0].CurrencyName);
        }

        [Fact]
        public void FilterWallet_NullEntriesInList_Skipped()
        {
            var wallet = new List<SnapshotWalletEntry>
            {
                new SnapshotWalletEntry { CurrencyId = 2, CurrencyName = "Karma", Value = 100 },
                null,
            };

            var result = SnapshotSearchResultBuilder.FilterWallet(wallet, "");

            Assert.Single(result);
        }

        [Fact]
        public void FilterWallet_NoMatch_ReturnsEmpty()
        {
            var wallet = new List<SnapshotWalletEntry>
            {
                new SnapshotWalletEntry { CurrencyId = 2, CurrencyName = "Karma", Value = 100 },
            };

            var result = SnapshotSearchResultBuilder.FilterWallet(wallet, "gems");

            Assert.Empty(result);
        }

        // ---- IsSourceEnabled ----
        [Fact]
        public void IsSourceEnabled_NullFilter_AlwaysTrue()
        {
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(AccountItemIndex.SourceBank, null));
        }

        [Fact]
        public void IsSourceEnabled_NullOrEmptySource_False()
        {
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(null, new SnapshotSourceFilter()));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled("", new SnapshotSourceFilter()));
        }

        [Fact]
        public void IsSourceEnabled_KnownSources_RespectEachFlagIndependently()
        {
            var filter = new SnapshotSourceFilter { Bank = false, MaterialStorage = true, SharedInventory = false };
            filter.UncheckedCharacters.Add("Bob");

            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(AccountItemIndex.SourceBank, filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(AccountItemIndex.SourceMaterialStorage, filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(AccountItemIndex.SourceSharedInventory, filter));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("Zaeed"), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("Bob"), filter));
        }

        // Pins the in-place name comparison (no Substring) against the
        // off-by-one and comparer mistakes an exact hit/miss pair cannot
        // catch: only the whole name after the prefix counts, and only an
        // ordinal match of it.
        [Fact]
        public void IsSourceEnabled_ExcludedCharacterMatchIsWholeNameAndOrdinal()
        {
            var filter = new SnapshotSourceFilter();
            filter.UncheckedCharacters.Add("Bo");
            filter.UncheckedCharacters.Add("Bobby");
            filter.UncheckedCharacters.Add("bob");

            // A strict prefix of the name, a strict extension of it, and a
            // case-only variant are all different characters.
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("Bob"), filter));

            // The same set still hides each of them under its own name.
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("Bo"), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("Bobby"), filter));
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("bob"), filter));

            // An excluded name is matched against the name half only, never
            // against the prefix or the whole raw source.
            var prefixFilter = new SnapshotSourceFilter();
            prefixFilter.UncheckedCharacters.Add(CharSource("Bob"));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("Bob"), prefixFilter));
        }

        // Degenerate but reachable shape: a zero-length name half. The
        // empty-string entry must match it and a non-empty one must not.
        [Fact]
        public void IsSourceEnabled_EmptyCharacterName_MatchesOnlyTheEmptyExclusion()
        {
            var emptyExcluded = new SnapshotSourceFilter();
            emptyExcluded.UncheckedCharacters.Add("");
            Assert.False(SnapshotSearchResultBuilder.IsSourceEnabled(AccountItemIndex.CharacterSourcePrefix, emptyExcluded));

            var otherExcluded = new SnapshotSourceFilter();
            otherExcluded.UncheckedCharacters.Add("Bob");
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(AccountItemIndex.CharacterSourcePrefix, otherExcluded));
            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("Bob"), emptyExcluded));
        }

        [Fact]
        public void IsSourceEnabled_NullUncheckedCharacterSet_TreatedAsEveryCharacterChecked()
        {
            var filter = new SnapshotSourceFilter { UncheckedCharacters = null };

            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled(CharSource("Zaeed"), filter));
        }

        [Fact]
        public void IsSourceEnabled_UnknownSourceShape_FailsOpenAndIsAlwaysTrue()
        {
            var filter = new SnapshotSourceFilter { Bank = false, MaterialStorage = false, SharedInventory = false };

            Assert.True(SnapshotSearchResultBuilder.IsSourceEnabled("SomeFutureSource", filter));
        }

        // ---- CollectCharacterNames ----
        [Fact]
        public void CollectCharacterNames_NullSnapshot_ReturnsEmpty()
        {
            Assert.Empty(SnapshotSearchResultBuilder.CollectCharacterNames(null));
        }

        [Fact]
        public void CollectCharacterNames_EmptySnapshot_ReturnsEmpty()
        {
            Assert.Empty(SnapshotSearchResultBuilder.CollectCharacterNames(new AccountSnapshot()));
        }

        [Fact]
        public void CollectCharacterNames_StorageSourcesIgnored_CharacterSourcesDeduped()
        {
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    Entry(1, "Iron Ore", 5, AccountItemIndex.SourceBank),
                    Entry(1, "Iron Ore", 5, AccountItemIndex.SourceMaterialStorage),
                    Entry(1, "Iron Ore", 5, CharSource("Zaeed")),
                    Entry(2, "Linen Scrap", 5, CharSource("Zaeed")),
                },
            };

            var result = SnapshotSearchResultBuilder.CollectCharacterNames(snapshot);

            Assert.Equal(new[] { "Zaeed" }, result);
        }

        [Fact]
        public void CollectCharacterNames_CharacterWithNoItems_StillListedFromDisciplines()
        {
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry> { Entry(1, "Iron Ore", 5, CharSource("Alice")) },
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Emptyhands", Discipline = "Chef", Rating = 400 },
                    new SnapshotCharacterDiscipline { CharacterName = "Alice", Discipline = "Armorsmith", Rating = 500 },
                },
            };

            var result = SnapshotSearchResultBuilder.CollectCharacterNames(snapshot);

            Assert.Equal(new[] { "Alice", "Emptyhands" }, result);
        }

        [Fact]
        public void CollectCharacterNames_ZeroCountEntry_StillListsTheCharacter()
        {
            // AccountItemIndex drops zero-count entries; the roster does not
            // - an empty character still gets its own checkbox.
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry> { Entry(1, "Iron Ore", 0, CharSource("Emptyhands")) },
            };

            var result = SnapshotSearchResultBuilder.CollectCharacterNames(snapshot);

            Assert.Equal(new[] { "Emptyhands" }, result);
        }

        [Fact]
        public void CollectCharacterNames_SortedCaseInsensitiveWithOrdinalTiebreak()
        {
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    Entry(1, "Iron Ore", 1, CharSource("zara")),
                    Entry(1, "Iron Ore", 1, CharSource("Alice")),
                    Entry(1, "Iron Ore", 1, CharSource("Zara")),
                    Entry(1, "Iron Ore", 1, CharSource("bob")),
                },
            };

            var result = SnapshotSearchResultBuilder.CollectCharacterNames(snapshot);

            Assert.Equal(new[] { "Alice", "bob", "Zara", "zara" }, result);
        }

        [Fact]
        public void CollectCharacterNames_NullAndBlankEntriesSkipped()
        {
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    null,
                    Entry(1, "Iron Ore", 1, null),
                    Entry(1, "Iron Ore", 1, AccountItemIndex.CharacterSourcePrefix),
                    Entry(1, "Iron Ore", 1, CharSource("Alice")),
                },
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                {
                    null,
                    new SnapshotCharacterDiscipline { CharacterName = "", Discipline = "Chef" },
                },
            };

            var result = SnapshotSearchResultBuilder.CollectCharacterNames(snapshot);

            Assert.Equal(new[] { "Alice" }, result);
        }

        // --- The one-letter empty-state hint ---
        //
        // The hint exists because BuildItemRows' own character-name matching
        // is held back below two characters (MinCharacterSearchLength), and
        // that hold-back is invisible: the list simply comes back empty.
        // These cases pin the pairing - the hint appears exactly where a
        // second keystroke really would change the result, and nowhere else.
        private static readonly List<string> Roster =
            new List<string> { "Zaeed Massani", "Ylva", "Bob" };

        [Fact]
        public void ShortQueryCharacterHint_OneLetterThatARosterNameCarries_IsHinted()
        {
            Assert.Equal(
                SnapshotSearchResultBuilder.ShortQueryCharacterHintText,
                SnapshotSearchResultBuilder.ShortQueryCharacterHint("y", Roster));

            // The very case the hint is for: the same one-letter query is
            // ignored by the character half of the real search path, so
            // without the hint an empty list is all the user sees.
            var index = new AccountItemIndex(new List<SnapshotItemEntry>
            {
                Entry(1, "Copper Ore", 5, CharSource("Ylva")),
            });
            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(new List<SnapshotItemEntry>
                {
                    Entry(1, "Copper Ore", 5, CharSource("Ylva")),
                }),
                index, "y", null, null);
            Assert.Empty(rows);

            // ...and two letters do surface it, which is what the hint
            // promises.
            var twoLetters = SnapshotSearchResultBuilder.BuildItemRows(
                SnapshotSearchResultBuilder.BuildRepresentativeIndex(new List<SnapshotItemEntry>
                {
                    Entry(1, "Copper Ore", 5, CharSource("Ylva")),
                }),
                index, "yl", null, null);
            Assert.Single(twoLetters);
        }

        [Fact]
        public void ShortQueryCharacterHint_MatchIsCaseInsensitiveAndMidName()
        {
            Assert.NotNull(SnapshotSearchResultBuilder.ShortQueryCharacterHint("Z", Roster));
            Assert.NotNull(SnapshotSearchResultBuilder.ShortQueryCharacterHint("s", Roster));
            Assert.NotNull(SnapshotSearchResultBuilder.ShortQueryCharacterHint(" b ", Roster));
        }

        [Fact]
        public void ShortQueryCharacterHint_OneLetterNoRosterNameCarriesIt_IsSilent()
        {
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint("q", Roster));
        }

        [Fact]
        public void ShortQueryCharacterHint_QueryAtOrPastTheMinimum_IsSilent()
        {
            // Two letters already search character names, so an empty list
            // there is a genuine no-results and the hint would be a lie.
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint("yl", Roster));
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint("ylva", Roster));
        }

        [Fact]
        public void ShortQueryCharacterHint_BlankOrNullQuery_IsSilent()
        {
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint(null, Roster));
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint("", Roster));
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint("   ", Roster));
        }

        [Fact]
        public void ShortQueryCharacterHint_NoRosterAtAll_IsSilent()
        {
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint("y", null));
            Assert.Null(SnapshotSearchResultBuilder.ShortQueryCharacterHint("y", new List<string>()));
            Assert.Null(
                SnapshotSearchResultBuilder.ShortQueryCharacterHint("y", new List<string> { null, "" }));
        }

        [Fact]
        public void ShortQueryCharacterHint_OnlyMatchingCharacterIsUnchecked_IsSilent()
        {
            // Another letter would still not surface that character, so the
            // hint must not offer it. The other roster names do not carry
            // the letter, which is what isolates the exclusion.
            var unchecked_ = new HashSet<string>(StringComparer.Ordinal) { "Ylva" };

            Assert.Null(
                SnapshotSearchResultBuilder.ShortQueryCharacterHint("y", Roster, unchecked_));

            // A different unchecked character leaves the match standing.
            Assert.NotNull(
                SnapshotSearchResultBuilder.ShortQueryCharacterHint(
                    "y", Roster, new HashSet<string>(StringComparer.Ordinal) { "Bob" }));
        }

        // ---- The "N of M items" line's two halves ----
        [Fact]
        public void UnfilteredRowCountEqualsDistinctItemCount()
        {
            // MainView builds both from one snapshot and prints them as
            // "Showing N of M items". A zero-count entry used to raise M
            // through the representative map while producing no row, so the
            // line read "1203 of 1204" with no filter to relax.
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Held", 5, AccountItemIndex.SourceBank),
                Entry(2, "Also held", 2, CharSource("Alice")),
                Entry(3, "Empty stack", 0, AccountItemIndex.SourceBank),
                Entry(4, "No source", 7, ""),
            };
            var index = new AccountItemIndex(items);

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", new SnapshotSourceFilter(), null);

            Assert.Equal(2, rows.Count);
            Assert.Equal(rows.Count, index.DistinctItemCount);
        }

        [Fact]
        public void ASourceFilterMakesRowsFewerThanDistinctItemCount()
        {
            // The genuine "N of M" case: M stays the account total and N
            // drops because the user unchecked a source.
            var items = new List<SnapshotItemEntry>
            {
                Entry(1, "Banked", 5, AccountItemIndex.SourceBank),
                Entry(2, "On Alice", 2, CharSource("Alice")),
            };
            var index = new AccountItemIndex(items);

            var rows = SnapshotSearchResultBuilder.BuildItemRows(
                ItemsById(items), index, "", Unchecked("Alice"), null);

            Assert.Single(rows);
            Assert.Equal(2, index.DistinctItemCount);
        }
    }
}
