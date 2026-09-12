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
    public class CraftingPlanPipelineTests
    {
        [Fact]
        public async Task SimpleCraftableItem_ProducesPlanWithStepsAndMetadata()
        {
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
                .Build();

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.NotNull(result.Plan);
            Assert.True(result.Plan.Steps.Count > 0);
            Assert.NotNull(result.ItemMetadata);
            Assert.True(result.ItemMetadata.ContainsKey(1));
            Assert.Equal("Target Item", result.ItemMetadata[1].Name);
        }

        [Fact]
        public async Task LeafOnlyItem_ProducesSingleBuyStep()
        {
            // No recipe for item 1
            var pipeline = PipelineBuilder.Create()
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 500)
                .WithItem(1, "Copper Ore", "copper.png")
                .Build();

            var result = await pipeline.GenerateStructuredAsync(1, 5, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Single(result.Plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, result.Plan.Steps[0].Source);
            Assert.Equal(5, result.Plan.Steps[0].Quantity);
            Assert.True(result.ItemMetadata.ContainsKey(1));
        }

        [Fact]
        public async Task AllStepItemIds_HaveMetadataPopulated()
        {
            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                        new RawIngredient { Type = "Item", Id = 3, Count = 2 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 10000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithPrice(3, buyUnitPrice: 20, sellUnitPrice: 200)
                .WithItem(1, "Final Item", "final.png")
                .WithItem(2, "Part A", "a.png")
                .WithItem(3, "Part B", "b.png")
                .Build();

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            foreach (var step in result.Plan.Steps)
            {
                Assert.True(result.ItemMetadata.ContainsKey(step.ItemId),
                    $"Missing metadata for item {step.ItemId}");
            }
        }

        // KNOWN-ISSUES #31/api-degradation F4: a failing learned-recipes fetch
        // must degrade to null (the same supported "unknown known-recipe
        // status" state PlanResultBuilder already handles) rather than
        // aborting an otherwise fully-successful, fully-priced plan.
        [Fact]
        public async Task GenerateStructuredAsync_LearnedRecipeFetchFails_DegradesToNull_DoesNotAbortPlan()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.ThrowOnGet = true; // has permission by default, but the fetch itself fails

            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target Item", "target.png")
                .WithItem(2, "Ingredient", "ingredient.png")
                .WithAccountRecipeClient(accountClient)
                .Build();

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // The plan itself must be fully built despite the failure.
            Assert.NotNull(result.Plan);
            Assert.True(result.Plan.Steps.Count > 0);

            // Recipe 10 is not inherently-available (no MysticForge/
            // Achievement/Merchant discipline) and learnedRecipeIds is
            // null, so IsMissing must be null ("unknown"), not crash and
            // not silently claim the recipe is known.
            var recipe = result.RequiredRecipes.FirstOrDefault(r => r.RecipeId == 10);
            Assert.NotNull(recipe);
            Assert.Null(recipe.IsMissing);
        }

        // KNOWN-ISSUES #31/api-degradation F4 (audit follow-up): the same
        // catch-and-degrade-to-null fix above is duplicated verbatim in
        // GenerateStructuredMultiAsync (the 2+ item path reached via the
        // IReadOnlyList<PlanRequestItem> overload below); only the
        // single-item path above had a regression test. Mirrors that test
        // exactly, just with two roots, so a future refactor that
        // reverts/diverges the multi-item copy of this catch block fails a
        // test instead of silently aborting an otherwise fully-priced
        // multi-item plan.
        [Fact]
        public async Task GenerateStructuredMultiAsync_LearnedRecipeFetchFails_DegradesToNull_DoesNotAbortPlan()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.ThrowOnGet = true; // has permission by default, but the fetch itself fails

            var pipeline = PipelineBuilder.TwoRootTree()
                .WithAccountRecipeClient(accountClient)
                .Build();

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 1 },
                new PlanRequestItem { ItemId = 2, Quantity = 1 },
            };

            var result = await pipeline.GenerateStructuredAsync(items, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // The multi-item plan itself must be fully built despite the
            // learned-recipe fetch failure.
            Assert.NotNull(result.Plan);
            Assert.True(result.Plan.Steps.Count > 0);
            Assert.Equal(2, result.MultiItemRoots.Count);

            // Neither recipe is inherently-available and learnedRecipeIds
            // is null, so IsMissing must be null ("unknown") for both
            // roots' recipes, not crash and not silently claim either
            // recipe is known.
            var recipeA = result.RequiredRecipes.FirstOrDefault(r => r.RecipeId == 10);
            var recipeB = result.RequiredRecipes.FirstOrDefault(r => r.RecipeId == 20);
            Assert.NotNull(recipeA);
            Assert.NotNull(recipeB);
            Assert.Null(recipeA.IsMissing);
            Assert.Null(recipeB.IsMissing);
        }

        // A player can buy and use a recipe sheet, then generate again,
        // without leaving the game. The second plan must report the recipe
        // as known. Nothing between the pipeline and /v2/account/recipes
        // may hold an older answer, so the client is asked once per
        // generation and its newest answer is the one that is annotated.
        [Fact]
        public async Task GenerateStructuredAsync_RecipeLearnedBetweenGenerations_SecondPlanSeesIt()
        {
            var accountClient = new InMemoryAccountRecipeClient();

            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target Item", "target.png")
                .WithItem(2, "Ingredient", "ingredient.png")
                .WithAccountRecipeClient(accountClient)
                .Build();

            var first = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            accountClient.AddLearnedRecipe(10);

            var second = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(2, accountClient.GetCallCount);

            var firstRecipe = first.RequiredRecipes.FirstOrDefault(r => r.RecipeId == 10);
            Assert.NotNull(firstRecipe);
            Assert.True(firstRecipe.IsMissing);

            var secondRecipe = second.RequiredRecipes.FirstOrDefault(r => r.RecipeId == 10);
            Assert.NotNull(secondRecipe);
            Assert.False(secondRecipe.IsMissing);
        }

        // A caller generating several plans in a row fetches the ids once
        // and hands them to each generation. The Crafting Ranker does this:
        // it solves two plans per watchlist row, and one fetch answers the
        // whole refresh.
        [Fact]
        public async Task GenerateStructuredAsync_SuppliedLearnedRecipeIds_AsksTheAccountClientOnce()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.AddLearnedRecipe(10);

            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target Item", "target.png")
                .WithItem(2, "Ingredient", "ingredient.png")
                .WithAccountRecipeClient(accountClient)
                .Build();

            var learned = await pipeline.GetLearnedRecipeIdsAsync(CancellationToken.None);
            Assert.Equal(1, accountClient.GetCallCount);

            var first = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy, learnedRecipeIds: learned);
            var second = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy, learnedRecipeIds: learned);

            Assert.Equal(1, accountClient.GetCallCount);

            // The supplied ids still drive the annotation on both plans.
            Assert.False(first.RequiredRecipes.FirstOrDefault(r => r.RecipeId == 10)?.IsMissing);
            Assert.False(second.RequiredRecipes.FirstOrDefault(r => r.RecipeId == 10)?.IsMissing);
        }

        // The batch fetch degrades the way the per-generation one does, so a
        // Ranker refresh survives a failing /v2/account/recipes with every
        // recipe state unknown instead of no plans at all.
        [Fact]
        public async Task GetLearnedRecipeIdsAsync_ClientFails_ReturnsNull()
        {
            var accountClient = new InMemoryAccountRecipeClient { ThrowOnGet = true };

            var pipeline = PipelineBuilder.Create()
                .WithAccountRecipeClient(accountClient)
                .Build();

            var learned = await pipeline.GetLearnedRecipeIdsAsync(CancellationToken.None);

            Assert.Null(learned);
            Assert.Equal(1, accountClient.GetCallCount);
        }

        // No permission means no request at all, and the plan reports every
        // recipe state as unknown.
        [Fact]
        public async Task GetLearnedRecipeIdsAsync_NoPermission_MakesNoRequest()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetHasPermission(false);

            var pipeline = PipelineBuilder.Create()
                .WithAccountRecipeClient(accountClient)
                .Build();

            var learned = await pipeline.GetLearnedRecipeIdsAsync(CancellationToken.None);

            Assert.Null(learned);
            Assert.Equal(0, accountClient.GetCallCount);
        }

        [Fact]
        public async Task MissingItemMetadata_StillProducesValidPlan()
        {
            // No recipe, and no metadata for item 1.
            var pipeline = PipelineBuilder.Create()
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 500)
                .Build();

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.NotNull(result.Plan);
            Assert.Single(result.Plan.Steps);
            Assert.False(result.ItemMetadata.ContainsKey(1));
        }

        [Fact]
        public async Task VendorOfferAvailable_SolverUsesIt()
        {
            // Vendor offers 1x item for 100 coin - cheaper than the 500 TP price.
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
                        OfferId = "test-vendor",
                        OutputItemId = 1,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 100 },
                        },
                        MerchantName = "Test NPC",
                    },
                });

                // No recipe for item 1
                var pipeline = PipelineBuilder.Create()
                    .WithPrice(1, buyUnitPrice: 500, sellUnitPrice: 5000)
                    .WithItem(1, "Vendor Item", "vendor.png")
                    .WithVendorOfferStore(store)
                    .Build();

                var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);

                Assert.Single(result.Plan.Steps);
                Assert.Equal(AcquisitionSource.BuyFromVendor, result.Plan.Steps[0].Source);
                Assert.Equal(100, result.Plan.TotalCoinCost);
            }
        }

        // SEASONAL VENDOR TIP:
        // real-path proof that SeasonalOfferFilter.ExcludeSeasonal is
        // actually wired into the SOLVE call site, not just unit-tested in
        // isolation (SeasonalOfferFilterTests). An item whose ONLY vendor
        // offer is seasonal must fall back to the TP, never BuyFromVendor
        // - and, with the festival active, the excluded offer should still
        // surface as a SeasonalVendorTips entry (the informational Notes
        // row this whole exclusion exists to make room for).
        [Fact]
        public async Task SeasonalOnlyVendorOffer_ExcludedFromSolve_FallsBackToTp_SurfacesAsTip()
        {
            // The ONLY vendor offer for item 1 is a seasonal (Halloween)
            // offer at 100 coin - cheaper than the 500 TP price, so if the
            // exclusion were NOT wired at this solve call site the solver
            // would wrongly pick BuyFromVendor here.
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
                        OfferId = "test-seasonal-vendor",
                        OutputItemId = 1,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 100 },
                        },
                        MerchantName = "Candy Corn Vendor (Weekly)",
                        SeasonalFestival = Gw2Constants.HalloweenFestivalName,
                    },
                });

                // No recipe for item 1
                var pipeline = PipelineBuilder.Create()
                    .WithPrice(1, buyUnitPrice: 500, sellUnitPrice: 500)
                    .WithItem(1, "Festival Item", "festival.png")
                    .WithVendorOfferStore(store)
                    .WithActiveFestivalNames(() => new[] { Gw2Constants.HalloweenFestivalName })
                    .Build();

                var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);

                // Never chosen as BuyFromVendor - the seasonal-only offer
                // was excluded from the solver's own candidate set.
                Assert.Single(result.Plan.Steps);
                Assert.Equal(AcquisitionSource.BuyFromTp, result.Plan.Steps[0].Source);
                Assert.Equal(500, result.Plan.TotalCoinCost);

                // Surfaces as an informational tip instead - the excluded
                // offer is still cheaper than the plan's own TP price.
                var tip = Assert.Single(result.SeasonalVendorTips);
                Assert.Equal(1, tip.ItemId);
                Assert.Equal(Gw2Constants.HalloweenFestivalName, tip.Festival);
                Assert.Equal(100, tip.OfferUnitCost);
                Assert.Equal(500, tip.PlanUnitPrice);
            }
        }

        [Fact]
        public async Task NullVendorStore_PipelineStillWorks()
        {
            // WithVendorOfferStore is deliberately not called: Build() passes
            // the null store this test is about.
            var pipeline = PipelineBuilder.Create()
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 500)
                .WithItem(1, "Item", "icon.png")
                .Build();

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.NotNull(result.Plan);
            Assert.Single(result.Plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromTp, result.Plan.Steps[0].Source);
        }
    }
}
