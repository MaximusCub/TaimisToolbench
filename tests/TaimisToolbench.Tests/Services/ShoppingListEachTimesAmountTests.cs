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
    /// The shopping list's Each cell must survive the check a reader can do
    /// by hand: Each times Amount reaches Total, or the cell says how many
    /// units its price buys.
    /// <para>
    /// Both figures used to come from integer division, so a vendor selling
    /// two units for five copper showed an Each of two against a Total of
    /// five. Driven through the real VendorOfferStore, PlanSolver,
    /// VendorBatchSolver and PlanViewModelBuilder.
    /// </para>
    /// </summary>
    public class ShoppingListEachTimesAmountTests
    {
        private const int VendorItem = 1;

        private static async Task<PlanRowViewModel> ShoppingRowAsync(
            int outputCount, long coinCostPerBatch, int quantityWanted)
        {
            var builder = PipelineBuilder.Create()
                .WithItem(VendorItem, "Batched Thing", "thing.png");

            CraftingPlanResult result;
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-batched-w79",
                        OutputItemId = VendorItem,
                        OutputCount = outputCount,
                        CostLines = new List<CostLine>
                        {
                            // Currency id 1 is coin - see Gw2Constants.
                            new CostLine
                            {
                                Type = "Currency",
                                Id = 1,
                                Count = (int)coinCostPerBatch,
                            },
                        },
                        MerchantName = "Test NPC",
                    },
                });

                var pipeline = builder.WithVendorOfferStore(store).Build();
                result = await pipeline.GenerateStructuredAsync(
                    VendorItem, quantityWanted, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
            }

            var section = new PlanViewModelBuilder().Build(result).Sections
                .Single(s => s.SectionType == PlanSectionType.ShoppingList);
            return section.Rows.Single(r => r.RowType == PlanRowType.ShoppingVendor);
        }

        [Fact]
        public async Task AnOfferSellingTwoForFive_StatesThePairRatherThanATruncatedEach()
        {
            // Wanting three units buys two batches: ten copper for four
            // units, three of them needed. No per-unit coin price exists.
            var row = await ShoppingRowAsync(outputCount: 2, coinCostPerBatch: 5, quantityWanted: 3);

            Assert.Equal(3, row.Quantity);
            Assert.Equal(10L, row.CoinValue);
            Assert.Equal(5L, row.UnitCoinValue);
            Assert.Equal(2, row.UnitCoinBundleQuantity);
        }

        [Fact]
        public async Task AnOfferSellingTwoForSix_StatesAWholePerUnitPrice()
        {
            var row = await ShoppingRowAsync(outputCount: 2, coinCostPerBatch: 6, quantityWanted: 4);

            Assert.Equal(4, row.Quantity);
            Assert.Equal(12L, row.CoinValue);
            Assert.Equal(3L, row.UnitCoinValue);
            Assert.Equal(0, row.UnitCoinBundleQuantity);
            Assert.Equal(row.CoinValue, row.UnitCoinValue * row.Quantity);
        }

        [Fact]
        public async Task ASingleUnitOfferKeepsItsPlainPerUnitPrice()
        {
            var row = await ShoppingRowAsync(outputCount: 1, coinCostPerBatch: 7, quantityWanted: 3);

            Assert.Equal(21L, row.CoinValue);
            Assert.Equal(7L, row.UnitCoinValue);
            Assert.Equal(0, row.UnitCoinBundleQuantity);
            Assert.Equal(row.CoinValue, row.UnitCoinValue * row.Quantity);
        }

        [Fact]
        public async Task EveryRowEitherMultipliesOutOrDeclaresItsBundle()
        {
            foreach (var (outputCount, coin, wanted) in new[]
            {
                (1, 7L, 3), (2, 5L, 3), (2, 6L, 4), (3, 10L, 5), (4, 3L, 2),
            })
            {
                var row = await ShoppingRowAsync(outputCount, coin, wanted);

                if (row.UnitCoinBundleQuantity == 0)
                {
                    Assert.Equal(row.CoinValue, row.UnitCoinValue * row.Quantity);
                }
                else
                {
                    // The price shown is what one purchase costs, and the
                    // cell names how many units that purchase yields.
                    Assert.True(row.UnitCoinBundleQuantity > 1);
                    Assert.NotEqual(0L, row.UnitCoinValue);
                }
            }
        }
    }
}
