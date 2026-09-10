using System;
using System.Threading;
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
        public async Task A_press_with_no_api_access_does_not_fetch()
        {
            int fetches = 0;
            var gate = Build(apiReady: false, fetches: () => fetches++);

            var outcome = await gate.RunAsync(null, Now);

            Assert.Equal(PlanRefreshOutcome.NoApiAccess, outcome);
            Assert.Equal(0, fetches);
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
        /// and has not published its fetch yet, which is where every
        /// claimant sits for the length of one method call. A press landing
        /// there finds nothing to wait for and cannot claim, so it solves
        /// against whatever snapshot is loaded and says nothing.
        /// </summary>
        [Fact]
        public async Task A_press_that_lands_between_a_claim_and_its_publication_neither_fetches_nor_waits()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.True(slot.TryClaim());
            Assert.Null(slot.RunningFetch);

            int fetches = 0;
            var gate = Build(slot: slot, fetches: () => fetches++);

            var outcome = await gate.RunAsync(null, Now);

            Assert.Equal(PlanRefreshOutcome.LostTheClaim, outcome);
            Assert.Equal(0, fetches);
        }

        [Fact]
        public async Task A_fetch_that_throws_throws_out_of_the_gate()
        {
            var gate = new PlanRefreshGate(
                new SnapshotRefreshSlot(),
                () => true,
                () => false,
                ct => { throw new InvalidOperationException("the account read failed"); });

            await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(null, Now));
        }

        private static PlanRefreshGate Build(
            SnapshotRefreshSlot slot = null,
            bool apiReady = true,
            bool inFailureBackoff = false,
            Action fetches = null)
        {
            return new PlanRefreshGate(
                slot ?? new SnapshotRefreshSlot(),
                () => apiReady,
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
