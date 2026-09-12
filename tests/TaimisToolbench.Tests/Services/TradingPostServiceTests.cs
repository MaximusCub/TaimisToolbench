using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class TradingPostServiceTests
    {
        // A gap that is inside the TTL whatever the TTL is set to, so a
        // change to that constant moves every "still cached" case with it.
        private static TimeSpan HalfOf(TimeSpan ttl)
        {
            return TimeSpan.FromTicks(ttl.Ticks / 2);
        }

        // The TTL is a spam guard on the Generate Plan button, not an
        // incidental cache size - see TradingPostService.CacheTtl. Pinned
        // so lengthening it back out is a deliberate edit here as well.
        [Fact]
        public void CacheTtl_IsTheTwoMinuteSpamGuard()
        {
            Assert.Equal(TimeSpan.FromSeconds(120), TradingPostService.CacheTtl);
        }

        [Fact]
        public async Task SingleItem_ReturnsBuyInstantAndSellInstant()
        {
            var api = new InMemoryPriceApiClient();
            // buys.unit_price=350 (sell-instant), sells.unit_price=400 (buy-instant)
            api.AddPrice(19684, buyUnitPrice: 350, sellUnitPrice: 400);
            var svc = new TradingPostService(api);

            var result = await svc.GetPricesAsync(new[] { 19684 }, CancellationToken.None);

            Assert.True(result.ContainsKey(19684));
            var price = result[19684];
            Assert.Equal(19684, price.ItemId);
            Assert.Equal(400, price.BuyInstant);   // sells.unit_price
            Assert.Equal(350, price.SellInstant);   // buys.unit_price
        }

        [Fact]
        public async Task ItemAbsentFromApi_NotInDictionary()
        {
            var api = new InMemoryPriceApiClient();
            // Item 99999 not added - simulates account-bound / non-tradeable item
            var svc = new TradingPostService(api);

            var result = await svc.GetPricesAsync(new[] { 99999 }, CancellationToken.None);

            Assert.False(result.ContainsKey(99999));
        }

        [Fact]
        public async Task ItemPresentWithZeroPrices_IncludedInDictionary()
        {
            var api = new InMemoryPriceApiClient();
            // Tradeable item but no current orders
            api.AddPrice(50000, buyUnitPrice: 0, sellUnitPrice: 0);
            var svc = new TradingPostService(api);

            var result = await svc.GetPricesAsync(new[] { 50000 }, CancellationToken.None);

            Assert.True(result.ContainsKey(50000));
            Assert.Equal(0, result[50000].BuyInstant);
            Assert.Equal(0, result[50000].SellInstant);
        }

        [Fact]
        public async Task MultipleItems_AllReturnedCorrectly()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            api.AddPrice(2, buyUnitPrice: 300, sellUnitPrice: 400);
            api.AddPrice(3, buyUnitPrice: 500, sellUnitPrice: 600);
            var svc = new TradingPostService(api);

            var result = await svc.GetPricesAsync(new[] { 1, 2, 3 }, CancellationToken.None);

            Assert.Equal(3, result.Count);
            Assert.Equal(200, result[1].BuyInstant);
            Assert.Equal(400, result[2].BuyInstant);
            Assert.Equal(600, result[3].BuyInstant);
        }

        [Fact]
        public async Task Deduplication_DuplicateIdsDoNotCauseDuplicateApiCalls()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            var svc = new TradingPostService(api);

            var result = await svc.GetPricesAsync(new[] { 1, 1, 1 }, CancellationToken.None);

            Assert.Single(result);
            Assert.Single(api.Calls);
            Assert.Single(api.Calls[0]); // only 1 unique ID sent
        }

        [Fact]
        public async Task Batching_LargeSetSplitIntoChunks()
        {
            var api = new InMemoryPriceApiClient();
            var ids = new List<int>();
            for (int i = 1; i <= 250; i++)
            {
                api.AddPrice(i, buyUnitPrice: i, sellUnitPrice: i * 2);
                ids.Add(i);
            }

            var svc = new TradingPostService(api);

            var result = await svc.GetPricesAsync(ids, CancellationToken.None);

            Assert.Equal(250, result.Count);
            Assert.Equal(2, api.Calls.Count);
            Assert.Equal(200, api.Calls[0].Count);
            Assert.Equal(50, api.Calls[1].Count);
        }

        [Fact]
        public async Task Caching_SecondCallOnlyFetchesNewIds()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            api.AddPrice(2, buyUnitPrice: 300, sellUnitPrice: 400);
            api.AddPrice(3, buyUnitPrice: 500, sellUnitPrice: 600);
            var svc = new TradingPostService(api);

            // First call fetches 1 and 2
            await svc.GetPricesAsync(new[] { 1, 2 }, CancellationToken.None);

            // Second call with 2 (cached) and 3 (new)
            var result = await svc.GetPricesAsync(new[] { 2, 3 }, CancellationToken.None);

            Assert.Equal(2, result.Count);
            Assert.Equal(2, api.Calls.Count);
            // Second call should only contain item 3
            Assert.Single(api.Calls[1]);
            Assert.Equal(3, api.Calls[1][0]);
        }

        [Fact]
        public async Task Ttl_ExpiredEntryIsRefetchedOnNextRequest()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var svc = new TradingPostService(api, () => clock);

            await svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            clock = clock.Add(TradingPostService.CacheTtl).AddSeconds(1);
            var result = await svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            Assert.Equal(2, api.Calls.Count);
            Assert.Equal(200, result[1].BuyInstant);
        }

        [Fact]
        public async Task Ttl_FreshEntryWithinTtlIsServedFromCache()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var svc = new TradingPostService(api, () => clock);

            await svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            clock = clock.Add(HalfOf(TradingPostService.CacheTtl));
            var result = await svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            Assert.Single(api.Calls); // no second API call
            Assert.Equal(200, result[1].BuyInstant);
        }

        // /v2/commerce/prices omits untradeable items from its response
        // entirely. An account-bound id (a gift, a clover, the legendary
        // target itself) therefore never reaches the positive cache, and
        // used to be re-requested on every single call however recently
        // it was asked for; it is now negative-cached on the same clock.
        [Fact]
        public async Task UntradeableId_NegativeCachedWithinTtl_NotRefetched()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            // 99999 is never added: the fake omits it from the response,
            // exactly as the real endpoint does for an account-bound item.
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var svc = new TradingPostService(api, () => clock);

            await svc.GetPricesAsync(new[] { 1, 99999 }, CancellationToken.None);

            clock = clock.Add(HalfOf(TradingPostService.CacheTtl));
            var result = await svc.GetPricesAsync(new[] { 1, 99999 }, CancellationToken.None);

            Assert.Single(api.Calls); // no second round trip for either id
            Assert.False(result.ContainsKey(99999)); // still absent, still an unpriceable hole
            Assert.Equal(200, result[1].BuyInstant);
        }

        [Fact]
        public async Task UntradeableId_RefetchedOnceTheNegativeEntryExpires()
        {
            var api = new InMemoryPriceApiClient();
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var svc = new TradingPostService(api, () => clock);

            await svc.GetPricesAsync(new[] { 99999 }, CancellationToken.None);

            clock = clock.Add(TradingPostService.CacheTtl).AddSeconds(1);
            await svc.GetPricesAsync(new[] { 99999 }, CancellationToken.None);

            Assert.Equal(2, api.Calls.Count);
        }

        [Fact]
        public async Task ItemThatBecomesTradeable_IsPricedAfterTheNegativeEntryExpires()
        {
            var api = new InMemoryPriceApiClient();
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var svc = new TradingPostService(api, () => clock);

            var beforePatch = await svc.GetPricesAsync(new[] { 99999 }, CancellationToken.None);
            Assert.False(beforePatch.ContainsKey(99999));

            api.AddPrice(99999, buyUnitPrice: 100, sellUnitPrice: 200); // a patch makes it tradeable
            clock = clock.Add(TradingPostService.CacheTtl).AddSeconds(1);
            var afterPatch = await svc.GetPricesAsync(new[] { 99999 }, CancellationToken.None);

            Assert.Equal(200, afterPatch[99999].BuyInstant);

            // The negative entry was dropped, not just outvoted: the now-
            // cached price serves the next call with no further request.
            clock = clock.Add(HalfOf(TradingPostService.CacheTtl));
            var third = await svc.GetPricesAsync(new[] { 99999 }, CancellationToken.None);
            Assert.Equal(2, api.Calls.Count);
            Assert.Equal(200, third[99999].BuyInstant);
        }

        [Fact]
        public async Task FailedBatch_DoesNotNegativeCacheItsIds()
        {
            // A batch that threw proves nothing about whether its ids are
            // tradeable - only a response that came back and omitted them
            // does. Negative-caching a transient failure would blank those
            // prices for a whole TTL.
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            api.ThrowOnCallNumber = 1;
            var svc = new TradingPostService(api);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => svc.GetPricesAsync(new[] { 1 }, CancellationToken.None));

            var result = await svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            Assert.Equal(2, api.Calls.Count);
            Assert.Equal(200, result[1].BuyInstant);
        }

        // The other half of the same rule: /v2/commerce/prices answers 404
        // for an endpoint-level outage as readily as for "every id in this
        // batch is untradeable", and Gw2PriceApiClient turns that 404 into
        // an empty batch WITHOUT throwing. Negative-caching those ids would
        // latch a whole plan into "no price for anything" for a full TTL,
        // and - because the freshness scan then skips them - without a
        // single further request to recover on.
        [Fact]
        public async Task UnprovenEmptyBatch_DoesNotNegativeCacheItsIds()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            api.AddPrice(2, buyUnitPrice: 300, sellUnitPrice: 400);
            api.UnprovenEmptyOnCallNumber = 1;
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var svc = new TradingPostService(api, () => clock);

            // The outage itself still degrades to unpriceable holes.
            var duringOutage = await svc.GetPricesAsync(new[] { 1, 2 }, CancellationToken.None);
            Assert.Empty(duringOutage);

            clock = clock.Add(HalfOf(TradingPostService.CacheTtl));
            var afterOutage = await svc.GetPricesAsync(new[] { 1, 2 }, CancellationToken.None);

            Assert.Equal(2, api.Calls.Count); // the recovery request happened
            Assert.Equal(200, afterOutage[1].BuyInstant);
            Assert.Equal(400, afterOutage[2].BuyInstant);
        }

        [Fact]
        public async Task Ttl_MixedFreshAndStaleBatchOnlyRefetchesStaleIds()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            api.AddPrice(2, buyUnitPrice: 300, sellUnitPrice: 400);
            var clock = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var svc = new TradingPostService(api, () => clock);

            // Item 1 fetched first, then left to go stale.
            await svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);
            clock = clock.Add(TradingPostService.CacheTtl).AddSeconds(1);

            // Item 2 fetched fresh, right before the mixed request.
            await svc.GetPricesAsync(new[] { 2 }, CancellationToken.None);

            // Third call requests both: item 1 is past the TTL, item 2 was just fetched.
            var result = await svc.GetPricesAsync(new[] { 1, 2 }, CancellationToken.None);

            Assert.Equal(3, api.Calls.Count);
            Assert.Single(api.Calls[2]);
            Assert.Equal(1, api.Calls[2][0]);
            Assert.Equal(200, result[1].BuyInstant);
            Assert.Equal(400, result[2].BuyInstant);
        }

        [Fact]
        public async Task Constructor_DefaultClockCachesWithinTtlWithoutExplicitClock()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            var svc = new TradingPostService(api); // utcNow omitted -> defaults to DateTime.UtcNow

            await svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);
            var result = await svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            Assert.Single(api.Calls); // second call served from cache, well within the TTL
            Assert.Equal(200, result[1].BuyInstant);
        }

        // KNOWN-ISSUES #31/31c-1: two overlapping GetPricesAsync calls for the
        // same not-yet-cached id must coalesce into a single upstream
        // fetch instead of each starting its own. The Gate holds the fake
        // API's response until BOTH calls have been started, so joining
        // is exercised deterministically rather than relying on timing.
        [Fact]
        public async Task ConcurrentCalls_SameId_CoalesceIntoSingleUpstreamFetch()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            var gate = new TaskCompletionSource<bool>();
            api.Gate = gate.Task;
            var svc = new TradingPostService(api);

            var task1 = svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);
            var task2 = svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            gate.SetResult(true);
            var result1 = await task1;
            var result2 = await task2;

            Assert.Single(api.Calls); // only one upstream fetch, shared by both callers
            Assert.Equal(200, result1[1].BuyInstant);
            Assert.Equal(200, result2[1].BuyInstant);
        }

        // Closer mirror of the real scenario (two overlapping plan
        // generations with overlapping-but-not-identical item sets): the
        // shared ids coalesce onto the first call's fetch, while the
        // second call still fetches its own unique id itself.
        [Fact]
        public async Task ConcurrentCalls_OverlappingIds_SharedIdsCoalesce_UniqueIdFetchedSeparately()
        {
            var api = new InMemoryPriceApiClient();
            for (int i = 1; i <= 4; i++)
            {
                api.AddPrice(i, buyUnitPrice: i, sellUnitPrice: i * 2);
            }

            var gate = new TaskCompletionSource<bool>();
            api.Gate = gate.Task;
            var svc = new TradingPostService(api);

            var task1 = svc.GetPricesAsync(new[] { 1, 2, 3 }, CancellationToken.None);
            var task2 = svc.GetPricesAsync(new[] { 2, 3, 4 }, CancellationToken.None);

            gate.SetResult(true);
            var result1 = await task1;
            var result2 = await task2;

            // task1's own batch covers {1,2,3}; task2 joins that for ids
            // 2 and 3, and only fetches id 4 itself - two upstream calls
            // total, not three.
            Assert.Equal(2, api.Calls.Count);
            Assert.Equal(3, result1.Count);
            Assert.Equal(3, result2.Count);
        }

        // KNOWN-ISSUES #31/31c-audit: the owning caller's cancellation must
        // never abandon the shared fetch a DIFFERENT, still-live caller is
        // joined onto. Caller A (owner) is cancelled while the upstream
        // fetch is still gated; caller B (joiner, never cancelled) must
        // still see its own price once the gate releases, not inherit A's
        // OperationCanceledException.
        [Fact]
        public async Task ConcurrentCalls_OwnerCancelled_JoinerWithLiveTokenStillSucceeds()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            var gate = new TaskCompletionSource<bool>();
            api.Gate = gate.Task;
            var svc = new TradingPostService(api);
            var ownerCts = new CancellationTokenSource();

            var ownerTask = svc.GetPricesAsync(new[] { 1 }, ownerCts.Token);
            var joinerTask = svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            ownerCts.Cancel();
            gate.SetResult(true);

            await Assert.ThrowsAsync<OperationCanceledException>(() => ownerTask);
            var joinerResult = await joinerTask;

            Assert.Equal(200, joinerResult[1].BuyInstant);
            Assert.Single(api.Calls); // still just one shared upstream fetch
        }

        // KNOWN-ISSUES #31/31c-audit: a joining caller's own cancellation must
        // be observed - it must not silently ride along on whatever the
        // owning caller's fetch eventually does. Caller B (joiner) is
        // cancelled while the upstream fetch is still gated (unreleased);
        // B must throw promptly on its own token without waiting for the
        // owner's fetch to finish, and the owner (never cancelled) must
        // still succeed once the gate is released.
        [Fact]
        public async Task ConcurrentCalls_JoinerCancelled_ThrowsPromptlyWithoutAffectingOwner()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 200);
            var gate = new TaskCompletionSource<bool>();
            api.Gate = gate.Task;
            var svc = new TradingPostService(api);
            var joinerCts = new CancellationTokenSource();

            var ownerTask = svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);
            var joinerTask = svc.GetPricesAsync(new[] { 1 }, joinerCts.Token);

            joinerCts.Cancel();

            // B's own cancellation must surface without the gate ever
            // being released - proves it did not wait on the owner's task.
            await Assert.ThrowsAsync<OperationCanceledException>(() => joinerTask);

            gate.SetResult(true);
            var ownerResult = await ownerTask;

            Assert.Equal(200, ownerResult[1].BuyInstant);
            Assert.Single(api.Calls); // still just one shared upstream fetch
        }

        // KNOWN-ISSUES #31/api-degradation F2: one bad batch amid otherwise-
        // healthy ones must degrade to missing ids (unpriceable holes
        // downstream) instead of aborting the whole call.
        [Fact]
        public async Task OneBatchFails_DegradesToHolesInsteadOfAbortingWholeCall()
        {
            var api = new InMemoryPriceApiClient();
            var ids = new List<int>();
            for (int i = 1; i <= 250; i++)
            {
                api.AddPrice(i, buyUnitPrice: i, sellUnitPrice: i * 2);
                ids.Add(i);
            }

            api.ThrowOnCallNumber = 2; // second batch (ids 201-250) fails

            var svc = new TradingPostService(api);

            var result = await svc.GetPricesAsync(ids, CancellationToken.None);

            // Batch 1 (1-200) succeeded and is present; batch 2 (201-250)
            // failed and is degraded to holes, not an aborted call.
            Assert.Equal(200, result.Count);
            Assert.True(result.ContainsKey(1));
            Assert.False(result.ContainsKey(201));
        }

        // KNOWN-ISSUES #31/api-degradation F2: a genuine total outage (every
        // batch fails) must still surface as an error, not silently render
        // an all-unpriceable plan.
        [Fact]
        public async Task AllBatchesFail_ThrowsInsteadOfSilentlyReturningEmpty()
        {
            var api = new InMemoryPriceApiClient();
            api.AddPrice(1, buyUnitPrice: 1, sellUnitPrice: 2);
            api.ThrowAlways = true;
            var svc = new TradingPostService(api);

            await Assert.ThrowsAsync<HttpRequestException>(
                () => svc.GetPricesAsync(new[] { 1 }, CancellationToken.None));
        }

        // Edge case of the 31c-1 fix: a caller whose entire request is
        // satisfied purely by joining another overlapping call's in-flight
        // fetch must still see the failure if that joined fetch fails
        // totally - it must not silently return an empty result just
        // because it started no batch of its own.
        [Fact]
        public async Task ConcurrentCalls_UpstreamFails_JoiningCallerAlsoThrows()
        {
            var api = new InMemoryPriceApiClient();
            api.ThrowAlways = true;
            var gate = new TaskCompletionSource<bool>();
            api.Gate = gate.Task;
            var svc = new TradingPostService(api);

            var task1 = svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);
            var task2 = svc.GetPricesAsync(new[] { 1 }, CancellationToken.None);

            gate.SetResult(true);

            await Assert.ThrowsAsync<HttpRequestException>(() => task1);
            await Assert.ThrowsAsync<HttpRequestException>(() => task2);
            Assert.Single(api.Calls); // still just one shared upstream attempt
        }
    }
}
