using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using VendorOfferUpdater;
using VendorOfferUpdater.Tests.Helpers;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    /// <summary>
    /// RecipeSheetMapBuilder is what makes ref/recipe_sheet_items.json a
    /// generated file instead of a hand-written one. These drive the real
    /// parse, batch and serialize paths; the HTTP layer is a canned
    /// handler so no test reaches api.guildwars2.com.
    /// </summary>
    public class RecipeSheetMapBuilderTests : IDisposable
    {
        private readonly string _tempDir;

        public RecipeSheetMapBuilderTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "VendorOfferUpdater_Sheets_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        // -- Reading the item endpoint -------------------------------
        [Fact]
        public void OnlyAnUnlockThatNamesACraftingRecipeIsASheet()
        {
            string body = @"[
              { ""id"": 9632, ""name"": ""Recipe: Gift of Color"",
                ""details"": { ""type"": ""Unlock"", ""unlock_type"": ""CraftingRecipe"", ""recipe_id"": 3165 } },
              { ""id"": 19721, ""name"": ""Glob of Ectoplasm"",
                ""details"": { ""type"": ""CraftingMaterial"" } },
              { ""id"": 43998, ""name"": ""Chak Egg"",
                ""details"": { ""type"": ""Unlock"", ""unlock_type"": ""Content"" } },
              { ""id"": 12345, ""name"": ""Broken Sheet"",
                ""details"": { ""type"": ""Unlock"", ""unlock_type"": ""CraftingRecipe"" } },
              { ""id"": 54321, ""name"": ""No Details At All"" }
            ]";

            var sheets = RecipeSheetMapBuilder.ParseSheets(body);

            var only = Assert.Single(sheets);
            Assert.Equal(9632, only.SheetItemId);
            Assert.Equal(3165, only.RecipeId);
            Assert.Equal("Recipe: Gift of Color", only.SheetItemName);
        }

        [Fact]
        public void AResponseThatIsNotAnArrayYieldsNothing()
        {
            Assert.Empty(RecipeSheetMapBuilder.ParseSheets("{ \"text\": \"all ids not found\" }"));
        }

        // -- Batching ------------------------------------------------
        [Fact]
        public async Task IdsAreAskedAboutTwoHundredAtATime()
        {
            var ids = Enumerable.Range(1, 450).ToList();
            var handler = new FakeHttpHandler();
            handler.MapUrl(url => url.Contains("/v2/items?ids="), "[]");
            using var client = new HttpClient(handler);

            await RecipeSheetMapBuilder.ResolveSheetsAsync(ids, client, 0, CancellationToken.None);

            Assert.Equal(3, handler.RequestedUrls.Count);
            Assert.Equal(200, IdCountIn(handler.RequestedUrls[0]));
            Assert.Equal(200, IdCountIn(handler.RequestedUrls[1]));
            Assert.Equal(50, IdCountIn(handler.RequestedUrls[2]));
            Assert.Equal(200, RecipeSheetMapBuilder.ItemBatchSize);
        }

        [Fact]
        public async Task ABatchTheApiKnowsNoIdInContributesNothingAndDoesNotThrow()
        {
            var handler = new FakeHttpHandler();
            handler.Enqueue("{ \"text\": \"all ids not found\" }", HttpStatusCode.NotFound);
            handler.Enqueue(@"[ { ""id"": 96274, ""name"": ""Recipe: Bowl of Fish Stew"",
                ""details"": { ""type"": ""Unlock"", ""unlock_type"": ""CraftingRecipe"", ""recipe_id"": 13853 } } ]");
            using var client = new HttpClient(handler);

            var sheets = await RecipeSheetMapBuilder.ResolveSheetsAsync(
                Enumerable.Range(1, 400).ToList(), client, 0, CancellationToken.None);

            var only = Assert.Single(sheets);
            Assert.Equal(13853, only.RecipeId);
        }

        [Fact]
        public async Task AnApiErrorStopsTheRunRatherThanWritingAShortMap()
        {
            var handler = new FakeHttpHandler();
            handler.MapUrl(url => true, "upstream is down", HttpStatusCode.ServiceUnavailable);
            using var client = new HttpClient(handler);

            await Assert.ThrowsAsync<HttpRequestException>(() =>
                RecipeSheetMapBuilder.ResolveSheetsAsync(
                    new List<int> { 1 }, client, 0, CancellationToken.None));
        }

        // -- Reading the offers file ---------------------------------
        [Fact]
        public void SoldItemIdsAreDistinctAndAscending()
        {
            string path = Path.Combine(_tempDir, "vendor_offers.json");
            File.WriteAllText(path, @"{ ""schemaVersion"": 1, ""source"": ""test"", ""offers"": [
              { ""offerId"": ""a"", ""outputItemId"": 500, ""outputCount"": 1, ""costLines"": [], ""merchantName"": ""M"" },
              { ""offerId"": ""b"", ""outputItemId"": 100, ""outputCount"": 1, ""costLines"": [], ""merchantName"": ""M"" },
              { ""offerId"": ""c"", ""outputItemId"": 500, ""outputCount"": 1, ""costLines"": [], ""merchantName"": ""N"" }
            ] }");

            Assert.Equal(new[] { 100, 500 }, RecipeSheetMapBuilder.ReadSoldItemIds(path).ToArray());
        }

        // -- Building the entries ------------------------------------
        [Fact]
        public void ARecipeTaughtByTwoSoldSheetsKeepsTheLowestSheetItemId()
        {
            var sheets = new List<RecipeSheetMapBuilder.ResolvedSheet>
            {
                new RecipeSheetMapBuilder.ResolvedSheet(900, 42, "Recipe: Later"),
                new RecipeSheetMapBuilder.ResolvedSheet(300, 42, "Recipe: Earlier"),
                new RecipeSheetMapBuilder.ResolvedSheet(700, 7, "Recipe: Only One"),
            };

            var entries = RecipeSheetMapBuilder.BuildEntries(sheets, _tempDir, out int shared);

            Assert.Equal(1, shared);
            Assert.Equal(new[] { 7, 42 }, entries.Select(e => e.RecipeId).ToArray());
            Assert.Equal(300, entries.Single(e => e.RecipeId == 42).SheetItemId);
        }

        [Fact]
        public void RecipeFieldsAreReadBackFromTheSeedsWhenTheyAreThere()
        {
            File.WriteAllText(
                Path.Combine(_tempDir, "recipes_seed.json"),
                @"{ ""schemaVersion"": 1, ""recipes"": [
                  { ""id"": 3165, ""outputItemId"": 19638, ""disciplines"": [""Chef""], ""minRating"": 400 } ] }");
            File.WriteAllText(
                Path.Combine(_tempDir, "item_name_seed.json"),
                @"[ { ""id"": 19638, ""name"": ""Gift of Color"" } ]");

            var entries = RecipeSheetMapBuilder.BuildEntries(
                new List<RecipeSheetMapBuilder.ResolvedSheet>
                {
                    new RecipeSheetMapBuilder.ResolvedSheet(9632, 3165, "Recipe: Gift of Color"),
                    new RecipeSheetMapBuilder.ResolvedSheet(1, 99999, "Recipe: Not In The Seed"),
                },
                _tempDir,
                out _);

            var known = entries.Single(e => e.RecipeId == 3165);
            Assert.Equal(19638, known.CraftedItemId);
            Assert.Equal("Gift of Color", known.CraftedItemName);
            Assert.Equal(new[] { "Chef" }, known.Disciplines!.ToArray());
            Assert.Equal(400, known.MinRating);

            // A recipe the seed does not carry still maps, because the id
            // pair is the whole point and the rest is provenance.
            var unknown = entries.Single(e => e.RecipeId == 99999);
            Assert.Equal(1, unknown.SheetItemId);
            Assert.Null(unknown.CraftedItemId);
            Assert.Null(unknown.CraftedItemName);
        }

        // -- Writing the file ----------------------------------------
        [Fact]
        public void TheWrittenFileIsAsciiWithUnixLineEndingsAndNoNullFields()
        {
            string json = RecipeSheetMapBuilder.Serialize(
                new List<RecipeSheetMapBuilder.RecipeSheetEntry>
                {
                    new RecipeSheetMapBuilder.RecipeSheetEntry
                    {
                        RecipeId = 12,
                        SheetItemId = 34,
                        SheetItemName = "Recipe: Caf\u00e9 au Lait",
                    },
                });

            Assert.DoesNotContain("\r\n", json);
            Assert.EndsWith("\n", json);
            Assert.All(json, c => Assert.True(c <= 0x7F));
            Assert.Contains("\\u00e9", json);

            using var document = JsonDocument.Parse(json);
            Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
            var item = document.RootElement.GetProperty("items")[0];
            Assert.Equal(12, item.GetProperty("recipeId").GetInt32());
            Assert.Equal(34, item.GetProperty("sheetItemId").GetInt32());

            // A field the seeds could not fill is left out rather than
            // written as null, so an entry never claims a crafted item of
            // nothing.
            Assert.False(item.TryGetProperty("craftedItemId", out _));
            Assert.False(item.TryGetProperty("disciplines", out _));
        }

        // -- Refusing to shrink the map ------------------------------
        [Fact]
        public void TheEntryCountOnDiskIsWhatAShrinkingRunIsMeasuredAgainst()
        {
            string path = Path.Combine(_tempDir, "recipe_sheet_items.json");
            Assert.Equal(0, RecipeSheetMapBuilder.CountShippedEntries(path));

            File.WriteAllText(path, "{ \"items\": [ { \"recipeId\": 1, \"sheetItemId\": 2 } ] }");
            Assert.Equal(1, RecipeSheetMapBuilder.CountShippedEntries(path));

            // A file that will not parse is not evidence of anything, so it
            // counts as zero and never blocks a run that resolved sheets.
            File.WriteAllText(path, "{ this is not json");
            Assert.Equal(0, RecipeSheetMapBuilder.CountShippedEntries(path));
        }

        // -- The shipped file ----------------------------------------
        [Fact]
        public void EverySheetInTheShippedMapIsSoldByAVendorInTheShippedOffers()
        {
            string mapPath = RepoFileLocator.FindRepoFile(Path.Combine("ref", "recipe_sheet_items.json"));
            string offersPath = RepoFileLocator.FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            Assert.False(string.IsNullOrEmpty(mapPath), "ref/recipe_sheet_items.json was not found.");
            Assert.False(string.IsNullOrEmpty(offersPath), "ref/vendor_offers.json was not found.");

            var sold = new HashSet<int>(RecipeSheetMapBuilder.ReadSoldItemIds(offersPath));

            using var document = JsonDocument.Parse(File.ReadAllText(mapPath));
            var items = document.RootElement.GetProperty("items");

            // The builder's input is exactly the ids the offers file sells,
            // so a sheet with no offer means the two files were generated
            // from different corpora and the map can name a price we do not
            // ship.
            var unsold = items.EnumerateArray()
                .Select(e => e.GetProperty("sheetItemId").GetInt32())
                .Where(id => !sold.Contains(id))
                .ToList();

            Assert.Empty(unsold);
            Assert.True(
                items.GetArrayLength() > 1000,
                $"ref/recipe_sheet_items.json holds {items.GetArrayLength()} entries. It held one " +
                "until it was generated, which is the state this asserts against.");
        }

        private static int IdCountIn(string url)
        {
            int marker = url.IndexOf("ids=", StringComparison.Ordinal);
            return url.Substring(marker + 4).Split(',').Length;
        }
    }
}
