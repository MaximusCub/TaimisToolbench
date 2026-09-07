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
    /// The "HAVE {used}/{total} NEEDED" badge is drawn on every node of an
    /// item the plan drew owned stock for, including the nodes that got
    /// none of it. Covers DecisionPillPlanner.BuildPillSpecs' new
    /// zero-coverage half and the PlanViewModelBuilder projection that
    /// feeds it.
    /// </summary>
    public class OwnedStockBadgeCoverageTests
    {
        private const string BadgePrefix = "HAVE ";

        private static CraftingTreeNode Node(int itemId, int quantity, int ownedQuantityUsed)
        {
            return new CraftingTreeNode
            {
                ItemId = itemId,
                NodeId = itemId,
                Name = "Test Item",
                Quantity = quantity,
                OwnedQuantityUsed = ownedQuantityUsed,
                Decision = CraftingDecision.BuyFromTp,
                CanBuyTp = true,
            };
        }

        private static string BadgeText(IEnumerable<PillSpec> specs)
        {
            return specs
                .Where(s => s.Kind == PillKind.OwnedInfo &&
                    s.Text != null && s.Text.StartsWith(BadgePrefix))
                .Select(s => s.Text)
                .SingleOrDefault();
        }

        [Fact]
        public void ANodeThatGotNoneOfTheStockStillGetsTheBadge()
        {
            var node = Node(itemId: 19721, quantity: 10, ownedQuantityUsed: 0);

            var specs = DecisionPillPlanner.BuildPillSpecs(
                node, null, null, new HashSet<int> { 19721 });

            Assert.Equal("HAVE 0/10 NEEDED", BadgeText(specs));
        }

        [Fact]
        public void ANodeThatGotSomeOfTheStockKeepsItsOwnCounts()
        {
            var node = Node(itemId: 19721, quantity: 584, ownedQuantityUsed: 16);

            var specs = DecisionPillPlanner.BuildPillSpecs(
                node, null, null, new HashSet<int> { 19721 });

            Assert.Equal("HAVE 16/600 NEEDED", BadgeText(specs));
        }

        [Fact]
        public void AnItemThePlanDrewNoStockFor_GetsNoBadge()
        {
            var node = Node(itemId: 19721, quantity: 10, ownedQuantityUsed: 0);

            var specs = DecisionPillPlanner.BuildPillSpecs(
                node, null, null, new HashSet<int> { 24295 });

            Assert.Null(BadgeText(specs));
        }

        [Fact]
        public void NoSetAtAll_LeavesTheBadgeOffAZeroCoverageNode()
        {
            // A caller with no plan to hand (the harness, a test, a plan
            // restored before this set existed) must not sprout badges.
            var node = Node(itemId: 19721, quantity: 10, ownedQuantityUsed: 0);

            var specs = DecisionPillPlanner.BuildPillSpecs(node);

            Assert.Null(BadgeText(specs));
        }

        [Fact]
        public void ACostComponentLeafNeverGetsTheBadge()
        {
            // A component leaf's ownership does not reduce its line's
            // cost, so it keeps the subdued OWN badge and must never show
            // HAVE - see DecisionPillPlanner's cost-component branch.
            var node = Node(itemId: 19721, quantity: 4, ownedQuantityUsed: 0);
            node.Decision = CraftingDecision.BuyFromVendor;
            node.IsCostComponent = true;
            node.SubtreeCost = 100;
            node.ComponentOwnedQuantity = 3;

            var specs = DecisionPillPlanner.BuildPillSpecs(
                node, null, null, new HashSet<int> { 19721 });

            Assert.Null(BadgeText(specs));
            Assert.Equal("OWN 3", Assert.Single(specs).Text);
        }

        [Fact]
        public void AFullyCoveredNodeKeepsThePlainHavePill()
        {
            var node = Node(itemId: 19721, quantity: 0, ownedQuantityUsed: 12);
            node.Decision = CraftingDecision.Have;

            var specs = DecisionPillPlanner.BuildPillSpecs(
                node, null, null, new HashSet<int> { 19721 });

            Assert.Equal("HAVE", Assert.Single(specs).Text);
        }

        [Fact]
        public async Task TwoNodesOfOneItem_BothCarryTheBadge_WhenOnlyTheFirstGotStock()
        {
            // Item 4 is needed twice: 5 under item 2 and 10 under item 3.
            // The account holds 3, and the reducer gives all 3 to the node
            // it reaches first. The second node is the one the report was
            // about - it got none, and used to show no badge at all.
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
                        new RawIngredient { Type = "Item", Id = 3, Count = 1 },
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
                        new RawIngredient { Type = "Item", Id = 4, Count = 5 },
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
                        new RawIngredient { Type = "Item", Id = 4, Count = 10 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 40000, sellUnitPrice: 90000)
                .WithPrice(2, buyUnitPrice: 4000, sellUnitPrice: 9000)
                .WithPrice(3, buyUnitPrice: 4000, sellUnitPrice: 9000)
                .WithPrice(4, buyUnitPrice: 10, sellUnitPrice: 20)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Left", "l.png")
                .WithItem(3, "Right", "r.png")
                .WithItem(4, "Shared", "s.png")
                .WithInventoryReducer()
                .Build();

            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry
                    {
                        ItemId = 4,
                        Count = 3,
                        Source = AccountItemIndex.SourceMaterialStorage,
                    },
                },
            };

            var result = await pipeline.GenerateStructuredAsync(
                1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            var left = result.CraftingTree.Children.Single(c => c.ItemId == 2);
            var right = result.CraftingTree.Children.Single(c => c.ItemId == 3);
            var covered = left.Children.Single(c => c.ItemId == 4);
            var uncovered = right.Children.Single(c => c.ItemId == 4);
            Assert.Equal(3, covered.OwnedQuantityUsed);
            Assert.Equal(0, uncovered.OwnedQuantityUsed);

            var vm = new PlanViewModelBuilder().Build(result);
            Assert.Contains(4, vm.OwnedStockDrawnItemIds);

            Assert.Equal("HAVE 3/5 NEEDED", BadgeText(DecisionPillPlanner.BuildPillSpecs(
                covered, vm.CurrencyPlanTotals, vm.OwnedCurrencyAmounts, vm.OwnedStockDrawnItemIds)));
            Assert.Equal("HAVE 0/10 NEEDED", BadgeText(DecisionPillPlanner.BuildPillSpecs(
                uncovered, vm.CurrencyPlanTotals, vm.OwnedCurrencyAmounts, vm.OwnedStockDrawnItemIds)));

            // The items the plan drew no stock for are untouched.
            Assert.Null(BadgeText(DecisionPillPlanner.BuildPillSpecs(
                left, vm.CurrencyPlanTotals, vm.OwnedCurrencyAmounts, vm.OwnedStockDrawnItemIds)));
        }

        [Fact]
        public async Task APlanThatDrewNoOwnedStock_HasNoItemIdSet()
        {
            var pipeline = PipelineBuilder.Create()
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
                .WithPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png")
                .WithInventoryReducer()
                .Build();

            var result = await pipeline.GenerateStructuredAsync(
                1, 1, new AccountSnapshot { Items = new List<SnapshotItemEntry>() },
                CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            var vm = new PlanViewModelBuilder().Build(result);

            Assert.Null(vm.OwnedStockDrawnItemIds);
        }
    }
}
