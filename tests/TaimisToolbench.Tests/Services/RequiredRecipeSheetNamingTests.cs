using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Required Recipes lists the recipe SHEET the player buys and
    /// consumes, not the item they cannot craft. These run the real
    /// pipeline end to end, so they also prove the sheet's own name and
    /// icon reached the plan: the pipeline has to widen its one bulk
    /// metadata fetch to cover the sheets before the result exists, and a
    /// sheet whose metadata is missing falls back to the crafted item
    /// rather than rendering "Unknown Item".
    /// </summary>
    public class RequiredRecipeSheetNamingTests
    {
        private const int CraftedItemId = 1;
        private const int IngredientItemId = 2;
        private const int RecipeId = 10;
        private const int SheetItemId = 9626;

        private static PipelineBuilder Tree()
        {
            return PipelineBuilder.Create()
                .WithSearchResult(CraftedItemId, RecipeId)
                .WithRecipe(new RawRecipe
                {
                    Id = RecipeId,
                    OutputItemId = CraftedItemId,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = IngredientItemId, Count = 1 },
                    },
                    Disciplines = new List<string> { "Armorsmith" },
                    MinRating = 400,
                    Flags = new List<string> { "LearnedFromItem" },
                })
                .WithPrice(CraftedItemId, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(IngredientItemId, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(CraftedItemId, "Gift of Light", "gift.png")
                .WithItem(IngredientItemId, "Ingredient", "i.png");
        }

        private static async Task<CraftingPlanResult> PlanAsync(PipelineBuilder builder)
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetLearnedRecipes();

            return await builder.WithAccountRecipeClient(accountClient).Build()
                .GenerateStructuredAsync(
                    CraftedItemId, 1, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
        }

        private static PlanRowViewModel RowOf(CraftingPlanResult result)
        {
            var vm = new PlanViewModelBuilder().Build(result);
            var section = vm.Sections.Single(
                s => s.SectionType == PlanSectionType.RequiredRecipes);
            return Assert.Single(section.Rows);
        }

        [Fact]
        public async Task TheRowNamesTheSheetTheSeedMapsTheRecipeTo()
        {
            var result = await PlanAsync(Tree()
                .WithItem(SheetItemId, "Recipe: Gift of Light", "sheet.png", "Rare")
                .WithRecipeSheetItemIds(new Dictionary<int, int> { { RecipeId, SheetItemId } }));

            Assert.Equal(SheetItemId, Assert.Single(result.RequiredRecipes).SheetItemId);

            var row = RowOf(result);
            Assert.Equal("Recipe: Gift of Light", row.Label);
            Assert.Equal("sheet.png", row.IconUrl);
            Assert.Equal("Rare", row.Rarity);

            // The row's own id, so its hover can show the sheet's stat
            // block instead of stopping at an icon and a name.
            Assert.Equal(SheetItemId, row.ItemId);

            // The sheet's name is all the reader sees, so the hover names
            // the item they were actually planning.
            Assert.Equal("Unlocks Gift of Light.", row.HintText);
        }

        [Fact]
        public async Task TheRowOpensTheSheetsOwnWikiPage()
        {
            var result = await PlanAsync(Tree()
                .WithItem(SheetItemId, "Recipe: Gift of Light", "sheet.png", "Rare")
                .WithRecipeSheetItemIds(new Dictionary<int, int> { { RecipeId, SheetItemId } }));

            var row = RowOf(result);
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Recipe:_Gift_of_Light",
                row.WikiTarget.BuildUrl());
            Assert.Equal(IconWikiTarget.HintText, row.WikiTarget.Hint);
        }

        /// <summary>
        /// With no seed entry the module has no sheet to name, so the row
        /// keeps naming the item that needs the recipe. That is the only
        /// subject it has, and its Acquisition section is where the
        /// player's unlock options are listed.
        /// </summary>
        [Fact]
        public async Task WithNoSheetInTheSeedTheRowKeepsNamingTheCraftedItem()
        {
            var result = await PlanAsync(Tree());

            Assert.Equal(0, Assert.Single(result.RequiredRecipes).SheetItemId);

            var row = RowOf(result);
            Assert.Equal("Gift of Light", row.Label);
            Assert.Equal(CraftedItemId, row.ItemId);
            Assert.Null(row.HintText);

            // Still the sheet PAGE, because the recipe is still learned
            // from an item; only the sheet's own identity is unknown.
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Recipe:_Gift_of_Light",
                row.WikiTarget.BuildUrl());
        }

        /// <summary>
        /// A seed entry whose item the metadata fetch did not return - a
        /// failed or partial /v2/items reply - must not render the row as
        /// "Unknown Item".
        /// </summary>
        [Fact]
        public async Task WithNoMetadataForTheSheetTheRowFallsBackToTheCraftedItem()
        {
            var result = await PlanAsync(Tree()
                .WithRecipeSheetItemIds(new Dictionary<int, int> { { RecipeId, SheetItemId } }));

            Assert.Equal(SheetItemId, Assert.Single(result.RequiredRecipes).SheetItemId);

            var row = RowOf(result);
            Assert.Equal("Gift of Light", row.Label);
            Assert.Equal("gift.png", row.IconUrl);
            Assert.Null(row.HintText);
        }

        /// <summary>
        /// A recipe learned any other way has no sheet to buy, so a stale
        /// seed row for its id cannot invent one.
        /// </summary>
        [Fact]
        public async Task ARecipeNotLearnedFromAnItemTakesNoSheet()
        {
            var builder = PipelineBuilder.Create()
                .WithSearchResult(CraftedItemId, RecipeId)
                .WithRecipe(new RawRecipe
                {
                    Id = RecipeId,
                    OutputItemId = CraftedItemId,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = IngredientItemId, Count = 1 },
                    },
                    Disciplines = new List<string> { "Armorsmith" },
                    MinRating = 400,
                })
                .WithPrice(CraftedItemId, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(IngredientItemId, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(CraftedItemId, "Gift of Light", "gift.png")
                .WithItem(IngredientItemId, "Ingredient", "i.png")
                .WithItem(SheetItemId, "Recipe: Gift of Light", "sheet.png", "Rare")
                .WithRecipeSheetItemIds(new Dictionary<int, int> { { RecipeId, SheetItemId } });

            var result = await PlanAsync(builder);

            Assert.Equal(0, Assert.Single(result.RequiredRecipes).SheetItemId);
            Assert.Equal("Gift of Light", RowOf(result).Label);
        }
    }
}
