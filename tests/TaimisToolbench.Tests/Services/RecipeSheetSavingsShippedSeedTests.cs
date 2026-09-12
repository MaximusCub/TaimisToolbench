using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RepoFileLocator;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// End to end over the shipped data: the real seed loader reads
    /// ref/recipe_sheet_items.json, a real VendorOfferStore reads
    /// ref/vendor_offers.json, and the real
    /// RecipeSheetSavingsCalculator prices a missing recipe from both.
    /// <para>
    /// The two recipes here came off a field report. A tester was missing
    /// "Recipe: Gift of Color" and "Recipe: Gift of Light", both sold, and
    /// the plan showed no price because the seed held one unrelated entry.
    /// </para>
    /// </summary>
    public class RecipeSheetSavingsShippedSeedTests : IDisposable
    {
        private const int GiftOfColorRecipeId = 3165;
        private const int GiftOfColorSheetItemId = 9632;
        private const int GiftOfLightRecipeId = 843;
        private const int GiftOfLightSheetItemId = 9626;

        private readonly string _tempDir;
        private readonly VendorOfferStore _store;

        public RecipeSheetSavingsShippedSeedTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TaimisToolbench_ShippedSheets_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);

            _store = new VendorOfferStore(_tempDir, new VendorOfferLoader());
            using (var stream = File.OpenRead(RequireRepoFile(Path.Combine("ref", "vendor_offers.json"))))
            {
                _store.LoadBaseline(stream);
            }
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        [Theory]
        [InlineData(GiftOfColorRecipeId, GiftOfColorSheetItemId)]
        [InlineData(GiftOfLightRecipeId, GiftOfLightSheetItemId)]
        public void AMissingSheetRecipeFromTheFieldReportNowGetsAPriceAndAVendor(
            int recipeId, int expectedSheetItemId)
        {
            var sheetMap = LoadShippedSheetMap();
            Assert.True(
                sheetMap.ContainsKey(recipeId),
                $"ref/recipe_sheet_items.json has no entry for recipe {recipeId}.");
            Assert.Equal(expectedSheetItemId, sheetMap[recipeId]);

            var result = new CraftingPlanResult
            {
                CraftingTree = BoughtItemWithAMissingSheetRecipe(recipeId),
            };

            RecipeSheetSavingsCalculator.Apply(
                result,
                learnedRecipeIds: new HashSet<int>(),
                prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder,
                offersForItem: _store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap,
                characterDisciplines: null);

            // The calculator returns without emitting when nothing in the
            // vendor data prices the sheet, so an opportunity here is proof
            // that it found and costed a shipped offer.
            var opportunity = Assert.Single(result.RecipeSheetSavingsOpportunities);
            Assert.Equal(recipeId, opportunity.RecipeId);
            Assert.Equal(expectedSheetItemId, opportunity.SheetItemId);
            Assert.Equal(100000, opportunity.SheetCost);

            // The vendors behind that price. The calculator reports a cost
            // and not a merchant, so the merchant is read back from the
            // same store it priced from.
            var merchants = _store.GetOffersForItem(expectedSheetItemId)
                .Select(o => o.MerchantName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(new[] { "Miyani", "Mystic Forge Attendant" }, merchants);
        }

        [Fact]
        public void TheShippedSeedCoversEverySheetTheOldOneDidNot()
        {
            var sheetMap = LoadShippedSheetMap();

            // The seed held exactly one entry from 2026-08-16 until it was
            // generated. That single entry has to survive being generated,
            // because it was verified by hand against the same endpoint.
            Assert.Equal(96274, sheetMap[13853]);
            Assert.True(
                sheetMap.Count > 1000,
                $"ref/recipe_sheet_items.json loads {sheetMap.Count} entries through " +
                "RecipeSheetItemSeedService.");
        }

        [Fact]
        public void EverySheetInTheSeedHasAtLeastOneShippedOffer()
        {
            var withoutAnOffer = LoadShippedSheetMap()
                .Where(pair => _store.GetOffersForItem(pair.Value).Count == 0)
                .Select(pair => pair.Key)
                .ToList();

            // The seed exists to put a price on a missing recipe. An entry
            // whose sheet no vendor sells can never do that, and the
            // calculator would walk the whole tree to reach the same
            // nothing it reached before the seed was generated.
            Assert.True(
                withoutAnOffer.Count == 0,
                $"{withoutAnOffer.Count} entries in ref/recipe_sheet_items.json name a sheet no " +
                "offer in ref/vendor_offers.json sells. The builder's input is the offers file, " +
                "so the two were generated from different corpora. Rerun " +
                "tools/VendorOfferUpdater --build-recipe-sheet-map.");
        }

        private static IReadOnlyDictionary<int, int> LoadShippedSheetMap()
        {
            using (var stream = File.OpenRead(RequireRepoFile(Path.Combine("ref", "recipe_sheet_items.json"))))
            {
                return RecipeSheetItemSeedService.Load(stream);
            }
        }

        private static string RequireRepoFile(string relativePath)
        {
            string path = FindRepoFile(relativePath);
            Assert.False(
                string.IsNullOrEmpty(path),
                $"Could not locate {relativePath} by walking up from the test assembly's directory.");
            return path;
        }

        /// <summary>
        /// A bought item costing 200000 a unit whose reference branch is
        /// the given recipe, unlearned, with one ingredient child costing
        /// 50000. Crafting therefore saves 150000 a unit, which is what
        /// makes the sheet worth naming.
        /// </summary>
        private static CraftingTreeNode BoughtItemWithAMissingSheetRecipe(int recipeId)
        {
            return new CraftingTreeNode
            {
                ItemId = 19638,
                Quantity = 1,
                Decision = CraftingDecision.BuyFromTp,
                UnitCost = 200000,
                IsReferenceBranch = true,
                ReferenceRecipeId = recipeId,
                ReferenceRecipeDisciplines = new List<string>(),
                ReferenceRecipeMinRating = 400,
                ReferenceRecipeIsLearnedFromItem = true,
                Children = new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 19721,
                        Quantity = 1,
                        Decision = CraftingDecision.BuyFromTp,
                        SubtreeCost = 50000,
                    },
                },
            };
        }
    }
}
