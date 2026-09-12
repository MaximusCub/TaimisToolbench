using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal class TradingPostService
    {
        private const int BatchSize = 200;

        /// <summary>
        /// How long a fetched price answers later requests before it is
        /// re-fetched. Also the window a known-untradeable id is remembered
        /// for, so an item that becomes tradeable in a patch is exactly as
        /// stale as a cached price is, no worse.
        /// <para>
        /// Two minutes is a spam guard, chosen by the maintainer: a user
        /// pressing Generate Plan repeatedly must not fire a price fetch
        /// each time, and trading post prices do not move much in two
        /// minutes. It replaced a 15 minute window, which was long enough
        /// that a plan generated twenty minutes apart still quoted the
        /// older prices.
        /// </para>
        /// </summary>
        public static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(120);

        private readonly IPriceApiClient _api;
        private readonly Func<DateTime> _utcNow;
        private readonly object _cacheLock = new object();
        private readonly Dictionary<int, (ItemPrice Price, DateTime FetchedUtc)> _cache =
            new Dictionary<int, (ItemPrice Price, DateTime FetchedUtc)>();

        // KNOWN-ISSUES #31/31c-1: per-id in-flight tracking so a second
        // overlapping GetPricesAsync call that needs an id this call is
        // already fetching awaits THIS call's fetch instead of starting a
        // duplicate one. Every not-yet-cached id a given call decides to
        // fetch itself is registered here, all pointing at the same
        // FetchOwnBatchesAsync Task for that call. Only ever mutated under
        // _cacheLock, and never while the lock is held across an await -
        // every access below is a plain synchronous dictionary op.
        //
        // KNOWN-ISSUES #31/31c-audit: a shared FetchOwnBatchesAsync Task here
        // is never tied to any single caller's CancellationToken (it
        // always runs with CancellationToken.None internally - see that
        // method) precisely because it is shared - a joiner's or the
        // owner's own cancellation must never abandon work the OTHER
        // caller still needs. Each caller instead observes its own ct
        // while awaiting via AwaitRespectingOwnCancellationAsync below, so
        // cancellation stays strictly per-caller in both directions.
        private readonly Dictionary<int, Task> _inFlight = new Dictionary<int, Task>();

        // Ids an answered batch REQUESTED but the response did not
        // contain: /v2/commerce/prices omits untradeable items entirely,
        // so an account-bound id (gifts, clovers, a legendary target
        // itself) never reaches _cache above and would otherwise be
        // re-requested on every call forever. Mirrors
        // ItemMetadataService._knownMissing, but TTL'd on the same
        // CacheTtl clock as the positive cache rather than kept for the
        // session: an item that becomes tradeable in a patch is then
        // exactly as stale as an already-cached price is, no worse.
        // Growth is bounded by the same thing _cache's is - how many
        // distinct ids one session looks up.
        private readonly Dictionary<int, DateTime> _knownMissing = new Dictionary<int, DateTime>();

        public TradingPostService(IPriceApiClient api, Func<DateTime> utcNow = null)
        {
            _api = api;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public async Task<IReadOnlyDictionary<int, ItemPrice>> GetPricesAsync(
            IEnumerable<int> itemIds, CancellationToken ct)
        {
            var uniqueIds = new HashSet<int>(itemIds);
            var now = _utcNow();

            // joinTasks: another overlapping call's own in-flight fetch
            // that already covers one or more of our ids - we wait on it
            // instead of re-fetching (KNOWN-ISSUES #31/31c-1). ownTask: this
            // call's own fetch for whatever ids are neither cache-fresh nor
            // already in flight elsewhere, or null if nothing needs
            // fetching.
            //
            // Deciding which ids are fresh/in-flight/covered and
            // registering this call's own fetch into _inFlight happen
            // inside ONE lock acquisition - splitting that into two
            // separate lock statements would leave a window where a second
            // overlapping caller's own decide-phase runs before this call's
            // ids are visible in _inFlight, letting both callers claim the
            // same "fresh" id and duplicate-fetch it (defeating the point
            // of this guard). FetchOwnBatchesAsync (see its own doc
            // comment) always suspends before doing any real work, so
            // calling it from inside this lock is safe - it can never race
            // ahead of its own registration below.
            var joinTasks = new List<Task>();
            Task ownTask = null;

            lock (_cacheLock)
            {
                var freshIds = new List<int>();
                foreach (var id in uniqueIds)
                {
                    if (_cache.TryGetValue(id, out var cached) && now - cached.FetchedUtc < CacheTtl)
                    {
                        continue;
                    }

                    if (_knownMissing.TryGetValue(id, out var missedUtc) && now - missedUtc < CacheTtl)
                    {
                        continue;
                    }

                    if (_inFlight.TryGetValue(id, out var existingFetch))
                    {
                        joinTasks.Add(existingFetch);
                    }
                    else
                    {
                        freshIds.Add(id);
                    }
                }

                if (freshIds.Count > 0)
                {
                    // Deliberately NOT this caller's ct - see
                    // FetchOwnBatchesAsync's own doc comment and the
                    // KNOWN-ISSUES #31/31c-audit note on _inFlight above.
                    ownTask = FetchOwnBatchesAsync(freshIds, now);
                    foreach (var id in freshIds)
                    {
                        _inFlight[id] = ownTask;
                    }
                }
            }

            // Every fetch operation relevant to THIS call's request -
            // whether it is this call's own (possibly multi-batch) fetch or
            // another overlapping caller's in-flight fetch we are joining -
            // counts toward the total-failure tally below.
            // FetchOwnBatchesAsync already degrades a single failing batch
            // to holes internally (KNOWN-ISSUES #31/api-degradation F2) and
            // only faults if ALL of its own batches failed; a caller whose
            // entire request is satisfied purely via joined fetches must
            // still see a thrown error if every one of those also failed,
            // exactly like a caller with its own failed fetch - otherwise
            // it would silently render an all-unpriceable plan instead of
            // surfacing "Refresh failed"/"Error: ...".
            Exception lastFailure = null;
            int attempted = 0;
            int succeeded = 0;

            if (ownTask != null)
            {
                attempted++;
                try
                {
                    // Respects THIS call's own ct even though ownTask
                    // itself runs with CancellationToken.None internally
                    // (KNOWN-ISSUES #31/31c-audit) - see
                    // AwaitRespectingOwnCancellationAsync's doc comment.
                    await AwaitRespectingOwnCancellationAsync(ownTask, ct);
                    succeeded++;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    lastFailure = ex;
                }
            }

            foreach (var task in joinTasks.Distinct())
            {
                attempted++;
                try
                {
                    // `task` belongs to a DIFFERENT overlapping caller
                    // that owns it; joining must still respect THIS
                    // caller's own ct rather than inheriting whatever
                    // that other caller's cancellation state is
                    // (KNOWN-ISSUES #31/31c-audit).
                    await AwaitRespectingOwnCancellationAsync(task, ct);
                    succeeded++;
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    // The other caller's fetch failed; the ids it covered
                    // stay missing from _cache -> unpriceable holes below,
                    // same degraded state as this call's own failed batch.
                    lastFailure = ex;
                }
            }

            if (attempted > 0 && succeeded == 0)
            {
                throw lastFailure;
            }

            var result = new Dictionary<int, ItemPrice>();
            lock (_cacheLock)
            {
                foreach (var id in uniqueIds)
                {
                    if (_cache.TryGetValue(id, out var cached) && now - cached.FetchedUtc < CacheTtl)
                    {
                        result[id] = cached.Price;
                    }
                }
            }

            return result;
        }

        // Fetches every batch of `ids` this call itself owns, strictly one
        // batch at a time in order - identical sequencing to this method's
        // old inline for-loop, so a single caller's own batch
        // count/order/timing is unaffected by the KNOWN-ISSUES #31/31c-1
        // coalescing added around it.
        //
        // KNOWN-ISSUES #31/31c-audit: deliberately takes no CancellationToken.
        // The returned Task is registered into _inFlight and may be
        // awaited by other overlapping GetPricesAsync callers besides the
        // one that decided to run it (a "joiner"); threading any single
        // caller's ct into the actual upstream fetch would let that one
        // caller's cancellation abandon work another, still-active caller
        // depends on. Every caller - owner or joiner - instead observes
        // its own ct only while awaiting the shared Task, via
        // AwaitRespectingOwnCancellationAsync below; the fetch itself
        // always runs to completion (success or genuine failure).
        private async Task FetchOwnBatchesAsync(List<int> ids, DateTime fetchedUtc)
        {
            // Always suspend here first, before doing any real work. The
            // caller (GetPricesAsync) invokes this method from inside
            // _cacheLock and then immediately registers the returned Task
            // into _inFlight for every id in `ids`. Without this yield, an
            // IPriceApiClient that completes synchronously (a fake used in
            // tests, or any future in-process fast path) could run this
            // whole method - including the finally's _inFlight cleanup
            // below - to completion before the caller has actually written
            // those _inFlight entries. The cleanup would then remove
            // nothing (the entries do not exist yet) and the caller's
            // subsequent write would leave a stale, never-cleaned-up entry
            // in _inFlight forever - silently breaking TTL re-fetches for
            // those ids on every later call.
            await Task.Yield();

            try
            {
                // KNOWN-ISSUES #31/api-degradation F2: a single failing batch
                // degrades to missing ids (unpriceable holes downstream, an
                // already-supported state) instead of aborting the whole
                // fetch - mirroring ItemMetadataService's retry-wave catch.
                // Re-thrown at the end only if EVERY batch failed (a
                // genuine total price-API outage), so a real outage still
                // surfaces as an error instead of silently rendering an
                // all-unpriceable plan.
                Exception lastBatchFailure = null;
                int succeededBatches = 0;
                int batchCount = 0;

                for (int i = 0; i < ids.Count; i += BatchSize)
                {
                    int count = Math.Min(BatchSize, ids.Count - i);
                    var batch = ids.GetRange(i, count);
                    batchCount++;

                    try
                    {
                        // CancellationToken.None, not any caller's ct -
                        // see this method's own doc comment.
                        var response = await _api.GetPricesAsync(batch, CancellationToken.None);

                        lock (_cacheLock)
                        {
                            var returned = new HashSet<int>();
                            foreach (var entry in response.Entries)
                            {
                                var price = new ItemPrice
                                {
                                    ItemId = entry.Id,
                                    BuyInstant = entry.SellUnitPrice,
                                    SellInstant = entry.BuyUnitPrice,
                                };
                                _cache[entry.Id] = (price, fetchedUtc);
                                returned.Add(entry.Id);

                                // Newly tradeable: drop the stale negative
                                // entry rather than leaving two records of
                                // the same id disagreeing.
                                _knownMissing.Remove(entry.Id);
                            }

                            // Only a batch the endpoint actually answered
                            // proves an id is absent: a thrown batch
                            // (below) tells us nothing, and neither does a
                            // 404, which the endpoint returns for an
                            // outage as readily as for "every id here is
                            // untradeable" (see
                            // PriceBatchResult.AbsenceProven). Negative-
                            // caching either would blank every price in
                            // the plan for a full CacheTtl without so much
                            // as another request to recover from.
                            if (response.AbsenceProven)
                            {
                                foreach (var id in batch)
                                {
                                    if (!returned.Contains(id))
                                    {
                                        _knownMissing[id] = fetchedUtc;
                                    }
                                }
                            }
                        }

                        succeededBatches++;
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        lastBatchFailure = ex;
                    }
                }

                if (batchCount > 0 && succeededBatches == 0)
                {
                    throw lastBatchFailure;
                }
            }
            finally
            {
                lock (_cacheLock)
                {
                    foreach (var id in ids)
                    {
                        _inFlight.Remove(id);
                    }
                }
            }
        }

        // KNOWN-ISSUES #31/31c-audit: awaits a shared fetch Task (owned or
        // joined) while respecting only THIS caller's own `ct`, never the
        // cancellation state of whichever caller happens to own `task`.
        // If `ct` fires first, throws a fresh OperationCanceledException
        // tied to `ct` immediately - `task` itself is left running
        // untouched for any other caller still awaiting it. If `task`
        // completes first, its own result/exception is propagated as-is.
        private static async Task AwaitRespectingOwnCancellationAsync(Task task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled)
            {
                await task;
                return;
            }

            var cancellationSignal = new TaskCompletionSource<bool>();
            using (ct.Register(() => cancellationSignal.TrySetResult(true)))
            {
                var completed = await Task.WhenAny(task, cancellationSignal.Task);
                if (completed == cancellationSignal.Task)
                {
                    throw new OperationCanceledException(ct);
                }
            }

            await task;
        }
    }
}
