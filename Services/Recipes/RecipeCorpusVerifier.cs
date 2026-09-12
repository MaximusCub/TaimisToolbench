using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TaimisToolbench.Services.Recipes
{
    internal enum CorpusVerificationStatus
    {
        /// <summary>The manifest already records this build and corpus size - 0 requests.</summary>
        Skipped,

        /// <summary>The corpus now mirrors the live id list; the manifest is stamped.</summary>
        Verified,

        /// <summary>The API could not be reached or answered partially; nothing served changed for the worse and the manifest is NOT stamped, so the probe re-arms.</summary>
        Failed,
    }

    internal class CorpusVerificationResult
    {
        public CorpusVerificationResult(
            CorpusVerificationStatus status,
            IReadOnlyList<int> addedRecipeIds = null,
            IReadOnlyList<int> removedRecipeIds = null,
            Exception error = null)
        {
            Status = status;
            AddedRecipeIds = addedRecipeIds ?? Array.Empty<int>();
            RemovedRecipeIds = removedRecipeIds ?? Array.Empty<int>();
            Error = error;
        }

        public CorpusVerificationStatus Status { get; }

        public IReadOnlyList<int> AddedRecipeIds { get; }

        public IReadOnlyList<int> RemovedRecipeIds { get; }

        public Exception Error { get; }
    }

    /// <summary>
    /// The corpus probe: one GET of the /v2/recipes id list per game build
    /// (30 KB gzipped, measured), diffed against the recipe ids the module
    /// holds. Ids it lacks are fetched by ?ids= and folded into the overlay
    /// as positives with their output's search row repaired - which is what
    /// licenses CompositeRecipeCacheStore's derived negatives as exact. A
    /// recipe cannot exist without an id in that list, so after a green
    /// probe the corpus is a superset of the live corpus.
    /// <para>
    /// Runs in the background off the plan path, cancelled on module
    /// unload; a failure changes nothing served and leaves the manifest
    /// unstamped so the caller retries at the next plan generation.
    /// </para>
    /// </summary>
    internal class RecipeCorpusVerifier
    {
        private const string RecipeListUrl =
            "https://api.guildwars2.com/v2/recipes?v=" + Gw2RecipeApiClient.SchemaVersion;

        // Matches Gw2BuildApiClient's per-attempt timeout: the probe rides
        // right behind the build fetch and must never hold the background
        // task long on a dead network.
        private static readonly TimeSpan IdListTimeout = TimeSpan.FromSeconds(3);

        private readonly HttpClient _http;
        private readonly Gw2RecipeApiClient _detailClient;
        private readonly CompositeRecipeCacheStore _store;
        private readonly Action<int> _onSearchRepaired;

        public RecipeCorpusVerifier(
            HttpClient http,
            CompositeRecipeCacheStore store,
            Action<int> onSearchRepaired = null)
        {
            _http = http;
            _detailClient = new Gw2RecipeApiClient(http);
            _store = store;
            _onSearchRepaired = onSearchRepaired;
        }

        public async Task<CorpusVerificationResult> VerifyAsync(
            int liveBuildId,
            IReadOnlyCollection<int> knownPositiveRecipeIds,
            CancellationToken ct)
        {
            Gw2ApiConnectionLimit.Apply();

            // The cheap-out: a relaunch inside the same patch with the same
            // corpus costs zero requests. The count re-arms the probe when
            // a module update swaps the seed or the user clears the cache.
            if (_store.NegativesVerifiedBuildId == liveBuildId
                && _store.VerifiedKnownRecipeCount == knownPositiveRecipeIds.Count)
            {
                return new CorpusVerificationResult(CorpusVerificationStatus.Skipped);
            }

            // Explicit checks at each phase boundary rather than trusting
            // HttpClient to observe the token: unload must never be held
            // behind a repair, and a cancelled run must never stamp.
            ct.ThrowIfCancellationRequested();

            List<int> liveIds;
            try
            {
                liveIds = await FetchLiveIdListAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new CorpusVerificationResult(
                    CorpusVerificationStatus.Failed, error: ex);
            }

            var known = new HashSet<int>();
            foreach (int id in knownPositiveRecipeIds)
            {
                if (id > 0)
                {
                    known.Add(id);
                }
            }

            var liveSet = new HashSet<int>(liveIds);
            var newIds = new List<int>();
            foreach (int id in liveIds)
            {
                if (!known.Contains(id))
                {
                    newIds.Add(id);
                }
            }

            var removedIds = new List<int>();
            foreach (int id in known)
            {
                if (!liveSet.Contains(id))
                {
                    removedIds.Add(id);
                }
            }

            ct.ThrowIfCancellationRequested();

            if (newIds.Count > 0)
            {
                List<RawRecipe> fetched;
                try
                {
                    fetched = await _detailClient.GetRecipesAsync(newIds, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return new CorpusVerificationResult(
                        CorpusVerificationStatus.Failed, error: ex);
                }

                foreach (var recipe in fetched)
                {
                    _store.PutRecipe(recipe.Id, recipe);
                    _store.PutSearch(
                        recipe.OutputItemId,
                        MergeRow(_store.TryGetSearch(recipe.OutputItemId), recipe.Id));
                    _onSearchRepaired?.Invoke(recipe.OutputItemId);
                }

                if (fetched.Count < newIds.Count)
                {
                    // The list promised ids the detail fetch did not
                    // deliver; whatever landed is kept (each is a true
                    // positive) but the corpus is not a proven superset,
                    // so the manifest stays unstamped and the probe
                    // re-arms.
                    var landed = new List<int>(fetched.Count);
                    foreach (var recipe in fetched)
                    {
                        landed.Add(recipe.Id);
                    }

                    return new CorpusVerificationResult(
                        CorpusVerificationStatus.Failed,
                        addedRecipeIds: landed,
                        error: new InvalidOperationException(
                            $"id list promised {newIds.Count} new recipes, ?ids= delivered {fetched.Count}"));
                }
            }

            if (removedIds.Count > 0)
            {
                foreach (int removedId in removedIds)
                {
                    var recipe = _store.TryGetRecipe(removedId);
                    if (recipe != null)
                    {
                        _onSearchRepaired?.Invoke(recipe.OutputItemId);
                    }
                }

                _store.SetRemovedRecipeIds(removedIds);
            }

            ct.ThrowIfCancellationRequested();

            // Stamped at the LIVE list's size, which equals the held
            // positive corpus after a clean probe. While a removed id is
            // still held on disk the counts differ, so the next launch
            // re-runs the probe and re-detects the removal (tombstones are
            // session-only).
            _store.SetCorpusVerified(liveBuildId, liveSet.Count);
            _store.Flush(force: true);

            return new CorpusVerificationResult(
                CorpusVerificationStatus.Verified, newIds, removedIds);
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

        private async Task<List<int>> FetchLiveIdListAsync(CancellationToken ct)
        {
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(IdListTimeout);

                using (var response = await _http.GetAsync(RecipeListUrl, timeoutCts.Token))
                {
                    response.EnsureSuccessStatusCode();
                    string json = await response.Content.ReadAsStringAsync();
                    return JsonConvert.DeserializeObject<List<int>>(json)
                           ?? new List<int>();
                }
            }
        }
    }
}
