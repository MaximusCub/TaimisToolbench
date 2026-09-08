using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Harness
{
    internal enum FetchApproach
    {
        /// <summary>
        /// The narrow per-character endpoints the module uses today:
        /// inventory, equipment and crafting, three requests per character.
        /// </summary>
        Narrow,

        /// <summary>/v2/characters?ids=all, one request for every character.</summary>
        FullAll,

        /// <summary>/v2/characters?page=N&amp;page_size=N.</summary>
        FullPaged,
    }

    internal class FetchConfig
    {
        public string Name { get; set; }

        public FetchApproach Approach { get; set; }

        /// <summary>How many characters run at once. 0 means all of them.</summary>
        public int CharacterParallelism { get; set; }

        /// <summary>
        /// Sockets allowed to api.guildwars2.com for this run. The shipped
        /// value is Services/Gw2ApiConnectionLimit.cs's ConnectionsPerServer.
        /// </summary>
        public int ConnectionLimit { get; set; }

        public int PageSize { get; set; }

        /// <summary>
        /// Pads the real roster out to this many entries to stand in for a
        /// larger account. 0 is the real roster. A padded entry is a name
        /// already in the list, so a padded run is a simulation of a bigger
        /// account, not a measurement of one.
        /// </summary>
        public int RosterTarget { get; set; }

        /// <summary>
        /// Waits for every account-wide response before the character phase
        /// starts, which is what Services/Gw2AccountSnapshotService.cs does.
        /// False starts the character phase as soon as the roster arrives.
        /// </summary>
        public bool SettleAccountWideFirst { get; set; }

        public int Runs { get; set; }

        /// <summary>
        /// What the three resolve passes cost, for scheduling only. The
        /// ledger records what a run actually sent, so an estimate that is
        /// off paces the experiment slightly and cannot overspend it.
        /// </summary>
        private const int DetailRequests = 6;

        public int RosterSize(int characterCount)
        {
            return RosterTarget > 0 ? RosterTarget : characterCount;
        }

        public int ExpectedRequests(int characterCount, bool includeArmory)
        {
            int accountWide = includeArmory ? 5 : 4;
            if (Approach == FetchApproach.FullAll)
            {
                return accountWide + 1 + DetailRequests;
            }

            if (Approach == FetchApproach.FullPaged)
            {
                int pages = (characterCount + PageSize - 1) / PageSize;
                return accountWide + Math.Max(1, pages) + DetailRequests;
            }

            return accountWide + 1 + (RosterSize(characterCount) * 3) + DetailRequests;
        }
    }

    internal class FetchRunResult
    {
        public string ConfigName { get; set; }

        public int Sweep { get; set; }

        public DateTime StartedUtc { get; set; }

        public double WallMs { get; set; }

        public int Requests { get; set; }

        public long BodyBytes { get; set; }

        public long WireBytes { get; set; }

        public double SlowestRequestMs { get; set; }

        public string SlowestRequestUrl { get; set; }

        public int CharacterCount { get; set; }

        public int ItemRows { get; set; }

        public int DisciplineRows { get; set; }

        public int WalletRows { get; set; }

        /// <summary>
        /// Item rows the resolve pass gave a display name. A snapshot whose
        /// rows are unnamed is not one the module could show.
        /// </summary>
        public int NamedItemRows { get; set; }

        public int IncompleteCharacters { get; set; }

        public string Failure { get; set; }
    }

    /// <summary>
    /// Keeps the whole experiment under a request-per-minute ceiling by
    /// waiting between runs, never inside one.
    /// </summary>
    /// <remarks>
    /// Throttling inside a run would be measured as that approach being
    /// slow, so the ledger is only ever consulted before a run starts. It
    /// admits a run only when the requests already sent in the trailing 60
    /// seconds, plus the run's expected count, stay under the ceiling.
    /// </remarks>
    internal class RequestRateLedger
    {
        private readonly List<DateTime> _sent = new List<DateTime>();
        private readonly int _perMinute;
        private readonly object _lock = new object();

        public RequestRateLedger(int perMinute)
        {
            _perMinute = perMinute;
        }

        public int Total { get; private set; }

        public void Record(int count)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                for (int i = 0; i < count; i++)
                {
                    _sent.Add(now);
                }

                Total += count;
            }
        }

        public int InLastMinute()
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow.AddSeconds(-60);
                _sent.RemoveAll(t => t < cutoff);
                return _sent.Count;
            }
        }

        /// <summary>
        /// Blocks until the run can start. A run that is on its own larger
        /// than the ceiling waits for an empty trailing minute instead of
        /// waiting forever for room it can never have.
        /// </summary>
        public async Task WaitForRoomAsync(int upcoming, CancellationToken ct)
        {
            int room = Math.Min(upcoming, _perMinute);
            while (InLastMinute() + room > _perMinute)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    internal static class FetchProfileReport
    {
        /// <summary>
        /// Renders one row per config: run count, the median and the full
        /// range of each measure. A single number would hide the variance
        /// that a network measurement always has.
        /// </summary>
        public static string RenderTable(
            IReadOnlyList<FetchConfig> configs, IReadOnlyList<FetchRunResult> results)
        {
            var builder = new StringBuilder();
            builder.AppendLine(
                "config                     runs  requests   wall ms (med / min-max)      "
                + "wire KB   body KB   slowest ms");
            builder.AppendLine(new string('-', 108));

            foreach (var config in configs)
            {
                var rows = results
                    .Where(r => r.ConfigName == config.Name && r.Failure == null)
                    .ToList();
                var failures = results.Count(r => r.ConfigName == config.Name && r.Failure != null);
                if (rows.Count == 0)
                {
                    builder.AppendLine(
                        Pad(config.Name, 26) + "  "
                        + (failures == 0 ? "not run" : failures + " runs failed"));
                    continue;
                }

                var wall = rows.Select(r => r.WallMs).ToList();
                var slowest = rows.Select(r => r.SlowestRequestMs).ToList();
                builder.AppendLine(
                    Pad(config.Name, 26) + "  "
                    + Pad(rows.Count.ToString(CultureInfo.InvariantCulture), 4) + "  "
                    + Pad(DescribeRequests(rows), 8) + "  "
                    + Pad(
                        Fixed(Median(wall)) + " / " + Fixed(wall.Min()) + "-" + Fixed(wall.Max()),
                        27) + "  "
                    + Pad(Fixed(Median(rows.Select(r => (double)r.WireBytes).ToList()) / 1024.0), 8) + "  "
                    + Pad(Fixed(Median(rows.Select(r => (double)r.BodyBytes).ToList()) / 1024.0), 8) + "  "
                    + Fixed(Median(slowest))
                    + (failures > 0 ? "  (" + failures + " failed)" : string.Empty));
            }

            return builder.ToString();
        }

        /// <summary>
        /// The request count, or its range when the runs disagreed. They
        /// disagree when a run failed part way, and printing one run's count
        /// as the config's would hide that.
        /// </summary>
        private static string DescribeRequests(List<FetchRunResult> rows)
        {
            int low = rows.Min(r => r.Requests);
            int high = rows.Max(r => r.Requests);
            return low == high
                ? low.ToString(CultureInfo.InvariantCulture)
                : low.ToString(CultureInfo.InvariantCulture) + "-"
                    + high.ToString(CultureInfo.InvariantCulture);
        }

        public static double Median(List<double> values)
        {
            if (values.Count == 0)
            {
                return 0;
            }

            var sorted = values.OrderBy(v => v).ToList();
            int mid = sorted.Count / 2;
            if (sorted.Count % 2 == 1)
            {
                return sorted[mid];
            }

            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        private static string Fixed(double value)
        {
            return value.ToString("F1", CultureInfo.InvariantCulture);
        }

        private static string Pad(string value, int width)
        {
            if (value.Length >= width)
            {
                return value;
            }

            return value + new string(' ', width - value.Length);
        }
    }
}
