namespace TaimisToolbench.Services
{
    /// <summary>
    /// The seven coarse, user-facing phases of one Generate Plan press:
    /// the wait Module spends on API access, then the six
    /// CraftingPlanPipeline.GenerateStructuredAsync reports. Deliberately coarser than the pipeline's own internal timingLog
    /// (~10 detailed steps - see CraftingPlanPipeline.FinishTimingLog),
    /// which keeps reporting unchanged; this enum exists purely to drive a
    /// stable, small live indicator (CraftingPlanView's status strip) that
    /// does not need to parse free-form text to know what stage generation
    /// is in.
    /// </summary>
    internal enum PlanPhase
    {
        // First because it is the only phase that runs before the pipeline
        // does, and PhaseOrdinalGuard needs declaration order to match
        // emission order. Module reports it, and only on a press that
        // actually waits - see ApiHandoverWait and Module's own
        // WaitForApiHandoverAsync.
        WaitingForGame,

        BuildingTree,
        FetchingPrices,
        SolvingDecisions,
        FetchingItemDetails,

        // Its own phase, not part of FetchingItemDetails: this is a single
        // /v2/account/recipes round trip, measured at 327-4557ms, and
        // folding it into the item-details phase made the status strip
        // claim "Fetching item details (N items)" while it was blocked on
        // an account endpoint. PhaseOrdinalGuard depends on this position
        // matching the pipeline's actual emission order.
        CheckingLearnedRecipes,

        BuildingDisplay,
    }

    /// <summary>
    /// One "a new phase has started" notification. Blish-free (no
    /// Blish_HUD/Gw2Sharp/Microsoft.Xna usings, matching every other type in
    /// this namespace - see ModuleLogEntry's own doc comment for why) so
    /// CraftingPlanPipeline stays independently testable. Reported via an
    /// optional IProgress&lt;PlanPhaseEvent&gt; callback threaded through
    /// CraftingPlanPipeline.GenerateStructuredAsync, alongside (not
    /// replacing) the existing IProgress&lt;PlanStatus&gt; channel
    /// (Models/PlanStatus.cs) - that channel keeps reporting its own
    /// finer-grained per-step text completely unchanged. Null callback = no
    /// behavior change, matching every other optional progress parameter
    /// this pipeline already has.
    /// </summary>
    internal class PlanPhaseEvent
    {
        public PlanPhase Phase { get; set; }

        /// <summary>Ready-to-render phase label, e.g. "Fetching prices".</summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Items/steps completed so far within this phase, if known. Always
        /// null in v1 (phase-level granularity only - see this milestone's
        /// own doc comment on why sub-phase counts are out of scope);
        /// reserved so a future finer-grained update does not need a new
        /// wire type.
        /// </summary>
        public int? Done { get; set; }

        /// <summary>Item/step count for this phase, if known up front (e.g. items to price). Null when not applicable.</summary>
        public int? Total { get; set; }

        /// <summary>
        /// Optional short additional detail, e.g. "may take several
        /// seconds on first run" on the very first BuildingTree event of a
        /// cold recipe cache (see
        /// CraftingPlanPipeline.FirstRunTreeHint and
        /// PlanStripTickDecision.FormatPhaseText). Null for every phase
        /// that has none.
        /// </summary>
        public string Detail { get; set; }
    }
}
