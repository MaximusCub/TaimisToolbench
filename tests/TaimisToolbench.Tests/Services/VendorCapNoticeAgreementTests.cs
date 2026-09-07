using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The plan's cap notices and the Crafting Ranker's cap notices are one
    /// list. Each surface held its own copy of the filter until now, and
    /// nothing checked that the two agreed. These run one plan result
    /// through both production paths and compare what each names.
    /// </summary>
    public class VendorCapNoticeAgreementTests
    {
        private const int TpLiquidItem = 19976;
        private const int BoundItem = 12345;

        private static CraftingPlanResult ResultWithCaps()
        {
            var result = CraftingPlanResultBuilders.MakeResult(
                totalCoinCost: 500,
                metadata: new Dictionary<int, ItemMetadata>
                {
                    [TpLiquidItem] = new ItemMetadata { Name = "Traded Thing" },
                    [BoundItem] = new ItemMetadata { Name = "Bound Thing" },
                },
                timegatedItems: new List<TimegatedItem>
                {
                    new TimegatedItem
                    {
                        ItemId = TpLiquidItem,
                        CapType = TimegatedCapType.Weekly,
                        CapValue = 10,
                        NeededCount = 16,
                    },
                    new TimegatedItem
                    {
                        ItemId = BoundItem,
                        CapType = TimegatedCapType.Weekly,
                        CapValue = 2,
                        NeededCount = 30,
                    },
                });

            result.SolveContext = new PlanSolveContext
            {
                Prices = new Dictionary<int, ItemPrice>
                {
                    [TpLiquidItem] = new ItemPrice
                    {
                        ItemId = TpLiquidItem,
                        BuyInstant = 100,
                        SellInstant = 120,
                    },
                    [BoundItem] = new ItemPrice { ItemId = BoundItem, BuyInstant = 0, SellInstant = 0 },
                },
            };

            return result;
        }

        private static List<string> PlanNoticeLabels(CraftingPlanResult result)
        {
            var vm = new PlanViewModelBuilder().Build(result);
            return vm.Sections
                .SelectMany(s => s.Rows)
                .Where(r => r.RowType == PlanRowType.TimegatedNotice)
                .Select(r => r.Label)
                .ToList();
        }

        [Fact]
        public void BothSurfacesDropTheCapOnATradedItem()
        {
            var result = ResultWithCaps();

            var metrics = RankerReadinessCalculator.Compute(result, result, null, 0);
            var labels = PlanNoticeLabels(result);

            // The remainder above the cap can be bought with coin, so the
            // cap is a price and not a wait.
            Assert.DoesNotContain(metrics.VendorCappedItems, c => c.ItemId == TpLiquidItem);
            Assert.DoesNotContain(labels, l => l.StartsWith("Traded Thing"));
        }

        [Fact]
        public void BothSurfacesKeepTheCapOnAnUnpricedItem()
        {
            var result = ResultWithCaps();

            var metrics = RankerReadinessCalculator.Compute(result, result, null, 0);
            var labels = PlanNoticeLabels(result);

            Assert.Contains(metrics.VendorCappedItems, c => c.ItemId == BoundItem);
            Assert.Contains(labels, l => l.StartsWith("Bound Thing is timegated"));
        }

        [Fact]
        public void TheTwoSurfacesNameTheSameNumberOfCaps()
        {
            var result = ResultWithCaps();

            var metrics = RankerReadinessCalculator.Compute(result, result, null, 0);

            Assert.Equal(PlanNoticeLabels(result).Count, metrics.VendorCappedItems.Count);
        }

        [Fact]
        public void WithNoPriceDataAtAllBothSurfacesKeepEveryCap()
        {
            var result = ResultWithCaps();
            result.SolveContext = null;

            var metrics = RankerReadinessCalculator.Compute(result, result, null, 0);

            // Never invent liquidity from an absent price table.
            Assert.Equal(2, metrics.VendorCappedItems.Count);
            Assert.Equal(2, PlanNoticeLabels(result).Count);
        }
    }
}
