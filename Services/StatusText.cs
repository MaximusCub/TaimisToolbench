using System;
using System.Collections.Generic;
using System.Globalization;

namespace TaimisToolbench.Services
{
    internal static class StatusText
    {
        /// <summary>
        /// A status read back from disk, ready to display. StatusStore keeps
        /// status.txt exactly as it was written, so a status saved by a build
        /// older than the hyphen separator still carries the em dash one; that
        /// is swapped here on the way to the screen. The file itself is left
        /// alone and the next status write replaces it.
        /// </summary>
        public static string Normalize(string status)
        {
            return status == null ? "" : status.Replace(LegacyStampSeparator, StampSeparator);
        }

        /// <summary>
        /// The one timestamp format every user-facing status line uses.
        /// Invariant culture for the reason LogLineFormat records: the
        /// module's strings are English-only, and "h:mm tt" yields an EMPTY
        /// AM/PM designator under some cultures.
        /// </summary>
        public const string TimestampFormat = "MMM d, yyyy h:mm tt";

        /// <summary>
        /// The one separator between a status verb and its timestamp:
        /// "Updated - Sep 6, 2026 4:28 PM". Four sites wrote this by hand
        /// before it existed, so keep new sites calling
        /// <see cref="Stamp"/> rather than spelling it out again.
        /// </summary>
        public const string StampSeparator = " - ";

        /// <summary>
        /// The separator builds before this one wrote. Only
        /// <see cref="Normalize"/> reads it, to display a status.txt saved
        /// by such a build.
        /// </summary>
        private const string LegacyStampSeparator = " \u2014 ";

        /// <summary>
        /// The shape of every timestamped status line in the module:
        /// "&lt;Sentence case verb&gt; - &lt;timestamp&gt;", one separator,
        /// one timestamp format. Four sites wrote this by hand with two
        /// different separators (Views/MainView's three snapshot lines,
        /// SettingsTabContent's "Saved", CraftingPlanView's "Plan
        /// generated"); they now all call here, so no future site can
        /// invent a fifth spelling.
        /// <para>
        /// The verb is written by the caller and is expected to be sentence
        /// case; a blank verb yields the bare timestamp rather than a
        /// dangling separator.
        /// </para>
        /// </summary>
        public static string Stamp(string verb, DateTime when)
        {
            string trimmedVerb = verb == null ? "" : verb.Trim();
            string timestamp = when.ToString(TimestampFormat, CultureInfo.InvariantCulture);
            return trimmedVerb.Length == 0 ? timestamp : trimmedVerb + StampSeparator + timestamp;
        }

        /// <summary>
        /// A counted noun, always correctly pluralized: "1 override",
        /// "3 overrides", "0 overrides". The module's one spelling of a
        /// count, so no user-facing string reaches for "(s)" - which is a
        /// developer's shorthand leaking into the interface, and which the
        /// module wrote in exactly one place before this existed.
        /// <para>
        /// A plural that is not the singular plus "s" is passed
        /// explicitly; the default covers every count this module shows.
        /// </para>
        /// </summary>
        public static string Count(int n, string singular, string plural = null)
        {
            return n + " " + (n == 1 ? singular : plural ?? singular + "s");
        }

        /// <summary>
        /// Characters the Crafting Ranker's status line may run to before
        /// it ellipsizes. MEASURED on screen at the module's 1378px window
        /// minimum, from where the ellipsizer cut a line too long for it:
        /// RankerRowLayout.Toolbar leaves the status band at least 779 and
        /// under 786 logical pixels once the Analyze button, the two
        /// display toggles and the spinner have taken theirs. 86 characters
        /// of a status line of this letter mix ink under 779; 87 ink over
        /// 785. The band gains a pixel for every pixel the window gains, so
        /// this is its floor.
        /// <para>
        /// A character count is a proxy for a pixel width and holds only
        /// for lines of roughly this mix. Blish's ContentService sets
        /// LetterSpacing to -1 on every font it loads, which is worth about
        /// a pixel per character, so a budget derived from raw glyph
        /// advances overstates an 80-character line by 80 pixels.
        /// </para>
        /// </summary>
        public const int RankerStatusBudgetChars = 86;

        /// <summary>
        /// Characters the Crafting Plan's status line may run to. Its band
        /// is 1206 logical pixels at the same window floor -
        /// TopRegionLayoutMath.StatusBandWidth derives that from the shipped
        /// constants and TopRegionLayoutMathTests pins it - and the two
        /// bands draw the same face, so this is
        /// <see cref="RankerStatusBudgetChars"/>' own measured rate carried
        /// across: 86 characters in 779 pixels is 9.06 a character, and 1206
        /// buys 133 of them.
        /// <para>
        /// The line is written short enough to fit rather than shortened
        /// after the fact. PlanStatusLineTests builds the widest line the
        /// strip can compose through the real composition and holds it
        /// under this number.
        /// </para>
        /// </summary>
        public const int PlanStatusBudgetChars = 133;

        /// <summary>
        /// The Crafting Ranker's per-item progress line: which item of how
        /// many is being solved, and on the first run of a session that
        /// this run is the slow one.
        /// <para>
        /// The first run downloads recipe data, and that used to be spelled
        /// out here in a full sentence. MEASURED: with a legendary's name
        /// in it the line was 122 characters and 1190px, well past the
        /// band, so it ellipsized on every item and the reader had to hover
        /// a line that kept moving. The mechanism is on the Analyze
        /// button's own tooltip; this line carries only what a waiting
        /// player acts on.
        /// </para>
        /// </summary>
        public static string ForRankerProgress(int position, int total, string name, bool firstRun)
        {
            string line = "Analyzing " + position + " of " + total;
            if (!string.IsNullOrEmpty(name))
            {
                line += " - " + name;
            }

            return firstRun ? line + " (first run is slower)" : line;
        }

        /// <summary>
        /// The re-solve status line for
        /// TreeSectionController.ApplyOverridesAndResolve.
        /// <para>
        /// It reports the EVENT and nothing else - never the standing override
        /// count, which is plan STATE and lives in the top strip's Overrides
        /// chip (see StatusText.ForOverridesChip). Why the two must not share
        /// one line: docs/ARCHITECTURE.md, S2.8.
        /// </para>
        /// <para>
        /// "Best path restored" is the Best Path preset's own label and must
        /// only be written when that preset is the trigger - never inferred
        /// from a zero override count, which every other trigger (Clear
        /// Overrides, a per-node pill, the ignore toggle) can also produce.
        /// </para>
        /// </summary>
        public static string ForOverrideResolve(bool isBestPathPreset)
        {
            return isBestPathPreset ? "Best path restored" : "Plan updated";
        }

        /// <summary>
        /// The top strip's two per-plan STATE chips. A labeled count, not
        /// a sentence: it is a gauge the reader glances at, and
        /// "Overrides: 1" beside "Ignored: 3" reads as one instrument
        /// panel where "1 override" beside "3 items" reads as prose that
        /// forgot to be a sentence. Both are hidden entirely at zero, so
        /// neither ever renders the count these two format worst.
        /// </summary>
        public static string ForOverridesChip(int overrideCount)
        {
            return "Overrides: " + overrideCount;
        }

        public static string ForIgnoredChip(int ignoredCount)
        {
            return "Ignored: " + ignoredCount;
        }

        // The three lines a click that would change nothing writes instead
        // of silently doing nothing. A dialog that protects nothing trains
        // people to click through dialogs, and a dead click with no
        // feedback trains them to click again harder; the click is skipped
        // AND the re-solve is skipped, and the strip says why.
        public const string NoOverridesToClear = "No decision overrides to clear";
        public const string AlreadyCraftingEverything = "Already crafting everything craftable";
        public const string AlreadyBuyingEverything = "Already buying everything buyable";

        /// <summary>
        /// The fourth, and a different KIND: the three above say the click
        /// was unnecessary, this one says it was impossible. A plan
        /// restored without its solve context renders, and its toolbar
        /// shows, but nothing local can be re-solved on it - so every
        /// decision pill, both presets and both chip clears land here.
        /// <para>
        /// Deliberately not one of the "already ..." lines. Those assert
        /// something about the plan's contents, which is exactly what
        /// cannot be known in this state, and asserting it anyway is the
        /// one failure mode a status line must not have.
        /// </para>
        /// </summary>
        public const string ReSolveUnavailable =
            "This plan cannot be changed - Generate Plan to rebuild it";

        /// <summary>
        /// The two failure verbs, deliberately different. A failed
        /// GENERATION leaves the tab with the plan it had (or none); a
        /// failed local re-solve leaves the plan on screen intact and only
        /// the change unapplied. "Error:" said neither.
        /// </summary>
        public static string ForGenerationFailure(string message)
        {
            return "Generation failed: " + (message ?? "");
        }

        public static string ForUpdateFailure(string message)
        {
            return "Update failed: " + (message ?? "");
        }

        // A month, flat: TimeSpan carries no calendar, and an age this
        // deep reads as "long dead" rather than a figure counted back from.
        private const int AgeDaysPerMonth = 30;

        /// <summary>
        /// How much time an age is, with no "ago" framing - the bucket
        /// ladder behind <see cref="ForAgeAgo"/>. Each bucket names the
        /// coarsest unit reached plus at most one finer term. The caller
        /// handles the sub-minute case; below a minute this reports "0m".
        /// </summary>
        private static string AgeMagnitude(TimeSpan age)
        {
            if (age.TotalHours < 1)
            {
                return $"{(int)age.TotalMinutes}m";
            }

            if (age.TotalDays < 1)
            {
                return $"{(int)age.TotalHours}h {age.Minutes}m";
            }

            if (age.TotalDays < AgeDaysPerMonth)
            {
                return $"{(int)age.TotalDays}d";
            }

            return $"{(int)(age.TotalDays / AgeDaysPerMonth)}mo";
        }

        /// <summary>
        /// The Snapshot header's age suffix: how long ago the snapshot on
        /// screen was captured, in <see cref="ForAgeAgo"/>'s framing and
        /// over its ladder. The caller punctuates it apart from the refresh
        /// timestamp it follows - Views/MainView.cs parenthesises it.
        /// <para>
        /// Sub-minute reads "just captured", not ForAgeAgo's "just now":
        /// the line pairs two moments - when the last refresh ATTEMPT
        /// happened and how old the snapshot is - and "just now" straight
        /// after an absolute timestamp reads as a restatement of that same
        /// instant rather than a second fact about a different one.
        /// </para>
        /// <para>
        /// A negative age (CapturedAt momentarily ahead of the local clock -
        /// e.g. minor clock skew right after a fetch) is treated as zero
        /// rather than shown as a negative duration.
        /// </para>
        /// </summary>
        public static string ForSnapshotAgeSuffix(TimeSpan age)
        {
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            return age.TotalMinutes < 1 ? "just captured" : ForAgeAgo(age);
        }

        /// <summary>
        /// A relative age - "5m ago", "3h 12m ago", "2d ago", "4mo ago" -
        /// over <see cref="AgeMagnitude"/>'s ladder. The module's one
        /// elapsed-time wording; <see cref="ForSnapshotAgeSuffix"/> is this
        /// with a different sub-minute case. Sub-minute reads "just now"; a
        /// negative age (clock skew) is treated as zero.
        /// </summary>
        public static string ForAgeAgo(TimeSpan age)
        {
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            if (age.TotalMinutes < 1)
            {
                return "just now";
            }

            return AgeMagnitude(age) + " ago";
        }

        /// <summary>
        /// The same age spelled out - "14 minutes ago", "3 hours ago",
        /// "2 days ago", "4 months ago". For prose a reader meets once: the
        /// stale-account-data dialog and its Log tab line.
        /// <para>
        /// <see cref="ForAgeAgo"/>'s "14m ago" is written for a status band
        /// that is already short of room and is read at a glance. Neither of
        /// these two is, and both have the width.
        /// </para>
        /// <para>
        /// Only the coarsest unit is named, so "3h 12m ago" becomes "3 hours
        /// ago". The finer term buys a reader deciding whether to regenerate
        /// nothing, and it costs the sentence its rhythm.
        /// </para>
        /// </summary>
        public static string ForAgeAgoInWords(TimeSpan age)
        {
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            if (age.TotalMinutes < 1)
            {
                return "just now";
            }

            if (age.TotalHours < 1)
            {
                return Count((int)age.TotalMinutes, "minute") + " ago";
            }

            if (age.TotalDays < 1)
            {
                return Count((int)age.TotalHours, "hour") + " ago";
            }

            if (age.TotalDays < AgeDaysPerMonth)
            {
                return Count((int)age.TotalDays, "day") + " ago";
            }

            return Count((int)(age.TotalDays / AgeDaysPerMonth), "month") + " ago";
        }

        /// <summary>
        /// How much of the account's character data a snapshot is missing,
        /// or null when it is missing none. A character counts when its
        /// bags, its equipment or its disciplines failed to fetch, so its
        /// holdings are absent and the plan can tell the user to buy an item
        /// their own bags hold.
        /// <para>
        /// The noun agrees with the total, so a one-character account reads
        /// "1 of 1 character" rather than "1 of 1 characters".
        /// </para>
        /// </summary>
        public static string ForIncompleteCharacters(int incompleteCharacters, int characterCount)
        {
            if (!HasIncompleteCharacters(incompleteCharacters, characterCount))
            {
                return null;
            }

            int incomplete = Math.Min(incompleteCharacters, characterCount);
            return "incomplete for " + incomplete + " of " + Count(characterCount, "character");
        }

        /// <summary>
        /// Whether a snapshot failed to read part of what a character was
        /// holding - the condition <see cref="ForIncompleteCharacters"/>
        /// writes its clause for. Views/MainView.cs reads it on its own to
        /// decide the Snapshot tab's amber recolor, which is a second
        /// consequence of the same fact rather than a second test of it.
        /// </summary>
        public static bool HasIncompleteCharacters(int incompleteCharacters, int characterCount)
        {
            return incompleteCharacters > 0 && characterCount > 0;
        }

        /// <summary>
        /// The parenthesised detail that follows a snapshot-backed
        /// timestamp: how old the data on screen is, then what the fetch
        /// could not read in full. The Snapshot tab and the Crafting Ranker
        /// both show exactly these two facts, so the join lives here rather
        /// than in each view.
        /// <para>
        /// Age first, because it is the fact the reader is already looking
        /// for beside a timestamp. Both halves are widest at once when a
        /// FRESH snapshot is missing a character: sub-minute reads "just
        /// captured", which is the longest string
        /// <see cref="ForSnapshotAgeSuffix"/> has. That combination is the
        /// reported fault's own condition, so it is also the line to size
        /// the band against - see <see cref="RankerStatusBudgetChars"/>.
        /// </para>
        /// </summary>
        public static string ForSnapshotDetail(
            TimeSpan age, int incompleteCharacters, int characterCount)
        {
            string detail = ForSnapshotAgeSuffix(age);
            string incomplete = ForIncompleteCharacters(incompleteCharacters, characterCount);
            return incomplete == null ? detail : detail + ", " + incomplete;
        }

        /// <summary>
        /// The Crafting Plan status line's account-data clause, or null when
        /// there is nothing to say about the snapshot the plan subtracted
        /// owned materials from. It reports two faults: data older than
        /// <paramref name="staleThreshold"/>, and characters the fetch could
        /// not read in full.
        /// <para>
        /// The age half is gated on the same threshold as the Snapshot tab's
        /// recolor and Module.Update()'s auto-refresh, so the three cannot
        /// disagree. The incomplete half has no threshold: a snapshot taken
        /// ten seconds ago with a character missing is exactly the fault
        /// that makes a plan recommend buying an owned item. It is a flag
        /// and not a count - four clauses compete for
        /// <see cref="PlanStatusBudgetChars"/>, and the Snapshot tab names
        /// the count through <see cref="ForSnapshotDetail"/>. The clause names
        /// account data rather than following the Crafting Ranker's bare
        /// "(37m ago)", which after a "Plan generated" timestamp would read
        /// as restating it.
        /// </para>
        /// </summary>
        public static string ForPlanAccountDataNote(
            TimeSpan age, TimeSpan staleThreshold, int incompleteCharacters, int characterCount)
        {
            if (age < TimeSpan.Zero)
            {
                age = TimeSpan.Zero;
            }

            var parts = new List<string>(2);
            if (IsStale(age, staleThreshold))
            {
                parts.Add(AgeMagnitude(age) + " old");
            }

            if (HasIncompleteCharacters(incompleteCharacters, characterCount))
            {
                parts.Add("incomplete");
            }

            return parts.Count == 0 ? null : "account data " + string.Join(", ", parts);
        }

        /// <summary>
        /// Whether the account snapshot has moved since the plan on screen
        /// was solved. True only when both stamps are known and the live
        /// one is strictly newer.
        /// <para>
        /// A plan with no stamp of its own never reports moved data. Two
        /// plans have none: one restored from disk, which no longer knows
        /// what it was solved against, and one solved with Use Own
        /// Materials off, which read no holdings and so cannot be
        /// superseded by new ones.
        /// </para>
        /// </summary>
        public static bool PlanAccountDataMoved(
            DateTime? planCapturedAtUtc, DateTime? currentCapturedAtUtc)
        {
            if (planCapturedAtUtc == null || currentCapturedAtUtc == null)
            {
                return false;
            }

            return currentCapturedAtUtc.Value > planCapturedAtUtc.Value;
        }

        /// <summary>
        /// The Crafting Plan strip's standing notice: what has changed
        /// since this plan was solved that the next Generate would pick up.
        /// Null when nothing has.
        /// <para>
        /// The remedy is not spelled out. The Generate Plan button sits on
        /// this same strip, two rows above the line, so "Generate Plan to
        /// apply" spent 22 of the band's 133 characters
        /// (<see cref="PlanStatusBudgetChars"/>) restating the control the
        /// reader is already looking at.
        /// </para>
        /// <para>
        /// Deliberately not merged with
        /// <see cref="ForPlanAccountDataNote"/>. That clause is frozen into
        /// the completion text and describes the data the plan USED; this
        /// one is standing state and reports data that arrived AFTER it.
        /// </para>
        /// </summary>
        public static string ForPlanStaleInputs(bool settingsChanged, bool accountDataChanged)
        {
            if (!settingsChanged && !accountDataChanged)
            {
                return null;
            }

            string subject;
            if (settingsChanged && accountDataChanged)
            {
                subject = "Settings and account data";
            }
            else if (settingsChanged)
            {
                subject = "Settings";
            }
            else
            {
                subject = "Account data";
            }

            return subject + " changed";
        }

        /// <summary>
        /// Whether a snapshot of the given age counts as stale against the
        /// caller-supplied threshold. The Snapshot tab's staleness recolor
        /// (Views/MainView.cs) and Module.Update()'s auto-refresh gate both
        /// derive their threshold from
        /// ModuleSettings.GetClampedSnapshotRefreshIntervalMinutes, so the
        /// warning color and the auto-refresh can never disagree about
        /// which snapshots are stale.
        /// </summary>
        public static bool IsStale(TimeSpan age, TimeSpan staleThreshold)
        {
            return age >= staleThreshold;
        }

        /// <summary>
        /// Cause text for a failed Refresh Now (Views/MainView.cs), keyed
        /// by SnapshotFailureClassifier's classification - the fix,
        /// measured in game, for the "Refresh Failed" dead end (at CHARACTER
        /// SELECT every source throws an invalid-token exception, and the
        /// bare status line gave no hint why). Callers pass the result to
        /// <see cref="Stamp"/> as the verb, so the Unknown case still reads
        /// like every other status line.
        /// ApiAccessNotReady also drives Views/MainView.cs's walkthrough
        /// dialog, but keeps its own status text so the header label reads
        /// correctly once that dialog is closed.
        /// <para>
        /// The cause clause is introduced by a COLON, not a dash:
        /// <see cref="StampSeparator"/> owns the dash, and a line carrying
        /// both ("Refresh failed - could not reach the GW2 API - Aug 15,
        /// 2026 3:41 PM") gave two clauses one separator. Every clause says
        /// FAILED, the partial one included: a fetch that could not read
        /// everything commits nothing.
        /// </para>
        /// </summary>
        public static string ForRefreshFailure(SnapshotFailureClassification classification)
        {
            if (classification == null)
            {
                return "Refresh failed";
            }

            switch (classification.Kind)
            {
                case SnapshotFailureKind.ApiAccessNotReady:
                    return "Refresh failed: GW2 API access not ready";
                case SnapshotFailureKind.NetworkOrApiDown:
                    return "Refresh failed: could not reach the GW2 API";
                case SnapshotFailureKind.PartialFailure:
                    return "Refresh failed: " + classification.FailedSourceCount
                        + " of " + classification.TotalSourceCount + " sources unavailable";
                case SnapshotFailureKind.IncompleteCharacters:
                    return "Refresh failed: could not read "
                        + Count(classification.IncompleteCharacterCount, "character") + " in full";
                default:
                    return "Refresh failed";
            }
        }
    }
}
