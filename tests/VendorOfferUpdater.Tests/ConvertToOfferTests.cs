using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using VendorOfferUpdater;
using VendorOfferUpdater.Models;
using VendorOfferUpdater.Tests.Helpers;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    public class ConvertToOfferTests
    {
        private static async Task<(Gw2ApiHelper helper, HttpClient httpClient)> CreateLoadedHelper()
        {
            var handler = new FakeHttpHandler();
            handler.MapUrl(
                url => url.Contains("/v2/currencies") && !url.Contains("ids="),
                "[2,23]");
            handler.MapUrl(
                url => url.Contains("/v2/currencies?ids="),
                "[{\"id\":2,\"name\":\"Karma\"},{\"id\":23,\"name\":\"Spirit Shard\"}]");

            var httpClient = new HttpClient(handler);
            var helper = new Gw2ApiHelper(httpClient);
            await helper.LoadCurrenciesAsync();
            return (helper, httpClient);
        }

        private static WikiVendorResult MakeResult(
            int gameId = 19685,
            string merchantName = "Merchant",
            int? outputQuantity = 1,
            List<WikiCostEntry> costEntries = null,
            List<string> locations = null,
            int? dailyCap = null,
            int? weeklyCap = null,
            int? seasonalCap = null,
            string requirement = null,
            string temporarySeasonalValue = null)
        {
            return new WikiVendorResult
            {
                GameId = gameId,
                MerchantName = merchantName,
                OutputQuantity = outputQuantity,
                CostEntries = costEntries ?? new List<WikiCostEntry>(),
                Locations = locations ?? new List<string>(),
                DailyCap = dailyCap,
                WeeklyCap = weeklyCap,
                SeasonalCap = seasonalCap,
                Requirement = requirement,
                TemporarySeasonalValue = temporarySeasonalValue,
            };
        }

        [Fact]
        public async Task CurrencyCost_ResolvedToCurrencyLine()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(costEntries: new List<WikiCostEntry>
            {
                new WikiCostEntry { Value = 500, Currency = "Karma" },
            });

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Single(offer.CostLines);
            Assert.Equal("Currency", offer.CostLines[0].Type);
            Assert.Equal(2, offer.CostLines[0].Id);
            Assert.Equal(500, offer.CostLines[0].Count);
        }

        [Fact]
        public async Task CoinAlias_ResolvedToCurrencyId1()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(costEntries: new List<WikiCostEntry>
            {
                new WikiCostEntry { Value = 10000, Currency = "Coin" },
            });

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal("Currency", offer.CostLines[0].Type);
            Assert.Equal(Gw2Constants.CoinCurrencyId, offer.CostLines[0].Id);
        }

        [Fact]
        public async Task ItemCost_ResolvedToItemLine()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var itemIdMap = new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Glob of Ectoplasm"] = 19721,
            };
            var result = MakeResult(costEntries: new List<WikiCostEntry>
            {
                new WikiCostEntry { Value = 3, Currency = "Glob of Ectoplasm" },
            });

            var offer = Program.ConvertToOffer(result, helper, itemIdMap);

            Assert.NotNull(offer);
            Assert.Single(offer.CostLines);
            Assert.Equal("Item", offer.CostLines[0].Type);
            Assert.Equal(19721, offer.CostLines[0].Id);
            Assert.Equal(3, offer.CostLines[0].Count);
        }

        [Fact]
        public async Task EmptyCurrency_DefaultsToCoins()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(costEntries: new List<WikiCostEntry>
            {
                new WikiCostEntry { Value = 256, Currency = "" },
            });

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal("Currency", offer.CostLines[0].Type);
            Assert.Equal(Gw2Constants.CoinCurrencyId, offer.CostLines[0].Id);
        }

        [Fact]
        public async Task UnresolvedCurrency_ReturnsNull()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(costEntries: new List<WikiCostEntry>
            {
                new WikiCostEntry { Value = 10, Currency = "Unknown Token" },
            });

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.Null(offer);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public async Task NullOrEmptyMerchant_ReturnsNull(string merchantName)
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(merchantName: merchantName);

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.Null(offer);
        }

        [Theory]
        [InlineData(null)]
        [InlineData(0)]
        [InlineData(-1)]
        public async Task InvalidOutputQuantity_DefaultsTo1(int? qty)
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(outputQuantity: qty);

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal(1, offer.OutputCount);
        }

        [Fact]
        public async Task OfferIdIsPopulated()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult();

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Matches("^[0-9a-f]{64}$", offer.OfferId);
        }

        [Fact]
        public async Task EmptyLocations_BecomesNull()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(locations: new List<string>());

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Null(offer.Locations);
        }

        [Fact]
        public async Task NonEmptyLocations_Preserved()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(locations: new List<string> { "Lion's Arch", "Divinity's Reach" });

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal(2, offer.Locations.Count);
            Assert.Contains("Lion's Arch", offer.Locations);
            Assert.Contains("Divinity's Reach", offer.Locations);
        }

        [Fact]
        public async Task NoCapData_OfferCapsStayNull()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult();

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Null(offer.DailyCap);
            Assert.Null(offer.WeeklyCap);
            Assert.Null(offer.SeasonalCap);
        }

        [Fact]
        public async Task CapData_ThreadedIntoOffer()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(dailyCap: 5, weeklyCap: 1);

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal(5, offer.DailyCap);
            Assert.Equal(1, offer.WeeklyCap);
        }

        [Fact]
        public async Task CapData_ChangesOfferIdVersusNoCap()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var uncapped = Program.ConvertToOffer(
                MakeResult(), helper, new Dictionary<string, int>());
            var capped = Program.ConvertToOffer(
                MakeResult(weeklyCap: 1), helper, new Dictionary<string, int>());

            Assert.NotNull(uncapped);
            Assert.NotNull(capped);
            // This is the exact KNOWN-ISSUES #28 named case: a "(Weekly)" vendor
            // offer that gains a real WeeklyCap must change OfferId, since the
            // hasher folds dailyCap/weeklyCap into the hashed string.
            Assert.NotEqual(uncapped.OfferId, capped.OfferId);
        }

        // Astral Acclaim package: SeasonalCap threading,
        // mirroring the daily/weekly cases above.
        [Fact]
        public async Task SeasonalCapData_ThreadedIntoOffer()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(gameId: 19675, merchantName: "Wizard's Vault", seasonalCap: 20);

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal(20, offer.SeasonalCap);
            // Daily/weekly must stay null - this offer has no such cap.
            Assert.Null(offer.DailyCap);
            Assert.Null(offer.WeeklyCap);
        }

        [Fact]
        public async Task SeasonalCapData_ChangesOfferIdVersusNoCap()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var uncapped = Program.ConvertToOffer(
                MakeResult(gameId: 19675, merchantName: "Wizard's Vault"),
                helper, new Dictionary<string, int>());
            var capped = Program.ConvertToOffer(
                MakeResult(gameId: 19675, merchantName: "Wizard's Vault", seasonalCap: 20),
                helper, new Dictionary<string, int>());

            Assert.NotNull(uncapped);
            Assert.NotNull(capped);
            // This is the exact task-named case: the Wizard's Vault Mystic
            // Clover row gaining a real SeasonalCap must change OfferId, since
            // the hasher folds seasonalCap into the hashed string.
            Assert.NotEqual(uncapped.OfferId, capped.OfferId);
        }

        [Fact]
        public async Task SeasonalCapData_DoesNotChangeOfferIdWhenDailyWeeklyAlsoDiffer()
        {
            // Sanity check that SeasonalCap and DailyCap/WeeklyCap are
            // independent hash inputs, not aliases of one another.
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var withDailyOnly = Program.ConvertToOffer(
                MakeResult(dailyCap: 5), helper, new Dictionary<string, int>());
            var withSeasonalOnly = Program.ConvertToOffer(
                MakeResult(seasonalCap: 5), helper, new Dictionary<string, int>());

            Assert.NotNull(withDailyOnly);
            Assert.NotNull(withSeasonalOnly);
            Assert.NotEqual(withDailyOnly.OfferId, withSeasonalOnly.OfferId);
        }

        // HomesteadTier wiring end-to-end through
        // ConvertToOffer.
        [Fact]
        public async Task NonHomesteadMerchant_HomesteadTierStaysNull()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(merchantName: "Miyani");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Null(offer.HomesteadTier);
        }

        [Fact]
        public async Task HomesteadMerchant_NoRequirement_HomesteadTierIsZero()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(gameId: 102205, merchantName: "Homestead Refinement\u2014Metal Forge");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal(0, offer.HomesteadTier);
        }

        [Fact]
        public async Task HomesteadMerchant_OneRequirement_HomesteadTierIsOne()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(
                gameId: 102205,
                merchantName: "Homestead Refinement\u2014Metal Forge",
                requirement: "one [[Homestead Upgrade: Ore Trade Efficiency]]");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal(1, offer.HomesteadTier);
        }

        [Fact]
        public async Task HomesteadMerchant_TwoRequirement_HomesteadTierIsTwo()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(
                gameId: 102205,
                merchantName: "Homestead Refinement\u2014Metal Forge",
                requirement: "two [[Homestead Upgrade: Ore Trade Efficiency]]");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal(2, offer.HomesteadTier);
        }

        [Fact]
        public async Task DifferentHomesteadTiers_ProduceDifferentOfferIds()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var tier0 = Program.ConvertToOffer(
                MakeResult(gameId: 102205, merchantName: "Homestead Refinement\u2014Metal Forge"),
                helper, new Dictionary<string, int>());
            var tier1 = Program.ConvertToOffer(
                MakeResult(
                    gameId: 102205,
                    merchantName: "Homestead Refinement\u2014Metal Forge",
                    requirement: "one [[Homestead Upgrade: Ore Trade Efficiency]]"),
                helper, new Dictionary<string, int>());

            Assert.NotNull(tier0);
            Assert.NotNull(tier1);
            // Same GameId/output/cost/merchant otherwise - only the tier
            // differs, and it must still change the OfferId (this is the
            // Potato T0/T1 collision the row-content-only hasher used to
            // miss - see WikiSmwClient.ComputeCompositeKey's doc comment).
            Assert.NotEqual(tier0.OfferId, tier1.OfferId);
        }

        [Fact]
        public async Task HomesteadMerchant_UnrecognizedRequirement_HomesteadTierStaysNull()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(
                gameId: 102306,
                merchantName: "Homestead Refinement\u2014Farm",
                requirement: "[[Some Unrelated Achievement]]");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Null(offer.HomesteadTier);
        }

        [Fact]
        public async Task HomesteadMerchant_NonMaterialOutput_HomesteadTierStaysNull()
        {
            // The station's own one-time efficiency/capacity Upgrade
            // purchase items share the identical merchant name (wiki's
            // "Has vendor" is hardcoded to the page name for every row on
            // the page) but are NOT a refined-material conversion - must
            // never be tagged with a tier concept that doesn't apply to
            // them, even though their own row has no requirement text
            // either (which would otherwise read as tier 0).
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(
                gameId: 102415, // Homestead Upgrade: Ore Trade Efficiency
                merchantName: "Homestead Refinement\u2014Metal Forge");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Null(offer.HomesteadTier);
        }

        // Festival-vendor auto-tagging follow-up:
        // SeasonalFestival threading through ConvertToOffer, mirroring the
        // HomesteadTier wiring block above.
        [Fact]
        public async Task NoTemporarySeasonalValue_SeasonalFestivalStaysNull()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(merchantName: "Candy Corn Vendor (Weekly)");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Null(offer.SeasonalFestival);
        }

        [Fact]
        public async Task KnownSeasonalValue_ResolvedToInternalFestivalKey()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(
                merchantName: "Candy Corn Vendor (Weekly)",
                temporarySeasonalValue: "Halloween");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal("halloween", offer.SeasonalFestival);
        }

        [Theory]
        [InlineData("Dragon Bash", "dragonbash")]
        [InlineData("Wintersday", "wintersday")]
        [InlineData("Festival of the Four Winds", "festivalofthefourwinds")]
        [InlineData("Lunar New Year", "lunarnewyear")]
        [InlineData("Super Adventure Festival", "superadventurefestival")]
        public async Task EachKnownFestival_ResolvedToInternalFestivalKey(
            string wikiDisplayName, string expectedKey)
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(temporarySeasonalValue: wikiDisplayName);

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Equal(expectedKey, offer.SeasonalFestival);
        }

        [Fact]
        public async Task UnrecognizedSeasonalValue_LeftUntagged_NeverGuessed()
        {
            // Real, live-confirmed unmapped event value (Consortium Trader
            // (Fractal Rush)) - must be left untagged, not guessed into
            // one of the six known festivals.
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(temporarySeasonalValue: "Fractal Rush");

            var offer = Program.ConvertToOffer(result, helper, new Dictionary<string, int>());

            Assert.NotNull(offer);
            Assert.Null(offer.SeasonalFestival);
        }

        [Fact]
        public async Task SeasonalFestival_DoesNotChangeOfferId()
        {
            // VendorOffer.SeasonalFestival's own doc comment: deliberately
            // NOT hashed by VendorOfferHasher, so tagging an
            // already-shipped offer never changes its OfferId (which
            // would otherwise look like a brand-new offer to any consumer
            // keyed on OfferId).
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var untagged = Program.ConvertToOffer(
                MakeResult(merchantName: "Candy Corn Vendor (Weekly)"),
                helper, new Dictionary<string, int>());
            var tagged = Program.ConvertToOffer(
                MakeResult(merchantName: "Candy Corn Vendor (Weekly)", temporarySeasonalValue: "Halloween"),
                helper, new Dictionary<string, int>());

            Assert.NotNull(untagged);
            Assert.NotNull(tagged);
            Assert.Equal(untagged.OfferId, tagged.OfferId);
            Assert.Null(untagged.SeasonalFestival);
            Assert.Equal("halloween", tagged.SeasonalFestival);
        }

        // Lyhr's real wiki rows: the Obsidian armour pieces carry
        // "Recipe: Legendary Obsidian Armor", item 101483, whose GW2 API
        // details name recipe 14083.
        private const string ObsidianSheetName = "Recipe: Legendary Obsidian Armor";
        private const int ObsidianSheetItemId = 101483;
        private const int ObsidianSheetRecipeId = 14083;

        private static Dictionary<string, int> SheetItemIdMap()
        {
            return new Dictionary<string, int>(System.StringComparer.OrdinalIgnoreCase)
            {
                [ObsidianSheetName] = ObsidianSheetItemId,
            };
        }

        [Fact]
        public async Task RecipeSheetRequirement_TagsBothUnlockIds()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(merchantName: "Lyhr", requirement: ObsidianSheetName);

            var offer = Program.ConvertToOffer(
                result, helper, SheetItemIdMap(),
                new Dictionary<int, int> { [ObsidianSheetItemId] = ObsidianSheetRecipeId });

            Assert.NotNull(offer);
            Assert.Equal(ObsidianSheetItemId, offer.UnlockRecipeItemId);
            Assert.Equal(ObsidianSheetRecipeId, offer.UnlockRecipeId);
        }

        [Fact]
        public async Task RecipeSheetRequirement_UnresolvedRecipeId_LeavesBothNull()
        {
            // Half a gate is worse than none: the sheet's item id is known
            // but nothing maps it to a recipe, so ownership could never be
            // checked.
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(merchantName: "Lyhr", requirement: ObsidianSheetName);

            var offer = Program.ConvertToOffer(result, helper, SheetItemIdMap());

            Assert.NotNull(offer);
            Assert.Null(offer.UnlockRecipeItemId);
            Assert.Null(offer.UnlockRecipeId);
        }

        [Fact]
        public async Task NonSheetRequirement_LeavesBothNull()
        {
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var result = MakeResult(
                merchantName: "Lyhr", requirement: "Obsidian Armor Crafting");

            var offer = Program.ConvertToOffer(
                result, helper, SheetItemIdMap(),
                new Dictionary<int, int> { [ObsidianSheetItemId] = ObsidianSheetRecipeId });

            Assert.NotNull(offer);
            Assert.Null(offer.UnlockRecipeItemId);
            Assert.Null(offer.UnlockRecipeId);
        }

        [Fact]
        public async Task UnlockRequirement_DoesNotChangeOfferId()
        {
            // Models/VendorOffer.cs: the unlock is deliberately not hashed,
            // so back-filling it onto an already-shipped row leaves the id
            // every consumer keys on untouched.
            var (helper, httpClient) = await CreateLoadedHelper();
            using var _ = httpClient;
            var untagged = Program.ConvertToOffer(
                MakeResult(merchantName: "Lyhr"), helper, SheetItemIdMap());
            var tagged = Program.ConvertToOffer(
                MakeResult(merchantName: "Lyhr", requirement: ObsidianSheetName),
                helper, SheetItemIdMap(),
                new Dictionary<int, int> { [ObsidianSheetItemId] = ObsidianSheetRecipeId });

            Assert.NotNull(untagged);
            Assert.NotNull(tagged);
            Assert.Equal(untagged.OfferId, tagged.OfferId);
            Assert.Null(untagged.UnlockRecipeItemId);
            Assert.Equal(ObsidianSheetItemId, tagged.UnlockRecipeItemId);
        }
    }
}
