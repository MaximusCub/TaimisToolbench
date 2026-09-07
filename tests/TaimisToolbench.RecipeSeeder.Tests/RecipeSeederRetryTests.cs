using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.RecipeSeeder;
using Xunit;

namespace TaimisToolbench.RecipeSeeder.Tests
{
    // A batch that gave up used to return an empty list, so a rate-limited
    // run wrote a short seed and exited 0. These pin the two halves of the
    // fix: which refusals are worth retrying, and that an unrecoverable one
    // reaches the caller.
    public class RecipeSeederRetryTests
    {
        private class ScriptedHandler : HttpMessageHandler
        {
            private readonly Queue<Func<HttpResponseMessage>> _responses;

            public ScriptedHandler(params Func<HttpResponseMessage>[] responses)
            {
                _responses = new Queue<Func<HttpResponseMessage>>(responses);
            }

            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;
                var next = _responses.Count > 1
                    ? _responses.Dequeue()
                    : _responses.Peek();
                return Task.FromResult(next());
            }
        }

        private static Func<HttpResponseMessage> Status(HttpStatusCode code)
        {
            return () => new HttpResponseMessage(code)
            {
                Content = new StringContent("{}"),
            };
        }

        private static Func<HttpResponseMessage> Ok(string body)
        {
            return () => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            };
        }

        private const string OneRecipe = @"[{
            ""id"": 10,
            ""output_item_id"": 1,
            ""output_item_count"": 1,
            ""disciplines"": [""Weaponsmith""],
            ""min_rating"": 0,
            ""flags"": [],
            ""ingredients"": [{ ""id"": 2, ""count"": 3 }]
        }]";

        [Theory]
        [InlineData(429, true)]
        [InlineData(500, true)]
        [InlineData(503, true)]
        [InlineData(400, false)]
        [InlineData(404, false)]
        [InlineData(401, false)]
        public void IsRetryable_SeparatesComeBackLaterFromOurOwnFault(
            int code, bool expected)
        {
            Assert.Equal(expected, HttpRetry.IsRetryable((HttpStatusCode)code));
        }

        [Fact]
        public void ResolveDelay_ReadsASecondsDelta()
        {
            using (var response = new HttpResponseMessage((HttpStatusCode)429))
            {
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));

                Assert.Equal(
                    TimeSpan.FromSeconds(7),
                    HttpRetry.ResolveDelay(
                        response, TimeSpan.FromSeconds(1), DateTimeOffset.UtcNow));
            }
        }

        // Retry-After is a delta OR an HTTP-date. Reading only Delta treats
        // every dated header as absent and retries far too early.
        [Fact]
        public void ResolveDelay_ReadsAnHttpDate()
        {
            var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
            using (var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            {
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(now.AddSeconds(30));

                Assert.Equal(
                    TimeSpan.FromSeconds(30),
                    HttpRetry.ResolveDelay(response, TimeSpan.FromSeconds(1), now));
            }
        }

        [Fact]
        public void ResolveDelay_ClampsAWaitTooLongToBeARetry()
        {
            var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
            using (var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            {
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(now.AddDays(1));

                Assert.Equal(
                    HttpRetry.MaxDelay,
                    HttpRetry.ResolveDelay(response, TimeSpan.FromSeconds(1), now));
            }
        }

        [Fact]
        public void ResolveDelay_NeverUndercutsTheCallersOwnBackoff()
        {
            var now = new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero);
            using (var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            {
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(now.AddSeconds(-60));

                Assert.Equal(
                    TimeSpan.FromSeconds(4),
                    HttpRetry.ResolveDelay(response, TimeSpan.FromSeconds(4), now));
            }
        }

        [Fact]
        public void ResolveDelay_FallsBackWhenTheServerAsksForNothing()
        {
            using (var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))
            {
                Assert.Equal(
                    TimeSpan.FromSeconds(2),
                    HttpRetry.ResolveDelay(
                        response, TimeSpan.FromSeconds(2), DateTimeOffset.UtcNow));
            }
        }

        [Fact]
        public async Task FetchRecipeBatchAsync_RetriesARefusalAndKeepsTheBatch()
        {
            using (var handler = new ScriptedHandler(
                Status(HttpStatusCode.ServiceUnavailable), Ok(OneRecipe)))
            using (var http = new HttpClient(handler))
            {
                var recipes = await Program.FetchRecipeBatchAsync(
                    http, new List<int> { 10 });

                Assert.Single(recipes);
                Assert.Equal(2, handler.RequestCount);
            }
        }

        [Fact]
        public async Task FetchRecipeBatchAsync_ThrowsRatherThanReturningAShortBatch()
        {
            using (var handler = new ScriptedHandler(Status((HttpStatusCode)429)))
            using (var http = new HttpClient(handler))
            {
                await Assert.ThrowsAsync<HttpRequestException>(
                    () => Program.FetchRecipeBatchAsync(http, new List<int> { 10 }));

                Assert.Equal(3, handler.RequestCount);
            }
        }

        [Fact]
        public async Task FetchAllRecipeIdsAsync_DoesNotRepeatARequestTheApiRejected()
        {
            using (var handler = new ScriptedHandler(Status(HttpStatusCode.BadRequest)))
            using (var http = new HttpClient(handler))
            {
                await Assert.ThrowsAsync<HttpRequestException>(
                    () => Program.FetchAllRecipeIdsAsync(http));

                Assert.Equal(1, handler.RequestCount);
            }
        }
    }
}
