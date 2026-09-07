using System;
using System.Net.Http;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// How long to wait before repeating a request a server refused.
    /// </summary>
    /// <remarks>
    /// Retry-After carries either a delta in seconds or an HTTP-date
    /// (RFC 9110 section 10.2.3). A client that reads only the delta treats
    /// every dated header as absent and comes back at once, against a
    /// server that just said when to return. This file is also compiled
    /// into tools/MysticForgeSeeder by source link, so the wiki scraper and
    /// the module answer the header the same way.
    /// </remarks>
    internal static class HttpRetry
    {
        /// <summary>
        /// Longest wait a Retry-After header can buy. A value further out
        /// than this is clamped rather than stalling the caller for as long
        /// as it asks.
        /// </summary>
        internal static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

        /// <summary>
        /// The wait <paramref name="response"/> asks for, or
        /// <paramref name="fallback"/> when it asks for nothing, for less,
        /// or for a moment already past. A header the framework could not
        /// parse reads as absent.
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
