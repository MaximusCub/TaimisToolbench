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
    /// Craft/vendor comparability-parity fix: a craft recipe carrying a
    /// Currency-type ingredient with NO user-provided valuation is now
    /// FALLBACK-tier, exactly like VendorBatchSolver.EvaluateVendorOffers
    /// already treats a vendor offer with an unvalued non-coin currency
    /// line - it never competes on coin cost against a comparable option
    /// (TP buy, comparable vendor offer, or another comparable recipe) in
    /// PickCheapest, but is still offered (CanCraft/the CRAFT pill stay
    /// true - a standing guarantee) and used as a last resort when nothing
    /// coin-comparable exists at all. See PlanSolver.Evaluate's recipe loop
    /// and its terminal fallback branch for the implementation; see
    /// PlanSolverCurrencyValuationTests for the pre-existing VALUED-currency
    /// coverage (unaffected by this fix - a valued currency ingredient
    /// already made its recipe comparable before and still does).
    /// </summary>
    public class PlanSolverCraftVendorComparabilityTests
    {
        [Fact]
        public void FallbackCraft_StillOffered_CanCraftTrueEvenWhenAutoDecisionPicksBuy()
        {
            // The CanCraft guarantee, explicitly isolated: a fallback-tier recipe
            // (unvalued currency) never wins the automatic decision against
            // a comparable buy price, but CanCraft/the CRAFT pill must
            // still report true - the option is offered, just not
            // auto-selected.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 1), Leaf(23, 3, "Currency")));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 50 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Decisions[0].Source);
            Assert.True(result.Decisions[0].CanCraft);
        }

        [Fact]
        public void ComparableRecipe_ChosenOverCheaperFallbackRecipe_OnSameNode()
        {
            // Item 1 has two recipes: recipe 10 is fallback-tier (unvalued
            // currency) and numerically cheaper (30); recipe 11 is
            // comparable (no currency ingredient) and numerically more
            // expensive (80) but still beats the far pricier 1000 buy
            // price. The comparable recipe must win - a cheaper-looking
            // fallback recipe on the SAME node must never be preferred
            // just because its raw number is lower.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 3), Leaf(23, 5, "Currency")), // 3*10=30, unvalued currency
                Option(11, 1, 1, Leaf(3, 1))); // 1*80=80, fully comparable
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 10 } },
                { 3, new ItemPrice { ItemId = 3, BuyInstant = 80 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(11, result.Decisions[0].RecipeId);
            Assert.Equal(80, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void MultipleFallbackRecipes_PicksCheapestFallback_TieBreaksOnLowestRecipeId()
        {
            // Two fallback-tier recipes (both carry an unvalued currency
            // ingredient) - the cheaper one wins the fallback-tier
            // tie-break, exactly like the pre-existing single-tier
            // tie-break did before this fix, now scoped to the fallback
            // tier specifically. Item 1 has no buy price, so the fallback
            // craft is used as the last resort.
            var tree = Craftable(1, 1,
                Option(11, 1, 1, Leaf(2, 3), Leaf(23, 1, "Currency")), // 3*100=300
                Option(10, 1, 1, Leaf(3, 1), Leaf(23, 1, "Currency"))); // 1*50=50 (cheaper)
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
                { 3, new ItemPrice { ItemId = 3, BuyInstant = 50 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(10, result.Decisions[0].RecipeId);
            Assert.Equal(50, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void ValuedCurrencyCraft_CompetesAsComparable_AndWinsAgainstVendorAndBuy()
        {
            // A fully-valued currency ingredient keeps its recipe
            // comparable (unaffected by this fix - see
            // PlanSolverCurrencyValuationTests for the pre-existing
            // coverage of this rule alone). Comparison cost 80 (50 real +
            // 3*10 valuation) beats both the comparable vendor offer (300)
            // and the buy price (1000); the committed real cost excludes
            // the valuation.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 1), Leaf(23, 3, "Currency")));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 50 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { CoinVendorOffer(1, 300) } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 23, 10 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(50, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void ValuedCurrencyIngredient_ComparableCraftWins_PlanTotalCoinCostExcludesValuation()
        {
            // Decision-only valuation principle (repo invariant: pricing
            // preserves multiple sources and avoids invalid currency
            // comparisons): a currency valuation may decide the
            // comparison, but must never inflate any user-visible coin
            // total, including plan.TotalCoinCost itself - not just the
            // individual step's TotalCost (see
            // PlanSolverCurrencyValuationTests for the step-level and
            // vendor-descendant coverage of this same rule).
            var evOption = new RecipeOption
            {
                RecipeId = 10,
                OutputCount = 1,
                CraftsNeeded = 1,
                Ingredients = new List<RecipeNode>
                {
                    Leaf(2, 1),
                    Leaf(23, 3, "Currency"),
                },
            };
            var tree = Craftable(1, 1, evOption);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 50 } },
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 23, 10 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, null, PriceBasis.InstantBuy, null, valuation);
            var plan = result.Plan;

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            // Comparison value (80 = 50 + 3*10) tipped the decision, but the
            // displayed plan total is the real coin part only (50).
            Assert.Equal(50, plan.TotalCoinCost);
        }

        [Fact]
        public void AllFallback_NoComparableOptionsExist_CheaperFallbackCraftWins()
        {
            // Nothing comparable exists anywhere for item 1 (no buy price,
            // no comparable vendor offer, no comparable recipe) - both
            // remaining candidates are fallback-tier. The cheaper of the
            // two (craft, 200) wins - "someone must still be picked",
            // mirroring VendorBatchSolver's own fallback-tier precedent,
            // now extended to a craft-vs-vendor fallback tie-break.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 2), Leaf(23, 5, "Currency"))); // 2*100=200
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 300, 24, 10) } }, // 300 coin + unvalued currency
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var plan = result.Plan;

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(200, result.Decisions[0].TotalCost);
            Assert.Equal(200, plan.TotalCoinCost);
        }

        [Fact]
        public void AllFallback_NoComparableOptionsExist_CheaperFallbackVendorWins()
        {
            // Same shape as the craft-wins case above, but the vendor's
            // fallback coin part (100) is now cheaper than the craft
            // fallback's real cost (200) - vendor wins.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 2), Leaf(23, 5, "Currency"))); // 2*100=200
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 100, 24, 10) } }, // 100 coin + unvalued currency
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var plan = result.Plan;

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(100, result.Decisions[0].TotalCost);
            Assert.Equal(100, plan.TotalCoinCost);
        }

        [Fact]
        public void AllFallback_ExactCoinTieBetweenCraftAndVendor_VendorWins()
        {
            // An exact tie between the two fallback candidates' real/coin
            // portions keeps vendor - the identical tie-break rule
            // PickCheapest's own comparable-tier craft/vendor comparison
            // already uses (see that method's doc comment), extended here
            // to the fallback tier.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 2), Leaf(23, 5, "Currency"))); // 2*100=200
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 200, 24, 10) } }, // 200 coin, ties craft
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(200, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void FallbackCraft_ExcludedByForceBuyOnlyPrePass_NeverWinsAsLastResort()
        {
            // A node force-flagged "craft excluded" (OwnedMaterialsForceBuyPrePass)
            // must not fall back to craft either, even when nothing else is
            // available - consistent with craft being excluded from every
            // automatic path for that node, not just the primary
            // comparison.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 1), Leaf(23, 3, "Currency")));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 50 } },
                // Item 1 has no buy price and no vendor offer.
            };
            var solver = new PlanSolver();
            var forceBuyOnly = new HashSet<int> { 0 }; // item 1's NodeId (DFS root)

            var result = solver.Solve(
                tree, prices, vendorOffers: null, priceBasis: PriceBasis.InstantBuy,
                overrides: null, currencyValuation: null, forceBuyOnlyNodeIds: forceBuyOnly);

            Assert.Equal(AcquisitionSource.UnknownSource, result.Decisions[0].Source);
            // CanCraft still reflects true feasibility (a recipe exists),
            // matching gw2e's own manual pill still being able to override
            // the pre-pass.
            Assert.True(result.Decisions[0].CanCraft);
        }

        [Fact]
        public void ManualOverride_ForcesFallbackCraft_EvenWhenAutoDecisionPickedBuy()
        {
            // A per-node override forcing Craft must still work when the
            // only recipe is fallback-tier (unvalued currency) - manual
            // overrides always win over the automatic comparison,
            // mirroring VendorBatchSolver's own
            // comparable-first-else-fallback override precedence for
            // BuyFromVendor.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 1), Leaf(23, 3, "Currency")));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 50 } },
            };
            var overrides = new Dictionary<int, AcquisitionSource> { { 0, AcquisitionSource.Craft } };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, null, PriceBasis.InstantBuy, overrides).Plan;

            var craftStep = plan.Steps.Single(s => s.Source == AcquisitionSource.Craft);
            Assert.Equal(1, craftStep.ItemId);
            Assert.Equal(50, craftStep.TotalCost); // real coin only, currency excluded
            Assert.Equal(50, plan.TotalCoinCost);
        }

        [Fact]
        public void AmalgamatedRiftEssenceShaped_IdenticalUnvaluedCurrencies_CraftWinsOnRealItemCostDifference()
        {
            // Models the parity gap's own motivating example: an item with
            // no TP listing, craftable from currencies (all unvalued,
            // identical amounts on both the craft recipe and the vendor
            // offer) plus a priced material - crafted using FEWER of that
            // material than the vendor offer needs. Both the craft recipe
            // and the vendor offer become fallback-tier (same unvalued
            // currencies on each side), so neither can win the primary
            // comparison - but the terminal fallback tie-break (cheaper of
            // the two known/priced portions wins) still lets craft win
            // HONESTLY on its real cost advantage, without ever needing to
            // know what the currencies are worth. The currencies are
            // ignored identically on both sides (decision-only valuation
            // principle), not "cancelled" by any new comparison math - see
            // KNOWN-ISSUES #44 for the documented limitation this leaves for
            // a case where the priced-material amounts are ALSO identical
            // (a genuine tie the fallback branch cannot break any more
            // finely than its existing coin-tie rule already does).
            var craftOption = new RecipeOption
            {
                RecipeId = 10,
                OutputCount = 1,
                CraftsNeeded = 1,
                Ingredients = new List<RecipeNode>
                {
                    Leaf(2, 50), // 50x ecto
                    Leaf(101, 3, "Currency"),
                    Leaf(102, 5, "Currency"),
                    Leaf(103, 1, "Currency"),
                },
            };
            var tree = Craftable(1, 1, craftOption);
            var prices = new Dictionary<int, ItemPrice>
            {
                // Item 1 (the ARE-shaped item) intentionally has no TP price.
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 10 } },
            };
            var vendorOffer = new VendorOffer
            {
                OfferId = "test-are-vendor",
                OutputItemId = 1,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = 101, Count = 3 },
                    new CostLine { Type = "Currency", Id = 102, Count = 5 },
                    new CostLine { Type = "Currency", Id = 103, Count = 1 },
                    new CostLine { Type = "Item", Id = 2, Count = 60 }, // 60x ecto
                },
                MerchantName = "Test Vendor",
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { vendorOffer } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);
            var plan = result.Plan;

            // 50 ecto @ 10 = 500 (craft) vs 60 ecto @ 10 = 600 (vendor) -
            // craft wins on the real cost difference, currencies ignored on
            // both sides.
            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(500, plan.TotalCoinCost);
            Assert.Contains(plan.Steps, s => s.Source == AcquisitionSource.Craft && s.ItemId == 1);
        }

        // --- Regression: findings on the fix above. See KNOWN-ISSUES #44
        // for the full writeup. ---
        [Fact]
        public void CoinTypedCurrencyIngredient_IsRealCoin_NeverDemotesRecipeToFallback()
        {
            // Finding: GW2 v2/recipes ingredients can carry IngredientType
            // "Currency" tagged with the COIN currency id itself
            // (Gw2Constants.CoinCurrencyId) - that is literal copper, not a
            // "currency" needing a user valuation at all (CurrencyValuation
            // hard-throws if ever keyed on the coin id). Without a
            // dedicated coin-currency branch mirroring
            // EvaluateVendorOffers' identical routing, this ingredient
            // could never be valued and would unconditionally demote its
            // recipe to the fallback tier - which would make this recipe
            // lose to its own real 1000 TP buy price (since a fallback-tier
            // craft never competes in PickCheapest's primary comparison),
            // even though its true real cost (10 + 50 = 60) is far cheaper.
            // Item ids deliberately avoid 1 (Gw2Constants.CoinCurrencyId)
            // to keep the coin-currency ingredient's id visually distinct
            // from the target item's own id in this test.
            var tree = Craftable(6, 1,
                Option(10, 1, 1,
                    Leaf(7, 1),
                    Leaf(Gw2Constants.CoinCurrencyId, 50, "Currency")));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 6, new ItemPrice { ItemId = 6, BuyInstant = 1000 } },
                { 7, new ItemPrice { ItemId = 7, BuyInstant = 10 } },
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(60, result.Decisions[0].TotalCost);

            // Fourth-site follow-up finding: plan.TotalCoinCost only summed
            // BuyFromTp/BuyFromVendor steps, so the 50-coin ingredient here
            // (folded into decision.TotalCost/the Recipe Tree/the Craft
            // shopping-list row above) never reached the Total Cost summary
            // - it showed 10 (just item 7's TP buy) instead of the true 60.
            // Locks the fix routing coin-typed currency ingredients into
            // totalCoinCost via currencyMap.
            Assert.Equal(60, result.Plan.TotalCoinCost);
            Assert.DoesNotContain(result.Plan.CurrencyCosts, c => c.CurrencyId == Gw2Constants.CoinCurrencyId);
        }

        [Fact]
        public void RecipeWithBothValuedAndUnvaluedCurrency_DiscardsValuation_RanksOnRealCostOnly()
        {
            // Findings 1 + 5 (cross-tier scale mismatch / mixed valued+
            // unvalued currency contamination): a recipe mixing ONE valued
            // currency ingredient (30, valued at 100/unit x2 = 200
            // valuation copper) with a SECOND, unvalued currency ingredient
            // (31) is fallback-tier overall (the unvalued line demotes it),
            // and must rank on its REAL cost (5, just the priced item) -
            // never on a number contaminated by the other line's discarded
            // valuation (205) - exactly mirroring EvaluateVendorOffers'
            // allValued gate, which discards ALL valuation the instant any
            // line is unvalued rather than partially retaining it. The
            // competing fallback vendor offer's real coin part (100) sits
            // BETWEEN the honest real cost (5) and the contaminated number
            // (205), so this test fails under either bug: it would only
            // pass today if both the within-recipe valuation leak (finding
            // 5) AND the cross-tier valuation-vs-real scale mismatch
            // (finding 1) are fixed together.
            var tree = Craftable(1, 1,
                Option(10, 1, 1,
                    Leaf(2, 1),
                    Leaf(30, 2, "Currency"),
                    Leaf(31, 1, "Currency")));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 5 } },
                // Item 1 intentionally has no TP price.
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 100, 32, 10) } }, // 100 real coin + unvalued currency
            };
            var valuation = new CurrencyValuation(new Dictionary<int, long> { { 30, 100 } });
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy, null, valuation);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[0].Source);
            Assert.Equal(5, result.Decisions[0].TotalCost);
        }

        [Fact]
        public void FallbackTaintPropagatesThroughAncestorCraft_NeverLaundersHiddenCurrencyCost()
        {
            // Finding 2 (transitive gap): item 2 crafts from a priced item
            // plus an unvalued currency and has no TP price/vendor offer of
            // its own, so it resolves via the terminal fallback to Craft
            // (real cost 10) - fallback-tier. Item 1 crafts from item 2
            // ALONE (no currency ingredient of its own) and DOES have a
            // real 500 TP price. Without propagating item 2's fallback-tier
            // taint up into item 1's own recipe, item 1 would be classified
            // fully COMPARABLE at "10" (item 2's returned ComparisonValue)
            // and would silently beat its real 500 buy price - laundering
            // item 2's hidden currency cost one Craft level up, exactly the
            // asymmetry this whole fix exists to close. With propagation,
            // item 1's own recipe is fallback-tier too, so it correctly
            // loses to its real, fully-known 500 TP buy - while still
            // being offered (CanCraft stays true).
            var tree = Craftable(1, 1,
                Option(10, 1, 1,
                    Craftable(2, 1,
                        Option(20, 1, 1,
                            Leaf(5, 1),
                            Leaf(40, 1, "Currency")))));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 500 } },
                { 5, new ItemPrice { ItemId = 5, BuyInstant = 10 } },
                // Item 2 intentionally has no TP price and no vendor offer.
            };
            var solver = new PlanSolver();

            // DFS NodeIds: item1=0, item2=1.
            var result = solver.Solve(tree, prices);

            Assert.Equal(AcquisitionSource.Craft, result.Decisions[1].Source);
            Assert.Equal(10, result.Decisions[1].TotalCost);

            Assert.Equal(AcquisitionSource.BuyFromTp, result.Decisions[0].Source);
            Assert.Equal(500, result.Decisions[0].TotalCost);
            Assert.True(result.Decisions[0].CanCraft); // still offered, just not auto-picked
        }

        [Fact]
        public void AllFallback_VendorZeroCoinPart_BeatsHigherRealCraftCost_DocumentedLimitation()
        {
            // Finding 4 (flagged, documented rather than fixed - see
            // KNOWN-ISSUES #44): the terminal fallback tie-break ranks purely
            // on each side's REAL coin part (the finding-1 fix above), the
            // SAME heuristic EvaluateVendorOffers' own fallback-vs-fallback
            // ranking already uses (coin part only -
            // currency is unvalued/unknowable on both sides, so there is no
            // sounder common unit). A vendor offer whose coin part is very
            // low - here, 0, entirely currency-paid - therefore beats a
            // craft fallback with a materially higher real coin cost, even
            // though the vendor's TRUE total cost (including its own huge
            // unvalued currency line) is unknown and could well be higher.
            // This is not new unsoundness introduced by extending fallback
            // ranking to craft: it is the identical heuristic already
            // accepted for vendor-vs-vendor fallback ranking (two
            // different-currency fallback offers are already ranked by
            // coin part alone, with no exchange rate available), now
            // visible in a new pairing. "Fixing" it would mean inventing a
            // currency comparison the repo invariant (avoid inventing
            // currency exchange rates) forbids. Scoped to CURRENCY: the
            // same shape with an unvalued BARTER ITEM line is not a
            // limitation and no longer ranks this way - see
            // PlanSolverUnpricedBarterOfferTests.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 5), Leaf(23, 1, "Currency"))); // 5*100=500 real
            var prices = new Dictionary<int, ItemPrice>
            {
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 100 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { MixedVendorOffer(1, 0, 24, 500000) } }, // 0 coin + huge unvalued currency
            };
            var solver = new PlanSolver();

            var result = solver.Solve(tree, prices, vendorOffers);

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Decisions[0].Source);
            Assert.Equal(0, result.Decisions[0].TotalCost);
        }
    }
}
