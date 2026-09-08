using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// When opening the Account Snapshot tab refreshes the account.
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
        /// Whether opening the tab should start a refresh. True when there
        /// is no snapshot at all. False for a snapshot stamped in the
        /// future, which is clock skew rather than freshness the caller can
        /// act on.
        /// </summary>
        public static bool ShouldRefreshOnTabOpen(DateTime? capturedAtUtc, DateTime utcNow)
        {
            if (capturedAtUtc == null)
            {
                return true;
            }

            return utcNow - capturedAtUtc.Value >= TabOpenFreshness;
        }
    }
}
