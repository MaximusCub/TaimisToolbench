using System;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The wait one Generate Plan press spends on Blish handing the module
    /// its API subtoken, before the plan is solved on whatever data is
    /// already loaded.
    /// <para>
    /// How long a wait can help depends on the client state, because Blish
    /// renews a subtoken off a MumbleLink character-name change and
    /// MumbleLink does not tick outside the world. The two windows are
    /// SnapshotRefreshPolicy.SubtokenHandover and
    /// SnapshotRefreshPolicy.GameLoadingHandover.
    /// </para>
    /// <para>
    /// A wait that ran out is not spent again while nothing has changed,
    /// which is what lets a second press mean "go ahead without it". Only
    /// reaching the world re-arms it, and only after a wait that ran out
    /// before the world loaded: that is the single change that makes a
    /// subtoken possible where it was not.
    /// </para>
    /// </summary>
    internal sealed class ApiHandoverWait
    {
        private const int NothingGivenUpOn = -1;

        private readonly ApiReadySignal _apiReady;
        private readonly Func<GameClientState> _gameState;

        // SnapshotRefreshPolicy.HandoverWaitFor in the module. Injected so
        // a test can drive the ran-out path without spending the real
        // window on the clock.
        private readonly Func<GameClientState, TimeSpan> _waitFor;

        // The client state a wait last ran out in, as its enum ordinal, or
        // NothingGivenUpOn. An int rather than a GameClientState? so a read
        // and a write from two threads cannot tear across the nullable's
        // two fields; presses are serialized by the view today, and this
        // does not rely on that staying true.
        private int _gaveUpIn = NothingGivenUpOn;

        public ApiHandoverWait(
            ApiReadySignal apiReady,
            Func<GameClientState> gameState,
            Func<GameClientState, TimeSpan> waitFor)
        {
            if (apiReady == null)
            {
                throw new ArgumentNullException("apiReady");
            }

            if (gameState == null)
            {
                throw new ArgumentNullException("gameState");
            }

            if (waitFor == null)
            {
                throw new ArgumentNullException("waitFor");
            }

            _apiReady = apiReady;
            _gameState = gameState;
            _waitFor = waitFor;
        }

        /// <summary>
        /// Waits for API access if waiting could produce any, and reports
        /// whether the module has it by the time this returns.
        /// <para>
        /// <paramref name="onWaitStarted"/> runs only when a real wait
        /// begins, with the state it is waiting in, so a caller can name
        /// the wait on screen without having to guess beforehand whether
        /// one will happen.
        /// </para>
        /// </summary>
        public async Task<bool> WaitAsync(Action<GameClientState> onWaitStarted, CancellationToken ct)
        {
            GameClientState state;
            if (!WouldWait(out state))
            {
                return _apiReady.IsReady();
            }

            onWaitStarted?.Invoke(state);

            if (await _apiReady.WaitAsync(_waitFor(state), ct))
            {
                return true;
            }

            Volatile.Write(ref _gaveUpIn, (int)state);
            return false;
        }

        /// <summary>
        /// Whether a press right now would wait, and the state it read to
        /// decide. False once access is granted, once the game is not
        /// running, and once a wait in this state has already run out.
        /// </summary>
        private bool WouldWait(out GameClientState state)
        {
            state = _gameState();

            if (_apiReady.IsReady())
            {
                return false;
            }

            // Read as the state, not as a zero budget: a wait still
            // STARTS when the budget is zero, and the caller has to be told
            // about it so the one it is told about is the one that happens.
            if (state == GameClientState.NotRunning)
            {
                return false;
            }

            int gaveUpIn = Volatile.Read(ref _gaveUpIn);
            if (gaveUpIn == NothingGivenUpOn)
            {
                return true;
            }

            // A wait that ran out with a character already in the world
            // ends the matter: the world is as far as the client goes, so
            // no later state can make the subtoken arrive.
            return gaveUpIn != (int)state && gaveUpIn != (int)GameClientState.InWorld;
        }
    }
}
