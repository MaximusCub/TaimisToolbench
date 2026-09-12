using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Services
{
    internal class CompositeRecipeApiClient : IRecipeApiClient
    {
        private readonly IRecipeApiClient _primary;
        private readonly MysticForgeRecipeData _mfData;

        public CompositeRecipeApiClient(IRecipeApiClient primary, MysticForgeRecipeData mfData)
        {
            _primary = primary;
            _mfData = mfData;
        }

        // The wiki-derived Mystic Forge recipes are a local overlay, never
        // evidence about what the API endpoint knows, so AbsenceProven is
        // always the primary's: an outage stays unproven even when MF data
        // fills the answer, because the API's own recipes for this item are
        // still unknown.
        public async Task<RecipeSearchResult> SearchByOutputAsync(int itemId, CancellationToken ct)
        {
            var apiResult = await _primary.SearchByOutputAsync(itemId, ct);
            var apiResults = apiResult.RecipeIds;
            var mfResults = _mfData.SearchByOutput(itemId);

            if (mfResults.Count == 0)
            {
                return apiResult;
            }

            if (apiResults.Count == 0)
            {
                return new RecipeSearchResult(mfResults, apiResult.AbsenceProven);
            }

            // Merge: API first, then MF, deduplicated
            var seen = new HashSet<int>();
            var merged = new List<int>();

            foreach (var id in apiResults)
            {
                if (seen.Add(id))
                {
                    merged.Add(id);
                }
            }

            foreach (var id in mfResults)
            {
                if (seen.Add(id))
                {
                    merged.Add(id);
                }
            }

            return new RecipeSearchResult(merged, apiResult.AbsenceProven);
        }

        public Task<RawRecipe> GetRecipeAsync(int recipeId, CancellationToken ct)
        {
            // Membership check, not a bare sign check: a negative recipeId
            // is Mystic Forge ONLY if MysticForgeRecipeData recognizes it.
            // Any other negative id must fall through to primary rather
            // than be swallowed as a false "not found" from mfData alone.
            if (recipeId < 0)
            {
                var mfRecipe = _mfData.GetRecipe(recipeId);
                if (mfRecipe != null)
                {
                    return Task.FromResult(mfRecipe);
                }
            }

            return _primary.GetRecipeAsync(recipeId, ct);
        }
    }
}
