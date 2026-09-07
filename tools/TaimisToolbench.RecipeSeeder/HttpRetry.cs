using System;
using System.Net;
using System.Net.Http;

namespace TaimisToolbench.RecipeSeeder
{
    /// <summary>
    /// The two decisions every retry in this seeder makes: whether a response
    /// is worth retrying at all, and how long to wait before doing so.
    /// </summary>
    /// <remarks>
    /// Split out from the fetch loops because both answers are pure and both
    /// were previously wrong in ways no test could see - a rate-limited batch
    /// was indistinguishable from an empty one. The contract behind them is
    /// in docs/api-client-contracts.md.
    /// </remarks>
    internal static class HttpRetry
    {
        /// <summary>
        /// Longest wait a Retry-After header can buy. A malformed or absurd
        /// value is clamped to this rather than stalling the run; the wait
        /// MediaWiki itself recommends is five seconds.
        /// </summary>
        internal static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

        /// <summary>
        /// True when the status says "come back later" rather than "this
        /// request is wrong". 429 and every 5xx are retryable; a 4xx is the
        /// seeder's own fault and repeating it only adds load.
        /// </summary>
        public static bool IsRetryable(HttpStatusCode status)
        {
            int code = (int)status;
            return code == 429 || code >= 500;
        }

        /// <summary>
        /// The wait a response asks for, or <paramref name="fallback"/> when
        /// it asks for nothing. Retry-After is either a delta in seconds or
        /// an HTTP-date (RFC 9110 section 10.2.3); reading only the delta
        /// treats every dated header as absent.
        /// </summary>
        public static TimeSpan ResolveDelay(
            HttpResponseMessage response, TimeSpan fallback, DateTimeOffset now)
        {
            TimeSpan requested = fallback;

            var retryAfter = response?.Headers?.RetryAfter;
            if (retryAfter?.Delta is TimeSpan delta)
            {
                requested = delta;
            }
            else if (retryAfter?.Date is DateTimeOffset date)
            {
                requested = date - now;
            }

            if (requested < TimeSpan.Zero)
            {
                requested = TimeSpan.Zero;
            }

            if (requested > MaxDelay)
            {
                requested = MaxDelay;
            }

            return requested > fallback ? requested : fallback;
        }
    }
}
