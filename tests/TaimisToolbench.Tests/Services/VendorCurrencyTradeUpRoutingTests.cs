using System;
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
    /// Three Secrets of the Obscure crafting materials are each sold for a
    /// flat 250 units of one map currency, and are each also listed against
    /// several zone reward chests. The chests are account bound, have no
    /// recipe and no vendor of their own, so the module can neither price
    /// them nor plan a route to one. Ranked on coin alone every chest
    /// scored 0 and beat the flat currency price, and the plan told the
    /// player to acquire chests instead of trading up the currency.
    /// <para>
    /// Driven over the SHIPPED corpus (ref/recipes_seed.json,
    /// ref/mystic_forge_recipes.json, ref/vendor_offers.json) through the
    /// production RecipeService, VendorOfferLoader and PlanSolver.
    /// </para>
    /// </summary>
    public class VendorCurrencyTradeUpRoutingTests
    {
        private const int ClotOfCongealedScreams = 100098;
        private const int PouchOfStardust = 99964;
        private const int CaseOfCapturedLightning = 100267;

        private const int CalcifiedGasp = 75;
        private const int PinchOfStardust = 73;
        private const int StaticCharge = 72;

        /// <summary>Every one of the three trades 250 currency for 1 item.</summary>
        private const int CurrencyPerUnit = 250;

        [Theory]
        [InlineData(ClotOfCongealedScreams, CalcifiedGasp)]
        [InlineData(PouchOfStardust, PinchOfStardust)]
        [InlineData(CaseOfCapturedLightning, StaticCharge)]
        public async Task TheTradeUpWins_AndNoChestCostReachesThePlan(int itemId, int currencyId)
        {
            const int Quantity = 18;

            var f = await BuildAsync(itemId, Quantity);
            var result = new PlanSolver().Solve(
                f.Tree, f.Prices, f.Offers, PriceBasis.InstantBuy,
                vendorCostSubtrees: f.Subtrees);

            var decision = result.Decisions[f.Tree.NodeId];

            Assert.Equal(AcquisitionSource.BuyFromVendor, decision.Source);

            var currencyCost = Assert.Single(decision.VendorCurrencyCosts);
            Assert.Equal(currencyId, currencyCost.Id);
            Assert.Equal(Quantity * CurrencyPerUnit, currencyCost.Count);

            // The chest routes are the ones that used to win here.
            Assert.Null(decision.VendorItemCosts);

            var step = Assert.Single(result.Plan.Steps, s => s.ItemId == itemId);
            Assert.Equal(AcquisitionSource.BuyFromVendor, step.Source);
            Assert.Null(step.VendorBarterItemCosts);
        }

        [Fact]
        public void EveryOneOfTheThree_ShipsBothRoutes()
        {
            // The routing assertion above is only meaningful while the
            // corpus still offers the chest route it has to beat. If a
            // future scrape drops either side, this fails rather than
            // letting the test above pass for the wrong reason.
            var corpus = RealCorpusFixture.Load();

            foreach (int itemId in new[]
            {
                ClotOfCongealedScreams, PouchOfStardust, CaseOfCapturedLightning,
            })
            {
                var offers = corpus.OffersByOutputItem[itemId];

                Assert.Contains(offers, o => IsFlatCurrencyTradeUp(o));
                Assert.Contains(offers, o => IsSingleItemCost(o));
            }
        }

        private static bool IsFlatCurrencyTradeUp(VendorOffer offer)
        {
            return offer.OutputCount == 1 &&
                offer.CostLines.Count == 1 &&
                offer.CostLines[0].Type == "Currency" &&
                offer.CostLines[0].Id != Gw2Constants.CoinCurrencyId &&
                offer.CostLines[0].Count == CurrencyPerUnit;
        }

        private static bool IsSingleItemCost(VendorOffer offer)
        {
            return offer.CostLines.Count == 1 && offer.CostLines[0].Type == "Item";
        }

        private sealed class Fixture
        {
            public RecipeNode Tree;
            public Dictionary<int, ItemPrice> Prices;
            public Dictionary<int, IReadOnlyList<VendorOffer>> Offers;
            public VendorCostLineSubtrees Subtrees;
        }

        /// <summary>
        /// Builds the tree and the cost-line subtrees the way
        /// CraftingPlanPipeline.ExpandVendorCostLinesAsync does, so the
        /// chest routes get every chance to be priced before the solve.
        /// </summary>
        private static async Task<Fixture> BuildAsync(int itemId, int quantity)
        {
            var corpus = RealCorpusFixture.Load();
            var recipeService = corpus.NewRecipeService();
            var tree = await recipeService.BuildTreeAsync(itemId, quantity, CancellationToken.None);

            var prices = new Dictionary<int, ItemPrice>();
            var offers = new Dictionary<int, IReadOnlyList<VendorOffer>>();
            AddOffers(corpus, ItemIdsIn(tree), offers);

            var subtreesByItemId = new Dictionary<int, RecipeNode>();
            var built = new HashSet<int>();
            var frontier = new List<IReadOnlyList<VendorOffer>>(offers.Values);

            for (int round = 0; round < 6; round++)
            {
                var wanted = VendorCostLineSubtrees.CollectUnpricedCostItemIds(
                    frontier, prices, PriceBasis.InstantBuy, built);
                if (wanted.Count == 0)
                {
                    break;
                }

                var next = new List<IReadOnlyList<VendorOffer>>();
                foreach (int costItemId in wanted.OrderBy(id => id))
                {
                    built.Add(costItemId);
                    var subtree = await recipeService.BuildTreeAsync(costItemId, 1, CancellationToken.None);
                    subtreesByItemId[costItemId] = subtree;
                    AddOffers(corpus, ItemIdsIn(subtree), offers, next);
                }

                frontier = next;
            }

            return new Fixture
            {
                Tree = tree,
                Prices = prices,
                Offers = offers,
                Subtrees = VendorCostLineSubtrees.Create(subtreesByItemId),
            };
        }

        private static HashSet<int> ItemIdsIn(RecipeNode root)
        {
            var ids = new HashSet<int>();
            Walk(root, ids);
            return ids;
        }

        private static void Walk(RecipeNode node, HashSet<int> ids)
        {
            if (node.IngredientType == "Item")
            {
                ids.Add(node.Id);
            }

            foreach (var recipe in node.Recipes)
            {
                foreach (var ingredient in recipe.Ingredients)
                {
                    Walk(ingredient, ids);
                }
            }
        }

        /// <summary>
        /// Offers are ordered by OfferId, matching VendorOfferStore, so a
        /// tie between two offers resolves here the way it does in the app.
        /// </summary>
        private static void AddOffers(
            RealCorpusFixture corpus,
            HashSet<int> itemIds,
            Dictionary<int, IReadOnlyList<VendorOffer>> into,
            List<IReadOnlyList<VendorOffer>> alsoInto = null)
        {
            foreach (int id in itemIds)
            {
                if (!corpus.OffersByOutputItem.TryGetValue(id, out var list))
                {
                    continue;
                }

                var ordered = list.OrderBy(o => o.OfferId, StringComparer.Ordinal).ToList();
                into[id] = ordered;
                alsoInto?.Add(ordered);
            }
        }
    }
}
