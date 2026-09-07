using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services.Recipes
{
    internal class CompositeRecipeCacheStore : IRecipeCacheStore
    {
        private readonly SeededRecipeCacheStore _seed;
        private readonly OverlayRecipeCacheStore _overlay;
        private readonly RecipeCacheStats _stats = new RecipeCacheStats();

        // Recipe ids the corpus probe found gone from the live id list -
        // measured 0 over 275 builds, so this is almost always null.
        // A replaced-wholesale set rather than a mutation of the stores'
        // dictionaries: the seed's maps are read lock-free by plan builds,
        // so they must never be mutated after load.
        private volatile HashSet<int> _removedRecipeIds;

        public RecipeCacheStats Stats => _stats;

        public bool SeedMayBeStale => _seed.SeedMayBeStale;

        public int? SeedBuildId => _seed.SeedBuildId;

        public int? CurrentBuildId => _seed.CurrentBuildId;

        /// <summary>
        /// Spec 2.3's "corpus usable": the seed actually loaded recipes.
        /// False disables derived negatives (TryGetSearch falls through to
        /// the API) and makes the corpus probe pointless - without a seed
        /// it would refetch the entire live corpus into the overlay.
        /// </summary>
        public bool CorpusUsable => _seed.RecipeCount > 0;

        public int NegativesVerifiedBuildId => _overlay.NegativesVerifiedBuildId;

        public int VerifiedKnownRecipeCount => _overlay.VerifiedKnownRecipeCount;

        public int CorpusRefreshBuildId => _overlay.CorpusRefreshBuildId;

        public int CorpusRefreshCursorId => _overlay.CorpusRefreshCursorId;

        public bool CorpusRefreshComplete => _overlay.CorpusRefreshComplete;

        public bool NegativesVerifiedAtCurrentBuild
        {
            get
            {
                int? current = _seed.CurrentBuildId;
                return current.HasValue
                    && _overlay.NegativesVerifiedBuildId == current.Value;
            }
        }

        public CompositeRecipeCacheStore(
            SeededRecipeCacheStore seed,
            OverlayRecipeCacheStore overlay)
        {
            _seed = seed;
            _overlay = overlay;
        }

        /// <summary>
        /// Records that the corpus was checked against the live recipe id
        /// list (RecipeCorpusVerifier); flushed into the overlay manifest.
        /// </summary>
        public void SetCorpusVerified(int buildId, int knownRecipeCount)
        {
            _overlay.SetCorpusVerified(buildId, knownRecipeCount);
        }

        /// <summary>
        /// Records the content sweep's resume point
        /// (RecipeCorpusRefresher); flushed into the overlay manifest.
        /// </summary>
        public void SetCorpusRefreshProgress(int buildId, int cursorRecipeId, bool complete)
        {
            _overlay.SetCorpusRefreshProgress(buildId, cursorRecipeId, complete);
        }

        /// <summary>
        /// The union the corpus probe diffs against the live id list.
        /// Positive ids only: the 1,595 negative-id rows (Mystic Forge and
        /// the hand-authored achievement chains) are never in the live
        /// list and must never be treated as removed by it.
        /// </summary>
        public IReadOnlyCollection<int> GetKnownPositiveRecipeIds()
        {
            var ids = new HashSet<int>();
            foreach (int id in _seed.RecipeIds)
            {
                if (id > 0)
                {
                    ids.Add(id);
                }
            }

            foreach (int id in _overlay.GetRecipeIds())
            {
                if (id > 0)
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        /// <summary>
        /// Drops probe-detected removals from everything served this
        /// session, without touching the underlying maps. Not persisted:
        /// the verified corpus count is stamped at the live list's size,
        /// so a later launch still holding these ids re-arms the probe and
        /// re-detects them.
        /// </summary>
        public void SetRemovedRecipeIds(IReadOnlyCollection<int> recipeIds)
        {
            _removedRecipeIds = recipeIds != null && recipeIds.Count > 0
                ? new HashSet<int>(recipeIds)
                : null;
        }

        public IReadOnlyList<int> TryGetSearch(int outputItemId)
        {
            // Overlay first - it has newer content discovered after seed.
            var result = _overlay.TryGetSearch(outputItemId)
                ?? _seed.TryGetSearch(outputItemId);
            result = FilterRemoved(result);
            if (result != null && result.Count > 0)
            {
                _stats.IncrementSearchHit();
                return result;
            }

            // The authoritative negative, derived rather than stored: the
            // corpus (seed + forge + overlay) holds every recipe the live
            // id list does, so "no known recipe outputs this item" IS the
            // answer - the search endpoint would add nothing but its 15
            // known false negatives. The CorpusUsable guard matters:
            // Module.cs's seed-load catch can leave an empty seed, and an
            // empty corpus must not answer "no recipe" for everything.
            if (CorpusUsable)
            {
                _stats.IncrementSearchHit();
                return Array.Empty<int>();
            }

            _stats.IncrementSearchMiss();
            return null;
        }

        private IReadOnlyList<int> FilterRemoved(IReadOnlyList<int> recipeIds)
        {
            var removed = _removedRecipeIds;
            if (removed == null || recipeIds == null)
            {
                return recipeIds;
            }

            var kept = new List<int>(recipeIds.Count);
            foreach (int id in recipeIds)
            {
                if (!removed.Contains(id))
                {
                    kept.Add(id);
                }
            }

            return kept;
        }

        public RawRecipe TryGetRecipe(int recipeId)
        {
            var removed = _removedRecipeIds;
            if (removed != null && removed.Contains(recipeId))
            {
                _stats.IncrementRecipeMiss();
                return null;
            }

            // Overlay first - it has newer content discovered after seed.
            var result = _overlay.TryGetRecipe(recipeId);
            if (result != null)
            {
                _stats.IncrementRecipeHit();
                return result;
            }

            result = _seed.TryGetRecipe(recipeId);
            if (result != null)
            {
                _stats.IncrementRecipeHit();
                return result;
            }

            _stats.IncrementRecipeMiss();
            return null;
        }

        public void PutSearch(int outputItemId, IReadOnlyList<int> recipeIds)
        {
            _overlay.PutSearch(outputItemId, recipeIds);
        }

        public void PutRecipe(int recipeId, RawRecipe recipe)
        {
            _overlay.PutRecipe(recipeId, recipe);
        }

        public void Flush(bool force = false)
        {
            _overlay.Flush(force);
        }
    }
}
