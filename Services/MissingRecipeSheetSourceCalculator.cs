using System;
using System.Collections.Generic;
using System.Text;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure, Blish-free post-solve annotation pass: for every recipe this
    /// plan needs and the account has not learned, names one vendor that
    /// sells the unlocking recipe sheet and what that vendor charges.
    /// Writes only CraftingPlanResult.MissingRecipeSheetSources -
    /// advisory, never fed back into a decision.
    /// <para>
    /// Reads CraftingPlanResult.RequiredRecipes, so it covers a recipe
    /// whatever the plan decided about the item it crafts.
    /// RecipeSheetSavingsCalculator covers only an item the plan decided
    /// to BUY. This pass skips a recipe that one already emitted.
    /// </para>
    /// <para>
    /// Deliberately not CostLineValuation.TryGetCoinCost, which values a
    /// barter item at its trading-post price: the player hands over the
    /// item, not the coin. 1,269 of the 1,788 sheets in
    /// ref/recipe_sheet_items.json cost something other than pure coin
    /// (MEASURED 2026-09-07).
    /// </para>
    /// </summary>
    internal static class MissingRecipeSheetSourceCalculator
    {
        internal static void Apply(
            CraftingPlanResult result,
            Func<int, IReadOnlyList<VendorOffer>> offersForItem)
        {
            var sources = new List<MissingRecipeSheetSource>();

            if (result == null)
            {
                return;
            }

            if (offersForItem != null && result.RequiredRecipes != null)
            {
                // A recipe the savings note already names would otherwise
                // get two Plan Notes entries stating the same sheet.
                var alreadyNoted = new HashSet<int>();
                if (result.RecipeSheetSavingsOpportunities != null)
                {
                    foreach (var opportunity in result.RecipeSheetSavingsOpportunities)
                    {
                        if (opportunity != null)
                        {
                            alreadyNoted.Add(opportunity.RecipeId);
                        }
                    }
                }

                var seenRecipeIds = new HashSet<int>();
                foreach (var recipe in result.RequiredRecipes)
                {
                    if (recipe == null ||
                        recipe.IsMissing != true ||
                        recipe.SheetItemId <= 0 ||
                        alreadyNoted.Contains(recipe.RecipeId) ||
                        !seenRecipeIds.Add(recipe.RecipeId))
                    {
                        continue;
                    }

                    var source = Describe(
                        recipe.RecipeId, recipe.SheetItemId, offersForItem(recipe.SheetItemId));
                    if (source != null)
                    {
                        sources.Add(source);
                    }
                }
            }

            result.MissingRecipeSheetSources = sources;
        }

        /// <summary>
        /// Picks one offer to name and counts the merchants selling on the
        /// same terms. Prefers an offer payable purely in coin, then one
        /// with fewer non-coin cost lines, then the cheaper coin part,
        /// then the earlier merchant name. The last two keys only break
        /// ties, so the choice is stable across runs and machines.
        /// Returns null when no offer survives Candidates.
        /// </summary>
        private static MissingRecipeSheetSource Describe(
            int recipeId, int sheetItemId, IReadOnlyList<VendorOffer> offers)
        {
            var candidates = Candidates(offers);
            if (candidates.Count == 0)
            {
                return null;
            }

            var best = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                if (Compare(candidates[i], best) < 0)
                {
                    best = candidates[i];
                }
            }

            var merchants = new HashSet<string>(StringComparer.Ordinal);
            foreach (var candidate in candidates)
            {
                if (string.Equals(candidate.Signature, best.Signature, StringComparison.Ordinal))
                {
                    merchants.Add(candidate.Offer.MerchantName ?? string.Empty);
                }
            }

            return new MissingRecipeSheetSource
            {
                RecipeId = recipeId,
                SheetItemId = sheetItemId,
                MerchantName = best.Offer.MerchantName,
                OtherMerchantCount = Math.Max(0, merchants.Count - 1),
                NonCoinCostLines = best.NonCoinLines.Count > 0 ? best.NonCoinLines : null,
                CoinCost = best.CoinCost,
            };
        }

        /// <summary>
        /// Offers this pass is willing to describe. A seasonal offer is
        /// skipped for the same reason RecipeSheetSavingsCalculator skips
        /// one: the plan always assumes the regular market. An offer
        /// producing anything but a single sheet is skipped because the
        /// note states the cost of one purchase. An unrecognized
        /// CostLine.Type is skipped rather than described as something it
        /// might not be.
        /// </summary>
        private static List<Candidate> Candidates(IReadOnlyList<VendorOffer> offers)
        {
            var candidates = new List<Candidate>();
            if (offers == null)
            {
                return candidates;
            }

            foreach (var offer in offers)
            {
                if (offer == null ||
                    offer.OutputCount != 1 ||
                    !string.IsNullOrEmpty(offer.SeasonalFestival) ||
                    !CostLineValuation.HasAnyCostLine(offer.CostLines))
                {
                    continue;
                }

                var candidate = Classify(offer);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private static Candidate Classify(VendorOffer offer)
        {
            long coin = 0;
            bool hasCoin = false;
            var nonCoin = new List<CostLine>(offer.CostLines.Count);

            foreach (var line in offer.CostLines)
            {
                if (line == null)
                {
                    return null;
                }

                bool isCurrency = string.Equals(line.Type, "Currency", StringComparison.Ordinal);
                if (isCurrency && line.Id == Gw2Constants.CoinCurrencyId)
                {
                    coin += line.Count;
                    hasCoin = true;
                }
                else if (isCurrency || string.Equals(line.Type, "Item", StringComparison.Ordinal))
                {
                    nonCoin.Add(line);
                }
                else
                {
                    return null;
                }
            }

            return new Candidate
            {
                Offer = offer,
                CoinCost = hasCoin ? (long?)coin : null,
                NonCoinLines = nonCoin,
                Signature = BuildSignature(offer.CostLines),
            };
        }

        private static int Compare(Candidate left, Candidate right)
        {
            int byShape = left.NonCoinLines.Count.CompareTo(right.NonCoinLines.Count);
            if (byShape != 0)
            {
                return byShape;
            }

            int byCoin = (left.CoinCost ?? 0).CompareTo(right.CoinCost ?? 0);
            if (byCoin != 0)
            {
                return byCoin;
            }

            int byMerchant = string.CompareOrdinal(
                left.Offer.MerchantName ?? string.Empty, right.Offer.MerchantName ?? string.Empty);
            if (byMerchant != 0)
            {
                return byMerchant;
            }

            return string.CompareOrdinal(left.Offer.OfferId ?? string.Empty, right.Offer.OfferId ?? string.Empty);
        }

        /// <summary>
        /// Order-independent identity for a cost-line list, used to count
        /// only the merchants charging what the named merchant charges.
        /// The vendor data lists the same cost in either order (a coin
        /// line comes first for some sheets and last for others).
        /// </summary>
        private static string BuildSignature(IReadOnlyList<CostLine> costLines)
        {
            var parts = new List<string>(costLines.Count);
            foreach (var line in costLines)
            {
                parts.Add(line.Type + ":" + line.Id + ":" + line.Count);
            }

            parts.Sort(StringComparer.Ordinal);

            var builder = new StringBuilder();
            foreach (var part in parts)
            {
                builder.Append(part).Append('|');
            }

            return builder.ToString();
        }

        private sealed class Candidate
        {
            public VendorOffer Offer;

            public long? CoinCost;

            public List<CostLine> NonCoinLines;

            public string Signature;
        }
    }
}
