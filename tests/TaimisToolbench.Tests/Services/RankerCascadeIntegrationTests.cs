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
    // The cascade against a REAL CraftingPlanPipeline with a real
    // InventoryReducer and a real PlanSolver - the only place the claims in
    // the design ("consumption is UsedMaterials, not the shopping list";
    // "a bought intermediate never consumes its own ingredients") can
    // actually be proven, because they are properties of the solver's own
    // decisions rather than of any arithmetic the Ranker does.
    public class RankerCascadeIntegrationTests
    {
        private const int Target = 1;
        private const int Ingredient = 2;
        private const int SubIngredient = 3;
        private const string Storage = AccountItemIndex.SourceMaterialStorage;

        /// <summary>Target 1 is crafted from 3x item 2; item 2 is a priced leaf.</summary>
        private static CraftingPlanPipeline BuildFlatPipeline()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(Target, 10);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = Target,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = Ingredient, Count = 3 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });

            var priceApi = new InMemoryPriceApiClient();
            // No price for the target itself, so crafting is the only route
            // and the tree always has the ingredient beneath it.
            priceApi.AddPrice(Ingredient, buyUnitPrice: 100, sellUnitPrice: 200);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(Target, "Target", "t.png");
            itemApi.AddItem(Ingredient, "Ingredient", "i.png");

            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());
        }

        /// <summary>
        /// Target 1 needs 1x item 2; item 2 is craftable from 5x item 3 but is
        /// far cheaper to buy outright, so the solver buys it - and a bought
        /// intermediate never demands its own ingredients.
        /// </summary>
        private static CraftingPlanPipeline BuildBuyTheIntermediatePipeline()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(Target, 10);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = Target,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = Ingredient, Count = 1 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });
            recipeApi.AddSearchResult(Ingredient, 20);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 20,
                OutputItemId = Ingredient,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = SubIngredient, Count = 5 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(Ingredient, buyUnitPrice: 50, sellUnitPrice: 90);
            priceApi.AddPrice(SubIngredient, buyUnitPrice: 100, sellUnitPrice: 150);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(Target, "Target", "t.png");
            itemApi.AddItem(Ingredient, "Ingredient", "i.png");
            itemApi.AddItem(SubIngredient, "Sub ingredient", "s.png");

            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());
        }

        private static AccountSnapshot SnapshotHolding(int itemId, int count, int coin = 0)
        {
            return new AccountSnapshot
            {
                CoinCopper = coin,
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = itemId, Count = count, Source = Storage, Name = "Held" },
                },
                Wallet = new List<SnapshotWalletEntry>(),
            };
        }

        private static Task<CraftingPlanResult> SolveAsync(
            CraftingPlanPipeline pipeline, AccountSnapshot snapshot)
        {
            return pipeline.GenerateStructuredAsync(
                Target, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.BuyOrder,
                ownMaterialsMode: OwnMaterialsMode.Free);
        }

        [Fact]
        public async Task TheSecondSlotOnlySeesWhatTheFirstLeftBehind()
        {
            var pipeline = BuildFlatPipeline();
            var cascade = new RankerPriorityCascade(SnapshotHolding(Ingredient, 3));

            var slotOneAvailability = cascade.CurrentAvailability;
            var slotOne = await SolveAsync(pipeline, slotOneAvailability.Snapshot);
            cascade.Consume(slotOne);

            var slotTwoAvailability = cascade.CurrentAvailability;
            var slotTwo = await SolveAsync(pipeline, slotTwoAvailability.Snapshot);

            // Slot 1 consumed all three; slot 2 has to buy them.
            Assert.Equal(3, slotOne.UsedMaterials.Single(u => u.ItemId == Ingredient).QuantityUsed);
            Assert.Equal(0, slotOne.Plan.TotalCoinCost);
            Assert.Empty(slotTwoAvailability.Snapshot.Items);
            Assert.Equal(300, slotTwo.Plan.TotalCoinCost);
            Assert.True(slotTwo.Plan.TotalCoinCost > slotOne.Plan.TotalCoinCost);
        }

        [Fact]
        public async Task TheCascadeChangesTheScore_AndTheContestedMarkerNamesWhy()
        {
            var pipeline = BuildFlatPipeline();
            var baseline = await SolveAsync(pipeline, null);
            var cascade = new RankerPriorityCascade(SnapshotHolding(Ingredient, 3, coin: 1000));

            var firstAvailability = cascade.CurrentAvailability;
            var first = await SolveAsync(pipeline, firstAvailability.Snapshot);
            var firstMetrics = RankerReadinessCalculator.Compute(baseline, first, firstAvailability, 0);
            cascade.Consume(first);

            var secondAvailability = cascade.CurrentAvailability;
            var second = await SolveAsync(pipeline, secondAvailability.Snapshot);
            var secondMetrics = RankerReadinessCalculator.Compute(baseline, second, secondAvailability, 1);

            Assert.Equal("100%", RankerReadinessCalculator.FormatReadiness(firstMetrics));
            Assert.Equal("0%", RankerReadinessCalculator.FormatReadiness(secondMetrics));

            // Without the cascade both rows would read 100%, which is the bug
            // the whole feature exists to fix.
            Assert.Equal(0, firstMetrics.ContestedItemCount);
            Assert.Equal(1, secondMetrics.ContestedItemCount);

            // Coin drains down the list too, so the second row's affordability
            // is measured after the first row has been paid for.
            Assert.True(firstMetrics.AffordableNow);
            Assert.Equal(1000, firstAvailability.CoinCopper);
            Assert.Equal(1000, secondAvailability.CoinCopper);
        }

        [Fact]
        public async Task CoinDrainsDownThePriorityList()
        {
            var pipeline = BuildFlatPipeline();
            var cascade = new RankerPriorityCascade(SnapshotHolding(Ingredient, 0, coin: 500));

            var first = await SolveAsync(pipeline, cascade.CurrentAvailability.Snapshot);
            Assert.Equal(300, first.Plan.TotalCoinCost);
            cascade.Consume(first);

            var secondAvailability = cascade.CurrentAvailability;
            Assert.Equal(200, secondAvailability.CoinCopper);

            var second = await SolveAsync(pipeline, secondAvailability.Snapshot);
            var metrics = RankerReadinessCalculator.Compute(
                await SolveAsync(pipeline, null), second, secondAvailability, 1);

            Assert.False(metrics.AffordableNow);
            Assert.Equal(100, metrics.ShortfallCoin);
        }

        [Fact]
        public async Task WithNothingOnHand_TheSolverBuysTheIntermediateAndConsumesNoIngredient()
        {
            // The intermediate costs 50 to buy and 500 to craft, so an empty
            // account buys it - and its own ingredients never enter the plan.
            var pipeline = BuildBuyTheIntermediatePipeline();
            var cascade = new RankerPriorityCascade(SnapshotHolding(SubIngredient, 0));

            var result = await SolveAsync(pipeline, cascade.CurrentAvailability.Snapshot);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Plan.Steps.Single(s => s.ItemId == Ingredient).Source);
            Assert.DoesNotContain(result.Plan.Steps, s => s.ItemId == SubIngredient);
            Assert.DoesNotContain(result.UsedMaterials, u => u.ItemId == SubIngredient && u.QuantityUsed > 0);

            cascade.Consume(result);
            Assert.DoesNotContain(SubIngredient, cascade.CurrentAvailability.ClaimedItemIds);
        }

        [Fact]
        public async Task OwnedIngredientsFlipTheIntermediateToCraft_AndTheCascadeTracksWhatWasReallyConsumed()
        {
            // MEASURED against the real pipeline: under OwnMaterialsMode.Free
            // there is no zero-owned decision guide (that is built only for
            // Valued - see CraftingPlanPipeline's useForceBuyPrePass gate), so
            // InventoryReducer's legacy primary-option heuristic lets owned
            // stock discount the craft branch, and the solve that follows picks
            // Craft over a purchase it would otherwise have made.
            //
            // This is precisely why the consumption record cannot be derived
            // from the tree's leaves: what gets consumed depends on a decision
            // that itself depends on what is owned. UsedMaterials is taken
            // after the solve, so it and the plan always agree.
            var pipeline = BuildBuyTheIntermediatePipeline();
            var cascade = new RankerPriorityCascade(SnapshotHolding(SubIngredient, 5));

            var first = await SolveAsync(pipeline, cascade.CurrentAvailability.Snapshot);

            Assert.Equal(AcquisitionSource.Craft, first.Plan.Steps.Single(s => s.ItemId == Ingredient).Source);
            Assert.Equal(5, first.UsedMaterials.Single(u => u.ItemId == SubIngredient).QuantityUsed);
            Assert.Equal(0, first.Plan.TotalCoinCost);

            cascade.Consume(first);

            // The stock is gone, so the next slot down cannot spend it twice.
            var secondAvailability = cascade.CurrentAvailability;
            Assert.Empty(secondAvailability.Snapshot.Items);
            Assert.Contains(SubIngredient, secondAvailability.ClaimedItemIds);

            var second = await SolveAsync(pipeline, secondAvailability.Snapshot);
            Assert.Equal(AcquisitionSource.BuyFromTp, second.Plan.Steps.Single(s => s.ItemId == Ingredient).Source);
            Assert.Equal(50, second.Plan.TotalCoinCost);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(9)]
        public async Task OwnedNeverCostsMoreThanBaselineUnderFreeMode(int held)
        {
            // Pins the Ranker's OwnMaterialsMode.Free contract: under Free,
            // reduction can only remove need, never add it, so readiness can
            // never go backwards as inventory grows. Deliberately NOT asserted
            // for OwnMaterialsMode.Valued, which prices owned materials at
            // their Trading Post opportunity cost and has no such property.
            var pipeline = BuildFlatPipeline();
            var baseline = await SolveAsync(pipeline, null);
            var owned = await SolveAsync(pipeline, SnapshotHolding(Ingredient, held));

            Assert.True(owned.Plan.TotalCoinCost <= baseline.Plan.TotalCoinCost);

            var metrics = RankerReadinessCalculator.Compute(
                baseline, owned, new RankerPriorityCascade(SnapshotHolding(Ingredient, held)).CurrentAvailability, 0);
            Assert.InRange(metrics.Readiness, 0.0, 1.0);
        }

        [Fact]
        public async Task HoldingTheTargetItemItselfChangesNothingForThatRow()
        {
            // The row asks for the target to be made, so a copy already in
            // storage buys it nothing (see PlanRootNodes). Monotonicity
            // still holds, at equality rather than below it.
            var pipeline = BuildFlatPipeline();
            var baseline = await SolveAsync(pipeline, null);
            var owned = await SolveAsync(pipeline, SnapshotHolding(Target, 1));

            Assert.True(owned.Plan.TotalCoinCost <= baseline.Plan.TotalCoinCost);
            Assert.Equal(baseline.Plan.TotalCoinCost, owned.Plan.TotalCoinCost);
            Assert.Equal(300, owned.Plan.TotalCoinCost);
        }

        [Fact]
        public async Task IndependentModeIgnoresPriorRows_WhereTheCascadeDoesNot()
        {
            // The same contention fixture as
            // TheCascadeChangesTheScore_AndTheContestedMarkerNamesWhy, run
            // both ways: three held ingredients cover ONE item. In cascade
            // mode the second row is measured after the first drained them
            // (0%); in independent mode every row is slot 1 against the
            // full account, so both read 100% - "which is closest to done
            // right now?" answered without the queue.
            var pipeline = BuildFlatPipeline();
            var baseline = await SolveAsync(pipeline, null);
            var snapshot = SnapshotHolding(Ingredient, 3, coin: 1000);

            // Cascade: slot 2 sees what slot 1 left behind.
            var cascade = new RankerPriorityCascade(snapshot);
            var firstAvailability = cascade.CurrentAvailability;
            cascade.Consume(await SolveAsync(pipeline, firstAvailability.Snapshot));
            var cascadeSecondAvailability = cascade.CurrentAvailability;
            var cascadeSecond = await SolveAsync(pipeline, cascadeSecondAvailability.Snapshot);
            var cascadeMetrics = RankerReadinessCalculator.Compute(
                baseline, cascadeSecond, cascadeSecondAvailability, 1, RankerMode.Cascade);

            // Independent: the second row gets the SAME full availability
            // slot 1 got - no Consume threaded between rows.
            var untouched = new RankerPriorityCascade(snapshot).CurrentAvailability;
            var independentSecond = await SolveAsync(pipeline, untouched.Snapshot);
            var independentMetrics = RankerReadinessCalculator.Compute(
                baseline, independentSecond, untouched, 1, RankerMode.Independent);

            Assert.Equal("0%", RankerReadinessCalculator.FormatReadiness(cascadeMetrics));
            Assert.Equal("100%", RankerReadinessCalculator.FormatReadiness(independentMetrics));
            Assert.True(independentMetrics.Readiness > cascadeMetrics.Readiness);

            // No prior rows means nothing can be contested and no queued
            // days exist - the row is measured purely on its own.
            Assert.Equal(0, independentMetrics.ContestedItemCount);
            Assert.Equal(0, independentMetrics.ContestedCurrencyCount);
            Assert.Equal(independentMetrics.DaysAlone, independentMetrics.DaysRemaining);
            Assert.Equal(1, cascadeMetrics.ContestedItemCount);
        }

        [Fact]
        public async Task TheBaselineSolveIsIndependentOfTheCascade()
        {
            // Why the cascade adds ZERO extra solves: the baseline is
            // inventory-independent by construction, so the cascade only ever
            // changes which snapshot the OWNED call receives.
            var pipeline = BuildFlatPipeline();

            var first = await SolveAsync(pipeline, null);
            var second = await SolveAsync(pipeline, null);

            Assert.Equal(first.Plan.TotalCoinCost, second.Plan.TotalCoinCost);
            Assert.Equal(300, first.Plan.TotalCoinCost);
        }
    }
}
