using System.Collections.Generic;
using System.IO;
using System.Text;
using TaimisToolbench.RecipeSeeder;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.RecipeSeeder.Tests
{
    // Program.MergeMysticForgeRecipes folds ref/mystic_forge_recipes.json into
    // the seed the module reads. A forge recipe written short an ingredient
    // costs less there than it does in game, and the solver ranks the cheapest
    // route first, so an unreadable ingredient has to refuse its whole recipe.
    public class MysticForgeMergeTests
    {
        private static Stream Json(string text)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(text));
        }

        private static Dictionary<int, RawRecipe> Merge(string json, out int count)
        {
            var allRecipes = new Dictionary<int, RawRecipe>();
            var searchIndex = new Dictionary<int, List<int>>();

            using (var stream = Json(json))
            {
                Program.MergeMysticForgeRecipes(stream, allRecipes, searchIndex, out count);
            }

            return allRecipes;
        }

        private const string FourIngredientRecipe = @"{""recipes"":[{
            ""id"": -100001,
            ""outputItemId"": 19675,
            ""outputItemCount"": 1,
            ""ingredients"": [
                {""type"":""Item"",""id"":20796,""count"":1},
                {""type"":""Item"",""id"":19721,""count"":1},
                {""type"":""Item"",""id"":20799,""count"":1},
                {""type"":""Item"",""id"":19976,""count"":1}
            ]}]}";

        // The ingredient short of "count" is the one that used to be dropped
        // on its own, leaving a three-ingredient Mystic Clover in the seed.
        private const string RecipeMissingAnIngredientCount = @"{""recipes"":[{
            ""id"": -100001,
            ""outputItemId"": 19675,
            ""outputItemCount"": 1,
            ""ingredients"": [
                {""type"":""Item"",""id"":20796,""count"":1},
                {""type"":""Item"",""id"":19721},
                {""type"":""Item"",""id"":20799,""count"":1},
                {""type"":""Item"",""id"":19976,""count"":1}
            ]}]}";

        [Fact]
        public void AWellFormedRecipe_IsSeededWithEveryIngredient()
        {
            var merged = Merge(FourIngredientRecipe, out int count);

            Assert.Equal(1, count);
            var recipe = Assert.Contains(-100001, merged);
            Assert.Equal(4, recipe.Ingredients.Count);
            Assert.Equal(19675, recipe.OutputItemId);
        }

        [Fact]
        public void AnIngredientMissingItsCount_RefusesTheWholeRecipe()
        {
            var merged = Merge(RecipeMissingAnIngredientCount, out int count);

            Assert.Empty(merged);
            Assert.Equal(0, count);
        }

        [Fact]
        public void ARefusedRecipe_ReachesNeitherTheSeedNorTheSearchIndex()
        {
            var allRecipes = new Dictionary<int, RawRecipe>();
            var searchIndex = new Dictionary<int, List<int>>();

            using (var stream = Json(RecipeMissingAnIngredientCount))
            {
                Program.MergeMysticForgeRecipes(
                    stream, allRecipes, searchIndex, out int count);
                Assert.Equal(0, count);
            }

            Assert.Empty(allRecipes);
            Assert.Empty(searchIndex);
        }

        [Fact]
        public void AGoodRecipeAlongsideABadOne_IsStillSeeded()
        {
            string json = @"{""recipes"":[
                {""id"": -100001, ""outputItemId"": 19675, ""outputItemCount"": 1,
                 ""ingredients"": [{""type"":""Item"",""id"":20796}]},
                {""id"": -100002, ""outputItemId"": 19676, ""outputItemCount"": 2,
                 ""ingredients"": [{""type"":""Item"",""id"":20797,""count"":3}]}
            ]}";

            var merged = Merge(json, out int count);

            Assert.Equal(1, count);
            Assert.DoesNotContain(-100001, merged.Keys);
            var recipe = Assert.Contains(-100002, merged);
            Assert.Equal(3, Assert.Single(recipe.Ingredients).Count);
        }
    }
}
