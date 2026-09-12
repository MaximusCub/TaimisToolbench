using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What the account refresh behind one Generate Plan click did. The
    /// Crafting Plan tab reads this to decide whether to interrupt the
    /// user, and to describe the data the finished plan used.
    /// </summary>
    internal class PlanAccountRefresh
    {
        public bool Failed { get; set; }

        /// <summary>
        /// Which unbroken run of failed refreshes this failure belongs to,
        /// or <see cref="RefreshFailureRun.None"/> when nothing failed. The
        /// tab raises its dialog once per run - see
        /// <see cref="StaleDataDialogGate"/>.
        /// </summary>
        public int FailureRunId { get; set; }

        /// <summary>
        /// Which reads went unread. Empty when nothing failed. Every source
        /// when the refresh failed without naming one.
        /// </summary>
        public IReadOnlyList<AccountDataSource> FailedSources { get; set; }

        /// <summary>
        /// Characters whose bags, equipment or disciplines could not be
        /// read in full. Null or empty when none.
        /// </summary>
        public IReadOnlyList<string> IncompleteCharacterNames { get; set; }

        /// <summary>
        /// The account data the generation solved against, refreshed or
        /// not. Null when the account has no snapshot at all.
        /// </summary>
        public AccountSnapshot Snapshot { get; set; }

        /// <summary>
        /// Whether the plan subtracted what the account owns. False when
        /// the user turned Use Own Materials off, and the plan then reads
        /// no holding from <see cref="Snapshot"/>.
        /// </summary>
        public bool UsedHoldings { get; set; }
    }
}
