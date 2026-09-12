using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaimisToolbench.Services;
using TaimisToolbench.Services.Recipes;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RepoFileLocator;

namespace TaimisToolbench.Tests.Services.Recipes
{
    /// <summary>
    /// Where every shipped recipe came from. The seed format records no
    /// source per row, so provenance is pinned through the recipe id space
    /// instead: tools/TaimisToolbench.RecipeSeeder writes positive ids only
    /// from api.guildwars2.com/v2/recipes, and negative ids only by copying
    /// rows out of ref/mystic_forge_recipes.json, which
    /// tools/MysticForgeSeeder scrapes from wiki.guildwars2.com.
    /// <para>
    /// The official API and the wiki are the only sources this module may
    /// ship recipe data from. A row from anywhere else has to break one of
    /// the assertions below: it either sits in the id range neither
    /// generator writes, or it carries a negative id whose row the forge
    /// source file does not match. ShippedSeedManifest closes the remaining
    /// gap, proving the seed file is what a seeder run emitted rather than
    /// something edited afterwards.
    /// </para>
    /// </summary>
    public class SeedProvenanceTests
    {
        // tools/MysticForgeSeeder numbers its block from here downwards.
        private const int GeneratedBlockBase = -100000;

        [Fact]
        public void ShippedForgeFile_UsesOnlyTheGeneratedHalfOfTheIdSpace()
        {
            foreach (var recipe in LoadForgeSource().Values)
            {
                Assert.True(
                    recipe.Id <= GeneratedBlockBase,
                    "forge recipe " + recipe.Id + " sits above the id base "
                        + "tools/MysticForgeSeeder numbers from");
            }
        }

        [Fact]
        public void EveryShippedRecipeId_BelongsToOneOfTheTwoGenerators()
        {
            var recipes = LoadSeed();
            Assert.NotEmpty(recipes);

            var strays = recipes.Keys
                .Where(id => id < 0 && id > GeneratedBlockBase)
                .OrderBy(id => id)
                .ToList();

            Assert.True(
                strays.Count == 0,
                "ref/recipes_seed.json carries recipe id(s) "
                    + string.Join(", ", strays)
                    + " that neither generator writes. A negative id belongs to "
                    + "tools/MysticForgeSeeder and starts at " + GeneratedBlockBase
                    + "; a positive id comes from the official API. A row in "
                    + "between came from somewhere else and must not ship.");
        }

        [Fact]
        public void EveryNegativeIdRecipe_MatchesARowInTheForgeSourceFile()
        {
            var recipes = LoadSeed();
            var forge = LoadForgeSource();

            var negatives = recipes.Values.Where(r => r.Id < 0).ToList();
            Assert.NotEmpty(negatives);

            foreach (var seeded in negatives)
            {
                Assert.True(
                    forge.ContainsKey(seeded.Id),
                    "ref/recipes_seed.json recipe " + seeded.Id
                        + " has no row in ref/mystic_forge_recipes.json, so no "
                        + "source in this repository produces it.");

                var source = forge[seeded.Id];
                Assert.Equal(source.OutputItemId, seeded.OutputItemId);
                Assert.Equal(source.OutputItemCount, seeded.OutputItemCount);
                Assert.Equal(Shape(source), Shape(seeded));
            }
        }

        [Fact]
        public void EverySearchIndexEntry_NamesARecipeTheSeedCarries()
        {
            var recipes = LoadSeed();

            Dictionary<int, IReadOnlyList<int>> searches;
            using (var stream = File.OpenRead(Locate("recipe_search_seed.json")))
            {
                searches = RecipeCacheSerializer.LoadSearchSeed(stream);
            }

            Assert.NotEmpty(searches);

            foreach (var entry in searches)
            {
                foreach (int recipeId in entry.Value)
                {
                    Assert.True(
                        recipes.ContainsKey(recipeId),
                        "ref/recipe_search_seed.json points item " + entry.Key
                            + " at recipe " + recipeId
                            + ", which ref/recipes_seed.json does not carry.");
                }
            }
        }

        // Ingredient type/id/count, ordered, so two rows compare by content
        // rather than by the order the two files happen to list them in.
        private static List<string> Shape(RawRecipe recipe)
        {
            return recipe.Ingredients
                .Select(i => i.Type + ":" + i.Id + "x" + i.Count)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToList();
        }

        private static Dictionary<int, RawRecipe> LoadSeed()
        {
            using (var stream = File.OpenRead(Locate("recipes_seed.json")))
            {
                return RecipeCacheSerializer.LoadRecipeSeed(stream);
            }
        }

        private static Dictionary<int, RawRecipe> LoadForgeSource()
        {
            using (var stream = File.OpenRead(Locate("mystic_forge_recipes.json")))
            {
                return MysticForgeRecipeData.Load(stream)
                    .AllRecipes.ToDictionary(r => r.Id);
            }
        }

        private static string Locate(string fileName)
        {
            string path = FindRepoFile(Path.Combine("ref", fileName));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/" + fileName
                    + " by walking up from the test assembly's directory.");
            return path;
        }
    }
}
