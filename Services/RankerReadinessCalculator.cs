using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Turns one item's two solves plus its cascade availability into the
    /// display-ready numbers a Crafting Ranker row shows. Pure and
    /// Blish-free; every edge case is settled here rather than in the view.
    ///
    /// The headline is a weighted mean of six gate completions, renormalised
    /// over the gates that apply to the item (see RankerReadinessWeights).
    /// Nothing is ever converted into anything else: every ratio the model
    /// computes carries the same unit above and below the line, which is what
    /// keeps an unvalued currency from being silently priced at zero.
    ///
    /// The property that matters most: an item with no time gate, no currency
    /// cost and no discipline requirement scores exactly what a coin-only
    /// metric would score it, because only the materials gate applies and
    /// renormalisation divides by its own weight.
    /// </summary>
    internal static class RankerReadinessCalculator
    {
        /// <summary>
        /// baseline is the snapshot:null solve, owned is the solve against
        /// this slot's availability - the cascade residual in Cascade mode,
        /// the full account in Independent mode (where the caller passes the
        /// unconsumed availability, so every claimed set is empty and the
        /// contested/queued arithmetic below is naturally inert). Both under
        /// OwnMaterialsMode.Free.
        /// </summary>
        public static RankerRowMetrics Compute(
            CraftingPlanResult baseline,
            CraftingPlanResult owned,
            RankerSlotAvailability availability,
            int priorityIndex,
            RankerMode mode = RankerMode.Cascade)
        {
            var metrics = new RankerRowMetrics
            {
                Mode = mode,
                Kind = RankerReadinessKind.NotMeasurable,
                Gates = BuildUnmeasuredGates(),
                CurrencyShortfalls = Array.Empty<RankerCurrencyShortfall>(),
                BarterItemShortfalls = Array.Empty<RankerBarterItemShortfall>(),
                VendorCappedItems = Array.Empty<TimegatedItem>(),
                DisciplineGaps = Array.Empty<RankerDisciplineGap>(),
                PriorityIndex = priorityIndex,
                ComputedAtUtc = DateTime.UtcNow,
            };

            if (baseline?.Plan == null || owned?.Plan == null)
            {
                return metrics;
            }

            metrics.BaselineCoinCost = baseline.Plan.TotalCoinCost;
            metrics.RemainingCoinCost = owned.Plan.TotalCoinCost;
            metrics.VendorCappedItems = VendorCapNotices.Filter(owned);

            var claimedGated = availability?.ClaimedGatedUnits ?? EmptyIntMap;
            var heldCurrency = availability?.Currency ?? EmptyIntMap;

            var gates = new List<RankerGateScore>(6)
            {
                ScoreMaterials(baseline, owned),
                ScoreCurrencies(baseline, owned, heldCurrency, metrics),
                ScoreTimeGates(baseline, owned, claimedGated, metrics),
                ScoreDisciplines(owned, metrics),
                ScoreRecipes(owned),
                ScoreBarterItems(baseline, owned, metrics),
            };
            metrics.Gates = gates;

            ApplyAffordability(availability, metrics);
            ApplyContested(availability, owned, metrics);

            double weighted = 0;
            double weightSum = 0;
            bool anyIncomplete = false;
            foreach (var gate in gates)
            {
                if (gate.Status == RankerGateStatus.Unmeasured)
                {
                    // Excluded from the blend, because entering a number the
                    // module never measured is the fault this whole model
                    // exists to avoid - but it still bars a full headline.
                    // A row cannot read 100% while a barrier it has is
                    // unknown.
                    anyIncomplete = true;
                    continue;
                }

                if (!gate.Applies)
                {
                    continue;
                }

                weighted += gate.Weight * gate.Completion;
                weightSum += gate.Weight;
                if (gate.Completion < 1.0)
                {
                    anyIncomplete = true;
                }
            }

            if (weightSum <= 0)
            {
                // No gate scored. Either there is genuinely nothing left, or
                // the plan is unpriceable - two different statements, and the
                // row must not render the second as "done". anyIncomplete can
                // only be set by an Unmeasured gate here, so it is the third
                // statement: a barrier exists and nothing looked at it.
                bool nothingOutstanding =
                    owned.Plan.TotalCoinCost == 0 &&
                    (owned.Plan.CurrencyCosts == null || owned.Plan.CurrencyCosts.Count == 0) &&
                    (owned.Plan.BarterItemCosts == null || owned.Plan.BarterItemCosts.Count == 0) &&
                    !anyIncomplete;
                metrics.Kind = nothingOutstanding
                    ? RankerReadinessKind.NothingLeft
                    : RankerReadinessKind.NotMeasurable;
                return metrics;
            }

            metrics.Kind = RankerReadinessKind.Measured;
            double readiness = Clamp01(weighted / weightSum);

            // A 100% that is not actually finished is the single most
            // trust-destroying number this tab can print, and with six gates
            // there are six ways to earn one by rounding.
            if (anyIncomplete && readiness > 0.99)
            {
                readiness = 0.99;
            }

            metrics.Readiness = readiness;
            return metrics;
        }

        /// <summary>
        /// "73%" / "Not measurable" / "Nothing left". InvariantCulture, no
        /// decimals, floored - 99.6% renders 99%, never 100%.
        /// </summary>
        public static string FormatReadiness(RankerRowMetrics metrics)
        {
            if (metrics == null)
            {
                return NotMeasurableText;
            }

            switch (metrics.Kind)
            {
                case RankerReadinessKind.NothingLeft:
                    return NothingLeftText;
                case RankerReadinessKind.Measured:
                    return FormatPercent(metrics.Readiness);
                default:
                    return NotMeasurableText;
            }
        }

        /// <summary>Floored whole-percent rendering of a 0..1 fraction.</summary>
        public static string FormatPercent(double fraction)
        {
            // The epsilon absorbs binary-representation wobble only (0.99
            // times 100 is not exactly 99 in a double); it is far too small to
            // round a genuine 99.6 up to 100.
            int percent = (int)Math.Floor(Clamp01(fraction) * 100.0 + 1e-9);
            return percent.ToString(CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>
        /// The Days cell: "62d", or "0" for an item that has been solved and
        /// has no daily gate at all, or a dash for one that has never been
        /// solved.
        /// <para>
        /// The zero is the point. A measured "no wait" used to draw the same
        /// dash as "not yet calculated", which made the two indistinguishable
        /// in the one column where the difference decides whether the row can
        /// be started today.
        /// </para>
        /// </summary>
        public static string FormatDays(RankerRowMetrics metrics)
        {
            if (metrics == null)
            {
                return DashText;
            }

            return metrics.DaysRemaining <= 0
                ? ZeroDaysText
                : metrics.DaysRemaining.ToString(CultureInfo.InvariantCulture) + "d";
        }

        /// <summary>
        /// One gate's cell in the breakdown sub-line. A percentage ONLY when
        /// the gate was scored, so the cells carrying one are exactly the
        /// cells the headline was blended from - which is the row's own
        /// disclosure of what it counted.
        /// <para>
        /// "n/a" is a barrier this item does not have. A dash is a barrier
        /// it has that nothing measured, and a missing gate object, which is
        /// a row that has never been measured at all. Neither may print a
        /// percentage, because a percentage here reads as a measurement.
        /// </para>
        /// </summary>
        public static string FormatGate(RankerGateScore gate)
        {
            if (gate == null)
            {
                return DashText;
            }

            switch (gate.Status)
            {
                case RankerGateStatus.Scored:
                    return FormatPercent(gate.Completion);
                case RankerGateStatus.NoBarrier:
                    return NotApplicableText;
                default:
                    return DashText;
            }
        }

        /// <summary>
        /// How full a gate cell's bar draws, 0..1. Only a scored gate paints
        /// any fill: a full bar is a completion claim, and the two other
        /// statuses have no measurement to make one from. The view does no
        /// arithmetic, so this lives here beside the text that has to agree
        /// with it.
        /// </summary>
        public static double GateBarFraction(RankerGateScore gate)
        {
            return gate != null && gate.Applies ? Clamp01(gate.Completion) : 0.0;
        }

        /// <summary>
        /// Why a gate is not a term of the headline, as one clause for the
        /// Ready hover and the gate cell's own tooltip. Null for a scored
        /// gate, which the caller renders with its figure and weight
        /// instead.
        /// </summary>
        public static string GateExclusionReason(RankerGateScore gate)
        {
            if (gate == null)
            {
                return "not yet calculated";
            }

            switch (gate.Status)
            {
                case RankerGateStatus.Scored:
                    return null;
                case RankerGateStatus.NoBarrier:
                    return "this item has none, so it is not part of the blend";
                default:
                    // Cause-neutral on purpose. Unmeasured is normally an
                    // account gap, but the same status also covers a row
                    // whose solves never arrived, and one clause has to be
                    // true of both.
                    return "nothing measured it, so it is not part of the blend";
            }
        }

        /// <summary>
        /// What a currency shortfall chip's "N short" is measured against.
        /// <para>
        /// In Cascade mode <see cref="RankerCurrencyShortfall.Held"/> is the
        /// wallet left after the higher-priority rows took theirs, not the
        /// account balance, so the bare chip states a number the wallet does
        /// not show. The coin chip carries the same qualifier in its own
        /// hover.
        /// </para>
        /// </summary>
        public static string ShortfallTooltip(
            RankerCurrencyShortfall shortfall, string currencyName, RankerMode mode)
        {
            if (shortfall == null)
            {
                return null;
            }

            string amount = shortfall.Short.ToString("N0", CultureInfo.InvariantCulture);
            string name = string.IsNullOrEmpty(currencyName) ? "this currency" : currencyName;
            return mode == RankerMode.Independent
                ? "You are " + amount + " " + name + " short for this item, measured against your full wallet."
                : "You are " + amount + " " + name +
                  " short for this item, counting what the higher-priority items above it would already have spent.";
        }

        public static string GateLabel(RankerGate gate)
        {
            switch (gate)
            {
                case RankerGate.Materials: return "Materials";
                case RankerGate.Currencies: return "Currencies";
                case RankerGate.TimeGates: return "Time gates";
                case RankerGate.Disciplines: return "Disciplines";
                case RankerGate.Recipes: return "Recipes";
                case RankerGate.BarterItems: return "Barter";
                default: return "";
            }
        }

        public const string NotMeasurableText = "Not measurable";
        public const string NothingLeftText = "Nothing left";
        public const string DashText = "-";

        /// <summary>A gate cell for a barrier this item does not have - see <see cref="FormatGate"/>.</summary>
        public const string NotApplicableText = "n/a";

        /// <summary>A measured absence of any daily gate - see <see cref="FormatDays"/>.</summary>
        public const string ZeroDaysText = "0";

        private static readonly IReadOnlyDictionary<int, int> EmptyIntMap = new Dictionary<int, int>();

        private static RankerGateScore ScoreMaterials(CraftingPlanResult baseline, CraftingPlanResult owned)
        {
            var gate = NewGate(RankerGate.Materials);
            long baselineCoin = baseline.Plan.TotalCoinCost;
            if (baselineCoin <= 0)
            {
                return gate;
            }

            gate.Status = RankerGateStatus.Scored;
            gate.Completion = Clamp01(1.0 - (double)owned.Plan.TotalCoinCost / baselineCoin);
            return gate;
        }

        private static RankerGateScore ScoreCurrencies(
            CraftingPlanResult baseline,
            CraftingPlanResult owned,
            IReadOnlyDictionary<int, int> held,
            RankerRowMetrics metrics)
        {
            var gate = NewGate(RankerGate.Currencies);

            var baselineNeed = SumCurrencies(baseline.Plan.CurrencyCosts);
            var ownedNeed = SumCurrencies(owned.Plan.CurrencyCosts);

            // Union, not just the baseline's keys. Reduction normally only
            // removes currency need, but InventoryReducer's documented
            // residual can flip a node from Craft to a vendor purchase, which
            // introduces one - and a currency the row never lists is a
            // currency the score silently ignored.
            var currencyIds = new HashSet<int>(baselineNeed.Keys);
            currencyIds.UnionWith(ownedNeed.Keys);
            if (currencyIds.Count == 0)
            {
                return gate;
            }

            var shortfalls = new List<RankerCurrencyShortfall>(currencyIds.Count);

            double total = 0;
            foreach (int currencyId in currencyIds)
            {
                ownedNeed.TryGetValue(currencyId, out long need);
                baselineNeed.TryGetValue(currencyId, out long fromScratch);
                held.TryGetValue(currencyId, out int have);
                long shortAmount = Math.Max(0, need - have);

                // The denominator is the larger of the two needs, so a
                // reduction-introduced cost cannot divide by zero and cannot
                // score above 100%.
                long denominator = Math.Max(fromScratch, need);

                shortfalls.Add(new RankerCurrencyShortfall
                {
                    CurrencyId = currencyId,
                    Needed = need,
                    Held = have,
                    Short = shortAmount,
                    BaselineNeeded = denominator,
                });

                // Unweighted: weighting by need would compare 500 Provisioner
                // Tokens against 1,000 Karma as though a token equalled a
                // karma. Each currency is one independent barrier, and the
                // only comparison made is within a single currency.
                total += denominator <= 0 ? 1.0 : Clamp01(1.0 - (double)shortAmount / denominator);
            }

            shortfalls.Sort((a, b) => b.Short.CompareTo(a.Short));
            metrics.CurrencyShortfalls = shortfalls;

            gate.Status = RankerGateStatus.Scored;
            gate.Completion = Clamp01(total / currencyIds.Count);
            return gate;
        }

        /// <summary>
        /// The item-id twin of <see cref="ScoreCurrencies"/>: the account-bound
        /// tokens a vendor takes in place of coin, which
        /// CraftingPlan.BarterItemCosts keeps out of TotalCoinCost because
        /// they have no Trading Post price to fold in.
        /// <para>
        /// The holding comes from the plan result rather than from the
        /// cascade ledger the currency gate reads: the solver never sees the
        /// wallet, so the Ranker nets currencies itself, while item holdings
        /// are already in the snapshot handed to the pipeline.
        /// </para>
        /// <para>
        /// One limit follows from that. RankerPriorityCascade.Consume
        /// reduces the snapshot by what higher slots CRAFT with and not by
        /// what they hand a vendor as a barter cost, so a token two rows
        /// both pay is credited to each of them.
        /// </para>
        /// </summary>
        private static RankerGateScore ScoreBarterItems(
            CraftingPlanResult baseline, CraftingPlanResult owned, RankerRowMetrics metrics)
        {
            var gate = NewGate(RankerGate.BarterItems);

            var baselineNeed = SumBarterItems(baseline.Plan.BarterItemCosts);
            var ownedNeed = SumBarterItems(owned.Plan.BarterItemCosts);

            // Union, for the reason the currency gate gives: reduction can
            // flip a node onto a vendor route that introduces a cost the
            // baseline never had.
            var itemIds = new HashSet<int>(baselineNeed.Keys);
            itemIds.UnionWith(ownedNeed.Keys);
            if (itemIds.Count == 0)
            {
                return gate;
            }

            var held = owned.OwnedVendorItemAmounts;
            var shortfalls = new List<RankerBarterItemShortfall>(itemIds.Count);

            double total = 0;
            foreach (int itemId in itemIds)
            {
                ownedNeed.TryGetValue(itemId, out long need);
                baselineNeed.TryGetValue(itemId, out long fromScratch);

                long have = 0;
                if (held != null && held.TryGetValue(itemId, out int owns))
                {
                    have = owns;
                }

                long shortAmount = Math.Max(0, need - have);
                long denominator = Math.Max(fromScratch, need);

                shortfalls.Add(new RankerBarterItemShortfall
                {
                    ItemId = itemId,
                    Needed = need,
                    Held = have,
                    Short = shortAmount,
                    BaselineNeeded = denominator,
                });

                // Unweighted, for the reason the currency gate gives: one
                // token is not comparable with another token, and the only
                // comparison made is within a single item.
                total += denominator <= 0 ? 1.0 : Clamp01(1.0 - (double)shortAmount / denominator);
            }

            shortfalls.Sort((a, b) => b.Short.CompareTo(a.Short));
            metrics.BarterItemShortfalls = shortfalls;

            gate.Status = RankerGateStatus.Scored;
            gate.Completion = Clamp01(total / itemIds.Count);
            return gate;
        }

        private static RankerGateScore ScoreTimeGates(
            CraftingPlanResult baseline,
            CraftingPlanResult owned,
            IReadOnlyDictionary<int, int> claimed,
            RankerRowMetrics metrics)
        {
            var gate = NewGate(RankerGate.TimeGates);

            // Only recipe-level daily cooldowns feed this. A vendor purchase
            // cap does not: a cap that is not binding is not a barrier, and
            // the shipped seed can cap an item as TP-liquid as Glob of
            // Ectoplasm through one incidental festival-vendor offer.
            //
            // Null is a cooldown seed that was never wired, not an account
            // gap, so it leaves the gate NoBarrier rather than Unmeasured -
            // the same call PlanViewModelBuilder.BuildCooldownNotices makes
            // for the same null.
            var cooldowns = owned.DailyCooldownItems ?? baseline.DailyCooldownItems;
            if (cooldowns == null || cooldowns.Count == 0)
            {
                return gate;
            }

            var baselineGated = GatedCraftQuantities(baseline, cooldowns);
            if (baselineGated.Count == 0)
            {
                return gate;
            }

            var ownedGated = GatedCraftQuantities(owned, cooldowns);

            // max, not sum, ACROSS different gated items: per-account daily
            // caps run independently, so a Lump of Mithrillium and a Glob of
            // Elder Spirit Residue can both be crafted on the same day. This
            // is the same rule PlanViewModelBuilder's own cooldown notice uses.
            metrics.DaysFromScratch = MaxDays(baselineGated, cooldowns, null);
            metrics.DaysAlone = MaxDays(ownedGated, cooldowns, null);

            // sum, DOWN the priority list, for the SAME gated item: that is
            // the cascade. Slot 1 occupies days 1..30 of the queue, slot 2
            // occupies 31..50.
            metrics.DaysRemaining = MaxDays(ownedGated, cooldowns, claimed);

            if (metrics.DaysFromScratch <= 0)
            {
                return gate;
            }

            gate.Status = RankerGateStatus.Scored;
            gate.Completion = Clamp01(1.0 - (double)metrics.DaysRemaining / metrics.DaysFromScratch);
            return gate;
        }

        private static RankerGateScore ScoreDisciplines(CraftingPlanResult owned, RankerRowMetrics metrics)
        {
            var gate = NewGate(RankerGate.Disciplines);

            var required = owned.RequiredDisciplines;
            if (required == null || required.Count == 0)
            {
                return gate;
            }

            // Null means no discipline data was ever captured, which is
            // distinct from captured-and-empty. Never fabricate a "not
            // trained" claim for a snapshot that did not look, and never let
            // the cell read as satisfied either.
            var characters = owned.CharacterDisciplines;
            if (characters == null)
            {
                gate.Status = RankerGateStatus.Unmeasured;
                return gate;
            }

            var gaps = new List<RankerDisciplineGap>(required.Count);
            double total = 0;
            int counted = 0;

            foreach (var requirement in required)
            {
                if (requirement == null || string.IsNullOrEmpty(requirement.Discipline))
                {
                    continue;
                }

                int bestRating = 0;
                string bestCharacter = null;
                foreach (var learned in characters)
                {
                    // Ordinal, matching CraftCompetencyEvaluator and
                    // PlanViewModelBuilder.BuildCharacterAvailabilityText.
                    // Those decide whether the plan prints "not trained on
                    // any character" for the same requirement, so a looser
                    // comparison here would score a discipline the plan
                    // reports as untrained.
                    if (learned == null ||
                        !string.Equals(learned.Discipline, requirement.Discipline, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Rating persists when a discipline is swapped out, so a
                    // learned discipline counts regardless of Active.
                    if (learned.Rating > bestRating)
                    {
                        bestRating = learned.Rating;
                        bestCharacter = learned.CharacterName;
                    }
                }

                gaps.Add(new RankerDisciplineGap
                {
                    Discipline = requirement.Discipline,
                    RequiredRating = requirement.MinRating,
                    BestRating = bestRating,
                    BestCharacterName = bestCharacter,
                });

                // Linear in rating points. Rating is NOT linear in cost - the
                // last 100 points cost far more than the first 100 - but any
                // corrective curve would be invented, and no API publishes
                // one. The bias makes a near-max discipline read slightly
                // better than it is; the gate's small weight bounds the
                // headline error, and the row shows the raw rating pair.
                total += requirement.MinRating <= 0
                    ? 1.0
                    : Clamp01((double)bestRating / requirement.MinRating);
                counted++;
            }

            if (counted == 0)
            {
                // Requirements existed and none carried a readable
                // discipline name, so the barrier is real and unscored.
                gate.Status = RankerGateStatus.Unmeasured;
                return gate;
            }

            gaps.Sort((a, b) => (a.BestRating - a.RequiredRating).CompareTo(b.BestRating - b.RequiredRating));
            metrics.DisciplineGaps = gaps;

            gate.Status = RankerGateStatus.Scored;
            gate.Completion = Clamp01(total / counted);
            return gate;
        }

        private static RankerGateScore ScoreRecipes(CraftingPlanResult owned)
        {
            var gate = NewGate(RankerGate.Recipes);

            var required = owned.RequiredRecipes;
            if (required == null || required.Count == 0)
            {
                return gate;
            }

            // RequiredRecipesVisibility.HasNoUnlockBarrier is the predicate
            // the plan's own Required Recipes section filters by, so the two
            // surfaces count one set. IsMissing null means the
            // learned-recipes check never ran (an API key without the
            // recipes permission), which is an account gap and leaves the
            // gate Unmeasured rather than satisfied.
            int counted = 0;
            int known = 0;
            bool sawUncheckable = false;
            foreach (var recipe in required)
            {
                if (recipe == null ||
                    RequiredRecipesVisibility.HasNoUnlockBarrier(recipe.IsAutoLearned, recipe.Disciplines))
                {
                    continue;
                }

                if (!recipe.IsMissing.HasValue)
                {
                    sawUncheckable = true;
                    continue;
                }

                counted++;
                if (!recipe.IsMissing.Value)
                {
                    known++;
                }
            }

            if (counted == 0)
            {
                gate.Status = sawUncheckable
                    ? RankerGateStatus.Unmeasured
                    : RankerGateStatus.NoBarrier;
                return gate;
            }

            gate.Status = RankerGateStatus.Scored;
            gate.Completion = Clamp01((double)known / counted);
            return gate;
        }

        private static void ApplyAffordability(RankerSlotAvailability availability, RankerRowMetrics metrics)
        {
            if (availability?.CoinCopper == null)
            {
                metrics.HasSnapshot = false;
                metrics.AffordableNow = false;
                metrics.ShortfallCoin = 0;
                return;
            }

            metrics.HasSnapshot = true;
            long coin = availability.CoinCopper.Value;
            metrics.AffordableNow = coin >= metrics.RemainingCoinCost;
            metrics.ShortfallCoin = metrics.AffordableNow ? 0 : metrics.RemainingCoinCost - coin;
        }

        private static void ApplyContested(
            RankerSlotAvailability availability, CraftingPlanResult owned, RankerRowMetrics metrics)
        {
            if (availability == null)
            {
                return;
            }

            var claimedItems = availability.ClaimedItemIds;
            var steps = owned.Plan.Steps;
            if (claimedItems != null && claimedItems.Count > 0 && steps != null)
            {
                var counted = new HashSet<int>();
                foreach (var step in steps)
                {
                    // A step means this slot must still acquire the item -
                    // so a higher slot having drained the account's stock of
                    // it is a cost caused by the user's own ordering.
                    if (step != null && claimedItems.Contains(step.ItemId))
                    {
                        counted.Add(step.ItemId);
                    }
                }

                metrics.ContestedItemCount = counted.Count;
            }

            var claimedCurrencies = availability.ClaimedCurrencyIds;
            if (claimedCurrencies != null && claimedCurrencies.Count > 0)
            {
                int contested = 0;
                foreach (var shortfall in metrics.CurrencyShortfalls)
                {
                    if (claimedCurrencies.Contains(shortfall.CurrencyId))
                    {
                        contested++;
                    }
                }

                metrics.ContestedCurrencyCount = contested;
            }
        }

        private static Dictionary<int, int> GatedCraftQuantities(
            CraftingPlanResult result, IReadOnlyDictionary<int, DailyCooldownItem> cooldowns)
        {
            var gated = new Dictionary<int, int>();
            var steps = result.Plan?.Steps;
            if (steps == null)
            {
                return gated;
            }

            foreach (var step in steps)
            {
                if (step == null || step.Source != AcquisitionSource.Craft || step.Quantity <= 0)
                {
                    continue;
                }

                if (!cooldowns.TryGetValue(step.ItemId, out var cooldown) || cooldown == null || cooldown.PerDayCap <= 0)
                {
                    continue;
                }

                gated[step.ItemId] = gated.TryGetValue(step.ItemId, out int existing)
                    ? existing + step.Quantity
                    : step.Quantity;
            }

            return gated;
        }

        private static int MaxDays(
            Dictionary<int, int> gatedQuantities,
            IReadOnlyDictionary<int, DailyCooldownItem> cooldowns,
            IReadOnlyDictionary<int, int> claimed)
        {
            int days = 0;
            foreach (var pair in gatedQuantities)
            {
                if (!cooldowns.TryGetValue(pair.Key, out var cooldown) || cooldown == null || cooldown.PerDayCap <= 0)
                {
                    continue;
                }

                long units = pair.Value;
                if (claimed != null && claimed.TryGetValue(pair.Key, out int alreadyClaimed))
                {
                    units += alreadyClaimed;
                }

                int itemDays = (int)Math.Ceiling((double)units / cooldown.PerDayCap);
                if (itemDays > days)
                {
                    days = itemDays;
                }
            }

            return days;
        }

        private static Dictionary<int, long> SumCurrencies(IReadOnlyList<CurrencyCost> costs)
        {
            var summed = new Dictionary<int, long>();
            if (costs == null)
            {
                return summed;
            }

            foreach (var cost in costs)
            {
                if (cost == null || cost.Amount <= 0)
                {
                    continue;
                }

                summed[cost.CurrencyId] = summed.TryGetValue(cost.CurrencyId, out long existing)
                    ? existing + cost.Amount
                    : cost.Amount;
            }

            return summed;
        }

        private static Dictionary<int, long> SumBarterItems(IReadOnlyList<BarterItemCost> costs)
        {
            var summed = new Dictionary<int, long>();
            if (costs == null)
            {
                return summed;
            }

            foreach (var cost in costs)
            {
                if (cost == null || cost.Amount <= 0)
                {
                    continue;
                }

                summed[cost.ItemId] = summed.TryGetValue(cost.ItemId, out long existing)
                    ? existing + cost.Amount
                    : cost.Amount;
            }

            return summed;
        }

        private static RankerGateScore NewGate(RankerGate gate)
        {
            return new RankerGateScore
            {
                Gate = gate,
                Status = RankerGateStatus.NoBarrier,
                Completion = 0,
                Weight = RankerReadinessWeights.For(gate),
            };
        }

        /// <summary>
        /// The gate list for a row whose solves never arrived. Every gate is
        /// Unmeasured, not NoBarrier: nothing looked at this item, so no cell
        /// may claim it has no such barrier.
        /// </summary>
        private static IReadOnlyList<RankerGateScore> BuildUnmeasuredGates()
        {
            var gates = new List<RankerGateScore>(6)
            {
                NewGate(RankerGate.Materials),
                NewGate(RankerGate.Currencies),
                NewGate(RankerGate.TimeGates),
                NewGate(RankerGate.Disciplines),
                NewGate(RankerGate.Recipes),
                NewGate(RankerGate.BarterItems),
            };

            foreach (var gate in gates)
            {
                gate.Status = RankerGateStatus.Unmeasured;
            }

            return gates;
        }

        private static double Clamp01(double value)
        {
            if (double.IsNaN(value))
            {
                return 0;
            }

            if (value < 0)
            {
                return 0;
            }

            return value > 1 ? 1 : value;
        }
    }
}
