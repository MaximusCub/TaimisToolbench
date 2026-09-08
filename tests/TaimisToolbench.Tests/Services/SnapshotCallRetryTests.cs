using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// One character's equipment fetch failed on 2026-09-07 and the
    /// snapshot committed 1020 items instead of 1039. These tests pin the
    /// policy that now repeats such a call in place: which failures earn
    /// another attempt, which do not, and how many calls a failure costs.
    /// <para>
    /// Gw2Sharp's exception classes cannot be referenced from the test
    /// project, so the policy matches on Exception.GetType().Name and the
    /// stand-ins at the bottom of this file carry those names.
    /// </para>
    /// </summary>
    public class SnapshotCallRetryTests
    {
        // Substitutes for the real wait so the suite never sleeps. It also
        // records what the policy asked for, which is the schedule itself.
        private readonly List<TimeSpan> _waits = new List<TimeSpan>();

        private Task RecordWait(TimeSpan delay, CancellationToken ct)
        {
            _waits.Add(delay);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task TransientFailure_SucceedsOnTheSecondAttempt()
        {
            int calls = 0;

            int result = await SnapshotCallRetry.RunAsync<int>(
                ct =>
                {
                    calls++;
                    if (calls == 1)
                    {
                        throw new ServerErrorException();
                    }

                    return Task.FromResult(1039);
                },
                null,
                RecordWait,
                CancellationToken.None);

            Assert.Equal(1039, result);
            Assert.Equal(2, calls);
            Assert.Equal(new[] { TimeSpan.FromMilliseconds(500) }, _waits);
        }

        [Fact]
        public async Task PersistentFailure_StopsAtTheCeilingAndThrowsTheLastFailure()
        {
            int calls = 0;

            var thrown = await Assert.ThrowsAsync<ServerErrorException>(
                () => SnapshotCallRetry.RunAsync<int>(
                    ct =>
                    {
                        calls++;
                        throw new ServerErrorException(calls);
                    },
                    null,
                    RecordWait,
                    CancellationToken.None));

            Assert.Equal(3, SnapshotCallRetry.MaxAttempts);
            Assert.Equal(3, calls);
            Assert.Equal(3, thrown.Attempt);
            Assert.Equal(
                new[] { TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(2) },
                _waits);
        }

        [Fact]
        public async Task PerCallTimeout_IsNotRetried()
        {
            // A call that spent its whole 20 second deadline has already
            // taken a third of the smallest fetch budget. Two more would
            // put the fetch past it, so the deadline ends the attempts.
            int calls = 0;

            await Assert.ThrowsAsync<TimeoutException>(
                () => SnapshotCallRetry.RunAsync<int>(
                    ct =>
                    {
                        calls++;
                        throw new TimeoutException("GW2 API request exceeded 20s.");
                    },
                    null,
                    RecordWait,
                    CancellationToken.None));

            Assert.Equal(1, calls);
            Assert.Empty(_waits);
        }

        [Fact]
        public async Task FetchDeadlineCancellation_IsNotRetried()
        {
            int calls = 0;
            using (var cts = new CancellationTokenSource())
            {
                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => SnapshotCallRetry.RunAsync<int>(
                        ct =>
                        {
                            calls++;
                            cts.Cancel();
                            throw new OperationCanceledException(cts.Token);
                        },
                        null,
                        RecordWait,
                        cts.Token));
            }

            Assert.Equal(1, calls);
            Assert.Empty(_waits);
        }

        [Fact]
        public async Task ApiFailureAfterTheDeadlineFired_IsNotRetried()
        {
            // The budget is already gone, so another attempt can only make
            // the fetch later. The failure still propagates as itself.
            int calls = 0;
            using (var cts = new CancellationTokenSource())
            {
                await Assert.ThrowsAsync<ServerErrorException>(
                    () => SnapshotCallRetry.RunAsync<int>(
                        ct =>
                        {
                            calls++;
                            cts.Cancel();
                            throw new ServerErrorException();
                        },
                        null,
                        RecordWait,
                        cts.Token));
            }

            Assert.Equal(1, calls);
        }

        [Fact]
        public void PermanentFailures_AreNotWorthRetrying()
        {
            // A bad or under-scoped key does not become valid in half a
            // second, and at character select every call fails that way.
            Assert.False(SnapshotCallRetry.IsWorthRetrying(new InvalidAccessTokenException(), CancellationToken.None));
            Assert.False(SnapshotCallRetry.IsWorthRetrying(new AuthorizationRequiredException(), CancellationToken.None));
            Assert.False(SnapshotCallRetry.IsWorthRetrying(new MissingScopesException(), CancellationToken.None));

            // A deleted character answers 404 for as long as the id list
            // that named it is the one this fetch already read.
            Assert.False(SnapshotCallRetry.IsWorthRetrying(new NotFoundException(), CancellationToken.None));
            Assert.False(SnapshotCallRetry.IsWorthRetrying(new BadRequestException(), CancellationToken.None));
        }

        [Fact]
        public void TransientAndUnrecognisedFailures_AreWorthRetrying()
        {
            Assert.True(SnapshotCallRetry.IsWorthRetrying(new ServiceUnavailableException(), CancellationToken.None));
            Assert.True(SnapshotCallRetry.IsWorthRetrying(new ServerErrorException(), CancellationToken.None));
            Assert.True(SnapshotCallRetry.IsWorthRetrying(new TooManyRequestsException(), CancellationToken.None));
            Assert.True(SnapshotCallRetry.IsWorthRetrying(new RequestCanceledException(), CancellationToken.None));

            // An unknown failure is more often transient than permanent,
            // and the cost of being wrong is two extra calls.
            Assert.True(SnapshotCallRetry.IsWorthRetrying(new InvalidOperationException(), CancellationToken.None));
        }

        [Fact]
        public void NoFailure_IsNotWorthRetrying()
        {
            Assert.False(SnapshotCallRetry.IsWorthRetrying(null, CancellationToken.None));
        }

        [Fact]
        public async Task AnswerTheCallerCannotUse_IsRetriedLikeAFailure()
        {
            // The crafting fetch reads an empty payload this way: a
            // response with no crafting list is not the same answer as a
            // character with no disciplines.
            int calls = 0;

            string result = await SnapshotCallRetry.RunAsync<string>(
                ct =>
                {
                    calls++;
                    return Task.FromResult(calls < 3 ? null : "Armorsmith");
                },
                value => value != null,
                RecordWait,
                CancellationToken.None);

            Assert.Equal("Armorsmith", result);
            Assert.Equal(3, calls);
        }

        [Fact]
        public async Task AnswerTheCallerCannotUse_IsHandedBackOnTheLastAttempt()
        {
            // The caller decides what an unusable final answer means. The
            // crafting fetch turns it into a degraded flag, which discards
            // every character's disciplines.
            int calls = 0;

            string result = await SnapshotCallRetry.RunAsync<string>(
                ct =>
                {
                    calls++;
                    return Task.FromResult<string>(null);
                },
                value => value != null,
                RecordWait,
                CancellationToken.None);

            Assert.Null(result);
            Assert.Equal(SnapshotCallRetry.MaxAttempts, calls);
        }

        [Fact]
        public async Task NullCall_ThrowsRatherThanRetryingNothing()
        {
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(
                () => SnapshotCallRetry.RunAsync<int>(null, null, RecordWait, CancellationToken.None));

            Assert.Equal("attempt", ex.ParamName);
        }

        // ---- Several characters failing at once. The GW2 API allows 300
        // requests a minute and Blish HUD's TokenBucket(300, 5) governs
        // every call this module makes, so the question is not whether one
        // retry fits but whether a bad minute multiplies without bound.
        [Fact]
        public async Task EveryCharacterFailing_CostsTheCeilingAndNoMore()
        {
            var names = new[] { "Ayn", "Bex", "Cyd", "Dov", "Eir", "Fen", "Gia", "Hux", "Ivo" };
            int calls = 0;

            var harvest = await CharacterSnapshotCollector.CollectAsync(
                names,
                // Any bound reaches the same total; the roster is what
                // multiplies, not how much of it runs at once.
                6,
                name => SnapshotCallRetry.RunAsync<CharacterSnapshotPart>(
                    ct =>
                    {
                        Interlocked.Increment(ref calls);
                        throw new ServerErrorException();
                    },
                    null,
                    (delay, ct) => Task.CompletedTask,
                    CancellationToken.None),
                CancellationToken.None);

            Assert.Equal(27, Volatile.Read(ref calls));
            Assert.Equal(names.Length * SnapshotCallRetry.MaxAttempts, Volatile.Read(ref calls));

            // The whole roster is named, so the fetch refuses to commit.
            Assert.Equal(names.Length, harvest.IncompleteCharacterCount);
            Assert.False(harvest.IsComplete);
        }

        private class ServerErrorException : Exception
        {
            public ServerErrorException()
                : this(0)
            {
            }

            public ServerErrorException(int attempt)
            {
                Attempt = attempt;
            }

            public int Attempt { get; }
        }

        private class ServiceUnavailableException : Exception
        {
        }

        private class TooManyRequestsException : Exception
        {
        }

        private class RequestCanceledException : Exception
        {
        }

        private class InvalidAccessTokenException : Exception
        {
        }

        private class AuthorizationRequiredException : Exception
        {
        }

        private class MissingScopesException : Exception
        {
        }

        private class NotFoundException : Exception
        {
        }

        private class BadRequestException : Exception
        {
        }
    }
}
