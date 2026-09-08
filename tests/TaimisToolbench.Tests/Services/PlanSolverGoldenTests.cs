using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Whole-result goldens for the solver's economic decisions, captured by
    /// running the real pipeline over a corpus that spans the axes
    /// PlanSolver.Evaluate branches on: vendor-priced leaves, own-materials
    /// off/free/valued, both PriceBasis values, the force-buy pre-pass,
    /// currency valuation, multi-item roots, and a deep nested tree.
    ///
    /// Each golden holds the ENTIRE CraftingPlanResult (see PlanResultDump): the
    /// acquisition source for every node, unit and total costs, craft step order,
    /// required recipes and disciplines, the shopping list, and every advisory
    /// list. Captured against the solver BEFORE Evaluate was decomposed, so a
    /// byte-identical sweep afterwards proves the extraction moved no arithmetic.
    ///
    /// A difference here is never a reason to re-baseline: investigate it, since
    /// the golden is the record of what the solver decided. To regenerate
    /// deliberately (a real, reviewed behaviour change), set
    /// TTB_REGEN_PLAN_GOLDENS=1 and run this class - it rewrites the files in
    /// tests/TaimisToolbench.Tests/Goldens/plan-solver/ and fails, so the
    /// rewrite can never pass unnoticed in CI.
    /// </summary>
    public class PlanSolverGoldenTests
    {
        /// <summary>
        /// A leaf with no recipe, priced on both the TP and at a coin
        /// vendor, so the vendor-vs-TP comparison is the decision under
        /// test at both price bases.
        /// </summary>
        [Theory]
        [InlineData(nameof(PriceBasis.BuyOrder))]
        [InlineData(nameof(PriceBasis.InstantBuy))]
        public async Task VendorPricedLeaf(string priceBasisName)
        {
            var basis = EnumArg.Parse<PriceBasis>(priceBasisName);
            using (var tmp = new TempDirectory())
            {
                var store = NewVendorStore(tmp.Path);
                store.AddOffersToOverlay(new[]
                {
                    VendorOfferBuilders.CoinVendorOffer(outputItemId: 2, coinCost: 24),
                });

                var pipeline = PipelineBuilder.SingleRecipeTree(3)
                    .WithPrice(1, buyUnitPrice: 400, sellUnitPrice: 900)
                    .WithPrice(2, buyUnitPrice: 20, sellUnitPrice: 60)
                    .WithVendorOfferStore(store)
                    .Build();

                var result = await pipeline.GenerateStructuredAsync(
                    1, 2, null, CancellationToken.None, priceBasis: basis);

                Verify("vendor-priced-leaf-" + basis, result);
            }
        }

        /// <summary>
        /// The same tree and the same holdings under all three owned-
        /// materials settings, so the golden records exactly what changing
        /// the toggle does to costs and to the chosen sources.
        /// </summary>
        [Theory]
        [InlineData(false, nameof(OwnMaterialsMode.Free))]
        [InlineData(true, nameof(OwnMaterialsMode.Free))]
        [InlineData(true, nameof(OwnMaterialsMode.Valued))]
        public async Task OwnMaterials(bool useSnapshot, string modeName)
        {
            var mode = EnumArg.Parse<OwnMaterialsMode>(modeName);
            var builder = PipelineBuilder.SingleRecipeTree(5).WithInventoryReducer();
            builder.PriceApi.AddPrice(1, buyUnitPrice: 1000, sellUnitPrice: 5000);
            builder.PriceApi.AddPrice(2, buyUnitPrice: 100, sellUnitPrice: 300);

            var result = await builder.Build().GenerateStructuredAsync(
                1, 1, useSnapshot ? PipelineBuilder.OwnIngredient(3) : null, CancellationToken.None,
                ownMaterialsMode: mode, priceBasis: PriceBasis.InstantBuy);

            Verify("own-materials-" + (useSnapshot ? "owned" : "none") + "-" + mode, result);
        }

        /// <summary>
        /// The force-buy pre-pass: owning 4 of the 5 needed ingredients
        /// makes the post-reduction craft look cheap, and the zero-owned
        /// baseline is what must keep the root bought anyway.
        /// </summary>
        [Fact]
        public async Task ForceBuyPrePass()
        {
            var builder = PipelineBuilder.SingleRecipeTree(5).WithInventoryReducer();
            builder.PriceApi.AddPrice(1, buyUnitPrice: 1000, sellUnitPrice: 100);
            builder.PriceApi.AddPrice(2, buyUnitPrice: 300, sellUnitPrice: 30);

            var snapshot = new AccountSnapshot
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

            var result = await builder.Build().GenerateStructuredAsync(
                1, 1, snapshot, CancellationToken.None,
                ownMaterialsMode: OwnMaterialsMode.Valued, priceBasis: PriceBasis.InstantBuy);

            Verify("force-buy-pre-pass", result);
        }

        /// <summary>
        /// A currency-priced vendor offer against a TP price, with and
        /// without a valuation for that currency - the two sides of
        /// "is a non-coin cost comparable to gold at all".
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(5)]
        public async Task CurrencyValuedVendorOffer(long copperPerUnit)
        {
            using (var tmp = new TempDirectory())
            {
                var store = NewVendorStore(tmp.Path);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "golden-karma-offer",
                        OutputItemId = 1,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = 2, Count = 50 },
                        },
                        MerchantName = "Karma Vendor",
                    },
                });

                var pipeline = PipelineBuilder.Create()
                    .WithPrice(1, buyUnitPrice: 1000, sellUnitPrice: 2000)
                    .WithItem(1, "Karma Item", "karma.png")
                    .WithVendorOfferStore(store)
                    .WithInventoryReducer()
                    .Build();

                var valuation = copperPerUnit > 0
                    ? new TaimisToolbench.Models.CurrencyValuation(new Dictionary<int, long> { { 2, copperPerUnit } })
                    : TaimisToolbench.Models.CurrencyValuation.None;

                var result = await pipeline.GenerateStructuredAsync(
                    1, 3, null, CancellationToken.None,
                    currencyValuation: valuation, priceBasis: PriceBasis.InstantBuy);

                Verify("currency-valuation-" + copperPerUnit, result);
            }
        }

        /// <summary>
        /// Two roots in one request, so the golden pins the multi-item
        /// aggregation as well as each root's own decision.
        /// </summary>
        [Fact]
        public async Task MultiItemRoots()
        {
            var pipeline = PipelineBuilder.TwoRootTree().Build();

            var result = await pipeline.GenerateStructuredAsync(
                new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 1, Quantity = 2 },
                    new PlanRequestItem { ItemId = 2, Quantity = 3 },
                },
                null,
                CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Verify("multi-item-roots", result);
        }

        /// <summary>
        /// Four levels of craftable nesting with a batching recipe partway
        /// down, so the golden covers craft step ordering and the
        /// craft-vs-buy comparison repeated at every depth rather than only
        /// at a root.
        /// </summary>
        [Fact]
        public async Task DeepNestedTree()
        {
            var builder = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(Recipe(10, output: 1, outputCount: 1, ingredientId: 2, ingredientCount: 2))
                .WithSearchResult(2, 20)
                .WithRecipe(Recipe(20, output: 2, outputCount: 3, ingredientId: 3, ingredientCount: 5))
                .WithSearchResult(3, 30)
                .WithRecipe(Recipe(30, output: 3, outputCount: 1, ingredientId: 4, ingredientCount: 4))
                .WithPrice(1, buyUnitPrice: 9000, sellUnitPrice: 20000)
                .WithPrice(2, buyUnitPrice: 1500, sellUnitPrice: 4000)
                .WithPrice(3, buyUnitPrice: 300, sellUnitPrice: 700)
                .WithPrice(4, buyUnitPrice: 30, sellUnitPrice: 90)
                .WithItem(1, "Depth 0", "d0.png", rarity: "Exotic")
                .WithItem(2, "Depth 1", "d1.png", rarity: "Rare")
                .WithItem(3, "Depth 2", "d2.png", rarity: "Masterwork")
                .WithItem(4, "Depth 3", "d3.png", rarity: "Fine");

            var result = await builder.Build().GenerateStructuredAsync(
                1, 2, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Verify("deep-nested-tree", result);
        }

        private static RawRecipe Recipe(
            int id, int output, int outputCount, int ingredientId, int ingredientCount)
        {
            return new RawRecipe
            {
                Id = id,
                OutputItemId = output,
                OutputItemCount = outputCount,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = ingredientId, Count = ingredientCount },
                },
                Disciplines = new List<string> { "Artificer" },
                MinRating = 300,
                Flags = new List<string> { "AutoLearned" },
            };
        }

        private static VendorOfferStore NewVendorStore(string dir)
        {
            var store = new VendorOfferStore(dir, new VendorOfferLoader());
            store.LoadBaseline(null);
            return store;
        }

        private static void Verify(string scenario, CraftingPlanResult result)
        {
            string actual = PlanResultDump.Render(result);
            string relative = Path.Combine("Goldens", "plan-solver", scenario + ".txt");
            string goldenPath = Path.Combine(AppContext.BaseDirectory, relative);

            if (Environment.GetEnvironmentVariable("TTB_REGEN_PLAN_GOLDENS") == "1")
            {
                string source = RepoFileLocator.FindRepoFile(
                    Path.Combine("tests", "TaimisToolbench.Tests", relative));
                Assert.True(source != null, "Cannot regenerate: no source golden at " + relative);
                File.WriteAllText(source, actual);

                // Never a silent pass: a regeneration run is a deliberate
                // act that must be reviewed, so it reports as a failure.
                Assert.Fail("Regenerated " + relative + " - review the diff, then unset TTB_REGEN_PLAN_GOLDENS.");
            }

            Assert.True(File.Exists(goldenPath), "Golden not found at " + goldenPath);

            var expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n").Split('\n');
            var lines = actual.Replace("\r\n", "\n").Split('\n');

            int shared = Math.Min(expected.Length, lines.Length);
            for (int i = 0; i < shared; i++)
            {
                // Line by line so a failure names the single decision that
                // moved rather than dumping the whole plan.
                Assert.Equal(expected[i], lines[i]);
            }

            Assert.Equal(expected.Length, lines.Length);
        }
    }
}
