using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;
using static TaimisToolbench.Tests.Helpers.VendorOfferBuilders;

namespace TaimisToolbench.Tests.Services
{
    public class PlanSolverHomesteadTierTests
    {
        // --- Homestead Refinement efficiency tier gating (gw2e parity,
        // KNOWN-ISSUES #24) - the live defect fix: our seed already carries
        // all tier rows untagged, so before this gate the solver silently
        // assumed every account had every efficiency upgrade. ---
        private static VendorOffer HomesteadOffer(
            int outputItemId, int inputCount, int outputCount, int homesteadTier,
            int? weeklyCap = null, string merchantName = "Homestead Refinement\u2014Metal Forge")
        {
            return new VendorOffer
            {
                OfferId = $"homestead-{outputItemId}-{homesteadTier}-{inputCount}",
                OutputItemId = outputItemId,
                OutputCount = outputCount,
                CostLines = new List<CostLine>
                {
                    // Item id is arbitrary/unique per test; only its buy
                    // price (set by the caller) matters to the solver.
                    new CostLine { Type = "Item", Id = 900 + homesteadTier, Count = inputCount },
                },
                MerchantName = merchantName,
                WeeklyCap = weeklyCap,
                HomesteadTier = homesteadTier,
            };
        }

        [Fact]
        public void HomesteadOffer_DefaultTierZero_ExcludesHigherTierOffers()
        {
            // Metal Forge Iron Ore, matching the wiki-verified conversion
            // table exactly: tier0 4->2, tier1 2->2, tier2 1->1 (docs/
            // docs/research/m37-r1-homestead.md Section 2.2). Iron ore costs 1
            // coin each; tier2's 1-ore rate is far cheaper per unit of
            // output than tier0's 4-ore rate. Default (no homesteadTiers
            // argument -> HomesteadEfficiencyTiers.Default, tier 0 for
            // every material) must still pick the tier-0 row.
            var tree = Leaf(102205, 2); // Refined Homestead Metal, need 2
            var prices = new Dictionary<int, ItemPrice>
            {
                { 900, new ItemPrice { ItemId = 900, BuyInstant = 1 } }, // tier0 input (900+0)
                { 901, new ItemPrice { ItemId = 901, BuyInstant = 1 } }, // tier1 input (900+1)
                { 902, new ItemPrice { ItemId = 902, BuyInstant = 1 } }, // tier2 input (900+2),
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    102205, new List<VendorOffer>
                    {
                        HomesteadOffer(102205, inputCount: 4, outputCount: 2, homesteadTier: 0),
                        HomesteadOffer(102205, inputCount: 2, outputCount: 2, homesteadTier: 1),
                        HomesteadOffer(102205, inputCount: 1, outputCount: 1, homesteadTier: 2),
                    }
                },
            };
            var solver = new PlanSolver();

            var plan = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy).Plan;

            var step = Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, step.Source);
            // Tier0 offer: ceil(2/2)=1 purchase of 4 ore = 4 coin.
            Assert.Equal(4, step.TotalCost);
        }

        [Fact]
        public void HomesteadOffer_TierTwoConfigured_AdmitsCheaperHigherTierOffer()
        {
            var tree = Leaf(102205, 2);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 900, new ItemPrice { ItemId = 900, BuyInstant = 1 } },
                { 901, new ItemPrice { ItemId = 901, BuyInstant = 1 } },
                { 902, new ItemPrice { ItemId = 902, BuyInstant = 1 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    102205, new List<VendorOffer>
                    {
                        HomesteadOffer(102205, inputCount: 4, outputCount: 2, homesteadTier: 0),
                        HomesteadOffer(102205, inputCount: 2, outputCount: 2, homesteadTier: 1),
                        HomesteadOffer(102205, inputCount: 1, outputCount: 1, homesteadTier: 2),
                    }
                },
            };
            var tiers = new HomesteadEfficiencyTiers(new Dictionary<int, int>
            {
                { Gw2Constants.RefinedHomesteadMetalItemId, 2 },
            });
            var solver = new PlanSolver();

            var plan = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy,
                homesteadTiers: tiers).Plan;

            var step = Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, step.Source);
            // Tier2 offer: ceil(2/1)=2 purchases of 1 ore = 2 coin - cheaper
            // than tier0's 4 coin, and only reachable once tier 2 is
            // configured for Metal.
            Assert.Equal(2, step.TotalCost);
        }

        [Fact]
        public void HomesteadOffer_TierOneConfigured_AdmitsTierOneButNotTierTwo()
        {
            var tree = Leaf(102205, 2);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 900, new ItemPrice { ItemId = 900, BuyInstant = 1 } },
                { 901, new ItemPrice { ItemId = 901, BuyInstant = 1 } },
                { 902, new ItemPrice { ItemId = 902, BuyInstant = 1 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    102205, new List<VendorOffer>
                    {
                        HomesteadOffer(102205, inputCount: 4, outputCount: 2, homesteadTier: 0),
                        HomesteadOffer(102205, inputCount: 2, outputCount: 2, homesteadTier: 1),
                        HomesteadOffer(102205, inputCount: 1, outputCount: 1, homesteadTier: 2),
                    }
                },
            };
            var tiers = new HomesteadEfficiencyTiers(new Dictionary<int, int>
            {
                { Gw2Constants.RefinedHomesteadMetalItemId, 1 },
            });
            var solver = new PlanSolver();

            var plan = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy,
                homesteadTiers: tiers).Plan;

            var step = Assert.Single(plan.Steps);
            // Cheapest of {tier0, tier1} (tier2 excluded): tier1's 2-ore
            // rate (ceil(2/2)=1 purchase = 2 coin) beats tier0's 4 coin.
            Assert.Equal(2, step.TotalCost);
        }

        [Fact]
        public void HomesteadTierConfigured_ForDifferentMaterial_DoesNotAffectThisOne()
        {
            // Configuring Fiber's tier to 2 must not admit a higher-tier
            // Metal offer - the gate is per-material, not global.
            var tree = Leaf(102205, 2);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 900, new ItemPrice { ItemId = 900, BuyInstant = 1 } },
                { 902, new ItemPrice { ItemId = 902, BuyInstant = 1 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    102205, new List<VendorOffer>
                    {
                        HomesteadOffer(102205, inputCount: 4, outputCount: 2, homesteadTier: 0),
                        HomesteadOffer(102205, inputCount: 1, outputCount: 1, homesteadTier: 2),
                    }
                },
            };
            var tiers = new HomesteadEfficiencyTiers(new Dictionary<int, int>
            {
                { Gw2Constants.RefinedHomesteadFiberItemId, 2 },
            });
            var solver = new PlanSolver();

            var plan = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy,
                homesteadTiers: tiers).Plan;

            var step = Assert.Single(plan.Steps);
            Assert.Equal(4, step.TotalCost);
        }

        [Fact]
        public void NonHomesteadVendorOffer_UnaffectedByHomesteadTierSetting()
        {
            // A plain vendor offer with HomesteadTier == null (every
            // existing non-Homestead offer in the seed) must be completely
            // unaffected by any homesteadTiers configuration, at default or
            // otherwise - byte-identical to before this feature existed.
            var tree = Leaf(1, 2);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 1, new List<VendorOffer> { CoinVendorOffer(1, 5) } },
            };
            var tiers = HomesteadEfficiencyTiers.Default;
            var solver = new PlanSolver();

            var planDefault = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy).Plan;
            var planExplicit = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy, homesteadTiers: tiers).Plan;

            Assert.Equal(AcquisitionSource.BuyFromVendor, planDefault.Steps[0].Source);
            Assert.Equal(10, planDefault.TotalCoinCost);
            Assert.Equal(planDefault.TotalCoinCost, planExplicit.TotalCoinCost);
        }

        [Fact]
        public void ExordiumStyleTree_NoHomesteadOffersReachable_ByteIdenticalAtAnyTier()
        {
            // Regression guard mirroring the research report's own
            // BFS-verified finding: Exordium's tree reaches zero Homestead
            // Refinement materials, so a plan whose reachable offers carry
            // no HomesteadTier must be byte-identical regardless of the
            // configured tier setting. The non-Homestead offer on item 2
            // makes the solver actually walk the per-offer tier gate (a
            // null vendorOffers dict would skip it entirely). A small
            // synthetic tree stands in for the real (14k-recipe) Exordium
            // tree here; the real tree is checked via the offline Harness
            // per this milestone's manual verification step.
            var tree = Craftable(1, 1,
                Option(10, 1, 1, Leaf(2, 3), Leaf(3, 5)));
            var prices = new Dictionary<int, ItemPrice>
            {
                { 1, new ItemPrice { ItemId = 1, BuyInstant = 1000 } },
                { 2, new ItemPrice { ItemId = 2, BuyInstant = 10 } },
                { 3, new ItemPrice { ItemId = 3, BuyInstant = 20 } },
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { 2, new List<VendorOffer> { CoinVendorOffer(2, 4) } },
            };
            var solver = new PlanSolver();

            var planTier0 = solver.Solve(tree, prices, vendorOffers, PriceBasis.InstantBuy).Plan;
            var maxTiers = new HomesteadEfficiencyTiers(new Dictionary<int, int>
            {
                { Gw2Constants.RefinedHomesteadFiberItemId, 2 },
                { Gw2Constants.RefinedHomesteadMetalItemId, 2 },
                { Gw2Constants.RefinedHomesteadWoodItemId, 2 },
            });
            var planTier2 = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy, homesteadTiers: maxTiers).Plan;

            Assert.Equal(planTier0.TotalCoinCost, planTier2.TotalCoinCost);
            Assert.Equal(planTier0.Steps.Count, planTier2.Steps.Count);
            for (int i = 0; i < planTier0.Steps.Count; i++)
            {
                Assert.Equal(planTier0.Steps[i].Source, planTier2.Steps[i].Source);
                Assert.Equal(planTier0.Steps[i].TotalCost, planTier2.Steps[i].TotalCost);
            }
        }

        [Fact]
        public void NullHomesteadTier_OnMaterialOutput_IsAdmittedRegardlessOfConfiguredTier()
        {
            // Documents CURRENT, by-design behavior (not a bug to fix
            // here): EvaluateVendorOffers only gates on
            // `offer.HomesteadTier.HasValue` - a null tier is NEVER
            // excluded, even when OutputItemId is one of the three real
            // Homestead Refinement materials and even at the most
            // restrictive tier (0). Null is meant for the 21 one-time
            // "Upgrade" purchase rows the same merchant pages also sell
            // (tier-independent by design), NOT for a material-conversion
            // row - if a future wiki re-scrape ever mistagged a material
            // row with a null tier, this is exactly the runtime behavior
            // that would silently readmit it at every tier, reintroducing
            // the always-max-tier defect PR #57 fixed. The solver itself
            // has no way to catch that mistake; the data-integrity test
            // ShippedSeedFile_HomesteadRefinementMaterialRows_AllHaveNonNullTierInRange
            // (VendorOfferStoreTests) exists precisely because of the
            // runtime behavior pinned here.
            var tree = Leaf(Gw2Constants.RefinedHomesteadMetalItemId, 2);
            var prices = new Dictionary<int, ItemPrice>
            {
                { 900, new ItemPrice { ItemId = 900, BuyInstant = 1 } },
            };
            var untaggedMaterialOffer = new VendorOffer
            {
                OfferId = "untagged-material-offer",
                OutputItemId = Gw2Constants.RefinedHomesteadMetalItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Item", Id = 900, Count = 1 },
                },
                MerchantName = "Homestead Refinement\u2014Metal Forge",
                HomesteadTier = null,
            };
            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                {
                    Gw2Constants.RefinedHomesteadMetalItemId,
                    new List<VendorOffer> { untaggedMaterialOffer }
                },
            };
            // Tier 0 is the most restrictive setting - if the gate applied
            // to this offer, it would still be excluded here.
            var tierZero = new HomesteadEfficiencyTiers(new Dictionary<int, int>
            {
                { Gw2Constants.RefinedHomesteadMetalItemId, 0 },
            });
            var solver = new PlanSolver();

            var plan = solver.Solve(
                tree, prices, vendorOffers, PriceBasis.InstantBuy,
                homesteadTiers: tierZero).Plan;

            var step = Assert.Single(plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, step.Source);
            Assert.Equal(2, step.TotalCost);
        }
    }
}
