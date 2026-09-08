using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;
using static TaimisToolbench.Tests.Helpers.VendorOfferBuilders;

namespace TaimisToolbench.Tests.Services
{
    public class PlanSolverCurrencyValuationTests
    {
        // --- Currency valuation tests ---
        // A user-provided CurrencyValuation makes an offer's non-coin
        // currency lines comparable, but ONLY when every line on the offer
        // has a valuation; the valuation affects comparison only, never the
        // currency amounts reported on the plan.
        [Fact]
        public void ValuedCurrencyOffer_BeatsExpensiveTp_AndPlanListsCurrencyCost()
        {
            // Karma-priced offer (0 coin, 50 karma) with a user valuation of
            // 5 copper/karma (= 250 total) beats a 1000-copper TP price. The
            // plan must still report the real karma amount to pay, not a
            // coin-converted figure.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 0, 2, 50) } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 2, 5 } });
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(0, plan.Steps[0].TotalCost); // coin part only - offer has no coin cost
            Assert.Equal(0, plan.TotalCoinCost);
            Assert.Single(plan.CurrencyCosts);
            Assert.Equal(2, plan.CurrencyCosts[0].CurrencyId);
            Assert.Equal(50, plan.CurrencyCosts[0].Amount);
        }

        [Fact]
        public void UnvaluedCurrencyOffer_WithoutValuation_StaysFallbackOnly()
        {
            // Same offer and prices as the valued-wins test above, but with
            // no valuation supplied at all: pins the existing fallback-only
            // behavior (TP wins; the offer never even enters the comparison).
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 0, 2, 50) } },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, plan.Steps[0].Source);
            Assert.Equal(1000, plan.TotalCoinCost);
            Assert.Empty(plan.CurrencyCosts);
        }

        [Fact]
        public void ValuedCurrencyOffer_LosesWhenValuedCostExceedsTp()
        {
            // Same karma offer, but its valued cost (250) now exceeds the TP
            // price (100): TP must win outright, and the losing offer must
            // not leak into CurrencyCosts.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 0, 2, 50) } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 2, 5 } });
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, plan.Steps[0].Source);
            Assert.Equal(100, plan.TotalCoinCost);
            Assert.Empty(plan.CurrencyCosts);
        }

        [Fact]
        public void MixedValuedAndUnvaluedCurrencyOffer_StaysFallbackTier()
        {
            // Offer costs both a valued currency (karma, id 2) and an
            // unvalued one (laurels, id 3). Any unvalued line must keep the
            // WHOLE offer in the fallback tier - it must not become
            // partially comparable. No TP price exists, so the fallback
            // offer is the only acquisition; both currency lines (valued and
            // unvalued alike) must appear in full on the plan.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var offer = new VendorOffer
            {
                OfferId = "test-mixed-valued-and-unvalued",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = 2, Count = 10 },
                    new CostLine { Type = "Currency", Id = 3, Count = 1000 },
                },
                MerchantName = "Mixed Vendor",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 2, 5 } });
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation).Plan;

            Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(0, plan.Steps[0].TotalCost);
            Assert.Equal(2, plan.CurrencyCosts.Count);
            Assert.Contains(plan.CurrencyCosts, c => c.CurrencyId == 2 && c.Amount == 10);
            Assert.Contains(plan.CurrencyCosts, c => c.CurrencyId == 3 && c.Amount == 1000);
        }

        [Fact]
        public void MixedValuedAndUnvaluedCurrencyOffer_DoesNotBeatTp()
        {
            // Even a trivially cheap valued line must not make the offer
            // comparable while any other line stays unvalued.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100 } },
            };
            var offer = new VendorOffer
            {
                OfferId = "test-mixed-valued-cheap",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = 2, Count = 1 },
                    new CostLine { Type = "Currency", Id = 3, Count = 1 },
                },
                MerchantName = "Mixed Vendor",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 2, 1 } });
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation).Plan;

            Assert.Equal(AcquisitionSource.BuyFromTp, plan.Steps[0].Source);
            Assert.Equal(100, plan.TotalCoinCost);
            Assert.Empty(plan.CurrencyCosts);
        }

        [Fact]
        public void ValuedCurrencyOffer_ForcedOverride_CarriesCurrencyCostsIntoPlan()
        {
            // A per-node override forcing BuyFromVendor on a fully-valued
            // offer must commit the same real coin part + currency lines as
            // the automatic comparison path.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 0, 2, 50) } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 2, 5 } });
            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { 0, AcquisitionSource.BuyFromVendor },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, overrides, valuation).Plan;

            Assert.Equal(AcquisitionSource.BuyFromVendor, plan.Steps[0].Source);
            Assert.Equal(0, plan.Steps[0].TotalCost);
            Assert.Single(plan.CurrencyCosts);
            Assert.Equal(2, plan.CurrencyCosts[0].CurrencyId);
            Assert.Equal(50, plan.CurrencyCosts[0].Amount);
        }

        // --- Comparison-value laundering regression tests ---
        // A valued vendor offer's coin-equivalent (coin + valued currency)
        // must survive being summed into an ANCESTOR's craft cost. Before
        // the fix, the craft loop summed each ingredient's returned REAL
        // coin cost (e.g. 0 for a karma-only vendor offer) instead of its
        // comparison value (coin + valued currency), so the karma cost was
        // laundered away and an ancestor could wrongly choose to craft
        // through a valued vendor offer that was actually more expensive.
        [Fact]
        public void ValuedVendorDescendant_DoesNotLaunderIntoCraftComparison_TpWinsForAncestor()
        {
            // B (item 2): TP buy 1000, or vendor offer 0 coin + 50 karma
            // (currency 3) valued at 5 copper/unit = 250 comparison value.
            // A (item 1): TP buy 200, or craft from 1x B.
            // Craft-A's true comparison cost is B's comparison value (250),
            // not B's real coin part (0), so TP-buy-A (200) must beat craft.
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 1)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 200 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 1000 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 2, new List<VendorOffer> { MixedVendorOffer(2, 0, 3, 50) } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 3, 5 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Decisions[0].Source);
            Assert.Single(result.Plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, result.Plan.Steps[0].Source);
            Assert.Equal(1, result.Plan.Steps[0].ItemId);
            Assert.Equal(200, result.Plan.TotalCoinCost);
        }

        [Fact]
        public void ValuedVendorDescendant_CraftStillWinsWhenGenuinelyCheaper_PlanShowsRealCoinAndCurrency()
        {
            // Same B options as above, but A's TP price (2000) is expensive
            // enough that craft (comparison cost 250) genuinely wins. The
            // committed plan must show the REAL coin cost (0, B's vendor
            // coin part) and the real karma amount (50) - the valuation used
            // to pick this path must never leak into the displayed coin.
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 1)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 2000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 1000 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 2, new List<VendorOffer> { MixedVendorOffer(2, 0, 3, 50) } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 3, 5 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);
            var plan = result.Plan;

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Contains(plan.Steps, s => s.Source == AcquisitionSource.Craft && s.ItemId == 1);
            Assert.Contains(plan.Steps, s => s.Source == AcquisitionSource.BuyFromVendor && s.ItemId == 2);

            // Real coin cost only (B's vendor coin part is 0) - the 250
            // comparison value used to pick this path must not appear here.
            Assert.Equal(0, plan.TotalCoinCost);
            Assert.Single(plan.CurrencyCosts);
            Assert.Equal(3, plan.CurrencyCosts[0].CurrencyId);
            Assert.Equal(50, plan.CurrencyCosts[0].Amount);
        }

        // --- currency-ux-package (Feature 3): SolverDecision.ComparisonValue ---
        [Fact]
        public void ComparisonValue_RollsUpThroughAncestorCraft_MatchesDecisionOnlyExpectation()
        {
            // Same shape as ValuedVendorDescendant_CraftStillWinsWhenGenuinelyCheaper
            // above: B's vendor decision has a real coin cost of 0 but a
            // ComparisonValue of 250 (50 karma valued at 5 copper/unit); A's
            // craft decision must roll that same 250 up into its own
            // ComparisonValue (real cost 0, since B is the only ingredient
            // and contributes 0 real coin) - ComparisonValue is never equal
            // to TotalCost for either node here.
            var tree = Craftable(1, 1, Option(10, 1, 1, Leaf(2, 1)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 2000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 1000 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 2, new List<VendorOffer> { MixedVendorOffer(2, 0, 3, 50) } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 3, 5 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            // DFS NodeIds: item1(craft)=0, item2(vendor)=1.
            Assert.Equal(0, result.Decisions[1].TotalCost);
            Assert.Equal(250, result.Decisions[1].ComparisonValue);
            Assert.Equal(0, result.Decisions[0].TotalCost);
            Assert.Equal(250, result.Decisions[0].ComparisonValue);
        }

        /// <summary>
        /// The sibling test above
        /// (ComparisonValue_RollsUpThroughAncestorCraft_MatchesDecisionOnlyExpectation)
        /// proves the raw SolverDecision.ComparisonValue rolls up correctly
        /// through the solver; this test walks the SAME shape one layer
        /// further - through CraftingTreeBuilder.BuildTree (which copies
        /// decision.ComparisonValue verbatim onto
        /// CraftingTreeNode.DecisionValue) and then
        /// ValueDetailTooltipBuilder.TryBuildContent, the exact two production
        /// steps between a solved decision and the tooltip a CRAFT pill
        /// hover renders. CurrencyDecisionDefaults' own curated value is
        /// used (via CurrencyValuation.WithDefaults) rather than a
        /// hand-picked test valuation.
        /// </summary>
        [Fact]
        public void CraftRoot_VendorChildValuedInCuratedCurrency_ValueDetailTooltipFires()
        {
            const int SpiritShardCurrencyId = 23;

            // Root (item 1): craft from 1x item 2 (Philosopher's Stone-
            // style vendor-only item). No TP price/vendor offer of its own,
            // so craft is the only source and wins outright.
            var tree = Craftable(1, 5, Option(10, 1, 1, Leaf(2, 5)));
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                // Bought purely with 20 spirit shards per unit - no coin,
                // no TP price at all for item 2 either, so BuyFromVendor is
                // the only source and wins outright too.
                { 2, new List<VendorOffer> { MixedVendorOffer(2, 0, SpiritShardCurrencyId, 20) } },
            };
            var valuation = CurrencyValuation.WithDefaults(CurrencyValuation.None);
            var solver = new PlanSolver();

            var solveResult = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            var builder = new CraftingTreeBuilder();
            var root = builder.BuildTree(tree, solveResult.Decisions, metadata: null);

            Assert.Equal(CraftingDecision.Craft, root.Decision);
            // Real gold cost is 0 (the vendor child's own coin part is 0);
            // DecisionValue is the shard cost folded up via the curated
            // 3600 copper/unit default: 5x item 2 needed, 20 shards/unit ->
            // 100 shards * 3600 = 360000 - the two must diverge for the
            // hover to fire.
            Assert.Equal(0, root.SubtreeCost);
            Assert.Equal(360000, root.DecisionValue);

            bool fired = ValueDetailTooltipBuilder.TryBuildContent(root, null, out var tooltip);

            Assert.True(fired);
            Assert.NotNull(tooltip);
            Assert.Contains("Crafting gold price:", tooltip.ToPlainText());
            Assert.Contains("Currencies:", tooltip.ToPlainText());
            Assert.Contains("Optimization price:", tooltip.ToPlainText());
        }

        [Fact]
        public void ComparisonValue_NoCurrencyContribution_EqualsTotalCost()
        {
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 100 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices);

            Assert.Equal(100, result.Decisions[0].TotalCost);
            Assert.Equal(result.Decisions[0].TotalCost, result.Decisions[0].ComparisonValue);
        }

        [Fact]
        public void MixedCoinValuedUnvaluedFallbackOffer_ComparisonValueMatchesTotalCost_NoTooltip()
        {
            // Regression test a fallback-tier vendor offer
            // (coin 100 + valued currency 2 x50 @1 copper/unit + unvalued
            // currency 3 x1000) used to have its ComparisonValue overwritten
            // by the vendorOccurrences post-selection pass in PlanSolver
            // (the pass immediately after AllocateVendorNodeCosts) with a
            // fabricated partial figure of 150 (100 real coin + 50 valued
            // currency, silently dropping the unvalued line) even though
            // Evaluate's own fallback-tier commit deliberately set
            // ComparisonValue == TotalCost (100) with no valuation folded
            // in - identical to every other fallback-tier commit site (see
            // RecomputeComparisonValues' own `decision.HasUnvaluedCurrency ?
            // ... : comparisonValue` gate). No TP price and no recipe exist
            // for item 1, so the vendor offer is the sole option and must
            // land in fallback tier (any unvalued non-coin line forces the
            // WHOLE offer to fallback - see VendorBatchSolver.Evaluate).
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var offer = new VendorOffer
            {
                OfferId = "test-mixed-coin-valued-unvalued",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 100 },
                    new CostLine { Type = "Currency", Id = 2, Count = 50 },
                    new CostLine { Type = "Currency", Id = 3, Count = 1000 },
                },
                MerchantName = "Mixed Vendor",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { offer } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 2, 1 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);
            var decision = result.Decisions[0];

            Assert.Equal(AcquisitionSource.BuyFromVendor, decision.Source);
            Assert.Equal(100, decision.TotalCost);
            Assert.Equal(100, decision.ComparisonValue);
            Assert.Equal(decision.TotalCost, decision.ComparisonValue);

            // The tooltip builder's divergence check must consequently
            // never fire for this fallback-tier vendor step: SubtreeCost
            // and DecisionValue are sourced straight from TotalCost/
            // ComparisonValue by CraftingTreeBuilder, so with the two equal
            // the delta <= 0 guard suppresses the hover.
            var treeNode = new CraftingTreeBuilder().BuildTree(
                tree, result.Decisions, new Dictionary<int, ItemMetadata>());

            bool tooltipBuilt = ValueDetailTooltipBuilder.TryBuildContent(treeNode, null, out var tooltip);

            Assert.False(tooltipBuilt);
            Assert.Null(tooltip);
        }
    }
}
