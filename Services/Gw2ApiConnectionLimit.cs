using System;
using System.Net;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Raises how many sockets .NET will open to api.guildwars2.com.
    /// Without this, running requests concurrently buys almost nothing:
    /// the extra ones queue for a free connection.
    /// <para>
    /// Measured on .NET Framework 4.8: HttpClientHandler seeds
    /// MaxConnectionsPerServer from ServicePointManager.DefaultConnectionLimit,
    /// which is 2 outside ASP.NET. Neither Blish HUD nor Gw2Sharp 1.7.4
    /// changes it, so the module gets 2. The documented int.MaxValue
    /// default is .NET Core behaviour, not this one.
    /// </para>
    /// <para>
    /// Setting one host's ServicePoint leaves the host application's other
    /// traffic alone, and unlike the process-wide default it reaches
    /// connection groups that already exist. Every entry point that starts
    /// a burst calls <see cref="Apply"/> for the reason given there.
    /// </para>
    /// </summary>
    internal static class Gw2ApiConnectionLimit
    {
        /// <summary>
        /// The module's own widest moment, plus room for the host
        /// application. 18 is a snapshot's character phase:
        /// CharacterSnapshotCollector.DefaultMaxCharactersInFlight, 6,
        /// times the three requests FetchCharacterAsync starts per
        /// character. 4 is RecipeService.DefaultMaxConcurrency, plan
        /// generation's widest phase, which can overlap the snapshot
        /// because the snapshot refreshes on a timer. 1 is the recipe
        /// corpus sweep, which sends one batch at a time. The last 2 is
        /// what Blish HUD's own Gw2Sharp traffic to this host had before
        /// the module raised anything.
        /// <para>
        /// A ceiling, not a fan-out: nothing here changes how many
        /// requests those bounds start, only how many of them get a socket
        /// instead of queueing. Blish HUD's token bucket (300 burst, 5 per
        /// second) still governs the Gw2Sharp half.
        /// </para>
        /// </summary>
        public const int ConnectionsPerServer = 25;

        private static readonly Uri ApiRoot = new Uri("https://api.guildwars2.com");

        /// <summary>
        /// Called at the start of every operation that starts several
        /// requests at once, not once at module load: a ServicePoint idle
        /// for ServicePointManager.MaxServicePointIdleTime, measured at
        /// 100,000ms, is replaced by a fresh one at the process default.
        /// The default snapshot refresh interval is 10 minutes, so a load
        /// time raise would be gone for most of a session.
        /// <para>
        /// Measured at 47.6ns per call over 1,000,000 calls with the
        /// ServicePoint already raised, which is why per-operation is
        /// affordable.
        /// </para>
        /// </summary>
        public static void Apply()
        {
            var servicePoint = ServicePointManager.FindServicePoint(ApiRoot);
            if (servicePoint.ConnectionLimit < ConnectionsPerServer)
            {
                servicePoint.ConnectionLimit = ConnectionsPerServer;
            }
        }
    }
}
