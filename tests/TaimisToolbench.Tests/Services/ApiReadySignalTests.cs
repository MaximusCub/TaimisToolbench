using System;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class ApiReadySignalTests
    {
        [Fact]
        public void A_probe_that_says_no_leaves_the_signal_unready()
        {
            var signal = new ApiReadySignal(() => false);
            Assert.False(signal.IsReady());
        }

        /// <summary>
        /// A key removed or narrowed mid-session has to read as no again.
        /// Module's other guards stop refreshing on that reading, and
        /// caching the first yes would have them spend requests that
        /// cannot work.
        /// </summary>
        [Fact]
        public void A_key_that_stops_working_reads_as_not_ready_again()
        {
            bool granted = true;
            var signal = new ApiReadySignal(() => granted);
            Assert.True(signal.IsReady());

            granted = false;

            Assert.False(signal.IsReady());
        }

        [Fact]
        public async Task A_wait_already_released_stays_released_when_the_key_stops_working()
        {
            bool granted = false;
            var signal = new ApiReadySignal(() => granted);

            var waited = signal.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
            granted = true;
            signal.IsReady();
            Assert.True(await waited);

            granted = false;

            Assert.True(await signal.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None));
        }

        [Fact]
        public async Task A_wait_with_no_budget_left_reads_the_probe_and_does_not_wait()
        {
            var signal = new ApiReadySignal(() => false);

            var waited = signal.WaitAsync(TimeSpan.Zero, CancellationToken.None);

            Assert.True(waited.IsCompleted);
            Assert.False(await waited);
        }

        [Fact]
        public async Task A_wait_with_no_budget_left_still_reports_access_already_granted()
        {
            var signal = new ApiReadySignal(() => true);

            Assert.True(await signal.WaitAsync(TimeSpan.Zero, CancellationToken.None));
        }

        /// <summary>
        /// The reason the signal exists. Blish grants the subtoken seconds
        /// after load, and whichever part of the module reads the probe
        /// first releases everything waiting on it.
        /// </summary>
        [Fact]
        public async Task A_wait_ends_as_soon_as_another_reader_sees_access_granted()
        {
            bool granted = false;
            var signal = new ApiReadySignal(() => granted);

            var waited = signal.WaitAsync(TimeSpan.FromMinutes(1), CancellationToken.None);
            Assert.False(waited.IsCompleted);

            granted = true;
            signal.IsReady();

            Assert.True(await waited);
        }

        [Fact]
        public async Task A_cancelled_generation_ends_the_wait_rather_than_holding_it_open()
        {
            var signal = new ApiReadySignal(() => false);
            using (var cts = new CancellationTokenSource())
            {
                var waited = signal.WaitAsync(TimeSpan.FromMinutes(1), cts.Token);
                Assert.False(waited.IsCompleted);

                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waited);
            }
        }
    }
}
