using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The single-fetch slot the account-snapshot refresh runs in: at most
    /// one refresh owns it at a time, and it owns that refresh's
    /// CancellationTokenSource and the task other callers wait on.
    /// <para>
    /// The claim is an Interlocked.CompareExchange, the same pattern
    /// ModuleLog.ScheduleFlush uses, and the source swap is a single
    /// Interlocked.Exchange so exactly one caller ever owns the outgoing
    /// source. A volatile bool cannot serve here: volatile makes a write
    /// VISIBLE, it does not make check-then-set ATOMIC.
    /// </para>
    /// <para>
    /// What the claim installs IS the thing losers wait on, so a caller
    /// that loses can always find the refresh that won.
    /// </para>
    /// <para>What the old check-then-set gate cost in practice:
    /// docs/ARCHITECTURE.md, "Services Q-Z: relocated design narrative".</para>
    /// </summary>
    internal sealed class SnapshotRefreshSlot
    {
        private Claim _claim;
        private CancellationTokenSource _cts;

        /// <summary>
        /// Whether a refresh currently holds the slot. Advisory only - a
        /// caller that intends to refresh must go through
        /// <see cref="TryClaim"/>, which is the actual gate.
        /// </summary>
        public bool IsClaimed
        {
            get { return Volatile.Read(ref _claim) != null; }
        }

        /// <summary>
        /// The refresh currently holding the slot, or null when none does.
        /// Non-null from the instant a claim is granted, before the fetch
        /// behind it starts, so a caller that loses <see cref="TryClaim"/>
        /// has something to wait on every time.
        /// </summary>
        public Task<AccountSnapshot> RunningFetch
        {
            get
            {
                var claim = Volatile.Read(ref _claim);
                return claim == null ? null : claim.Waiters.Task;
            }
        }

        /// <summary>
        /// Claims the slot for this caller, or returns false if another
        /// caller already holds it. Exactly one of any number of concurrent
        /// callers gets true. A caller that gets true MUST call
        /// <see cref="Release"/> in a finally.
        /// </summary>
        public bool TryClaim()
        {
            return Interlocked.CompareExchange(ref _claim, new Claim(), null) == null;
        }

        /// <summary>
        /// Hands this claim's fetch to whoever is waiting on
        /// <see cref="RunningFetch"/>. They get what it returns, and the
        /// exception or the cancellation if it does not return.
        /// </summary>
        public void PublishFetch(Task<AccountSnapshot> fetch)
        {
            var claim = Volatile.Read(ref _claim);
            if (claim == null || fetch == null)
            {
                return;
            }

            // Recorded before the continuation is attached so Release can
            // settle the waiters itself. Release runs in the claimant's
            // finally, which can beat this continuation to a fetch that has
            // only just completed, and cancelling the waiters there would
            // report a refresh that did happen as one that did not.
            Volatile.Write(ref claim.Fetch, fetch);

            fetch.ContinueWith(
                (completed, state) => Settle((Claim)state, completed),
                claim,
                TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// Ends the claim, handing its waiters whatever the fetch behind it
        /// did. A claim released without ever publishing a fetch cancels
        /// them: nothing was read, and telling them otherwise would report
        /// a refresh that never ran.
        /// </summary>
        public void Release()
        {
            var claim = Interlocked.Exchange(ref _claim, null);
            if (claim == null)
            {
                return;
            }

            var fetch = Volatile.Read(ref claim.Fetch);
            if (fetch == null)
            {
                claim.Waiters.TrySetCanceled();
            }
            else if (fetch.IsCompleted)
            {
                Settle(claim, fetch);
            }

            // else: the continuation PublishFetch attached settles them.
        }

        /// <summary>
        /// Publishes a fresh CancellationTokenSource as the live one,
        /// cancelling and disposing whatever it replaced, and returns the new
        /// token. The token is captured before publication precisely so the
        /// caller never has to re-read the field it just wrote.
        /// </summary>
        public CancellationToken BeginFetch()
        {
            var next = new CancellationTokenSource();
            var token = next.Token;
            Swap(next);
            return token;
        }

        /// <summary>
        /// Cancels and disposes the live source, if any, and leaves the slot
        /// with none - Clear Cache and Unload. Safe to call any number of
        /// times and from any thread; the source is disposed exactly once.
        /// </summary>
        public void CancelCurrent()
        {
            Swap(null);
        }

        private static void Settle(Claim claim, Task<AccountSnapshot> fetch)
        {
            if (fetch.IsFaulted)
            {
                claim.Waiters.TrySetException(fetch.Exception.InnerExceptions);

                // Marks the stored exception observed. Nothing waits on a
                // claim no other caller joined, and an unobserved faulted
                // task raises TaskScheduler.UnobservedTaskException when it
                // is finalized. A waiter still gets the exception.
                var observed = claim.Waiters.Task.Exception;
                _ = observed;
            }
            else if (fetch.IsCanceled)
            {
                claim.Waiters.TrySetCanceled();
            }
            else
            {
                claim.Waiters.TrySetResult(fetch.Result);
            }
        }

        private void Swap(CancellationTokenSource next)
        {
            var previous = Interlocked.Exchange(ref _cts, next);
            previous?.Cancel();
            previous?.Dispose();
        }

        /// <summary>
        /// One refresh's hold on the slot: what waiters wait on, and the
        /// fetch that settles them once it exists.
        /// </summary>
        private sealed class Claim
        {
            public readonly TaskCompletionSource<AccountSnapshot> Waiters =
                new TaskCompletionSource<AccountSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);

            public Task<AccountSnapshot> Fetch;
        }
    }
}
