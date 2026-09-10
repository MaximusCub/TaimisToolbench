using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The Crafting Plan strip's single status line: whatever the strip is
    /// reporting right now, then the notices that outlive it.
    /// <para>
    /// Blish-free so the widest line the strip can compose is measurable in
    /// a test. That line has to FIT the band rather than be shortened to it
    /// - see <see cref="StatusText.PlanStatusBudgetChars"/> - so every
    /// clause here is written to its share of 133 characters.
    /// </para>
    /// </summary>
    internal static class PlanStatusLine
    {
        /// <summary>
        /// Separator between the line's clauses. Narrow: at "  |  " the two
        /// separators cost four of the band's 133 characters
        /// (<see cref="StatusText.PlanStatusBudgetChars"/>), and the bar
        /// already separates the clauses on its own.
        /// </summary>
        public const string NoticeSeparator = " | ";

        /// <summary>
        /// The finished plan's own clause. Carries the account-data note
        /// only when the plan subtracted owned materials from a snapshot
        /// that is overdue for a refresh or missing a character - see
        /// <see cref="StatusText.ForPlanAccountDataNote"/>, which owns that
        /// decision and the wording.
        /// </summary>
        public static string ForGeneratedPlan(
            DateTime generatedAt,
            TimeSpan accountDataAge,
            TimeSpan staleThreshold,
            int incompleteCharacters,
            int characterCount)
        {
            string status = StatusText.Stamp("Plan generated", generatedAt);
            string clause = StatusText.ForPlanAccountDataNote(
                accountDataAge, staleThreshold, incompleteCharacters, characterCount);

            return clause == null ? status : status + " (" + clause + ")";
        }

        /// <summary>
        /// <paramref name="status"/> with the strip's standing notices
        /// appended. Returns <paramref name="status"/> itself, allocating
        /// nothing, when there are none: this runs on every spinner render
        /// for the whole of every generation.
        /// <para>
        /// The settings and account-data facts share one notice rather than
        /// taking a clause each - <see cref="StatusText.ForPlanStaleInputs"/>
        /// owns that wording.
        /// </para>
        /// </summary>
        public static string WithStandingNotices(
            string status,
            string unresolvedRowsNotice,
            bool settingsChanged,
            bool accountDataChanged)
        {
            string staleInputs = StatusText.ForPlanStaleInputs(settingsChanged, accountDataChanged);
            if (string.IsNullOrEmpty(unresolvedRowsNotice) && staleInputs == null)
            {
                return status;
            }

            var parts = new List<string>(3);
            if (!string.IsNullOrEmpty(status))
            {
                parts.Add(status);
            }

            if (!string.IsNullOrEmpty(unresolvedRowsNotice))
            {
                parts.Add(unresolvedRowsNotice);
            }

            if (staleInputs != null)
            {
                parts.Add(staleInputs);
            }

            return string.Join(NoticeSeparator, parts);
        }
    }
}
