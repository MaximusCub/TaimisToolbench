using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using VendorOfferUpdater.Models;

namespace VendorOfferUpdater
{
    internal class Program
    {
        /// <summary>Cap on deferred names listed by name. Counts are exact.</summary>
        private const int DeferredNamesListed = 20;

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
            catch (SafetyLimitException ex)
            {
                Console.Error.WriteLine($"SAFETY LIMIT: {ex.Message}");
                return 2;
            }
            catch (HttpRequestException ex)
            {
                Console.Error.WriteLine($"ERROR: Network request failed: {ex.Message}");
                return 1;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR: {ex.GetType().Name}: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args, CancellationToken ct)
        {
            string? outputPath = null;
            string? queryCondition = null;
            string? mergeIntoPath = null;
            bool dryRun = false;
            bool skipItemResolution = false;
            bool resolveOnly = false;
            int maxDepth = 2;
            int maxRequests = QueryOptions.DefaultMaxTotalRequests;
            int maxRuntimeMinutes = 30;
            int delayMs = QueryOptions.DefaultDelayBetweenRequestsMs;
            int maxAttempts = QueryOptions.DefaultMaxAttempts;
            bool allowCoverageDrop = false;
            bool recheckMisses = false;
            bool tagSeasonalFestivals = false;
            int maxSeasonalPages = 500;
            string? diffSummaryBefore = null;
            string? diffSummaryAfter = null;

            for (int i = 0; i < args.Length; i++)
            {
                // Matched on the flag alone, then arity-checked below, rather
                // than on "flag plus two operands". Every other flag here falls
                // through to the positional branch when short an operand, which
                // for this one would mean silently discarding a read-only
                // request and starting a 15-minute scrape that WRITES.
                if (args[i] == "--diff-summary")
                {
                    if (i + 2 >= args.Length)
                    {
                        Console.Error.WriteLine(
                            "ERROR: --diff-summary needs two paths: --diff-summary <old> <new>.");
                        return 1;
                    }

                    diffSummaryBefore = args[++i];
                    diffSummaryAfter = args[++i];
                }
                else if (args[i] == "--query" && i + 1 < args.Length)
                {
                    queryCondition = args[++i];
                }
                else if (args[i] == "--dry-run")
                {
                    dryRun = true;
                }
                else if (args[i] == "--max-depth" && i + 1 < args.Length)
                {
                    maxDepth = int.Parse(args[++i]);
                }
                else if (args[i] == "--max-requests" && i + 1 < args.Length)
                {
                    maxRequests = int.Parse(args[++i]);
                }
                else if (args[i] == "--max-runtime" && i + 1 < args.Length)
                {
                    maxRuntimeMinutes = int.Parse(args[++i]);
                }
                else if (args[i] == "--delay" && i + 1 < args.Length)
                {
                    delayMs = int.Parse(args[++i]);
                }
                else if (args[i] == "--skip-item-resolution")
                {
                    skipItemResolution = true;
                }
                else if (args[i] == "--resolve-item-currencies-only")
                {
                    resolveOnly = true;
                }
                else if (args[i] == "--merge-into" && i + 1 < args.Length)
                {
                    mergeIntoPath = args[++i];
                }
                else if (args[i] == "--max-attempts" && i + 1 < args.Length)
                {
                    maxAttempts = int.Parse(args[++i]);
                }
                else if (args[i] == "--allow-coverage-drop")
                {
                    allowCoverageDrop = true;
                }
                else if (args[i] == "--recheck-misses")
                {
                    recheckMisses = true;
                }
                else if (args[i] == "--tag-seasonal-festivals")
                {
                    tagSeasonalFestivals = true;
                }
                else if (args[i] == "--max-seasonal-pages" && i + 1 < args.Length)
                {
                    maxSeasonalPages = int.Parse(args[++i]);
                }
                else if (!args[i].StartsWith("--"))
                {
                    outputPath = args[i];
                }
            }

            // Unlike every other
            // int.Parse'd flag in this loop, --max-seasonal-pages is a
            // safety LIMIT - 0 or a negative value made every
            // --tag-seasonal-festivals run with any uncached page throw
            // SafetyLimitException immediately, with a message that reads
            // like a data problem ("exceeding --max-seasonal-pages (0)")
            // rather than a misconfigured argument.
            if (maxSeasonalPages <= 0)
            {
                Console.Error.WriteLine(
                    $"ERROR: --max-seasonal-pages must be a positive integer, got {maxSeasonalPages}.");
                return 1;
            }

            // --diff-summary is a read-only report over two dataset files, so
            // it short-circuits before any wiki/API setup and never writes.
            if (diffSummaryBefore != null && diffSummaryAfter != null)
            {
                return await RunDiffSummaryAsync(diffSummaryBefore, diffSummaryAfter);
            }

            if (maxAttempts <= 0)
            {
                Console.Error.WriteLine(
                    $"ERROR: --max-attempts must be a positive integer, got {maxAttempts}.");
                return 1;
            }

            var queryOptions = new QueryOptions
            {
                MaxPrefixDepth = maxDepth,
                MaxTotalRequests = maxRequests,
                MaxRuntime = TimeSpan.FromMinutes(maxRuntimeMinutes),
                DelayBetweenRequestsMs = delayMs,
                MaxAttempts = maxAttempts,
                DryRun = dryRun,
            };

            outputPath ??= Path.Combine(FindRepoRoot(), "ref", "vendor_offers.json");

            Console.WriteLine($"Output: {outputPath}");
            if (queryCondition != null)
            {
                Console.WriteLine($"Query:  {queryCondition}");
            }

            if (dryRun)
            {
                Console.WriteLine("Mode:   DRY RUN (no HTTP calls to wiki)");
            }

            Console.WriteLine(
                $"Limits: maxDepth={queryOptions.MaxPrefixDepth}, " +
                $"maxRequests={queryOptions.MaxTotalRequests}, " +
                $"maxRuntime={queryOptions.MaxRuntime.TotalMinutes:F0}min, " +
                $"delay={Math.Max(200, queryOptions.DelayBetweenRequestsMs)}ms, " +
                $"maxAttempts={queryOptions.MaxAttempts}");
            Console.WriteLine();

            using var httpClient = new HttpClient();

            // MediaWiki's User-Agent policy asks for a way to contact whoever
            // is running the client. Without one, an operator who wants this
            // traffic to stop has no option but to block the address.
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(
                "TaimisToolbench-VendorOfferUpdater/1.0 " +
                "(+https://github.com/MaximusCub/TaimisToolbench)");

            // Step 1: Load currency mappings from GW2 API
            if (!dryRun)
            {
                var apiHelper = new Gw2ApiHelper(httpClient);
                await apiHelper.LoadCurrenciesAsync();
                Console.WriteLine();

                var wikiClient = new WikiSmwClient(httpClient);
                List<WikiVendorResult> wikiResults;

                // The pages actually touched by
                // THIS run's --query (null for --resolve-item-currencies-
                // only, which has no --query and processes the whole
                // cache by design) - see ResolveSeasonalFestivalValuesAsync's
                // queryScopedResults parameter doc comment for why this
                // must NOT be wikiResults after Step 2's cache merge.
                List<WikiVendorResult>? queryScopedResults = null;

                string wikiCachePath = Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? ".",
                    "wiki_vendor_cache.json");

                if (resolveOnly)
                {
                    // --resolve-item-currencies-only: load cached wiki results
                    if (!File.Exists(wikiCachePath))
                    {
                        Console.Error.WriteLine(
                            $"ERROR: Wiki cache not found at {wikiCachePath}.");
                        Console.Error.WriteLine(
                            "Run with --skip-item-resolution first to generate it.");
                        return 1;
                    }

                    Console.WriteLine($"Loading wiki vendor cache from {wikiCachePath}...");
                    string cacheJson = await File.ReadAllTextAsync(wikiCachePath);
                    wikiResults = JsonSerializer.Deserialize<List<WikiVendorResult>>(cacheJson)
                        ?? throw new InvalidOperationException(
                            $"Wiki cache at {wikiCachePath} deserialized to null.");
                    Console.WriteLine($"  Loaded {wikiResults.Count} cached wiki results.");
                    Console.WriteLine();
                }
                else
                {
                    // Step 2: Query wiki for vendor items
                    Console.WriteLine("Querying GW2 Wiki for vendor items...");
                    var (results, queryStats) =
                        await wikiClient.QueryVendorItemsAsync(queryCondition, queryOptions, ct);
                    wikiResults = results;
                    queryScopedResults = results;
                    Console.WriteLine($"Total wiki results: {wikiResults.Count}");
                    Console.WriteLine();

                    // Print query summary
                    PrintQuerySummary(queryStats);

                    if (queryStats.WasInterrupted)
                    {
                        Console.WriteLine(
                            "WARNING: Query was interrupted by safety limits. " +
                            "Results are partial. Increase --max-runtime or --max-requests.");
                        Console.WriteLine();
                    }

                    // Save wiki results cache for --resolve-item-currencies-only
                    // Merge with existing cache if present (supports multi-pass querying).
                    // Freshly-queried pages always overwrite any existing cache entry for
                    // the same PageName, so a full Pass 1 re-scrape after a WikiVendorResult
                    // schema change (e.g. new printouts/fields) is not silently discarded by
                    // stale cache data. Pages not touched by this pass keep their cached copy.
                    if (File.Exists(wikiCachePath))
                    {
                        string existingCacheJson = await File.ReadAllTextAsync(wikiCachePath);
                        var existing = JsonSerializer.Deserialize<List<WikiVendorResult>>(
                            existingCacheJson) ?? new List<WikiVendorResult>();
                        var mergeResult = MergeWikiCache(existing, wikiResults);
                        Console.WriteLine(
                            $"Merged wiki cache: {mergeResult.Added} new + " +
                            $"{mergeResult.Refreshed} refreshed + " +
                            $"{mergeResult.Unchanged} unchanged = " +
                            $"{mergeResult.Merged.Count} total");
                        wikiResults = mergeResult.Merged;
                    }

                    string cacheJson = JsonSerializer.Serialize(wikiResults);
                    await File.WriteAllTextAsync(wikiCachePath, cacheJson);
                    Console.WriteLine(
                        $"Saved wiki vendor cache ({wikiResults.Count} results) to {wikiCachePath}");
                    Console.WriteLine();
                }

                // Step 3: Resolve item-based currencies and unlock recipe
                // sheet names via wiki
                string cachePath = Path.Combine(
                    Path.GetDirectoryName(outputPath) ?? ".",
                    "item_id_cache.json");
                var itemIdCache = ItemIdCache.Load(cachePath);
                ReportCachedMisses(itemIdCache, recheckMisses);
                RegisterCachedCurrencyNames(itemIdCache, apiHelper);

                if (!skipItemResolution)
                {
                    var unknownNames = wikiResults
                        .SelectMany(r => r.CostEntries)
                        .Select(c => c.Currency)
                        // An unlock requirement names a recipe sheet by the
                        // same item name a cost line would, and needs the
                        // same name-to-id resolution.
                        .Concat(wikiResults.Select(r =>
                            VendorUnlockRequirementParser.ExtractRecipeSheetName(r.Requirement)))
                        .Where(name => !string.IsNullOrEmpty(name)
                            && !apiHelper.ResolveCurrencyId(name).HasValue
                            && !itemIdCache.Contains(name))
                        // IsNullOrEmpty above already proved non-null/non-empty;
                        // the static element type just doesn't narrow across
                        // the Where lambda boundary.
                        .Select(name => name!)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    if (unknownNames.Count > 0)
                    {
                        await ResolveUnknownNamesAsync(
                            unknownNames,
                            wikiClient,
                            apiHelper,
                            itemIdCache,
                            queryOptions,
                            ct);

                        itemIdCache.Save(cachePath);
                        Console.WriteLine();
                    }
                    else if (itemIdCache.Count > 0)
                    {
                        Console.WriteLine(
                            $"All item names resolved from cache ({itemIdCache.Count} entries).");
                        Console.WriteLine();
                    }
                }
                else
                {
                    Console.WriteLine(
                        "Skipping item name resolution (--skip-item-resolution).");
                    Console.WriteLine();
                }

                var itemIdMap = new Dictionary<string, int>(
                    itemIdCache.Ids, StringComparer.OrdinalIgnoreCase);

                // Step 3.4: Resolve the recipe each unlock sheet named by a
                // "Has requirement" value actually unlocks. Costs one GW2
                // API item lookup per DISTINCT sheet, and no wiki traffic
                // at all - measured over the full 70,644-row scrape that is
                // one lookup, for Lyhr's "Recipe: Legendary Obsidian Armor".
                var unlockRecipeIdByItemId =
                    await ResolveUnlockRecipeIdsAsync(wikiResults, itemIdMap, apiHelper, ct);

                // Step 3.5: Resolve festival-vendor seasonal tags via wiki
                // (opt-in, --tag-seasonal-festivals - see
                // ResolveSeasonalFestivalValuesAsync's own doc comment for
                // why this is a separate, explicitly-requested pass rather
                // than part of every default run).
                if (tagSeasonalFestivals)
                {
                    string seasonalCachePath = Path.Combine(
                        Path.GetDirectoryName(outputPath) ?? ".",
                        "seasonal_wikitext_cache.json");

                    await ResolveSeasonalFestivalValuesAsync(
                        wikiResults, wikiClient, seasonalCachePath, maxSeasonalPages, delayMs, ct,
                        queryScopedResults);

                    // WikiVendorResult.
                    // TemporarySeasonalValue's own doc comment claims this
                    // value "still round-trips through wiki_vendor_cache.
                    // json for a later run without needing to re-fetch the
                    // page" - false as written, because Step 2 above wrote
                    // wikiCachePath BEFORE this pass populated the field,
                    // so every row in that file had it as null. Re-save
                    // now that wikiResults carries the resolved values, so
                    // a later --resolve-item-currencies-only (or any other
                    // run that loads wikiCachePath) actually gets them
                    // without re-fetching every vendor page's wikitext.
                    string cacheJsonWithSeasonalTags = JsonSerializer.Serialize(wikiResults);
                    await File.WriteAllTextAsync(wikiCachePath, cacheJsonWithSeasonalTags);
                    Console.WriteLine(
                        $"Re-saved wiki vendor cache ({wikiResults.Count} results, now including " +
                        $"resolved seasonal festival tags) to {wikiCachePath}");
                    Console.WriteLine();
                }

                // Step 4: Convert to VendorOffers
                Console.WriteLine("Converting to vendor offers...");
                var offers = new List<VendorOffer>();
                int skippedNoId = 0;
                int skippedUnresolved = 0;

                // A merchant with a GameId<=0 row
                // in THIS pass means the pass's own wiki query failed to
                // resolve a game id for at least one of that merchant's
                // items (a query defect, not proof the wiki dropped the
                // item - a scoped festival-vendor run measured the wiki
                // still serving real ids for rows its own cache recorded
                // as GameId 0). MergeIntoBaseline's per-merchant wholesale
                // replacement must not be allowed to delete that
                // merchant's baseline offers on the strength of an
                // incomplete fresh result - see the set built below and
                // its use at the --merge-into call site.
                var skippedNoIdMerchants = new HashSet<string>(StringComparer.Ordinal);

                foreach (var result in wikiResults)
                {
                    if (result.GameId <= 0)
                    {
                        skippedNoId++;
                        skippedNoIdMerchants.Add(result.MerchantName ?? string.Empty);
                        continue;
                    }

                    var offer = ConvertToOffer(
                        result, apiHelper, itemIdMap, unlockRecipeIdByItemId);
                    if (offer != null)
                    {
                        offers.Add(offer);
                    }
                    else
                    {
                        skippedUnresolved++;
                    }
                }

                Console.WriteLine(
                    $"  Converted: {offers.Count} offers " +
                    $"(skipped: {skippedNoId} no game ID, {skippedUnresolved} unresolved cost)");

                // Deduplicate by OfferId
                var uniqueOffers = offers
                    .GroupBy(o => o.OfferId)
                    .Select(g => g.First())
                    .OrderBy(o => o.OfferId, StringComparer.Ordinal)
                    .ToList();

                Console.WriteLine($"  Unique offers: {uniqueOffers.Count}");
                Console.WriteLine();

                // Step 5: Merge into an existing baseline, if requested
                // ("regenerate ONLY those pages'
                // rows" - a scoped re-scrape, via --query, of a handful of
                // merchant pages should not replace the WHOLE baseline
                // dataset the way a from-scratch run does; --merge-into
                // instead removes only the merchants this pass actually
                // queried, then unions in the fresh, freshly-tagged offers
                // for exactly those merchants).
                var finalOffers = uniqueOffers;
                if (mergeIntoPath != null)
                {
                    if (!File.Exists(mergeIntoPath))
                    {
                        Console.Error.WriteLine($"ERROR: --merge-into baseline not found at {mergeIntoPath}.");
                        return 1;
                    }

                    string baselineJson = await File.ReadAllTextAsync(mergeIntoPath, ct);
                    var readOptions = new JsonSerializerOptions
                    {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                        PropertyNameCaseInsensitive = true,
                    };
                    var baseline = JsonSerializer.Deserialize<VendorOfferDataset>(baselineJson, readOptions)
                                   ?? new VendorOfferDataset();

                    // VendorOffer.Locations defaults to a fresh empty List
                    // (field initializer), so deserializing an offer whose
                    // "locations" key was OMITTED (null when originally
                    // written - DefaultIgnoreCondition.WhenWritingNull only
                    // omits null, never an empty-but-present array) leaves
                    // Locations as an empty list, not null. Re-serializing
                    // would then write "locations":[] where the baseline
                    // never had the key at all - a large, spurious diff
                    // across every untouched offer with no location data.
                    // Restored to null here so an offer this pass does NOT
                    // touch round-trips byte-for-byte. CostLines needs no
                    // equivalent fix - the baseline never omits that key
                    // (confirmed: every offer has it, sometimes as `[]`).
                    foreach (var offer in baseline.Offers)
                    {
                        if (offer.Locations != null && offer.Locations.Count == 0)
                        {
                            offer.Locations = null;
                        }
                    }

                    var mergeResult = MergeIntoBaseline(baseline.Offers, uniqueOffers, skippedNoIdMerchants);
                    finalOffers = mergeResult.Merged;
                    Console.WriteLine(
                        $"Merged into baseline ({baseline.Offers.Count} offers): " +
                        $"removed {mergeResult.RemovedFromBaseline} offer(s) for " +
                        $"{mergeResult.MerchantNamesReplaced.Count} merchant(s), " +
                        $"added {finalOffers.Count - (baseline.Offers.Count - mergeResult.RemovedFromBaseline)} " +
                        $"=> {finalOffers.Count} total");

                    if (mergeResult.MerchantNamesProtected.Count > 0)
                    {
                        Console.WriteLine(
                            $"  WARNING: {mergeResult.MerchantNamesProtected.Count} merchant(s) had " +
                            "row(s) with no game id this pass, so their baseline offers were NOT " +
                            "dropped (DATA LOSS guard) - re-run once every row resolves a game id " +
                            "to clean up any now-stale baseline rows for: " +
                            string.Join(", ", mergeResult.MerchantNamesProtected));
                    }

                    Console.WriteLine();
                }

                // Step 5c: Drop hand-verified exclusions. The SMW scrape
                // cannot tell a live vendor from one whose sale a patch
                // removed when the wiki page is not marked historical, so
                // ref/vendor_offer_exclusions.json refuses those rows by
                // (merchant, item) with the evidence for each. Applied
                // after the merge so a baseline row cannot survive either.
                int excludedCount = ApplyExclusions(
                    ref finalOffers, Path.GetDirectoryName(outputPath) ?? ".");
                if (excludedCount > 0)
                {
                    Console.WriteLine(
                        $"Excluded {excludedCount} hand-verified stale offer(s) " +
                        "(ref/vendor_offer_exclusions.json).");
                    Console.WriteLine();
                }

                // Step 5d: Report what the wiki never answered, and leave a
                // sidecar naming those sections so the next run can re-target
                // them by condition instead of scraping everything again.
                var unresolved = wikiClient.UnresolvedSections;
                PrintUnresolvedSections(unresolved);

                string? sidecarPath = await UnresolvedSectionFile.SaveAsync(outputPath, unresolved);
                if (sidecarPath != null)
                {
                    Console.WriteLine($"Written unresolved-section report to {sidecarPath}");
                    Console.WriteLine();
                }

                // Step 5e: Coverage check. Rows a run never fetched are
                // invisible to the merge guard above, which only protects rows
                // already in the baseline.
                var previousOffers = await LoadOffersForCoverageAsync(outputPath);
                var coverage = CoverageGate.Evaluate(
                    previousOffers,
                    finalOffers,
                    unresolved,
                    CoverageGate.DefaultMaxDropFraction,
                    allowCoverageDrop);
                Console.Write(CoverageGate.Format(coverage));
                Console.WriteLine();

                if (coverage.Blocked)
                {
                    Console.Error.WriteLine(
                        $"ERROR: coverage check blocked the write to {outputPath}.");
                    return 3;
                }

                // Step 6: Write output
                var dataset = new VendorOfferDataset
                {
                    SchemaVersion = 1,
                    Source = "gw2wiki-smw",
                    Offers = finalOffers,
                };

                string json = SerializeDataset(dataset);

                string? dir = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                await File.WriteAllTextAsync(outputPath, json);
                Console.WriteLine($"Written {finalOffers.Count} offers to {outputPath}");
                Console.WriteLine($"File size: {new FileInfo(outputPath).Length:N0} bytes");

                // The run's timestamp goes in the sibling manifest, never in
                // the data file - see VendorOfferDataset's own note. A refresh
                // that changes nothing must leave the 14.8MB blob byte-for-byte
                // untouched, so `git status` after it is the no-op signal.
                string manifestPath = ManifestPathFor(outputPath);
                var manifest = new VendorOfferManifest
                {
                    ManifestVersion = 1,
                    SchemaVersion = dataset.SchemaVersion,
                    Source = dataset.Source,
                    OfferCount = finalOffers.Count,
                    Sha256 = HashFile(outputPath),
                    GeneratedAt = DateTime.UtcNow.ToString("o"),
                };
                await File.WriteAllTextAsync(manifestPath, SerializeManifest(manifest));
                Console.WriteLine($"Written provenance manifest to {manifestPath}");

                return 0;
            }
            else
            {
                // Dry-run path: only print plan, no HTTP to wiki
                var wikiClient = new WikiSmwClient(httpClient);
                var (_, stats) =
                    await wikiClient.QueryVendorItemsAsync(queryCondition, queryOptions, ct);
                return 0;
            }
        }

        /// <summary>
        /// Lists every section the wiki never answered, with the reason and
        /// the query. A count alone was what made this class of loss
        /// invisible, so each section is named.
        /// </summary>
        private static void PrintUnresolvedSections(IReadOnlyList<UnresolvedSection> unresolved)
        {
            if (unresolved.Count == 0)
            {
                Console.WriteLine("Unresolved sections: none.");
                Console.WriteLine();
                return;
            }

            Console.WriteLine($"=== UNRESOLVED SECTIONS ({unresolved.Count}) ===");
            Console.WriteLine(
                "  These were asked for and never answered. They are NOT empty, and the");
            Console.WriteLine(
                "  rows they hold are missing from this run's dataset.");
            foreach (var section in unresolved)
            {
                Console.WriteLine(
                    $"  - {section.Kind} [{section.Label}] after {section.Attempts} attempt(s): " +
                    $"{section.ErrorCode}");
                Console.WriteLine($"      reason: {section.Reason}");
                Console.WriteLine($"      query:  {section.Condition}");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Reads the dataset a write would replace, for the coverage check.
        /// A missing or unreadable file is a first run, not a coverage
        /// failure, so it yields no offers rather than an error.
        /// </summary>
        private static async Task<List<VendorOffer>> LoadOffersForCoverageAsync(string path)
        {
            if (!File.Exists(path))
            {
                return new List<VendorOffer>();
            }

            try
            {
                string existingJson = await File.ReadAllTextAsync(path);
                var readOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    PropertyNameCaseInsensitive = true,
                };
                var existing = JsonSerializer.Deserialize<VendorOfferDataset>(existingJson, readOptions);
                return existing?.Offers ?? new List<VendorOffer>();
            }
            catch (JsonException ex)
            {
                Console.WriteLine(
                    $"  WARNING: could not read {path} for the coverage check ({ex.Message}). " +
                    "Treating this run as the first.");
                return new List<VendorOffer>();
            }
        }

        private static void PrintQuerySummary(QueryStats stats)
        {
            Console.WriteLine("=== Query Summary ===");
            Console.WriteLine($"  HTTP requests:    {stats.TotalHttpRequests}");
            Console.WriteLine($"  Rows fetched:     {stats.TotalRowsFetched}");
            Console.WriteLine($"  Distinct results: {stats.DistinctResults}");
            Console.WriteLine($"  Duplicates:       {stats.DuplicatesDiscarded}");
            Console.WriteLine($"  Truncated parts:  {stats.TruncatedPartitions}");
            Console.WriteLine($"  Elapsed:          {stats.Elapsed}");

            if (stats.NonAlphaVendors.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"  WARNING: Found {stats.NonAlphaVendors.Count} vendor(s) whose " +
                    "first character is outside the partition character set, so a " +
                    "prefix split cannot reach them:");
                foreach (var name in stats.NonAlphaVendors)
                {
                    Console.WriteLine($"    - {name}");
                }
            }

            if (stats.TruncatedPartitions > 0)
            {
                Console.WriteLine();
                Console.WriteLine(
                    $"  WARNING: Coverage may be incomplete - " +
                    $"{stats.TruncatedPartitions} partition(s) were truncated at max depth.");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// Result of merging a freshly-queried batch of wiki results into an
        /// existing wiki-results cache. See <see cref="MergeWikiCache"/>.
        /// </summary>
        internal sealed class WikiCacheMergeResult
        {
            public List<WikiVendorResult> Merged { get; set; } = new List<WikiVendorResult>();

            public int Added { get; set; }

            public int Refreshed { get; set; }

            public int Unchanged { get; set; }
        }

        /// <summary>
        /// Merges freshly-queried wiki results into an existing wiki-results cache,
        /// keyed by PageName. A fresh result for a PageName already present in the
        /// cache always overwrites the cached entry in full (so newly-added fields,
        /// e.g. purchase caps, are never silently discarded by a stale cached copy).
        /// A cached PageName not present in the fresh batch is preserved unchanged.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static WikiCacheMergeResult MergeWikiCache(
            List<WikiVendorResult> existing,
            List<WikiVendorResult> fresh)
        {
            existing ??= new List<WikiVendorResult>();
            fresh ??= new List<WikiVendorResult>();

            var merged = new Dictionary<string, WikiVendorResult>(StringComparer.Ordinal);
            var existingKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in existing)
            {
                string key = r.PageName ?? string.Empty;
                merged[key] = r;
                existingKeys.Add(key);
            }

            // Quality-audit B4 (KNOWN-ISSUES #53): counted against
            // existingKeys, not merged.ContainsKey - merged mutates during
            // this same loop, so a duplicate PageName within one fresh
            // batch was double-counted as Refreshed. addedKeys/
            // refreshedKeys are sets for the same reason: two fresh
            // entries sharing a PageName are one net page, not two.
            var addedKeys = new HashSet<string>(StringComparer.Ordinal);
            var refreshedKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in fresh)
            {
                string key = r.PageName ?? string.Empty;
                if (existingKeys.Contains(key))
                {
                    refreshedKeys.Add(key);
                }
                else
                {
                    addedKeys.Add(key);
                }

                merged[key] = r;
            }

            int added = addedKeys.Count;
            int refreshed = refreshedKeys.Count;
            int unchanged = existingKeys.Count - refreshed;

            return new WikiCacheMergeResult
            {
                Merged = merged.Values.ToList(),
                Added = added,
                Refreshed = refreshed,
                Unchanged = unchanged,
            };
        }

        /// <summary>
        /// Result of merging a scoped, freshly-queried batch of offers into
        /// an existing baseline dataset. See <see cref="MergeIntoBaseline"/>.
        /// </summary>
        internal sealed class BaselineMergeResult
        {
            public List<VendorOffer> Merged { get; set; } = new List<VendorOffer>();

            public int RemovedFromBaseline { get; set; }

            public List<string> MerchantNamesReplaced { get; set; } = new List<string>();

            // Merchants that appeared in the
            // fresh batch but were EXCLUDED from wholesale replacement
            // because merchantsWithSkippedRows flagged them - their
            // baseline offers were kept, not dropped. See
            // MergeIntoBaseline's own doc comment.
            public List<string> MerchantNamesProtected { get; set; } = new List<string>();
        }

        /// <summary>
        /// Merges a scoped, freshly-queried batch into an existing full
        /// baseline, replacing ONLY the merchants the scoped query covered: a
        /// merchant present in <paramref name="fresh"/> has every baseline
        /// offer removed first and every fresh offer added, never a partial
        /// union; every other merchant passes through untouched.
        ///
        /// <paramref name="merchantsWithSkippedRows"/> (merchants with at
        /// least one GameId&lt;=0 row this pass, built before the GameId
        /// filter runs) opts a merchant OUT of that replacement: its baseline
        /// offers are unioned with the fresh ones, deduplicated by OfferId
        /// with fresh preferred - a losing row's SeasonalFestival tag is
        /// carried onto an untagged winner - plus a content-key pass
        /// (ComputeContentKey) for rows predating a hash-format change. Why
        /// replacing there is unsafe: docs/ARCHITECTURE.md section T.4.
        ///
        /// NOTE (non-purity): assigns onto SeasonalFestival/OfferId of rows in
        /// the caller's own <paramref name="fresh"/>/<paramref name="baseline"/>
        /// lists rather than cloning, so a caller's own reference sees it.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static BaselineMergeResult MergeIntoBaseline(
            List<VendorOffer> baseline,
            List<VendorOffer> fresh,
            ISet<string>? merchantsWithSkippedRows = null)
        {
            baseline ??= new List<VendorOffer>();
            fresh ??= new List<VendorOffer>();
            merchantsWithSkippedRows ??= new HashSet<string>(StringComparer.Ordinal);

            var merchantsInFresh = fresh
                .Select(o => o.MerchantName ?? string.Empty)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(m => m, StringComparer.Ordinal)
                .ToList();

            var merchantsProtected = merchantsInFresh
                .Where(m => merchantsWithSkippedRows.Contains(m))
                .ToList();
            var merchantsReplaced = merchantsInFresh
                .Where(m => !merchantsWithSkippedRows.Contains(m))
                .ToList();
            var merchantsReplacedSet = new HashSet<string>(merchantsReplaced, StringComparer.Ordinal);

            // An ORDINARY (non-protected) replaced merchant's
            // baseline rows are about to be dropped entirely by `kept`
            // below, before the fresh/kept GroupBy tag-carry-forward logic
            // further down ever runs - that logic only ever sees a
            // baseline row for PROTECTED merchants. Without this, a
            // transiently failed fetch loses a previously-shipped tag for
            // every ordinary merchant. Harvest each replaced merchant's
            // tagged baseline rows into a lookup BEFORE `kept` drops them,
            // keyed by both OfferId and ComputeContentKey - a
            // VendorOfferHasher hash-format migration can leave either as
            // the only field still matching between the baseline and fresh
            // copies of the same offer (see the protected-merchant
            // content-key dedupe pass further below for the same
            // reasoning) - then apply the harvested tag onto that
            // merchant's fresh rows that have no tag of their own. A fresh
            // row that already carries its own (possibly different) tag is
            // never overwritten - fresh always wins when both sides are
            // tagged.
            if (merchantsReplacedSet.Count > 0)
            {
                var replacedTagsByOfferId = new Dictionary<string, string>(StringComparer.Ordinal);
                var replacedTagsByContentKey = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var o in baseline)
                {
                    if (o.SeasonalFestival == null)
                    {
                        continue;
                    }

                    if (!merchantsReplacedSet.Contains(o.MerchantName ?? string.Empty))
                    {
                        continue;
                    }

                    if (o.OfferId != null)
                    {
                        replacedTagsByOfferId[o.OfferId] = o.SeasonalFestival;
                    }

                    replacedTagsByContentKey[ComputeContentKey(o)] = o.SeasonalFestival;
                }

                if (replacedTagsByOfferId.Count > 0 || replacedTagsByContentKey.Count > 0)
                {
                    foreach (var o in fresh)
                    {
                        if (o.SeasonalFestival != null)
                        {
                            continue;
                        }

                        if (!merchantsReplacedSet.Contains(o.MerchantName ?? string.Empty))
                        {
                            continue;
                        }

                        if (o.OfferId != null
                            && replacedTagsByOfferId.TryGetValue(o.OfferId, out var tagById))
                        {
                            o.SeasonalFestival = tagById;
                        }
                        else if (replacedTagsByContentKey.TryGetValue(
                            ComputeContentKey(o), out var tagByContent))
                        {
                            o.SeasonalFestival = tagByContent;
                        }
                    }
                }
            }

            var kept = baseline
                .Where(o => !merchantsReplacedSet.Contains(o.MerchantName ?? string.Empty))
                .ToList();
            int removed = baseline.Count - kept.Count;

            //
            // fresh must come FIRST in the concat so an OfferId collision
            // resolves to the FRESH row via GroupBy(...).Select(g =>
            // g.First()) below. The old kept.Concat(fresh) order let the
            // BASELINE row win every collision - for a protected merchant
            // (kept includes its baseline rows; SeasonalFestival is
            // deliberately NOT hashed by VendorOfferHasher, so a row whose
            // content is otherwise unchanged collides on OfferId) this
            // silently discarded the freshly-derived SeasonalFestival tag,
            // i.e. exactly the merchants the protected-merchant guard
            // exists to preserve data for kept shipping untagged.
            // This pass used to just take
            // g.First() unconditionally, so a FRESH row with no
            // SeasonalFestival (e.g. one whose page's wikitext fetch
            // missed this run - see ResolveSeasonalFestivalValuesAsync's
            // null-wikitext handling) silently deleted a shipped, tagged
            // baseline row on an OfferId collision - the exact opposite of
            // the content-key pass below, which already prefers whichever
            // side carries the tag. Same rule now applies here: keep the
            // winning row (fresh, if present in the group, for freshness
            // of everything else), but carry a losing sibling's tag
            // forward if the winner itself has none.
            var merged = fresh.Concat(kept)
                .GroupBy(o => o.OfferId, StringComparer.Ordinal)
                .Select(g =>
                {
                    var winner = g.First();
                    if (winner.SeasonalFestival == null)
                    {
                        var taggedSibling = g.FirstOrDefault(o => o.SeasonalFestival != null);
                        if (taggedSibling != null)
                        {
                            winner.SeasonalFestival = taggedSibling.SeasonalFestival;
                        }
                    }

                    return winner;
                })
                .ToList();

            // A protected merchant's baseline row can also predate a
            // VendorOfferHasher hash-format change (see that file's own
            // doc comment: "any offer's OfferId changes the first time it
            // is recomputed") - the fresh, content-identical row then gets
            // a DIFFERENT OfferId, the GroupBy above does not catch the
            // duplicate, and the union would ship two rows (one tagged,
            // one untagged) for the same vendor+item. Only protected
            // merchants can have this cross-duplication (a replaced
            // merchant's `kept` excludes it entirely), so scope the
            // content-based dedupe to them; prefer whichever survivor
            // carries a fresh SeasonalFestival tag.
            if (merchantsProtected.Count > 0)
            {
                var protectedSet = new HashSet<string>(merchantsProtected, StringComparer.Ordinal);

                // When a content-key collision
                // resolves to the baseline (kept) row because it carries
                // the tag and the fresh row does not, the swap below used
                // to keep the baseline row's OfferId wholesale - stale,
                // pre-hash-format-change - discarding the fresh row's
                // current-format OfferId even though the fresh row is
                // otherwise thrown away. Track which OfferId strings came
                // from THIS run's fresh batch so the winning row can be
                // migrated onto the current-format id instead of carrying
                // the stale one forward indefinitely.
                var freshOfferIds = new HashSet<string>(
                    fresh.Where(o => o.OfferId != null).Select(o => o.OfferId!),
                    StringComparer.Ordinal);

                var byContentKey = new Dictionary<string, VendorOffer>(StringComparer.Ordinal);
                var result = new List<VendorOffer>();
                foreach (var offer in merged)
                {
                    if (!protectedSet.Contains(offer.MerchantName ?? string.Empty))
                    {
                        result.Add(offer);
                        continue;
                    }

                    string contentKey = ComputeContentKey(offer);
                    if (byContentKey.TryGetValue(contentKey, out var survivor))
                    {
                        if (survivor.SeasonalFestival == null && offer.SeasonalFestival != null)
                        {
                            if (offer.OfferId != null && survivor.OfferId != null
                                && freshOfferIds.Contains(survivor.OfferId)
                                && !freshOfferIds.Contains(offer.OfferId))
                            {
                                offer.OfferId = survivor.OfferId;
                            }

                            byContentKey[contentKey] = offer;
                        }
                    }
                    else
                    {
                        byContentKey[contentKey] = offer;
                    }
                }

                result.AddRange(byContentKey.Values);
                merged = result;
            }

            merged = merged
                .OrderBy(o => o.OfferId, StringComparer.Ordinal)
                .ToList();

            return new BaselineMergeResult
            {
                Merged = merged,
                RemovedFromBaseline = removed,
                MerchantNamesReplaced = merchantsReplaced,
                MerchantNamesProtected = merchantsProtected,
            };
        }

        /// <summary>
        /// Canonical content key for a VendorOffer, used by
        /// MergeIntoBaseline's protected-merchant dedupe pass to catch two
        /// rows that describe the identical offer but carry different
        /// OfferId hash strings (e.g. a baseline row that predates a
        /// VendorOfferHasher hash-format change). Mirrors the same fields
        /// VendorOfferHasher.ComputeOfferId hashes, EXCEPT OfferId itself
        /// and SeasonalFestival - the latter deliberately excluded so a
        /// freshly-tagged row and its untagged baseline counterpart are
        /// recognized as the same offer rather than two different ones.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static string ComputeContentKey(VendorOffer offer)
        {
            var sb = new StringBuilder();

            sb.Append("merchant=");
            sb.Append(offer.MerchantName ?? "");

            sb.Append(";output=");
            sb.Append(offer.OutputItemId);
            sb.Append('/');
            sb.Append(offer.OutputCount);

            sb.Append(";costs=");
            var sortedCosts = (offer.CostLines ?? new List<CostLine>())
                .OrderBy(c => c.Type, StringComparer.Ordinal)
                .ThenBy(c => c.Id)
                .ThenBy(c => c.Count)
                .ToList();
            for (int i = 0; i < sortedCosts.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append(sortedCosts[i].Type);
                sb.Append(':');
                sb.Append(sortedCosts[i].Id);
                sb.Append(':');
                sb.Append(sortedCosts[i].Count);
            }

            sb.Append(";locations=");
            var sortedLocations = (offer.Locations ?? new List<string>())
                .OrderBy(l => l, StringComparer.Ordinal)
                .ToList();
            sb.Append(string.Join(",", sortedLocations));

            sb.Append(";dailyCap=");
            sb.Append(offer.DailyCap.HasValue ? offer.DailyCap.Value.ToString() : "null");

            sb.Append(";weeklyCap=");
            sb.Append(offer.WeeklyCap.HasValue ? offer.WeeklyCap.Value.ToString() : "null");

            sb.Append(";homesteadTier=");
            sb.Append(offer.HomesteadTier.HasValue ? offer.HomesteadTier.Value.ToString() : "null");

            sb.Append(";seasonalCap=");
            sb.Append(offer.SeasonalCap.HasValue ? offer.SeasonalCap.Value.ToString() : "null");

            return sb.ToString();
        }

        /// <summary>
        /// Converts a single wiki vendor result to a VendorOffer.
        /// Returns null if any cost line cannot be resolved.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static VendorOffer? ConvertToOffer(
            WikiVendorResult result,
            Gw2ApiHelper apiHelper,
            Dictionary<string, int> itemIdMap,
            IReadOnlyDictionary<int, int>? unlockRecipeIdByItemId = null)
        {
            int outputCount = result.OutputQuantity ?? 1;
            if (outputCount <= 0)
            {
                outputCount = 1;
            }

            string? merchant = result.MerchantName;
            if (string.IsNullOrEmpty(merchant))
            {
                return null;
            }

            var costLines = new List<CostLine>();

            foreach (var cost in result.CostEntries)
            {
                int? currencyId = apiHelper.ResolveCurrencyId(cost.Currency);
                if (currencyId.HasValue)
                {
                    costLines.Add(new CostLine
                    {
                        Type = "Currency",
                        Id = currencyId.Value,
                        Count = cost.Value,
                    });
                }
                else if (!string.IsNullOrEmpty(cost.Currency) &&
                         itemIdMap.TryGetValue(cost.Currency, out int itemId))
                {
                    costLines.Add(new CostLine
                    {
                        Type = "Item",
                        Id = itemId,
                        Count = cost.Value,
                    });
                }
                else if (!string.IsNullOrEmpty(cost.Currency))
                {
                    // Unresolved currency/item name - skip this offer
                    return null;
                }
                else
                {
                    // No currency specified, assume coins
                    costLines.Add(new CostLine
                    {
                        Type = "Currency",
                        Id = Gw2Constants.CoinCurrencyId,
                        Count = cost.Value,
                    });
                }
            }

            var locations = result.Locations
                .Where(l => !string.IsNullOrEmpty(l))
                .ToList();

            var offerLocations = locations.Count > 0 ? locations : null;

            // Null for every non-Homestead-
            // Refinement offer. Also gated on the OUTPUT being one of the
            // three known refined materials - the same three "Homestead
            // Refinement-X" merchant pages also sell unrelated rows under
            // the identical merchant name (the station's own one-time
            // efficiency/capacity Upgrade purchase items, "Has vendor" is
            // hardcoded to the page name for every row on the page
            // regardless of subsection - confirmed live: without this
            // guard, an Upgrade-purchase row's requirement-less "Has
            // requirement" would otherwise be misread as tier 0). For a
            // Homestead Refinement row with unrecognized requirement text,
            // also null (with a console warning) rather than guessing -
            // never invent a tier.
            int? homesteadTier = Gw2Constants.IsHomesteadRefinementMaterialId(result.GameId)
                ? HomesteadTierResolver.ResolveTier(merchant, result.Requirement)
                : null;
            if (homesteadTier == null &&
                Gw2Constants.IsHomesteadRefinementMaterialId(result.GameId) &&
                !string.IsNullOrEmpty(merchant) &&
                merchant.Contains("Homestead Refinement", StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(result.Requirement))
            {
                Console.WriteLine(
                    $"  WARNING: Homestead Refinement row for game id {result.GameId} " +
                    $"has unrecognized requirement text \"{result.Requirement}\" - left untagged.");
            }

            // Festival-vendor auto-tagging follow-up: resolves
            // the raw wiki "seasonal="/"event=" value (if any, from a
            // separate ResolveSeasonalFestivalValuesAsync pass) to the
            // internal festival key. A present-but-unrecognized value
            // (e.g. a one-off non-festival event like "Fractal Rush") is
            // deliberately left untagged with a warning rather than
            // guessed - never hashed into OfferId either way, matching
            // VendorOffer.SeasonalFestival's own doc comment (this field
            // is deliberately not hashed by VendorOfferHasher, so tagging
            // an already-shipped offer never changes its OfferId).
            string? seasonalFestival = Gw2Constants.ResolveSeasonalFestivalKey(result.TemporarySeasonalValue);
            if (seasonalFestival == null && !string.IsNullOrWhiteSpace(result.TemporarySeasonalValue))
            {
                Console.WriteLine(
                    $"  WARNING: Vendor \"{merchant}\" has an unrecognized wiki " +
                    $"seasonal/event value \"{result.TemporarySeasonalValue}\" in its " +
                    "{{Temporary}} template - left untagged (no invented festival mapping).");
            }

            // Both ids stay null unless the requirement parses AND the
            // sheet's own item id and the recipe it unlocks are both known.
            // A half-resolved gate would name a sheet whose ownership the
            // module could never check. Deliberately not passed to
            // ComputeOfferId below - see Models/VendorOffer.cs.
            int? unlockRecipeItemId = null;
            int? unlockRecipeId = null;
            string? unlockSheetName =
                VendorUnlockRequirementParser.ExtractRecipeSheetName(result.Requirement);
            if (unlockSheetName != null &&
                itemIdMap.TryGetValue(unlockSheetName, out int sheetItemId) &&
                unlockRecipeIdByItemId != null &&
                unlockRecipeIdByItemId.TryGetValue(sheetItemId, out int sheetRecipeId))
            {
                unlockRecipeItemId = sheetItemId;
                unlockRecipeId = sheetRecipeId;
            }

            string offerId = VendorOfferHasher.ComputeOfferId(
                result.GameId,
                outputCount,
                costLines,
                merchant,
                offerLocations,
                result.DailyCap,
                result.WeeklyCap,
                homesteadTier,
                result.SeasonalCap);

            return new VendorOffer
            {
                OfferId = offerId,
                OutputItemId = result.GameId,
                OutputCount = outputCount,
                CostLines = costLines,
                MerchantName = merchant,
                Locations = offerLocations,
                DailyCap = result.DailyCap,
                WeeklyCap = result.WeeklyCap,
                HomesteadTier = homesteadTier,
                SeasonalCap = result.SeasonalCap,
                SeasonalFestival = seasonalFestival,
                UnlockRecipeItemId = unlockRecipeItemId,
                UnlockRecipeId = unlockRecipeId,
            };
        }

        /// <summary>
        /// Maps every recipe sheet item id named by a row's "Has
        /// requirement" to the recipe that sheet unlocks, via
        /// Gw2ApiHelper.ResolveRecipeSheetRecipeIdAsync - one lookup per
        /// DISTINCT sheet, against api.guildwars2.com and never the wiki.
        /// A sheet that unlocks no crafting recipe, or whose lookup fails,
        /// is warned about and left out, so ConvertToOffer leaves that
        /// offer's gate untagged rather than half-tagged.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static async Task<Dictionary<int, int>> ResolveUnlockRecipeIdsAsync(
            IReadOnlyList<WikiVendorResult> wikiResults,
            IReadOnlyDictionary<string, int> itemIdMap,
            Gw2ApiHelper apiHelper,
            CancellationToken ct)
        {
            var sheetItemIds = new SortedSet<int>();
            foreach (var result in wikiResults)
            {
                string? sheetName =
                    VendorUnlockRequirementParser.ExtractRecipeSheetName(result.Requirement);
                if (sheetName != null && itemIdMap.TryGetValue(sheetName, out int itemId))
                {
                    sheetItemIds.Add(itemId);
                }
            }

            var resolved = new Dictionary<int, int>();
            foreach (int itemId in sheetItemIds)
            {
                ct.ThrowIfCancellationRequested();

                int? recipeId;
                try
                {
                    recipeId = await apiHelper.ResolveRecipeSheetRecipeIdAsync(itemId);
                }
                catch (HttpRequestException ex)
                {
                    Console.WriteLine(
                        $"  WARNING: unlock requirement item {itemId} could not be looked " +
                        $"up ({ex.Message}) - offers gated on it stay untagged.");
                    continue;
                }

                if (recipeId.HasValue)
                {
                    resolved[itemId] = recipeId.Value;
                }
                else
                {
                    Console.WriteLine(
                        $"  WARNING: unlock requirement item {itemId} unlocks no crafting " +
                        "recipe - offers gated on it stay untagged.");
                }
            }

            return resolved;
        }

        /// <summary>
        /// Festival-vendor auto-tagging: fetches each distinct wiki vendor
        /// PAGE's raw wikitext (WikiSmwClient.FetchWikitextAsync) and extracts
        /// its {{Temporary|...}} template's seasonal/event value
        /// (TemporaryTemplateParser), so ConvertToOffer can tag that vendor's
        /// offers. Opt-in via --tag-seasonal-festivals: no SMW property
        /// carries that template, so the pass costs one extra HTTP request per
        /// distinct vendor page.
        ///
        /// Results are cached by real wiki page title (PageName up to the
        /// first '#', since PageName is a "Sells item" SUBOBJECT key), with ""
        /// meaning "checked, no tag", in a gitignored dev-local JSON file.
        ///
        /// <paramref name="maxSeasonalPages"/> caps how many NEW pages one run
        /// fetches: over budget it fetches up to the cap, saves the cache and
        /// logs the remainder rather than throwing; only a budget &lt;= 0
        /// throws. <paramref name="queryScopedResults"/> scopes that budget to
        /// the pages THIS run's --query returned, not the full merged cache.
        /// Why each of those holds: docs/ARCHITECTURE.md section T.5.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static async Task ResolveSeasonalFestivalValuesAsync(
            List<WikiVendorResult> wikiResults,
            WikiSmwClient wikiClient,
            string cachePath,
            int maxSeasonalPages,
            int delayMs,
            CancellationToken ct,
            IReadOnlyList<WikiVendorResult>? queryScopedResults = null)
        {
            // Hard-abort fix: defense in depth - Program.cs's
            // own arg parsing already rejects --max-seasonal-pages <= 0
            // before this method is ever called from RunAsync, but this
            // method is also called directly by tests and could in
            // principle be called by a future caller that skips that
            // check. A budget of 0 or less is not a normal "run out of
            // budget, continue next time" case (see the self-healing fetch-
            // up-to-budget logic below) - it means no run could ever make
            // progress, so it stays a genuine SafetyLimitException.
            if (maxSeasonalPages <= 0)
            {
                throw new SafetyLimitException(
                    $"--max-seasonal-pages must be a positive integer, got {maxSeasonalPages}.");
            }

            var cache = LoadSeasonalWikitextCache(cachePath);

            var fetchScope = queryScopedResults ?? (IReadOnlyList<WikiVendorResult>)wikiResults;

            var distinctPageNames = fetchScope
                .Select(r => r.PageName)
                .Where(p => !string.IsNullOrEmpty(p))
                .Select(p => StripSubobjectSuffix(p!))
                .Where(p => p.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var toFetch = distinctPageNames
                .Where(p => !cache.ContainsKey(p))
                .ToList();

            if (toFetch.Count > 0)
            {
                int totalUncached = toFetch.Count;

                // Self-healing fix: a from-scratch run against
                // a real dataset (thousands of distinct vendor pages) with
                // no prior ref/seasonal_wikitext_cache.json (gitignored,
                // empty on a fresh clone) used to hit this budget on its
                // very first invocation and throw SafetyLimitException
                // BEFORE fetching anything - the run exited non-zero, Pass
                // 2 never ran, and a re-run made no progress at all (same
                // empty cache, same over-budget toFetch, same throw).
                // Instead, fetch only UP TO the budget this run, save
                // whatever was fetched (the existing try/finally below
                // already does this), and leave the rest for a later run -
                // next time, this run's own newly-cached pages shrink
                // toFetch, so repeated runs make steady forward progress
                // and the overall process converges instead of looping
                // forever on the same failure. See the maxSeasonalPages
                // <= 0 check above for the one budget shape that still
                // hard-aborts.
                if (toFetch.Count > maxSeasonalPages)
                {
                    int remaining = toFetch.Count - maxSeasonalPages;
                    Console.WriteLine(
                        $"NOTE: {toFetch.Count} vendor page(s) need seasonal tagging, but " +
                        $"the --max-seasonal-pages budget for this run is {maxSeasonalPages}. " +
                        $"Fetching {maxSeasonalPages} page(s) now; {remaining} page(s) remain " +
                        "and will be picked up by a subsequent run (this run's cache save " +
                        "covers everything fetched below, so the remaining count only shrinks " +
                        "from here).");
                    toFetch = toFetch.Take(maxSeasonalPages).ToList();
                }

                Console.WriteLine(
                    $"Resolving seasonal festival tags for {toFetch.Count} " +
                    $"uncached vendor page(s) ({distinctPageNames.Count - totalUncached} already cached)...");

                int effectiveDelay = Math.Max(200, delayMs);

                // The try/finally below saves whatever this pass fetched
                // no matter how the loop exits (Ctrl-C, parse failure,
                // transport error), so a mid-run failure keeps the pages
                // already fetched; a parse failure is treated exactly like
                // an HTTP failure (warn, leave this one page uncached,
                // continue with the rest).
                try
                {
                    for (int i = 0; i < toFetch.Count; i++)
                    {
                        ct.ThrowIfCancellationRequested();

                        string pageName = toFetch[i];

                        // Throttle-class fix: the inter-
                        // request delay used to sit ONLY after a
                        // successful fetch+parse, guarded by the same
                        // "not the last item" check now on the finally
                        // below. Every `continue` above it (HTTP failure,
                        // JSON parse failure, null wikitext) skipped the
                        // delay entirely, so a stretch of missing/failing
                        // pages issued back-to-back requests against
                        // api.guildwars2.com with no throttling at all,
                        // defeating both --delay and the 200ms floor. A
                        // `finally` runs on every exit from the try below
                        // - success, a `continue`, or an uncaught
                        // exception propagating out - so moving the delay
                        // here makes every iteration throttle uniformly,
                        // not just the ones that happen to succeed.
                        try
                        {
                            string? wikitext;
                            try
                            {
                                wikitext = await wikiClient.FetchWikitextAsync(pageName, ct);
                            }
                            catch (HttpRequestException ex)
                            {
                                Console.WriteLine(
                                    $"  WARNING: Failed to fetch wikitext for \"{pageName}\": {ex.Message} - left uncached.");
                                continue;
                            }
                            catch (JsonException ex)
                            {
                                Console.WriteLine(
                                    $"  WARNING: Failed to parse wikitext response for \"{pageName}\": {ex.Message} - left uncached.");
                                continue;
                            }

                            // A null wikitext (missing/
                            // renamed page, or an "error" object in the API
                            // response - see WikiSmwClient.FetchWikitextAsync's
                            // own doc comment) is NOT the same thing as "page
                            // fetched fine, no {{Temporary}} template found" -
                            // the latter legitimately caches as "" below. Caching
                            // a null the same way baked a false "checked - not
                            // tagged" negative into the cache permanently, with
                            // no warning and no future retry. Warn and leave the
                            // page uncached instead, same as an HTTP/JSON failure.
                            if (wikitext == null)
                            {
                                Console.WriteLine(
                                    $"  WARNING: No wikitext returned for \"{pageName}\" " +
                                    "(missing/renamed page, or a wiki API error response) - left uncached.");
                                continue;
                            }

                            string? raw = TemporaryTemplateParser.ExtractSeasonalOrEventParameter(wikitext);
                            cache[pageName] = raw ?? string.Empty;
                        }
                        finally
                        {
                            if (i + 1 < toFetch.Count)
                            {
                                await Task.Delay(effectiveDelay, ct);
                            }
                        }
                    }
                }
                finally
                {
                    SaveSeasonalWikitextCache(cachePath, cache);
                }
            }
            else if (distinctPageNames.Count > 0)
            {
                Console.WriteLine(
                    $"All {distinctPageNames.Count} vendor page(s) already checked for seasonal festival tags.");
            }

            foreach (var result in wikiResults)
            {
                if (string.IsNullOrEmpty(result.PageName))
                {
                    continue;
                }

                string pageTitle = StripSubobjectSuffix(result.PageName);
                if (pageTitle.Length > 0 && cache.TryGetValue(pageTitle, out var value))
                {
                    // Assign
                    // unconditionally (including "" -> null) rather than
                    // only ever ASSIGNING a non-empty value - the old
                    // guard never CLEARED one. Combined with Program.cs
                    // Step 3.5's re-save of wiki_vendor_cache.json, a
                    // value that once round-tripped into the cache could
                    // never be un-set: if the wiki later drops a
                    // {{Temporary}} template, this now un-tags the vendor
                    // instead of re-tagging it forever off a stale value.
                    result.TemporarySeasonalValue = string.IsNullOrEmpty(value) ? null : value;
                }
            }
        }

        /// <summary>
        /// Strips a Semantic MediaWiki subobject suffix ("#vendor1", etc.)
        /// off a "Sells item" subject key, returning the vendor's real,
        /// fetchable wiki page title. A subject with no '#' (already a
        /// plain page title) is returned unchanged. See
        /// ResolveSeasonalFestivalValuesAsync's own doc comment for the
        /// live-confirmed subject-key shape this un-does.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static string StripSubobjectSuffix(string subjectKey)
        {
            int hashIndex = subjectKey.IndexOf('#');
            return hashIndex >= 0 ? subjectKey.Substring(0, hashIndex) : subjectKey;
        }

        // Reserved dictionary key (not a real
        // wiki page title - none contain "__") that stores the cache
        // format version alongside the real page entries, so a version
        // bump can force a one-time recheck of entries written before a
        // fix that changes what "" ("checked - no tag") means. See
        // SeasonalWikitextCacheVersion's own doc comment.
        private const string SeasonalWikitextCacheVersionKey = "__cache_version__";

        // Bump this when a fix changes the MEANING of an already-cached
        // value, so LoadSeasonalWikitextCache purges the affected entries
        // instead of trusting them forever. Current bump (2 - the
        // &redirects=1 fix, WikiSmwClient.FetchWikitextAsync): before that
        // fix, a redirected vendor page's wikitext came back as
        // "#REDIRECT [[Target]]", TemplateRegex found no {{Temporary}}
        // template in that, and the caller cached it as "" - identical to
        // a real, deliberate "checked, not tagged" - so those pages were
        // never retried even though the fix would resolve them correctly.
        // A missing/older version number purges every "" entry (the only
        // ones that ambiguity could have affected; a non-empty resolved
        // value was never subject to it) so they get one clean re-fetch.
        private const int SeasonalWikitextCacheVersion = 2;

        private static Dictionary<string, string> LoadSeasonalWikitextCache(string path)
        {
            var cache = new Dictionary<string, string>(StringComparer.Ordinal);
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
                    cache[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }

                bool isCurrentVersion = cache.TryGetValue(SeasonalWikitextCacheVersionKey, out var versionText)
                    && versionText == SeasonalWikitextCacheVersion.ToString(CultureInfo.InvariantCulture);
                cache.Remove(SeasonalWikitextCacheVersionKey);

                if (!isCurrentVersion)
                {
                    var staleEmptyKeys = cache
                        .Where(kv => kv.Value.Length == 0)
                        .Select(kv => kv.Key)
                        .ToList();
                    foreach (var key in staleEmptyKeys)
                    {
                        cache.Remove(key);
                    }

                    if (staleEmptyKeys.Count > 0)
                    {
                        Console.WriteLine(
                            $"Seasonal wikitext cache version bump: cleared {staleEmptyKeys.Count} " +
                            "pre-redirects-fix \"\" entr" +
                            (staleEmptyKeys.Count == 1 ? "y" : "ies") +
                            " for recheck.");
                    }
                }

                Console.WriteLine($"Loaded seasonal wikitext cache ({cache.Count} entries) from {path}");
            }
            catch
            {
                // Ignore corrupt cache
            }

            return cache;
        }

        private static void SaveSeasonalWikitextCache(string path, Dictionary<string, string> cache)
        {
            var toWrite = new Dictionary<string, string>(cache, StringComparer.Ordinal)
            {
                [SeasonalWikitextCacheVersionKey] =
                    SeasonalWikitextCacheVersion.ToString(CultureInfo.InvariantCulture),
            };

            var sorted = toWrite
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(sorted, options);

            // Unlike
            // Services/VendorOfferStore.SaveOverlay's temp-file +
            // File.Replace pattern, this used to write path directly - a
            // crash mid-write left a truncated cache. Bounded impact
            // (LoadSeasonalWikitextCache swallows a parse failure and
            // returns empty, so the only cost was a silent full re-fetch
            // next run), but this pass's own resilience fix now calls
            // this method from a `finally` block specifically to survive
            // a mid-run crash/cancellation, so the write itself should be
            // no less durable than that.
            string tmpPath = path + ".tmp";
            File.WriteAllText(tmpPath, json);
            if (File.Exists(path))
            {
                File.Replace(tmpPath, path, null);
            }
            else
            {
                File.Move(tmpPath, path);
            }

            Console.WriteLine($"  Saved seasonal wikitext cache ({cache.Count} entries) to {path}");
        }

        /// <summary>
        /// Says how old the remembered misses are, and drops them when
        /// --recheck-misses was passed. A miss is only ever as good as the
        /// day it was recorded: an item the wiki had not documented yet is
        /// indistinguishable from one that does not exist.
        /// </summary>
        private static void ReportCachedMisses(ItemIdCache cache, bool recheckMisses)
        {
            if (cache.Misses.Count == 0)
            {
                return;
            }

            if (recheckMisses)
            {
                int dropped = cache.ForgetMisses();
                Console.WriteLine(
                    $"  --recheck-misses: dropped {dropped} remembered miss(es); " +
                    "they will be asked about again this run.");
                return;
            }

            var oldest = cache.OldestMissAge(DateTime.UtcNow);
            string age = oldest.HasValue
                ? $"the oldest recorded {oldest.Value.TotalDays:F0} day(s) ago"
                : "none of them dated";
            int undated = cache.UndatedMissCount;
            string undatedNote = undated > 0
                ? $", {undated} carrying no date at all"
                : string.Empty;

            Console.WriteLine(
                $"  {cache.Misses.Count} remembered miss(es), {age}{undatedNote}. " +
                "Pass --recheck-misses to ask the wiki about them again.");
        }

        /// <summary>
        /// Re-registers the currency names a previous run settled, so a run
        /// that resolves nothing new still prices those cost lines.
        /// <para>
        /// An entry the API no longer names is dropped rather than kept. The
        /// cache is consulted before the wiki is, so a stale entry would go
        /// on naming a currency that has left the game, and nothing would
        /// ever ask again.
        /// </para>
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static void RegisterCachedCurrencyNames(ItemIdCache cache, Gw2ApiHelper apiHelper)
        {
            if (cache.CurrencyNames.Count == 0)
            {
                return;
            }

            var stale = new List<string>();
            foreach (var pair in cache.CurrencyNames)
            {
                if (!apiHelper.AddCurrencyAlias(pair.Key, pair.Value))
                {
                    stale.Add(pair.Key);
                }
            }

            foreach (var name in stale)
            {
                cache.ForgetCurrencyName(name);
            }

            Console.WriteLine(
                $"  {cache.CurrencyNames.Count} cached cost name(s) name a wallet currency." +
                (stale.Count > 0
                    ? $" Dropped {stale.Count} the API no longer names; they are asked again."
                    : string.Empty));
        }

        /// <summary>
        /// Settles the names neither the API's currency list nor the cache
        /// knows, in two steps: ask the wiki which page each name actually
        /// names, then decide whether that page is a currency or an item.
        /// Cost lines and unlock requirements both spell an item the same
        /// way, so both arrive here.
        /// <para>
        /// A name whose title request went unanswered is left for the next
        /// run rather than settled here. Nothing was asked about it, so there
        /// is nothing to record either way, and a cached miss is permanent.
        /// </para>
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static async Task ResolveUnknownNamesAsync(
            IReadOnlyList<string> unknownNames,
            WikiSmwClient wikiClient,
            Gw2ApiHelper apiHelper,
            ItemIdCache itemIdCache,
            QueryOptions queryOptions,
            CancellationToken ct)
        {
            Console.WriteLine(
                $"Resolving {unknownNames.Count} unknown name(s) via the wiki...");

            var titles = await wikiClient.ResolveTitlesAsync(unknownNames, ct, queryOptions);

            var unanswered = new List<string>();
            var itemQueries = new List<KeyValuePair<string, string>>();
            int namedCurrencies = 0;

            foreach (string name in unknownNames)
            {
                if (!titles.Answered.Contains(name))
                {
                    unanswered.Add(name);
                    continue;
                }

                string title = titles.Resolved.TryGetValue(name, out var resolved)
                    ? resolved
                    : name;

                if (apiHelper.AddCurrencyAlias(name, title))
                {
                    itemIdCache.RecordCurrencyName(name, title);
                    namedCurrencies++;
                }
                else
                {
                    itemQueries.Add(new KeyValuePair<string, string>(name, title));
                }
            }

            Console.WriteLine(
                $"  {titles.Resolved.Count} of {unknownNames.Count} point at a page spelled " +
                $"differently. {namedCurrencies} name a wallet currency outright, " +
                $"{unanswered.Count} went unanswered.");

            var update = new ItemIdCacheUpdate();
            int itemsAsked = itemQueries.Count;
            int reclassified = 0;

            if (itemQueries.Count > 0)
            {
                var askedTitles = itemQueries
                    .Select(query => query.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var resolution =
                    await wikiClient.ResolveItemGameIdsAsync(askedTitles, ct, queryOptions);

                var stillItems = new List<KeyValuePair<string, string>>();
                foreach (var query in itemQueries)
                {
                    if (ClaimAsCurrency(query, resolution, apiHelper, itemIdCache))
                    {
                        reclassified++;
                    }
                    else
                    {
                        stillItems.Add(query);
                    }
                }

                update = itemIdCache.Record(stillItems, resolution, DateTime.UtcNow);
            }

            Console.WriteLine(
                $"  Of the {itemsAsked} asked about as items: {update.Hits} resolved, " +
                $"{reclassified} turned out to be a currency, " +
                $"{update.Misses} cached as a miss the wiki answered for, " +
                $"{update.Deferred.Count} left to ask again.");

            // A cached miss is permanent - the resolution filter never asks
            // about a settled name again - so a name the wiki never answered
            // about is cached neither way, at either step.
            ReportUnsettled(
                update.Deferred,
                "These names were never answered for, so nothing was cached and the " +
                "next run asks again:");
            ReportUnsettled(
                unanswered,
                "The wiki never said which page these names point at, so they were " +
                "asked no further and nothing was cached:");
        }

        /// <summary>
        /// Settles one cost name as a currency when the wiki has answered
        /// that the page it names carries no item id. A page is one thing or
        /// the other, and this is the only point at which the wiki has said
        /// which, so it is also the only point at which a name may be matched
        /// against the currency list on anything looser than its exact text.
        /// </summary>
        private static bool ClaimAsCurrency(
            KeyValuePair<string, string> query,
            ItemIdResolution resolution,
            Gw2ApiHelper apiHelper,
            ItemIdCache itemIdCache)
        {
            bool hasItemId = resolution.Resolved.TryGetValue(query.Value, out int id) && id > 0;
            if (hasItemId || !resolution.Answered.Contains(query.Value))
            {
                return false;
            }

            string? currencyName = apiHelper.MatchCurrencyName(query.Value);
            if (currencyName == null || !apiHelper.AddCurrencyAlias(query.Key, currencyName))
            {
                return false;
            }

            itemIdCache.RecordCurrencyName(query.Key, currencyName);
            return true;
        }

        private static void ReportUnsettled(IReadOnlyList<string> names, string heading)
        {
            if (names.Count == 0)
            {
                return;
            }

            Console.WriteLine($"  {heading}");
            foreach (var name in names.Take(DeferredNamesListed))
            {
                Console.WriteLine($"    - {name}");
            }

            if (names.Count > DeferredNamesListed)
            {
                Console.WriteLine($"    ... and {names.Count - DeferredNamesListed} more");
            }
        }

        /// <summary>
        /// --diff-summary: reports what changed between two vendor datasets.
        /// Read-only - it exists so a `data(vendor):` pull request can carry a
        /// reviewable summary of a change whose own diff is one 14.8MB line.
        /// See <see cref="VendorOfferDiff"/> for what "changed" means here.
        /// </summary>
        private static async Task<int> RunDiffSummaryAsync(string beforePath, string afterPath)
        {
            foreach (string path in new[] { beforePath, afterPath })
            {
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"ERROR: --diff-summary input not found: {path}");
                    return 1;
                }
            }

            var readOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true,
            };

            var before = JsonSerializer.Deserialize<VendorOfferDataset>(
                await File.ReadAllTextAsync(beforePath), readOptions);
            var after = JsonSerializer.Deserialize<VendorOfferDataset>(
                await File.ReadAllTextAsync(afterPath), readOptions);

            if (before == null || after == null)
            {
                Console.Error.WriteLine(
                    "ERROR: --diff-summary input deserialized to null (empty or malformed JSON).");
                return 1;
            }

            var result = VendorOfferDiff.Compute(before.Offers, after.Offers);
            Console.Write(VendorOfferDiff.Format(
                result, Path.GetFileName(beforePath), Path.GetFileName(afterPath)));

            return 0;
        }

        /// <summary>
        /// Serializes a dataset into exactly the byte form
        /// ref/vendor_offers.json is checked in as.
        /// <para>
        /// System.Text.Json's DEFAULT encoder conservatively HTML-escapes
        /// '\'', '&amp;', '&lt;', '&gt;' (as <c>&amp;#x27;</c> etc.) even for pure JSON
        /// output with no HTML context - but the already-checked-in
        /// ref/vendor_offers.json never does this (confirmed: 222 literal
        /// '&amp;' characters, 0 <c>&amp;#x26;</c> escapes; "Hearth's Glow" stored with a
        /// literal apostrophe). UnsafeRelaxedJsonEscaping skips that extra
        /// HTML-safety escaping (while still escaping the JSON-mandatory
        /// '"'/'\\'/control characters) but ALSO stops escaping non-ASCII
        /// text, which the existing file DOES do (e.g. "Homestead
        /// Refinement-Farm"). <see cref="EscapeNonAscii"/> restores exactly
        /// that: non-ASCII escaped, everything else literal - matching the
        /// existing file's convention so a scoped --merge-into run's diff
        /// stays confined to the offers actually changed, not every
        /// apostrophe/ampersand in the whole 53k-row dataset.
        /// </para>
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static string SerializeDataset(VendorOfferDataset dataset)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            };

            return EscapeNonAscii(JsonSerializer.Serialize(dataset, jsonOptions));
        }

        /// <summary>
        /// Lowercase hex SHA-256 of a file's bytes. Same definition the
        /// module side uses (RecipeCacheSerializer.HashFile) - this project
        /// does not reference the module, so the four lines are repeated
        /// rather than shared, and both are pinned by the seed tests that
        /// compare a manifest digest against the file it describes.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static string HashFile(string path)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }

        /// <summary>
        /// Sibling manifest path for a dataset path: ref/vendor_offers.json
        /// becomes ref/vendor_offers_manifest.json. Derived from the dataset
        /// path rather than hardcoded so a --merge-into run against a scratch
        /// copy writes its manifest next to that copy, not over the shipped one.
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static string ManifestPathFor(string datasetPath)
        {
            string? dir = Path.GetDirectoryName(datasetPath);
            string name = Path.GetFileNameWithoutExtension(datasetPath) + "_manifest.json";
            return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
        }

        /// <summary>
        /// Indented, newline-terminated, camelCase - the manifest is meant to
        /// be read in a diff, unlike the dataset it describes.
        /// <para>
        /// The line endings are forced to LF. System.Text.Json's WriteIndented
        /// emits Environment.NewLine, so the same manifest content would be
        /// written as CRLF on Windows and LF elsewhere - a determinism bug in
        /// the file whose entire job is to make no-op refreshes provable.
        /// (JsonSerializerOptions.NewLine only exists from .NET 9; this project
        /// targets net8.0.) No manifest value can contain a newline, so the
        /// replace cannot touch anything but the formatting.
        /// </para>
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static string SerializeManifest(VendorOfferManifest manifest)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true,
            };

            string json = JsonSerializer.Serialize(manifest, jsonOptions);
            return json.Replace("\r\n", "\n") + "\n";
        }

        /// <summary>
        /// Escapes every character above U+007F as a lowercase \uXXXX
        /// sequence, leaving every ASCII character (including '\'', '&amp;',
        /// '&lt;', '&gt;') exactly as JsonSerializer.UnsafeRelaxedJsonEscaping
        /// already left it. Matches ref/vendor_offers.json's established
        /// convention exactly (see the call site's doc comment) - a plain
        /// per-char scan rather than a Regex, since this runs once over the
        /// whole serialized dataset and needs no backtracking/pattern
        /// matching. Assumes the input is already valid, escaped JSON (a
        /// literal '\' is never re-escaped here, since JsonSerializer's own
        /// escaping already turned any real backslash into "\\\\" before
        /// this runs - each '\\' char in that pair is &lt;= 0x7F and passes
        /// through unchanged, which is correct).
        /// </summary>
        // internal for testability (VendorOfferUpdater.Tests)
        internal static string EscapeNonAscii(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return json;
            }

            var sb = new StringBuilder(json.Length);
            foreach (char c in json)
            {
                if (c > 0x7F)
                {
                    sb.Append("\\u").Append(((int)c).ToString("x4"));
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Walks up from <paramref name="startDirectory"/> (the running
        /// binary's own directory by default) to the first ancestor
        /// carrying a ".git" entry, falling back to the process's working
        /// directory when none does.
        /// <para>
        /// The entry is tested as a file AND as a directory: a linked
        /// worktree's ".git" is a FILE holding a gitdir pointer, so a
        /// directory-only probe walks past the worktree root and resolves
        /// ref/ in whichever repo it meets next. Same fix, same reason, at
        /// tools/MysticForgeSeeder/Program.cs.
        /// </para>
        /// </summary>
        internal static string FindRepoRoot(string? startDirectory = null)
        {
            string? dir = startDirectory ?? AppContext.BaseDirectory;
            while (dir != null)
            {
                string gitPath = Path.Combine(dir, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            // Fallback: current directory
            return Directory.GetCurrentDirectory();
        }

        /// <summary>
        /// Removes offers named in ref/vendor_offer_exclusions.json. An
        /// entry carrying an outputItemId refuses that one (merchant, item)
        /// row; an entry with merchantName alone refuses every row of that
        /// merchant, which is what a vendor removed from the game needs -
        /// naming its 49 sales one by one would be 49 copies of a single
        /// claim, and a re-scrape that adds a 50th would slip through. A
        /// missing or unreadable file is a warning, not a failure - the
        /// refresh still produces data, it just carries rows a human had
        /// refused, which the module's own agreement test then catches.
        /// An entry matching nothing is warned about: a merchant-wide
        /// refusal that has quietly stopped matching (a wiki page rename,
        /// a typo) removes nothing and looks identical to a clean run.
        /// </summary>
        internal static int ApplyExclusions(ref List<VendorOffer> offers, string outputDir)
        {
            string path = Path.Combine(outputDir, "vendor_offer_exclusions.json");
            if (!File.Exists(path))
            {
                Console.Error.WriteLine(
                    $"Warning: no exclusion list at {path} - shipping every scraped row.");
                return 0;
            }

            var refusedRows = new HashSet<(string Merchant, int ItemId)>();
            var refusedMerchants = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using (var doc = JsonDocument.Parse(File.ReadAllText(path)))
                {
                    if (doc.RootElement.TryGetProperty("exclusions", out var arr))
                    {
                        foreach (var entry in arr.EnumerateArray())
                        {
                            if (!entry.TryGetProperty("merchantName", out var m))
                            {
                                continue;
                            }

                            string merchant = m.GetString() ?? string.Empty;
                            if (merchant.Length == 0)
                            {
                                // A blank name would match every offer whose
                                // merchantName the scrape left null.
                                Console.Error.WriteLine(
                                    "Warning: exclusion entry with a blank merchantName ignored.");
                                continue;
                            }

                            if (!entry.TryGetProperty("outputItemId", out var i))
                            {
                                refusedMerchants.Add(merchant);
                            }
                            else if (i.ValueKind == JsonValueKind.Number)
                            {
                                refusedRows.Add((merchant, i.GetInt32()));
                            }
                            else
                            {
                                // Never widened to a merchant-wide refusal:
                                // a mistyped id must drop nothing, not
                                // everything the merchant sells.
                                Console.Error.WriteLine(
                                    $"Warning: exclusion for \"{merchant}\" has a " +
                                    "non-numeric outputItemId - entry ignored.");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"Warning: could not read the exclusion list: {ex.Message}");
                return 0;
            }

            if (refusedRows.Count == 0 && refusedMerchants.Count == 0)
            {
                return 0;
            }

            var unmatchedRows = new HashSet<(string Merchant, int ItemId)>(refusedRows);
            var unmatchedMerchants = new HashSet<string>(refusedMerchants, StringComparer.Ordinal);
            var kept = new List<VendorOffer>(offers.Count);
            foreach (var offer in offers)
            {
                string merchant = offer.MerchantName ?? string.Empty;
                if (refusedMerchants.Contains(merchant))
                {
                    unmatchedMerchants.Remove(merchant);
                    continue;
                }

                var key = (merchant, offer.OutputItemId);
                if (refusedRows.Contains(key))
                {
                    unmatchedRows.Remove(key);
                    continue;
                }

                kept.Add(offer);
            }

            foreach (string merchant in unmatchedMerchants)
            {
                Console.Error.WriteLine(
                    $"Warning: exclusion for merchant \"{merchant}\" matched no offer.");
            }

            foreach (var (merchant, itemId) in unmatchedRows)
            {
                // A row entry under a merchant this file also refuses
                // wholesale never gets the chance to match, so warning
                // about it would report a fault that is not there.
                if (refusedMerchants.Contains(merchant))
                {
                    continue;
                }

                Console.Error.WriteLine(
                    $"Warning: exclusion for \"{merchant}\" item {itemId} matched no offer.");
            }

            int before = offers.Count;
            offers = kept;
            return before - offers.Count;
        }
    }
}
