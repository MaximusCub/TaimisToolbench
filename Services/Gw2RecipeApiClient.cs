using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TaimisToolbench.Services
{
    internal class Gw2RecipeApiClient : IRecipeApiClient
    {
        private const string BaseUrl = "https://api.guildwars2.com/v2";

        // The GW2 API hides the whole currency-ingredient era of recipes
        // from UNVERSIONED /v2/recipes responses (an unversioned
        // /v2/recipes/14025 404s outright). Pinned to a literal date
        // rather than "v=latest" so an upstream schema bump can never
        // silently change the ingredient JSON shape for this client;
        // re-pin only alongside a verified review of the new schema's
        // ingredient shape. Internal so RecipeCorpusVerifier's id-list
        // request pins the same version instead of duplicating the literal.
        internal const string SchemaVersion = "2026-08-15";

        // /v2 page cap, the same batch idiom as ItemMetadataService.
        // Internal so RecipeCorpusRefresher chunks its sweep at the same
        // width this client would split on anyway, rather than restating
        // the cap and drifting from it.
        internal const int BatchSize = 200;

        private readonly HttpClient _http;
        private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;

        /// <summary>
        /// <paramref name="retryDelay"/> is injected by tests so a scripted
        /// refusal costs no wall-clock time; null uses Task.Delay.
        /// </summary>
        public Gw2RecipeApiClient(
            HttpClient http, Func<TimeSpan, CancellationToken, Task> retryDelay = null)
        {
            _http = http;
            _retryDelay = retryDelay;
        }

        public async Task<RecipeSearchResult> SearchByOutputAsync(int itemId, CancellationToken ct)
        {
            // Even with the schema version pinned, upstream's search
            // index has its own gap: some recipes exist and are fetchable
            // by id yet return an empty search result. Those remain
            // discoverable only through the seeded search index
            // (ref/recipe_search_seed.json) - a live cache-miss fallback
            // through this method alone cannot find them.
            var url = $"{BaseUrl}/recipes/search?output={itemId}&v={SchemaVersion}";
            string json = await GetJsonAsync(url, ct);
            if (json == null)
            {
                // Empty, but NOT proof of absence - see
                // RecipeSearchResult.AbsenceProven.
                return new RecipeSearchResult(new List<int>(), absenceProven: false);
            }

            return new RecipeSearchResult(
                JsonConvert.DeserializeObject<List<int>>(json), absenceProven: true);
        }

        public async Task<RawRecipe> GetRecipeAsync(int recipeId, CancellationToken ct)
        {
            var url = $"{BaseUrl}/recipes/{recipeId}?v={SchemaVersion}";
            string json = await GetJsonAsync(url, ct);
            if (json == null)
            {
                return null;
            }

            return ParseRecipe(json);
        }

        /// <summary>
        /// Batched detail fetch via ?ids= for the corpus repair path
        /// (RecipeCorpusVerifier) - 200 recipes per round trip, versus one
        /// per round trip on the search path. Ids the API does not know
        /// are simply absent from the result (a whole-batch 404 means none
        /// of them exist).
        /// </summary>
        public async Task<List<RawRecipe>> GetRecipesAsync(
            IReadOnlyList<int> recipeIds, CancellationToken ct)
        {
            var result = new List<RawRecipe>(recipeIds.Count);
            for (int offset = 0; offset < recipeIds.Count; offset += BatchSize)
            {
                int count = System.Math.Min(BatchSize, recipeIds.Count - offset);
                var batch = new System.Text.StringBuilder();
                for (int i = 0; i < count; i++)
                {
                    if (i > 0)
                    {
                        batch.Append(',');
                    }

                    batch.Append(recipeIds[offset + i]);
                }

                var url = $"{BaseUrl}/recipes?ids={batch}&v={SchemaVersion}";
                string json = await GetJsonAsync(url, ct);
                if (json == null)
                {
                    continue;
                }

                foreach (var token in JArray.Parse(json))
                {
                    if (token is JObject obj)
                    {
                        result.Add(ParseRecipe(obj));
                    }
                }
            }

            return result;
        }

        // Gw2ApiRequest rather than HttpClient directly: this method turns
        // every non-success status into an exception, and a thrown recipe
        // fetch degrades that recipe to UNKNOWN in the plan, so a refusal
        // the API said to retry must not reach the throw below. It also
        // keeps ct honoured, which the classic GetStringAsync overload
        // (no CancellationToken parameter on net472) silently did not.
        private async Task<string> GetJsonAsync(string url, CancellationToken ct)
        {
            using (var response = await Gw2ApiRequest.GetAsync(_http, url, ct, _retryDelay))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return null;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"GW2 API error {(int)response.StatusCode} from {url}");
                }

                return await response.Content.ReadAsStringAsync();
            }
        }

        internal static RawRecipe ParseRecipe(string json)
        {
            return ParseRecipe(JObject.Parse(json));
        }

        internal static RawRecipe ParseRecipe(JObject obj)
        {
            var recipe = new RawRecipe
            {
                Id = obj.Value<int>("id"),
                OutputItemId = obj.Value<int>("output_item_id"),
                OutputItemCount = obj.Value<int>("output_item_count"),
                MinRating = obj.Value<int?>("min_rating") ?? 0,
            };

            var disciplines = obj["disciplines"];
            if (disciplines != null)
            {
                foreach (var d in disciplines)
                {
                    recipe.Disciplines.Add(d.Value<string>());
                }
            }

            var flags = obj["flags"];
            if (flags != null)
            {
                foreach (var f in flags)
                {
                    recipe.Flags.Add(f.Value<string>());
                }
            }

            var ingredients = obj["ingredients"];
            if (ingredients != null)
            {
                foreach (var ing in ingredients)
                {
                    // The versioned schema keys every ingredient's id as
                    // "id"; "item_id" is the unversioned shape's key,
                    // kept only as a defensive fallback.
                    //
                    // Value<int?> returns null only when the key is
                    // genuinely absent (Value<int> silently returns 0),
                    // so a row missing id or count is skipped rather than
                    // ingested as item 0 - which would trigger a price
                    // lookup and render an unnamed leaf. Skipping just
                    // the bad row (unlike the seeder, which throws) keeps
                    // the rest of a real recipe rendering on the live
                    // plan-generation path.
                    int? id = ing.Value<int?>("id") ?? ing.Value<int?>("item_id");
                    int? count = ing.Value<int?>("count");
                    if (!id.HasValue || id.Value <= 0 || !count.HasValue)
                    {
                        continue;
                    }

                    recipe.Ingredients.Add(new RawIngredient
                    {
                        Type = ing.Value<string>("type") ?? "Item",
                        Id = id.Value,
                        Count = count.Value,
                    });
                }
            }

            return recipe;
        }
    }
}
