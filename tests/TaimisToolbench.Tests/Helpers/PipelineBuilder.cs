using System;
using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Tests.Helpers
{
    /// <summary>
    /// One definition of what a default test pipeline looks like.
    ///
    /// The CraftingPlanPipeline* test classes used to be a single 4,719-line
    /// file that spelled out the same four-service wiring at 58 separate
    /// construction sites. That wiring lives here now, once: Build() is the
    /// only place a CraftingPlanPipeline is constructed for those tests, so
    /// changing what a default test pipeline means is a one-file edit.
    ///
    /// The builder wires REAL production objects - RecipeService,
    /// TradingPostService, PlanSolver, ItemMetadataService over the suite's
    /// in-memory API clients - never mocks, matching what the tests did
    /// before the extraction.
    ///
    /// Every builder owns its own client graph and every static fixture
    /// returns a fresh one; nothing is cached or shared between calls, so
    /// the split test classes never contend when xUnit runs them in
    /// parallel.
    /// </summary>
    internal sealed class PipelineBuilder
    {
        /// <summary>
        /// The in-memory clients this builder wires in. Exposed because a
        /// good number of tests drive them past what the fluent With*
        /// methods cover - fault injection (ThrowAlways, ThrowOnCallNumber,
        /// Return404For, DropOnce), the request-batching assertions that
        /// read Calls, and the gated-response cancellation tests.
        /// </summary>
        public InMemoryRecipeApiClient RecipeApi { get; } = new InMemoryRecipeApiClient();

        public InMemoryPriceApiClient PriceApi { get; } = new InMemoryPriceApiClient();

        public InMemoryItemApiClient ItemApi { get; } = new InMemoryItemApiClient();

        private VendorOfferStore _vendorOfferStore;
        private InventoryReducer _reducer;
        private IAccountRecipeClient _accountRecipeClient;
        private CurrencyMetadataService _currencyMetadataService;
        private IReadOnlyDictionary<int, AcquisitionHint> _acquisitionHints;
        private ModuleLog _moduleLog;
        private IReadOnlyDictionary<int, DailyCooldownItem> _dailyCooldownItems;
        private IReadOnlyDictionary<int, int> _recipeSheetItemIdByRecipeId;
        private Func<IReadOnlyList<string>> _activeFestivalNames;
        private IAccountProgressionClient _accountProgressionClient;

        public static PipelineBuilder Create()
        {
            return new PipelineBuilder();
        }

        public PipelineBuilder WithSearchResult(int itemId, params int[] recipeIds)
        {
            RecipeApi.AddSearchResult(itemId, recipeIds);
            return this;
        }

        public PipelineBuilder WithRecipe(RawRecipe recipe)
        {
            RecipeApi.AddRecipe(recipe);
            return this;
        }

        public PipelineBuilder WithPrice(int itemId, int buyUnitPrice, int sellUnitPrice)
        {
            PriceApi.AddPrice(itemId, buyUnitPrice, sellUnitPrice);
            return this;
        }

        public PipelineBuilder WithItem(int id, string name, string icon, string rarity = null, List<string> flags = null)
        {
            ItemApi.AddItem(id, name, icon, rarity, flags);
            return this;
        }

        public PipelineBuilder WithItem(RawItem item)
        {
            ItemApi.AddItem(item);
            return this;
        }

        public PipelineBuilder WithVendorOfferStore(VendorOfferStore vendorOfferStore)
        {
            _vendorOfferStore = vendorOfferStore;
            return this;
        }

        public PipelineBuilder WithReducer(InventoryReducer reducer)
        {
            _reducer = reducer;
            return this;
        }

        /// <summary>Wires a real InventoryReducer - the owned-materials tests' default.</summary>
        public PipelineBuilder WithInventoryReducer()
        {
            return WithReducer(new InventoryReducer());
        }

        public PipelineBuilder WithAccountRecipeClient(IAccountRecipeClient accountRecipeClient)
        {
            _accountRecipeClient = accountRecipeClient;
            return this;
        }

        public PipelineBuilder WithCurrencyMetadataService(CurrencyMetadataService currencyMetadataService)
        {
            _currencyMetadataService = currencyMetadataService;
            return this;
        }

        public PipelineBuilder WithAcquisitionHints(IReadOnlyDictionary<int, AcquisitionHint> acquisitionHints)
        {
            _acquisitionHints = acquisitionHints;
            return this;
        }

        public PipelineBuilder WithModuleLog(ModuleLog moduleLog)
        {
            _moduleLog = moduleLog;
            return this;
        }

        public PipelineBuilder WithDailyCooldownItems(IReadOnlyDictionary<int, DailyCooldownItem> dailyCooldownItems)
        {
            _dailyCooldownItems = dailyCooldownItems;
            return this;
        }

        public PipelineBuilder WithRecipeSheetItemIds(IReadOnlyDictionary<int, int> recipeSheetItemIdByRecipeId)
        {
            _recipeSheetItemIdByRecipeId = recipeSheetItemIdByRecipeId;
            return this;
        }

        public PipelineBuilder WithActiveFestivalNames(Func<IReadOnlyList<string>> activeFestivalNames)
        {
            _activeFestivalNames = activeFestivalNames;
            return this;
        }

        public PipelineBuilder WithAccountProgressionClient(
            IAccountProgressionClient accountProgressionClient)
        {
            _accountProgressionClient = accountProgressionClient;
            return this;
        }

        /// <summary>
        /// The single construction site. Unset optionals are passed as the
        /// nulls the constructor defaults them to, so a builder that had no
        /// With* call beyond the clients produces exactly the four-argument
        /// pipeline the tests used to write out by hand.
        /// </summary>
        public CraftingPlanPipeline Build()
        {
            return new CraftingPlanPipeline(
                new RecipeService(RecipeApi),
                new TradingPostService(PriceApi),
                new PlanSolver(),
                new ItemMetadataService(ItemApi),
                _vendorOfferStore,
                _reducer,
                _accountRecipeClient,
                _currencyMetadataService,
                _acquisitionHints,
                _moduleLog,
                _dailyCooldownItems,
                _recipeSheetItemIdByRecipeId,
                _activeFestivalNames,
                _accountProgressionClient);
        }

        /// <summary>
        /// The suite's canonical craft tree: item 1 &lt;- recipe 10 &lt;-
        /// <paramref name="ingredientCount"/>x item 2, Weaponsmith 400,
        /// AutoLearned. Deliberately priceless - each caller adds the prices
        /// its own scenario turns on.
        /// </summary>
        public static PipelineBuilder SingleRecipeTree(int ingredientCount)
        {
            return Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
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
                })
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png");
        }

        /// <summary>
        /// SingleRecipeTree's shape stripped of the discipline, rating and
        /// AutoLearned metadata, and priced so the craft wins: item 1
        /// &lt;- recipe 10 &lt;- 3x item 2, with item 1 at 50/1000 and item
        /// 2 at 10/100. The progress-and-logging fixture - those tests
        /// assert on phase events and log lines, never on disciplines.
        /// </summary>
        public static PipelineBuilder PricedRecipeTreeWithoutDiscipline()
        {
            return Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png");
        }

        /// <summary>
        /// The suite's canonical two-root tree, for the
        /// IReadOnlyList&lt;PlanRequestItem&gt; overload: item 1 &lt;-
        /// recipe 10 &lt;- 1x item 3, item 2 &lt;- recipe 20 &lt;- 1x item
        /// 4, every one of the four priced so both roots craft. Neither
        /// recipe carries a discipline or an AutoLearned flag, which is
        /// what makes both of them "not inherently available" for the
        /// learned-recipe tests.
        /// </summary>
        public static PipelineBuilder TwoRootTree()
        {
            return Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 3, Count = 1 },
                    },
                })
                .WithSearchResult(2, 20)
                .WithRecipe(new RawRecipe
                {
                    Id = 20,
                    OutputItemId = 2,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 4, Count = 1 },
                    },
                })
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 1000)
                .WithPrice(2, buyUnitPrice: 60, sellUnitPrice: 1200)
                .WithPrice(3, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithPrice(4, buyUnitPrice: 20, sellUnitPrice: 200)
                .WithItem(1, "Target Item A", "targeta.png")
                .WithItem(2, "Target Item B", "targetb.png")
                .WithItem(3, "Ingredient A", "ingredienta.png")
                .WithItem(4, "Ingredient B", "ingredientb.png");
        }

        /// <summary>
        /// SingleRecipeTree with 3x the ingredient and no reducer - the
        /// sell-side economics fixture, used from the economics, progress-
        /// logging and advisory-annotation test classes.
        /// </summary>
        public static CraftingPlanPipeline BuildEconomicsPipeline(
            out InMemoryPriceApiClient priceApi)
        {
            var builder = SingleRecipeTree(3);
            priceApi = builder.PriceApi;
            return builder.Build();
        }

        /// <summary>
        /// SingleRecipeTree with a real InventoryReducer - the owned-
        /// materials fixture, used from the own-materials, ignore, force-buy
        /// and cancellation test classes.
        /// </summary>
        public static CraftingPlanPipeline BuildOwnMaterialsPipeline(
            out InMemoryPriceApiClient priceApi, int ingredientCount = 5)
        {
            var builder = SingleRecipeTree(ingredientCount).WithInventoryReducer();
            priceApi = builder.PriceApi;
            return builder.Build();
        }

        /// <summary>
        /// A snapshot owning <paramref name="count"/> of SingleRecipeTree's
        /// item 2, in material storage.
        /// </summary>
        public static AccountSnapshot OwnIngredient(int count)
        {
            return new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry
                    {
                        ItemId = 2,
                        Count = count,
                        Source = AccountItemIndex.SourceMaterialStorage,
                    },
                },
            };
        }
    }
}
