using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Gw2Sharp;
using Gw2Sharp.WebApi;
using Gw2Sharp.WebApi.Caching;
using Gw2Sharp.WebApi.V2;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Gw2SharpHttpClient = Gw2Sharp.WebApi.Http.HttpClient;

namespace TaimisToolbench.Harness
{
    /// <summary>
    /// Measures how long an account snapshot takes to fetch, for several
    /// ways of fetching one, and what each way costs in requests and bytes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The API key is read from the GW2_API_KEY environment variable and is
    /// never written anywhere: not to the console, not to the result file,
    /// not into a recorded URL. Gw2Sharp sends it in a header.
    /// </para>
    /// <para>
    /// This talks to Gw2Sharp directly rather than through Blish HUD's
    /// Gw2ApiManager, which needs a loaded module. What that leaves out is
    /// recorded in tools/TaimisToolbench.Harness/README.md.
    /// </para>
    /// </remarks>
    internal static class FetchProfiler
    {
        private static readonly Uri ApiRoot = new Uri("https://api.guildwars2.com");

        private const string KeyVariable = "GW2_API_KEY";

        // Matches Gw2AccountSnapshotService.PerCallTimeout, so a stuck
        // request costs a run the same 20 seconds it would cost the module.
        private static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(20);

        public static async Task<int> RunAsync(string[] args)
        {
            bool dryRun = false;
            int perMinute = 55;
            int maxRequests = 750;
            int assumedCharacters = 6;
            string outputDirectory = null;
            string only = null;
            int runsOverride = 0;
            bool compare = false;
            int compareRuns = 3;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--per-minute":
                        if (i + 1 < args.Length)
                        {
                            perMinute = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        }

                        break;
                    case "--max-requests":
                        if (i + 1 < args.Length)
                        {
                            maxRequests = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        }

                        break;
                    case "--characters":
                        if (i + 1 < args.Length)
                        {
                            assumedCharacters = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        }

                        break;
                    case "--out":
                        if (i + 1 < args.Length)
                        {
                            outputDirectory = args[++i];
                        }

                        break;
                    case "--only":
                        if (i + 1 < args.Length)
                        {
                            only = args[++i];
                        }

                        break;
                    case "--compare":
                        compare = true;
                        break;
                    case "--compare-runs":
                        if (i + 1 < args.Length)
                        {
                            compareRuns = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        }

                        break;
                    case "--runs":
                        if (i + 1 < args.Length)
                        {
                            runsOverride = int.Parse(args[++i], CultureInfo.InvariantCulture);
                        }

                        break;
                }
            }

            var configs = BuildConfigs();
            if (only != null)
            {
                var wanted = new HashSet<string>(
                    only.Split(','), StringComparer.OrdinalIgnoreCase);
                configs = configs.Where(c => wanted.Contains(c.Name)).ToList();
            }

            if (runsOverride > 0)
            {
                foreach (var config in configs)
                {
                    config.Runs = runsOverride;
                }
            }

            if (configs.Count == 0 && !compare)
            {
                Console.Error.WriteLine("--only matched no config.");
                return 1;
            }

            if (dryRun)
            {
                PrintPlan(configs, assumedCharacters, perMinute, maxRequests);
                return 0;
            }

            if (compare)
            {
                configs = new List<FetchConfig>();
            }

            string apiKey = Environment.GetEnvironmentVariable(KeyVariable);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Error.WriteLine(
                    "No API key. Set " + KeyVariable + " for this command only, then rerun:");
                Console.Error.WriteLine(
                    "  " + KeyVariable + "=<key> dotnet run --project "
                    + "tools/TaimisToolbench.Harness/TaimisToolbench.Harness.csproj -- --fetch-profile");
                Console.Error.WriteLine(
                    "Add --dry-run to print the schedule without a key.");
                return 2;
            }

            if (compare)
            {
                return await CompareAsync(apiKey, compareRuns, perMinute, maxRequests);
            }

            return await MeasureAsync(apiKey, configs, perMinute, maxRequests, outputDirectory);
        }

        private static List<FetchConfig> BuildConfigs()
        {
            return new List<FetchConfig>
            {
                Narrow("narrow-p6-c25", 6, 25, 3),
                new FetchConfig
                {
                    Name = "narrow-p6-c25-nobar",
                    Approach = FetchApproach.Narrow,
                    CharacterParallelism = 6,
                    ConnectionLimit = 25,
                    SettleAccountWideFirst = false,
                    Runs = 3,
                },
                Narrow("narrow-p2-c25", 2, 25, 3),
                Narrow("narrow-p3-c25", 3, 25, 3),
                Narrow("narrow-p6-c2", 6, 2, 2),
                Narrow("narrow-p6-c8", 6, 8, 2),
                Full("full-all-c25", FetchApproach.FullAll, 0, 4),
                Full("full-page3-c25", FetchApproach.FullPaged, 3, 3),
                Full("full-page2-c25", FetchApproach.FullPaged, 2, 3),
                new FetchConfig
                {
                    Name = "narrow-n14-p6-c25",
                    Approach = FetchApproach.Narrow,
                    CharacterParallelism = 6,
                    ConnectionLimit = 25,
                    RosterTarget = 14,
                    SettleAccountWideFirst = true,
                    Runs = 2,
                },
                new FetchConfig
                {
                    Name = "narrow-n14-pall-c25",
                    Approach = FetchApproach.Narrow,
                    CharacterParallelism = 0,
                    ConnectionLimit = 25,
                    RosterTarget = 14,
                    SettleAccountWideFirst = true,
                    Runs = 1,
                },
            };
        }

        private static FetchConfig Narrow(string name, int parallelism, int connections, int runs)
        {
            return new FetchConfig
            {
                Name = name,
                Approach = FetchApproach.Narrow,
                CharacterParallelism = parallelism,
                ConnectionLimit = connections,
                SettleAccountWideFirst = true,
                Runs = runs,
            };
        }

        private static FetchConfig Full(string name, FetchApproach approach, int pageSize, int runs)
        {
            return new FetchConfig
            {
                Name = name,
                Approach = approach,
                ConnectionLimit = 25,
                PageSize = pageSize,
                SettleAccountWideFirst = true,
                Runs = runs,
            };
        }

        private static void PrintPlan(
            List<FetchConfig> configs, int characters, int perMinute, int maxRequests)
        {
            Console.WriteLine(
                "Fetch profile plan, assuming " + characters + " characters and the unlocks scope.");
            Console.WriteLine("Request ceiling " + perMinute + " per minute, " + maxRequests + " total.");
            Console.WriteLine();
            int total = 0;
            foreach (var config in configs)
            {
                int perRun = config.ExpectedRequests(characters, true);
                total += perRun * config.Runs;
                Console.WriteLine(
                    config.Name + ": " + config.Runs + " runs of " + perRun + " requests");
            }

            Console.WriteLine();
            Console.WriteLine("Scheduled requests: " + total);
        }

        private static async Task<int> MeasureAsync(
            string apiKey,
            List<FetchConfig> configs,
            int perMinute,
            int maxRequests,
            string outputDirectory)
        {
            var ledger = new RequestRateLedger(perMinute);
            var results = new List<FetchRunResult>();
            using (var cts = new CancellationTokenSource())
            {
                bool includeArmory;
                int characterCount;
                var probe = await ProbeAsync(apiKey, ledger, cts.Token);
                includeArmory = probe.Item1;
                characterCount = probe.Item2;
                Console.WriteLine(
                    "Roster: " + characterCount + " characters. Legendary Armory readable: "
                    + includeArmory + ".");
                Console.WriteLine();

                string path = OpenResultFile(outputDirectory);
                Console.WriteLine("Raw timings: " + path);
                Console.WriteLine();

                int maxSweeps = configs.Max(c => c.Runs);
                for (int sweep = 0; sweep < maxSweeps; sweep++)
                {
                    // Rotated so a given config is not always measured at the
                    // same point in a sweep. Server load drifts over minutes
                    // and a fixed order would charge that drift to whichever
                    // config always ran last.
                    var order = configs.Where(c => c.Runs > sweep).ToList();
                    for (int i = 0; i < order.Count; i++)
                    {
                        var config = order[(i + sweep) % order.Count];
                        int expected = config.ExpectedRequests(characterCount, includeArmory);
                        if (ledger.Total + expected > maxRequests)
                        {
                            Console.WriteLine(
                                "Stopping: " + ledger.Total + " requests sent, " + maxRequests
                                + " is the cap.");
                            sweep = maxSweeps;
                            break;
                        }

                        await ledger.WaitForRoomAsync(expected, cts.Token);
                        var result = await ExecuteAsync(
                            apiKey, config, sweep, includeArmory, characterCount, cts.Token);
                        ledger.Record(result.Requests);
                        results.Add(result);
                        Append(path, result);
                        Console.WriteLine(Describe(result));
                    }
                }

                Console.WriteLine();
                Console.WriteLine(FetchProfileReport.RenderTable(configs, results));
                Console.WriteLine("Total requests sent: " + ledger.Total);
            }

            return 0;
        }

        /// <summary>
        /// One roster call and one token call, before any measurement, so the
        /// schedule knows how big each run will be.
        /// </summary>
        private static async Task<Tuple<bool, int>> ProbeAsync(
            string apiKey, RequestRateLedger ledger, CancellationToken ct)
        {
            using (var handler = new ByteCountingHandler(new HttpClientHandler()))
            {
                var counter = NewCountingClient(handler);
                using (var client = NewClient(apiKey, counter, null))
                {
                    var info = await client.WebApi.V2.TokenInfo.GetAsync(ct);
                    bool unlocks = info.Permissions.Any(
                        p => string.Equals(p.RawValue, "unlocks", StringComparison.OrdinalIgnoreCase));
                    var names = await client.WebApi.V2.Characters.IdsAsync(ct);
                    ledger.Record(counter.Requests.Count);
                    return Tuple.Create(unlocks, names.Count);
                }
            }
        }

        /// <summary>
        /// Builds the counting client over a handler the caller owns.
        /// </summary>
        /// <remarks>
        /// Measured on Gw2Sharp 1.7.4: its own HttpClient disposes the
        /// System.Net.Http.HttpClient it was handed once the request is done,
        /// so the second request through a shared one throws
        /// ObjectDisposedException. It is handed a new one per request
        /// instead. The handler is what holds the connection pool, so it
        /// outlives them and a run's connection limit still means something.
        /// </remarks>
        private static CountingGw2HttpClient NewCountingClient(HttpMessageHandler handler)
        {
            return new CountingGw2HttpClient(new Gw2SharpHttpClient(() =>
            {
                var client = new System.Net.Http.HttpClient(handler, false);
                client.Timeout = PerCallTimeout;
                return client;
            }));
        }

        /// <summary>
        /// A client built the way Blish HUD builds the module's, except for
        /// the token bucket (see the README).
        /// </summary>
        /// <remarks>
        /// Blish's connection uses MemoryCacheMethod, so a real-roster run
        /// uses one too; each run gets its own, so every run is a cold fetch
        /// and nothing is served from a previous run's cache. A padded roster
        /// repeats a character name, which a URL-keyed cache would collapse
        /// into one request and turn the simulation into a measurement of
        /// caching, so those runs get NullCacheMethod instead.
        /// </remarks>
        private static Gw2Client NewClient(
            string apiKey, CountingGw2HttpClient http, FetchConfig config)
        {
            bool padded = config != null && config.RosterTarget > 0;
            var connection = new Connection(
                apiKey,
                Locale.English,
                padded ? (ICacheMethod)new NullCacheMethod() : new MemoryCacheMethod(),
                new NullCacheMethod(),
                null,
                Gw2ApiUserAgent.Build("TaimisToolbench-Harness", "0.0.0"),
                http);
            return new Gw2Client(connection);
        }

        private static async Task<FetchRunResult> ExecuteAsync(
            string apiKey,
            FetchConfig config,
            int sweep,
            bool includeArmory,
            int characterCount,
            CancellationToken ct)
        {
            var result = new FetchRunResult
            {
                ConfigName = config.Name,
                Sweep = sweep,
                StartedUtc = DateTime.UtcNow,
                CharacterCount = config.RosterSize(characterCount),
            };

            // A fresh handler per run, so no run inherits an open connection
            // from the run before it. The ServicePoint is set as well as the
            // handler because on .NET Framework the handler seeds its limit
            // from ServicePointManager (Services/Gw2ApiConnectionLimit.cs).
            ServicePointManager.FindServicePoint(ApiRoot).ConnectionLimit = config.ConnectionLimit;
            var transport = new HttpClientHandler();
            transport.MaxConnectionsPerServer = config.ConnectionLimit;
            using (var handler = new ByteCountingHandler(transport))
            {
                var counter = NewCountingClient(handler);
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    using (var client = NewClient(apiKey, counter, config))
                    {
                        var snapshot = await SnapshotFetchShapes.BuildAsync(
                            client, config, includeArmory, characterCount, ct);
                        result.ItemRows = snapshot.Items.Count;
                        result.DisciplineRows = snapshot.CharacterDisciplines == null
                            ? 0
                            : snapshot.CharacterDisciplines.Count;
                        result.WalletRows = snapshot.Wallet.Count;
                        result.NamedItemRows = snapshot.Items.Count(i => i.Name.Length > 0);
                        result.IncompleteCharacters = snapshot.IncompleteCharacterCount;
                    }
                }
                catch (Exception ex)
                {
                    // Scrubbed rather than trusted: this string is printed and
                    // written to the result file, and nothing guarantees a
                    // client library keeps the token out of its messages.
                    result.Failure = Scrub(ex.GetType().Name + ": " + ex.Message, apiKey);
                }

                stopwatch.Stop();
                result.WallMs = stopwatch.Elapsed.TotalMilliseconds;
                result.WireBytes = handler.CompressedBytes;
                Summarize(result, counter.Requests);
            }

            return result;
        }

        private static string Scrub(string text, string apiKey)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(apiKey))
            {
                return text;
            }

            return text.Replace(apiKey, "<redacted>");
        }

        private static void Summarize(FetchRunResult result, IReadOnlyList<ProfiledRequest> requests)
        {
            result.Requests = requests.Count;
            result.BodyBytes = requests.Sum(r => r.BodyBytes);
            var slowest = requests.OrderByDescending(r => r.DurationMs).FirstOrDefault();
            if (slowest != null)
            {
                result.SlowestRequestMs = slowest.DurationMs;
                result.SlowestRequestUrl = slowest.Url;
            }
        }

        /// <summary>
        /// Fetches the same account both ways, in one process, and reports
        /// every field the two snapshots disagree on.
        /// </summary>
        /// <remarks>
        /// The paged side runs Services/CharacterRecordProjection.cs and
        /// Services/CharacterPagePlan.cs, the code the module ships, so a
        /// clean result is evidence about the module and not about a copy of
        /// it. The narrow side keeps this project's own projection, because
        /// it is the shape the module no longer has.
        /// </remarks>
        private static async Task<int> CompareAsync(
            string apiKey, int runs, int perMinute, int maxRequests)
        {
            var ledger = new RequestRateLedger(perMinute);
            int mismatches = 0;
            using (var cts = new CancellationTokenSource())
            {
                var probe = await ProbeAsync(apiKey, ledger, cts.Token);
                bool includeArmory = probe.Item1;
                int characterCount = probe.Item2;
                Console.WriteLine(
                    "Roster: " + characterCount + " characters. Page size "
                    + CharacterPagePlan.PageSize + ", "
                    + CharacterPagePlan.PageCount(characterCount) + " pages.");
                Console.WriteLine();

                var oldShape = Narrow("old-narrow", 6, 25, 1);
                var newShape = Full("new-paged", FetchApproach.FullPaged, CharacterPagePlan.PageSize, 1);

                for (int run = 0; run < runs; run++)
                {
                    int expected = oldShape.ExpectedRequests(characterCount, includeArmory)
                        + newShape.ExpectedRequests(characterCount, includeArmory);
                    if (ledger.Total + expected > maxRequests)
                    {
                        Console.WriteLine(
                            "Stopping: " + ledger.Total + " requests sent, " + maxRequests
                            + " is the cap.");
                        break;
                    }

                    await ledger.WaitForRoomAsync(
                        oldShape.ExpectedRequests(characterCount, includeArmory), cts.Token);
                    var before = await FetchOnceAsync(
                        apiKey, oldShape, includeArmory, characterCount, ledger, cts.Token);

                    await ledger.WaitForRoomAsync(
                        newShape.ExpectedRequests(characterCount, includeArmory), cts.Token);
                    var after = await FetchOnceAsync(
                        apiKey, newShape, includeArmory, characterCount, ledger, cts.Token);

                    var differences = SnapshotComparison.Differences(before, after);
                    Console.WriteLine(
                        "run " + run + ": narrow " + Describe(before) + "; paged "
                        + Describe(after));
                    if (differences.Count == 0)
                    {
                        Console.WriteLine("  identical");
                        continue;
                    }

                    mismatches++;
                    foreach (string difference in differences)
                    {
                        Console.WriteLine("  " + difference);
                    }
                }

                Console.WriteLine();
                Console.WriteLine("Total requests sent: " + ledger.Total);
                Console.WriteLine(
                    mismatches == 0
                        ? "Every comparison found an identical snapshot."
                        : mismatches + " comparisons found a difference.");
            }

            return mismatches == 0 ? 0 : 1;
        }

        private static async Task<AccountSnapshot> FetchOnceAsync(
            string apiKey,
            FetchConfig config,
            bool includeArmory,
            int characterCount,
            RequestRateLedger ledger,
            CancellationToken ct)
        {
            Gw2ApiConnectionLimit.Apply();
            var transport = new HttpClientHandler();
            transport.MaxConnectionsPerServer = config.ConnectionLimit;
            using (var handler = new ByteCountingHandler(transport))
            {
                var counter = NewCountingClient(handler);
                try
                {
                    using (var client = NewClient(apiKey, counter, config))
                    {
                        return await SnapshotFetchShapes.BuildAsync(
                            client, config, includeArmory, characterCount, ct);
                    }
                }
                finally
                {
                    ledger.Record(counter.Requests.Count);
                }
            }
        }

        private static string Describe(AccountSnapshot snapshot)
        {
            return snapshot.Items.Count + " items, "
                + snapshot.Wallet.Count + " wallet rows, "
                + (snapshot.CharacterDisciplines == null
                    ? "no disciplines"
                    : snapshot.CharacterDisciplines.Count + " disciplines")
                + ", " + snapshot.IncompleteCharacterCount + " incomplete";
        }

        private static string OpenResultFile(string outputDirectory)
        {
            string directory = outputDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "TaimisToolbench",
                    "fetch-profile");
            }

            Directory.CreateDirectory(directory);
            string name = "fetch-profile-"
                + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)
                + ".jsonl";
            return Path.Combine(directory, name);
        }

        private static void Append(string path, FetchRunResult result)
        {
            File.AppendAllText(path, JsonSerializer.Serialize(result) + Environment.NewLine);
        }

        private static string Describe(FetchRunResult result)
        {
            if (result.Failure != null)
            {
                return result.ConfigName + " sweep " + result.Sweep + ": FAILED " + result.Failure;
            }

            return result.ConfigName + " sweep " + result.Sweep + ": "
                + result.WallMs.ToString("F0", CultureInfo.InvariantCulture) + " ms, "
                + result.Requests + " requests, "
                + (result.WireBytes / 1024.0).ToString("F1", CultureInfo.InvariantCulture) + " KB wire, "
                + result.ItemRows + " item rows (" + result.NamedItemRows + " named), "
                + result.DisciplineRows + " discipline rows, slowest "
                + result.SlowestRequestMs.ToString("F0", CultureInfo.InvariantCulture) + " ms";
        }
    }
}
