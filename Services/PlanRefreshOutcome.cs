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
        /// The player is not in the world, so Blish cannot hand the module
        /// a subtoken yet and waiting for one would achieve nothing.
        /// </summary>
        NotInWorld,

        /// <summary>
        /// The player is in the world and the subtoken still did not
        /// arrive, so the module has no GW2 API access it can use.
        /// </summary>
        NoApiAccess,

        /// <summary>
        /// A refresh failed recently and its backoff window is still open,
        /// so this press did not spend an attempt that would fail the same
        /// way.
        /// </summary>
        SkippedInBackoff,

        /// <summary>
        /// Two passes each found the slot taken by the time this press
        /// tried to claim it and free by the time it looked for something
        /// to wait on. Nothing was fetched and nothing was waited for.
        /// </summary>
        LostTheClaim,
    }
}
