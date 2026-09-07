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
    /// The item a plan was asked for is planned as if the account owns
    /// none of it, however many are already in the bank. Everything below
    /// it in the tree still gets the ordinary owned-stock reduction.
    /// </summary>
    public class PlanRootOwnedStockTests
    {
        private static PipelineBuilder RootAndIngredient()
        {
            return PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 5 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    Flags = new List<string> { "AutoLearned" },
                })
                .WithPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png")
                .WithInventoryReducer();
        }

        private static SnapshotItemEntry Owned(int itemId, int count)
        {
            return new SnapshotItemEntry
            {
                ItemId = itemId,
                Count = count,
                Source = AccountItemIndex.SourceMaterialStorage,
            };
        }

        [Fact]
        public async Task OwningTheRequestedItem_StillPlansToMakeIt()
        {
            var pipeline = RootAndIngredient().Build();
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry> { Owned(1, 1) },
            };

            var result = await pipeline.GenerateStructuredAsync(
                1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(1, result.CraftingTree.Quantity);
            Assert.Equal(CraftingDecision.Craft, result.CraftingTree.Decision);
            Assert.Equal(0, result.CraftingTree.OwnedQuantityUsed);
            Assert.DoesNotContain(result.UsedMaterials, u => u.ItemId == 1);

            // 5 x item 2 bought at 100 each, exactly as if nothing were owned.
            Assert.Equal(500, result.Plan.TotalCoinCost);
        }

        [Fact]
        public async Task OwningPartOfTheRequestedQuantity_StillPlansThemAll()
        {
            var pipeline = RootAndIngredient().Build();
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry> { Owned(1, 2) },
            };

            var result = await pipeline.GenerateStructuredAsync(
                1, 3, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(3, result.CraftingTree.Quantity);
            // Three crafts' worth of item 2 at 100 each, not one.
            Assert.Equal(1500, result.Plan.TotalCoinCost);
        }

        [Fact]
        public async Task OwnedIngredientBelowTheRoot_IsSubtracted()
        {
            var pipeline = RootAndIngredient().Build();
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry> { Owned(2, 3) },
            };

            var result = await pipeline.GenerateStructuredAsync(
                1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            var ingredient = result.CraftingTree.Children.Single(c => c.ItemId == 2);
            Assert.Equal(3, ingredient.OwnedQuantityUsed);
            // 2 of the 5 still bought at 100 each.
            Assert.Equal(200, result.Plan.TotalCoinCost);
        }

        [Fact]
        public async Task OwnedRootStock_IsStillAvailableToADeeperNodeOfTheSameItem()
        {
            // Item 1 is both the requested item and, one level down, an
            // ingredient of its own ingredient. The root is planned in
            // full; the deeper occurrence still draws on account stock.
            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                })
                .WithSearchResult(2, 11)
                .WithRecipe(new RawRecipe
                {
                    Id = 11,
                    OutputItemId = 2,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 1, Count = 2 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 4000, sellUnitPrice: 9000)
                .WithPrice(2, buyUnitPrice: 4000, sellUnitPrice: 9000)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Middle", "m.png")
                .WithInventoryReducer()
                .Build();

            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry> { Owned(1, 2) },
            };

            var result = await pipeline.GenerateStructuredAsync(
                1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(1, result.CraftingTree.Quantity);
            Assert.Equal(0, result.CraftingTree.OwnedQuantityUsed);
            var middle = result.CraftingTree.Children.Single(c => c.ItemId == 2);
            var deeperOccurrence = middle.Children.Single(c => c.ItemId == 1);
            Assert.Equal(2, deeperOccurrence.OwnedQuantityUsed);
        }

        [Fact]
        public async Task EveryRootOfABatch_IsPlannedInFull()
        {
            var pipeline = TwoRootBatch();
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry> { Owned(1, 5), Owned(3, 5) },
            };

            var result = await pipeline.GenerateStructuredAsync(
                BatchRequest(), snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(2, result.MultiItemRoots.Count);
            Assert.All(result.MultiItemRoots, r => Assert.Equal(1, r.Quantity));
            Assert.All(result.MultiItemRoots, r => Assert.Equal(CraftingDecision.Craft, r.Decision));
            // 5 of item 2 for the first root and 2 for the second, at 100 each.
            Assert.Equal(700, result.Plan.TotalCoinCost);
        }

        private static CraftingPlanPipeline TwoRootBatch()
        {
            return PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 5 },
                    },
                })
                .WithSearchResult(3, 12)
                .WithRecipe(new RawRecipe
                {
                    Id = 12,
                    OutputItemId = 3,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 2 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithPrice(3, buyUnitPrice: 400, sellUnitPrice: 1000)
                .WithItem(1, "Target A", "a.png")
                .WithItem(2, "Ingredient", "i.png")
                .WithItem(3, "Target B", "b.png")
                .WithInventoryReducer()
                .Build();
        }

        private static IReadOnlyList<PlanRequestItem> BatchRequest()
        {
            return new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 1 },
                new PlanRequestItem { ItemId = 3, Quantity = 1 },
            };
        }
    }
}
