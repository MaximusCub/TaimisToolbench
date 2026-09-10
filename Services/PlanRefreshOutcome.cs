namespace TaimisToolbench.Services
{
    /// <summary>
    /// What one Generate Plan press did about the account snapshot. A
    /// refresh that was attempted and threw is not here: it leaves
    /// <see cref="PlanRefreshGate"/> as an exception.
    /// </summary>
    internal enum PlanRefreshOutcome
    {
        /// <summary>
        /// The loaded snapshot was inside
        /// SnapshotRefreshPolicy.GenerateFreshness, so the plan solved
        /// against it.
        /// </summary>
        UsedLoadedData,

        /// <summary>This press fetched the account itself.</summary>
        Refreshed,

        /// <summary>
        /// This press waited for a refresh another caller was already
        /// running, and solved against what that refresh read.
        /// </summary>
        JoinedRunningFetch,

        /// <summary>
        /// The module has no GW2 API access it can use, so there was
        /// nothing to fetch with.
        /// </summary>
        NoApiAccess,

        /// <summary>
        /// A refresh failed recently and its backoff window is still open,
        /// so this press did not spend an attempt that would fail the same
        /// way.
        /// </summary>
        SkippedInBackoff,

        /// <summary>
        /// Another caller claimed the refresh slot between this press
        /// reading the published fetch and trying to claim it, and no
        /// fetch had been published to wait for.
        /// </summary>
        LostTheClaim,
    }
}
