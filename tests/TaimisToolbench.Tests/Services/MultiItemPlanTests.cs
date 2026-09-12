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
    /// <summary>
    /// The synthetic
    /// wrapper pipeline (RecipeService.BuildMultiItemTreeAsync ->
    /// CraftingPlanPipeline.GenerateStructuredAsync(IReadOnlyList
    /// &lt;PlanRequestItem&gt;, ...) -> PlanSolver -> CraftingTreeBuilder),
    /// exercised end to end via the same InMemory fakes the rest of the
    /// pipeline test suite uses - real production code paths throughout,
    /// no Blish references.
    /// </summary>
    public class MultiItemPlanTests
    {
        [Fact]
        public async Task GenerateStructuredAsync_SingleEntryList_MatchesLegacySingleItemCall()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(1, 10);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(1, buyUnitPrice: 5000, sellUnitPrice: 10000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1, "Target", "t.png");
            itemApi.AddItem(2, "Ingredient", "i.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());

            var legacy = await pipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            var viaList = await pipeline.GenerateStructuredAsync(
                new List<PlanRequestItem> { new PlanRequestItem { ItemId = 1, Quantity = 1 } },
                null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            // Same plan-level totals
            Assert.Equal(legacy.Plan.TotalCoinCost, viaList.Plan.TotalCoinCost);
            Assert.Equal(legacy.Plan.Steps.Count, viaList.Plan.Steps.Count);
            for (int i = 0; i < legacy.Plan.Steps.Count; i++)
            {
                Assert.Equal(legacy.Plan.Steps[i].ItemId, viaList.Plan.Steps[i].ItemId);
                Assert.Equal(legacy.Plan.Steps[i].Source, viaList.Plan.Steps[i].Source);
                Assert.Equal(legacy.Plan.Steps[i].Quantity, viaList.Plan.Steps[i].Quantity);
                Assert.Equal(legacy.Plan.Steps[i].TotalCost, viaList.Plan.Steps[i].TotalCost);
                Assert.Equal(legacy.Plan.Steps[i].RecipeId, viaList.Plan.Steps[i].RecipeId);
            }

            // Same result-model tree, one node at a time
            AssertTreesEqual(legacy.CraftingTree, viaList.CraftingTree);

            // Same derived required-disciplines/recipes
            Assert.Equal(legacy.RequiredDisciplines.Count, viaList.RequiredDisciplines.Count);
            Assert.Equal(legacy.RequiredRecipes.Count, viaList.RequiredRecipes.Count);

            // The ComputePerItemEconomics/
            // ComputeMaterialOpportunityCost extraction must not change the
            // single-item sell-side economics fields either - both entry
            // points call the SAME (refactored) ApplySellSideEconomics.
            // Item 1's own price (buyUnitPrice: 5000) gives it a live
            // SellInstant, so these fields are genuinely populated here,
            // not just both-null.
            Assert.NotNull(legacy.NetSaleValue);
            Assert.Equal(legacy.PriceBasis, viaList.PriceBasis);
            Assert.Equal(legacy.SellableQuantity, viaList.SellableQuantity);
            Assert.Equal(legacy.TargetUnitSellPrice, viaList.TargetUnitSellPrice);
            Assert.Equal(legacy.NetSaleValue, viaList.NetSaleValue);
            Assert.Equal(legacy.CraftingProfit, viaList.CraftingProfit);
            Assert.Equal(legacy.MaterialOpportunityCost, viaList.MaterialOpportunityCost);

            // No wrapper metadata leaks into a single-item result reached
            // through the list overload - it short-circuited straight to
            // the untouched single-item method, exactly like gw2e's own
            // `if (r.length === 1) return r[0]`.
            Assert.Null(viaList.MultiItemRoots);
            Assert.Null(viaList.RequestedItems);
            Assert.NotNull(viaList.CraftingTree);
        }

        /// <summary>
        /// Pins the dispatcher invariant SellSideEconomics.ApplyForPlanShape
        /// relies on: a single-entry list request routes to the single-item
        /// path, so the solved tree root carries the real item id - never
        /// Gw2Constants.MultiItemWrapperItemId - and the result keeps the
        /// single-item shape. This is what makes the sentinel check agree
        /// with the old `items == null` generation-time dispatch.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_SingleEntryList_RoutesToSingleItemShape()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(1, 10);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(1, buyUnitPrice: 5000, sellUnitPrice: 10000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1, "Target", "t.png");
            itemApi.AddItem(2, "Ingredient", "i.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi));

            var result = await pipeline.GenerateStructuredAsync(
                new List<PlanRequestItem> { new PlanRequestItem { ItemId = 1, Quantity = 2 } },
                null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            // Single-item path: real ids in the solve context, no wrapper
            // sentinel anywhere ApplyForPlanShape could see one.
            Assert.NotNull(result.SolveContext);
            Assert.NotNull(result.SolveContext.Tree);
            Assert.NotEqual(Gw2Constants.MultiItemWrapperItemId, result.SolveContext.Tree.Id);
            Assert.Equal(1, result.SolveContext.TargetItemId);
            Assert.Equal(2, result.SolveContext.Quantity);

            // Single-shape result: no batch rollup fields.
            Assert.Null(result.MultiItemRoots);
            Assert.Null(result.RequestedItems);
            Assert.NotNull(result.CraftingTree);
        }

        private static void AssertTreesEqual(CraftingTreeNode expected, CraftingTreeNode actual)
        {
            Assert.Equal(expected.ItemId, actual.ItemId);
            Assert.Equal(expected.NodeId, actual.NodeId);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Quantity, actual.Quantity);
            Assert.Equal(expected.Decision, actual.Decision);
            Assert.Equal(expected.SubtreeCost, actual.SubtreeCost);
            Assert.Equal(expected.UnitCost, actual.UnitCost);
            Assert.Equal(expected.RecipeId, actual.RecipeId);
            Assert.Equal(expected.Children.Count, actual.Children.Count);
            for (int i = 0; i < expected.Children.Count; i++)
            {
                AssertTreesEqual(expected.Children[i], actual.Children[i]);
            }
        }

        /// <summary>
        /// Two items each need 2 of a shared item (500) that is buyable
        /// ONLY from a bulk vendor offer (5 units for 20 coin, no TP
        /// price). Solved independently, each item's own ceil(2/5)=1
        /// purchase costs 20 coin, 40 total. Merged under the wrapper, the
        /// combined demand is 4 (2+2), ceil(4/5)=1 purchase - 20 coin total,
        /// not 40 - proving the merge-then-ceil path
        /// (FinalizeVendorBatches) applies across item roots, not just
        /// within one item's own tree.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_TwoItems_SharedBulkVendorMaterial_SingleCeilAcrossBoth()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(100, 110);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 110,
                OutputItemId = 100,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 500, Count = 2 },
                },
            });
            recipeApi.AddSearchResult(200, 210);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 210,
                OutputItemId = 200,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 500, Count = 2 },
                },
            });

            // No TP price for 100, 200 (force-craft, they have a recipe) or
            // for 500 (only the vendor offer prices it).
            var priceApi = new InMemoryPriceApiClient();

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(100, "Item A", "a.png");
            itemApi.AddItem(200, "Item B", "b.png");
            itemApi.AddItem(500, "Shared Material", "m.png");

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
                        OfferId = "bulk-material",
                        OutputItemId = 500,
                        OutputCount = 5,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 20 },
                        },
                        MerchantName = "Test NPC",
                    },
                });

                var pipeline = new CraftingPlanPipeline(
                    new RecipeService(recipeApi),
                    new TradingPostService(priceApi),
                    new PlanSolver(),
                    new ItemMetadataService(itemApi),
                    store);

                // Contrast: solving each item alone costs 20 coin each (40 total).
                var alone100 = await pipeline.GenerateStructuredAsync(
                    100, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
                var alone200 = await pipeline.GenerateStructuredAsync(
                    200, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
                Assert.Equal(20, alone100.Plan.TotalCoinCost);
                Assert.Equal(20, alone200.Plan.TotalCoinCost);

                var items = new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 100, Quantity = 1 },
                    new PlanRequestItem { ItemId = 200, Quantity = 1 },
                };

                var result = await pipeline.GenerateStructuredAsync(
                    items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

                var vendorStep = Assert.Single(
                    result.Plan.Steps.Where(s => s.ItemId == 500 && s.Source == AcquisitionSource.BuyFromVendor));
                Assert.Equal(4, vendorStep.Quantity);
                Assert.Equal(20, vendorStep.TotalCost);
                Assert.Equal(20, result.Plan.TotalCoinCost);

                // Both craft steps present, merged into one plan.
                Assert.Contains(result.Plan.Steps, s => s.ItemId == 100 && s.Source == AcquisitionSource.Craft);
                Assert.Contains(result.Plan.Steps, s => s.ItemId == 200 && s.Source == AcquisitionSource.Craft);

                // The synthetic wrapper never surfaces anywhere in the result.
                Assert.DoesNotContain(result.Plan.Steps, s => s.ItemId == Gw2Constants.MultiItemWrapperItemId);
                Assert.Null(result.CraftingTree);
                Assert.NotNull(result.MultiItemRoots);
                Assert.Equal(2, result.MultiItemRoots.Count);
                Assert.All(result.MultiItemRoots, r => Assert.NotEqual(Gw2Constants.MultiItemWrapperItemId, r.ItemId));
                Assert.Equal(100, result.MultiItemRoots[0].ItemId);
                Assert.Equal(200, result.MultiItemRoots[1].ItemId);
                Assert.DoesNotContain(result.RequiredRecipes, r => r.OutputItemId == Gw2Constants.MultiItemWrapperItemId);

                Assert.NotNull(result.RequestedItems);
                Assert.Equal(2, result.RequestedItems.Count);
                Assert.Equal(100, result.RequestedItems[0].ItemId);
                Assert.Equal(200, result.RequestedItems[1].ItemId);
            }
        }

        private static CraftingPlanPipeline BuildTwoIndependentItemsPipeline(
            out InMemoryPriceApiClient priceApi)
        {
            var recipeApi = new InMemoryRecipeApiClient();
            // Item 300 <- recipe 310 <- item 301 (priced ingredient)
            recipeApi.AddSearchResult(300, 310);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 310,
                OutputItemId = 300,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 301, Count = 1 },
                },
            });
            // Item 400 <- recipe 410 <- item 401 (priced ingredient)
            recipeApi.AddSearchResult(400, 410);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 410,
                OutputItemId = 400,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 401, Count = 1 },
                },
            });

            priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(301, buyUnitPrice: 100, sellUnitPrice: 100);
            priceApi.AddPrice(401, buyUnitPrice: 200, sellUnitPrice: 200);
            // Both finished items ALSO have their own (much higher) TP
            // price, so craft (100 / 200) wins by default, but a manual
            // BuyFromTp override on either root is feasible and actually
            // changes its decision - needed to prove an override targeting
            // one root's NodeId is scoped correctly and does not leak into
            // the other root.
            priceApi.AddPrice(300, buyUnitPrice: 5000, sellUnitPrice: 5000);
            priceApi.AddPrice(400, buyUnitPrice: 5000, sellUnitPrice: 5000);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(300, "Item C", "c.png");
            itemApi.AddItem(301, "Ingredient C", "ic.png");
            itemApi.AddItem(400, "Item D", "d.png");
            itemApi.AddItem(401, "Ingredient D", "id.png");

            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());
        }

        [Fact]
        public async Task ResolveWithOverrides_MultiItem_OverrideOnOneRootOnlyAffectsThatRoot()
        {
            var pipeline = BuildTwoIndependentItemsPipeline(out _);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 300, Quantity = 1 },
                new PlanRequestItem { ItemId = 400, Quantity = 1 },
            };

            var initial = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.Craft, initial.MultiItemRoots[0].Decision);
            Assert.Equal(CraftingDecision.Craft, initial.MultiItemRoots[1].Decision);

            // Force root B (item 400) to buy instead of craft; root A
            // (item 300) is untouched by this override.
            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.MultiItemRoots[1].NodeId, AcquisitionSource.BuyFromTp },
            };

            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);

            Assert.Equal(2, resolved.MultiItemRoots.Count);
            Assert.Equal(CraftingDecision.BuyFromTp, resolved.MultiItemRoots[1].Decision);
            Assert.Equal(CraftingDecision.Craft, resolved.MultiItemRoots[0].Decision);
            // Root A's own child ingredient is unaffected too - the override
            // only ever touched root B's NodeId.
            Assert.Equal(CraftingDecision.BuyFromTp, resolved.MultiItemRoots[0].Children[0].Decision);

            Assert.Null(resolved.CraftingTree);
            Assert.NotNull(resolved.RequestedItems);
            Assert.Equal(2, resolved.RequestedItems.Count);
        }

        [Fact]
        public async Task ResolveWithOverrides_MultiItem_IgnoredItemId_ZeroesCostAcrossBothRoots()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            // Both items 300 and 400 share the SAME ingredient item 301.
            recipeApi.AddSearchResult(300, 310);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 310,
                OutputItemId = 300,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 301, Count = 2 },
                },
            });
            recipeApi.AddSearchResult(400, 410);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 410,
                OutputItemId = 400,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 301, Count = 3 },
                },
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(301, buyUnitPrice: 100, sellUnitPrice: 100);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(300, "Item C", "c.png");
            itemApi.AddItem(400, "Item D", "d.png");
            itemApi.AddItem(301, "Shared Ingredient", "s.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 300, Quantity = 1 },
                new PlanRequestItem { ItemId = 400, Quantity = 1 },
            };

            var initial = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            // 2x100 + 3x100 = 500 coin before Ignore.
            Assert.Equal(500, initial.Plan.TotalCoinCost);

            var ignored = new HashSet<int> { 301 };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, null, ignored);

            // Ignoring item 301 tree-wide zeroes its cost under BOTH roots,
            // matching gw2e's "Ignore marks every occurrence of that item id,
            // tree-wide" semantics extended across multiple selected items.
            Assert.Equal(0, resolved.Plan.TotalCoinCost);
            Assert.Empty(resolved.Plan.Steps.Where(s => s.ItemId == 301));
            Assert.True(resolved.MultiItemRoots[0].Children[0].IsIgnored);
            Assert.True(resolved.MultiItemRoots[1].Children[0].IsIgnored);
        }

        /// <summary>
        /// Item 600's own TP buy price is cheaper than crafting it, so a
        /// standalone single-item solve buys it instead of crafting -
        /// verifies the multi-item wrapper does not silently force-craft
        /// every selected root: each item root gets EXACTLY the same
        /// craft-vs-buy treatment it would get as a standalone tree
        /// (PlanSolver.Evaluate has no root-only special case at
        /// all, so this holds structurally, not via any new force-craft
        /// code).
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_PerRootDecision_MatchesStandaloneSingleItemSolve()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            // Item 600: craft cost (5 x 100 = 500) is more expensive than
            // its own TP buy price (50) - standalone solve buys it.
            recipeApi.AddSearchResult(600, 610);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 610,
                OutputItemId = 600,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 601, Count = 5 },
                },
            });
            // Item 700: no TP price of its own - always force-crafts.
            recipeApi.AddSearchResult(700, 710);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 710,
                OutputItemId = 700,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 701, Count = 1 },
                },
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(600, buyUnitPrice: 50, sellUnitPrice: 50);
            priceApi.AddPrice(601, buyUnitPrice: 100, sellUnitPrice: 100);
            priceApi.AddPrice(701, buyUnitPrice: 10, sellUnitPrice: 10);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(600, "Item E", "e.png");
            itemApi.AddItem(601, "Ingredient E", "ie.png");
            itemApi.AddItem(700, "Item F", "f.png");
            itemApi.AddItem(701, "Ingredient F", "if.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());

            var standalone600 = await pipeline.GenerateStructuredAsync(
                600, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            Assert.Equal(CraftingDecision.BuyFromTp, standalone600.CraftingTree.Decision);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 600, Quantity = 1 },
                new PlanRequestItem { ItemId = 700, Quantity = 1 },
            };

            var batch = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.BuyFromTp, batch.MultiItemRoots[0].Decision);
            Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[1].Decision);
        }

        /// <summary>
        /// Every prior multi-item test passed snapshot=null, so
        /// InventoryReducer.Reduce's shared consumption pool - created once per
        /// GenerateStructuredMultiAsync call and walked depth-first through the
        /// wrapper's N item-root ingredients in request order, see
        /// InventoryReducer.ReduceNodeSourced's own doc comment - was never
        /// exercised across two roots.
        ///
        /// Two items (800, 801) each need 3 of the SAME owned raw material
        /// (900); the account owns 4. Root 800 is walked first (request order)
        /// and fully satisfies its need of 3, draining the shared pool to 1
        /// remaining unit BEFORE root 801 is reduced, so 801 finds only 1 unit
        /// left and must buy the other 2. That is what distinguishes a
        /// genuinely SHARED pool from each root being reduced against its own
        /// fresh copy of the ownership index: in the latter both roots would
        /// independently see all 4 owned units and neither would buy anything
        /// (0 total purchases) instead of the 2 asserted below.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_WithSnapshot_SharedOwnedRawMaterial_PoolDrainsAcrossRootsInRequestOrder()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(800, 810);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 810,
                OutputItemId = 800,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 900, Count = 3 },
                },
            });
            recipeApi.AddSearchResult(801, 811);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 811,
                OutputItemId = 801,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 900, Count = 3 },
                },
            });

            // No TP price for 800/801 (force-craft, they have a recipe).
            // Shared material 900 is TP-buyable: InstantBuy cost per unit
            // is driven by the raw sellUnitPrice param (see
            // CraftingPlanPipelineForceBuyTests.BuildForceBuyPipeline's own doc
            // comment on this InMemoryPriceApiClient/TradingPostService
            // mapping) - 10 coin per unit here.
            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(900, buyUnitPrice: 1, sellUnitPrice: 10);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(800, "Item G", "g.png");
            itemApi.AddItem(801, "Item H", "h.png");
            itemApi.AddItem(900, "Shared Owned Material", "m.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());

            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 900, Count = 4, Source = AccountItemIndex.SourceMaterialStorage },
                },
            };

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 800, Quantity = 1 },
                new PlanRequestItem { ItemId = 801, Quantity = 1 },
            };

            var result = await pipeline.GenerateStructuredAsync(
                items, snapshot, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            // Both crafts still present (no TP price of their own).
            Assert.Contains(result.Plan.Steps, s => s.ItemId == 800 && s.Source == AcquisitionSource.Craft);
            Assert.Contains(result.Plan.Steps, s => s.ItemId == 801 && s.Source == AcquisitionSource.Craft);

            // Only 2 of the 6 total needed units of 900 are bought - the
            // other 4 came from the shared pool (3 to root 800, 1 to root
            // 801), proving the pool is shared and drained in request
            // order rather than re-initialized per root.
            var buyStep = Assert.Single(
                result.Plan.Steps.Where(s => s.ItemId == 900 && s.Source == AcquisitionSource.BuyFromTp));
            Assert.Equal(2, buyStep.Quantity);
            Assert.Equal(20, buyStep.TotalCost);
            Assert.Equal(20, result.Plan.TotalCoinCost);

            // All 4 owned units were consumed (aggregated across both
            // roots into a single UsedMaterials entry, per-item-id).
            var used = Assert.Single(result.UsedMaterials.Where(u => u.ItemId == 900));
            Assert.Equal(4, used.QuantityUsed);
        }

        /// <summary>
        /// No other multi-item test exercised the
        /// "Value Own Materials" force-buy pre-pass
        /// (OwnedMaterialsForceBuyPrePass.ComputeForceBuyOnlyNodeIds),
        /// which only runs when OwnMaterialsMode.Valued AND a non-null
        /// snapshot are both supplied - the gate every prior multi-item
        /// test's snapshot=null call skipped entirely.
        ///
        /// Two INDEPENDENT roots (900 and 902, sharing no ingredients) are
        /// each shaped exactly like
        /// CraftingPlanPipelineForceBuyTests.BuildForceBuyPipeline's single-item
        /// scenario: owning 4 of the 5 needed ingredient collapses the
        /// POST-reduction craft cost below the fresh buy price, so without
        /// the force-buy pre-pass's zero-owned baseline each root would
        /// wrongly flip to Craft. Asserting the batch result against the
        /// two standalone single-item Valued-mode solves (using the same
        /// snapshot) proves the pre-pass, computed against the UNREDUCED
        /// wrapper tree, still applies per-root inside the wrapper.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_ValuedMode_ForceBuyPrePass_MatchesStandaloneResultsPerRoot()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(900, 910);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 910,
                OutputItemId = 900,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 901, Count = 5 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });
            recipeApi.AddSearchResult(902, 920);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 920,
                OutputItemId = 902,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 903, Count = 5 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });

            // Same InstantBuy pricing shape as BuildForceBuyPipeline: fresh
            // buy(100) < craft(5x30=150) beats craft on a zero-owned
            // baseline for BOTH roots.
            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(900, buyUnitPrice: 1000, sellUnitPrice: 100);
            priceApi.AddPrice(901, buyUnitPrice: 300, sellUnitPrice: 30);
            priceApi.AddPrice(902, buyUnitPrice: 1000, sellUnitPrice: 100);
            priceApi.AddPrice(903, buyUnitPrice: 300, sellUnitPrice: 30);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(900, "Item I", "i.png");
            itemApi.AddItem(901, "Ingredient I", "ii.png");
            itemApi.AddItem(902, "Item J", "j.png");
            itemApi.AddItem(903, "Ingredient J", "ij.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());

            // Own 4 of each root's own (unshared) ingredient.
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 901, Count = 4, Source = AccountItemIndex.SourceMaterialStorage },
                    new SnapshotItemEntry { ItemId = 903, Count = 4, Source = AccountItemIndex.SourceMaterialStorage },
                },
            };

            var standaloneA = await pipeline.GenerateStructuredAsync(
                900, 1, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);
            var standaloneB = await pipeline.GenerateStructuredAsync(
                902, 1, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.BuyFromTp, standaloneA.CraftingTree.Decision);
            Assert.Equal(CraftingDecision.BuyFromTp, standaloneB.CraftingTree.Decision);
            Assert.Equal(100, standaloneA.Plan.TotalCoinCost);
            Assert.Equal(100, standaloneB.Plan.TotalCoinCost);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 900, Quantity = 1 },
                new PlanRequestItem { ItemId = 902, Quantity = 1 },
            };

            var batch = await pipeline.GenerateStructuredAsync(
                items, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);

            // Batch result equals the two standalone results combined: both
            // roots still buy (the pre-pass's zero-owned baseline held per
            // root inside the wrapper), and the merged total is exactly
            // their sum since the two ingredient items (901, 903) share
            // nothing.
            Assert.Equal(CraftingDecision.BuyFromTp, batch.MultiItemRoots[0].Decision);
            Assert.Equal(CraftingDecision.BuyFromTp, batch.MultiItemRoots[1].Decision);
            Assert.Equal(standaloneA.Plan.TotalCoinCost + standaloneB.Plan.TotalCoinCost, batch.Plan.TotalCoinCost);
            Assert.Equal(200, batch.Plan.TotalCoinCost);
            Assert.Contains(batch.Plan.Steps, s => s.ItemId == 900 && s.Source == AcquisitionSource.BuyFromTp);
            Assert.Contains(batch.Plan.Steps, s => s.ItemId == 902 && s.Source == AcquisitionSource.BuyFromTp);
            // The now-unneeded craft ingredients never surface as steps.
            Assert.DoesNotContain(batch.Plan.Steps, s => s.ItemId == 901);
            Assert.DoesNotContain(batch.Plan.Steps, s => s.ItemId == 903);
        }

        // --- Multi-item sell-side economics (gw2efficiency parity,
        // closes KNOWN-ISSUES #25) ---

        /// <summary>
        /// Two independent crafted items with NEITHER finished item ever
        /// given its own TP price (only their raw ingredients are priced) -
        /// the genuine "not one requested root has a live sell price" case
        /// (ApplyBatchSellSideEconomics' own `!anySellable` early return),
        /// same untradable-finished-item shape as
        /// GenerateStructuredAsync_TwoItems_SharedBulkVendorMaterial_SingleCeilAcrossBoth's
        /// items 100/200 above.
        /// </summary>
        private static CraftingPlanPipeline BuildTwoUntradableItemsPipeline()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(300, 310);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 310,
                OutputItemId = 300,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 301, Count = 1 },
                },
            });
            recipeApi.AddSearchResult(400, 410);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 410,
                OutputItemId = 400,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 401, Count = 1 },
                },
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(301, buyUnitPrice: 100, sellUnitPrice: 100);
            priceApi.AddPrice(401, buyUnitPrice: 200, sellUnitPrice: 200);
            // Deliberately NO AddPrice call for 300/400 themselves - both
            // finished items are untradable (no TP listings at all), so
            // both force-craft (no buy price to compare against) AND both
            // leave NetSaleValue null.
            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(300, "Item C (untradable)", "c.png");
            itemApi.AddItem(301, "Ingredient C", "ic.png");
            itemApi.AddItem(400, "Item D (untradable)", "d.png");
            itemApi.AddItem(401, "Ingredient D", "id.png");

            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());
        }

        /// <summary>
        /// Before ApplyBatchSellSideEconomics existed, NOTHING ever set
        /// CraftingPlanResult.PriceBasis for a multi-item batch
        /// (PlanResultBuilder.Build never touches it, and
        /// GenerateStructuredMultiAsync never called ApplySellSideEconomics,
        /// the only other place that did) - it silently stayed at the enum
        /// default (PriceBasis.InstantBuy = 0) regardless of which basis
        /// actually priced the plan, so a batch generated with the module's own
        /// default (BuyOrder) never showed the "Total (buy-order prices)" label
        /// suffix. ApplyBatchSellSideEconomics now sets it unconditionally,
        /// mirroring ApplySellSideEconomics' single-item behavior, even when
        /// zero roots qualify for the sell/profit rollup below.
        ///
        /// The fixture matters: an earlier version of this test reused
        /// BuildTwoIndependentItemsPipeline, whose items 300/400 BOTH have a
        /// live sell price, so every requested root qualified for the rollup
        /// and the !anySellable early-return branch this test's name claims to
        /// cover was never exercised. BuildTwoUntradableItemsPipeline (above)
        /// genuinely has zero qualifying roots.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_PriceBasisIsSetEvenWithNoQualifyingRoots()
        {
            var pipeline = BuildTwoUntradableItemsPipeline();
            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 300, Quantity = 1 },
                new PlanRequestItem { ItemId = 400, Quantity = 1 },
            };

            // Default priceBasis is PriceBasis.BuyOrder (see
            // GenerateStructuredAsync's own default parameter) - deliberately
            // NOT overridden here, unlike every other test in this file.
            var batch = await pipeline.GenerateStructuredAsync(items, null, CancellationToken.None);

            Assert.Equal(PriceBasis.BuyOrder, batch.PriceBasis);

            // Genuinely zero qualifying roots: neither 300 nor 400 has a
            // live TP price of its own, so SellableQuantity/NetSaleValue/
            // CraftingProfit all stay at their "not one requested root
            // qualifies" defaults, even though PriceBasis above is still set.
            Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[0].Decision);
            Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[1].Decision);
            Assert.Equal(0, batch.SellableQuantity);
            Assert.Null(batch.NetSaleValue);
            Assert.Null(batch.CraftingProfit);
        }

        /// <summary>
        /// Both requested roots are crafted and have a live TP sell price -
        /// no shared materials between them (301/401 are distinct), so the
        /// batch sum is exactly the two standalone single-item results
        /// combined.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_BothCraftedAndTradable_SumsAcrossBothRoots()
        {
            var pipeline = BuildTwoIndependentItemsPipeline(out _);

            var standaloneA = await pipeline.GenerateStructuredAsync(
                300, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            var standaloneB = await pipeline.GenerateStructuredAsync(
                400, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            Assert.Equal(CraftingDecision.Craft, standaloneA.CraftingTree.Decision);
            Assert.Equal(CraftingDecision.Craft, standaloneB.CraftingTree.Decision);
            Assert.NotNull(standaloneA.NetSaleValue);
            Assert.NotNull(standaloneB.NetSaleValue);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 300, Quantity = 1 },
                new PlanRequestItem { ItemId = 400, Quantity = 1 },
            };

            var batch = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(standaloneA.SellableQuantity + standaloneB.SellableQuantity, batch.SellableQuantity);
            Assert.Equal(standaloneA.NetSaleValue.Value + standaloneB.NetSaleValue.Value, batch.NetSaleValue);
            Assert.Equal(standaloneA.CraftingProfit.Value + standaloneB.CraftingProfit.Value, batch.CraftingProfit);
            // A batch has N per-item unit prices, not one - never a
            // meaningless "average".
            Assert.Null(batch.TargetUnitSellPrice);
        }

        /// <summary>
        /// Mixed tradable/untradable roots: item 500 is crafted and has a
        /// live TP sell price; item 600 is ALSO crafted (force-crafted -
        /// it has a recipe but no TP price of its own at all, like an
        /// account-bound material) but has no sell price. Plan.TotalCoinCost
        /// includes BOTH items' craft cost, but the sell/profit rollup must
        /// include ONLY item 500's contribution - proving the deliberate
        /// divergence from gw2e's own rollup (which would instead drag the
        /// total down by item 600's full craft cost as a hidden negative -
        /// see ApplyBatchSellSideEconomics's own doc comment, divergence
        /// item 2).
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_OneRootUntradable_ExcludedFromSumNotNegative()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(500, 510);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 510,
                OutputItemId = 500,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 501, Count = 2 },
                },
            });
            recipeApi.AddSearchResult(600, 610);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 610,
                OutputItemId = 600,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 601, Count = 3 },
                },
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(501, buyUnitPrice: 5, sellUnitPrice: 50);
            priceApi.AddPrice(601, buyUnitPrice: 5, sellUnitPrice: 20);
            // Item 500 has its own TP price (tradable); item 600 does NOT -
            // no AddPrice call at all, matching an account-bound/unlisted
            // item that still has a known recipe.
            priceApi.AddPrice(500, buyUnitPrice: 1000, sellUnitPrice: 9000);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(500, "Item K", "k.png");
            itemApi.AddItem(501, "Ingredient K", "ik.png");
            itemApi.AddItem(600, "Item L (untradable)", "l.png");
            itemApi.AddItem(601, "Ingredient L", "il.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());

            var standalone500 = await pipeline.GenerateStructuredAsync(
                500, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            Assert.Equal(CraftingDecision.Craft, standalone500.CraftingTree.Decision);
            Assert.NotNull(standalone500.NetSaleValue);

            var standalone600 = await pipeline.GenerateStructuredAsync(
                600, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            Assert.Equal(CraftingDecision.Craft, standalone600.CraftingTree.Decision);
            Assert.Null(standalone600.NetSaleValue);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 500, Quantity = 1 },
                new PlanRequestItem { ItemId = 600, Quantity = 1 },
            };

            var batch = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[0].Decision);
            Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[1].Decision);
            Assert.Equal(160, batch.Plan.TotalCoinCost);

            Assert.Equal(standalone500.SellableQuantity, batch.SellableQuantity);
            Assert.Equal(standalone500.NetSaleValue, batch.NetSaleValue);
            Assert.Equal(standalone500.CraftingProfit, batch.CraftingProfit);
        }

        /// <summary>
        /// Item 1100's own TP buy price is cheaper than crafting it (like
        /// GenerateStructuredAsync_MultiItem_PerRootDecision_MatchesStandaloneSingleItemSolve
        /// above), so the solver buys it rather than crafting - but it DOES
        /// have a live TP sell price. Item 1200 is crafted and also has a
        /// live sell price. Regression: the batch
        /// rollup has NO craft-vs-buy filter at all (docs/research/
        /// docs/research/m37-r2-batch-economics.md Section 4.1.1 explicitly recommends
        /// against replicating gw2e's own craft===true filter) - a
        /// bought-but-tradable root still contributes its own
        /// NetSaleValue/CraftingProfit, exactly like the single-item path
        /// already would if you ran it alone.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_OneRootBoughtButTradable_IncludedInSum()
        {
            var pipeline = BuildBuyVsCraftPipeline();

            var standalone1200 = await pipeline.GenerateStructuredAsync(
                1200, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            Assert.Equal(CraftingDecision.Craft, standalone1200.CraftingTree.Decision);
            Assert.NotNull(standalone1200.NetSaleValue);

            var standalone1100 = await pipeline.GenerateStructuredAsync(
                1100, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            Assert.Equal(CraftingDecision.BuyFromTp, standalone1100.CraftingTree.Decision);
            // The single-item path never filters by craft-vs-buy - a
            // bought target still shows economics (a flip/arbitrage
            // number), and the batch rollup below must match it.
            Assert.NotNull(standalone1100.NetSaleValue);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1100, Quantity = 1 },
                new PlanRequestItem { ItemId = 1200, Quantity = 1 },
            };

            var batch = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.BuyFromTp, batch.MultiItemRoots[0].Decision);
            Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[1].Decision);

            // Batch sum equals BOTH standalone results combined - the
            // bought root's own contribution is NOT dropped.
            Assert.Equal(standalone1100.SellableQuantity + standalone1200.SellableQuantity, batch.SellableQuantity);
            Assert.Equal(standalone1100.NetSaleValue.Value + standalone1200.NetSaleValue.Value, batch.NetSaleValue);
            Assert.Equal(standalone1100.CraftingProfit.Value + standalone1200.CraftingProfit.Value, batch.CraftingProfit);
        }

        /// <summary>
        /// Re-solve recompute (override): item 1100 is already included in
        /// the batch rollup even while bought (see
        /// GenerateStructuredAsync_MultiItem_OneRootBoughtButTradable_IncludedInSum
        /// above - no craft-vs-buy filter), so forcing it from Buy to Craft
        /// via a per-node override does NOT add a new item to the sum - it
        /// recomputes that SAME root's own contribution using its new
        /// (more expensive) craft cost instead of its buy cost.
        /// NetSaleValue/SellableQuantity are unaffected (revenue depends
        /// only on the live sell price, not on how the root was acquired);
        /// only CraftingProfit changes, by exactly the craft-cost increase.
        /// Still proves ApplyBatchSellSideEconomics reruns (via
        /// ResolveWithOverrides' else branch) exactly like every other
        /// part of a re-solve.
        /// </summary>
        [Fact]
        public async Task ResolveWithOverrides_MultiItem_OverrideRootFromBuyToCraft_RecomputesItsCostContribution()
        {
            var pipeline = BuildBuyVsCraftPipeline();

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1100, Quantity = 1 },
                new PlanRequestItem { ItemId = 1200, Quantity = 1 },
            };

            var initial = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            Assert.Equal(CraftingDecision.BuyFromTp, initial.MultiItemRoots[0].Decision);
            Assert.NotNull(initial.NetSaleValue);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.MultiItemRoots[0].NodeId, AcquisitionSource.Craft },
            };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);

            Assert.Equal(CraftingDecision.Craft, resolved.MultiItemRoots[0].Decision);

            // Already included before the override (its own buy cost, 50
            // coin) - the override only swaps its cost contribution from
            // 50 (buy) to 500 (5 x 100 craft), a 450-coin increase;
            // revenue/quantity are unaffected since they never depended on
            // the craft-vs-buy decision.
            Assert.Equal(initial.SellableQuantity, resolved.SellableQuantity);
            Assert.Equal(initial.NetSaleValue, resolved.NetSaleValue);
            Assert.Equal(initial.CraftingProfit.Value - 450, resolved.CraftingProfit.Value);
        }

        /// <summary>
        /// Re-solve recompute (ignore): marking one requested root's own
        /// item id "Ignore" flips its decision to Have/UnknownSource (see
        /// PlanSolver.Evaluate's ignoredItemIds early-return) and zeroes its
        /// own craft-cost contribution (ItemCraftCost), but does NOT drop it
        /// from the rollup: ComputePerItemEconomics' NetSaleValue is driven
        /// purely by the item's own live TP price, never by its acquisition
        /// decision (no craft-vs-buy filter, see
        /// GenerateStructuredAsync_MultiItem_OneRootBoughtButTradable_IncludedInSum
        /// above), matching the single-item path's own pre-existing
        /// "ignoring the target still shows a sell number" convention.
        /// </summary>
        [Fact]
        public async Task ResolveWithOverrides_MultiItem_IgnoreRootItemId_ZeroesCostButKeepsItInRollup()
        {
            var pipeline = BuildTwoIndependentItemsPipeline(out _);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 300, Quantity = 1 },
                new PlanRequestItem { ItemId = 400, Quantity = 1 },
            };

            var initial = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(initial.NetSaleValue);

            var ignored = new HashSet<int> { 300 };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, null, ignored);

            Assert.True(resolved.MultiItemRoots[0].IsIgnored);
            Assert.Equal(CraftingDecision.Have, resolved.MultiItemRoots[0].Decision);

            // Root 300 stays IN the rollup (still tradable) - only its own
            // craft cost (100 coin, from ingredient 301) drops to 0.
            // SellableQuantity/NetSaleValue are unaffected: neither ever
            // depended on the craft-vs-buy/ignore decision.
            Assert.Equal(initial.SellableQuantity, resolved.SellableQuantity);
            Assert.Equal(initial.NetSaleValue, resolved.NetSaleValue);
            Assert.Equal(initial.CraftingProfit.Value + 100, resolved.CraftingProfit.Value);
        }

        /// <summary>
        /// Item 1100: craft cost (5 x 100 = 500) is more expensive than its
        /// own TP buy price (50), so the solver buys it - even though it
        /// also has a live sell price (1000), used by
        /// GenerateStructuredAsync_MultiItem_OneRootBoughtButTradable_IncludedInSum
        /// and ResolveWithOverrides_MultiItem_OverrideRootFromBuyToCraft_RecomputesItsCostContribution
        /// above. Item 1200's craft cost (10) beats its own buy price
        /// (99999), so it always force-crafts.
        /// </summary>
        private static CraftingPlanPipeline BuildBuyVsCraftPipeline()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(1100, 1110);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 1110,
                OutputItemId = 1100,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 1101, Count = 5 },
                },
            });
            recipeApi.AddSearchResult(1200, 1210);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 1210,
                OutputItemId = 1200,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 1201, Count = 1 },
                },
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(1101, buyUnitPrice: 5, sellUnitPrice: 100);
            priceApi.AddPrice(1201, buyUnitPrice: 5, sellUnitPrice: 10);
            // Item 1100: SellInstant 1000 (tradable), BuyInstant 50 (cheaper
            // than the 500-coin craft cost - solver buys).
            priceApi.AddPrice(1100, buyUnitPrice: 1000, sellUnitPrice: 50);
            // Item 1200: SellInstant 2000 (tradable), BuyInstant 99999 (far
            // more than the 10-coin craft cost - solver crafts).
            priceApi.AddPrice(1200, buyUnitPrice: 2000, sellUnitPrice: 99999);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1100, "Item M (bought)", "m.png");
            itemApi.AddItem(1101, "Ingredient M", "im.png");
            itemApi.AddItem(1200, "Item N (crafted)", "n.png");
            itemApi.AddItem(1201, "Ingredient N", "in.png");

            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());
        }

        /// <summary>
        /// Re-baselined under the decision-guided reduction design. Originally,
        /// InventoryReducer.Reduce walked the WHOLE unreduced wrapper tree
        /// price-blind, before PlanSolver.Solve ever decided per-root Buy vs.
        /// Craft, so it phantom-consumed root 1100's owned craft ingredient
        /// (1101) even though 1100 always ends up BOUGHT (see
        /// BuildBuyVsCraftPipeline's own doc comment - buy 50 beats craft 500
        /// even zero-owned), folding a forgone-value deduction into
        /// MaterialOpportunityCost for a branch that was never crafted.
        ///
        /// Now the guided reduction (InventoryReducer.Reduce's
        /// zeroOwnedDecisions parameter) sees that 1100's zero-owned decision
        /// is BuyFromTp, so no option under 1100 consumes the pool and the
        /// owned 2 units of item 1101 are never touched. MaterialOpportunityCost
        /// is therefore null throughout - standalone 1100, standalone 1200
        /// (which never owned anything), and the batch - rather than non-zero
        /// and folded in regardless of the decision. This test locks in the
        /// FIXED interaction, so a regression cannot silently reintroduce
        /// phantom credit for a bought root.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_ValuedMode_MixedBuyCraftBatch_MaterialOpportunityCostNullForBoughtRootOwnedIngredient()
        {
            var pipeline = BuildBuyVsCraftPipeline();

            // Own 2 of the 5 units of item 1101 (root 1100's own craft
            // ingredient) - root 1200 owns nothing.
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 1101, Count = 2, Source = AccountItemIndex.SourceMaterialStorage },
                },
            };

            var standalone1100 = await pipeline.GenerateStructuredAsync(
                1100, 1, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);
            var standalone1200 = await pipeline.GenerateStructuredAsync(
                1200, 1, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);

            // Root 1100 still buys outright (partial ownership of its own
            // craft ingredient never makes crafting cheaper than its
            // 50-coin buy price). Post-fix, that owned stock is never
            // consumed at all (1100's branch was never chosen), so
            // MaterialOpportunityCost is null, not a phantom non-zero
            // credit against a branch that was never crafted.
            Assert.Equal(CraftingDecision.BuyFromTp, standalone1100.CraftingTree.Decision);
            Assert.Null(standalone1100.MaterialOpportunityCost);
            Assert.Equal(CraftingDecision.Craft, standalone1200.CraftingTree.Decision);
            Assert.Null(standalone1200.MaterialOpportunityCost);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1100, Quantity = 1 },
                new PlanRequestItem { ItemId = 1200, Quantity = 1 },
            };

            var batch = await pipeline.GenerateStructuredAsync(
                items, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.BuyFromTp, batch.MultiItemRoots[0].Decision);
            Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[1].Decision);

            // Neither standalone root produced any UsedMaterials post-fix,
            // so the batch's single whole-tree MaterialOpportunityCost is
            // also null - not a sum of phantom per-root credits.
            Assert.Null(batch.MaterialOpportunityCost);

            // Both roots are tradable, so both still contribute their own
            // NetSaleValue to the batch rollup (no craft-vs-buy filter
            // - see GenerateStructuredAsync_MultiItem_OneRootBoughtButTradable_IncludedInSum
            // above) - unaffected by the MaterialOpportunityCost fix, since
            // that only ever subtracted from CraftingProfit, never from
            // NetSaleValue itself.
            Assert.Equal(standalone1100.NetSaleValue.Value + standalone1200.NetSaleValue.Value, batch.NetSaleValue);
            // No MaterialOpportunityCost to subtract now (ApplyBatchSellSideEconomics
            // only subtracts it when HasValue) - profit is revenue minus cost alone.
            Assert.Equal(
                batch.NetSaleValue.Value - batch.Plan.TotalCoinCost,
                batch.CraftingProfit.Value);
        }

        /// <summary>
        /// Coverage: every Valued-mode multi-item assertion above exercised a
        /// wrapper root whose guide decision was Buy, so UsedMaterials and
        /// MaterialOpportunityCost were always asserted NULL/empty; and the one
        /// test that does drain owned stock through the synthetic wrapper
        /// (GenerateStructuredAsync_MultiItem_WithSnapshot_SharedOwnedRawMaterial_PoolDrainsAcrossRootsInRequestOrder
        /// above) calls GenerateStructuredAsync WITHOUT ownMaterialsMode, which
        /// defaults to Free and never builds a guide. Net effect: positive
        /// owned-material crediting through the wrapper in Valued mode had zero
        /// coverage - if the wrapper root's guide decision were ever not
        /// Craft-with-matching-RecipeId, ALL owned-material crediting in
        /// multi-item Valued mode would silently vanish, suite still green.
        ///
        /// Reuses BuildBuyVsCraftPipeline's exact fixture (root 1100 always
        /// buys, root 1200 always crafts - see that method's own doc comment)
        /// but owns root 1200's OWN craft ingredient (1201, exactly the 1 unit
        /// its recipe needs). Since 1200's zero-owned decision is Craft, the
        /// guided reduction must let that unit be consumed, proving the positive
        /// crediting path works through the wrapper, not only the Buy-decided one.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_MultiItem_ValuedMode_CraftDecidedRootOwnedIngredientIsCreditedThroughWrapper()
        {
            var pipeline = BuildBuyVsCraftPipeline();

            // Own exactly the 1 unit of item 1201 that root 1200's recipe
            // needs (root 1100 owns nothing of its own ingredient, 1101).
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 1201, Count = 1, Source = AccountItemIndex.SourceMaterialStorage },
                },
            };

            var standalone1200 = await pipeline.GenerateStructuredAsync(
                1200, 1, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.Craft, standalone1200.CraftingTree.Decision);
            var standaloneUsed = Assert.Single(standalone1200.UsedMaterials);
            Assert.Equal(1201, standaloneUsed.ItemId);
            Assert.Equal(1, standaloneUsed.QuantityUsed);
            // NetSaleRevenue(unitPrice: 5 [1201's SellInstant, the raw
            // buyUnitPrice param - see BuildBuyVsCraftPipeline], quantity: 1)
            // = 5 - max(1, round(5*5%)) - max(1, round(5*10%)) = 5 - 1 - 1 = 3.
            Assert.Equal(3, standalone1200.MaterialOpportunityCost);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1100, Quantity = 1 },
                new PlanRequestItem { ItemId = 1200, Quantity = 1 },
            };

            var batch = await pipeline.GenerateStructuredAsync(
                items, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.BuyFromTp, batch.MultiItemRoots[0].Decision);
            Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[1].Decision);

            // The wrapper's SHARED reduction pool credited root 1200's
            // owned ingredient exactly as the standalone solve did - proof
            // that positive owned-material crediting survives the
            // multi-item wrapper in Valued mode, not just the Buy-decided
            // (nothing-credited) case every other Valued-mode multi-item
            // test in this class covers.
            var batchUsed = Assert.Single(batch.UsedMaterials);
            Assert.Equal(1201, batchUsed.ItemId);
            Assert.Equal(1, batchUsed.QuantityUsed);
            Assert.Equal(3, batch.MaterialOpportunityCost);
            // Root 1100 still buys outright at 50 coin; root 1200 needs no
            // purchase at all (its one ingredient came entirely from
            // inventory) - matching the standalone sum.
            Assert.Equal(50, batch.Plan.TotalCoinCost);
            Assert.DoesNotContain(batch.Plan.Steps, s => s.ItemId == 1201);
        }

        /// <summary>
        /// Regression: no committed test exercised ApplyBatchSellSideEconomics'
        /// per-root ItemCraftCost summation when two qualifying (crafted +
        /// tradable) roots share a cost that FinalizeVendorBatches /
        /// AllocateVendorNodeCosts merges across roots. Same shared-bulk-vendor
        /// -material shape as
        /// GenerateStructuredAsync_TwoItems_SharedBulkVendorMaterial_SingleCeilAcrossBoth
        /// above (two roots each need 2 of a shared "5 for 20 coin" vendor
        /// material - merged demand 4, one batch, 20 coin total), but this time
        /// BOTH finished items also have a live TP sell price, so the
        /// SellableQuantity/NetSaleValue/CraftingProfit summing actually runs.
        /// AllocateVendorNodeCosts redistributes the corrected 20-coin batch
        /// total across the two occurrences by largest-remainder apportionment
        /// proportional to each occurrence's quantity share of demand (see that
        /// method's own doc comment) - here an even 10/10 split, since both need
        /// the same quantity (2) and 20 divides 4 exactly - proving the batch's
        /// CraftingProfit uses this real, non-duplicated per-root share, which
        /// sums to EXACTLY Plan.TotalCoinCost, rather than double-counting or
        /// dropping the shared portion.
        /// </summary>
        [Fact]
        public async Task GenerateStructuredAsync_TwoItems_SharedBulkVendorMaterial_BothTradable_CraftingProfitUsesRealNonDuplicatedSharedCost()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(100, 110);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 110,
                OutputItemId = 100,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 500, Count = 2 },
                },
            });
            recipeApi.AddSearchResult(200, 210);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 210,
                OutputItemId = 200,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 500, Count = 2 },
                },
            });

            var priceApi = new InMemoryPriceApiClient();
            // Both finished items have a live sell price, and a BuyInstant
            // far above their own (proportionally-allocated) vendor-batch
            // craft cost, so craft still wins for both.
            priceApi.AddPrice(100, buyUnitPrice: 1000, sellUnitPrice: 99999);
            priceApi.AddPrice(200, buyUnitPrice: 2000, sellUnitPrice: 99999);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(100, "Item A", "a.png");
            itemApi.AddItem(200, "Item B", "b.png");
            itemApi.AddItem(500, "Shared Material", "m.png");

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
                        OfferId = "bulk-material",
                        OutputItemId = 500,
                        OutputCount = 5,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 20 },
                        },
                        MerchantName = "Test NPC",
                    },
                });

                var pipeline = new CraftingPlanPipeline(
                    new RecipeService(recipeApi),
                    new TradingPostService(priceApi),
                    new PlanSolver(),
                    new ItemMetadataService(itemApi),
                    store);

                var items = new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 100, Quantity = 1 },
                    new PlanRequestItem { ItemId = 200, Quantity = 1 },
                };

                var batch = await pipeline.GenerateStructuredAsync(
                    items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

                Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[0].Decision);
                Assert.Equal(CraftingDecision.Craft, batch.MultiItemRoots[1].Decision);
                Assert.Equal(20, batch.Plan.TotalCoinCost);

                // Each root's own allocated share of the merged vendor
                // batch (not double-counted, not dropped) sums to exactly
                // Plan.TotalCoinCost.
                Assert.Equal(10, batch.MultiItemRoots[0].SubtreeCost);
                Assert.Equal(10, batch.MultiItemRoots[1].SubtreeCost);
                Assert.Equal(
                    batch.MultiItemRoots[0].SubtreeCost.Value + batch.MultiItemRoots[1].SubtreeCost.Value,
                    batch.Plan.TotalCoinCost);

                Assert.Equal(2, batch.SellableQuantity);
                long netSaleValueA = TradingPostMath.NetSaleRevenue(1000, 1);
                long netSaleValueB = TradingPostMath.NetSaleRevenue(2000, 1);
                Assert.Equal(netSaleValueA + netSaleValueB, batch.NetSaleValue);
                // CraftingProfit uses each root's own allocated share, not
                // a double-counted or dropped one - the sum still equals
                // NetSaleValue minus the real (undivided) Plan.TotalCoinCost.
                Assert.Equal(batch.NetSaleValue.Value - batch.Plan.TotalCoinCost, batch.CraftingProfit);
            }
        }

        // --- Achievement-bit ingredient dedup -
        // the report's exact multi-item-request double-count scenario
        // (docs/research/m37-r3-achievement-dedup.md Section 4.6), using
        // the real, wiki/API-verified Infinite Trebuchet Blueprint ids. ---
        [Fact]
        public async Task GenerateStructuredAsync_BlueprintAchievementRecipe_PlusDirectBitItemRequest_DedupsSharedIngredient()
        {
            // Infinite Trebuchet Blueprint (item 103980, achievement 8493)
            // needs one of each of 4 achievement-bit ingredients (bits 0-3:
            // items 103886/103834/103801/103974). The user ALSO separately
            // requests 1x item 103886 (Pile of Recycled Trebuchets)
            // directly, for some unrelated reason - exactly the report's
            // constructed repro. Before this fix, the tree would carry TWO
            // independent demands for 103886; after, the achievement-bit
            // occurrence (nested inside the Blueprint) is zeroed and the
            // direct request keeps its own full, un-deduped demand of 1.
            const int blueprintId = 103980;
            const int bit0PileOfRecycledTrebuchets = 103886;
            const int bit1TrebuchetMechanism = 103834;
            const int bit2ProofOfSiegeExpertise = 103801;
            const int bit3BoxOfScavengedTrebuchetParts = 103974;
            const int blueprintRecipeId = -1592;

            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(blueprintId, blueprintRecipeId);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = blueprintRecipeId,
                OutputItemId = blueprintId,
                OutputItemCount = 1,
                AchievementId = 8493,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = bit0PileOfRecycledTrebuchets, Count = 1, AchievementId = 8493, AchievementBit = 0 },
                    new RawIngredient { Type = "Item", Id = bit1TrebuchetMechanism, Count = 1, AchievementId = 8493, AchievementBit = 1 },
                    new RawIngredient { Type = "Item", Id = bit2ProofOfSiegeExpertise, Count = 1, AchievementId = 8493, AchievementBit = 2 },
                    new RawIngredient { Type = "Item", Id = bit3BoxOfScavengedTrebuchetParts, Count = 1, AchievementId = 8493, AchievementBit = 3 },
                },
                Disciplines = new List<string> { "Achievement" },
            });
            // No recipe registered for any of the 4 bit items - each is
            // priced directly. Giving them acquisition paths is out of
            // scope for this test, which covers the dedup pass alone.

            // InMemoryPriceApiClient.AddPrice(id, buyUnitPrice, sellUnitPrice)
            // feeds RawPriceEntry.BuyUnitPrice/SellUnitPrice, which
            // TradingPostService maps INVERTED onto ItemPrice
            // (BuyInstant = entry.SellUnitPrice, SellInstant =
            // entry.BuyUnitPrice - "instant buy" = the lowest active SELL
            // listing) - sellUnitPrice below is therefore the InstantBuy
            // cost this test's PriceBasis.InstantBuy actually reads.
            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(bit0PileOfRecycledTrebuchets, buyUnitPrice: 90, sellUnitPrice: 100);
            priceApi.AddPrice(bit1TrebuchetMechanism, buyUnitPrice: 45, sellUnitPrice: 50);
            priceApi.AddPrice(bit2ProofOfSiegeExpertise, buyUnitPrice: 25, sellUnitPrice: 30);
            priceApi.AddPrice(bit3BoxOfScavengedTrebuchetParts, buyUnitPrice: 15, sellUnitPrice: 20);
            // No price for the Blueprint itself - forces Craft (its own
            // achievement "recipe" is the only path).
            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(blueprintId, "Infinite Trebuchet Blueprint", "blueprint.png");
            itemApi.AddItem(bit0PileOfRecycledTrebuchets, "Pile of Recycled Trebuchets", "pile.png");
            itemApi.AddItem(bit1TrebuchetMechanism, "Trebuchet Mechanism", "mechanism.png");
            itemApi.AddItem(bit2ProofOfSiegeExpertise, "Proof of Siege Expertise", "proof.png");
            itemApi.AddItem(bit3BoxOfScavengedTrebuchetParts, "Box of Scavenged Trebuchet Parts", "box.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = blueprintId, Quantity = 1 },
                new PlanRequestItem { ItemId = bit0PileOfRecycledTrebuchets, Quantity = 1 },
            };

            var result = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(2, result.MultiItemRoots.Count);
            var blueprintRoot = result.MultiItemRoots[0];
            var directPileRoot = result.MultiItemRoots[1];
            Assert.Equal(blueprintId, blueprintRoot.ItemId);
            Assert.Equal(bit0PileOfRecycledTrebuchets, directPileRoot.ItemId);

            // The Blueprint's own bit-0 ingredient is deduped: HAVE display,
            // COUNTED-ELSEWHERE flag, zero quantity, no children of its own.
            var dedupedBit0 = blueprintRoot.Children.Single(c => c.ItemId == bit0PileOfRecycledTrebuchets);
            Assert.Equal(CraftingDecision.Have, dedupedBit0.Decision);
            Assert.True(dedupedBit0.IsAchievementBitDeduped);
            Assert.Equal(0, dedupedBit0.Quantity);
            Assert.Empty(dedupedBit0.Children);

            // Bits 1-3 have no coexisting normal occurrence anywhere in
            // this plan, so none of them are deduped - each keeps its own
            // real quantity/cost.
            var bit1Node = blueprintRoot.Children.Single(c => c.ItemId == bit1TrebuchetMechanism);
            var bit2Node = blueprintRoot.Children.Single(c => c.ItemId == bit2ProofOfSiegeExpertise);
            var bit3Node = blueprintRoot.Children.Single(c => c.ItemId == bit3BoxOfScavengedTrebuchetParts);
            Assert.False(bit1Node.IsAchievementBitDeduped);
            Assert.False(bit2Node.IsAchievementBitDeduped);
            Assert.False(bit3Node.IsAchievementBitDeduped);
            Assert.Equal(CraftingDecision.BuyFromTp, bit1Node.Decision);
            Assert.Equal(50, bit1Node.SubtreeCost);
            Assert.Equal(CraftingDecision.BuyFromTp, bit2Node.Decision);
            Assert.Equal(30, bit2Node.SubtreeCost);
            Assert.Equal(CraftingDecision.BuyFromTp, bit3Node.Decision);
            Assert.Equal(20, bit3Node.SubtreeCost);

            // The directly-requested root keeps its own full, un-deduped
            // demand of 1 - not affected by the dedup at all.
            Assert.False(directPileRoot.IsAchievementBitDeduped);
            Assert.Equal(1, directPileRoot.Quantity);
            Assert.Equal(CraftingDecision.BuyFromTp, directPileRoot.Decision);
            Assert.Equal(100, directPileRoot.SubtreeCost);

            // Exactly ONE step for item 103886 in the whole plan (Quantity
            // 1, cost 100) - not two, not a zero-quantity ghost row on top.
            var pileSteps = result.Plan.Steps.Where(s => s.ItemId == bit0PileOfRecycledTrebuchets).ToList();
            var pileStep = Assert.Single(pileSteps);
            Assert.Equal(1, pileStep.Quantity);
            Assert.Equal(100, pileStep.TotalCost);

            // Total plan cost: Blueprint's own craft cost (0 for the deduped
            // bit0 + 50 + 30 + 20 = 100) plus the direct Pile purchase
            // (100) = 200 - NOT 300, which is what double-counting the
            // shared bit-0 demand would have produced.
            Assert.Equal(200, result.Plan.TotalCoinCost);
        }
    }
}
