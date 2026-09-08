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
    /// docs/ARCHITECTURE.md, "Account data a plan reads".</para>
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
        /// The dialog's text. Plain sentences, no ids, and no claim that
        /// the plan is either wrong or fine.
        /// <para>
        /// One paragraph with no line breaks: ModalDialog hands its message
        /// to DialogLayoutMath as a single paragraph and renders only the
        /// first block, so an embedded newline would not break a line and
        /// a second paragraph would not be drawn at all.
        /// </para>
        /// </summary>
        public static string Compose(StaleAccountDataNotice notice)
        {
            if (notice == null)
            {
                return null;
            }

            var sentences = new List<string>
            {
                "Unable to refresh account snapshot.",
                "This plan used account data captured " + StatusText.ForAgeAgo(notice.Age) + ".",
                "Could not read: " + JoinLabels(notice.Sources) + ".",
            };

            if (notice.ItemNames.Count > 0)
            {
                sentences.Add("The plan needs " + JoinItems(notice.ItemNames) + " from there.");
            }

            if (notice.AffectsCurrencyAmounts)
            {
                sentences.Add("The plan spends currencies your wallet holds.");
            }

            if (notice.AffectsCraftingDisciplines)
            {
                sentences.Add("The plan crafts, and your disciplines decide what you can make.");
            }

            sentences.Add("We cannot tell whether a refresh would have changed this plan.");

            return string.Join(" ", sentences);
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
