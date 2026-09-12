using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;
using static TaimisToolbench.Tests.Helpers.VendorOfferBuilders;

namespace TaimisToolbench.Tests.Services
{
    public class PlanSolverPriceBasisAndOverrideTests
    {
        // --- Price basis tests ---
        [Fact]
        public void BuyOrderBasis_UsesBuyOrderPrice()
        {
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100, SellInstant = 60 } },
            };
            var solver = new PlanSolver();

            var instant = solver.Solve(Leaf(1, 2), prices, null, PriceBasis.InstantBuy);
            var order = solver.Solve(Leaf(1, 2), prices, null, PriceBasis.BuyOrder);

            Assert.Equal(200, instant.Plan.TotalCoinCost);
            Assert.Equal(120, order.Plan.TotalCoinCost);
            // Preferred side present on both sides ->
            // used directly, no same-item other-side fallback triggered.
            Assert.False(instant.Decisions[0].PriceSideFellBack);
            Assert.False(order.Decisions[0].PriceSideFellBack);
        }

        [Fact]
        public void BuyOrderBasis_NoBuyOrders_FallsBackToInstantBuyPrice()
        {
            // The
            // preferred side (buy orders / SellInstant) is empty, but this
            // SAME item's other side (instant-buy / BuyInstant) has a real
            // listing - gw2e falls back to it instead of treating the item
            // as unpriceable. Previously this returned UnknownSource.
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100, SellInstant = 0 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(Leaf(1, 1), prices, null, PriceBasis.BuyOrder);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Plan.Steps[0].Source);
            Assert.Equal(100, result.Plan.TotalCoinCost);
            Assert.True(result.Decisions[0].PriceSideFellBack);
        }

        [Fact]
        public void BuyOrderBasis_BothSidesEmpty_ItemNotPriceable()
        {
            // Both TP sides empty stays unpriceable - the
            // fallback only ever tries this SAME item's other side, never
            // invents a price from nothing.
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 0, SellInstant = 0 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(Leaf(1, 1), prices, null, PriceBasis.BuyOrder);

            Assert.Equal(AcquisitionSource.UnknownSource, result.Plan.Steps[0].Source);
            Assert.False(result.Decisions[0].PriceSideFellBack);
        }

        [Fact]
        public void BuyOrderBasis_CraftWinsOverFallbackPricedBuy_DecisionFlagStaysFalse()
        {
            // BuyPriceSideFellBack is computed unconditionally
            // for every node's own TP price (item 1's preferred side, buy
            // orders / SellInstant, is empty here - the buy-side total only
            // exists via this same item's other-side fallback to BuyInstant).
            // Craft still wins the three-way comparison (20 < 100), so
            // Commit's `src == AcquisitionSource.BuyFromTp` gate must keep
            // the flag false on the winning Craft decision even though the
            // losing buy option internally fell back.
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 1)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100, SellInstant = 0 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 0, SellInstant = 20 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, null, PriceBasis.BuyOrder);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(20, result.Plan.TotalCoinCost);
            Assert.False(result.Decisions[0].PriceSideFellBack);
        }

        [Fact]
        public void BuyOrderBasis_VendorWinsOverFallbackPricedBuy_DecisionFlagStaysFalse()
        {
            // Sibling of
            // BuyOrderBasis_CraftWinsOverFallbackPricedBuy_DecisionFlagStaysFalse
            // above, but exercising the OTHER half of Commit's
            // `src == AcquisitionSource.BuyFromTp` gate - a BuyFromVendor
            // win, not a Craft win. Item 1's preferred side (buy orders /
            // SellInstant) is empty, so buyPriceSideFellBack is computed
            // true for the (losing) TP option; the vendor coin offer (40)
            // beats both the fallback-priced buy (100) and the leaf's lack
            // of a craft option outright, so the gate must keep the flag
            // false on the winning BuyFromVendor decision.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100, SellInstant = 0 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { CoinVendorOffer(1, 40) } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.BuyOrder);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(40, result.Plan.TotalCoinCost);
            Assert.False(result.Decisions[0].PriceSideFellBack);
        }

        [Fact]
        public void BuyOrderBasis_FallbackPricedBuyWinsOverCraft_SourceIsBuyFromTp()
        {
            // The sibling
            // *_WinsOverFallbackPricedBuy_DecisionFlagStaysFalse tests above
            // only cover the fallback-priced buy LOSING the comparison -
            // every one of them still passes if the fallback were removed
            // entirely (buyTotalCost -> null, craft/vendor still wins). This
            // pins the other outcome: item 1's preferred side (buy orders /
            // SellInstant) is empty, so its buy option is only priced via
            // the same-item other-side fallback (BuyInstant = 100); that
            // fallback-priced buy (100) is cheaper than the only recipe
            // (1x item 2 at SellInstant 200) and must WIN the three-way
            // comparison, not just lose it gracefully.
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 1)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100, SellInstant = 0 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 200, SellInstant = 200 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, null, PriceBasis.BuyOrder);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Decisions[0].Source);
            Assert.Equal(100, result.Plan.TotalCoinCost);
            Assert.True(result.Decisions[0].PriceSideFellBack);
        }

        [Fact]
        public void BuyOrderBasis_CanFlipBuyVsCraftDecision()
        {
            // Output: instant 100 / order 90. Craft from 2x ingredient:
            // instant 2x60=120 (buy wins), order 2x30=60 (craft wins).
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100, SellInstant = 90 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 60, SellInstant = 30 } },
            };
            var solver = new PlanSolver();

            var instant = solver.Solve(
                Craftable(1, 1, Option(10, 1, 1, Leaf(2, 2))), prices, null,
                PriceBasis.InstantBuy).Plan;
            var order = solver.Solve(
                Craftable(1, 1, Option(10, 1, 1, Leaf(2, 2))), prices, null,
                PriceBasis.BuyOrder).Plan;

            Assert.Single(instant.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, instant.Steps[0].Source);
            Assert.Contains(order.Steps, s => s.Source == AcquisitionSource.Craft);
            Assert.Equal(60, order.TotalCoinCost);
        }

        [Fact]
        public void BuyOrderBasis_VendorItemBarter_PricedAtBasis()
        {
            // Offer: 5x item 42. Instant 10 -> 50; order 4 -> 20.
            var offer = new VendorOffer
            {
                OfferId = "test-barter-basis",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Item", Id = 42, Count = 5 },
                },
                MerchantName = "Barter Vendor",
            };
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 200, SellInstant = 100 } },
                { 42, new ItemPrice { ItemId = 42, BuyInstant = 10, SellInstant = 4 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var order = solver.Solve(Leaf(1, 1), prices, vendorOffers, PriceBasis.BuyOrder).Plan;

            Assert.Equal(AcquisitionSource.BuyFromVendor, order.Steps[0].Source);
            Assert.Equal(20, order.TotalCoinCost);
        }

        [Fact]
        public void BuyOrderBasis_VendorItemBarter_BarterItemFallsBackToOtherSide()
        {
            // PlanSolver.GetUnitPrice is the single site
            // VendorBatchSolver's Item-cost-line pricing routes through, so
            // the same-item other-side fallback must reach it too. Barter
            // item 42's preferred side (buy orders / SellInstant) is empty
            // here - only its BuyInstant side has a listing - so the offer
            // must still price (5 x 4 = 20) rather than be dropped as
            // unpriceable, same total as the sibling
            // BuyOrderBasis_VendorItemBarter_PricedAtBasis test above where
            // both sides were populated directly.
            var offer = new VendorOffer
            {
                OfferId = "test-barter-basis-fallback",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Item", Id = 42, Count = 5 },
                },
                MerchantName = "Barter Vendor",
            };
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 200, SellInstant = 100 } },
                { 42, new ItemPrice { ItemId = 42, BuyInstant = 4, SellInstant = 0 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var order = solver.Solve(Leaf(1, 1), prices, vendorOffers, PriceBasis.BuyOrder).Plan;

            Assert.Equal(AcquisitionSource.BuyFromVendor, order.Steps[0].Source);
            Assert.Equal(20, order.TotalCoinCost);
        }

        // --- Per-node override tests ---
        [Fact]
        public void Override_ForcesBuyOverCheaperCraft()
        {
            // Craft = 60 beats buy = 100; user forces buy on the root.
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 2)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 30 } },
            };
            var solver = new PlanSolver();

            var baseline = solver.Solve(tree, prices, null);
            int rootNodeId = 0; // DFS pre-pass: root is always node 0
            Assert.Equal(AcquisitionSource.Craft, baseline.Decisions[rootNodeId].Source);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { rootNodeId, AcquisitionSource.BuyFromTp },
            };
            var forced = solver.Solve(tree, prices, null, PriceBasis.InstantBuy, overrides);

            Assert.Single(forced.Plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, forced.Plan.Steps[0].Source);
            Assert.Equal(100, forced.Plan.TotalCoinCost);
        }

        [Fact]
        public void Override_ForcesCraftOverCheaperBuy()
        {
            // Buy = 50 beats craft = 200; user forces craft.
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 2)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 50 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var solver = new PlanSolver();

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { 0, AcquisitionSource.Craft },
            };
            var forced = solver.Solve(tree, prices, null, PriceBasis.InstantBuy, overrides);

            Assert.Contains(forced.Plan.Steps, s => s.Source == AcquisitionSource.Craft && s.ItemId == 1);
            Assert.Equal(200, forced.Plan.TotalCoinCost);
        }

        [Fact]
        public void Override_Infeasible_IgnoredAndBestPathApplies()
        {
            // Leaf with no recipes: forcing Craft is infeasible.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100 } },
            };
            var solver = new PlanSolver();

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { 0, AcquisitionSource.Craft },
            };
            var plan = solver.Solve(tree, prices, null, PriceBasis.InstantBuy, overrides).Plan;

            Assert.Equal(AcquisitionSource.BuyFromTp, plan.Steps[0].Source);
        }

        [Fact]
        public void Override_OnChildNode_ParentCraftCostUsesForcedChildCost()
        {
            // Child 2: craft (20) beats buy (100). Forcing child to buy makes
            // the parent's craft cost 100, so the parent flips to buying at 90.
            var tree = Craftable(1, 1,
                Option(10, 1, 1,
                    Craftable(2, 1,
                        Option(20, 1, 1, Leaf(3, 2)))));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 90 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
                { 3, new ItemPrice { ItemId = 3, BuyInstant = 10 } },
            };
            var solver = new PlanSolver();

            var baseline = solver.Solve(tree, prices, null);
            // Baseline: craft chain, total 20
            Assert.Equal(20, baseline.Plan.TotalCoinCost);

            // Child 2 is NodeId 1 (DFS: root=0, first child=1)
            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { 1, AcquisitionSource.BuyFromTp },
            };
            var forced = solver.Solve(tree, prices, null, PriceBasis.InstantBuy, overrides);

            // Parent now prefers its own buy at 90 over craft-with-forced-child at 100
            Assert.Equal(AcquisitionSource.BuyFromTp, forced.Decisions[0].Source);
            Assert.Equal(90, forced.Plan.TotalCoinCost);
        }

        [Fact]
        public void UnpriceableRecipe_CanCraftIsTrue_ForceCraftSucceedsWithZeroFilledCost()
        {
            // Partial-pricing parity:
            // CanCraft now means "has a recipe" (gw2e's hasComponents), not
            // "recipe is fully priceable" - a recipe with an unpriceable
            // ingredient is always force-craftable (the ingredient just
            // zero-fills the craft cost). Item 1 is TP-priced (100) AND has
            // a recipe whose ingredient has no price; without any override
            // at all, craft (0, zero-filled) already strictly beats buy
            // (100), so this also demonstrates the natural (non-forced)
            // pick, not just the override path.
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 2)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100 } },
            };
            var solver = new PlanSolver();

            var natural = solver.Solve(tree, prices, null);
            Assert.Equal(AcquisitionSource.Craft, natural.Decisions[0].Source);
            Assert.True(natural.Decisions[0].CanCraft);
            Assert.True(natural.Decisions[0].CanBuyTp);
            Assert.Equal(0, natural.Plan.TotalCoinCost);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { 0, AcquisitionSource.Craft },
            };
            var forced = solver.Solve(tree, prices, null, PriceBasis.InstantBuy, overrides);

            Assert.Equal(AcquisitionSource.Craft, forced.Decisions[0].Source);
            Assert.Equal(0, forced.Plan.TotalCoinCost);
        }

        [Fact]
        public void AvailabilityFlags_ReflectFeasiblePaths()
        {
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 2, new List<VendorOffer> { CoinVendorOffer(2, 500) } },
            };
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 1)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            // Root: craftable, no TP price, no vendor offer
            Assert.True(result.Decisions[0].CanCraft);
            Assert.False(result.Decisions[0].CanBuyTp);
            Assert.False(result.Decisions[0].CanBuyVendor);
            // Child: leaf with TP price and vendor offer
            Assert.False(result.Decisions[1].CanCraft);
            Assert.True(result.Decisions[1].CanBuyTp);
            Assert.True(result.Decisions[1].CanBuyVendor);
        }
    }
}
