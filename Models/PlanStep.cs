using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    internal class PlanStep
    {
        public int ItemId { get; set; }

        public int Quantity { get; set; }

        public AcquisitionSource Source { get; set; }

        public long UnitCost { get; set; }

        public long TotalCost { get; set; }

        public int RecipeId { get; set; }

        // Non-coin currency cost of this step (Source == BuyFromVendor
        // only), already scaled/aggregated to this step's Quantity. Null
        // for every other step. See SolverDecision.VendorCurrencyCosts.
        public List<CostLine> VendorCurrencyCosts { get; set; }

        // Winning vendor offer's batch shape (Source == BuyFromVendor only,
        // and only when every tree occurrence merged into this step used
        // the IDENTICAL offer - see VendorBatchSolver.FinalizeVendorBatches):
        // OutputCount is the offer's own per-purchase output count, and
        // VendorOfferCurrencyCostLinesPerBatch is that offer's UNSCALED
        // non-coin currency cost for ONE purchase (not this step's
        // aggregated total). Used to derive the shopping list's currency
        // "Each" cell as the offer's true per-unit rate rather
        // than a truncated total/Quantity average. OutputCount stays 0 (and
        // VendorOfferCurrencyCostLinesPerBatch null) when not applicable -
        // a non-vendor step, or a vendor step whose occurrences resolved to
        // more than one distinct offer.
        public int VendorOfferOutputCount { get; set; }

        public List<CostLine> VendorOfferCurrencyCostLinesPerBatch { get; set; }

        // True when this step's winning vendor offer is paid partly in an
        // untradeable barter item - an Item cost line with no Trading Post
        // price, whose units are the cost. UnitCost/TotalCost then do NOT
        // represent this step's whole cost, exactly as they do not when
        // VendorCurrencyCosts is non-empty: any consumer treating a coin
        // figure as complete must check both.
        public bool VendorHasBarterItemCost { get; set; }

        // The barter quantities behind VendorHasBarterItemCost:
        // Item-typed CostLines (Id is an ITEM id, never a currency id)
        // aggregated to this step's Quantity, exactly as
        // VendorCurrencyCosts is. Null for every non-vendor step and for a
        // vendor step whose winning offer takes no barter. Re-derived from
        // the winning offer's batch shape by
        // VendorBatchSolver.FinalizeVendorBatches on every step whose
        // occurrences agreed on that offer; a step whose occurrences
        // disagreed keeps the sum of their per-occurrence ceils, and so
        // overcounts by exactly as much as its own TotalCost does. Either
        // way this - never the pre-merge decision lines - is what
        // CraftingPlan.BarterItemCosts is summed from.
        public List<CostLine> VendorBarterItemCosts { get; set; }

        // The unlock gate the winning vendor offer names: the recipe sheet
        // item the account must own, and the recipe id that sheet unlocks.
        // Both null for every non-vendor step, for a vendor offer with no
        // gate, and for a vendor step whose occurrences resolved to more
        // than one distinct offer - the same three cases that leave
        // VendorOfferOutputCount at 0. See VendorOffer.UnlockRecipeItemId.
        public int? VendorUnlockRecipeItemId { get; set; }

        public int? VendorUnlockRecipeId { get; set; }
    }
}
