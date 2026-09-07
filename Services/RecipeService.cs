using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services.Recipes;

namespace TaimisToolbench.Services
{
    internal class RecipeService
    {
        private readonly IRecipeApiClient _api;
        private readonly int _maxConcurrency;
        private readonly IRecipeCacheStore _cacheStore;
        private readonly Dictionary<int, IReadOnlyList<int>> _searchCache = new Dictionary<int, IReadOnlyList<int>>();
        private readonly Dictionary<int, RawRecipe> _recipeCache = new Dictionary<int, RawRecipe>();
        private readonly object _cacheGate = new object();
        private Task _pendingCacheFlush = Task.CompletedTask;

        private const int DefaultMaxConcurrency = 4;

        public Action<string> OnStatusUpdate { get; set; }

        /// <summary>
        /// The persist started by the last completed tree build. Completes
        /// when that write has landed on disk; callers that need the overlay
        /// durable at a chosen moment wait on this rather than racing it.
        /// </summary>
        public Task PendingCacheFlush => Volatile.Read(ref _pendingCacheFlush);

        public RecipeService(
            IRecipeApiClient api,
            int maxConcurrency = DefaultMaxConcurrency,
            IRecipeCacheStore cacheStore = null)
        {
            if (maxConcurrency < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
            }

            _api = api;
            _maxConcurrency = maxConcurrency;
            _cacheStore = cacheStore ?? new InMemoryRecipeCacheStore();
        }

        public async Task<RecipeNode> BuildTreeAsync(int itemId, int quantity, CancellationToken ct)
        {
            try
            {
                return await BuildTreeCoreAsync(itemId, quantity, ct);
            }
            finally
            {
                SchedulePersist();
            }
        }

        /// <summary>
        /// Builds a single item's tree via BuildTreeAsync's own logic
        /// without the
        /// per-call cache flush, so BuildMultiItemTreeAsync below can build
        /// N item trees and flush exactly once at the end instead of once
        /// per item (a hot-path allocation/IO concern when N is not 1).
        /// </summary>
        private async Task<RecipeNode> BuildTreeCoreAsync(int itemId, int quantity, CancellationToken ct)
        {
            await PreWarmCacheAsync(itemId, ct);

            var visiting = new HashSet<int>();
            return await BuildNodeAsync(itemId, "Item", quantity, visiting, ct);
        }

        /// <summary>
        /// Builds each requested item's own tree via the exact same
        /// BuildTreeAsync path a single-item request uses, then - for 2+ items -
        /// wraps them under a synthetic root RecipeNode: a reserved-id,
        /// never-rendered "recipe" whose Ingredients are the N real item trees,
        /// each already carrying its own requested amount as its own Quantity.
        ///
        /// A single-entry request returns that item's own tree UNCHANGED - no
        /// wrapper at all - so a caller feeding a single-entry list into this
        /// method gets byte-identical output to calling BuildTreeAsync directly.
        ///
        /// Items are built sequentially, not concurrently: the persistent
        /// search/recipe caches (see PreWarmCacheAsync) already make any overlap
        /// between requested items' subtrees cheap on repeat calls, and
        /// sequential building adds no concurrency surface to a structure not
        /// designed for parallel mutation.
        ///
        /// Why a wrapper gives merged totals for free, and the gw2efficiency
        /// behaviour it echoes: docs/ARCHITECTURE.md, S2.6.
        /// </summary>
        public async Task<RecipeNode> BuildMultiItemTreeAsync(
            IReadOnlyList<PlanRequestItem> items, CancellationToken ct)
        {
            if (items == null || items.Count == 0)
            {
                throw new ArgumentException("At least one item is required.", nameof(items));
            }

            try
            {
                if (items.Count == 1)
                {
                    return await BuildTreeCoreAsync(items[0].ItemId, items[0].Quantity, ct);
                }

                var itemTrees = new List<RecipeNode>(items.Count);
                foreach (var item in items)
                {
                    itemTrees.Add(await BuildTreeCoreAsync(item.ItemId, item.Quantity, ct));
                }

                return BuildWrapperNode(itemTrees);
            }
            finally
            {
                SchedulePersist();
            }
        }

        /// <summary>
        /// Persists what this build discovered without the caller waiting for
        /// it. The overlay store rewrites its whole cache to disk, tens of
        /// milliseconds of file IO that CraftingPlanPipeline would otherwise
        /// spend inside its tree-build phase, growing with the cache; nothing
        /// downstream of the build reads those files.
        /// </summary>
        private void SchedulePersist()
        {
            Volatile.Write(
                ref _pendingCacheFlush,
                Task.Run(() => _cacheStore.Flush(force: true)));
        }

        /// <summary>
        /// See BuildMultiItemTreeAsync's doc comment. OutputCount/
        /// CraftsNeeded/ExpectedOutputCount are all 1 (gw2e's own
        /// `quantity: 1, output: 1`), so InventoryReducer's
        /// ComputeCraftsNeeded ratio math is a no-op for this synthetic
        /// node, and it never has a Disciplines entry (empty list, the
        /// RecipeOption default) so it can never be mistaken for a real
        /// craftable recipe by PlanResultBuilder's discipline/required-
        /// recipe derivation - moot anyway, since PlanSolver.Collect never
        /// generates a step for this node at all (see its own doc comment).
        /// </summary>
        private static RecipeNode BuildWrapperNode(List<RecipeNode> itemTrees)
        {
            var wrapperRecipe = new RecipeOption
            {
                RecipeId = Gw2Constants.MultiItemWrapperRecipeId,
                OutputCount = 1,
                CraftsNeeded = 1,
                ExpectedOutputCount = 1,
            };
            wrapperRecipe.Ingredients.AddRange(itemTrees);

            var wrapper = new RecipeNode
            {
                Id = Gw2Constants.MultiItemWrapperItemId,
                IngredientType = "Item",
                Quantity = 1,
            };
            wrapper.Recipes.Add(wrapperRecipe);
            return wrapper;
        }

        private async Task PreWarmCacheAsync(int itemId, CancellationToken ct)
        {
            var visited = new HashSet<int>();
            var frontier = new HashSet<int> { itemId };
            bool statusReported = false;
            bool staleReported = false;

            while (frontier.Count > 0)
            {
                // Sub-phase A: Search all frontier items concurrently
                await BoundedConcurrency.ForEachAsync(
                    frontier,
                    _maxConcurrency,
                    id => SearchByOutputCachedAsync(id, ct),
                    ct);

                // Report status if API calls dominate (miss rate > 50%)
                if (!statusReported)
                {
                    var stats = _cacheStore.Stats;
                    int total = stats.SearchHits + stats.SearchMisses;
                    if (total > 0 && stats.SearchMisses > stats.SearchHits)
                    {
                        OnStatusUpdate?.Invoke(
                            "Discovering recipes from API (first run may take 10s+)...");
                        statusReported = true;
                    }
                }

                // Say once per run when derived negatives may be behind the
                // live build: the seed predates it and no corpus probe has
                // confirmed the id list yet.
                if (!staleReported
                    && _cacheStore is CompositeRecipeCacheStore composite
                    && composite.SeedMayBeStale
                    && !composite.NegativesVerifiedAtCurrentBuild)
                {
                    int lastVerified = composite.NegativesVerifiedBuildId > 0
                        ? composite.NegativesVerifiedBuildId
                        : composite.SeedBuildId ?? 0;
                    OnStatusUpdate?.Invoke(string.Format(
                        CultureInfo.InvariantCulture,
                        "Recipe data not verified against the live build (last verified at build {0}); recipes added since then may show as UNKNOWN.",
                        lastVerified));
                    staleReported = true;
                }

                // Collect recipe IDs not yet cached
                var recipeIds = new HashSet<int>();
                foreach (var fid in frontier)
                {
                    IReadOnlyList<int> rids;
                    lock (_cacheGate)
                    {
                        _searchCache.TryGetValue(fid, out rids);
                    }

                    if (rids == null)
                    {
                        continue;
                    }

                    foreach (var rid in rids)
                    {
                        bool cached;
                        lock (_cacheGate)
                        {
                            cached = _recipeCache.ContainsKey(rid);
                        }

                        if (!cached)
                        {
                            recipeIds.Add(rid);
                        }
                    }
                }

                // Sub-phase B: Fetch all recipe details concurrently
                await BoundedConcurrency.ForEachAsync(
                    recipeIds,
                    _maxConcurrency,
                    rid => GetRecipeCachedAsync(rid, ct),
                    ct);

                // Build next frontier from ingredient item IDs
                visited.UnionWith(frontier);
                var nextFrontier = new HashSet<int>();

                foreach (var fid in frontier)
                {
                    IReadOnlyList<int> rids;
                    lock (_cacheGate)
                    {
                        _searchCache.TryGetValue(fid, out rids);
                    }

                    if (rids == null)
                    {
                        continue;
                    }

                    foreach (var rid in rids)
                    {
                        RawRecipe recipe;
                        lock (_cacheGate)
                        {
                            _recipeCache.TryGetValue(rid, out recipe);
                        }

                        if (recipe == null)
                        {
                            continue;
                        }

                        foreach (var ingredient in recipe.Ingredients)
                        {
                            if (ingredient.Type == "Item" && !visited.Contains(ingredient.Id))
                            {
                                nextFrontier.Add(ingredient.Id);
                            }
                        }
                    }
                }

                frontier = nextFrontier;
            }
        }

        private async Task<RecipeNode> BuildNodeAsync(
            int id, string ingredientType, int quantity,
            HashSet<int> visiting, CancellationToken ct,
            // Carried straight from the parent
            // RawIngredient that produced this node - null for the tree
            // root (never itself an ingredient) and for every ordinary
            // ingredient. See RecipeNode.AchievementBit's doc comment.
            int? achievementId = null, int? achievementBit = null)
        {
            var node = new RecipeNode
            {
                Id = id,
                IngredientType = ingredientType,
                Quantity = quantity,
                AchievementId = achievementId,
                AchievementBit = achievementBit,
            };

            if (ingredientType != "Item")
            {
                return node;
            }

            if (!visiting.Add(id))
            {
                return node;
            }

            try
            {
                var recipeIds = await SearchByOutputCachedAsync(id, ct);

                foreach (var recipeId in recipeIds)
                {
                    var raw = await GetRecipeCachedAsync(recipeId, ct);

                    // KNOWN-ISSUES #31/api-degradation F5: GetRecipeAsync now
                    // returns null on a 404 instead of throwing (previously
                    // unreachable here, since the search endpoint and the
                    // detail endpoint are backed by the same data - but no
                    // longer guaranteed given the new 404 handling). Skip
                    // this recipe id rather than crash on a null
                    // dereference below; mirrors PreWarmCacheAsync's own
                    // existing null-recipe guard a few lines up.
                    if (raw == null)
                    {
                        continue;
                    }

                    // Defaults to the nominal OutputItemCount (a no-op)
                    // whenever the source recipe has no fractional EV; only
                    // Mystic Clover-style Mystic Forge recipes set this
                    // below OutputItemCount.
                    double expectedOutputCount = raw.ExpectedOutputCount.HasValue && raw.ExpectedOutputCount.Value > 0
                        ? raw.ExpectedOutputCount.Value
                        : raw.OutputItemCount;

                    // craftsNeeded (and therefore every
                    // ingredient quantity scaled by it below) is computed
                    // from the EXPECTED output, not the nominal integer
                    // output - echoing gw2e's single-field output_item_count
                    // model (r1/r2), where quantity propagation and pricing
                    // both derive from the same fractional value. For a
                    // Mystic Clover-style recipe (EV 0.31) this means the
                    // number of Mystic Forge attempts - and thus the raw
                    // ingredients consumed - already reflects the expected
                    // failure rate, so the shopping list is never
                    // under-provisioned relative to what the craft step
                    // actually costs (see PlanSolver, which no longer
                    // re-amortizes cost on top of this).
                    int craftsNeeded;
                    try
                    {
                        craftsNeeded = checked((int)Math.Ceiling((double)quantity / expectedOutputCount));
                    }
                    catch (OverflowException)
                    {
                        // Malformed seed data (an absurdly tiny
                        // ExpectedOutputCount) - fall back to the nominal
                        // integer output rather than crash the whole tree
                        // build.
                        craftsNeeded = (int)Math.Ceiling((double)quantity / raw.OutputItemCount);
                    }

                    var option = new RecipeOption
                    {
                        RecipeId = raw.Id,
                        OutputCount = raw.OutputItemCount,
                        CraftsNeeded = craftsNeeded,
                        ExpectedOutputCount = expectedOutputCount,
                        Disciplines = new List<string>(raw.Disciplines),
                        MinRating = raw.MinRating,
                        Flags = new List<string>(raw.Flags),
                    };

                    foreach (var ingredient in raw.Ingredients)
                    {
                        int ingredientQuantity = craftsNeeded * ingredient.Count;
                        var childNode = await BuildNodeAsync(
                            ingredient.Id, ingredient.Type, ingredientQuantity,
                            visiting, ct, ingredient.AchievementId, ingredient.AchievementBit);
                        option.Ingredients.Add(childNode);
                    }

                    node.Recipes.Add(option);
                }
            }
            finally
            {
                visiting.Remove(id);
            }

            return node;
        }

        /// <summary>
        /// Drops one item's session-cached search row, so a corpus repair
        /// landing mid-session (RecipeCorpusVerifier) is visible to later
        /// builds instead of shadowed by the L0 memo for the whole session.
        /// </summary>
        public void InvalidateSearch(int outputItemId)
        {
            lock (_cacheGate)
            {
                _searchCache.Remove(outputItemId);
            }
        }

        private async Task<IReadOnlyList<int>> SearchByOutputCachedAsync(int itemId, CancellationToken ct)
        {
            lock (_cacheGate)
            {
                if (_searchCache.TryGetValue(itemId, out var cached))
                {
                    return cached;
                }
            }

            // Check persistent cache store before hitting API
            var stored = _cacheStore.TryGetSearch(itemId);
            if (stored != null)
            {
                lock (_cacheGate)
                {
                    if (!_searchCache.ContainsKey(itemId))
                    {
                        _searchCache[itemId] = stored;
                    }
                }

                return stored;
            }

            var result = await _api.SearchByOutputAsync(itemId, ct);

            // Only an answer the search endpoint actually gave is worth
            // keeping: a 404 means "nothing produces this item" as readily as
            // it means the endpoint is down (see
            // RecipeSearchResult.AbsenceProven), and what survives such a
            // response is at best incomplete - empty for an ordinary item,
            // or Mystic-Forge-only for one the composite client could fill
            // in. Held in _searchCache, that would render a craftable item
            // as an uncraftable (or half-craftable) leaf for the rest of
            // the session.
            if (!result.AbsenceProven)
            {
                return result.RecipeIds;
            }

            lock (_cacheGate)
            {
                if (!_searchCache.ContainsKey(itemId))
                {
                    _searchCache[itemId] = result.RecipeIds;
                }
            }

            // Only a non-empty answer is persisted, mirroring the null
            // guard on PutRecipe below: the search endpoint returns an
            // empty answer for 15 real craftable items whose recipes are
            // fetchable by id, so an empty answer is never a fact worth
            // keeping beyond this session. Cross-session "no recipe"
            // answers are derived from the corpus by
            // CompositeRecipeCacheStore instead.
            if (result.RecipeIds.Count > 0)
            {
                _cacheStore.PutSearch(itemId, result.RecipeIds);
            }

            return result.RecipeIds;
        }

        private async Task<RawRecipe> GetRecipeCachedAsync(int recipeId, CancellationToken ct)
        {
            lock (_cacheGate)
            {
                if (_recipeCache.TryGetValue(recipeId, out var cached))
                {
                    return cached;
                }
            }

            // Check persistent cache store before hitting API
            var stored = _cacheStore.TryGetRecipe(recipeId);
            if (stored != null)
            {
                lock (_cacheGate)
                {
                    if (!_recipeCache.ContainsKey(recipeId))
                    {
                        _recipeCache[recipeId] = stored;
                    }
                }

                return stored;
            }

            var result = await _api.GetRecipeAsync(recipeId, ct);

            lock (_cacheGate)
            {
                if (!_recipeCache.ContainsKey(recipeId))
                {
                    _recipeCache[recipeId] = result;
                }
            }

            // KNOWN-ISSUES #31/api-degradation F5: GetRecipeAsync can now
            // return null (a 404), which the in-memory _recipeCache above
            // tolerates fine (a session-lifetime negative cache, avoiding
            // repeat round-trips for a genuinely-missing id) - but the
            // persistent overlay store's own serializer
            // (RecipeCacheSerializer.SerializeRecipes) does
            // `recipes.Values.OrderBy(r => r.Id)`, which throws a
            // NullReferenceException on any null entry. That exception is
            // swallowed by OverlayRecipeCacheStore's own catch-all around
            // Flush(), but _recipes never removes the poisoned null entry -
            // so persisting the recipe overlay (and the manifest write that
            // follows it in the same call) would silently stop working for
            // the rest of the module session. Only persist a genuine hit.
            if (result != null)
            {
                _cacheStore.PutRecipe(recipeId, result);
            }

            return result;
        }
    }
}
