using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class CraftingPlanPipelineAdvisoryAnnotationTests
    {
        // --- Annotation-detection characterization tests. A mutation
        // deleting all four post-solve
        // annotation-pass calls (CompetencyOpportunityCalculator.Apply,
        // ExcessCraftOutputCalculator.Apply, RecipeSheetSavingsCalculator.
        // Apply, SeasonalVendorTipCalculator.Apply) at the multi-item
        // generation site and inside ResolveWithOverrides left the suite
        // green at 1765 tests - none of the existing coverage asserted on
        // these four CraftingPlanResult properties from either of those two
        // call shapes, only from the single-item GenerateStructuredAsync
        // path. Every one of the four calculators unconditionally assigns
        // its own result property (empty list, never left null) once
        // called - see each calculator's own Apply() doc comment - so a
        // plain NotNull assertion here is a precise, minimal proof the call
        // actually ran; it deliberately does not re-assert calculator
        // CONTENT correctness, which the dedicated *CalculatorTests classes
        // already cover in isolation.
        private static void AssertAllAdvisoryListsPopulated(CraftingPlanResult result)
        {
            Assert.NotNull(result.CompetencyOpportunities);
            Assert.NotNull(result.ExcessCraftOutputs);
            Assert.NotNull(result.RecipeSheetSavingsOpportunities);
            Assert.NotNull(result.SeasonalVendorTips);
        }

        [Fact]
        public async Task GenerateStructuredAsync_ListOverload_MultiItem_PopulatesAllFourAdvisoryLists()
        {
            var pipeline = PipelineBuilder.TwoRootTree().Build();

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 1 },
                new PlanRequestItem { ItemId = 2, Quantity = 1 },
            };

            // The public list overload (GenerateStructuredAsync(items, ...))
            // with 2+ items dispatches to the genuine multi-item path
            // (GenerateStructuredMultiAsync) rather than the single-item
            // short-circuit - see that method's own doc comment.
            var result = await pipeline.GenerateStructuredAsync(items, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(2, result.MultiItemRoots.Count);
            AssertAllAdvisoryListsPopulated(result);
        }

        [Fact]
        public async Task ResolveWithOverrides_SingleItemContext_PopulatesAllFourAdvisoryLists()
        {
            var pipeline = PipelineBuilder.BuildEconomicsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var initial = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            // context.Tree.Id is the real target item id here, never the
            // multi-item wrapper id - this is the single-item context shape.
            Assert.NotEqual(Gw2Constants.MultiItemWrapperItemId, initial.SolveContext.Tree.Id);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.CraftingTree.NodeId, AcquisitionSource.BuyFromTp },
            };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);

            AssertAllAdvisoryListsPopulated(resolved);
        }

        [Fact]
        public async Task ResolveWithOverrides_MultiItemContext_PopulatesAllFourAdvisoryLists()
        {
            var pipeline = PipelineBuilder.TwoRootTree().Build();

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 1 },
                new PlanRequestItem { ItemId = 2, Quantity = 1 },
            };

            var initial = await pipeline.GenerateStructuredAsync(items, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            // context.Tree.Id is the synthetic multi-item wrapper id here -
            // the multi-item context shape ResolveWithOverrides' own
            // SellSideEconomics dispatch (B8) branches on.
            Assert.Equal(Gw2Constants.MultiItemWrapperItemId, initial.SolveContext.Tree.Id);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.MultiItemRoots[0].NodeId, AcquisitionSource.BuyFromTp },
            };
            var resolved = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);

            Assert.Equal(2, resolved.MultiItemRoots.Count);
            AssertAllAdvisoryListsPopulated(resolved);
        }

        // Pins the list overload's own dispatcher invariant
        // (GenerateStructuredAsync(items, ...), items.Count == 1 routes to
        // the untouched single-item GenerateStructuredAsync overload, NOT
        // GenerateStructuredMultiAsync with a one-item wrapper - see that
        // overload's own doc comment for the "byte-identical output, no
        // wrapper built at all" claim this test proves. The multi-item path
        // always sets result.RequestedItems and MultiItemRoots (never
        // CraftingTree) - see GenerateStructuredMultiAsync's own
        // assignments - so any of those three shape signals flipping would
        // mean the dispatcher mis-routed a single-entry list into the
        // multi-item path.
        [Fact]
        public async Task GenerateStructuredAsync_ListOverload_SingleItem_RoutesToSingleItemPath_NotMultiItemWrapper()
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
                    },
                })
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target Item", "target.png")
                .WithItem(2, "Ingredient", "ingredient.png")
                .Build();

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 1 },
            };

            var result = await pipeline.GenerateStructuredAsync(items, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // Single-item shape: CraftingTree populated, MultiItemRoots and
            // RequestedItems both left null - only the multi-item path ever
            // sets the latter two (result.RequestedItems = items; inside
            // GenerateStructuredMultiAsync; BuildCraftingTreeResult only
            // populates MultiItemRoots when tree.Id is the wrapper id).
            Assert.NotNull(result.CraftingTree);
            Assert.Equal(1, result.CraftingTree.ItemId);
            Assert.Null(result.MultiItemRoots);
            Assert.Null(result.RequestedItems);
            Assert.NotEqual(Gw2Constants.MultiItemWrapperItemId, result.SolveContext.Tree.Id);
        }

        // A currency-valued vendor child's contribution must survive the whole
        // GenerateStructuredAsync path - effective-default valuation threading,
        // VOM reduction, and the post-selection ComparisonValue passes - and
        // still reach the craft root as a DecisionValue/SubtreeCost divergence
        // the value-detail hover can render.
        [Fact]
        public async Task GenerateStructuredAsync_CraftRootWithVendorChildValuedInCuratedCurrency_VomOn_ValueDetailTooltipFires()
        {
            const int SpiritShardCurrencyId = 23;
            const int RootItemId = 1;
            const int VendorOnlyChildItemId = 2; // Philosopher's Stone-style
            const int OrdinaryChildItemId = 3;

            var builder = PipelineBuilder.Create()
                .WithSearchResult(RootItemId, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = RootItemId,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = VendorOnlyChildItemId, Count = 1 },
                        new RawIngredient { Type = "Item", Id = OrdinaryChildItemId, Count = 2 },
                    },
                })
                // No TP price for the root or the vendor-only child - craft and
                // BuyFromVendor are each the only source for their own item.
                // Ordinary child has a real TP price (craft-cost basis 10/unit).
                .WithPrice(OrdinaryChildItemId, buyUnitPrice: 10, sellUnitPrice: 20)
                .WithItem(RootItemId, "Deldrimor Steel Ingot", "root.png")
                .WithItem(VendorOnlyChildItemId, "Philosopher's Stone", "stone.png")
                .WithItem(OrdinaryChildItemId, "Ordinary Ingredient", "ingredient.png")
                .WithInventoryReducer();

            using (var tmp = new TempDirectory())
            {
                var loader = new VendorOfferLoader();
                var store = new VendorOfferStore(tmp.Path, loader);
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-spirit-shard-stone",
                        OutputItemId = VendorOnlyChildItemId,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = SpiritShardCurrencyId, Count = 20 },
                        },
                        MerchantName = "Mystic Forge Attendant",
                    },
                });

                var pipeline = builder.WithVendorOfferStore(store).Build();

                // Same valuation ModuleSettings.GetEffectiveCurrencyValuation()
                // returns on a fresh settings state: no user overrides, so only
                // CurrencyDecisionDefaults' curated table applies.
                var valuation = CurrencyValuation.WithDefaults(CurrencyValuation.None);

                // Owns 3 of the 10 needed (2/craft x 5 root quantity) units of
                // the ORDINARY sibling, not the vendor-only child, so ownership
                // reduction never touches the node the divergence comes from.
                var snapshot = new AccountSnapshot
                {
                    Items = new List<SnapshotItemEntry>
                    {
                        new SnapshotItemEntry
                        {
                            ItemId = OrdinaryChildItemId,
                            Count = 3,
                            Source = AccountItemIndex.SourceMaterialStorage,
                        },
                    },
                };

                var result = await pipeline.GenerateStructuredAsync(
                    RootItemId, 5, snapshot, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy,
                    currencyValuation: valuation,
                    ownMaterialsMode: OwnMaterialsMode.Valued);

                var root = result.CraftingTree;
                Assert.Equal(CraftingDecision.Craft, root.Decision);

                // Real gold cost: only the ordinary child's remaining (10-3=7)
                // units, bought at the InstantBuy basis' sell price of 20 = 140;
                // the vendor child's coin part is 0. Comparison value
                // additionally folds in the vendor child's shard cost: 5 crafts
                // x 20 shards/unit x 3600 copper/shard = 360000, so
                // DecisionValue exceeds SubtreeCost by exactly that amount.
                Assert.Equal(140L, root.SubtreeCost);
                Assert.Equal(360140L, root.DecisionValue);

                bool fired = ValueDetailTooltipBuilder.TryBuildContent(root, null, out var tooltip);

                Assert.True(fired, "Value-detail hover must fire for the craft root live pipeline case.");
                // Coin spelling changed with the
                // CoinSegmentMath.GameStyleText consolidation: every
                // composer now spells a coin amount the way the icons
                // beside it do.
                Assert.Contains("Crafting gold price: 1s 40c", tooltip.ToPlainText());
                Assert.Contains("Currencies: 36g 0s 0c", tooltip.ToPlainText());
                Assert.Contains("Optimization price: 36g 1s 40c", tooltip.ToPlainText());
            }
        }

        // The test above leaves the root single-option, so its committed pill is
        // PillKind.Locked. A deliberately uncompetitive TP price on the root
        // makes it multi-option, exercising the PillKind.Selected branch that
        // TreeSectionController's value-detail append gate also accepts.
        [Fact]
        public async Task GenerateStructuredAsync_CraftRootSelectedAmongMultipleOptions_ValueDetailTooltipFires()
        {
            const int SpiritShardCurrencyId = 23;
            const int RootItemId = 1;
            const int VendorOnlyChildItemId = 2;
            const int OrdinaryChildItemId = 3;

            var builder = PipelineBuilder.Create()
                .WithSearchResult(RootItemId, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = RootItemId,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = VendorOnlyChildItemId, Count = 1 },
                        new RawIngredient { Type = "Item", Id = OrdinaryChildItemId, Count = 2 },
                    },
                })
                .WithPrice(OrdinaryChildItemId, buyUnitPrice: 10, sellUnitPrice: 20)
                // Root ALSO has a (much higher) TP price, so it becomes a
                // multi-option node and the committed pill is PillKind.Selected
                // rather than Locked.
                .WithPrice(RootItemId, buyUnitPrice: 1000000, sellUnitPrice: 1000000)
                .WithItem(RootItemId, "Deldrimor Steel Ingot", "root.png")
                .WithItem(VendorOnlyChildItemId, "Philosopher's Stone", "stone.png")
                .WithItem(OrdinaryChildItemId, "Ordinary Ingredient", "ingredient.png")
                .WithInventoryReducer();

            using (var tmp = new TempDirectory())
            {
                var loader = new VendorOfferLoader();
                var store = new VendorOfferStore(tmp.Path, loader);
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-spirit-shard-stone",
                        OutputItemId = VendorOnlyChildItemId,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = SpiritShardCurrencyId, Count = 20 },
                        },
                        MerchantName = "Mystic Forge Attendant",
                    },
                });

                var pipeline = builder.WithVendorOfferStore(store).Build();

                var valuation = CurrencyValuation.WithDefaults(CurrencyValuation.None);
                var snapshot = new AccountSnapshot
                {
                    Items = new List<SnapshotItemEntry>
                    {
                        new SnapshotItemEntry
                        {
                            ItemId = OrdinaryChildItemId,
                            Count = 3,
                            Source = AccountItemIndex.SourceMaterialStorage,
                        },
                    },
                };

                var result = await pipeline.GenerateStructuredAsync(
                    RootItemId, 5, snapshot, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy,
                    currencyValuation: valuation,
                    ownMaterialsMode: OwnMaterialsMode.Valued);

                var root = result.CraftingTree;

                Assert.Equal(CraftingDecision.Craft, root.Decision);

                Assert.Equal(140L, root.SubtreeCost);
                Assert.Equal(360140L, root.DecisionValue);

                // Multi-option: the losing TP pill must be present too, or the
                // root would have taken BuildPillSpecs' single-option path and
                // the Selected assertion below would be checking a Locked pill.
                var specs = DecisionPillPlanner.BuildPillSpecs(root, null, null);
                Assert.Contains(specs, s => s.Text == "TP");
                var craftSpec = Assert.Single(specs, s => s.Text == "CRAFT");
                Assert.Equal(PillKind.Selected, craftSpec.Kind);

                bool fired = ValueDetailTooltipBuilder.TryBuildContent(root, null, out var tooltip);

                Assert.True(fired, "Value-detail hover must fire when the committed pill is genuinely Selected (2+ options).");
                // Coin spelling changed with the
                // CoinSegmentMath.GameStyleText consolidation: every
                // composer now spells a coin amount the way the icons
                // beside it do.
                Assert.Contains("Crafting gold price: 1s 40c", tooltip.ToPlainText());
                Assert.Contains("Currencies: 36g 0s 0c", tooltip.ToPlainText());
                Assert.Contains("Optimization price: 36g 1s 40c", tooltip.ToPlainText());
            }
        }
    }
}
