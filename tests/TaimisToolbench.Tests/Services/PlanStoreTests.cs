using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // Plan persistence across module restarts. Mirrors
    // SnapshotStoreTests' shape (a real store against a real temp
    // directory - no fake file I/O). The round-trip fidelity tests build a
    // real CraftingPlanResult via CraftingPlanPipeline + the fake API
    // clients (InMemoryRecipeApiClient/InMemoryPriceApiClient/
    // InMemoryItemApiClient), matching CraftingPlanPipelineTests' own
    // fixture shape, rather than hand-constructing a CraftingPlanResult -
    // the whole risk this package investigated (PlanSolveContext's
    // interface-typed dictionaries/ISet, CurrencyValuation/
    // HomesteadEfficiencyTiers' non-default constructors, the RecipeNode/
    // CraftingTreeNode trees) only shows up on a REAL pipeline-produced
    // result.
    public class PlanStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly PlanStore _store;

        public PlanStoreTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _store = new PlanStore(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
        }

        private static CraftingPlanPipeline BuildPipeline(out InMemoryPriceApiClient priceApi)
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
                Flags = new List<string> { "AutoLearned" },
            });

            priceApi = new InMemoryPriceApiClient();

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1, "Target", "t.png");
            itemApi.AddItem(2, "Ingredient", "i.png");

            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi));
        }

        private static CraftingPlanPipeline BuildPipelineWith(
            InMemoryRecipeApiClient recipeApi, InMemoryPriceApiClient priceApi)
        {
            var itemApi = new InMemoryItemApiClient();
            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi));
        }

        // A THREE-level real tree (item 1 <-
        // recipe 10 <- item 2 <- recipe 20 <- item 3), so
        // result.CraftingTree.Children[0].Children[0] is a real depth-2
        // node - the PlanStructuralValidator tests below corrupt exactly
        // that node (the default-collapsed depth the "Expand All"/per-node-
        // toggle crash sites, and PlanContentHeightMath.IsNodeExpanded's
        // own "depth < 2" default, both hinge on). Item 1/2 have no TP
        // price at all (never passed to priceApi.AddPrice), so CanBuyTp is
        // false for both and craft is the only feasible source - both
        // therefore always have Children, regardless of price ordering.
        private static CraftingPlanPipeline BuildDeepPipeline(out InMemoryPriceApiClient priceApi)
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
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });
            recipeApi.AddSearchResult(2, 20);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 20,
                OutputItemId = 2,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 3, Count = 1 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 100,
                Flags = new List<string> { "AutoLearned" },
            });

            priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(3, buyUnitPrice: 10, sellUnitPrice: 100);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1, "Target", "t.png");
            itemApi.AddItem(2, "Middle", "m.png");
            itemApi.AddItem(3, "Leaf", "l.png");

            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi));
        }

        // Mirrors PipelineBuilder.BuildOwnMaterialsPipeline
        // exactly (item 1 <- recipe 10 <- ingredientCount x item 2, with a
        // real InventoryReducer wired in) - reused here (rather than made
        // accessible cross-class) so this file's own fixtures stay
        // self-contained, matching every other helper already duplicated
        // between the two files (BuildPipeline itself, etc.).
        private static CraftingPlanPipeline BuildOwnMaterialsPipeline(
            out InMemoryPriceApiClient priceApi, int ingredientCount = 5)
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
                    new RawIngredient { Type = "Item", Id = 2, Count = ingredientCount },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });

            priceApi = new InMemoryPriceApiClient();

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1, "Target", "t.png");
            itemApi.AddItem(2, "Ingredient", "i.png");

            return new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi),
                reducer: new InventoryReducer());
        }

        // Mirrors CraftingPlanPipelineForceBuyTests.BuildForceBuyPipeline/
        // OwnFourOfIngredient exactly: item 1's fresh (zero-owned) buy(100)
        // < craft(5x30=150)*0.85=127.5, so item 1's node is force-buy-
        // flagged under OwnMaterialsMode.Valued - the scenario that
        // populates PlanSolveContext.ForceBuyOnlyNodeIds (an ISet<int>) with
        // real content, one of the exact shapes this sweep
        // targets.
        private static CraftingPlanPipeline BuildForceBuyPipeline(out InMemoryPriceApiClient priceApi)
        {
            var pipeline = BuildOwnMaterialsPipeline(out priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 1000, sellUnitPrice: 100);
            priceApi.AddPrice(2, buyUnitPrice: 300, sellUnitPrice: 30);
            return pipeline;
        }

        private static AccountSnapshot OwnFourOfIngredient()
        {
            return new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry
                    {
                        ItemId = 2,
                        Count = 4,
                        Source = AccountItemIndex.SourceMaterialStorage,
                    },
                },
            };
        }

        private static PersistedPlan Wrap(
            CraftingPlanResult result, DateTime generatedAt, int quantity = 1, bool useOwn = false,
            PriceBasis priceBasis = PriceBasis.InstantBuy,
            IReadOnlyDictionary<int, AcquisitionSource> nodeOverrides = null,
            IReadOnlyList<int> ignoredItemIds = null,
            // Mirrors useOwn/priceBasis above - default true
            // matches ValueOwnMaterials' own real-world default (see
            // Views/CraftingPlanView.cs's _valueOwnMaterials field).
            bool valueOwn = true)
        {
            return new PersistedPlan
            {
                // SchemaVersion has no
                // property initializer any more (see PersistedPlan's own
                // doc comment) - every real construction site sets it
                // explicitly, so this fixture-building helper must too or
                // every test built through it would round-trip as
                // SchemaVersion 0 and get rejected as old-schema by
                // PlanStoreHelpers.DeserializePersistedPlan.
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = generatedAt,
                RequestItems = new List<PlanRequestItem> { new PlanRequestItem { ItemId = 1, Quantity = quantity } },
                UseOwnMaterials = useOwn,
                PriceBasis = priceBasis,
                ValueOwnMaterials = valueOwn,
                Result = result,
                // PersistedPlan.NodeOverrides/IgnoredItemIds
                // are empty (never null) on every real persist path (see
                // Module.PersistAfterGenerateAsync/
                // PersistResolvedPlanInBackground) - defaulting the same
                // way here keeps every pre-existing call to this helper
                // exercising that same real shape instead of null.
                NodeOverrides = nodeOverrides ?? new Dictionary<int, AcquisitionSource>(),
                IgnoredItemIds = ignoredItemIds ?? new List<int>(),
            };
        }

        private static string ToJson(object value) => JsonConvert.SerializeObject(value, Formatting.Indented);

        [Fact]
        public async Task Save_Load_RoundTripsResultAsSameViewModel()
        {
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            _store.Save(Wrap(result, new DateTime(2026, 8, 9, 10, 30, 0, DateTimeKind.Local)));

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.NotNull(loaded.Result);
            Assert.NotSame(result, loaded.Result);

            var vmBuilder = new PlanViewModelBuilder();
            var originalVm = vmBuilder.Build(result);
            var reloadedVm = vmBuilder.Build(loaded.Result);

            Assert.Equal(ToJson(originalVm), ToJson(reloadedVm));
        }

        [Fact]
        public async Task Save_Load_ResolveWithOverrides_MatchesOriginalContext()
        {
            var pipeline = BuildPipeline(out var priceApi);
            // Craft (30) beats buy (1000); gives the override something real to flip.
            priceApi.AddPrice(1, buyUnitPrice: 1000, sellUnitPrice: 2000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(result.SolveContext);
            Assert.Equal(CraftingDecision.Craft, result.CraftingTree.Decision);

            _store.Save(Wrap(result, DateTime.Now));

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded?.Result?.SolveContext);

            // Same override, applied to both the original in-memory context
            // and the reloaded-from-disk one - the correctness bar:
            // both must produce identical decisions.
            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { result.CraftingTree.NodeId, AcquisitionSource.BuyFromTp },
            };

            var resolvedOriginal = pipeline.ResolveWithOverrides(result.SolveContext, overrides);
            var resolvedReloaded = pipeline.ResolveWithOverrides(loaded.Result.SolveContext, overrides);

            Assert.Equal(AcquisitionSource.BuyFromTp, resolvedOriginal.Plan.Steps[0].Source);
            Assert.Equal(resolvedOriginal.Plan.TotalCoinCost, resolvedReloaded.Plan.TotalCoinCost);
            Assert.Equal(resolvedOriginal.CraftingProfit, resolvedReloaded.CraftingProfit);
            Assert.Equal(resolvedOriginal.CraftingTree.Decision, resolvedReloaded.CraftingTree.Decision);

            var vmBuilder = new PlanViewModelBuilder();
            Assert.Equal(ToJson(vmBuilder.Build(resolvedOriginal)), ToJson(vmBuilder.Build(resolvedReloaded)));
        }

        [Fact]
        public async Task Save_Load_RequestAndTimestampRoundTrip()
        {
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 3, null, CancellationToken.None,
                priceBasis: PriceBasis.BuyOrder);

            var timestamp = new DateTime(2026, 8, 9, 14, 22, 0, DateTimeKind.Local);
            _store.Save(Wrap(result, timestamp, quantity: 3, useOwn: true, priceBasis: PriceBasis.BuyOrder, valueOwn: false));

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.Equal(timestamp, loaded.GeneratedAt);
            Assert.True(loaded.UseOwnMaterials);
            Assert.Equal(PriceBasis.BuyOrder, loaded.PriceBasis);
            // ValueOwnMaterials round-trips independently of
            // UseOwnMaterials - false here specifically to prove it is not
            // just silently mirroring useOwn's own true value above.
            Assert.False(loaded.ValueOwnMaterials);
            Assert.NotNull(loaded.RequestItems);
            Assert.Single(loaded.RequestItems);
            Assert.Equal(1, loaded.RequestItems[0].ItemId);
            Assert.Equal(3, loaded.RequestItems[0].Quantity);
        }

        // --- Restore-inputs pinning (restore-inputs branch): the three
        // persisted-but-previously-ignored request inputs must round-trip
        // in their NON-default direction, because the live controls they
        // reseed default the other way (Use Own Materials checkbox true,
        // price basis BuyOrder) - a wiring slip that dropped the loaded
        // value would be invisible to a default-direction assertion. ---
        [Fact]
        public void Save_Load_UseOwnMaterialsFalse_RoundTripsAsFalse()
        {
            _store.Save(new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem> { new PlanRequestItem { ItemId = 5, Quantity = 1 } },
                UseOwnMaterials = false,
                PriceBasis = PriceBasis.BuyOrder,
                Result = new CraftingPlanResult { Plan = new CraftingPlan { TargetItemId = 5, TargetQuantity = 1 } },
            });

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.False(loaded.UseOwnMaterials);
        }

        [Fact]
        public void Save_Load_NonDefaultPriceBasis_RoundTrips()
        {
            _store.Save(new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem> { new PlanRequestItem { ItemId = 5, Quantity = 1 } },
                UseOwnMaterials = true,
                PriceBasis = PriceBasis.InstantBuy,
                Result = new CraftingPlanResult { Plan = new CraftingPlan { TargetItemId = 5, TargetQuantity = 1 } },
            });

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.Equal(PriceBasis.InstantBuy, loaded.PriceBasis);
        }

        [Fact]
        public void Save_Load_MultiItemRequestWithQuantities_RoundTripsInRequestOrder()
        {
            _store.Save(new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 5, Quantity = 3 },
                    new PlanRequestItem { ItemId = 6, Quantity = 250 },
                    new PlanRequestItem { ItemId = 7, Quantity = 1 },
                },
                Result = new CraftingPlanResult { Plan = new CraftingPlan { TargetItemId = 5, TargetQuantity = 3 } },
            });

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.Equal(new[] { 5, 6, 7 }, loaded.RequestItems.Select(r => r.ItemId));
            Assert.Equal(new[] { 3, 250, 1 }, loaded.RequestItems.Select(r => r.Quantity));
        }

        [Fact]
        public async Task Save_AfterOverride_RoundTripsOverriddenResultInPlace()
        {
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 1000, sellUnitPrice: 2000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { result.CraftingTree.NodeId, AcquisitionSource.BuyFromTp },
            };
            var overridden = pipeline.ResolveWithOverrides(result.SolveContext, overrides);
            Assert.Equal(AcquisitionSource.BuyFromTp, overridden.Plan.Steps[0].Source);

            // "In place": same GeneratedAt/request as the original Generate,
            // only Result swapped for the override-updated one - mirrors
            // Module.PersistResolvedPlanInBackground's own shape.
            var generatedAt = new DateTime(2026, 8, 9, 9, 0, 0, DateTimeKind.Local);
            _store.Save(Wrap(result, generatedAt));
            _store.Save(Wrap(overridden, generatedAt));

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.Equal(generatedAt, loaded.GeneratedAt);
            Assert.NotNull(loaded.Result);
            Assert.Equal(AcquisitionSource.BuyFromTp, loaded.Result.Plan.Steps[0].Source);

            var vmBuilder = new PlanViewModelBuilder();
            Assert.Equal(ToJson(vmBuilder.Build(overridden)), ToJson(vmBuilder.Build(loaded.Result)));
        }

        // --- Regression: the user's decision-pill overrides
        // themselves must round-trip, not just the Result they produced -
        // see Models/PersistedPlan.cs's NodeOverrides/IgnoredItemIds doc
        // comments and TreeSectionController.RestoreOverrides. ---
        [Fact]
        public async Task Save_Load_NodeOverridesAndIgnoredItemIds_RoundTripAndDriveIdenticalReResolve()
        {
            var pipeline = BuildOwnMaterialsPipeline(out var priceApi, ingredientCount: 5);
            priceApi.AddPrice(1, buyUnitPrice: 10000, sellUnitPrice: 20000); // buying the target outright is far pricier - craft wins
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100); // BuyInstant (craft-cost basis) = 100

            var result = await pipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            int rootNodeId = result.CraftingTree.NodeId;

            // Mirrors what Module.PersistResolvedPlanInBackground actually
            // persists: the SAME overrides/ignoredItemIds that produced
            // Result, alongside Result itself - not just the Result.
            var overrides = new Dictionary<int, AcquisitionSource> { { rootNodeId, AcquisitionSource.Craft } };
            var ignoredItemIds = new HashSet<int> { 2 };
            var overridden = pipeline.ResolveWithOverrides(result.SolveContext, overrides, ignoredItemIds);
            Assert.Equal(0, overridden.Plan.TotalCoinCost); // craft, with its only ingredient ignored (zeroed)

            var generatedAt = new DateTime(2026, 8, 9, 11, 0, 0, DateTimeKind.Local);
            _store.Save(Wrap(overridden, generatedAt, nodeOverrides: overrides, ignoredItemIds: new List<int>(ignoredItemIds)));

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);

            // The overrides/ignore set round-trip with their exact content -
            // this is the state a restored session's decision-pill loop
            // reseeds from (TreeSectionController.RestoreOverrides), not
            // merely the Result they happened to produce.
            Assert.NotNull(loaded.NodeOverrides);
            Assert.Single(loaded.NodeOverrides);
            Assert.Equal(AcquisitionSource.Craft, loaded.NodeOverrides[rootNodeId]);
            Assert.NotNull(loaded.IgnoredItemIds);
            Assert.Equal(new[] { 2 }, loaded.IgnoredItemIds);

            // The persistence correctness bar: re-applying the RELOADED
            // overrides/ignoredItemIds to the RELOADED context (exactly what
            // a FURTHER pill click after a restart would do) must produce
            // identical decisions/economics to the same overrides applied to
            // the original in-memory context - proving the overrides
            // THEMSELVES survive to drive a further re-solve, not just that
            // the already-overridden Result rendered the same.
            var reloadedIgnored = new HashSet<int>(loaded.IgnoredItemIds);
            var reResolvedOriginal = pipeline.ResolveWithOverrides(result.SolveContext, overrides, ignoredItemIds);
            var reResolvedReloaded = pipeline.ResolveWithOverrides(loaded.Result.SolveContext, loaded.NodeOverrides, reloadedIgnored);

            Assert.Equal(0, reResolvedReloaded.Plan.TotalCoinCost);
            Assert.Equal(reResolvedOriginal.CraftingTree.Decision, reResolvedReloaded.CraftingTree.Decision);
            Assert.Equal(reResolvedOriginal.CraftingTree.Children[0].IsIgnored, reResolvedReloaded.CraftingTree.Children[0].IsIgnored);

            var vmBuilder = new PlanViewModelBuilder();
            Assert.Equal(ToJson(vmBuilder.Build(reResolvedOriginal)), ToJson(vmBuilder.Build(reResolvedReloaded)));
        }

        [Fact]
        public async Task Save_Load_FreshGenerate_NodeOverridesAndIgnoredItemIdsAreEmptyNotNull()
        {
            // Module.PersistAfterGenerateAsync (a fresh Generate, no
            // overrides applied yet) always persists empty collections, not
            // null - see that method's own doc comment. Deserializing an
            // empty JSON array/object must still hand back an empty
            // (usable) collection, not null, so TreeSectionController.
            // RestoreOverrides' own null-check branches stay purely
            // defensive rather than load-bearing on the ordinary path.
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            _store.Save(Wrap(result, DateTime.Now));

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.NotNull(loaded.NodeOverrides);
            Assert.Empty(loaded.NodeOverrides);
            Assert.NotNull(loaded.IgnoredItemIds);
            Assert.Empty(loaded.IgnoredItemIds);
        }

        [Fact]
        public void LoadLatest_MissingFile_ReturnsNull()
        {
            Assert.Null(_store.LoadLatest()?.Plan);
        }

        [Fact]
        public void LoadLatest_CorruptTruncatedJson_ReturnsNullAndLogsWarnNoThrow()
        {
            string filePath = Path.Combine(_tempDir, "plan.json");
            File.WriteAllText(filePath, "{ \"Result\": { \"Plan\": { \"Target");

            string capturedMessage = null;
            Exception capturedException = null;
            var store = new PlanStore(_tempDir, (message, ex) =>
            {
                capturedMessage = message;
                capturedException = ex;
            });

            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded);
            Assert.NotNull(capturedMessage);
            Assert.NotNull(capturedException);
        }

        [Fact]
        public void LoadLatest_MissingResult_ReturnsNullAndLogsWarn()
        {
            // Renamed from LoadLatest_WrongSchema_MissingResult_...: the
            // version and structural verdicts are separate throws now, and
            // this file takes the structural (corrupt) one - Result/Plan
            // has existed since schema 1, so a document without it was
            // never a valid plan at any version.
            string filePath = Path.Combine(_tempDir, "plan.json");
            File.WriteAllText(filePath, "{ \"GeneratedAt\": \"2026-08-09T00:00:00\", \"UseOwnMaterials\": true }");

            string capturedMessage = null;
            var store = new PlanStore(_tempDir, (message, ex) => capturedMessage = message);

            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded);
            Assert.NotNull(capturedMessage);
        }

        // --- Regression: SchemaVersion is what makes the
        // "old-schema file = fresh start with one log line" tolerance
        // contract enforceable against a FUTURE member rename/removal, not
        // just a Result/Plan structurally missing entirely - see
        // Models/PersistedPlan.cs's CurrentSchemaVersion doc comment.
        //
        // A file at an older SHIPPED version asserts the INFO channel, not
        // the error one. Those asserted onError (wired to Warn in Module.cs)
        // until the 2026-08-23 investigation: that mismatch is an expected,
        // self-healing outcome, and logging it at the same severity and in
        // the same words as real corruption is what made that incident
        // unexplainable from the log alone. A version this build never
        // shipped (0, or one from the future) is the opposite case and stays
        // on the error channel - see the pair at the end of this block. ---
        [Fact]
        public void LoadLatest_ExplicitZeroSchemaVersion_ReturnsNullAndLogsWarn()
        {
            string filePath = Path.Combine(_tempDir, "plan.json");
            // Structurally valid (Result/Plan present, would have passed
            // the old structural check) but stamped 0 - a version no build
            // ever wrote, so it is the same verdict as the omitted-field
            // case below rather than ordinary drift.
            File.WriteAllText(filePath,
                "{ \"SchemaVersion\": 0, \"Result\": { \"Plan\": { \"TargetItemId\": 1 } } }");

            string capturedError = null;
            Exception capturedException = null;
            string capturedInfo = null;
            var store = new PlanStore(
                _tempDir,
                (message, ex) =>
                {
                    capturedError = message;
                    capturedException = ex;
                },
                message => capturedInfo = message);

            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded);
            Assert.Null(capturedInfo);
            Assert.NotNull(capturedError);
            Assert.Contains("no schema version", capturedException.Message);
            // Not damage either - the file parsed. It must not borrow
            // corruption's words any more than drift's channel.
            Assert.DoesNotContain("corrupt", capturedException.Message, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LoadLatest_FutureSchemaVersionFile_ReturnsNullAndLogsWarn()
        {
            // A plan written by a NEWER build, read after a downgrade. Not
            // drift: this build cannot know what is in it, and reporting it
            // at Info would present an unreadable file as routine. Both
            // versions are named, so the log says which way round it is.
            int future = PersistedPlan.CurrentSchemaVersion + 1;
            string filePath = Path.Combine(_tempDir, "plan.json");
            File.WriteAllText(filePath,
                "{ \"SchemaVersion\": " + future + ", \"Result\": { \"Plan\": { \"TargetItemId\": 1 } } }");

            string capturedError = null;
            Exception capturedException = null;
            string capturedInfo = null;
            var store = new PlanStore(
                _tempDir,
                (message, ex) =>
                {
                    capturedError = message;
                    capturedException = ex;
                },
                message => capturedInfo = message);

            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded);
            Assert.Null(capturedInfo);
            Assert.NotNull(capturedError);
            Assert.Contains($"schema {future}", capturedException.Message);
            Assert.Contains(PersistedPlan.CurrentSchemaVersion.ToString(), capturedException.Message);
        }

        [Fact]
        public void LoadLatest_VomSchemaVersion1File_ReturnsNullAndLogsInfo()
        {
            // CurrentSchemaVersion bumped 1 -> 2
            // for the new PersistedPlan.ValueOwnMaterials field. A
            // genuinely realistic old file - SchemaVersion 1 (the actual
            // previous CurrentSchemaVersion, not the synthetic "0"
            // LoadLatest_ExplicitZeroSchemaVersion_ReturnsNullAndLogsWarn
            // above uses) - must be rejected the same way, degrading to
            // Module's "no restored plan" fresh-start path, not silently
            // defaulting ValueOwnMaterials to false. Unlike that 0, this is
            // drift and belongs on the Info channel.
            //
            // This is the exact file the 2026-08-23 incident hit: a plan
            // written at schema 1 on 2026-08-15, read 8 days later by a
            // build at 3. Renamed from ...LogsWarn and re-pointed at the
            // Info channel - the outcome is unchanged (null, fresh start),
            // only the severity is now honest. The message must name both
            // versions, which is what that investigation had to recover
            // from commit timestamps instead.
            string filePath = Path.Combine(_tempDir, "plan.json");
            File.WriteAllText(filePath,
                "{ \"SchemaVersion\": 1, \"Result\": { \"Plan\": { \"TargetItemId\": 1 } } }");

            string capturedError = null;
            string capturedInfo = null;
            var store = new PlanStore(
                _tempDir, (message, ex) => capturedError = message, message => capturedInfo = message);

            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded);
            Assert.Null(capturedError);
            Assert.NotNull(capturedInfo);
            Assert.Contains("schema 1", capturedInfo);
            Assert.Contains(PersistedPlan.CurrentSchemaVersion.ToString(), capturedInfo);
            Assert.DoesNotContain("corrupt", capturedInfo, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void LoadLatest_QualityAuditSchemaVersion2File_ReturnsNullAndLogsInfo()
        {
            // Quality-audit fix (B1): CurrentSchemaVersion bumped 2 -> 3
            // because the persisted graph grew ~275 lines of new members
            // (see PersistedPlan.CurrentSchemaVersion's own doc comment)
            // after the 1 -> 2 bump with no matching version bump. A
            // genuinely realistic old file - SchemaVersion 2 (the actual
            // previous CurrentSchemaVersion) - must be rejected exactly
            // the same way as the SchemaVersion-1 file above, degrading
            // to Module's "no restored plan" fresh-start path, not
            // silently restoring with the newer members (CraftCostBreakdown,
            // CompetencyIndependentForceBuyNodeIds, UnreducedTree, etc.)
            // null.
            string filePath = Path.Combine(_tempDir, "plan.json");
            File.WriteAllText(filePath,
                "{ \"SchemaVersion\": 2, \"Result\": { \"Plan\": { \"TargetItemId\": 1 } } }");

            string capturedError = null;
            string capturedInfo = null;
            var store = new PlanStore(
                _tempDir, (message, ex) => capturedError = message, message => capturedInfo = message);

            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded);
            Assert.Null(capturedError);
            Assert.NotNull(capturedInfo);
        }

        [Fact]
        public void LoadLatest_SchemaDriftAndCorruption_ReportDistinctMessagesAndSeverities()
        {
            // The 2026-08-23 blocker itself: both verdicts threw one
            // InvalidDataException whose text said "corrupt or old-schema
            // file", so the log could not tell an expected version
            // rejection from real damage. This test fails if they are ever
            // merged again - in EITHER direction (same words, or same
            // channel). Severity is the channel: PlanStore takes no logger,
            // and Module.cs wires onError -> Warn and onInfo -> Info (one
            // site each, see Module.Initialize).
            string filePath = Path.Combine(_tempDir, "plan.json");

            // Drift: a recognizable plan, wrong version.
            File.WriteAllText(filePath,
                "{ \"SchemaVersion\": 1, \"Result\": { \"Plan\": { \"TargetItemId\": 1 } } }");

            string driftError = null;
            string driftInfo = null;
            var driftStore = new PlanStore(
                _tempDir, (message, ex) => driftError = message, message => driftInfo = message);
            Assert.Null(driftStore.LoadLatest()?.Plan);

            // Damage: current version, but no Result/Plan at all - a shape
            // that was never a valid plan file at ANY schema version.
            File.WriteAllText(filePath,
                "{ \"SchemaVersion\": " + PersistedPlan.CurrentSchemaVersion + ", \"Result\": { } }");

            string damageError = null;
            string damageInfo = null;
            Exception damageException = null;
            var damageStore = new PlanStore(
                _tempDir,
                (message, ex) =>
                {
                    damageError = message;
                    damageException = ex;
                },
                message => damageInfo = message);
            Assert.Null(damageStore.LoadLatest()?.Plan);

            // Distinct severities: each verdict uses one channel and only
            // one, so neither can borrow the other's level.
            Assert.NotNull(driftInfo);
            Assert.Null(driftError);
            Assert.NotNull(damageError);
            Assert.Null(damageInfo);

            // Distinct messages: only damage says "corrupt", and only
            // drift names the versions.
            Assert.Contains("corrupt", damageException.Message, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("corrupt", driftInfo, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("schema 1", driftInfo);
            Assert.NotEqual(driftInfo, damageException.Message);
        }

        [Fact]
        public void LoadLatest_SingleItemPlan_ReMarksTheRestoredRootAsPlanRoot()
        {
            // CraftingTreeNode.IsPlanRoot is internal and so never
            // serialized (deliberately - a schema bump would discard every
            // existing user's saved plan). Restore is the one path that
            // produces a CraftingPlanResult without CraftingTreeBuilder,
            // so PlanStoreHelpers re-derives the flag; without that, a
            // restored root row offers the IGNORE pill again.
            var plan = new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem> { new PlanRequestItem { ItemId = 5, Quantity = 1 } },
                Result = new CraftingPlanResult
                {
                    Plan = new CraftingPlan { TargetItemId = 5, TargetQuantity = 1 },
                    CraftingTree = new CraftingTreeNode
                    {
                        ItemId = 5,
                        NodeId = 1,
                        Name = "Target",
                        Quantity = 1,
                        Decision = CraftingDecision.BuyFromTp,
                        CanBuyTp = true,
                        CanBuyVendor = true,
                        Children = new List<CraftingTreeNode>
                        {
                            new CraftingTreeNode
                            {
                                ItemId = 6,
                                NodeId = 2,
                                Name = "Child",
                                Quantity = 1,
                                Decision = CraftingDecision.BuyFromTp,
                                CanBuyTp = true,
                                CanBuyVendor = true,
                            },
                        },
                    },
                },
            };

            _store.Save(plan);
            var loaded = _store.LoadLatest()?.Plan;

            Assert.True(loaded.Result.CraftingTree.IsPlanRoot);
            Assert.False(loaded.Result.CraftingTree.Children[0].IsPlanRoot);
            Assert.DoesNotContain(
                DecisionPillPlanner.BuildPillSpecs(loaded.Result.CraftingTree),
                s => s.Kind == PillKind.Ignore);
            Assert.Contains(
                DecisionPillPlanner.BuildPillSpecs(loaded.Result.CraftingTree.Children[0]),
                s => s.Kind == PillKind.Ignore);
        }

        [Fact]
        public void LoadLatest_MultiItemPlan_ReMarksEveryRestoredRootAsPlanRoot()
        {
            // A batch has N roots, not one - MultiItemRoots must be walked
            // too, or only the first requested item keeps the suppression.
            var plan = new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 5, Quantity = 1 },
                    new PlanRequestItem { ItemId = 6, Quantity = 1 },
                },
                Result = new CraftingPlanResult
                {
                    Plan = new CraftingPlan { TargetItemId = 5, TargetQuantity = 1 },
                    MultiItemRoots = new List<CraftingTreeNode>
                    {
                        new CraftingTreeNode
                        {
                            ItemId = 5, NodeId = 1, Name = "A", Quantity = 1,
                            Decision = CraftingDecision.BuyFromTp, CanBuyTp = true,
                        },
                        new CraftingTreeNode
                        {
                            ItemId = 6, NodeId = 2, Name = "B", Quantity = 1,
                            Decision = CraftingDecision.BuyFromTp, CanBuyTp = true,
                        },
                    },
                },
            };

            _store.Save(plan);
            var loaded = _store.LoadLatest()?.Plan;

            Assert.All(loaded.Result.MultiItemRoots, r => Assert.True(r.IsPlanRoot));
        }

        [Fact]
        public void Save_Load_ExplicitCurrentSchemaVersion_RoundTrips()
        {
            // PersistedPlan.SchemaVersion has
            // NO property initializer any more - a caller (every real
            // construction site in Module.cs) must set it explicitly. This
            // proves the field itself round-trips correctly when set that
            // way, complementing LoadLatest_MissingSchemaVersionField_
            // ReturnsNullAndLogsWarn below, which proves the opposite case
            // (never set at all) is correctly rejected rather than silently
            // treated as current.
            var plan = new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem> { new PlanRequestItem { ItemId = 5, Quantity = 2 } },
                UseOwnMaterials = false,
                PriceBasis = PriceBasis.InstantBuy,
                Result = new CraftingPlanResult { Plan = new CraftingPlan { TargetItemId = 5, TargetQuantity = 2 } },
            };

            _store.Save(plan);
            var loaded = _store.LoadLatest()?.Plan;

            Assert.NotNull(loaded);
            Assert.Equal(PersistedPlan.CurrentSchemaVersion, loaded.SchemaVersion);
        }

        [Fact]
        public void LoadLatest_MissingSchemaVersionField_ReturnsNullAndLogsWarn()
        {
            // A file with the member omitted entirely rather than stamped
            // (written before the field existed, or by any code that forgets
            // to set it). Newtonsoft only overwrites properties present in
            // the JSON, so this is the exact case a `= CurrentSchemaVersion`
            // property initializer would have let sail through silently -
            // see PersistedPlan.SchemaVersion's own doc comment.
            //
            // The defect this guards is a PersistedPlan construction site
            // that never stamps the version: every plan it saves is
            // unrestorable, forever, and the only symptom is this log line.
            // So it takes the error channel, not the Info one the older-
            // shipped-version rejections use - the version it reads as (0)
            // is one no build ever wrote.
            string filePath = Path.Combine(_tempDir, "plan.json");
            File.WriteAllText(filePath,
                "{ \"GeneratedAt\": \"2026-08-09T00:00:00\", \"Result\": { \"Plan\": { \"TargetItemId\": 1 } } }");

            string capturedError = null;
            Exception capturedException = null;
            string capturedInfo = null;
            var store = new PlanStore(
                _tempDir,
                (message, ex) =>
                {
                    capturedError = message;
                    capturedException = ex;
                },
                message => capturedInfo = message);

            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded);
            Assert.Null(capturedInfo);
            Assert.NotNull(capturedError);
            Assert.Contains("no schema version", capturedException.Message);
        }

        [Fact]
        public void LoadLatest_EmptyFile_ReturnsNull()
        {
            string filePath = Path.Combine(_tempDir, "plan.json");
            File.WriteAllText(filePath, "");

            Assert.Null(_store.LoadLatest()?.Plan);
        }

        [Fact]
        public void Save_Load_ProducesNewInstance()
        {
            var plan = new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem> { new PlanRequestItem { ItemId = 5, Quantity = 2 } },
                UseOwnMaterials = false,
                PriceBasis = PriceBasis.InstantBuy,
                Result = new CraftingPlanResult
                {
                    Plan = new CraftingPlan { TargetItemId = 5, TargetQuantity = 2 },
                },
            };
            _store.Save(plan);

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.NotSame(plan, loaded);
            Assert.Equal(5, loaded.Result.Plan.TargetItemId);
        }

        [Fact]
        public void Save_LeavesNoTmpFileBehind()
        {
            var plan = new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem> { new PlanRequestItem { ItemId = 1, Quantity = 1 } },
                UseOwnMaterials = false,
                PriceBasis = PriceBasis.InstantBuy,
                Result = new CraftingPlanResult { Plan = new CraftingPlan { TargetItemId = 1, TargetQuantity = 1 } },
            };
            _store.Save(plan);

            string tmpPath = Path.Combine(_tempDir, "plan.json.tmp");
            Assert.False(File.Exists(tmpPath));
        }

        [Fact]
        public void Save_DirectoryCreationFails_InvokesOnErrorInsteadOfThrowing()
        {
            string blockingPath = Path.Combine(_tempDir, "blocked-data-dir");
            File.WriteAllText(blockingPath, "not a directory");

            string capturedMessage = null;
            Exception capturedException = null;
            var store = new PlanStore(blockingPath, (message, ex) =>
            {
                capturedMessage = message;
                capturedException = ex;
            });

            store.Save(new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                Result = new CraftingPlanResult { Plan = new CraftingPlan() },
            });

            Assert.NotNull(capturedMessage);
            Assert.NotNull(capturedException);
        }

        // --- Regression: the investigation's own flagged
        // "exact shapes" (ISet<int>, IReadOnlyDictionary<int,
        // IReadOnlyList<VendorOffer>>, a NON-empty CurrencyValuation/
        // HomesteadEfficiencyTiers) were never actually exercised by any
        // test above - every one built its pipeline with 4 args (no vendor
        // store, no account recipe client, no currencyValuation/
        // homesteadTiers/snapshot), so those SolveContext members were
        // always null/empty in every round trip. These three tests build
        // full-featured pipelines (vendor offers, a learned-recipe account
        // client, a real snapshot, non-default CurrencyValuation/
        // HomesteadEfficiencyTiers, and a genuine multi-item batch) so the
        // serialization-fidelity risk item 1 of KNOWN-ISSUES #53
        // investigated is actually exercised, not just asserted. ---
        [Fact]
        public async Task Save_Load_ForceBuyOnlyNodeIds_RoundTripsAndManualOverrideStillWinsAfterReload()
        {
            // Mirrors CraftingPlanPipelineForceBuyTests'
            // ResolveWithOverrides_ForceBuyPrePass_ManualOverrideStillWins
            // exactly, adding a persist/reload round trip in the middle -
            // PlanSolveContext.ForceBuyOnlyNodeIds (an ISet<int> computed
            // once at generation time) must survive that round trip for a
            // restored session's manual-override-beats-automatic-pre-pass
            // behavior to keep working identically.
            var pipeline = BuildForceBuyPipeline(out _);

            var initial = await pipeline.GenerateStructuredAsync(
                1, 1, OwnFourOfIngredient(), CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(CraftingDecision.BuyFromTp, initial.CraftingTree.Decision);
            Assert.NotNull(initial.SolveContext.ForceBuyOnlyNodeIds);
            Assert.NotEmpty(initial.SolveContext.ForceBuyOnlyNodeIds);

            _store.Save(Wrap(initial, DateTime.Now));
            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded?.Result?.SolveContext);

            Assert.NotNull(loaded.Result.SolveContext.ForceBuyOnlyNodeIds);
            Assert.Equal(
                new HashSet<int>(initial.SolveContext.ForceBuyOnlyNodeIds),
                new HashSet<int>(loaded.Result.SolveContext.ForceBuyOnlyNodeIds));

            // A no-op re-solve on the RELOADED context must still apply the
            // pre-pass exactly like the original generation did.
            var noOpReloaded = pipeline.ResolveWithOverrides(loaded.Result.SolveContext, null);
            Assert.Equal(AcquisitionSource.BuyFromTp, noOpReloaded.Plan.Steps[0].Source);
            Assert.Equal(100, noOpReloaded.Plan.TotalCoinCost);

            // A manual override on the RELOADED context must still win over
            // the automatic pre-pass, same as the original.
            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { initial.CraftingTree.NodeId, AcquisitionSource.Craft },
            };
            var manualOriginal = pipeline.ResolveWithOverrides(initial.SolveContext, overrides);
            var manualReloaded = pipeline.ResolveWithOverrides(loaded.Result.SolveContext, overrides);

            Assert.Equal(CraftingDecision.Craft, manualReloaded.CraftingTree.Decision);
            Assert.Equal(manualOriginal.Plan.TotalCoinCost, manualReloaded.Plan.TotalCoinCost);
        }

        [Fact]
        public async Task Save_Load_CompetencyIndependentForceBuyNodeIds_PopulatedSetRoundTrips()
        {
            // Follow-up fix (recorded non-blocking, srcsel verification):
            // PlanSolveContext.CompetencyIndependentForceBuyNodeIds (see
            // OwnedMaterialsForceBuyPrePass.ForceBuyPrePassResult's own doc
            // comment) had no persistence round-trip coverage at all -
            // ForceBuyOnlyNodeIds' sibling test above never asserted on it.
            // Same fixture as that test: no CharacterDisciplines snapshot,
            // so the competency-resolved and competency-blind evaluations
            // agree and this narrower set comes out equal to
            // ForceBuyOnlyNodeIds - real, non-empty content either way.
            var pipeline = BuildForceBuyPipeline(out _);

            var initial = await pipeline.GenerateStructuredAsync(
                1, 1, OwnFourOfIngredient(), CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued,
                priceBasis: PriceBasis.InstantBuy);

            Assert.NotNull(initial.SolveContext.CompetencyIndependentForceBuyNodeIds);
            Assert.NotEmpty(initial.SolveContext.CompetencyIndependentForceBuyNodeIds);

            _store.Save(Wrap(initial, DateTime.Now));
            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded?.Result?.SolveContext);

            Assert.NotNull(loaded.Result.SolveContext.CompetencyIndependentForceBuyNodeIds);
            Assert.Equal(
                new HashSet<int>(initial.SolveContext.CompetencyIndependentForceBuyNodeIds),
                new HashSet<int>(loaded.Result.SolveContext.CompetencyIndependentForceBuyNodeIds));
        }

        [Fact]
        public async Task Save_Load_ForceBuyNodeIdSets_NullInJson_DeserializeToNullWithoutValidatorRejection()
        {
            // Follow-up fix (recorded non-blocking): OwnMaterialsMode.Free
            // never runs the pre-pass (see CraftingPlanPipeline's own
            // useForceBuyPrePass gate), so both ForceBuyOnlyNodeIds and
            // CompetencyIndependentForceBuyNodeIds stay null on the
            // generated result. Newtonsoft's default NullValueHandling is
            // Include (measured below, not the Ignore this test's original
            // "absent-in-JSON" framing assumed) - PlanStoreHelpers uses no
            // custom JsonSerializerSettings, so both fields are written as
            // an explicit JSON null, not omitted. Either way the round trip
            // must land back on null with PlanStructuralValidator NOT
            // rejecting the reload on that basis (it never references
            // either field - see Services/PlanStructuralValidator.cs).
            var pipeline = BuildPipeline(out _);

            var initial = await pipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Null(initial.SolveContext.ForceBuyOnlyNodeIds);
            Assert.Null(initial.SolveContext.CompetencyIndependentForceBuyNodeIds);

            string json = PlanStore.Serialize(Wrap(initial, DateTime.Now));
            Assert.Contains("\"ForceBuyOnlyNodeIds\":null", json);
            Assert.Contains("\"CompetencyIndependentForceBuyNodeIds\":null", json);

            _store.Save(Wrap(initial, DateTime.Now));
            var loaded = _store.LoadLatest()?.Plan;

            Assert.NotNull(loaded?.Result?.SolveContext);
            Assert.Null(loaded.Result.SolveContext.ForceBuyOnlyNodeIds);
            Assert.Null(loaded.Result.SolveContext.CompetencyIndependentForceBuyNodeIds);
        }

        [Fact]
        public async Task Save_Load_FullFeaturedFixture_RoundTripsPreviouslyUnexercisedSolveContextShapes()
        {
            using (var tmp = new TempDirectory())
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
                        new RawIngredient { Type = "Item", Id = 2, Count = 5 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 500,
                    Flags = new List<string> { "AutoLearned" },
                });

                var priceApi = new InMemoryPriceApiClient();
                priceApi.AddPrice(1, buyUnitPrice: 100, sellUnitPrice: 5000); // buying item 1 outright is far pricier - craft wins
                priceApi.AddPrice(2, buyUnitPrice: 100, sellUnitPrice: 1000); // item 2's own TP price - far pricier than the vendor offer below

                var itemApi = new InMemoryItemApiClient();
                itemApi.AddItem(1, "Target", "t.png");
                itemApi.AddItem(2, "Ingredient", "i.png");

                var loader = new VendorOfferLoader();
                var vendorStore = new VendorOfferStore(tmp.Path, loader);
                vendorStore.LoadBaseline(null);
                vendorStore.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-ingredient-offer",
                        OutputItemId = 2,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = 2, Count = 10 },
                        },
                        MerchantName = "Test Vendor",
                    },
                });

                var accountClient = new InMemoryAccountRecipeClient();
                accountClient.AddLearnedRecipe(10);

                var pipeline = new CraftingPlanPipeline(
                    new RecipeService(recipeApi),
                    new TradingPostService(priceApi),
                    new PlanSolver(),
                    new ItemMetadataService(itemApi),
                    vendorStore,
                    reducer: new InventoryReducer(),
                    accountRecipeClient: accountClient);

                var valuation = new CurrencyValuation(new Dictionary<int, long> { { 2, 1 } });
                var tiers = new HomesteadEfficiencyTiers(new Dictionary<int, int>
                {
                    { Gw2Constants.RefinedHomesteadMetalItemId, 2 },
                });

                var snapshot = new AccountSnapshot
                {
                    Items = new List<SnapshotItemEntry>
                    {
                        new SnapshotItemEntry { ItemId = 2, Count = 2, Source = AccountItemIndex.SourceMaterialStorage },
                    },
                    Wallet = new List<SnapshotWalletEntry>
                    {
                        new SnapshotWalletEntry { CurrencyId = 2, Value = 500 },
                    },
                    CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                    {
                        new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Weaponsmith", Rating = 500, Active = true },
                    },
                };

                var result = await pipeline.GenerateStructuredAsync(
                    1, 1, snapshot, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy,
                    currencyValuation: valuation,
                    homesteadTiers: tiers);

                // Every one of the shapes the investigation flagged as risky
                // (and previously untested) genuinely has content here.
                Assert.NotNull(result.SolveContext.LearnedRecipeIds);
                Assert.Contains(10, result.SolveContext.LearnedRecipeIds);

                Assert.NotNull(result.SolveContext.VendorOffers);
                Assert.True(result.SolveContext.VendorOffers.TryGetValue(2, out var item2Offers));
                Assert.NotEmpty(item2Offers);

                Assert.NotNull(result.SolveContext.CurrencyValuation);
                Assert.True(result.SolveContext.CurrencyValuation.TryGetCopperValue(2, out long copperPerUnit));
                Assert.Equal(1, copperPerUnit);

                Assert.NotNull(result.SolveContext.HomesteadTiers);
                Assert.Equal(2, result.SolveContext.HomesteadTiers.GetTier(Gw2Constants.RefinedHomesteadMetalItemId));

                Assert.NotNull(result.UsedMaterials);
                Assert.Contains(result.UsedMaterials, u => u.ItemId == 2 && u.QuantityUsed == 2);

                Assert.NotNull(result.CharacterDisciplines);
                Assert.Single(result.CharacterDisciplines);
                Assert.Equal("Anna", result.CharacterDisciplines[0].CharacterName);

                // Persist + reload.
                var generatedAt = new DateTime(2026, 8, 9, 12, 0, 0, DateTimeKind.Local);
                _store.Save(Wrap(result, generatedAt, useOwn: true));

                var loaded = _store.LoadLatest()?.Plan;
                Assert.NotNull(loaded?.Result?.SolveContext);

                // The reloaded result renders identically to the original.
                var vmBuilder = new PlanViewModelBuilder();
                Assert.Equal(ToJson(vmBuilder.Build(result)), ToJson(vmBuilder.Build(loaded.Result)));

                // Every flagged shape survived the round trip with its
                // content intact, not just structurally present.
                Assert.NotNull(loaded.Result.SolveContext.LearnedRecipeIds);
                Assert.Contains(10, loaded.Result.SolveContext.LearnedRecipeIds);

                Assert.True(loaded.Result.SolveContext.VendorOffers.TryGetValue(2, out var reloadedItem2Offers));
                Assert.Equal(item2Offers.Count, reloadedItem2Offers.Count);
                Assert.Equal(item2Offers[0].OfferId, reloadedItem2Offers[0].OfferId);

                Assert.True(loaded.Result.SolveContext.CurrencyValuation.TryGetCopperValue(2, out long reloadedCopperPerUnit));
                Assert.Equal(copperPerUnit, reloadedCopperPerUnit);

                Assert.Equal(2, loaded.Result.SolveContext.HomesteadTiers.GetTier(Gw2Constants.RefinedHomesteadMetalItemId));

                // And the whole graph still re-solves identically after the
                // round trip - the same correctness bar, now
                // proven against a fixture carrying every flagged shape at
                // once rather than a minimal two-item tree.
                var overrides = new Dictionary<int, AcquisitionSource>
                {
                    { result.CraftingTree.NodeId, AcquisitionSource.BuyFromTp },
                };
                var resolvedOriginal = pipeline.ResolveWithOverrides(result.SolveContext, overrides);
                var resolvedReloaded = pipeline.ResolveWithOverrides(loaded.Result.SolveContext, overrides);

                Assert.Equal(AcquisitionSource.BuyFromTp, resolvedReloaded.Plan.Steps[0].Source);
                Assert.Equal(resolvedOriginal.Plan.TotalCoinCost, resolvedReloaded.Plan.TotalCoinCost);
                Assert.Equal(ToJson(vmBuilder.Build(resolvedOriginal)), ToJson(vmBuilder.Build(resolvedReloaded)));
            }
        }

        [Fact]
        public async Task Save_Load_MultiItemBatch_RoundTripsAndResolveWithOverridesMatchesViaBatchBranch()
        {
            // CraftingPlanPipeline.ResolveWithOverrides branches on
            // context.Tree.Id == Gw2Constants.MultiItemWrapperItemId
            // (ApplySellSideEconomics vs. ApplyBatchSellSideEconomics) - a
            // completely different code path than every single-item test
            // above exercises. RequestedItems/MultiItemRoots must both
            // survive the round trip for a restored multi-item plan's
            // decision pills to keep resolving through the correct branch.
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(1, 10);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 3, Count = 1 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });
            recipeApi.AddSearchResult(2, 20);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 20,
                OutputItemId = 2,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 4, Count = 1 },
                },
                Disciplines = new List<string> { "Armorsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(1, buyUnitPrice: 50, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 60, sellUnitPrice: 1200);
            priceApi.AddPrice(3, buyUnitPrice: 10, sellUnitPrice: 100);
            priceApi.AddPrice(4, buyUnitPrice: 20, sellUnitPrice: 200);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1, "Target Item A", "targeta.png");
            itemApi.AddItem(2, "Target Item B", "targetb.png");
            itemApi.AddItem(3, "Ingredient A", "ingredienta.png");
            itemApi.AddItem(4, "Ingredient B", "ingredientb.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi));

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 2 },
                new PlanRequestItem { ItemId = 2, Quantity = 3 },
            };

            var result = await pipeline.GenerateStructuredAsync(items, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Equal(2, result.MultiItemRoots.Count);
            Assert.Equal(Gw2Constants.MultiItemWrapperItemId, result.SolveContext.Tree.Id);
            Assert.NotNull(result.RequestedItems);
            Assert.Equal(2, result.RequestedItems.Count);

            var plan = new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = new DateTime(2026, 8, 9, 13, 0, 0, DateTimeKind.Local),
                RequestItems = items,
                UseOwnMaterials = false,
                PriceBasis = PriceBasis.InstantBuy,
                Result = result,
                NodeOverrides = new Dictionary<int, AcquisitionSource>(),
                IgnoredItemIds = new List<int>(),
            };
            _store.Save(plan);

            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded?.Result?.SolveContext);
            Assert.Equal(2, loaded.Result.MultiItemRoots.Count);
            Assert.Equal(Gw2Constants.MultiItemWrapperItemId, loaded.Result.SolveContext.Tree.Id);
            Assert.NotNull(loaded.Result.RequestedItems);
            Assert.Equal(2, loaded.Result.RequestedItems.Count);
            Assert.Equal(2, loaded.RequestItems.Count);
            Assert.Equal(1, loaded.RequestItems[0].ItemId);
            Assert.Equal(2, loaded.RequestItems[0].Quantity);
            Assert.Equal(2, loaded.RequestItems[1].ItemId);
            Assert.Equal(3, loaded.RequestItems[1].Quantity);

            var vmBuilder = new PlanViewModelBuilder();
            Assert.Equal(ToJson(vmBuilder.Build(result)), ToJson(vmBuilder.Build(loaded.Result)));

            // A local override re-solve through the MULTI-ITEM branch of
            // ResolveWithOverrides must produce identical results on the
            // original and reloaded contexts.
            var overrides = new Dictionary<int, AcquisitionSource>
            {
                { result.MultiItemRoots[0].NodeId, AcquisitionSource.BuyFromTp },
            };
            var resolvedOriginal = pipeline.ResolveWithOverrides(result.SolveContext, overrides);
            var resolvedReloaded = pipeline.ResolveWithOverrides(loaded.Result.SolveContext, overrides);

            Assert.Equal(resolvedOriginal.Plan.TotalCoinCost, resolvedReloaded.Plan.TotalCoinCost);
            Assert.Equal(ToJson(vmBuilder.Build(resolvedOriginal)), ToJson(vmBuilder.Build(resolvedReloaded)));
        }

        // --- Regression: PlanStructuralValidator - a
        // structurally-valid-but-degraded plan.json (e.g. a null entry deep
        // inside CraftingTreeNode.Children, invisible to
        // PlanViewModelBuilder's reference-copying vm build) used to sail
        // through PlanStoreHelpers' tolerance gate and only NRE later, from
        // an UNGUARDED Blish click handler (Expand All / the per-node
        // expand toggle in TreeSectionController.RenderTreeNode, and
        // TreeSectionController's Craft All/Buy All buttons via
        // CraftingPlanPipeline.BuildPresetOverrides) with no try/catch
        // anywhere nearby - see PlanStructuralValidator's own doc comment
        // for the full inventory. Every fixture below starts from a REAL
        // pipeline-produced PersistedPlan (matching this file's own
        // established "real serialized fixtures, not hand-built objects"
        // convention), serialized via the actual production
        // PlanStoreHelpers.SerializePersistedPlan, then surgically
        // corrupted at one exact JSON location via Newtonsoft's JObject -
        // proving the validator rejects a file a naive parse/schema check
        // would have accepted, and does so with the required "one Warn log
        // line, no result handed back" contract (never a partial accept).
        // The request layer survives every one of these - it is versioned
        // separately and is never bound against the damaged graph (see
        // docs/ARCHITECTURE.md section 12) - so what each asserts is that
        // no RESULT reached the caller, not that nothing did. ---
        private static string SerializeAndCorrupt(PersistedPlan plan, Action<JObject> corrupt)
        {
            string json = PlanStoreHelpers.SerializePersistedPlan(plan);
            var jObj = JObject.Parse(json);
            corrupt(jObj);
            return jObj.ToString(Formatting.None);
        }

        // getLastMessage exposes the wrapped EXCEPTION's own Message (e.g.
        // PlanStructuralValidator's "...failed structural validation
        // (...)" text) rather than PlanStore.LoadLatest's own generic
        // "Failed to load plan from {path}" wrapper string - the latter is
        // identical across every failure kind, so only the exception
        // message can distinguish "rejected by PlanStructuralValidator"
        // from every other pre-existing rejection reason (a JSON parse
        // failure, the Result/Plan/SchemaVersion gate).
        private PlanStore NewWarnCountingStore(out Func<int> getWarnCount, out Func<string> getLastMessage)
        {
            int warnCount = 0;
            string lastExceptionMessage = null;
            var store = new PlanStore(_tempDir, (message, ex) =>
            {
                warnCount++;
                lastExceptionMessage = ex?.Message;
            });
            getWarnCount = () => warnCount;
            getLastMessage = () => lastExceptionMessage;
            return store;
        }

        [Fact]
        public async Task LoadLatest_NullChildEntryInCraftingTreeAtDepth2_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            var pipeline = BuildDeepPipeline(out _);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // Confirm the real fixture actually has the shape this test
            // needs before corrupting it: root (depth 0, Craft) -> item 2
            // (depth 1, Craft) -> item 3 (depth 2, leaf).
            Assert.Equal(CraftingDecision.Craft, result.CraftingTree.Decision);
            Assert.Single(result.CraftingTree.Children);
            var depth1Node = result.CraftingTree.Children[0];
            Assert.Equal(CraftingDecision.Craft, depth1Node.Decision);
            Assert.Single(depth1Node.Children);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                var depth2Children = (JArray)jObj["Result"]["CraftingTree"]["Children"][0]["Children"];
                depth2Children[0] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            // Pins this down to PlanStructuralValidator specifically - not
            // the pre-existing Result/Plan/SchemaVersion gate or a plain
            // JSON parse failure, both of which use different message text.
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("CraftingTree.Children[0].Children[0]", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_ExplicitNullChildrenListOnTreeNode_LoadsSuccessfully()
        {
            // CraftingTreeNode.Children's own setter coerces a null value to
            // Array.Empty<CraftingTreeNode>() (see that property's doc
            // comment) - Newtonsoft always calls a writable property's
            // public setter, so an explicit JSON "Children": null on some
            // node can never actually reach PlanStructuralValidator as a
            // null LIST; it is already neutralized one layer down, at the
            // model itself. This proves the validator does not (and
            // structurally cannot) false-reject that shape - a real
            // "Children": null file still loads and renders exactly like
            // the childless leaf it already was.
            var pipeline = BuildDeepPipeline(out _);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                // Item 3 (depth 2) is a leaf - its own Children is already
                // an empty array on disk; overwrite with an explicit null.
                jObj["Result"]["CraftingTree"]["Children"][0]["Children"][0]["Children"] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var loaded = _store.LoadLatest()?.Plan;

            Assert.NotNull(loaded);
            var leaf = loaded.Result.CraftingTree.Children[0].Children[0];
            Assert.NotNull(leaf.Children);
            Assert.Empty(leaf.Children);
        }

        [Fact]
        public async Task LoadLatest_NullRecipesListOnSolveContextTreeNode_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            // Unlike CraftingTreeNode.Children, RecipeNode.Recipes has no
            // null-coalescing setter (a plain auto-property with a "= new
            // List<RecipeOption>()" initializer Newtonsoft overwrites
            // verbatim) - so a null LIST is genuinely reachable here, the
            // closest real equivalent to a "null Children list" corruption.
            // PlanSolver.Evaluate/CraftingTreeBuilder.BuildNode/
            // CraftingPlanPipeline.CollectPresetOverrides all walk
            // node.Recipes unconditionally on EVERY override re-solve
            // (Craft All/Buy All/a plain pill click), not just when there
            // happens to be a Craft step.
            var pipeline = BuildDeepPipeline(out _);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(result.SolveContext);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                var item2Node = jObj["Result"]["SolveContext"]["Tree"]["Recipes"][0]["Ingredients"][0];
                item2Node["Recipes"] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("SolveContext.Tree.Recipes[0].Ingredients[0].Recipes", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullIngredientEntryInSolveContextTree_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            // A null RecipeNode ENTRY inside RecipeOption.Ingredients (as
            // opposed to the whole list being null, covered above) -
            // reachable the same way: every override re-solve walks the
            // full Ingredients list of every recipe on the path from Tree's
            // root, unconditionally.
            var pipeline = BuildDeepPipeline(out _);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(result.SolveContext);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                var item2Recipe = jObj["Result"]["SolveContext"]["Tree"]["Recipes"][0]["Ingredients"][0]["Recipes"][0];
                ((JArray)item2Recipe["Ingredients"])[0] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("SolveContext.Tree.Recipes[0].Ingredients[0].Recipes[0].Ingredients[0]", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullSolveContextPrices_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            // A solve-context COLLECTION nulled (as opposed to a tree
            // shape) - PlanSolver.GetBuyCost/CraftingPlanPipeline.
            // CollectPresetOverrides both call prices.TryGetValue(...) with
            // no null check on the dictionary itself, so a null Prices
            // would NRE on the very first node of the very first override
            // re-solve or Craft All/Buy All click after a restore.
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(result.SolveContext);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                jObj["Result"]["SolveContext"]["Prices"] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("SolveContext.Prices is null", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullEntryInSolveContextUsedMaterials_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            // SolveContext.UsedMaterials is a
            // SEPARATELY serialized copy of the same list as
            // CraftingPlanResult.UsedMaterials (Newtonsoft writes no $ref) -
            // this corrupts ONLY the SolveContext copy, leaving
            // Result.UsedMaterials itself clean, to prove the validator no
            // longer has the asymmetry a plain check on Result.UsedMaterials
            // alone would miss. Reachable from ANY override re-solve after a
            // restore (ResolveWithOverrides -> PlanResultBuilder.Build's
            // "foreach (var used in usedMaterials) { ... used.ItemId ... }"
            // and SellSideEconomics.ComputeMaterialOpportunityCost's
            // "used.ItemId"/"used.QuantityUsed", neither with a per-entry
            // null check), not just Craft All/Buy All.
            var pipeline = BuildOwnMaterialsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 10000, sellUnitPrice: 20000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(
                1, 1, OwnFourOfIngredient(), CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy, ownMaterialsMode: OwnMaterialsMode.Valued);
            Assert.NotNull(result.SolveContext);
            Assert.NotEmpty(result.SolveContext.UsedMaterials);
            Assert.NotEmpty(result.UsedMaterials);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                ((JArray)jObj["Result"]["SolveContext"]["UsedMaterials"])[0] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("SolveContext.UsedMaterials[0] is null", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullRecipesListOnSolveContextUnreducedTree_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            // UnreducedTree is walked by
            // ResolveWithOverrides' guideSolve (_solver.Solve) and
            // re-reduction (_reducer.Reduce) on EVERY override re-solve
            // once the force-buy pre-pass ran at generation time (Valued
            // mode + a non-null snapshot - see PlanSolveContext.
            // UnreducedTree's own doc comment), the exact same
            // unconditional node.Recipes walk as Tree above (which the two
            // tests above this one already cover). Before this fix, a
            // plan.json with "UnreducedTree":{"Recipes":null} sailed
            // through PlanStructuralValidator untouched and NREd on the
            // very first override pill click of a restored plan.
            var pipeline = BuildOwnMaterialsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 10000, sellUnitPrice: 20000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(
                1, 1, OwnFourOfIngredient(), CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy, ownMaterialsMode: OwnMaterialsMode.Valued);
            Assert.NotNull(result.SolveContext);
            Assert.NotNull(result.SolveContext.UnreducedTree);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                jObj["Result"]["SolveContext"]["UnreducedTree"]["Recipes"] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("SolveContext.UnreducedTree.Recipes is null", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullEntryInSolveContextAccountItems_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            // AccountItemIndex's constructor (Services/
            // AccountItemIndex.cs) null-checks the LIST but not each entry
            // ("entry.Count"/"entry.Source" with no per-entry guard) - a
            // null ENTRY NREs identically to the UnreducedTree gap above,
            // reachable the same way (any override re-solve once the
            // force-buy pre-pass ran).
            var pipeline = BuildOwnMaterialsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 10000, sellUnitPrice: 20000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(
                1, 1, OwnFourOfIngredient(), CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy, ownMaterialsMode: OwnMaterialsMode.Valued);
            Assert.NotNull(result.SolveContext);
            Assert.NotEmpty(result.SolveContext.AccountItems);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                ((JArray)jObj["Result"]["SolveContext"]["AccountItems"])[0] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("SolveContext.AccountItems[0] is null", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_SolveContextUnreducedTreeSetButAccountItemsNull_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            // VOM finding #1 bonus fix: UnreducedTree and AccountItems are
            // always populated TOGETHER at generation time (both gated on
            // useForceBuyPrePass). Without this check, a restored file with
            // UnreducedTree set but AccountItems null degraded SILENTLY
            // instead of crashing or being rejected: AccountItemIndex(null)
            // builds an empty index, so a subsequent override re-solve
            // re-prices every owned material as if none were owned.
            var pipeline = BuildOwnMaterialsPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 10000, sellUnitPrice: 20000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(
                1, 1, OwnFourOfIngredient(), CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy, ownMaterialsMode: OwnMaterialsMode.Valued);
            Assert.NotNull(result.SolveContext);
            Assert.NotNull(result.SolveContext.UnreducedTree);
            Assert.NotEmpty(result.SolveContext.AccountItems);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                jObj["Result"]["SolveContext"]["AccountItems"] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains(
                "SolveContext.UnreducedTree is set but SolveContext.AccountItems is null",
                lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullEntryInPlanSteps_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            // Plan.Steps is read unconditionally by
            // PlanViewModelBuilder.Build (result.Plan.Steps.Where(...)) - a
            // null ENTRY inside an otherwise non-null list NREs on the very
            // first vm build, whether that is the restore itself or a later
            // override re-solve.
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotEmpty(result.Plan.Steps);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                ((JArray)jObj["Result"]["Plan"]["Steps"])[0] = JValue.CreateNull();
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("Plan.Steps[0] is null", lastMessage());
        }

        // --- Quality-audit fix (B2): CompetencyOpportunities/
        // ExcessCraftOutputs/RecipeSheetSavingsOpportunities/
        // SeasonalVendorTips all had the exact same "list null-checked,
        // entry not" gap the LoadLatest_NullEntryInPlanSteps test above
        // covers for Plan.Steps - PlanViewModelBuilder.BuildNotesSection
        // dereferences one field on each entry (opportunity.ItemId/
        // excess.ItemId/opp.ItemId/tip.CostLines) with no per-entry null
        // check, once the surrounding list-level Count > 0 guard passes.
        // Each calculator (CompetencyOpportunityCalculator/
        // ExcessCraftOutputCalculator/RecipeSheetSavingsCalculator/
        // SeasonalVendorTipCalculator) always assigns its field to a
        // (possibly empty) list for a real, non-null CraftingPlanResult -
        // never leaves it null - so every real plan.json this module ever
        // writes carries an actual (if often empty) JSON array here,
        // exactly like Plan.Steps above; the corruption below overwrites
        // that empty array wholesale rather than one existing element,
        // since a fresh single-item fixture never triggers any of these
        // four calculators into producing a real entry. ---
        [Fact]
        public async Task LoadLatest_NullEntryInCompetencyOpportunities_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(result.CompetencyOpportunities);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                jObj["Result"]["CompetencyOpportunities"] = new JArray(JValue.CreateNull());
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("CompetencyOpportunities[0] is null", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullEntryInExcessCraftOutputs_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(result.ExcessCraftOutputs);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                jObj["Result"]["ExcessCraftOutputs"] = new JArray(JValue.CreateNull());
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("ExcessCraftOutputs[0] is null", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullEntryInRecipeSheetSavingsOpportunities_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(result.RecipeSheetSavingsOpportunities);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                jObj["Result"]["RecipeSheetSavingsOpportunities"] = new JArray(JValue.CreateNull());
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("RecipeSheetSavingsOpportunities[0] is null", lastMessage());
        }

        [Fact]
        public async Task LoadLatest_NullEntryInSeasonalVendorTips_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            Assert.NotNull(result.SeasonalVendorTips);

            string json = SerializeAndCorrupt(Wrap(result, DateTime.Now), jObj =>
            {
                jObj["Result"]["SeasonalVendorTips"] = new JArray(JValue.CreateNull());
            });
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.Contains("structural validation", lastMessage());
            Assert.Contains("SeasonalVendorTips[0] is null", lastMessage());
        }

        // --- Compression: the
        // on-disk container is gzip, sniffed by its first two magic
        // bytes (0x1F 0x8B) so plan.json files written by the pre-gzip
        // PR #107 code (plain compact JSON) still load. Payload schema/
        // PlanStructuralValidator gate are unchanged - only these four
        // tests target the new container-encoding logic itself; every
        // fixture/test above this point still exercises the same real
        // PlanStore/production paths, now transparently gzip-backed. ---

        // Real gzip bytes for a fixture STRING, built the same way
        // PlanStore.Save's own (private) Compress helper does - reused by
        // the backward-compat-break tests below, which need to hand-craft
        // an on-disk file PlanStore.Save itself would never produce (a
        // valid gzip member wrapping something other than a real
        // PersistedPlan).
        private static byte[] GzipBytes(string text)
        {
            byte[] textBytes = Encoding.UTF8.GetBytes(text);
            using (var output = new MemoryStream())
            {
                using (var gzip = new GZipStream(output, CompressionMode.Compress, leaveOpen: true))
                {
                    gzip.Write(textBytes, 0, textBytes.Length);
                }

                return output.ToArray();
            }
        }

        [Fact]
        public async Task Save_Load_RoundTrip_WritesGzipOnDiskMateriallySmallerThanRawJson()
        {
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            var plan = Wrap(result, new DateTime(2026, 8, 15, 10, 30, 0, DateTimeKind.Local));

            _store.Save(plan);

            string filePath = Path.Combine(_tempDir, "plan.json");
            byte[] onDiskBytes = File.ReadAllBytes(filePath);

            // Gzip magic number (RFC 1952) - proves Save wrote a compressed
            // container, not plain JSON.
            Assert.True(onDiskBytes.Length >= 2);
            Assert.Equal(0x1F, onDiskBytes[0]);
            Assert.Equal(0x8B, onDiskBytes[1]);

            int rawJsonByteLength = Encoding.UTF8.GetByteCount(PlanStoreHelpers.SerializePersistedPlan(plan));
            Assert.True(onDiskBytes.Length < rawJsonByteLength,
                $"Expected the gzip container ({onDiskBytes.Length} bytes) to be materially " +
                $"smaller than the raw JSON it replaces ({rawJsonByteLength} bytes).");

            // And the plan itself still round-trips through the compressed
            // container exactly like it did through plain JSON pre-fix.
            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            var vmBuilder = new PlanViewModelBuilder();
            Assert.Equal(ToJson(vmBuilder.Build(result)), ToJson(vmBuilder.Build(loaded.Result)));
        }

        [Fact]
        public async Task LoadLatest_PlainUncompressedJsonFile_StillLoads_BackwardCompat()
        {
            // A real plan.json from before this fix (PR #107's plain
            // File.WriteAllText(json) path, never gzipped) - written here
            // via the same production serializer PlanStore.Save itself
            // uses internally, just without the compression step, to
            // reproduce that exact on-disk shape.
            var pipeline = BuildPipeline(out var priceApi);
            priceApi.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
            var plan = Wrap(result, new DateTime(2026, 8, 15, 11, 0, 0, DateTimeKind.Local));

            string json = PlanStoreHelpers.SerializePersistedPlan(plan);
            File.WriteAllText(Path.Combine(_tempDir, "plan.json"), json);

            var loaded = _store.LoadLatest()?.Plan;

            Assert.NotNull(loaded);
            var vmBuilder = new PlanViewModelBuilder();
            Assert.Equal(ToJson(vmBuilder.Build(result)), ToJson(vmBuilder.Build(loaded.Result)));
        }

        [Fact]
        public void LoadLatest_TruncatedGzipData_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            var plan = new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = DateTime.Now,
                RequestItems = new List<PlanRequestItem> { new PlanRequestItem { ItemId = 1, Quantity = 1 } },
                UseOwnMaterials = false,
                PriceBasis = PriceBasis.InstantBuy,
                Result = new CraftingPlanResult { Plan = new CraftingPlan { TargetItemId = 1, TargetQuantity = 1 } },
                NodeOverrides = new Dictionary<int, AcquisitionSource>(),
                IgnoredItemIds = new List<int>(),
            };
            _store.Save(plan);

            string filePath = Path.Combine(_tempDir, "plan.json");
            byte[] validGzipBytes = File.ReadAllBytes(filePath);
            Assert.True(validGzipBytes.Length > 10, "Fixture gzip payload too small to truncate meaningfully.");

            byte[] truncated = new byte[validGzipBytes.Length / 2];
            Array.Copy(validGzipBytes, truncated, truncated.Length);
            File.WriteAllBytes(filePath, truncated);

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.NotNull(lastMessage());
        }

        [Fact]
        public void LoadLatest_GzipWrappingInvalidJson_DiscardsTheResultAndLogsWarnExactlyOnce()
        {
            string filePath = Path.Combine(_tempDir, "plan.json");
            File.WriteAllBytes(filePath, GzipBytes("{ \"Result\": { \"Plan\": { \"Target"));

            var store = NewWarnCountingStore(out var warnCount, out var lastMessage);
            var loaded = store.LoadLatest()?.Plan;

            Assert.Null(loaded?.Result);
            Assert.Equal(1, warnCount());
            Assert.NotNull(lastMessage());
        }

        // ---- Vendor cost-component leaves: persistence round-trip
        // + PlanStructuralValidator acceptance ----

        /// <summary>
        /// Real pipeline, real VendorOfferStore-backed offer mixing a
        /// TP-valued Item cost line with a non-coin currency cost line (2
        /// kinds) - so CraftingTreeBuilder synthesizes component leaves
        /// (see CraftingPlanPipelineVendorCostComponentTests.
        /// GenerateMixedVendorPlanAsync for the sibling copy of this
        /// fixture shape). Mirrors this file's own
        /// "build a REAL CraftingPlanResult, never hand-construct one"
        /// discipline (see this class's own header comment).
        /// </summary>
        private static async Task<CraftingPlanResult> GenerateMixedVendorResultAsync()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            // No recipe for item 1 - vendor-only.
            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(42, buyUnitPrice: 10, sellUnitPrice: 20);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1, "Amalgamated Rift Essence", "essence.png");
            itemApi.AddItem(42, "Glob of Ectoplasm", "ecto.png");

            using (var tmp = new TempDirectory())
            {
                var loader = new VendorOfferLoader();
                var store = new VendorOfferStore(tmp.Path, loader);
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-mixed-w4b-persist",
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

                var pipeline = new CraftingPlanPipeline(
                    new RecipeService(recipeApi),
                    new TradingPostService(priceApi),
                    new PlanSolver(),
                    new ItemMetadataService(itemApi),
                    store);

                return await pipeline.GenerateStructuredAsync(1, 2, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
            }
        }

        [Fact]
        public async Task Save_Load_RoundTripsComponentLeaves_AndPassesStructuralValidation()
        {
            var result = await GenerateMixedVendorResultAsync();
            Assert.Equal(2, result.CraftingTree.Children.Count); // sanity: leaves were actually built

            _store.Save(Wrap(result, new DateTime(2026, 8, 15, 9, 0, 0, DateTimeKind.Local), quantity: 2));

            // A successful non-null LoadLatest() already proves
            // PlanStructuralValidator.IsStructurallyValid accepted the
            // component leaves (a rejection returns null - see
            // PlanStoreHelpers.DeserializePersistedPlan/
            // PlanStructuralValidator's own doc comment): IsValidCraftingTreeNode's
            // recursive Children walk covers these leaves the same as any
            // other node, with no leaf-specific change needed there.
            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);
            Assert.NotNull(loaded.Result.CraftingTree);
            Assert.Equal(2, loaded.Result.CraftingTree.Children.Count);

            var itemLeaf = loaded.Result.CraftingTree.Children.Single(c => c.ItemId == 42);
            Assert.True(itemLeaf.IsCostComponent);
            Assert.Equal("Glob of Ectoplasm", itemLeaf.Name);
            Assert.Equal(10, itemLeaf.Quantity);
            Assert.Equal(200, itemLeaf.SubtreeCost);
            Assert.Equal(0, itemLeaf.ComponentOwnedQuantity);
            Assert.Equal(result.CraftingTree.Children.Single(c => c.ItemId == 42).NodeId, itemLeaf.NodeId);

            var currencyLeaf = loaded.Result.CraftingTree.Children.Single(c => c.ItemId == 23);
            Assert.True(currencyLeaf.IsCostComponent);
            Assert.Equal(6, currencyLeaf.Quantity);
            Assert.Null(currencyLeaf.SubtreeCost);
        }

        [Fact]
        public async Task Save_Load_RoundTripsTheDeepestRealisticChain()
        {
            // Regression: a persisted +24 Agony Infusion plan (23 recipe
            // levels, the deepest chain in the game) exceeded Newtonsoft's
            // default read MaxDepth of 64 and silently failed to load.
            // 30 levels here nests ~90 JSON levels, past the old default.
            var recipeApi = new InMemoryRecipeApiClient();
            const int depth = 30;
            for (int level = 1; level <= depth; level++)
            {
                int outputItem = level;
                recipeApi.AddSearchResult(outputItem, 1000 + level);
                recipeApi.AddRecipe(new RawRecipe
                {
                    Id = 1000 + level,
                    OutputItemId = outputItem,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient
                        {
                            Type = "Item",
                            Id = level == 1 ? depth + 1 : level - 1,
                            Count = 1,
                        },
                    },
                    Disciplines = new List<string> { "Artificer" },
                    MinRating = 400,
                    Flags = new List<string> { "AutoLearned" },
                });
            }

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(depth + 1, buyUnitPrice: 10, sellUnitPrice: 12);
            var pipeline = BuildPipelineWith(recipeApi, priceApi);

            var result = await pipeline.GenerateStructuredAsync(
                depth, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            _store.Save(Wrap(result, new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Local)));
            var loaded = _store.LoadLatest()?.Plan;

            Assert.NotNull(loaded);
            int walked = 0;
            var node = loaded.Result.CraftingTree;
            while (node != null)
            {
                walked++;
                node = node.Children?.FirstOrDefault(c => !c.IsCostComponent);
            }

            Assert.True(walked >= depth, $"walked only {walked} levels of {depth}");
        }

        // --- The concurrency PlanStore._saveLock exists for. Two
        // genuinely independent writers (Module.PersistAfterGenerateAsync's
        // post-Generate persist and PersistResolvedPlanInBackground's
        // pill-click persist - a decision pill on an OLD plan stays
        // clickable while a NEW Generate is in flight) plus LoadLatest,
        // which deliberately takes NO lock and relies on the atomic
        // .tmp+Replace write instead.
        //
        // One test, two phases, because the claims need opposite setups:
        // the never-torn one needs a reader racing the writers, and that
        // same reader makes IOException a legitimate outcome, which is
        // exactly what an unserialized writer collision looks like. The
        // serialization claim therefore gets its own reader-free phase
        // where zero failures is the only permitted result. ---
        // The 60s is a hang catcher, not a latency claim - every verdict
        // below is about WHAT the racing threads produced, never how fast.
        // Framework-held for the reason spelled out on the same attribute in
        // Gw2BuildApiClientTests.TryGetBuildId_HungResponse_IsAbandonedAndRetried.
        [Fact(Timeout = 60000)]
        public async Task SaveAndLoad_ConcurrentWriters_SerializeAndNeverYieldATornPlan()
        {
            var shallowPipeline = BuildPipeline(out var shallowPrices);
            shallowPrices.AddPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            shallowPrices.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            var shallowResult = await shallowPipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            var deepPipeline = BuildDeepPipeline(out _);
            var deepResult = await deepPipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            // Header marker (RequestItems quantity) and payload (tree
            // depth) are written by the same Save and must always be read
            // back together - that pairing is what makes a torn read
            // detectable at all, rather than merely "parsed, so probably
            // fine".
            var shallowPlan = Wrap(shallowResult, new DateTime(2026, 8, 25, 9, 0, 0, DateTimeKind.Local), quantity: 1);
            var deepPlan = Wrap(deepResult, new DateTime(2026, 8, 25, 10, 0, 0, DateTimeKind.Local), quantity: 7);
            int shallowDepth = TreeDepth(shallowPlan.Result.CraftingTree);
            int deepDepth = TreeDepth(deepPlan.Result.CraftingTree);
            Assert.NotEqual(shallowDepth, deepDepth);

            // Seed the file so the race runs entirely against the
            // File.Replace branch (the one a reader can actually collide
            // with), not the one-shot File.Move branch.
            var storeFailures = new ConcurrentQueue<Exception>();
            var store = new PlanStore(_tempDir, (message, ex) => storeFailures.Enqueue(ex));
            store.Save(shallowPlan);

            const int writes = 60;
            var escaped = new ConcurrentQueue<Exception>();
            var samples = new ConcurrentQueue<PersistedPlan>();
            int nullLoads = 0;
            var start = new ManualResetEventSlim(false);

            Action<PersistedPlan> writer = plan => Run(start, escaped, () =>
            {
                for (int i = 0; i < writes; i++)
                {
                    store.Save(plan);
                }
            });

            var tasks = new[]
            {
                Task.Run(() => writer(shallowPlan)),
                Task.Run(() => writer(deepPlan)),
                Task.Run(() => Run(start, escaped, () =>
                {
                    for (int i = 0; i < writes * 2; i++)
                    {
                        // Paced deliberately. An unpaced reader keeps a
                        // handle on plan.json almost continuously and
                        // starves the race of anything to observe: measured
                        // 111 of 120 loads returning null and 60 of 120
                        // saves losing their File.Replace to the reader's
                        // handle. With the pause it is 111 real samples,
                        // 9 nulls and 8 lost writes - still genuinely
                        // overlapping, but actually sampling the file.
                        Thread.Sleep(1);
                        var loaded = store.LoadLatest()?.Plan;
                        if (loaded == null)
                        {
                            Interlocked.Increment(ref nullLoads);
                        }
                        else
                        {
                            samples.Enqueue(loaded);
                        }
                    }
                })),
            };

            start.Set();
            await Task.WhenAll(tasks);

            // Nothing may escape the public surface: Save and LoadLatest
            // both degrade IO failure to their callbacks, and a lost write
            // under contention is tolerated - a thrown exception is not.
            Assert.Empty(escaped);

            // What contention IS allowed to cost: a lost write or a skipped
            // read, both reported as OS access failures. A torn or
            // half-written file would instead surface here as a DATA
            // verdict - gzip's InvalidDataException, a JsonReaderException,
            // or PlanStoreHelpers' own corrupt-file throw - so this is the
            // assertion that actually pins "never partially written".
            foreach (var failure in storeFailures)
            {
                Assert.True(
                    failure is IOException || failure is UnauthorizedAccessException,
                    $"contention must fail as IO, not data: {failure.GetType().Name} - {failure.Message}");
            }

            // Every load landed on exactly one of the two permitted
            // outcomes - a whole plan, or null - and the reader ran to
            // completion rather than dying part-way.
            Assert.Equal(writes * 2, samples.Count + nullLoads);
            Assert.True(samples.Count > 0, $"every one of the {writes * 2} loads returned null");
            foreach (var sample in samples)
            {
                int quantity = sample.RequestItems.Single().Quantity;
                Assert.Contains(quantity, new[] { 1, 7 });
                Assert.Equal(
                    quantity == 1 ? shallowDepth : deepDepth,
                    TreeDepth(sample.Result.CraftingTree));
            }

            // Everything above is insensitive to _saveLock. Measured: with
            // `lock (_saveLock)` deleted the phase above passes 4 runs out
            // of 4, because a reader in the race makes IOException a
            // permitted outcome and the writers' own collisions on the one
            // shared .tmp path arrive as exactly that - roughly 85 of the
            // 120 saves failing, every failure classified as tolerated
            // contention. The torn-file assertions are real; the
            // serialization claim needs its own phase.
            //
            // So: same two writers, no reader. The reader was the only
            // legitimate source of failure, and two writers holding the
            // lock cannot collide with anything, so a single failure here
            // means Save is not serialized. A fresh store keeps its own
            // failure queue, so the tolerated failures above cannot mask
            // one; it shares the lock the writers need because they share
            // the instance.
            var serializedFailures = new ConcurrentQueue<Exception>();
            var serializedStore = new PlanStore(_tempDir, (message, ex) => serializedFailures.Enqueue(ex));
            var serializedStart = new ManualResetEventSlim(false);

            Action<PersistedPlan> serializedWriter = plan => Run(serializedStart, escaped, () =>
            {
                for (int i = 0; i < writes; i++)
                {
                    serializedStore.Save(plan);
                }
            });

            var writersOnly = Task.WhenAll(
                Task.Run(() => serializedWriter(shallowPlan)),
                Task.Run(() => serializedWriter(deepPlan)));
            serializedStart.Set();
            await writersOnly;

            Assert.Empty(escaped);
            Assert.True(
                serializedFailures.IsEmpty,
                $"Save must be serialized: {serializedFailures.Count} of {writes * 2} uncontended-by-a-reader "
                + $"writes failed, first {serializedFailures.FirstOrDefault()?.GetType().Name} - "
                + serializedFailures.FirstOrDefault()?.Message);

            // The file survives both races intact, and the uncontended write
            // that follows them consumes its own .tmp.
            store.Save(deepPlan);
            var final = store.LoadLatest()?.Plan;
            Assert.NotNull(final);
            Assert.Equal(7, final.RequestItems.Single().Quantity);
            Assert.Equal(deepDepth, TreeDepth(final.Result.CraftingTree));
            Assert.False(File.Exists(Path.Combine(_tempDir, "plan.json.tmp")));
        }

        private static void Run(ManualResetEventSlim start, ConcurrentQueue<Exception> escaped, Action body)
        {
            start.Wait();
            try
            {
                body();
            }
            catch (Exception ex)
            {
                escaped.Enqueue(ex);
            }
        }

        // Longest non-cost-component chain, root inclusive - cost-component
        // children are currency leaves, not crafting steps.
        private static int TreeDepth(CraftingTreeNode node)
        {
            if (node == null)
            {
                return 0;
            }

            int deepestChild = 0;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    if (child.IsCostComponent)
                    {
                        continue;
                    }

                    int depth = TreeDepth(child);
                    if (depth > deepestChild)
                    {
                        deepestChild = depth;
                    }
                }
            }

            return deepestChild + 1;
        }
    }
}
