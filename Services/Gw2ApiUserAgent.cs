using System;
using System.Net.Http;
using System.Text;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Builds the User-Agent this project sends to api.guildwars2.com and
    /// applies it to an <see cref="HttpClient"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// HttpClient sends no User-Agent unless one is set, so a client that
    /// skips this identifies itself to ArenaNet as nothing at all and gives
    /// them no address to reach. What each host this project talks to asks
    /// of a client is recorded in docs/api-client-contracts.md.
    /// </para>
    /// <para>
    /// <see cref="ReadManifestVersion"/> takes the manifest as
    /// <see cref="object"/> and reflects over it so this file stays free of
    /// Blish HUD and of the SemVer package that types Manifest.Version.
    /// </para>
    /// </remarks>
    internal static class Gw2ApiUserAgent
    {
        internal const string ContactUrl =
            "https://github.com/MaximusCub/TaimisToolbench";

        internal const string ModuleProduct = "TaimisToolbench";

        internal const string UnknownVersion = "0.0.0";

        /// <summary>
        /// Composes "product/version (+contact)".
        /// </summary>
        /// <remarks>
        /// Both fields are reduced to RFC 9110 token characters first. A
        /// version carrying anything else - a space, a bracket, a quote -
        /// makes ParseAdd throw, and the only caller that could supply one
        /// reads it out of a manifest this code does not control.
        /// </remarks>
        public static string Build(string product, string version)
        {
            string safeProduct = ToToken(product);
            if (safeProduct.Length == 0)
            {
                safeProduct = ModuleProduct;
            }

            string safeVersion = ToToken(version);
            if (safeVersion.Length == 0)
            {
                safeVersion = UnknownVersion;
            }

            return safeProduct + "/" + safeVersion + " (+" + ContactUrl + ")";
        }

        /// <summary>
        /// Reads a module manifest's Version property as text, or null when
        /// the object has no readable one.
        /// </summary>
        public static string ReadManifestVersion(object manifest)
        {
            if (manifest == null)
            {
                return null;
            }

            try
            {
                var property = manifest.GetType().GetProperty("Version");
                string text = property?.GetValue(manifest)?.ToString();
                return string.IsNullOrWhiteSpace(text) ? null : text;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Replaces whatever User-Agent <paramref name="http"/> carries with
        /// this project's. Safe to call more than once on one client.
        /// </summary>
        public static void Apply(HttpClient http, string product, string version)
        {
            if (http == null)
            {
                return;
            }

            http.DefaultRequestHeaders.UserAgent.Clear();
            http.DefaultRequestHeaders.UserAgent.ParseAdd(Build(product, version));
        }

        private static string ToToken(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (IsTokenChar(c))
                {
                    builder.Append(c);
                }
            }

            return builder.ToString();
        }

        // RFC 9110 section 5.6.2's tchar set. It excludes "/", "(" and
        // ")", which is what makes reducing a field to it enough to keep
        // that field from reading as User-Agent structure.
        private static bool IsTokenChar(char c)
        {
            if (c >= 'a' && c <= 'z')
            {
                return true;
            }

            if (c >= 'A' && c <= 'Z')
            {
                return true;
            }

            if (c >= '0' && c <= '9')
            {
                return true;
            }

            return "!#$%&'*+-.^_`|~".IndexOf(c) >= 0;
        }
    }
}
