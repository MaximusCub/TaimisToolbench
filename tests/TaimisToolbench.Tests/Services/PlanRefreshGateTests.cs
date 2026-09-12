using System;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Drives the real gate with a real refresh slot and real tasks. The
    /// "fetch" stands in for Module.FetchForPlanAsync, which is Blish-bound
    /// and cannot be reached from this suite.
    /// </summary>
    public class PlanRefreshGateTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 10, 3, 2, 19, DateTimeKind.Utc);

        [Fact]
        public async Task A_press_on_data_inside_the_freshness_window_does_not_fetch()
        {
            int fetches = 0;
            var gate = Build(fetches: () => fetches++);

            var outcome = await gate.RunAsync(Now.AddSeconds(-30), Now);

            Assert.Equal(PlanRefreshOutcome.UsedLoadedData, outcome);
            Assert.Equal(0, fetches);
        }

        [Fact]
        public async Task A_press_on_data_past_the_freshness_window_fetches()
        {
            int fetches = 0;
            var gate = Build(fetches: () => fetches++);

            var outcome = await gate.RunAsync(Now.AddSeconds(-90), Now);

            Assert.Equal(PlanRefreshOutcome.Refreshed, outcome);
            Assert.Equal(1, fetches);
        }

        [Fact]
        public async Task A_press_in_world_with_no_api_access_does_not_fetch()
        {
            int fetches = 0;
            var gate = Build(apiReady: false, state: GameClientState.InWorld, fetches: () => fetches++);

            var outcome = await gate.RunAsync(null, Now);

            Assert.Equal(PlanRefreshOutcome.NoApiAccess, outcome);
            Assert.Equal(0, fetches);
        }

        /// <summary>
        /// Out of world the player is told to sign in, in the world they
        /// are told the module has no API access. The wait that precedes
        /// both is over by the time the gate runs - see ApiHandoverWait.
        /// </summary>
        [Fact]
        public async Task A_press_out_of_world_with_no_api_access_names_the_sign_in()
        {
            int fetches = 0;
            var gate = Build(apiReady: false, state: GameClientState.Loading, fetches: () => fetches++);

            var press = gate.RunAsync(null, Now);

            Assert.True(press.IsCompleted);
            Assert.Equal(PlanRefreshOutcome.NotInWorld, await press);
            Assert.Equal(0, fetches);
        }

        [Fact]
        public async Task A_press_out_of_world_still_fetches_once_the_subtoken_has_arrived()
        {
            int fetches = 0;
            var gate = Build(apiReady: true, state: GameClientState.Loading, fetches: () => fetches++);

            var outcome = await gate.RunAsync(null, Now);

            Assert.Equal(PlanRefreshOutcome.Refreshed, outcome);
            Assert.Equal(1, fetches);
        }

        [Fact]
        public async Task A_press_inside_the_failure_backoff_does_not_fetch()
        {
            int fetches = 0;
            var gate = Build(inFailureBackoff: true, fetches: () => fetches++);

            var outcome = await gate.RunAsync(null, Now);

            Assert.Equal(PlanRefreshOutcome.SkippedInBackoff, outcome);
            Assert.Equal(0, fetches);
        }

        [Fact]
        public async Task A_press_waits_for_a_fetch_that_is_already_published()
        {
            var slot = new SnapshotRefreshSlot();
            var running = new TaskCompletionSource<AccountSnapshot>();
            slot.TryClaim();
            slot.PublishFetch(running.Task);

            int fetches = 0;
            var gate = Build(slot: slot, fetches: () => fetches++);

            var press = gate.RunAsync(null, Now);
            Assert.False(press.IsCompleted);

            running.SetResult(new AccountSnapshot { CapturedAt = Now });

            Assert.Equal(PlanRefreshOutcome.JoinedRunningFetch, await press);
            Assert.Equal(0, fetches);
        }

        /// <summary>
        /// The window this suite exists for. A refresh has claimed the slot
        /// and has not started its fetch yet, which is where every claimant
        /// sits for the length of one method call. A press landing there
        /// used to find nothing to wait for and no slot to claim, so it
        /// solved against whatever snapshot was loaded and said nothing.
        /// </summary>
        [Fact]
        public async Task A_press_that_lands_between_a_claim_and_its_fetch_waits_for_that_claim()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.True(slot.TryClaim());

            int fetches = 0;
            var gate = Build(slot: slot, fetches: () => fetches++);

            var press = gate.RunAsync(null, Now);
            Assert.False(press.IsCompleted);

            // What the winning claimant does next, one method call later.
            var winning = new TaskCompletionSource<AccountSnapshot>();
            slot.PublishFetch(winning.Task);
            Assert.False(press.IsCompleted);

            winning.SetResult(new AccountSnapshot { CapturedAt = Now });

            Assert.Equal(PlanRefreshOutcome.JoinedRunningFetch, await press);
            Assert.Equal(0, fetches);
        }

        [Fact]
        public async Task A_press_waiting_on_a_claim_that_fails_sees_the_failure()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.True(slot.TryClaim());
            var gate = Build(slot: slot);

            var press = gate.RunAsync(null, Now);

            var winning = new TaskCompletionSource<AccountSnapshot>();
            slot.PublishFetch(winning.Task);
            winning.SetException(new InvalidOperationException("the account read failed"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => press);
        }

        [Fact]
        public async Task A_fetch_that_throws_throws_out_of_the_gate()
        {
            var gate = new PlanRefreshGate(
                new SnapshotRefreshSlot(),
                new ApiReadySignal(() => true),
                () => GameClientState.InWorld,
                () => false,
                ct => { throw new InvalidOperationException("the account read failed"); });

            await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(null, Now));
        }

        /// <summary>
        /// Freshness is settled before API access, so a press that wants
        /// no fetch is not held up by a key the module has not got.
        /// </summary>
        [Fact]
        public async Task A_press_on_fresh_data_uses_it_without_any_api_access()
        {
            var gate = Build(apiReady: false);

            var outcome = await gate.RunAsync(Now.AddSeconds(-30), Now);

            Assert.Equal(PlanRefreshOutcome.UsedLoadedData, outcome);
        }

        private static PlanRefreshGate Build(
            SnapshotRefreshSlot slot = null,
            bool apiReady = true,
            GameClientState state = GameClientState.InWorld,
            bool inFailureBackoff = false,
            Action fetches = null)
        {
            return new PlanRefreshGate(
                slot ?? new SnapshotRefreshSlot(),
                new ApiReadySignal(() => apiReady),
                () => state,
                () => inFailureBackoff,
                ct =>
                {
                    if (fetches != null)
                    {
                        fetches();
                    }

                    return Task.FromResult(new AccountSnapshot { CapturedAt = Now });
                });
        }
    }
}
