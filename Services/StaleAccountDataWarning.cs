using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What a plan takes from each part of the account, and therefore
    /// whether a failed refresh is worth interrupting the user over.
    /// <para>
    /// The honest limit, which every string below respects: this can tell
    /// that a plan READS a source. It cannot tell whether refreshing that
    /// source would have CHANGED the plan, because knowing that needs the
    /// read that just failed. A second limit is narrower and matters when
    /// reading the item rules: a source counts as holding an item when the
    /// last successful capture recorded it there, so an item the account
    /// acquired since is invisible here.
    /// </para>
    /// <para>The reads, established from the pipeline: see
    /// docs/ARCHITECTURE.md section 10a.</para>
    /// </summary>
    internal static class StaleAccountDataWarning
    {
        /// <summary>How many item names a message spells out before it counts the rest.</summary>
        private const int NamedItemLimit = 3;

        /// <summary>
        /// The failed sources this plan depends on, or null when it depends
        /// on none of them and the user should not be interrupted.
        /// </summary>
        /// <param name="failedSources">
        /// Which reads failed. The caller names every source when a refresh
        /// failed without saying which read did, so an empty list here
        /// alongside no incomplete character means nothing failed.
        /// </param>
        /// <param name="planUsedHoldings">
        /// Whether this plan subtracted what the account owns. False when
        /// the user turned Use Own Materials off, and the plan then reads
        /// no holding at all.
        /// </param>
        public static StaleAccountDataNotice Evaluate(
            IReadOnlyList<AccountDataSource> failedSources,
            IReadOnlyList<string> incompleteCharacterNames,
            AccountSnapshot snapshot,
            CraftingPlanResult result,
            bool planUsedHoldings,
            DateTime utcNow)
        {
            if (snapshot == null || result == null)
            {
                return null;
            }

            var unread = UnreadSources(failedSources, incompleteCharacterNames);
            if (unread.Count == 0)
            {
                return null;
            }

            var planItemIds = new HashSet<int>(PlanItemIds.ForResult(result));
            AddVendorItemCostIds(result, planItemIds);
            AddUsedMaterialIds(result, planItemIds);

            bool readsCurrencies = ReadsCurrencies(result);
            bool readsDisciplines = result.RequiredDisciplines != null && result.RequiredDisciplines.Count > 0;

            var depended = new List<AccountDataSource>();
            var itemNames = new List<string>();
            var namedIds = new HashSet<int>();
            bool affectsCurrencies = false;
            bool affectsDisciplines = false;

            foreach (var source in AccountDataSources.All)
            {
                if (!unread.Contains(source))
                {
                    continue;
                }

                bool depends = false;

                if (source == AccountDataSource.Wallet)
                {
                    depends = readsCurrencies;
                    affectsCurrencies |= depends;
                }

                if (source == AccountDataSource.Characters && readsDisciplines)
                {
                    depends = true;
                    affectsDisciplines = true;
                }

                if (planUsedHoldings)
                {
                    var held = HeldPlanItems(snapshot, source, planItemIds);
                    if (held.Count > 0)
                    {
                        depends = true;
                        foreach (var entry in held)
                        {
                            if (!string.IsNullOrEmpty(entry.Name) && namedIds.Add(entry.ItemId))
                            {
                                itemNames.Add(entry.Name);
                            }
                        }
                    }
                }

                if (depends)
                {
                    depended.Add(source);
                }
            }

            if (depended.Count == 0)
            {
                return null;
            }

            var age = utcNow - snapshot.CapturedAt;
            return new StaleAccountDataNotice(
                depended,
                itemNames,
                affectsCurrencies,
                affectsDisciplines,
                age < TimeSpan.Zero ? TimeSpan.Zero : age);
        }

        /// <summary>
        /// Longest message <see cref="Compose"/> can return, in characters.
        /// A ceiling with room to spare over the worst shape, not a target:
        /// it exists so a sentence added here fails a test instead of
        /// reaching a player as a wall of prose. The message this replaced
        /// ran to six sentences.
        /// </summary>
        public const int MaxMessageLength = 200;

        /// <summary>
        /// Beyond this many unread sources the message stops naming them
        /// and says how much of the account went unread instead. A list of
        /// three lower-case labels inside a sentence reads as a fragment,
        /// and every label is in the log line either way.
        /// </summary>
        private const int NamedSourceLimit = 2;

        /// <summary>
        /// The dialog's text: what happened, and what it means for the
        /// plan on screen. Two sentences, no ids, and no claim that the
        /// plan is either wrong or fine - "may have changed" is the whole
        /// of what this can honestly say, because knowing more needs the
        /// read that just failed.
        /// <para>
        /// Everything a longer message would add - which items the unread
        /// sources held, the currency and discipline dependencies - goes to
        /// <see cref="ComposeLogDetail"/> and the Log tab, where a reader
        /// who wants it can go and find it.
        /// </para>
        /// </summary>
        public static string Compose(StaleAccountDataNotice notice)
        {
            if (notice == null)
            {
                return null;
            }

            return "Could not refresh your account, so this plan used data from "
                + StatusText.ForAgeAgoInWords(notice.Age) + ". "
                + SecondSentence(notice.Sources);
        }

        /// <summary>
        /// What the plan may be missing, named where naming it is short
        /// enough to read. The closer is the one part that is always true
        /// and always said: a refresh that did not happen cannot be
        /// reported as having changed nothing.
        /// <para>
        /// A notice with no source at all is defensive -
        /// <see cref="Evaluate"/> returns null rather than produce one -
        /// and the closer then stands as the whole sentence.
        /// </para>
        /// </summary>
        private static string SecondSentence(IReadOnlyList<AccountDataSource> sources)
        {
            const string Closer = "what you own may have changed since.";

            if (sources == null || sources.Count == 0)
            {
                return char.ToUpperInvariant(Closer[0]) + Closer.Substring(1);
            }

            string subject = sources.Count > NamedSourceLimit
                ? "Several parts of your account"
                : "Your " + string.Join(" and ", sources.Select(AccountDataSources.Label));

            return subject + " could not be read, and " + Closer;
        }

        /// <summary>
        /// The whole finding, for the Log tab: every unread source the plan
        /// depends on, the plan items those sources held, and whether the
        /// plan reads currencies or disciplines. This is the detail the
        /// dialog deliberately does not carry.
        /// </summary>
        public static string ComposeLogDetail(StaleAccountDataNotice notice)
        {
            if (notice == null)
            {
                return null;
            }

            var parts = new List<string>
            {
                "Refresh failed before a plan solved against data "
                    + StatusText.ForAgeAgoInWords(notice.Age) + ".",
                "Unread and used by the plan: " + JoinLabels(notice.Sources) + ".",
            };

            if (notice.ItemNames.Count > 0)
            {
                parts.Add("Plan items held there: " + JoinItems(notice.ItemNames) + ".");
            }

            if (notice.AffectsCurrencyAmounts)
            {
                parts.Add("The plan spends currencies the wallet holds.");
            }

            if (notice.AffectsCraftingDisciplines)
            {
                parts.Add("The plan crafts, so character disciplines decide what it can make.");
            }

            return string.Join(" ", parts);
        }

        private static HashSet<AccountDataSource> UnreadSources(
            IReadOnlyList<AccountDataSource> failedSources,
            IReadOnlyList<string> incompleteCharacterNames)
        {
            var unread = new HashSet<AccountDataSource>();

            if (failedSources != null)
            {
                foreach (var source in failedSources)
                {
                    unread.Add(source);
                }
            }

            if (incompleteCharacterNames != null && incompleteCharacterNames.Count > 0)
            {
                unread.Add(AccountDataSource.Characters);
            }

            return unread;
        }

        /// <summary>
        /// Plan items this source held at the last successful capture. The
        /// snapshot's flat Items list is the only record of which part of
        /// the account holds what, so this is a scan rather than a lookup;
        /// it runs once per failed source, per Generate.
        /// </summary>
        private static List<SnapshotItemEntry> HeldPlanItems(
            AccountSnapshot snapshot, AccountDataSource source, HashSet<int> planItemIds)
        {
            var held = new List<SnapshotItemEntry>();
            if (snapshot.Items == null)
            {
                return held;
            }

            foreach (var entry in snapshot.Items)
            {
                if (entry == null || entry.Count <= 0 || !planItemIds.Contains(entry.ItemId))
                {
                    continue;
                }

                if (AccountDataSources.ForItemSource(entry.Source) == source)
                {
                    held.Add(entry);
                }
            }

            return held;
        }

        /// <summary>
        /// Whether the plan shows what the wallet holds. Coin is not part
        /// of this: the solver folds coin costs into the plan's total and
        /// never asks the wallet how much the account has.
        /// </summary>
        private static bool ReadsCurrencies(CraftingPlanResult result)
        {
            if (result.Plan?.CurrencyCosts != null && result.Plan.CurrencyCosts.Count > 0)
            {
                return true;
            }

            return result.OwnedCurrencyAmounts != null && result.OwnedCurrencyAmounts.Count > 0;
        }

        /// <summary>
        /// Items the reduction drew out of the account. Added because the
        /// reducer clears the sub-recipes of an ingredient the account
        /// covers in full, so a plan can consume an item whose branch the
        /// solved tree no longer carries.
        /// </summary>
        private static void AddUsedMaterialIds(CraftingPlanResult result, HashSet<int> planItemIds)
        {
            if (result.UsedMaterials == null)
            {
                return;
            }

            foreach (var used in result.UsedMaterials)
            {
                if (used != null && used.ItemId > 0)
                {
                    planItemIds.Add(used.ItemId);
                }
            }
        }

        private static void AddVendorItemCostIds(CraftingPlanResult result, HashSet<int> planItemIds)
        {
            if (result.OwnedVendorItemAmounts == null)
            {
                return;
            }

            foreach (var id in result.OwnedVendorItemAmounts.Keys)
            {
                planItemIds.Add(id);
            }
        }

        private static string JoinLabels(IReadOnlyList<AccountDataSource> sources)
        {
            return string.Join(", ", sources.Select(AccountDataSources.Label));
        }

        private static string JoinItems(IReadOnlyList<string> names)
        {
            if (names.Count <= NamedItemLimit)
            {
                return string.Join(", ", names);
            }

            return string.Join(", ", names.Take(NamedItemLimit))
                + " and " + (names.Count - NamedItemLimit) + " more";
        }
    }

    /// <summary>
    /// What a failed refresh left unread that the plan on screen actually
    /// reads. Produced by <see cref="StaleAccountDataWarning.Evaluate"/>;
    /// a null notice means say nothing.
    /// </summary>
    internal sealed class StaleAccountDataNotice
    {
        public StaleAccountDataNotice(
            IReadOnlyList<AccountDataSource> sources,
            IReadOnlyList<string> itemNames,
            bool affectsCurrencyAmounts,
            bool affectsCraftingDisciplines,
            TimeSpan age)
        {
            Sources = sources;
            ItemNames = itemNames;
            AffectsCurrencyAmounts = affectsCurrencyAmounts;
            AffectsCraftingDisciplines = affectsCraftingDisciplines;
            Age = age;
        }

        public IReadOnlyList<AccountDataSource> Sources { get; }

        /// <summary>
        /// Names of the plan's items the unread sources held, in snapshot
        /// order. Names, never ids. Empty when the sources hold none of the
        /// plan's items and the dependency is on a currency or a
        /// discipline instead.
        /// </summary>
        public IReadOnlyList<string> ItemNames { get; }

        public bool AffectsCurrencyAmounts { get; }

        public bool AffectsCraftingDisciplines { get; }

        /// <summary>How old the data the plan used is. Never negative.</summary>
        public TimeSpan Age { get; }
    }
}
