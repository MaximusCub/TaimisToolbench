using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Module reaches this slot from three threads (LoadAsync on a ThreadPool
    /// task, Update() on the main thread, OnSubtokenUpdated on a thread the
    /// module does not control) through two entry points, so the tests that
    /// matter here are the concurrent ones. They drive the real slot; the
    /// "fetch" is a stand-in for FetchAndSaveSnapshotAsync, which is Blish-
    /// bound and cannot be reached from this suite.
    /// </summary>
    public class SnapshotRefreshSlotTests
    {
        private const int Contenders = 8;

        // Keeps the token read above from being optimized away without
        // tripping the unused-local rule.
        private static bool _tokenReadSink;

        [Fact]
        public void TryClaim_grants_the_slot_to_exactly_one_of_many_concurrent_callers()
        {
            for (int round = 0; round < 200; round++)
            {
                var slot = new SnapshotRefreshSlot();
                int granted = RunTogether(Contenders, () => slot.TryClaim() ? 1 : 0).Sum();
                Assert.Equal(1, granted);
            }
        }

        [Fact]
        public void A_released_slot_is_claimable_again_by_exactly_one_caller()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.True(slot.TryClaim());
            Assert.False(slot.TryClaim());

            slot.Release();

            int granted = RunTogether(Contenders, () => slot.TryClaim() ? 1 : 0).Sum();
            Assert.Equal(1, granted);
        }

        [Fact]
        public void IsClaimed_tracks_the_claim()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.False(slot.IsClaimed);

            Assert.True(slot.TryClaim());
            Assert.True(slot.IsClaimed);

            slot.Release();
            Assert.False(slot.IsClaimed);
        }

        /// <summary>
        /// The failure this closes: both entry points got past the old
        /// non-atomic guard, each ran cancel/dispose/assign over the shared
        /// field, and the loser's own Token read then threw
        /// ObjectDisposedException - which Module reported as a network
        /// failure and punished with a 60-second retry backoff.
        /// </summary>
        [Fact]
        public void Both_entry_points_hammering_the_slot_run_one_fetch_at_a_time_and_never_throw()
        {
            var slot = new SnapshotRefreshSlot();
            var failures = new ConcurrentQueue<Exception>();
            int concurrentFetches = 0;
            int peakConcurrency = 0;
            int fetchesRun = 0;

            RunTogether(Contenders, () =>
            {
                for (int i = 0; i < 300; i++)
                {
                    if (!slot.TryClaim())
                    {
                        continue;
                    }

                    try
                    {
                        // Exactly what both Module entry points do inside
                        // their claim: publish a source, read the token it
                        // handed back, fetch under it.
                        CancellationToken token = slot.BeginFetch();

                        int now = Interlocked.Increment(ref concurrentFetches);
                        InterlockedMax(ref peakConcurrency, now);
                        Assert.False(token.IsCancellationRequested);
                        Interlocked.Increment(ref fetchesRun);
                        Interlocked.Decrement(ref concurrentFetches);
                    }
                    catch (Exception ex)
                    {
                        failures.Enqueue(ex);
                    }
                    finally
                    {
                        slot.Release();
                    }
                }

                return 0;
            });

            Assert.Empty(failures);
            Assert.Equal(1, peakConcurrency);
            Assert.True(fetchesRun > 0, "no fetch ever claimed the slot");
        }

        /// <summary>
        /// The claim is what a loser waits on, so a granted claim is
        /// waitable before the fetch behind it exists. Losing the claim and
        /// finding nothing published is what made a Generate press solve
        /// against old data without saying so.
        /// </summary>
        [Fact]
        public void Every_caller_that_loses_the_claim_finds_the_refresh_that_won()
        {
            for (int round = 0; round < 200; round++)
            {
                var slot = new SnapshotRefreshSlot();
                var waitable = new ConcurrentQueue<Task<AccountSnapshot>>();

                int granted = RunTogether(Contenders, () =>
                {
                    bool won = slot.TryClaim();
                    if (!won)
                    {
                        waitable.Enqueue(slot.RunningFetch);
                    }

                    return won ? 1 : 0;
                }).Sum();

                Assert.Equal(1, granted);
                Assert.Equal(Contenders - 1, waitable.Count);
                Assert.DoesNotContain(null, waitable);
                slot.Release();
            }
        }

        [Fact]
        public async Task A_published_fetch_hands_its_result_to_a_waiter()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.True(slot.TryClaim());
            Task<AccountSnapshot> waiter = slot.RunningFetch;

            var fetch = new TaskCompletionSource<AccountSnapshot>();
            slot.PublishFetch(fetch.Task);
            Assert.False(waiter.IsCompleted);

            var fetched = new AccountSnapshot { CapturedAt = new DateTime(2026, 9, 10, 3, 2, 45, DateTimeKind.Utc) };
            fetch.SetResult(fetched);

            Assert.Same(fetched, await waiter);
        }

        [Fact]
        public async Task A_published_fetch_hands_its_failure_to_a_waiter()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.True(slot.TryClaim());
            Task<AccountSnapshot> waiter = slot.RunningFetch;

            var fetch = new TaskCompletionSource<AccountSnapshot>();
            slot.PublishFetch(fetch.Task);
            fetch.SetException(new InvalidOperationException("the account read failed"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => waiter);
        }

        /// <summary>
        /// A claim can end without ever starting a fetch - BeginFetch
        /// throwing out of a registered cancellation callback is the case
        /// on record. Its waiters must not be told a refresh happened.
        /// </summary>
        [Fact]
        public async Task A_claim_released_without_a_fetch_cancels_its_waiters()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.True(slot.TryClaim());
            Task<AccountSnapshot> waiter = slot.RunningFetch;

            slot.Release();

            await Assert.ThrowsAsync<TaskCanceledException>(() => waiter);
        }

        [Fact]
        public void A_free_slot_has_no_refresh_to_wait_on()
        {
            var slot = new SnapshotRefreshSlot();
            Assert.Null(slot.RunningFetch);

            Assert.True(slot.TryClaim());
            Assert.NotNull(slot.RunningFetch);

            slot.Release();
            Assert.Null(slot.RunningFetch);
        }

        /// <summary>
        /// Clear Cache and Unload cancel the live source from a thread of
        /// their own while a fetch is running. That must cancel the fetch,
        /// not corrupt the slot: the old shape disposed the source out from
        /// under a token read and could dispose the same instance twice.
        /// </summary>
        [Fact]
        public async Task CancelCurrent_racing_BeginFetch_never_double_disposes_or_throws()
        {
            var slot = new SnapshotRefreshSlot();
            var failures = new ConcurrentQueue<Exception>();
            var stop = new ManualResetEventSlim(false);

            var canceller = Task.Run(() =>
            {
                while (!stop.IsSet)
                {
                    try
                    {
                        slot.CancelCurrent();
                    }
                    catch (Exception ex)
                    {
                        failures.Enqueue(ex);
                    }
                }
            });

            for (int i = 0; i < 5000; i++)
            {
                try
                {
                    CancellationToken token = slot.BeginFetch();

                    // Reading a token whose source a racing CancelCurrent has
                    // since disposed must still be legal - that is the whole
                    // point of handing it back by value.
                    Volatile.Write(ref _tokenReadSink, token.IsCancellationRequested);
                }
                catch (Exception ex)
                {
                    failures.Enqueue(ex);
                }
            }

            stop.Set();
            await canceller;
            slot.CancelCurrent();
            stop.Dispose();

            Assert.Empty(failures);
        }

        [Fact]
        public void CancelCurrent_cancels_the_token_the_live_fetch_is_running_under()
        {
            var slot = new SnapshotRefreshSlot();
            CancellationToken token = slot.BeginFetch();
            Assert.False(token.IsCancellationRequested);

            slot.CancelCurrent();

            Assert.True(token.IsCancellationRequested);
        }

        [Fact]
        public void A_new_fetch_cancels_the_one_it_supersedes()
        {
            var slot = new SnapshotRefreshSlot();
            CancellationToken first = slot.BeginFetch();

            CancellationToken second = slot.BeginFetch();

            Assert.True(first.IsCancellationRequested);
            Assert.False(second.IsCancellationRequested);
        }

        [Fact]
        public void CancelCurrent_is_safe_with_nothing_in_flight_and_safe_to_repeat()
        {
            var slot = new SnapshotRefreshSlot();
            slot.CancelCurrent();
            slot.BeginFetch();
            slot.CancelCurrent();
            slot.CancelCurrent();
        }

        private static int[] RunTogether(int threadCount, Func<int> body)
        {
            var results = new int[threadCount];
            using (var barrier = new Barrier(threadCount))
            {
                var tasks = Enumerable.Range(0, threadCount)
                    .Select(i => Task.Run(() =>
                    {
                        barrier.SignalAndWait();
                        results[i] = body();
                    }))
                    .ToArray();

                Task.WaitAll(tasks);
            }

            return results;
        }

        private static void InterlockedMax(ref int target, int value)
        {
            int seen = Volatile.Read(ref target);
            while (value > seen)
            {
                int actual = Interlocked.CompareExchange(ref target, value, seen);
                if (actual == seen)
                {
                    return;
                }

                seen = actual;
            }
        }
    }
}
