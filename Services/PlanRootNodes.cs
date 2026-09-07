using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The nodes of a recipe tree that stand for items the user actually
    /// typed into the planner.
    /// <para>
    /// A single-item plan has one, the tree itself. A batch of 2+ items is
    /// wrapped under the synthetic multi-item root
    /// (RecipeService.BuildMultiItemTreeAsync), and its N requested items
    /// are that wrapper recipe's ingredients; the wrapper node is never one
    /// of them. Two places need the same answer - InventoryReducer, which
    /// must not spend account stock against these nodes, and
    /// CraftingPlanPipeline, which builds one CraftingTreeNode per root -
    /// so the shape is described once here.
    /// </para>
    /// </summary>
    internal static class PlanRootNodes
    {
        public static IReadOnlyList<RecipeNode> Of(RecipeNode tree)
        {
            if (tree == null)
            {
                return Array.Empty<RecipeNode>();
            }

            if (tree.Id != Gw2Constants.MultiItemWrapperItemId)
            {
                return new[] { tree };
            }

            var wrapperRecipe = tree.Recipes.FirstOrDefault(
                r => r.RecipeId == Gw2Constants.MultiItemWrapperRecipeId);
            if (wrapperRecipe == null)
            {
                return Array.Empty<RecipeNode>();
            }

            return wrapperRecipe.Ingredients;
        }
    }
}
