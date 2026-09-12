using System;
using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    internal class CraftingTreeNode
    {
        private IReadOnlyList<CraftingTreeNode> _children = Array.Empty<CraftingTreeNode>();

        public int ItemId { get; set; }

        // Structural solver node id (internal plumbing for override maps;
        // never displayed). Stable for a given tree shape.
        public int NodeId { get; set; }

        public string Name { get; set; }

        public string IconUrl { get; set; }

        // GW2 API rarity string (e.g. "Fine", "Exotic"); null/empty = unknown.
        public string Rarity { get; set; }

        public int Quantity { get; set; }

        public CraftingDecision Decision { get; set; }

        // How many units of this node's own demand were covered by owned
        // inventory during reduction; 0 when reduction never ran or
        // nothing was consumed. Quantity + OwnedQuantityUsed recovers the
        // original pre-reduction demand, making a partially-owned node
        // representable.
        public int OwnedQuantityUsed { get; set; }

        // True when the user manually marked this item's id "Ignore".
        // Distinct from genuine full ownership: an ignored node also gets
        // Decision = Have, but this flag lets the pill layer show the
        // clickable "IGNORED" toggle alongside HAVE.
        public bool IsIgnored { get; set; }

        // True when this occurrence was zeroed by
        // AchievementBitDedupPrePass because the same item id is already
        // counted elsewhere in the tree. Coexists with Decision == Have
        // but means something different: nothing here is actually owned -
        // the item still needs to be obtained once, just not counted
        // twice. Renders as "COUNTED ELSEWHERE", never plain HAVE.
        public bool IsAchievementBitDeduped { get; set; }

        // Feasible acquisition paths for this node (drives override cycling).
        public bool CanCraft { get; set; }

        public bool CanBuyTp { get; set; }

        public bool CanBuyVendor { get; set; }

        public int? RecipeId { get; set; }

        // Batch shape of the chosen recipe at this exact tree occurrence
        // (docs/gw2e-considerations.md #4) - CraftsNeeded * RecipeOutputCount is what
        // this craft actually produces, which can exceed Quantity (this
        // node's own real demand) when the batch doesn't divide evenly. Set
        // only for Decision == Craft nodes (CraftingTreeBuilder.BuildNode,
        // straight from the chosen RecipeOption); null for every other
        // decision. Read by ExcessCraftOutputCalculator to aggregate
        // sellable/stranded surplus - never fed back into any cost or total.
        public int? CraftsNeeded { get; set; }

        public int? RecipeOutputCount { get; set; }

        // The basis CraftsNeeded was actually derived from (ceil(Quantity
        // / ExpectedOutputCount), never RecipeOutputCount). Equal for
        // integer-yield recipes; a fractional-EV recipe diverges, and
        // "produced" MUST be recovered from CraftsNeeded *
        // RecipeExpectedOutputCount or a large integer surplus is
        // fabricated for an already probability-adjusted yield. Set only
        // for Decision == Craft nodes.
        public double? RecipeExpectedOutputCount { get; set; }

        public long? UnitCost { get; set; }

        public long? SubtreeCost { get; set; }

        // Passthrough of SolverDecision.ComparisonValue. DECISION-ONLY:
        // SubtreeCost remains the sole displayed cost; this exists only
        // to explain on hover why a decision won, never to be folded into
        // a displayed total. Equal to SubtreeCost when no currency
        // valuation contributed.
        public long? DecisionValue { get; set; }

        // Passthrough of SolverDecision.VendorComponentCostsUnreliable.
        // Both component-leaf synthesis and the value-detail tooltip gate
        // on this, so neither presents a currency figure that cannot be
        // proven to sum to the corrected total.
        public bool VendorComponentCostsUnreliable { get; set; }

        // True when this node's UnitCost came from an item's
        // non-preferred TP side because the preferred side had no
        // listings. Three producers: a plain BuyFromTp node, a
        // cost-component leaf (its OWN price), and a BuyFromVendor node
        // (the OR of its VendorItemCosts lines - the flag then describes
        // a cost item falling back, not the row's own item, so the
        // tooltip wording differs). Parent and leaf can both carry the
        // flag; they are separate nodes, no double-counting.
        public bool PriceSideFellBack { get; set; }

        // Non-coin currency cost of a BuyFromVendor decision (see
        // SolverDecision.VendorCurrencyCosts). Null for every other
        // Decision, and also null for a BuyFromVendor decision whose offer
        // was purely coin-priced (nothing to report). Internal ids only -
        // a later display task is responsible for resolving these to
        // names/icons before render.
        public IReadOnlyList<CostLine> VendorCurrencyCosts { get; set; }

        // True when this BuyFromVendor node's offer is paid partly in an
        // untradeable barter item. Its twin of VendorCurrencyCosts: both
        // mean "SubtreeCost/UnitCost do not represent this node's whole
        // cost", so every consumer that reads a coin figure as complete
        // must check BOTH (see PlanStep.VendorHasBarterItemCost).
        public bool VendorHasBarterItemCost { get; set; }

        // True only on a node CraftingTreeBuilder.BuildTree returned: the
        // single-item plan's target, or one of a batch's N requested roots
        // (the synthetic multi-item wrapper never becomes a
        // CraftingTreeNode). The plan's own target is the thing being
        // planned, not an acquisition decision to opt out of, so
        // DecisionPillPlanner offers no IGNORE toggle here.
        // Internal, not public: Newtonsoft's default contract serializes
        // public properties only, so this stays out of the persisted graph
        // and needs no PersistedPlan schema bump (a bump costs a saved
        // result whenever PersistedPlan.MinimumReadableSchemaVersion has
        // to move with it). PlanStoreHelpers re-derives it on
        // restore - see DeserializePersistedPlan.
        internal bool IsPlanRoot { get; set; }

        // True when this node was bought but also has a known recipe, so
        // Children holds the "what it would cost to craft instead"
        // reference branch - rendered dimmed and collapsed. For a vendor
        // node that also synthesized cost-component leaves, Children is a
        // stack of both (leaves first); this flag means "Children
        // includes the reference-branch ingredients", not "exclusively".
        public bool IsReferenceBranch { get; set; }

        // The raw candidate recipe the reference branch was built from,
        // kept so RecipeSheetSavingsCalculator can check whether the
        // hypothetical craft is blocked on an unlearned purchasable-sheet
        // recipe without re-walking the solver tree. Set only alongside
        // IsReferenceBranch; never used for cost math or display.
        public int? ReferenceRecipeId { get; set; }

        public List<string> ReferenceRecipeDisciplines { get; set; }

        public int ReferenceRecipeMinRating { get; set; }

        // True when ReferenceRecipeId's own recipe carries the GW2 API's
        // "LearnedFromItem" flag (unlocked by consuming a purchasable
        // recipe sheet, rather than auto-known or achievement/vendor-
        // sourced) - see RecipeOption.Flags. Meaningless when
        // ReferenceRecipeId is null.
        public bool ReferenceRecipeIsLearnedFromItem { get; set; }

        // True only for a display-only synthetic leaf under a
        // BuyFromVendor node whose winning offer mixed 2+ cost kinds -
        // never on a real solver-backed node. Gets only informational
        // badges, never a decision pill or the Ignore toggle, and
        // corresponds to no RecipeNode at all, so it cannot affect a
        // solver decision.
        public bool IsCostComponent { get; set; }

        // Informational-only "how much of this component do you own"
        // count, populated only when IsCostComponent. Unlike
        // OwnedQuantityUsed it never reduces Quantity or any cost - a
        // cost component is a fact about what the offer charges, not a
        // shopping demand the reducer sees - hence the separate field.
        public int ComponentOwnedQuantity { get; set; }

        // Wiki-derived acquisition guidance (see AcquisitionHintService),
        // set for Decision == Unknown nodes with a seeded hint. Also set
        // (guildupgrade-ingredients fix) for Decision == GuildUpgrade
        // nodes with a fixed, non-seeded explanation - see
        // CraftingTreeBuilder's "GuildUpgrade" branch - since that
        // decision has the same "no priceable source, here is why" shape
        // as Unknown but no AcquisitionHintService entry to draw from.
        // Tooltip-only text, never an id.
        public string AcquisitionHint { get; set; }

        // Short pill label (e.g. "SALVAGE", "EXPLORE") for the same seeded
        // hint entry as AcquisitionHint, set only under the same
        // Decision == Unknown guard. Null/empty when the hint has no badge
        // (or no hint at all) - the view falls back to "UNKNOWN".
        public string AcquisitionBadge { get; set; }

        // Raw cost breakdowns for every feasible source, passthrough of
        // SolverDecision's matching fields - null on the early-return
        // nodes that have no real source choice. Consumed by
        // PillSubduingEvaluator; never fed back into any displayed cost.
        public PillSourceCostBreakdown CraftCostBreakdown { get; set; }

        public PillSourceCostBreakdown BuyFromTpCostBreakdown { get; set; }

        public PillSourceCostBreakdown BuyFromVendorCostBreakdown { get; set; }

        // Passthrough of SolverDecision's matching fields - true when
        // craft was excluded from the automatic pick because no character
        // meets the winning recipe's discipline requirement. Consumed by
        // CompetencyOpportunityCalculator; never fed back into any
        // displayed cost. The companion fields describe the recipe that
        // would have won.
        public bool CraftExcludedByCompetency { get; set; }

        public long? CraftExcludedRealCost { get; set; }

        public IReadOnlyList<string> CraftExcludedDisciplines { get; set; }

        public int CraftExcludedMinRating { get; set; }

        // Passthrough of SolverDecision's matching fields - true whenever
        // the numerically cheapest raw craft recipe overall is untrained,
        // independent of whether the automatic pick got excluded (also
        // covers a competent other-tier or costlier-sibling recipe
        // winning instead). Consumed by CompetencyOpportunityCalculator;
        // never fed back into any displayed cost. See
        // SolverDecision.CheapestCraftUntrained for the force-buy gating.
        public bool CheapestCraftUntrained { get; set; }

        public long? CheapestCraftRealCost { get; set; }

        public IReadOnlyList<string> CheapestCraftDisciplines { get; set; }

        public int CheapestCraftMinRating { get; set; }

        public IReadOnlyList<CraftingTreeNode> Children
        {
            get => _children;
            set => _children = value ?? Array.Empty<CraftingTreeNode>();
        }
    }
}
