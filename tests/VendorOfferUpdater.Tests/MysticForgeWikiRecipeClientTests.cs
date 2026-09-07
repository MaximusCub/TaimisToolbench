using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace MysticForgeSeeder.Tests
{
    // The wiki states a refusal in the response body, not in the status
    // code, so every case below is an HTTP 200 the scrape must not read as
    // the last page. The delay and the clock are injected, so the suite
    // asserts what the client would have waited without waiting it.
    public class MysticForgeWikiRecipeClientTests
    {
        private static readonly DateTimeOffset Now =
            new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);

        private const string OneRecipePage =
            "{\"query\":{\"results\":{\"Mystic Clover#recipe\":{\"printouts\":{" +
            "\"Has canonical name\":[\"Mystic Clover\"]," +
            "\"Has output quantity\":[1]," +
            "\"Has ingredient\":[]}}}}}";

        private const string EmptyPage = "{\"query\":{\"results\":[]}}";

        private const string LagRefusal =
            "{\"error\":{\"code\":\"maxlag\",\"info\":\"Waiting for db1: 6 seconds lagged.\"}}";

        private class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses;

            public ScriptedHandler(params Func<HttpResponseMessage>[] responses)
            {
                _responses = new Queue<Func<HttpResponseMessage>>(responses);
            }

            public List<string> RequestBodies { get; } = new List<string>();

            protected override async Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestBodies.Add(request.Content == null
                    ? string.Empty
                    : await request.Content.ReadAsStringAsync(cancellationToken));

                return _responses.Dequeue()();
            }
        }

        private static Func<HttpResponseMessage> Answer(
            string body,
            HttpStatusCode status = HttpStatusCode.OK,
            string retryAfter = null)
        {
            return () =>
            {
                var response = new HttpResponseMessage(status)
                {
                    Content = new StringContent(body),
                };

                if (retryAfter != null)
                {
                    response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
                }

                return response;
            };
        }

        private static (WikiRecipeClient Client, List<TimeSpan> Waits) Build(
            HttpClient http)
        {
            var waits = new List<TimeSpan>();
            var client = new WikiRecipeClient(
                http,
                delayMs: 0,
                maxRequests: 200,
                delay: (d, ct) =>
                {
                    waits.Add(d);
                    return Task.CompletedTask;
                },
                now: () => Now);

            return (client, waits);
        }

        [Fact]
        public async Task EveryQueryCarriesMaxlag()
        {
            using var handler = new ScriptedHandler(Answer(EmptyPage));
            using var http = new HttpClient(handler);
            var (client, _) = Build(http);

            await client.QueryMysticForgeRecipesAsync();

            Assert.Contains("maxlag=5", Assert.Single(handler.RequestBodies));
        }

        [Fact]
        public async Task EmptyResultPage_EndsTheScrapeWithoutError()
        {
            using var handler = new ScriptedHandler(Answer(EmptyPage));
            using var http = new HttpClient(handler);
            var (client, _) = Build(http);

            var recipes = await client.QueryMysticForgeRecipesAsync();

            Assert.Empty(recipes);
            Assert.Single(handler.RequestBodies);
        }

        [Fact]
        public async Task LagRefusal_IsWaitedOutAndTheScrapeContinues()
        {
            using var handler = new ScriptedHandler(
                Answer(LagRefusal, retryAfter: "5"), Answer(OneRecipePage));
            using var http = new HttpClient(handler);
            var (client, waits) = Build(http);

            var recipes = await client.QueryMysticForgeRecipesAsync();

            Assert.Equal("Mystic Clover", Assert.Single(recipes).OutputName);
            Assert.Equal(2, handler.RequestBodies.Count);
            Assert.InRange(Assert.Single(waits).TotalMilliseconds, 4500, 5500);
        }

        [Fact]
        public async Task LagRefusalOnEveryAttempt_FailsInsteadOfReturningAShortScrape()
        {
            using var handler = new ScriptedHandler(
                Answer(LagRefusal), Answer(LagRefusal),
                Answer(LagRefusal), Answer(LagRefusal));
            using var http = new HttpClient(handler);
            var (client, _) = Build(http);

            var error = await Assert.ThrowsAsync<WikiApiException>(
                () => client.QueryMysticForgeRecipesAsync());

            Assert.Contains("maxlag", error.Message);
            Assert.Equal(4, handler.RequestBodies.Count);
        }

        [Fact]
        public async Task ErrorTheWikiWillNotChangeItsMindAbout_FailsWithoutRetrying()
        {
            using var handler = new ScriptedHandler(
                Answer("{\"error\":{\"code\":\"invalidquery\",\"info\":\"bad query\"}}"));
            using var http = new HttpClient(handler);
            var (client, _) = Build(http);

            var error = await Assert.ThrowsAsync<WikiApiException>(
                () => client.QueryMysticForgeRecipesAsync());

            Assert.Contains("invalidquery", error.Message);
            Assert.Single(handler.RequestBodies);
        }

        [Fact]
        public async Task BodyWithNeitherResultsNorError_FailsRatherThanEndingTheScrape()
        {
            using var handler = new ScriptedHandler(Answer("{\"servedby\":\"mw1\"}"));
            using var http = new HttpClient(handler);
            var (client, _) = Build(http);

            await Assert.ThrowsAsync<WikiApiException>(
                () => client.QueryMysticForgeRecipesAsync());
        }

        [Fact]
        public async Task RetryAfterDelta_IsHonouredOverTheExponentialFallback()
        {
            var waits = await WaitsAfterRefusal("3");

            Assert.InRange(Assert.Single(waits).TotalMilliseconds, 2700, 3300);
        }

        [Fact]
        public async Task RetryAfterHttpDate_IsHonouredRatherThanTreatedAsAbsent()
        {
            var waits = await WaitsAfterRefusal(
                Now.AddSeconds(45).ToString("R"));

            Assert.InRange(Assert.Single(waits).TotalMilliseconds, 40_500, 49_500);
        }

        [Fact]
        public async Task RetryAfterDateAlreadyPast_FallsBackToTheExponentialWait()
        {
            var waits = await WaitsAfterRefusal(
                Now.AddSeconds(-60).ToString("R"));

            Assert.InRange(Assert.Single(waits).TotalMilliseconds, 900, 1100);
        }

        [Fact]
        public async Task MalformedRetryAfter_FallsBackToTheExponentialWait()
        {
            var waits = await WaitsAfterRefusal("shortly");

            Assert.InRange(Assert.Single(waits).TotalMilliseconds, 900, 1100);
        }

        // One 429 followed by a page: the fallback on the first attempt is one
        // second, so anything else the client waits came out of the header.
        private static async Task<List<TimeSpan>> WaitsAfterRefusal(string retryAfter)
        {
            using var handler = new ScriptedHandler(
                Answer(string.Empty, HttpStatusCode.TooManyRequests, retryAfter),
                Answer(EmptyPage));
            using var http = new HttpClient(handler);
            var (client, waits) = Build(http);

            await client.QueryMysticForgeRecipesAsync();

            Assert.Equal(2, handler.RequestBodies.Count);
            return waits;
        }
    }
}
