using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace TaimisToolbench.Services.Recipes
{
    internal class SeededRecipeCacheStore : IRecipeCacheStore
    {
        private Dictionary<int, IReadOnlyList<int>> _searches =
            new Dictionary<int, IReadOnlyList<int>>();

        private Dictionary<int, RawRecipe> _recipes =
            new Dictionary<int, RawRecipe>();

        private readonly RecipeCacheStats _stats = new RecipeCacheStats();
        private int? _seedBuildId;
        private int _currentBuildId;
        private int _hasCurrent;

        public RecipeCacheStats Stats => _stats;

        /// <summary>
        /// How many recipes the corpus holds. 0 means the seed failed to
        /// load, in which case "no known recipe outputs item X" proves
        /// nothing - the composite's derived negatives key off this.
        /// </summary>
        public int RecipeCount => _recipes.Count;

        public int SearchRowCount => _searches.Count;

        /// <summary>
        /// Read-only after load (the seed maps never mutate once startup
        /// finishes), so handing out the key collection is safe.
        /// </summary>
        public IEnumerable<int> RecipeIds => _recipes.Keys;

        public int? SeedBuildId => _seedBuildId;

        public int? CurrentBuildId
        {
            get { return Volatile.Read(ref _hasCurrent) == 1 ? (int?)Volatile.Read(ref _currentBuildId) : null; }
        }

        /// <summary>
        /// Whether the seed is not known to match the live game build.
        /// True when the live build differs, and also when the live build is
        /// UNKNOWN - a /v2/build fetch that failed leaves the module unable
        /// to vouch for the seed, which is not the same as vouching for it.
        /// Reading unknown as fresh silenced the staleness status for the
        /// whole of any session that launched offline.
        /// <para>
        /// False when the seed carries no build id of its own: there is then
        /// no claim to make either way.
        /// </para>
        /// </summary>
        public bool SeedMayBeStale
        {
            get
            {
                if (!_seedBuildId.HasValue)
                {
                    return false;
                }

                if (Volatile.Read(ref _hasCurrent) == 0)
                {
                    return true;
                }

                return _seedBuildId.Value != Volatile.Read(ref _currentBuildId);
            }
        }

        public void Load(Stream searchStream, Stream recipesStream)
        {
            if (searchStream == null)
            {
                throw new ArgumentNullException(nameof(searchStream));
            }

            if (recipesStream == null)
            {
                throw new ArgumentNullException(nameof(recipesStream));
            }

            _searches = RecipeCacheSerializer.LoadSearchSeed(searchStream);
            _recipes = RecipeCacheSerializer.LoadRecipeSeed(recipesStream);
        }

        /// <summary>
        /// Folds the wiki-sourced Mystic Forge recipes into the seed, so
        /// they are served from cache like any other seeded recipe.
        /// <para>
        /// Load-time only (same contract as <see cref="Load"/>: called once
        /// at startup, before any lookup), and additive - an existing
        /// search entry keeps its API-sourced ids and gains the MF ones
        /// after them, matching CompositeRecipeApiClient's own merge order.
        /// </para>
        /// <para>
        /// Without this, wiki-sourced forge recipes would not be part of
        /// the corpus at all: "no known recipe outputs this item" (the
        /// answer <see cref="FinalizeIndex"/> and the composite's derived
        /// negatives are built from) would be wrong for every forge-only
        /// item, and the item would render UNKNOWN.
        /// </para>
        /// </summary>
        public void MergeMysticForgeRecipes(MysticForgeRecipeData mysticForge)
        {
            if (mysticForge == null)
            {
                return;
            }

            foreach (var recipe in mysticForge.AllRecipes)
            {
                _recipes[recipe.Id] = recipe;
                AddRecipeIdToRow(_searches, recipe.OutputItemId, recipe.Id);
            }
        }

        /// <summary>
        /// Makes the search index complete and positive-only: every held
        /// recipe's output item gains a row carrying that recipe's id, and
        /// every row still empty afterwards is dropped. An empty row was
        /// the seeder's stored "the API knows no recipe for this item"
        /// negative; under the staleness policy negatives are derived at
        /// lookup time from the corpus (CompositeRecipeCacheStore), so a
        /// surviving empty row could only shadow real data. Load-time
        /// only, called after <see cref="MergeMysticForgeRecipes"/>.
        /// </summary>
        public void FinalizeIndex()
        {
            foreach (var recipe in _recipes.Values)
            {
                AddRecipeIdToRow(_searches, recipe.OutputItemId, recipe.Id);
            }

            var emptyRows = _searches
                .Where(kvp => kvp.Value.Count == 0)
                .Select(kvp => kvp.Key)
                .ToList();
            foreach (int key in emptyRows)
            {
                _searches.Remove(key);
            }
        }

        // Shared with OverlayRecipeCacheStore's own index pass rather than
        // duplicated there.
        internal static bool AddRecipeIdToRow(
            Dictionary<int, IReadOnlyList<int>> searches, int outputItemId, int recipeId)
        {
            if (!searches.TryGetValue(outputItemId, out var existing))
            {
                searches[outputItemId] = new List<int> { recipeId };
                return true;
            }

            if (existing.Contains(recipeId))
            {
                return false;
            }

            var merged = new List<int>(existing.Count + 1);
            merged.AddRange(existing);
            merged.Add(recipeId);
            searches[outputItemId] = merged;
            return true;
        }

        public void LoadManifest(Stream manifestStream)
        {
            if (manifestStream == null)
            {
                return;
            }

            var manifest = RecipeCacheSerializer.LoadManifest<RecipeSeedManifest>(manifestStream);
            if (manifest.Gw2BuildId > 0)
            {
                _seedBuildId = manifest.Gw2BuildId;
            }
        }

        public void SetCurrentBuildId(int buildId)
        {
            Volatile.Write(ref _currentBuildId, buildId);
            Volatile.Write(ref _hasCurrent, 1);
        }

        public IReadOnlyList<int> TryGetSearch(int outputItemId)
        {
            if (_searches.TryGetValue(outputItemId, out var result))
            {
                _stats.IncrementSearchHit();
                return result;
            }

            _stats.IncrementSearchMiss();
            return null;
        }

        public RawRecipe TryGetRecipe(int recipeId)
        {
            if (_recipes.TryGetValue(recipeId, out var result))
            {
                _stats.IncrementRecipeHit();
                return result;
            }

            _stats.IncrementRecipeMiss();
            return null;
        }

        public void PutSearch(int outputItemId, IReadOnlyList<int> recipeIds)
        {
            // Read-only store - no-op.
        }

        public void PutRecipe(int recipeId, RawRecipe recipe)
        {
            // Read-only store - no-op.
        }

        public void Flush(bool force = false)
        {
            // Read-only store - nothing to persist.
        }
    }
}
