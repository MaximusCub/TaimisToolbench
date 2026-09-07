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
    /// The Crafting Ranker's Recipes cell and the plan's Required Recipes
    /// header must describe the same set of recipes.
    /// <para>
    /// Driven over the SHIPPED corpus: ref/recipes_seed.json and
    /// ref/mystic_forge_recipes.json, through the production RecipeService,
    /// PlanSolver, PlanResultBuilder, PlanViewModelBuilder and
    /// RankerReadinessCalculator. Gift of Dedication is forged from four
    /// items, one of which is an Auric Ingot, and the Auric Ingot recipe is
    /// learned from a recipe sheet. So the plan needs exactly one recipe the
    /// player can be missing and one Mystic Forge step that has no unlock at
    /// all.
    /// </para>
    /// </summary>
    public class RankerRecipeGateAgreesWithPlanTests
    {
        private const int GiftOfDedication = 78936;
        private const int AuricIngotRecipeId = 10229;

        private static async Task<CraftingPlanResult> PlanGiftOfDedicationAsync()
        {
            var corpus = RealCorpusFixture.Load();

            // Empty learned set: the account has unlocked nothing, which is
            // what makes the Auric Ingot recipe report IsMissing = true.
            var accountRecipes = new InMemoryAccountRecipeClient();
            accountRecipes.SetLearnedRecipes();

            var pipeline = new CraftingPlanPipeline(
                corpus.NewRecipeService(),
                new TradingPostService(new InMemoryPriceApiClient()),
                new PlanSolver(),
                new ItemMetadataService(new InMemoryItemApiClient()),
                accountRecipeClient: accountRecipes);

            return await pipeline.GenerateStructuredAsync(
                GiftOfDedication, 1, null, CancellationToken.None);
        }

        private static List<PlanRowViewModel> RecipeRows(CraftingPlanResult result)
        {
            var section = new PlanViewModelBuilder().Build(result).Sections
                .First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            return section.Rows;
        }

        private static RankerGateScore RecipesGate(CraftingPlanResult result)
        {
            var metrics = RankerReadinessCalculator.Compute(result, result, null, 0);
            return metrics.Gates.First(g => g.Gate == RankerGate.Recipes);
        }

        [Fact]
        public async Task TheCorpusPlanHasOneMissingRecipeAndOneMysticForgeStep()
        {
            var result = await PlanGiftOfDedicationAsync();

            var forgeOnly = result.RequiredRecipes
                .Where(r => r.Disciplines.Count > 0 && r.Disciplines.All(d => d == "MysticForge"))
                .ToList();
            Assert.Single(forgeOnly);

            // The Mystic Forge step reports "not missing" because it has no
            // unlock concept, not because the player learned anything.
            Assert.False(forgeOnly[0].IsMissing);

            var auricIngot = result.RequiredRecipes.Single(r => r.RecipeId == AuricIngotRecipeId);
            Assert.True(auricIngot.IsMissing);
            Assert.False(auricIngot.IsAutoLearned);
        }

        [Fact]
        public async Task ThePlanHeaderReadsOneMissingOfOne()
        {
            var result = await PlanGiftOfDedicationAsync();
            var rows = RecipeRows(result);

            int missing = rows.Count(r => !RequiredRecipesVisibility.IsUnlocked(r.StatusTag));
            Assert.Single(rows);
            Assert.Equal(1, missing);
            Assert.Equal(
                "Required Recipes (showing 1 missing of 1)",
                RequiredRecipesVisibility.BuildHeaderTitle(rows.Count, missing, hideUnlocked: true));
        }

        [Fact]
        public async Task TheRankerRecipesCellReadsZeroPercentForTheSamePlan()
        {
            var result = await PlanGiftOfDedicationAsync();

            // The plan lists one recipe and it is missing, so the Ranker has
            // to say none of the recipes are done. Counting the Mystic Forge
            // step as a known recipe used to make this cell read 50%.
            Assert.Equal("0%", RankerReadinessCalculator.FormatGate(RecipesGate(result)));
        }

        [Fact]
        public async Task TheRankerScoresExactlyTheRecipesThePlanLists()
        {
            var result = await PlanGiftOfDedicationAsync();
            var rows = RecipeRows(result);

            // No recipe in this plan is auto-learned, so every listed row is
            // one the Ranker also scores and the two counts line up exactly.
            // The Ranker drops auto-learned recipes; the plan still lists
            // them, so a plan that has some would not satisfy this equality.
            Assert.DoesNotContain(rows, r => r.StatusTag == "Auto-learned");

            int unlocked = rows.Count(r => RequiredRecipesVisibility.IsUnlocked(r.StatusTag));
            double expected = (double)unlocked / rows.Count;

            var gate = RecipesGate(result);
            Assert.True(gate.Applies);
            Assert.Equal(expected, gate.Completion, 9);
        }
    }
}
