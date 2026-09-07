using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The vendor-batching sub-engine: batch-shape state types plus the
    /// methods that turn per-occurrence vendor-offer evaluations into one
    /// true per-item merged-ceil cost and post-solve "timegated" cap
    /// notices. PlanSolver holds one instance as an injected collaborator.
    /// The nested types are `internal` only because PlanSolver needs to
    /// declare fields/locals of them.
    /// <para>See docs/ARCHITECTURE.md section 7.</para>
    /// </summary>
    internal class VendorBatchSolver
    {
        // See PlanSolver.Decision.VendorBatch's doc comment.
        internal struct VendorOfferBatch
        {
            public int OutputCount;
            public long CoinCostPerBatch;
            public List<CostLine> CurrencyCostLinesPerBatch;

            // This offer's BARTER Item cost lines for ONE purchase, Item-
            // typed and unscaled - the untradeable lines that fold nothing
            // into CoinCostPerBatch. Null when the offer takes no barter.
            // Deliberately NOT compared by VendorBatchesEqual: that
            // comparison decides VendorBatchState.Conflict, which decides
            // whether a merged step's coin total is re-derived at all, and
            // widening it would move reported coin totals. Two offers for
            // the same item that agree on output count, coin, currency
            // lines and caps but not on which token they take would
            // therefore report the first-seen occurrence's token here.
            public List<CostLine> BarterItemCostLinesPerBatch;

            public int? DailyCap;
            public int? WeeklyCap;

            // Wizard's Vault seasonal purchase cap. Independent of
            // DailyCap/WeeklyCap - FinalizeVendorBatches checks it
            // separately so an offer carrying both can surface both notices.
            public int? SeasonalCap;

            // This offer's unlock gate (VendorOffer.UnlockRecipeItemId),
            // carried so a merged step can report the sheet a player must
            // own. Deliberately NOT compared by VendorBatchesEqual, for
            // exactly the reason BarterItemCostLinesPerBatch is not: that
            // comparison decides VendorBatchState.Conflict, which decides
            // whether a merged step's coin total is re-derived at all, so
            // widening it would move reported coin totals.
            public int? UnlockRecipeItemId;

            public int? UnlockRecipeId;
        }

        // Per-item-id (BuyFromVendor stepKey) bookkeeping built up across
        // every tree occurrence during PlanSolver.Collect/AggregateStep:
        // which offer batch shape was seen, and whether every occurrence
        // agreed (a node's own per-occurrence ceil can, in principle, pick
        // a different offer at a different local quantity - see
        // AggregateStep). Conflict is a one-way ratchet: once true, it
        // never resets, and FinalizeVendorBatches leaves that step's
        // already-per-occurrence-summed cost alone rather than guessing
        // which of several genuinely different offers should apply to the
        // merged total.
        internal sealed class VendorBatchState
        {
            public VendorOfferBatch Batch;
            public bool Conflict;

            // Do NOT add a coarser cap ratchet here to sum cap notices for
            // mixed-offer steps: the wiki's per-row cap is a template
            // parameter, not a confirmed per-station aggregate, so
            // occurrences agreeing on the raw tuple does not mean a real
            // shared limit. Conflict alone suppresses the notice.
        }

        /// <summary>
        /// EvaluateVendorOffers' result - a pure data carrier; see that
        /// method's doc comment for the comparable/fallback split,
        /// valuation rules, and cap-notice plumbing this carries.
        /// </summary>
        internal readonly struct VendorOfferEvaluation
        {
            public readonly long? BestComparableValue;
            public readonly long? BestComparableCoinCost;
            public readonly List<CostLine> BestComparableCurrencyCosts;
            public readonly VendorOfferBatch? BestComparableBatch;
            public readonly long? FallbackCoinCost;
            public readonly List<CostLine> FallbackCurrencyCosts;
            public readonly VendorOfferBatch? FallbackBatch;

            // Additive outputs captured at the same per-line
            // multiplication EvaluateVendorOffers already performs (never
            // a second, divergent computation), so a display-tree leaf can
            // be built from real, already-computed numbers.
            // BestComparableItemCosts/FallbackItemCosts are the winning
            // offer's Item cost lines, scaled to this occurrence's
            // unitsNeeded - null when the winning offer had none. A
            // TP-valued line carries its GoldValue; a barter line's
            // GoldValue is null, because nothing of it was folded into the
            // coin total. BestComparableHasRawCoin/
            // FallbackHasRawCoin report only whether the winning offer had a
            // genuine raw coin cost line (Type=="Currency",
            // Id==Gw2Constants.CoinCurrencyId) with Count > 0 - distinct
            // from an item-folded coin contribution - so a caller can tell
            // "coin" apart from "item money" as its own cost KIND without
            // re-deriving it from TotalCost (which mixes both). Neither
            // field changes any existing comparison/selection/scaling
            // logic in this method.
            public readonly List<VendorItemCostLine> BestComparableItemCosts;
            public readonly bool BestComparableHasRawCoin;
            public readonly List<VendorItemCostLine> FallbackItemCosts;
            public readonly bool FallbackHasRawCoin;

            /// <summary>
            /// The fallback-tier offer charges everything a craftable recipe
            /// of this node charges, and more (VendorOfferDomination), so it
            /// cannot be the cheaper route however its coin part happens to
            /// rank. It is still reported, still clickable, still the answer
            /// when nothing else is - it simply cannot WIN a comparison
            /// against the recipe it is a strictly worse copy of.
            /// </summary>
            public readonly bool FallbackIsDominatedByCraft;

            public VendorOfferEvaluation(
                long? bestComparableValue,
                long? bestComparableCoinCost,
                List<CostLine> bestComparableCurrencyCosts,
                VendorOfferBatch? bestComparableBatch,
                long? fallbackCoinCost,
                List<CostLine> fallbackCurrencyCosts,
                VendorOfferBatch? fallbackBatch,
                List<VendorItemCostLine> bestComparableItemCosts = null,
                bool bestComparableHasRawCoin = false,
                List<VendorItemCostLine> fallbackItemCosts = null,
                bool fallbackHasRawCoin = false,
                bool fallbackIsDominatedByCraft = false)
            {
                BestComparableValue = bestComparableValue;
                BestComparableCoinCost = bestComparableCoinCost;
                BestComparableCurrencyCosts = bestComparableCurrencyCosts;
                BestComparableBatch = bestComparableBatch;
                FallbackCoinCost = fallbackCoinCost;
                FallbackCurrencyCosts = fallbackCurrencyCosts;
                FallbackBatch = fallbackBatch;
                BestComparableItemCosts = bestComparableItemCosts;
                BestComparableHasRawCoin = bestComparableHasRawCoin;
                FallbackItemCosts = fallbackItemCosts;
                FallbackHasRawCoin = fallbackHasRawCoin;
                FallbackIsDominatedByCraft = fallbackIsDominatedByCraft;
            }
        }

        /// <summary>
        /// Splits vendor offers into comparable and fallback tiers on their NON-COIN
        /// cost lines: a non-coin wallet currency, or a BARTER line - an Item cost
        /// line neither the Trading Post nor <paramref name="costLineResolver"/>
        /// (which costs a line by solving it) can price - an Item line either can
        /// price is money, folding into real coin and consulting no valuation.
        /// COMPARABLE means no non-coin lines at all, or a valuation for every
        /// one; any unvalued one makes the offer fallback-only. A valuation moves
        /// the comparison value alone - the coin, currency and barter amounts
        /// committed to the plan are never scaled by it. A fallback coin-part tie
        /// breaks on unit count ONLY when both offers cost the same single
        /// non-coin line, kind included - across different lines there is no
        /// exchange rate to compare their unit counts through.
        /// Tiers: docs/ARCHITECTURE.md sections 7.1 and 7.4.
        /// </summary>
        /// <remarks>
        /// A DailyCap/WeeklyCap/SeasonalCap NEVER excludes an offer or affects its
        /// tier: both tiers carry the raw caps through for FinalizeVendorBatches to
        /// check once against aggregate demand.
        /// </remarks>
        internal VendorOfferEvaluation EvaluateVendorOffers(
            RecipeNode node,
            IReadOnlyDictionary<int, ItemPrice> prices,
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers,
            PriceBasis priceBasis,
            CurrencyValuation currencyValuation,
            HomesteadEfficiencyTiers homesteadTiers,
            // Answers "what does one of this Item cost line cost to
            // acquire" for a line the Trading Post cannot price, by solving
            // that item the way a recipe ingredient is solved. Null keeps
            // every such line a barter line, which is what this method did
            // before cost-line expansion existed. See
            // PlanSolver.ResolveCostLineUnitValue.
            Func<int, CostLineUnitValue> costLineResolver = null,
            // Account competency, for the superset-domination check only
            // (VendorOfferDomination). Null means competency is unknown and
            // no offer is ever demoted for being dominated.
            IReadOnlyDictionary<string, int> bestRatingByDiscipline = null)
        {
            long? bestComparableValue = null;
            long? bestComparableCoinCost = null;
            List<CostLine> bestComparableCurrencyCosts = null;
            VendorOfferBatch? bestComparableBatch = null;
            long? fallbackCoinCost = null;
            List<CostLine> fallbackCurrencyCosts = null;
            VendorOfferBatch? fallbackBatch = null;
            // Tie-break state for the fallback tier: the scaled unit count
            // of the winning offer's single non-coin cost line, and that
            // line's identity as a (kind, id) pair.
            long fallbackNonCoinUnits = 0;
            int fallbackSingleLineId = -1;
            bool fallbackSingleLineIsBarter = false;
            // Whether the winning fallback offer charges a cost line this
            // module cannot price at all. Ranked ahead of coin cost below.
            bool fallbackHasBarterLine = false;

            List<VendorItemCostLine> bestComparableItemCosts = null;
            bool bestComparableHasRawCoin = false;
            List<VendorItemCostLine> fallbackItemCosts = null;
            bool fallbackHasRawCoin = false;
            bool fallbackIsDominatedByCraft = false;

            if (vendorOffers == null ||
                !vendorOffers.TryGetValue(node.Id, out var offers))
            {
                return new VendorOfferEvaluation(
                    bestComparableValue,
                    bestComparableCoinCost,
                    bestComparableCurrencyCosts,
                    bestComparableBatch,
                    fallbackCoinCost,
                    fallbackCurrencyCosts,
                    fallbackBatch);
            }

            foreach (var offer in offers)
            {
                if (offer.OutputCount <= 0)
                {
                    continue;
                }

                // The seed carries every Homestead Refinement row
                // unconditionally, so without this gate the solver behaves
                // as if every account had every efficiency upgrade. Keyed
                // on OutputItemId, not a merchant-name match: HomesteadTier
                // is only set on rows the seeding pass already confirmed
                // are Homestead Refinement, so a string check here would be
                // redundant work in a hot loop.
                if (offer.HomesteadTier.HasValue &&
                    offer.HomesteadTier.Value > homesteadTiers.GetTier(offer.OutputItemId))
                {
                    continue;
                }

                // An offer with no cost lines is data we cannot interpret,
                // not a free lunch: the fold below would leave coinCost 0,
                // priceable true and allValued true, landing it in the
                // COMPARABLE tier at a comparison value of 0 - beating
                // every priced route. See CostLineValuation.HasAnyCostLine
                // for how much of the shipped seed this is.
                // Skipped rather than demoted to the fallback tier: that
                // tier ranks on the coin part, where a 0 wins just as
                // wrongly, and unlike a barter offer - which has a real
                // cost that merely has no coin equivalent - this row has no
                // cost line left to report in a demoted decision at all.
                if (!CostLineValuation.HasAnyCostLine(offer.CostLines))
                {
                    continue;
                }

                // Computed before any pricing, because it needs none: an
                // offer that charges a craftable recipe's ingredients plus a
                // fee is a strictly worse copy of that recipe whatever the
                // numbers say, so it is barred from the comparable tier
                // rather than left to be beaten on price.
                bool dominated = VendorOfferDomination.IsDominatedByAnyRecipe(
                    offer, node, bestRatingByDiscipline);

                long coinCost = 0;
                bool priceable = true;
                var currencyCosts = new List<CostLine>();

                // Raw (unscaled, per-batch) capture of this offer's
                // coin-presence and Item cost lines, purely additive.
                // hasRawCoin distinguishes a genuine coin line from coin
                // that only exists because an Item line was folded in.
                // itemCostRaw is exactly what feeds the coinCost fold
                // below - captured so a display leaf can be built without
                // recomputing the multiplication, with this same line's own
                // GetUnitPrice PriceSideFellBack out param alongside so the
                // scaled VendorItemCostLine can flag it the way a plain
                // BuyFromTp node already is. UnitPrice 0 marks a BARTER
                // line (see the Item branch below).
                bool hasRawCoin = false;
                int barterLineCount = 0;
                // UnitCoin is a long, not the int a raw TP price fits in:
                // a resolved cost line's unit coin is a whole solved
                // acquisition, which for a legendary component runs well past
                // int range. UnitCoin == 0 still means "barter line" - the one
                // encoding every reader below keys on.
                List<(int ItemId, int Count, long UnitCoin, bool PriceSideFellBack, long ComparisonExtraPerUnit)> itemCostRaw = null;

                // Set when a RESOLVED cost line's own subtree carries a cost
                // with no honest coin equivalent. Its coin part is real and
                // folded in below, but it is not the whole story, so the offer
                // is fallback-tier exactly as an unvalued line has always made
                // it.
                bool resolvedLineHasUnvaluedCost = false;

                foreach (var cost in offer.CostLines)
                {
                    if (string.Equals(cost.Type, "Currency", StringComparison.Ordinal))
                    {
                        if (cost.Id == Gw2Constants.CoinCurrencyId)
                        {
                            coinCost += (long)cost.Count;
                            if (cost.Count > 0)
                            {
                                hasRawCoin = true;
                            }
                        }
                        else
                        {
                            // Guarded with
                            // Count > 0, mirroring the raw-coin branch's own
                            // `if (cost.Count > 0)` guard 5 lines above and
                            // the identical Item-cost-line guard below - a
                            // zero/negative-count non-coin Currency cost
                            // line (e.g. malformed wiki-scraped seed data)
                            // must never invent a phantom "currency" cost
                            // KIND. Without this, a single-kind offer with a
                            // stray Count-0 Currency line would wrongly flip
                            // into leaf-synthesis mode
                            // (CraftingTreeBuilder.BuildVendorCostComponentLeaves'
                            // kindCount gate) and render a 0-quantity ghost
                            // leaf with a blank cost and no pill (a negative
                            // Count would render a negative-quantity leaf
                            // instead - survives the `scaled > int.MaxValue`
                            // check below just like the Item side). coinCost
                            // above is untouched either way - a Count of 0
                            // never reaches it from this branch.
                            if (cost.Count > 0)
                            {
                                currencyCosts.Add(cost);
                            }
                        }
                    }
                    else if (string.Equals(cost.Type, "Item", StringComparison.Ordinal))
                    {
                        // The 3-arg GetUnitPrice overload captures this
                        // barter item's own fell-back-side fact rather than
                        // discarding it - see itemCostRaw's doc comment
                        // above and VendorItemCostLine.PriceSideFellBack.
                        int unitPrice = 0;
                        bool itemPriceSideFellBack = false;
                        if (prices.TryGetValue(cost.Id, out var itemPrice))
                        {
                            unitPrice = PlanSolver.GetUnitPrice(itemPrice, priceBasis, out itemPriceSideFellBack);
                        }

                        if (unitPrice > 0)
                        {
                            coinCost += (long)cost.Count * unitPrice;
                            // Guard the raw
                            // capture with Count > 0, mirroring the raw-coin
                            // branch's own `if (cost.Count > 0)` guard above
                            // - a zero/negative-count Item cost line (e.g.
                            // malformed wiki-scraped seed data) must never
                            // invent a phantom "item" cost KIND. Without
                            // this, a single-kind offer with a stray
                            // Count-0 Item line would wrongly flip into
                            // leaf-synthesis mode (CraftingTreeBuilder.
                            // BuildVendorCostComponentLeaves' kindCount
                            // gate) and render a 0-quantity/negative-cost
                            // ghost leaf. coinCost above is left untouched -
                            // a Count of 0 contributes nothing to it either
                            // way.
                            if (cost.Count > 0)
                            {
                                (itemCostRaw ?? (itemCostRaw = NewItemCostRaw()))
                                    .Add((cost.Id, cost.Count, unitPrice, itemPriceSideFellBack, 0L));
                            }
                        }
                        else if (cost.Count > 0)
                        {
                            // No Trading Post price - which used to be the end
                            // of the road, and is why an offer that mirrors a
                            // recipe and adds a fee looked cheaper than the
                            // recipe (docs/ARCHITECTURE.md section 7.4).
                            var solved = costLineResolver?.Invoke(cost.Id);

                            long lineCoin = 0L;
                            if (solved != null && solved.RealCoin > 0L)
                            {
                                try
                                {
                                    lineCoin = checked((long)cost.Count * solved.RealCoin);
                                    coinCost = checked(coinCost + lineCoin);
                                }
                                catch (OverflowException)
                                {
                                    // Fall back to treating it as a barter
                                    // line rather than committing a wrapped
                                    // total; the same demote-never-crash
                                    // posture the valuation loops below take.
                                    // coinCost is unchanged: the checked add
                                    // throws before it assigns.
                                    lineCoin = 0L;
                                }
                            }

                            if (lineCoin > 0L)
                            {
                                resolvedLineHasUnvaluedCost |= solved.HasUnvaluedCost;
                                (itemCostRaw ?? (itemCostRaw = NewItemCostRaw()))
                                    .Add((cost.Id, cost.Count, solved.RealCoin, false, solved.ComparisonExtra));
                            }
                            else
                            {
                                // A barter line: an untradeable token (654 of
                                // the 1,032 item ids used as costs in
                                // ref/vendor_offers.json have no TP price at
                                // all, nearly all AccountBound) that nothing
                                // could cost either. Its units are the cost,
                                // so nothing folds into coinCost - it is
                                // valued for COMPARISON only, exactly like a
                                // non-coin currency line. Discarding the offer
                                // instead would report "no vendor route" for
                                // an item that is genuinely purchasable, just
                                // not with gold.
                                barterLineCount++;
                                (itemCostRaw ?? (itemCostRaw = NewItemCostRaw()))
                                    .Add((cost.Id, cost.Count, 0L, false, 0L));
                            }
                        }
                    }
                    else
                    {
                        // An unrecognized CostLine.Type must never be
                        // silently dropped from the fold above - doing so
                        // would leave `priceable` true and cost this offer
                        // as if the line were not there at all, understating
                        // it and letting it win BuyFromVendor at a
                        // fabricated-low price. VendorOfferLoader performs
                        // no type validation at load and ref/vendor_offers.json
                        // is tool-scraped, so a future third CostLine.Type
                        // (today only "Currency"/"Item" appear) reaches this
                        // loop directly, the same way "GuildUpgrade" reached
                        // the recipe-ingredient guards this mirrors. Mirrors
                        // the Item-with-no-price branch immediately above:
                        // treat the whole offer as unpriceable rather than
                        // guess at a cost for a cost-line shape this solver
                        // has never seen.
                        priceable = false;
                        break;
                    }
                }

                if (!priceable)
                {
                    continue;
                }

                int unitsNeeded = (int)Math.Ceiling((double)node.Quantity / offer.OutputCount);

                long totalCoinCost = coinCost * unitsNeeded;

                // valuationCopper/allValued are shared with the currency
                // scaling below: a barter line and a non-coin currency line
                // are valued by the same rule and land in the same
                // comparison figure, and both stay vacuously true/zero for
                // a pure-coin offer. totalBarterUnits feeds the fallback
                // tie-break only.
                long valuationCopper = 0;
                bool allValued = !resolvedLineHasUnvaluedCost;
                long totalBarterUnits = 0;

                // Scale itemCostRaw by the same unitsNeeded factor
                // totalCoinCost just applied - the same arithmetic, never
                // a second, potentially-diverging computation. The
                // `itemsScalable`/`continue` guard is structurally
                // identical to the pre-existing currency `scalable` guard
                // below, can only fire above int.MaxValue scaled quantity,
                // and skips rather than clamps - a clamp is silently wrong.
                List<VendorItemCostLine> scaledItemCosts = null;

                // The barter half of itemCostRaw, kept UNSCALED so
                // FinalizeVendorBatches can re-scale it to a merged step's
                // aggregate purchase count the same way it re-scales
                // CurrencyCostLinesPerBatch - see VendorOfferBatch.
                List<CostLine> barterLinesPerBatch = null;
                bool itemsScalable = true;
                if (itemCostRaw != null)
                {
                    scaledItemCosts = new List<VendorItemCostLine>(itemCostRaw.Count);
                    foreach (var ic in itemCostRaw)
                    {
                        long scaledQty = (long)ic.Count * unitsNeeded;
                        if (scaledQty > int.MaxValue)
                        {
                            itemsScalable = false;
                            break;
                        }

                        // A barter line (UnitPrice 0) has NO gold value: its
                        // cost is its units, and any valuation of it is
                        // decision-only and must never surface as gold.
                        // GoldValue's null is what a display leaf renders as
                        // a blank cost cell, the same way a currency leaf
                        // already does.
                        scaledItemCosts.Add(new VendorItemCostLine
                        {
                            ItemId = ic.ItemId,
                            Quantity = (int)scaledQty,
                            GoldValue = ic.UnitCoin > 0
                                ? (long)ic.Count * unitsNeeded * ic.UnitCoin
                                : (long?)null,
                            PriceSideFellBack = ic.PriceSideFellBack,
                        });

                        if (ic.UnitCoin > 0)
                        {
                            // A resolved line can carry a decision-only
                            // remainder (a valued wallet currency somewhere
                            // under it). It rides in valuationCopper with
                            // every other decision-only figure, so it can
                            // move a comparison and can never reach the coin
                            // total - the separation the whole two-price
                            // model rests on.
                            if (ic.ComparisonExtraPerUnit > 0L && allValued)
                            {
                                try
                                {
                                    valuationCopper = checked(valuationCopper + (scaledQty * ic.ComparisonExtraPerUnit));
                                }
                                catch (OverflowException)
                                {
                                    allValued = false;
                                }
                            }

                            continue;
                        }

                        totalBarterUnits += scaledQty;
                        (barterLinesPerBatch ?? (barterLinesPerBatch = new List<CostLine>()))
                            .Add(new CostLine { Type = "Item", Id = ic.ItemId, Count = ic.Count });

                        if (!allValued)
                        {
                            continue;
                        }

                        if (currencyValuation != null &&
                            currencyValuation.TryGetItemCopperValue(ic.ItemId, out long itemCopperPerUnit))
                        {
                            try
                            {
                                valuationCopper = checked(valuationCopper + (scaledQty * itemCopperPerUnit));
                            }
                            catch (OverflowException)
                            {
                                // Absurd valuation input; fall back rather
                                // than crash or silently misrank offers.
                                allValued = false;
                            }
                        }
                        else
                        {
                            allValued = false;
                        }
                    }
                }

                if (!itemsScalable)
                {
                    continue;
                }

                // Scale and value the non-coin currency lines (no-op for a
                // pure-coin offer, which has none).
                List<CostLine> scaledCurrencyCosts = null;
                long totalCurrencyUnits = 0;
                bool scalable = true;

                if (currencyCosts.Count > 0)
                {
                    scaledCurrencyCosts = new List<CostLine>(currencyCosts.Count);
                    foreach (var cc in currencyCosts)
                    {
                        long scaled = (long)cc.Count * unitsNeeded;
                        if (scaled > int.MaxValue)
                        {
                            // A quantity this large cannot be represented in a
                            // CostLine; skip the offer rather than crash the solve.
                            scalable = false;
                            break;
                        }

                        totalCurrencyUnits += scaled;
                        scaledCurrencyCosts.Add(new CostLine
                        {
                            Type = cc.Type,
                            Id = cc.Id,
                            Count = (int)scaled,
                        });

                        if (allValued)
                        {
                            if (currencyValuation != null &&
                                currencyValuation.TryGetCopperValue(cc.Id, out long copperPerUnit))
                            {
                                try
                                {
                                    valuationCopper = checked(valuationCopper + (scaled * copperPerUnit));
                                }
                                catch (OverflowException)
                                {
                                    // Absurd valuation input; fall back rather
                                    // than crash or silently misrank offers.
                                    allValued = false;
                                }
                            }
                            else
                            {
                                allValued = false;
                            }
                        }
                    }
                }

                if (!scalable)
                {
                    continue;
                }

                if (allValued)
                {
                    long comparisonValue = 0;
                    try
                    {
                        comparisonValue = checked(totalCoinCost + valuationCopper);
                    }
                    catch (OverflowException)
                    {
                        // Demote, never drop - the same treatment the two
                        // valuation-accumulation loops above already give
                        // an overflowing valuation. Dropping the offer
                        // outright reported "no vendor route" for a route
                        // that genuinely exists and whose coin part is
                        // still real, purely because a valuation the user
                        // supplied was absurd.
                        allValued = false;
                    }

                    if (allValued && !dominated)
                    {
                        if (!bestComparableValue.HasValue ||
                            comparisonValue < bestComparableValue.Value)
                        {
                            bestComparableValue = comparisonValue;
                            bestComparableCoinCost = totalCoinCost;
                            bestComparableCurrencyCosts = scaledCurrencyCosts;
                            bestComparableItemCosts = scaledItemCosts;
                            bestComparableHasRawCoin = hasRawCoin;
                            bestComparableBatch = new VendorOfferBatch
                            {
                                OutputCount = offer.OutputCount,
                                CoinCostPerBatch = coinCost,
                                CurrencyCostLinesPerBatch = currencyCosts.Count > 0 ? currencyCosts : null,
                                BarterItemCostLinesPerBatch = barterLinesPerBatch,
                                DailyCap = offer.DailyCap,
                                WeeklyCap = offer.WeeklyCap,
                                SeasonalCap = offer.SeasonalCap,
                                UnlockRecipeItemId = offer.UnlockRecipeItemId,
                                UnlockRecipeId = offer.UnlockRecipeId,
                            };
                        }

                        continue;
                    }
                }

                // The offer's single non-coin cost line, or -1 when it spans
                // several (unit counts are then never compared). A currency
                // id and an item id are different id spaces, so the KIND is
                // part of the key: a 3-Karma offer and a 3-token offer must
                // never tie-break against each other.
                bool singleLineIsBarter = currencyCosts.Count == 0 && barterLineCount == 1;
                int singleLineId =
                    currencyCosts.Count + barterLineCount != 1 ? -1 :
                    singleLineIsBarter ? BarterLineId(itemCostRaw) : currencyCosts[0].Id;
                long totalNonCoinUnits = totalCurrencyUnits + totalBarterUnits;

                // Ranking key, most significant first: carries a barter
                // line, then coin cost, then the unit tie-break above.
                //
                // A barter line is a cost this module cannot price, so it
                // contributes nothing to totalCoinCost. Ranking on coin
                // alone therefore reads an unpriceable cost as a cheap one:
                // an offer charging one account-bound chest scored 0 and
                // beat the same item's flat 250-map-currency price, and the
                // plan then told the player to acquire a chest it cannot
                // cost, buy or craft. Both offers stay selectable; only the
                // order changes.
                bool hasBarterLine = barterLineCount > 0;
                bool better =
                    !fallbackCoinCost.HasValue ||
                    (fallbackHasBarterLine && !hasBarterLine) ||
                    (fallbackHasBarterLine == hasBarterLine &&
                     (totalCoinCost < fallbackCoinCost.Value ||
                      (totalCoinCost == fallbackCoinCost.Value &&
                       singleLineId != -1 &&
                       singleLineIsBarter == fallbackSingleLineIsBarter &&
                       singleLineId == fallbackSingleLineId &&
                       totalNonCoinUnits < fallbackNonCoinUnits)));

                if (better)
                {
                    fallbackIsDominatedByCraft = dominated;
                    fallbackHasBarterLine = hasBarterLine;
                    fallbackCoinCost = totalCoinCost;
                    fallbackCurrencyCosts = scaledCurrencyCosts;
                    fallbackNonCoinUnits = totalNonCoinUnits;
                    fallbackSingleLineId = singleLineId;
                    fallbackSingleLineIsBarter = singleLineIsBarter;
                    fallbackItemCosts = scaledItemCosts;
                    fallbackHasRawCoin = hasRawCoin;
                    fallbackBatch = new VendorOfferBatch
                    {
                        OutputCount = offer.OutputCount,
                        CoinCostPerBatch = coinCost,
                        CurrencyCostLinesPerBatch = currencyCosts.Count > 0 ? currencyCosts : null,
                        BarterItemCostLinesPerBatch = barterLinesPerBatch,
                        DailyCap = offer.DailyCap,
                        WeeklyCap = offer.WeeklyCap,
                        SeasonalCap = offer.SeasonalCap,
                        UnlockRecipeItemId = offer.UnlockRecipeItemId,
                        UnlockRecipeId = offer.UnlockRecipeId,
                    };
                }
            }

            return new VendorOfferEvaluation(
                bestComparableValue,
                bestComparableCoinCost,
                bestComparableCurrencyCosts,
                bestComparableBatch,
                fallbackCoinCost,
                fallbackCurrencyCosts,
                fallbackBatch,
                bestComparableItemCosts,
                bestComparableHasRawCoin,
                fallbackItemCosts,
                fallbackHasRawCoin,
                fallbackIsDominatedByCraft);
        }

        /// <summary>
        /// The item id of the one barter (UnitPrice 0) entry in a raw item
        /// cost list, for the fallback tie-break. Only called once the
        /// caller has established there is exactly one; -1 otherwise, which
        /// the tie-break reads as "no like-for-like comparison".
        /// </summary>
        private static List<(int ItemId, int Count, long UnitCoin, bool PriceSideFellBack, long ComparisonExtraPerUnit)> NewItemCostRaw()
        {
            return new List<(int, int, long, bool, long)>();
        }

        private static int BarterLineId(
            List<(int ItemId, int Count, long UnitCoin, bool PriceSideFellBack, long ComparisonExtraPerUnit)> itemCostRaw)
        {
            if (itemCostRaw != null)
            {
                foreach (var ic in itemCostRaw)
                {
                    if (ic.UnitCoin == 0)
                    {
                        return ic.ItemId;
                    }
                }
            }

            return -1;
        }

        /// <summary>
        /// Sums <paramref name="add"/> into <paramref name="existing"/> by
        /// currency id (a node can be aggregated into the same PlanStep row
        /// from multiple tree occurrences - see PlanSolver.AggregateStep).
        /// Always returns a fresh list when there is anything to carry, so
        /// the solver-internal Decision's own list is never mutated/aliased
        /// into a PlanStep.
        /// </summary>
        internal List<CostLine> MergeVendorCurrencyCosts(
            List<CostLine> existing, IReadOnlyList<CostLine> add)
        {
            if (add == null || add.Count == 0)
            {
                return existing;
            }

            var merged = existing != null
                ? new List<CostLine>(existing)
                : new List<CostLine>();

            foreach (var line in add)
            {
                int idx = merged.FindIndex(c => c.Id == line.Id);
                if (idx >= 0)
                {
                    // CostLine.Count is int; clamp rather than let two
                    // near-int.MaxValue occurrences silently wrap negative.
                    long summed = (long)merged[idx].Count + line.Count;
                    merged[idx] = new CostLine
                    {
                        Type = merged[idx].Type,
                        Id = merged[idx].Id,
                        Count = ClampToInt(summed),
                    };
                }
                else
                {
                    merged.Add(new CostLine { Type = line.Type, Id = line.Id, Count = line.Count });
                }
            }

            return merged;
        }

        /// <summary>
        /// Sums the BARTER lines of <paramref name="add"/> - the Item cost lines
        /// carrying no <see cref="VendorItemCostLine.GoldValue"/>, i.e. the ones
        /// that folded nothing into the coin total - into
        /// <paramref name="existing"/> by ITEM id, as Item-typed CostLines. The
        /// barter twin of <see cref="MergeVendorCurrencyCosts"/>, with the same
        /// fresh-list and int-clamp contract; TP-valued Item lines are skipped
        /// because their whole cost is already in the step's coin figure.
        /// </summary>
        internal List<CostLine> MergeVendorBarterItemCosts(
            List<CostLine> existing, IReadOnlyList<VendorItemCostLine> add)
        {
            if (add == null || add.Count == 0)
            {
                return existing;
            }

            bool anyBarter = false;
            foreach (var line in add)
            {
                if (line != null && !line.GoldValue.HasValue && line.Quantity > 0)
                {
                    anyBarter = true;
                    break;
                }
            }

            if (!anyBarter)
            {
                return existing;
            }

            var merged = existing != null
                ? new List<CostLine>(existing)
                : new List<CostLine>();

            foreach (var line in add)
            {
                if (line == null || line.GoldValue.HasValue || line.Quantity <= 0)
                {
                    continue;
                }

                int idx = merged.FindIndex(c => c.Id == line.ItemId);
                if (idx >= 0)
                {
                    merged[idx] = new CostLine
                    {
                        Type = merged[idx].Type,
                        Id = merged[idx].Id,
                        Count = ClampToInt((long)merged[idx].Count + line.Quantity),
                    };
                }
                else
                {
                    merged.Add(new CostLine { Type = "Item", Id = line.ItemId, Count = line.Quantity });
                }
            }

            return merged;
        }

        /// <summary>
        /// Re-derives every merged BuyFromVendor PlanStep's true cost from its
        /// AGGREGATE Quantity and the winning offer's batch shape, ceiling the
        /// purchase count exactly once. Applied only when every occurrence resolved
        /// to the identical winning offer (Conflict false); a Conflict step keeps
        /// AggregateStep's sum of real per-occurrence purchases. Do not re-add a
        /// branch that sums a cap notice for Conflict steps whose occurrences agree
        /// on the raw cap tuple - the premise is false, see VendorBatchState.
        ///
        /// Also folds each vendor step's final VendorCurrencyCosts into currencyMap
        /// and its VendorBarterItemCosts into barterItemMap - the only place either
        /// reaches a plan-wide total - and collects timegated notices for any
        /// uniform step whose aggregate purchase count exceeds the daily (preferred)
        /// or weekly cap, plus an independent Seasonal-cap notice; the checks do not
        /// suppress each other. Caps never exclude an offer or change Source/TotalCost.
        ///
        /// The recomputed step.UnitCost is the winning offer's own
        /// CoinCostPerBatch/OutputCount rate, not a truncating total/Quantity average.
        /// Derivation: docs/ARCHITECTURE.md, "Merged-ceil vendor batching".
        /// </summary>
        internal List<TimegatedItem> FinalizeVendorBatches(
            Dictionary<(int, AcquisitionSource, int), PlanStep> stepMap,
            Dictionary<(int, AcquisitionSource, int), VendorBatchState> vendorBatchTracking,
            Dictionary<int, long> currencyMap,
            Dictionary<int, long> barterItemMap)
        {
            var timegatedItems = new List<TimegatedItem>();

            foreach (var kvp in stepMap)
            {
                var step = kvp.Value;
                if (step.Source != AcquisitionSource.BuyFromVendor)
                {
                    continue;
                }

                if (vendorBatchTracking.TryGetValue(kvp.Key, out var state) &&
                    !state.Conflict && state.Batch.OutputCount > 0)
                {
                    var batch = state.Batch;
                    int unitsNeeded = step.Quantity > 0
                        ? (int)Math.Ceiling((double)step.Quantity / batch.OutputCount)
                        : 0;

                    step.TotalCost = batch.CoinCostPerBatch * unitsNeeded;
                    // The coin "Each" cell must show the winning offer's own
                    // true per-unit rate (its per-batch coin cost divided by
                    // its own OutputCount), not a truncating average of the
                    // corrected AGGREGATE total over aggregate Quantity -
                    // the same defect class already fixed for the
                    // currency "Each" cell via CurrencyDisplayResolver.
                    // ResolveUnitAmounts. Example: a "2 for 5" offer merged
                    // to demand 3 gives TotalCost=10 (2 batches); the old
                    // 10/3=3 truncated average implied a per-unit price no
                    // real purchase of this offer ever charges, whereas the
                    // offer's actual rate is 5/2=2 (batch.OutputCount is
                    // already guarded > 0 by the branch condition above).
                    step.UnitCost = batch.CoinCostPerBatch / batch.OutputCount;
                    step.VendorCurrencyCosts = ScaleCostLines(batch.CurrencyCostLinesPerBatch, unitsNeeded);

                    // Re-derived from the batch shape for the same reason
                    // the currency lines above are: AggregateStep's own
                    // sum is of per-occurrence ceils and overcounts a
                    // merged step.
                    step.VendorBarterItemCosts = ScaleCostLines(batch.BarterItemCostLinesPerBatch, unitsNeeded);
                    step.VendorOfferOutputCount = batch.OutputCount;
                    step.VendorOfferCurrencyCostLinesPerBatch = batch.CurrencyCostLinesPerBatch;
                    step.VendorUnlockRecipeItemId = batch.UnlockRecipeItemId;
                    step.VendorUnlockRecipeId = batch.UnlockRecipeId;

                    int? cap = batch.DailyCap.HasValue && batch.DailyCap.Value > 0
                        ? batch.DailyCap
                        : (batch.WeeklyCap.HasValue && batch.WeeklyCap.Value > 0 ? batch.WeeklyCap : (int?)null);
                    if (cap.HasValue && unitsNeeded > cap.Value)
                    {
                        timegatedItems.Add(new TimegatedItem
                        {
                            ItemId = step.ItemId,
                            CapType = (batch.DailyCap.HasValue && batch.DailyCap.Value > 0)
                                ? TimegatedCapType.Daily
                                : TimegatedCapType.Weekly,
                            CapValue = cap.Value,
                            NeededCount = unitsNeeded,
                        });
                    }

                    // SeasonalCap is checked independently of Daily/Weekly
                    // so an offer carrying both surfaces both notices.
                    // Same warn-only semantics and zero-means-uncapped
                    // convention as above.
                    if (batch.SeasonalCap.HasValue && batch.SeasonalCap.Value > 0 &&
                        unitsNeeded > batch.SeasonalCap.Value)
                    {
                        timegatedItems.Add(new TimegatedItem
                        {
                            ItemId = step.ItemId,
                            CapType = TimegatedCapType.Seasonal,
                            CapValue = batch.SeasonalCap.Value,
                            NeededCount = unitsNeeded,
                        });
                    }
                }

                // Conflict intentionally produces no cap notice - a cap
                // cannot be soundly computed across genuinely different
                // offers (see this method's doc comment).
                if (step.VendorCurrencyCosts != null)
                {
                    foreach (var cc in step.VendorCurrencyCosts)
                    {
                        currencyMap[cc.Id] = currencyMap.TryGetValue(cc.Id, out var existing)
                            ? checked(existing + cc.Count)
                            : cc.Count;
                    }
                }

                if (barterItemMap != null && step.VendorBarterItemCosts != null)
                {
                    foreach (var bc in step.VendorBarterItemCosts)
                    {
                        barterItemMap[bc.Id] = barterItemMap.TryGetValue(bc.Id, out var existing)
                            ? checked(existing + bc.Count)
                            : bc.Count;
                    }
                }
            }

            return timegatedItems;
        }

        /// <summary>
        /// Redistributes each FinalizeVendorBatches-corrected merged vendor step's
        /// true aggregate TotalCost back to the per-occurrence memo (Decision)
        /// entries that fed it, so CraftingTreeNode.SubtreeCost stops reporting the
        /// stale per-occurrence overcount. Only stepKeys that method actually
        /// corrected are touched (step.VendorOfferOutputCount &gt; 0): where
        /// occurrences disagreed on the winning offer each memo TotalCost is already
        /// individually correct, and blending them would replace correct values.
        ///
        /// Allocation is largest-remainder (Hamilton) apportionment by Quantity
        /// share. The shares always sum to precisely step.TotalCost, and two
        /// occurrences of equal quantity diverge by at most 1 copper; the multiply
        /// widens to decimal so that holds for any TotalCost/Quantity pair.
        ///
        /// A component leaf's raw VendorItemCosts/VendorCurrencyCosts are NOT
        /// re-derived here and can disagree with the corrected share; the caller
        /// reads this method's outputs to mark which decisions must suppress
        /// component-leaf display (see FlagUnreliableVendorComponentCosts).
        /// Derivation: docs/ARCHITECTURE.md, "Merged-ceil vendor batching".
        /// </summary>
        internal void AllocateVendorNodeCosts(
            Dictionary<(int, AcquisitionSource, int), PlanStep> stepMap,
            Dictionary<(int, AcquisitionSource, int), List<(int NodeId, int Quantity)>> vendorOccurrences,
            Dictionary<int, PlanSolver.Decision> memo)
        {
            foreach (var kvp in vendorOccurrences)
            {
                if (!stepMap.TryGetValue(kvp.Key, out var step) || step.VendorOfferOutputCount <= 0)
                {
                    continue;
                }

                var occurrences = kvp.Value;

                long totalQuantity = 0L;
                for (int i = 0; i < occurrences.Count; i++)
                {
                    totalQuantity += occurrences[i].Quantity;
                }

                if (totalQuantity <= 0)
                {
                    // Defensive only: vendorOccurrences' construction
                    // (PlanSolver.AggregateStep) never records a
                    // non-positive Quantity in practice. Leaves this
                    // step's occurrences untouched rather than divide by
                    // zero.
                    continue;
                }

                var shares = new long[occurrences.Count];
                var remainders = new long[occurrences.Count];
                long allocated = 0L;
                for (int i = 0; i < occurrences.Count; i++)
                {
                    // Overflow: step.TotalCost * quantity can
                    // exceed long range (up to totalQuantity times larger
                    // than the old UnitCost * quantity product this shape
                    // replaced), and on wrap the numerator goes negative,
                    // silently breaking the sum-to-step.TotalCost invariant
                    // below. Widened to decimal for the multiply/divide:
                    // step.TotalCost is long (<= ~9.2e18) and Quantity is
                    // int (<= ~2.1e9), so their product (<= ~1.98e28) is
                    // always well inside decimal's range (~7.9e28) - no
                    // overflow possible for any value either operand's own
                    // type can hold. Both operands are whole coppers, so
                    // truncating back to long after the divide is exact,
                    // matching the prior integer-division floor.
                    decimal numerator = (decimal)step.TotalCost * occurrences[i].Quantity;
                    shares[i] = (long)(numerator / totalQuantity);
                    remainders[i] = (long)(numerator % totalQuantity);
                    allocated += shares[i];
                }

                long leftover = step.TotalCost - allocated;
                if (leftover > 0)
                {
                    var byLargestRemainder = Enumerable.Range(0, occurrences.Count)
                        .OrderByDescending(i => remainders[i])
                        .ThenBy(i => i);
                    foreach (int i in byLargestRemainder)
                    {
                        if (leftover <= 0)
                        {
                            break;
                        }

                        shares[i]++;
                        leftover--;
                    }
                }

                for (int i = 0; i < occurrences.Count; i++)
                {
                    if (memo.TryGetValue(occurrences[i].NodeId, out var decision))
                    {
                        decision.TotalCost = shares[i];
                        memo[occurrences[i].NodeId] = decision;
                    }
                }
            }
        }

        /// <summary>
        /// Structural equality for the fields that determine whether two
        /// tree occurrences of the same item genuinely used the same
        /// vendor offer (see FinalizeVendorBatches). CurrencyCostLinesPerBatch
        /// is compared by content/order, not reference - both occurrences'
        /// lists ultimately come from the same offer's own CostLines, built
        /// independently but identically each time EvaluateVendorOffers
        /// scans that item's offer list.
        /// </summary>
        internal bool VendorBatchesEqual(VendorOfferBatch a, VendorOfferBatch b)
        {
            if (a.OutputCount != b.OutputCount || a.CoinCostPerBatch != b.CoinCostPerBatch)
            {
                return false;
            }

            var linesA = a.CurrencyCostLinesPerBatch;
            var linesB = b.CurrencyCostLinesPerBatch;
            if (linesA == null || linesB == null)
            {
                return linesA == null && linesB == null;
            }

            if (linesA.Count != linesB.Count)
            {
                return false;
            }

            for (int i = 0; i < linesA.Count; i++)
            {
                if (linesA[i].Id != linesB[i].Id ||
                    linesA[i].Count != linesB[i].Count ||
                    !string.Equals(linesA[i].Type, linesB[i].Type, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Scales a per-batch (one purchase's worth) cost-line list by the
        /// number of purchases, clamping to int.MaxValue rather than
        /// overflowing a CostLine's int Count (mirrors the identical clamp
        /// in MergeVendorCurrencyCosts). Used for both a batch's currency
        /// lines and its barter Item lines; Type/Id are carried through
        /// untouched, so the caller's id space is preserved.
        /// </summary>
        internal List<CostLine> ScaleCostLines(List<CostLine> perBatch, int unitsNeeded)
        {
            if (perBatch == null || perBatch.Count == 0)
            {
                return null;
            }

            var scaled = new List<CostLine>(perBatch.Count);
            foreach (var line in perBatch)
            {
                long count = (long)line.Count * unitsNeeded;
                scaled.Add(new CostLine
                {
                    Type = line.Type,
                    Id = line.Id,
                    Count = ClampToInt(count),
                });
            }

            return scaled;
        }

        /// <summary>
        /// Clamps a long to int.MaxValue rather than overflowing a
        /// CostLine's int Count - shared by MergeVendorCurrencyCosts and
        /// ScaleCostLines, the two places a currency amount can grow beyond
        /// int range (summing across occurrences, or scaling by a purchase
        /// count).
        /// </summary>
        private static int ClampToInt(long value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
