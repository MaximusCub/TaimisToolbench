using System;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The account refresh behind one Generate Plan press: whether to
    /// fetch, whether to wait for a fetch already running, and whether to
    /// leave the loaded snapshot alone.
    /// <para>
    /// Blish-free so the orderings it has to get right - a press landing
    /// beside a refresh the module started for its own reasons - are
    /// reachable from tests. Module supplies the three readings it cannot:
    /// whether Blish has granted usable API access, whether a recent
    /// failure's backoff window is open, and the fetch itself.
    /// </para>
    /// </summary>
    internal sealed class PlanRefreshGate
    {
        private readonly SnapshotRefreshSlot _slot;
        private readonly Func<bool> _apiReady;
        private readonly Func<bool> _inFailureBackoff;
        private readonly Func<CancellationToken, Task<AccountSnapshot>> _fetch;

        public PlanRefreshGate(
            SnapshotRefreshSlot slot,
            Func<bool> apiReady,
            Func<bool> inFailureBackoff,
            Func<CancellationToken, Task<AccountSnapshot>> fetch)
        {
            if (slot == null)
            {
                throw new ArgumentNullException("slot");
            }

            if (apiReady == null)
            {
                throw new ArgumentNullException("apiReady");
            }

            if (inFailureBackoff == null)
            {
                throw new ArgumentNullException("inFailureBackoff");
            }

            if (fetch == null)
            {
                throw new ArgumentNullException("fetch");
            }

            _slot = slot;
            _apiReady = apiReady;
            _inFailureBackoff = inFailureBackoff;
            _fetch = fetch;
        }

        /// <summary>
        /// Runs the refresh this press should run, and names what it did.
        /// A fetch that throws throws out of here: the caller owns what a
        /// failed attempt says to the user.
        /// </summary>
        public async Task<PlanRefreshOutcome> RunAsync(DateTime? capturedAtUtc, DateTime utcNow)
        {
            if (!_apiReady())
            {
                return PlanRefreshOutcome.NoApiAccess;
            }

            if (!SnapshotRefreshPolicy.ShouldRefreshOnGenerate(capturedAtUtc, utcNow))
            {
                return PlanRefreshOutcome.UsedLoadedData;
            }

            var running = _slot.RunningFetch;
            if (running != null)
            {
                await running;
                return PlanRefreshOutcome.JoinedRunningFetch;
            }

            if (_inFailureBackoff())
            {
                return PlanRefreshOutcome.SkippedInBackoff;
            }

            if (!_slot.TryClaim())
            {
                return PlanRefreshOutcome.LostTheClaim;
            }

            try
            {
                await _fetch(_slot.BeginFetch());
                return PlanRefreshOutcome.Refreshed;
            }
            finally
            {
                _slot.Release();
            }
        }
    }
}
