using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// See docs/ARCHITECTURE.md section 8 (solver decision rules: TP-buy
    /// baseline, strict-cheaper craft/vendor comparisons, Mystic Clover EV
    /// pricing, force-craft) for the durable rationale.
    /// </summary>
    internal class PlanSolver
    {
        // The vendor-batching sub-engine lives in the injected
        // VendorBatchSolver collaborator; the parameterless constructor
        // keeps every existing `new PlanSolver()` call site unchanged.
        private readonly VendorBatchSolver _vendorBatchSolver;

        public PlanSolver()
            : this(new VendorBatchSolver())
        {
        }

        public PlanSolver(VendorBatchSolver vendorBatchSolver)
        {
            _vendorBatchSolver = vendorBatchSolver ?? new VendorBatchSolver();
        }

        // Internal (not private) so VendorBatchSolver's
        // AllocateVendorNodeCosts can read/write a Decision by NodeId.
        internal struct Decision
        {
            public AcquisitionSource Source;

            // REAL coin cost of this decision: what display, PlanStep, and
            // CraftingTreeNode.SubtreeCost show. Never includes a valued
            // currency's coin-equivalent - only the coin actually spent.
            public long? TotalCost;

            // The value used to compare this decision against siblings at
            // the parent level: same as TotalCost for TP buys, but a
            // comparable vendor offer folds in its valued non-coin lines
            // (currency and barter item alike), and a comparable craft sums
            // its ingredients'
            // ComparisonValues plus any valued Currency ingredient - never
            // their TotalCost. Keeping this separate from TotalCost stops
            // a valued coin-equivalent from being "laundered" away when an
            // ancestor sums child costs. For a fallback-tier decision (see
            // HasUnvaluedCurrency) this is always identical to TotalCost -
            // real coin only, no valuation ever folded in.
            public long? ComparisonValue;
            public int RecipeId;
            public List<CostLine> VendorCurrencyCosts;

            // Passthrough of VendorBatchSolver.VendorOfferEvaluation's
            // matching fields for whichever offer this decision committed to.
            public List<VendorItemCostLine> VendorItemCosts;
            public bool VendorHasRawCoin;

            // True once AllocateVendorNodeCosts has reallocated this
            // occurrence's share of a vendor step that merged 2+ tree
            // occurrences: TotalCost is corrected, but VendorItemCosts/
            // VendorCurrencyCosts stay the pre-merge per-occurrence
            // numbers and may no longer sum to the corrected total.
            // CraftingTreeBuilder suppresses component-leaf synthesis when
            // set. Always false for a single-occurrence vendor buy.
            public bool VendorComponentCostsUnreliable;

            public bool CanCraft;
            public bool CanBuyTp;
            public bool CanBuyVendor;

            // True when this committed decision is fallback-tier - an
            // unvalued currency or barter item, a GuildUpgrade, or another
            // unpriceable ingredient type - directly on the chosen
            // recipe/offer, or
            // transitively via a chosen ingredient's own fallback-tier
            // decision. Without the transitive propagation, an unpriceable
            // cost two Craft levels deep would launder back into a
            // comparable-looking ComparisonValue one level up. Never
            // surfaced on the public SolverDecision.
            public bool HasUnvaluedCurrency;

            // Winning vendor offer's batch shape (BuyFromVendor only):
            // OutputCount and the unscaled per-batch cost, so
            // FinalizeVendorBatches can re-derive a merged step's true
            // cost from aggregate demand and ceil once, instead of summing
            // several already-ceil'd per-occurrence costs.
            public VendorBatchSolver.VendorOfferBatch? VendorBatch;

            // True only for a BuyFromTp decision whose committed unit
            // price came from the non-preferred TP side because the
            // preferred side had no listings; Commit gates it on the
            // Source so a stale true never leaks onto another decision.
            // Drives the unit-price tooltip caveat.
            public bool PriceSideFellBack;

            // Raw cost breakdowns for every feasible source at this node,
            // computed regardless of which one wins. Always non-null, with
            // IsAvailable mirroring CanCraft/CanBuyTp/CanBuyVendor. Feeds
            // PillSubduingEvaluator; never read by PickCheapest or any
            // cost total.
            public PillSourceCostBreakdown CraftCostBreakdown;
            public PillSourceCostBreakdown BuyFromTpCostBreakdown;
            public PillSourceCostBreakdown BuyFromVendorCostBreakdown;

            // True when craft was excluded from the automatic pick
            // specifically because no character meets the winning recipe's
            // discipline requirement - distinct from the force-buy
            // pre-pass also setting craftExcludedFromAutoPick, which needs
            // no user-facing explanation. Never true when there was no
            // comparable alternative to fall back to (craft still
            // auto-wins then, so there is nothing to report).
            public bool CraftExcludedByCompetency;

            // The real coin cost craft would have committed at, and the
            // winning recipe's discipline requirement - only meaningful
            // when CraftExcludedByCompetency is true. Lets Plan Notes
            // report "crafting would cost N less, but no character has
            // Discipline R" with concrete numbers.
            public long? CraftExcludedRealCost;
            public IReadOnlyList<string> CraftExcludedDisciplines;
            public int CraftExcludedMinRating;

            // CraftExcludedByCompetency stays false for two shapes where
            // competency still silently raised the plan's cost: a
            // competent recipe exists only in the other tier, or a
            // costlier competent sibling wins Craft over a cheaper
            // untrained one. CheapestCraftUntrained generalizes the
            // question: true whenever the numerically cheapest raw craft
            // candidate (same tier priority as autoPickCraftOption, but
            // without the competent-first override) fails AccountCanCraft.
            // Never true when bestRatingByDiscipline is null (competency
            // unknown - never penalize on missing data). Purely additive
            // display data for CompetencyOpportunityCalculator; never
            // drives a decision.
            public bool CheapestCraftUntrained;
            public long? CheapestCraftRealCost;
            public IReadOnlyList<string> CheapestCraftDisciplines;
            public int CheapestCraftMinRating;
        }

        /// <summary>
        /// The single resolved "which recipe would auto-win Craft" answer
        /// for one Evaluate() call. Binding Option/RealCost/
        /// ComparisonValue/RecipeId together, resolved once, keeps a
        /// Commit from ever pairing one recipe's cost with another
        /// recipe's id.
        /// </summary>
        private readonly struct CraftAutoPickCandidate
        {
            public readonly RecipeOption Option;
            public readonly long? RealCost;

            /// <summary>
            /// The comparable-tier ComparisonValue - null whenever this
            /// candidate is a fallback-tier recipe.
            /// </summary>
            public readonly long? ComparisonValue;
            public readonly int RecipeId;

            public CraftAutoPickCandidate(RecipeOption option, long? realCost, long? comparisonValue, int recipeId)
            {
                Option = option;
                RealCost = realCost;
                ComparisonValue = comparisonValue;
                RecipeId = recipeId;
            }
        }

        /// <summary>
        /// Running best-recipe state for one selection tier. Offer keeps
        /// the lowest-Cost candidate, breaking an exact cost tie toward
        /// the lowest RecipeId so selection is deterministic regardless of
        /// recipe list order. Cost is the tier's ranking key; RealCost is
        /// the real coin figure a Commit would use (the fallback tier
        /// passes its real cost for both). Updating all four fields in one
        /// place keeps them from falling out of step (see
        /// CraftAutoPickCandidate). Mutable struct: use only as a direct
        /// local - a copy forks the state and silently drops updates.
        /// </summary>
        private struct BestRecipeTracker
        {
            public long? Cost;
            public long? RealCost;
            public int RecipeId;
            public RecipeOption Option;

            public void Offer(long cost, long realCost, RecipeOption recipe)
            {
                if (!Cost.HasValue ||
                    cost < Cost.Value ||
                    (cost == Cost.Value && recipe.RecipeId < RecipeId))
                {
                    Cost = cost;
                    RealCost = realCost;
                    RecipeId = recipe.RecipeId;
                    Option = recipe;
                }
            }
        }

        /// <summary>
        /// What the recipe phase found: the four trackers the decision
        /// reads. Copies, deliberately - BestRecipeTracker is a mutable
        /// struct that must only ever be accumulated through a direct
        /// local, so this carries the finished state out and nothing more.
        /// </summary>
        private readonly struct RecipeCandidates
        {
            /// <summary>Cheapest recipe whose cost is comparable in coin.</summary>
            public readonly BestRecipeTracker BestComparable;

            /// <summary>Cheapest recipe demoted by an unvalued currency.</summary>
            public readonly BestRecipeTracker BestFallback;

            /// <summary>The same two, restricted to recipes the account can craft.</summary>
            public readonly BestRecipeTracker BestCompetentComparable;

            public readonly BestRecipeTracker BestCompetentFallback;

            public RecipeCandidates(
                BestRecipeTracker bestComparable,
                BestRecipeTracker bestFallback,
                BestRecipeTracker bestCompetentComparable,
                BestRecipeTracker bestCompetentFallback)
            {
                BestComparable = bestComparable;
                BestFallback = bestFallback;
                BestCompetentComparable = bestCompetentComparable;
                BestCompetentFallback = bestCompetentFallback;
            }
        }

        /// <summary>
        /// Everything one Solve() call needs to price vendor cost lines by
        /// solving them: the prebuilt quantity-1 subtrees, a memo keyed by
        /// item id, the sibling <see cref="EvaluateContext"/> those subtrees
        /// are evaluated into, and the three guards that make the recursion
        /// terminate. Why the work is linear, and why a cut answers
        /// Unresolved rather than a partial figure:
        /// docs/ARCHITECTURE.md section 7.4.
        /// <para>
        /// The subtrees are evaluated into their OWN memo, never the plan
        /// tree's: every entry of that memo becomes a public SolverDecision,
        /// and a subtree node is not a node of the plan.
        /// </para>
        /// </summary>
        private sealed class CostLineResolutionState
        {
            private static readonly Dictionary<int, RecipeNode> EmptySubtrees =
                new Dictionary<int, RecipeNode>();

            public CostLineResolutionState(
                IReadOnlyDictionary<int, RecipeNode> subtrees,
                IReadOnlyDictionary<int, CostLineUnitValue> seedValues,
                int maxDepth,
                int budget)
            {
                Subtrees = subtrees ?? EmptySubtrees;
                MaxDepth = maxDepth;
                Budget = budget;

                if (seedValues != null)
                {
                    // A re-solve is handed the values the generating solve
                    // already computed, not the subtrees behind them: the
                    // memo starts full and no subtree is ever needed.
                    foreach (var kvp in seedValues)
                    {
                        Memo[kvp.Key] = kvp.Value;
                    }
                }
            }

            public IReadOnlyDictionary<int, RecipeNode> Subtrees { get; }

            public Dictionary<int, CostLineUnitValue> Memo { get; } =
                new Dictionary<int, CostLineUnitValue>();

            public HashSet<int> Visiting { get; } = new HashSet<int>();

            public int MaxDepth { get; }

            public int Budget { get; set; }

            /// <summary>
            /// Set once, immediately after construction: the context the
            /// subtrees evaluate into, whose own CostLines points back here so
            /// a cost line under a cost line resolves the same way.
            /// </summary>
            public EvaluateContext Context { get; set; }
        }

        /// <summary>
        /// Solve-invariant state threaded through every Evaluate()
        /// recursion, constructed once per Solve() call. Only the node
        /// under evaluation varies per call and stays a plain parameter.
        /// Fields hold the locals Solve() previously threaded into
        /// Evaluate(), already normalized where Solve() normalizes them.
        /// </summary>
        private sealed class EvaluateContext
        {
            public IReadOnlyDictionary<int, ItemPrice> Prices { get; }

            public IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> VendorOffers { get; }

            public Dictionary<int, Decision> Memo { get; }

            public PriceBasis PriceBasis { get; }

            public IReadOnlyDictionary<int, AcquisitionSource> Overrides { get; }

            public CurrencyValuation CurrencyValuation { get; }

            public ISet<int> ForceBuyOnlyNodeIds { get; }

            public ISet<int> CompetencyIndependentForceBuyNodeIds { get; }

            public Dictionary<int, (long? BuyCost, long? CraftCost)> CostDiagnostics { get; }

            public Dictionary<int, long?> RawCraftCostDiagnostics { get; }

            public ISet<int> IgnoredItemIds { get; }

            /// <summary>Never null - normalized in the constructor.</summary>
            public HomesteadEfficiencyTiers HomesteadTiers { get; }

            /// <summary>
            /// Precomputed account best-rating-per-discipline lookup; built
            /// exactly once per Solve() call, never per node.
            /// </summary>
            public IReadOnlyDictionary<string, int> BestRatingByDiscipline { get; }

            /// <summary>
            /// Reference-keyed per-node owned-material usage from
            /// InventoryReducer.Reduce - the same node objects Evaluate
            /// walks on a post-reduction tree, so no NodeId translation is
            /// needed. Only flags a craft breakdown as unreliable for
            /// StrictDomination (see BuildCraftCostBreakdown); null
            /// disables the check.
            /// </summary>
            public Dictionary<RecipeNode, int> OwnedQuantityUsedByNode { get; }

            /// <summary>
            /// Cost-line resolution state, shared by reference with the
            /// sibling context that evaluates the subtrees themselves.
            /// Null when the caller supplied no cost-line inputs, which is
            /// exactly the pre-expansion behaviour.
            /// </summary>
            public CostLineResolutionState CostLines { get; set; }

            /// <summary>
            /// The delegate handed to EvaluateVendorOffers, built ONCE per
            /// solve rather than per node: Evaluate runs at every node of the
            /// tree (842 of them for a legendary armour piece), so a lambda
            /// built at the call site would allocate a closure that many
            /// times per solve, and again for every re-solve behind an
            /// override click. Null exactly when CostLines is.
            /// </summary>
            public Func<int, CostLineUnitValue> CostLineResolver { get; set; }

            public EvaluateContext(
                IReadOnlyDictionary<int, ItemPrice> prices,
                IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers,
                Dictionary<int, Decision> memo,
                PriceBasis priceBasis,
                IReadOnlyDictionary<int, AcquisitionSource> overrides,
                CurrencyValuation currencyValuation,
                ISet<int> forceBuyOnlyNodeIds,
                ISet<int> competencyIndependentForceBuyNodeIds,
                Dictionary<int, (long? BuyCost, long? CraftCost)> costDiagnostics,
                Dictionary<int, long?> rawCraftCostDiagnostics,
                ISet<int> ignoredItemIds,
                HomesteadEfficiencyTiers homesteadTiers,
                IReadOnlyDictionary<string, int> bestRatingByDiscipline,
                Dictionary<RecipeNode, int> ownedQuantityUsedByNode)
            {
                Prices = prices;
                VendorOffers = vendorOffers;
                Memo = memo;
                PriceBasis = priceBasis;
                Overrides = overrides;
                CurrencyValuation = currencyValuation;
                ForceBuyOnlyNodeIds = forceBuyOnlyNodeIds;
                CompetencyIndependentForceBuyNodeIds = competencyIndependentForceBuyNodeIds;
                CostDiagnostics = costDiagnostics;
                RawCraftCostDiagnostics = rawCraftCostDiagnostics;
                IgnoredItemIds = ignoredItemIds;
                HomesteadTiers = homesteadTiers ?? HomesteadEfficiencyTiers.Default;
                BestRatingByDiscipline = bestRatingByDiscipline;
                OwnedQuantityUsedByNode = ownedQuantityUsedByNode;
            }
        }

        /// <summary>
        /// Solve-invariant accumulator state threaded through every
        /// Collect() recursion, constructed once per Solve() call. The
        /// node under collection varies per call and stays a plain
        /// parameter, as does the craft-order counter (mutable
        /// accumulation, threaded by ref).
        /// </summary>
        private sealed class CollectContext
        {
            public Dictionary<int, Decision> Memo { get; }

            public Dictionary<(int, AcquisitionSource, int), PlanStep> StepMap { get; }

            public Dictionary<int, long> CurrencyMap { get; }

            public Dictionary<(int, int), int> CraftOrder { get; }

            public Dictionary<(int, AcquisitionSource, int), VendorBatchSolver.VendorBatchState> VendorBatchTracking { get; }

            public Dictionary<(int, AcquisitionSource, int), List<(int NodeId, int Quantity)>> VendorOccurrences { get; }

            public Dictionary<(int, AcquisitionSource, int), List<int>> CraftOccurrences { get; }

            public ISet<int> IgnoredItemIds { get; }

            public CollectContext(
                Dictionary<int, Decision> memo,
                Dictionary<(int, AcquisitionSource, int), PlanStep> stepMap,
                Dictionary<int, long> currencyMap,
                Dictionary<(int, int), int> craftOrder,
                Dictionary<(int, AcquisitionSource, int), VendorBatchSolver.VendorBatchState> vendorBatchTracking,
                Dictionary<(int, AcquisitionSource, int), List<(int NodeId, int Quantity)>> vendorOccurrences,
                Dictionary<(int, AcquisitionSource, int), List<int>> craftOccurrences,
                ISet<int> ignoredItemIds)
            {
                Memo = memo;
                StepMap = stepMap;
                CurrencyMap = currencyMap;
                CraftOrder = craftOrder;
                VendorBatchTracking = vendorBatchTracking;
                VendorOccurrences = vendorOccurrences;
                CraftOccurrences = craftOccurrences;
                IgnoredItemIds = ignoredItemIds;
            }
        }

        public SolveResult Solve(RecipeNode tree, IReadOnlyDictionary<int, ItemPrice> prices)
        {
            return Solve(tree, prices, null);
        }

        public SolveResult Solve(
            RecipeNode tree,
            IReadOnlyDictionary<int, ItemPrice> prices,
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers,
            PriceBasis priceBasis = PriceBasis.InstantBuy,
            IReadOnlyDictionary<int, AcquisitionSource> overrides = null,
            CurrencyValuation currencyValuation = null,
            // Nodes in this set have craft excluded from the automatic
            // comparison (buying outright beats crafting fresh components
            // by gw2e's 15% margin - see OwnedMaterialsForceBuyPrePass).
            // A manual per-node override still wins.
            ISet<int> forceBuyOnlyNodeIds = null,
            // Nodes force-buy-excluded under BOTH the competency-resolved
            // and a competency-blind evaluation of the 0.85 rule (see
            // OwnedMaterialsForceBuyPrePass.ForceBuyPrePassResult). Used
            // only to gate cheapestCraftUntrained, never
            // craftExcludedFromAutoPick. Null disables the distinction.
            ISet<int> competencyIndependentForceBuyNodeIds = null,
            // When non-null, populated with each node's raw (buyCost,
            // craftCost) so OwnedMaterialsForceBuyPrePass can apply gw2e's
            // buyPrice < craftDecisionPrice * 0.85 rule without
            // duplicating this method's aggregation. Never affects this
            // solve's own Decisions/Plan.
            Dictionary<int, (long? BuyCost, long? CraftCost)> costDiagnostics = null,
            // When non-null, populated with each node's raw
            // competency-blind cheapest craft real cost, letting
            // OwnedMaterialsForceBuyPrePass run its second 0.85 evaluation
            // from this same throwaway solve without a second Solve() call.
            Dictionary<int, long?> rawCraftCostDiagnostics = null,
            // When false, the tree's existing NodeIds are trusted as-is:
            // the force-buy pre-pass pre-assigned them (surviving pruning
            // via CloneNode) and renumbering would desync its
            // forceBuyOnlyNodeIds set.
            bool assignNodeIds = true,
            // Item ids treated as fully in-hand tree-wide for this solve:
            // zero cost, no step or shopping row, no recursion into their
            // ingredients (gw2e's usedQuantity == 0 rule). Per-ItemId,
            // unlike the per-NodeId `overrides`.
            ISet<int> ignoredItemIds = null,
            // Per-material Homestead Refinement efficiency tiers. Null
            // behaves as HomesteadEfficiencyTiers.Default (tier 0 for
            // every material, gw2e's own default).
            HomesteadEfficiencyTiers homesteadTiers = null,
            // Per-character discipline data, used only to decide whether a
            // Craft decision that would win the automatic comparison is
            // actually craftable by this account (see
            // CraftCompetencyEvaluator). Null means competency is unknown
            // and never penalizes craft.
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines = null,
            // Reference-keyed owned-material usage from the same
            // InventoryReducer.Reduce call that produced `tree`. Null
            // disables the RawQuantitiesReducedByOwnedStock check.
            Dictionary<RecipeNode, int> ownedQuantityUsedByNode = null,
            // Quantity-1 acquisition subtrees for the Item cost lines of
            // this solve's vendor offers, so a line with no Trading Post
            // price is COSTED rather than counted as free. Null restores
            // the pre-expansion behaviour exactly (every such line stays a
            // barter line). See VendorCostLineSubtrees.
            VendorCostLineSubtrees vendorCostSubtrees = null,
            // Cost-line unit values a PREVIOUS solve of this same plan
            // already computed. An override re-solve is given these instead
            // of the subtrees behind them: it must cost the offer's lines
            // the way the generating solve did, and the values are a few
            // dozen small rows where the subtrees are several thousand
            // nodes. See PlanSolveContext.VendorCostLineValues.
            IReadOnlyDictionary<int, CostLineUnitValue> vendorCostLineValues = null)
        {
            var valuation = currencyValuation ?? CurrencyValuation.None;
            var tiers = homesteadTiers ?? HomesteadEfficiencyTiers.Default;
            var memo = new Dictionary<int, Decision>();

            // Built once per solve (not per node/recipe) - see
            // CraftCompetencyEvaluator.BuildBestRatingByDiscipline.
            var bestRatingByDiscipline = CraftCompetencyEvaluator.BuildBestRatingByDiscipline(characterDisciplines);

            // Pre-pass: assign unique NodeIds to every node in the tree.
            // Assignment is deterministic (DFS order), so NodeIds - and any
            // overrides keyed on them - are stable across re-solves of the
            // same tree.
            if (assignNodeIds)
            {
                RecipeNodeIds.Assign(tree);
            }

            // Pass 1: decide buy vs craft vs vendor at every node
            // Named throughout: 14 positionals with three same-typed
            // ISet<int> params is a silent-transposition hazard.
            var evaluateContext = new EvaluateContext(
                prices: prices,
                vendorOffers: vendorOffers,
                memo: memo,
                priceBasis: priceBasis,
                overrides: overrides,
                currencyValuation: valuation,
                forceBuyOnlyNodeIds: forceBuyOnlyNodeIds,
                competencyIndependentForceBuyNodeIds: competencyIndependentForceBuyNodeIds,
                costDiagnostics: costDiagnostics,
                rawCraftCostDiagnostics: rawCraftCostDiagnostics,
                ignoredItemIds: ignoredItemIds,
                homesteadTiers: tiers,
                bestRatingByDiscipline: bestRatingByDiscipline,
                ownedQuantityUsedByNode: ownedQuantityUsedByNode);

            // The subtrees evaluate into their own memo and their own
            // NodeId space, and see none of the plan tree's NodeId-keyed or
            // reference-keyed inputs (overrides, the force-buy sets, the
            // diagnostics dictionaries, the owned-usage map): every one of
            // those is keyed to a node of the PLAN, and applying it to a
            // subtree node that merely shares an id would silently answer a
            // different question. IgnoredItemIds is the exception, and
            // deliberately carried: it is keyed by ITEM id and means "the
            // player already has this", which is as true under a cost line
            // as anywhere else.
            CostLineResolutionState costLineState = null;
            if (vendorCostSubtrees != null || vendorCostLineValues != null)
            {
                var costLines = new CostLineResolutionState(
                    vendorCostSubtrees?.ByItemId,
                    vendorCostLineValues,
                    VendorCostLineSubtrees.DefaultMaxResolutionDepth,
                    vendorCostSubtrees?.Count ?? 0);

                costLines.Context = new EvaluateContext(
                    prices: prices,
                    vendorOffers: vendorOffers,
                    memo: new Dictionary<int, Decision>(),
                    priceBasis: priceBasis,
                    overrides: null,
                    currencyValuation: valuation,
                    forceBuyOnlyNodeIds: null,
                    competencyIndependentForceBuyNodeIds: null,
                    costDiagnostics: null,
                    rawCraftCostDiagnostics: null,
                    ignoredItemIds: ignoredItemIds,
                    homesteadTiers: tiers,
                    bestRatingByDiscipline: bestRatingByDiscipline,
                    ownedQuantityUsedByNode: null);

                // One delegate for both contexts: the resolver reads only
                // the shared state, so the sibling context needs no second
                // closure of its own.
                Func<int, CostLineUnitValue> resolver = id => ResolveCostLineUnitValue(id, costLines);

                costLines.Context.CostLines = costLines;
                costLines.Context.CostLineResolver = resolver;
                evaluateContext.CostLines = costLines;
                evaluateContext.CostLineResolver = resolver;
                costLineState = costLines;
            }

            Evaluate(tree, evaluateContext);

            // Pass 2: collect steps and currency costs following pass-1 decisions
            var stepMap = new Dictionary<(int, AcquisitionSource, int), PlanStep>();
            var currencyMap = new Dictionary<int, long>();

            // The barter twin of currencyMap, keyed by ITEM id - a
            // different id space that collides numerically with the
            // currency ids above, which is why it is a second map and
            // never a shared one (Models/BarterItemCost.cs).
            var barterItemMap = new Dictionary<int, long>();

            // The sub-currency behind each barterItemMap entry a currency
            // trade-up fed, keyed by ITEM id: Id is the currency, Count the
            // units one item costs. Display only - the Total Cost table's
            // note says what a holding of that currency buys.
            var tradeUpCurrencyByItemId = new Dictionary<int, CostLine>();
            var craftOrder = new Dictionary<(int, int), int>();
            var vendorBatchTracking = new Dictionary<(int, AcquisitionSource, int), VendorBatchSolver.VendorBatchState>();
            var vendorOccurrences = new Dictionary<(int, AcquisitionSource, int), List<(int NodeId, int Quantity)>>();
            // Every tree occurrence's NodeId that fed a merged Craft-type
            // stepKey, in first-seen (DFS) order - the Craft-side twin of
            // vendorOccurrences, consumed by RefreshCraftStepCosts.
            var craftOccurrences = new Dictionary<(int, AcquisitionSource, int), List<int>>();
            int craftCounter = 0;

            var collectContext = new CollectContext(
                memo, stepMap, currencyMap, craftOrder, vendorBatchTracking,
                vendorOccurrences, craftOccurrences, ignoredItemIds);
            Collect(tree, collectContext, ref craftCounter);

            // Pass 2b: re-derive each merged vendor step's
            // true cost from its AGGREGATE Quantity and the winning offer's
            // batch shape, ceiling once instead of trusting the sum of
            // several already-per-occurrence-ceil'd costs; also folds the
            // (now-correct) vendor currency costs into currencyMap and
            // collects any post-solve "timegated" (cap-exceeded) notices.
            // A requested item is what the plan produces, so its own
            // acquisition can never be coalesced into a requirement for
            // itself - the currency it costs is the answer there.
            var requestedItemIds = new HashSet<int>();
            foreach (var root in PlanRootNodes.Of(tree))
            {
                requestedItemIds.Add(root.Id);
            }

            var timegatedItems = _vendorBatchSolver.FinalizeVendorBatches(
                stepMap, vendorBatchTracking, currencyMap, barterItemMap,
                valuation, tradeUpCurrencyByItemId, requestedItemIds);

            // Pass 2c: FinalizeVendorBatches only corrects the merged
            // PlanStep/currencyMap view, never `memo` - which is what the
            // public Decisions dict and every CraftingTreeNode.SubtreeCost
            // are built from. Re-derive each corrected vendor step's true
            // per-occurrence share (AllocateVendorNodeCosts), then re-sum
            // every Craft ancestor bottom-up (RecomputeCraftCosts, no
            // depth bound) so the correction propagates to the root.
            //
            // The currency-equivalent contribution is re-derived from the
            // corrected merged batch shape rather than replaying stale
            // per-occurrence deltas (which double-counted valued currency
            // lines): step.VendorCurrencyCosts is already re-scaled to the
            // aggregate unitsNeeded, so its valuation is computed once per
            // merged step, then allocated across occurrences with the same
            // largest-remainder (Hamilton) apportionment
            // AllocateVendorNodeCosts uses for TotalCost - shares always
            // sum to precisely the step total. A step with
            // VendorOfferOutputCount <= 0 (occurrences disagreed on the
            // winning offer) is left untouched, exactly as TotalCost is.
            _vendorBatchSolver.AllocateVendorNodeCosts(stepMap, vendorOccurrences, memo);

            foreach (var kvp in vendorOccurrences)
            {
                if (!stepMap.TryGetValue(kvp.Key, out var step) || step.VendorOfferOutputCount <= 0)
                {
                    continue;
                }

                long totalCurrencyValue = 0L;
                if (step.VendorCurrencyCosts != null)
                {
                    try
                    {
                        foreach (var line in step.VendorCurrencyCosts)
                        {
                            if (valuation != null && valuation.TryGetCopperValue(line.Id, out long copperPerUnit))
                            {
                                totalCurrencyValue = checked(totalCurrencyValue + ((long)line.Count * copperPerUnit));
                            }
                        }
                    }
                    catch (OverflowException)
                    {
                        // Fall back to whatever was accumulated before the
                        // overflow rather than failing the whole Solve() -
                        // matches RecomputeComparisonValues' and
                        // EvaluateVendorOffers' no-crash posture.
                    }
                }

                var occurrences = kvp.Value;

                // Mirrors AllocateVendorNodeCosts' largest-remainder
                // (Hamilton) apportionment; a "last occurrence absorbs the
                // remainder" shape let ComparisonValue diverge from the
                // apportioned TotalCost by up to step.Quantity - 1 copper.
                long totalQuantity = 0L;
                for (int i = 0; i < occurrences.Count; i++)
                {
                    totalQuantity += occurrences[i].Quantity;
                }

                var currencyShares = new long[occurrences.Count];
                if (totalQuantity > 0)
                {
                    var currencyRemainders = new long[occurrences.Count];
                    long allocatedCurrency = 0L;
                    for (int i = 0; i < occurrences.Count; i++)
                    {
                        // totalCurrencyValue (long) * quantity (int) can
                        // exceed long range; widened to decimal, which holds
                        // any such product. Both operands are whole coppers,
                        // so truncating back to long is exact.
                        decimal numerator = (decimal)totalCurrencyValue * occurrences[i].Quantity;
                        currencyShares[i] = (long)(numerator / totalQuantity);
                        currencyRemainders[i] = (long)(numerator % totalQuantity);
                        allocatedCurrency += currencyShares[i];
                    }

                    long leftover = totalCurrencyValue - allocatedCurrency;
                    if (leftover > 0)
                    {
                        var byLargestRemainder = Enumerable.Range(0, occurrences.Count)
                            .OrderByDescending(i => currencyRemainders[i])
                            .ThenBy(i => i);
                        foreach (int i in byLargestRemainder)
                        {
                            if (leftover <= 0)
                            {
                                break;
                            }

                            currencyShares[i]++;
                            leftover--;
                        }
                    }
                }

                // else: totalQuantity <= 0 is defensive only - currencyShares
                // stays all-zero rather than divide by zero.
                for (int i = 0; i < occurrences.Count; i++)
                {
                    int nodeId = occurrences[i].NodeId;
                    long currencyShare = currencyShares[i];

                    // A fallback-tier offer deliberately commits
                    // ComparisonValue == TotalCost with no valuation folded
                    // in; overwriting it with a partial figure made the
                    // value-detail tooltip render a precise-looking price
                    // for an offer that was never valued. A VALUED barter
                    // offer is skipped for the mirror-image reason: its
                    // valued lines are Item lines, absent from
                    // step.VendorCurrencyCosts and so contributing nothing
                    // to the share above. Consequence: a skipped decision
                    // keeps its pre-correction ComparisonValue, so
                    // ComparisonValue == TotalCost + share need not hold
                    // after the merged correction.
                    if (memo.TryGetValue(nodeId, out var decision) && decision.TotalCost.HasValue &&
                        !decision.HasUnvaluedCurrency && !HasBarterItemCost(decision))
                    {
                        decision.ComparisonValue = decision.TotalCost.Value + currencyShare;
                        memo[nodeId] = decision;
                    }
                }
            }

            // AllocateVendorNodeCosts corrects decision.TotalCost, but
            // VendorItemCosts/VendorCurrencyCosts (captured pre-merge) are
            // never re-derived - see FlagUnreliableVendorComponentCosts.
            // Kept out of VendorBatchSolver: it only reads that method's
            // outputs and writes an auxiliary flag.
            FlagUnreliableVendorComponentCosts(stepMap, vendorOccurrences, memo);
            RecomputeCraftCosts(tree, memo, ignoredItemIds);

            // The ComparisonValue twin of RecomputeCraftCosts above; must
            // run after both AllocateVendorNodeCosts and
            // RecomputeCraftCosts so it walks fully corrected inputs.
            RecomputeComparisonValues(tree, memo, ignoredItemIds, valuation);

            // Pass 2d: stepMap's Craft-type PlanStep entries were summed
            // before the correction passes ran; refresh them so the
            // Crafting Steps rows agree with the corrected tree and
            // totals - see RefreshCraftStepCosts.
            RefreshCraftStepCosts(stepMap, craftOccurrences, memo);

            // Build ordered step list: buys/unknowns first, then crafts in bottom-up order
            var buysAndUnknowns = new List<PlanStep>();
            var crafts = new List<(PlanStep step, int order)>();

            foreach (var step in stepMap.Values)
            {
                if (step.Source == AcquisitionSource.Craft)
                {
                    var craftKey = (step.ItemId, step.RecipeId);
                    int order = craftOrder.ContainsKey(craftKey) ? craftOrder[craftKey] : 0;
                    crafts.Add((step, order));
                }
                else
                {
                    buysAndUnknowns.Add(step);
                }
            }

            crafts.Sort((a, b) => a.order.CompareTo(b.order));

            var steps = new List<PlanStep>(buysAndUnknowns);
            steps.AddRange(crafts.Select(c => c.step));

            long totalCoinCost = 0L;
            foreach (var step in steps)
            {
                if (step.Source == AcquisitionSource.BuyFromTp ||
                    step.Source == AcquisitionSource.BuyFromVendor)
                {
                    totalCoinCost += step.TotalCost;
                }
            }

            // A coin-typed Currency ingredient is real copper spent
            // directly in a recipe, with no Buy step of its own - fold it
            // into totalCoinCost so the Total Cost summary agrees with the
            // Recipe Tree and Craft rows. Excluded from currencyCosts so
            // it never double-displays as a "currency 1" line.
            if (currencyMap.TryGetValue(Gw2Constants.CoinCurrencyId, out long coinIngredientTotal))
            {
                totalCoinCost = checked(totalCoinCost + coinIngredientTotal);
            }

            var currencyCosts = new List<CurrencyCost>();
            foreach (var kvp in currencyMap)
            {
                if (kvp.Key == Gw2Constants.CoinCurrencyId)
                {
                    continue;
                }

                currencyCosts.Add(new CurrencyCost { CurrencyId = kvp.Key, Amount = checked(kvp.Value) });
            }

            var barterItemCosts = new List<BarterItemCost>(barterItemMap.Count);
            foreach (var kvp in barterItemMap)
            {
                tradeUpCurrencyByItemId.TryGetValue(kvp.Key, out var tradeUp);
                barterItemCosts.Add(new BarterItemCost
                {
                    ItemId = kvp.Key,
                    Amount = kvp.Value,
                    TradeUpCurrencyId = tradeUp?.Id,
                    TradeUpCurrencyPerUnit = tradeUp?.Count,
                });
            }

            var plan = new CraftingPlan
            {
                TargetItemId = tree.Id,
                TargetQuantity = tree.Quantity,
                Steps = steps,
                TotalCoinCost = totalCoinCost,
                CurrencyCosts = currencyCosts,
                BarterItemCosts = barterItemCosts,
                TimegatedItems = timegatedItems,
            };

            // Convert internal memo to public decisions dict
            var decisions = new Dictionary<int, SolverDecision>(memo.Count);
            foreach (var kvp in memo)
            {
                decisions[kvp.Key] = new SolverDecision
                {
                    Source = kvp.Value.Source,
                    RecipeId = kvp.Value.RecipeId,
                    TotalCost = kvp.Value.TotalCost,
                    // Public passthrough of the private Decision.ComparisonValue.
                    ComparisonValue = kvp.Value.ComparisonValue,
                    VendorCurrencyCosts = kvp.Value.VendorCurrencyCosts,
                    VendorItemCosts = kvp.Value.VendorItemCosts,
                    VendorHasRawCoin = kvp.Value.VendorHasRawCoin,
                    VendorComponentCostsUnreliable = kvp.Value.VendorComponentCostsUnreliable,
                    CanCraft = kvp.Value.CanCraft,
                    CanBuyTp = kvp.Value.CanBuyTp,
                    CanBuyVendor = kvp.Value.CanBuyVendor,
                    PriceSideFellBack = kvp.Value.PriceSideFellBack,
                    CraftCostBreakdown = kvp.Value.CraftCostBreakdown,
                    BuyFromTpCostBreakdown = kvp.Value.BuyFromTpCostBreakdown,
                    BuyFromVendorCostBreakdown = kvp.Value.BuyFromVendorCostBreakdown,
                    CraftExcludedByCompetency = kvp.Value.CraftExcludedByCompetency,
                    CraftExcludedRealCost = kvp.Value.CraftExcludedRealCost,
                    CraftExcludedDisciplines = kvp.Value.CraftExcludedDisciplines,
                    CraftExcludedMinRating = kvp.Value.CraftExcludedMinRating,
                    CheapestCraftUntrained = kvp.Value.CheapestCraftUntrained,
                    CheapestCraftRealCost = kvp.Value.CheapestCraftRealCost,
                    CheapestCraftDisciplines = kvp.Value.CheapestCraftDisciplines,
                    CheapestCraftMinRating = kvp.Value.CheapestCraftMinRating,
                };
            }

            return new SolveResult
            {
                Plan = plan,
                Decisions = decisions,
                VendorCostLineValues = costLineState?.Memo,
            };
        }

        /// <summary>
        /// Evaluates the cheapest acquisition for <paramref name="node"/>
        /// and commits it to the context's memo. Returns the
        /// decision's ComparisonValue, NOT its real coin TotalCost -
        /// callers summing ingredient costs for a parent craft need
        /// comparison values for the parent's own craft-vs-buy comparison
        /// (see Decision.ComparisonValue). Every "Item" ingredient of
        /// every recipe gets its own memo entry regardless of what this
        /// node ends up choosing; non-"Item" ingredients are never
        /// Evaluate()-called and get no memo entry.
        /// </summary>
        private long? Evaluate(RecipeNode node, EvaluateContext ctx)
        {
            // Item-positive guard (not an enumerated deny-list): only an
            // "Item" node is ever priced here; the ingredient loop never
            // recurses into a non-Item ingredient, so this is
            // defense-in-depth for a future direct caller.
            if (node.IngredientType != "Item")
            {
                return null;
            }

            // An "Ignore"-d item id is fully in-hand: zero cost, no
            // evaluation, and no recursion into its own ingredients
            // (gw2e's "an un-crafted branch never asks for its
            // ingredients"). CanCraft/CanBuyTp/CanBuyVendor stay false;
            // CraftingTreeBuilder short-circuits to Have anyway.
            if (ctx.IgnoredItemIds != null && ctx.IgnoredItemIds.Contains(node.Id))
            {
                ctx.Memo[node.NodeId] = new Decision
                {
                    Source = AcquisitionSource.UnknownSource,
                    TotalCost = 0L,
                    ComparisonValue = 0L,
                    RecipeId = 0,
                    VendorCurrencyCosts = null,
                    CanCraft = false,
                    CanBuyTp = false,
                    CanBuyVendor = false,
                    VendorBatch = null,
                    // Kept non-null for the same reason the flags above
                    // are explicitly false rather than omitted.
                    CraftCostBreakdown = new PillSourceCostBreakdown { IsAvailable = false },
                    BuyFromTpCostBreakdown = new PillSourceCostBreakdown { IsAvailable = false },
                    BuyFromVendorCostBreakdown = new PillSourceCostBreakdown { IsAvailable = false },
                };
                return 0L;
            }

            long? buyTotalCost = GetBuyCost(node.Id, node.Quantity, ctx.Prices, ctx.PriceBasis, out bool buyPriceSideFellBack);

            // Evaluate vendor offers. Coin-only offers (directly or via
            // TP-priced barter) compete in PickCheapest. Offers with
            // non-coin lines - a wallet currency, or an untradeable barter
            // item - compete only when every such line has a valuation;
            // with any unvalued line the offer is NOT comparable (rating it
            // by its coin part alone would let a 500k-karma offer beat
            // every coin option) and is kept only as a fallback (repo
            // invariant: never invent exchange rates). A winning offer's
            // non-coin lines are always reported on the plan - valuation
            // affects comparison, never the displayed cost.
            var vendorEvaluation = _vendorBatchSolver.EvaluateVendorOffers(
                node, ctx.Prices, ctx.VendorOffers, ctx.PriceBasis, ctx.CurrencyValuation, ctx.HomesteadTiers,
                ctx.CostLineResolver,
                ctx.BestRatingByDiscipline);

            var candidates = SelectBestRecipes(node, ctx);
            var bestComparable = candidates.BestComparable;
            var bestFallback = candidates.BestFallback;
            var bestCompetentComparable = candidates.BestCompetentComparable;
            var bestCompetentFallback = candidates.BestCompetentFallback;

            // canCraft = gw2e's "hasComponents": true whenever a recipe
            // exists, comparable or fallback tier alike. A node with a
            // comparable recipe but no buy price always force-crafts via
            // PickCheapest (craftBeatsBuy is true when buyCost is null).
            bool canCraft = bestComparable.Cost.HasValue || bestFallback.Cost.HasValue;
            bool canBuyTp = buyTotalCost.HasValue;
            bool canBuyVendor = vendorEvaluation.BestComparableValue.HasValue ||
                                vendorEvaluation.FallbackCoinCost.HasValue;

            // The force-buy pre-pass marks this node craft:false before
            // the automatic comparison; a manual override (checked next,
            // using the unmodified canCraft) still always wins.
            bool isForceBuyOnly = ctx.ForceBuyOnlyNodeIds != null &&
                ctx.ForceBuyOnlyNodeIds.Contains(node.NodeId);
            bool craftExcludedFromAutoPick = isForceBuyOnly;

            // Craft should only win the automatic pick when some character
            // can actually craft it - checked against whichever recipe
            // would actually be used. Folded into the same
            // craftExcludedFromAutoPick flag the force-buy pre-pass uses;
            // canCraft and the manual-override branch never read it, so
            // CRAFT stays clickable. Null bestRatingByDiscipline means
            // competency unknown and never penalizes.
            //
            // Also gated on a genuine COMPARABLE next-best source
            // existing: a node whose only path is Craft must still
            // auto-pick Craft, or its cost drops out of the plan entirely
            // (UnknownSource, null TotalCost). A fallback-tier vendor
            // offer does not count as a genuine alternative - excluding
            // craft for one would commit an unvalued-currency purchase and
            // silently drop the node's real priced cost from the gold
            // total.
            bool hasComparableAlternative = buyTotalCost.HasValue || vendorEvaluation.BestComparableValue.HasValue;
            // Prefer the best competent option per tier (comparable
            // first), falling back to the raw best only when no competent
            // option exists anywhere. That raw fallback still wins
            // automatically when hasComparableAlternative is false - the
            // "no genuine alternative -> auto-craft regardless of
            // competency" carve-out. Also the recipe fed to
            // BuildCraftCostBreakdown, so the CRAFT pill's comparison uses
            // whichever recipe would actually be used.
            bool anyCompetentCraftOption = bestCompetentComparable.Option != null ||
                bestCompetentFallback.Option != null;

            // Resolved once into a single CraftAutoPickCandidate -
            // comparable-first, fallback otherwise, competent-preferred
            // within each tier. A fallback-tier candidate's
            // ComparisonValue is null; a null Option means no recipe at all.
            CraftAutoPickCandidate? autoPickCandidate;
            if (bestCompetentComparable.Option != null)
            {
                autoPickCandidate = new CraftAutoPickCandidate(
                    bestCompetentComparable.Option, bestCompetentComparable.RealCost,
                    bestCompetentComparable.Cost, bestCompetentComparable.RecipeId);
            }
            else if (bestCompetentFallback.Option != null)
            {
                autoPickCandidate = new CraftAutoPickCandidate(
                    bestCompetentFallback.Option, bestCompetentFallback.RealCost,
                    null, bestCompetentFallback.RecipeId);
            }
            else if (bestComparable.Option != null)
            {
                autoPickCandidate = new CraftAutoPickCandidate(
                    bestComparable.Option, bestComparable.RealCost,
                    bestComparable.Cost, bestComparable.RecipeId);
            }
            else if (bestFallback.Option != null)
            {
                autoPickCandidate = new CraftAutoPickCandidate(
                    bestFallback.Option, bestFallback.RealCost,
                    null, bestFallback.RecipeId);
            }
            else
            {
                autoPickCandidate = null;
            }

            RecipeOption autoPickCraftOption = autoPickCandidate?.Option;

            // DecisionValue must reflect the tier autoPickCraftOption came
            // from - null for a fallback-tier pick, per
            // PillSourceCostBreakdown.DecisionValue's null contract.
            long? craftBreakdownDecisionValue = autoPickCandidate?.ComparisonValue;

            // The real cost/RecipeId twin of craftBreakdownDecisionValue,
            // feeding PickCheapest and the Craft Commit sites so they
            // always operate on the same recipe; falls back to the raw
            // (possibly-incompetent) cost when nothing competent exists
            // and craftExcludedFromAutoPick is false.
            long? autoPickCraftRealCost = autoPickCandidate?.RealCost;
            int autoPickRecipeId = autoPickCandidate?.RecipeId ?? 0;

            // Tracked separately from craftExcludedFromAutoPick (which the
            // force-buy pre-pass also sets) so Plan Notes can tell "no
            // character is trained" apart from "buying is cheaper".
            bool craftExcludedByCompetency = autoPickCraftOption != null &&
                hasComparableAlternative &&
                !anyCompetentCraftOption;
            if (craftExcludedByCompetency)
            {
                craftExcludedFromAutoPick = true;
            }

            // The numerically cheapest raw craft candidate overall - same
            // tier priority as autoPickCraftOption but without the
            // competent-first override, so this can be untrained even when
            // the auto pick resolved to a competent recipe.
            RecipeOption cheapestCraftOptionOverall = bestComparable.Option ?? bestFallback.Option;
            long? cheapestCraftRealCostOverall = bestComparable.Option != null
                ? bestComparable.RealCost
                : bestFallback.RealCost;

            // Gated on !isCompetencyIndependentForceBuy, not
            // !isForceBuyOnly: force-buy membership can itself be
            // competency-caused (the pre-pass's craft diagnostic is
            // competency-resolved), in which case training would empty the
            // force-buy set - exactly the opportunity this field reports.
            // Only a node forced under both evaluations of the 0.85 rule
            // is genuinely forced regardless of training.
            bool isCompetencyIndependentForceBuy = ctx.CompetencyIndependentForceBuyNodeIds != null &&
                ctx.CompetencyIndependentForceBuyNodeIds.Contains(node.NodeId);
            bool cheapestCraftUntrained = !isCompetencyIndependentForceBuy &&
                cheapestCraftOptionOverall != null &&
                ctx.BestRatingByDiscipline != null &&
                !CraftCompetencyEvaluator.AccountCanCraft(
                    cheapestCraftOptionOverall.Disciplines, cheapestCraftOptionOverall.MinRating, ctx.BestRatingByDiscipline);

            // Raw cost breakdowns for every feasible source, computed
            // unconditionally and never fed back into any comparison (see
            // PillSourceCostBreakdown).
            var tpBreakdown = canBuyTp
                ? new PillSourceCostBreakdown
                {
                    IsAvailable = true,
                    RawCoin = buyTotalCost.Value,
                    DecisionValue = buyTotalCost.Value,
                }
                : new PillSourceCostBreakdown { IsAvailable = false };

            PillSourceCostBreakdown vendorBreakdown;
            if (vendorEvaluation.BestComparableValue.HasValue)
            {
                vendorBreakdown = BuildVendorCostBreakdown(
                    vendorEvaluation.BestComparableCoinCost, vendorEvaluation.BestComparableCurrencyCosts, vendorEvaluation.BestComparableItemCosts,
                    vendorEvaluation.BestComparableValue);
            }
            else if (vendorEvaluation.FallbackCoinCost.HasValue)
            {
                // Fallback tier: an unvalued non-coin currency line exists
                // on this offer - DecisionValue stays null, mirroring
                // hasUnvaluedCurrency's craft-side treatment.
                vendorBreakdown = BuildVendorCostBreakdown(
                    vendorEvaluation.FallbackCoinCost, vendorEvaluation.FallbackCurrencyCosts, vendorEvaluation.FallbackItemCosts, null);
            }
            else
            {
                vendorBreakdown = new PillSourceCostBreakdown { IsAvailable = false };
            }

            // Raw diagnostics for OwnedMaterialsForceBuyPrePass, recorded
            // regardless of decision. CraftCost is the same tier/
            // competency-resolved pair the Craft commit sites use - a
            // competency-blind figure here would let the 0.85 comparison
            // run on a craft cost the real solve would never commit to.
            if (ctx.CostDiagnostics != null)
            {
                ctx.CostDiagnostics[node.NodeId] = (buyTotalCost, craftBreakdownDecisionValue ?? autoPickCraftRealCost);
            }

            // The competency-blind twin of the write above, letting the
            // pre-pass run its second 0.85 evaluation without a second
            // Solve() call.
            if (ctx.RawCraftCostDiagnostics != null)
            {
                ctx.RawCraftCostDiagnostics[node.NodeId] = cheapestCraftRealCostOverall;
            }

            // True when any direct ingredient of this breakdown's recipe
            // was reduced by owned account stock (see
            // PillSourceCostBreakdown.RawQuantitiesReducedByOwnedStock).
            bool craftIngredientsReducedByOwnedStock = autoPickCraftOption != null &&
                ctx.OwnedQuantityUsedByNode != null &&
                AnyIngredientReducedByOwnedStock(autoPickCraftOption, ctx.OwnedQuantityUsedByNode);
            var craftBreakdown = autoPickCraftOption != null
                ? BuildCraftCostBreakdown(autoPickCraftOption, craftBreakdownDecisionValue, craftIngredientsReducedByOwnedStock)
                : new PillSourceCostBreakdown { IsAvailable = false };

            // cost = real coin (Decision.TotalCost); comparisonValue =
            // parent-comparison value; Commit returns comparisonValue.
            // hasUnvaluedCurrency is passed true only from the
            // fallback-tier commit sites.
            long? Commit(
                AcquisitionSource src, long? cost, long? comparisonValue,
                int recipeId, List<CostLine> vendorCurrencyCosts,
                VendorBatchSolver.VendorOfferBatch? vendorBatch = null,
                // Only passed non-default by the BuyFromVendor call sites.
                List<VendorItemCostLine> vendorItemCosts = null,
                bool vendorHasRawCoin = false,
                bool hasUnvaluedCurrency = false)
            {
                ctx.Memo[node.NodeId] = new Decision
                {
                    Source = src,
                    TotalCost = cost,
                    ComparisonValue = comparisonValue,
                    RecipeId = recipeId,
                    VendorCurrencyCosts = vendorCurrencyCosts,
                    VendorItemCosts = vendorItemCosts,
                    VendorHasRawCoin = vendorHasRawCoin,
                    CanCraft = canCraft,
                    CanBuyTp = canBuyTp,
                    CanBuyVendor = canBuyVendor,
                    HasUnvaluedCurrency = hasUnvaluedCurrency,
                    VendorBatch = vendorBatch,
                    // buyPriceSideFellBack is computed unconditionally, so
                    // gate on the committed Source actually being BuyFromTp.
                    PriceSideFellBack = src == AcquisitionSource.BuyFromTp && buyPriceSideFellBack,
                    CraftCostBreakdown = craftBreakdown,
                    BuyFromTpCostBreakdown = tpBreakdown,
                    BuyFromVendorCostBreakdown = vendorBreakdown,
                    CraftExcludedByCompetency = craftExcludedByCompetency,
                    CraftExcludedRealCost = craftExcludedByCompetency ? autoPickCraftRealCost : null,
                    CraftExcludedDisciplines = craftExcludedByCompetency ? autoPickCraftOption?.Disciplines : null,
                    CraftExcludedMinRating = craftExcludedByCompetency ? (autoPickCraftOption?.MinRating ?? 0) : 0,
                    CheapestCraftUntrained = cheapestCraftUntrained,
                    CheapestCraftRealCost = cheapestCraftUntrained ? cheapestCraftRealCostOverall : null,
                    CheapestCraftDisciplines = cheapestCraftUntrained ? cheapestCraftOptionOverall?.Disciplines : null,
                    CheapestCraftMinRating = cheapestCraftUntrained ? (cheapestCraftOptionOverall?.MinRating ?? 0) : 0,
                };
                return comparisonValue;
            }

            // A user override wins whenever it is feasible for this node;
            // infeasible overrides are ignored and the best path applies.
            if (ctx.Overrides != null &&
                ctx.Overrides.TryGetValue(node.NodeId, out var forced))
            {
                if (forced == AcquisitionSource.Craft && canCraft)
                {
                    // Comparable-first, fallback otherwise - same
                    // precedence as VendorBatchSolver's override handling.
                    return bestComparable.Cost.HasValue
                        ? Commit(AcquisitionSource.Craft, bestComparable.RealCost, bestComparable.Cost, bestComparable.RecipeId, null)
                        : Commit(AcquisitionSource.Craft, bestFallback.RealCost, bestFallback.Cost, bestFallback.RecipeId, null, hasUnvaluedCurrency: true);
                }

                if (forced == AcquisitionSource.BuyFromTp && canBuyTp)
                {
                    return Commit(AcquisitionSource.BuyFromTp, buyTotalCost, buyTotalCost, 0, null);
                }

                if (forced == AcquisitionSource.BuyFromVendor && canBuyVendor)
                {
                    return vendorEvaluation.BestComparableValue.HasValue
                        ? Commit(AcquisitionSource.BuyFromVendor, vendorEvaluation.BestComparableCoinCost, vendorEvaluation.BestComparableValue, 0, vendorEvaluation.BestComparableCurrencyCosts, vendorEvaluation.BestComparableBatch, vendorEvaluation.BestComparableItemCosts, vendorEvaluation.BestComparableHasRawCoin)
                        : Commit(AcquisitionSource.BuyFromVendor, vendorEvaluation.FallbackCoinCost, vendorEvaluation.FallbackCoinCost, 0, vendorEvaluation.FallbackCurrencyCosts, vendorEvaluation.FallbackBatch, vendorEvaluation.FallbackItemCosts, vendorEvaluation.FallbackHasRawCoin, hasUnvaluedCurrency: true);
                }
            }

            // Three-way comparison: vendor (coin + valued currency lines)
            // vs TP buy vs craft. Only the comparable craft cost
            // participates - craftBreakdownDecisionValue is null for a
            // fallback-tier pick, so that arm contributes nothing here,
            // exactly like a fallback-tier vendor offer.
            var source = PickCheapest(
                buyTotalCost,
                craftExcludedFromAutoPick ? null : craftBreakdownDecisionValue,
                vendorEvaluation.BestComparableValue);

            if (source == AcquisitionSource.BuyFromVendor)
            {
                return Commit(AcquisitionSource.BuyFromVendor, vendorEvaluation.BestComparableCoinCost, vendorEvaluation.BestComparableValue, 0, vendorEvaluation.BestComparableCurrencyCosts, vendorEvaluation.BestComparableBatch, vendorEvaluation.BestComparableItemCosts, vendorEvaluation.BestComparableHasRawCoin);
            }

            if (source == AcquisitionSource.BuyFromTp)
            {
                return Commit(AcquisitionSource.BuyFromTp, buyTotalCost, buyTotalCost, 0, null);
            }

            if (source == AcquisitionSource.Craft)
            {
                // Commit the same recipe PickCheapest just compared - the
                // competent comparable option when one exists, or the raw
                // one when nothing competent exists and craft had no
                // genuine alternative to lose to.
                return Commit(AcquisitionSource.Craft, autoPickCraftRealCost, craftBreakdownDecisionValue, autoPickRecipeId, null);
            }

            // Fallback: nothing comparable beat buy (UnknownSource here
            // implies buyCost, the comparable craft cost, and
            // vendorEvaluation.BestComparableValue are all null). A fallback-tier craft
            // or vendor offer is a concrete acquisition even though its
            // full cost cannot honestly be compared with coin, and is
            // used as a last resort. When both exist, the numerically
            // cheaper REAL coin cost wins and an exact tie keeps vendor -
            // comparing real cost (never the valuation-tainted craftCost)
            // keeps both sides on the same scale. Force-buy-only nodes
            // never fall back to craft. Otherwise this is gw2e's "Not
            // sold or crafted".
            //
            // autoPickCraftRealCost is gated to the fallback tier only
            // (craftBreakdownDecisionValue null): a comparable-tier craft
            // that legitimately lost PickCheapest must not get a second
            // chance here.
            long? fallbackCraftCost = craftExcludedFromAutoPick || craftBreakdownDecisionValue.HasValue
                ? null
                : autoPickCraftRealCost;

            if (fallbackCraftCost.HasValue || vendorEvaluation.FallbackCoinCost.HasValue)
            {
                // A BARTER line contributes nothing to an offer's coin
                // part, so that part is a PARTIAL accounting while a craft
                // route's real cost is a complete one; ranking them against
                // each other lets an offer win on a price missing most of
                // itself. An unvalued non-coin CURRENCY line deliberately
                // does not count here: it has no coin equivalent by
                // invariant rather than by missing data, and both sides
                // omit one the same way. docs/ARCHITECTURE.md sections 7.1
                // and 8.
                bool fallbackVendorOmitsItemCost =
                    HasBarterItemCost(vendorEvaluation.FallbackItemCosts);

                // The second, price-free reason an offer cannot beat a craft
                // route: it charges that route's own ingredients and a fee on
                // top (VendorOfferDomination). Same treatment as an omitted
                // cost - the route stays offered, it just cannot win here.
                bool fallbackVendorIsWorseCopyOfCraft =
                    vendorEvaluation.FallbackIsDominatedByCraft;

                bool fallbackVendorWins = vendorEvaluation.FallbackCoinCost.HasValue &&
                    (!fallbackCraftCost.HasValue ||
                     (!fallbackVendorOmitsItemCost &&
                      !fallbackVendorIsWorseCopyOfCraft &&
                      vendorEvaluation.FallbackCoinCost.Value <= fallbackCraftCost.Value));

                if (fallbackVendorWins)
                {
                    return Commit(AcquisitionSource.BuyFromVendor, vendorEvaluation.FallbackCoinCost, vendorEvaluation.FallbackCoinCost, 0, vendorEvaluation.FallbackCurrencyCosts, vendorEvaluation.FallbackBatch, vendorEvaluation.FallbackItemCosts, vendorEvaluation.FallbackHasRawCoin, hasUnvaluedCurrency: true);
                }

                // fallbackCraftCost == autoPickCraftRealCost here (the
                // fallback-tier commit sites use the same real value for
                // both cost and comparisonValue).
                return Commit(AcquisitionSource.Craft, autoPickCraftRealCost, autoPickCraftRealCost, autoPickRecipeId, null, hasUnvaluedCurrency: true);
            }

            return Commit(AcquisitionSource.UnknownSource, null, null, 0, null);
        }

        /// <summary>
        /// What one unit of vendor cost-line item <paramref name="itemId"/>
        /// costs to acquire, by running the SAME <see cref="Evaluate"/> over
        /// that item's own quantity-1 subtree. This is the whole point of the
        /// expansion: a cost line and a recipe ingredient are costed by one
        /// code path, so the craft route and the vendor route that mirrors it
        /// are genuinely comparable instead of one of them being free.
        /// <para>
        /// Returns null - the pre-expansion behaviour, a barter line worth no
        /// coin - when there is no subtree, when the recursion is cut, or
        /// when the subtree has no priceable route of its own.
        /// </para>
        /// </summary>
        private CostLineUnitValue ResolveCostLineUnitValue(int itemId, CostLineResolutionState state)
        {
            if (state.Memo.TryGetValue(itemId, out var cached))
            {
                return cached;
            }

            if (!state.Subtrees.TryGetValue(itemId, out var subtree) || subtree == null)
            {
                return null;
            }

            // Three independent bounds, any one of which alone terminates the
            // recursion; see CostLineResolutionState. A cut is memoized like
            // any other answer, which is what makes the total work linear.
            if (state.Visiting.Count >= state.MaxDepth || state.Budget <= 0 || !state.Visiting.Add(itemId))
            {
                state.Memo[itemId] = null;
                return null;
            }

            CostLineUnitValue value;
            try
            {
                state.Budget--;
                Evaluate(subtree, state.Context);

                if (!state.Context.Memo.TryGetValue(subtree.NodeId, out var decision) ||
                    !decision.TotalCost.HasValue ||
                    decision.TotalCost.Value <= 0L)
                {
                    // No priceable route at all, or one whose whole cost is
                    // unpriceable and therefore summed to zero: honestly
                    // unresolved rather than a fabricated free acquisition.
                    value = null;
                }
                else
                {
                    long realCoin = decision.TotalCost.Value;

                    // ComparisonValue can only be read as "real coin plus a
                    // decision-only remainder" on a COMPARABLE decision; a
                    // fallback-tier one sets both to the same real figure and
                    // has no remainder to carry.
                    long comparisonExtra = decision.ComparisonValue.HasValue && !decision.HasUnvaluedCurrency
                        ? decision.ComparisonValue.Value - realCoin
                        : 0L;

                    value = new CostLineUnitValue
                    {
                        RealCoin = realCoin,
                        ComparisonExtra = comparisonExtra > 0L ? comparisonExtra : 0L,
                        HasUnvaluedCost = decision.HasUnvaluedCurrency || HasBarterItemCost(decision),
                    };
                }
            }
            finally
            {
                state.Visiting.Remove(itemId);
            }

            state.Memo[itemId] = value;
            return value;
        }

        /// <summary>
        /// The recipe phase of <see cref="Evaluate"/>: every option this
        /// node has, costed and ranked into the four trackers the decision
        /// below reads - cheapest comparable, cheapest fallback, and the
        /// cheapest of each that the account can actually craft.
        /// </summary>
        /// <remarks>
        /// Recurses into <see cref="Evaluate"/> for every Item ingredient,
        /// so the memo is filled for the whole subtree before this returns.
        /// Pure code motion out of Evaluate - no arithmetic, comparison,
        /// tie-break or ordering here differs from the inline version it
        /// replaced (see Goldens/plan-solver).
        /// </remarks>
        private RecipeCandidates SelectBestRecipes(RecipeNode node, EvaluateContext ctx)
        {
            // Evaluate recipe options. Every non-currency ingredient of
            // every recipe is always evaluated - no short-circuit on the
            // first unpriceable ingredient - so every node always gets a
            // memo entry, even under a recipe this node doesn't choose.
            //
            // An unpriceable ingredient doesn't disqualify its recipe
            // (gw2e's craftPrice = sum(component.craftResultPrice || 0)):
            // it contributes zero, so craftCost is defined whenever
            // recipes exist. Coin totals are then deliberately partial;
            // the descendant still surfaces with its own decision.
            //
            // Recipe candidates split into COMPARABLE and FALLBACK tiers,
            // mirroring EvaluateVendorOffers: a recipe with an unvalued
            // Currency ingredient never competes on coin cost (its real
            // cost is unknown, and ranking by priced ingredients alone
            // would hide it), but stays offered (CanCraft true) and is
            // used when nothing coin-comparable exists.
            var bestComparable = default(BestRecipeTracker);
            var bestFallback = default(BestRecipeTracker);

            // Tracked in addition to bestComparable/bestFallback,
            // restricted to recipes that pass AccountCanCraft: gating the
            // auto-pick on only the single cheapest option's competency
            // wrongly excluded the whole Craft arm even when a costlier
            // sibling recipe in the same tier was fully craftable. The
            // unfiltered bests still feed canCraft and manual overrides.
            var bestCompetentComparable = default(BestRecipeTracker);
            var bestCompetentFallback = default(BestRecipeTracker);

            foreach (var recipe in node.Recipes)
            {
                // craftCost sums ingredient ComparisonValues and drives
                // recipe selection; craftRealCost sums the same
                // ingredients' real TotalCost (read back from memo) and
                // becomes the committed decision's real coin cost.
                //
                // EV pricing (fractional Mystic Forge recipes): ingredient
                // quantities were already scaled by RecipeService using
                // CraftsNeeded = ceil(quantity / ExpectedOutputCount), so
                // the summed costs already reflect the expected number of
                // attempts; adjusting again here would double-amortize.
                // A no-op for ordinary recipes (ExpectedOutputCount
                // defaults to OutputCount).
                long craftCost = 0L;
                long craftRealCost = 0L;
                // Accumulated separately and folded in only if this recipe
                // stays comparable - mirrors EvaluateVendorOffers'
                // valuationCopper/allValued split: a fallback-tier recipe
                // discards ALL valuation, never partially retains it.
                long valuationCopper = 0L;
                bool hasUnvaluedCurrency = false;

                foreach (var ingredient in recipe.Ingredients)
                {
                    if (ingredient.IngredientType == "Currency")
                    {
                        // A Currency ingredient tagged with the coin
                        // currency id IS real copper - CurrencyValuation
                        // hard-throws if keyed on that id, so without this
                        // branch a coin-typed ingredient would always
                        // demote its recipe to the fallback tier.
                        // Contributes to both comparison and real cost.
                        if (ingredient.Id == Gw2Constants.CoinCurrencyId)
                        {
                            craftCost += (long)ingredient.Quantity;
                            craftRealCost += (long)ingredient.Quantity;
                            continue;
                        }

                        // Currencies contribute to the craft-vs-buy
                        // decision value only (via user valuation), never
                        // to the displayed real coin cost - the plan's
                        // gold total never invents an exchange rate. An
                        // unvalued currency demotes the recipe to the
                        // fallback tier instead of contributing zero.
                        if (ctx.CurrencyValuation != null &&
                            ctx.CurrencyValuation.TryGetCopperValue(ingredient.Id, out long copperPerUnit))
                        {
                            try
                            {
                                valuationCopper = checked(valuationCopper + (long)ingredient.Quantity * copperPerUnit);
                            }
                            catch (OverflowException)
                            {
                                // Absurd valuation input; demote to fallback
                                // rather than crash - mirrors
                                // EvaluateVendorOffers.
                                hasUnvaluedCurrency = true;
                            }
                        }
                        else
                        {
                            hasUnvaluedCurrency = true;
                        }

                        continue;
                    }

                    if (ingredient.IngredientType != "Item")
                    {
                        // Non-Item ingredient (GuildUpgrade/unrecognized):
                        // its id space has no relationship to prices or
                        // valuations, so it demotes the recipe to the
                        // fallback tier and contributes zero.
                        hasUnvaluedCurrency = true;
                        continue;
                    }

                    long? ingredientCost = Evaluate(ingredient, ctx);
                    craftCost += ingredientCost ?? 0L;
                    var ingredientDecision = ctx.Memo[ingredient.NodeId];
                    craftRealCost += ingredientDecision.TotalCost ?? 0L;

                    // Transitive fallback-tier propagation: a chosen
                    // ingredient whose own decision is fallback-tier
                    // taints this recipe too - otherwise a currency cost
                    // one Craft level down would launder back into a
                    // comparable-looking ancestor.
                    if (ingredientDecision.HasUnvaluedCurrency)
                    {
                        hasUnvaluedCurrency = true;
                    }
                }

                // Valuation only reaches craftCost when this recipe stays
                // comparable - mirrors EvaluateVendorOffers' allValued gate.
                if (!hasUnvaluedCurrency)
                {
                    // craftCost and valuationCopper can each stay in range
                    // while their sum overflows; demote to fallback rather
                    // than crash.
                    try
                    {
                        craftCost = checked(craftCost + valuationCopper);
                    }
                    catch (OverflowException)
                    {
                        hasUnvaluedCurrency = true;
                    }
                }

                bool competent = CraftCompetencyEvaluator.AccountCanCraft(
                    recipe.Disciplines, recipe.MinRating, ctx.BestRatingByDiscipline);
                if (hasUnvaluedCurrency)
                {
                    // Ranked on real cost only (never the valuation-
                    // tainted craftCost), passed for BOTH tracker slots so
                    // the returned ComparisonValue can never carry hidden
                    // valuation upward.
                    bestFallback.Offer(craftRealCost, craftRealCost, recipe);
                    if (competent)
                    {
                        bestCompetentFallback.Offer(craftRealCost, craftRealCost, recipe);
                    }
                }
                else
                {
                    bestComparable.Offer(craftCost, craftRealCost, recipe);
                    if (competent)
                    {
                        bestCompetentComparable.Offer(craftCost, craftRealCost, recipe);
                    }
                }
            }

            return new RecipeCandidates(
                bestComparable, bestFallback, bestCompetentComparable, bestCompetentFallback);
        }

        /// <summary>
        /// Pick cheapest among TP buy, craft, and vendor. TP buy is the
        /// baseline and wins every tie; craft or vendor win only when
        /// strictly cheaper, and a missing buy price counts as "beats buy"
        /// (force-craft). When both craft and vendor beat buy, the
        /// numerically cheaper wins; an exact craft/vendor tie keeps
        /// vendor. Returns UnknownSource if none are available.
        /// </summary>
        private static AcquisitionSource PickCheapest(
            long? buyCost, long? craftCost, long? vendorCost)
        {
            bool craftBeatsBuy = craftCost.HasValue &&
                (!buyCost.HasValue || craftCost.Value < buyCost.Value);
            bool vendorBeatsBuy = vendorCost.HasValue &&
                (!buyCost.HasValue || vendorCost.Value < buyCost.Value);

            if (craftBeatsBuy && vendorBeatsBuy)
            {
                return vendorCost.Value <= craftCost.Value
                    ? AcquisitionSource.BuyFromVendor
                    : AcquisitionSource.Craft;
            }

            if (vendorBeatsBuy)
            {
                return AcquisitionSource.BuyFromVendor;
            }

            if (craftBeatsBuy)
            {
                return AcquisitionSource.Craft;
            }

            if (buyCost.HasValue)
            {
                return AcquisitionSource.BuyFromTp;
            }

            return AcquisitionSource.UnknownSource;
        }

        /// <summary>
        /// True when a committed vendor decision carries at least one
        /// BARTER cost line - an untradeable item with no Trading Post
        /// price, marked by a null VendorItemCostLine.GoldValue.
        /// </summary>
        private static bool HasBarterItemCost(Decision decision)
        {
            return HasBarterItemCost(decision.VendorItemCosts);
        }

        /// <summary>
        /// The same test against a vendor evaluation's raw item cost lines,
        /// before any decision has been committed from them. Indexed rather
        /// than foreach'd: the parameter is an interface, so foreach would
        /// box a heap enumerator on every call, and the decision overload
        /// above runs once per vendor step in the aggregation walk.
        /// </summary>
        private static bool HasBarterItemCost(IReadOnlyList<VendorItemCostLine> itemCosts)
        {
            if (itemCosts == null)
            {
                return false;
            }

            for (int i = 0; i < itemCosts.Count; i++)
            {
                if (!itemCosts[i].GoldValue.HasValue)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Decomposes a winning-or-fallback vendor offer's already-
        /// evaluated cost fields into a PillSourceCostBreakdown. RawCoin
        /// subtracts each item line's GoldValue back out of coinCost so
        /// the item's raw quantity is what competes in strict-domination
        /// comparisons, not its TP-valued gold. A barter line (null
        /// GoldValue) contributed nothing to coinCost in the first place,
        /// so there is nothing to subtract for it - only its raw quantity
        /// competes, which is exactly the intent for every item line.
        /// </summary>
        private static PillSourceCostBreakdown BuildVendorCostBreakdown(
            long? coinCost, List<CostLine> currencyCosts, List<VendorItemCostLine> itemCosts, long? decisionValue)
        {
            long itemFoldedValue = 0L;
            var lines = new List<CostLine>();
            if (currencyCosts != null)
            {
                lines.AddRange(currencyCosts);
            }

            if (itemCosts != null)
            {
                foreach (var line in itemCosts)
                {
                    itemFoldedValue += line.GoldValue ?? 0L;
                    lines.Add(new CostLine { Type = "Item", Id = line.ItemId, Count = line.Quantity });
                }
            }

            return new PillSourceCostBreakdown
            {
                IsAvailable = true,
                RawCoin = (coinCost ?? 0L) - itemFoldedValue,
                CostLines = lines,
                DecisionValue = decisionValue,
            };
        }

        /// <summary>
        /// Decomposes a candidate recipe's direct ingredient list into a
        /// PillSourceCostBreakdown: Currency ingredients become raw
        /// currency lines (or RawCoin for the coin id), Item ingredients
        /// become raw item lines at their stated quantity - directly
        /// comparable to a vendor offer's cost lines with no pricing or
        /// recursion. A GuildUpgrade/unrecognized ingredient has no
        /// representable line and marks the breakdown IsIncomplete, so an
        /// unrepresentable cost never manufactures a false domination
        /// claim. Duplicate (Type, Id) entries are summed.
        /// </summary>
        /// <param name="rawQuantitiesReducedByOwnedStock">
        /// Stamped straight onto the returned breakdown; computed by the
        /// caller, which has access to ownedQuantityUsedByNode.
        /// </param>
        private static PillSourceCostBreakdown BuildCraftCostBreakdown(
            RecipeOption option, long? decisionValue, bool rawQuantitiesReducedByOwnedStock = false)
        {
            long rawCoin = 0L;
            bool isIncomplete = false;
            var lineTotals = new Dictionary<(string Type, int Id), int>();

            foreach (var ingredient in option.Ingredients)
            {
                if (ingredient.IngredientType == "Currency")
                {
                    if (ingredient.Id == Gw2Constants.CoinCurrencyId)
                    {
                        rawCoin += ingredient.Quantity;
                        continue;
                    }

                    var key = ("Currency", ingredient.Id);
                    lineTotals[key] = lineTotals.TryGetValue(key, out int existing)
                        ? existing + ingredient.Quantity
                        : ingredient.Quantity;
                }
                else if (ingredient.IngredientType == "Item")
                {
                    var key = ("Item", ingredient.Id);
                    lineTotals[key] = lineTotals.TryGetValue(key, out int existing)
                        ? existing + ingredient.Quantity
                        : ingredient.Quantity;
                }
                else
                {
                    // GuildUpgrade/unrecognized type - no representable
                    // line (see this method's own doc comment).
                    isIncomplete = true;
                }
            }

            var lines = new List<CostLine>(lineTotals.Count);
            foreach (var kvp in lineTotals)
            {
                lines.Add(new CostLine { Type = kvp.Key.Type, Id = kvp.Key.Id, Count = kvp.Value });
            }

            return new PillSourceCostBreakdown
            {
                IsAvailable = true,
                RawCoin = rawCoin,
                CostLines = lines,
                DecisionValue = decisionValue,
                IsIncomplete = isIncomplete,
                RawQuantitiesReducedByOwnedStock = rawQuantitiesReducedByOwnedStock,
            };
        }

        /// <summary>
        /// True when at least one of <paramref name="option"/>'s direct
        /// Item ingredients was actually reduced by owned stock.
        /// Reference-keyed: the ingredient nodes are the same instances
        /// InventoryReducer walked. Currency ingredients are skipped.
        /// </summary>
        private static bool AnyIngredientReducedByOwnedStock(
            RecipeOption option, Dictionary<RecipeNode, int> ownedQuantityUsedByNode)
        {
            foreach (var ingredient in option.Ingredients)
            {
                if (ingredient.IngredientType == "Item" &&
                    ownedQuantityUsedByNode.TryGetValue(ingredient, out int used) &&
                    used > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void Collect(RecipeNode node, CollectContext ctx, ref int craftCounter)
        {
            if (node.IngredientType == "Currency")
            {
                // A coin-typed Currency node is real copper, already in
                // its consuming Craft decision's TotalCost; it accumulates
                // into currencyMap once per occurrence like any currency,
                // but the conversion below routes it into totalCoinCost
                // and excludes it from currencyCosts (coin has its own
                // display) so all cost surfaces agree.
                if (ctx.CurrencyMap.ContainsKey(node.Id))
                {
                    ctx.CurrencyMap[node.Id] = checked(ctx.CurrencyMap[node.Id] + node.Quantity);
                }
                else
                {
                    ctx.CurrencyMap[node.Id] = node.Quantity;
                }

                return;
            }

            if (node.IngredientType != "Item")
            {
                // Non-Item node (GuildUpgrade/unrecognized): never
                // accumulates into currencyMap and carries no memo entry,
                // so no step generation runs for it.
                return;
            }

            // A Quantity == 0 "Item" node draws no demand and must never
            // generate a step - matches CraftingTreeBuilder.BuildNode's
            // own Quantity == 0 collapse, whether zeroed by
            // InventoryReducer or AchievementBitDedupPrePass. Without this
            // guard, a zeroed node whose "real" counterpart resolves to a
            // different stepKey leaves a "0 units, 0 cost" ghost row.
            //
            // Relied-on invariant: every "Item" node reaching here with
            // Quantity == 0 already has empty Recipes (both zeroing sites
            // pair Quantity = 0 with Recipes.Clear()). If that pairing is
            // ever broken, this guard would skip the node's children and
            // drop their real costs - keep any new Quantity-zeroing code
            // paired with clearing Recipes.
            if (node.Quantity == 0)
            {
                return;
            }

            // An ignored item generates no step or shopping row; Evaluate
            // already committed a zero-cost memo entry without recursing.
            if (ctx.IgnoredItemIds != null && ctx.IgnoredItemIds.Contains(node.Id))
            {
                return;
            }

            if (!ctx.Memo.TryGetValue(node.NodeId, out var decision))
            {
                return;
            }

            // The synthetic multi-item wrapper root is never a real
            // acquisition: recurse straight into its recipe's ingredients
            // (the N real roots) without generating a step/craftOrder
            // entry for the wrapper itself. Evaluate always force-crafts
            // it (recipe, no buy price); the check still guards against
            // future change.
            if (node.Id == Gw2Constants.MultiItemWrapperItemId &&
                decision.Source == AcquisitionSource.Craft)
            {
                var wrapperRecipe = node.Recipes.FirstOrDefault(r => r.RecipeId == decision.RecipeId);
                if (wrapperRecipe != null)
                {
                    foreach (var itemRoot in wrapperRecipe.Ingredients)
                    {
                        Collect(itemRoot, ctx, ref craftCounter);
                    }
                }

                return;
            }

            if (decision.Source == AcquisitionSource.Craft)
            {
                // Recurse into the chosen recipe's ingredients first (bottom-up)
                var chosenRecipe = node.Recipes.FirstOrDefault(r => r.RecipeId == decision.RecipeId);
                if (chosenRecipe != null)
                {
                    foreach (var ingredient in chosenRecipe.Ingredients)
                    {
                        Collect(ingredient, ctx, ref craftCounter);
                    }
                }

                // Record craft order (first time seeing this item+recipe as craft)
                var craftOrderKey = (node.Id, decision.RecipeId);
                if (!ctx.CraftOrder.ContainsKey(craftOrderKey))
                {
                    ctx.CraftOrder[craftOrderKey] = craftCounter++;
                }

                var stepKey = (node.Id, AcquisitionSource.Craft, decision.RecipeId);
                AggregateStep(ctx.StepMap, stepKey, node, decision, ctx.VendorBatchTracking, ctx.VendorOccurrences, ctx.CraftOccurrences);
            }
            else if (decision.Source == AcquisitionSource.BuyFromVendor)
            {
                // Vendor currency costs are folded into currencyMap once,
                // after every merged vendor step's aggregate-then-ceil
                // cost is known (FinalizeVendorBatches); folding the
                // per-occurrence costs here would re-introduce the
                // overcount that pass exists to fix.
                var stepKey = (node.Id, AcquisitionSource.BuyFromVendor, 0);
                AggregateStep(ctx.StepMap, stepKey, node, decision, ctx.VendorBatchTracking, ctx.VendorOccurrences, ctx.CraftOccurrences);
            }
            else
            {
                var stepKey = (node.Id, decision.Source, 0);
                AggregateStep(ctx.StepMap, stepKey, node, decision, ctx.VendorBatchTracking, ctx.VendorOccurrences, ctx.CraftOccurrences);
            }
        }

        private void AggregateStep(
            Dictionary<(int, AcquisitionSource, int), PlanStep> stepMap,
            (int, AcquisitionSource, int) stepKey,
            RecipeNode node,
            Decision decision,
            Dictionary<(int, AcquisitionSource, int), VendorBatchSolver.VendorBatchState> vendorBatchTracking,
            Dictionary<(int, AcquisitionSource, int), List<(int NodeId, int Quantity)>> vendorOccurrences,
            Dictionary<(int, AcquisitionSource, int), List<int>> craftOccurrences)
        {
            if (decision.Source == AcquisitionSource.Craft)
            {
                // Remembers each occurrence's NodeId that fed this merged
                // Craft stepKey, in first-seen (DFS) order, for
                // RefreshCraftStepCosts - the Craft twin of the vendor
                // bookkeeping below.
                if (!craftOccurrences.TryGetValue(stepKey, out var craftOccurrenceList))
                {
                    craftOccurrenceList = new List<int>();
                    craftOccurrences[stepKey] = craftOccurrenceList;
                }

                craftOccurrenceList.Add(node.NodeId);
            }

            if (decision.Source == AcquisitionSource.BuyFromVendor && decision.VendorBatch.HasValue)
            {
                var batch = decision.VendorBatch.Value;

                if (vendorBatchTracking.TryGetValue(stepKey, out var trackedState))
                {
                    if (!trackedState.Conflict && !_vendorBatchSolver.VendorBatchesEqual(trackedState.Batch, batch))
                    {
                        // Ratchet only: a later occurrence agreeing with the
                        // tracked batch must not clear a conflict a prior
                        // occurrence already raised.
                        trackedState.Conflict = true;
                    }
                }
                else
                {
                    vendorBatchTracking[stepKey] = new VendorBatchSolver.VendorBatchState
                    {
                        Batch = batch,
                        Conflict = false,
                    };
                }

                // Remembers each occurrence's NodeId and Quantity that fed
                // this merged vendor stepKey, in first-seen (DFS) order,
                // so AllocateVendorNodeCosts can redistribute the
                // corrected merged total back to each memo entry.
                if (!vendorOccurrences.TryGetValue(stepKey, out var occurrenceList))
                {
                    occurrenceList = new List<(int NodeId, int Quantity)>();
                    vendorOccurrences[stepKey] = occurrenceList;
                }

                occurrenceList.Add((node.NodeId, node.Quantity));
            }

            if (stepMap.TryGetValue(stepKey, out var existing))
            {
                existing.Quantity += node.Quantity;
                existing.TotalCost = decision.TotalCost.HasValue
                    ? existing.TotalCost + decision.TotalCost.Value
                    : existing.TotalCost;
                if ((existing.Source == AcquisitionSource.BuyFromTp ||
                     existing.Source == AcquisitionSource.BuyFromVendor) &&
                    existing.Quantity > 0)
                {
                    existing.UnitCost = existing.TotalCost / existing.Quantity;
                }

                if (decision.Source == AcquisitionSource.BuyFromVendor)
                {
                    existing.VendorCurrencyCosts = _vendorBatchSolver.MergeVendorCurrencyCosts(
                        existing.VendorCurrencyCosts, decision.VendorCurrencyCosts);
                    existing.VendorBarterItemCosts = _vendorBatchSolver.MergeVendorBarterItemCosts(
                        existing.VendorBarterItemCosts, decision.VendorItemCosts);

                    // One-way ratchet, like the batch Conflict flag above:
                    // the merged step's coin figure is incomplete as soon
                    // as ANY occurrence paid partly in barter.
                    existing.VendorHasBarterItemCost |= HasBarterItemCost(decision);
                }
            }
            else
            {
                long unitCost = 0;
                if ((decision.Source == AcquisitionSource.BuyFromTp ||
                     decision.Source == AcquisitionSource.BuyFromVendor) &&
                    node.Quantity > 0 && decision.TotalCost.HasValue)
                {
                    unitCost = decision.TotalCost.Value / node.Quantity;
                }

                stepMap[stepKey] = new PlanStep
                {
                    ItemId = node.Id,
                    Quantity = node.Quantity,
                    Source = decision.Source,
                    UnitCost = unitCost,
                    TotalCost = decision.TotalCost ?? 0L,
                    RecipeId = decision.RecipeId,
                    VendorCurrencyCosts = decision.Source == AcquisitionSource.BuyFromVendor
                        ? _vendorBatchSolver.MergeVendorCurrencyCosts(null, decision.VendorCurrencyCosts)
                        : null,
                    VendorBarterItemCosts = decision.Source == AcquisitionSource.BuyFromVendor
                        ? _vendorBatchSolver.MergeVendorBarterItemCosts(null, decision.VendorItemCosts)
                        : null,
                    VendorHasBarterItemCost = decision.Source == AcquisitionSource.BuyFromVendor &&
                        HasBarterItemCost(decision),
                };
            }
        }

        /// <summary>
        /// Marks every occurrence of a merged (2+ occurrence) vendor
        /// step's memo entry VendorComponentCostsUnreliable, so
        /// CraftingTreeBuilder never synthesizes a cost-component leaf
        /// from the stale pre-merge numbers. Runs strictly after
        /// AllocateVendorNodeCosts, using the same
        /// VendorOfferOutputCount &gt; 0 gate that decides whether a step
        /// was corrected; a single-occurrence step's share always equals
        /// step.TotalCost, so nothing there is stale. Read-only toward
        /// VendorBatchSolver.
        /// </summary>
        private static void FlagUnreliableVendorComponentCosts(
            Dictionary<(int, AcquisitionSource, int), PlanStep> stepMap,
            Dictionary<(int, AcquisitionSource, int), List<(int NodeId, int Quantity)>> vendorOccurrences,
            Dictionary<int, Decision> memo)
        {
            foreach (var kvp in vendorOccurrences)
            {
                var occurrences = kvp.Value;
                if (occurrences.Count <= 1)
                {
                    continue;
                }

                if (!stepMap.TryGetValue(kvp.Key, out var step) || step.VendorOfferOutputCount <= 0)
                {
                    continue;
                }

                foreach (var (nodeId, _) in occurrences)
                {
                    if (memo.TryGetValue(nodeId, out var decision))
                    {
                        decision.VendorComponentCostsUnreliable = true;
                        memo[nodeId] = decision;
                    }
                }
            }
        }

        /// <summary>
        /// Re-sums every Craft decision's TotalCost bottom-up from its
        /// chosen recipe's (possibly corrected) ingredient TotalCosts,
        /// mirroring Evaluate's craftRealCost aggregation. Needed because
        /// Evaluate ran before FinalizeVendorBatches/
        /// AllocateVendorNodeCosts, so Craft ancestors above a corrected
        /// vendor leaf would keep summing the stale pre-correction share.
        /// Walks only the chosen path, unbounded depth, from the tree root.
        /// </summary>
        private static long? RecomputeCraftCosts(
            RecipeNode node, Dictionary<int, Decision> memo, ISet<int> ignoredItemIds)
        {
            // Item-positive guard mirroring Evaluate's own top guard - a
            // non-Item ingredient type carries no memo entry (see Evaluate's
            // ingredient loop), so this is defense-in-depth consistency.
            if (node.IngredientType != "Item")
            {
                return null;
            }

            if (ignoredItemIds != null && ignoredItemIds.Contains(node.Id))
            {
                return memo.TryGetValue(node.NodeId, out var ignoredDecision)
                    ? ignoredDecision.TotalCost
                    : 0L;
            }

            if (!memo.TryGetValue(node.NodeId, out var decision))
            {
                return null;
            }

            if (decision.Source != AcquisitionSource.Craft)
            {
                return decision.TotalCost;
            }

            var chosenRecipe = node.Recipes.FirstOrDefault(r => r.RecipeId == decision.RecipeId);
            long craftRealCost = 0L;
            if (chosenRecipe != null)
            {
                foreach (var ingredient in chosenRecipe.Ingredients)
                {
                    if (ingredient.IngredientType == "Currency")
                    {
                        // A coin-typed Currency ingredient is real copper
                        // (Evaluate folds it into craftRealCost); it must
                        // be re-added here or this re-derivation would
                        // strip it. Non-coin currencies still contribute
                        // nothing.
                        if (ingredient.Id == Gw2Constants.CoinCurrencyId)
                        {
                            craftRealCost += ingredient.Quantity;
                        }

                        continue;
                    }

                    if (ingredient.IngredientType != "Item")
                    {
                        // Non-Item ingredient: never a real coin
                        // contribution and carries no memo entry; skip.
                        continue;
                    }

                    craftRealCost += RecomputeCraftCosts(ingredient, memo, ignoredItemIds) ?? 0L;
                }
            }

            decision.TotalCost = craftRealCost;
            memo[node.NodeId] = decision;
            return craftRealCost;
        }

        /// <summary>
        /// The ComparisonValue twin of RecomputeCraftCosts - same walk
        /// shape, but re-derives Decision.ComparisonValue. Without it, a
        /// Craft node above a vendor-corrected leaf kept the
        /// pre-correction ComparisonValue, drifting from the corrected
        /// TotalCost. Mirrors Evaluate's ingredient loop for a comparable
        /// recipe; decision.HasUnvaluedCurrency already carries the tier
        /// (including transitive propagation), and a fallback-tier
        /// decision's ComparisonValue is set equal to its corrected
        /// TotalCost with no valuation folded in. Descendants are visited
        /// unconditionally; only this node's own aggregation is gated on
        /// the tier.
        /// </summary>
        private static long? RecomputeComparisonValues(
            RecipeNode node, Dictionary<int, Decision> memo, ISet<int> ignoredItemIds,
            CurrencyValuation currencyValuation)
        {
            if (node.IngredientType != "Item")
            {
                return null;
            }

            if (ignoredItemIds != null && ignoredItemIds.Contains(node.Id))
            {
                return memo.TryGetValue(node.NodeId, out var ignoredDecision)
                    ? ignoredDecision.ComparisonValue
                    : 0L;
            }

            if (!memo.TryGetValue(node.NodeId, out var decision))
            {
                return null;
            }

            if (decision.Source != AcquisitionSource.Craft)
            {
                // Non-Craft leaf: already corrected by the vendor-currency
                // reallocation pass (BuyFromVendor) or never touched
                // (BuyFromTp/UnknownSource).
                return decision.ComparisonValue;
            }

            var chosenRecipe = node.Recipes.FirstOrDefault(r => r.RecipeId == decision.RecipeId);
            long comparisonValue = 0L;
            if (chosenRecipe != null)
            {
                foreach (var ingredient in chosenRecipe.Ingredients)
                {
                    if (ingredient.IngredientType == "Currency")
                    {
                        if (ingredient.Id == Gw2Constants.CoinCurrencyId)
                        {
                            comparisonValue += ingredient.Quantity;
                            continue;
                        }

                        // Mirrors Evaluate's valuationCopper accumulation;
                        // decision.HasUnvaluedCurrency already reflects
                        // whether this recipe stayed comparable.
                        if (!decision.HasUnvaluedCurrency &&
                            currencyValuation != null &&
                            currencyValuation.TryGetCopperValue(ingredient.Id, out long copperPerUnit))
                        {
                            try
                            {
                                comparisonValue = checked(comparisonValue + (long)ingredient.Quantity * copperPerUnit);
                            }
                            catch (OverflowException)
                            {
                                // Unreachable in practice (an overflowing
                                // valuation already demoted this recipe at
                                // Evaluate() time); defense in depth only.
                            }
                        }

                        continue;
                    }

                    if (ingredient.IngredientType != "Item")
                    {
                        continue;
                    }

                    // Always recurse regardless of this node's tier so
                    // nested descendants are corrected; only the
                    // aggregation is gated on HasUnvaluedCurrency.
                    comparisonValue += RecomputeComparisonValues(ingredient, memo, ignoredItemIds, currencyValuation) ?? 0L;
                }
            }

            // Fallback tier never folds in valuation - identical to the
            // (already corrected) TotalCost, mirroring Evaluate's own
            // fallback commit sites. See this method's own summary.
            long finalValue = decision.HasUnvaluedCurrency ? (decision.TotalCost ?? 0L) : comparisonValue;
            decision.ComparisonValue = finalValue;
            memo[node.NodeId] = decision;
            return finalValue;
        }

        /// <summary>
        /// Re-derives every Craft-type PlanStep's TotalCost from `memo`
        /// after the correction passes, instead of trusting
        /// AggregateStep's pre-correction running sum - otherwise a Craft
        /// shopping-list row stays stale while the tree and totals show
        /// the corrected number. Running the merge before Collect was
        /// rejected: the merge needs aggregate demand, only known after a
        /// full walk, so reordering would mean two full tree walks; this
        /// is a flat stepMap-sized refresh instead. A missing/null memo
        /// TotalCost contributes 0, matching AggregateStep.
        /// </summary>
        private static void RefreshCraftStepCosts(
            Dictionary<(int, AcquisitionSource, int), PlanStep> stepMap,
            Dictionary<(int, AcquisitionSource, int), List<int>> craftOccurrences,
            Dictionary<int, Decision> memo)
        {
            foreach (var kvp in craftOccurrences)
            {
                if (!stepMap.TryGetValue(kvp.Key, out var step))
                {
                    continue;
                }

                long total = 0L;
                foreach (var nodeId in kvp.Value)
                {
                    if (memo.TryGetValue(nodeId, out var decision) && decision.TotalCost.HasValue)
                    {
                        total += decision.TotalCost.Value;
                    }
                }

                step.TotalCost = total;
            }
        }

        private long? GetBuyCost(
            int itemId, int quantity,
            IReadOnlyDictionary<int, ItemPrice> prices,
            PriceBasis priceBasis,
            out bool priceSideFellBack)
        {
            priceSideFellBack = false;
            if (prices.TryGetValue(itemId, out var price))
            {
                int unitPrice = GetUnitPrice(price, priceBasis, out priceSideFellBack);
                if (unitPrice > 0)
                {
                    return (long)quantity * unitPrice;
                }
            }

            return null;
        }

        /// <summary>
        /// Unit acquisition cost under the chosen basis: lowest sell listing
        /// (instant) or highest buy order (patient), falling back to this
        /// SAME item's other TP side when the preferred side is empty (see
        /// the 3-arg overload below for the full rationale). 0 = not
        /// priceable (both sides empty). Discards whether a fallback
        /// happened - callers that need that fact (e.g. to surface it in
        /// the UI) must call the 3-arg overload instead.
        /// </summary>
        internal static int GetUnitPrice(ItemPrice price, PriceBasis priceBasis)
        {
            return GetUnitPrice(price, priceBasis, out _);
        }

        /// <summary>
        /// Same as the two-arg overload, but also reports whether the
        /// preferred side was empty and the item's other TP side was used
        /// instead - gw2e's own live behavior (preferred side first,
        /// cross-side fallback, unpriced only when both sides are empty).
        /// The single side-selection logic shared with VendorBatchSolver's
        /// cost-line pricing. 0 on both sides returns 0 with the out
        /// param false.
        /// </summary>
        internal static int GetUnitPrice(ItemPrice price, PriceBasis priceBasis, out bool priceSideFellBack)
        {
            int preferred = priceBasis == PriceBasis.BuyOrder
                ? price.SellInstant
                : price.BuyInstant;
            if (preferred > 0)
            {
                priceSideFellBack = false;
                return preferred;
            }

            int otherSide = priceBasis == PriceBasis.BuyOrder
                ? price.BuyInstant
                : price.SellInstant;
            priceSideFellBack = otherSide > 0;
            return otherSide;
        }
    }
}
