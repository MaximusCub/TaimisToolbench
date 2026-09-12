using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TaimisToolbench.Services;
using TaimisToolbench.Services.Recipes;
using TaimisToolbench.Tests.Helpers;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RepoFileLocator;

namespace TaimisToolbench.Tests.Services.Recipes
{
    /// <summary>
    /// Regression: the real
    /// ref/recipes_seed.json / ref/recipe_search_seed.json were
    /// previously never loaded through the production deserialization path
    /// (RecipeCacheSerializer.LoadRecipeSeed/LoadSearchSeed, used at
    /// runtime by SeededRecipeCacheStore.Load via Module.cs) by any
    /// committed test - only by a manual, discarded check. This pins the
    /// real files against silent drift, mirroring
    /// AcquisitionHintServiceTests' FindRepoFile pattern.
    /// </summary>
    public class RecipeCacheSerializerTests
    {
        [Fact]
        public void LoadRecipeSeed_ShippedSeedFile_ParsesEveryRow()
        {
            string path = FindRepoFile(Path.Combine("ref", "recipes_seed.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/recipes_seed.json by walking up from the test assembly's directory.");

            using (var stream = File.OpenRead(path))
            {
                var recipes = RecipeCacheSerializer.LoadRecipeSeed(stream);

                // Row count and bytes, both against what the seeder itself
                // recorded in ref/recipe_seed_manifest.json - see
                // ShippedSeedManifest for why the exact literal that used to
                // sit here (and the changelog of past reseeds above it) was
                // a tripwire nobody could read.
                ShippedSeedManifest.AssertRecipeSeedMatches(
                    "recipes_seed.json", recipes.Count);

                // Secondary sanity band, independent of the manifest: a
                // seeder run that agreed with its own manifest but emitted a
                // tenth of the corpus is still wrong.
                Assert.InRange(recipes.Count, 12000, 30000);

                // Amalgamated Rift Essence (recipe 14025 -> item 100930):
                // the concrete recipe that was invisible to every
                // unversioned recipe call before this fix - unversioned
                // /v2/recipes/14025 404s outright even though the recipe
                // fully exists. Currency ingredients key their id as "id"
                // (not "item_id" - the bug this fix closes).
                Assert.True(recipes.ContainsKey(14025));
                var riftEssence = recipes[14025];
                Assert.Equal(100930, riftEssence.OutputItemId);
                Assert.Equal(4, riftEssence.Ingredients.Count);
                Assert.Equal(3, riftEssence.Ingredients.Count(i => i.Type == "Currency"));
                var ectoIngredient = riftEssence.Ingredients.Single(i => i.Type == "Item");
                Assert.Equal(19721, ectoIngredient.Id);
                Assert.Equal(50, ectoIngredient.Count);

                // The Mystic Forge block must parse too.
                var mysticForge = recipes.Values
                    .Where(r => r.Id < 0 && r.Disciplines.Contains("MysticForge"))
                    .ToList();
                Assert.NotEmpty(mysticForge);

                // The Mystic Clover forge recipe once lost its fractional
                // ExpectedOutputCount (0.31 -> null) to a reseed:
                // MergeMysticForgeRecipes did not copy the field out of
                // ref/mystic_forge_recipes.json. RecipeService.
                // GetRecipeCachedAsync reads the seeded row before
                // MysticForgeRecipeData's own value, so a null here defaults
                // craftsNeeded to OutputItemCount (1) instead of
                // ceil(q/0.31) for every chain that forges Mystic Clovers.
                // Reached through the output item, not the recipe id:
                // tools/MysticForgeSeeder renumbers the whole generated
                // block on every run.
                var clover = Assert.Single(
                    mysticForge.Where(r => r.OutputItemId == 19675
                        && r.ExpectedOutputCount != null));
                Assert.Equal(0.31, clover.ExpectedOutputCount);
            }
        }

        [Fact]
        public void LoadRecipeSeed_ShippedSeedFile_PreservesEveryMysticForgeExpectedOutputCount()
        {
            // A defensive, class-level guard (not just the single Mystic
            // Clover pin above) - for
            // every recipe ref/mystic_forge_recipes.json declares a
            // fractional ExpectedOutputCount for, the shipped
            // ref/recipes_seed.json row for that same id must carry the
            // identical value. Catches the same MergeMysticForgeRecipes
            // field-drop class for ANY future recipe, not just the one
            // instance a manual reseed happened to catch this time.
            string seedPath = FindRepoFile(Path.Combine("ref", "recipes_seed.json"));
            string mfPath = FindRepoFile(Path.Combine("ref", "mystic_forge_recipes.json"));
            Assert.False(string.IsNullOrEmpty(seedPath));
            Assert.False(string.IsNullOrEmpty(mfPath));

            Dictionary<int, RawRecipe> recipes;
            using (var stream = File.OpenRead(seedPath))
            {
                recipes = RecipeCacheSerializer.LoadRecipeSeed(stream);
            }

            var expectedById = new Dictionary<int, double>();
            using (var doc = JsonDocument.Parse(File.ReadAllText(mfPath)))
            {
                foreach (var entry in doc.RootElement.GetProperty("recipes").EnumerateArray())
                {
                    if (entry.TryGetProperty("expectedOutputCount", out var ev) &&
                        ev.ValueKind != JsonValueKind.Null)
                    {
                        expectedById[entry.GetProperty("id").GetInt32()] = ev.GetDouble();
                    }
                }
            }

            // Sanity: the source file must actually declare at least one
            // fractional override, or this test would pass vacuously.
            Assert.NotEmpty(expectedById);

            foreach (var kvp in expectedById)
            {
                Assert.True(
                    recipes.ContainsKey(kvp.Key),
                    $"ref/mystic_forge_recipes.json declares recipe {kvp.Key} but it is missing from the shipped seed.");
                Assert.Equal(kvp.Value, recipes[kvp.Key].ExpectedOutputCount);
            }
        }

        [Fact]
        public void LoadSearchSeed_ShippedSeedFile_MatchesItsManifest()
        {
            string path = FindRepoFile(Path.Combine("ref", "recipe_search_seed.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/recipe_search_seed.json by walking up from the test assembly's directory.");

            using (var stream = File.OpenRead(path))
            {
                var searches = RecipeCacheSerializer.LoadSearchSeed(stream);

                ShippedSeedManifest.AssertRecipeSeedMatches(
                    "recipe_search_seed.json", searches.Count);
                Assert.InRange(searches.Count, 13000, 32000);

                // Amalgamated Rift Essence's search entry (item 100930):
                // previously a STALE NEGATIVE entry ("100930": []) - the
                // seeder had genuinely discovered every other recipe
                // producing this item was invisible, so it correctly (for
                // the data it could see) recorded "no known recipe". Now a
                // real mapping. Note this is populated ONLY because the
                // seeder walks the full /v2/recipes id list, not because
                // live /v2/recipes/search?output=100930 works - that
                // upstream search endpoint has its own, separate index gap
                // and returns empty even versioned (see
                // Gw2RecipeApiClient.SearchByOutputAsync's own doc comment).
                Assert.True(searches.ContainsKey(100930));
                Assert.Contains(14025, searches[100930]);
            }
        }

        // FindRepoFile comes from Helpers/RepoFileLocator.cs.
    }
}
