using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Barter offers: a vendor offer whose cost includes an untradeable
    /// Item line. Measured over ref/vendor_offers.json plus
    /// /v2/commerce/prices and /v2/items: 1,032 distinct item ids appear as
    /// vendor cost lines, 654 of them have no Trading Post price at all,
    /// and those 654 account for 10,551 of the 21,489 item cost-line
    /// usages (49%).
    ///
    /// <para>
    /// These pin the whole ladder the solver walks for such a line: no TP
    /// price and no valuation is FALLBACK-tier (incomparable with coin,
    /// still a real acquisition route), a valuation makes it comparable,
    /// and a TP price still wins over a valuation because a TP price is
    /// money rather than an opinion.
    /// </para>
    /// </summary>
    public class PlanSolverBarterItemValuationTests
    {
        // 43992 Black Lion Claim Ticket and 86694 Black Lion Statuette:
        // the two most-used unpriced barter items in
        // ref/vendor_offers.json, and both deliberately left out of
        // BarterItemDecisionDefaults, so a valuation only ever reaches
        // these tests from the test's own explicit input.
        private const int BarterTokenItemId = 43992;
        private const int SecondBarterTokenItemId = 86694;

        private static VendorOffer BarterOffer(
            int outputItemId, int barterItemId, int barterCount, int coinCost = 0, int outputCount = 1)
        {
            var costLines = new List<CostLine>();
            if (coinCost > 0)
            {
                costLines.Add(new CostLine
                {
                    Type = "Currency",
                    Id = Gw2Constants.CoinCurrencyId,
                    Count = coinCost,
                });
            }

            costLines.Add(new CostLine { Type = "Item", Id = barterItemId, Count = barterCount });

            return new VendorOffer
            {
                OfferId = $"test-barter-{outputItemId}-{barterItemId}-{barterCount}",
                OutputItemId = outputItemId,
                OutputCount = outputCount,
                CostLines = costLines,
                MerchantName = "Barter Vendor",
                Locations = new List<string>(),
            };
        }

        private static Dictionary<int, IReadOnlyList<VendorOffer>> Offers(params VendorOffer[] offers)
        {
            var byOutput = new Dictionary<int, IReadOnlyList<VendorOffer>>();
            foreach (var group in offers.GroupBy(o => o.OutputItemId))
            {
                byOutput[group.Key] = group.ToList();
            }

            return byOutput;
        }

        [Fact]
        public void UnpricedItemCostLine_NoValuation_OnlyRouteAvailable()
        {
            // Nothing else can supply item 1: no TP price and no recipe. The
            // barter offer is the only acquisition route there is, so the
            // solver must surface it rather than report "not sold or
            // crafted".
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = Offers(BarterOffer(1, BarterTokenItemId, 5));
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.True(result.Decisions[0].CanBuyVendor);

            // Fallback tier: the barter token has no honest coin equivalent,
            // so the committed coin cost is the offer's coin part only (0
            // here) and nothing about the token is folded into it.
            Assert.Equal(0, result.Plan.Steps.Single(s => s.ItemId == 1).TotalCost);
            Assert.Null(result.Decisions[0].VendorItemCosts.Single().GoldValue);
        }

        [Fact]
        public void UnpricedItemCostLine_NoValuation_AgainstDearerTp()
        {
            // The fallback tier must never win a comparison against a real
            // coin cost: 5 account-bound tokens have no coin equivalent, so
            // a 1000-copper TP price is the only comparable option.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var vendorOffers = Offers(BarterOffer(1, BarterTokenItemId, 5));
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Decisions[0].Source);
            Assert.True(result.Decisions[0].CanBuyVendor);
        }

        [Fact]
        public void UnpricedItemCostLine_MixedWithCoin_AgainstDearerTp()
        {
            // A 30-copper coin line on the same offer as the unpriced token:
            // the coin part alone would beat the 1000-copper TP price, and
            // must NOT be allowed to, because the token beside it has no
            // coin equivalent. Dropping the token line and comparing on 30
            // copper would understate the offer; the fallback tier is what
            // keeps the whole offer out of the comparison instead.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var vendorOffers = Offers(BarterOffer(1, BarterTokenItemId, 5, coinCost: 30));
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Decisions[0].Source);
            Assert.True(result.Decisions[0].CanBuyVendor);
        }

        [Fact]
        public void UnpricedItemCostLine_SecondFullyPricedOfferStillFound()
        {
            // Two offers for the same item, one unpriceable: the priceable
            // one must still be found. Guards the `break`/`continue` pair -
            // an unpriceable offer aborts its OWN evaluation, never the loop.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var vendorOffers = Offers(
                BarterOffer(1, BarterTokenItemId, 5),
                new VendorOffer
                {
                    OfferId = "test-coin-alt",
                    OutputItemId = 1,
                    OutputCount = 1,
                    CostLines = new List<CostLine>
                    {
                        new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 40 },
                    },
                    MerchantName = "Coin Vendor",
                    Locations = new List<string>(),
                });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(40, result.Plan.Steps.Single(s => s.ItemId == 1).TotalCost);
        }

        [Fact]
        public void UnpricedItemCostLine_WithValuation_IsComparableAndBeatsExpensiveTp()
        {
            // 5 tokens valued at 40 copper each = 200 copper of comparison
            // value, against a 1000-copper TP price.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var vendorOffers = Offers(BarterOffer(1, BarterTokenItemId, 5));
            var valuation = new CurrencyValuation(
                null, null, new Dictionary<int, long> { { BarterTokenItemId, 40 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(200, result.Decisions[0].ComparisonValue);

            // DECISION-ONLY: the valuation tips the comparison but is never
            // spent - the plan still commits only the offer's real coin part,
            // and the token line still carries no gold value.
            Assert.Equal(0, result.Plan.Steps.Single(s => s.ItemId == 1).TotalCost);
            Assert.Null(result.Decisions[0].VendorItemCosts.Single().GoldValue);
        }

        [Fact]
        public void UnpricedItemCostLine_WithValuation_LosesToCheaperTp()
        {
            // Same valuation, cheaper TP: the valued offer competes on equal
            // footing and loses, rather than winning by default.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 150 } },
            };
            var vendorOffers = Offers(BarterOffer(1, BarterTokenItemId, 5));
            var valuation = new CurrencyValuation(
                null, null, new Dictionary<int, long> { { BarterTokenItemId, 40 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Decisions[0].Source);
        }

        [Fact]
        public void UnpricedItemCostLine_WithValuation_CoinPartStillCommitted()
        {
            // A mixed coin + barter offer: the coin part is real money and
            // must be committed to the plan; only the token's 200 copper of
            // valuation is comparison-only.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var vendorOffers = Offers(BarterOffer(1, BarterTokenItemId, 5, coinCost: 30));
            var valuation = new CurrencyValuation(
                null, null, new Dictionary<int, long> { { BarterTokenItemId, 40 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(230, result.Decisions[0].ComparisonValue);
            Assert.Equal(30, result.Plan.Steps.Single(s => s.ItemId == 1).TotalCost);
        }

        [Fact]
        public void PartlyValuedOffer_StaysFallbackTier()
        {
            // One valued token and one unvalued one on the same offer: a
            // partial valuation is not a valuation. Mixing them would price
            // the offer at only the part we can value, understating it.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var offer = new VendorOffer
            {
                OfferId = "test-barter-partly-valued",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Item", Id = BarterTokenItemId, Count = 5 },
                    new CostLine { Type = "Item", Id = SecondBarterTokenItemId, Count = 1 },
                },
                MerchantName = "Barter Vendor",
                Locations = new List<string>(),
            };
            var valuation = new CurrencyValuation(
                null, null, new Dictionary<int, long> { { BarterTokenItemId, 40 } });
            var solver = new PlanSolver();

            var result = solver.Solve(
                tree, prices, Offers(offer), PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Decisions[0].Source);
            Assert.True(result.Decisions[0].CanBuyVendor);
        }

        [Fact]
        public void TpPricedItemCostLine_IgnoresValuation()
        {
            // A barter item that DOES have a TP price is money, not an
            // opinion: the TP price is folded into the offer's real coin
            // cost and the valuation must not displace it. 5 * 10 = 50
            // copper of real coin, not 5 * 40 = 200 of valuation.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
                { BarterTokenItemId, new ItemPrice { ItemId = BarterTokenItemId, BuyInstant = 10 } },
            };
            var vendorOffers = Offers(BarterOffer(1, BarterTokenItemId, 5));
            var valuation = new CurrencyValuation(
                null, null, new Dictionary<int, long> { { BarterTokenItemId, 40 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(50, result.Decisions[0].ComparisonValue);
            Assert.Equal(50, result.Plan.Steps.Single(s => s.ItemId == 1).TotalCost);
            Assert.Equal(50, result.Decisions[0].VendorItemCosts.Single().GoldValue);
        }

        [Fact]
        public void ValuedOffer_WinsOverAFallbackOfferForTheSameItem()
        {
            // A comparable (valued) offer and a fallback (unvalued) one for
            // the same item: the comparable one is committed, because a
            // fallback-tier offer is a last resort and never outranks an
            // offer whose whole cost can be compared.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = Offers(
                BarterOffer(1, BarterTokenItemId, 5),
                BarterOffer(1, SecondBarterTokenItemId, 1));
            var valuation = new CurrencyValuation(
                null, null, new Dictionary<int, long> { { BarterTokenItemId, 40 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(200, result.Decisions[0].ComparisonValue);
            Assert.Equal(BarterTokenItemId, result.Decisions[0].VendorItemCosts.Single().ItemId);
        }

        [Fact]
        public void FallbackTieBreak_ComparesUnitsOnlyForTheSameBarterItem()
        {
            // Two coin-free fallback offers costing the SAME token: fewer
            // units wins, the like-for-like comparison the currency side
            // already makes. The 8-unit offer is listed first, so a plain
            // first-listed-wins would keep it.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = Offers(
                BarterOffer(1, BarterTokenItemId, 8),
                BarterOffer(1, BarterTokenItemId, 3));
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(3, result.Decisions[0].VendorItemCosts.Single().Quantity);
        }

        [Fact]
        public void FallbackTieBreak_NeverComparesUnitsAcrossDifferentBarterItems()
        {
            // 8 of one token against 1 of a different one: there is no
            // exchange rate between them, so the counts must not be ranked
            // and the first-listed offer stands.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var vendorOffers = Offers(
                BarterOffer(1, BarterTokenItemId, 8),
                BarterOffer(1, SecondBarterTokenItemId, 1));
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(BarterTokenItemId, result.Decisions[0].VendorItemCosts.Single().ItemId);
        }

        [Fact]
        public void FallbackTieBreak_NeverComparesABarterItemAgainstACurrency()
        {
            // 8 tokens against 1 unvalued wallet currency unit. An item id
            // and a currency id are different id spaces, so a bare id match
            // must not be mistaken for a like-for-like comparison. The
            // currency offer wins here because the fallback tier ranks a
            // barter line behind a priced one before it ever compares unit
            // counts, so the two counts are never weighed against each
            // other.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var currencyOffer = new VendorOffer
            {
                OfferId = "test-barter-currency-twin",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = BarterTokenItemId, Count = 1 },
                },
                MerchantName = "Currency Vendor",
                Locations = new List<string>(),
            };
            var solver = new PlanSolver();

            var result = solver.Solve(
                tree, prices, Offers(BarterOffer(1, BarterTokenItemId, 8), currencyOffer));

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Null(result.Decisions[0].VendorItemCosts);
            Assert.Equal(
                BarterTokenItemId,
                result.Decisions[0].VendorCurrencyCosts.Single().Id);
        }

        [Fact]
        public void BarterOffer_LosesToAPricedOffer_EvenWhenItsCoinPartIsLower()
        {
            // The barter offer charges 1 copper plus 5 untradeable tokens;
            // the other charges 500 copper and nothing else. The token line
            // carries no price, so it adds nothing to the barter offer's
            // coin total, and ranking on coin alone read 1 as cheaper than
            // 500. It is not cheaper: the tokens' cost is unknown, not zero.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var pricedOffer = new VendorOffer
            {
                OfferId = "test-priced-500",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 500 },
                },
                MerchantName = "Coin Vendor",
                Locations = new List<string>(),
            };
            var solver = new PlanSolver();

            var result = solver.Solve(
                tree,
                prices,
                Offers(BarterOffer(1, BarterTokenItemId, 5, coinCost: 1), pricedOffer));

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Null(result.Decisions[0].VendorItemCosts);
            Assert.Equal(500, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void BarterOffer_StillWins_WhenEveryOfferCarriesABarterLine()
        {
            // The rule above only orders barter offers behind priced ones.
            // With no priced offer to lose to, the cheaper barter offer is
            // still chosen and the route is still offered.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>();
            var solver = new PlanSolver();

            var result = solver.Solve(
                tree,
                prices,
                Offers(
                    BarterOffer(1, BarterTokenItemId, 5, coinCost: 90),
                    BarterOffer(1, SecondBarterTokenItemId, 5, coinCost: 10)));

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(
                SecondBarterTokenItemId,
                result.Decisions[0].VendorItemCosts.Single().ItemId);
        }

        [Fact]
        public void CuratedDefault_IsNotAppliedByTheSolverItself()
        {
            // Defaults are folded in exactly once, by
            // ModuleSettings.GetEffectiveCurrencyValuation via
            // CurrencyValuation.WithDefaults - never inside the solver. A
            // bare Solve with no valuation must therefore leave even a
            // curated barter item (19925 Obsidian Shard) fallback-tier.
            var tree = Leaf(1, 1);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1_000_000 } },
            };
            var vendorOffers = Offers(BarterOffer(1, 19925, 1));
            var solver = new PlanSolver();

            var bare = solver.Solve(tree, prices, vendorOffers);
            Assert.Equal(AcquisitionSource.BuyFromTp, bare.Decisions[0].Source);

            var withDefaults = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy, null,
                CurrencyValuation.WithDefaults(CurrencyValuation.None));

            Assert.Equal(AcquisitionSource.BuyFromVendor, withDefaults.Decisions[0].Source);
            Assert.Equal(
                BarterItemDecisionDefaults.Defaults[19925].CopperPerUnit,
                withDefaults.Decisions[0].ComparisonValue);
        }
    }
}
