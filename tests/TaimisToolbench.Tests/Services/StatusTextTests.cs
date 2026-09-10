using System;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class StatusTextTests
    {
        [Fact]
        public void Normalize_NonNull_ReturnsSameString()
        {
            Assert.Equal("Updated - 1:00 PM", StatusText.Normalize("Updated - 1:00 PM"));
        }

        // A status.txt written by a build older than the hyphen separator
        // still holds the em dash one, and StatusStore returns the file
        // verbatim. Normalize is the load-side pass Views/MainView.cs runs
        // it through.
        [Fact]
        public void Normalize_LegacySeparator_ReadsAsTheCurrentSeparator()
        {
            Assert.Equal("Updated - 1:00 PM", StatusText.Normalize("Updated \u2014 1:00 PM"));
        }

        [Fact]
        public void Normalize_Null_ReturnsEmpty()
        {
            Assert.Equal("", StatusText.Normalize(null));
        }

        [Fact]
        public void Normalize_Empty_ReturnsEmpty()
        {
            Assert.Equal("", StatusText.Normalize(""));
        }

        // Count: the module's one spelling of a counted noun. "(s)" is
        // banned from user-facing text, and every count the plan view is
        // about to show (overrides, ignored items, copied lines) goes
        // through here.
        [Theory]
        [InlineData(0, "0 overrides")]
        [InlineData(1, "1 override")]
        [InlineData(2, "2 overrides")]
        [InlineData(147, "147 overrides")]
        public void Count_PluralizesOnOneAndNothingElse(int n, string expected)
        {
            Assert.Equal(expected, StatusText.Count(n, "override"));
        }

        [Fact]
        public void Count_IrregularPlural_IsPassedExplicitly()
        {
            Assert.Equal("1 entry", StatusText.Count(1, "entry", "entries"));
            Assert.Equal("2 entries", StatusText.Count(2, "entry", "entries"));
        }

        [Fact]
        public void Count_NegativeCount_StillReadsAsAPlural()
        {
            // Not reachable from any current caller, but a count that went
            // negative must not read "-1 override" as though it were one.
            Assert.Equal("-1 overrides", StatusText.Count(-1, "override"));
        }

        // The ignore toggle (and every other
        // non-Best-Path re-solve trigger) must never produce the Best
        // Path preset's own label - this is exactly the "Best path
        // restored" mislabel bug.
        [Fact]
        public void ForOverrideResolve_NotBestPathPreset_ReportsTheEventOnly()
        {
            Assert.Equal("Plan updated", StatusText.ForOverrideResolve(isBestPathPreset: false));
        }

        [Fact]
        public void ForOverrideResolve_BestPathPreset_ReturnsBestPathRestored()
        {
            Assert.Equal("Best path restored", StatusText.ForOverrideResolve(isBestPathPreset: true));
        }

        /// <summary>
        /// The events/state split: the status line reports what HAPPENED
        /// and carries no standing count. How many decisions are overridden
        /// is state, and lives in its own chip.
        /// </summary>
        [Fact]
        public void ForOverrideResolve_CarriesNoCount()
        {
            string line = StatusText.ForOverrideResolve(isBestPathPreset: false);

            Assert.DoesNotContain("override", line, StringComparison.OrdinalIgnoreCase);
            foreach (char c in line)
            {
                Assert.False(char.IsDigit(c), "the status line must carry no count: " + line);
            }
        }

        [Theory]
        [InlineData(0, "Overrides: 0")]
        [InlineData(1, "Overrides: 1")]
        [InlineData(12, "Overrides: 12")]
        public void ForOverridesChip_IsALabelledCount(int n, string expected)
        {
            Assert.Equal(expected, StatusText.ForOverridesChip(n));
        }

        [Theory]
        [InlineData(1, "Ignored: 1")]
        [InlineData(7, "Ignored: 7")]
        public void ForIgnoredChip_IsALabelledCount(int n, string expected)
        {
            Assert.Equal(expected, StatusText.ForIgnoredChip(n));
        }

        /// <summary>
        /// The two failure verbs must stay distinct: a failed GENERATION
        /// leaves the tab with the plan it had, a failed local re-solve
        /// leaves the plan on screen intact with only the change
        /// unapplied. "Error:" said neither.
        /// </summary>
        [Fact]
        public void FailureVerbs_NameWhatFailed_AndDiffer()
        {
            Assert.Equal("Generation failed: no route to host", StatusText.ForGenerationFailure("no route to host"));
            Assert.Equal("Update failed: no route to host", StatusText.ForUpdateFailure("no route to host"));
            Assert.NotEqual(
                StatusText.ForGenerationFailure("x"), StatusText.ForUpdateFailure("x"));
        }

        [Fact]
        public void NoOpLines_SayWhyTheClickDidNothing()
        {
            // Each pairs with one guard in the confirm matrix. They are
            // sentence-case event lines like every other status write, and
            // none of them reaches for "(s)".
            foreach (string line in new[]
            {
                StatusText.NoOverridesToClear,
                StatusText.AlreadyCraftingEverything,
                StatusText.AlreadyBuyingEverything,
                StatusText.ReSolveUnavailable,
            })
            {
                Assert.False(string.IsNullOrWhiteSpace(line));
                Assert.DoesNotContain("(s)", line);
                Assert.Equal(char.ToUpperInvariant(line[0]), line[0]);
            }
        }

        [Fact]
        public void UnavailableIsNotUnnecessary_SoItsLineClaimsNothingAboutThePlan()
        {
            // A plan restored without its solve context can be rendered and
            // not re-solved. The three lines above assert what the plan
            // ALREADY contains, which is exactly what nothing has read in
            // that state - so this one must not be any of them, and must
            // name the action that gets out of it.
            Assert.NotEqual(StatusText.AlreadyCraftingEverything, StatusText.ReSolveUnavailable);
            Assert.NotEqual(StatusText.AlreadyBuyingEverything, StatusText.ReSolveUnavailable);
            Assert.NotEqual(StatusText.NoOverridesToClear, StatusText.ReSolveUnavailable);

            Assert.DoesNotContain("Already", StatusText.ReSolveUnavailable);
            Assert.Contains("Generate Plan", StatusText.ReSolveUnavailable);
        }

        // ---- IsStale (the staleness label and Module.Update()'s
        // auto-refresh gate share ONE threshold, sourced from
        // SnapshotRefreshIntervalMinutes - the threshold is a parameter
        // here precisely so the caller's setting value flows through
        // rather than being hardcoded on either side) ----
        [Fact]
        public void IsStale_AgeBelowThreshold_ReturnsFalse()
        {
            Assert.False(StatusText.IsStale(TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public void IsStale_AgeEqualToThreshold_ReturnsTrue()
        {
            Assert.True(StatusText.IsStale(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public void IsStale_AgeAboveThreshold_ReturnsTrue()
        {
            Assert.True(StatusText.IsStale(TimeSpan.FromMinutes(11), TimeSpan.FromMinutes(10)));
        }

        [Fact]
        public void IsStale_SameAge_DifferentThresholds_ThresholdDecides()
        {
            // The same 6-minute-old snapshot is stale under a 5-minute
            // setting and fresh under a 10-minute one - pins that the
            // verdict tracks the supplied threshold, not a constant.
            TimeSpan age = TimeSpan.FromMinutes(6);
            Assert.True(StatusText.IsStale(age, TimeSpan.FromMinutes(5)));
            Assert.False(StatusText.IsStale(age, TimeSpan.FromMinutes(10)));
        }

        // ---- ForRefreshFailure (pain seen in game: the
        // Snapshot tab's Refresh Now used to show only bare
        // "Refresh Failed - {time}" regardless of cause) ----
        [Fact]
        public void ForRefreshFailure_ApiAccessNotReady_ReturnsAccessNotReadyText()
        {
            Assert.Equal(
                "Refresh failed: GW2 API access not ready",
                StatusText.ForRefreshFailure(Classification(SnapshotFailureKind.ApiAccessNotReady, 5, 5)));
        }

        [Fact]
        public void ForRefreshFailure_NetworkOrApiDown_ReturnsCouldNotReachText()
        {
            Assert.Equal(
                "Refresh failed: could not reach the GW2 API",
                StatusText.ForRefreshFailure(Classification(SnapshotFailureKind.NetworkOrApiDown, 5, 5)));
        }

        [Fact]
        public void ForRefreshFailure_PartialFailure_SaysTheRefreshFailedNotThatItHalfWorked()
        {
            // A fetch that could not read everything commits nothing, so
            // the data on screen is still the previous snapshot. The old
            // "Refresh partially failed" read as though half of it landed.
            Assert.Equal(
                "Refresh failed: 2 of 5 sources unavailable",
                StatusText.ForRefreshFailure(Classification(SnapshotFailureKind.PartialFailure, 2, 5)));
        }

        [Fact]
        public void ForRefreshFailure_IncompleteCharacters_NamesTheCountOfCharacters()
        {
            Assert.Equal(
                "Refresh failed: could not read 1 character in full",
                StatusText.ForRefreshFailure(
                    new SnapshotFailureClassification(SnapshotFailureKind.IncompleteCharacters, 0, 6, 1)));

            Assert.Equal(
                "Refresh failed: could not read 3 characters in full",
                StatusText.ForRefreshFailure(
                    new SnapshotFailureClassification(SnapshotFailureKind.IncompleteCharacters, 0, 6, 3)));
        }

        [Fact]
        public void ForRefreshFailure_Unknown_ReturnsBareFailedText()
        {
            // Matches the pre-existing "Refresh failed - {time}" shape
            // exactly - callers append the time suffix themselves.
            Assert.Equal(
                "Refresh failed",
                StatusText.ForRefreshFailure(Classification(SnapshotFailureKind.Unknown, 0, 0)));
        }

        [Fact]
        public void ForRefreshFailure_NoClassification_StillReadsAsAStatusLine()
        {
            Assert.Equal("Refresh failed", StatusText.ForRefreshFailure(null));
        }

        private static SnapshotFailureClassification Classification(
            SnapshotFailureKind kind, int failedSourceCount, int totalSourceCount)
        {
            return new SnapshotFailureClassification(kind, failedSourceCount, totalSourceCount);
        }

        // ---- Stamp: the ONE shape every timestamped
        // status line in the module uses. Four sites wrote it by hand with
        // two different separators before this. ----
        [Fact]
        public void Stamp_VerbAndTime_UsesTheSingleSeparatorAndFormat()
        {
            Assert.Equal(
                "Plan generated - Aug 8, 2026 3:00 PM",
                StatusText.Stamp("Plan generated", new DateTime(2026, 8, 8, 15, 0, 0)));
        }

        [Fact]
        public void Stamp_FailureCause_ReadsAsOneClausePerSeparator()
        {
            // The cause clause is colon-introduced and the timestamp
            // dash-introduced, so the composed line never repeats one
            // separator at two grammatical levels.
            string cause = StatusText.ForRefreshFailure(
                Classification(SnapshotFailureKind.NetworkOrApiDown, 5, 5));
            Assert.Equal(
                "Refresh failed: could not reach the GW2 API - Aug 15, 2026 3:41 PM",
                StatusText.Stamp(cause, new DateTime(2026, 8, 15, 15, 41, 0)));
        }

        [Fact]
        public void Stamp_BlankVerb_ReturnsBareTimestampNotADanglingSeparator()
        {
            string expected = "Aug 8, 2026 3:00 PM";
            Assert.Equal(expected, StatusText.Stamp(null, new DateTime(2026, 8, 8, 15, 0, 0)));
            Assert.Equal(expected, StatusText.Stamp("   ", new DateTime(2026, 8, 8, 15, 0, 0)));
        }

        // ---- ForSnapshotAgeSuffix: an elapsed time, which the caller
        // punctuates apart from the refresh timestamp it follows. ----
        [Fact]
        public void ForSnapshotAgeSuffix_SubMinute_ReadsAsCapturedNotJustNow()
        {
            // Its one departure from ForAgeAgo: "just now" straight after a
            // refresh timestamp reads as a restatement of that instant.
            Assert.Equal("just captured", StatusText.ForSnapshotAgeSuffix(TimeSpan.FromSeconds(30)));
            Assert.Equal("just captured", StatusText.ForSnapshotAgeSuffix(TimeSpan.Zero));
        }

        [Fact]
        public void ForSnapshotAgeSuffix_Negative_ClampedToZero()
        {
            // CapturedAt momentarily ahead of the local clock (minor clock
            // skew) must never render as a negative duration.
            Assert.Equal("just captured", StatusText.ForSnapshotAgeSuffix(TimeSpan.FromSeconds(-5)));
        }

        // The bucket ladder, boundary by boundary. Every bucket carries the
        // coarsest unit the age has reached and at most one finer term.
        [Theory]
        [InlineData(0.5, "just captured")]
        [InlineData(1, "1m ago")]
        [InlineData(59, "59m ago")]
        [InlineData(60, "1h 0m ago")]
        [InlineData(65, "1h 5m ago")]
        [InlineData(1439, "23h 59m ago")]
        [InlineData(1440, "1d ago")]
        [InlineData(2880, "2d ago")]
        [InlineData(41760, "29d ago")]
        [InlineData(43199, "29d ago")]
        [InlineData(43200, "1mo ago")]
        [InlineData(86400, "2mo ago")]
        [InlineData(525600, "12mo ago")]
        public void ForSnapshotAgeSuffix_BucketLadder(double minutes, string expected)
        {
            Assert.Equal(expected, StatusText.ForSnapshotAgeSuffix(TimeSpan.FromMinutes(minutes)));
        }

        [Fact]
        public void ForSnapshotAgeSuffix_MaxTimeSpan_StillFormats()
        {
            // The top bucket divides TotalDays, so even TimeSpan.MaxValue
            // stays inside int - no overflow, no negative month count.
            Assert.EndsWith("mo ago", StatusText.ForSnapshotAgeSuffix(TimeSpan.MaxValue));
        }

        // ---- ForIncompleteCharacters and ForPlanAccountDataNote: what
        // the module says about a snapshot it could not read in full. The
        // plan clause follows a "Plan generated" timestamp, so unlike
        // ForSnapshotAgeSuffix it must name what the age belongs to.
        private static readonly TimeSpan DefaultRefreshInterval = TimeSpan.FromMinutes(10);

        [Fact]
        public void ForIncompleteCharacters_NoneMissing_SaysNothing()
        {
            Assert.Null(StatusText.ForIncompleteCharacters(0, 9));
            Assert.Null(StatusText.ForIncompleteCharacters(-1, 9));
        }

        [Fact]
        public void ForIncompleteCharacters_NoCharacters_SaysNothing()
        {
            // An account whose character list never loaded is a failed
            // source, not an incomplete one. Reporting "0 of 0" here would
            // put a fault on screen that this value does not describe.
            Assert.Null(StatusText.ForIncompleteCharacters(0, 0));
            Assert.Null(StatusText.ForIncompleteCharacters(2, 0));
        }

        [Fact]
        public void ForIncompleteCharacters_NounAgreesWithTheTotal()
        {
            Assert.Equal(
                "incomplete for 2 of 9 characters",
                StatusText.ForIncompleteCharacters(2, 9));
            Assert.Equal(
                "incomplete for 1 of 1 character",
                StatusText.ForIncompleteCharacters(1, 1));
        }

        [Fact]
        public void ForIncompleteCharacters_MoreMissingThanKnown_ClampsToTheTotal()
        {
            // Never claims more characters failed than the fetch asked for.
            Assert.Equal(
                "incomplete for 3 of 3 characters",
                StatusText.ForIncompleteCharacters(5, 3));
        }

        [Fact]
        public void ForPlanAccountDataNote_FreshAndComplete_SaysNothing()
        {
            // The healthy case is the common one, and a clause on every plan
            // would be furniture rather than a warning.
            Assert.Null(StatusText.ForPlanAccountDataNote(
                TimeSpan.FromMinutes(9), DefaultRefreshInterval, 0, 9));
            Assert.Null(StatusText.ForPlanAccountDataNote(
                TimeSpan.Zero, DefaultRefreshInterval, 0, 9));
        }

        [Fact]
        public void ForPlanAccountDataNote_AtOrPastThreshold_ReportsTheAge()
        {
            // The same boundary IsStale uses, so the clause and the Snapshot
            // tab's amber recolor can never disagree about one snapshot.
            Assert.Equal(
                "account data 10m old",
                StatusText.ForPlanAccountDataNote(
                    TimeSpan.FromMinutes(10), DefaultRefreshInterval, 0, 9));
            Assert.Equal(
                "account data 37m old",
                StatusText.ForPlanAccountDataNote(
                    TimeSpan.FromMinutes(37), DefaultRefreshInterval, 0, 9));
        }

        [Fact]
        public void ForPlanAccountDataNote_IncompleteIsReportedAtAnyAge()
        {
            // A snapshot captured seconds ago with a character missing is
            // exactly the fault that makes a plan recommend buying an owned
            // item, so no age threshold gates this half. It is a flag and
            // not a count: the band holds four clauses in 133 characters,
            // and ForSnapshotDetail names the count on the Snapshot tab.
            Assert.Equal(
                "account data incomplete",
                StatusText.ForPlanAccountDataNote(
                    TimeSpan.Zero, DefaultRefreshInterval, 2, 9));
            Assert.Equal(
                "account data incomplete",
                StatusText.ForPlanAccountDataNote(
                    TimeSpan.Zero, DefaultRefreshInterval, 99, 99));
        }

        [Fact]
        public void ForPlanAccountDataNote_BothFaults_ReportsBothOnOneClause()
        {
            Assert.Equal(
                "account data 37m old, incomplete",
                StatusText.ForPlanAccountDataNote(
                    TimeSpan.FromMinutes(37), DefaultRefreshInterval, 2, 9));
        }

        [Fact]
        public void ForPlanAccountDataNote_ThresholdDecidesTheAgeHalf()
        {
            var age = TimeSpan.FromMinutes(30);
            Assert.NotNull(StatusText.ForPlanAccountDataNote(
                age, TimeSpan.FromMinutes(15), 0, 9));
            Assert.Null(StatusText.ForPlanAccountDataNote(
                age, TimeSpan.FromMinutes(120), 0, 9));
        }

        [Fact]
        public void ForPlanAccountDataNote_Negative_ClampedToZero()
        {
            // CapturedAt momentarily ahead of the local clock must never
            // render as a negative duration, nor read as stale.
            Assert.Null(StatusText.ForPlanAccountDataNote(
                TimeSpan.FromSeconds(-5), DefaultRefreshInterval, 0, 9));
        }

        [Theory]
        [InlineData(60, "account data 1h 0m old")]
        [InlineData(1440, "account data 1d old")]
        [InlineData(43200, "account data 1mo old")]
        public void ForPlanAccountDataNote_RidesTheSameLadder(double minutes, string expected)
        {
            Assert.Equal(expected, StatusText.ForPlanAccountDataNote(
                TimeSpan.FromMinutes(minutes), DefaultRefreshInterval, 0, 9));
        }

        // ---- ForPlanStaleInputs and PlanAccountDataMoved: the strip's
        // standing notice, and the decision behind its account-data half.
        [Fact]
        public void ForPlanStaleInputs_NothingChanged_SaysNothing()
        {
            Assert.Null(StatusText.ForPlanStaleInputs(false, false));
        }

        [Fact]
        public void ForPlanStaleInputs_NamesWhicheverChanged()
        {
            Assert.Equal(
                "Settings changed",
                StatusText.ForPlanStaleInputs(true, false));
            Assert.Equal(
                "Account data changed",
                StatusText.ForPlanStaleInputs(false, true));
        }

        /// <summary>
        /// Both facts, ONE clause, and no remedy spelled out. The Generate
        /// Plan button is on the same strip, and the band holds 133
        /// characters for four competing clauses.
        /// </summary>
        [Fact]
        public void ForPlanStaleInputs_BothChanged_ShareOneClause()
        {
            string both = StatusText.ForPlanStaleInputs(true, true);

            Assert.Equal("Settings and account data changed", both);
            Assert.Equal(1, CountOccurrences(both, "changed"));
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                 i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        [Fact]
        public void PlanAccountDataMoved_OnlyWhenTheLiveStampIsStrictlyNewer()
        {
            var solved = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

            Assert.True(StatusText.PlanAccountDataMoved(solved, solved.AddSeconds(1)));
            Assert.False(StatusText.PlanAccountDataMoved(solved, solved));

            // An older live stamp is Clear Cache restoring an earlier
            // capture, not the plan falling behind one.
            Assert.False(StatusText.PlanAccountDataMoved(solved, solved.AddMinutes(-5)));
        }

        /// <summary>
        /// A restored plan and a plan solved with Use Own Materials off
        /// both carry no stamp, and neither can be told it is behind data
        /// it never read.
        /// </summary>
        [Fact]
        public void PlanAccountDataMoved_WithNoStampOnEitherSide_IsFalse()
        {
            var stamp = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

            Assert.False(StatusText.PlanAccountDataMoved(null, stamp));
            Assert.False(StatusText.PlanAccountDataMoved(stamp, null));
            Assert.False(StatusText.PlanAccountDataMoved(null, null));
        }

        // ForAgeAgo rides the SAME ladder and the same framing - the Plan
        // History detail panel's cost-delta line - and differs only below a
        // minute.
        [Theory]
        [InlineData(0.5, "just now")]
        [InlineData(1, "1m ago")]
        [InlineData(65, "1h 5m ago")]
        [InlineData(2880, "2d ago")]
        [InlineData(43200, "1mo ago")]
        public void ForAgeAgo_BucketLadder(double minutes, string expected)
        {
            Assert.Equal(expected, StatusText.ForAgeAgo(TimeSpan.FromMinutes(minutes)));
        }

        [Fact]
        public void ForAgeAgo_Negative_ClampedToZero()
        {
            Assert.Equal("just now", StatusText.ForAgeAgo(TimeSpan.FromSeconds(-5)));
        }

        // ForAgeAgoInWords is the same ladder spelled out, for the two
        // places a reader meets an age in prose rather than in a band: the
        // stale-account-data dialog and its Log tab line.
        [Theory]
        [InlineData(0.5, "just now")]
        [InlineData(1, "1 minute ago")]
        [InlineData(14, "14 minutes ago")]
        [InlineData(59, "59 minutes ago")]
        [InlineData(60, "1 hour ago")]
        [InlineData(192, "3 hours ago")]
        [InlineData(1440, "1 day ago")]
        [InlineData(2880, "2 days ago")]
        [InlineData(43200, "1 month ago")]
        [InlineData(172800, "4 months ago")]
        public void ForAgeAgoInWords_SpellsTheCoarsestUnitOnly(double minutes, string expected)
        {
            Assert.Equal(expected, StatusText.ForAgeAgoInWords(TimeSpan.FromMinutes(minutes)));
        }

        [Fact]
        public void ForAgeAgoInWords_Negative_ClampedToZero()
        {
            Assert.Equal("just now", StatusText.ForAgeAgoInWords(TimeSpan.FromSeconds(-5)));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(59)]
        [InlineData(60)]
        [InlineData(1440)]
        [InlineData(43200)]
        [InlineData(525600)]
        public void ForSnapshotAgeSuffixAndForAgeAgo_AgreeAboveAMinute(double minutes)
        {
            var age = TimeSpan.FromMinutes(minutes);
            Assert.Equal(StatusText.ForAgeAgo(age), StatusText.ForSnapshotAgeSuffix(age));
        }

        [Fact]
        public void RankerProgress_NamesTheItemAndItsPlaceInTheRun()
        {
            Assert.Equal(
                "Analyzing 3 of 12 - Gift of the Pact Marshal",
                StatusText.ForRankerProgress(3, 12, "Gift of the Pact Marshal", false));

            // A missing name leaves the count standing rather than a
            // dangling separator.
            Assert.Equal("Analyzing 3 of 12", StatusText.ForRankerProgress(3, 12, null, false));
            Assert.Equal("Analyzing 3 of 12", StatusText.ForRankerProgress(3, 12, "", false));
        }

        [Fact]
        public void RankerProgress_FirstRun_SaysSoWithoutOverrunningTheStatusBand()
        {
            string first = StatusText.ForRankerProgress(3, 12, "Gift of the Pact Marshal", true);

            Assert.Equal(
                "Analyzing 3 of 12 - Gift of the Pact Marshal (first run is slower)", first);

            // The sentence this replaced ran the line to 122 characters,
            // which ellipsized on every item and left the full text only on
            // a tooltip over a line that kept changing.
            Assert.True(
                first.Length <= StatusText.RankerStatusBudgetChars,
                $"{first.Length} characters, budget {StatusText.RankerStatusBudgetChars}");

            // The item's own name is the only unbounded term, so the line
            // fits for every name up to the room the rest of it leaves.
            Assert.True(
                StatusText.ForRankerProgress(3, 12, new string('x', 30), true).Length
                    <= StatusText.RankerStatusBudgetChars);
        }

        [Fact]
        public void ForSnapshotDetail_JoinsTheAgeAndTheHoleAndDropsTheHoleWhenThereIsNone()
        {
            Assert.Equal(
                "just captured, incomplete for 2 of 9 characters",
                StatusText.ForSnapshotDetail(TimeSpan.Zero, 2, 9));
            Assert.Equal(
                "2d ago, incomplete for 2 of 9 characters",
                StatusText.ForSnapshotDetail(TimeSpan.FromDays(2), 2, 9));

            // No hole, no clause - the age stands on its own exactly as it
            // did before a snapshot counted what it could not read.
            Assert.Equal("just captured", StatusText.ForSnapshotDetail(TimeSpan.Zero, 0, 9));
            Assert.Equal("2d ago", StatusText.ForSnapshotDetail(TimeSpan.FromDays(2), 0, 9));
        }

        [Fact]
        public void ForSnapshotAgeSuffix_IsLongestWhenTheSnapshotIsFresh()
        {
            // The sub-minute case is the LONGEST branch, not the shortest.
            // A worst-case line width taken from an AGED snapshot therefore
            // understates the real worst case, which is a snapshot captured
            // seconds ago that is missing a character - the reported fault's
            // own condition.
            string fresh = StatusText.ForSnapshotAgeSuffix(TimeSpan.Zero);
            Assert.Equal("just captured", fresh);

            double[] ages = { 1, 37, 59, 60, 719, 1439, 1440, 43199, 43200, 525600, 5256000 };
            foreach (double minutes in ages)
            {
                string aged = StatusText.ForSnapshotAgeSuffix(TimeSpan.FromMinutes(minutes));
                Assert.True(aged.Length <= fresh.Length, $"{minutes}m reads {aged}");
            }
        }

        [Fact]
        public void TheRankerStatusLine_AtItsWidest_StaysInsideTheStatusBand()
        {
            // Every term at its widest at once: the longest timestamp
            // TimestampFormat can print, the longest age suffix, and the
            // largest character counts a GW2 account reaches. This is the
            // line the band has to hold, and the one a "37m ago" worst case
            // missed by ten characters.
            string widest = StatusText.Stamp("Analyzed", new DateTime(2026, 5, 20, 10, 0, 0))
                + " (" + StatusText.ForSnapshotDetail(TimeSpan.Zero, 20, 20) + ")";

            Assert.Equal(
                "Analyzed - May 20, 2026 10:00 AM "
                    + "(just captured, incomplete for 20 of 20 characters)",
                widest);
            Assert.True(
                widest.Length <= StatusText.RankerStatusBudgetChars,
                $"{widest.Length} characters, budget {StatusText.RankerStatusBudgetChars}");
        }
    }
}
