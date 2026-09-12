using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Services.Recipes
{
    internal enum CorpusRefreshStatus
    {
        /// <summary>The sweep already finished at this build - 0 requests.</summary>
        Skipped,

        /// <summary>Every held positive recipe was refetched at this build.</summary>
        Completed,

        /// <summary>The API stopped answering; progress is persisted and the next launch resumes from the cursor.</summary>
        Interrupted,
    }

    internal class CorpusRefreshResult
    {
        public CorpusRefreshResult(
            CorpusRefreshStatus status,
            int recipesUpdated = 0,
            int recipesFetched = 0,
            int requestCount = 0,
            int resumedFromCursorId = 0,
            Exception error = null)
        {
            Status = status;
            RecipesUpdated = recipesUpdated;
            RecipesFetched = recipesFetched;
            RequestCount = requestCount;
            ResumedFromCursorId = resumedFromCursorId;
            Error = error;
        }

        public CorpusRefreshStatus Status { get; }

        /// <summary>Rows whose fetched content differed from the held one.</summary>
        public int RecipesUpdated { get; }

        public int RecipesFetched { get; }

        public int RequestCount { get; }

        /// <summary>0 when this run started the sweep from the beginning.</summary>
        public int ResumedFromCursorId { get; }

        public Exception Error { get; }
    }

    /// <summary>
    /// The corpus sweep: refetches every positive recipe the module holds from
    /// <c>/v2/recipes?ids=</c> once per game build, so a recipe whose id never
    /// changed but whose INGREDIENTS did is not served stale forever.
    /// <see cref="RecipeCorpusVerifier"/> cannot see such a change, because it
    /// only ever fetches ids the corpus lacks.
    /// <para>
    /// What comes back is stored, not compared-then-stored: the response IS the
    /// recipe's current shape, so there is nothing to validate it against. The
    /// one comparison here decides whether the row needs WRITING at all, and it
    /// can only suppress a write that would have changed nothing.
    /// </para>
    /// <para>
    /// Different guarantee from the verifier's, so it is sequenced after it and
    /// gates nothing: the verifier licenses NEGATIVES ("no recipe makes this"),
    /// this repairs POSITIVES ("here is what it consumes"). Plan generation
    /// never waits on either; the corpus improves underneath it. See
    /// docs/ARCHITECTURE.md, S2.11.
    /// </para>
    /// </summary>
    internal class RecipeCorpusRefresher
    {
        // A background repair, not a race: the sweep is ~67 requests for a
        // 13,371-recipe corpus and nothing is waiting on it, so it idles
        // between batches rather than emptying the corpus at line rate.
        private static readonly TimeSpan DefaultBatchDelay = TimeSpan.FromSeconds(1);

        private readonly Gw2RecipeApiClient _detailClient;
        private readonly CompositeRecipeCacheStore _store;
        private readonly Action<int> _onSearchRepaired;
        private readonly Action<string> _onDiagnostic;
        private readonly TimeSpan _batchDelay;

        public RecipeCorpusRefresher(
            HttpClient http,
            CompositeRecipeCacheStore store,
            Action<int> onSearchRepaired = null,
            Action<string> onDiagnostic = null,
            TimeSpan? batchDelay = null)
        {
            _detailClient = new Gw2RecipeApiClient(http);
            _store = store;
            _onSearchRepaired = onSearchRepaired;
            _onDiagnostic = onDiagnostic;
            _batchDelay = batchDelay ?? DefaultBatchDelay;
        }

        public async Task<CorpusRefreshResult> RefreshAsync(
            int liveBuildId,
            IReadOnlyCollection<int> knownPositiveRecipeIds,
            IReadOnlyCollection<int> priorityRecipeIds,
            CancellationToken ct)
        {
            Gw2ApiConnectionLimit.Apply();

            // An empty corpus is not a refreshed one: stamping it complete
            // here would suppress the sweep for the whole patch cycle if
            // the seed failed to load.
            if (knownPositiveRecipeIds == null || knownPositiveRecipeIds.Count == 0)
            {
                return new CorpusRefreshResult(CorpusRefreshStatus.Skipped);
            }

            bool sameBuild = _store.CorpusRefreshBuildId == liveBuildId;
            if (sameBuild && _store.CorpusRefreshComplete)
            {
                return new CorpusRefreshResult(CorpusRefreshStatus.Skipped);
            }

            ct.ThrowIfCancellationRequested();

            // A build that moved is exactly the trigger: drop the
            // old cursor and rebuild the corpus for the new build.
            int cursor = sameBuild ? _store.CorpusRefreshCursorId : 0;

            var ascending = new List<int>(knownPositiveRecipeIds.Count);
            foreach (int id in knownPositiveRecipeIds)
            {
                if (id > cursor)
                {
                    ascending.Add(id);
                }
            }

            ascending.Sort();

            // The priority pass is ordering ONLY - every id in it is also
            // in the ascending pass. Excluding them would let the cursor
            // march past an id that a later launch no longer considers
            // priority, stranding it stale; re-fetching a few hundred ids
            // costs a request or two out of ~67 and keeps the cursor's
            // meaning total: everything at or below it has been refetched.
            var order = new List<int>(ascending.Count + (priorityRecipeIds?.Count ?? 0));
            if (priorityRecipeIds != null)
            {
                var known = new HashSet<int>(knownPositiveRecipeIds);
                foreach (int id in priorityRecipeIds)
                {
                    if (id > 0 && known.Contains(id))
                    {
                        order.Add(id);
                    }
                }
            }

            int priorityCount = order.Count;
            order.AddRange(ascending);

            return await SweepAsync(liveBuildId, order, priorityCount, cursor, ct);
        }

        private async Task<CorpusRefreshResult> SweepAsync(
            int liveBuildId,
            IReadOnlyList<int> order,
            int priorityCount,
            int resumedFrom,
            CancellationToken ct)
        {
            int updated = 0;
            int fetched = 0;
            int requests = 0;
            int cursor = resumedFrom;
            var batch = new List<int>(Gw2RecipeApiClient.BatchSize);

            for (int offset = 0; offset < order.Count; offset += Gw2RecipeApiClient.BatchSize)
            {
                // Explicit at the batch boundary rather than trusting
                // HttpClient to observe the token: unload must never be
                // held behind a sweep.
                ct.ThrowIfCancellationRequested();

                batch.Clear();
                int count = Math.Min(Gw2RecipeApiClient.BatchSize, order.Count - offset);
                for (int i = 0; i < count; i++)
                {
                    batch.Add(order[offset + i]);
                }

                List<RawRecipe> rows;
                try
                {
                    rows = await _detailClient.GetRecipesAsync(batch, ct);
                    requests++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // Whatever landed stays; the cursor already persisted
                    // covers only batches that completed, so the next
                    // launch re-fetches this one rather than skipping it.
                    PersistProgress(liveBuildId, cursor, complete: false);
                    return new CorpusRefreshResult(
                        CorpusRefreshStatus.Interrupted, updated, fetched,
                        requests, resumedFrom, ex);
                }

                fetched += rows.Count;
                foreach (var recipe in rows)
                {
                    if (Store(recipe))
                    {
                        updated++;
                    }
                }

                // The cursor only means anything for the ascending pass;
                // the priority batches run before it and are covered again
                // below, so they must not advance it.
                bool inAscendingPass = offset + count > priorityCount;
                if (inAscendingPass)
                {
                    cursor = order[offset + count - 1];
                }

                PersistProgress(liveBuildId, cursor, complete: false);

                if (offset + count < order.Count)
                {
                    await Task.Delay(_batchDelay, ct);
                }
            }

            PersistProgress(liveBuildId, cursor, complete: true);
            return new CorpusRefreshResult(
                CorpusRefreshStatus.Completed, updated, fetched, requests, resumedFrom);
        }

        /// <summary>
        /// Writes the fetched row when it differs from what the store
        /// serves today, and returns whether it did. Every field the API
        /// populates is compared, so "no difference" provably means the
        /// write would have been a no-op - the ingredient TYPE change that
        /// motivates this class cannot slip through a field the comparison
        /// forgot.
        /// </summary>
        private bool Store(RawRecipe fetched)
        {
            var held = _store.TryGetRecipe(fetched.Id);
            if (held != null && SameApiContent(held, fetched))
            {
                return false;
            }

            _store.PutRecipe(fetched.Id, CarryLocalFields(held, fetched));

            // An output that moved leaves the old item's row pointing at a
            // recipe that no longer makes it. The row is reduced when
            // something is left in it; an empty one cannot be stored (the
            // overlay refuses empties by design, negatives being derived),
            // so the stale single-entry case is left to the next verifier
            // pass rather than half-shadowed here.
            if (held != null && held.OutputItemId != fetched.OutputItemId)
            {
                RemoveFromRow(held.OutputItemId, fetched.Id);
                _onSearchRepaired?.Invoke(held.OutputItemId);
            }

            _store.PutSearch(
                fetched.OutputItemId,
                MergeRow(_store.TryGetSearch(fetched.OutputItemId), fetched.Id));
            _onSearchRepaired?.Invoke(fetched.OutputItemId);

            _onDiagnostic?.Invoke(
                $"recipe {fetched.Id} content changed at this build "
                + $"(output {fetched.OutputItemId}, {fetched.Ingredients.Count} ingredient(s))");
            return true;
        }

        /// <summary>
        /// Copies the fields the GW2 API does not serve and the seeder
        /// authors locally - the fractional expected-output override and
        /// the achievement ids behind ingredient dedup - onto the fetched
        /// row before it is stored. The API never returns these, so
        /// dropping them would not be "the API says null"; it would be
        /// this class deleting curated data it never asked about.
        /// </summary>
        private static RawRecipe CarryLocalFields(RawRecipe held, RawRecipe fetched)
        {
            if (held == null)
            {
                return fetched;
            }

            fetched.ExpectedOutputCount = fetched.ExpectedOutputCount ?? held.ExpectedOutputCount;
            fetched.AchievementId = fetched.AchievementId ?? held.AchievementId;

            if (held.Ingredients == null || fetched.Ingredients == null)
            {
                return fetched;
            }

            foreach (var ingredient in fetched.Ingredients)
            {
                foreach (var previous in held.Ingredients)
                {
                    if (previous.Id == ingredient.Id
                        && string.Equals(previous.Type, ingredient.Type, StringComparison.Ordinal))
                    {
                        ingredient.AchievementId = ingredient.AchievementId ?? previous.AchievementId;
                        ingredient.AchievementBit = ingredient.AchievementBit ?? previous.AchievementBit;
                        break;
                    }
                }
            }

            return fetched;
        }

        private void RemoveFromRow(int outputItemId, int recipeId)
        {
            var row = _store.TryGetSearch(outputItemId);
            if (row == null || row.Count == 0)
            {
                return;
            }

            var kept = new List<int>(row.Count);
            foreach (int id in row)
            {
                if (id != recipeId)
                {
                    kept.Add(id);
                }
            }

            if (kept.Count > 0 && kept.Count != row.Count)
            {
                _store.PutSearch(outputItemId, kept);
            }
        }

        private void PersistProgress(int buildId, int cursorRecipeId, bool complete)
        {
            _store.SetCorpusRefreshProgress(buildId, cursorRecipeId, complete);
            _store.Flush(force: true);
        }

        // Ordered rather than set-wise on every list: reordering alone is
        // not worth suppressing a write over, and being wrong in this
        // direction only ever costs one redundant write.
        private static bool SameApiContent(RawRecipe a, RawRecipe b)
        {
            if (a.Id != b.Id
                || a.OutputItemId != b.OutputItemId
                || a.OutputItemCount != b.OutputItemCount
                || a.MinRating != b.MinRating)
            {
                return false;
            }

            return SameStrings(a.Disciplines, b.Disciplines)
                   && SameStrings(a.Flags, b.Flags)
                   && SameIngredients(a.Ingredients, b.Ingredients);
        }

        private static bool SameIngredients(
            IReadOnlyList<RawIngredient> a, IReadOnlyList<RawIngredient> b)
        {
            int countA = a?.Count ?? 0;
            int countB = b?.Count ?? 0;
            if (countA != countB)
            {
                return false;
            }

            for (int i = 0; i < countA; i++)
            {
                if (a[i].Id != b[i].Id
                    || a[i].Count != b[i].Count
                    || !string.Equals(a[i].Type, b[i].Type, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SameStrings(IReadOnlyList<string> a, IReadOnlyList<string> b)
        {
            int countA = a?.Count ?? 0;
            int countB = b?.Count ?? 0;
            if (countA != countB)
            {
                return false;
            }

            for (int i = 0; i < countA; i++)
            {
                if (!string.Equals(a[i], b[i], StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static IReadOnlyList<int> MergeRow(IReadOnlyList<int> existing, int recipeId)
        {
            if (existing == null || existing.Count == 0)
            {
                return new List<int> { recipeId };
            }

            var merged = new List<int>(existing.Count + 1);
            merged.AddRange(existing);
            if (!merged.Contains(recipeId))
            {
                merged.Add(recipeId);
            }

            return merged;
        }
    }
}
