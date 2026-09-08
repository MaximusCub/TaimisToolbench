using System.Collections.Generic;
using System.IO;
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
    /// A Crafting Ranker gate cell may print a percentage only for a barrier
    /// the module actually measured. Two other outcomes exist and neither may
    /// read as complete: "n/a" for a barrier the item does not have, and a
    /// dash for one it has that the account data could not answer.
    /// <para>
    /// Driven over the SHIPPED corpus - ref/recipes_seed.json,
    /// ref/mystic_forge_recipes.json and ref/vendor_offers.json - through the
    /// production RecipeService, VendorOfferLoader, CraftingPlanPipeline,
    /// PlanSolver, PlanResultBuilder and RankerReadinessCalculator.
    /// </para>
    /// <para>
    /// Views/RankerTabContent.cs draws these cells and is Blish-bound, so
    /// nothing here covers the drawing. Only the text and the bar fraction it
    /// is handed.
    /// </para>
    /// </summary>
    public class RankerGateStatusTests
    {
        /// <summary>The legendary staff. 15 required recipes carry an unlock, 9 do not.</summary>
        private const int TheBifrost = 30698;

        /// <summary>The legendary ring. Its plan pays a vendor in items as well as coin.</summary>
        private const int EndlessSummer = 107022;

        /// <summary>
        /// The reported fault. An API key without the recipes permission
        /// leaves every IsMissing null, so the Recipes gate counted nothing -
        /// and an uncounted gate used to print 100% and paint a full bar for
        /// a set of unlocks the module had never looked at.
        /// </summary>
        [Fact]
        public async Task WithoutTheRecipesPermission_TheRecipesCellIsADashRatherThan100Percent()
        {
            var result = await PlanAsync(TheBifrost, hasRecipePermission: false);

            // The recipes are there; only the account answer is missing.
            Assert.NotEmpty(result.RequiredRecipes);
            Assert.Contains(
                result.RequiredRecipes,
                r => !RequiredRecipesVisibility.HasNoUnlockBarrier(r.IsAutoLearned, r.Disciplines));
            Assert.All(
                result.RequiredRecipes.Where(
                    r => !RequiredRecipesVisibility.HasNoUnlockBarrier(r.IsAutoLearned, r.Disciplines)),
                r => Assert.Null(r.IsMissing));

            var gate = GateOf(result, RankerGate.Recipes);

            Assert.False(gate.Applies);
            Assert.Equal(RankerReadinessCalculator.DashText, RankerReadinessCalculator.FormatGate(gate));
            Assert.Equal(0.0, RankerReadinessCalculator.GateBarFraction(gate));
        }

        /// <summary>
        /// The same account gap, one level up: a row may not certify itself
        /// finished while a barrier it has is unknown. The headline is capped
        /// below 100 rather than blended out of existence.
        /// </summary>
        [Fact]
        public async Task AnUnmeasuredGate_KeepsTheHeadlineOffOneHundred()
        {
            var result = await PlanAsync(TheBifrost, hasRecipePermission: false);

            // Solved against nothing owned, so every barrier is at its
            // from-scratch worst and the headline is nowhere near 100 anyway.
            // What this pins is the rule, read off the gate itself.
            var recipes = GateOf(result, RankerGate.Recipes);
            Assert.False(recipes.Applies);

            var metrics = RankerReadinessCalculator.Compute(result, result, null, 0);
            Assert.NotEqual("100%", RankerReadinessCalculator.FormatReadiness(metrics));
        }

        /// <summary>
        /// The second account gap. A plan solved with no snapshot has no
        /// character disciplines, so the module cannot say whether anyone can
        /// craft the thing - and the cell must not say it can.
        /// </summary>
        [Fact]
        public async Task WithNoSnapshot_TheDisciplinesCellIsADashRatherThan100Percent()
        {
            var result = await PlanAsync(TheBifrost, hasRecipePermission: true);

            Assert.NotEmpty(result.RequiredDisciplines);
            Assert.Null(result.CharacterDisciplines);

            var gate = GateOf(result, RankerGate.Disciplines);

            Assert.False(gate.Applies);
            Assert.Equal(RankerReadinessCalculator.DashText, RankerReadinessCalculator.FormatGate(gate));
            Assert.Equal(0.0, RankerReadinessCalculator.GateBarFraction(gate));
        }

        /// <summary>
        /// The other half of the rule. A barrier the item genuinely does not
        /// have is not a barrier the module failed to read, so it reads
        /// "n/a" rather than a dash - and still paints no bar, because a full
        /// one is a completion claim in the other medium.
        /// </summary>
        [Fact]
        public async Task ABarrierTheItemDoesNotHave_ReadsNotApplicable()
        {
            var result = await PlanAsync(TheBifrost, hasRecipePermission: true);

            // Nothing in this plan is a once-per-day craft: the cooldown seed
            // is not wired into this pipeline, which is a shipped-data
            // absence rather than an account one.
            var gate = GateOf(result, RankerGate.TimeGates);

            Assert.False(gate.Applies);
            Assert.Equal(
                RankerReadinessCalculator.NotApplicableText,
                RankerReadinessCalculator.FormatGate(gate));
            Assert.Equal(0.0, RankerReadinessCalculator.GateBarFraction(gate));
        }

        /// <summary>
        /// The barter gate. Endless Summer pays a vendor in items that carry
        /// no Trading Post price, so CraftingPlan.BarterItemCosts keeps them
        /// out of TotalCoinCost - and before this gate existed no gate scored
        /// them, so the plan was ranked as though that cost was not there.
        /// </summary>
        [Fact]
        public async Task AVendorBillPaidInItems_IsScoredRatherThanIgnored()
        {
            var result = await PlanWithVendorOffersAsync(EndlessSummer);

            Assert.NotEmpty(result.Plan.BarterItemCosts);

            var gate = GateOf(result, RankerGate.BarterItems);

            Assert.True(gate.Applies);
            Assert.Equal(RankerReadinessWeights.BarterItems, gate.Weight);

            // Solved against nothing owned, so none of the bill is cleared.
            Assert.Equal(0.0, gate.Completion, 9);
            Assert.Equal("0%", RankerReadinessCalculator.FormatGate(gate));

            // Every barter line the plan owes is named on the row, so the
            // figure above is explainable rather than asserted.
            Assert.Equal(
                result.Plan.BarterItemCosts.Select(b => b.ItemId).OrderBy(id => id).ToList(),
                MetricsOf(result).BarterItemShortfalls.Select(s => s.ItemId).OrderBy(id => id).ToList());
        }

        /// <summary>
        /// The gate is real arithmetic, not a flag: owning the whole bill
        /// clears it, and owning none of it does not.
        /// </summary>
        [Fact]
        public async Task OwningTheBarterItemsClearsTheGate()
        {
            var result = await PlanWithVendorOffersAsync(EndlessSummer);
            var owed = result.Plan.BarterItemCosts;
            Assert.NotEmpty(owed);

            var held = owed.ToDictionary(b => b.ItemId, b => (int)b.Amount);
            var stocked = CloneWithHoldings(result, held);

            var before = GateOf(result, RankerGate.BarterItems);
            var after = RankerReadinessCalculator
                .Compute(result, stocked, null, 0)
                .Gates.Single(g => g.Gate == RankerGate.BarterItems);

            Assert.Equal(0.0, before.Completion, 9);
            Assert.Equal(1.0, after.Completion, 9);
            Assert.Equal("100%", RankerReadinessCalculator.FormatGate(after));
        }

        /// <summary>
        /// A plan that pays no barter item must be untouched by the gate.
        /// The gate is the whole change, so an item with no such bill has to
        /// score exactly what it scored before the gate existed.
        /// </summary>
        [Fact]
        public async Task APlanWithNoBarterBill_ReadsNotApplicableAndDoesNotJoinTheBlend()
        {
            var result = await PlanAsync(TheBifrost, hasRecipePermission: true);

            Assert.Empty(result.Plan.BarterItemCosts);

            var gate = GateOf(result, RankerGate.BarterItems);

            Assert.False(gate.Applies);
            Assert.Equal(
                RankerReadinessCalculator.NotApplicableText,
                RankerReadinessCalculator.FormatGate(gate));
        }

        private static RankerRowMetrics MetricsOf(CraftingPlanResult result)
        {
            return RankerReadinessCalculator.Compute(result, result, null, 0);
        }

        private static RankerGateScore GateOf(CraftingPlanResult result, RankerGate gate)
        {
            return MetricsOf(result).Gates.Single(g => g.Gate == gate);
        }

        /// <summary>
        /// The same result with the barter bill declared as owned. Holdings
        /// are display data the solver never nets (see
        /// CraftingPlanResult.OwnedVendorItemAmounts), so this is the only
        /// field a stocked account changes about the plan's barter cost.
        /// </summary>
        private static CraftingPlanResult CloneWithHoldings(
            CraftingPlanResult result, IReadOnlyDictionary<int, int> held)
        {
            return new CraftingPlanResult
            {
                Plan = result.Plan,
                RequiredRecipes = result.RequiredRecipes,
                RequiredDisciplines = result.RequiredDisciplines,
                CharacterDisciplines = result.CharacterDisciplines,
                DailyCooldownItems = result.DailyCooldownItems,
                OwnedVendorItemAmounts = held,
            };
        }

        private static async Task<CraftingPlanResult> PlanAsync(int itemId, bool hasRecipePermission)
        {
            var corpus = RealCorpusFixture.Load();

            var accountRecipes = new InMemoryAccountRecipeClient();
            accountRecipes.SetLearnedRecipes();
            accountRecipes.SetHasPermission(hasRecipePermission);

            var pipeline = new CraftingPlanPipeline(
                corpus.NewRecipeService(),
                new TradingPostService(new InMemoryPriceApiClient()),
                new PlanSolver(),
                new ItemMetadataService(new InMemoryItemApiClient()),
                accountRecipeClient: accountRecipes);

            return await pipeline.GenerateStructuredAsync(
                itemId, 1, null, CancellationToken.None);
        }

        /// <summary>
        /// The barter cases need the shipped offers: a barter cost is a
        /// vendor's Item cost line, and no vendor route exists without them.
        /// </summary>
        private static async Task<CraftingPlanResult> PlanWithVendorOffersAsync(int itemId)
        {
            var corpus = RealCorpusFixture.Load();
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                using (var offers = File.OpenRead(RepoFileLocator.FindRepoFile("ref/vendor_offers.json")))
                {
                    store.LoadBaseline(offers);
                }

                var pipeline = new CraftingPlanPipeline(
                    corpus.NewRecipeService(),
                    new TradingPostService(new InMemoryPriceApiClient()),
                    new PlanSolver(),
                    new ItemMetadataService(new InMemoryItemApiClient()),
                    store,
                    new InventoryReducer());

                return await pipeline.GenerateStructuredAsync(
                    itemId, 1, null, CancellationToken.None,
                    priceBasis: PriceBasis.BuyOrder,
                    ownMaterialsMode: OwnMaterialsMode.Free);
            }
        }
    }
}
