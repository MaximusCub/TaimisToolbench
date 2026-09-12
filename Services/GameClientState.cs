namespace TaimisToolbench.Services
{
    /// <summary>
    /// How far along the Guild Wars 2 client is, as far as an API subtoken
    /// is concerned. Blish-free so the wait that reads it is testable.
    /// </summary>
    internal enum GameClientState
    {
        /// <summary>
        /// Blish HUD is not attached to a running client, so nothing about
        /// the game is going to change.
        /// </summary>
        NotRunning,

        /// <summary>
        /// Running with no character in the world: a loading screen, a
        /// cinematic, or character select. Blish reports the three as one
        /// state and cannot separate them, so a wait here is waiting on a
        /// screen that may end in a second or may never end.
        /// </summary>
        Loading,

        /// <summary>
        /// A character is in the world and MumbleLink is ticking, which is
        /// the one condition under which Blish renews a module's subtoken.
        /// </summary>
        InWorld,
    }
}
