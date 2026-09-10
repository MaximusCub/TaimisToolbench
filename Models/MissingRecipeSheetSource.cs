using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// One vendor offer for the recipe sheet that unlocks a recipe this
    /// plan needs and the account has not learned - see
    /// Services/MissingRecipeSheetSourceCalculator, the sole producer.
    /// Cosmetic display data only, same "advisory, never fed back into a
    /// decision or total" contract as RecipeSheetSavingsOpportunity's own
    /// doc comment. PlanViewModelBuilder.BuildNotesSection is the only
    /// consumer.
    /// <para>
    /// Distinct from RecipeSheetSavingsOpportunity, which compares a
    /// bought item's price against crafting it. This states where to buy a
    /// sheet and what it costs, whatever the plan decided about the item.
    /// </para>
    /// </summary>
    internal class MissingRecipeSheetSource
    {
        /// <summary>The unlearned recipe, from RequiredRecipe.RecipeId.</summary>
        public int RecipeId { get; set; }

        /// <summary>The recipe sheet that unlocks RecipeId.</summary>
        public int SheetItemId { get; set; }

        /// <summary>Merchant selling the offer this record describes.</summary>
        public string MerchantName { get; set; }

        /// <summary>
        /// How many further merchants sell the sheet on the SAME cost
        /// lines. 0 when MerchantName is the only one at that price. 281
        /// of the 1,788 sheets in ref/recipe_sheet_items.json have offers
        /// that disagree on cost (MEASURED 2026-09-07), so a count over
        /// every offer would attach the wrong price to some of them.
        /// </summary>
        public int OtherMerchantCount { get; set; }

        /// <summary>
        /// The chosen offer's cost lines, minus the raw coin line that
        /// CoinCost carries. Null when the offer is pure coin.
        /// </summary>
        public List<CostLine> NonCoinCostLines { get; set; }

        /// <summary>
        /// The offer's raw coin line, or null when it has none. Kept apart
        /// from NonCoinCostLines so the note can hand this number to the
        /// row's CoinValue and let the view draw coin icons.
        /// </summary>
        public long? CoinCost { get; set; }
    }
}
