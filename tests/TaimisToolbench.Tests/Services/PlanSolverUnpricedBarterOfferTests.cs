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
    /// A fallback-tier vendor offer's coin part omits every BARTER line on
    /// it (an untradeable item with no Trading Post price), so it is a
    /// partial accounting of what the offer really costs. The terminal
    /// fallback branch used to rank that partial figure directly against a
    /// craft route's real cost - a complete accounting of every priceable
    /// component in its subtree - and let the offer win on a price missing
    /// most of itself.
    /// <para>
    /// These pin the boundary of the rule that closed it: an unvalued
    /// non-coin CURRENCY line still ranks on the coin part (both sides omit
    /// a wallet currency the same way, and there is no exchange rate to
    /// invent), while an unpriced ITEM line stops the offer winning that
    /// comparison at all. The offer stays a reachable route either way.
    /// See docs/ARCHITECTURE.md sections 7.1 and 8, and
    /// docs/KNOWN-ISSUES.md item 44.
    /// </para>
    /// </summary>
    public class PlanSolverUnpricedBarterOfferTests
    {
        // The reported case's real ids, so the shape is recognisable:
        // 101521 Obsidian Heavy Breastplate, sold by Lyhr for 10 Globs of
        // Ectoplasm plus four account-bound Gifts, and craftable from those
        // same four Gifts alone. Prices below are the test's own inputs;
        // none of the Gift ids carries a BarterItemDecisionDefaults entry,
        // and the solver never applies those defaults itself
        // (PlanSolverBarterItemValuationTests.CuratedDefault_IsNotAppliedByTheSolverItself).
        private const int BreastplateItemId = 101521;
        private const int EctoplasmItemId = 19721;
        private const int CraftableGiftItemId = 100852;
        private const int VendorOnlyGiftItemId = 100509;
        private const int PricedMaterialItemId = 19721 + 1;
        private const int AccountBoundTokenItemId = 43992;

        private const int EctoplasmUnitPrice = 2916;
        private const int PricedMaterialUnitPrice = 500000;

        private static VendorOffer Offer(int outputItemId, string offerId, params CostLine[] costLines)
        {
            return new VendorOffer
            {
                OfferId = offerId,
                OutputItemId = outputItemId,
                OutputCount = 1,
                CostLines = costLines.ToList(),
                MerchantName = "Lyhr",
            };
        }

        private static CostLine ItemLine(int itemId, int count)
        {
            return new CostLine { Type = "Item", Id = itemId, Count = count };
        }

        /// <summary>
        /// The reported tree: the breastplate has no TP price, one recipe
        /// costing both Gifts, and one vendor offer costing the same two
        /// Gifts PLUS 10 Globs of Ectoplasm. One Gift is vendor-only and
        /// itself paid for in an account-bound token, which makes both the
        /// craft route and the offer fallback-tier.
        /// </summary>
        private static RecipeNode BuildReportedTree()
        {
            return Craftable(
                BreastplateItemId, 1,
                Option(
                    14073, 1, 1,
                    Craftable(
                        CraftableGiftItemId, 1,
                        Option(20, 1, 1, Leaf(PricedMaterialItemId, 1))),
                    Leaf(VendorOnlyGiftItemId, 1)));
        }

        private static Dictionary<int, ItemPrice> BuildReportedPrices()
        {
            return new Dictionary<int, ItemPrice>
            {
                // The breastplate and both Gifts are account-bound: no TP
                // price for any of them.
                { EctoplasmItemId, new ItemPrice { ItemId = EctoplasmItemId, BuyInstant = EctoplasmUnitPrice } },
                { PricedMaterialItemId, new ItemPrice { ItemId = PricedMaterialItemId, BuyInstant = PricedMaterialUnitPrice } },
            };
        }

        private static Dictionary<int, IReadOnlyList<VendorOffer>> BuildReportedOffers()
        {
            return new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    BreastplateItemId,
                    new List<VendorOffer>
                    {
                        Offer(
                            BreastplateItemId, "test-lyhr-breastplate",
                            ItemLine(EctoplasmItemId, 10),
                            ItemLine(CraftableGiftItemId, 1),
                            ItemLine(VendorOnlyGiftItemId, 1)),
                    }
                },
                {
                    VendorOnlyGiftItemId,
                    new List<VendorOffer>
                    {
                        Offer(
                            VendorOnlyGiftItemId, "test-lyhr-gift",
                            ItemLine(AccountBoundTokenItemId, 1)),
                    }
                },
            };
        }

        [Fact]
        public void ReportedCase_VendorOfferChargingUnpricedGifts_NeverWinsOnItsEctoplasmPartAlone()
        {
            var solver = new PlanSolver();

            var result = solver.Solve(BuildReportedTree(), BuildReportedPrices(), BuildReportedOffers());

            // The offer's coin part is 10 x 2916 = 29,160: the ectoplasm
            // and nothing else, because both Gift lines are untradeable and
            // fold into no coin at all. Committing it would quote 29,160
            // for an acquisition that also costs two account-bound Gifts.
            Assert.NotEqual(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(PricedMaterialUnitPrice, result.Decisions[0].TotalCost);
            Assert.Equal(PricedMaterialUnitPrice, result.Decisions[0].ComparisonValue);
            Assert.Equal(PricedMaterialUnitPrice, result.Plan.TotalCoinCost);
            Assert.DoesNotContain(result.Plan.Steps, s => s.ItemId == BreastplateItemId && s.Source == AcquisitionSource.BuyFromVendor);

            // Committing the offer collapsed the whole tree into one step.
            // Crafting expands it, so every Gift the player must obtain is
            // named with a quantity - including the vendor-only one, whose
            // own acquisition is itself unpriced and which must not vanish
            // just because no coin figure can be put on it.
            Assert.Contains(result.Plan.Steps, s => s.ItemId == CraftableGiftItemId);
            var vendorOnly = result.Plan.Steps.Single(s => s.ItemId == VendorOnlyGiftItemId);
            Assert.Equal(1, vendorOnly.Quantity);
            Assert.True(vendorOnly.VendorHasBarterItemCost);
        }

        [Fact]
        public void ReportedCase_TheVendorRouteIsStillOfferedRatherThanDropped()
        {
            // Losing the comparison must not hide the route: a player
            // already holding the Gifts still wants to see that Lyhr sells
            // it, and the VENDOR pill has to stay clickable.
            var solver = new PlanSolver();

            var result = solver.Solve(BuildReportedTree(), BuildReportedPrices(), BuildReportedOffers());

            Assert.True(result.Decisions[0].CanBuyVendor);
            Assert.True(result.Decisions[0].BuyFromVendorCostBreakdown.IsAvailable);

            // Fallback tier, so the breakdown carries no DecisionValue -
            // the offer's cost is explicitly not a comparable coin figure.
            Assert.Null(result.Decisions[0].BuyFromVendorCostBreakdown.DecisionValue);
        }

        [Fact]
        public void ReportedCase_ManualOverrideStillCommitsTheVendorRoute_WithBothGiftsReportedUnpriced()
        {
            // The user's own choice still wins, and when it does, the two
            // Gift lines reach the decision as quantities with NO gold
            // value - the unpriceable cost is carried, not flattened away
            // and forgotten.
            var overrides = new Dictionary<int, AcquisitionSource> { { 0, AcquisitionSource.BuyFromVendor } };
            var solver = new PlanSolver();

            var result = solver.Solve(
                BuildReportedTree(), BuildReportedPrices(), BuildReportedOffers(),
                PriceBasis.InstantBuy, overrides);

            var decision = result.Decisions[0];
            Assert.Equal(AcquisitionSource.BuyFromVendor, decision.Source);

            var unpriced = decision.VendorItemCosts.Where(l => !l.GoldValue.HasValue).ToList();
            Assert.Equal(2, unpriced.Count);
            Assert.Contains(unpriced, l => l.ItemId == CraftableGiftItemId);
            Assert.Contains(unpriced, l => l.ItemId == VendorOnlyGiftItemId);

            // And the step says so, so no consumer reads the coin figure as
            // the whole cost.
            var step = result.Plan.Steps.Single(s => s.ItemId == BreastplateItemId);
            Assert.True(step.VendorHasBarterItemCost);
        }

        [Fact]
        public void UnpricedItemLine_NeverWins_EvenWithAZeroCoinPart()
        {
            // The starkest form: the offer costs no coin at all, only an
            // untradeable token. Zero is the lowest number there is, and
            // ranking on it would beat every craft route in existence.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 5), Leaf(23, 1, "Currency"))); // 5 x 100 = 500 real
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { Offer(1, "test-token-only", ItemLine(AccountBoundTokenItemId, 1)) } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(500, result.Decisions[0].TotalCost);
            Assert.True(result.Decisions[0].CanBuyVendor);
        }

        [Fact]
        public void UnpricedItemLine_StillCommitted_WhenNoCraftRouteExistsAtAll()
        {
            // The rule stops an unpriced offer WINNING a comparison; it
            // must not stop it being the answer when it is the only one.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { Offer(1, "test-only-route", ItemLine(AccountBoundTokenItemId, 3)) } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(3, result.Decisions[0].VendorItemCosts.Single().Quantity);
            Assert.Null(result.Decisions[0].VendorItemCosts.Single().GoldValue);
        }

        [Fact]
        public void UnpricedItemLine_StillCommitted_WhenCraftIsExcludedByTheForceBuyPrePass()
        {
            // Craft excluded for this node means there is no craft route to
            // prefer, so the offer is committed exactly as before - the
            // guard must key on a craft route EXISTING, not on the offer's
            // shape alone.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 5), Leaf(23, 1, "Currency")));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { Offer(1, "test-token-only", ItemLine(AccountBoundTokenItemId, 1)) } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy,
                overrides: null, currencyValuation: null,
                forceBuyOnlyNodeIds: new HashSet<int> { 0 });

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(0, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void TpPricedItemLine_IsMoney_AndStillRanksOnTheCoinPart()
        {
            // The boundary in the other direction: an Item cost line WITH a
            // TP price folds into the offer's real coin cost, so nothing is
            // omitted by it and the offer ranks normally. Here the offer
            // costs 1 x 100 of a priced item plus an unvalued wallet
            // currency (which is what keeps it fallback-tier), and beats
            // the 500 craft.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 5), Leaf(23, 1, "Currency"))); // 5 x 100 = 500 real
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
                { 3, new ItemPrice { ItemId = 3, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    1,
                    new List<VendorOffer>
                    {
                        Offer(
                            1, "test-priced-item-plus-currency",
                            ItemLine(3, 1),
                            new CostLine { Type = "Currency", Id = 24, Count = 10 }),
                    }
                },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(100, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void ValuedBarterLineOnAFallbackOffer_CountsAsOmitted_BecauseTheValuationIsDiscarded()
        {
            // A fallback offer discards ALL valuation, never partially
            // retains it (EvaluateVendorOffers' allValued gate). So a
            // valued token sitting on an offer that is fallback-tier for a
            // DIFFERENT reason - here an unvalued wallet currency - still
            // contributes nothing to the coin part, and the guard must fire
            // on it rather than trusting a valuation that was thrown away.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 5), Leaf(23, 1, "Currency"))); // 5 x 100 = 500 real
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    1,
                    new List<VendorOffer>
                    {
                        Offer(
                            1, "test-valued-token-plus-unvalued-currency",
                            ItemLine(AccountBoundTokenItemId, 1),
                            new CostLine { Type = "Currency", Id = 24, Count = 10 }),
                    }
                },
            };
            var valuation = new CurrencyValuation(
                null, null, new Dictionary<int, long> { { AccountBoundTokenItemId, 40 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(500, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void OverflowingValuation_DemotesTheOfferToFallback_RatherThanDiscardingTheRoute()
        {
            // The mirror-image defect: an offer whose comparison value
            // overflows used to be dropped from BOTH tiers, reporting "no
            // vendor route" for a route that exists and whose 100-copper
            // coin part is perfectly real. The two valuation-accumulation
            // loops beside it already demote instead of dropping; this now
            // matches them. Item 1 has no other source, so a dropped offer
            // shows up as UnknownSource.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 100, 24, 1) } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 24, long.MaxValue } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(100, result.Decisions[0].TotalCost);
            Assert.Equal(100, result.Decisions[0].ComparisonValue);
        }
    }
}
