using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;

namespace MysticForgeSeeder
{
    public class WikiIngredientEntry
    {
        public int? Index { get; set; }

        public int Quantity { get; set; }

        /// <summary>
        /// The item id the wiki itself asserts for this ingredient, from
        /// the recipe subobject's "Has ingredient with id" record. Null on
        /// the older recipe template, which publishes names only.
        /// </summary>
        public int? GameId { get; set; }

        // Always set at construction (ParseIngredientRecord's one object
        // initializer, guarded by an IsNullOrEmpty check just before it).
        public string Name { get; set; } = string.Empty;
    }

    public class WikiRecipeEntry
    {
        // Always set at construction (ParseRecipeResult's one object
        // initializer, guarded by an IsNullOrEmpty check just before it).
        public string OutputName { get; set; } = string.Empty;

        public int OutputQuantity { get; set; } = 1;

        /// <summary>
        /// The item id the wiki itself asserts for this recipe's output,
        /// from the recipe template's "output item id" parameter. Null when
        /// the template omits it.
        /// </summary>
        public int? OutputGameId { get; set; }

        public List<WikiIngredientEntry> Ingredients { get; set; }
            = new List<WikiIngredientEntry>();
    }

    public class WikiRecipeClient
    {
        private const string WikiApiUrl = "https://wiki.guildwars2.com/api.php";
        private const int MaxRetries = 3;
        private const int QueryLimit = 500;

        /// <summary>
        /// The replication lag, in seconds, above which the wiki should
        /// refuse this scrape rather than serve it. API:Etiquette asks a
        /// non-interactive client to send the parameter and names 5 as the
        /// value for a client in no hurry.
        /// </summary>
        private const string MaxLagSeconds = "5";

        /// <summary>
        /// Shortest pause after a lag refusal. Manual:Maxlag parameter asks
        /// for at least five seconds.
        /// </summary>
        private const int LagBackoffMs = 5000;

        private readonly HttpClient _httpClient;
        private readonly int _delayMs;
        private readonly int _maxRequests;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Func<DateTimeOffset> _now;
        private int _requestCount;

        public WikiRecipeClient(
            HttpClient httpClient,
            int delayMs = 250,
            int maxRequests = 200,
            Func<TimeSpan, CancellationToken, Task>? delay = null,
            Func<DateTimeOffset>? now = null)
        {
            _httpClient = httpClient;
            _delayMs = delayMs;
            _maxRequests = maxRequests;
            _delay = delay ?? ((d, ct) => Task.Delay(d, ct));
            _now = now ?? (() => DateTimeOffset.UtcNow);
        }

        /// <summary>
        /// Queries wiki for all Mystic Forge recipes via SMW.
        /// Paginates via query-continue-offset, deduplicates by OutputName.
        /// </summary>
        public async Task<List<WikiRecipeEntry>> QueryMysticForgeRecipesAsync(
            CancellationToken ct = default)
        {
            var allEntries = new List<WikiRecipeEntry>();
            int offset = 0;
            int pagesFetched = 0;

            string baseQuery =
                "[[Has recipe source::Mystic forge]]" +
                "|?Has canonical name" +
                "|?Has output quantity" +
                "|?Has output game id" +
                "|?Has ingredient" +
                "|?Has ingredient with id" +
                $"|limit={QueryLimit}";

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                CheckRequestLimit();

                string query = baseQuery + $"|offset={offset}";
                string responseJson = await PostSmwQueryAsync(query, ct);
                pagesFetched++;

                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                // A page with no results says so as an empty results object;
                // a body that states no results at all is the wiki declining
                // in a shape this tool does not know, and ending the scrape
                // on it would write a short seed and call it complete.
                if (!root.TryGetProperty("query", out var queryEl) ||
                    !queryEl.TryGetProperty("results", out var results))
                {
                    throw new WikiApiException(
                        $"Wiki response at offset {offset} carried neither " +
                        "results nor an error object.");
                }

                // Empty results come back as [] instead of {}
                if (results.ValueKind != JsonValueKind.Object)
                {
                    break;
                }

                int batchCount = 0;
                foreach (var prop in results.EnumerateObject())
                {
                    var entry = ParseRecipeResult(prop.Name, prop.Value);
                    if (entry != null)
                    {
                        allEntries.Add(entry);
                        batchCount++;
                    }
                }

                Console.WriteLine(
                    $"  Page {pagesFetched}: offset={offset}, +{batchCount} recipes");

                if (batchCount == 0)
                {
                    break;
                }

                if (root.TryGetProperty("query-continue-offset", out var continueEl))
                {
                    if (!TryReadInt(continueEl, out int nextOffset))
                    {
                        break;
                    }

                    if (nextOffset <= offset)
                    {
                        break;
                    }

                    offset = nextOffset;
                    await _delay(TimeSpan.FromMilliseconds(_delayMs), ct);
                }
                else
                {
                    break;
                }
            }

            Console.WriteLine(
                $"  Total: {allEntries.Count} raw recipes in {pagesFetched} pages, " +
                $"{_requestCount} HTTP requests used");

            // Dedup: same OutputName (case-insensitive), keep most ingredients
            var deduped = allEntries
                .GroupBy(e => e.OutputName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderByDescending(e => e.Ingredients.Count).First())
                .ToList();

            if (deduped.Count < allEntries.Count)
            {
                Console.WriteLine(
                    $"  After dedup: {deduped.Count} recipes " +
                    $"({allEntries.Count - deduped.Count} duplicates removed)");
            }

            return deduped;
        }

        /// <summary>
        /// Resolves item names to GW2 item IDs via wiki SMW queries.
        /// Batches names in groups of 10 using [[A]]OR[[B]] syntax.
        /// Returns canonical fulltext as key (trimmed, case-preserved).
        /// </summary>
        public async Task<Dictionary<string, int>> ResolveItemIdsAsync(
            IEnumerable<string> names,
            CancellationToken ct = default)
        {
            var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var nameList = names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            const int batchSize = 10;
            int totalBatches = (nameList.Count + batchSize - 1) / batchSize;

            for (int i = 0; i < nameList.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                CheckRequestLimit();

                var batch = nameList.Skip(i).Take(batchSize).ToList();
                var condition = string.Join("OR", batch.Select(n => $"[[{n}]]"));
                var query = condition + "|?Has game id";

                Console.WriteLine(
                    $"  Resolving batch {i / batchSize + 1}/{totalBatches} " +
                    $"({batch.Count} names)...");

                string responseJson;
                try
                {
                    responseJson = await PostSmwQueryAsync(query, ct);
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine(
                        $"  WARNING: Resolution interrupted at batch " +
                        $"{i / batchSize + 1}: {ex.Message}");
                    break;
                }

                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;

                if (root.TryGetProperty("query", out var queryEl) &&
                    queryEl.TryGetProperty("results", out var results) &&
                    results.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in results.EnumerateObject())
                    {
                        // Use canonical fulltext from response as key
                        string canonicalName = prop.Name;
                        if (prop.Value.TryGetProperty("fulltext", out var ft))
                        {
                            canonicalName = ft.GetString()?.Trim() ?? prop.Name;
                        }

                        if (prop.Value.TryGetProperty("printouts", out var printouts) &&
                            printouts.TryGetProperty("Has game id", out var gameIds) &&
                            gameIds.GetArrayLength() > 0)
                        {
                            if (TryReadInt(gameIds[0], out int gameId) &&
                                gameId > 0)
                            {
                                // Log case collisions
                                if (result.TryGetValue(canonicalName, out int existing) &&
                                    existing != gameId)
                                {
                                    Console.WriteLine(
                                        $"    COLLISION: '{canonicalName}' had ID " +
                                        $"{existing}, now {gameId}");
                                }

                                result[canonicalName] = gameId;
                            }
                        }
                    }
                }

                if (i + batchSize < nameList.Count)
                {
                    await _delay(TimeSpan.FromMilliseconds(_delayMs), ct);
                }
            }

            return result;
        }

        private WikiRecipeEntry? ParseRecipeResult(string pageName, JsonElement element)
        {
            if (!element.TryGetProperty("printouts", out var printouts))
            {
                return null;
            }

            // Output name: Has canonical name[0], fallback fulltext with #recipe suffix stripped
            string? outputName = null;
            if (printouts.TryGetProperty("Has canonical name", out var canonicalArr) &&
                canonicalArr.GetArrayLength() > 0)
            {
                outputName = canonicalArr[0].GetString()?.Trim();
            }

            if (string.IsNullOrEmpty(outputName))
            {
                string fulltext = pageName;
                if (element.TryGetProperty("fulltext", out var ft))
                {
                    fulltext = ft.GetString() ?? pageName;
                }

                int hashIdx = fulltext.IndexOf('#');
                outputName = hashIdx >= 0
                    ? fulltext.Substring(0, hashIdx).Trim()
                    : fulltext.Trim();
            }

            if (string.IsNullOrEmpty(outputName))
            {
                return null;
            }

            // Output quantity: default 1
            int outputQuantity = 1;
            if (printouts.TryGetProperty("Has output quantity", out var qtyArr) &&
                qtyArr.GetArrayLength() > 0)
            {
                if (TryReadInt(qtyArr[0], out int val) && val > 0)
                {
                    outputQuantity = val;
                }
            }

            // Output item id: the recipe template's own "output item id"
            // parameter. Absent on templates that omit it, in which case
            // the wiki derives nothing and the name is all we have.
            int? outputGameId = null;
            if (printouts.TryGetProperty("Has output game id", out var outIdArr) &&
                outIdArr.GetArrayLength() > 0 &&
                TryReadInt(outIdArr[0], out int outIdVal) &&
                outIdVal > 0)
            {
                outputGameId = outIdVal;
            }

            // Ingredients from Has ingredient records
            var ingredients = new List<WikiIngredientEntry>();
            if (printouts.TryGetProperty("Has ingredient", out var ingArray))
            {
                foreach (var record in ingArray.EnumerateArray())
                {
                    var ing = ParseIngredientRecord(record);
                    if (ing != null)
                    {
                        ingredients.Add(ing);
                    }
                }
            }

            AttachIngredientIds(printouts, ingredients);

            // Deterministic sort: indexed first (ascending), then unindexed (by name)
            ingredients = ingredients
                .OrderBy(i => i.Index.HasValue ? 0 : 1)
                .ThenBy(i => i.Index ?? int.MaxValue)
                .ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return new WikiRecipeEntry
            {
                OutputName = outputName,
                OutputQuantity = outputQuantity,
                OutputGameId = outputGameId,
                Ingredients = ingredients,
            };
        }

        /// <summary>
        /// Copies the item ids from the recipe subobject's "Has ingredient
        /// with id" records onto the matching "Has ingredient" entries.
        /// The two arrays hold the same ingredients in unrelated order, so
        /// they are joined on "Has ingredient index" - the only key the
        /// wiki publishes for them. An index that is missing or repeated on
        /// either side carries no id rather than a guessed one.
        /// </summary>
        private static void AttachIngredientIds(
            JsonElement printouts, List<WikiIngredientEntry> ingredients)
        {
            if (!printouts.TryGetProperty("Has ingredient with id", out var withIdArray))
            {
                return;
            }

            var byIndex = new Dictionary<int, int>();
            var ambiguous = new HashSet<int>();

            foreach (var record in withIdArray.EnumerateArray())
            {
                var entry = ParseIngredientRecord(record);
                if (entry?.Index == null || entry.GameId == null)
                {
                    continue;
                }

                if (!byIndex.TryAdd(entry.Index.Value, entry.GameId.Value))
                {
                    ambiguous.Add(entry.Index.Value);
                }
            }

            var seen = new HashSet<int>();
            foreach (var ing in ingredients)
            {
                if (ing.Index != null && !seen.Add(ing.Index.Value))
                {
                    ambiguous.Add(ing.Index.Value);
                }
            }

            foreach (var ing in ingredients)
            {
                if (ing.Index == null || ambiguous.Contains(ing.Index.Value))
                {
                    continue;
                }

                if (byIndex.TryGetValue(ing.Index.Value, out int gameId))
                {
                    ing.GameId = gameId;
                }
            }
        }

        private static WikiIngredientEntry? ParseIngredientRecord(JsonElement record)
        {
            // Name: Has ingredient name.item[0].fulltext
            string? name = null;
            if (record.TryGetProperty("Has ingredient name", out var nameObj) &&
                nameObj.TryGetProperty("item", out var nameItems) &&
                nameItems.GetArrayLength() > 0)
            {
                var first = nameItems[0];
                if (first.ValueKind == JsonValueKind.Object &&
                    first.TryGetProperty("fulltext", out var ft))
                {
                    name = ft.GetString()?.Trim();
                }
                else if (first.ValueKind == JsonValueKind.String)
                {
                    name = first.GetString()?.Trim();
                }
            }

            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            // Quantity: Has ingredient quantity.item[0], default 1
            int quantity = 1;
            if (record.TryGetProperty("Has ingredient quantity", out var qtyObj) &&
                qtyObj.TryGetProperty("item", out var qtyItems) &&
                qtyItems.GetArrayLength() > 0)
            {
                if (TryReadInt(qtyItems[0], out int val) && val > 0)
                {
                    quantity = val;
                }
            }

            // Index: Has ingredient index.item[0] (nullable)
            int? index = null;
            if (record.TryGetProperty("Has ingredient index", out var idxObj) &&
                idxObj.TryGetProperty("item", out var idxItems) &&
                idxItems.GetArrayLength() > 0)
            {
                if (TryReadInt(idxItems[0], out int idx))
                {
                    index = idx;
                }
            }

            // Item id: Has ingredient id.item[0]. Present only on the
            // "Has ingredient with id" records, not on "Has ingredient".
            int? gameId = null;
            if (record.TryGetProperty("Has ingredient id", out var gidObj) &&
                gidObj.TryGetProperty("item", out var gidItems) &&
                gidItems.GetArrayLength() > 0)
            {
                if (TryReadInt(gidItems[0], out int gid) && gid > 0)
                {
                    gameId = gid;
                }
            }

            return new WikiIngredientEntry
            {
                Index = index,
                Quantity = quantity,
                Name = name,
                GameId = gameId,
            };
        }

        /// <summary>
        /// POSTs an SMW ask query. All wiki queries use POST to avoid URL length limits.
        /// Includes retry with exponential backoff + jitter and Retry-After support.
        /// </summary>
        private async Task<string> PostSmwQueryAsync(string query, CancellationToken ct)
        {
            _requestCount++;

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var content = new FormUrlEncodedContent(
                        new Dictionary<string, string>
                        {
                            ["action"] = "ask",
                            ["format"] = "json",
                            ["maxlag"] = MaxLagSeconds,
                            ["query"] = query,
                        });

                    using var response = await _httpClient.PostAsync(
                        WikiApiUrl, content, ct);
                    int statusCode = (int)response.StatusCode;

                    if (statusCode == 403)
                    {
                        if (attempt >= MaxRetries)
                        {
                            throw new HttpRequestException(
                                $"HTTP 403 after {MaxRetries + 1} attempts. " +
                                "Wiki may be rate-limiting.");
                        }

                        int cooldownMs = WaitMs(response, 30_000 * (1 << attempt));

                        Console.WriteLine(
                            $"    HTTP 403, cooling down {cooldownMs / 1000}s " +
                            $"(attempt {attempt + 1}/{MaxRetries})...");
                        await _delay(TimeSpan.FromMilliseconds(cooldownMs), ct);
                        continue;
                    }

                    if (statusCode == 429 || statusCode >= 500)
                    {
                        if (attempt >= MaxRetries)
                        {
                            response.EnsureSuccessStatusCode();
                        }

                        int backoffMs = WaitMs(response, 1000 * (1 << attempt));

                        Console.WriteLine(
                            $"    HTTP {statusCode}, retrying in {backoffMs}ms " +
                            $"(attempt {attempt + 1}/{MaxRetries})...");
                        await _delay(TimeSpan.FromMilliseconds(backoffMs), ct);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    string body = await response.Content.ReadAsStringAsync(ct);

                    var refusal = WikiApiRefusal.Read(body);
                    if (refusal == null)
                    {
                        return body;
                    }

                    if (!refusal.IsTransient)
                    {
                        throw new WikiApiException(refusal.ToString());
                    }

                    if (attempt >= MaxRetries)
                    {
                        throw new WikiApiException(
                            $"{refusal} after {MaxRetries + 1} attempts.");
                    }

                    int lagWaitMs = WaitMs(
                        response,
                        Math.Max(LagBackoffMs, 1000 * (1 << attempt)));

                    Console.WriteLine(
                        $"    {refusal}, retrying in {lagWaitMs}ms " +
                        $"(attempt {attempt + 1}/{MaxRetries})...");
                    await _delay(TimeSpan.FromMilliseconds(lagWaitMs), ct);
                }
                catch (HttpRequestException) when (attempt < MaxRetries)
                {
                    int backoffMs = AddJitter(1000 * (1 << attempt));

                    Console.WriteLine(
                        $"    Request failed, retrying in {backoffMs}ms " +
                        $"(attempt {attempt + 1}/{MaxRetries})...");
                    await _delay(TimeSpan.FromMilliseconds(backoffMs), ct);
                }
            }

            throw new HttpRequestException(
                $"Failed after {MaxRetries + 1} attempts");
        }

        /// <summary>
        /// The wait before the next attempt: whatever Retry-After asks for,
        /// or <paramref name="fallbackMs"/> when it asks for nothing or for
        /// less, jittered so a run does not synchronise its retries.
        /// </summary>
        private int WaitMs(HttpResponseMessage response, int fallbackMs)
        {
            TimeSpan wait = HttpRetry.ResolveDelay(
                response, TimeSpan.FromMilliseconds(fallbackMs), _now());
            return AddJitter((int)wait.TotalMilliseconds);
        }

        /// <summary>
        /// Safely reads an integer from a JsonElement.
        /// The GW2 Wiki SMW API serialises all numeric printout values as
        /// JSON floats (e.g. 1.0 instead of 1) because the underlying
        /// MediaWiki property type is "Quantity". System.Text.Json treats
        /// these as non-integer, so TryGetInt32 fails and we must fall back
        /// to TryGetDouble. We still validate that the value is a whole
        /// number within int range to avoid silently truncating bad data.
        /// </summary>
        private static bool TryReadInt(JsonElement el, out int value)
        {
            if (el.TryGetInt32(out value))
            {
                return true;
            }

            if (el.TryGetDouble(out double d))
            {
                if (d < int.MinValue || d > int.MaxValue)
                {
                    value = 0;
                    return false;
                }

                double rounded = Math.Round(d);
                if (Math.Abs(d - rounded) > 1e-9)
                {
                    value = 0;
                    return false;
                }

                value = (int)rounded;
                return true;
            }

            value = 0;
            return false;
        }

        private static int AddJitter(int baseMs)
        {
            int jitter = (int)(baseMs * 0.1);
            int result = baseMs + Random.Shared.Next(-jitter, jitter + 1);
            return Math.Max(result, 0);
        }

        private void CheckRequestLimit()
        {
            if (_requestCount >= _maxRequests)
            {
                throw new InvalidOperationException(
                    $"Reached request limit ({_maxRequests}). " +
                    "Use --max-requests to increase.");
            }
        }
    }
}
