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

        /// <summary>The legendary staff, and the legendary ring.</summary>
        private const int TheBifrost = 30698;

        private const int EndlessSummer = 107022;

        private static Task<CraftingPlanResult> PlanGiftOfDedicationAsync()
        {
            return PlanAsync(GiftOfDedication);
        }

        private static async Task<CraftingPlanResult> PlanAsync(
            int itemId, params int[] learnedRecipeIds)
        {
            var corpus = RealCorpusFixture.Load();

            // Empty learned set by default: the account has unlocked nothing,
            // which is what makes the Auric Ingot recipe report
            // IsMissing = true.
            var accountRecipes = new InMemoryAccountRecipeClient();
            accountRecipes.SetLearnedRecipes(learnedRecipeIds);

            var pipeline = new CraftingPlanPipeline(
                corpus.NewRecipeService(),
                new TradingPostService(new InMemoryPriceApiClient()),
                new PlanSolver(),
                new ItemMetadataService(new InMemoryItemApiClient()),
                accountRecipeClient: accountRecipes);

            return await pipeline.GenerateStructuredAsync(
                itemId, 1, null, CancellationToken.None);
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

            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);
            Assert.Single(rows);
            Assert.Single(visible);
            Assert.Equal(
                "Required Recipes (showing 1 missing of 1)",
                RequiredRecipesVisibility.BuildHeaderTitle(rows, visible, hideUnlocked: true));
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

            // Both surfaces filter by RequiredRecipesVisibility.
            // HasNoUnlockBarrier, so no listed row is one the Ranker skips
            // and no scored recipe is one the plan hides.
            Assert.DoesNotContain(rows, r => r.StatusTag == "Auto-learned");

            int unlocked = rows.Count(r => RequiredRecipesVisibility.IsUnlocked(r.StatusTag));
            double expected = (double)unlocked / rows.Count;

            var gate = RecipesGate(result);
            Assert.True(gate.Applies);
            Assert.Equal(expected, gate.Completion, 9);
        }

        /// <summary>
        /// The reported disagreement, on the two items it was measured on.
        /// The Ranker's denominator was 6 and 12 while the plan's header read
        /// "showing 6 missing of 15" and "showing 12 missing of 27" - the gap
        /// being exactly the auto-learned recipes, which the game hands the
        /// player with the discipline rating and which neither surface now
        /// counts.
        /// </summary>
        [Theory]
        [InlineData(TheBifrost, 6)]
        [InlineData(EndlessSummer, 12)]
        public async Task ThePlanHeaderAndTheRankerDenominatorAreOneNumber(
            int itemId, int expectedBarrierCount)
        {
            var result = await PlanAsync(itemId);

            // The plan HAS auto-learned recipes, which is what made these two
            // items disagree; the Gift of Dedication above has none.
            Assert.NotEmpty(result.RequiredRecipes.Where(r => r.IsAutoLearned));

            var rows = RecipeRows(result);
            Assert.Equal(expectedBarrierCount, rows.Count);

            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);
            Assert.Equal(
                "Required Recipes (showing " + expectedBarrierCount +
                    " missing of " + expectedBarrierCount + ")",
                RequiredRecipesVisibility.BuildHeaderTitle(rows, visible, hideUnlocked: true));

            // The Ranker's denominator is read off its own arithmetic rather
            // than recounted here: learning exactly one of the recipes the
            // plan lists has to move the cell to 1/N, which pins N.
            var gate = await RecipesGateWithOneLearnedAsync(itemId, result);
            Assert.Equal(1.0 / expectedBarrierCount, gate.Completion, 9);
        }

        /// <summary>
        /// Re-solves with a single recipe learned - the first one the plan
        /// reports missing, so it is one of the rows the header counted.
        /// </summary>
        private static async Task<RankerGateScore> RecipesGateWithOneLearnedAsync(
            int itemId, CraftingPlanResult fromEmpty)
        {
            int learnedRecipeId = fromEmpty.RequiredRecipes
                .First(r => r.IsMissing == true &&
                            !RequiredRecipesVisibility.HasNoUnlockBarrier(r.IsAutoLearned, r.Disciplines))
                .RecipeId;

            return RecipesGate(await PlanAsync(itemId, learnedRecipeId));
        }
    }
}
