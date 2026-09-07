namespace TaimisToolbench.Services
{
    /// <summary>
    /// Coarse cause classification for a failed snapshot refresh. The
    /// Snapshot tab's Refresh Now used to show only "Refresh Failed -
    /// {time}" with no hint why; a real incident had every account data
    /// source throw an invalid-token exception because the user was at
    /// CHARACTER SELECT, where Blish has not yet resolved the game's
    /// Mumble identity and therefore has no API key to send.
    /// Produced by <see cref="SnapshotFailureClassifier"/> from the
    /// underlying exception; consumed by Views/MainView.cs to decide
    /// between the ApiAccessNotReady walkthrough dialog and a plain
    /// status-label cause for everything else.
    /// </summary>
    internal enum SnapshotFailureKind
    {
        /// <summary>
        /// No known pattern matched - the same bare "Refresh failed"
        /// wording as before this classification existed.
        /// </summary>
        Unknown,

        /// <summary>
        /// One or more sources failed because the API key is invalid,
        /// missing, or lacks a required permission scope (Gw2Sharp's
        /// InvalidAccessTokenException/AuthorizationRequiredException/
        /// MissingScopesException) - the character-select scenario above.
        /// Takes priority over the other kinds: a broken token affects
        /// every source using it, so it is the actionable cause regardless
        /// of how many sources also failed for other reasons.
        /// </summary>
        ApiAccessNotReady,

        /// <summary>
        /// Every source failed (no partial success) for a network/API-
        /// availability reason - a timeout, 5xx, or similar transport
        /// failure - rather than a token problem.
        /// </summary>
        NetworkOrApiDown,

        /// <summary>
        /// Some sources succeeded and some failed, and no failure was
        /// classified as ApiAccessNotReady. The conservative-persistence
        /// rule (SnapshotFetchFailedException) still throws on this, but
        /// the cause is "an incomplete fetch", not "access is broken" or
        /// "the API is down".
        /// </summary>
        PartialFailure,

        /// <summary>
        /// Every account-wide source was read, but at least one character's
        /// bags, equipment or crafting disciplines was not. The same
        /// conservative-persistence rule throws on this, so the user is
        /// still looking at the previous snapshot and has to be told which
        /// fact the status line is reporting.
        /// </summary>
        IncompleteCharacters,
    }
}
