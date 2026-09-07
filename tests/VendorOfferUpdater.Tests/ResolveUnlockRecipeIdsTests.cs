using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using VendorOfferUpdater;
using VendorOfferUpdater.Tests.Helpers;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    public class ResolveUnlockRecipeIdsTests
    {
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

        private static WikiVendorResult MakeResult(string requirement)
        {
            return new WikiVendorResult
            {
                GameId = 19685,
                MerchantName = "Lyhr",
                OutputQuantity = 1,
                CostEntries = new List<WikiCostEntry>(),
                Locations = new List<string>(),
                Requirement = requirement,
            };
        }

        private static async Task<Gw2ApiHelper> LoadedHelperAsync(
            FakeHttpHandler handler, HttpClient client)
        {
            handler.MapUrl(
                url => url.Contains("/v2/currencies") && !url.Contains("ids="),
                "[2]");
            handler.MapUrl(
                url => url.Contains("/v2/currencies?ids="),
                "[{\"id\":2,\"name\":\"Karma\"}]");

            var helper = new Gw2ApiHelper(client);
            await helper.LoadCurrenciesAsync();
            return helper;
        }

        [Fact]
        public async Task ReadsTheSheetsRecipeFromTheGw2Api()
        {
            var handler = new FakeHttpHandler();
            handler.MapUrl(
                url => url.EndsWith("/v2/items/" + ObsidianSheetItemId),
                "{\"id\":101483,\"name\":\"Recipe: Legendary Obsidian Armor\"," +
                "\"type\":\"Consumable\",\"details\":{\"type\":\"Unlock\"," +
                "\"unlock_type\":\"CraftingRecipe\",\"recipe_id\":14083," +
                "\"extra_recipe_ids\":[14073,14080]}}");

            using var httpClient = new HttpClient(handler);
            var helper = await LoadedHelperAsync(handler, httpClient);

            var results = new List<WikiVendorResult>
            {
                MakeResult(ObsidianSheetName),
                // The same sheet again, and a gate that is not a sheet at
                // all - neither may add a lookup.
                MakeResult(ObsidianSheetName),
                MakeResult("Obsidian Armor Crafting"),
            };

            var resolved = await Program.ResolveUnlockRecipeIdsAsync(
                results, SheetItemIdMap(), helper, CancellationToken.None);

            Assert.Equal(ObsidianSheetRecipeId, Assert.Single(resolved).Value);
            Assert.Single(
                handler.RequestedUrls,
                url => url.Contains("/v2/items/" + ObsidianSheetItemId));
        }

        [Fact]
        public async Task ItemThatUnlocksNoCraftingRecipe_IsLeftOut()
        {
            var handler = new FakeHttpHandler();
            handler.MapUrl(
                url => url.EndsWith("/v2/items/" + ObsidianSheetItemId),
                "{\"id\":101483,\"name\":\"Recipe: Legendary Obsidian Armor\"," +
                "\"type\":\"Trophy\"}");

            using var httpClient = new HttpClient(handler);
            var helper = await LoadedHelperAsync(handler, httpClient);

            var resolved = await Program.ResolveUnlockRecipeIdsAsync(
                new List<WikiVendorResult> { MakeResult(ObsidianSheetName) },
                SheetItemIdMap(), helper, CancellationToken.None);

            Assert.Empty(resolved);
        }

        [Fact]
        public async Task SheetNameWithNoKnownItemId_MakesNoLookup()
        {
            var handler = new FakeHttpHandler();
            using var httpClient = new HttpClient(handler);
            var helper = await LoadedHelperAsync(handler, httpClient);

            var resolved = await Program.ResolveUnlockRecipeIdsAsync(
                new List<WikiVendorResult> { MakeResult("Recipe: Something Unmapped") },
                SheetItemIdMap(), helper, CancellationToken.None);

            Assert.Empty(resolved);
            Assert.DoesNotContain(handler.RequestedUrls, url => url.Contains("/v2/items/"));
        }
    }
}
