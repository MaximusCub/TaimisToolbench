using System;
using System.Collections.Generic;
using System.Linq;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Classifies a failed snapshot refresh into a SnapshotFailureKind - see
    /// that enum's own doc comment for the in-game incident this
    /// exists for. Deliberately matches by exception TYPE NAME (a plain
    /// string, e.g. "InvalidAccessTokenException") rather than by C# "is"
    /// type checks against Gw2Sharp's own exception classes: this file
    /// stays completely free of Gw2Sharp/Blish HUD references so it can be
    /// exercised by REAL unit tests per the repo's "tests must never
    /// reference Blish HUD/Gw2Sharp" invariant (see
    /// SnapshotFetchFailedExceptionTests' own doc comment for the same
    /// reasoning applied to that type). Gw2AccountSnapshotService - which
    /// IS allowed to reference Gw2Sharp - turns real caught exceptions
    /// into type name strings (ex.GetType().Name) via
    /// SnapshotFetchFailedException's FailedSourceExceptionTypeNames; the
    /// Classify(Exception) overload below does the same for a bare
    /// exception.
    /// </summary>
    internal static class SnapshotFailureClassifier
    {
        /// <summary>
        /// Exception type names that mean "the API key this module would
        /// use is invalid, missing, or under-scoped" - Gw2Sharp's own
        /// InvalidAccessTokenException (no/garbage token - the character-
        /// select case), AuthorizationRequiredException (request sent with
        /// no token at all), and MissingScopesException (a real token that
        /// lacks a permission this module needs - the dialog's "3. This
        /// module has permission..." check).
        /// </summary>
        private static readonly HashSet<string> ApiAccessExceptionTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "InvalidAccessTokenException",
            "AuthorizationRequiredException",
            "MissingScopesException",
        };

        /// <summary>
        /// Exception type names that mean "the GW2 API (or the transport to
        /// it) is unavailable" rather than an access problem - Gw2Sharp's
        /// ServiceUnavailableException (503)/ServerErrorException (5xx)/
        /// TooManyRequestsException (429, the API is up but throttling)/
        /// RequestCanceledException (an internal request-level
        /// cancellation/timeout distinct from a caller's own
        /// OperationCanceledException - see Gw2AccountSnapshotService's
        /// per-source catch filters), plus the plain BCL TimeoutException
        /// Module.FetchAndSaveSnapshotAsync throws when its own fetch
        /// timeout fires, and HttpRequestException for a lower-level
        /// transport failure (DNS, connection refused, TLS).
        /// </summary>
        private static readonly HashSet<string> NetworkOrApiDownExceptionTypeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "ServiceUnavailableException",
            "ServerErrorException",
            "TooManyRequestsException",
            "RequestCanceledException",
            "TimeoutException",
            "HttpRequestException",
        };

        /// <summary>
        /// Primary, fully pure classification entry point - takes the
        /// already-extracted per-source exception type names (see
        /// SnapshotFetchFailedException.FailedSourceExceptionTypeNames)
        /// plus the source counts, and applies the priority rules described
        /// on SnapshotFailureKind: ApiAccessNotReady first (a broken token
        /// affects every source using it, regardless of what else also
        /// failed), then PartialFailure (some sources succeeded), then
        /// NetworkOrApiDown (a total failure with a known network/API
        /// cause), then IncompleteCharacters (every source read, a
        /// character not), then Unknown.
        /// </summary>
        public static SnapshotFailureClassification Classify(
            IReadOnlyList<string> failedSourceExceptionTypeNames,
            int failedSourceCount,
            int totalSourceCount)
        {
            return Classify(failedSourceExceptionTypeNames, failedSourceCount, totalSourceCount, 0);
        }

        /// <summary>
        /// The same, for a fetch that also reports characters it could not
        /// read in full. A failed source outranks that: it is the bigger
        /// fact, and the character failures usually share its cause.
        /// </summary>
        public static SnapshotFailureClassification Classify(
            IReadOnlyList<string> failedSourceExceptionTypeNames,
            int failedSourceCount,
            int totalSourceCount,
            int incompleteCharacterCount)
        {
            var kind = ClassifyKind(
                failedSourceExceptionTypeNames, failedSourceCount, totalSourceCount, incompleteCharacterCount);
            return new SnapshotFailureClassification(
                kind, failedSourceCount, totalSourceCount, incompleteCharacterCount);
        }

        /// <summary>
        /// Convenience overload for MainView's catch block: unwraps a
        /// SnapshotFetchFailedException into its per-source detail, or
        /// treats any other exception (e.g. the bare TimeoutException
        /// Module.FetchAndSaveSnapshotAsync throws on its own internal
        /// fetch timeout, which never goes through
        /// Gw2AccountSnapshotService at all) as a single unattributed
        /// source with no known counts.
        /// </summary>
        public static SnapshotFailureClassification Classify(Exception exception)
        {
            if (exception is SnapshotFetchFailedException fetchFailed)
            {
                return Classify(
                    fetchFailed.FailedSourceExceptionTypeNames,
                    fetchFailed.FailedSourceCount,
                    fetchFailed.TotalSourceCount,
                    fetchFailed.IncompleteCharacterNames.Count);
            }

            string[] typeNames = exception != null
                ? new[] { exception.GetType().Name }
                : new string[0];
            return Classify(typeNames, failedSourceCount: 0, totalSourceCount: 0);
        }

        private static SnapshotFailureKind ClassifyKind(
            IReadOnlyList<string> failedSourceExceptionTypeNames,
            int failedSourceCount,
            int totalSourceCount,
            int incompleteCharacterCount)
        {
            if (ContainsAny(failedSourceExceptionTypeNames, ApiAccessExceptionTypeNames))
            {
                return SnapshotFailureKind.ApiAccessNotReady;
            }

            if (totalSourceCount > 0 && failedSourceCount > 0 && failedSourceCount < totalSourceCount)
            {
                return SnapshotFailureKind.PartialFailure;
            }

            if (ContainsAny(failedSourceExceptionTypeNames, NetworkOrApiDownExceptionTypeNames))
            {
                return SnapshotFailureKind.NetworkOrApiDown;
            }

            if (failedSourceCount == 0 && incompleteCharacterCount > 0)
            {
                return SnapshotFailureKind.IncompleteCharacters;
            }

            return SnapshotFailureKind.Unknown;
        }

        private static bool ContainsAny(IReadOnlyList<string> typeNames, HashSet<string> candidates)
        {
            if (typeNames == null)
            {
                return false;
            }

            return typeNames.Any(name => name != null && candidates.Contains(name));
        }
    }
}
