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
    /// A real trade-up, driven end to end over the SHIPPED corpus:
    /// ref/recipes_seed.json, ref/mystic_forge_recipes.json and
    /// ref/vendor_offers.json, through the production RecipeService,
    /// VendorOfferStore, CraftingPlanPipeline, PlanSolver and
    /// PlanViewModelBuilder.
    /// <para>
    /// Gift of Stormy Skies takes 5 each of three Secrets of the Obscure
    /// materials, and its Gift of the Wizard's Tower ingredient takes 13
    /// more of each as vendor barter, so the plan needs 18 of each. Every
    /// one of the three is sold for a flat 250 of one map currency the
    /// module puts no coin value on (docs/ARCHITECTURE.md section 8.3), so
    /// all three coalesce into item rows carrying that currency's rate.
    /// </para>
    /// </summary>
    public class CurrencyTradeUpRealCorpusTests
    {
        private const int GiftOfStormySkies = 100288;

        /// <summary>The four-currency vendor purchase inside the same plan.</summary>
        private const int MultiCurrencyPurchase = 99962;

        private const int PerUnit = 250;
        private const int Required = 18;

        /// <summary>Each traded-up item and the map currency it is sold for.</summary>
        public static IEnumerable<object[]> TradedUpItems()
        {
            yield return new object[] { 100267, 72 };
            yield return new object[] { 99964, 73 };
            yield return new object[] { 100098, 75 };
        }

        /// <summary>
        /// The reported disagreement, on real data. The Shopping List used
        /// to omit these three items entirely whenever the wallet could
        /// convert none of them, which is every plan with no account
        /// snapshot, while the Total Cost table asked for 18 of each.
        /// </summary>
        [Theory]
        [MemberData(nameof(TradedUpItems))]
        public async Task WithNoSnapshot_BothSurfacesStateTheSameRequirementAndCost(int itemId, int currencyId)
        {
            var result = await PlanAsync(null);
            var vm = new PlanViewModelBuilder().Build(result);

            var tableRow = Assert.Single(NonCoinRows(vm), r => r.ItemId == itemId);
            Assert.True(tableRow.IsBarterItemCost);
            Assert.Equal(Required, tableRow.Quantity);

            var shoppingRow = Assert.Single(ShoppingRows(vm), r => r.ItemId == itemId);
            Assert.Equal(Required, shoppingRow.Quantity);

            var amount = Assert.Single(shoppingRow.CurrencyCosts);
            Assert.Equal(Required * PerUnit, amount.Amount);
            Assert.Equal(PerUnit, Assert.Single(shoppingRow.UnitCurrencyCosts).Amount);

            var cost = Assert.Single(result.Plan.BarterItemCosts, b => b.ItemId == itemId);
            Assert.Equal(currencyId, cost.TradeUpCurrencyId);
            Assert.Equal(PerUnit, cost.TradeUpCurrencyPerUnit);
        }

        /// <summary>
        /// A wallet holding moves the Note and nothing else. 1,600 Static
        /// Charge converts into 6 of the 18, so the row says 6 and asks for
        /// the remaining 12, while the Shopping List still states the whole
        /// 18 and the 4,500 they cost.
        /// </summary>
        [Fact]
        public async Task AWalletHolding_MovesTheNoteAndNotTheListedCost()
        {
            var vm = new PlanViewModelBuilder().Build(await PlanAsync(Wallet(1600, 700, 0)));

            var lightning = Assert.Single(NonCoinRows(vm), r => r.ItemId == 100267);
            Assert.Equal(Required, lightning.Quantity);
            Assert.Equal(6, lightning.TradeUpBuysQuantity);
            Assert.Equal(12, lightning.CurrencyNeededQuantity);

            var stardust = Assert.Single(NonCoinRows(vm), r => r.ItemId == 99964);
            Assert.Equal(2, stardust.TradeUpBuysQuantity);
            Assert.Equal(16, stardust.CurrencyNeededQuantity);

            // Held is 0, not unknown: the wallet was read and reports none.
            var screams = Assert.Single(NonCoinRows(vm), r => r.ItemId == 100098);
            Assert.Equal(0, screams.TradeUpCurrencyHeld);
            Assert.Equal(0, screams.TradeUpBuysQuantity);
            Assert.Equal(Required, screams.CurrencyNeededQuantity);

            foreach (int itemId in new[] { 100267, 99964, 100098 })
            {
                var shoppingRow = Assert.Single(ShoppingRows(vm), r => r.ItemId == itemId);
                Assert.Equal(Required, shoppingRow.Quantity);
                Assert.Equal(Required * PerUnit, Assert.Single(shoppingRow.CurrencyCosts).Amount);
            }
        }

        /// <summary>
        /// The same plan buys one item for 250 each of four different
        /// currencies. All four are on its Shopping List row, so the row
        /// states the whole price and not one currency of it.
        /// </summary>
        [Fact]
        public async Task AFourCurrencyPurchase_ListsAllFourCurrencies()
        {
            var vm = new PlanViewModelBuilder().Build(await PlanAsync(null));

            var row = Assert.Single(ShoppingRows(vm), r => r.ItemId == MultiCurrencyPurchase);
            Assert.Equal(1, row.Quantity);
            Assert.Equal(4, row.CurrencyCosts.Count);
            Assert.All(row.CurrencyCosts, c => Assert.Equal(PerUnit, c.Amount));
        }

        /// <summary>
        /// A coalesced item states its requirement once, as the item. Its
        /// map currency is never also a wallet line, which would be the
        /// same requirement counted twice in two units.
        /// </summary>
        [Fact]
        public async Task ACoalescedItemIsNeverAlsoAWalletLine()
        {
            var plan = (await PlanAsync(null)).Plan;

            foreach (var currencyId in new[] { 72, 73, 75 })
            {
                var line = Assert.Single(plan.CurrencyCosts, c => c.CurrencyId == currencyId);

                // The four-currency purchase above is the only claim on
                // these currencies the plan makes.
                Assert.Equal(PerUnit, line.Amount);
            }
        }

        private static async Task<CraftingPlanResult> PlanAsync(AccountSnapshot snapshot)
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
                    GiftOfStormySkies, 1, snapshot, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
            }
        }

        private static AccountSnapshot Wallet(int staticCharge, int stardust, int gasp)
        {
            return new AccountSnapshot
            {
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>(),
                Items = new List<SnapshotItemEntry>(),
                Wallet = new List<SnapshotWalletEntry>
                {
                    new SnapshotWalletEntry { CurrencyId = 72, Value = staticCharge },
                    new SnapshotWalletEntry { CurrencyId = 73, Value = stardust },
                    new SnapshotWalletEntry { CurrencyId = 75, Value = gasp },
                },
            };
        }

        private static List<PlanRowViewModel> NonCoinRows(PlanViewModel vm)
        {
            return vm.Sections
                .Single(s => s.SectionType == PlanSectionType.Summary)
                .Rows
                .Where(r => r.RowType == PlanRowType.CurrencyCost)
                .ToList();
        }

        private static List<PlanRowViewModel> ShoppingRows(PlanViewModel vm)
        {
            return vm.Sections
                .Single(s => s.SectionType == PlanSectionType.ShoppingList)
                .Rows;
        }
    }
}
