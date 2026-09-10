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
    /// <see cref="BeginFetch"/> hands back the token by value, before the
    /// source is published, so a concurrent <see cref="CancelCurrent"/>
    /// cancels the fetch - which is what it means - instead of racing a
    /// field read the fetch would otherwise have to make.
    /// </para>
    /// <para>
    /// What the claim installs IS the thing losers wait on, so a caller
    /// that loses can always find the refresh that won. Publishing the
    /// fetch separately left a window between the winning claim and the
    /// publication in which a loser found nothing to wait on and no slot
    /// to claim.
    /// </para>
    /// <para>What the old check-then-set gate cost in practice:
    /// docs/ARCHITECTURE.md, "Services Q-Z: relocated design narrative".</para>
    /// </summary>
    internal sealed class SnapshotRefreshSlot
    {
        private TaskCompletionSource<AccountSnapshot> _claim;
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
                return claim == null ? null : claim.Task;
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
            var candidate = new TaskCompletionSource<AccountSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            return Interlocked.CompareExchange(ref _claim, candidate, null) == null;
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

            fetch.ContinueWith(
                (completed, state) => Settle((TaskCompletionSource<AccountSnapshot>)state, completed),
                claim,
                TaskContinuationOptions.ExecuteSynchronously);
        }

        /// <summary>
        /// Ends the claim. A claim released without ever publishing a fetch
        /// hands its waiters a cancellation: nothing was read, and telling
        /// them otherwise would report a refresh that never ran.
        /// </summary>
        public void Release()
        {
            var claim = Interlocked.Exchange(ref _claim, null);
            if (claim != null)
            {
                claim.TrySetCanceled();
            }
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

        private static void Settle(TaskCompletionSource<AccountSnapshot> claim, Task<AccountSnapshot> fetch)
        {
            if (fetch.IsFaulted)
            {
                claim.TrySetException(fetch.Exception.InnerExceptions);

                // Marks the stored exception observed. Nothing waits on a
                // claim no other caller joined, and an unobserved faulted
                // task raises TaskScheduler.UnobservedTaskException when
                // it is finalized. A waiter still gets the exception.
                var observed = claim.Task.Exception;
                _ = observed;
            }
            else if (fetch.IsCanceled)
            {
                claim.TrySetCanceled();
            }
            else
            {
                claim.TrySetResult(fetch.Result);
            }
        }

        private void Swap(CancellationTokenSource next)
        {
            var previous = Interlocked.Exchange(ref _cts, next);
            previous?.Cancel();
            previous?.Dispose();
        }
    }
}
