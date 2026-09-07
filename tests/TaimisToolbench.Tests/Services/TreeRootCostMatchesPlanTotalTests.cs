using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The recipe tree's root cost cell and the Total Cost section's
    /// "Actual Cost to Craft" sit inches apart and are computed twice: the
    /// section sums aggregated PlanStep totals, the tree rolls up the
    /// per-node memo. PlanSolver goes to trouble to keep them reconciled and
    /// nothing pinned it, so a change to either summation could have parted
    /// them silently.
    /// <para>
    /// Coin-only plans. A plan carrying currency costs has no single number
    /// to compare: the tree cell is coin and the section reports currency
    /// beside it rather than inside it.
    /// </para>
    /// </summary>
    public class TreeRootCostMatchesPlanTotalTests
    {
        private static void AssertRootMatchesTotal(CraftingPlanResult result)
        {
            Assert.NotNull(result.CraftingTree);
            Assert.Empty(result.Plan.CurrencyCosts);
            Assert.True(result.CraftingTree.SubtreeCost.HasValue);
            Assert.Equal(result.Plan.TotalCoinCost, result.CraftingTree.SubtreeCost.Value);
        }

        [Fact]
        public async Task ASingleCraftStepAgrees()
        {
            var pipeline = PipelineBuilder.SingleRecipeTree(3)
                .WithPrice(1, buyUnitPrice: 500, sellUnitPrice: 900)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .Build();

            var result = await pipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            AssertRootMatchesTotal(result);
        }

        [Fact]
        public async Task ABoughtRootAgrees()
        {
            var pipeline = PipelineBuilder.SingleRecipeTree(3)
                .WithPrice(1, buyUnitPrice: 5, sellUnitPrice: 5)
                .WithPrice(2, buyUnitPrice: 1000, sellUnitPrice: 1000)
                .Build();

            var result = await pipeline.GenerateStructuredAsync(
                1, 4, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Plan.Steps[0].Source);
            AssertRootMatchesTotal(result);
        }

        [Fact]
        public async Task AQuantityThatDoesNotDivideEvenlyStillAgrees()
        {
            // Odd prices and an odd quantity: the aggregate sum and the
            // recursive roll-up round nothing the same way by accident.
            var pipeline = PipelineBuilder.SingleRecipeTree(7)
                .WithPrice(1, buyUnitPrice: 999999, sellUnitPrice: 999999)
                .WithPrice(2, buyUnitPrice: 13, sellUnitPrice: 13)
                .Build();

            var result = await pipeline.GenerateStructuredAsync(
                1, 3, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            AssertRootMatchesTotal(result);
        }

        [Fact]
        public async Task AVendorStepAgrees()
        {
            var builder = PipelineBuilder.Create()
                .WithItem(1, "Vendor Thing", "thing.png");

            CraftingPlanResult result;
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-root-cost-w79",
                        OutputItemId = 1,
                        OutputCount = 2,
                        CostLines = new List<CostLine>
                        {
                            // Currency id 1 is coin - see Gw2Constants.
                            new CostLine { Type = "Currency", Id = 1, Count = 5 },
                        },
                        MerchantName = "Test NPC",
                        Locations = new List<string>(),
                    },
                });

                var pipeline = builder.WithVendorOfferStore(store).Build();
                result = await pipeline.GenerateStructuredAsync(
                    1, 3, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            }

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Plan.Steps[0].Source);
            AssertRootMatchesTotal(result);
        }
    }
}
