using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // Without the build id the recipe overlay is neither read nor written for
    // the whole session, so a single slow or blocked /v2/build response would
    // silently cost every plan its persistent recipe cache. These tests
    // exercise the real HTTP path (the StubHandler pattern established by
    // Gw2ApiClient404Tests), with the retry delay injected so they do not
    // spend the production backoff.
    public class Gw2BuildApiClientTests
    {
        private class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses;

            public ScriptedHandler(params Func<HttpResponseMessage>[] responses)
            {
                _responses = new Queue<Func<HttpResponseMessage>>(responses);
            }

            public int Calls { get; private set; }

            public Uri LastRequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                LastRequestUri = request.RequestUri;
                return Task.FromResult(_responses.Dequeue()());
            }
        }

        // A /v2/build that never answers - the case the per-attempt timeout
        // exists for, and the only one a scripted response cannot express.
        private class HangingHandler : HttpMessageHandler
        {
            public int Calls { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                var abandoned = new TaskCompletionSource<bool>();
                using (cancellationToken.Register(() => abandoned.TrySetResult(true)))
                {
                    await abandoned.Task;
                }

                throw new OperationCanceledException(cancellationToken);
            }
        }

        private static Func<HttpResponseMessage> Ok(int buildId)
        {
            return () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($@"{{""id"":{buildId}}}"),
            };
        }

        private static Func<HttpResponseMessage> Status(HttpStatusCode code)
        {
            return () => new HttpResponseMessage(code)
            {
                Content = new StringContent(""),
            };
        }

        private static Func<HttpResponseMessage> Throws()
        {
            return () => throw new HttpRequestException("connection refused");
        }

        // TryAddWithoutValidation, so a deliberately malformed value reaches
        // the client instead of being rejected here.
        private static Func<HttpResponseMessage> Refuses(string retryAfter)
        {
            return () =>
            {
                var response = new HttpResponseMessage(
                    HttpStatusCode.ServiceUnavailable)
                {
                    Content = new StringContent(string.Empty),
                };

                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
                return response;
            };
        }

        private static Gw2BuildApiClient NoDelay(
            HttpClient http, TimeSpan? attemptTimeout = null)
        {
            return new Gw2BuildApiClient(
                http, (d, ct) => Task.CompletedTask, attemptTimeout);
        }

        [Fact]
        public async Task TryGetBuildId_FirstAttemptSucceeds_MakesOneRequest()
        {
            using (var handler = new ScriptedHandler(Ok(205780)))
            using (var http = new HttpClient(handler))
            {
                var result = await NoDelay(http).TryGetBuildIdAsync(CancellationToken.None);

                Assert.Equal(205780, result.BuildId);
                Assert.Equal(1, result.Attempts);
                Assert.Null(result.LastError);
                Assert.Equal(1, handler.Calls);
                Assert.Equal(
                    "https://api.guildwars2.com/v2/build",
                    handler.LastRequestUri.ToString());
            }
        }

        [Fact]
        public async Task TryGetBuildId_TransientFailures_AreRetriedAndRecover()
        {
            using (var handler = new ScriptedHandler(
                Throws(), Status(HttpStatusCode.InternalServerError), Ok(205780)))
            using (var http = new HttpClient(handler))
            {
                var result = await NoDelay(http).TryGetBuildIdAsync(CancellationToken.None);

                Assert.Equal(205780, result.BuildId);
                Assert.Equal(3, result.Attempts);
                Assert.Equal(3, handler.Calls);
            }
        }

        [Fact]
        public async Task TryGetBuildId_AllAttemptsFail_ReportsFailureInsteadOfThrowing()
        {
            using (var handler = new ScriptedHandler(Throws(), Throws(), Throws()))
            using (var http = new HttpClient(handler))
            {
                var result = await NoDelay(http).TryGetBuildIdAsync(CancellationToken.None);

                Assert.Null(result.BuildId);
                Assert.Equal(3, result.Attempts);
                Assert.IsType<HttpRequestException>(result.LastError);

                // Bounded: the caller is told to degrade, not left waiting.
                Assert.Equal(3, handler.Calls);
            }
        }

        // Bounded by the framework rather than by a wall clock inside the
        // test: the per-attempt timeout is the ONLY thing that ever completes
        // this call when the caller passes None, so if that timeout regresses
        // this test must go RED rather than hang the suite to the job timeout
        // with no test named as the culprit. The 30s is a hang catcher, not a
        // latency claim - the real run is three 50ms attempts - because a
        // bound raced inside the test is a race a starved CI thread pool can
        // lose while the code under test is correct. MEASURED on xUnit 2.6.6:
        // Timeout is enforced for async tests only, and it abandons the
        // overrunning test rather than waiting it out.
        [Fact(Timeout = 30000)]
        public async Task TryGetBuildId_HungResponse_IsAbandonedAndRetried()
        {
            using (var handler = new HangingHandler())
            using (var http = new HttpClient(handler))
            {
                var result = await NoDelay(http, TimeSpan.FromMilliseconds(50))
                    .TryGetBuildIdAsync(CancellationToken.None);

                // A response that never arrives must be given up on per attempt
                // and retried, not waited out: the caller is told to degrade.
                Assert.Null(result.BuildId);
                Assert.Equal(3, result.Attempts);
                Assert.Equal(3, handler.Calls);
                Assert.IsAssignableFrom<OperationCanceledException>(result.LastError);
            }
        }

        // Retry-After is a delta in seconds or an HTTP-date (RFC 9110 section
        // 10.2.3). Reading only the delta sends the next attempt the moment
        // the API named a time to come back at, which is the one thing the
        // header exists to stop.
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        private static async Task<List<TimeSpan>> WaitsAfterRefusal(string retryAfter)
        {
            using (var handler = new ScriptedHandler(Refuses(retryAfter), Ok(205780)))
            using (var http = new HttpClient(handler))
            {
                var waits = new List<TimeSpan>();
                var client = new Gw2BuildApiClient(
                    http,
                    (d, ct) =>
                    {
                        waits.Add(d);
                        return Task.CompletedTask;
                    },
                    null,
                    () => Now);

                var result = await client.TryGetBuildIdAsync(CancellationToken.None);

                Assert.Equal(205780, result.BuildId);
                return waits;
            }
        }

        [Fact]
        public async Task TryGetBuildId_RetryAfterDelta_WaitsWhatTheApiAsked()
        {
            var waits = await WaitsAfterRefusal("7");

            Assert.Equal(TimeSpan.FromSeconds(7), Assert.Single(waits));
        }

        [Fact]
        public async Task TryGetBuildId_RetryAfterHttpDate_WaitsUntilTheMomentNamed()
        {
            var waits = await WaitsAfterRefusal(Now.AddSeconds(9).ToString("R"));

            Assert.Equal(TimeSpan.FromSeconds(9), Assert.Single(waits));
        }

        [Fact]
        public async Task TryGetBuildId_RetryAfterDateAlreadyPast_UsesTheFixedDelay()
        {
            var waits = await WaitsAfterRefusal(Now.AddMinutes(-1).ToString("R"));

            Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(waits));
        }

        [Fact]
        public async Task TryGetBuildId_MalformedRetryAfter_UsesTheFixedDelay()
        {
            var waits = await WaitsAfterRefusal("shortly");

            Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(waits));
        }

        [Fact]
        public async Task TryGetBuildId_RetryAfterBeyondTheBound_StopsRatherThanReturningEarly()
        {
            using (var handler = new ScriptedHandler(Refuses("300")))
            using (var http = new HttpClient(handler))
            {
                var waits = new List<TimeSpan>();
                var client = new Gw2BuildApiClient(
                    http,
                    (d, ct) =>
                    {
                        waits.Add(d);
                        return Task.CompletedTask;
                    },
                    null,
                    () => Now);

                var result = await client.TryGetBuildIdAsync(CancellationToken.None);

                Assert.Null(result.BuildId);
                Assert.Equal(1, result.Attempts);
                Assert.Equal(1, handler.Calls);
                Assert.Empty(waits);
            }
        }

        [Fact]
        public async Task TryGetBuildId_CallerCancellation_StopsRetrying()
        {
            using (var handler = new ScriptedHandler(Throws(), Throws(), Throws()))
            using (var http = new HttpClient(handler))
            using (var cts = new CancellationTokenSource())
            {
                // Cancelled during the backoff between attempt 1 and 2 -
                // module unload must not be waited out.
                var client = new Gw2BuildApiClient(http, (d, ct) =>
                {
                    cts.Cancel();
                    ct.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                });

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => client.TryGetBuildIdAsync(cts.Token));

                Assert.Equal(1, handler.Calls);
            }
        }
    }
}
