using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace MysticForgeSeeder
{
    internal class Program
    {
        /// <summary>
        /// First (least negative) recipe id this tool assigns; ids descend
        /// from here, so the generated block occupies (-inf, RecipeIdBase]
        /// no matter how many recipes the wiki grows.
        /// <para>
        /// ref/recipes_seed.json holds negative-id recipes from two
        /// unrelated producers - this generated forge block, and rows
        /// hand-authored directly into the seed (currently the four
        /// Merchant/achievement rows at -1592..-1595) that
        /// TaimisToolbench.RecipeSeeder's Step 5a carries forward. They
        /// merge into one dictionary keyed by recipe id, so an overlap
        /// silently replaces a hand-authored row with a forge one. The two
        /// producers therefore own disjoint halves of the negative id
        /// space: hand-authored rows take [-99999, -1], the generated block
        /// takes RecipeIdBase and below.
        /// tests/TaimisToolbench.Tests/Services/Recipes/MysticForgeSeedIdSpaceTests
        /// fails the build if the shipped data ever breaches it.
        /// </para>
        /// </summary>
        private const int RecipeIdBase = -100000;

        private static async Task<int> Main(string[] args)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            try
            {
                return await RunAsync(args, cts.Token);
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine("Cancelled.");
                return 130;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.Message}");
                return 1;
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("request limit"))
            {
                Console.Error.WriteLine($"LIMIT: {ex.Message}");
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> RunAsync(
            string[] args, CancellationToken ct)
        {
            bool dryRun = false;
            bool forceResolve = false;
            int delayMs = 250;
            int maxRequests = 200;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--force-resolve":
                        forceResolve = true;
                        break;
                    case "--delay" when i + 1 < args.Length:
                        delayMs = int.Parse(args[++i]);
                        break;
                    case "--max-requests" when i + 1 < args.Length:
                        maxRequests = int.Parse(args[++i]);
                        break;
                }
            }

            string repoRoot = FindRepoRoot();
            string outputPath = Path.Combine(
                repoRoot, "ref", "mystic_forge_recipes.json");
            string cachePath = Path.Combine(
                repoRoot, "ref", "mf_item_id_cache.json");

            Console.WriteLine($"Output:        {outputPath}");
            Console.WriteLine($"Cache:         {cachePath}");
            Console.WriteLine($"Dry run:       {dryRun}");
            Console.WriteLine($"Force resolve: {forceResolve}");
            Console.WriteLine($"Delay:         {delayMs}ms");
            Console.WriteLine($"Max requests:  {maxRequests}");
            Console.WriteLine();

            using var httpClient = new HttpClient();
            // MediaWiki's User-Agent policy asks a script to supply a way to
            // reach its operator; a name alone leaves the wiki nothing but an
            // IP to act on. See docs/api-client-contracts.md.
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "TaimisToolbench-MysticForgeSeeder/1.0 " +
                "(+https://github.com/MaximusCub/TaimisToolbench)");

            var client = new WikiRecipeClient(
                httpClient, delayMs, maxRequests);

            // ================================================================
            // Step 1: Query wiki for all MF recipes
            // ================================================================
            Console.WriteLine(
                "=== Step 1: Query wiki for Mystic Forge recipes ===");
            var recipes = await client.QueryMysticForgeRecipesAsync(ct);
            Console.WriteLine($"  Recipes: {recipes.Count}");
            Console.WriteLine();

            // ================================================================
            // Step 2: Collect unique item names
            // ================================================================
            Console.WriteLine("=== Step 2: Collect unique item names ===");

            // Build canonical name set (case-insensitive dedup, log collisions)
            var canonicalNames = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var recipe in recipes)
            {
                TrackCanonicalName(canonicalNames, recipe.OutputName);
                foreach (var ing in recipe.Ingredients)
                {
                    TrackCanonicalName(canonicalNames, ing.Name);
                }
            }

            Console.WriteLine(
                $"  Total unique names: {canonicalNames.Count}");

            var cache = LoadCache(cachePath);
            Console.WriteLine($"  Cache entries: {cache.Count}");

            List<string> namesToResolve;
            if (forceResolve)
            {
                namesToResolve = canonicalNames.Values.ToList();
            }
            else
            {
                namesToResolve = canonicalNames.Values
                    .Where(n => !cache.ContainsKey(n))
                    .ToList();
            }

            Console.WriteLine(
                $"  Names to resolve: {namesToResolve.Count}");
            Console.WriteLine();

            // ================================================================
            // Step 3: Resolve names to GW2 item IDs
            // ================================================================
            Console.WriteLine(
                "=== Step 3: Resolve item IDs via wiki ===");

            if (namesToResolve.Count > 0)
            {
                var resolved = await client.ResolveItemIdsAsync(
                    namesToResolve, ct);

                int resolvedCount = 0;
                int unresolvedCount = 0;
                var unresolvedNames = new List<string>();

                foreach (var name in namesToResolve)
                {
                    if (resolved.TryGetValue(name, out int id))
                    {
                        // Store under canonical wiki fulltext (trimmed)
                        // Log if cache already had a different ID
                        if (cache.TryGetValue(name, out int oldId) &&
                            oldId != id && oldId > 0)
                        {
                            Console.WriteLine(
                                $"  CACHE UPDATE: '{name}' " +
                                $"{oldId} -> {id}");
                        }

                        cache[name] = id;
                        resolvedCount++;
                    }
                    else if (!cache.ContainsKey(name))
                    {
                        cache[name] = -1; // miss sentinel
                        unresolvedCount++;
                        unresolvedNames.Add(name);
                    }
                }

                Console.WriteLine($"  Resolved: {resolvedCount}");
                Console.WriteLine($"  Unresolved: {unresolvedCount}");

                if (unresolvedNames.Count > 0)
                {
                    int showCount = Math.Min(unresolvedNames.Count, 50);
                    foreach (var name in unresolvedNames
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .Take(showCount))
                    {
                        Console.WriteLine($"    - {name}");
                    }

                    if (unresolvedNames.Count > 50)
                    {
                        Console.WriteLine(
                            $"    ... and {unresolvedNames.Count - 50} more");
                    }
                }

                SaveCacheAtomic(cachePath, cache);
            }
            else
            {
                Console.WriteLine("  All names already cached.");
            }

            Console.WriteLine();

            // ================================================================
            // Step 4: Build recipe objects
            // ================================================================
            Console.WriteLine("=== Step 4: Build recipe objects ===");

            var validRecipes = new List<ValidRecipe>();
            int skippedOutput = 0;
            int skippedIngredient = 0;
            int skippedQuantity = 0;
            int wikiIdUsedForOutput = 0;
            int wikiIdUsedForIngredient = 0;

            foreach (var recipe in recipes)
            {
                if (!TryResolveId(
                    cache,
                    recipe.OutputName,
                    recipe.OutputGameId,
                    out int outputId,
                    out bool outputFromWiki))
                {
                    Console.WriteLine(
                        $"  SKIP: {recipe.OutputName}" +
                        " - output ID unresolved");
                    skippedOutput++;
                    continue;
                }

                if (outputFromWiki)
                {
                    wikiIdUsedForOutput++;
                }

                bool valid = true;
                var ingredients = new List<RecipeIngredient>();

                foreach (var ing in recipe.Ingredients)
                {
                    if (ing.Quantity <= 0)
                    {
                        Console.WriteLine(
                            $"  SKIP: {recipe.OutputName}" +
                            $" - ingredient '{ing.Name}' count <= 0");
                        skippedQuantity++;
                        valid = false;
                        break;
                    }

                    if (!TryResolveId(
                        cache,
                        ing.Name,
                        ing.GameId,
                        out int ingId,
                        out bool ingFromWiki))
                    {
                        // Whole recipe, not just this ingredient: a recipe
                        // emitted with an ingredient missing costs less than
                        // it really does, and the solver would rank that
                        // cheaper-than-reality recipe first.
                        Console.WriteLine(
                            $"  SKIP: {recipe.OutputName}" +
                            $" - ingredient '{ing.Name}' ID unresolved");
                        skippedIngredient++;
                        valid = false;
                        break;
                    }

                    if (ingFromWiki)
                    {
                        wikiIdUsedForIngredient++;
                    }

                    ingredients.Add(new RecipeIngredient
                    {
                        Id = ingId,
                        Count = ing.Quantity,
                    });
                }

                if (!valid)
                {
                    continue;
                }

                validRecipes.Add(new ValidRecipe
                {
                    OutputItemId = outputId,
                    OutputItemCount = recipe.OutputQuantity,
                    OutputName = recipe.OutputName,
                    Ingredients = ingredients,
                });
            }

            int skipped = skippedOutput + skippedIngredient + skippedQuantity;

            // Deterministic sort: by outputItemId, then by OutputName for ties
            validRecipes = validRecipes
                .OrderBy(r => r.OutputItemId)
                .ThenBy(r => r.OutputName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Two wiki pages can document one forge recipe (e.g. "1 Mystic
            // Clover or other materials (random)" and "Mystic Clover
            // Average"). Step 1 dedups on output name, which cannot see
            // that, and shipping both would put two identical options in
            // front of the user and split any hand-authored override across
            // them. First in the sort above wins, so the survivor is stable.
            int duplicateContent = validRecipes.Count;
            var seenContent = new HashSet<string>(StringComparer.Ordinal);
            validRecipes = validRecipes
                .Where(r => seenContent.Add(BuildContentKey(r)))
                .ToList();
            duplicateContent -= validRecipes.Count;

            Console.WriteLine($"  Valid recipes: {validRecipes.Count}");
            Console.WriteLine(
                $"  Skipped: {skipped}" +
                $" (output unresolved {skippedOutput}," +
                $" ingredient unresolved {skippedIngredient}," +
                $" ingredient count <= 0 {skippedQuantity})");
            Console.WriteLine(
                $"  IDs taken from the wiki's own recipe fields:" +
                $" {wikiIdUsedForOutput} output(s)," +
                $" {wikiIdUsedForIngredient} ingredient(s)");
            Console.WriteLine(
                $"  Dropped as duplicate content: {duplicateContent}");

            CarryForwardHandAuthoredOverrides(outputPath, validRecipes);
            Console.WriteLine();

            // ================================================================
            // Step 5: Write output
            // ================================================================
            Console.WriteLine("=== Step 5: Write output ===");

            if (dryRun)
            {
                Console.WriteLine(
                    "  DRY RUN - skipping recipe file write.");
            }
            else
            {
                WriteRecipeFileAtomic(outputPath, validRecipes);
                var fileInfo = new FileInfo(outputPath);
                Console.WriteLine($"  Written to: {outputPath}");
                Console.WriteLine(
                    $"  File size: {fileInfo.Length:N0} bytes");
            }

            Console.WriteLine();

            // ================================================================
            // Summary
            // ================================================================
            Console.WriteLine("=== Mystic Forge Seeder Complete ===");
            Console.WriteLine($"Recipes written: {validRecipes.Count}");
            Console.WriteLine(
                $"Recipes skipped: {skipped} (see warnings above)");
            int totalUnresolved = cache.Values.Count(v => v == -1);
            Console.WriteLine($"Unresolved names: {totalUnresolved}");
            Console.WriteLine("Next steps:");
            Console.WriteLine(
                "  dotnet run --project " +
                "tools/TaimisToolbench.RecipeSeeder/" +
                "TaimisToolbench.RecipeSeeder.csproj");
            Console.WriteLine(
                "  dotnet build TaimisToolbench.csproj -p:Platform=x64");
            Console.WriteLine(
                "  dotnet test tests/TaimisToolbench.Tests/" +
                "TaimisToolbench.Tests.csproj");

            return 0;
        }

        /// <summary>
        /// Restores the expectedOutputCount overrides (and the comments
        /// carrying their provenance) that a previous run's output file
        /// holds, onto the recipes this run rebuilt.
        /// <para>
        /// Nothing on the wiki expresses expected output: recipe -1591's
        /// 0.31 for the 1-clover Mystic Forge gamble came from a community
        /// study, was written into ref/mystic_forge_recipes.json by hand,
        /// and a rewrite drops it. Losing it does not read as a gap - it
        /// makes a 6-Philosopher's-Stone gamble look like a guaranteed
        /// clover, so the solver prices clovers at about a third of what
        /// they cost and confidently recommends the cheaper plan.
        /// </para>
        /// <para>
        /// Matching is on recipe content, not on id: ids renumber on every
        /// run, so an id is the one thing about a regenerated recipe that
        /// carries nothing forward. Run after the content dedup above, so
        /// one override can only ever land on one recipe.
        /// </para>
        /// </summary>
        private static void CarryForwardHandAuthoredOverrides(
            string previousPath, List<ValidRecipe> recipes)
        {
            if (!File.Exists(previousPath))
            {
                return;
            }

            var overrides = new Dictionary<string, (double Ev, string? Comment)>(
                StringComparer.Ordinal);

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(previousPath));
                if (!doc.RootElement.TryGetProperty("recipes", out var arr) ||
                    arr.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                foreach (var entry in arr.EnumerateArray())
                {
                    if (!entry.TryGetProperty("expectedOutputCount", out var ev) ||
                        ev.ValueKind != JsonValueKind.Number ||
                        !ev.TryGetDouble(out double evValue))
                    {
                        continue;
                    }

                    string? key = TryBuildPreviousContentKey(entry);
                    if (key == null)
                    {
                        continue;
                    }

                    string? comment =
                        entry.TryGetProperty("comment", out var c) &&
                        c.ValueKind == JsonValueKind.String
                            ? c.GetString()
                            : null;

                    overrides[key] = (evValue, comment);
                }
            }
            catch (JsonException)
            {
                Console.WriteLine(
                    "  WARNING: previous recipe file unreadable - any" +
                    " hand-authored expectedOutputCount override in it is" +
                    " NOT carried forward.");
                return;
            }

            if (overrides.Count == 0)
            {
                return;
            }

            var matched = new HashSet<string>(StringComparer.Ordinal);
            int applied = 0;

            foreach (var recipe in recipes)
            {
                string key = BuildContentKey(recipe);
                if (!overrides.TryGetValue(key, out var carried))
                {
                    continue;
                }

                recipe.ExpectedOutputCount = carried.Ev;
                if (!string.IsNullOrEmpty(carried.Comment))
                {
                    recipe.CommentOverride = carried.Comment;
                }

                matched.Add(key);
                applied++;
            }

            Console.WriteLine(
                $"  Hand-authored expectedOutputCount overrides:" +
                $" {overrides.Count} in the previous file," +
                $" applied to {applied} regenerated recipe(s)");

            foreach (var key in overrides.Keys)
            {
                if (!matched.Contains(key))
                {
                    Console.WriteLine(
                        "  WARNING: expectedOutputCount override LOST - no" +
                        $" regenerated recipe matches {key}." +
                        " Restore it by hand or the solver will price that" +
                        " recipe as a guaranteed output.");
                }
            }
        }

        private static string BuildContentKey(ValidRecipe recipe)
        {
            var ingredients = recipe.Ingredients
                .Select(i => $"{i.Id}x{i.Count}")
                .OrderBy(s => s, StringComparer.Ordinal);

            return $"{recipe.OutputItemId}/{recipe.OutputItemCount}/" +
                string.Join(",", ingredients);
        }

        private static string? TryBuildPreviousContentKey(JsonElement entry)
        {
            if (!entry.TryGetProperty("outputItemId", out var outId) ||
                !outId.TryGetInt32(out int outputItemId) ||
                !entry.TryGetProperty("outputItemCount", out var outCount) ||
                !outCount.TryGetInt32(out int outputItemCount) ||
                !entry.TryGetProperty("ingredients", out var ings) ||
                ings.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var ing in ings.EnumerateArray())
            {
                if (!ing.TryGetProperty("id", out var idEl) ||
                    !idEl.TryGetInt32(out int id) ||
                    !ing.TryGetProperty("count", out var countEl) ||
                    !countEl.TryGetInt32(out int count))
                {
                    return null;
                }

                parts.Add($"{id}x{count}");
            }

            parts.Sort(StringComparer.Ordinal);
            return $"{outputItemId}/{outputItemCount}/" + string.Join(",", parts);
        }

        /// <summary>
        /// Tracks canonical name for case-insensitive dedup.
        /// Logs collisions where two different-case variants exist.
        /// Keeps the first-seen variant as canonical.
        /// </summary>
        private static void TrackCanonicalName(
            Dictionary<string, string> canonicalNames, string name)
        {
            string? trimmed = name?.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                return;
            }

            if (canonicalNames.TryGetValue(trimmed, out string? existing))
            {
                // Case-insensitive match exists; log if cases differ
                if (!string.Equals(existing, trimmed, StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        $"  CASE COLLISION: '{trimmed}' vs '{existing}'" +
                        " - keeping first seen");
                }
            }
            else
            {
                canonicalNames[trimmed] = trimmed;
            }
        }

        /// <summary>
        /// Resolves one output/ingredient name to a GW2 item id, preferring
        /// the id the name itself resolved to and falling back to the id the
        /// wiki's recipe subobject asserts.
        /// <para>
        /// Neither source is redundant and the precedence is not arbitrary:
        /// the wiki derives an unasserted id by a name lookup that picks one
        /// arbitrary member of a same-name pair, while an asserted id is the
        /// only id a multi-variant equipment page carries at all. Both
        /// cases, with the wiki pages they were measured on:
        /// docs/ARCHITECTURE.md section 9.
        /// </para>
        /// </summary>
        private static bool TryResolveId(
            Dictionary<string, int> cache,
            string name,
            int? wikiGameId,
            out int id,
            out bool fromWiki)
        {
            fromWiki = false;

            if (!string.IsNullOrEmpty(name) &&
                cache.TryGetValue(name, out id) &&
                id > 0)
            {
                return true;
            }

            if (wikiGameId.HasValue && wikiGameId.Value > 0)
            {
                id = wikiGameId.Value;
                fromWiki = true;
                return true;
            }

            id = 0;
            return false;
        }

        /// <summary>
        /// Writes the recipe file using Utf8JsonWriter for deterministic
        /// property order. Atomic: writes to .tmp then moves.
        ///
        /// Property order (invariant):
        ///   root: schemaVersion, recipes
        ///   recipe: id, outputItemId, outputItemCount,
        ///           expectedOutputCount (only when set), ingredients,
        ///           comment
        ///   ingredient: type, id, count
        ///
        /// Ids run from <see cref="RecipeIdBase"/> downwards - see that
        /// constant for the partition they have to stay inside.
        /// </summary>
        private static void WriteRecipeFileAtomic(
            string outputPath, List<ValidRecipe> recipes)
        {
            string tmpPath = outputPath + ".tmp";

            using (var stream = new FileStream(
                tmpPath, FileMode.Create, FileAccess.Write))
            {
                using var writer = new Utf8JsonWriter(stream,
                    new JsonWriterOptions { Indented = true });

                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", 1);
                writer.WriteStartArray("recipes");

                int recipeId = RecipeIdBase;
                foreach (var recipe in recipes)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("id", recipeId);
                    writer.WriteNumber("outputItemId", recipe.OutputItemId);
                    writer.WriteNumber(
                        "outputItemCount", recipe.OutputItemCount);

                    if (recipe.ExpectedOutputCount.HasValue)
                    {
                        writer.WriteNumber(
                            "expectedOutputCount",
                            recipe.ExpectedOutputCount.Value);
                    }

                    writer.WriteStartArray("ingredients");
                    foreach (var ing in recipe.Ingredients)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "Item");
                        writer.WriteNumber("id", ing.Id);
                        writer.WriteNumber("count", ing.Count);
                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();

                    writer.WriteString("comment",
                        recipe.CommentOverride
                            ?? $"{recipe.OutputName} (from wiki)");
                    writer.WriteEndObject();

                    recipeId--;
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            File.Move(tmpPath, outputPath, overwrite: true);
        }

        private static Dictionary<string, int> LoadCache(string path)
        {
            var cache = new Dictionary<string, int>(
                StringComparer.OrdinalIgnoreCase);

            if (!File.Exists(path))
            {
                return cache;
            }

            try
            {
                string json = File.ReadAllText(path);
                using var doc = JsonDocument.Parse(json);
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    cache[prop.Name] = prop.Value.GetInt32();
                }

                Console.WriteLine(
                    $"  Loaded cache ({cache.Count} entries) from {path}");
            }
            catch
            {
                Console.WriteLine(
                    "  WARNING: Cache file corrupt, starting fresh.");
            }

            return cache;
        }

        private static void SaveCacheAtomic(
            string path, Dictionary<string, int> cache)
        {
            string tmpPath = path + ".tmp";

            var sorted = cache
                .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(sorted, options);

            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, path, overwrite: true);
            Console.WriteLine(
                $"  Saved cache ({cache.Count} entries) to {path}");
        }

        private static string FindRepoRoot()
        {
            string? dir = AppContext.BaseDirectory;
            while (dir != null)
            {
                // A linked worktree's ".git" is a file holding a gitdir
                // pointer, not a directory, so a directory-only probe walks
                // past the worktree root and rewrites ref/ in whichever repo
                // it hits next - or in the process's working directory.
                string gitPath = Path.Combine(dir, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return Directory.GetCurrentDirectory();
        }
    }

    internal class ValidRecipe
    {
        public int OutputItemId { get; set; }

        public int OutputItemCount { get; set; }

        // Always set at construction (Step 4's one object initializer).
        public string OutputName { get; set; } = string.Empty;

        /// <summary>
        /// Carried forward from the previous output file, never derived
        /// from the wiki - see CarryForwardHandAuthoredOverrides.
        /// </summary>
        public double? ExpectedOutputCount { get; set; }

        /// <summary>
        /// The previous file's comment for a recipe whose override was
        /// carried forward, kept so the override's provenance survives the
        /// rewrite. Null for every generated comment.
        /// </summary>
        public string? CommentOverride { get; set; }

        public List<RecipeIngredient> Ingredients { get; set; }
            = new List<RecipeIngredient>();
    }

    internal class RecipeIngredient
    {
        public int Id { get; set; }

        public int Count { get; set; }
    }
}
