using System;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Index lookups and edits the Plan History rows need, kept out of
    /// Module.cs so a real test can drive them. Module owns the lock and the
    /// save; these decide what changes.
    /// </summary>
    internal static class PlanHistoryIndexEdits
    {
        /// <summary>
        /// The row a Generate with this dedup key belongs to, or null when
        /// the index has none yet. Both the capture path and the
        /// override-marking path search this way, so they can never disagree
        /// about which row a plan owns.
        /// </summary>
        public static PlanHistoryEntry FindByDedupKey(PlanHistoryIndex index, string key)
        {
            if (index?.Entries == null || key == null)
            {
                return null;
            }

            foreach (var candidate in index.Entries)
            {
                if (candidate != null
                    && string.Equals(PlanHistoryDedupKey.ForEntry(candidate), key, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        /// <summary>
        /// Records that the plan tab re-solved this row's plan with manual
        /// decisions on it. Returns true when the row changed, so the caller
        /// only writes the index when there is something to write.
        /// <para>
        /// The row's Cost stays the cost at Generate, which is what its
        /// field says it is, so it can disagree with the plan tab's Total
        /// Cost as soon as a decision pill moves. The counts are what make
        /// the row say so: PlanHistoryTabContent already draws a chip and a
        /// "restored by Open, not by Re-solve" note from them, and both were
        /// unreachable because nothing ever wrote a count above zero.
        /// </para>
        /// <para>
        /// Counts only. PlanHistoryDedupKey.ForEntry reads IgnoredItemIds,
        /// so writing the ignored ids here would stop the next Generate
        /// finding this row and would leave a duplicate behind.
        /// </para>
        /// </summary>
        public static bool MarkOverrides(PlanHistoryEntry entry, int overrideCount, int ignoredCount)
        {
            if (entry == null)
            {
                return false;
            }

            int overrides = Math.Max(0, overrideCount);
            int ignored = Math.Max(0, ignoredCount);
            if (entry.OverrideCountAtGeneration == overrides
                && entry.IgnoredCountAtGeneration == ignored)
            {
                return false;
            }

            entry.OverrideCountAtGeneration = overrides;
            entry.IgnoredCountAtGeneration = ignored;
            return true;
        }
    }
}
