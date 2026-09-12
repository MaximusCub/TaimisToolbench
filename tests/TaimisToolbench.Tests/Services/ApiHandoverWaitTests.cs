using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Drives the real wait with a real ApiReadySignal and real tasks. No
    /// test here sleeps: a wait that must run out is given a zero window,
    /// which reads the probe once and gives up, and a wait that must end
    /// early is ended by granting access the way Module does.
    /// </summary>
    public class ApiHandoverWaitTests
    {
        [Fact]
        public async Task A_press_that_already_has_access_does_not_wait()
        {
            var states = new List<GameClientState>();
            var wait = Build(granted: () => true, state: () => GameClientState.InWorld);

            Assert.True(await wait.WaitAsync(states.Add, CancellationToken.None));
            Assert.Empty(states);
        }

        /// <summary>
        /// Blish has no client to read MumbleLink from, so no length of
        /// wait produces a subtoken. The press must come straight back.
        /// </summary>
        [Fact]
        public async Task A_press_with_no_game_running_does_not_wait()
        {
            var states = new List<GameClientState>();
            var wait = Build(
                granted: () => false,
                state: () => GameClientState.NotRunning,
                window: _ => TimeSpan.FromDays(1));

            var press = wait.WaitAsync(states.Add, CancellationToken.None);

            Assert.True(press.IsCompleted);
            Assert.False(await press);
            Assert.Empty(states);
        }

        /// <summary>
        /// What the owner hit. He pressed Generate on a loading screen,
        /// the module gave up on one reading of the probe, and the plan
        /// was solved on twelve hour old data.
        /// </summary>
        [Fact]
        public async Task A_press_while_the_game_is_loading_waits_and_then_succeeds()
        {
            bool granted = false;
            var states = new List<GameClientState>();
            var signal = new ApiReadySignal(() => granted);
            var wait = new ApiHandoverWait(
                signal, () => GameClientState.Loading, _ => TimeSpan.FromDays(1));

            var press = wait.WaitAsync(states.Add, CancellationToken.None);

            Assert.False(press.IsCompleted);
            Assert.Equal(new[] { GameClientState.Loading }, states);

            // What Module.Update does on the tick Blish grants the token.
            granted = true;
            Assert.True(signal.IsReady());

            Assert.True(await press);
        }

        /// <summary>
        /// The wait ends on the grant, not on the window. A press that sat
        /// out its whole window after the token arrived would be the same
        /// freeze this was built to remove.
        /// </summary>
        [Fact]
        public async Task The_wait_ends_as_soon_as_the_key_arrives()
        {
            bool granted = false;
            var signal = new ApiReadySignal(() => granted);
            var wait = new ApiHandoverWait(
                signal, () => GameClientState.InWorld, _ => TimeSpan.FromDays(1));

            var press = wait.WaitAsync(null, CancellationToken.None);
            Assert.False(press.IsCompleted);

            granted = true;
            Assert.True(signal.IsReady());

            Assert.True(await press);
        }

        /// <summary>
        /// The escape. A player parked at character select presses again
        /// and gets a plan rather than a second full window of waiting.
        /// </summary>
        [Fact]
        public async Task A_second_press_in_the_same_state_does_not_wait_again()
        {
            var states = new List<GameClientState>();
            var wait = Build(granted: () => false, state: () => GameClientState.Loading);

            Assert.False(await wait.WaitAsync(states.Add, CancellationToken.None));
            Assert.Equal(new[] { GameClientState.Loading }, states);

            var second = wait.WaitAsync(states.Add, CancellationToken.None);

            Assert.True(second.IsCompleted);
            Assert.False(await second);
            Assert.Single(states);
        }

        /// <summary>
        /// Reaching the world is the one change that makes a subtoken
        /// possible where it was not, so it is the one change that buys a
        /// second wait.
        /// </summary>
        [Fact]
        public async Task Signing_in_re_arms_a_wait_that_ran_out_on_a_loading_screen()
        {
            bool granted = false;
            var state = GameClientState.Loading;
            var states = new List<GameClientState>();
            var signal = new ApiReadySignal(() => granted);
            var windows = new Dictionary<GameClientState, TimeSpan>
            {
                { GameClientState.Loading, TimeSpan.Zero },
                { GameClientState.InWorld, TimeSpan.FromDays(1) },
            };
            var wait = new ApiHandoverWait(signal, () => state, s => windows[s]);

            Assert.False(await wait.WaitAsync(states.Add, CancellationToken.None));

            state = GameClientState.InWorld;
            var second = wait.WaitAsync(states.Add, CancellationToken.None);

            Assert.False(second.IsCompleted);
            Assert.Equal(
                new[] { GameClientState.Loading, GameClientState.InWorld }, states);

            granted = true;
            Assert.True(signal.IsReady());

            Assert.True(await second);
        }

        /// <summary>
        /// A wait that ran out with a character already in the world ends
        /// the matter. The world is as far as the client goes, so a later
        /// loading screen is not new information about the key.
        /// </summary>
        [Fact]
        public async Task A_wait_that_ran_out_in_the_world_is_not_re_armed_by_a_loading_screen()
        {
            var state = GameClientState.InWorld;
            var states = new List<GameClientState>();
            var wait = Build(granted: () => false, state: () => state);

            Assert.False(await wait.WaitAsync(states.Add, CancellationToken.None));

            state = GameClientState.Loading;
            var second = wait.WaitAsync(states.Add, CancellationToken.None);

            Assert.True(second.IsCompleted);
            Assert.False(await second);
            Assert.Single(states);
        }

        [Fact]
        public async Task A_cancelled_press_raises_the_cancellation()
        {
            var cts = new CancellationTokenSource();
            var wait = new ApiHandoverWait(
                new ApiReadySignal(() => false),
                () => GameClientState.Loading,
                _ => TimeSpan.FromDays(1));

            var press = wait.WaitAsync(null, cts.Token);
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => press);
        }

        private static ApiHandoverWait Build(
            Func<bool> granted,
            Func<GameClientState> state,
            Func<GameClientState, TimeSpan> window = null)
        {
            return new ApiHandoverWait(
                new ApiReadySignal(granted), state, window ?? (_ => TimeSpan.Zero));
        }
    }
}
