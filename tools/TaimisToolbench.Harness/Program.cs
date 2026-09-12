using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Services.Diagnostics;
using TaimisToolbench.Services.Recipes;

namespace TaimisToolbench.Harness
{
    // --- Null API clients for offline mode ---
    internal class NullRecipeApiClient : IRecipeApiClient
    {
        public Task<RecipeSearchResult> SearchByOutputAsync(int itemId, CancellationToken ct)
        {
            // Offline mode answers for the endpoint, so its empties are as
            // final as a 2xx body's - nothing here is a degraded API.
            return Task.FromResult(
                new RecipeSearchResult(Array.Empty<int>(), absenceProven: true));
        }

        public Task<RawRecipe> GetRecipeAsync(int recipeId, CancellationToken ct)
        {
            return Task.FromResult<RawRecipe>(null);
        }
    }

    internal class NullPriceApiClient : IPriceApiClient
    {
        public Task<PriceBatchResult> GetPricesAsync(
            IReadOnlyList<int> itemIds, CancellationToken ct)
        {
            return Task.FromResult(
                new PriceBatchResult(Array.Empty<RawPriceEntry>(), absenceProven: true));
        }
    }

    internal class NullItemApiClient : IItemApiClient
    {
        public Task<IReadOnlyList<RawItem>> GetItemsAsync(
            IReadOnlyList<int> itemIds, CancellationToken ct)
        {
            return Task.FromResult<IReadOnlyList<RawItem>>(Array.Empty<RawItem>());
        }
    }

    // --- Profile item definition ---
    internal class ProfileItem
    {
        public string Name { get; set; }

        public int ItemId { get; set; }

        public int Quantity { get; set; }

        public bool RequiresLive { get; set; }
    }

    // --- Program ---
    internal class Program
    {
        private static int Main(string[] args)
        {
            return MainAsync(args).GetAwaiter().GetResult();
        }

        private static async Task<int> MainAsync(string[] args)
        {
            if (args.Contains("--fetch-profile"))
            {
                return await FetchProfiler.RunAsync(args);
            }

            // Parse CLI arguments
            int profile = -1;
            int iterations = 1;
            bool live = false;
            bool raw = false;
            bool printCacheStats = false;
            bool clearOverlayCache = false;
            bool dumpTree = false;
            bool classify = false;
            bool forceCraftRoot = false;
            bool alloc = false;
            int drift = 0;
            bool startupTiming = false;
            List<int> itemOverrides = null;
            // -1 = not specified -> pipeline default
            // (HomesteadEfficiencyTiers.Default, tier 0 for every material).
            // Applies the SAME tier uniformly to Fiber/Metal/Wood - a single
            // flag is sufficient for this offline verification tool; the
            // live module exposes the three independently (Settings tab).
            int homesteadTier = -1;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--profile":
                        if (i + 1 < args.Length)
                        {
                            profile = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        }

                        break;
                    case "--iterations":
                        if (i + 1 < args.Length)
                        {
                            iterations = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        }

                        break;
                    case "--live":
                        live = true;
                        break;
                    case "--raw":
                        raw = true;
                        break;
                    case "--print-cache-stats":
                        printCacheStats = true;
                        break;
                    case "--clear-overlay-cache":
                        clearOverlayCache = true;
                        break;
                    case "--dump-tree":
                        dumpTree = true;
                        break;
                    case "--classify":
                        classify = true;
                        break;
                    case "--force-craft-root":
                        forceCraftRoot = true;
                        break;
                    case "--alloc":
                        alloc = true;
                        break;
                    case "--drift":
                        if (i + 1 < args.Length)
                        {
                            drift = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        }

                        break;
                    case "--startup-timing":
                        startupTiming = true;
                        break;
                    case "--items":
                        if (i + 1 < args.Length)
                        {
                            itemOverrides = new List<int>();
                            foreach (var part in args[++i].Split(','))
                            {
                                itemOverrides.Add(
                                    int.Parse(part.Trim(), CultureInfo.InvariantCulture));
                            }
                        }

                        break;
                    case "--homestead-tier":
                        if (i + 1 < args.Length)
                        {
                            homesteadTier = int.Parse(args[++i], CultureInfo.InvariantCulture);
                            // HomesteadEfficiencyTiers'
                            // constructor throws ArgumentOutOfRangeException for
                            // any tier > 2, and this flag's own usage string below
                            // documents <0|1|2> as the only valid values - reject
                            // out-of-range input here with a usage error instead
                            // of letting the exception crash the tool, mirroring
                            // ModuleSettings.ClampTier's own 0-2 range.
                            if (homesteadTier < 0 || homesteadTier > 2)
                            {
                                Console.Error.WriteLine(
                                    $"Invalid --homestead-tier value: {homesteadTier} " +
                                    "(must be 0, 1, or 2).");
                                return 1;
                            }
                        }

                        break;
                }
            }

            if (profile < 0 && itemOverrides == null)
            {
                Console.Error.WriteLine(
                    "Usage: TaimisToolbench.Harness --profile <n> " +
                    "[--iterations <n>] [--live] [--raw] " +
                    "[--print-cache-stats] [--clear-overlay-cache] [--dump-tree] " +
                    "[--classify] [--force-craft-root] " +
                    "[--homestead-tier <0|1|2>] " +
                    "[--alloc] [--drift <n>] [--startup-timing] [--items <id,id,...>]\n"
                    + "   or: TaimisToolbench.Harness --fetch-profile [--dry-run] "
                    + "[--per-minute <n>] [--max-requests <n>] [--characters <n>] "
                    + "[--only <substring>] [--out <dir>]");
                return 1;
            }

            // Get profile items
            List<ProfileItem> items;
            if (itemOverrides != null)
            {
                items = new List<ProfileItem>();
                foreach (int id in itemOverrides)
                {
                    items.Add(new ProfileItem
                    {
                        Name = $"Item {id}",
                        ItemId = id,
                        Quantity = 1,
                        RequiresLive = false,
                    });
                }
            }
            else
            {
                items = HarnessProfiles.GetProfileItems(profile, live);
            }

            if (items == null || items.Count == 0)
            {
                Console.Error.WriteLine($"Unknown profile: {profile}");
                return 1;
            }

            string mode = live ? "live" : "offline";
            Console.WriteLine($"Profile {profile} | Mode: {mode} | Iterations: {iterations}");
            Console.WriteLine();

            // Build pipeline
            HttpClient httpClient = null;
            var stepSw = new Stopwatch();

            void ReportStartup(string label)
            {
                if (startupTiming)
                {
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "[startup] {0}: {1}ms", label, stepSw.ElapsedMilliseconds));
                }
            }

            try
            {
                IRecipeApiClient recipeApi;
                IPriceApiClient priceApi;
                IItemApiClient itemApi;

                // Loaded once, wired both into the composite API client
                // and (below) merged into the recipe seed - mirroring
                // Module.cs, so the corpus the derived negatives are built
                // from includes the forge recipes here too.
                stepSw.Restart();
                var mfData = RecipeClientFactory.LoadData(new FileMysticForgeRecipeSource());
                ReportStartup("Mystic Forge data load");

                if (live)
                {
                    httpClient = new HttpClient();
                    var rawRecipeApi = new Gw2RecipeApiClient(httpClient);
                    recipeApi = RecipeClientFactory.Create(rawRecipeApi, mfData);
                    priceApi = new Gw2PriceApiClient(httpClient);
                    itemApi = new Gw2ItemApiClient(httpClient);
                }
                else
                {
                    var nullRecipe = new NullRecipeApiClient();
                    recipeApi = RecipeClientFactory.Create(nullRecipe, mfData);
                    priceApi = new NullPriceApiClient();
                    itemApi = new NullItemApiClient();
                }

                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string dataDir = Path.Combine(baseDir, "harness_data");
                Directory.CreateDirectory(dataDir);

                var vendorLoader = new VendorOfferLoader();
                var vendorStore = new VendorOfferStore(dataDir, vendorLoader);
                string vendorBaseline = Path.Combine(baseDir, "ref", "vendor_offers.json");
                stepSw.Restart();
                if (File.Exists(vendorBaseline))
                {
                    using (var stream = File.OpenRead(vendorBaseline))
                    {
                        vendorStore.LoadBaseline(stream);
                    }
                }
                else
                {
                    vendorStore.LoadBaseline(null);
                }

                ReportStartup("Vendor offer baseline load");

                // Recipe cache: seed + overlay
                var recipeSeed = new SeededRecipeCacheStore();
                string seedSearchPath = Path.Combine(baseDir, "ref", "recipe_search_seed.json");
                string seedRecipesPath = Path.Combine(baseDir, "ref", "recipes_seed.json");
                stepSw.Restart();
                if (File.Exists(seedSearchPath) && File.Exists(seedRecipesPath))
                {
                    using (var s1 = File.OpenRead(seedSearchPath))
                    using (var s2 = File.OpenRead(seedRecipesPath))
                    {
                        recipeSeed.Load(s1, s2);
                    }
                }

                ReportStartup("Recipe seed load (search + recipes)");

                stepSw.Restart();
                recipeSeed.MergeMysticForgeRecipes(mfData);
                recipeSeed.FinalizeIndex();
                ReportStartup("Merge MF recipes + FinalizeIndex");

                string seedManifestPath = Path.Combine(baseDir, "ref", "recipe_seed_manifest.json");
                if (File.Exists(seedManifestPath))
                {
                    using (var ms = File.OpenRead(seedManifestPath))
                    {
                        recipeSeed.LoadManifest(ms);
                    }
                }

                var recipeOverlay = new OverlayRecipeCacheStore(dataDir);

                if (clearOverlayCache)
                {
                    Console.WriteLine("Clearing overlay cache...");
                    string overlayCacheDir = Path.Combine(dataDir, "recipe_cache");
                    if (Directory.Exists(overlayCacheDir))
                    {
                        Directory.Delete(overlayCacheDir, recursive: true);
                    }
                }

                {
                    recipeOverlay.Load();

                    int? buildId = null;
                    if (live && httpClient != null)
                    {
                        try
                        {
                            buildId = await FetchBuildIdAsync(httpClient);
                        }
                        catch
                        {
                        }
                    }

                    if (buildId.HasValue)
                    {
                        recipeOverlay.SetCurrentBuildId(buildId.Value);
                        recipeSeed.SetCurrentBuildId(buildId.Value);
                    }
                }

                // Item name seed: reused as the ItemMetadataService fallback
                // (mirrors Module.cs) so offline/live runs resolve real
                // names instead of "Unknown Item" for every node - matches
                // what the live module always has wired.
                ItemNameSeedData itemNameSeed = null;
                string itemNameSeedPath = Path.Combine(baseDir, "ref", "item_name_seed.json");
                stepSw.Restart();
                if (File.Exists(itemNameSeedPath))
                {
                    using (var nameStream = File.OpenRead(itemNameSeedPath))
                    {
                        ItemSearchProviderFactory.Create(nameStream, out _, out itemNameSeed);
                    }
                }

                ReportStartup("Item name seed load");

                // Acquisition hints seed (docs/KNOWN-ISSUES #8/#17),
                // mirroring Module.cs so Unknown-decision nodes carry the
                // same tooltip/badge data the live module would show.
                IReadOnlyDictionary<int, AcquisitionHint> acquisitionHints = null;
                string hintsSeedPath = Path.Combine(baseDir, "ref", "acquisition_hints_seed.json");
                if (File.Exists(hintsSeedPath))
                {
                    using (var hintsStream = File.OpenRead(hintsSeedPath))
                    {
                        acquisitionHints = AcquisitionHintService.Load(hintsStream);
                    }
                }

                var recipeCacheStore = new CompositeRecipeCacheStore(recipeSeed, recipeOverlay);

                var solver = new PlanSolver();
                var pipeline = new CraftingPlanPipeline(
                    new RecipeService(recipeApi, cacheStore: recipeCacheStore),
                    new TradingPostService(priceApi),
                    solver,
                    new ItemMetadataService(itemApi, itemNameSeed),
                    vendorStore,
                    reducer: new InventoryReducer(),
                    accountRecipeClient: null,
                    currencyMetadataService: null,
                    acquisitionHints: acquisitionHints);

                // Null (unspecified --homestead-tier)
                // -> GenerateStructuredAsync's own default
                // (HomesteadEfficiencyTiers.Default, tier 0 everywhere).
                HomesteadEfficiencyTiers homesteadTiers = null;
                if (homesteadTier >= 0)
                {
                    homesteadTiers = new HomesteadEfficiencyTiers(new Dictionary<int, int>
                    {
                        { Gw2Constants.RefinedHomesteadFiberItemId, homesteadTier },
                        { Gw2Constants.RefinedHomesteadMetalItemId, homesteadTier },
                        { Gw2Constants.RefinedHomesteadWoodItemId, homesteadTier },
                    });
                    Console.WriteLine($"Homestead efficiency tier: {homesteadTier} (all materials)");
                    Console.WriteLine();
                }

                // ModuleSettings.GetEffectiveCurrencyValuation materializes
                // the curated defaults before handing a CurrencyValuation to
                // the solver; a raw instance (what a null argument becomes)
                // silently yields ZERO curated valuations, which is not what
                // any running module does. See CurrencyValuation.WithDefaults.
                var valuation = CurrencyValuation.WithDefaults(CurrencyValuation.None);

                if (alloc)
                {
                    await AllocBench.RunAsync(pipeline, items[0], solver);
                }
                else if (drift > 0)
                {
                    await RunDrift(pipeline, items, drift, valuation, homesteadTiers);
                }
                else if (classify)
                {
                    await TerminalClassifier.RunAsync(
                        pipeline, items, mode, valuation, homesteadTiers, forceCraftRoot);
                }
                else
                {
                    // Run each profile item
                    foreach (var item in items)
                    {
                        if (dumpTree)
                        {
                            await DumpItemTree(pipeline, item, mode, valuation, homesteadTiers);
                        }
                        else
                        {
                            await RunItemProfile(
                                pipeline, item, iterations, raw, mode, valuation, homesteadTiers);
                        }

                        Console.WriteLine();
                    }
                }

                if (printCacheStats)
                {
                    var stats = recipeCacheStore.Stats;
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "Recipe cache: Search hits={0} misses={1} | Recipe hits={2} misses={3}",
                        stats.SearchHits, stats.SearchMisses,
                        stats.RecipeHits, stats.RecipeMisses));
                }
            }
            finally
            {
                httpClient?.Dispose();
            }

            return 0;
        }

        /// <summary>
        /// Runs a single generation and prints the raw pre-solve RecipeNode
        /// tree (recipe availability, independent of pricing) next to the
        /// solved CraftingTreeNode tree (the decision/pricing the live
        /// module would render), for item-by-item parity
        /// research. Ids are printed freely here - this is a dev-only tool,
        /// not the module's UI (see the CLAUDE.md no-displayed-ids
        /// invariant, which only governs runtime UI surfaces).
        /// </summary>
        private static async Task DumpItemTree(
            CraftingPlanPipeline pipeline, ProfileItem item, string mode,
            CurrencyValuation valuation,
            HomesteadEfficiencyTiers homesteadTiers = null)
        {
            Console.WriteLine($"=== {item.Name} ({item.ItemId}) x{item.Quantity} -- tree dump [{mode}] ===");
            Console.WriteLine();

            var result = await pipeline.GenerateStructuredAsync(
                item.ItemId, item.Quantity, null, CancellationToken.None,
                currencyValuation: valuation, homesteadTiers: homesteadTiers);

            Console.WriteLine("--- Raw pre-solve recipe tree (node.Recipes.Count = seed coverage) ---");
            if (result.SolveContext != null && result.SolveContext.Tree != null)
            {
                DumpRawNode(result.SolveContext.Tree, 0);
            }
            else
            {
                Console.WriteLine("(no raw tree available)");
            }

            Console.WriteLine();
            Console.WriteLine("--- Solved crafting tree (decision the live module would render) ---");
            if (result.CraftingTree != null)
            {
                DumpSolvedNode(result.CraftingTree, 0);
            }
            else
            {
                Console.WriteLine("(no solved tree available)");
            }
        }

        private const int MaxDumpDepth = 100;

        private static void DumpRawNode(RecipeNode node, int depth)
        {
            if (depth > MaxDumpDepth)
            {
                Console.WriteLine($"{Indent(depth)}... (max depth {MaxDumpDepth} reached, truncated)");
                return;
            }

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}[{1}] id={2} qty={3} recipeCount={4}",
                Indent(depth), node.IngredientType, node.Id, node.Quantity, node.Recipes.Count));

            foreach (var recipe in node.Recipes)
            {
                string evSuffix = recipe.ExpectedOutputCount != recipe.OutputCount
                    ? string.Format(CultureInfo.InvariantCulture, " ev={0}", recipe.ExpectedOutputCount)
                    : string.Empty;
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}  recipe={1} output={2}{3} craftsNeeded={4}",
                    Indent(depth), recipe.RecipeId, recipe.OutputCount, evSuffix, recipe.CraftsNeeded));

                foreach (var ingredient in recipe.Ingredients)
                {
                    DumpRawNode(ingredient, depth + 2);
                }
            }
        }

        private static void DumpSolvedNode(CraftingTreeNode node, int depth)
        {
            if (depth > MaxDumpDepth)
            {
                Console.WriteLine($"{Indent(depth)}... (max depth {MaxDumpDepth} reached, truncated)");
                return;
            }

            string flags = string.Format(
                CultureInfo.InvariantCulture,
                "craft={0} tp={1} vendor={2}",
                node.CanCraft, node.CanBuyTp, node.CanBuyVendor);
            string unitCost = node.UnitCost.HasValue
                ? node.UnitCost.Value.ToString(CultureInfo.InvariantCulture)
                : "-";
            string subtreeCost = node.SubtreeCost.HasValue
                ? node.SubtreeCost.Value.ToString(CultureInfo.InvariantCulture)
                : "-";
            string badge = !string.IsNullOrEmpty(node.AcquisitionBadge)
                ? string.Format(CultureInfo.InvariantCulture, " badge={0}", node.AcquisitionBadge)
                : string.Empty;
            string reference = node.IsReferenceBranch ? " [reference]" : string.Empty;

            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}id={1} name=\"{2}\" qty={3} decision={4} ({5}) unit={6}c subtree={7}c{8}{9}",
                Indent(depth), node.ItemId, node.Name, node.Quantity, node.Decision,
                flags, unitCost, subtreeCost, badge, reference));

            if (node.VendorCurrencyCosts != null && node.VendorCurrencyCosts.Count > 0)
            {
                foreach (var cost in node.VendorCurrencyCosts)
                {
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}  currency type={1} id={2} count={3}",
                        Indent(depth), cost.Type, cost.Id, cost.Count));
                }
            }

            foreach (var child in node.Children)
            {
                DumpSolvedNode(child, depth + 1);
            }
        }

        private static string Indent(int depth)
        {
            return new string(' ', depth * 2);
        }

        private static async Task RunItemProfile(
            CraftingPlanPipeline pipeline,
            ProfileItem item,
            int iterations,
            bool raw,
            string mode,
            CurrencyValuation valuation,
            HomesteadEfficiencyTiers homesteadTiers = null)
        {
            Console.WriteLine($"=== {item.Name} ({item.ItemId}) x{item.Quantity} -- {iterations} iteration(s) [{mode}] ===");

            var allParsed = new List<List<PlanTimingAnalyzer.ParsedPhase>>();

            for (int i = 0; i < iterations; i++)
            {
                var result = await pipeline.GenerateStructuredAsync(
                    item.ItemId, item.Quantity, null, CancellationToken.None,
                    currencyValuation: valuation, homesteadTiers: homesteadTiers);

                // Extract timing lines (everything before the summary header)
                var timingLines = new List<string>();
                if (result.DebugLog != null)
                {
                    foreach (var line in result.DebugLog)
                    {
                        if (line == "--- Timing Summary ---")
                        {
                            break;
                        }

                        timingLines.Add(line);
                    }
                }

                var parsed = PlanTimingAnalyzer.Parse(timingLines);
                allParsed.Add(parsed);

                if (raw)
                {
                    Console.WriteLine($"  Iteration {i + 1}:");
                    foreach (var phase in parsed)
                    {
                        Console.WriteLine($"    {phase.Name}: {phase.ElapsedMs}ms");
                    }
                }
            }

            if (allParsed.Count == 0 || allParsed[0].Count == 0)
            {
                Console.WriteLine("  No timing data collected.");
                return;
            }

            Console.WriteLine();

            // --- Cold run (iteration 1) ---
            var cold = allParsed[0];
            long coldTotal = cold.Sum(p => p.ElapsedMs);

            Console.WriteLine("[COLD RUN]");
            Console.WriteLine(string.Format(
                CultureInfo.InvariantCulture, "Total: {0}ms", coldTotal));

            var coldSorted = cold.OrderByDescending(p => p.ElapsedMs).ToList();
            foreach (var phase in coldSorted)
            {
                double pct = coldTotal > 0
                    ? (double)phase.ElapsedMs / coldTotal * 100.0
                    : 0.0;
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}: {1}ms ({2:F1}%)",
                    phase.Name, phase.ElapsedMs, pct));
            }

            // --- Warm median (iterations 2+) ---
            if (allParsed.Count > 1)
            {
                var warmRuns = allParsed.Skip(1).ToList();
                var phaseNames = allParsed[0].Select(p => p.Name).ToList();
                var warmPhaseData = new Dictionary<string, List<long>>();
                var warmTotals = new List<long>();

                foreach (var name in phaseNames)
                {
                    warmPhaseData[name] = new List<long>();
                }

                foreach (var run in warmRuns)
                {
                    long runTotal = 0;
                    foreach (var phase in run)
                    {
                        if (warmPhaseData.ContainsKey(phase.Name))
                        {
                            warmPhaseData[phase.Name].Add(phase.ElapsedMs);
                        }

                        runTotal += phase.ElapsedMs;
                    }

                    warmTotals.Add(runTotal);
                }

                foreach (var entry in warmPhaseData)
                {
                    entry.Value.Sort();
                }

                warmTotals.Sort();

                double warmMedianTotal = Median(warmTotals);

                Console.WriteLine();
                Console.WriteLine("[WARM MEDIAN]");
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture, "Total: {0}ms", (long)warmMedianTotal));

                var warmStats = phaseNames
                    .Where(name => warmPhaseData[name].Count > 0)
                    .Select(name =>
                    {
                        var data = warmPhaseData[name];
                        double med = Median(data);
                        double pct = warmMedianTotal > 0
                            ? med / warmMedianTotal * 100.0
                            : 0.0;
                        return new { Name = name, Med = med, Pct = pct };
                    })
                    .OrderByDescending(s => s.Med)
                    .ToList();

                foreach (var s in warmStats)
                {
                    Console.WriteLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: {1}ms ({2:F1}%)",
                        s.Name, (long)s.Med, s.Pct));
                }
            }
        }

        /// <summary>
        /// Solves <paramref name="count"/> plans back to back in one
        /// process, cycling the given items, and reports per-solve wall
        /// time plus managed heap after a forced full collection. Flat
        /// time and flat memory across solves = no per-solve accumulation;
        /// growth = something retains per-solve state.
        /// </summary>
        private static async Task RunDrift(
            CraftingPlanPipeline pipeline,
            List<ProfileItem> items,
            int count,
            CurrencyValuation valuation,
            HomesteadEfficiencyTiers homesteadTiers)
        {
            Console.WriteLine($"=== Drift run: {count} back-to-back solves ===");
            Console.WriteLine("| Solve | Item | ms | Managed heap after GC (bytes) | Log ring entries |");
            Console.WriteLine("|---|---|---|---|---|");

            var sw = new Stopwatch();
            for (int i = 0; i < count; i++)
            {
                var item = items[i % items.Count];
                sw.Restart();

                // A non-positive id (--items 0) runs the loop without
                // generating - a control that separates growth caused by a
                // generation from growth caused by the measurement loop.
                if (item.ItemId > 0)
                {
                    await pipeline.GenerateStructuredAsync(
                        item.ItemId, item.Quantity, null, CancellationToken.None,
                        currencyValuation: valuation, homesteadTiers: homesteadTiers);
                }

                sw.Stop();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long heap = GC.GetTotalMemory(forceFullCollection: true);

                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "| {0} | {1} | {2} | {3:N0} | {4} |",
                    i + 1, item.Name, sw.ElapsedMilliseconds, heap,
                    ModuleLog.Shared.Version));
            }
        }

        private static double Median(List<long> sorted)
        {
            if (sorted.Count == 0)
            {
                return 0;
            }

            int mid = sorted.Count / 2;
            if (sorted.Count % 2 == 0)
            {
                return (sorted[mid - 1] + sorted[mid]) / 2.0;
            }

            return sorted[mid];
        }

        private static async Task<int> FetchBuildIdAsync(HttpClient httpClient)
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                var response = await httpClient.GetAsync(
                    "https://api.guildwars2.com/v2/build", cts.Token);
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
