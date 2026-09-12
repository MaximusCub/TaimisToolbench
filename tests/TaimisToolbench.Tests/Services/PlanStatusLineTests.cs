using System;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The Crafting Plan strip is one line, and four separate facts can be
    /// true of a plan at once. Composed here so the widest line the strip
    /// can produce is measurable against the band it has to fit; the label
    /// that draws it is view code and is not covered.
    /// </summary>
    public class PlanStatusLineTests
    {
        private static readonly DateTime Generated = new DateTime(2026, 9, 30, 12, 38, 0);
        private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(30);

        [Fact]
        public void GeneratedPlan_FreshAndComplete_IsJustTheStamp()
        {
            Assert.Equal(
                StatusText.Stamp("Plan generated", Generated),
                PlanStatusLine.ForGeneratedPlan(
                    Generated, TimeSpan.FromMinutes(1), StaleAfter, 0, 9));
        }

        [Fact]
        public void GeneratedPlan_StaleData_ParenthesisesTheAccountClause()
        {
            string line = PlanStatusLine.ForGeneratedPlan(
                Generated, TimeSpan.FromHours(11), StaleAfter, 0, 9);

            Assert.EndsWith(" (account data 11h 0m old)", line);
            Assert.StartsWith(StatusText.Stamp("Plan generated", Generated), line);
        }

        [Fact]
        public void StandingNotices_NoneOfThem_ReturnsTheStatusUntouched()
        {
            string status = "Plan generated - Sep 30, 2026 12:38 PM";

            Assert.Same(
                status,
                PlanStatusLine.WithStandingNotices(status, null, false, false));
        }

        [Fact]
        public void StandingNotices_EachOneAppendsExactlyOneClause()
        {
            string status = "Plan generated";
            string unresolved = ItemRowSelection.UnresolvedRowsNotice(2);

            string withRows = PlanStatusLine.WithStandingNotices(status, unresolved, false, false);
            string withBoth = PlanStatusLine.WithStandingNotices(status, unresolved, true, true);

            Assert.Equal(status + PlanStatusLine.NoticeSeparator + unresolved, withRows);
            Assert.Equal(
                withRows + PlanStatusLine.NoticeSeparator + StatusText.ForPlanStaleInputs(true, true),
                withBoth);
        }

        [Fact]
        public void StandingNotices_NoStatusYet_StillCarriesTheNotices()
        {
            // The strip is rebuilt before anything has generated, and the
            // notices must survive that rebuild on their own.
            string line = PlanStatusLine.WithStandingNotices(null, null, true, false);

            Assert.Equal(StatusText.ForPlanStaleInputs(true, false), line);
        }

        /// <summary>
        /// The widest line the strip can compose, built the way the view
        /// builds it. Every clause here can be true of one plan at once: it
        /// was solved against old account data with characters unread, some
        /// input rows named nothing, and both the settings and the snapshot
        /// have moved since.
        /// </summary>
        private static string WorstRealisticLine()
        {
            string generated = PlanStatusLine.ForGeneratedPlan(
                Generated,
                TimeSpan.FromMinutes((11 * 60) + 59),
                StaleAfter,
                incompleteCharacters: 99,
                characterCount: 99);

            return PlanStatusLine.WithStandingNotices(
                generated,
                ItemRowSelection.UnresolvedRowsNotice(12),
                settingsChanged: true,
                accountDataChanged: true);
        }

        /// <summary>
        /// Ceiling for that line, in characters. A ratchet: a clause added
        /// to the strip fails here and has to be a decision rather than a
        /// silent widening.
        /// </summary>
        private const int WorstRealisticLineChars = 132;

        [Fact]
        public void WorstRealisticLine_StaysWithinItsPinnedLength()
        {
            string line = WorstRealisticLine();

            Assert.Equal(WorstRealisticLineChars, line.Length);
        }

        [Fact]
        public void WorstRealisticLine_FitsTheBand()
        {
            // The whole point of the clause wording. The line is short
            // enough to draw in full, so nothing is shortened and nothing
            // hides on a hover. This was 232 characters against a band that
            // holds about 133.
            string line = WorstRealisticLine();

            Assert.True(
                line.Length <= StatusText.PlanStatusBudgetChars,
                $"worst line {line.Length} chars, budget "
                    + $"{StatusText.PlanStatusBudgetChars}");
        }

        [Fact]
        public void WorstRealisticLine_NamesEveryFactItWasBuiltFrom()
        {
            // Shortening the clauses must not have dropped one of them.
            // Each fact is still on the line, in its own words.
            string line = WorstRealisticLine();

            Assert.Contains("Plan generated", line);
            Assert.Contains("11h 59m old", line);
            Assert.Contains("incomplete", line);
            Assert.Contains("12 rows left out", line);
            Assert.Contains("Settings and account data changed", line);
        }

        [Fact]
        public void OrdinaryCompletionLine_FitsTheBandWithRoomToSpare()
        {
            // The line almost every generation actually shows: fresh data,
            // nothing outstanding.
            string line = PlanStatusLine.WithStandingNotices(
                PlanStatusLine.ForGeneratedPlan(Generated, TimeSpan.FromMinutes(1), StaleAfter, 0, 9),
                null,
                settingsChanged: false,
                accountDataChanged: false);

            Assert.True(
                line.Length < StatusText.PlanStatusBudgetChars,
                $"ordinary line {line.Length} chars, budget {StatusText.PlanStatusBudgetChars}");
        }
    }
}
