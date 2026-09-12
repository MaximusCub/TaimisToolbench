using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TaimisToolbench.Services.Diagnostics
{
    /// <summary>
    /// Formats a compact, coarse-phase timing summary for
    /// CraftingPlanPipeline's "Info on finish" ModuleLog line - e.g. "tree
    /// 120ms, prices 8400ms (418 items), solve 30ms, ... - total 19036ms
    /// (phases 18158ms)". Pure function over the same raw timingLog data
    /// PlanTimingAnalyzer parses, bucketed into the coarser phases
    /// PlanPhaseEvent exposes to the live UI. Reads straight from a full
    /// CraftingPlanResult.DebugLog and stops scanning at
    /// <see cref="PlanTimingAnalyzer.SummaryHeaderLine"/>, so it can never
    /// mis-bucket a later, unrelated debug line.
    /// <para>
    /// The SUM of the raw per-step lines omits every un-instrumented gap
    /// between them, so it is always LESS THAN OR EQUAL TO wall-clock - for a
    /// real ~19s generation the two can differ by seconds, not milliseconds.
    /// The optional wallClockMs parameter on
    /// <see cref="FormatCompactSummary"/> lets the caller supply the figure a
    /// a player actually experiences; when absent the "total" stays the
    /// phase sum. Derivation: docs/ARCHITECTURE.md section S1.9.
    /// </para>
    /// </summary>
    internal static class PlanPhaseTimingSummary
    {
        // Order matters: this is the emission order of the compact
        // summary, matching PlanPhaseEvent's own BuildingTree ->
        // FetchingPrices -> SolvingDecisions -> FetchingItemDetails ->
        // CheckingLearnedRecipes -> BuildingDisplay sequence.
        private static readonly string[] BucketOrder =
        {
            "tree", "prices", "account", "solve", "item details", "learned recipes", "display",
        };

        // Maps a raw timingLog step name (PlanTimingAnalyzer.ParsedPhase.Name)
        // to the coarse bucket it belongs to. "Build recipe trees" (plural,
        // with a count) is the multi-item path's own tree-build line - see
        // CraftingPlanPipeline.GenerateStructuredMultiAsync.
        private static readonly Dictionary<string, string> BucketByStepName =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Build recipe tree", "tree" },
                { "Build recipe trees", "tree" },
                { "Collect item IDs", "tree" },
                { "Fetch TP prices", "prices" },
                { "Query vendor offers", "prices" },
                // Its own bucket, and usually near zero: the account
                // refresh a Generate starts runs alongside the tree and
                // price work, so this is only what the overlap left over.
                { "Await account refresh", "account" },
                { "Inventory reduction", "solve" },
                { "Solve", "solve" },
                { "Fetch item metadata", "item details" },
                { "Fetch currency metadata", "item details" },
                // Its own bucket: a single /v2/account/recipes round trip
                // that has nothing to do with the N items "item details"
                // is annotated with (see CountSourceByBucket below), and
                // large enough to dominate that bucket when folded in.
                { "Fetch learned recipes", "learned recipes" },
                { "Build result", "display" },
            };

        // Which single raw step supplies a bucket's optional "(N items)"
        // annotation - only the two buckets with a genuinely meaningful,
        // non-redundant item count show one (see the class doc comment's
        // example - "solve"/"display" never carry a count).
        private static readonly Dictionary<string, string> CountSourceByBucket =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "prices", "Fetch TP prices" },
                { "item details", "Fetch item metadata" },
            };

        /// <summary>
        /// Builds the compact summary from a full CraftingPlanResult.DebugLog
        /// (or any list starting with the same raw timing lines). Returns
        /// an empty string for a null/empty input or one with no
        /// recognizable timing lines at all - callers should treat that as
        /// "no summary available" and fall back to their own wording,
        /// exactly like every other degrade-gracefully seam in this
        /// pipeline.
        /// </summary>
        /// <param name="debugLog">The plan's full debug log (or any prefix ending at the summary-header marker).</param>
        /// <param name="wallClockMs">
        /// the wrapper's own wall-clock elapsed
        /// milliseconds, if known - see the class doc comment's own
        /// "phase sum vs wall clock" note. Null (the default) preserves
        /// the original phase-sum-only "total" wording exactly, so every
        /// pre-existing caller/test is unaffected.
        /// </param>
        public static string FormatCompactSummary(IReadOnlyList<string> debugLog, long? wallClockMs = null)
        {
            if (debugLog == null || debugLog.Count == 0)
            {
                return string.Empty;
            }

            var rawLines = new List<string>(debugLog.Count);
            foreach (var line in debugLog)
            {
                if (line == PlanTimingAnalyzer.SummaryHeaderLine)
                {
                    break;
                }

                rawLines.Add(line);
            }

            var phases = PlanTimingAnalyzer.Parse(rawLines);
            if (phases.Count == 0)
            {
                return string.Empty;
            }

            var msByBucket = new Dictionary<string, long>(StringComparer.Ordinal);
            var countByBucket = new Dictionary<string, int>(StringComparer.Ordinal);
            long total = 0;

            foreach (var phase in phases)
            {
                total += phase.ElapsedMs;

                if (!BucketByStepName.TryGetValue(phase.Name, out var bucket))
                {
                    // Forward-compatible: an unrecognized future timingLog
                    // step still counts toward the grand total, just is not
                    // attributed to any one bucket rather than dropped.
                    continue;
                }

                msByBucket[bucket] = msByBucket.TryGetValue(bucket, out var existingMs)
                    ? existingMs + phase.ElapsedMs
                    : phase.ElapsedMs;

                if (phase.Count.HasValue &&
                    CountSourceByBucket.TryGetValue(bucket, out var countSource) &&
                    string.Equals(phase.Name, countSource, StringComparison.Ordinal))
                {
                    countByBucket[bucket] = phase.Count.Value;
                }
            }

            var sb = new StringBuilder();
            bool first = true;
            foreach (var bucket in BucketOrder)
            {
                if (!msByBucket.TryGetValue(bucket, out var ms))
                {
                    continue;
                }

                if (!first)
                {
                    sb.Append(", ");
                }

                first = false;

                sb.Append(bucket).Append(' ')
                  .Append(ms.ToString(CultureInfo.InvariantCulture)).Append("ms");

                if (countByBucket.TryGetValue(bucket, out var count))
                {
                    sb.Append(" (").Append(count.ToString(CultureInfo.InvariantCulture)).Append(" items)");
                }
            }

            if (first)
            {
                // Every parsed line was unrecognized (should not happen in
                // practice - see the class doc comment) - nothing bucketed,
                // so there is nothing meaningful to summarize.
                return string.Empty;
            }

            sb.Append(" - total ");
            if (wallClockMs.HasValue)
            {
                // the wrapper's real wall-clock duration is
                // the number a a player actually experiences - it is
                // always >= the phase sum (un-instrumented gaps only ever
                // ADD time) - shown alongside the phase sum, not in place
                // of it, so neither figure is lost.
                sb.Append(wallClockMs.Value.ToString(CultureInfo.InvariantCulture)).Append("ms (phases ")
                  .Append(total.ToString(CultureInfo.InvariantCulture)).Append("ms)");
            }
            else
            {
                sb.Append(total.ToString(CultureInfo.InvariantCulture)).Append("ms");
            }

            return sb.ToString();
        }
    }
}
