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
    public class CraftingPlanPipelineVendorCostComponentTests
    {
        // --- Vendor cost-component leaves, end-to-end through the real pipeline ---

        /// <summary>
        /// Real field case shape: a vendor-only item (no recipe, no TP
        /// price) whose winning offer mixes a TP-valued Item cost line
        /// (Globs of Ectoplasm, id 42) with a non-coin currency cost line
        /// (id 23) - 2 kinds, so CraftingTreeBuilder synthesizes component
        /// leaves. Proves the metadata-fetch widening (item 42's real name/
        /// icon resolve, not "Unknown Item"), the leaf synthesis itself
        /// through the full pipeline (not just the unit-level builder
        /// tests), and the parent/leaf consistency end to end.
        /// </summary>
        private static async Task<(CraftingPlanPipeline Pipeline, CraftingPlanResult Result)> GenerateMixedVendorPlanAsync(
            AccountSnapshot snapshot = null)
        {
            // No recipe for item 1 - vendor-only, matching the real
            // Amalgamated Rift Essence field case.
            var builder = PipelineBuilder.Create()
                .WithPrice(42, buyUnitPrice: 10, sellUnitPrice: 20)
                .WithItem(1, "Amalgamated Rift Essence", "essence.png")
                .WithItem(42, "Glob of Ectoplasm", "ecto.png")
                .WithInventoryReducer();

            CraftingPlanPipeline pipeline;
            CraftingPlanResult result;
            // Scoped like every other tmp-dir call site in this file -
            // VendorOfferStore loads everything it needs into memory
            // inside this block; nothing after GenerateStructuredAsync
            // returns re-reads the directory (ResolveWithOverrides reuses
            // the in-memory PlanSolveContext.VendorOffers captured here,
            // never the store itself again).
            using (var tmp = new TempDirectory())
            {
                var loader = new VendorOfferLoader();
                var store = new VendorOfferStore(tmp.Path, loader);
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-mixed-w4b",
                        OutputItemId = 1,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Item", Id = 42, Count = 5 },
                            new CostLine { Type = "Currency", Id = 23, Count = 3 },
                        },
                        MerchantName = "Test NPC",
                    },
                });

                pipeline = builder.WithVendorOfferStore(store).Build();

                result = await pipeline.GenerateStructuredAsync(1, 2, snapshot, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
            }

            return (pipeline, result);
        }

        [Fact]
        public async Task MixedVendorOffer_SynthesizesLeavesWithRealMetadata()
        {
            var (_, result) = await GenerateMixedVendorPlanAsync();

            Assert.Equal(AcquisitionSource.BuyFromVendor, result.Plan.Steps[0].Source);
            Assert.NotNull(result.CraftingTree);
            Assert.Equal(2, result.CraftingTree.Children.Count);

            var itemLeaf = result.CraftingTree.Children.Single(c => c.ItemId == 42);
            Assert.True(itemLeaf.IsCostComponent);
            // Metadata-fetch widening: item 42 is never a recipe-tree
            // ingredient (only a vendor CostLines entry), so its real
            // name/icon only resolves if AddVendorItemComponentIds worked.
            Assert.Equal("Glob of Ectoplasm", itemLeaf.Name);
            Assert.Equal("ecto.png", itemLeaf.IconUrl);
            Assert.Equal(10, itemLeaf.Quantity); // 5 * requested qty 2
            Assert.Equal(200, itemLeaf.SubtreeCost); // 10 * unit price 10

            var currencyLeaf = result.CraftingTree.Children.Single(c => c.ItemId == 23);
            Assert.True(currencyLeaf.IsCostComponent);
            Assert.Equal(6, currencyLeaf.Quantity);
            Assert.Null(currencyLeaf.SubtreeCost);

            // Parent total == the item leaf's exact gold value (no raw
            // coin, no other component in this offer).
            Assert.Equal(itemLeaf.SubtreeCost, result.CraftingTree.SubtreeCost);
        }

        [Fact]
        public async Task MixedVendorOffer_NoSnapshot_HavePillDataAbsent()
        {
            var (_, result) = await GenerateMixedVendorPlanAsync(snapshot: null);

            Assert.Null(result.SolveContext.OwnedVendorItemAmounts);
            var itemLeaf = result.CraftingTree.Children.Single(c => c.ItemId == 42);
            Assert.Equal(0, itemLeaf.ComponentOwnedQuantity);
        }

        [Fact]
        public async Task MixedVendorOffer_WithSnapshot_HavePillDataFlowsToLeaves()
        {
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 42, Count = 4, Source = AccountItemIndex.SourceMaterialStorage },
                },
                Wallet = new List<SnapshotWalletEntry>
                {
                    new SnapshotWalletEntry { CurrencyId = 23, Value = 999 },
                },
            };

            var (_, result) = await GenerateMixedVendorPlanAsync(snapshot);

            Assert.NotNull(result.SolveContext.OwnedVendorItemAmounts);
            Assert.Equal(4, result.SolveContext.OwnedVendorItemAmounts[42]);

            var itemLeaf = result.CraftingTree.Children.Single(c => c.ItemId == 42);
            Assert.Equal(4, itemLeaf.ComponentOwnedQuantity); // partial: need 10, own 4
            Assert.Equal(10, itemLeaf.Quantity); // unchanged by ownership

            var currencyLeaf = result.CraftingTree.Children.Single(c => c.ItemId == 23);
            Assert.Equal(999, currencyLeaf.ComponentOwnedQuantity); // raw holding (own 999, need 6) - never clamped
        }

        [Fact]
        public async Task MixedVendorOffer_ResolveWithOverrides_LeavesSurviveRoundTrip_StableIds()
        {
            var (pipeline, result) = await GenerateMixedVendorPlanAsync();
            var itemLeafBefore = result.CraftingTree.Children.Single(c => c.ItemId == 42);
            var currencyLeafBefore = result.CraftingTree.Children.Single(c => c.ItemId == 23);

            // A plain no-op re-solve (null overrides), exactly what a
            // decision-pill click on some OTHER node in a real plan would
            // trigger - proves the component leaves survive
            // ResolveWithOverrides' rebuild with the SAME NodeIds (so
            // TreeSectionController's expansion-state dictionary is not
            // silently orphaned).
            var resolved = pipeline.ResolveWithOverrides(result.SolveContext, null);

            Assert.NotNull(resolved.CraftingTree);
            Assert.Equal(2, resolved.CraftingTree.Children.Count);
            var itemLeafAfter = resolved.CraftingTree.Children.Single(c => c.ItemId == 42);
            var currencyLeafAfter = resolved.CraftingTree.Children.Single(c => c.ItemId == 23);

            Assert.Equal(itemLeafBefore.NodeId, itemLeafAfter.NodeId);
            Assert.Equal(currencyLeafBefore.NodeId, currencyLeafAfter.NodeId);
            Assert.Equal(itemLeafBefore.SubtreeCost, itemLeafAfter.SubtreeCost);
        }

        [Fact]
        public async Task MixedVendorOffer_BuildPresetOverrides_WalksSolverTreeOnly_UnaffectedByComponentLeaves()
        {
            // BuildPresetOverrides/CollectPresetOverrides walk
            // PlanSolveContext.Tree (RecipeNode/RecipeOption) - a
            // completely separate object graph from CraftingTreeNode/the
            // synthetic component leaves, which never correspond to any
            // RecipeNode at all. This proves it end to end: building a
            // preset override map and resolving with it neither throws nor
            // somehow keys an override off a negative synthetic NodeId (a
            // RecipeNode's own NodeId is always >= 0 - see RecipeNodeIds).
            var (pipeline, result) = await GenerateMixedVendorPlanAsync();

            var buyAll = CraftingPlanPipeline.BuildPresetOverrides(result.SolveContext, AcquisitionSource.BuyFromTp);
            Assert.All(buyAll.Keys, nodeId => Assert.True(nodeId >= 0));

            var resolved = pipeline.ResolveWithOverrides(result.SolveContext, buyAll);

            // Item 1 has no TP price at all, so the "buy all" preset cannot
            // apply to it (infeasible override is ignored) - it keeps its
            // vendor decision and its component leaves, proving the preset
            // build/resolve pass never disturbed them.
            Assert.Equal(AcquisitionSource.BuyFromVendor, resolved.Plan.Steps[0].Source);
            Assert.Equal(2, resolved.CraftingTree.Children.Count);
        }

        /// <summary>
        /// Item 1's BASELINE decision is Craft (a cheap 1x item-2 recipe
        /// undercuts the vendor offer below), so the winning offer's item cost
        /// component (id 42) is never scanned by the decisions-only
        /// AddVendorItemComponentIds overload - only the every-offer overload
        /// AddAllVendorOfferItemComponentIds can widen metadata AND
        /// BuildOwnedVendorItemComponentAmounts' ownership scan for it.
        /// A manual per-node override forcing item 1 to BuyFromVendor via
        /// ResolveWithOverrides then surfaces that leaf: without the metadata
        /// fix it renders "Unknown Item"/null icon forever; without the
        /// ownership fix it shows a correct name and icon but NO have pill
        /// forever, even with the item owned (ResolveWithOverrides re-fetches
        /// neither - see its own doc comment).
        ///
        /// The offer's non-coin Currency line (id 23) is the exact
        /// currency-side sibling: BuildOwnedCurrencyAmounts used to scope its
        /// ownership scan to the baseline plan's aggregated CurrencyCosts, so
        /// a currency surfaced only by this override had the same permanent
        /// missing-pill gap - the wallet entry below proves it is closed.
        /// </summary>
        [Fact]
        public async Task MixedVendorOffer_NotBaselineWinner_ResolveWithOverrides_StillResolvesRealItemMetadataAndOwnership()
        {
            var builder = PipelineBuilder.Create()
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
                .WithPrice(2, buyUnitPrice: 1, sellUnitPrice: 1) // craft is cheap - the baseline winner
                .WithPrice(42, buyUnitPrice: 10, sellUnitPrice: 20)
                .WithItem(1, "Amalgamated Rift Essence", "essence.png")
                .WithItem(2, "Cheap Ingredient", "cheap.png")
                .WithItem(42, "Glob of Ectoplasm", "ecto.png")
                .WithInventoryReducer();

            // Snapshot has 4 of item 42 in the account - partial coverage
            // of the 10 (5 * requested qty 2) the override's winning offer
            // will need. Attached at generation time (while the baseline
            // decision is still Craft, so item 42 never touches
            // decisions-scoped ownership either) to prove
            // BuildOwnedVendorItemComponentAmounts' widened vendorOffers
            // scan - not just AddVendorItemComponentIds' decisions scan -
            // is what puts 42 into PlanSolveContext.OwnedVendorItemAmounts.
            //
            // Wallet has 5 of currency 23 - full coverage of the 6 (3 *
            // requested qty 2) the override's winning offer will need for
            // that non-coin currency component, same "baseline decision is
            // Craft, so decisions-scoped ownership never sees it" setup as
            // item 42 above, proving BuildOwnedCurrencyAmounts' widened
            // vendorOffers scan populates PlanSolveContext.
            // OwnedCurrencyAmounts for it too.
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 42, Count = 4, Source = AccountItemIndex.SourceMaterialStorage },
                },
                Wallet = new List<SnapshotWalletEntry>
                {
                    new SnapshotWalletEntry { CurrencyId = 23, Value = 5 },
                },
            };

            CraftingPlanPipeline pipeline;
            CraftingPlanResult result;
            using (var tmp = new TempDirectory())
            {
                var loader = new VendorOfferLoader();
                var store = new VendorOfferStore(tmp.Path, loader);
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-not-baseline-w4b",
                        OutputItemId = 1,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Item", Id = 42, Count = 5 },
                            new CostLine { Type = "Currency", Id = 23, Count = 3 },
                        },
                        MerchantName = "Test NPC",
                    },
                });

                pipeline = builder.WithVendorOfferStore(store).Build();

                result = await pipeline.GenerateStructuredAsync(1, 2, snapshot, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
            }

            // Baseline: craft wins, so no component leaves exist yet, and
            // the winning decision never touched VendorItemCosts/
            // VendorCurrencyCosts at all - but OwnedVendorItemAmounts/
            // OwnedCurrencyAmounts must already carry item 42's and
            // currency 23's owned counts, both widened from vendorOffers
            // rather than decisions.
            Assert.Equal(CraftingDecision.Craft, result.CraftingTree.Decision);
            Assert.Empty(result.CraftingTree.Children.Where(c => c.IsCostComponent));
            Assert.NotNull(result.SolveContext.OwnedVendorItemAmounts);
            Assert.Equal(4, result.SolveContext.OwnedVendorItemAmounts[42]);
            Assert.NotNull(result.SolveContext.OwnedCurrencyAmounts);
            Assert.Equal(5, result.SolveContext.OwnedCurrencyAmounts[23]);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { result.CraftingTree.NodeId, AcquisitionSource.BuyFromVendor },
            };
            var resolved = pipeline.ResolveWithOverrides(result.SolveContext, overrides);

            Assert.Equal(CraftingDecision.BuyFromVendor, resolved.CraftingTree.Decision);
            var itemLeaf = resolved.CraftingTree.Children.Single(c => c.ItemId == 42);
            Assert.True(itemLeaf.IsCostComponent);
            Assert.Equal("Glob of Ectoplasm", itemLeaf.Name);
            Assert.Equal("ecto.png", itemLeaf.IconUrl);
            // The ownership fix under test: 4 owned out of 10 needed
            // (5 * requested qty 2) must survive the local re-solve, not
            // silently reset to 0 because this offer was never the
            // baseline winner.
            Assert.Equal(10, itemLeaf.Quantity);
            Assert.Equal(4, itemLeaf.ComponentOwnedQuantity);

            // Regression: the currency-side twin of the item
            // assertion above - 5 owned out of 6 needed (3 * requested
            // qty 2) must equally survive the local re-solve, proving
            // BuildOwnedCurrencyAmounts' widened vendorOffers scan (not
            // just plan.CurrencyCosts) is what put currency 23 into
            // PlanSolveContext.OwnedCurrencyAmounts in the first place.
            var currencyLeaf = resolved.CraftingTree.Children.Single(c => c.ItemId == 23);
            Assert.True(currencyLeaf.IsCostComponent);
            Assert.Equal(6, currencyLeaf.Quantity);
            Assert.Equal(5, currencyLeaf.ComponentOwnedQuantity);
            // Cost cell deliberately blank for currency components (repo
            // invariant restated in BuildVendorCostComponentLeaves' doc
            // comment) - unaffected by this round's ownership fix.
            Assert.Null(currencyLeaf.SubtreeCost);
        }

        /// <summary>
        /// Proves the stacked "component leaves + reference branch" shape
        /// CraftingTreeBuilder.BuildNode produces for a Craft-baseline item
        /// overridden to BuyFromVendor survives the ResolveWithOverrides
        /// round trip, and that ReceiptCaptionHelper - the exact consumer
        /// TreeSectionController's render call sites feed - still finds a
        /// valid split on the resulting node. This is the deepest
        /// Blish-free seam for this path: TreeSectionController itself is
        /// Blish-bound, so a render-path miss beyond this point cannot
        /// surface here - see KNOWN-ISSUES #62.
        /// </summary>
        [Fact]
        public async Task MixedVendorOffer_NotBaselineWinner_ResolveWithOverrides_ProducesReferenceBranchWithValidCaptionSplit()
        {
            var builder = PipelineBuilder.Create()
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
                .WithPrice(2, buyUnitPrice: 1, sellUnitPrice: 1) // craft is cheap - the baseline winner
                .WithPrice(42, buyUnitPrice: 10, sellUnitPrice: 20)
                .WithItem(1, "Amalgamated Rift Essence", "essence.png")
                .WithItem(2, "Cheap Ingredient", "cheap.png")
                .WithItem(42, "Glob of Ectoplasm", "ecto.png")
                .WithInventoryReducer();

            CraftingPlanPipeline pipeline;
            CraftingPlanResult result;
            using (var tmp = new TempDirectory())
            {
                var loader = new VendorOfferLoader();
                var store = new VendorOfferStore(tmp.Path, loader);
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-not-baseline-caption",
                        OutputItemId = 1,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Item", Id = 42, Count = 5 },
                            new CostLine { Type = "Currency", Id = 23, Count = 3 },
                        },
                        MerchantName = "Test NPC",
                    },
                });

                pipeline = builder.WithVendorOfferStore(store).Build();

                result = await pipeline.GenerateStructuredAsync(1, 2, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
            }

            Assert.Equal(CraftingDecision.Craft, result.CraftingTree.Decision);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { result.CraftingTree.NodeId, AcquisitionSource.BuyFromVendor },
            };
            var resolved = pipeline.ResolveWithOverrides(result.SolveContext, overrides);

            Assert.Equal(CraftingDecision.BuyFromVendor, resolved.CraftingTree.Decision);
            // The exact live shape: component leaves (item 42, currency 23)
            // stacked ahead of the reference-branch ingredient (item 2) -
            // see CraftingTreeBuilder.BuildNode's componentLeaves != null &&
            // wantsReferenceBranch branch.
            Assert.True(resolved.CraftingTree.IsReferenceBranch);
            Assert.Equal(3, resolved.CraftingTree.Children.Count);
            Assert.True(resolved.CraftingTree.Children[0].IsCostComponent);
            Assert.True(resolved.CraftingTree.Children[1].IsCostComponent);
            Assert.False(resolved.CraftingTree.Children[2].IsCostComponent);

            int splitIndex = ReceiptCaptionHelper.ComputeCaptionSplitIndex(resolved.CraftingTree);
            Assert.Equal(2, splitIndex);
            Assert.Equal(
                ReceiptCaptionHelper.VendorPriceCaption,
                ReceiptCaptionHelper.CaptionForChildIndex(splitIndex, 0));
            Assert.Equal(
                ReceiptCaptionHelper.CraftReferenceCaption,
                ReceiptCaptionHelper.CaptionForChildIndex(splitIndex, splitIndex));
        }

        // This test used to prove the (now-deleted, test-only)
        // GenerateAsync produced the same base plan as GenerateStructuredAsync
        // with a null snapshot, plus the latter's extra structured fields.
        // With only one entry point left, the assertion intent becomes:
        // GenerateStructuredAsync itself, with no snapshot, still produces
        // the expected craft-vs-buy plan (same economics as before - craft
        // via 3x item 2 at 300 total beats buying item 1 outright at 10000)
        // and still populates its structured-only fields on a snapshot-free
        // run.
        [Fact]
        public async Task GenerateStructuredAsync_NullSnapshot_ProducesCraftPlanAndPopulatesStructuredFields()
        {
            var pipeline = PipelineBuilder.BuildOwnMaterialsPipeline(out var priceApi, ingredientCount: 3);
            priceApi.AddPrice(1, buyUnitPrice: 5000, sellUnitPrice: 10000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // Craft (3 x 100 = 300, InstantBuy basis) beats buying item 1
            // outright (10000): one craft step for item 1, one buy step for
            // its 3x item 2 ingredient.
            Assert.Equal(2, result.Plan.Steps.Count);
            var craftStep = result.Plan.Steps.FirstOrDefault(s => s.ItemId == 1);
            Assert.NotNull(craftStep);
            Assert.Equal(AcquisitionSource.Craft, craftStep.Source);
            var buyStep = result.Plan.Steps.FirstOrDefault(s => s.ItemId == 2);
            Assert.NotNull(buyStep);
            Assert.Equal(AcquisitionSource.BuyFromTp, buyStep.Source);
            Assert.Equal(3, buyStep.Quantity);

            // Structured result has extra fields populated
            Assert.NotNull(result.RequiredDisciplines);
            Assert.NotNull(result.RequiredRecipes);
            Assert.NotNull(result.DebugLog);
            Assert.Empty(result.UsedMaterials);
        }

        [Fact]
        public async Task GenerateStructuredAsync_WithSnapshot_ReducesTree()
        {
            // Not SingleRecipeTree: this recipe deliberately carries no
            // AutoLearned flag.
            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 5 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                })
                .WithPrice(1, buyUnitPrice: 5000, sellUnitPrice: 10000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png")
                .WithInventoryReducer()
                .Build();

            // Snapshot owns 3 of ingredient (item 2)
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 2, Count = 3, Source = AccountItemIndex.SourceMaterialStorage },
                },
            };

            var withoutSnapshot = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            var withSnapshot = await pipeline.GenerateStructuredAsync(1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // With snapshot should buy fewer of item 2
            var buyStepWithout = withoutSnapshot.Plan.Steps
                .FirstOrDefault(s => s.ItemId == 2 && s.Source == AcquisitionSource.BuyFromTp);
            var buyStepWith = withSnapshot.Plan.Steps
                .FirstOrDefault(s => s.ItemId == 2 && s.Source == AcquisitionSource.BuyFromTp);

            Assert.NotNull(buyStepWithout);
            Assert.Equal(5, buyStepWithout.Quantity);
            Assert.NotNull(buyStepWith);
            Assert.Equal(2, buyStepWith.Quantity); // 5 - 3 = 2

            // UsedMaterials should report the 3 consumed
            Assert.Single(withSnapshot.UsedMaterials);
            Assert.Equal(2, withSnapshot.UsedMaterials[0].ItemId);
            Assert.Equal(3, withSnapshot.UsedMaterials[0].QuantityUsed);
        }
    }
}
