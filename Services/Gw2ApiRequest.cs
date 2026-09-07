using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// One GW2 API GET, sent a second time if the API refused the first
    /// and said when to come back.
    /// </summary>
    /// <remarks>
    /// api.guildwars2.com reports X-Rate-Limit-Limit: 600 a minute
    /// (docs/api-client-contracts.md section 2). The module's own clients
    /// turn any non-success status into an exception, and an exception on
    /// the recipe path degrades that recipe to UNKNOWN in the plan, so
    /// before this a rate-limit trip changed plan CONTENT rather than plan
    /// timing. Raising the connection limit lets the module's declared
    /// concurrency bounds actually run, which makes a trip more likely
    /// rather than less.
    /// </remarks>
    internal static class Gw2ApiRequest
    {
        /// <summary>
        /// Waited when a refusal names no Retry-After. One second is the
        /// gap RecipeCorpusRefresher already leaves between its batches.
        /// </summary>
        internal static readonly TimeSpan FallbackDelay = TimeSpan.FromSeconds(1);

        /// <summary>
        /// A refusal asking for longer than this is returned to the caller
        /// unretried, which degrades exactly as it did before. Plan
        /// generation tells the player it "may take several seconds on
        /// first run"; a single ingredient held past that reads as a hang.
        /// </summary>
        internal static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(10);

        /// <summary>
        /// Retries HTTP 429 and 503 only. Every other status, success or
        /// not, is the caller's to interpret: 404 is an answer on this API,
        /// and a 400 repeated is still a 400.
        /// </summary>
        /// <param name="retryDelay">Injected by tests so they never sleep.
        /// Null uses Task.Delay.</param>
        public static async Task<HttpResponseMessage> GetAsync(
            HttpClient http,
            string url,
            CancellationToken ct,
            Func<TimeSpan, CancellationToken, Task> retryDelay = null)
        {
            var response = await http.GetAsync(url, ct);
            if (!IsRefusal(response.StatusCode))
            {
                return response;
            }

            TimeSpan wait = HttpRetry.ResolveDelay(response, FallbackDelay, DateTimeOffset.UtcNow);
            if (wait > MaxRetryDelay)
            {
                return response;
            }

            response.Dispose();
            await (retryDelay ?? DefaultDelay)(wait, ct);
            return await http.GetAsync(url, ct);
        }

        private static Task DefaultDelay(TimeSpan wait, CancellationToken ct)
        {
            return Task.Delay(wait, ct);
        }

        private static bool IsRefusal(HttpStatusCode status)
        {
            // TooManyRequests is not in net48's HttpStatusCode enum.
            return (int)status == 429 || status == HttpStatusCode.ServiceUnavailable;
        }
    }
}
