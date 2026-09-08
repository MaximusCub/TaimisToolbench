using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;
using static TaimisToolbench.Tests.Helpers.VendorOfferBuilders;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// A vendor offer whose costLines array is EMPTY costs nothing the
    /// solver can name. Folding over no lines terminates at coin 0 with
    /// every "is this priceable" flag still true, which used to land the
    /// row in the comparable tier at a comparison value of 0 - the lowest
    /// number there is, so it beat every priced route.
    /// <para>
    /// Not a hypothetical shape: 1,896 of the 59,414 rows shipped in
    /// ref/vendor_offers.json carry one, across 721 distinct output items.
    /// None of those items is reachable from today's recipe corpus, so
    /// these pin the rule before the next vendor re-scrape makes one
    /// reachable. See CostLineValuation.HasAnyCostLine.
    /// </para>
    /// </summary>
    public class PlanSolverEmptyCostLineOfferTests
    {
        private const int TargetItemId = 1;
        private const int IngredientItemId = 2;
        private const int IngredientUnitPrice = 100;
        private const int CraftCost = IngredientUnitPrice * 5;

        private static VendorOffer NoCostLineOffer(int outputItemId)
        {
            return new VendorOffer
            {
                OfferId = "test-empty-cost-lines-" + outputItemId,
                OutputItemId = outputItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>(),
                MerchantName = "TestMerchant",
            };
        }

        private static RecipeNode CraftableFromPricedIngredient()
        {
            return Craftable(
                TargetItemId, 1,
                Option(10, 1, 1, Leaf(IngredientItemId, 5)));
        }

        private static Dictionary<int, ItemPrice> IngredientPrices()
        {
            return new Dictionary<int, ItemPrice>
            {
                {
                    IngredientItemId,
                    new ItemPrice { ItemId = IngredientItemId, BuyInstant = IngredientUnitPrice }
                },
            };
        }

        [Fact]
        public void EmptyCostLines_NeverOutranksAPricedCraftRoute()
        {
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { TargetItemId, new List<VendorOffer> { NoCostLineOffer(TargetItemId) } },
            };

            var result = new PlanSolver().Solve(
                CraftableFromPricedIngredient(), IngredientPrices(), vendorOffers);

            var decision = result.Decisions[0];
            Assert.Equal(AcquisitionSource.Craft, decision.Source);
            Assert.Equal(CraftCost, decision.TotalCost);
            Assert.Equal(CraftCost, result.Plan.TotalCoinCost);

            // Not merely out-ranked: the row is no route at all, so nothing
            // downstream can render it as a free purchase.
            Assert.False(decision.CanBuyVendor);
            Assert.DoesNotContain(
                result.Plan.Steps,
                s => s.Source == AcquisitionSource.BuyFromVendor);
        }

        [Fact]
        public void EmptyCostLines_IsNotOfferedEvenWhenItIsTheOnlyCandidateRoute()
        {
            // The barter rule stops an unpriced offer WINNING but keeps it
            // as the answer of last resort, because a barter offer has a
            // real cost that merely has no coin equivalent
            // (PlanSolverUnpricedBarterOfferTests). This row has no cost to
            // report at all, so "no vendor route" is the honest answer and a
            // 0-coin BuyFromVendor step is not.
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { TargetItemId, new List<VendorOffer> { NoCostLineOffer(TargetItemId) } },
            };

            var result = new PlanSolver().Solve(
                Leaf(TargetItemId, 1), new Dictionary<int, ItemPrice>(), vendorOffers);

            var decision = result.Decisions[0];
            Assert.False(decision.CanBuyVendor);
            Assert.NotEqual(AcquisitionSource.BuyFromVendor, decision.Source);
            Assert.DoesNotContain(
                result.Plan.Steps,
                s => s.Source == AcquisitionSource.BuyFromVendor);
        }

        [Fact]
        public void EmptyCostLines_SkipsOnlyThatRow_LeavingTheItemsRealOfferIntact()
        {
            // The guard is per offer, not per item: 721 output items carry a
            // cost-line-less row, and several also carry a genuine one.
            const int VendorCoinCost = 300;
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    TargetItemId,
                    new List<VendorOffer>
                    {
                        NoCostLineOffer(TargetItemId),
                        CoinVendorOffer(TargetItemId, VendorCoinCost),
                    }
                },
            };

            var result = new PlanSolver().Solve(
                CraftableFromPricedIngredient(), IngredientPrices(), vendorOffers);

            var decision = result.Decisions[0];
            Assert.Equal(AcquisitionSource.BuyFromVendor, decision.Source);
            Assert.Equal(VendorCoinCost, decision.TotalCost);
            Assert.Equal(
                VendorCoinCost,
                result.Plan.Steps.Single(s => s.Source == AcquisitionSource.BuyFromVendor).TotalCost);
        }
    }
}
