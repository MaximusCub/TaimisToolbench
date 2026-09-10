using System;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Whether Blish has granted the module GW2 API access it can use, and
    /// a way to wait for it.
    /// <para>
    /// Blish hands a module its subtoken asynchronously after load, so for
    /// the first seconds of a session the probe says no on an account that
    /// is perfectly well configured. One such session was measured at 29
    /// seconds. A caller that reads the probe once in that window and gives
    /// up cannot tell "not yet" from "not ever".
    /// </para>
    /// <para>
    /// Every reading latches, so a wait ends as soon as any part of the
    /// module observes access rather than only when Blish raises its event
    /// - which FirstLoadSnapshotGate records can fire before the handler
    /// is attached.
    /// </para>
    /// </summary>
    internal sealed class ApiReadySignal
    {
        private readonly Func<bool> _probe;

        private readonly TaskCompletionSource<bool> _latched =
            new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        public ApiReadySignal(Func<bool> probe)
        {
            if (probe == null)
            {
                throw new ArgumentNullException("probe");
            }

            _probe = probe;
        }

        /// <summary>
        /// Reads the probe and latches on the first reading that says yes.
        /// </summary>
        public bool IsReady()
        {
            if (_latched.Task.IsCompleted)
            {
                return true;
            }

            if (!_probe())
            {
                return false;
            }

            _latched.TrySetResult(true);
            return true;
        }

        /// <summary>
        /// True as soon as access is granted, or false once
        /// <paramref name="budget"/> runs out. A budget of zero or less
        /// reads the probe and does not wait.
        /// </summary>
        public async Task<bool> WaitAsync(TimeSpan budget, CancellationToken ct)
        {
            if (IsReady())
            {
                return true;
            }

            if (budget <= TimeSpan.Zero)
            {
                return false;
            }

            var finished = await Task.WhenAny(_latched.Task, Task.Delay(budget, ct));
            if (finished == _latched.Task)
            {
                return true;
            }

            // Task.WhenAny hands back a cancelled task rather than throwing
            // for it, so the caller's cancellation has to be re-raised here
            // or a cancelled generation reads as one with no API access.
            ct.ThrowIfCancellationRequested();

            // The probe is read again on the timeout path: a grant that
            // arrived while nothing else in the module happened to read it
            // has nothing to latch it.
            return IsReady();
        }
    }
}
