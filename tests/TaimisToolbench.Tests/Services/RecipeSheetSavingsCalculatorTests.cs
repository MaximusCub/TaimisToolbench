using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// opportunity-notes (RECIPE-SHEET SAVINGS) - direct unit tests on
    /// RecipeSheetSavingsCalculator's pure tree-walk, using plain
    /// CraftingTreeNode fixtures (no Blish reference, no solver/pipeline
    /// round-trip needed) plus a REAL VendorOfferStore backed by a
    /// temp-directory baseline (repo invariant: use real stores with
    /// temporary directories, never a fake/mirrored store).
    /// </summary>
    public class RecipeSheetSavingsCalculatorTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly VendorOfferLoader _loader;

        public RecipeSheetSavingsCalculatorTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            _loader = new VendorOfferLoader();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private VendorOfferStore MakeStore(params VendorOffer[] offers)
        {
            var store = new VendorOfferStore(_tempDir, _loader);
            var dataset = new VendorOfferDataset
            {
                SchemaVersion = 1,
                GeneratedAt = "2026-01-01T00:00:00Z",
                Source = "test",
                Offers = new List<VendorOffer>(offers),
            };
            string json = _loader.Serialize(dataset);
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                store.LoadBaseline(stream);
            }

            return store;
        }

        private static VendorOffer CoinSheetOffer(int sheetItemId, int coinCost)
        {
            return new VendorOffer
            {
                OfferId = "sheet-" + sheetItemId,
                OutputItemId = sheetItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = coinCost },
                },
                MerchantName = "TestMerchant",
            };
        }

        /// <summary>
        /// Bought item 100 (chosen unit cost 50), with a reference branch
        /// (recipe 999, "Chef" 400, LearnedFromItem) whose one ingredient
        /// costs 300 total for the 10 units needed (30/unit craft cost) -
        /// 20/unit savings. Sheet (item 500) costs 200 coin. Recipe 999 is
        /// missing (not in learnedRecipeIds).
        /// </summary>
        private static CraftingTreeNode BoughtNodeWithReferenceBranch(
            long unitCost = 50, int quantity = 10, long ingredientSubtreeCost = 300,
            int recipeId = 999, bool learnedFromItem = true,
            List<string> disciplines = null, int minRating = 400,
            List<CostLine> vendorCurrencyCosts = null,
            IEnumerable<CraftingTreeNode> extraChildren = null)
        {
            var children = new List<CraftingTreeNode>
            {
                new CraftingTreeNode
                {
                    ItemId = 200,
                    Quantity = quantity * 2,
                    Decision = CraftingDecision.BuyFromTp,
                    SubtreeCost = ingredientSubtreeCost,
                },
            };
            if (extraChildren != null)
            {
                children.AddRange(extraChildren);
            }

            return new CraftingTreeNode
            {
                ItemId = 100,
                Quantity = quantity,
                Decision = CraftingDecision.BuyFromTp,
                UnitCost = unitCost,
                IsReferenceBranch = true,
                ReferenceRecipeId = recipeId,
                ReferenceRecipeDisciplines = disciplines ?? new List<string> { "Chef" },
                ReferenceRecipeMinRating = minRating,
                ReferenceRecipeIsLearnedFromItem = learnedFromItem,
                VendorCurrencyCosts = vendorCurrencyCosts,
                Children = children,
            };
        }

        [Fact]
        public void PositiveSavings_SheetAvailable_MissingLearnedFromItemRecipe_EmitsOpportunity()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Single(result.RecipeSheetSavingsOpportunities);
            var opp = result.RecipeSheetSavingsOpportunities[0];
            Assert.Equal(100, opp.ItemId);
            Assert.Equal(999, opp.RecipeId);
            Assert.Equal(500, opp.SheetItemId);
            Assert.Equal(200, opp.SheetCost);
            Assert.Equal(20, opp.SavingsPerUnit); // 50 - 30
            Assert.False(opp.DisciplineBlocked); // no snapshot -> never claim blocked
        }

        [Fact]
        public void RecipeAlreadyLearned_NoOpportunity()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int> { 999 }, prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void NotLearnedFromItem_NoOpportunity()
        {
            var node = BoughtNodeWithReferenceBranch(learnedFromItem: false);
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void SheetNotInCuratedMap_EmitsNothing()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: new Dictionary<int, int>(), characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void SheetInMapButNoVendorOffer_EmitsNothing()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(); // no offers at all
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        // OffersForItem narrowed from VendorOfferStore to
        // Func<int, IReadOnlyList<VendorOffer>> - pins the null-delegate
        // guard (the direct replacement for the old `vendorOfferStore !=
        // null` check) with every OTHER input otherwise satisfied, so this
        // fails only on the delegate-null branch, not any of the other
        // required-input guards NoTreeAtAll/SheetInMapButNoVendorOffer
        // above already cover.
        [Fact]
        public void NullOffersForItemDelegate_NoOffersSource_EmitsNothing_NoCrash()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: null,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.NotNull(result.RecipeSheetSavingsOpportunities);
            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        // A sheet offer that is ONLY available during a
        // festival must not be priced as if it were a year-round offer
        // (SeasonalOfferFilter's "the plan always assumes the regular
        // market" law - see VendorOffer.SeasonalFestival's own doc
        // comment). No such offer exists in shipped data today; this
        // proves the guard rather than a real-data regression.
        [Fact]
        public void SheetOfferIsSeasonalOnly_SkippedForPricing_EmitsNothing()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var seasonalOffer = CoinSheetOffer(500, 200);
            seasonalOffer.SeasonalFestival = Gw2Constants.HalloweenFestivalName;
            var store = MakeStore(seasonalOffer);
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        // A reference branch whose only children are
        // cost-component leaves (IsCostComponent) has zero children
        // actually counted into craftTotal - without the countedChildren
        // guard this would leave craftUnitCost at 0 and report the full
        // chosen price as SavingsPerUnit.
        [Fact]
        public void OnlyCostComponentChildren_NoOpportunity()
        {
            var node = new CraftingTreeNode
            {
                ItemId = 100,
                Quantity = 10,
                Decision = CraftingDecision.BuyFromTp,
                UnitCost = 50,
                IsReferenceBranch = true,
                ReferenceRecipeId = 999,
                ReferenceRecipeDisciplines = new List<string> { "Chef" },
                ReferenceRecipeMinRating = 400,
                ReferenceRecipeIsLearnedFromItem = true,
                Children = new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 200,
                        Quantity = 20,
                        Decision = CraftingDecision.BuyFromVendor,
                        SubtreeCost = 300,
                        IsCostComponent = true,
                    },
                },
            };
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void NonPositiveSavings_NoOpportunity()
        {
            // Craft cost per unit (300/10=30) now exceeds chosen cost (25/unit).
            var node = BoughtNodeWithReferenceBranch(unitCost: 25);
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        // A reference-branch child reported as
        // owned (Have) must NOT contribute 0 to the hypothetical craft
        // cost - this whole node is a hypothetical "what if I crafted
        // instead" branch, and those owned units may already be allocated
        // to the real plan elsewhere, so the craft cost is unprovable, not
        // free. Extra Have child alongside the normal priced ingredient -
        // if Have still contributed 0 this would otherwise emit the same
        // opportunity as PositiveSavings_SheetAvailable_MissingLearnedFromItemRecipe_EmitsOpportunity.
        [Fact]
        public void HaveChild_TreatedAsUnprovable_NoOpportunity()
        {
            var node = BoughtNodeWithReferenceBranch(extraChildren: new[]
            {
                new CraftingTreeNode
                {
                    ItemId = 201,
                    Quantity = 5,
                    Decision = CraftingDecision.Have,
                },
            });
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        // The pre-existing "unprovable child bails
        // the whole craft cost" path (Currency/Unknown/GuildUpgrade/
        // UnrecognizedIngredient children) had no direct test - only the
        // Have-child path was silently exempted from it. Currency here
        // stands in for the whole "not Craft/BuyFromTp/BuyFromVendor"
        // bail branch.
        [Fact]
        public void UnprovableChild_Currency_TreatedAsUnprovable_NoOpportunity()
        {
            var node = BoughtNodeWithReferenceBranch(extraChildren: new[]
            {
                new CraftingTreeNode
                {
                    ItemId = 202,
                    Quantity = 5,
                    Decision = CraftingDecision.Currency,
                },
            });
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void VendorCurrencyCostsPresent_NotComparable_NoOpportunity()
        {
            var node = BoughtNodeWithReferenceBranch(
                vendorCurrencyCosts: new List<CostLine> { new CostLine { Type = "Currency", Id = 2, Count = 10 } });
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void VendorBarterItemCostPresent_NotComparable_NoOpportunity()
        {
            // A barter-priced vendor node reaches "UnitCost does not
            // represent the whole cost" by the other route: its cost is an
            // untradeable item's units, so VendorCurrencyCosts is empty and
            // a currency-only guard would wave it through.
            var node = BoughtNodeWithReferenceBranch();
            node.VendorHasBarterItemCost = true;
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void NestedVendorBarterItemCost_RecursivelyDetected_NoOpportunity()
        {
            // The barter twin of NestedVendorCurrencyCosts_... below: the
            // barter-priced node sits two levels down, so a direct-child
            // check would count the Craft child's SubtreeCost as pure coin.
            var barterGrandchild = new CraftingTreeNode
            {
                ItemId = 300,
                Quantity = 1,
                Decision = CraftingDecision.BuyFromVendor,
                SubtreeCost = 0,
                VendorHasBarterItemCost = true,
            };
            var craftChild = new CraftingTreeNode
            {
                ItemId = 301,
                Quantity = 1,
                Decision = CraftingDecision.Craft,
                SubtreeCost = 1,
                Children = new List<CraftingTreeNode> { barterGrandchild },
            };
            var node = BoughtNodeWithReferenceBranch(extraChildren: new[] { craftChild });
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        // VendorCurrencyCostsPresent_NotComparable_
        // NoOpportunity above only sets VendorCurrencyCosts on the PARENT
        // fixture's direct BuyFromTp child - it never proves the guard
        // walks deeper. This fixture puts the karma-priced vendor node two
        // levels down (a Craft child whose own child is the BuyFromVendor
        // node), so a direct-child-only check would wrongly count the
        // Craft child's SubtreeCost as pure coin and still emit an
        // opportunity.
        [Fact]
        public void NestedVendorCurrencyCosts_RecursivelyDetected_NoOpportunity()
        {
            var karmaGrandchild = new CraftingTreeNode
            {
                ItemId = 300,
                Quantity = 1,
                Decision = CraftingDecision.BuyFromVendor,
                SubtreeCost = 1,
                VendorCurrencyCosts = new List<CostLine> { new CostLine { Type = "Currency", Id = 2, Count = 2000 } },
            };
            var craftChild = new CraftingTreeNode
            {
                ItemId = 301,
                Quantity = 1,
                Decision = CraftingDecision.Craft,
                SubtreeCost = 1,
                Children = new List<CraftingTreeNode> { karmaGrandchild },
            };
            var node = BoughtNodeWithReferenceBranch(extraChildren: new[] { craftChild });
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void DisciplineBlocked_WhenAccountLacksIt()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };
            var characterDisciplines = new List<SnapshotCharacterDiscipline>
            {
                new SnapshotCharacterDiscipline { CharacterName = "Alice", Discipline = "Chef", Rating = 200 },
            };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: characterDisciplines);

            var opp = Assert.Single(result.RecipeSheetSavingsOpportunities);
            Assert.True(opp.DisciplineBlocked);
            Assert.Equal("Chef", opp.Discipline);
            Assert.Equal(400, opp.RequiredRating);
        }

        [Fact]
        public void DisciplineSatisfied_NotBlocked()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };
            var characterDisciplines = new List<SnapshotCharacterDiscipline>
            {
                new SnapshotCharacterDiscipline { CharacterName = "Alice", Discipline = "Chef", Rating = 400 },
            };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: characterDisciplines);

            var opp = Assert.Single(result.RecipeSheetSavingsOpportunities);
            Assert.False(opp.DisciplineBlocked);
        }

        // Multi-discipline recipe must steer toward the
        // discipline the account is closest to training, not whichever
        // name sorts first - "Armorsmith" sorts before "Weaponsmith", but
        // the account here is far closer to the Weaponsmith requirement.
        [Fact]
        public void MultiDiscipline_PicksClosestAccountRating_NotAlphabeticallyFirst()
        {
            var node = BoughtNodeWithReferenceBranch(
                disciplines: new List<string> { "Armorsmith", "Weaponsmith" }, minRating: 400);
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };
            var characterDisciplines = new List<SnapshotCharacterDiscipline>
            {
                new SnapshotCharacterDiscipline { CharacterName = "Alice", Discipline = "Armorsmith", Rating = 100 },
                new SnapshotCharacterDiscipline { CharacterName = "Alice", Discipline = "Weaponsmith", Rating = 350 },
            };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: characterDisciplines);

            var opp = Assert.Single(result.RecipeSheetSavingsOpportunities);
            Assert.Equal("Weaponsmith", opp.Discipline);
            Assert.True(opp.DisciplineBlocked);
        }

        [Fact]
        public void MultiItemRoots_WalksEveryRoot()
        {
            var nodeA = BoughtNodeWithReferenceBranch();
            nodeA.ItemId = 100;
            var nodeB = BoughtNodeWithReferenceBranch(recipeId: 998);
            nodeB.ItemId = 101;
            var result = new CraftingPlanResult
            {
                MultiItemRoots = new List<CraftingTreeNode> { nodeA, nodeB },
            };
            var store = MakeStore(CoinSheetOffer(500, 200), CoinSheetOffer(600, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 }, { 998, 600 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Equal(2, result.RecipeSheetSavingsOpportunities.Count);
        }

        [Fact]
        public void NullResult_NoOp()
        {
            RecipeSheetSavingsCalculator.Apply(
                null, null, null, PriceBasis.BuyOrder, null, null, null);
        }

        [Fact]
        public void NoTreeAtAll_EmptyOutputsListNotNull()
        {
            var result = new CraftingPlanResult();

            RecipeSheetSavingsCalculator.Apply(
                result, new HashSet<int>(), new Dictionary<int, ItemPrice>(), PriceBasis.BuyOrder,
                MakeStore().GetOffersForItem, new Dictionary<int, int> { { 1, 2 } }, null);

            Assert.NotNull(result.RecipeSheetSavingsOpportunities);
            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }

        [Fact]
        public void PicksCheapestOfMultipleSheetOffers()
        {
            var node = BoughtNodeWithReferenceBranch();
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(
                new VendorOffer
                {
                    OfferId = "expensive", OutputItemId = 500, OutputCount = 1,
                    CostLines = new List<CostLine> { new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 999 } },
                    MerchantName = "Expensive",
                },
                CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Equal(200, result.RecipeSheetSavingsOpportunities[0].SheetCost);
        }

        // --- Correction (merged-ceil-remainder,
        // measured): despite the name, this is NOT a real downstream
        // consumer of AllocateVendorNodeCosts - it hand-constructs a
        // CraftingTreeNode tree directly via BoughtNodeWithReferenceBranch
        // and feeds `ingredientSubtreeCost` in as a plain constructor
        // constant. It never calls PlanSolver.Solve or
        // AllocateVendorNodeCosts, so it cannot exercise the merge/
        // apportionment code path at all and cannot detect a regression
        // in it - only in RecipeSheetSavingsCalculator's own craftUnitCost
        // = SubtreeCost / Quantity arithmetic. The 500 below was chosen by
        // hand to EQUAL what AllocateVendorNodeCosts' fair proportional
        // share would be for a two-occurrence "100 for 1000c" bulk offer
        // (see MultiOccurrenceEqualQuantityBulkVendorOffer_BatchOverrun-
        // SharedProportionally in PlanSolverVendorBatchingTests for the
        // actual integration coverage of that shape) - it is illustrative
        // context for why 500, not evidence the calculator was ever wired
        // to it.
        [Fact]
        public void MergedVendorLeafIngredient_FairProportionalShare_NoOverstatedSavings()
        {
            // node.Quantity (item 100's own quantity) = 10, chosen unit
            // cost 50/unit, ingredient craftTotal = 500 (the fair
            // proportional share). craftUnitCost = 500 / 10 = 50, exactly
            // matching chosenUnitCost - correctly zero real savings, so
            // nothing is emitted at all (savingsPerUnit <= 0 bails before
            // ever constructing an opportunity).
            var node = BoughtNodeWithReferenceBranch(unitCost: 50, quantity: 10, ingredientSubtreeCost: 500);
            var result = new CraftingPlanResult { CraftingTree = node };
            var store = MakeStore(CoinSheetOffer(500, 200));
            var sheetMap = new Dictionary<int, int> { { 999, 500 } };

            RecipeSheetSavingsCalculator.Apply(
                result, learnedRecipeIds: new HashSet<int>(), prices: new Dictionary<int, ItemPrice>(),
                priceBasis: PriceBasis.BuyOrder, offersForItem: store.GetOffersForItem,
                recipeSheetItemIdByRecipeId: sheetMap, characterDisciplines: null);

            Assert.Empty(result.RecipeSheetSavingsOpportunities);
        }
    }
}
