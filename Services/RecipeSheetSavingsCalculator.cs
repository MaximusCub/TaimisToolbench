using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure, Blish-free post-solve annotation pass: walks the display
    /// tree for a BOUGHT item whose reference branch is blocked on an
    /// unlearned, LearnedFromItem recipe that a purchasable recipe sheet
    /// would unlock - and would be cheaper to craft than to keep buying.
    ///
    /// recipeSheetItemIdByRecipeId is an injectable lookup, loaded from
    /// ref/recipe_sheet_items.json; a recipe not in the map emits nothing.
    /// The GW2 API states the link from the item side only, so the map is
    /// built ahead of time by tools/VendorOfferUpdater rather than
    /// discovered here.
    ///
    /// Writes only CraftingPlanResult.RecipeSheetSavingsOpportunities -
    /// advisory, never fed back into a decision.
    /// </summary>
    internal static class RecipeSheetSavingsCalculator
    {
        // Narrowed to the one VendorOfferStore method this calculator
        // ever calls; a null delegate means "no offer source available ->
        // emit nothing".
        internal static void Apply(
            CraftingPlanResult result,
            ISet<int> learnedRecipeIds,
            IReadOnlyDictionary<int, ItemPrice> prices,
            PriceBasis priceBasis,
            Func<int, IReadOnlyList<VendorOffer>> offersForItem,
            IReadOnlyDictionary<int, int> recipeSheetItemIdByRecipeId,
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines)
        {
            var opportunities = new List<RecipeSheetSavingsOpportunity>();

            if (result == null)
            {
                return;
            }

            // Every input is required to safely determine "missing",
            // "purchasable", or "worth the note" - a missing input means
            // the answer is genuinely unknown, so no opportunity is ever
            // fabricated.
            if (learnedRecipeIds != null && prices != null && offersForItem != null &&
                recipeSheetItemIdByRecipeId != null && recipeSheetItemIdByRecipeId.Count > 0)
            {
                var seenItemIds = new HashSet<int>();

                if (result.CraftingTree != null)
                {
                    Walk(result.CraftingTree, learnedRecipeIds, prices, priceBasis, offersForItem,
                        recipeSheetItemIdByRecipeId, characterDisciplines, seenItemIds, opportunities);
                }

                if (result.MultiItemRoots != null)
                {
                    foreach (var root in result.MultiItemRoots)
                    {
                        Walk(root, learnedRecipeIds, prices, priceBasis, offersForItem,
                            recipeSheetItemIdByRecipeId, characterDisciplines, seenItemIds, opportunities);
                    }
                }
            }

            result.RecipeSheetSavingsOpportunities = opportunities;
        }

        private static void Walk(
            CraftingTreeNode node,
            ISet<int> learnedRecipeIds,
            IReadOnlyDictionary<int, ItemPrice> prices,
            PriceBasis priceBasis,
            Func<int, IReadOnlyList<VendorOffer>> offersForItem,
            IReadOnlyDictionary<int, int> recipeSheetItemIdByRecipeId,
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines,
            HashSet<int> seenItemIds,
            List<RecipeSheetSavingsOpportunity> opportunities)
        {
            if (node == null)
            {
                return;
            }

            if (node.IsReferenceBranch &&
                node.ReferenceRecipeId.HasValue &&
                node.ReferenceRecipeIsLearnedFromItem &&
                learnedRecipeIds.Contains(node.ReferenceRecipeId.Value) == false &&
                node.Quantity > 0 &&
                node.UnitCost.HasValue &&
                // A vendor decision priced partly in non-coin currency OR
                // in an untradeable barter item is not fully represented by
                // UnitCost alone (see PlanStep.VendorCurrencyCosts' and
                // VendorHasBarterItemCost's own doc comments) - not safely
                // comparable to a pure-coin craft cost.
                (node.VendorCurrencyCosts == null || node.VendorCurrencyCosts.Count == 0) &&
                !node.VendorHasBarterItemCost &&
                // Read-only check here (Add happens only on a successful
                // emit, inside TryEmit) - an earlier tree occurrence of
                // this same item that failed to qualify (e.g. a different
                // owned/reduction context made its own reference branch
                // unprovable) must not block a LATER occurrence that does
                // qualify.
                !seenItemIds.Contains(node.ItemId) &&
                recipeSheetItemIdByRecipeId.TryGetValue(node.ReferenceRecipeId.Value, out int sheetItemId))
            {
                TryEmit(node, sheetItemId, prices, priceBasis, offersForItem, characterDisciplines,
                    seenItemIds, opportunities);
            }

            foreach (var child in node.Children)
            {
                Walk(child, learnedRecipeIds, prices, priceBasis, offersForItem,
                    recipeSheetItemIdByRecipeId, characterDisciplines, seenItemIds, opportunities);
            }
        }

        private static void TryEmit(
            CraftingTreeNode node,
            int sheetItemId,
            IReadOnlyDictionary<int, ItemPrice> prices,
            PriceBasis priceBasis,
            Func<int, IReadOnlyList<VendorOffer>> offersForItem,
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines,
            HashSet<int> seenItemIds,
            List<RecipeSheetSavingsOpportunity> opportunities)
        {
            // Craft-if-crafted cost: sum of the reference branch's direct
            // children (each SubtreeCost is already the full recursive
            // total). Skips display-only cost-component leaves. Any child
            // whose cost cannot be proven in coin makes the whole craft
            // cost unprovable - bail rather than under-count.
            //
            // A Have child also bails: this is a hypothetical branch, and
            // a child reported as owned may already be allocated to the
            // real plan elsewhere - treating it as free would inflate
            // SavingsPerUnit up to the full purchase price and make the
            // note mean different things under Free vs Valued mode.
            long craftTotal = 0;
            // Counts children actually summed into craftTotal - a
            // reference branch with zero of them would leave craftTotal
            // at 0 and report the full chosen price as SavingsPerUnit.
            int countedChildren = 0;
            foreach (var child in node.Children)
            {
                if (child.IsCostComponent)
                {
                    continue;
                }

                if (child.Decision != CraftingDecision.Craft &&
                    child.Decision != CraftingDecision.BuyFromTp &&
                    child.Decision != CraftingDecision.BuyFromVendor)
                {
                    return;
                }

                if (!child.SubtreeCost.HasValue)
                {
                    return;
                }

                // SubtreeCost is coin-only, so a karma-priced or
                // barter-priced descendant anywhere under this child would
                // silently vanish from craftTotal; bail rather than
                // under-count.
                if (SubtreeHasNonCoinVendorCosts(child))
                {
                    return;
                }

                craftTotal += child.SubtreeCost.Value;
                countedChildren++;
            }

            if (countedChildren == 0)
            {
                return;
            }

            // Floor division - conservative bias, see perSheet below.
            long craftUnitCost = craftTotal / node.Quantity;
            long chosenUnitCost = node.UnitCost.Value;
            long savingsPerUnit = chosenUnitCost - craftUnitCost;
            if (savingsPerUnit <= 0)
            {
                return;
            }

            // Floor division is a conservative bias: it can only
            // understate the craft cost or overstate the sheet's cost,
            // never overstate SavingsPerUnit. A null delegate result
            // degrades to "no offers" - offersForItem is caller-supplied.
            var offers = offersForItem(sheetItemId) ?? Array.Empty<VendorOffer>();
            long? cheapestSheetCost = null;
            foreach (var offer in offers)
            {
                if (offer == null || offer.OutputCount <= 0)
                {
                    continue;
                }

                // Skip a seasonal-only offer for the sheet - the plan
                // always assumes the regular market. No such data exists
                // today; guards against a future seasonal sheet offer
                // being priced as available year-round.
                if (!string.IsNullOrEmpty(offer.SeasonalFestival))
                {
                    continue;
                }

                if (!CostLineValuation.TryGetCoinCost(offer.CostLines, prices, priceBasis, out long coinCost))
                {
                    continue;
                }

                long perSheet = coinCost / offer.OutputCount;
                if (!cheapestSheetCost.HasValue || perSheet < cheapestSheetCost.Value)
                {
                    cheapestSheetCost = perSheet;
                }
            }

            if (!cheapestSheetCost.HasValue)
            {
                // Not present/priceable in our vendor data - emit nothing.
                return;
            }

            bool disciplineBlocked = false;
            string discipline = null;
            int requiredRating = 0;
            var realDisciplines = node.ReferenceRecipeDisciplines?
                .Where(d => d != "MysticForge" && d != "Achievement" && d != "Merchant")
                .OrderBy(d => d, System.StringComparer.Ordinal)
                .ToList();
            if (realDisciplines != null && realDisciplines.Count > 0 && characterDisciplines != null)
            {
                requiredRating = node.ReferenceRecipeMinRating;

                // Pick the discipline whose best account rating is closest
                // to requiredRating - steer the player toward the one they
                // are nearest to training, not whichever sorts first.
                // Alphabetical on an exact tie, for determinism.
                discipline = realDisciplines
                    .OrderBy(d => Math.Abs(BestAccountRating(d, characterDisciplines) - requiredRating))
                    .ThenBy(d => d, System.StringComparer.Ordinal)
                    .First();

                bool accountHasIt = characterDisciplines.Any(cd =>
                    cd != null &&
                    realDisciplines.Contains(cd.Discipline) &&
                    cd.Rating >= requiredRating);
                disciplineBlocked = !accountHasIt;
            }

            seenItemIds.Add(node.ItemId);
            opportunities.Add(new RecipeSheetSavingsOpportunity
            {
                ItemId = node.ItemId,
                RecipeId = node.ReferenceRecipeId.Value,
                SheetItemId = sheetItemId,
                SheetCost = cheapestSheetCost.Value,
                SavingsPerUnit = savingsPerUnit,
                DisciplineBlocked = disciplineBlocked,
                Discipline = discipline,
                RequiredRating = requiredRating,
            });
        }

        /// <summary>
        /// True when node or any descendant is paid partly in something
        /// other than coin - a non-empty VendorCurrencyCosts, or an
        /// untradeable barter item - i.e. the subtree's rolled-up
        /// SubtreeCost is not fully representable in coin.
        /// </summary>
        private static bool SubtreeHasNonCoinVendorCosts(CraftingTreeNode node)
        {
            if (node == null)
            {
                return false;
            }

            if (node.VendorHasBarterItemCost ||
                (node.VendorCurrencyCosts != null && node.VendorCurrencyCosts.Count > 0))
            {
                return true;
            }

            foreach (var child in node.Children)
            {
                if (SubtreeHasNonCoinVendorCosts(child))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Highest rating any character on the account has in discipline,
        /// or 0 when no character has it at all - same "0 = untrained"
        /// convention BestCharacterRating-style helpers elsewhere in this
        /// module use. characterDisciplines is guaranteed non-null by this
        /// method's sole call site.
        /// </summary>
        private static int BestAccountRating(
            string discipline, IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines)
        {
            int best = 0;
            foreach (var cd in characterDisciplines)
            {
                if (cd != null &&
                    string.Equals(cd.Discipline, discipline, StringComparison.Ordinal) &&
                    cd.Rating > best)
                {
                    best = cd.Rating;
                }
            }

            return best;
        }
    }
}
