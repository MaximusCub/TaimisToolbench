namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure decision for whether a live <see cref="PlanPhaseEvent"/> should
    /// still be applied to the status strip: an event whose phase ordinal is
    /// not strictly greater than the last one actually APPLIED is stale and
    /// must be dropped, whatever order events drain in. Called at the moment
    /// each event drains, not when it was queued - the race it closes cannot
    /// be seen any other way, and StatusUpdateGuard alone cannot catch it.
    /// <para>
    /// This holds only because <see cref="PlanPhase"/>'s declaration order
    /// IS the emission order (WaitingForGame -&gt; BuildingTree -&gt;
    /// FetchingPrices -&gt; SolvingDecisions -&gt; FetchingItemDetails
    /// -&gt; CheckingLearnedRecipes -&gt; BuildingDisplay), which makes its
    /// int ordinal a monotonic sequence per generation. Inserting or
    /// reordering a member out of emission order breaks this guard
    /// silently - see PlanPhaseEvent's own doc comment, Module's
    /// WaitForApiHandoverAsync and CraftingPlanPipeline's
    /// phaseTracker.Start call sites.
    /// </para>
    /// <para>Derivation: docs/ARCHITECTURE.md section 6.1.</para>
    /// </summary>
    internal static class PhaseOrdinalGuard
    {
        public static bool ShouldApply(int eventPhaseOrdinal, int currentPhaseOrdinal)
        {
            return eventPhaseOrdinal > currentPhaseOrdinal;
        }
    }
}
