using System;
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
        /// SELECT every account data source throws an invalid-token
        /// exception, and the bare status line gave no hint why). Callers
        /// pass the result to <see cref="Stamp"/> as the verb, so the
        /// Unknown case still reads exactly like every other status line
        /// (nothing more specific to say once no known pattern matched).
        /// ApiAccessNotReady also drives Views/MainView.cs's walkthrough
        /// dialog, but still gets its own status text here so the header
        /// label reads correctly once that dialog is closed.
        /// <para>
        /// The cause clause is introduced by a COLON, not a dash:
        /// <see cref="StampSeparator"/> owns the dash, and a line carrying
        /// both ("Refresh failed - could not reach the GW2 API - Aug 15,
        /// 2026 3:41 PM") gave two unrelated clauses the same separator and
        /// no way to tell which was which.
        /// </para>
        /// </summary>
        public static string ForRefreshFailure(SnapshotFailureKind kind, int failedSourceCount, int totalSourceCount)
        {
            switch (kind)
            {
                case SnapshotFailureKind.ApiAccessNotReady:
                    return "Refresh failed: GW2 API access not ready";
                case SnapshotFailureKind.NetworkOrApiDown:
                    return "Refresh failed: could not reach the GW2 API";
                case SnapshotFailureKind.PartialFailure:
                    return $"Refresh partially failed: {failedSourceCount} of {totalSourceCount} sources";
                default:
                    return "Refresh failed";
            }
        }
    }
}
