using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace VendorOfferUpdater
{
    /// <summary>
    /// Queries the GW2 Wiki Semantic MediaWiki API for vendor offer data.
    /// Uses the action=ask endpoint with vendor-related properties.
    /// See docs/ARCHITECTURE.md section 9 in the main module repo (data
    /// pipeline: seeds, wiki scrapes, dev-only caches) for how this tool's
    /// output feeds the shipped module.
    /// </summary>
    public class WikiSmwClient
    {
        private const string WikiApiUrl = "https://wiki.guildwars2.com/api.php";
        private const int QueryLimit = 500;

        // MediaWiki caps action=query at 50 titles per request for a client
        // without the apihighlimits right, which this one is. Asking for more
        // is refused outright rather than truncated.
        private const int TitleBatchSize = 50;

        // Asks the server to refuse the query while its database replicas are
        // more than this many seconds behind, which is MediaWiki's documented
        // way of shedding load before it starts blocking a client outright.
        // A refused maxlag query is an ordinary retryable API error here.
        private const int MaxLagSeconds = 5;

        private readonly HttpClient _httpClient;

        // Sections the wiki never answered. Accumulated across every call on
        // this client, including ResolveItemGameIdsAsync, which has no
        // QueryStats of its own.
        private readonly List<UnresolvedSection> _unresolved = new List<UnresolvedSection>();

        // Per-query state. _options carries a default so a client method
        // reached without a QueryVendorItemsAsync call first still throttles
        // and still retries; _stats and _stopwatch are null-forgiven because
        // only the pagination path reads them.
        private QueryOptions _options = new QueryOptions();
        private QueryStats _stats = null!;
        private Stopwatch _stopwatch = null!;
        private int _effectiveDelay = QueryOptions.DefaultDelayBetweenRequestsMs;
        private int _consecutiveUnresolved;

        public WikiSmwClient(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        /// <summary>
        /// Sections this client asked for and never got an answer to. Empty
        /// on a clean run. Each entry carries the query that failed.
        /// </summary>
        public IReadOnlyList<UnresolvedSection> UnresolvedSections => _unresolved;

        // Characters appended to a vendor-name prefix when splitting a query
        // past the wiki's ~5500 result offset limit. Both the case and the
        // punctuation are load-bearing, and neither is a free choice:
        // the glob is a byte-wise LIKE over a sortkey, so it is
        // case-sensitive, a space is a real character to split on and an
        // underscore is not, and the leading double quote is the only way to
        // reach five live merchants. Every character here appears in a
        // merchant name in ref/vendor_offers.json. Widening the set costs one
        // request per overflowing partition; the derivation, from Semantic
        // MediaWiki's own source, is docs/ARCHITECTURE.md section T.9.
        private static readonly string[] PartitionCharacters =
            ("ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
             "abcdefghijklmnopqrstuvwxyz" +
             "0123456789" +
             " -'.,/():&\"")
                .Select(c => c.ToString()).ToArray();

        /// <summary>
        /// What a partition's own query returned. A partition that was never
        /// answered is not an empty one, and a parent that overflowed cannot
        /// have children that are all empty.
        /// </summary>
        private enum PartitionOutcome
        {
            Rows,
            NoRows,
            Unresolved,
        }

        // Note: the wiki also exposes "Has character purchase cap" and
        // "Has total purchase cap" on the same per-offer subobjects, but the
        // module has no consuming model for either (the solver has no
        // account/character concept at all) - deliberately not scraped here.
        // See KNOWN-ISSUES #28.
        //
        // "Has seasonal purchase cap" (Astral Acclaim package): IS scraped
        // below - live-confirmed exclusively on the "Wizard's Vault" /
        // "Wizard's Vault/Historical Astral Rewards" / "Wizard's Vault/Legacy
        // Rewards" subobjects (a wiki-wide `[[Has seasonal purchase cap::+]]`
        // probe returned 29 rows, all three under one of those page names -
        // no other vendor on the wiki uses this property). The parsed value
        // is threaded into VendorOffer.SeasonalCap and the hasher, and is now
        // also consumed by the runtime solver via
        // TimegatedCapType.Seasonal (see KNOWN-ISSUES #33 /
        // VendorBatchSolver.FinalizeVendorBatches).
        //
        // "Has requirement" is populated by the
        // {{vendor table row|requirement=...}} parameter (confirmed live via
        // a direct SMW ask probe against Homestead Refinement-Metal Forge:
        // tier-0 rows return an empty array, tier-1/tier-2 rows return
        // exactly "one [[Homestead Upgrade: ...]]" / "two [[Homestead
        // Upgrade: ...]]" as a single _txt-typed value) - this is how
        // ConvertToOffer/HomesteadTierResolver determine a Homestead
        // Refinement offer's efficiency tier. The property is generic
        // (used by many non-Homestead vendor rows too, e.g. achievement
        // gates); HomesteadTierResolver only interprets it for the three
        // known Homestead Refinement merchant names.
        private static readonly string PrintoutSuffix =
            "|?Sells item.Has game id" +
            "|?Sells item" +
            "|?Has item quantity" +
            "|?Has item cost" +
            "|?Has vendor" +
            "|?Located in" +
            "|?Has daily purchase cap" +
            "|?Has weekly purchase cap" +
            "|?Has seasonal purchase cap" +
            "|?Has requirement";

        /// <summary>
        /// Queries the wiki for items sold by vendors, returning the raw
        /// parsed rows and this query's statistics.
        ///
        /// Vendor data lives on subobject pages (e.g. "NPC#vendor1") with properties:
        ///   Sells item          - the item page
        ///   Sells item.Has game id - item's GW2 game ID (property chain)
        ///   Has item quantity    - output count
        ///   Has item cost        - record: { Has item value, Has item currency }
        ///   Has vendor           - NPC page
        ///   Located in           - location pages
        ///   Has daily purchase cap  - daily purchase limit (absent = uncapped)
        ///   Has weekly purchase cap - weekly purchase limit (absent = uncapped)
        ///   Has seasonal purchase cap - Wizard's Vault seasonal purchase limit
        ///                               (absent = uncapped or not a Vault offer)
        ///
        /// Past the SMW API's ~5500-result limit per condition, the query is
        /// split by vendor name prefix: see PartitionCharacters.
        /// </summary>
        public async Task<(List<WikiVendorResult> Results, QueryStats Stats)> QueryVendorItemsAsync(
            string? queryCondition = null, QueryOptions? options = null, CancellationToken ct = default)
        {
            _options = options ?? new QueryOptions();
            _effectiveDelay = _options.DelayBetweenRequestsMs;
            _stats = new QueryStats();
            _stopwatch = Stopwatch.StartNew();

            string condition = queryCondition ?? "[[Sells item::+]]";

            if (_options.DryRun)
            {
                PrintDryRunPlan(condition);
                return (new List<WikiVendorResult>(), _stats);
            }

            var allResults = new List<WikiVendorResult>();
            var seenKeys = new HashSet<string>(StringComparer.Ordinal);

            try
            {
                await PaginateConditionAsync(condition, null, 0, allResults, seenKeys, ct);
            }
            catch (SafetyLimitException ex)
            {
                Console.WriteLine($"  SAFETY LIMIT: {ex.Message}");
                Console.WriteLine($"  Returning {seenKeys.Count} partial results collected so far.");
                _stats.WasInterrupted = true;
            }

            _stopwatch.Stop();
            _stats.Elapsed = _stopwatch.Elapsed;
            _stats.DistinctResults = seenKeys.Count;

            // Names whose first character no prefix can reach. Measured
            // against the split's own character set rather than a separate
            // notion of "alphanumeric": those two disagreeing is what makes a
            // warning claim a gap that is not there, or miss one that is.
            var firstCharacters = new HashSet<string>(PartitionCharacters, StringComparer.Ordinal);
            var nonAlpha = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in allResults)
            {
                if (!string.IsNullOrEmpty(r.MerchantName) &&
                    !firstCharacters.Contains(r.MerchantName[0].ToString()))
                {
                    nonAlpha.Add(r.MerchantName);
                }
            }

            foreach (var name in nonAlpha.OrderBy(n => n, StringComparer.Ordinal))
            {
                _stats.NonAlphaVendors.Add(name);
            }

            return (allResults, _stats);
        }

        private async Task<PartitionOutcome> PaginateConditionAsync(
            string baseCondition,
            string? vendorPrefix,
            int depth,
            List<WikiVendorResult> allResults,
            HashSet<string> seenKeys,
            CancellationToken ct)
        {
            string condition = baseCondition;
            if (vendorPrefix != null)
            {
                condition += $"[[Has vendor::~{vendorPrefix}*]]";
            }

            string label = vendorPrefix ?? "all";

            int partitionRowsAdded = 0;
            int partitionHttpRequests = 0;

            // Rows the wiki RETURNED, before deduplication. A sub-partition
            // re-fetches from offset 0, so its rows are usually all duplicates
            // of the parent's and add nothing; that is still an answer of
            // "this prefix holds rows", which is what the overflow arithmetic
            // below is checked against.
            int partitionRowsReturned = 0;
            bool hitOffsetLimit = false;
            bool sectionUnresolved = false;
            int offset = 0;

            while (true)
            {
                CheckSafetyLimits(ct, label, depth, seenKeys.Count);

                var query = condition +
                    PrintoutSuffix +
                    $"|limit={QueryLimit}" +
                    $"|offset={offset}";

                var url = BuildAskUrl(query);

                _stats.TotalHttpRequests++;
                partitionHttpRequests++;

                string response;
                try
                {
                    response = await FetchWithRetryAsync(url, label, condition, ct);
                }
                catch (HttpRequestException ex)
                {
                    RecordUnresolved("partition", label, vendorPrefix, condition, ex);
                    sectionUnresolved = true;
                    break;
                }

                using var doc = JsonDocument.Parse(response);
                var reading = WikiAskResponse.Read(doc.RootElement);

                if (reading.Shape == WikiAskShape.NoRows)
                {
                    RecordSectionAnswered();
                    break;
                }

                if (reading.Shape != WikiAskShape.Rows)
                {
                    // Neither rows nor an empty result set. Not proof this
                    // section is empty, so it is recorded, not skipped.
                    RecordUnresolved(
                        "partition",
                        label,
                        vendorPrefix,
                        condition,
                        reading.Error ?? UnreadableResponse());
                    sectionUnresolved = true;
                    break;
                }

                RecordSectionAnswered();

                var root = doc.RootElement;
                var results = reading.Results;

                int batchAdded = 0;
                foreach (var resultProp in results.EnumerateObject())
                {
                    partitionRowsReturned++;

                    var parsed = ParseResult(resultProp.Name, resultProp.Value);
                    if (parsed == null)
                    {
                        continue;
                    }

                    _stats.TotalRowsFetched++;
                    string compositeKey = ComputeCompositeKey(parsed);
                    if (seenKeys.Add(compositeKey))
                    {
                        allResults.Add(parsed);
                        partitionRowsAdded++;
                        batchAdded++;
                    }
                    else
                    {
                        _stats.DuplicatesDiscarded++;
                    }
                }

                Console.WriteLine($"  [{label}] offset={offset} +{batchAdded} new");

                if (root.TryGetProperty("query-continue-offset", out var continueOffset))
                {
                    int nextOffset = continueOffset.GetInt32();
                    if (nextOffset <= offset)
                    {
                        // SMW offset limit reached
                        hitOffsetLimit = true;
                        break;
                    }

                    offset = nextOffset;
                    await Task.Delay(_effectiveDelay, ct);
                }
                else
                {
                    break;
                }
            }

            // Record partition stats
            var pStats = new PartitionStats
            {
                Prefix = vendorPrefix,
                Depth = depth,
                RowsAdded = partitionRowsAdded,
                HttpRequests = partitionHttpRequests,
            };
            _stats.Partitions.Add(pStats);

            if (sectionUnresolved)
            {
                Console.WriteLine(
                    $"  [{label}] UNRESOLVED: kept {partitionRowsAdded} row(s) already " +
                    "collected, continuing with the rest of the run.");
                return PartitionOutcome.Unresolved;
            }

            if (!hitOffsetLimit)
            {
                Console.WriteLine(
                    $"  [{label}] done: {partitionRowsAdded} rows in {partitionHttpRequests} requests");
                return partitionRowsReturned > 0 ? PartitionOutcome.Rows : PartitionOutcome.NoRows;
            }

            // OVERFLOW - check depth limit
            if (depth >= _options.MaxPrefixDepth)
            {
                pStats.WasTruncated = true;
                _stats.TruncatedPartitions++;

                // Truncation is silent data loss, not a warning: this prefix
                // holds rows past the offset limit that no query in this run
                // will ask for. Recorded so the coverage gate can refuse to
                // publish a dataset with a hole in it.
                RecordUnresolved(
                    "partition",
                    label,
                    vendorPrefix,
                    condition,
                    new WikiApiError(
                        "truncated-at-max-depth",
                        $"Overflowed the offset limit at max depth {depth} with " +
                        $"{partitionRowsAdded} rows kept. Rows past that are not fetched. " +
                        "Raise --max-depth to split this prefix further."));
                return PartitionOutcome.Rows;
            }

            Console.WriteLine(
                $"  [{label}] overflow at depth {depth}, splitting on the next character...");

            // Split and paginate each child (KEEP all rows already collected).
            // There is no separate probe request: an empty child costs one
            // request to paginate and one to probe, so probing only added a
            // request per non-empty child, and a second place to read a
            // response and call it empty.
            int childrenWithRows = 0;
            int emptyChildren = 0;
            int unresolvedChildren = 0;
            var emptyPrefixes = new List<string>();

            foreach (var character in PartitionCharacters)
            {
                string subPrefix = (vendorPrefix ?? string.Empty) + character;

                var outcome = await PaginateConditionAsync(
                    baseCondition, subPrefix, depth + 1, allResults, seenKeys, ct);

                switch (outcome)
                {
                    case PartitionOutcome.Rows:
                        childrenWithRows++;
                        break;
                    case PartitionOutcome.NoRows:
                        emptyChildren++;
                        emptyPrefixes.Add($"'{subPrefix}'");
                        break;
                    default:
                        unresolvedChildren++;
                        break;
                }

                await Task.Delay(_effectiveDelay, ct);
            }

            // The prefixes are named, not just counted: a count alone cannot
            // tell "these prefixes hold no vendors" from "these queries never
            // matched the way their names are actually spelled".
            Console.WriteLine(
                $"  [{label}] split done: {childrenWithRows} with rows, " +
                $"{emptyChildren} empty, {unresolvedChildren} unresolved" +
                (emptyPrefixes.Count > 0 ? $". Empty: {string.Join(" ", emptyPrefixes)}" : string.Empty));

            // The arithmetic that makes an unreachable prefix impossible to
            // miss. This partition returned more rows than one query can
            // page through, so at least one child must hold rows. Every child
            // answering "none" is a contradiction, and it means the split is
            // not reaching the names - not that the names are not there.
            if (childrenWithRows == 0 && unresolvedChildren == 0)
            {
                RecordUnresolved(
                    "partition",
                    label,
                    vendorPrefix,
                    condition,
                    new WikiApiError(
                        "empty-subpartitions",
                        $"Overflowed the offset limit, so this prefix holds more rows " +
                        $"than one query returns, yet all {emptyChildren} sub-prefixes " +
                        "returned none. The split is not reaching those names."));
            }

            return PartitionOutcome.Rows;
        }

        private void CheckSafetyLimits(
            CancellationToken ct, string label, int depth, int distinctCount)
        {
            ct.ThrowIfCancellationRequested();

            if (HasStoppedAnswering())
            {
                throw new SafetyLimitException(
                    $"{_consecutiveUnresolved} sections in a row went unanswered, " +
                    $"the last at [{label}] depth={depth}. Stopping rather than " +
                    "asking the wiki the same question section by section. " +
                    $"Rows: {_stats.TotalRowsFetched} ({distinctCount} distinct).");
            }

            if (_stats.TotalHttpRequests >= _options.MaxTotalRequests)
            {
                throw new SafetyLimitException(
                    $"Exceeded {_options.MaxTotalRequests} request limit " +
                    $"at partition [{label}] depth={depth}. " +
                    $"Requests: {_stats.TotalHttpRequests}, " +
                    $"Rows: {_stats.TotalRowsFetched} ({distinctCount} distinct).");
            }

            if (_stopwatch.Elapsed >= _options.MaxRuntime)
            {
                throw new SafetyLimitException(
                    $"Exceeded {_options.MaxRuntime.TotalMinutes:F0}min runtime limit " +
                    $"at partition [{label}] depth={depth}. " +
                    $"Requests: {_stats.TotalHttpRequests}, " +
                    $"Rows: {_stats.TotalRowsFetched} ({distinctCount} distinct).");
            }
        }

        private void PrintDryRunPlan(string condition)
        {
            Console.WriteLine("=== DRY RUN ===");
            Console.WriteLine($"Base condition: {condition}");
            Console.WriteLine();
            Console.WriteLine("Configured caps:");
            Console.WriteLine($"  Max prefix depth:  {_options.MaxPrefixDepth}");
            Console.WriteLine($"  Max total requests: {_options.MaxTotalRequests}");
            Console.WriteLine($"  Max runtime:        {_options.MaxRuntime.TotalMinutes:F0} min");
            Console.WriteLine($"  Delay between reqs: {_effectiveDelay} ms");
            Console.WriteLine($"  Attempts per ask:   {Math.Max(1, _options.MaxAttempts)}");
            Console.WriteLine();
            Console.WriteLine("Traversal structure:");
            Console.WriteLine($"  Level 0: 1 root partition");
            for (int d = 1; d <= _options.MaxPrefixDepth; d++)
            {
                double maxPartitions = Math.Pow(PartitionCharacters.Length, d);
                Console.WriteLine(
                    $"  Level {d}: up to {maxPartitions:N0} prefixes " +
                    $"({PartitionCharacters.Length} per overflow at level {d - 1})");
            }

            Console.WriteLine();
            Console.WriteLine(
                "A level is only reached where the level above it overflowed, so the "
                + "actual request count depends on data distribution and is bounded by "
                + $"--max-requests ({_options.MaxTotalRequests}).");
        }

        private static string ComputeCompositeKey(WikiVendorResult r)
        {
            string merchant = (r.MerchantName ?? "").Trim();
            merchant = Regex.Replace(merchant, @"\s+", " ");

            var costs = r.CostEntries
                .OrderBy(c => c.Currency ?? "", StringComparer.Ordinal)
                .ThenBy(c => c.Value)
                .Select(c => $"{c.Value}:{c.Currency ?? ""}")
                .ToArray();

            // Requirement is folded in so two rows
            // that differ ONLY by requirement text are not conflated as
            // "the same row seen twice" - the real, wiki-documented Potato
            // anomaly (Homestead Refinement-Farm: the tier-1 row is not
            // discounted from the tier-0 row, so both are "8 Potato -> 1
            // Fiber" with different Requirement text) would otherwise be
            // silently collapsed to one row here, before ConvertToOffer/the
            // OfferId hasher ever get a chance to tell them apart by tier.
            return $"{r.GameId}|{r.OutputQuantity ?? 1}|{merchant}|{string.Join(";", costs)}|{r.Requirement ?? ""}";
        }

        /// <summary>
        /// Issues one ask and returns its body, retrying the failures the
        /// wiki is expected to produce: HTTP 403, 429 and 5xx, and an HTTP
        /// 200 whose body is an API error rather than a result set. The last
        /// of those is the reason this returns a body only when the body
        /// holds an answer: every earlier caller had to decide for itself
        /// what a refusal meant, and both decided it meant "no rows".
        /// </summary>
        /// <param name="url">The request URL.</param>
        /// <param name="section">The section this request belongs to.</param>
        /// <param name="condition">The query condition, for the log.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <param name="retryApiErrors">
        /// Whether an HTTP 200 API error is retried and then thrown. False
        /// returns the error body to the caller instead.
        /// </param>
        private async Task<string> FetchWithRetryAsync(
            string url,
            string section,
            string condition,
            CancellationToken ct,
            bool retryApiErrors = true)
        {
            int maxAttempts = Math.Max(1, _options.MaxAttempts);

            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var response = await _httpClient.GetAsync(url, ct);
                    int statusCode = (int)response.StatusCode;

                    if (statusCode == 403)
                    {
                        // 403 is often a temporary block from the wiki.
                        // Use a long cooldown (30s base) with exponential backoff + jitter.
                        if (attempt >= maxAttempts)
                        {
                            throw new HttpRequestException(
                                $"HTTP 403 Forbidden after {maxAttempts} attempts. " +
                                "The wiki may be rate-limiting this IP. " +
                                "Try increasing --delay or waiting before retrying.");
                        }

                        int cooldownMs = BackoffMs(attempt) * 30;
                        if (response.Headers.RetryAfter?.Delta is TimeSpan delta403)
                        {
                            cooldownMs = Math.Max(cooldownMs, (int)delta403.TotalMilliseconds);
                        }

                        // Add jitter: +/-10%
                        int jitter = (int)(cooldownMs * 0.1);
                        cooldownMs += Random.Shared.Next(-jitter, jitter + 1);
                        cooldownMs = Math.Max(cooldownMs, 0);

                        Console.WriteLine(
                            $"    WARNING: HTTP 403 (possible rate-limit block), " +
                            $"cooling down {cooldownMs / 1000}s " +
                            $"(attempt {attempt}/{maxAttempts})...");
                        await Task.Delay(cooldownMs, ct);
                        continue;
                    }

                    if (statusCode == 429 || statusCode >= 500)
                    {
                        if (attempt >= maxAttempts)
                        {
                            response.EnsureSuccessStatusCode();
                        }

                        int backoffMs = BackoffMs(attempt);
                        if (response.Headers.RetryAfter?.Delta is TimeSpan delta)
                        {
                            backoffMs = Math.Max(backoffMs, (int)delta.TotalMilliseconds);
                        }

                        // maxlag refusals arrive as 503 with the reason in the
                        // body, so the body is read here too rather than only
                        // on the 200 path.
                        string failureBody = await response.Content.ReadAsStringAsync();
                        var statusError = WikiAskResponse.ReadApiError(failureBody);

                        Console.WriteLine(
                            $"    HTTP {statusCode}, retrying in {backoffMs}ms " +
                            $"(attempt {attempt}/{maxAttempts})...");
                        if (statusError != null)
                        {
                            LogApiError(statusError, section, condition, attempt, maxAttempts);
                        }

                        await Task.Delay(backoffMs, ct);
                        continue;
                    }

                    response.EnsureSuccessStatusCode();
                    string body = await response.Content.ReadAsStringAsync();

                    var apiError = WikiAskResponse.ReadApiError(body);
                    if (apiError == null || !retryApiErrors)
                    {
                        return body;
                    }

                    LogApiError(apiError, section, condition, attempt, maxAttempts);

                    if (attempt >= maxAttempts)
                    {
                        throw new WikiApiErrorException(apiError, section, maxAttempts);
                    }

                    await Task.Delay(BackoffMs(attempt), ct);
                }
                catch (WikiApiErrorException)
                {
                    throw;
                }
                catch (HttpRequestException) when (attempt < maxAttempts)
                {
                    int backoffMs = BackoffMs(attempt);
                    Console.WriteLine(
                        $"    Request failed, retrying in {backoffMs}ms " +
                        $"(attempt {attempt}/{maxAttempts})...");
                    await Task.Delay(backoffMs, ct);
                }
            }

            throw new HttpRequestException($"Failed after {maxAttempts} attempts: {url}");
        }

        private int BackoffMs(int attempt)
        {
            return Math.Max(0, _options.RetryBackoffBaseMs) * (1 << (attempt - 1));
        }

        private static string BuildAskUrl(string query)
        {
            return $"{WikiApiUrl}?action=ask&query={Uri.EscapeDataString(query)}" +
                   $"&format=json&maxlag={MaxLagSeconds}";
        }

        private static string BuildTitleUrl(string pipeSeparatedTitles)
        {
            return $"{WikiApiUrl}?action=query" +
                   $"&titles={Uri.EscapeDataString(pipeSeparatedTitles)}" +
                   $"&redirects=1&format=json&maxlag={MaxLagSeconds}";
        }

        private static WikiApiError UnreadableResponse()
        {
            return new WikiApiError(
                "unreadable-response",
                "The response was neither a result set nor an API error.");
        }

        /// <summary>
        /// Prints the refusal in full. Nothing recorded what these errors
        /// actually say before this, so the code, the text, the query and the
        /// attempt all go to the log rather than a summary count.
        /// </summary>
        private static void LogApiError(
            WikiApiError error, string section, string condition, int attempt, int maxAttempts)
        {
            Console.WriteLine(
                $"    WIKI API ERROR [{section}] attempt {attempt}/{maxAttempts}: code={error.Code}");
            Console.WriteLine($"      info:  {error.Info}");
            Console.WriteLine($"      query: {condition}");
        }

        private void RecordUnresolved(
            string kind, string label, string? prefix, string condition, WikiApiError error)
        {
            RecordUnresolved(
                kind, label, prefix, condition, error.Code, error.Info, Math.Max(1, _options.MaxAttempts));
        }

        private void RecordUnresolved(
            string kind, string label, string? prefix, string condition, HttpRequestException ex)
        {
            bool refused = ex is WikiApiErrorException;
            string code = refused ? ((WikiApiErrorException)ex).Error.Code : "transport";
            int attempts = refused
                ? ((WikiApiErrorException)ex).Attempts
                : Math.Max(1, _options.MaxAttempts);
            RecordUnresolved(kind, label, prefix, condition, code, ex.Message, attempts);
        }

        private void RecordUnresolved(
            string kind,
            string label,
            string? prefix,
            string condition,
            string code,
            string reason,
            int attempts)
        {
            _unresolved.Add(new UnresolvedSection
            {
                Kind = kind,
                Label = label,
                Prefix = prefix,
                Condition = condition,
                ErrorCode = code,
                Reason = reason,
                Attempts = attempts,
            });

            _consecutiveUnresolved++;
            Console.WriteLine($"    UNRESOLVED {kind} [{label}]: {code} - {reason}");
        }

        private void RecordSectionAnswered()
        {
            _consecutiveUnresolved = 0;
        }

        private bool HasStoppedAnswering()
        {
            return _consecutiveUnresolved >= _options.MaxConsecutiveUnresolvedSections;
        }

        private static WikiVendorResult? ParseResult(string pageName, JsonElement element)
        {
            if (!element.TryGetProperty("printouts", out var printouts))
            {
                return null;
            }

            var result = new WikiVendorResult { PageName = pageName };

            // Sells item.Has game id (property chain result)
            if (printouts.TryGetProperty("Has game id", out var gameIds) &&
                gameIds.GetArrayLength() > 0)
            {
                result.GameId = gameIds[0].GetInt32();
            }

            // Sells item (item page name, for logging)
            if (printouts.TryGetProperty("Sells item", out var sellsItem) &&
                sellsItem.GetArrayLength() > 0)
            {
                var item = sellsItem[0];
                if (item.TryGetProperty("fulltext", out var fulltext))
                {
                    result.ItemName = fulltext.GetString();
                }
            }

            // Has item quantity
            if (printouts.TryGetProperty("Has item quantity", out var qty) &&
                qty.GetArrayLength() > 0)
            {
                result.OutputQuantity = qty[0].GetInt32();
            }

            // Has daily purchase cap - empty array means no cap (stays null, not 0)
            if (printouts.TryGetProperty("Has daily purchase cap", out var dailyCap) &&
                dailyCap.GetArrayLength() > 0)
            {
                result.DailyCap = dailyCap[0].GetInt32();
            }

            // Has weekly purchase cap - empty array means no cap (stays null, not 0)
            if (printouts.TryGetProperty("Has weekly purchase cap", out var weeklyCap) &&
                weeklyCap.GetArrayLength() > 0)
            {
                result.WeeklyCap = weeklyCap[0].GetInt32();
            }

            // Has seasonal purchase cap - same empty-array-means-uncapped shape.
            // Astral Acclaim package: live-confirmed exclusively on Wizard's
            // Vault subobjects (see the PrintoutSuffix doc comment above).
            if (printouts.TryGetProperty("Has seasonal purchase cap", out var seasonalCap) &&
                seasonalCap.GetArrayLength() > 0)
            {
                result.SeasonalCap = seasonalCap[0].GetInt32();
            }

            // Has requirement - a _txt property, so
            // its array entries are plain strings (e.g. "one [[Homestead
            // Upgrade: Ore Trade Efficiency]]"), not page-link objects.
            // First non-empty entry only: confirmed live that Homestead
            // Refinement rows carry at most one value (no vendor-level
            // default requirement is set on these pages) - see
            // HomesteadTierResolver for the actual tier interpretation.
            if (printouts.TryGetProperty("Has requirement", out var requirement) &&
                requirement.GetArrayLength() > 0)
            {
                result.Requirement = requirement[0].GetString();
            }

            // Has item cost - record type containing nested fields
            if (printouts.TryGetProperty("Has item cost", out var costArray))
            {
                foreach (var costRecord in costArray.EnumerateArray())
                {
                    var entry = new WikiCostEntry();

                    if (costRecord.TryGetProperty("Has item value", out var valueObj) &&
                        valueObj.TryGetProperty("item", out var valueItems) &&
                        valueItems.GetArrayLength() > 0)
                    {
                        var rawVal = valueItems[0].GetString();
                        if (int.TryParse(rawVal, out int parsed))
                        {
                            entry.Value = parsed;
                        }
                    }

                    if (costRecord.TryGetProperty("Has item currency", out var currObj) &&
                        currObj.TryGetProperty("item", out var currItems) &&
                        currItems.GetArrayLength() > 0)
                    {
                        entry.Currency = currItems[0].GetString();
                    }

                    if (entry.Value > 0)
                    {
                        result.CostEntries.Add(entry);
                    }
                }
            }

            // Has vendor (NPC page)
            if (printouts.TryGetProperty("Has vendor", out var vendor) &&
                vendor.GetArrayLength() > 0)
            {
                var v = vendor[0];
                if (v.TryGetProperty("fulltext", out var vName))
                {
                    result.MerchantName = vName.GetString();
                }
            }

            // Located in (location pages)
            if (printouts.TryGetProperty("Located in", out var locations))
            {
                foreach (var loc in locations.EnumerateArray())
                {
                    if (loc.TryGetProperty("fulltext", out var locName))
                    {
                        // "fulltext" is always a JSON string on a page-link
                        // object, never JSON null.
                        result.Locations.Add(locName.GetString()!);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Resolves item names to GW2 game IDs by querying wiki pages directly.
        /// Uses the page title as the SMW subject (e.g. [[Piece of Candy Corn]])
        /// rather than matching on property values, which is more reliable across
        /// redirects and naming variants.
        /// Names are batched using [[A||B||C]] OR syntax to minimize requests.
        /// <para>
        /// Pass <paramref name="options"/> on a run that never called
        /// QueryVendorItemsAsync: this method is the whole of the
        /// --resolve-item-currencies-only pass, and without them it would use
        /// the built-in delay and attempt count rather than the run's.
        /// </para>
        /// </summary>
        public async Task<ItemIdResolution> ResolveItemGameIdsAsync(
            IEnumerable<string> itemNames,
            CancellationToken ct = default,
            QueryOptions? options = null)
        {
            if (options != null)
            {
                _options = options;
                _effectiveDelay = options.DelayBetweenRequestsMs;
            }

            var resolution = new ItemIdResolution();
            var result = resolution.Resolved;
            var names = itemNames.ToList();

            int delay = _effectiveDelay;

            // Batch into groups - wiki SMW limits query complexity (OR conditions).
            // 50 items per batch exceeds the wiki's depth limit; 10 is safe.
            const int batchSize = 10;

            for (int i = 0; i < names.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();

                if (HasStoppedAnswering())
                {
                    // Sections, not batches: the count carries over from the
                    // vendor query on this same client, so a wiki that shut
                    // during the scrape is not asked 111 more questions here.
                    Console.WriteLine(
                        $"  WARNING: {_consecutiveUnresolved} section(s) in a row went " +
                        $"unanswered. Stopping with {result.Count} resolved.");
                    break;
                }

                var batch = names.Skip(i).Take(batchSize).ToList();
                var condition = "[[" + string.Join("||", batch) + "]]";
                var query = condition + "|?Has game id";

                var url = BuildAskUrl(query);
                string label = $"item batch {i / batchSize + 1}";

                Console.WriteLine(
                    $"  Resolving batch {i / batchSize + 1} ({batch.Count} items)...");

                string response;
                try
                {
                    response = await FetchWithRetryAsync(url, label, condition, ct);
                }
                catch (WikiApiErrorException ex)
                {
                    // A refusal is one batch's problem. The next batch is a
                    // different query and may well be answered, so the run
                    // continues and this batch is recorded for a re-target.
                    RecordUnresolved("item-batch", label, null, condition, ex);
                    if (i + batchSize < names.Count)
                    {
                        await Task.Delay(delay, ct);
                    }

                    continue;
                }
                catch (HttpRequestException ex)
                {
                    // Transport, not refusal: the wiki is unreachable rather
                    // than unwilling, so pressing on would only add requests.
                    RecordUnresolved("item-batch", label, null, condition, ex);
                    Console.WriteLine(
                        $"  WARNING: Item resolution interrupted at batch {i / batchSize + 1}: {ex.Message}");
                    Console.WriteLine(
                        $"  Returning {result.Count} partial results.");
                    break;
                }

                using var doc = JsonDocument.Parse(response);
                var reading = WikiAskResponse.Read(doc.RootElement);

                if (reading.Shape == WikiAskShape.Rows || reading.Shape == WikiAskShape.NoRows)
                {
                    RecordSectionAnswered();

                    // The wiki answered for this batch, so a name it returned
                    // no id for is a genuine absence rather than a question
                    // that was never put. Only the caller can act on that
                    // difference, and only if it is told which names these
                    // are - a flat dictionary of hits cannot say.
                    foreach (var name in batch)
                    {
                        resolution.Answered.Add(name);
                    }
                }

                if (reading.Shape == WikiAskShape.Rows)
                {
                    foreach (var prop in reading.Results.EnumerateObject())
                    {
                        if (prop.Value.TryGetProperty("printouts", out var printouts) &&
                            printouts.TryGetProperty("Has game id", out var gameIds) &&
                            gameIds.GetArrayLength() > 0)
                        {
                            int gameId = gameIds[0].GetInt32();
                            if (gameId > 0)
                            {
                                result[prop.Name] = gameId;
                            }
                        }
                    }
                }
                else if (reading.Shape != WikiAskShape.NoRows)
                {
                    RecordUnresolved(
                        "item-batch",
                        label,
                        null,
                        condition,
                        reading.Error ?? UnreadableResponse());
                }

                if (i + batchSize < names.Count)
                {
                    await Task.Delay(delay, ct);
                }
            }

            return resolution;
        }

        /// <summary>
        /// Asks MediaWiki which page each display string actually names,
        /// following redirects and title normalization.
        /// <para>
        /// A cost's currency reaches this tool as the wiki's display text,
        /// and that text and the canonical page title disagree often: a
        /// plural form, a doubled space, a reworded chest name. No string
        /// rule reaches a rename, so the wiki has to answer. This is the
        /// same omission docs/ARCHITECTURE.md section T.6 records for
        /// FetchWikitextAsync, on the other endpoint.
        /// </para>
        /// <para>
        /// Names in a batch the wiki never answered are absent from
        /// <see cref="TitleResolution.Answered"/>. They did not resolve to
        /// themselves - nothing was asked - and a caller that records an
        /// absence has to tell the two apart.
        /// </para>
        /// </summary>
        public async Task<TitleResolution> ResolveTitlesAsync(
            IEnumerable<string> titles,
            CancellationToken ct = default,
            QueryOptions? options = null)
        {
            if (options != null)
            {
                _options = options;
                _effectiveDelay = options.DelayBetweenRequestsMs;
            }

            var resolution = new TitleResolution();
            var names = titles.ToList();

            for (int i = 0; i < names.Count; i += TitleBatchSize)
            {
                ct.ThrowIfCancellationRequested();

                if (HasStoppedAnswering())
                {
                    Console.WriteLine(
                        $"  WARNING: {_consecutiveUnresolved} section(s) in a row went " +
                        $"unanswered. Stopping with {resolution.Answered.Count} name(s) answered.");
                    break;
                }

                var batch = names.Skip(i).Take(TitleBatchSize).ToList();
                string joined = string.Join("|", batch);
                string label = $"title batch {i / TitleBatchSize + 1}";

                Console.WriteLine(
                    $"  Resolving titles, batch {i / TitleBatchSize + 1} ({batch.Count} name(s))...");

                string response;
                try
                {
                    response = await FetchWithRetryAsync(BuildTitleUrl(joined), label, joined, ct);
                }
                catch (WikiApiErrorException ex)
                {
                    RecordUnresolved("title-batch", label, null, joined, ex);
                    if (i + TitleBatchSize < names.Count)
                    {
                        await Task.Delay(_effectiveDelay, ct);
                    }

                    continue;
                }
                catch (HttpRequestException ex)
                {
                    RecordUnresolved("title-batch", label, null, joined, ex);
                    Console.WriteLine(
                        $"  WARNING: Title resolution interrupted at batch " +
                        $"{i / TitleBatchSize + 1}: {ex.Message}");
                    break;
                }

                if (ReadTitleBatch(response, batch, resolution))
                {
                    RecordSectionAnswered();
                }
                else
                {
                    RecordUnresolved("title-batch", label, null, joined, UnreadableResponse());
                }

                if (i + TitleBatchSize < names.Count)
                {
                    await Task.Delay(_effectiveDelay, ct);
                }
            }

            return resolution;
        }

        /// <summary>
        /// Applies one action=query answer to <paramref name="resolution"/>.
        /// Returns false when the body is not one, so the caller records the
        /// batch unresolved rather than recording it answered.
        /// <para>
        /// "normalized" and "redirects" are separate arrays, and a title can
        /// appear in one, in the other, or in the first and then the second,
        /// so each name is followed hop by hop rather than looked up once.
        /// </para>
        /// </summary>
        private static bool ReadTitleBatch(
            string response, List<string> batch, TitleResolution resolution)
        {
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("query", out var query) ||
                query.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var hops = new Dictionary<string, string>(StringComparer.Ordinal);
            ReadTitleHops(query, "normalized", hops);
            ReadTitleHops(query, "redirects", hops);

            foreach (var name in batch)
            {
                resolution.Answered.Add(name);

                string current = name;
                var seen = new HashSet<string>(StringComparer.Ordinal) { current };
                while (hops.TryGetValue(current, out var next) && seen.Add(next))
                {
                    current = next;
                }

                if (!string.Equals(current, name, StringComparison.Ordinal))
                {
                    resolution.Resolved[name] = current;
                }
            }

            return true;
        }

        private static void ReadTitleHops(
            JsonElement query, string arrayName, Dictionary<string, string> hops)
        {
            if (!query.TryGetProperty(arrayName, out var entries) ||
                entries.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("from", out var from) ||
                    from.ValueKind != JsonValueKind.String ||
                    !entry.TryGetProperty("to", out var to) ||
                    to.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                string source = from.GetString()!;
                if (!hops.ContainsKey(source))
                {
                    hops[source] = to.GetString()!;
                }
            }
        }

        /// <summary>
        /// Fetches a single wiki page's raw wikitext via
        /// action=parse&amp;prop=wikitext - used by the festival-vendor
        /// auto-tagging pass (Program.ResolveSeasonalFestivalValuesAsync) to
        /// read a vendor NPC page's own {{Temporary|...}} template, which
        /// (unlike "Has requirement"/"Has seasonal purchase cap") has no
        /// equivalent Semantic MediaWiki property to query via action=ask.
        /// The &amp;redirects=1 is load-bearing: action=parse does not follow
        /// redirects by default, unlike action=ask's SMW queries.
        ///
        /// Returns null if the page does not exist or the response otherwise
        /// has no wikitext - never a network error, since FetchWithRetryAsync
        /// has already retried those. A null is NOT interchangeable with an
        /// empty wikitext body at the call site: see docs/ARCHITECTURE.md
        /// section T.6.
        /// </summary>
        public async Task<string?> FetchWikitextAsync(string pageName, CancellationToken ct = default)
        {
            var url = $"{WikiApiUrl}?action=parse&page={Uri.EscapeDataString(pageName)}" +
                       $"&prop=wikitext&redirects=1&format=json&maxlag={MaxLagSeconds}";

            // retryApiErrors is off here because "missingtitle" is an ordinary
            // outcome for this call: a vendor page named by a subobject may
            // simply not exist, and retrying that five times would spend four
            // requests to learn what the first answer already said.
            string response = await FetchWithRetryAsync(
                url, $"wikitext {pageName}", pageName, ct, retryApiErrors: false);

            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            if (root.TryGetProperty("parse", out var parse) &&
                parse.TryGetProperty("wikitext", out var wikitext) &&
                wikitext.TryGetProperty("*", out var text))
            {
                return text.GetString();
            }

            return null;
        }
    }

    /// <summary>
    /// What a resolution pass learned, and what it never got to ask.
    /// <para>
    /// <see cref="Resolved"/> alone cannot tell a name the wiki answered
    /// nothing for from a name in a batch that was refused, failed, or was
    /// never sent: both are simply absent from it. A caller that caches an
    /// absence has to know the difference, so <see cref="Answered"/> names
    /// every item in a batch the wiki did answer.
    /// </para>
    /// </summary>
    public class ItemIdResolution
    {
        public Dictionary<string, int> Resolved { get; } =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Answered { get; } =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Which page each display string names, and which strings were never
    /// asked about.
    /// <para>
    /// <see cref="Resolved"/> holds only the names whose page title differs
    /// from the string itself, so a name absent from it either already names
    /// its own page or was never answered for. Only <see cref="Answered"/>
    /// separates those two.
    /// </para>
    /// <para>
    /// Both are keyed ordinally, because a hop is applied only to a string
    /// the wiki echoed back byte for byte.
    /// </para>
    /// </summary>
    public class TitleResolution
    {
        /// <summary>Display string to the page title it names.</summary>
        public Dictionary<string, string> Resolved { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Every name in a batch the wiki answered.</summary>
        public HashSet<string> Answered { get; } =
            new HashSet<string>(StringComparer.Ordinal);
    }

    public class WikiCostEntry
    {
        public int Value { get; set; }

        // Set only when "Has item cost"'s nested "Has item currency"
        // record is present - absent for some rows (see ConvertToOffer's
        // no-currency-specified/coins fallback).
        public string? Currency { get; set; }
    }

    public class WikiVendorResult
    {
        // Always set at construction (ParseResult's one object
        // initializer), but nullable rather than defaulted to "": this
        // type round-trips through the wiki_vendor_cache.json cache
        // (JsonSerializer.Serialize/Deserialize with default options, which
        // write/read an explicit null verbatim), and MergeWikiCache's own
        // "?? string.Empty" coalescing below already treats a deserialized
        // PageName as possibly null.
        public string? PageName { get; set; }

        public int GameId { get; set; }

        // The following are all set conditionally, after construction,
        // only when their SMW printout is present on the page - each stays
        // null when the wiki row has no data for that property.
        public string? ItemName { get; set; }

        public int? OutputQuantity { get; set; }

        public List<WikiCostEntry> CostEntries { get; set; } = new List<WikiCostEntry>();

        public string? MerchantName { get; set; }

        public List<string> Locations { get; set; } = new List<string>();

        public int? DailyCap { get; set; }

        public int? WeeklyCap { get; set; }

        // Astral Acclaim package: Wizard's Vault seasonal purchase cap, or
        // null if the row has none (or isn't a Wizard's Vault offer). See
        // the PrintoutSuffix doc comment above.
        public int? SeasonalCap { get; set; }

        // Raw "Has requirement" text, or null if
        // the row has none. See HomesteadTierResolver.
        public string? Requirement { get; set; }

        // Festival-vendor auto-tagging follow-up: the raw
        // "seasonal="/"event=" value pulled from this vendor page's own
        // {{Temporary|...}} wikitext template by
        // TemporaryTemplateParser.ExtractSeasonalOrEventParameter, or null
        // if the page has no such template/parameter. This is NOT sourced
        // from the SMW "ask" printouts (there is no semantic property for
        // it) - it comes from a separate, opt-in wikitext-fetch pass (see
        // Program.ResolveSeasonalFestivalValuesAsync), populated onto each
        // result sharing that page's PageName before ConvertToOffer runs.
        // ConvertToOffer resolves this raw wiki string to the internal
        // festival key (Gw2Constants.ResolveSeasonalFestivalKey) - kept
        // raw here, not pre-resolved, so a value this pass could not map
        // yet still round-trips through wiki_vendor_cache.json for a
        // later run without needing to re-fetch the page.
        public string? TemporarySeasonalValue { get; set; }
    }
}
