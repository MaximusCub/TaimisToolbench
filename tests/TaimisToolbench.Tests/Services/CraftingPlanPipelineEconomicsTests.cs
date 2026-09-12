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
    public class CraftingPlanPipelineEconomicsTests
    {
        // --- Sell-side economics ---
        [Fact]
        public async Task VendorOfferItemCost_OutsideTree_PriceFetchedAndOfferUsed()
        {
            // Regression (Gift of Glory): a vendor offer charging an ITEM
            // that appears nowhere in the recipe tree was skipped as
            // unpriceable because the cost item's TP price was never
            // fetched, leaving the target as UnknownSource.
            // No recipe and no TP price for target item 1. Cost item 999
            // (not in any tree) has a TP price of 2c.
            var builder = PipelineBuilder.Create()
                .WithPrice(999, buyUnitPrice: 1, sellUnitPrice: 2)
                .WithItem(1, "Gifted Item", "g.png")
                .WithItem(999, "Cost Token", "t.png");

            using (var tmp = new TempDirectory())
            {
                var tempDir = tmp.Path;
                var loader = new VendorOfferLoader();
                var store = new VendorOfferStore(tempDir, loader);
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-item-cost-outside-tree",
                        OutputItemId = 1,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Item", Id = 999, Count = 250 },
                        },
                        MerchantName = "Token Vendor",
                    },
                });

                var pipeline = builder.WithVendorOfferStore(store).Build();

                var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);

                Assert.Single(result.Plan.Steps);
                Assert.Equal(AcquisitionSource.BuyFromVendor, result.Plan.Steps[0].Source);
                // 250 x 2c (instant-buy basis) = 500
                Assert.Equal(500, result.Plan.TotalCoinCost);
            }
        }

        [Fact]
        public async Task Structured_TargetHasBuyOrders_ProfitFieldsComputed()
        {
            var pipeline = PipelineBuilder.BuildEconomicsPipeline(out var priceApi);
            // Target: buy orders at 400 (sell revenue), sell listings at 1000.
            // Ingredient: instant buy 100 -> craft cost 3x100=300 < buy 1000.
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(300, result.Plan.TotalCoinCost);
            Assert.Equal(400, result.TargetUnitSellPrice);
            // 400 - 20 (5%) - 40 (10%) = 340 net
            Assert.Equal(340, result.NetSaleValue);
            Assert.Equal(40, result.CraftingProfit);
            Assert.Equal(PriceBasis.InstantBuy, result.PriceBasis);
        }

        [Fact]
        public async Task Structured_NoBuyOrders_ProfitFieldsNull()
        {
            var pipeline = PipelineBuilder.BuildEconomicsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 0, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Null(result.TargetUnitSellPrice);
            Assert.Null(result.NetSaleValue);
            Assert.Null(result.CraftingProfit);
        }

        [Fact]
        public async Task Structured_RootRecipeOverproduces_RevenueCoversWholeBatch()
        {
            // Recipe outputs 5 per craft; requesting 1 still costs a full
            // craft, so all 5 produced units count as sellable revenue.
            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 5,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    Flags = new List<string> { "AutoLearned" },
                })
                .WithPrice(1, buyUnitPrice: 400, sellUnitPrice: 10000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png")
                .Build();

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // Craft cost: 3x100 = 300 for a batch of 5
            Assert.Equal(300, result.Plan.TotalCoinCost);
            Assert.Equal(5, result.SellableQuantity);
            // Revenue: 5 x 400 = 2000 total; -100 listing -200 exchange = 1700
            Assert.Equal(1700, result.NetSaleValue);
            Assert.Equal(1400, result.CraftingProfit);
        }

        [Fact]
        public async Task ResolveWithOverrides_LocalResolveFlipsDecisionAndEconomics()
        {
            var pipeline = PipelineBuilder.BuildEconomicsPipeline(out var priceApi);
            // Craft (300) beats buy (1000); target sells to orders at 400.
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var initial = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(initial.SolveContext);
            Assert.Equal(300, initial.Plan.TotalCoinCost);

            // Force the root to be bought instead
            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.CraftingTree.NodeId, AcquisitionSource.BuyFromTp },
            };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);

            Assert.Single(resolved.Plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, resolved.Plan.Steps[0].Source);
            Assert.Equal(1000, resolved.Plan.TotalCoinCost);
            // Economics recomputed: 340 net - 1000 cost = -660
            Assert.Equal(-660, resolved.CraftingProfit);
            // Context is carried forward for subsequent re-solves
            Assert.Same(initial.SolveContext, resolved.SolveContext);
            // Tree reflects the forced decision and availability
            Assert.Equal(CraftingDecision.BuyFromTp, resolved.CraftingTree.Decision);
            Assert.True(resolved.CraftingTree.CanCraft);
            Assert.True(resolved.CraftingTree.CanBuyTp);
        }

        // A local override re-solve must keep
        // carrying CharacterDisciplines forward from the generation-time
        // context (see PlanSolveContext.CharacterDisciplines' own doc
        // comment) - deleting the one-line passthrough in
        // ResolveWithOverrides still leaves the whole suite green without
        // this test, since only the leaf builder and the store were
        // previously covered.
        [Fact]
        public async Task ResolveWithOverrides_CarriesCharacterDisciplinesForward()
        {
            var pipeline = PipelineBuilder.BuildEconomicsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var snapshot = new AccountSnapshot
            {
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Weaponsmith", Rating = 500, Active = true },
                },
            };

            var initial = await pipeline.GenerateStructuredAsync(1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(initial.CharacterDisciplines);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.CraftingTree.NodeId, AcquisitionSource.BuyFromTp },
            };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);

            Assert.Same(initial.CharacterDisciplines, resolved.CharacterDisciplines);
        }

        // Companion null-snapshot case: a generation with no account data
        // must keep re-solving to a null CharacterDisciplines, never
        // fabricate one on a later override.
        [Fact]
        public async Task ResolveWithOverrides_NullSnapshot_CharacterDisciplinesStaysNull()
        {
            var pipeline = PipelineBuilder.BuildEconomicsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var initial = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.Null(initial.CharacterDisciplines);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.CraftingTree.NodeId, AcquisitionSource.BuyFromTp },
            };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);

            Assert.Null(resolved.CharacterDisciplines);
        }

        // CraftingPlanPipeline
        // assigns DailyCooldownItems at five hand-copied sites (mirroring
        // AcquisitionHints), none of which had a test pinning the seed
        // survives a GenerateStructuredAsync -> ResolveWithOverrides round
        // trip - a future refactor could silently drop one site. This
        // mirrors ResolveWithOverrides_CarriesCharacterDisciplinesForward's
        // shape immediately above, applied to the daily-cooldown seed
        // instead.
        [Fact]
        public async Task DailyCooldownItems_SurvivesGenerateStructuredAsync_AndResolveWithOverridesRoundTrip()
        {
            var seed = new Dictionary<int, DailyCooldownItem>
            {
                [2] = new DailyCooldownItem { ItemId = 2, PerDayCap = 1 },
            };

            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target Item", "target.png")
                .WithItem(2, "Ingredient", "ingredient.png")
                .WithDailyCooldownItems(seed)
                .Build();

            var initial = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.Same(seed, initial.DailyCooldownItems);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.CraftingTree.NodeId, AcquisitionSource.BuyFromTp },
            };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);

            Assert.Same(seed, resolved.DailyCooldownItems);
        }

        [Fact]
        public async Task BuildPresetOverrides_CraftAll_ReachesNodesUnderBoughtIntermediates()
        {
            // Item 1 <- recipe(2) <- recipe(3). Intermediate 2 is cheap to
            // buy, so the best path hides node 3 below a bought node; the
            // Craft All preset must still force-craft both levels.
            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithSearchResult(2, 20)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    Flags = new List<string> { "AutoLearned" },
                })
                .WithRecipe(new RawRecipe
                {
                    Id = 20,
                    OutputItemId = 2,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 3, Count = 2 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    Flags = new List<string> { "AutoLearned" },
                })
                // Buying 2 (50) beats crafting it (2x100=200); buying 1 (500)
                // loses to crafting-from-bought-2 (50)
                .WithPrice(1, buyUnitPrice: 10, sellUnitPrice: 500)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 50)
                .WithPrice(3, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Mid", "m.png")
                .WithItem(3, "Base", "b.png")
                .Build();

            var initial = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            // Baseline: craft 1 from bought 2 (50)
            Assert.Equal(50, initial.Plan.TotalCoinCost);

            var craftAll = CraftingPlanPipeline.BuildPresetOverrides(
                initial.SolveContext, AcquisitionSource.Craft);
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, craftAll);

            // Now both levels craft: cost = 2x100 for item 3
            Assert.Equal(200, resolved.Plan.TotalCoinCost);
            Assert.Contains(resolved.Plan.Steps,
                s => s.Source == AcquisitionSource.Craft && s.ItemId == 2);
            // Metadata must cover items surfaced only by the override
            // (regression: items under bought nodes were never fetched and
            // rendered as "Unknown Item")
            Assert.All(resolved.Plan.Steps,
                s => Assert.True(resolved.ItemMetadata.ContainsKey(s.ItemId)));

            // Buy All flips everything buyable to TP purchases
            var buyAll = CraftingPlanPipeline.BuildPresetOverrides(
                initial.SolveContext, AcquisitionSource.BuyFromTp);
            var bought = pipeline.ResolveWithOverrides(initial.SolveContext, buyAll);
            Assert.Single(bought.Plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, bought.Plan.Steps[0].Source);
        }

        [Fact]
        public async Task Structured_BuyOrderBasis_MaterialsCostedAtBuyOrders()
        {
            var pipeline = PipelineBuilder.BuildEconomicsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            // Ingredient: instant 100, buy order 10 -> craft cost 30 at basis.
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.BuyOrder);

            Assert.Equal(30, result.Plan.TotalCoinCost);
            Assert.Equal(PriceBasis.BuyOrder, result.PriceBasis);
            Assert.Equal(310, result.CraftingProfit);
        }
    }
}
