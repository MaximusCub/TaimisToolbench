using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;

namespace TaimisToolbench.Tests.Services
{
    public class PlanResultBuilderTests
    {
        private readonly PlanResultBuilder _builder = new PlanResultBuilder();

        /// <summary>
        /// Helper: build a minimal tree with one recipe option on the root.
        /// </summary>
        private static RecipeNode TreeWithCraftStep(
            int itemId, int recipeId, int outputCount,
            List<string> disciplines, int minRating, List<string> flags,
            params RecipeNode[] ingredients)
        {
            return TreeWithCraftStep(
                itemId, recipeId, outputCount, disciplines, minRating, flags,
                expectedOutputCount: null, ingredients);
        }

        // Gambling-forge scope:
        // expectedOutputCount overload - every pre-existing call site above
        // routes through the 6-arg overload, which passes null (preserving
        // the exact prior behavior: RecipeOption.ExpectedOutputCount left
        // at its C# default 0.0). Only the forge-scope test below needs a
        // real ExpectedOutputCount < OutputCount to exercise
        // PlanResultBuilder's Mystic-Clover-style detection.
        private static RecipeNode TreeWithCraftStep(
            int itemId, int recipeId, int outputCount,
            List<string> disciplines, int minRating, List<string> flags,
            double? expectedOutputCount, params RecipeNode[] ingredients)
        {
            var option = new RecipeOption
            {
                RecipeId = recipeId,
                OutputCount = outputCount,
                CraftsNeeded = 1,
                Disciplines = disciplines ?? new List<string>(),
                MinRating = minRating,
                Flags = flags ?? new List<string>(),
                ExpectedOutputCount = expectedOutputCount ?? 0,
            };

            foreach (var ing in ingredients)
            {
                option.Ingredients.Add(ing);
            }

            return new RecipeNode
            {
                Id = itemId,
                IngredientType = "Item",
                Quantity = 1,
                Recipes = new List<RecipeOption> { option },
            };
        }

        // Leaf comes from Helpers/RecipeNodeBuilders.cs.
        [Fact]
        public void RequiredDisciplines_FromCraftSteps_HighestRatingWins()
        {
            // Two craft steps for the same discipline with different ratings
            var leaf1 = Leaf(2, 1);
            var leaf2 = Leaf(3, 1);
            var innerNode = TreeWithCraftStep(
                3, 20, 1,
                new List<string> { "Weaponsmith" }, 400, new List<string> { "AutoLearned" },
                Leaf(4, 1));

            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 500, new List<string> { "AutoLearned" },
                leaf1, innerNode);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 20 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Weaponsmith", result.RequiredDisciplines[0].Discipline);
            Assert.Equal(500, result.RequiredDisciplines[0].MinRating);
        }

        [Fact]
        public void RequiredDisciplines_ExcludesNonCraftSteps()
        {
            // BuyFromTp step for item 2 - its discipline should NOT appear
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Armorsmith" }, 300, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.BuyFromTp },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            // Only Armorsmith from the Craft step
            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Armorsmith", result.RequiredDisciplines[0].Discipline);
        }

        [Fact]
        public void RequiredDisciplines_MultiDisciplineRecipe_SelectsExactlyOne()
        {
            // Recipe craftable by four disciplines.
            // Algorithm must select exactly one from the recipe's allowed set.
            var allowed = new HashSet<string> { "Weaponsmith", "Armorsmith", "Huntsman", "Artificer" };
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string>(allowed),
                500, new List<string> { "AutoLearned" },
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.RequiredDisciplines);
            Assert.Contains(result.RequiredDisciplines[0].Discipline, allowed);
            Assert.Equal(500, result.RequiredDisciplines[0].MinRating);
        }

        // The greedy set-cover tiebreak used to
        // fall straight to alphabetical order whenever no Pass 1/pre-cover
        // discipline had already been selected - see PlanResultBuilder.
        // Build's accountDisciplineNames doc comment. A recipe craftable by
        // Armorsmith/Leatherworker/Tailor with no other craft step in the
        // plan used to always report "Armorsmith" (alpha-first) regardless
        // of what the account actually has, misleadingly reading as "you
        // must level Armorsmith" even for a player who already has Tailor.
        [Fact]
        public void RequiredDisciplines_MultiDisciplineRecipe_PrefersAccountDiscipline()
        {
            var allowed = new HashSet<string> { "Armorsmith", "Leatherworker", "Tailor" };
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string>(allowed),
                450, new List<string> { "AutoLearned" },
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var characterDisciplines = new List<SnapshotCharacterDiscipline>
            {
                new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Tailor", Rating = 500, Active = true },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null, characterDisciplines);

            // Tailor is the only allowed discipline the account has - it
            // must win over the alphabetically-earlier Armorsmith/
            // Leatherworker even though all three cover the recipe equally.
            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Tailor", result.RequiredDisciplines[0].Discipline);
            Assert.Equal(450, result.RequiredDisciplines[0].MinRating);
        }

        // Companion to the test above: when characterDisciplines is null
        // (no snapshot data at all - the pre-existing default for every
        // other test in this file), the tiebreak must still fall back to
        // the plain alphabetical order rather than throwing or behaving
        // unpredictably - accountDisciplineNames is empty in that case, so
        // every candidate ties at 0 and alpha decides.
        [Fact]
        public void RequiredDisciplines_MultiDisciplineRecipe_NoCharacterData_FallsBackToAlpha()
        {
            var allowed = new HashSet<string> { "Armorsmith", "Leatherworker", "Tailor" };
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string>(allowed),
                450, new List<string> { "AutoLearned" },
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null, characterDisciplines: null);

            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Armorsmith", result.RequiredDisciplines[0].Discipline);
        }

        [Fact]
        public void RequiredDisciplines_MultiDisciplineRecipe_PrefersAlreadySelected()
        {
            // Inner recipe is single-discipline Weaponsmith (must-use).
            // Outer recipe is multi-discipline including Weaponsmith - should reuse it.
            var innerNode = TreeWithCraftStep(
                3, 20, 1,
                new List<string> { "Weaponsmith" }, 400, new List<string> { "AutoLearned" },
                Leaf(4, 1));

            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Armorsmith", "Weaponsmith" }, 500, new List<string> { "AutoLearned" },
                Leaf(2, 1), innerNode);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 20 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            // Only Weaponsmith needed - reused for the multi-discipline recipe
            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Weaponsmith", result.RequiredDisciplines[0].Discipline);
            Assert.Equal(500, result.RequiredDisciplines[0].MinRating);
        }

        [Fact]
        public void RequiredDisciplines_TwoMultiDisciplineRecipes_NoOverlap_SelectsTwoDisciplines()
        {
            // Two multi-discipline recipes with disjoint discipline sets.
            // Must select one discipline from each recipe's set.
            var setA = new HashSet<string> { "Armorsmith", "Weaponsmith" };
            var setB = new HashSet<string> { "Leatherworker", "Tailor" };
            var innerNode = TreeWithCraftStep(
                3, 20, 1,
                new List<string>(setB), 300, new List<string>(),
                Leaf(4, 1));

            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string>(setA), 500, new List<string>(),
                Leaf(2, 1), innerNode);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 20 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Equal(2, result.RequiredDisciplines.Count);
            var names = result.RequiredDisciplines.Select(d => d.Discipline).ToList();
            Assert.Single(names, n => setA.Contains(n));
            Assert.Single(names, n => setB.Contains(n));
        }

        [Fact]
        public void RequiredDisciplines_OverlappingMultiDiscipline_GreedyCoverSelectsShared()
        {
            // Recipe A: {Armorsmith, Weaponsmith}, Recipe B: {Weaponsmith, Leatherworker}.
            // Weaponsmith appears in both - greedy cover picks it alone.
            var innerNode = TreeWithCraftStep(
                3, 20, 1,
                new List<string> { "Weaponsmith", "Leatherworker" }, 400, new List<string>(),
                Leaf(4, 1));

            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Armorsmith", "Weaponsmith" }, 500, new List<string>(),
                Leaf(2, 1), innerNode);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 20 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            // Greedy cover: Weaponsmith covers both, max rating = 500
            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Weaponsmith", result.RequiredDisciplines[0].Discipline);
            Assert.Equal(500, result.RequiredDisciplines[0].MinRating);
        }

        [Fact]
        public void RequiredDisciplines_MultiDiscipline_PreCoveredByPass1_MaxRating()
        {
            // Step 1: single-discipline Weaponsmith at 300 (Pass 1 must-use).
            // Step 2: multi-discipline {Weaponsmith, Armorsmith, Huntsman} at 500.
            // Pre-cover should recognize Step 2 is already covered by Weaponsmith
            // and bump Weaponsmith's MinRating to 500 without adding new disciplines.
            var innerNode = TreeWithCraftStep(
                3, 20, 1,
                new List<string> { "Weaponsmith", "Armorsmith", "Huntsman" }, 500,
                new List<string> { "AutoLearned" },
                Leaf(4, 1));

            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 300,
                new List<string> { "AutoLearned" },
                Leaf(2, 1), innerNode);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 20 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            // Only Weaponsmith selected; max rating is 500 from the multi-discipline recipe
            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Weaponsmith", result.RequiredDisciplines[0].Discipline);
            Assert.Equal(500, result.RequiredDisciplines[0].MinRating);
        }

        [Fact]
        public void RequiredDisciplines_PreCover_MultiplePass1Overlap_OnlyHighestRatedBumped()
        {
            // Pass 1 selects Weaponsmith (400) and Armorsmith (300).
            // Multi-disc recipe {Weaponsmith, Armorsmith} at 500 is coverable by either.
            // Pre-cover should pick Weaponsmith (highest existing rating) and bump only
            // Weaponsmith to 500. Armorsmith must stay at 300.
            var node3 = TreeWithCraftStep(
                5, 30, 1,
                new List<string> { "Weaponsmith", "Armorsmith" }, 500, new List<string>(),
                Leaf(6, 1));

            var node2 = TreeWithCraftStep(
                3, 20, 1,
                new List<string> { "Armorsmith" }, 300, new List<string>(),
                Leaf(4, 1));

            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 400, new List<string>(),
                Leaf(2, 1), node2, node3);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 20 },
                    new PlanStep { ItemId = 5, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 30 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Equal(2, result.RequiredDisciplines.Count);
            var ws = result.RequiredDisciplines.First(d => d.Discipline == "Weaponsmith");
            var arm = result.RequiredDisciplines.First(d => d.Discipline == "Armorsmith");
            Assert.Equal(500, ws.MinRating);
            Assert.Equal(300, arm.MinRating);
        }

        [Fact]
        public void RequiredDisciplines_ExcludesBuyFromVendorSteps()
        {
            // Craft step for item 1, BuyFromVendor step for item 3
            var innerNode = TreeWithCraftStep(
                3, 20, 1,
                new List<string> { "Leatherworker" }, 400, new List<string>(),
                Leaf(4, 1));

            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 500, new List<string>(),
                Leaf(2, 1), innerNode);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.BuyFromVendor },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            // Only Weaponsmith from the Craft step; Leatherworker from BuyFromVendor excluded
            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Weaponsmith", result.RequiredDisciplines[0].Discipline);
        }

        [Fact]
        public void RequiredDisciplines_ExcludesCurrencySteps()
        {
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 500, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 5, Quantity = 10, Source = AcquisitionSource.Currency },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.RequiredDisciplines);
            Assert.Equal("Weaponsmith", result.RequiredDisciplines[0].Discipline);
        }

        [Fact]
        public void RequiredDisciplines_MysticForgeNoDisciplines_EmptyList()
        {
            // Mystic Forge recipe (negative ID) with no disciplines
            var tree = TreeWithCraftStep(
                1, -100, 1,
                new List<string>(), 0, new List<string>(),
                Leaf(2, 1), Leaf(3, 1), Leaf(4, 1), Leaf(5, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = -100 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Empty(result.RequiredDisciplines);
        }

        [Fact]
        public void RequiredDisciplines_RealMysticForgeDiscipline_ExcludedAsFacility()
        {
            // Confirmed in game (user-approved, supersedes the
            // test this replaces): real production Mystic Forge recipes
            // always carry Disciplines = ["MysticForge"]
            // (MysticForgeRecipeData.Load sets this unconditionally),
            // unlike the empty-Disciplines test fixture above. The forge is
            // a facility, not a player-levelable discipline, so
            // NonCraftingDisciplines now excludes it here too (joining
            // Achievement/Merchant) - RequiredRecipes (the forge is still a
            // real, always-available crafting step) is unaffected.
            var tree = TreeWithCraftStep(
                1, -100, 1,
                new List<string> { "MysticForge" }, 0, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = -100 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Empty(result.RequiredDisciplines);
            Assert.Single(result.RequiredRecipes);
            Assert.False(result.RequiredRecipes[0].IsMissing);
        }

        [Fact]
        public void ProbabilisticForgeOutputItemIds_MysticCloverStyleYield_PopulatesOutputItemId()
        {
            // A MysticForge recipe whose ExpectedOutputCount (2.5) is below
            // OutputCount (3) is the documented Mystic-Clover-style
            // fractional-yield signal - see RecipeOption.ExpectedOutputCount's
            // own doc comment.
            var tree = TreeWithCraftStep(
                1, -100, outputCount: 3,
                disciplines: new List<string> { "MysticForge" }, minRating: 0, flags: new List<string>(),
                expectedOutputCount: 2.5, Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = -100 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.ProbabilisticForgeOutputItemIds);
            Assert.Equal(1, result.ProbabilisticForgeOutputItemIds[0]);
        }

        [Fact]
        public void ProbabilisticForgeOutputItemIds_WholeNumberYield_NotFlagged()
        {
            // ExpectedOutputCount == OutputCount (the common, non-fractional
            // case - most MysticForge recipes) must NOT be flagged.
            var tree = TreeWithCraftStep(
                1, -100, outputCount: 3,
                disciplines: new List<string> { "MysticForge" }, minRating: 0, flags: new List<string>(),
                expectedOutputCount: 3, Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = -100 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Empty(result.ProbabilisticForgeOutputItemIds);
        }

        [Fact]
        public void ProbabilisticForgeOutputItemIds_NonMysticForgeCraftStep_NotFlagged()
        {
            // A regular crafting-discipline recipe with a fractional
            // ExpectedOutputCount (should not happen in real data, but the
            // detection must be gated on MysticForge membership, not on
            // ExpectedOutputCount alone).
            var tree = TreeWithCraftStep(
                1, 10, outputCount: 3,
                disciplines: new List<string> { "Weaponsmith" }, minRating: 400, flags: new List<string>(),
                expectedOutputCount: 2, Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Empty(result.ProbabilisticForgeOutputItemIds);
        }

        [Fact]
        public void RequiredDisciplines_AchievementOrMerchantDiscipline_ExcludedFromList()
        {
            // "Achievement"/
            // "Merchant" are gw2e-borrowed informational source tags on the
            // new achievement-bit seed recipes, not real, player-levelable
            // GW2 crafting disciplines - they must never appear in Required
            // Disciplines (which the player reads as "disciplines to
            // unlock/level for this plan").
            var tree = TreeWithCraftStep(
                1, -1592, 1,
                new List<string> { "Achievement" }, 0, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = -1592 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Empty(result.RequiredDisciplines);
            Assert.Single(result.RequiredRecipes);
            Assert.Contains("Achievement", result.RequiredRecipes[0].Disciplines);
        }

        [Fact]
        public void RequiredRecipes_AchievementRecipe_IsMissingFalse_NotFlaggedAsUnlockable()
        {
            // An achievement-sourced recipe (negative id,
            // adjacent to but distinct from the Mystic Forge id range) is
            // inherently available - no "learn this recipe" concept
            // applies - exactly like a real Mystic Forge recipe, even
            // though the underlying check is no longer a bare
            // "recipeId < 0" sign check (see
            // RequiredRecipesVisibility.IsUnlockFree).
            var tree = TreeWithCraftStep(
                1, -1592, 1,
                new List<string> { "Achievement" }, 0, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = -1592 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var learnedRecipeIds = new HashSet<int>(); // player has learned nothing
            var result = _builder.Build(plan, tree, metadata, null, learnedRecipeIds);

            Assert.Single(result.RequiredRecipes);
            Assert.False(result.RequiredRecipes[0].IsMissing);
        }

        [Fact]
        public void RequiredDisciplines_NoCraftSteps_EmptyList()
        {
            var tree = Leaf(1, 5);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 5,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 5, Source = AcquisitionSource.BuyFromTp },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Empty(result.RequiredDisciplines);
        }

        [Fact]
        public void RequiredRecipes_AutoLearnedFlag()
        {
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 400,
                new List<string> { "AutoLearned" },
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.RequiredRecipes);
            Assert.True(result.RequiredRecipes[0].IsAutoLearned);
        }

        // Same Flags-membership
        // pattern as RequiredRecipes_AutoLearnedFlag above.
        [Fact]
        public void RequiredRecipes_LearnedFromItemFlag()
        {
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 400,
                new List<string> { "LearnedFromItem" },
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.RequiredRecipes);
            Assert.True(result.RequiredRecipes[0].IsLearnedFromItem);
        }

        [Fact]
        public void RequiredRecipes_NoLearnedFromItemFlag_DefaultsFalse()
        {
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 400,
                new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.RequiredRecipes);
            Assert.False(result.RequiredRecipes[0].IsLearnedFromItem);
        }

        [Fact]
        public void RequiredRecipes_MissingFlag_WithLearnedSet()
        {
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 400, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            // Learned set does NOT contain recipe 10
            var learnedIds = new HashSet<int> { 99 };
            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, learnedIds);

            Assert.Single(result.RequiredRecipes);
            Assert.True(result.RequiredRecipes[0].IsMissing);
        }

        [Fact]
        public void RequiredRecipes_LearnedFlag_WithLearnedSet()
        {
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 400, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            // Learned set CONTAINS recipe 10
            var learnedIds = new HashSet<int> { 10 };
            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, learnedIds);

            Assert.Single(result.RequiredRecipes);
            Assert.False(result.RequiredRecipes[0].IsMissing);
        }

        [Fact]
        public void RequiredRecipes_NullLearnedSet_MissingIsNull()
        {
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 400, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.RequiredRecipes);
            Assert.Null(result.RequiredRecipes[0].IsMissing);
        }

        [Fact]
        public void RequiredRecipes_DeduplicatedByRecipeId()
        {
            // Two steps reference the same recipe ID
            var tree = TreeWithCraftStep(
                1, 10, 1,
                new List<string> { "Weaponsmith" }, 400, new List<string>(),
                Leaf(2, 1));

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            Assert.Single(result.RequiredRecipes);
            Assert.Equal(10, result.RequiredRecipes[0].RecipeId);
        }

        [Fact]
        public void UsedMaterials_PassedThrough()
        {
            var tree = Leaf(1, 5);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 5,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 2, Source = AcquisitionSource.BuyFromTp },
                },
            };

            var usedMaterials = new List<UsedMaterial>
            {
                new UsedMaterial { ItemId = 1, QuantityUsed = 3 },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, usedMaterials, null);

            Assert.Single(result.UsedMaterials);
            Assert.Equal(1, result.UsedMaterials[0].ItemId);
            Assert.Equal(3, result.UsedMaterials[0].QuantityUsed);
        }

        /// <summary>
        /// Helper for the duplicate-RecipeId regression tests below:
        /// builds a root whose single option has two ingredient branches, each
        /// carrying its own RecipeOption with the SAME RecipeId but different
        /// Disciplines/MinRating (deliberately unrealistic - real GW2 recipe
        /// data would never disagree with itself - but that is exactly what
        /// makes the tree-position winner observable in a test).
        /// </summary>
        private static RecipeNode TreeWithDuplicateRecipeId(
            RecipeNode firstBranch, RecipeNode secondBranch)
        {
            var rootOption = new RecipeOption
            {
                RecipeId = 1,
                OutputCount = 1,
                CraftsNeeded = 1,
                Disciplines = new List<string> { "Tailor" },
                MinRating = 100,
                Flags = new List<string>(),
            };
            rootOption.Ingredients.Add(firstBranch);
            rootOption.Ingredients.Add(secondBranch);

            return new RecipeNode
            {
                Id = 1,
                IngredientType = "Item",
                Quantity = 1,
                Recipes = new List<RecipeOption> { rootOption },
            };
        }

        private static RecipeNode DuplicateRecipeBranch(
            int nodeId, string discipline, int minRating)
        {
            var option = new RecipeOption
            {
                RecipeId = 99,
                OutputCount = 1,
                CraftsNeeded = 1,
                Disciplines = new List<string> { discipline },
                MinRating = minRating,
                Flags = new List<string>(),
            };

            return new RecipeNode
            {
                Id = nodeId,
                IngredientType = "Item",
                Quantity = 1,
                Recipes = new List<RecipeOption> { option },
            };
        }

        [Fact]
        public void DuplicateRecipeId_AcrossTreePositions_FirstDfsOccurrenceWins()
        {
            // Same RecipeId (99) exists at two different tree positions with
            // different Disciplines/MinRating. The old FindRecipeOption did a
            // preorder DFS over node.Recipes then each option's Ingredients
            // (fully descending into one ingredient's subtree before moving
            // to the next) and returned on first match - so with branchA
            // first in the root option's Ingredients list, branchA's
            // RecipeOption must win, exactly as it would have before the
            // single-walk-Dictionary memoization.
            var branchA = DuplicateRecipeBranch(2, "Weaponsmith", 400);
            var branchB = DuplicateRecipeBranch(3, "Armorsmith", 999);
            var tree = TreeWithDuplicateRecipeId(branchA, branchB);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 1 },
                    new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 99 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            var required = result.RequiredRecipes.Single(r => r.RecipeId == 99);
            Assert.Equal(400, required.MinRating);
            Assert.Equal(new List<string> { "Weaponsmith" }, required.Disciplines);

            Assert.Contains(result.RequiredDisciplines, d => d.Discipline == "Weaponsmith" && d.MinRating == 400);
            Assert.DoesNotContain(result.RequiredDisciplines, d => d.Discipline == "Armorsmith");
        }

        [Fact]
        public void DuplicateRecipeId_AcrossTreePositions_WinnerFollowsIngredientOrder()
        {
            // Same tree shape as above with the two branches swapped - proves
            // the winner tracks true DFS visiting order (branchB now first)
            // rather than some other tie-break (e.g. declaration/alphabetical
            // order), which would silently mask a traversal-order bug.
            var branchA = DuplicateRecipeBranch(2, "Weaponsmith", 400);
            var branchB = DuplicateRecipeBranch(3, "Armorsmith", 999);
            var tree = TreeWithDuplicateRecipeId(branchB, branchA);

            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 1 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 99 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, tree, metadata, null, null);

            var required = result.RequiredRecipes.Single(r => r.RecipeId == 99);
            Assert.Equal(999, required.MinRating);
            Assert.Equal(new List<string> { "Armorsmith" }, required.Disciplines);

            Assert.Contains(result.RequiredDisciplines, d => d.Discipline == "Armorsmith" && d.MinRating == 999);
            Assert.DoesNotContain(result.RequiredDisciplines, d => d.Discipline == "Weaponsmith");
        }

        [Fact]
        public void Build_NullTree_WithCraftStep_ThrowsLikeOldFindRecipeOption()
        {
            // BuildRecipeOptionIndex intentionally has no
            // null-tree guard. Pinning that a null treeUsedForSolve combined
            // with at least one Craft step fails loud (NullReferenceException)
            // rather than silently returning an empty index/missing recipe
            // data - exactly matching the old FindRecipeOption(node, id),
            // which dereferenced node.Recipes with no null check.
            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 1 },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();

            Assert.Throws<System.NullReferenceException>(
                () => _builder.Build(plan, null, metadata, null, null));
        }

        [Fact]
        public void Build_NullTree_WithNoCraftSteps_DoesNotThrow()
        {
            // Companion to the test above: with zero Craft steps, the old
            // FindRecipeOption was never called at all (both call sites are
            // inside `foreach (var step in craftSteps)`), so a null
            // treeUsedForSolve never threw. BuildRecipeOptionIndex must stay
            // just as lazy - only walking (and dereferencing) the tree when
            // there is actually a Craft step to resolve.
            var plan = new CraftingPlan
            {
                TargetItemId = 1,
                TargetQuantity = 1,
                Steps = new List<PlanStep>
                {
                    new PlanStep { ItemId = 1, Quantity = 1, Source = AcquisitionSource.BuyFromTp },
                },
            };

            var metadata = new Dictionary<int, ItemMetadata>();
            var result = _builder.Build(plan, null, metadata, null, null);

            Assert.Empty(result.RequiredRecipes);
            Assert.Empty(result.RequiredDisciplines);
        }
    }
}
