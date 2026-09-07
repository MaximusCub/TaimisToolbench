using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    internal class RequiredRecipe
    {
        public int RecipeId { get; set; }

        public int OutputItemId { get; set; }

        public bool IsAutoLearned { get; set; }

        // True when this
        // recipe's unlock method is a consumable recipe sheet
        // (RecipeOption.Flags contains "LearnedFromItem" - see
        // PlanResultBuilder). Drives which wiki page the Required Recipes
        // Missing! row links to (WikiLinkBuilder.BuildRequiredRecipeUrl):
        // the recipe's own "Recipe: <name>" sheet page when true, the
        // output item's page + "#Acquisition" anchor otherwise.
        public bool IsLearnedFromItem { get; set; }

        public int MinRating { get; set; }

        public List<string> Disciplines { get; set; } = new List<string>();

        public bool? IsMissing { get; set; }

        // The consumable recipe sheet that unlocks this recipe, from
        // ref/recipe_sheet_items.json. 0 when the module knows of no
        // sheet, which covers a recipe learned by discovery or from a
        // trainer as well as one the seed does not carry. The Required
        // Recipes row names the SHEET when this is set: a sheet is the
        // thing the player buys and consumes, and the crafted item is not
        // sold as a recipe. Saved plans written before this field existed
        // deserialize it as 0 and keep naming the crafted item.
        public int SheetItemId { get; set; }
    }
}
