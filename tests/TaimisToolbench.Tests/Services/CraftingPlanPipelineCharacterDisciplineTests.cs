using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class CraftingPlanPipelineCharacterDisciplineTests
    {
        // --- Regression: zero prior coverage on the pipeline
        // wiring that carries AccountSnapshot.CharacterDisciplines through
        // to CraftingPlanResult/PlanSolveContext and back out again through
        // a local ResolveWithOverrides re-solve - only the leaf builder
        // (PlanResultBuilderTests) and the store (SnapshotStoreTests) had
        // coverage; the snapshot -> result -> re-solve wiring that makes
        // the feature appear at all was unverified. ---

        /// <summary>
        /// Item 1 &lt;- recipe 10 &lt;- 1x item 2, gated on Weaponsmith 400
        /// and deliberately NOT AutoLearned, priced so the craft (10c) is far
        /// cheaper than the TP buy (5000c) - which is what makes an untrained
        /// character's exclusion visible in the chosen source. Scenario data
        /// only: the pipeline wiring itself still comes from PipelineBuilder.
        /// </summary>
        private static PipelineBuilder WeaponsmithGatedTree()
        {
            return PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
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
                })
                .WithPrice(1, buyUnitPrice: 5000, sellUnitPrice: 10000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png");
        }

        [Fact]
        public async Task GenerateStructuredAsync_WithCharacterDisciplines_CarriesIntoResultAndContext()
        {
            var pipeline = WeaponsmithGatedTree().Build();

            var snapshot = new AccountSnapshot
            {
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Weaponsmith", Rating = 500, Active = true },
                },
            };

            var result = await pipeline.GenerateStructuredAsync(1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.NotNull(result.CharacterDisciplines);
            Assert.Single(result.CharacterDisciplines);
            Assert.Equal("Anna", result.CharacterDisciplines[0].CharacterName);
            Assert.NotNull(result.SolveContext);
            Assert.Same(result.CharacterDisciplines, result.SolveContext.CharacterDisciplines);
        }

        // (#7, source-selection-simplification
        // design-law gap): real pipeline round-trip (recipe API -> solve
        // -> CraftingTreeBuilder -> CompetencyOpportunityCalculator),
        // proving the whole CraftExcludedByCompetency threading actually
        // reaches CraftingPlanResult.CompetencyOpportunities end-to-end,
        // not just the isolated calculator unit coverage in
        // CompetencyOpportunityCalculatorTests.
        [Fact]
        public async Task GenerateStructuredAsync_CraftExcludedByCompetency_PopulatesCompetencyOpportunities()
        {
            var pipeline = WeaponsmithGatedTree().Build();

            var snapshot = new AccountSnapshot
            {
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                {
                    // Untrained relative to the recipe's MinRating 400 -
                    // craft (10c) is excluded from the automatic pick even
                    // though far cheaper than the TP buy (5000c).
                    new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Weaponsmith", Rating = 100, Active = true },
                },
            };

            var result = await pipeline.GenerateStructuredAsync(1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            var targetStep = Assert.Single(result.Plan.Steps, s => s.ItemId == 1);
            Assert.Equal(AcquisitionSource.BuyFromTp, targetStep.Source);
            Assert.NotNull(result.CompetencyOpportunities);
            var opportunity = Assert.Single(result.CompetencyOpportunities);
            Assert.Equal(1, opportunity.ItemId);
            Assert.Equal(targetStep.TotalCost - opportunity.CraftCost, opportunity.DeltaCost);
            Assert.True(opportunity.DeltaCost > 0);
            Assert.Equal("Weaponsmith", Assert.Single(opportunity.Disciplines));
            Assert.Equal(400, opportunity.MinRating);
        }

        // End-to-end coverage of RecipeSheetSavingsCalculator's production
        // wiring through _offersForRecipeSheetItem (see KNOWN-ISSUES #49,
        // item 3), a Func computed once in the pipeline constructor:
        // nulling that assignment leaves every other test green while
        // silently disabling recipe-sheet-savings notes in production, so
        // this test asserts a NON-EMPTY note via a real temp-directory
        // VendorOfferStore, unlike the guard-only tests above.
        [Fact]
        public async Task GenerateStructuredAsync_RecipeSheetSavings_EndToEnd_PopulatesOpportunity()
        {
            // Same tree as WeaponsmithGatedTree, but the recipe is
            // LearnedFromItem - the flag that makes a purchasable sheet exist
            // at all.
            var builder = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
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
                    Flags = new List<string> { "LearnedFromItem" },
                })
                .WithPrice(1, buyUnitPrice: 5000, sellUnitPrice: 10000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png");

            // Recipe 10 deliberately left unlearned (no AddLearnedRecipe
            // call) - the "missing, purchasable recipe sheet" case this
            // whole feature exists for.
            var accountClient = new InMemoryAccountRecipeClient();

            CraftingPlanResult result;
            using (var tmp = new TempDirectory())
            {
                var loader = new VendorOfferLoader();
                var store = new VendorOfferStore(tmp.Path, loader);
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "recipe-sheet-10",
                        OutputItemId = 500,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 200 },
                        },
                        MerchantName = "Test NPC",
                    },
                });

                var pipeline = builder
                    .WithVendorOfferStore(store)
                    .WithAccountRecipeClient(accountClient)
                    .WithRecipeSheetItemIds(new Dictionary<int, int> { { 10, 500 } })
                    .Build();

                var snapshot = new AccountSnapshot
                {
                    CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                    {
                        // Untrained relative to the recipe's MinRating 400 -
                        // craft is excluded from the automatic pick even
                        // though far cheaper than the TP buy, so the plan
                        // buys instead and CraftingTreeBuilder attaches an
                        // automatic reference branch for the excluded craft.
                        new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Weaponsmith", Rating = 100, Active = true },
                    },
                };

                result = await pipeline.GenerateStructuredAsync(1, 1, snapshot, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
            }

            var targetStep = Assert.Single(result.Plan.Steps, s => s.ItemId == 1);
            Assert.Equal(AcquisitionSource.BuyFromTp, targetStep.Source);
            Assert.True(result.CraftingTree.IsReferenceBranch);

            Assert.NotNull(result.RecipeSheetSavingsOpportunities);
            var opp = Assert.Single(result.RecipeSheetSavingsOpportunities);
            Assert.Equal(1, opp.ItemId);
            Assert.Equal(10, opp.RecipeId);
            Assert.Equal(500, opp.SheetItemId);
            Assert.Equal(200, opp.SheetCost);
            Assert.True(opp.SavingsPerUnit > 0);
            // The fixture's own untrained-Weaponsmith
            // snapshot (already required above to force the Buy baseline)
            // also drives DisciplineBlocked - pin it too rather than
            // leaving that half of the fixture's effect unasserted.
            Assert.True(opp.DisciplineBlocked);
            Assert.Equal("Weaponsmith", opp.Discipline);
            Assert.Equal(400, opp.RequiredRating);
        }

        [Fact]
        public async Task GenerateStructuredAsync_NullSnapshot_CharacterDisciplinesIsNull()
        {
            // No recipe for item 1 - simplest possible leaf-only plan.
            var pipeline = PipelineBuilder.Create()
                .WithPrice(1, buyUnitPrice: 50, sellUnitPrice: 500)
                .WithItem(1, "Copper Ore", "copper.png")
                .Build();

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.Null(result.CharacterDisciplines);
            Assert.Null(result.SolveContext.CharacterDisciplines);
        }

        [Fact]
        public async Task GenerateStructuredMultiAsync_WithCharacterDisciplines_CarriesIntoResultAndContext()
        {
            var pipeline = PipelineBuilder.TwoRootTree().Build();

            var snapshot = new AccountSnapshot
            {
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Bob", Discipline = "Chef", Rating = 300, Active = false },
                },
            };

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 1 },
                new PlanRequestItem { ItemId = 2, Quantity = 1 },
            };

            var result = await pipeline.GenerateStructuredAsync(items, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.NotNull(result.CharacterDisciplines);
            Assert.Single(result.CharacterDisciplines);
            Assert.Equal("Bob", result.CharacterDisciplines[0].CharacterName);
            Assert.NotNull(result.SolveContext);
            Assert.Same(result.CharacterDisciplines, result.SolveContext.CharacterDisciplines);
        }

        // --- Regression: the explicit
        // characterDisciplines argument (see GenerateStructuredAsync's own
        // doc comment on that parameter) must feed PlanResultBuilder.Build's
        // discipline tiebreak on the list overload's SINGLE-item
        // short-circuit exactly like a non-null snapshot would - this is
        // the precise call shape Module.cs's useOwn:false branch uses
        // (snapshot: null, characterDisciplines: the real account list) to
        // keep the Required Disciplines row from silently reporting a
        // discipline the account doesn't have (and then rewriting itself on
        // the very next local override re-solve, once SolveContext started
        // carrying the real list forward). ---
        [Fact]
        public async Task GenerateStructuredAsync_ListOverload_NullSnapshotWithExplicitCharacterDisciplines_TiebreakPrefersAccountDiscipline()
        {
            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                    // No single craft step elsewhere in the plan to seed a Pass
                    // 1 preference - matches PlanResultBuilderTests.
                    // RequiredDisciplines_MultiDisciplineRecipe_PrefersAccountDiscipline's
                    // own setup, so a bare alphabetical fallback would report
                    // "Armorsmith" here if the tiebreak never saw account data.
                    Disciplines = new List<string> { "Armorsmith", "Leatherworker", "Tailor" },
                    MinRating = 450,
                })
                .WithPrice(1, buyUnitPrice: 5000, sellUnitPrice: 10000)
                .WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Target", "t.png")
                .WithItem(2, "Ingredient", "i.png")
                .Build();

            var accountDisciplines = new List<SnapshotCharacterDiscipline>
            {
                new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Tailor", Rating = 500, Active = true },
            };

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 1 },
            };

            // snapshot: null (as Module.cs passes when "Use Own Materials"
            // is off) but characterDisciplines explicitly supplied - the
            // exact shape of the bug this test guards against.
            var result = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy,
                characterDisciplines: accountDisciplines);

            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Tailor", result.RequiredDisciplines[0].Discipline);
            Assert.Same(accountDisciplines, result.CharacterDisciplines);
            Assert.Same(accountDisciplines, result.SolveContext.CharacterDisciplines);

            // The bug this guards against: a local override re-solve used
            // to see a DIFFERENT (newly non-null) CharacterDisciplines than
            // the initial Build() call did, silently changing the reported
            // discipline. Since SolveContext already carries the correct
            // list from generation time, a no-op re-solve must report the
            // identical discipline, not "discover" Tailor for the first
            // time here.
            var resolved = pipeline.ResolveWithOverrides(result.SolveContext, null);
            Assert.Single(resolved.RequiredDisciplines);
            Assert.Equal("Tailor", resolved.RequiredDisciplines[0].Discipline);
        }

        [Fact]
        public async Task GenerateStructuredMultiAsync_NullSnapshotWithExplicitCharacterDisciplines_TiebreakPrefersAccountDiscipline()
        {
            var pipeline = PipelineBuilder.Create()
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
                    Disciplines = new List<string> { "Armorsmith", "Leatherworker", "Tailor" },
                    MinRating = 450,
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
                .WithItem(4, "Ingredient B", "ingredientb.png")
                .Build();

            var accountDisciplines = new List<SnapshotCharacterDiscipline>
            {
                new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Tailor", Rating = 500, Active = true },
            };

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 1 },
                new PlanRequestItem { ItemId = 2, Quantity = 1 },
            };

            var result = await pipeline.GenerateStructuredAsync(
                items, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy,
                characterDisciplines: accountDisciplines);

            Assert.Contains(result.RequiredDisciplines, d => d.Discipline == "Tailor");
            Assert.DoesNotContain(result.RequiredDisciplines, d => d.Discipline == "Armorsmith" || d.Discipline == "Leatherworker");
            Assert.Same(accountDisciplines, result.CharacterDisciplines);
            Assert.Same(accountDisciplines, result.SolveContext.CharacterDisciplines);
        }

        [Fact]
        public async Task GenerateStructuredAsync_OwnedIntermediate_RemovesCraftStep_And_Discipline()
        {
            // Item 1 -> recipe 10 (Weaponsmith 500) -> item 2
            // Item 2 -> recipe 20 (Armorsmith 400) -> item 3
            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 500,
                    Flags = new List<string> { "AutoLearned" },
                })
                .WithSearchResult(2, 20)
                .WithRecipe(new RawRecipe
                {
                    Id = 20,
                    OutputItemId = 2,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 3, Count = 2 },
                    },
                    Disciplines = new List<string> { "Armorsmith" },
                    MinRating = 400,
                    Flags = new List<string> { "AutoLearned" },
                })
                .WithPrice(1, buyUnitPrice: 50000, sellUnitPrice: 100000)
                .WithPrice(2, buyUnitPrice: 10000, sellUnitPrice: 50000)
                .WithPrice(3, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Final", "f.png")
                .WithItem(2, "Intermediate", "m.png")
                .WithItem(3, "Raw Mat", "r.png")
                .WithInventoryReducer()
                .Build();

            // Own item 2 - the intermediate craftable
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 2, Count = 1, Source = AccountItemIndex.SourceMaterialStorage },
                },
            };

            var result = await pipeline.GenerateStructuredAsync(1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // Item 2's Craft step (recipe 20) should be gone
            Assert.DoesNotContain(result.Plan.Steps,
                s => s.RecipeId == 20 && s.Source == AcquisitionSource.Craft);

            // Item 3's buy step should also be gone (no longer needed)
            Assert.DoesNotContain(result.Plan.Steps, s => s.ItemId == 3);

            // Armorsmith discipline should NOT be required (recipe 20 pruned)
            Assert.DoesNotContain(result.RequiredDisciplines,
                d => d.Discipline == "Armorsmith");

            // Recipe 20 should NOT be in required recipes
            Assert.DoesNotContain(result.RequiredRecipes, r => r.RecipeId == 20);

            // Weaponsmith discipline SHOULD still be required (recipe 10 still needed)
            Assert.Contains(result.RequiredDisciplines,
                d => d.Discipline == "Weaponsmith");

            // Recipe 10 SHOULD still be in required recipes
            Assert.Contains(result.RequiredRecipes, r => r.RecipeId == 10);

            // UsedMaterials should report item 2 consumed
            Assert.Contains(result.UsedMaterials,
                u => u.ItemId == 2 && u.QuantityUsed == 1);
        }

        [Fact]
        public async Task GenerateStructuredAsync_UsedMaterialIds_HaveMetadata()
        {
            // Item 1 -> recipe 10 -> item 2 (intermediate) -> recipe 20 -> item 3
            var pipeline = PipelineBuilder.Create()
                .WithSearchResult(1, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 500,
                })
                .WithSearchResult(2, 20)
                .WithRecipe(new RawRecipe
                {
                    Id = 20,
                    OutputItemId = 2,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 3, Count = 2 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                })
                .WithPrice(1, buyUnitPrice: 50000, sellUnitPrice: 100000)
                .WithPrice(2, buyUnitPrice: 10000, sellUnitPrice: 50000)
                .WithPrice(3, buyUnitPrice: 10, sellUnitPrice: 100)
                .WithItem(1, "Final", "f.png")
                .WithItem(2, "Intermediate", "m.png")
                .WithItem(3, "Raw Mat", "r.png")
                .WithInventoryReducer()
                .Build();

            // Own the intermediate item 2 - it gets pruned from steps but
            // should still have metadata for display in UsedMaterials section
            var snapshot = new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 2, Count = 1, Source = AccountItemIndex.SourceMaterialStorage },
                },
            };

            var result = await pipeline.GenerateStructuredAsync(1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            // UsedMaterials includes item 2
            Assert.Contains(result.UsedMaterials, u => u.ItemId == 2);

            // Item 2 should have metadata even though it's not in plan steps
            Assert.True(result.ItemMetadata.ContainsKey(2),
                "UsedMaterial item ID should have metadata populated");
            Assert.Equal("Intermediate", result.ItemMetadata[2].Name);
        }

        [Fact]
        public async Task GenerateStructuredAsync_DebugLogContainsTimingEntries()
        {
            var pipeline = PipelineBuilder.PricedRecipeTreeWithoutDiscipline().Build();

            var result = await pipeline.GenerateStructuredAsync(1, 1, null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);

            Assert.NotNull(result.DebugLog);

            // These 6 phase prefixes are shared with (were originally pinned
            // against) the now-deleted GenerateAsync and must still appear
            // with timing (the dead "Resolve vendor offers" step
            // was removed along with the always-null VendorOfferResolver
            // seam); GenerateStructuredAsync's own additional phases
            // (Inventory reduction, Fetch currency metadata, Fetch learned
            // recipes, Build result) are a superset and not asserted here.
            var expectedPrefixes = new[]
            {
                "Build recipe tree",
                "Collect item IDs",
                "Fetch TP prices",
                "Query vendor offers",
                "Solve",
                "Fetch item metadata",
            };

            var timingPattern = new Regex(@"\d+ms");

            foreach (var prefix in expectedPrefixes)
            {
                var match = result.DebugLog.FirstOrDefault(
                    line => line.StartsWith(prefix) && timingPattern.IsMatch(line));
                Assert.True(match != null,
                    $"DebugLog missing timing entry for phase '{prefix}'. "
                    + $"Entries: [{string.Join(", ", result.DebugLog)}]");
            }

            // Timing summary block must be present
            Assert.Contains(result.DebugLog,
                line => line == "--- Timing Summary ---");
        }

        [Fact]
        public async Task GenerateStructuredAsync_ReportsProgressForEachPhase()
        {
            var pipeline = PipelineBuilder.BuildOwnMaterialsPipeline(out var priceApi, ingredientCount: 3);
            priceApi.AddPrice(1, buyUnitPrice: 5000, sellUnitPrice: 10000);
            priceApi.AddPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            var progress = new CapturingProgress<PlanStatus>();

            await pipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None, progress,
                priceBasis: PriceBasis.InstantBuy);

            // All 9 expected phase messages in pipeline order
            // (the dead "Resolving vendor offers..." message was
            // removed along with the always-null VendorOfferResolver seam)
            var expectedSubstrings = new[]
            {
                "recipe tree",
                "Collecting item IDs",
                "Fetching prices",
                "Looking up vendor offers",
                "Reducing inventory",
                "Solving crafting plan",
                "Fetching item details",
                "Checking learned recipes",
                "Building final result",
            };

            Assert.True(progress.Reports.Count >= expectedSubstrings.Length,
                $"Expected >= {expectedSubstrings.Length} progress reports, "
                + $"got {progress.Reports.Count}: "
                + $"[{string.Join(", ", progress.Reports.Select(r => r.Message))}]");

            // Verify each expected substring appears in order
            int searchFrom = 0;
            foreach (var expected in expectedSubstrings)
            {
                int found = -1;
                for (int i = searchFrom; i < progress.Reports.Count; i++)
                {
                    if (progress.Reports[i].Message != null
                        && progress.Reports[i].Message.Contains(expected))
                    {
                        found = i;
                        break;
                    }
                }

                Assert.True(found >= 0,
                    $"Progress message containing '{expected}' not found at or after index {searchFrom}. "
                    + $"Reports: [{string.Join(", ", progress.Reports.Select(r => r.Message))}]");
                searchFrom = found + 1;
            }
        }
    }
}
