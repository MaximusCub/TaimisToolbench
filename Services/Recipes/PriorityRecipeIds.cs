using System.Collections.Generic;

namespace TaimisToolbench.Services.Recipes
{
    /// <summary>
    /// Turns the item ids the user actually depends on - the Ranker
    /// watchlist, the current plan, plan history - into the recipe ids
    /// reachable from them, so <see cref="RecipeCorpusRefresher"/> can
    /// refetch those first.
    /// <para>
    /// The walk is transitive because a stale row deep in a tree misprices
    /// the root just as badly as a stale root: it takes each item's recipe
    /// rows, then the item-typed ingredients of those recipes, and repeats.
    /// Currency and GuildUpgrade ingredients are a different id space and
    /// are never enqueued as items.
    /// </para>
    /// <para>
    /// Read entirely out of the corpus already on disk - no requests - so
    /// it is safe to call before the sweep opens a socket. Ordering only:
    /// the sweep covers every held id regardless, so a walk that stops
    /// early at <see cref="MaxIds"/> costs priority, never coverage.
    /// </para>
    /// </summary>
    internal static class PriorityRecipeIds
    {
        // A watchlist is capped at 25 items and a plan is usually a
        // handful, but history is unbounded and a deep tree fans out, so
        // the walk is capped rather than trusted to stay small. 2,000 ids
        // is 10 of the sweep's 200-wide requests: enough to cover any
        // realistic set of trees, small enough that the priority pass
        // cannot become the sweep.
        internal const int MaxIds = 2000;

        public static IReadOnlyList<int> FromItemIds(
            IRecipeCacheStore store, IEnumerable<int> itemIds)
        {
            var recipeIds = new List<int>();
            if (store == null || itemIds == null)
            {
                return recipeIds;
            }

            var seenRecipes = new HashSet<int>();
            var seenItems = new HashSet<int>();
            var pending = new Queue<int>();

            foreach (int itemId in itemIds)
            {
                if (itemId > 0 && seenItems.Add(itemId))
                {
                    pending.Enqueue(itemId);
                }
            }

            while (pending.Count > 0 && recipeIds.Count < MaxIds)
            {
                int itemId = pending.Dequeue();
                var rows = store.TryGetSearch(itemId);
                if (rows == null)
                {
                    continue;
                }

                foreach (int recipeId in rows)
                {
                    // Negative ids are the Mystic Forge rows; the live
                    // API has no such recipe and the sweep never asks for
                    // one.
                    if (recipeId <= 0 || !seenRecipes.Add(recipeId))
                    {
                        continue;
                    }

                    recipeIds.Add(recipeId);
                    if (recipeIds.Count >= MaxIds)
                    {
                        break;
                    }

                    var recipe = store.TryGetRecipe(recipeId);
                    if (recipe?.Ingredients == null)
                    {
                        continue;
                    }

                    foreach (var ingredient in recipe.Ingredients)
                    {
                        if (ingredient.Type == "Item"
                            && ingredient.Id > 0
                            && seenItems.Add(ingredient.Id))
                        {
                            pending.Enqueue(ingredient.Id);
                        }
                    }
                }
            }

            return recipeIds;
        }
    }
}
