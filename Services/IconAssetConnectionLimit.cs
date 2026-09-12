using System;
using System.Net;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Raises how many sockets .NET will open to assets.gw2dat.com, the
    /// host Blish HUD fetches item and currency icon art from.
    /// <para>
    /// Blish HUD 1.3.0 does not use the GW2 render service for icons.
    /// ContentService.GetRenderServiceTexture reads the signature and file
    /// id out of the render URL, drops the signature, and calls
    /// DatAssetCache.GetTextureFromAssetId, which requests
    /// https://assets.gw2dat.com/{fileId}.png through Flurl.Http 2.4.2.
    /// Flurl's default handler leaves MaxConnectionsPerServer at the
    /// ServicePoint's value, and Blish bounds neither how fast nor how
    /// many of those it starts, so this ServicePoint is the only thing
    /// holding back a cold-cache icon flood.
    /// </para>
    /// </summary>
    internal static class IconAssetConnectionLimit
    {
        /// <summary>
        /// What HTTP/1.1 browsers settled on for many small static images
        /// from one host: Firefox's
        /// network.http.max-persistent-connections-per-server defaults to
        /// 6 (Mozilla bug 423377). assets.gw2dat.com is a community-run
        /// mirror rather than an ArenaNet service and publishes no limit
        /// of its own, so the browser number is the anchor.
        /// <para>
        /// Measured on 2026-09-07: 40 icons the CDN had not served
        /// recently, fetched over one reused connection, averaged 0.446s
        /// each. The owner's 927 distinct snapshot icons therefore take
        /// about 207s at the default 2 and about 69s at 6.
        /// </para>
        /// </summary>
        public const int ConnectionsPerServer = 6;

        private static readonly Uri AssetRoot = new Uri("https://assets.gw2dat.com");

        /// <summary>
        /// Called wherever the module hands Blish an icon URL, for the
        /// same idle-scavenging reason as
        /// <see cref="Gw2ApiConnectionLimit.Apply"/> and at the same
        /// measured cost.
        /// </summary>
        public static void Apply()
        {
            var servicePoint = ServicePointManager.FindServicePoint(AssetRoot);
            if (servicePoint.ConnectionLimit < ConnectionsPerServer)
            {
                servicePoint.ConnectionLimit = ConnectionsPerServer;
            }
        }
    }
}
