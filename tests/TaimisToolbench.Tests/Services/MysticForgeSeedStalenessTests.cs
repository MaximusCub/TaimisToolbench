using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Services.Recipes;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Reported in game: Gift of Rays shows UNKNOWN in the recipe tree,
    /// with the module logging "Recipe seed built for
    /// build 205505; current build 205780; seed negative entries will fall
    /// back to API" on every generation.
    ///
    /// The shape below is the REAL one, transcribed from the shipped seed:
    /// ref/recipe_search_seed.json maps item 107040 to recipe -1587, and
    /// ref/recipes_seed.json and ref/mystic_forge_recipes.json both carry
    /// -1587 (Gift of Rays, wiki-sourced). Every route through the cache is
    /// run against a game build that does NOT match the seed's, since that
    /// is the state it was seen in.
    /// <para>
    /// Measured outcome (see KNOWN-ISSUES #60, item 4):
    /// no route reproduces the reported UNKNOWN for Gift of Rays - it
    /// crafts from the shipped seed on every one of them, including as a
    /// child of the real parent plan. What these tests pin is that the
    /// build id cannot decide whether a wiki forge recipe resolves.
    /// </para>
    /// </summary>
    public class MysticForgeSeedStalenessTests
    {
        private const int GiftOfRays = 107040;
        // Only for the hand-built in-memory fixtures below; the shipped
        // seed's own id for this recipe is read from the seed, never pinned.
        private const int GiftOfRaysRecipe = -1587;
        private const int SeedBuild = 205505;
        private const int CurrentBuild = 205780;

        private static RawRecipe GiftOfRaysRawRecipe()
        {
            return new RawRecipe
            {
                Id = GiftOfRaysRecipe,
                OutputItemId = GiftOfRays,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 107136, Count = 1 },
                    new RawIngredient { Type = "Item", Id = 107201, Count = 1 },
                    new RawIngredient { Type = "Item", Id = 106975, Count = 1 },
                    new RawIngredient { Type = "Item", Id = 100569, Count = 10 },
                },
                Disciplines = new List<string> { "MysticForge" },
                MinRating = 0,
                Flags = new List<string>(),
            };
        }

        private static SeededRecipeCacheStore SeedStore(
            Dictionary<int, RawRecipe> recipes,
            Dictionary<int, IReadOnlyList<int>> searches,
            int seedBuildId,
            int currentBuildId)
        {
            var store = new SeededRecipeCacheStore();
            using (var recipeStream = new MemoryStream(
                Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeRecipes(recipes))))
            using (var searchStream = new MemoryStream(
                Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeSearches(searches))))
            {
                store.Load(searchStream, recipeStream);
            }

            string manifestJson = RecipeCacheSerializer.SerializeManifest(
                new RecipeSeedManifest { SeedVersion = 1, Gw2BuildId = seedBuildId });
            using (var manifestStream = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson)))
            {
                store.LoadManifest(manifestStream);
            }

            store.SetCurrentBuildId(currentBuildId);
            return store;
        }

        [Fact]
        public void StaleSeed_KeepsServingAMysticForgeRecipeItAlreadyHolds()
        {
            var store = SeedStore(
                new Dictionary<int, RawRecipe> { { GiftOfRaysRecipe, GiftOfRaysRawRecipe() } },
                new Dictionary<int, IReadOnlyList<int>>
                {
                    { GiftOfRays, new List<int> { GiftOfRaysRecipe } },
                },
                SeedBuild, CurrentBuild);

            Assert.True(store.SeedMayBeStale);
            Assert.Equal(new[] { GiftOfRaysRecipe }, store.TryGetSearch(GiftOfRays));
            Assert.NotNull(store.TryGetRecipe(GiftOfRaysRecipe));
        }

        [Fact]
        public async Task StaleSeed_GiftOfRaysStillCraftsWithoutTheApiKnowingTheRecipe()
        {
            // The live API cannot serve a Mystic Forge recipe at all
            // (/v2/recipes has no negative ids), so anything that pushed
            // this item onto the API path would produce exactly the
            // reported UNKNOWN.
            var seed = SeedStore(
                new Dictionary<int, RawRecipe> { { GiftOfRaysRecipe, GiftOfRaysRawRecipe() } },
                new Dictionary<int, IReadOnlyList<int>>
                {
                    { GiftOfRays, new List<int> { GiftOfRaysRecipe } },
                },
                SeedBuild, CurrentBuild);

            using (var temp = new TempDirectory())
            {
                var overlay = new OverlayRecipeCacheStore(temp.Path);
                overlay.Load();
                var cacheStore = new CompositeRecipeCacheStore(seed, overlay);

                var api = new InMemoryRecipeApiClient();
                api.Return404For.Add(GiftOfRaysRecipe);

                var itemApi = new InMemoryItemApiClient();
                itemApi.AddItem(GiftOfRays, "Gift of Rays", "gift.png");

                var pipeline = new CraftingPlanPipeline(
                    new RecipeService(api, cacheStore: cacheStore),
                    new TradingPostService(new InMemoryPriceApiClient()),
                    new PlanSolver(),
                    new ItemMetadataService(itemApi),
                    reducer: new InventoryReducer());

                var result = await pipeline.GenerateStructuredAsync(
                    GiftOfRays, 1, null, CancellationToken.None);

                Assert.Equal(CraftingDecision.Craft, result.CraftingTree.Decision);
                Assert.Equal(4, result.CraftingTree.Children.Count);
            }
        }

        /// <summary>
        /// The whole plan reported in game - Endless Summer
        /// (107022), the parent the reported module_log and
        /// persisted plan.json name - run through the REAL shipped seed
        /// against a build the seed does not match.
        /// <para>
        /// This is what the report's four items actually are: Gift of Rays
        /// has a wiki Mystic Forge recipe in the seed and crafts; the other
        /// three carry an EMPTY search row (the seeder's "the API knows no
        /// recipe") and no forge recipe anywhere, because the wiki says
        /// they are bought or awarded, never crafted. Their UNKNOWN is
        /// correct, so the fix is the seeded acquisition hint that says
        /// where they come from, asserted here through the real hint file
        /// and the real CraftingTreeBuilder path.
        /// </para>
        /// </summary>
        [Fact]
        public async Task RealShippedSeed_EndlessSummerGifts_CraftOrCarryTheirAcquisitionHint()
        {
            const int EndlessSummer = 107022;
            const int GiftOfTheSurvivors = 106712;
            const int GiftOfThePeople = 105804;
            const int GiftOfTheHylek = 106986;

            var seed = LoadShippedSeed(out int seedBuildId);
            seed.SetCurrentBuildId(seedBuildId + 275);
            Assert.True(seed.SeedMayBeStale);

            IReadOnlyDictionary<int, AcquisitionHint> hints;
            using (var hintStream = File.OpenRead(
                RepoFileLocator.FindRepoFile("ref/acquisition_hints_seed.json")))
            {
                hints = AcquisitionHintService.Load(hintStream);
            }

            using (var temp = new TempDirectory())
            {
                var overlay = new OverlayRecipeCacheStore(temp.Path);
                overlay.Load();

                var pipeline = new CraftingPlanPipeline(
                    new RecipeService(new InMemoryRecipeApiClient(),
                        cacheStore: new CompositeRecipeCacheStore(seed, overlay)),
                    new TradingPostService(new InMemoryPriceApiClient()),
                    new PlanSolver(),
                    new ItemMetadataService(new InMemoryItemApiClient()),
                    reducer: new InventoryReducer(),
                    acquisitionHints: hints);

                var result = await pipeline.GenerateStructuredAsync(
                    EndlessSummer, 1, null, CancellationToken.None);

                var children = result.CraftingTree.Children
                    .ToDictionary(c => c.ItemId, c => c);

                Assert.Equal(CraftingDecision.Craft, children[GiftOfRays].Decision);

                // The shipped forge block renumbers on every
                // tools/MysticForgeSeeder run, so what matters is that the
                // solver picked a recipe the seed itself lists for Gift of
                // Rays, not which id that recipe happens to carry today.
                Assert.NotNull(children[GiftOfRays].RecipeId);
                Assert.Contains(
                    children[GiftOfRays].RecipeId.Value,
                    seed.TryGetSearch(GiftOfRays));

                foreach (int giftId in new[] { GiftOfTheSurvivors, GiftOfThePeople, GiftOfTheHylek })
                {
                    Assert.Equal(CraftingDecision.Unknown, children[giftId].Decision);
                    Assert.False(string.IsNullOrEmpty(children[giftId].AcquisitionHint));
                    Assert.False(string.IsNullOrEmpty(children[giftId].AcquisitionBadge));
                }
            }
        }

        private static SeededRecipeCacheStore LoadShippedSeed(out int seedBuildId)
        {
            string searchPath = RepoFileLocator.FindRepoFile("ref/recipe_search_seed.json");
            string recipesPath = RepoFileLocator.FindRepoFile("ref/recipes_seed.json");
            string manifestPath = RepoFileLocator.FindRepoFile("ref/recipe_seed_manifest.json");
            Assert.NotNull(searchPath);
            Assert.NotNull(recipesPath);
            Assert.NotNull(manifestPath);

            var seed = new SeededRecipeCacheStore();
            using (var searchStream = File.OpenRead(searchPath))
            using (var recipesStream = File.OpenRead(recipesPath))
            {
                seed.Load(searchStream, recipesStream);
            }

            using (var manifestStream = File.OpenRead(manifestPath))
            {
                seed.LoadManifest(manifestStream);
            }

            seedBuildId = seed.SeedBuildId.Value;
            return seed;
        }

        [Fact]
        public async Task RealShippedSeed_ResolvesGiftOfRaysUnderABuildBump()
        {
            // Same run against the REAL shipped files rather than a
            // transcription of them: whatever ref.dat carries is what the
            // module sees, so this is the check that the data itself - not
            // a hand-written stand-in - survives the build mismatch.
            string searchPath = RepoFileLocator.FindRepoFile("ref/recipe_search_seed.json");
            string recipesPath = RepoFileLocator.FindRepoFile("ref/recipes_seed.json");
            string manifestPath = RepoFileLocator.FindRepoFile("ref/recipe_seed_manifest.json");
            Assert.NotNull(searchPath);
            Assert.NotNull(recipesPath);
            Assert.NotNull(manifestPath);

            var seed = new SeededRecipeCacheStore();
            using (var searchStream = File.OpenRead(searchPath))
            using (var recipesStream = File.OpenRead(recipesPath))
            {
                seed.Load(searchStream, recipesStream);
            }

            using (var manifestStream = File.OpenRead(manifestPath))
            {
                seed.LoadManifest(manifestStream);
            }

            seed.SetCurrentBuildId(seed.SeedBuildId.Value + 275);
            Assert.True(seed.SeedMayBeStale);

            using (var temp = new TempDirectory())
            {
                var overlay = new OverlayRecipeCacheStore(temp.Path);
                overlay.Load();
                var cacheStore = new CompositeRecipeCacheStore(seed, overlay);

                var api = new InMemoryRecipeApiClient();
                var itemApi = new InMemoryItemApiClient();
                itemApi.AddItem(GiftOfRays, "Gift of Rays", "gift.png");

                var pipeline = new CraftingPlanPipeline(
                    new RecipeService(api, cacheStore: cacheStore),
                    new TradingPostService(new InMemoryPriceApiClient()),
                    new PlanSolver(),
                    new ItemMetadataService(itemApi),
                    reducer: new InventoryReducer());

                var result = await pipeline.GenerateStructuredAsync(
                    GiftOfRays, 1, null, CancellationToken.None);

                Assert.Equal(CraftingDecision.Craft, result.CraftingTree.Decision);
            }
        }

        private static MysticForgeRecipeData MysticForgeData(params RawRecipe[] recipes)
        {
            // Through the real loader, from the real JSON shape - the file
            // the module ships is the only way these recipes ever exist.
            var sb = new StringBuilder();
            sb.Append("{\"schemaVersion\":1,\"recipes\":[");
            for (int i = 0; i < recipes.Length; i++)
            {
                var r = recipes[i];
                if (i > 0)
                {
                    sb.Append(',');
                }

                sb.Append("{\"id\":").Append(r.Id)
                  .Append(",\"outputItemId\":").Append(r.OutputItemId)
                  .Append(",\"outputItemCount\":").Append(r.OutputItemCount)
                  .Append(",\"ingredients\":[");
                for (int j = 0; j < r.Ingredients.Count; j++)
                {
                    var ing = r.Ingredients[j];
                    if (j > 0)
                    {
                        sb.Append(',');
                    }

                    sb.Append("{\"type\":\"").Append(ing.Type)
                      .Append("\",\"id\":").Append(ing.Id)
                      .Append(",\"count\":").Append(ing.Count).Append('}');
                }

                sb.Append("]}");
            }

            sb.Append("]}");

            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(sb.ToString())))
            {
                return MysticForgeRecipeData.Load(stream);
            }
        }

        private static CraftingPlanPipeline Pipeline(
            IRecipeCacheStore cacheStore, IRecipeApiClient api)
        {
            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(GiftOfRays, "Gift of Rays", "gift.png");

            return new CraftingPlanPipeline(
                new RecipeService(api, cacheStore: cacheStore),
                new TradingPostService(new InMemoryPriceApiClient()),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());
        }

        [Fact]
        public async Task EmptySeedEntry_ShadowsAMysticForgeRecipe_UntilItIsMergedIn()
        {
            // The reachable defect: the seeder writes an EMPTY search row
            // for "the API knows no recipe for this item", and that row is
            // served as a cache HIT - so a Mystic Forge recipe added to
            // ref/mystic_forge_recipes.json after the last seeder run is
            // never consulted, and the item renders UNKNOWN.
            var mf = MysticForgeData(GiftOfRaysRawRecipe());

            var shadowed = SeedStore(
                new Dictionary<int, RawRecipe>(),
                new Dictionary<int, IReadOnlyList<int>>
                {
                    { GiftOfRays, new List<int>() },
                },
                SeedBuild, SeedBuild);

            using (var temp = new TempDirectory())
            {
                var overlay = new OverlayRecipeCacheStore(temp.Path);
                overlay.Load();

                // No API rescue available: the live API has no negative
                // recipe ids and no search hit for a forge-only item.
                var before = await Pipeline(
                    new CompositeRecipeCacheStore(shadowed, overlay),
                    new InMemoryRecipeApiClient())
                    .GenerateStructuredAsync(GiftOfRays, 1, null, CancellationToken.None);

                Assert.Equal(CraftingDecision.Unknown, before.CraftingTree.Decision);
            }

            var merged = SeedStore(
                new Dictionary<int, RawRecipe>(),
                new Dictionary<int, IReadOnlyList<int>>
                {
                    { GiftOfRays, new List<int>() },
                },
                SeedBuild, SeedBuild);
            merged.MergeMysticForgeRecipes(mf);

            using (var temp = new TempDirectory())
            {
                var overlay = new OverlayRecipeCacheStore(temp.Path);
                overlay.Load();

                var after = await Pipeline(
                    new CompositeRecipeCacheStore(merged, overlay),
                    new InMemoryRecipeApiClient())
                    .GenerateStructuredAsync(GiftOfRays, 1, null, CancellationToken.None);

                Assert.Equal(CraftingDecision.Craft, after.CraftingTree.Decision);
                Assert.Equal(4, after.CraftingTree.Children.Count);
            }
        }

        [Fact]
        public void MergedMysticForgeRecipes_AreBuildIndependent()
        {
            // The build id decides whether an EMPTY row falls back to the
            // API. A merged MF row is not empty, so the answer is the same
            // on both sides of a build bump - wiki data has no build.
            var mf = MysticForgeData(GiftOfRaysRawRecipe());

            foreach (int currentBuild in new[] { SeedBuild, CurrentBuild })
            {
                var store = SeedStore(
                    new Dictionary<int, RawRecipe>(),
                    new Dictionary<int, IReadOnlyList<int>>
                    {
                        { GiftOfRays, new List<int>() },
                    },
                    SeedBuild, currentBuild);
                store.MergeMysticForgeRecipes(mf);

                Assert.Equal(new[] { GiftOfRaysRecipe }, store.TryGetSearch(GiftOfRays));
                Assert.NotNull(store.TryGetRecipe(GiftOfRaysRecipe));
            }
        }

        [Fact]
        public void Merge_KeepsApiRecipesFirst_AndIsIdempotent()
        {
            // An item can have both a real crafting recipe and a forge
            // recipe; the merge must offer both, API first (the order
            // CompositeRecipeApiClient already uses), and re-running it
            // must not duplicate anything.
            var mf = MysticForgeData(GiftOfRaysRawRecipe());
            var store = SeedStore(
                new Dictionary<int, RawRecipe>(),
                new Dictionary<int, IReadOnlyList<int>>
                {
                    { GiftOfRays, new List<int> { 4242 } },
                },
                SeedBuild, SeedBuild);

            store.MergeMysticForgeRecipes(mf);
            store.MergeMysticForgeRecipes(mf);

            Assert.Equal(new[] { 4242, GiftOfRaysRecipe }, store.TryGetSearch(GiftOfRays));
        }

        [Fact]
        public void Merge_ToleratesNoMysticForgeDataAtAll()
        {
            // RecipeClientFactory hands over MysticForgeRecipeData.Empty
            // when the file is missing or unreadable; the seed must be
            // untouched rather than throwing at startup.
            var store = SeedStore(
                new Dictionary<int, RawRecipe> { { GiftOfRaysRecipe, GiftOfRaysRawRecipe() } },
                new Dictionary<int, IReadOnlyList<int>>
                {
                    { GiftOfRays, new List<int> { GiftOfRaysRecipe } },
                },
                SeedBuild, SeedBuild);

            store.MergeMysticForgeRecipes(null);
            store.MergeMysticForgeRecipes(MysticForgeRecipeData.Empty);

            Assert.Equal(new[] { GiftOfRaysRecipe }, store.TryGetSearch(GiftOfRays));
        }
    }
}
