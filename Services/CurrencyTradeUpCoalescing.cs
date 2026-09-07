using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Decides which finished vendor steps state their whole cost as one
    /// map currency the module puts no coin value on, and how many of that
    /// currency one unit of the item costs.
    /// <para>
    /// Three Secrets of the Obscure materials are the live case. Each is
    /// sold for a flat 250 units of one map currency, and each is also
    /// listed against zone reward chests the module can neither price nor
    /// route to. The plan used to report the same item twice: once as an
    /// item another vendor offer takes in barter, and once as that map
    /// currency, at 250 per unit. One item, two ledger lines, two numbers.
    /// </para>
    /// <para>
    /// The step-level twin of <see cref="CurrencyTradeUpRow"/>, which asks
    /// the same question of a single tree row. This one adds the valuation
    /// test: a currency the module values has a coin equivalent and stays a
    /// wallet line, because the plan can compare a route to it.
    /// </para>
    /// </summary>
    internal static class CurrencyTradeUpCoalescing
    {
        /// <summary>
        /// True when <paramref name="step"/> buys its item for one unvalued
        /// wallet currency and nothing else. A non-zero TotalCost is part of
        /// the test: any coin part, or any item cost the module could price,
        /// lands there and makes the step something other than a straight
        /// currency trade.
        /// <para>
        /// VendorOfferOutputCount is 0 when the step's occurrences resolved
        /// to more than one offer, and the per-unit rate below is then not
        /// a single number. Such a step is left alone rather than coalesced
        /// at a rate the plan cannot state.
        /// </para>
        /// </summary>
        internal static bool MatchesStep(PlanStep step, CurrencyValuation valuation)
        {
            // TryGetEffectiveCopperValue, not TryGetCopperValue: it answers
            // the same for a valuation the settings layer already merged
            // the curated defaults into and for a raw one, so a caller that
            // passes neither cannot quietly coalesce a currency the module
            // does value.
            var effective = valuation ?? CurrencyValuation.None;
            return step != null &&
                step.Source == AcquisitionSource.BuyFromVendor &&
                step.Quantity > 0 &&
                step.TotalCost == 0 &&
                !step.VendorHasBarterItemCost &&
                (step.VendorBarterItemCosts == null || step.VendorBarterItemCosts.Count == 0) &&
                step.VendorOfferOutputCount > 0 &&
                step.VendorCurrencyCosts != null &&
                step.VendorCurrencyCosts.Count == 1 &&
                step.VendorCurrencyCosts[0] != null &&
                step.VendorCurrencyCosts[0].Count > 0 &&
                step.VendorCurrencyCosts[0].Id != Gw2Constants.CoinCurrencyId &&
                !effective.TryGetEffectiveCopperValue(step.VendorCurrencyCosts[0].Id, out _) &&
                TryGetPerUnitCurrencyCount(step, out _);
        }

        /// <summary>
        /// How much of the currency one unit of the item costs, read from
        /// the winning offer's own batch shape rather than from the step's
        /// aggregate total. False when the batch does not divide evenly:
        /// the true rate is then not a whole number, and the plan states no
        /// rate rather than a rounded one.
        /// </summary>
        internal static bool TryGetPerUnitCurrencyCount(PlanStep step, out int perUnit)
        {
            perUnit = 0;
            if (step == null || step.VendorOfferOutputCount <= 0)
            {
                return false;
            }

            var lines = step.VendorOfferCurrencyCostLinesPerBatch;
            if (lines == null || lines.Count != 1 || lines[0] == null || lines[0].Count <= 0)
            {
                return false;
            }

            if (lines[0].Count % step.VendorOfferOutputCount != 0)
            {
                return false;
            }

            perUnit = lines[0].Count / step.VendorOfferOutputCount;
            return perUnit > 0;
        }

        /// <summary>
        /// How many of the item a holding of the sub-currency buys right
        /// now, never more than <paramref name="outstanding"/> asks for.
        /// Whole units only: the vendor takes no part payment.
        /// </summary>
        internal static int BuysNow(int held, int perUnitCurrencyCount, int outstanding)
        {
            if (held <= 0 || perUnitCurrencyCount <= 0 || outstanding <= 0)
            {
                return 0;
            }

            int buys = held / perUnitCurrencyCount;
            return buys > outstanding ? outstanding : buys;
        }
    }
}
