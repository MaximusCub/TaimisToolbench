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
        /// Kept at 25 because it was measured there, not because a sum
        /// still lands on it. In one sweep on a 6-character account the
        /// same snapshot took 8180ms at 25 sockets and 15878ms at 8, and
        /// at 2 it took 26680ms and failed to read 3 of the 6 characters
        /// inside the per-call timeout (PR #319).
        /// <para>
        /// The module's widest moment is now well under the number: the
        /// character phase sends CharacterPagePlan.MaxPagesInFlight, 6,
        /// where it used to send 18. Headroom is the point, because plan
        /// generation and the host application share this host.
        /// </para>
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
