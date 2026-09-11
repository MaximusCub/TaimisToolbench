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

        /// <summary>
        /// Each note leads with the sheet's own icon and name, then says
        /// where to buy it. The price is NOT here: it is the Cost cell on
        /// that recipe's own Required Recipes row.
        /// </summary>
        [Fact]
        public async Task ThePlanNotesNameEachMissingSheetAndItsMerchants()
        {
            var result = await PlanEndlessSummerAsync();
            var vm = new PlanViewModelBuilder().Build(result);

            var notes = vm.Sections.SingleOrDefault(s => s.SectionType == PlanSectionType.Notes);
            Assert.NotNull(notes);

            var lightRow = notes.Rows.Single(r => r.NoteSubject == "Recipe: Gift of Light");
            Assert.Equal(GiftOfLightSheetItemId, lightRow.ItemId);
            Assert.Equal("Missing Recipe. Buy from Miyani and 1 other merchant", lightRow.Label);
            Assert.Equal(0, lightRow.CoinValue);

            var relicRow = notes.Rows.Single(r => r.NoteSubject == "Recipe: Relic of the Sunless");
            Assert.Equal(RelicOfTheSunlessSheetItemId, relicRow.ItemId);
            Assert.Equal(
                "Missing Recipe. Buy from Abram and 74 other merchants", relicRow.Label);
            Assert.Equal(0, relicRow.CoinValue);
        }

        /// <summary>
        /// The merchant phrase is the link, and it opens the sheet page's
        /// own Acquisition section - the full merchant list, not the one
        /// merchant the note names. The lead-in is not a link.
        /// </summary>
        [Fact]
        public async Task TheMerchantPhraseLinksToTheSheetsFullMerchantList()
        {
            var result = await PlanEndlessSummerAsync();
            var vm = new PlanViewModelBuilder().Build(result);

            var notes = vm.Sections.Single(s => s.SectionType == PlanSectionType.Notes);
            var lightRow = notes.Rows.Single(r => r.NoteSubject == "Recipe: Gift of Light");

            Assert.Equal(2, lightRow.NoteSegments.Count);
            Assert.False(lightRow.NoteSegments[0].IsLink);
            Assert.Equal("Missing Recipe. Buy from ", lightRow.NoteSegments[0].Text);

            var link = lightRow.NoteSegments[1];
            Assert.True(link.IsLink);
            Assert.Equal("Miyani and 1 other merchant", link.Text);
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Recipe:_Gift_of_Light#Acquisition",
                link.Link.BuildUrl());
            Assert.Equal(IconWikiTarget.AcquisitionHintText, link.Link.Hint);
        }

        /// <summary>
        /// The section the report was looking in. Every part of the price
        /// rides a field the view draws as a number with its own icon: coin
        /// and wallet currency on CoinValue/CurrencyCosts, bartered ITEMS
        /// on SheetBarterItems. The merchants are the Sold By cell.
        /// </summary>
        [Fact]
        public async Task TheRequiredRecipesRowsCarryTheirSheetPriceAndMerchant()
        {
            var result = await PlanEndlessSummerAsync();
            var vm = new PlanViewModelBuilder().Build(result);

            var recipes = vm.Sections.Single(s => s.SectionType == PlanSectionType.RequiredRecipes);

            var lightRow = recipes.Rows.Single(r => r.Label == "Recipe: Gift of Light");
            Assert.Equal(RequiredRecipesVisibility.MissingStatusTag, lightRow.StatusTag);
            Assert.Equal(100000, lightRow.CoinValue);
            Assert.Null(lightRow.SheetBarterItems);
            Assert.Null(lightRow.CurrencyCosts);
            Assert.Equal("Miyani and 1 other merchant", lightRow.SoldByText);

            var relicRow = recipes.Rows.Single(r => r.Label == "Recipe: Relic of the Sunless");
            Assert.Equal(RequiredRecipesVisibility.MissingStatusTag, relicRow.StatusTag);
            Assert.Equal(0, relicRow.CoinValue);
            Assert.Null(relicRow.CurrencyCosts);
            Assert.Equal("Abram and 74 other merchants", relicRow.SoldByText);

            // The barter half is an item id and a count, so the cell draws
            // "5" and the Charm of Skill icon rather than the words.
            var charm = Assert.Single(relicRow.SheetBarterItems);
            Assert.Equal(CharmOfSkill, charm.ItemId);
            Assert.Equal(5, charm.Amount);
        }

        /// <summary>
        /// The Sold By cell's right-click opens the sheet page's own
        /// Acquisition section, which is where every merchant is listed.
        /// </summary>
        [Fact]
        public async Task TheSoldByCellOpensTheSheetsFullMerchantList()
        {
            var result = await PlanEndlessSummerAsync();
            var vm = new PlanViewModelBuilder().Build(result);

            var recipes = vm.Sections.Single(s => s.SectionType == PlanSectionType.RequiredRecipes);
            var relicRow = recipes.Rows.Single(r => r.Label == "Recipe: Relic of the Sunless");

            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Recipe:_Relic_of_the_Sunless#Acquisition",
                relicRow.SoldByWikiTarget.BuildUrl());
            Assert.Equal(
                IconWikiTarget.AcquisitionHintText, relicRow.SoldByWikiTarget.Hint);
        }

        /// <summary>
        /// A recipe the account already has must not carry a price: the
        /// column exists to answer "what does it cost to stop being
        /// missing this", and a Learned row is not missing anything.
        /// </summary>
        [Fact]
        public async Task ALearnedRecipeRowCarriesNoSheetPrice()
        {
            var result = await PlanEndlessSummerAsync(
                learnedRecipeIds: new HashSet<int> { GiftOfLightRecipeId });
            var vm = new PlanViewModelBuilder().Build(result);

            var recipes = vm.Sections.Single(s => s.SectionType == PlanSectionType.RequiredRecipes);
            var lightRow = recipes.Rows.Single(r => r.Label == "Recipe: Gift of Light");

            Assert.Equal(RequiredRecipesVisibility.LearnedStatusTag, lightRow.StatusTag);
            Assert.Equal(0, lightRow.CoinValue);
            Assert.Null(lightRow.SheetBarterItems);
            Assert.Null(lightRow.SoldByText);
        }

        private static async Task<CraftingPlanResult> PlanEndlessSummerAsync(
            ISet<int> learnedRecipeIds = null)
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
                    learnedRecipeIds: learnedRecipeIds ?? new HashSet<int>());
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
