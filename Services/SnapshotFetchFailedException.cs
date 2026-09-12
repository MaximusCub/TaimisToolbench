using System;
using System.Collections.Generic;
using System.Linq;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Thrown by Gw2AccountSnapshotService.FetchSnapshotAsync when part of
    /// the account could not be read for this fetch (KNOWN-ISSUES
    /// #31/api-degradation F1): an account-wide source, or one character's
    /// bags, equipment or crafting disciplines.
    ///
    /// Conservative persistence rule: FetchSnapshotAsync only ever returns
    /// normally when every one of those was read in full. ANY failure -
    /// partial or total - throws this instead of returning a snapshot with
    /// holes, so a caller can never silently replace a good cached snapshot
    /// with one that is missing what the previous snapshot had. Module.cs's
    /// callers let it propagate to their generic-Exception catch, which
    /// keeps the prior snapshot and surfaces a "Refresh failed" status.
    ///
    /// Genuine caller cancellation is unaffected: the per-source catches
    /// exclude OperationCanceledException, so a real cancellation still
    /// propagates as itself and is never wrapped in this type.
    /// </summary>
    internal class SnapshotFetchFailedException : Exception
    {
        public int FailedSourceCount { get; }

        public int TotalSourceCount { get; }

        /// <summary>
        /// The .NET type name (Exception.GetType().Name, e.g.
        /// "InvalidAccessTokenException") of each individual source failure
        /// that contributed to FailedSourceCount, in no particular order.
        /// Deliberately a plain string list, not the exceptions themselves
        /// or Gw2Sharp's own exception types - this class must stay
        /// Gw2Sharp/Blish-free (see SnapshotFailureClassifier's doc
        /// comment) so it can keep being exercised by real unit tests.
        /// Never null; empty when the caller does not supply per-source
        /// detail (the pre-existing 2-arg constructor below, kept for its
        /// original call sites/tests).
        /// </summary>
        public IReadOnlyList<string> FailedSourceExceptionTypeNames { get; }

        /// <summary>
        /// The characters whose bags, equipment or crafting disciplines
        /// could not be read in full, in character-list order. Never null.
        /// Names, not ids: the user reads these in game, and the account
        /// data is not trustworthy without them.
        /// </summary>
        public IReadOnlyList<string> IncompleteCharacterNames { get; }

        /// <summary>
        /// Which account-wide reads failed, as opposed to how many. Never
        /// null; empty when the caller supplied no names, which a reader
        /// must not read as "none failed" - check
        /// <see cref="FailedSourceCount"/> for that.
        /// </summary>
        public IReadOnlyList<AccountDataSource> FailedSources { get; }

        public SnapshotFetchFailedException(int failedSourceCount, int totalSourceCount)
            : this(failedSourceCount, totalSourceCount, null)
        {
        }

        public SnapshotFetchFailedException(int failedSourceCount, int totalSourceCount, IEnumerable<string> failedSourceExceptionTypeNames)
            : this(failedSourceCount, totalSourceCount, failedSourceExceptionTypeNames, null)
        {
        }

        public SnapshotFetchFailedException(
            int failedSourceCount,
            int totalSourceCount,
            IEnumerable<string> failedSourceExceptionTypeNames,
            IEnumerable<string> incompleteCharacterNames)
            : this(failedSourceCount, totalSourceCount, failedSourceExceptionTypeNames, incompleteCharacterNames, null)
        {
        }

        public SnapshotFetchFailedException(
            int failedSourceCount,
            int totalSourceCount,
            IEnumerable<string> failedSourceExceptionTypeNames,
            IEnumerable<string> incompleteCharacterNames,
            IEnumerable<AccountDataSource> failedSources)
            : base(BuildMessage(failedSourceCount, totalSourceCount, Names(incompleteCharacterNames)))
        {
            FailedSourceCount = failedSourceCount;
            TotalSourceCount = totalSourceCount;
            FailedSourceExceptionTypeNames = failedSourceExceptionTypeNames?.ToList() ?? new List<string>();
            IncompleteCharacterNames = Names(incompleteCharacterNames);
            FailedSources = failedSources?.ToList() ?? new List<AccountDataSource>();
        }

        private static List<string> Names(IEnumerable<string> names)
        {
            return names?.ToList() ?? new List<string>();
        }

        /// <summary>
        /// What actually failed, in the caller's own words. A character
        /// failure is not an account-wide source, so it gets its own
        /// sentence rather than being folded into the source tally.
        /// </summary>
        private static string BuildMessage(
            int failedSourceCount, int totalSourceCount, List<string> incompleteCharacterNames)
        {
            var parts = new List<string>(2);
            if (failedSourceCount >= totalSourceCount && failedSourceCount > 0)
            {
                parts.Add("All account data sources failed.");
            }
            else if (failedSourceCount > 0)
            {
                parts.Add($"{failedSourceCount} of {totalSourceCount} account data sources failed.");
            }

            if (incompleteCharacterNames.Count > 0)
            {
                string label = incompleteCharacterNames.Count == 1 ? "character" : "characters";
                parts.Add(
                    $"{incompleteCharacterNames.Count} {label} could not be read in full: "
                    + string.Join(", ", incompleteCharacterNames) + ".");
            }

            return parts.Count == 0
                ? "The account snapshot fetch failed."
                : string.Join(" ", parts);
        }
    }
}
