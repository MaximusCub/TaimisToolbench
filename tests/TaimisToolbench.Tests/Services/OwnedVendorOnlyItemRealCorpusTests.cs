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
    /// A field report, driven end to end over the SHIPPED corpus:
    /// ref/recipes_seed.json, ref/mystic_forge_recipes.json and
    /// ref/vendor_offers.json, through the production RecipeService,
    /// VendorOfferLoader, CraftingPlanPipeline, PlanSolver and
    /// InventoryReducer. KNOWN-ISSUES #67.
    /// <para>
    /// A plan for the legendary ring Endless Summer showed its Gift of the
    /// Hylek ingredient as a vendor purchase while the account held one in
    /// the bank. The gift has no recipe, so the solver can only buy it, and
    /// the report asked whether a node the solver decided to buy is
    /// eligible for owned stock at all. It is: only a node's DESCENDANTS
    /// are gated on its own decision, never the node's own quantity - see
    /// docs/ARCHITECTURE.md section 8.2.
    /// </para>
    /// </summary>
    public class OwnedVendorOnlyItemRealCorpusTests
    {
        private const int EndlessSummer = 107022;
        private const int GiftOfTheHylek = 106986;
        private const int SunBead = 19717;
        private const int KarmaCurrencyId = 2;

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GiftHeldInTheBank_CountsAsOwned_NotAsAVendorPurchase(bool valueOwnMaterials)
        {
            var result = await PlanEndlessSummerAsync(
                OwnOneGiftIn(AccountItemIndex.SourceBank), valueOwnMaterials);

            var used = result.UsedMaterials.Single(u => u.ItemId == GiftOfTheHylek);
            Assert.Equal(1, used.QuantityUsed);
            Assert.Equal(
                AccountItemIndex.SourceBank,
                used.Sources.Single().Source);

            var gift = FindGift(result);
            Assert.Equal(CraftingDecision.Have, gift.Decision);
            Assert.Equal(1, gift.OwnedQuantityUsed);
            Assert.Equal(0, gift.Quantity);

            Assert.Equal(new[] { "HAVE" }, PillTextsFor(result, gift));
        }

        /// <summary>
        /// Every storage location the snapshot can report the gift from,
        /// against the same plan. Material storage is where the game files
        /// a Mystic crafting material by default; the bank, a shared
        /// inventory slot and a character's bags are the other three the
        /// account snapshot distinguishes.
        /// </summary>
        [Theory]
        [InlineData("MaterialStorage")]
        [InlineData("SharedInventory")]
        [InlineData("Bank")]
        [InlineData("Character:Taimi")]
        public async Task GiftHeldAnywhereTheSnapshotReports_CountsAsOwned(string source)
        {
            var result = await PlanEndlessSummerAsync(OwnOneGiftIn(source), valueOwnMaterials: true);

            Assert.Equal(1, result.UsedMaterials.Single(u => u.ItemId == GiftOfTheHylek).QuantityUsed);
            Assert.Equal(CraftingDecision.Have, FindGift(result).Decision);
        }

        /// <summary>
        /// The control, and what the field report's screenshot shows: with
        /// no account snapshot the gift is a vendor purchase carrying its
        /// two cost lines. Its only pills are the single-source VENDOR
        /// badge and the manual Ignore toggle: no owned stock reached it.
        /// </summary>
        [Fact]
        public async Task WithNoAccountSnapshot_TheGiftIsPlannedAsAVendorPurchase()
        {
            var result = await PlanEndlessSummerAsync(snapshot: null, valueOwnMaterials: true);

            Assert.DoesNotContain(result.UsedMaterials, u => u.ItemId == GiftOfTheHylek);

            var gift = FindGift(result);
            Assert.Equal(CraftingDecision.BuyFromVendor, gift.Decision);
            Assert.Equal(1, gift.Quantity);
            Assert.Equal(0, gift.OwnedQuantityUsed);
            Assert.Equal(new[] { "VENDOR", "IGNORE" }, PillTextsFor(result, gift));

            Assert.Contains(gift.Children, c => c.ItemId == SunBead);
            Assert.Contains(gift.VendorCurrencyCosts, c => c.Id == KarmaCurrencyId);
        }

        private static AccountSnapshot OwnOneGiftIn(string source)
        {
            return new AccountSnapshot
            {
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>(),
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry
                    {
                        ItemId = GiftOfTheHylek,
                        Count = 1,
                        Source = source,
                    },
                },
            };
        }

        /// <summary>
        /// No trading-post prices are supplied: the gift has no recipe and
        /// no trading-post listing in game, so nothing about this plan's
        /// treatment of it depends on a price. Leaving prices out keeps the
        /// test off the network and stable as the market moves.
        /// </summary>
        private static async Task<CraftingPlanResult> PlanEndlessSummerAsync(
            AccountSnapshot snapshot, bool valueOwnMaterials)
        {
            var corpus = RealCorpusFixture.Load();
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                using (var offers = File.OpenRead(RepoFileLocator.FindRepoFile("ref/vendor_offers.json")))
                {
                    store.LoadBaseline(offers);
                }

                var pipeline = new CraftingPlanPipeline(
                    corpus.NewRecipeService(),
                    new TradingPostService(new InMemoryPriceApiClient()),
                    new PlanSolver(),
                    new ItemMetadataService(new InMemoryItemApiClient()),
                    store,
                    new InventoryReducer());

                return await pipeline.GenerateStructuredAsync(
                    EndlessSummer, 1, snapshot, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy,
                    ownMaterialsMode: valueOwnMaterials
                        ? OwnMaterialsMode.Valued
                        : OwnMaterialsMode.Free);
            }
        }

        private static CraftingTreeNode FindGift(CraftingPlanResult result)
        {
            return result.CraftingTree.Children.Single(c => c.ItemId == GiftOfTheHylek);
        }

        /// <summary>
        /// The pills the tree row actually draws, from the same two
        /// production types the view uses: PlanViewModelBuilder projects the
        /// reducer's UsedMaterials into the drawn-stock id set, and
        /// DecisionPillPlanner turns that plus the node into pills.
        /// </summary>
        private static IReadOnlyList<string> PillTextsFor(CraftingPlanResult result, CraftingTreeNode node)
        {
            var vm = new PlanViewModelBuilder().Build(result);
            return DecisionPillPlanner
                .BuildPillSpecs(node, null, null, vm.OwnedStockDrawnItemIds)
                .Select(s => s.Text)
                .ToList();
        }
    }
}
