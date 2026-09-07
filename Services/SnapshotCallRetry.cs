using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Repeats one failed GW2 API call inside the fetch that issued it.
    /// <para>
    /// The unit is a single call, so only the part that failed runs again.
    /// A snapshot fetch issues six account-wide calls plus three per
    /// character. One bad call used to cost the whole fetch, and the
    /// caller's failure backoff then re-ran every other call a minute
    /// later.
    /// </para>
    /// <para>
    /// Failures are matched by exception type NAME, not by "is" checks
    /// against Gw2Sharp's exception classes. That keeps this file free of
    /// Gw2Sharp and Blish HUD references so real unit tests can drive it,
    /// which is why SnapshotFailureClassifier matches on names too.
    /// </para>
    /// </summary>
    internal static class SnapshotCallRetry
    {
        /// <summary>
        /// The wait before each retry, one entry per retry, so a call is
        /// attempted <see cref="MaxAttempts"/> times in all. The first wait
        /// is short because most transient refusals clear at once. The
        /// second is longer for a server still recovering. Both are paid
        /// out of the fetch budget, so neither is generous.
        /// </summary>
        public static readonly IReadOnlyList<TimeSpan> Delays = new[]
        {
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromSeconds(2),
        };

        public static int MaxAttempts
        {
            get { return Delays.Count + 1; }
        }

        /// <summary>
        /// Failures a second attempt cannot fix. An invalid or under-scoped
        /// API key does not become valid in half a second, and at character
        /// select every call fails that way, so retrying would triple a
        /// whole fetch's requests for nothing. A 404 means the character was
        /// deleted between the id list and this call, and the next fetch's
        /// id list will not name it.
        /// </summary>
        private static readonly HashSet<string> PermanentExceptionTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "InvalidAccessTokenException",
            "AuthorizationRequiredException",
            "MissingScopesException",
            "NotFoundException",
            "BadRequestException",
        };

        /// <summary>
        /// Whether <paramref name="failure"/> is worth another attempt.
        /// A cancellation of <paramref name="ct"/> is the fetch's own
        /// deadline, which a retry can only make later. A
        /// <see cref="TimeoutException"/> is one call spending its whole
        /// per-call deadline, which is a third of the smallest fetch
        /// budget; two more of those would put the fetch past it. Any other
        /// unrecognised failure is retried, because an unknown failure is
        /// more often transient than permanent and the cost is bounded at
        /// two extra calls.
        /// </summary>
        public static bool IsWorthRetrying(Exception failure, CancellationToken ct)
        {
            if (failure == null || ct.IsCancellationRequested)
            {
                return false;
            }

            if (failure is OperationCanceledException || failure is TimeoutException)
            {
                return false;
            }

            return !PermanentExceptionTypeNames.Contains(failure.GetType().Name);
        }

        public static Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> attempt, CancellationToken ct)
        {
            return RunAsync(attempt, null, null, ct);
        }

        /// <summary>
        /// Runs <paramref name="attempt"/> until it produces a usable
        /// result, until its failure is not worth retrying, or until the
        /// attempts run out. The last attempt's failure is the one that
        /// propagates.
        /// </summary>
        /// <param name="isUsable">
        /// Rejects a result the caller cannot use even though the call
        /// returned, such as the crafting fetch's empty payload. Null
        /// accepts every result.
        /// </param>
        /// <param name="delay">
        /// The wait between attempts. Tests replace it so they run without
        /// sleeping, the way Gw2BuildApiClient takes its own delay.
        /// </param>
        public static async Task<T> RunAsync<T>(
            Func<CancellationToken, Task<T>> attempt,
            Func<T, bool> isUsable,
            Func<TimeSpan, CancellationToken, Task> delay,
            CancellationToken ct)
        {
            if (attempt == null)
            {
                throw new ArgumentNullException(nameof(attempt));
            }

            var wait = delay ?? ((d, token) => Task.Delay(d, token));

            for (int retry = 0; retry < Delays.Count; retry++)
            {
                try
                {
                    var result = await attempt(ct);
                    if (isUsable == null || isUsable(result))
                    {
                        return result;
                    }
                }
                catch (Exception ex) when (IsWorthRetrying(ex, ct))
                {
                    // Dropped on purpose. The final attempt below is what
                    // reports a failure the caller has to act on.
                }

                await wait(Delays[retry], ct);
            }

            return await attempt(ct);
        }
    }
}
