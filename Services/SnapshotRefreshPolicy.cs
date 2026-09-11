using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// When an account snapshot already on hand is fresh enough to use, and
    /// when the module fetches a new one instead. Two entry points, one per
    /// trigger, so the two windows are named beside each other rather than
    /// spelled out at their call sites.
    /// </summary>
    internal static class SnapshotRefreshPolicy
    {
        /// <summary>
        /// How fresh the snapshot has to be for opening the tab to leave it
        /// alone.
        /// <para>
        /// Fifteen seconds is the maintainer's figure. It is short enough
        /// that a tab whose whole job is showing what the account holds
        /// almost always shows current data, and long enough that flicking
        /// between two tabs does not refresh twice. A refresh of a
        /// nine-character account was measured at 7 to 11 seconds and costs
        /// roughly 33 API requests, so a shorter window would let tab
        /// switching alone spend the request budget.
        /// </para>
        /// </summary>
        public static readonly TimeSpan TabOpenFreshness = TimeSpan.FromSeconds(15);

        /// <summary>
        /// How fresh the snapshot has to be for Generate Plan to solve
        /// against what is already loaded instead of fetching the account
        /// again.
        /// <para>
        /// One minute, the maintainer's figure, for the reason he gave: a
        /// freshness window stops repeated Generate presses firing a full
        /// account fetch each time. Generate refreshes unconditionally
        /// without one, and a fetch costs six account-wide requests plus
        /// three per character, so five presses in a minute cost five of
        /// those.
        /// </para>
        /// <para>
        /// Longer than <see cref="TabOpenFreshness"/> because the two
        /// triggers want different things. The Snapshot tab exists to show
        /// current holdings, so it pays for currency. A plan is solved from
        /// holdings that moved seconds ago and holdings that moved a minute
        /// ago alike.
        /// </para>
        /// </summary>
        public static readonly TimeSpan GenerateFreshness = TimeSpan.FromSeconds(60);

        /// <summary>
        /// How long a Generate Plan press waits for Blish to hand the
        /// module its API subtoken before solving without it.
        /// <para>
        /// Blish only renews a module's subtoken when MumbleLink reports a
        /// character name change, and MumbleLink does not tick outside the
        /// world. So the wait is for the handover itself, which was
        /// measured at 1.16 and 2.36 seconds from the first in-world tick
        /// across two sessions. Five seconds is roughly double the slower
        /// of the two.
        /// </para>
        /// <para>
        /// It covers the handover alone, so it only applies once a
        /// character is in the world. The screens before that are
        /// <see cref="GameLoadingHandover"/>.
        /// </para>
        /// </summary>
        public static readonly TimeSpan SubtokenHandover = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How long a Generate Plan press waits for the subtoken while the
        /// game is running with no character in the world.
        /// <para>
        /// Twenty seconds. One press at 03:36:42.727 solved on twelve hour
        /// old data and warned that it had; the subtoken arrived at
        /// 03:36:55.915, 13.2 seconds later, on the first frame
        /// Module.Update found API access. Twenty covers that measurement
        /// with margin.
        /// </para>
        /// <para>
        /// It is the whole wait rather than an addition to
        /// <see cref="SubtokenHandover"/>, because the handover cannot
        /// start until the world loads and so is already inside this
        /// window. A wait that runs out is not repeated in the same client
        /// state - see ApiHandoverWait.
        /// </para>
        /// </summary>
        public static readonly TimeSpan GameLoadingHandover = TimeSpan.FromSeconds(20);

        /// <summary>
        /// How long a press waits for the subtoken in
        /// <paramref name="state"/>. Zero when the game is not running,
        /// because nothing is on its way.
        /// </summary>
        public static TimeSpan HandoverWaitFor(GameClientState state)
        {
            switch (state)
            {
                case GameClientState.InWorld:
                    return SubtokenHandover;
                case GameClientState.Loading:
                    return GameLoadingHandover;
                default:
                    return TimeSpan.Zero;
            }
        }

        /// <summary>
        /// Whether opening the tab should start a refresh. True when there
        /// is no snapshot at all. False for a snapshot stamped in the
        /// future, which is clock skew rather than freshness the caller can
        /// act on.
        /// </summary>
        public static bool ShouldRefreshOnTabOpen(DateTime? capturedAtUtc, DateTime utcNow)
        {
            return IsOlderThan(capturedAtUtc, utcNow, TabOpenFreshness);
        }

        /// <summary>
        /// Whether Generate Plan should refresh the account before it
        /// solves. Same two edge rules as
        /// <see cref="ShouldRefreshOnTabOpen"/>, over
        /// <see cref="GenerateFreshness"/>.
        /// </summary>
        public static bool ShouldRefreshOnGenerate(DateTime? capturedAtUtc, DateTime utcNow)
        {
            return IsOlderThan(capturedAtUtc, utcNow, GenerateFreshness);
        }

        /// <summary>
        /// The rule both triggers share. No snapshot is always stale. A
        /// future stamp yields a negative age, which is below every window,
        /// so clock skew reads as fresh rather than firing a refresh on
        /// every trigger for as long as the skew lasts.
        /// </summary>
        private static bool IsOlderThan(DateTime? capturedAtUtc, DateTime utcNow, TimeSpan window)
        {
            if (capturedAtUtc == null)
            {
                return true;
            }

            return utcNow - capturedAtUtc.Value >= window;
        }
    }
}
