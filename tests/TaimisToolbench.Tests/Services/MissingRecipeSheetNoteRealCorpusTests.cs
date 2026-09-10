using System.Collections.Generic;
using System.IO;
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
    /// A field report, driven end to end over the SHIPPED corpus through
    /// the production RecipeService, VendorOfferStore,
    /// CraftingPlanPipeline and PlanViewModelBuilder.
    /// <para>
    /// A player planning Endless Summer without the Gift of Light or
    /// Relic of the Sunless recipes saw the two Required Recipes rows and
    /// no Plan Notes section at all. Both sheets are in
    /// ref/recipe_sheet_items.json and both are sold in
    /// ref/vendor_offers.json.
    /// </para>
    /// </summary>
    public class MissingRecipeSheetNoteRealCorpusTests
    {
        private const int EndlessSummer = 107022;
        private const int GiftOfLight = 19632;
        private const int GiftOfLightRecipeId = 843;
        private const int GiftOfLightSheetItemId = 9626;
        private const int RelicOfTheSunlessRecipeId = 14026;
        private const int RelicOfTheSunlessSheetItemId = 100705;
        private const int CharmOfSkill = 89216;

        [Fact]
        public async Task BothMissingRecipesReachRequiredRecipesWithTheirSheet()
        {
            var result = await PlanEndlessSummerAsync();

            var light = result.RequiredRecipes.Single(r => r.RecipeId == GiftOfLightRecipeId);
            Assert.True(light.IsMissing);
            Assert.Equal(GiftOfLightSheetItemId, light.SheetItemId);

            var relic = result.RequiredRecipes.Single(r => r.RecipeId == RelicOfTheSunlessRecipeId);
            Assert.True(relic.IsMissing);
            Assert.Equal(RelicOfTheSunlessSheetItemId, relic.SheetItemId);
        }

        /// <summary>
        /// Why RecipeSheetSavingsCalculator emits nothing here, asserted so
        /// a later change cannot quietly move the cause. Gift of Light is
        /// account bound with no vendor offer, so the solver crafts it, and
        /// CraftingTreeBuilder only builds a reference branch under a
        /// BuyFromTp or BuyFromVendor node. The calculator's walk requires
        /// CraftingTreeNode.IsReferenceBranch.
        /// </summary>
        [Fact]
        public async Task TheSavingsCalculatorEmitsNothing_BecauseTheGiftIsCraftedNotBought()
        {
            var result = await PlanEndlessSummerAsync();

            var gift = FindNode(result.CraftingTree, GiftOfLight);
            Assert.Equal(CraftingDecision.Craft, gift.Decision);
            Assert.False(gift.IsReferenceBranch);
            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public async Task ThePlanNotesNameEachMissingSheetsPriceAndMerchant()
        {
            var result = await PlanEndlessSummerAsync();
            var vm = new PlanViewModelBuilder().Build(result);

            var notes = vm.Sections.SingleOrDefault(s => s.SectionType == PlanSectionType.Notes);
            Assert.NotNull(notes);

            // The sheet is pure coin: 10 gold from Miyani or the Mystic
            // Forge Attendant. The coin figure rides the row's CoinValue so
            // the view draws it with icons, per the repo invariant.
            var lightRow = notes.Rows.Single(r => r.Label.Contains("Recipe: Gift of Light"));
            Assert.Equal(
                "Missing recipe - buy Recipe: Gift of Light from Miyani and 1 other merchant for",
                lightRow.Label);
            Assert.Equal(100000, lightRow.CoinValue);

            // The sheet is bartered for 5 Charm of Skill. CostLineValuation
            // cannot price that in coin, so the note states the barter cost
            // as text rather than being dropped.
            var relicRow = notes.Rows.Single(r => r.Label.Contains("Recipe: Relic of the Sunless"));
            Assert.Equal(
                "Missing recipe - buy Recipe: Relic of the Sunless from Abram and 74 other " +
                "merchants for 5x Charm of Skill",
                relicRow.Label);
            Assert.Equal(0, relicRow.CoinValue);
        }

        private static async Task<CraftingPlanResult> PlanEndlessSummerAsync()
        {
            var corpus = RealCorpusFixture.Load();
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                using (var offers = File.OpenRead(RepoFileLocator.FindRepoFile("ref/vendor_offers.json")))
                {
                    store.LoadBaseline(offers);
                }

                IReadOnlyDictionary<int, int> sheetMap;
                using (var sheets = File.OpenRead(RepoFileLocator.FindRepoFile("ref/recipe_sheet_items.json")))
                {
                    sheetMap = RecipeSheetItemSeedService.Load(sheets);
                }

                // The names the live /v2/items fetch supplies in game. Not
                // in ref/item_name_seed.json, which carries recipe outputs
                // rather than the sheets that teach them.
                var itemApi = new InMemoryItemApiClient();
                itemApi.AddItem(GiftOfLightSheetItemId, "Recipe: Gift of Light", "sheet.png");
                itemApi.AddItem(RelicOfTheSunlessSheetItemId, "Recipe: Relic of the Sunless", "sheet.png");
                itemApi.AddItem(CharmOfSkill, "Charm of Skill", "charm.png");

                var pipeline = new CraftingPlanPipeline(
                    corpus.NewRecipeService(),
                    new TradingPostService(new InMemoryPriceApiClient()),
                    new PlanSolver(),
                    new ItemMetadataService(itemApi),
                    store,
                    new InventoryReducer(),
                    recipeSheetItemIdByRecipeId: sheetMap);

                // An empty set, not null: the account has learned nothing,
                // which is what makes every recipe in the plan missing.
                return await pipeline.GenerateStructuredAsync(
                    EndlessSummer, 1, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy,
                    learnedRecipeIds: new HashSet<int>());
            }
        }

        private static CraftingTreeNode FindNode(CraftingTreeNode root, int itemId)
        {
            if (root.ItemId == itemId)
            {
                return root;
            }

            foreach (var child in root.Children)
            {
                var hit = FindNode(child, itemId);
                if (hit != null)
                {
                    return hit;
                }
            }

            return null;
        }
    }
}
