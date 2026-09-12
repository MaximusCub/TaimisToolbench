using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// Everything needed to re-solve a generated plan locally (no network):
    /// the reduced tree plus the fetched prices, offers, and metadata from
    /// the originating generation. Enables instant per-node override
    /// recomputes in the UI.
    /// </summary>
    internal class PlanSolveContext
    {
        public int TargetItemId { get; set; }

        public int Quantity { get; set; }

        public RecipeNode Tree { get; set; }

        public IReadOnlyDictionary<int, ItemPrice> Prices { get; set; }

        public IReadOnlyDictionary<int, IReadOnlyList<VendorOffer>> VendorOffers { get; set; }

        /// <summary>
        /// What one unit of each vendor cost-line item cost at GENERATION
        /// time, snapshotted for the same reason as Prices/VendorOffers
        /// above: an override re-solve must cost a vendor offer's cost lines
        /// exactly the way the original Generate did, and re-deriving them
        /// would need the recipe corpus and a fresh price fetch.
        /// <para>
        /// The VALUES are snapshotted, not the acquisition subtrees behind
        /// them - a few dozen small rows rather than several thousand
        /// RecipeNodes, on a path that re-serializes the whole context on
        /// every override click. Null when no cost line needed one, which
        /// behaves as the solver did before cost lines were solved.
        /// </para>
        /// </summary>
        public IReadOnlyDictionary<int, CostLineUnitValue> VendorCostLineValues { get; set; }

        public IReadOnlyDictionary<int, ItemMetadata> Metadata { get; set; }

        public ISet<int> LearnedRecipeIds { get; set; }

        public List<UsedMaterial> UsedMaterials { get; set; }

        public PriceBasis PriceBasis { get; set; }

        /// <summary>
        /// Currency name/icon metadata snapshotted at GENERATION time, so
        /// that ResolveWithOverrides' local re-solve can reuse it on
        /// CurrencyCost rows without any network call (same reasoning as
        /// Prices/VendorOffers/Metadata above).
        /// </summary>
        public IReadOnlyDictionary<int, CurrencyMetadata> CurrencyMetadata { get; set; }

        /// <summary>
        /// The currency valuation in effect at GENERATION time, snapshotted
        /// here alongside Prices/VendorOffers/Metadata. This is intentional:
        /// ResolveWithOverrides re-solves locally and, like prices and
        /// vendor data, deliberately reuses the generation-time valuation
        /// rather than re-reading live settings - a local override toggle
        /// must not silently re-price the plan out from under the user with
        /// whatever the settings say right now. Freshly edited rates apply
        /// starting with the next full Generate.
        /// </summary>
        public CurrencyValuation CurrencyValuation { get; set; }

        /// <summary>
        /// The own-materials valuation mode in effect at GENERATION time,
        /// snapshotted here for the same reason as CurrencyValuation: a
        /// local override re-solve must keep pricing owned materials the
        /// way the original Generate did, not whatever CraftingPlanView's
        /// per-plan "Value Own Materials" checkbox currently shows (VOM
        /// design Section 5.2 - this is a per-plan session choice, not a
        /// live-read global setting, exactly like PriceBasis above). A
        /// freshly toggled checkbox applies starting with the next full
        /// Generate. Valued now covers both the pre-existing 15% sell-back
        /// force-buy guard AND the decision-invariant reduction guide fed
        /// into InventoryReducer.Reduce (see that method's zeroOwnedDecisions
        /// doc comment) - Free behaves exactly as before this design.
        /// </summary>
        public OwnMaterialsMode OwnMaterialsMode { get; set; }

        /// <summary>
        /// Wiki-derived acquisition hints snapshotted at GENERATION time, so
        /// that ResolveWithOverrides' local re-solve can keep hint text on
        /// unpriceable nodes without any refetch (same reasoning as
        /// CurrencyMetadata above - this is a static local seed, not a live
        /// fetch, but the snapshot keeps the two code paths symmetric).
        /// </summary>
        public IReadOnlyDictionary<int, AcquisitionHint> AcquisitionHints { get; set; }

        /// <summary>
        /// Wiki-verified daily-craft-cooldown data snapshotted at GENERATION
        /// time, mirroring AcquisitionHints immediately above (same
        /// static-local-seed reasoning) - so ResolveWithOverrides' local
        /// re-solve keeps producing craft-cooldown notices without any
        /// refetch.
        /// </summary>
        public IReadOnlyDictionary<int, DailyCooldownItem> DailyCooldownItems { get; set; }

        /// <summary>
        /// Per-node owned-quantity attribution snapshotted at generation
        /// time. NodeId is stable across repeat Solve() calls on the same
        /// Tree, so a local re-solve reuses this as-is.
        /// </summary>
        public IReadOnlyDictionary<int, int> OwnedQuantityUsedByNodeId { get; set; }

        /// <summary>
        /// Owned amount per currency id referenced by the plan's
        /// CurrencyCosts, snapshotted at generation time. Cosmetic only;
        /// null when no wallet snapshot or no currency need.
        /// </summary>
        public IReadOnlyDictionary<int, int> OwnedCurrencyAmounts { get; set; }

        /// <summary>
        /// Owned amount per item id appearing as a vendor Item cost line,
        /// snapshotted at generation time. Cosmetic only - feeds a
        /// component leaf's informational pill, never consulted by the
        /// reducer or solver. Null under the same conditions as
        /// OwnedCurrencyAmounts.
        /// </summary>
        public IReadOnlyDictionary<int, int> OwnedVendorItemAmounts { get; set; }

        /// <summary>
        /// NodeIds the force-buy pre-pass excluded from crafting at
        /// generation time, snapshotted so a local re-solve keeps
        /// applying it rather than forgetting it on the first pill click.
        /// Null when the pre-pass never ran.
        /// </summary>
        public ISet<int> ForceBuyOnlyNodeIds { get; set; }

        /// <summary>
        /// The competency-independent subset of ForceBuyOnlyNodeIds (see
        /// OwnedMaterialsForceBuyPrePass.ForceBuyPrePassResult),
        /// reapplied on every local re-solve so Plan Notes never diverge
        /// from the original generation. Null under the same conditions
        /// as ForceBuyOnlyNodeIds.
        /// </summary>
        public ISet<int> CompetencyIndependentForceBuyNodeIds { get; set; }

        /// <summary>
        /// The original per-item request snapshotted at GENERATION time,
        /// for the same
        /// reason as CurrencyValuation/OwnMaterialsMode above - so a local
        /// override re-solve (ResolveWithOverrides) can keep populating
        /// CraftingPlanResult.RequestedItems/MultiItemRoots consistently on
        /// every re-solve of a multi-item batch, not just the first
        /// generation. Null for a single-item plan (Tree is then the real
        /// item's own tree, not the synthetic wrapper).
        /// </summary>
        public IReadOnlyList<PlanRequestItem> RequestedItems { get; set; }

        /// <summary>
        /// The Homestead Refinement efficiency tier configuration in effect
        /// at GENERATION time, snapshotted here for
        /// the same reason as CurrencyValuation/OwnMaterialsMode above: a
        /// local override re-solve (ResolveWithOverrides) must keep gating
        /// Homestead offers the way the original Generate did, not whatever
        /// the setting reads right now. A freshly changed tier setting
        /// applies starting with the next full Generate.
        /// </summary>
        public HomesteadEfficiencyTiers HomesteadTiers { get; set; }

        /// <summary>
        /// Per-character crafting discipline data snapshotted at GENERATION
        /// time, for the
        /// same reason as OwnedCurrencyAmounts above: so a local override
        /// re-solve (ResolveWithOverrides) can keep populating
        /// CraftingPlanResult.CharacterDisciplines on every re-solve, not
        /// just the first generation. Cosmetic display data only; null
        /// under the same conditions as the source field.
        /// </summary>
        public IReadOnlyList<SnapshotCharacterDiscipline> CharacterDisciplines { get; set; }

        /// <summary>
        /// The ORIGINAL, unreduced tree from GENERATION
        /// time (the same `tree` OwnedMaterialsForceBuyPrePass and the
        /// zero-owned decision pass ran against, in
        /// CraftingPlanPipeline), snapshotted here ONLY when the force-buy pre-pass
        /// ran (ForceBuyOnlyNodeIds != null) so ResolveWithOverrides can
        /// re-run InventoryReducer.Reduce with an overrides-aware guide on
        /// every local re-solve, instead of permanently replaying overrides
        /// against the ALREADY-reduced Tree above - which froze ingredient
        /// discounts against the zero-owned decision forever, even after a
        /// manual override flips a force-buy-flagged node to Craft. Null
        /// whenever the pre-pass did not run (Free mode, or no snapshot/
        /// reducer at generation time) - Tree/UsedMaterials above are
        /// already final and correct in that case, so no re-reduction is
        /// needed or possible. See AccountItems/ActiveCharacterName below,
        /// both populated under the exact same condition as this field.
        /// </summary>
        public RecipeNode UnreducedTree { get; set; }

        /// <summary>
        /// The raw owned-item entries InventoryReducer.Reduce's
        /// AccountItemIndex was built from at GENERATION time (NOT the
        /// AccountItemIndex itself - that type lives in the Services
        /// namespace, and Models deliberately never references Services),
        /// retained so ResolveWithOverrides can rebuild an identical index
        /// and re-run reduction against the SAME owned-material pool - see
        /// UnreducedTree's doc comment for when this is populated.
        /// </summary>
        public IReadOnlyList<SnapshotItemEntry> AccountItems { get; set; }

        /// <summary>
        /// The active character name at GENERATION time, threaded through
        /// to a re-reduction the same way UnreducedTree/AccountItems are -
        /// see UnreducedTree's doc comment.
        /// </summary>
        public string ActiveCharacterName { get; set; }

        /// <summary>
        /// What the account had unlocked at GENERATION time, snapshotted
        /// for the same reason as LearnedRecipeIds above: a local override
        /// re-solve must re-derive the vendor-requirement notices without a
        /// network call. Null when no client was wired up or the fetch
        /// failed, which reads as "not checked" rather than "not met".
        /// </summary>
        public AccountProgression AccountProgression { get; set; }

        /// <summary>
        /// Why <see cref="AccountProgression"/> is what it is, snapshotted
        /// with it so a re-solve tells the player the same thing about the
        /// same read - see Models/AccountProgression.cs.
        /// </summary>
        public AccountProgressionAccess AccountProgressionAccess { get; set; }
    }
}
