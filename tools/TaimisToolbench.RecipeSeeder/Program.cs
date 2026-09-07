using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using TaimisToolbench.Services.Recipes;

namespace TaimisToolbench.RecipeSeeder
{
    internal class Program
    {
        private const string BaseUrl = "https://api.guildwars2.com/v2";
        private const int BatchSize = 200;
        private const int MaxConcurrency = 4;
        private const int MaxAttempts = 3;

        private const string SeederProduct = "TaimisToolbench-RecipeSeeder";
        private const string SeederVersion = "1.0";

        // Mirrors Gw2RecipeApiClient.SchemaVersion, including the
        // rationale for pinning a literal date instead of "v=latest" -
        // see that constant's doc comment. A separate constant so the
        // runtime client and the offline seeder can re-pin independently.
        // Without the pin, the seeder silently omits every
        // currency-ingredient-era recipe from the seed files it writes.
        private const string SchemaVersion = "2026-08-15";

        // The least negative id tools/MysticForgeSeeder assigns; see that
        // tool's RecipeIdBase for why the two producers of negative-id
        // recipes have to stay in disjoint halves of the id space. Duplicated
        // rather than shared because MysticForgeSeeder is a standalone net8
        // project with no reference to this one or to the module.
        private const int MysticForgeRecipeIdBase = -100000;

        private static int Main(string[] args)
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args)
        {
            string outputDir = null;
            bool force = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--output-dir":
                        if (i + 1 < args.Length)
                        {
                            outputDir = args[++i];
                        }

                        break;
                    case "--force":
                        force = true;
                        break;
                }
            }

            // Default output to repo ref/ directory
            if (string.IsNullOrEmpty(outputDir))
            {
                outputDir = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory, "ref");
            }

            // Check for existing files
            string searchPath = Path.Combine(outputDir, "recipe_search_seed.json");
            string recipesPath = Path.Combine(outputDir, "recipes_seed.json");
            string manifestPath = Path.Combine(outputDir, "recipe_seed_manifest.json");
            string itemNamePath = Path.Combine(outputDir, "item_name_seed.json");

            if (!force && (File.Exists(searchPath) || File.Exists(recipesPath)))
            {
                Console.Error.WriteLine(
                    "Seed files already exist. Use --force to overwrite.");
                return 1;
            }

            Console.WriteLine($"Output: {outputDir}");
            Console.WriteLine();

            using (var httpClient = new HttpClient())
            {
                httpClient.Timeout = TimeSpan.FromMinutes(5);
                Gw2ApiUserAgent.Apply(httpClient, SeederProduct, SeederVersion);
                var totalSw = Stopwatch.StartNew();

                // Step 1: Fetch GW2 build ID
                int gw2BuildId = 0;
                try
                {
                    gw2BuildId = await FetchBuildIdAsync(httpClient);
                    Console.WriteLine($"GW2 Build ID: {gw2BuildId}");
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Warning: Could not fetch build ID: {ex.Message}");
                }

                // Step 2: Fetch all recipe IDs from /v2/recipes
                Console.Write("Fetching recipe ID list...");
                var sw = Stopwatch.StartNew();
                var allRecipeIds = await FetchAllRecipeIdsAsync(httpClient);
                sw.Stop();
                Console.WriteLine(
                    $" {allRecipeIds.Count} recipes ({sw.ElapsedMilliseconds}ms)");

                // Step 3: Batch-fetch all recipe details
                Console.Write(
                    $"Fetching recipe details ({allRecipeIds.Count} recipes " +
                    $"in batches of {BatchSize}, concurrency {MaxConcurrency})...");
                sw.Restart();
                var allRecipes = await FetchAllRecipesAsync(
                    httpClient, allRecipeIds);
                sw.Stop();
                Console.WriteLine(
                    $" {allRecipes.Count} fetched ({sw.ElapsedMilliseconds}ms)");

                // Step 4: Build search index (outputItemId -> recipeIds)
                var searchIndex = new Dictionary<int, List<int>>();
                foreach (var recipe in allRecipes.Values)
                {
                    if (!searchIndex.TryGetValue(
                        recipe.OutputItemId, out var list))
                    {
                        list = new List<int>();
                        searchIndex[recipe.OutputItemId] = list;
                    }

                    list.Add(recipe.Id);
                }

                // Step 5: Load and merge mystic forge recipes
                int mfCount = 0;
                try
                {
                    var mfSource = new FileMysticForgeRecipeSource();
                    using (var mfStream = mfSource.Open())
                    {
                        MergeMysticForgeRecipes(
                            mfStream, allRecipes, searchIndex, out mfCount);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"Warning: Could not load mystic forge recipes: {ex.Message}");
                }

                // Step 5a: Carry forward hand-authored negative-id recipes
                // that no generator reproduces. ref/recipes_seed.json ships
                // four synthetic Merchant/achievement rows (ids -1592..-1595,
                // the Infinite Trebuchet Blueprint chain) that exist in no
                // source file: mystic_forge_recipes.json holds forge recipes
                // only, and the API serves no negative ids, so a reseed used
                // to delete them silently. Same defect class as the dropped
                // expectedOutputCount overrides in MergeMysticForgeRecipes -
                // preserve by construction rather than by remembering.
                int preservedCount = 0;
                if (File.Exists(recipesPath))
                {
                    try
                    {
                        Dictionary<int, RawRecipe> previous;
                        using (var prevStream = File.OpenRead(recipesPath))
                        {
                            previous = RecipeCacheSerializer.LoadRecipeSeed(prevStream);
                        }

                        foreach (var kvp in previous)
                        {
                            if (kvp.Key >= 0 || allRecipes.ContainsKey(kvp.Key))
                            {
                                continue;
                            }

                            allRecipes[kvp.Key] = kvp.Value;
                            if (!searchIndex.TryGetValue(
                                kvp.Value.OutputItemId, out var preservedList))
                            {
                                preservedList = new List<int>();
                                searchIndex[kvp.Value.OutputItemId] = preservedList;
                            }

                            if (!preservedList.Contains(kvp.Key))
                            {
                                preservedList.Add(kvp.Key);
                            }

                            preservedCount++;
                        }

                        if (preservedCount > 0)
                        {
                            Console.WriteLine(
                                $"Preserved {preservedCount} hand-authored negative-id " +
                                "recipe(s) from the existing seed.");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            "Warning: could not read the existing recipe seed to " +
                            $"preserve hand-authored rows: {ex.Message}");
                    }
                }

                // Step 5b: Add negative search entries for leaf items
                // (ingredients that aren't the output of any recipe)
                var allIngredientIds = new HashSet<int>();
                foreach (var recipe in allRecipes.Values)
                {
                    foreach (var ing in recipe.Ingredients)
                    {
                        if (ing.Type == "Item")
                        {
                            allIngredientIds.Add(ing.Id);
                        }
                    }
                }

                int negativeCount = 0;
                foreach (var ingId in allIngredientIds)
                {
                    if (!searchIndex.ContainsKey(ingId))
                    {
                        searchIndex[ingId] = new List<int>();
                        negativeCount++;
                    }
                }

                // Sort search index entries for deterministic output
                foreach (var list in searchIndex.Values)
                {
                    list.Sort();
                }

                totalSw.Stop();
                Console.WriteLine();
                Console.WriteLine(
                    $"Total: {allRecipes.Count} recipes, " +
                    $"{searchIndex.Count} search entries " +
                    $"({mfCount} mystic forge, " +
                    $"{negativeCount} negative/leaf entries) " +
                    $"in {totalSw.ElapsedMilliseconds}ms");

                // Step 6: Convert and write seed files
                Directory.CreateDirectory(outputDir);

                var searches = searchIndex.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (IReadOnlyList<int>)kvp.Value.AsReadOnly());

                string searchJson = RecipeCacheSerializer.SerializeSearches(searches);
                File.WriteAllText(searchPath, searchJson, Encoding.UTF8);

                string recipeJson = RecipeCacheSerializer.SerializeRecipes(allRecipes);
                File.WriteAllText(recipesPath, recipeJson, Encoding.UTF8);

                // The manifest is written LAST (below), after item_name_seed.json
                // exists, because it now carries a row count and a SHA-256 for
                // every seed file this run produced - the numbers the seed tests
                // check against, so those tests pin what the seeder emitted
                // rather than a literal somebody typed into a test file.

                // Step 7: Generate item name seed for search provider
                var craftableItemIds = searchIndex
                    .Where(kvp => kvp.Value.Count > 0)
                    .Select(kvp => kvp.Key)
                    .ToList();
                craftableItemIds.Sort();

                Console.Write(
                    $"Fetching item names ({craftableItemIds.Count} craftable items " +
                    $"in batches of {BatchSize})...");
                sw.Restart();
                var itemNames = await FetchItemNamesAsync(
                    httpClient, craftableItemIds);
                sw.Stop();
                Console.WriteLine(
                    $" {itemNames.Count} fetched ({sw.ElapsedMilliseconds}ms)");

                var itemNameJson = JsonSerializer.Serialize(
                    itemNames.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                        .Select(e => new { id = e.Id, name = e.Name, icon = e.Icon }),
                    new JsonSerializerOptions { WriteIndented = false });
                File.WriteAllText(itemNamePath, itemNameJson, Encoding.UTF8);

                var manifest = new RecipeSeedManifest
                {
                    SeedVersion = 1,
                    Gw2BuildId = gw2BuildId,
                    CreatedUtc = DateTime.UtcNow.ToString("o"),
                    Files = new List<SeedFileIntegrity>
                    {
                        Integrity(recipesPath, allRecipes.Count),
                        Integrity(searchPath, searchIndex.Count),
                        Integrity(itemNamePath, itemNames.Count),
                    },
                };
                string manifestJson =
                    RecipeCacheSerializer.SerializeManifest(manifest);
                File.WriteAllText(manifestPath, manifestJson, Encoding.UTF8);

                Console.WriteLine();
                Console.WriteLine($"Written: {searchPath}");
                Console.WriteLine($"Written: {recipesPath}");
                Console.WriteLine($"Written: {manifestPath}");
                Console.WriteLine($"Written: {itemNamePath} ({itemNames.Count} items)");
            }

            return 0;
        }

        // Internal (not private) + the
        // matching InternalsVisibleTo in this project's .csproj so
        // TaimisToolbench.RecipeSeeder.Tests can assert the schema-
        // version query parameter on the actual outgoing request, mirroring
        // Gw2RecipeApiClientHttpTests' StubHandler coverage of the runtime
        // client's own identical fix.
        /// <summary>
        /// Integrity record for a seed file this run just wrote: the row
        /// count the seeder knows it emitted, and the SHA-256 of the bytes on
        /// disk. Read the file back rather than hashing the in-memory string,
        /// so the digest describes what a consumer will actually open.
        /// </summary>
        private static SeedFileIntegrity Integrity(string path, int rowCount)
        {
            return new SeedFileIntegrity
            {
                Name = Path.GetFileName(path),
                RowCount = rowCount,
                Sha256 = RecipeCacheSerializer.HashFile(path),
            };
        }

        internal static async Task<List<int>> FetchAllRecipeIdsAsync(
            HttpClient httpClient)
        {
            string json = await GetJsonAsync(
                httpClient, $"{BaseUrl}/recipes?v={SchemaVersion}");
            return JsonSerializer.Deserialize<List<int>>(json);
        }

        /// <summary>
        /// GETs one GW2 API URL, retrying only what the API says is worth
        /// retrying, and throwing rather than returning nothing.
        /// </summary>
        /// <remarks>
        /// The seed is written from whatever the batches returned, so a
        /// caller that swallows a refusal writes a short corpus and exits
        /// zero. A run that cannot reach the API has to fail instead. The
        /// published limits this respects are in
        /// docs/api-client-contracts.md.
        /// </remarks>
        private static async Task<string> GetJsonAsync(
            HttpClient httpClient, string url)
        {
            string lastFailure = null;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                var wait = TimeSpan.FromSeconds(attempt + 1);
                bool retryable;

                try
                {
                    using (var response = await httpClient.GetAsync(url))
                    {
                        if (response.IsSuccessStatusCode)
                        {
                            return await response.Content.ReadAsStringAsync();
                        }

                        lastFailure =
                            $"HTTP {(int)response.StatusCode} from {url}";
                        retryable = HttpRetry.IsRetryable(response.StatusCode);
                        if (retryable)
                        {
                            wait = HttpRetry.ResolveDelay(
                                response, wait, DateTimeOffset.UtcNow);
                        }
                    }
                }
                catch (HttpRequestException ex)
                {
                    lastFailure = $"{ex.Message} ({url})";
                    retryable = true;
                }

                if (!retryable)
                {
                    break;
                }

                if (attempt < MaxAttempts - 1)
                {
                    Console.Error.WriteLine(
                        $"  {lastFailure}; retrying in {wait.TotalSeconds:0.#}s");
                    await Task.Delay(wait);
                }
            }

            throw new HttpRequestException("GW2 API: " + lastFailure);
        }

        private static async Task<Dictionary<int, RawRecipe>> FetchAllRecipesAsync(
            HttpClient httpClient, List<int> recipeIds)
        {
            var result = new Dictionary<int, RawRecipe>();
            var batches = new List<List<int>>();

            for (int i = 0; i < recipeIds.Count; i += BatchSize)
            {
                int count = Math.Min(BatchSize, recipeIds.Count - i);
                batches.Add(recipeIds.GetRange(i, count));
            }

            int completed = 0;
            int total = batches.Count;
            var gate = new object();

            await BoundedConcurrency.ForEachAsync(
                batches, MaxConcurrency, async batch =>
                {
                    var recipes = await FetchRecipeBatchAsync(
                        httpClient, batch);

                    lock (gate)
                    {
                        foreach (var recipe in recipes)
                        {
                            result[recipe.Id] = recipe;
                        }

                        completed++;
                        if (completed % 10 == 0 || completed == total)
                        {
                            Console.Write(
                                $"\r  Batches: {completed}/{total}   ");
                        }
                    }
                }, CancellationToken.None);

            Console.WriteLine();
            return result;
        }

        // Internal, see
        // FetchAllRecipeIdsAsync's matching doc comment above.
        internal static async Task<List<RawRecipe>> FetchRecipeBatchAsync(
            HttpClient httpClient, List<int> ids)
        {
            string idsParam = string.Join(",",
                ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            string url = $"{BaseUrl}/recipes?ids={idsParam}&v={SchemaVersion}";

            return ParseRecipeBatch(await GetJsonAsync(httpClient, url));
        }

        private static List<RawRecipe> ParseRecipeBatch(string json)
        {
            var recipes = new List<RawRecipe>();
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    var recipe = new RawRecipe
                    {
                        Id = elem.GetProperty("id").GetInt32(),
                        OutputItemId = elem.GetProperty("output_item_id").GetInt32(),
                        OutputItemCount = elem.GetProperty("output_item_count").GetInt32(),
                        MinRating = elem.TryGetProperty("min_rating", out var mr)
                            ? mr.GetInt32() : 0,
                    };

                    if (elem.TryGetProperty("disciplines", out var disc))
                    {
                        foreach (var d in disc.EnumerateArray())
                        {
                            recipe.Disciplines.Add(d.GetString());
                        }
                    }

                    if (elem.TryGetProperty("flags", out var flags))
                    {
                        foreach (var f in flags.EnumerateArray())
                        {
                            recipe.Flags.Add(f.GetString());
                        }
                    }

                    if (elem.TryGetProperty("ingredients", out var ings))
                    {
                        foreach (var ing in ings.EnumerateArray())
                        {
                            recipe.Ingredients.Add(new RawIngredient
                            {
                                Type = ing.TryGetProperty("type", out var t)
                                    ? t.GetString() ?? "Item" : "Item",
                                // KNOWN-ISSUES #48
                                //: mirrors
                                // Gw2RecipeApiClient.ParseRecipe's own "id"-
                                // with-"item_id"-fallback fix. This one is
                                // not just a shape mismatch but a crash: with
                                // the schema version now pinned above, EVERY
                                // ingredient (Currency or Item) keys its item
                                // id as "id" - the old unconditional
                                // GetProperty("item_id") throws
                                // KeyNotFoundException-style
                                // System.Text.Json.JsonException on any such
                                // row (e.g. every ingredient of recipe 14025)
                                // instead of silently mis-parsing it, since
                                // GetProperty (unlike Newtonsoft's
                                // Value<T>(key)) throws on a missing
                                // property rather than returning a default.
                                Id = ing.TryGetProperty("id", out var idProp)
                                    ? idProp.GetInt32()
                                    : ing.GetProperty("item_id").GetInt32(),
                                Count = ing.GetProperty("count").GetInt32(),
                            });
                        }
                    }

                    recipes.Add(recipe);
                }
            }

            return recipes;
        }

        private static void MergeMysticForgeRecipes(
            Stream mfStream,
            Dictionary<int, RawRecipe> allRecipes,
            Dictionary<int, List<int>> searchIndex,
            out int count)
        {
            count = 0;

            using (var reader = new StreamReader(mfStream))
            {
                string json = reader.ReadToEnd();
                using (var doc = JsonDocument.Parse(json))
                {
                    if (!doc.RootElement.TryGetProperty("recipes", out var arr))
                    {
                        return;
                    }

                    foreach (var entry in arr.EnumerateArray())
                    {
                        if (!entry.TryGetProperty("id", out var idProp))
                        {
                            continue;
                        }

                        int id = idProp.GetInt32();
                        if (id >= 0)
                        {
                            continue;
                        }

                        // Step 5a below carries forward every negative-id
                        // row of the seed this run is about to overwrite,
                        // but only where the id is still free - so a forge
                        // row landing on a hand-authored id replaces it
                        // rather than colliding with it. The two producers
                        // own disjoint halves of the negative id space
                        // (MysticForgeSeeder.RecipeIdBase states the
                        // partition); refuse the row instead of silently
                        // eating whatever it lands on.
                        if (id > MysticForgeRecipeIdBase)
                        {
                            Console.Error.WriteLine(
                                $"Warning: skipping mystic forge recipe {id} -" +
                                " it sits in the hand-authored half of the" +
                                " negative id space and would overwrite a row" +
                                " no generator can rebuild. Re-run" +
                                " tools/MysticForgeSeeder to renumber the block.");
                            continue;
                        }

                        if (!entry.TryGetProperty("outputItemId", out var outId) ||
                            !entry.TryGetProperty("outputItemCount", out var outCount))
                        {
                            continue;
                        }

                        if (!entry.TryGetProperty("ingredients", out var ingsArr))
                        {
                            continue;
                        }

                        var recipe = new RawRecipe
                        {
                            Id = id,
                            OutputItemId = outId.GetInt32(),
                            OutputItemCount = outCount.GetInt32(),
                            // This field
                            // was previously never copied from the source
                            // JSON, silently dropping every hand-authored
                            // fractional EV override (e.g. recipe -1591,
                            // Mystic Clover, 0.31 - see
                            // ref/mystic_forge_recipes.json) on every reseed.
                            // TryGetProperty (not GetProperty) because most
                            // rows omit this field entirely (ordinary 1:1
                            // recipes have no EV override).
                            ExpectedOutputCount = entry.TryGetProperty("expectedOutputCount", out var evProp) && evProp.ValueKind != JsonValueKind.Null
                                ? evProp.GetDouble()
                                : (double?)null,
                            Disciplines = new List<string> { "MysticForge" },
                            MinRating = 0,
                            Flags = new List<string>(),
                        };

                        foreach (var ing in ingsArr.EnumerateArray())
                        {
                            if (!ing.TryGetProperty("type", out var ingType) ||
                                !ing.TryGetProperty("id", out var ingId) ||
                                !ing.TryGetProperty("count", out var ingCount))
                            {
                                continue;
                            }

                            recipe.Ingredients.Add(new RawIngredient
                            {
                                Type = ingType.GetString() ?? "Item",
                                Id = ingId.GetInt32(),
                                Count = ingCount.GetInt32(),
                            });
                        }

                        if (recipe.Ingredients.Count == 0)
                        {
                            continue;
                        }

                        allRecipes[recipe.Id] = recipe;

                        if (!searchIndex.TryGetValue(
                            recipe.OutputItemId, out var list))
                        {
                            list = new List<int>();
                            searchIndex[recipe.OutputItemId] = list;
                        }

                        if (!list.Contains(recipe.Id))
                        {
                            list.Add(recipe.Id);
                        }

                        count++;
                    }
                }
            }
        }

        private static async Task<List<ItemNameInfo>> FetchItemNamesAsync(
            HttpClient httpClient, List<int> itemIds)
        {
            var result = new List<ItemNameInfo>();
            var batches = new List<List<int>>();

            for (int i = 0; i < itemIds.Count; i += BatchSize)
            {
                int count = Math.Min(BatchSize, itemIds.Count - i);
                batches.Add(itemIds.GetRange(i, count));
            }

            int completed = 0;
            int total = batches.Count;
            var gate = new object();

            await BoundedConcurrency.ForEachAsync(
                batches, MaxConcurrency, async batch =>
                {
                    var items = await FetchItemBatchAsync(httpClient, batch);

                    lock (gate)
                    {
                        result.AddRange(items);
                        completed++;
                        if (completed % 10 == 0 || completed == total)
                        {
                            Console.Write(
                                $"\r  Batches: {completed}/{total}   ");
                        }
                    }
                }, CancellationToken.None);

            Console.WriteLine();
            return result;
        }

        private static async Task<List<ItemNameInfo>> FetchItemBatchAsync(
            HttpClient httpClient, List<int> ids)
        {
            string idsParam = string.Join(",",
                ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));
            string url = $"{BaseUrl}/items?ids={idsParam}";

            return ParseItemBatch(await GetJsonAsync(httpClient, url));
        }

        private static List<ItemNameInfo> ParseItemBatch(string json)
        {
            var items = new List<ItemNameInfo>();
            using (var doc = JsonDocument.Parse(json))
            {
                foreach (var elem in doc.RootElement.EnumerateArray())
                {
                    var item = new ItemNameInfo
                    {
                        Id = elem.GetProperty("id").GetInt32(),
                        Name = elem.TryGetProperty("name", out var n)
                            ? n.GetString() ?? "" : "",
                        Icon = elem.TryGetProperty("icon", out var ic)
                            ? ic.GetString() : null,
                    };
                    if (!string.IsNullOrEmpty(item.Name))
                    {
                        items.Add(item);
                    }
                }
            }

            return items;
        }

        private class ItemNameInfo
        {
            public int Id { get; set; }

            public string Name { get; set; }

            public string Icon { get; set; }
        }

        private static async Task<int> FetchBuildIdAsync(HttpClient httpClient)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var response = await httpClient.GetAsync(
                    $"{BaseUrl}/build", cts.Token);
                response.EnsureSuccessStatusCode();
                string json = await response.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(json))
                {
                    return doc.RootElement.GetProperty("id").GetInt32();
                }
            }
        }
    }
}
