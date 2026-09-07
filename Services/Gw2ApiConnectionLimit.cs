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
    /// connection groups that already exist. A ServicePoint is scavenged
    /// once idle and rebuilt from the process default, so
    /// <see cref="Apply"/> runs at the start of every fetch.
    /// </para>
    /// </summary>
    internal static class Gw2ApiConnectionLimit
    {
        /// <summary>
        /// Covers the widest moment of a snapshot fetch: six characters in
        /// flight at three requests each. Blish HUD's own token bucket
        /// (300 burst, 5 per second) still governs the request rate.
        /// </summary>
        public const int ConnectionsPerServer = 20;

        private static readonly Uri ApiRoot = new Uri("https://api.guildwars2.com");

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
