using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services.Diagnostics;

namespace TaimisToolbench.Services
{
    internal class CraftingPlanPipeline
    {
        private readonly RecipeService _recipeService;
        private readonly TradingPostService _tradingPostService;
        private readonly PlanSolver _solver;
        private readonly ItemMetadataService _itemMetadataService;
        private readonly VendorOfferStore _vendorOfferStore;

        /// <summary>
        /// How many times cost-line expansion may look at what the previous
        /// round's subtrees exposed. Measured on the shipped corpus, the
        /// deepest real case (item 101521) closes after 3.
        /// </summary>
        private const int MaxVendorCostLineRounds = 6;

        // Computed once so a null store degrades to a null delegate,
        // matching RecipeSheetSavingsCalculator's no-offer-source guard.
        private readonly Func<int, IReadOnlyList<VendorOffer>> _offersForRecipeSheetItem;
        private readonly InventoryReducer _reducer;
        private readonly IAccountRecipeClient _accountRecipeClient;
        private readonly CurrencyMetadataService _currencyMetadataService;
        private readonly IReadOnlyDictionary<int, AcquisitionHint> _acquisitionHints;
        private readonly IReadOnlyDictionary<int, DailyCooldownItem> _dailyCooldownItems;

        // Recipe id -> unlocking recipe-sheet item id for
        // RecipeSheetSavingsCalculator. Empty (never null) when absent.
        private readonly IReadOnlyDictionary<int, int> _recipeSheetItemIdByRecipeId;

        // Active festival names, read lazily at plan-generation time:
        // Blish's FestivalContext loads asynchronously, so a one-shot
        // Initialize()-time read could observe NotReady and silently
        // disable the feature for the whole session.
        private readonly Func<IReadOnlyList<string>> _activeFestivalNames;

        // Last-built AccountItemIndex, keyed by reference equality on the
        // context so repeat override clicks against the same plan skip the
        // rebuild. No locking: ResolveWithOverrides runs only on the UI
        // thread; background generation uses its own local index.
        private PlanSolveContext _cachedAccountIndexContext;
        private AccountItemIndex _cachedAccountIndex;

        // Defaults to ModuleLog.Shared; tests inject an isolated instance
        // for deterministic assertions.
        private readonly ModuleLog _moduleLog;

        // Shared by the phase event's Detail and the PlanStatus wording so
        // the two channels never drift.
        private const string FirstRunTreeHint = "may take several seconds on first run";

        public CraftingPlanPipeline(
            RecipeService recipeService,
            TradingPostService tradingPostService,
            PlanSolver solver,
            ItemMetadataService itemMetadataService,
            VendorOfferStore vendorOfferStore = null,
            InventoryReducer reducer = null,
            IAccountRecipeClient accountRecipeClient = null,
            CurrencyMetadataService currencyMetadataService = null,
            IReadOnlyDictionary<int, AcquisitionHint> acquisitionHints = null,
            ModuleLog moduleLog = null,
            IReadOnlyDictionary<int, DailyCooldownItem> dailyCooldownItems = null,
            IReadOnlyDictionary<int, int> recipeSheetItemIdByRecipeId = null,
            Func<IReadOnlyList<string>> activeFestivalNames = null)
        {
            _recipeService = recipeService;
            _tradingPostService = tradingPostService;
            _solver = solver;
            _itemMetadataService = itemMetadataService;
            _vendorOfferStore = vendorOfferStore;
            _offersForRecipeSheetItem = vendorOfferStore != null
                ? (Func<int, IReadOnlyList<VendorOffer>>)vendorOfferStore.GetOffersForItem
                : null;
            _reducer = reducer;
            _accountRecipeClient = accountRecipeClient;
            _currencyMetadataService = currencyMetadataService;
            _acquisitionHints = acquisitionHints;
            _moduleLog = moduleLog ?? ModuleLog.Shared;
            _dailyCooldownItems = dailyCooldownItems;
            _recipeSheetItemIdByRecipeId = recipeSheetItemIdByRecipeId ?? new Dictionary<int, int>();
            _activeFestivalNames = activeFestivalNames ?? (() => Array.Empty<string>());
        }

        public async Task<CraftingPlanResult> GenerateStructuredAsync(
            int targetItemId, int quantity, AccountSnapshot snapshot,
            CancellationToken ct, IProgress<PlanStatus> progress = null,
            string activeCharacterName = null,
            // Default matches gw2efficiency's "buy price" (buy orders) basis.
            PriceBasis priceBasis = PriceBasis.BuyOrder,
            CurrencyValuation currencyValuation = null,
            OwnMaterialsMode ownMaterialsMode = OwnMaterialsMode.Free,
            HomesteadEfficiencyTiers homesteadTiers = null,
            // Live coarse-phase events for the status strip; see PlanPhaseEvent.
            IProgress<PlanPhaseEvent> phaseProgress = null,
            // Threaded separately from `snapshot`: the useOwn:false path
            // passes snapshot: null, which must not also blank the Required
            // Disciplines tiebreak (see AccountSnapshot.CharacterDisciplines).
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines = null)
        {
            var valuation = currencyValuation ?? CurrencyValuation.None;
            var tiers = homesteadTiers ?? HomesteadEfficiencyTiers.Default;
            var sw = new Stopwatch();
            var timingLog = new List<string>();
            var phaseTracker = new PhaseTracker(phaseProgress, _moduleLog);

            // Build recipe tree
            phaseTracker.Start(PlanPhase.BuildingTree, "Building recipe tree", null, FirstRunTreeHint);
            progress?.Report(new PlanStatus
            {
                Message = $"Building recipe tree ({FirstRunTreeHint})...",
            });
            // These RecipeService diagnostics explain a slow first run and a
            // stale recipe seed; RecipeService bounds each to one Info line
            // per generation.
            _recipeService.OnStatusUpdate = msg =>
            {
                progress?.Report(new PlanStatus { Message = msg });
                _moduleLog.Write(ModuleLogLevel.Info, "plan", msg);
            };
            sw.Restart();
            RecipeNode tree;
            try
            {
                tree = await _recipeService.BuildTreeAsync(targetItemId, quantity, ct);
            }
            finally
            {
                _recipeService.OnStatusUpdate = null;
            }

            sw.Stop();
            timingLog.Add($"Build recipe tree: {sw.ElapsedMilliseconds}ms");

            return await RunPipelineAsync(
                tree, targetItemId, quantity, items: null, snapshot, ct, progress,
                activeCharacterName, priceBasis, valuation, ownMaterialsMode, tiers,
                characterDisciplines, sw, timingLog, phaseTracker);
        }

        /// <summary>
        /// Everything from price lookup through the result build, shared by
        /// the single-item and multi-item paths. Callers build the tree
        /// themselves so
        /// PlanPhaseTimingSummary keeps its per-path phase labels. items is
        /// null on the single-item path; when set, tree is the synthetic
        /// multi-item wrapper and targetItemId/quantity carry the wrapper
        /// sentinel values for the SolveContext.
        /// </summary>
        private async Task<CraftingPlanResult> RunPipelineAsync(
            RecipeNode tree,
            int targetItemId,
            int quantity,
            IReadOnlyList<PlanRequestItem> items,
            AccountSnapshot snapshot,
            CancellationToken ct,
            IProgress<PlanStatus> progress,
            string activeCharacterName,
            PriceBasis priceBasis,
            CurrencyValuation valuation,
            OwnMaterialsMode ownMaterialsMode,
            HomesteadEfficiencyTiers tiers,
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines,
            Stopwatch sw,
            List<string> timingLog,
            PhaseTracker phaseTracker)
        {
            // Always applied; a no-op when the tree has no achievement-bit
            // ingredients. Must run before inventory reduction and the
            // force-buy pre-pass - see AchievementBitDedupPrePass.
            AchievementBitDedupPrePass.Apply(tree);

            // Collect all item IDs from the tree for price lookup
            progress?.Report(new PlanStatus { Message = "Collecting item IDs..." });
            sw.Restart();
            var allItemIds = new HashSet<int>();
            CollectItemIds(tree, allItemIds);
            sw.Stop();
            timingLog.Add($"Collect item IDs: {sw.ElapsedMilliseconds}ms ({allItemIds.Count} items)");

            // Fetch TP prices
            phaseTracker.Start(PlanPhase.FetchingPrices, "Fetching prices", allItemIds.Count);
            progress?.Report(new PlanStatus
            {
                Message = $"Fetching prices ({allItemIds.Count} items)...",
                Total = allItemIds.Count,
            });
            sw.Restart();
            var prices = await _tradingPostService.GetPricesAsync(allItemIds, ct);
            sw.Stop();
            timingLog.Add($"Fetch TP prices: {sw.ElapsedMilliseconds}ms ({allItemIds.Count} items)");

            // Query vendor offers, then price any vendor-only cost items
            var vendorContext = await FetchPricedVendorContextAsync(
                allItemIds, prices, priceBasis, progress, sw, timingLog, ct);
            var vendorOffers = vendorContext.VendorOffers;
            prices = vendorContext.Prices;
            var costLineSubtrees = vendorContext.CostLineSubtrees;

            // The solver's offer set excludes seasonal offers (see
            // SeasonalOfferFilter); `vendorOffers` stays the raw, unfiltered
            // dictionary for everything else in this method.
            var solverVendorOffers = SeasonalOfferFilter.ExcludeSeasonal(vendorOffers);

            // gw2e's "Value Own Materials" force-buy pre-pass - only when
            // the setting is Valued and a snapshot drives reduction (see
            // OwnedMaterialsForceBuyPrePass).
            bool useForceBuyPrePass = ownMaterialsMode == OwnMaterialsMode.Valued &&
                snapshot != null && _reducer != null;

            if (useForceBuyPrePass)
            {
                // Pre-assign stable NodeIds to the unreduced tree before
                // the inventory reduction clones it: CloneNode preserves
                // NodeIds, so the
                // pre-pass's forceBuyOnlyNodeIds keys match the ids the
                // real solve uses.
                RecipeNodeIds.Assign(tree);
            }

            // Computed before every Solve() call so the competency check
            // sees the same discipline data at every solve of this
            // generation, including the zero-owned guide solve.
            var effectiveCharacterDisciplines = characterDisciplines ?? snapshot?.CharacterDisciplines;

            // Steps 5.5/5.6: both computed against `tree` - the original,
            // unreduced tree - matching gw2e's zero-owned-baseline
            // mechanics: on the already-reduced tree the rule would be a
            // near no-op, since owned components already make craft cost
            // look cheap regardless of what a fresh purchase would cost.
            //
            // The throwaway Solve() runs on the same zero-owned tree with
            // forceBuyOnlyNodeIds applied; InventoryReducer.Reduce uses its
            // Decisions as a guide so owned stock can only strengthen the
            // zero-owned winner, never flip a decision. A null guide leaves
            // the legacy primary-option heuristic in charge.
            ISet<int> forceBuyOnlyNodeIds = null;
            // Competency-independent subset of forceBuyOnlyNodeIds,
            // threaded to every Solve() so Decision.CheapestCraftUntrained
            // is gated consistently (see OwnedMaterialsForceBuyPrePass).
            ISet<int> competencyIndependentForceBuyNodeIds = null;
            IReadOnlyDictionary<int, SolverDecision> zeroOwnedDecisions = null;
            if (useForceBuyPrePass)
            {
                var forceBuyPrePassResult = OwnedMaterialsForceBuyPrePass.ComputeForceBuyOnlyNodeIds(
                    _solver, tree, prices, solverVendorOffers, priceBasis, valuation,
                    characterDisciplines: effectiveCharacterDisciplines,
                    vendorCostSubtrees: costLineSubtrees);
                forceBuyOnlyNodeIds = forceBuyPrePassResult.ForceBuyOnlyNodeIds;
                competencyIndependentForceBuyNodeIds = forceBuyPrePassResult.CompetencyIndependentForceBuyNodeIds;

                var zeroOwnedSolve = _solver.Solve(
                    tree, prices, solverVendorOffers, priceBasis,
                    overrides: null, currencyValuation: valuation,
                    forceBuyOnlyNodeIds: forceBuyOnlyNodeIds,
                    competencyIndependentForceBuyNodeIds: competencyIndependentForceBuyNodeIds,
                    homesteadTiers: tiers,
                    characterDisciplines: effectiveCharacterDisciplines,
                    vendorCostSubtrees: costLineSubtrees);
                zeroOwnedDecisions = zeroOwnedSolve.Decisions;
            }

            // Inventory reduction
            phaseTracker.Start(PlanPhase.SolvingDecisions, "Solving decisions", null);
            progress?.Report(new PlanStatus { Message = "Reducing inventory..." });
            sw.Restart();
            RecipeNode treeUsedForSolve = tree;
            List<UsedMaterial> usedMaterials = null;
            Dictionary<RecipeNode, int> ownedQuantityUsedByNode = null;
            // Captured here rather than scoped inside the `if` below so it
            // can also feed PlanSolveContext.
            // AccountItems further down - see that field's own doc comment.
            AccountItemIndex accountIndex = null;

            if (snapshot != null && _reducer != null)
            {
                accountIndex = new AccountItemIndex(snapshot.Items);
                var reduced = _reducer.Reduce(tree, accountIndex, activeCharacterName, zeroOwnedDecisions);
                treeUsedForSolve = reduced.ReducedTree;
                usedMaterials = reduced.UsedMaterials;
                ownedQuantityUsedByNode = reduced.OwnedQuantityUsedByNode;
            }

            sw.Stop();
            timingLog.Add($"Inventory reduction: {sw.ElapsedMilliseconds}ms");

            // Solve. assignNodeIds:false only when the pre-pass
            // pre-assigned ids, so forceBuyOnlyNodeIds' keys still match.
            progress?.Report(new PlanStatus { Message = "Solving crafting plan..." });
            sw.Restart();
            var solveResult = _solver.Solve(
                treeUsedForSolve, prices, solverVendorOffers, priceBasis,
                overrides: null, currencyValuation: valuation,
                forceBuyOnlyNodeIds: forceBuyOnlyNodeIds,
                competencyIndependentForceBuyNodeIds: competencyIndependentForceBuyNodeIds,
                assignNodeIds: !useForceBuyPrePass,
                homesteadTiers: tiers,
                characterDisciplines: effectiveCharacterDisciplines,
                // See PlanSolver.Evaluate's ownedQuantityUsedByNode doc comment.
                ownedQuantityUsedByNode: ownedQuantityUsedByNode,
                vendorCostSubtrees: costLineSubtrees);
            var plan = solveResult.Plan;
            sw.Stop();
            timingLog.Add($"Solve: {sw.ElapsedMilliseconds}ms");

            // Convert the reference-keyed owned-usage side channel
            // into a NodeId-keyed lookup now that Solve() assigned NodeIds.
            IReadOnlyDictionary<int, int> ownedQuantityUsedByNodeId =
                BuildOwnedQuantityUsedByNodeId(ownedQuantityUsedByNode);

            // Fetch item metadata for all step items + target + used materials + tree items
            // Fetch metadata for EVERY tree item (not just chosen-path ones):
            // local override re-solves can surface any node's item in steps,
            // and the cached SolveContext metadata must cover them all.
            var metadataIds = new HashSet<int>(allItemIds);
            metadataIds.UnionWith(plan.Steps.Select(s => s.ItemId));
            if (items != null)
            {
                foreach (var item in items)
                {
                    metadataIds.Add(item.ItemId);
                }
            }
            else
            {
                metadataIds.Add(targetItemId);
            }

            if (usedMaterials != null)
            {
                foreach (var um in usedMaterials)
                {
                    metadataIds.Add(um.ItemId);
                }
            }

            // Vendor cost-component item leaves are never tree ingredients,
            // so allItemIds never collects them; add them before the bulk
            // metadata fetch (see AddVendorItemComponentIds).
            AddVendorItemComponentIds(solveResult.Decisions, metadataIds);
            // Also widen for offers a later manual override could reach -
            // see AddAllVendorOfferItemComponentIds.
            AddAllVendorOfferItemComponentIds(vendorOffers, metadataIds);
            AddAllVendorOfferUnlockItemIds(vendorOffers, metadataIds);
            phaseTracker.Start(PlanPhase.FetchingItemDetails, "Fetching item details", metadataIds.Count);
            progress?.Report(new PlanStatus
            {
                Message = $"Fetching item details ({metadataIds.Count} items)...",
                Total = metadataIds.Count,
            });
            sw.Restart();

            // Fetch currency metadata in parallel with item metadata; the
            // service has its own timeout. Observed independently so a
            // fault is never left unobserved if item metadata throws first.
            var currencyTask = _currencyMetadataService?.GetAllAsync(ct);
            ObserveFault(currencyTask);

            var metadata = await _itemMetadataService.GetMetadataAsync(metadataIds, ct);
            sw.Stop();
            timingLog.Add($"Fetch item metadata: {sw.ElapsedMilliseconds}ms ({metadataIds.Count} items)");

            // Await the currency metadata fetch started above
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata =
                await AwaitCurrencyMetadataOrNullAsync(currencyTask, progress, sw, timingLog, ct);

            // Fetch learned recipe IDs (if permission available)
            ISet<int> learnedRecipeIds =
                await FetchLearnedRecipeIdsAsync(progress, sw, timingLog, phaseTracker, ct);

            // Build structured result
            phaseTracker.Start(PlanPhase.BuildingDisplay, "Building display", null);
            progress?.Report(new PlanStatus { Message = "Building final result..." });
            sw.Restart();
            var resultBuilder = new PlanResultBuilder();
            // Cosmetic only: feeds the Build() tiebreak, which can relabel
            // which equally-good discipline is reported but never change a
            // decision or total (see PlanResultBuilder.Build).
            var result = resultBuilder.Build(
                plan, treeUsedForSolve, metadata, usedMaterials, learnedRecipeIds, effectiveCharacterDisciplines);
            result.CurrencyMetadata = currencyMetadata;
            result.AcquisitionHints = _acquisitionHints;
            result.DailyCooldownItems = _dailyCooldownItems;
            result.RequestedItems = items;
            result.CharacterDisciplines = effectiveCharacterDisciplines;

            // Owned-currency annotation, cosmetic only - never fed back
            // into any decision or total (see BuildOwnedCurrencyAmounts).
            IReadOnlyDictionary<int, int> ownedCurrencyAmounts =
                BuildOwnedCurrencyAmounts(snapshot, plan.CurrencyCosts, vendorOffers);
            result.OwnedCurrencyAmounts = ownedCurrencyAmounts;

            // Owned-item annotation for vendor cost-component leaves,
            // cosmetic only (see BuildOwnedVendorItemComponentAmounts).
            IReadOnlyDictionary<int, int> ownedVendorItemAmounts =
                BuildOwnedVendorItemComponentAmounts(snapshot, solveResult.Decisions, vendorOffers);
            result.OwnedVendorItemAmounts = ownedVendorItemAmounts;

            BuildCraftingTreeResult(
                result, treeUsedForSolve, solveResult.Decisions, metadata,
                _acquisitionHints, ownedQuantityUsedByNodeId, ignoredItemIds: null,
                currencyMetadata: currencyMetadata, ownedCurrencyAmounts: ownedCurrencyAmounts,
                ownedVendorItemAmounts: ownedVendorItemAmounts);

            // Shape dispatch: see SellSideEconomics.ApplyForPlanShape.
            SellSideEconomics.ApplyForPlanShape(
                result, treeUsedForSolve, solveResult, prices,
                targetItemId, quantity, items, priceBasis, usedMaterials, ownMaterialsMode);

            // Annotation-only: writes only result.ExcessCraftOutputs.
            ExcessCraftOutputCalculator.Apply(result, prices, metadata);

            // Annotation-only. Uses the raw `vendorOffers` (not
            // solverVendorOffers) - see that variable's own comment.
            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds, prices, priceBasis, _offersForRecipeSheetItem,
                _recipeSheetItemIdByRecipeId, effectiveCharacterDisciplines);
            SeasonalVendorTipCalculator.Apply(
                result, vendorOffers, prices, priceBasis, _activeFestivalNames());

            // Annotation-only: writes only result.CompetencyOpportunities.
            CompetencyOpportunityCalculator.Apply(result);

            // Capture inputs so the UI can re-solve locally with per-node
            // overrides (no network round-trips).
            result.SolveContext = new PlanSolveContext
            {
                TargetItemId = targetItemId,
                Quantity = quantity,
                Tree = treeUsedForSolve,
                Prices = prices,
                VendorOffers = vendorOffers,
                VendorCostLineValues = solveResult.VendorCostLineValues,
                Metadata = metadata,
                LearnedRecipeIds = learnedRecipeIds,
                UsedMaterials = usedMaterials,
                PriceBasis = priceBasis,
                CurrencyValuation = valuation,
                OwnMaterialsMode = ownMaterialsMode,
                CurrencyMetadata = currencyMetadata,
                AcquisitionHints = _acquisitionHints,
                DailyCooldownItems = _dailyCooldownItems,
                OwnedQuantityUsedByNodeId = ownedQuantityUsedByNodeId,
                OwnedCurrencyAmounts = ownedCurrencyAmounts,
                OwnedVendorItemAmounts = ownedVendorItemAmounts,
                ForceBuyOnlyNodeIds = forceBuyOnlyNodeIds,
                CompetencyIndependentForceBuyNodeIds = competencyIndependentForceBuyNodeIds,
                RequestedItems = items,
                HomesteadTiers = tiers,
                CharacterDisciplines = result.CharacterDisciplines,
                // Only populated when the force-buy pre-pass ran - see
                // PlanSolveContext.UnreducedTree.
                UnreducedTree = useForceBuyPrePass ? tree : null,
                AccountItems = useForceBuyPrePass ? ProjectAccountItemsForSolveContext(snapshot.Items) : null,
                ActiveCharacterName = useForceBuyPrePass ? activeCharacterName : null,
            };
            sw.Stop();
            timingLog.Add($"Build result: {sw.ElapsedMilliseconds}ms");

            FinishTimingLog(result, timingLog);
            phaseTracker.Finish();

            return result;
        }

        /// <summary>
        /// Generates a combined plan for N requested items in one
        /// calculation. A single-entry list delegates straight to the
        /// single-item overload above. For 2+ items, builds the synthetic
        /// wrapper tree (RecipeService.BuildMultiItemTreeAsync) and feeds
        /// it through the same pipeline a single item uses - merged totals
        /// across shared materials fall out of the existing per-item-id
        /// aggregation (see PlanSolver.Collect's AggregateStep).
        /// </summary>
        public async Task<CraftingPlanResult> GenerateStructuredAsync(
            IReadOnlyList<PlanRequestItem> items,
            AccountSnapshot snapshot,
            CancellationToken ct,
            IProgress<PlanStatus> progress = null,
            string activeCharacterName = null,
            PriceBasis priceBasis = PriceBasis.BuyOrder,
            CurrencyValuation currencyValuation = null,
            OwnMaterialsMode ownMaterialsMode = OwnMaterialsMode.Free,
            HomesteadEfficiencyTiers homesteadTiers = null,
            // See the single-item overload's matching parameter.
            IProgress<PlanPhaseEvent> phaseProgress = null,
            // Best-effort "name x quantity" label for the start/finish log
            // lines; null falls back to "(N items)".
            string requestLabel = null,
            // See the single-item overload's matching parameter.
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines = null)
        {
            // Marked async so this validation throws inside the returned
            // Task, like every other failure mode of this method.
            if (items == null || items.Count == 0)
            {
                throw new ArgumentException("At least one plan request item is required.", nameof(items));
            }

            // This thin dispatcher is the one entry point Module.cs calls,
            // so logging only here covers every real call site.
            var sw = Stopwatch.StartNew();
            string itemWord = items.Count == 1 ? "item" : "items";
            string label = string.IsNullOrEmpty(requestLabel) ? $"{items.Count} {itemWord}" : requestLabel;
            _moduleLog.Write(ModuleLogLevel.Info, "plan", $"Generating plan for {label}");

            try
            {
                CraftingPlanResult result;
                if (items.Count == 1)
                {
                    result = await GenerateStructuredAsync(
                        items[0].ItemId, items[0].Quantity, snapshot, ct, progress,
                        activeCharacterName, priceBasis, currencyValuation, ownMaterialsMode,
                        homesteadTiers, phaseProgress, characterDisciplines: characterDisciplines);
                }
                else
                {
                    result = await GenerateStructuredMultiAsync(
                        items, snapshot, ct, progress, activeCharacterName,
                        priceBasis, currencyValuation, ownMaterialsMode, homesteadTiers,
                        phaseProgress, characterDisciplines: characterDisciplines);
                }

                // Compact per-phase summary derived from the timing lines
                // already in result.DebugLog (see PlanPhaseTimingSummary).
                // sw is passed as wallClockMs: a phase-sum-only total would
                // exclude un-instrumented gaps and under-report duration.
                string phaseSummary = PlanPhaseTimingSummary.FormatCompactSummary(result?.DebugLog, sw.ElapsedMilliseconds);
                _moduleLog.Write(ModuleLogLevel.Info, "plan",
                    string.IsNullOrEmpty(phaseSummary)
                        ? $"Generation finished in {sw.ElapsedMilliseconds}ms"
                        : $"Plan for {label}: {phaseSummary}");
                return result;
            }
            catch (OperationCanceledException)
            {
                _moduleLog.Write(ModuleLogLevel.Info, "plan", $"Generation cancelled after {sw.ElapsedMilliseconds}ms ({label})");
                throw;
            }
            catch (Exception ex)
            {
                _moduleLog.Write(ModuleLogLevel.Warn, "plan", $"Generation failed after {sw.ElapsedMilliseconds}ms ({label}): {ex.GetType().Name} - {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// The genuine (2+ item) path behind the list overload above.
        /// Mirrors the single-item pipeline step-for-step with the wrapper
        /// tree standing in for a single item's tree; PlanSolver,
        /// InventoryReducer, and OwnedMaterialsForceBuyPrePass are all
        /// oblivious to the wrapper. Sell-side economics aggregate across
        /// every requested root via ApplyBatchSellSideEconomics.
        /// </summary>
        private async Task<CraftingPlanResult> GenerateStructuredMultiAsync(
            IReadOnlyList<PlanRequestItem> items,
            AccountSnapshot snapshot,
            CancellationToken ct,
            IProgress<PlanStatus> progress,
            string activeCharacterName,
            PriceBasis priceBasis,
            CurrencyValuation currencyValuation,
            OwnMaterialsMode ownMaterialsMode,
            HomesteadEfficiencyTiers homesteadTiers,
            IProgress<PlanPhaseEvent> phaseProgress,
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines = null)
        {
            var valuation = currencyValuation ?? CurrencyValuation.None;
            var tiers = homesteadTiers ?? HomesteadEfficiencyTiers.Default;
            var sw = new Stopwatch();
            var timingLog = new List<string>();
            var phaseTracker = new PhaseTracker(phaseProgress, _moduleLog);

            // Build each item's own tree, then wrap them under the
            // synthetic multi-item root (RecipeService.BuildMultiItemTreeAsync).
            phaseTracker.Start(PlanPhase.BuildingTree, "Building recipe tree", null, FirstRunTreeHint);
            progress?.Report(new PlanStatus
            {
                Message = $"Building recipe trees ({FirstRunTreeHint})...",
            });
            _recipeService.OnStatusUpdate = msg =>
            {
                progress?.Report(new PlanStatus { Message = msg });
                _moduleLog.Write(ModuleLogLevel.Info, "plan", msg);
            };
            sw.Restart();
            RecipeNode tree;
            try
            {
                tree = await _recipeService.BuildMultiItemTreeAsync(items, ct);
            }
            finally
            {
                _recipeService.OnStatusUpdate = null;
            }

            sw.Stop();
            timingLog.Add($"Build recipe trees: {sw.ElapsedMilliseconds}ms ({items.Count} items)");

            return await RunPipelineAsync(
                tree, Gw2Constants.MultiItemWrapperItemId, quantity: 1, items, snapshot, ct,
                progress, activeCharacterName, priceBasis, valuation, ownMaterialsMode, tiers,
                characterDisciplines, sw, timingLog, phaseTracker);
        }

        /// <summary>
        /// Re-solves a previously generated plan with per-node decision
        /// overrides. Purely local: reuses the context's tree, prices,
        /// offers, and metadata; no network calls. Because it never
        /// re-fetches anything, context.Metadata and the owned-amount maps
        /// must already cover every id any override could surface - the
        /// generation-time callers widen them via
        /// AddAllVendorOfferItemComponentIds and the vendorOffers-aware
        /// owned-amount builders.
        /// </summary>
        public CraftingPlanResult ResolveWithOverrides(
            PlanSolveContext context,
            IReadOnlyDictionary<int, AcquisitionSource> overrides,
            // Item ids the user marked "fully in-hand" for this session.
            // Live state supplied fresh on every re-solve, exactly like
            // `overrides` - deliberately not part of PlanSolveContext.
            ISet<int> ignoredItemIds = null)
        {
            // The context's tree was reduced at generation time using a
            // guide keyed to the zero-owned decisions, so replaying
            // overrides against it goes wrong the moment an override flips
            // a force-buy-flagged node to Craft: its ingredients stay
            // priced at full un-owned cost. When UnreducedTree is set,
            // re-run the same decision-pass-then-reduce dance with
            // `overrides` applied so the guide stays in sync with what the
            // user picked; otherwise fall back to the frozen context
            // values (nothing to re-guide).
            RecipeNode solveTree = context.Tree;
            List<UsedMaterial> usedMaterials = context.UsedMaterials;
            IReadOnlyDictionary<int, int> ownedQuantityUsedByNodeId = context.OwnedQuantityUsedByNodeId;
            // Reference-keyed twin of ownedQuantityUsedByNodeId for
            // PlanSolver.Evaluate's StrictDomination check; only populated
            // when this call actually re-reduces below.
            Dictionary<RecipeNode, int> resolveOwnedQuantityUsedByNode = null;

            // The _reducer null-check below guards a context generated by
            // one pipeline but resolved against another with no reducer.

            // context.VendorOffers is the raw, unfiltered dictionary; the
            // solver must still exclude seasonal offers, exactly like
            // generation did.
            var solverVendorOffers = SeasonalOfferFilter.ExcludeSeasonal(context.VendorOffers);

            if (context.UnreducedTree != null && _reducer != null)
            {
                var guideSolve = _solver.Solve(
                    context.UnreducedTree, context.Prices, solverVendorOffers,
                    context.PriceBasis, overrides, context.CurrencyValuation,
                    forceBuyOnlyNodeIds: context.ForceBuyOnlyNodeIds,
                    competencyIndependentForceBuyNodeIds: context.CompetencyIndependentForceBuyNodeIds,
                    assignNodeIds: false,
                    ignoredItemIds: ignoredItemIds,
                    homesteadTiers: context.HomesteadTiers,
                    characterDisciplines: context.CharacterDisciplines,
                    vendorCostLineValues: context.VendorCostLineValues);

                var accountIndex = GetOrBuildAccountItemIndex(context);
                var reduced = _reducer.Reduce(
                    context.UnreducedTree, accountIndex, context.ActiveCharacterName,
                    guideSolve.Decisions);

                solveTree = reduced.ReducedTree;
                usedMaterials = reduced.UsedMaterials;
                ownedQuantityUsedByNodeId = BuildOwnedQuantityUsedByNodeId(reduced.OwnedQuantityUsedByNode);
                resolveOwnedQuantityUsedByNode = reduced.OwnedQuantityUsedByNode;
            }

            // Reapply the generation-time force-buy pre-pass result so a
            // local re-solve doesn't forget it; a manual override still
            // wins. assignNodeIds:false - nodes already carry stable ids
            // (CloneNode preserves them), and renumbering would desync
            // forceBuyOnlyNodeIds' keys.
            var solveResult = _solver.Solve(
                solveTree, context.Prices, solverVendorOffers,
                context.PriceBasis, overrides, context.CurrencyValuation,
                forceBuyOnlyNodeIds: context.ForceBuyOnlyNodeIds,
                competencyIndependentForceBuyNodeIds: context.CompetencyIndependentForceBuyNodeIds,
                assignNodeIds: false,
                ignoredItemIds: ignoredItemIds,
                homesteadTiers: context.HomesteadTiers,
                characterDisciplines: context.CharacterDisciplines,
                ownedQuantityUsedByNode: resolveOwnedQuantityUsedByNode,
                vendorCostLineValues: context.VendorCostLineValues);

            var resultBuilder = new PlanResultBuilder();
            var result = resultBuilder.Build(
                solveResult.Plan, solveTree, context.Metadata,
                usedMaterials, context.LearnedRecipeIds,
                context.CharacterDisciplines);
            result.CurrencyMetadata = context.CurrencyMetadata;
            result.AcquisitionHints = context.AcquisitionHints;
            result.DailyCooldownItems = context.DailyCooldownItems;
            result.OwnedCurrencyAmounts = context.OwnedCurrencyAmounts;
            result.OwnedVendorItemAmounts = context.OwnedVendorItemAmounts;
            result.RequestedItems = context.RequestedItems;
            // Cosmetic only, carried forward so a re-solve keeps showing it.
            result.CharacterDisciplines = context.CharacterDisciplines;

            BuildCraftingTreeResult(
                result, solveTree, solveResult.Decisions, context.Metadata,
                context.AcquisitionHints, ownedQuantityUsedByNodeId, ignoredItemIds,
                currencyMetadata: context.CurrencyMetadata, ownedCurrencyAmounts: context.OwnedCurrencyAmounts,
                ownedVendorItemAmounts: context.OwnedVendorItemAmounts);

            // Recompute whichever sell-side economics the generation used
            // so the Total Cost section stays live across re-solves. The
            // single-vs-multi shape check lives in ApplyForPlanShape; the
            // operand is solveTree (possibly a fresh re-reduced clone),
            // not context.Tree.
            SellSideEconomics.ApplyForPlanShape(
                result, solveTree, solveResult, context.Prices,
                context.TargetItemId, context.Quantity, context.RequestedItems,
                context.PriceBasis, usedMaterials, context.OwnMaterialsMode);
            // `context` is carried forward verbatim, so context.Tree stays
            // the generation-time tree even when the branch above just
            // re-reduced. Harmless for repeat re-solves, but
            // BuildPresetOverrides walks context.Tree directly and can emit
            // overrides for pruned nodes and miss reappeared ones.

            // A re-solve must recompute the annotations below too - the
            // chosen cost an opportunity compares against can change with
            // an override. Raw context.VendorOffers, same as generation.
            ExcessCraftOutputCalculator.Apply(result, context.Prices, context.Metadata);
            RecipeSheetSavingsCalculator.Apply(
                result, context.LearnedRecipeIds, context.Prices, context.PriceBasis, _offersForRecipeSheetItem,
                _recipeSheetItemIdByRecipeId, context.CharacterDisciplines);
            SeasonalVendorTipCalculator.Apply(
                result, context.VendorOffers, context.Prices, context.PriceBasis, _activeFestivalNames());
            CompetencyOpportunityCalculator.Apply(result);

            result.SolveContext = context;

            if (result.DebugLog == null)
            {
                result.DebugLog = new List<string>();
            }

            result.DebugLog.Insert(0,
                "Local re-solve with " + StatusText.Count(overrides?.Count ?? 0, "override") +
                ", " + StatusText.Count(ignoredItemIds?.Count ?? 0, "ignored item"));

            return result;
        }

        /// <summary>Vendor offers for a request, paired with vendor-augmented prices.</summary>
        private readonly struct PricedVendorContext
        {
            public PricedVendorContext(
                IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers,
                IReadOnlyDictionary<int, ItemPrice> prices,
                VendorCostLineSubtrees costLineSubtrees)
            {
                VendorOffers = vendorOffers;
                Prices = prices;
                CostLineSubtrees = costLineSubtrees;
            }

            public IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> VendorOffers { get; }

            public IReadOnlyDictionary<int, ItemPrice> Prices { get; }

            /// <summary>Null when no cost line needed one.</summary>
            public VendorCostLineSubtrees CostLineSubtrees { get; }
        }

        /// <summary>
        /// Queries vendor offers for the given item ids, then augments prices for
        /// vendor-only cost items not covered by the recipe-tree price fetch (see
        /// AugmentWithVendorCostPricesAsync).
        /// </summary>
        private async Task<PricedVendorContext> FetchPricedVendorContextAsync(
            HashSet<int> allItemIds,
            IReadOnlyDictionary<int, ItemPrice> prices,
            PriceBasis priceBasis,
            IProgress<PlanStatus> progress,
            Stopwatch sw,
            List<string> timingLog,
            CancellationToken ct)
        {
            progress?.Report(new PlanStatus { Message = "Looking up vendor offers..." });
            sw.Restart();
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers = null;
            if (_vendorOfferStore != null)
            {
                vendorOffers = _vendorOfferStore.GetOffersForItems(allItemIds);
            }

            sw.Stop();
            timingLog.Add($"Query vendor offers: {sw.ElapsedMilliseconds}ms");

            var mergedPrices = await AugmentWithVendorCostPricesAsync(prices, vendorOffers, ct);

            sw.Restart();
            VendorCostLineExpansion expansion;
            try
            {
                expansion = await ExpandVendorCostLinesAsync(
                    allItemIds, vendorOffers, mergedPrices, priceBasis, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Expansion makes an existing plan MORE honest; it must never
                // be the reason there is no plan. Its own price and offer
                // lookups are extra calls this method did not used to make, so
                // a failure here degrades to no subtrees - the solver's
                // pre-expansion behaviour - rather than losing the generation.
                _moduleLog.Write(
                    ModuleLogLevel.Warn,
                    "plan",
                    $"Vendor cost-line expansion failed; vendor cost lines will not be costed: {ex.GetType().Name} - {ex.Message}");
                expansion = new VendorCostLineExpansion(vendorOffers, mergedPrices, null);
            }

            sw.Stop();
            timingLog.Add(
                $"Expand vendor cost lines: {sw.ElapsedMilliseconds}ms ({expansion.Subtrees?.Count ?? 0} items)");

            return new PricedVendorContext(
                expansion.VendorOffers, expansion.Prices, expansion.Subtrees);
        }

        /// <summary>
        /// Builds a quantity-1 acquisition subtree for every Item cost line
        /// the Trading Post cannot price, so PlanSolver can cost it instead of
        /// counting it as free (docs/ARCHITECTURE.md section 7.4). Each round
        /// prices and looks up offers for what the previous round's subtrees
        /// introduced, then expands whatever THAT exposed, until nothing new
        /// appears or a bound is hit.
        /// </summary>
        /// <remarks>
        /// The loop cannot run away: an id is expanded at most once
        /// (<c>built</c>), so it terminates on the finite set of cost item ids
        /// in the corpus whatever cycles that set contains - the shipped one
        /// has them. DefaultMaxDistinctItems and the round cap are belt and
        /// braces against a corpus larger than the one measured. Measured on
        /// item 101521 over the shipped data: 3 rounds, 77 items, 4,215 nodes.
        /// <para>
        /// Failure here is never fatal to the plan: with no subtrees the
        /// solver behaves exactly as it did before expansion existed.
        /// </para>
        /// </remarks>
        private async Task<VendorCostLineExpansion> ExpandVendorCostLinesAsync(
            HashSet<int> allItemIds,
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers,
            IReadOnlyDictionary<int, ItemPrice> prices,
            PriceBasis priceBasis,
            CancellationToken ct)
        {
            if (vendorOffers == null || vendorOffers.Count == 0 ||
                _vendorOfferStore == null || _recipeService == null)
            {
                return new VendorCostLineExpansion(vendorOffers, prices, null);
            }

            var offers = new Dictionary<int, IReadOnlyList<VendorOffer>>(vendorOffers.Count);
            foreach (var kvp in vendorOffers)
            {
                offers[kvp.Key] = kvp.Value;
            }

            var subtreesByItemId = new Dictionary<int, RecipeNode>();
            var built = new HashSet<int>();
            var frontierOffers = new List<IReadOnlyList<VendorOffer>>(offers.Values);
            var knownItemIds = new HashSet<int>(allItemIds);
            var mergedPrices = prices;

            for (int round = 0; round < MaxVendorCostLineRounds; round++)
            {
                ct.ThrowIfCancellationRequested();

                var wanted = VendorCostLineSubtrees.CollectUnpricedCostItemIds(
                    frontierOffers, mergedPrices, priceBasis, built);
                if (wanted.Count == 0)
                {
                    break;
                }

                var newItemIds = new HashSet<int>();
                var nextFrontier = new List<IReadOnlyList<VendorOffer>>();
                foreach (int itemId in Ordered(wanted))
                {
                    if (subtreesByItemId.Count >= VendorCostLineSubtrees.DefaultMaxDistinctItems)
                    {
                        break;
                    }

                    built.Add(itemId);
                    RecipeNode subtree;
                    try
                    {
                        subtree = await _recipeService.BuildTreeAsync(itemId, 1, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // One unbuildable cost item leaves its own line a
                        // barter line; it must not cost the plan every other
                        // line's honest price.
                        continue;
                    }

                    subtreesByItemId[itemId] = subtree;

                    var subtreeIds = new HashSet<int>();
                    CollectItemIds(subtree, subtreeIds);
                    foreach (int id in subtreeIds)
                    {
                        if (knownItemIds.Add(id))
                        {
                            newItemIds.Add(id);
                        }
                    }
                }

                if (newItemIds.Count == 0)
                {
                    break;
                }

                // A subtree's own nodes need the same two lookups the plan
                // tree's did, or the round that follows would judge them
                // unpriced purely because nobody asked.
                mergedPrices = await MergePricesAsync(mergedPrices, newItemIds, ct);
                foreach (var kvp in _vendorOfferStore.GetOffersForItems(newItemIds))
                {
                    offers[kvp.Key] = kvp.Value;
                    nextFrontier.Add(kvp.Value);
                }

                frontierOffers = nextFrontier;
            }

            return new VendorCostLineExpansion(
                offers, mergedPrices, VendorCostLineSubtrees.Create(subtreesByItemId));
        }

        /// <summary>
        /// Ascending item-id order, so which ids a
        /// DefaultMaxDistinctItems cut-off drops is deterministic rather than
        /// whatever a HashSet happened to enumerate.
        /// </summary>
        private static List<int> Ordered(HashSet<int> ids)
        {
            var list = new List<int>(ids);
            list.Sort();
            return list;
        }

        private async Task<IReadOnlyDictionary<int, ItemPrice>> MergePricesAsync(
            IReadOnlyDictionary<int, ItemPrice> prices, HashSet<int> newIds, CancellationToken ct)
        {
            var fetched = await _tradingPostService.GetPricesAsync(newIds, ct);
            if (fetched == null || fetched.Count == 0)
            {
                return prices;
            }

            var merged = new Dictionary<int, ItemPrice>(prices.Count + fetched.Count);
            foreach (var kvp in prices)
            {
                merged[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in fetched)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        private readonly struct VendorCostLineExpansion
        {
            public VendorCostLineExpansion(
                IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers,
                IReadOnlyDictionary<int, ItemPrice> prices,
                VendorCostLineSubtrees subtrees)
            {
                VendorOffers = vendorOffers;
                Prices = prices;
                Subtrees = subtrees;
            }

            public IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> VendorOffers { get; }

            public IReadOnlyDictionary<int, ItemPrice> Prices { get; }

            public VendorCostLineSubtrees Subtrees { get; }
        }

        /// <summary>
        /// Awaits the currency-metadata fetch started earlier. Null task or any
        /// non-cancellation failure yields null (currency rows fall back to
        /// text-only formatting via PlanViewModelBuilder's Gw2Constants fallback).
        /// </summary>
        private static async Task<IReadOnlyDictionary<int, CurrencyMetadata>> AwaitCurrencyMetadataOrNullAsync(
            Task<IReadOnlyDictionary<int, CurrencyMetadata>> currencyTask,
            IProgress<PlanStatus> progress,
            Stopwatch sw,
            List<string> timingLog,
            CancellationToken ct)
        {
            progress?.Report(new PlanStatus { Message = "Fetching currency details..." });
            sw.Restart();
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata = null;
            if (currencyTask != null)
            {
                try
                {
                    currencyMetadata = await currencyTask;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    currencyMetadata = null;
                }
            }

            sw.Stop();
            timingLog.Add($"Fetch currency metadata: {sw.ElapsedMilliseconds}ms");
            return currencyMetadata;
        }

        /// <summary>
        /// Fetches learned recipe ids if the account client is wired up and
        /// permitted. KNOWN-ISSUES #31/api-degradation F4: any non-cancellation
        /// failure degrades to null, a state PlanResultBuilder already treats
        /// as supported rather than discarding an otherwise-priced plan.
        /// </summary>
        private async Task<ISet<int>> FetchLearnedRecipeIdsAsync(
            IProgress<PlanStatus> progress,
            Stopwatch sw,
            List<string> timingLog,
            PhaseTracker phaseTracker,
            CancellationToken ct)
        {
            phaseTracker.Start(PlanPhase.CheckingLearnedRecipes, "Checking learned recipes", null);
            progress?.Report(new PlanStatus { Message = "Checking learned recipes..." });
            sw.Restart();
            ISet<int> learnedRecipeIds = null;
            if (_accountRecipeClient != null && _accountRecipeClient.HasRequiredPermission())
            {
                try
                {
                    learnedRecipeIds = await _accountRecipeClient.GetLearnedRecipeIdsAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                    learnedRecipeIds = null;
                }
            }

            sw.Stop();
            timingLog.Add($"Fetch learned recipes: {sw.ElapsedMilliseconds}ms");
            return learnedRecipeIds;
        }

        /// <summary>
        /// Prepends the timing log and its PlanTimingAnalyzer summary to
        /// <paramref name="result"/>.DebugLog, initializing the list if needed.
        /// </summary>
        private static void FinishTimingLog(CraftingPlanResult result, List<string> timingLog)
        {
            if (result.DebugLog == null)
            {
                result.DebugLog = new List<string>();
            }

            result.DebugLog.InsertRange(0, timingLog);
            var summary = PlanTimingAnalyzer.Summarize(timingLog);
            result.DebugLog.InsertRange(timingLog.Count, summary);
        }

        /// <summary>
        /// Fetches TP prices for vendor-offer Item cost lines that are not
        /// already priced (they are not recipe-tree items, so the main price
        /// fetch never sees them) and returns a merged price dictionary.
        /// </summary>
        private async Task<IReadOnlyDictionary<int, ItemPrice>> AugmentWithVendorCostPricesAsync(
            IReadOnlyDictionary<int, ItemPrice> prices,
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers,
            CancellationToken ct)
        {
            if (vendorOffers == null)
            {
                return prices;
            }

            var costItemIds = new HashSet<int>();
            foreach (var offerList in vendorOffers.Values)
            {
                foreach (var offer in offerList)
                {
                    if (offer.CostLines == null)
                    {
                        continue;
                    }

                    foreach (var cost in offer.CostLines)
                    {
                        if (string.Equals(cost.Type, "Item", StringComparison.Ordinal) &&
                            !prices.ContainsKey(cost.Id))
                        {
                            costItemIds.Add(cost.Id);
                        }
                    }
                }
            }

            if (costItemIds.Count == 0)
            {
                return prices;
            }

            var costPrices = await _tradingPostService.GetPricesAsync(costItemIds, ct);
            var merged = new Dictionary<int, ItemPrice>(prices.Count + costPrices.Count);
            foreach (var kvp in prices)
            {
                merged[kvp.Key] = kvp.Value;
            }

            foreach (var kvp in costPrices)
            {
                merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        /// <summary>
        /// Builds an override map forcing <paramref name="source"/> on every
        /// node of the context's solver tree where it is feasible: nodes
        /// with recipes for Craft, nodes priced under the context's basis
        /// for BuyFromTp. Walks the full tree so nodes hidden beneath
        /// bought intermediates are covered in a single pass.
        /// </summary>
        public static Dictionary<int, AcquisitionSource> BuildPresetOverrides(
            PlanSolveContext context, AcquisitionSource source)
        {
            var overrides = new Dictionary<int, AcquisitionSource>();
            CollectPresetOverrides(context.Tree, context, source, overrides);
            return overrides;
        }

        private static void CollectPresetOverrides(
            RecipeNode node,
            PlanSolveContext context,
            AcquisitionSource source,
            Dictionary<int, AcquisitionSource> overrides)
        {
            if (node.IngredientType == "Item")
            {
                bool feasible = false;
                if (source == AcquisitionSource.Craft)
                {
                    // Permissive: the solver ignores forced crafts whose cost
                    // is not fully priceable, so stray entries are harmless.
                    feasible = node.Recipes.Count > 0;
                }
                else if (source == AcquisitionSource.BuyFromTp)
                {
                    feasible = context.Prices != null &&
                               context.Prices.TryGetValue(node.Id, out var price) &&
                               PlanSolver.GetUnitPrice(price, context.PriceBasis) > 0;
                }

                if (feasible)
                {
                    overrides[node.NodeId] = source;
                }
            }

            foreach (var recipe in node.Recipes)
            {
                foreach (var ingredient in recipe.Ingredients)
                {
                    CollectPresetOverrides(ingredient, context, source, overrides);
                }
            }
        }

        /// <summary>
        /// Builds CraftingPlanResult.CraftingTree (single-item) or
        /// MultiItemRoots (multi-item); the synthetic wrapper root never
        /// surfaces in either. Shared with ResolveWithOverrides so a
        /// re-solved multi-item batch keeps exposing the same N roots.
        /// </summary>
        private static void BuildCraftingTreeResult(
            CraftingPlanResult result,
            RecipeNode tree,
            IReadOnlyDictionary<int, SolverDecision> decisions,
            IReadOnlyDictionary<int, ItemMetadata> metadata,
            IReadOnlyDictionary<int, AcquisitionHint> hints,
            IReadOnlyDictionary<int, int> ownedQuantityUsedByNodeId,
            ISet<int> ignoredItemIds,
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata = null,
            IReadOnlyDictionary<int, int> ownedCurrencyAmounts = null,
            IReadOnlyDictionary<int, int> ownedVendorItemAmounts = null)
        {
            var treeBuilder = new CraftingTreeBuilder();

            if (tree.Id == Gw2Constants.MultiItemWrapperItemId)
            {
                var itemRoots = PlanRootNodes.Of(tree);
                var roots = new List<CraftingTreeNode>(itemRoots.Count);
                foreach (var itemRoot in itemRoots)
                {
                    roots.Add(treeBuilder.BuildTree(
                        itemRoot, decisions, metadata, hints,
                        ownedQuantityUsedByNodeId, ignoredItemIds,
                        currencyMetadata, ownedCurrencyAmounts, ownedVendorItemAmounts));
                }

                result.CraftingTree = null;
                result.MultiItemRoots = roots;
            }
            else
            {
                result.CraftingTree = treeBuilder.BuildTree(
                    tree, decisions, metadata, hints,
                    ownedQuantityUsedByNodeId, ignoredItemIds,
                    currencyMetadata, ownedCurrencyAmounts, ownedVendorItemAmounts);
                result.MultiItemRoots = null;
            }
        }

        /// <summary>
        /// Converts the reference-keyed owned-usage side channel into a
        /// NodeId-keyed lookup once Solve() has assigned NodeIds. Null
        /// input yields an empty dictionary.
        /// </summary>
        private static IReadOnlyDictionary<int, int> BuildOwnedQuantityUsedByNodeId(
            Dictionary<RecipeNode, int> ownedQuantityUsedByNode)
        {
            var result = new Dictionary<int, int>(ownedQuantityUsedByNode?.Count ?? 0);
            if (ownedQuantityUsedByNode == null)
            {
                return result;
            }

            foreach (var kvp in ownedQuantityUsedByNode)
            {
                result[kvp.Key.NodeId] = kvp.Value;
            }

            return result;
        }

        /// <summary>
        /// Returns the AccountItemIndex for <paramref name="context"/>,
        /// reusing the cached one for the same context reference instead
        /// of rebuilding on every override click.
        /// </summary>
        private AccountItemIndex GetOrBuildAccountItemIndex(PlanSolveContext context)
        {
            if (!ReferenceEquals(_cachedAccountIndexContext, context))
            {
                _cachedAccountIndex = new AccountItemIndex(context.AccountItems);
                _cachedAccountIndexContext = context;
            }

            return _cachedAccountIndex;
        }

        /// <summary>
        /// PlanSolveContext.AccountItems is persisted verbatim into
        /// plan.json but only ever consumed via AccountItemIndex, which
        /// reads ItemId/Count/Source. Projects down to those three fields
        /// so a full account snapshot isn't serialized for nothing. Null
        /// input yields null.
        /// </summary>
        private static IReadOnlyList<SnapshotItemEntry> ProjectAccountItemsForSolveContext(
            IReadOnlyList<SnapshotItemEntry> items)
        {
            if (items == null)
            {
                return null;
            }

            var projected = new List<SnapshotItemEntry>(items.Count);
            foreach (var entry in items)
            {
                projected.Add(new SnapshotItemEntry
                {
                    ItemId = entry.ItemId,
                    Count = entry.Count,
                    Source = entry.Source,
                });
            }

            return projected;
        }

        /// <summary>
        /// Owned-currency annotation for the plan's currency totals -
        /// cosmetic only, never fed back into decisions. Null when there
        /// is no wallet snapshot or no currency ids at all, so callers can
        /// distinguish "no data" from "0 owned". Scans
        /// <paramref name="vendorOffers"/> for every non-coin Currency cost
        /// line on any offer (not just the baseline plan's aggregated
        /// costs) so a leaf first surfaced by a manual override still gets
        /// a HAVE pill.
        /// </summary>
        private static IReadOnlyDictionary<int, int> BuildOwnedCurrencyAmounts(
            AccountSnapshot snapshot, List<CurrencyCost> currencyCosts,
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers = null)
        {
            if (snapshot == null)
            {
                return null;
            }

            var currencyIds = new HashSet<int>();
            if (currencyCosts != null)
            {
                foreach (var cc in currencyCosts)
                {
                    currencyIds.Add(cc.CurrencyId);
                }
            }

            AddAllVendorOfferCurrencyComponentIds(vendorOffers, currencyIds);
            if (currencyIds.Count == 0)
            {
                return null;
            }

            var currencyIndex = new AccountCurrencyIndex(snapshot.Wallet);
            var result = new Dictionary<int, int>(currencyIds.Count);
            foreach (var currencyId in currencyIds)
            {
                result[currencyId] = currencyIndex.GetQuantity(currencyId);
            }

            return result;
        }

        /// <summary>
        /// Widens <paramref name="metadataIds"/> to every offer's unlock
        /// recipe sheet (VendorOffer.UnlockRecipeItemId): PlanResultBuilder
        /// names it in Required Recipes, and it is never a tree ingredient,
        /// a step item or a cost line, so nothing else collects it.
        /// <para>
        /// Separate from AddAllVendorOfferItemComponentIds, not folded into
        /// it, because that method is also what
        /// BuildOwnedVendorItemComponentAmounts scans - an unlock sheet is
        /// not a cost component and must not gain an owned-amount entry.
        /// </para>
        /// </summary>
        private static void AddAllVendorOfferUnlockItemIds(
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers, HashSet<int> metadataIds)
        {
            if (vendorOffers == null)
            {
                return;
            }

            foreach (var offers in vendorOffers.Values)
            {
                if (offers == null)
                {
                    continue;
                }

                foreach (var offer in offers)
                {
                    if (offer != null && offer.UnlockRecipeItemId.HasValue)
                    {
                        metadataIds.Add(offer.UnlockRecipeItemId.Value);
                    }
                }
            }
        }

        /// <summary>
        /// Currency-side twin of AddAllVendorOfferItemComponentIds, using
        /// the same non-coin-currency filter as
        /// VendorBatchSolver.EvaluateVendorOffers so the widened set only
        /// contains ids a real leaf could surface.
        /// </summary>
        private static void AddAllVendorOfferCurrencyComponentIds(
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers, HashSet<int> currencyIds)
        {
            if (vendorOffers == null)
            {
                return;
            }

            foreach (var offers in vendorOffers.Values)
            {
                if (offers == null)
                {
                    continue;
                }

                foreach (var offer in offers)
                {
                    if (offer?.CostLines == null)
                    {
                        continue;
                    }

                    foreach (var cost in offer.CostLines)
                    {
                        if (string.Equals(cost.Type, "Currency", StringComparison.Ordinal)
                            && cost.Id != Gw2Constants.CoinCurrencyId
                            && cost.Count > 0)
                        {
                            currencyIds.Add(cost.Id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Adds every item id appearing as an Item cost line on a winning
        /// BuyFromVendor decision, so synthesized component leaves get a
        /// real name/icon instead of the "Unknown Item" fallback.
        /// </summary>
        private static void AddVendorItemComponentIds(
            IReadOnlyDictionary<int, SolverDecision> decisions, HashSet<int> metadataIds)
        {
            if (decisions == null)
            {
                return;
            }

            foreach (var decision in decisions.Values)
            {
                if (decision.VendorItemCosts == null)
                {
                    continue;
                }

                foreach (var line in decision.VendorItemCosts)
                {
                    metadataIds.Add(line.ItemId);
                }
            }
        }

        /// <summary>
        /// Widens <paramref name="metadataIds"/> to every Item cost line on
        /// ANY vendor offer, not just baseline winning decisions:
        /// ResolveWithOverrides never re-fetches metadata, so an offer
        /// first reached via a manual override would otherwise render
        /// "Unknown Item" until the plan is regenerated. No extra network
        /// round trip - vendorOffers is already fetched.
        /// </summary>
        private static void AddAllVendorOfferItemComponentIds(
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers, HashSet<int> metadataIds)
        {
            if (vendorOffers == null)
            {
                return;
            }

            foreach (var offers in vendorOffers.Values)
            {
                if (offers == null)
                {
                    continue;
                }

                foreach (var offer in offers)
                {
                    if (offer?.CostLines == null)
                    {
                        continue;
                    }

                    foreach (var cost in offer.CostLines)
                    {
                        if (string.Equals(cost.Type, "Item", StringComparison.Ordinal))
                        {
                            metadataIds.Add(cost.Id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Owned-item annotation for vendor cost-component item leaves -
        /// same cosmetic, never-fed-back contract as
        /// BuildOwnedCurrencyAmounts, and widened the same way: scans
        /// every offer so a leaf first surfaced by a manual override still
        /// gets its HAVE pill. Null when there is no snapshot or no such
        /// component, distinguishing "no data" from "0 owned".
        /// </summary>
        private static IReadOnlyDictionary<int, int> BuildOwnedVendorItemComponentAmounts(
            AccountSnapshot snapshot, IReadOnlyDictionary<int, SolverDecision> decisions,
            IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> vendorOffers)
        {
            if (snapshot == null)
            {
                return null;
            }

            var itemIds = new HashSet<int>();
            AddVendorItemComponentIds(decisions, itemIds);
            AddAllVendorOfferItemComponentIds(vendorOffers, itemIds);
            if (itemIds.Count == 0)
            {
                return null;
            }

            var itemIndex = new AccountItemIndex(snapshot.Items);
            var result = new Dictionary<int, int>(itemIds.Count);
            foreach (var itemId in itemIds)
            {
                int total = 0;
                foreach (var source in itemIndex.GetSources(itemId))
                {
                    total += itemIndex.GetQuantity(itemId, source);
                }

                result[itemId] = total;
            }

            return result;
        }

        /// <summary>
        /// Attaches a fire-and-forget continuation that touches Exception
        /// on fault, so a task's failure is always observed even if the
        /// caller's own await of it is skipped (e.g. an earlier awaited
        /// step throws first) - prevents an unobserved task exception at
        /// GC time. Does not change the task's outcome for anyone who does
        /// await it.
        /// </summary>
        private static void ObserveFault(Task task)
        {
            task?.ContinueWith(
                t => { var _ = t.Exception; },
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously);
        }

        private static void CollectItemIds(RecipeNode node, HashSet<int> ids)
        {
            // The synthetic wrapper's sentinel id is not a real GW2 item
            // and must never trigger a TP price fetch; the recursion still
            // walks past it into the N real item roots.
            if (node.IngredientType == "Item" && node.Id != Gw2Constants.MultiItemWrapperItemId)
            {
                ids.Add(node.Id);
            }

            foreach (var recipe in node.Recipes)
            {
                foreach (var ingredient in recipe.Ingredients)
                {
                    CollectItemIds(ingredient, ids);
                }
            }
        }

        /// <summary>
        /// Tracks the coarse user-facing phases of one generation: fires a
        /// live PlanPhaseEvent when a phase starts and writes one Debug
        /// log entry when it completes (next Start, or Finish). Separate
        /// from the finer-grained timingLog channel, which is unchanged.
        /// Single-threaded: constructed fresh per generation and driven
        /// only by that call's own async state machine. If the generation
        /// throws mid-phase, the open phase gets no completion entry; the
        /// wrapper's cancelled/failed log line already reports elapsed time.
        /// </summary>
        private sealed class PhaseTracker
        {
            private readonly IProgress<PlanPhaseEvent> _phaseProgress;
            private readonly ModuleLog _moduleLog;
            private readonly Stopwatch _sw = new Stopwatch();
            private PlanPhase? _currentPhase;
            private string _currentDisplayName;
            private int? _currentTotal;

            public PhaseTracker(IProgress<PlanPhaseEvent> phaseProgress, ModuleLog moduleLog)
            {
                _phaseProgress = phaseProgress;
                _moduleLog = moduleLog;
            }

            /// <summary>
            /// Completes the previous phase (if any), then starts and
            /// reports the new one. <paramref name="total"/> is a count
            /// known up front, or null; <paramref name="detail"/> is an
            /// optional short hint.
            /// </summary>
            public void Start(PlanPhase phase, string displayName, int? total, string detail = null)
            {
                CompleteCurrent();
                _currentPhase = phase;
                _currentDisplayName = displayName;
                _currentTotal = total;
                _sw.Restart();
                _phaseProgress?.Report(new PlanPhaseEvent
                {
                    Phase = phase,
                    DisplayName = displayName,
                    Total = total,
                    Detail = detail,
                });
            }

            /// <summary>
            /// Completes the final phase (writing its Debug entry). Safe to
            /// call even if <see cref="Start"/> was never called (no-op).
            /// </summary>
            public void Finish()
            {
                CompleteCurrent();
            }

            // This Debug figure is wall time between consecutive Start()
            // calls (includes un-instrumented gaps); the Info summary
            // buckets only the stopwatched work, so the same phase can
            // legitimately show two different ms figures.
            private void CompleteCurrent()
            {
                if (_currentPhase == null)
                {
                    return;
                }

                _sw.Stop();
                long ms = _sw.ElapsedMilliseconds;
                string countSuffix = _currentTotal.HasValue
                    ? $" ({_currentTotal.Value} items)"
                    : string.Empty;
                _moduleLog.Write(ModuleLogLevel.Debug, "plan", $"{_currentDisplayName}: {ms}ms{countSuffix}");
                _currentPhase = null;
            }
        }
    }
}
