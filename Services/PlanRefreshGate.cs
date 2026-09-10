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
        private readonly ApiReadySignal _apiReady;
        private readonly Func<bool> _inWorld;
        private readonly Func<bool> _inFailureBackoff;
        private readonly Func<CancellationToken, Task<AccountSnapshot>> _fetch;

        public PlanRefreshGate(
            SnapshotRefreshSlot slot,
            ApiReadySignal apiReady,
            Func<bool> inWorld,
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

            if (inWorld == null)
            {
                throw new ArgumentNullException("inWorld");
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
            _inWorld = inWorld;
            _inFailureBackoff = inFailureBackoff;
            _fetch = fetch;
        }

        /// <summary>
        /// Runs the refresh this press should run, and names what it did.
        /// A fetch that throws throws out of here: the caller owns what a
        /// failed attempt says to the user.
        /// <para>
        /// Freshness is settled before API access, so a press that wants no
        /// fetch waits for no subtoken.
        /// </para>
        /// </summary>
        public async Task<PlanRefreshOutcome> RunAsync(
            DateTime? capturedAtUtc, DateTime utcNow, TimeSpan apiWaitBudget, CancellationToken ct)
        {
            if (!SnapshotRefreshPolicy.ShouldRefreshOnGenerate(capturedAtUtc, utcNow))
            {
                return PlanRefreshOutcome.UsedLoadedData;
            }

            if (!_apiReady.IsReady())
            {
                // Blish renews a module's subtoken off a MumbleLink
                // character-name change, and MumbleLink does not tick
                // outside the world. Waiting out of world waits for
                // something that cannot happen.
                if (!_inWorld())
                {
                    return PlanRefreshOutcome.NotInWorld;
                }

                if (!await _apiReady.WaitAsync(apiWaitBudget, ct))
                {
                    return PlanRefreshOutcome.NoApiAccess;
                }
            }

            // Two passes, not a spin. The second only runs when another
            // refresh claimed AND released the slot between this caller's
            // read and its own claim, which leaves nothing to wait on and
            // the slot free again. A third pass would need that to happen
            // twice inside two field reads, and an unbounded loop on the
            // Generate path is not worth the case it would cover.
            for (int attempt = 0; attempt < 2; attempt++)
            {
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

                if (_slot.TryClaim())
                {
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

            return PlanRefreshOutcome.LostTheClaim;
        }
    }
}
