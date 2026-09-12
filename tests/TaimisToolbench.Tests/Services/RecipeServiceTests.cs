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
    public class RecipeServiceTests
    {
        [Fact]
        public async Task LeafNode_NoRecipe_ReturnsLeafWithQuantity()
        {
            var api = new InMemoryRecipeApiClient();
            var svc = new RecipeService(api);

            var node = await svc.BuildTreeAsync(100, 5, CancellationToken.None);

            Assert.Equal(100, node.Id);
            Assert.Equal("Item", node.IngredientType);
            Assert.Equal(5, node.Quantity);
            Assert.True(node.IsLeaf);
            Assert.Empty(node.Recipes);
        }

        [Fact]
        public async Task SingleLevelRecipe_IngredientsAreLeaves()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                    new RawIngredient { Type = "Item", Id = 3, Count = 1 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            Assert.False(node.IsLeaf);
            Assert.Single(node.Recipes);

            var option = node.Recipes[0];
            Assert.Equal(10, option.RecipeId);
            Assert.Equal(1, option.OutputCount);
            Assert.Equal(1, option.CraftsNeeded);
            Assert.Equal(2, option.Ingredients.Count);

            Assert.Equal(2, option.Ingredients[0].Id);
            Assert.Equal(3, option.Ingredients[0].Quantity);
            Assert.True(option.Ingredients[0].IsLeaf);

            Assert.Equal(3, option.Ingredients[1].Id);
            Assert.Equal(1, option.Ingredients[1].Quantity);
            Assert.True(option.Ingredients[1].IsLeaf);
        }

        [Fact]
        public async Task MultiLevelChain_ThreeLevelsDeep()
        {
            var api = new InMemoryRecipeApiClient();

            // A (item 1) -> recipe 10 -> ingredient B (item 2)
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
            });

            // B (item 2) -> recipe 20 -> ingredient C (item 3, leaf)
            api.AddSearchResult(2, 20);
            api.AddRecipe(new RawRecipe
            {
                Id = 20,
                OutputItemId = 2,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 3, Count = 2 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            // Level 1: A
            Assert.False(node.IsLeaf);
            var bNode = node.Recipes[0].Ingredients[0];

            // Level 2: B
            Assert.Equal(2, bNode.Id);
            Assert.False(bNode.IsLeaf);
            var cNode = bNode.Recipes[0].Ingredients[0];

            // Level 3: C (leaf)
            Assert.Equal(3, cNode.Id);
            Assert.Equal(2, cNode.Quantity);
            Assert.True(cNode.IsLeaf);
        }

        [Fact]
        public async Task QuantityPropagation_CeilDivision()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 2,  // makes 2 per craft
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 4 },
                },
            });

            var svc = new RecipeService(api);
            // Need 3, recipe makes 2 -> ceil(3/2) = 2 crafts
            var node = await svc.BuildTreeAsync(1, 3, CancellationToken.None);

            var option = node.Recipes[0];
            Assert.Equal(2, option.CraftsNeeded);
            // 2 crafts * 4 per craft = 8
            Assert.Equal(8, option.Ingredients[0].Quantity);
        }

        [Fact]
        public async Task MultipleRecipes_BothPresent()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10, 11);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
            });
            api.AddRecipe(new RawRecipe
            {
                Id = 11,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 3, Count = 2 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            Assert.Equal(2, node.Recipes.Count);
            Assert.Equal(10, node.Recipes[0].RecipeId);
            Assert.Equal(11, node.Recipes[1].RecipeId);
        }

        [Fact]
        public async Task CurrencyIngredient_IsLeaf()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    new RawIngredient { Type = "Currency", Id = 99, Count = 50 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            var currencyNode = node.Recipes[0].Ingredients[1];
            Assert.Equal(99, currencyNode.Id);
            Assert.Equal("Currency", currencyNode.IngredientType);
            Assert.Equal(50, currencyNode.Quantity);
            Assert.True(currencyNode.IsLeaf);
        }

        [Fact]
        public async Task NullTypedIngredient_BecomesLeaf_RecipeNeverExpanded()
        {
            // RawIngredient.Type
            // deserializes to null when a seed/overlay JSON row omits
            // "type" (System.Text.Json applies no default), so this shape
            // is reachable from real cache data even though today's seed
            // always carries a type string. BuildNodeAsync's guard must be
            // Item-positive like every other guard in this fix series
            // (PlanSolver.Evaluate/Collect/RecomputeCraftCosts,
            // CraftingTreeBuilder.BuildNode) - null must be treated as
            // NOT an item and never recurse into a recipe search, so the
            // rest of the pipeline's own Item-positive guards (which
            // already skip pricing/expanding a non-"Item" node) stay in
            // sync with what the tree actually contains instead of being
            // handed an unexpectedly-populated subtree for a node type
            // they treat as an unpriced leaf.
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    new RawIngredient { Type = null, Id = 99, Count = 5 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            var nullTypedNode = node.Recipes[0].Ingredients[1];
            Assert.Equal(99, nullTypedNode.Id);
            Assert.Null(nullTypedNode.IngredientType);
            Assert.Equal(5, nullTypedNode.Quantity);
            Assert.True(nullTypedNode.IsLeaf);
            Assert.Empty(nullTypedNode.Recipes);
        }

        [Fact]
        public async Task SelfReferentialIngredient_BecomesLeaf_QuantityDoesNotCompound()
        {
            // A real, wiki-verified
            // Mystic Forge "trophy tier-up" recipe shape - N of the tier
            // below + 1 of ITS OWN output + junk items -> a few of itself
            // (Obsidian Shard, id 19925, is the exact real example already
            // in ref/recipes_seed.json). Echoes gw2e's recipe-nesting
            // "the component is the recipe! Abort!" rule: the self-ingredient
            // becomes an inert leaf (no further recipe expansion), so its
            // quantity is a single scale-up from THIS craft only - it must
            // NOT recurse into its own recipe again and compound.
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(19925, -496);
            api.AddRecipe(new RawRecipe
            {
                Id = -496,
                OutputItemId = 19925,
                OutputItemCount = 3,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 19925, Count = 1 }, // self
                    new RawIngredient { Type = "Item", Id = 19976, Count = 1 }, // Mystic Coin
                    new RawIngredient { Type = "Item", Id = 24335, Count = 1 },
                    new RawIngredient { Type = "Item", Id = 39090, Count = 1 },
                },
            });

            var svc = new RecipeService(api);
            // Need 300 Obsidian Shards; recipe makes 3 -> ceil(300/3) = 100 crafts.
            var node = await svc.BuildTreeAsync(19925, 300, CancellationToken.None);

            Assert.False(node.IsLeaf);
            var option = node.Recipes[0];
            Assert.Equal(100, option.CraftsNeeded);

            var selfIngredient = option.Ingredients.Single(i => i.Id == 19925);
            // One-time scale-up (100 crafts * 1 per craft) - a sane,
            // wiki-scale number, NOT a recursively-compounded explosion.
            Assert.Equal(100, selfIngredient.Quantity);
            Assert.True(selfIngredient.IsLeaf, "the self-referential ingredient must not re-expand its own recipe");
            Assert.Empty(selfIngredient.Recipes);

            var coinIngredient = option.Ingredients.Single(i => i.Id == 19976);
            Assert.Equal(100, coinIngredient.Quantity);
        }

        [Fact]
        public async Task SelfReferentialMultiTierChain_SaneWikiScaleQuantities()
        {
            // Realistic 4-tier salvage-trophy chain (Small/Claw/Sharp/Large
            // Claw shape): each tier needs 50 of the tier below + 1 of ITS
            // OWN output + dust + Philosopher's Stones -> 7 of itself.
            // Verified (m5's "explosion to millions" is real wiki-scale
            // math for this brutal ratio, not a compounding bug): the
            // demand must grow by a bounded, deterministic multiplier per
            // tier (not runaway/unbounded), and every self-ingredient at
            // every tier must stay an inert, non-recursing leaf.
            var api = new InMemoryRecipeApiClient();
            const int tinyClaw = 90101, smallClaw = 90102, claw = 90103, sharpClaw = 90104, largeClaw = 90105;
            const int dustA = 90201, dustB = 90202, dustC = 90203, dustD = 90204, philStone = 90300;

            api.AddSearchResult(smallClaw, -1592);
            api.AddRecipe(new RawRecipe
            {
                Id = -1592,
                OutputItemId = smallClaw,
                OutputItemCount = 7,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = tinyClaw, Count = 50 },
                    new RawIngredient { Type = "Item", Id = smallClaw, Count = 1 },
                    new RawIngredient { Type = "Item", Id = dustA, Count = 5 },
                    new RawIngredient { Type = "Item", Id = philStone, Count = 1 },
                },
            });
            api.AddSearchResult(claw, -1593);
            api.AddRecipe(new RawRecipe
            {
                Id = -1593,
                OutputItemId = claw,
                OutputItemCount = 7,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = smallClaw, Count = 50 },
                    new RawIngredient { Type = "Item", Id = claw, Count = 1 },
                    new RawIngredient { Type = "Item", Id = dustB, Count = 5 },
                    new RawIngredient { Type = "Item", Id = philStone, Count = 2 },
                },
            });
            api.AddSearchResult(sharpClaw, -1594);
            api.AddRecipe(new RawRecipe
            {
                Id = -1594,
                OutputItemId = sharpClaw,
                OutputItemCount = 7,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = claw, Count = 50 },
                    new RawIngredient { Type = "Item", Id = sharpClaw, Count = 1 },
                    new RawIngredient { Type = "Item", Id = dustC, Count = 5 },
                    new RawIngredient { Type = "Item", Id = philStone, Count = 3 },
                },
            });
            api.AddSearchResult(largeClaw, -1595);
            api.AddRecipe(new RawRecipe
            {
                Id = -1595,
                OutputItemId = largeClaw,
                OutputItemCount = 7,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = sharpClaw, Count = 50 },
                    new RawIngredient { Type = "Item", Id = largeClaw, Count = 1 },
                    new RawIngredient { Type = "Item", Id = dustD, Count = 5 },
                    new RawIngredient { Type = "Item", Id = philStone, Count = 4 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(largeClaw, 500, CancellationToken.None);

            int LargeCraftsNeeded = node.Recipes[0].CraftsNeeded;
            Assert.Equal(72, LargeCraftsNeeded); // ceil(500/7)

            var sharpNode = node.Recipes[0].Ingredients.Single(i => i.Id == sharpClaw);
            Assert.Equal(72 * 50, sharpNode.Quantity); // 3600 - one-time scale-up, sane
            Assert.False(sharpNode.IsLeaf);

            var selfLarge = node.Recipes[0].Ingredients.Single(i => i.Id == largeClaw);
            Assert.Equal(72, selfLarge.Quantity);
            Assert.True(selfLarge.IsLeaf);

            var sharpOption = sharpNode.Recipes[0];
            var selfSharp = sharpOption.Ingredients.Single(i => i.Id == sharpClaw);
            Assert.True(selfSharp.IsLeaf);
            Assert.Equal(sharpOption.CraftsNeeded, selfSharp.Quantity);

            // Every level's demand is a deterministic, bounded multiple of
            // its parent's need (real 50-per-craft/7-out ratio compounding
            // across genuinely distinct tiers) - not an unbounded/incorrect
            // blow-up. The whole chain resolves without infinite recursion.
            var tinyNode = sharpNode.Recipes[0]
                .Ingredients.Single(i => i.Id == claw).Recipes[0]
                .Ingredients.Single(i => i.Id == smallClaw).Recipes[0]
                .Ingredients.Single(i => i.Id == tinyClaw);
            Assert.True(tinyNode.IsLeaf); // no recipe seeded for Tiny Claw
            Assert.True(tinyNode.Quantity > 0);
        }

        [Fact]
        public async Task RawRecipe_ExpectedOutputCountSet_PropagatesToRecipeOption()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(19675, -1);
            api.AddRecipe(new RawRecipe
            {
                Id = -1,
                OutputItemId = 19675,
                OutputItemCount = 1,
                ExpectedOutputCount = 0.31,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(19675, 1, CancellationToken.None);

            Assert.Equal(0.31, node.Recipes[0].ExpectedOutputCount);
        }

        [Fact]
        public async Task FractionalExpectedOutputCount_CraftsNeededAndIngredientQuantity_UseExpectedOutputCount()
        {
            // craftsNeeded (and therefore
            // every ingredient quantity scaled by it) must derive from the
            // EXPECTED output, not the nominal integer output, exactly
            // mirroring the real Mystic Clover shape (recipe -1591,
            // ExpectedOutputCount 0.31) needed 77x by Mystic Tribute.
            // ceil(77 / 0.31) = 249 forge attempts - NOT ceil(77/1)=77,
            // which would silently under-provision every raw ingredient by
            // ~1/0.31 (the exact bug this fix closes).
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(19675, -1591);
            api.AddRecipe(new RawRecipe
            {
                Id = -1591,
                OutputItemId = 19675,
                OutputItemCount = 1,
                ExpectedOutputCount = 0.31,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 19925, Count = 1 }, // Obsidian Shard
                    new RawIngredient { Type = "Item", Id = 19976, Count = 1 }, // Mystic Coin
                    new RawIngredient { Type = "Item", Id = 19721, Count = 1 }, // Glob of Ectoplasm
                    new RawIngredient { Type = "Item", Id = 20796, Count = 6 }, // Philosopher's Stone,
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(19675, 77, CancellationToken.None);

            var option = node.Recipes[0];
            Assert.Equal(1, option.OutputCount);
            Assert.Equal(0.31, option.ExpectedOutputCount);
            Assert.Equal(249, option.CraftsNeeded); // ceil(77/0.31) = 249

            Assert.Equal(249, option.Ingredients.Single(i => i.Id == 19925).Quantity);
            Assert.Equal(249, option.Ingredients.Single(i => i.Id == 19976).Quantity);
            Assert.Equal(249, option.Ingredients.Single(i => i.Id == 19721).Quantity);
            Assert.Equal(249 * 6, option.Ingredients.Single(i => i.Id == 20796).Quantity);
        }

        [Fact]
        public async Task FractionalExpectedOutputCount_AbsurdlyTinyValue_OverflowFallsBackToNominal()
        {
            // A corrupt/malicious seed could set an ExpectedOutputCount so
            // small that ceil(quantity/ExpectedOutputCount) overflows int -
            // must fall back to the nominal integer-output calculation
            // rather than crash the whole tree build.
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                ExpectedOutputCount = 1e-15,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
            });

            var svc = new RecipeService(api);

            RecipeNode node = null;
            var exception = await Record.ExceptionAsync(async () =>
            {
                node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);
            });

            Assert.Null(exception);
            // Falls back to ceil(1/1)=1, exactly the nominal-output result.
            Assert.Equal(1, node.Recipes[0].CraftsNeeded);
            Assert.Equal(1, node.Recipes[0].Ingredients[0].Quantity);
        }

        [Fact]
        public async Task RawRecipe_NoExpectedOutputCount_DefaultsToOutputCount()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 5,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
                // ExpectedOutputCount left null (the common case)
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            Assert.Equal(5.0, node.Recipes[0].ExpectedOutputCount);
        }

        [Fact]
        public async Task OutputCountGreaterThanOne_CraftsNeededRoundsUp()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 5,  // makes 5 per craft
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                },
            });

            var svc = new RecipeService(api);
            // Need 7, recipe makes 5 -> ceil(7/5) = 2 crafts
            var node = await svc.BuildTreeAsync(1, 7, CancellationToken.None);

            var option = node.Recipes[0];
            Assert.Equal(5, option.OutputCount);
            Assert.Equal(2, option.CraftsNeeded);
            // 2 crafts * 3 per craft = 6
            Assert.Equal(6, option.Ingredients[0].Quantity);
        }

        [Fact]
        public async Task RecipeOption_CarriesDisciplinesFromRawRecipe()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
                Disciplines = new List<string> { "Weaponsmith", "Huntsman" },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            var option = node.Recipes[0];
            Assert.Equal(2, option.Disciplines.Count);
            Assert.Contains("Weaponsmith", option.Disciplines);
            Assert.Contains("Huntsman", option.Disciplines);
        }

        [Fact]
        public async Task RecipeOption_CarriesMinRatingAndFlags()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
                Disciplines = new List<string> { "Armorsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            var option = node.Recipes[0];
            Assert.Equal(400, option.MinRating);
            Assert.Single(option.Flags);
            Assert.Contains("AutoLearned", option.Flags);
        }

        [Fact]
        public async Task RecipeOption_DefaultsWhenFieldsAbsent()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
                // No Disciplines, MinRating, or Flags set - use defaults
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            var option = node.Recipes[0];
            Assert.Empty(option.Disciplines);
            Assert.Equal(0, option.MinRating);
            Assert.Empty(option.Flags);
        }

        [Fact]
        public async Task RecipeOption_MissingFlags_DefaultsToNotAutoLearned()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
                Flags = new List<string>(), // empty flags,
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            var option = node.Recipes[0];
            Assert.DoesNotContain("AutoLearned", option.Flags);
        }

        // --- Achievement-bit ingredient propagation ---
        [Fact]
        public async Task AchievementFields_PropagateFromRawIngredientOntoChildNode()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(103980, -1592);
            api.AddRecipe(new RawRecipe
            {
                Id = -1592,
                OutputItemId = 103980,
                OutputItemCount = 1,
                AchievementId = 8493,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 103886, Count = 1, AchievementId = 8493, AchievementBit = 0 },
                    new RawIngredient { Type = "Item", Id = 103974, Count = 1, AchievementId = 8493, AchievementBit = 3 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(103980, 1, CancellationToken.None);

            var bit0 = node.Recipes[0].Ingredients.Single(i => i.Id == 103886);
            Assert.Equal(8493, bit0.AchievementId);
            Assert.Equal(0, bit0.AchievementBit);
            Assert.False(bit0.IsAchievementBitDeduped); // not this class's job to set

            var bit3 = node.Recipes[0].Ingredients.Single(i => i.Id == 103974);
            Assert.Equal(8493, bit3.AchievementId);
            Assert.Equal(3, bit3.AchievementBit);
        }

        [Fact]
        public async Task OrdinaryIngredient_NoAchievementFields_LeavesBothNull()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

            Assert.Null(node.AchievementId);
            Assert.Null(node.AchievementBit);
            Assert.Null(node.Recipes[0].Ingredients[0].AchievementId);
            Assert.Null(node.Recipes[0].Ingredients[0].AchievementBit);
        }

        // --- BuildMultiItemTreeAsync (gw2e parity, multi-item plans) ---
        [Fact]
        public async Task BuildMultiItemTreeAsync_SingleEntry_ReturnsItemTreeUnwrapped_NoSyntheticRoot()
        {
            var api = new InMemoryRecipeApiClient();
            api.AddSearchResult(1, 10);
            api.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                },
            });

            var svc = new RecipeService(api);
            var node = await svc.BuildMultiItemTreeAsync(
                new List<PlanRequestItem> { new PlanRequestItem { ItemId = 1, Quantity = 5 } },
                CancellationToken.None);

            // Echoes gw2e's own `if (r.length === 1) return r[0]` - the real
            // item's own tree, completely unwrapped.
            Assert.Equal(1, node.Id);
            Assert.Equal(5, node.Quantity);
            Assert.NotEqual(Gw2Constants.MultiItemWrapperItemId, node.Id);
            Assert.Single(node.Recipes);
            Assert.Equal(2, node.Recipes[0].Ingredients[0].Id);
        }

        [Fact]
        public async Task BuildMultiItemTreeAsync_MultipleItems_WrapsUnderSyntheticRoot()
        {
            var api = new InMemoryRecipeApiClient();
            // Item 1: leaf, no recipe. Item 2: leaf, no recipe.
            var svc = new RecipeService(api);

            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 3 },
                new PlanRequestItem { ItemId = 2, Quantity = 7 },
            };

            var wrapper = await svc.BuildMultiItemTreeAsync(items, CancellationToken.None);

            Assert.Equal(Gw2Constants.MultiItemWrapperItemId, wrapper.Id);
            Assert.Equal("Item", wrapper.IngredientType);
            Assert.Equal(1, wrapper.Quantity);
            Assert.Single(wrapper.Recipes);

            var wrapperRecipe = wrapper.Recipes[0];
            Assert.Equal(Gw2Constants.MultiItemWrapperRecipeId, wrapperRecipe.RecipeId);
            Assert.Equal(1, wrapperRecipe.OutputCount);
            Assert.Equal(1, wrapperRecipe.CraftsNeeded);
            Assert.Equal(2, wrapperRecipe.Ingredients.Count);

            // Each item tree is carried under the wrapper with its own
            // requested amount as its Quantity - exactly like an ordinary
            // recipe ingredient's quantity.
            Assert.Equal(1, wrapperRecipe.Ingredients[0].Id);
            Assert.Equal(3, wrapperRecipe.Ingredients[0].Quantity);
            Assert.Equal(2, wrapperRecipe.Ingredients[1].Id);
            Assert.Equal(7, wrapperRecipe.Ingredients[1].Quantity);
        }

        [Fact]
        public async Task BuildMultiItemTreeAsync_NullOrEmptyList_Throws()
        {
            var svc = new RecipeService(new InMemoryRecipeApiClient());

            await Assert.ThrowsAsync<System.ArgumentException>(
                () => svc.BuildMultiItemTreeAsync(null, CancellationToken.None));
            await Assert.ThrowsAsync<System.ArgumentException>(
                () => svc.BuildMultiItemTreeAsync(new List<PlanRequestItem>(), CancellationToken.None));
        }

        // KNOWN-ISSUES #31/api-degradation F5 :
        // Gw2RecipeApiClient.GetRecipeAsync can now return null on a 404
        // instead of throwing. A recipe id a search result points to that
        // then 404s on its own detail lookup must not crash the tree build
        // (the option is simply skipped) and must not poison the
        // persistent recipe overlay cache for every subsequent Flush -
        // real OverlayRecipeCacheStore backed by a temp directory, per the
        // repo's real-storage-testing convention, so this exercises the
        // actual serializer, not a fake.
        [Fact]
        public async Task RecipeId_404sOnDetailLookup_SkipsOptionAndDoesNotPoisonPersistentCache()
        {
            string tempDir = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "ttb-test-" + System.Guid.NewGuid());
            try
            {
                const int buildId = 205780;
                var cacheStore = new TaimisToolbench.Services.Recipes.OverlayRecipeCacheStore(tempDir);
                cacheStore.Load();
                cacheStore.SetCurrentBuildId(buildId);

                var api = new InMemoryRecipeApiClient();
                // Item 1 has two candidate recipes: 10 (healthy) and 11
                // (404s on detail lookup despite being a real search hit).
                api.AddSearchResult(1, 10, 11);
                api.AddRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                });
                api.Return404For.Add(11);

                var svc = new RecipeService(api, cacheStore: cacheStore);

                var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);

                // Only the healthy recipe (10) became an option; the 404'd
                // one (11) was skipped, not crashed on.
                Assert.Single(node.Recipes);
                Assert.Equal(10, node.Recipes[0].RecipeId);

                // Persisting must still succeed (the null recipe is never
                // written into the store) and other recipes remain
                // retrievable - proving Flush() was not silently broken by
                // RecipeCacheSerializer.SerializeRecipes throwing on a null
                // entry in _recipes.
                cacheStore.Flush(force: true);

                var reloaded = new TaimisToolbench.Services.Recipes.OverlayRecipeCacheStore(tempDir);
                reloaded.Load();
                var persistedRecipe = reloaded.TryGetRecipe(10);
                Assert.NotNull(persistedRecipe);
                Assert.Equal(1, persistedRecipe.OutputItemId);

                // The 404'd id must not appear in the persisted store at
                // all - a genuine cache miss, not a stored null.
                Assert.Null(reloaded.TryGetRecipe(11));
            }
            finally
            {
                if (System.IO.Directory.Exists(tempDir))
                {
                    System.IO.Directory.Delete(tempDir, recursive: true);
                }
            }
        }

        // Persisting the overlay is file IO the plan pipeline used to spend
        // inside its timed tree-build phase. The build must hand back its
        // tree without waiting for the write, and the write must still land.
        [Fact]
        public async Task BuildTree_DoesNotWaitForThePersist_ButStillPersists()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildId = 205780;
                var overlay = new TaimisToolbench.Services.Recipes.OverlayRecipeCacheStore(tmp.Path);
                overlay.Load();
                overlay.SetCurrentBuildId(buildId);

                var api = new InMemoryRecipeApiClient();
                api.AddSearchResult(1, 10);
                api.AddRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 1,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                });

                var gated = new GatedFlushStore(overlay);
                var svc = new RecipeService(api, cacheStore: gated);

                // Opens the gate late as well, so a build that does wait on
                // the persist fails the assertion below instead of hanging.
                // Deliberately not awaited - it is a watchdog, and the
                // discard says so.
                _ = Task.Delay(System.TimeSpan.FromSeconds(5))
                    .ContinueWith(t => gated.Release());

                var node = await svc.BuildTreeAsync(1, 1, CancellationToken.None);
                bool persistedBeforeReturn = gated.FlushCompleted;

                gated.Release();
                await svc.PendingCacheFlush;

                Assert.False(persistedBeforeReturn);
                Assert.Single(node.Recipes);

                var reloaded = new TaimisToolbench.Services.Recipes.OverlayRecipeCacheStore(tmp.Path);
                reloaded.Load();
                Assert.NotNull(reloaded.TryGetRecipe(10));
                Assert.NotNull(reloaded.TryGetSearch(1));
            }
        }

        // Every call reaches a real OverlayRecipeCacheStore; the gate only
        // holds Flush at the door so a test can see whether its caller waits.
        private sealed class GatedFlushStore : TaimisToolbench.Services.Recipes.IRecipeCacheStore
        {
            private readonly TaimisToolbench.Services.Recipes.IRecipeCacheStore _inner;
            private readonly TaskCompletionSource<bool> _release =
                new TaskCompletionSource<bool>();

            private int _flushCompleted;

            public GatedFlushStore(TaimisToolbench.Services.Recipes.IRecipeCacheStore inner)
            {
                _inner = inner;
            }

            public bool FlushCompleted => Volatile.Read(ref _flushCompleted) == 1;

            public void Release() => _release.TrySetResult(true);

            public TaimisToolbench.Services.Recipes.RecipeCacheStats Stats => _inner.Stats;

            public IReadOnlyList<int> TryGetSearch(int outputItemId) =>
                _inner.TryGetSearch(outputItemId);

            public RawRecipe TryGetRecipe(int recipeId) => _inner.TryGetRecipe(recipeId);

            public void PutSearch(int outputItemId, IReadOnlyList<int> recipeIds) =>
                _inner.PutSearch(outputItemId, recipeIds);

            public void PutRecipe(int recipeId, RawRecipe recipe) =>
                _inner.PutRecipe(recipeId, recipe);

            public void Flush(bool force = false)
            {
                _release.Task.GetAwaiter().GetResult();
                _inner.Flush(force);
                Volatile.Write(ref _flushCompleted, 1);
            }
        }
    }
}
