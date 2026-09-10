using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The single-fetch slot the account-snapshot refresh runs in: at most one
    /// refresh owns it at a time, and it owns that refresh's
    /// CancellationTokenSource.
    /// <para>
    /// The claim is an Interlocked.CompareExchange, the same pattern
    /// ModuleLog.ScheduleFlush uses, and the source swap is a single
    /// Interlocked.Exchange so exactly one caller ever owns the outgoing
    /// source. A volatile bool cannot serve here: volatile makes a write
    /// VISIBLE, it does not make check-then-set ATOMIC.
    /// <see cref="BeginFetch"/> hands back the token by value, before the
    /// source is published, so a concurrent <see cref="CancelCurrent"/> cancels
    /// the fetch - which is what it means - instead of racing a field read the
    /// fetch would otherwise have to make.
    /// </para>
    /// <para>What the old check-then-set gate cost in practice:
    /// docs/ARCHITECTURE.md, "Services Q-Z: relocated design narrative".</para>
    /// </summary>
    internal sealed class SnapshotRefreshSlot
    {
        private int _claimed;
        private CancellationTokenSource _cts;
        private Task<AccountSnapshot> _fetchInFlight;

        /// <summary>
        /// The fetch a later caller joins instead of starting its own, or
        /// null when none is published. Only a caller holding the claim
        /// ever publishes one, so at most one is live; a joiner can still
        /// read one that has just finished, which costs it an
        /// already-completed await.
        /// </summary>
        public Task<AccountSnapshot> RunningFetch
        {
            get { return Volatile.Read(ref _fetchInFlight); }
        }

        /// <summary>
        /// Publishes the fetch this claim is running, so a later caller
        /// can wait for it.
        /// </summary>
        public void PublishFetch(Task<AccountSnapshot> fetch)
        {
            Volatile.Write(ref _fetchInFlight, fetch);
        }

        /// <summary>
        /// Withdraws <paramref name="fetch"/> only while it is still the
        /// published one, so a later refresh's publication survives this
        /// one's exit.
        /// </summary>
        public void ClearFetch(Task<AccountSnapshot> fetch)
        {
            Interlocked.CompareExchange(ref _fetchInFlight, null, fetch);
        }

        /// <summary>
        /// Whether a refresh currently holds the slot. Advisory only - a
        /// caller that intends to refresh must go through
        /// <see cref="TryClaim"/>, which is the actual gate.
        /// </summary>
        public bool IsClaimed => Volatile.Read(ref _claimed) != 0;

        /// <summary>
        /// Claims the slot for this caller, or returns false if another
        /// caller already holds it. Exactly one of any number of concurrent
        /// callers gets true. A caller that gets true MUST call
        /// <see cref="Release"/> in a finally.
        /// </summary>
        public bool TryClaim()
        {
            return Interlocked.CompareExchange(ref _claimed, 1, 0) == 0;
        }

        public void Release()
        {
            Interlocked.Exchange(ref _claimed, 0);
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

        private void Swap(CancellationTokenSource next)
        {
            var previous = Interlocked.Exchange(ref _cts, next);
            previous?.Cancel();
            previous?.Dispose();
        }
    }
}
