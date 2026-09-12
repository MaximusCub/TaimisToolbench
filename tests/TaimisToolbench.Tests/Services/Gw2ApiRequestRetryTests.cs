using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // api.guildwars2.com reports X-Rate-Limit-Limit: 600 a minute. Every one
    // of these clients turns a non-success status into an exception, and on
    // the recipe path a thrown fetch degrades that recipe to UNKNOWN in the
    // plan, so a rate-limit trip used to change plan content rather than plan
    // timing. These run the real client methods over a scripted handler, with
    // the wait injected so nothing sleeps.
    public class Gw2ApiRequestRetryTests
    {
        private class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses;

            public ScriptedHandler(params Func<HttpResponseMessage>[] responses)
            {
                _responses = new Queue<Func<HttpResponseMessage>>(responses);
            }

            public int Calls { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(_responses.Dequeue()());
            }
        }

        private class RecordedDelay
        {
            public List<TimeSpan> Waits { get; } = new List<TimeSpan>();

            public Task Wait(TimeSpan delay, CancellationToken ct)
            {
                Waits.Add(delay);
                return Task.CompletedTask;
            }
        }

        private static Func<HttpResponseMessage> Refusal(
            int statusCode, RetryConditionHeaderValue retryAfter = null)
        {
            return () =>
            {
                var response = new HttpResponseMessage((HttpStatusCode)statusCode)
                {
                    Content = new StringContent(string.Empty),
                };
                if (retryAfter != null)
                {
                    response.Headers.RetryAfter = retryAfter;
                }

                return response;
            };
        }

        private static Func<HttpResponseMessage> Ok(string body)
        {
            return () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            };
        }

        [Fact]
        public async Task Recipe429_WaitsTheHeadersDeltaAndReturnsTheSecondAnswer()
        {
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(
                Refusal(429, new RetryConditionHeaderValue(TimeSpan.FromSeconds(2))),
                Ok("[10, 20]")))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2RecipeApiClient(http, delay.Wait);
                var result = await client.SearchByOutputAsync(1, CancellationToken.None);

                Assert.Equal(new[] { 10, 20 }, result.RecipeIds);
                Assert.Equal(2, handler.Calls);
                Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, delay.Waits);
            }
        }

        [Fact]
        public async Task Recipe429_WithNoHeader_WaitsTheFallback()
        {
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(Refusal(429), Ok("[10]")))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2RecipeApiClient(http, delay.Wait);
                await client.SearchByOutputAsync(1, CancellationToken.None);

                Assert.Equal(new[] { Gw2ApiRequest.FallbackDelay }, delay.Waits);
            }
        }

        [Fact]
        public async Task Recipe429_AskingForLongerThanTheCap_IsNotRetried()
        {
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(
                Refusal(429, new RetryConditionHeaderValue(TimeSpan.FromMinutes(10)))))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2RecipeApiClient(http, delay.Wait);

                await Assert.ThrowsAsync<HttpRequestException>(
                    () => client.SearchByOutputAsync(1, CancellationToken.None));

                Assert.Equal(1, handler.Calls);
                Assert.Empty(delay.Waits);
            }
        }

        [Fact]
        public async Task Recipe429_WithADatedHeader_ReadsTheDateRatherThanFallingBack()
        {
            // HttpRetry.ResolveDelay's date branch: a dated header read as
            // absent would give exactly the fallback instead.
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(
                Refusal(429, new RetryConditionHeaderValue(DateTimeOffset.UtcNow.AddSeconds(8))),
                Ok("[10]")))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2RecipeApiClient(http, delay.Wait);
                await client.SearchByOutputAsync(1, CancellationToken.None);

                Assert.Equal(2, handler.Calls);
                var waited = Assert.Single(delay.Waits);
                Assert.True(waited > Gw2ApiRequest.FallbackDelay, $"waited {waited}");
                Assert.True(waited <= TimeSpan.FromSeconds(8), $"waited {waited}");
            }
        }

        [Fact]
        public async Task Recipe503_IsRetried()
        {
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(Refusal(503), Ok("[7]")))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2RecipeApiClient(http, delay.Wait);
                var result = await client.SearchByOutputAsync(1, CancellationToken.None);

                Assert.Equal(new[] { 7 }, result.RecipeIds);
                Assert.Equal(2, handler.Calls);
            }
        }

        [Fact]
        public async Task Recipe404_IsAnAnswer_NotARefusal()
        {
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(Refusal(404)))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2RecipeApiClient(http, delay.Wait);
                var result = await client.SearchByOutputAsync(1, CancellationToken.None);

                Assert.Empty(result.RecipeIds);
                Assert.Equal(1, handler.Calls);
                Assert.Empty(delay.Waits);
            }
        }

        [Fact]
        public async Task Recipe500_IsNotRetried()
        {
            // A repeated 500 is still a 500, and the API names no wait.
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(Refusal(500)))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2RecipeApiClient(http, delay.Wait);

                await Assert.ThrowsAsync<HttpRequestException>(
                    () => client.SearchByOutputAsync(1, CancellationToken.None));

                Assert.Equal(1, handler.Calls);
                Assert.Empty(delay.Waits);
            }
        }

        [Fact]
        public async Task Price429_IsRetried()
        {
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(
                Refusal(429),
                Ok("[{\"id\":19700,\"buys\":{\"unit_price\":11},\"sells\":{\"unit_price\":13}}]")))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2PriceApiClient(http, delay.Wait);
                var result = await client.GetPricesAsync(new[] { 19700 }, CancellationToken.None);

                var entry = Assert.Single(result.Entries);
                Assert.Equal(11, entry.BuyUnitPrice);
                Assert.Equal(2, handler.Calls);
            }
        }

        [Fact]
        public async Task Item429_IsRetried()
        {
            var delay = new RecordedDelay();
            using (var handler = new ScriptedHandler(
                Refusal(429),
                Ok("[{\"id\":19700,\"name\":\"Mithril Ore\"}]")))
            using (var http = new HttpClient(handler))
            {
                var client = new Gw2ItemApiClient(http, delay.Wait);
                var result = await client.GetItemsAsync(new[] { 19700 }, CancellationToken.None);

                var entry = Assert.Single(result);
                Assert.Equal("Mithril Ore", entry.Name);
                Assert.Equal(2, handler.Calls);
            }
        }
    }
}
