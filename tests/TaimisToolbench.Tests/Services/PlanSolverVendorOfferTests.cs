using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;
using static TaimisToolbench.Tests.Helpers.VendorOfferBuilders;

namespace TaimisToolbench.Tests.Services
{
    public class PlanSolverVendorOfferTests
    {
        // --- VendorCurrencyCosts threading tests ---
        [Fact]
        public void VendorCurrencyCosts_ThreadedOntoSolverDecisionAndPlanStep()
        {
            var tree = Leaf(1, 2);
            var prices = new Dictionary<int, ItemPrice>();
            var offer = new VendorOffer
            {
                OfferId = "test-currency-thread",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 10 },
                    new CostLine { Type = "Currency", Id = 23, Count = 50 },
                },
                MerchantName = "Miyani",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.NotNull(result.Decisions[0].VendorCurrencyCosts);
            Assert.Single(result.Decisions[0].VendorCurrencyCosts);
            Assert.Equal(23, result.Decisions[0].VendorCurrencyCosts[0].Id);
            Assert.Equal(100, result.Decisions[0].VendorCurrencyCosts[0].Count); // 50/unit * qty 2

            var step = result.Plan.Steps.Single(s => s.ItemId == 1);
            Assert.NotNull(step.VendorCurrencyCosts);
            Assert.Single(step.VendorCurrencyCosts);
            Assert.Equal(23, step.VendorCurrencyCosts[0].Id);
            Assert.Equal(100, step.VendorCurrencyCosts[0].Count);
        }

        [Fact]
        public void VendorCurrencyCosts_MergedAcrossDeduplicatedOccurrences()
        {
            // Same vendor-sourced item reached via two tree branches must
            // sum its currency cost into the single aggregated PlanStep row,
            // not just the last-seen occurrence's amount. Item 3 (which
            // consumes item 2, the vendor-fallback-tier item under test)
            // intentionally has NO TP price: craft/vendor comparability-
            // parity fix's transitive-taint propagation (regression
            // follow-up - Decision.HasUnvaluedCurrency) now correctly
            // demotes item 3's own craft-via-item-2 recipe to fallback-tier
            // too, since it consumes item 2's own fallback (unvalued
            // currency) vendor decision - so a real TP price for item 3
            // would make item 3 buy from the TP instead of crafting via
            // item 2, and item 2 would then only be reached via ONE tree
            // occurrence instead of the two this test needs. With no TP
            // price, item 3 still crafts via item 2 as the last resort
            // (nothing else available - same established pattern already
            // used elsewhere in this suite for a fallback-tier decision),
            // so this test's real subject (VendorCurrencyCosts merging
            // across two tree occurrences) still applies.
            var tree = Craftable(1, 1,
                Option(10, 1, 1,
                    Leaf(2, 1),
                    Craftable(3, 1,
                        Option(20, 1, 1,
                            Leaf(2, 1)))));
            var prices = new Dictionary<int, ItemPrice>();
            var offer = new VendorOffer
            {
                OfferId = "test-dedup-currency",
                OutputItemId = 2,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = 23, Count = 10 },
                },
                MerchantName = "Miyani",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 2, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            var item2Steps = plan.Steps.Where(s => s.ItemId == 2).ToList();
            Assert.Single(item2Steps); // deduplicated into one row
            Assert.NotNull(item2Steps[0].VendorCurrencyCosts);
            Assert.Single(item2Steps[0].VendorCurrencyCosts);
            Assert.Equal(23, item2Steps[0].VendorCurrencyCosts[0].Id);
            Assert.Equal(20, item2Steps[0].VendorCurrencyCosts[0].Count); // 10 + 10 across both occurrences
        }

        [Fact]
        public void VendorCurrencyCosts_MergeOverflow_ClampsRatherThanWraps()
        {
            // Two occurrences of the same vendor-sourced item, each with a
            // currency count near int.MaxValue, sum past int.MaxValue -
            // must clamp, not silently wrap to a negative/garbage count.
            // Item 3 intentionally has NO TP price - see the identical note
            // in VendorCurrencyCosts_MergedAcrossDeduplicatedOccurrences
            // above (craft/vendor comparability-parity fix's transitive
            // taint propagation).
            const int nearMax = 1_200_000_000;
            var tree = Craftable(1, 1,
                Option(10, 1, 1,
                    Leaf(2, 1),
                    Craftable(3, 1,
                        Option(20, 1, 1,
                            Leaf(2, 1)))));
            var prices = new Dictionary<int, ItemPrice>();
            var offer = new VendorOffer
            {
                OfferId = "test-overflow-currency",
                OutputItemId = 2,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = 23, Count = nearMax },
                },
                MerchantName = "Miyani",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 2, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            var item2Step = plan.Steps.Single(s => s.ItemId == 2);
            Assert.Equal(int.MaxValue, item2Step.VendorCurrencyCosts[0].Count);
        }

        // --- Backward-compat regression tests ---
        [Fact]
        public void ExistingLeafBuyFromTp_WithNullVendorOffers_Unchanged()
        {
            var tree = Leaf(1, 5);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100 } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, null).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, plan.Steps[0].Source);
            Assert.Equal(500, plan.TotalCoinCost);
        }

        [Fact]
        public void ExistingCraftCheaper_WithEmptyVendorOffers_Unchanged()
        {
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 2)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>();
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Equal(2, plan.Steps.Count);
            Assert.Contains(plan.Steps, s => s.Source == AcquisitionSource.Craft && s.ItemId == 1);
            Assert.Equal(200, plan.TotalCoinCost);
        }

        // --- Vendor offer tests ---
        [Fact]
        public void VendorCheaperThanTpAndCraft_ChoosesVendor()
        {
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 2)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 500 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 400 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { CoinVendorOffer(1, 200) } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(200, plan.Steps[0].TotalCost);
            Assert.Equal(200, plan.TotalCoinCost);
        }

        [Fact]
        public void VendorMoreExpensiveThanTp_ChoosesTp()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 200 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { CoinVendorOffer(1, 500) } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, plan.Steps[0].Source);
            Assert.Equal(200, plan.TotalCoinCost);
        }

        [Fact]
        public void VendorWithCurrencyCost_TracksCurrencyInPlan()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var offer = new VendorOffer
            {
                OfferId = "test-mixed",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 100 },
                    new CostLine { Type = "Currency", Id = 2, Count = 50 },
                },
                MerchantName = "Karma Vendor",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(100, plan.Steps[0].TotalCost);
            Assert.Equal(100, plan.TotalCoinCost);
            Assert.Single(plan.CurrencyCosts);
            Assert.Equal(2, plan.CurrencyCosts[0].CurrencyId);
            Assert.Equal(50, plan.CurrencyCosts[0].Amount);
        }

        [Fact]
        public void VendorOnlyOption_NoTpNoCraft_ChoosesVendor()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { CoinVendorOffer(1, 300) } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(300, plan.TotalCoinCost);
        }

        [Fact]
        public void MultipleVendorOffers_PicksCheapest()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    1, new List<VendorOffer>
                    {
                        CoinVendorOffer(1, 500),
                        CoinVendorOffer(1, 100),
                    }
                },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(100, plan.TotalCoinCost);
        }

        [Fact]
        public void VendorOfferWithItemCosts_PricesViaTP()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 200 } },
                { 42, new ItemPrice { ItemId = 42, BuyInstant = 10 } },
            };
            var offer = new VendorOffer
            {
                OfferId = "test-item-cost",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Item", Id = 42, Count = 5 },
                },
                MerchantName = "Barter Vendor",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            // Vendor cost = 5 * 10 = 50, TP buy = 200 -> vendor wins
            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(50, plan.TotalCoinCost);
        }

        [Fact]
        public void VendorOfferWithUnrecognizedCostLineType_TreatedAsUnpriceable_NeverWinsOverTp()
        {
            // Regression (class-level sibling of
            // the recipe-ingredient Item-positive sweep): before the fix,
            // VendorBatchSolver's cost-line fold had no `else` branch, so an
            // unrecognized CostLine.Type (VendorOfferLoader performs no type
            // validation at load, and ref/vendor_offers.json is
            // tool-scraped, so a future third type is directly reachable)
            // was silently dropped - `priceable` stayed true and the offer
            // was costed as if the line were not there at all, a
            // fabricated-low (here: zero) coin cost that would always beat
            // a real TP price. This offer's only cost line is an
            // unrecognized type, so a correctly-fixed solver must exclude
            // it entirely and fall back to the real TP price instead of
            // "buying" it from the vendor for free.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 200 } },
            };
            var offer = new VendorOffer
            {
                OfferId = "test-unrecognized-cost-line",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "MysteryCostType", Id = 999, Count = 5 },
                },
                MerchantName = "Barter Vendor",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, plan.Steps[0].Source);
            Assert.Equal(200, plan.TotalCoinCost);
        }

        [Fact]
        public void VendorOfferWithOutputCountGreaterThanOne_ScalesCorrectly()
        {
            var tree = Leaf(1, 5);
            var prices = new Dictionary<int, ItemPrice>();
            // Vendor sells 2 for 100 coin each batch -> need ceil(5/2)=3 batches = 300
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { CoinVendorOffer(1, 100, outputCount: 2) } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(300, plan.TotalCoinCost);
        }

        // --- SolverDecision.VendorItemCosts/VendorHasRawCoin ---
        [Fact]
        public void MixedItemAndCurrencyOffer_PopulatesVendorItemCosts_AndNotHasRawCoin()
        {
            // 5x item 42 (TP 10 each = 50) + 3x currency 23, no raw coin.
            var tree = Leaf(1, 2);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 42, new ItemPrice { ItemId = 42, BuyInstant = 10 } },
            };
            var offer = ItemAndCurrencyVendorOffer(
                1, new[] { (42, 5) }, new[] { (23, 3) });
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var decision = result.Decisions.Values.Single(d => d.Source == AcquisitionSource.BuyFromVendor);

            Assert.False(decision.VendorHasRawCoin);
            Assert.NotNull(decision.VendorItemCosts);
            Assert.Single(decision.VendorItemCosts);
            Assert.Equal(42, decision.VendorItemCosts[0].ItemId);
            Assert.Equal(10, decision.VendorItemCosts[0].Quantity); // 5 * qty 2
            Assert.Equal(100, decision.VendorItemCosts[0].GoldValue); // 10 * 10 unit price

            Assert.NotNull(decision.VendorCurrencyCosts);
            Assert.Single(decision.VendorCurrencyCosts);
            Assert.Equal(23, decision.VendorCurrencyCosts[0].Id);
            Assert.Equal(6, decision.VendorCurrencyCosts[0].Count); // 3 * qty 2

            // The item's folded gold is part of TotalCost/plan.TotalCoinCost
            // - the exact same number GoldValue reports, never a divergent
            // recompute.
            Assert.Equal(100, decision.TotalCost);
        }

        [Fact]
        public void RawCoinPlusItemOffer_HasRawCoinTrue()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 42, new ItemPrice { ItemId = 42, BuyInstant = 10 } },
            };
            var offer = ItemAndCurrencyVendorOffer(
                1, new[] { (42, 2) }, currencyCostLines: null, coinCost: 5);
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var decision = result.Decisions.Values.Single(d => d.Source == AcquisitionSource.BuyFromVendor);

            Assert.True(decision.VendorHasRawCoin);
            Assert.NotNull(decision.VendorItemCosts);
            Assert.Single(decision.VendorItemCosts);
            Assert.Equal(20, decision.VendorItemCosts[0].GoldValue); // 2 * 10
            Assert.Equal(25, decision.TotalCost); // 5 raw coin + 20 item-folded
        }

        [Fact]
        public void PureItemOffer_VendorItemCostsPopulated_HasRawCoinFalse()
        {
            // Single-kind (item-only) offer - still populates VendorItemCosts
            // (CraftingTreeBuilder's own kind-count gate is what decides
            // whether a leaf gets synthesized, not this raw field).
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 200 } },
                { 42, new ItemPrice { ItemId = 42, BuyInstant = 10 } },
            };
            var offer = ItemAndCurrencyVendorOffer(1, new[] { (42, 5) }, currencyCostLines: null);
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var decision = result.Decisions.Values.Single(d => d.Source == AcquisitionSource.BuyFromVendor);

            Assert.False(decision.VendorHasRawCoin);
            Assert.Null(decision.VendorCurrencyCosts);
            Assert.NotNull(decision.VendorItemCosts);
            Assert.Single(decision.VendorItemCosts);
            Assert.Equal(50, decision.VendorItemCosts[0].GoldValue);
        }

        [Fact]
        public void PureCoinOffer_VendorItemCostsNull_HasRawCoinTrue()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { CoinVendorOffer(1, 100) } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var decision = result.Decisions.Values.Single(d => d.Source == AcquisitionSource.BuyFromVendor);

            Assert.True(decision.VendorHasRawCoin);
            Assert.Null(decision.VendorItemCosts);
            Assert.Null(decision.VendorCurrencyCosts);
        }

        [Fact]
        public void NonVendorDecision_VendorItemCostsNull_HasRawCoinFalse()
        {
            var tree = Leaf(1, 5);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 50 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, null);
            var decision = result.Decisions.Values.Single(d => d.Source == AcquisitionSource.BuyFromTp);

            Assert.Null(decision.VendorItemCosts);
            Assert.False(decision.VendorHasRawCoin);
        }

        /// <summary>
        /// A malformed offer with a Count-0 Item
        /// cost line (e.g. bad wiki-scraped seed data) must not invent a
        /// phantom "item" cost KIND - matches the raw-coin branch's own
        /// `if (cost.Count > 0)` guard a few lines above it. Mixed with a
        /// real currency line so the pre-fix bug (VendorItemCosts wrongly
        /// populated with a 0-quantity/0-gold entry, flipping kindCount
        /// from 1 real kind to 2) is directly observable on the committed
        /// decision.
        /// </summary>
        [Fact]
        public void ZeroCountItemCostLine_DoesNotPopulateVendorItemCosts()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 42, new ItemPrice { ItemId = 42, BuyInstant = 10 } },
            };
            var offer = ItemAndCurrencyVendorOffer(
                1, new[] { (42, 0) }, new[] { (23, 10) });
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var decision = result.Decisions.Values.Single(d => d.Source == AcquisitionSource.BuyFromVendor);

            Assert.Null(decision.VendorItemCosts);
            Assert.NotNull(decision.VendorCurrencyCosts);
            Assert.Single(decision.VendorCurrencyCosts);
        }

        /// <summary>
        /// The sibling defect to
        /// <see cref="ZeroCountItemCostLine_DoesNotPopulateVendorItemCosts"/>
        /// one field over - a malformed offer with a Count-0 non-coin
        /// Currency cost line must not invent a phantom "currency" cost
        /// KIND either. Mixed with a real Item line so the pre-fix bug
        /// (VendorCurrencyCosts wrongly populated with a 0-quantity entry,
        /// flipping kindCount from 1 real kind to 2) is directly observable
        /// on the committed decision.
        /// </summary>
        [Fact]
        public void ZeroCountCurrencyCostLine_DoesNotPopulateVendorCurrencyCosts()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 42, new ItemPrice { ItemId = 42, BuyInstant = 10 } },
            };
            var offer = ItemAndCurrencyVendorOffer(
                1, new[] { (42, 5) }, new[] { (23, 0) });
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var decision = result.Decisions.Values.Single(d => d.Source == AcquisitionSource.BuyFromVendor);

            Assert.Null(decision.VendorCurrencyCosts);
            Assert.NotNull(decision.VendorItemCosts);
            Assert.Single(decision.VendorItemCosts);
        }
    }
}
