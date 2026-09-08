using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    internal class CraftingPlanResult
    {
        public CraftingPlan Plan { get; set; }

        public IReadOnlyDictionary<int, ItemMetadata> ItemMetadata { get; set; }

        public List<UsedMaterial> UsedMaterials { get; set; }

        public List<RequiredDiscipline> RequiredDisciplines { get; set; }

        public List<RequiredRecipe> RequiredRecipes { get; set; }

        // Nodes where craft was excluded from the automatic pick because
        // no character meets the winning recipe's discipline requirement,
        // even though it would have been cheaper (see
        // CompetencyOpportunityCalculator). Null/empty when nothing
        // qualifies. Rendered as concrete "would save N" Plan Notes -
        // never fed back into any cost/comparison.
        public List<CompetencyOpportunity> CompetencyOpportunities { get; set; }

        public CraftingTreeNode CraftingTree { get; set; }

        public List<string> DebugLog { get; set; }

        /// <summary>Price basis used for material costs in this plan.</summary>
        public PriceBasis PriceBasis { get; set; }

        /// <summary>
        /// Instant-sell unit price of the target item (buys.unit_price),
        /// null when the item has no buy orders / is untradable. Always
        /// null for a multi-item batch - no single number generalizes N
        /// per-item unit prices.
        /// </summary>
        public long? TargetUnitSellPrice { get; set; }

        /// <summary>
        /// Units the plan actually produces (>= requested quantity when
        /// the chosen root recipe over-produces). For a multi-item batch,
        /// the sum across every requested root with a live sell price -
        /// no craft-vs-buy filter; a root with no sell price is excluded
        /// entirely rather than contributing 0.
        /// </summary>
        public int SellableQuantity { get; set; }

        /// <summary>
        /// Net coin from instant-selling the crafted quantity after the
        /// 15% Trading Post fees; null when no sell price exists. For a
        /// multi-item batch, the sum across every requested root with a
        /// live sell price (no craft-vs-buy filter); null when not one
        /// root has a live sell price.
        /// </summary>
        public long? NetSaleValue { get; set; }

        /// <summary>
        /// NetSaleValue minus the plan's total COIN cost. Non-coin
        /// currency costs are not valued and are excluded; null when no
        /// sell price. For a multi-item batch, the cost subtracted is the
        /// sum of only the SELLABLE roots' own cost - NOT
        /// Plan.TotalCoinCost, which also includes unsellable roots
        /// excluded from this figure entirely.
        /// </summary>
        public long? CraftingProfit { get; set; }

        /// <summary>
        /// Inputs for local re-solving (per-node overrides). Populated by
        /// GenerateStructuredAsync; null on the legacy path.
        /// </summary>
        public PlanSolveContext SolveContext { get; set; }

        /// <summary>
        /// What selling the already-owned UsedMaterials would have netted
        /// after Trading Post fees. Null in OwnMaterialsMode.Free or when
        /// reduction used nothing; an unpriced material contributes 0
        /// rather than being excluded. For a multi-item batch, computed
        /// once over the merged UsedMaterials list, independent of the
        /// per-root sell-price filter - set whenever Valued mode produced
        /// any usedMaterials at all.
        /// </summary>
        public long? MaterialOpportunityCost { get; set; }

        /// <summary>
        /// Name/icon metadata for wallet currencies referenced by
        /// Plan.CurrencyCosts, keyed by currency id. Null when the pipeline
        /// was not given a CurrencyMetadataService, or when that service's
        /// first fetch has not completed yet; CurrencyCost rows then render
        /// text-only using the Gw2Constants offline name fallback (see
        /// PlanViewModelBuilder).
        /// </summary>
        public IReadOnlyDictionary<int, CurrencyMetadata> CurrencyMetadata { get; set; }

        /// <summary>
        /// Wiki-derived acquisition hints for unpriceable items, keyed by
        /// item id (see AcquisitionHintService / ref/acquisition_hints_seed.json).
        /// Hint text is tooltip-only presentation; null when the module was
        /// not wired with hint data.
        /// </summary>
        public IReadOnlyDictionary<int, AcquisitionHint> AcquisitionHints { get; set; }

        /// <summary>
        /// Wiki-verified daily-craft-cooldown data for recipes whose crafting
        /// action itself is server-capped, keyed by item id (see
        /// DailyCooldownItemService / ref/daily_cooldown_items.json).
        /// Additive, informational only - PlanViewModelBuilder reads this to
        /// append a "this will take N days" notice to the Crafting Steps
        /// section for any Craft-source step whose aggregate quantity
        /// exceeds the cap; never affects the solve itself. Null when the
        /// module was not wired with this seed data.
        /// </summary>
        public IReadOnlyDictionary<int, DailyCooldownItem> DailyCooldownItems { get; set; }

        /// <summary>
        /// Owned amount per currency id referenced by Plan.CurrencyCosts
        /// (see AccountCurrencyIndex). Cosmetic display data only - never
        /// fed back into any decision or total. Null when no wallet
        /// snapshot was available or the plan needs no currency.
        /// </summary>
        public IReadOnlyDictionary<int, int> OwnedCurrencyAmounts { get; set; }

        /// <summary>
        /// Owned amount per item id appearing as a vendor Item cost line -
        /// the account-wide total across bank, shared inventory, material
        /// storage and every character's bags (see AccountItemIndex).
        /// Cosmetic display data only, on the same never-fed-back contract
        /// as OwnedCurrencyAmounts above. Null when no account snapshot was
        /// available or no offer carries an item cost line, so a caller can
        /// tell "no data" from "0 owned".
        /// </summary>
        public IReadOnlyDictionary<int, int> OwnedVendorItemAmounts { get; set; }

        /// <summary>
        /// The original per-item request (item id + quantity) this
        /// result was generated
        /// for, in request order. Populated ONLY for a genuine multi-item
        /// batch (2+ requested items, solved via the synthetic wrapper -
        /// see Gw2Constants.MultiItemWrapperItemId); null for a single-item
        /// plan, including a single-item request made through the
        /// multi-item entry point (which short-circuits straight to the
        /// untouched single-item path, echoing gw2e's own `if
        /// (r.length===1) return r[0]` - see
        /// CraftingPlanPipeline.GenerateStructuredAsync's list overload).
        /// A caller must not fall back to Plan.TargetItemId/TargetQuantity
        /// for a multi-item batch: those hold the internal wrapper's own
        /// placeholder id/quantity there and must never be displayed - use
        /// MultiItemRoots (or this list) instead.
        /// </summary>
        public IReadOnlyList<PlanRequestItem> RequestedItems { get; set; }

        /// <summary>
        /// Populated instead of CraftingTree for a multi-item plan: one
        /// full CraftingTreeNode per requested item, in request order.
        /// The synthetic wrapper root used to solve them together never
        /// surfaces here. Null for a single-item plan.
        /// </summary>
        public List<CraftingTreeNode> MultiItemRoots { get; set; }

        /// <summary>
        /// Per-character crafting discipline data - a straight passthrough
        /// of AccountSnapshot.CharacterDisciplines (null "no data" stays
        /// distinct from empty). Cosmetic display data only - never fed
        /// into solving or any total.
        /// </summary>
        public IReadOnlyList<SnapshotCharacterDiscipline> CharacterDisciplines { get; set; }

        /// <summary>
        /// Source of the Plan Notes excess/reclaim lines: per-item
        /// crafting surplus, aggregated across every Decision == Craft
        /// occurrence in CraftingTree/MultiItemRoots - see
        /// Services/ExcessCraftOutputCalculator.Apply, the sole producer.
        /// Cosmetic display data only (same "advisory, never fed back into
        /// a decision or total" contract as MaterialOpportunityCost above -
        /// see that field's own doc comment); null until the calculator
        /// runs, empty (not null) when the tree has no surplus at all.
        /// </summary>
        public List<ExcessCraftOutput> ExcessCraftOutputs { get; set; }

        /// <summary>
        /// Source of the Plan Notes gambling-forge scope caveat:
        /// output item ids of every chosen recipe that is a Mystic-Clover-
        /// style fractional-yield Mystic Forge combine (Disciplines
        /// contains "MysticForge" and ExpectedOutputCount &lt; OutputCount -
        /// see RecipeOption.ExpectedOutputCount's own doc comment).
        /// Populated by PlanResultBuilder.Build alongside RequiredRecipes,
        /// from the same recipeOptionIndex walk. Deliberately does NOT
        /// cover true multi-outcome gambles (precursor forging etc.) -
        /// those never reach the solved tree at all
        /// (docs/gw2e-considerations.md #17) so there is nothing here to detect for
        /// them; PlanViewModelBuilder's note wording must not conflate the
        /// two. Empty list (not null) when no such recipe was chosen,
        /// matching RequiredDisciplines/RequiredRecipes' own convention.
        /// </summary>
        public List<int> ProbabilisticForgeOutputItemIds { get; set; }

        /// <summary>
        /// See RecipeSheetSavingsCalculator.Apply, the sole producer.
        /// Cosmetic display data only; null until the calculator runs,
        /// empty (not null) when it found nothing.
        /// </summary>
        public List<RecipeSheetSavingsOpportunity> RecipeSheetSavingsOpportunities { get; set; }

        /// <summary>
        /// See SeasonalVendorTipCalculator.Apply, the sole producer.
        /// Cosmetic display data only; null until the calculator runs,
        /// empty (not null) when no active festival beats this plan.
        /// </summary>
        public List<SeasonalVendorTip> SeasonalVendorTips { get; set; }

        /// <summary>
        /// See MissingRecipeSheetSourceCalculator.Apply, the sole
        /// producer. Cosmetic display data only; null until the calculator
        /// runs, empty (not null) when no missing recipe has a sheet any
        /// vendor sells.
        /// </summary>
        public List<MissingRecipeSheetSource> MissingRecipeSheetSources { get; set; }
    }
}
