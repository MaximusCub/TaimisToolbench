using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.CraftingPlanResultBuilders;

namespace TaimisToolbench.Tests.Services
{
    public class PlanViewModelBuilderStepSectionsTests
    {
        private readonly PlanViewModelBuilder _builder = new PlanViewModelBuilder();

        // --- Crafting Steps ---
        [Fact]
        public void CraftingSteps_OnlyCraftSource()
        {
            var result = MakeResult(steps: new List<PlanStep>
            {
                new PlanStep { ItemId = 1, Quantity = 3, Source = AcquisitionSource.BuyFromTp },
                new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                new PlanStep { ItemId = 3, Quantity = 2, Source = AcquisitionSource.BuyFromVendor },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            Assert.Single(section.Rows);
            Assert.Equal(PlanRowType.CraftStep, section.Rows[0].RowType);
        }

        [Fact]
        public void CraftingSteps_PreservesOrder()
        {
            var meta = MetaFor((2, "Blade", "blade.png"), (3, "Hilt", "hilt.png"));
            var result = MakeResult(
                metadata: meta,
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    new PlanStep { ItemId = 3, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 20 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            Assert.Equal(2, section.Rows.Count);
            Assert.Equal("Blade", section.Rows[0].Label);
            Assert.Equal("Hilt", section.Rows[1].Label);
        }

        [Fact]
        public void NoCraftSteps_NoCraftingSection()
        {
            var result = MakeResult(steps: new List<PlanStep>
            {
                new PlanStep { ItemId = 1, Quantity = 5, Source = AcquisitionSource.BuyFromTp },
            });
            var vm = _builder.Build(result);

            Assert.DoesNotContain(vm.Sections, s => s.SectionType == PlanSectionType.CraftingSteps);
        }

        [Fact]
        public void TimegatedItems_AppendedAsNoticeRowsInCraftingSteps()
        {
            // A timegated (vendor purchase cap) notice renders as
            // a plain informational row alongside real craft steps, never
            // altering the numbered CraftStep rows themselves.
            var meta = MetaFor((2, "Blade", "blade.png"), (9, "Obsidian Shard", "shard.png"));
            var result = MakeResult(
                metadata: meta,
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                },
                timegatedItems: new List<TimegatedItem>
                {
                    new TimegatedItem { ItemId = 9, CapType = TimegatedCapType.Daily, CapValue = 3, NeededCount = 4 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            Assert.Equal(2, section.Rows.Count);
            Assert.Equal(PlanRowType.CraftStep, section.Rows[0].RowType);
            Assert.Equal(PlanRowType.TimegatedNotice, section.Rows[1].RowType);
            Assert.Contains("Obsidian Shard", section.Rows[1].Label);
            Assert.Contains("Daily", section.Rows[1].Label);
            Assert.Contains("3", section.Rows[1].Label);
            Assert.Contains("4", section.Rows[1].Label);
        }

        [Fact]
        public void TimegatedItems_NoCraftSteps_StillCreatesCraftingSection()
        {
            // A plan with zero real craft steps but a timegated vendor buy
            // must still surface the notice - the section is no longer
            // gated purely on craftSteps.Count.
            var result = MakeResult(
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 9, Quantity = 4, Source = AcquisitionSource.BuyFromVendor },
                },
                timegatedItems: new List<TimegatedItem>
                {
                    new TimegatedItem { ItemId = 9, CapType = TimegatedCapType.Weekly, CapValue = 3, NeededCount = 4 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            Assert.Single(section.Rows);
            Assert.Equal(PlanRowType.TimegatedNotice, section.Rows[0].RowType);
        }

        [Fact]
        public void TimegatedItems_SeasonalCapType_RendersSeasonWording()
        {
            // Astral Acclaim package: Seasonal renders
            // with the noun "Season" (matching gw2e's own Wizard's Vault
            // wording), keeping the same "{CapLabel} limit: N (plan needs
            // M)" shape Daily/Weekly already use.
            var meta = MetaFor((9, "Obsidian Shard", "shard.png"));
            var result = MakeResult(
                metadata: meta,
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 9, Quantity = 60, Source = AcquisitionSource.BuyFromVendor },
                },
                timegatedItems: new List<TimegatedItem>
                {
                    new TimegatedItem { ItemId = 9, CapType = TimegatedCapType.Seasonal, CapValue = 20, NeededCount = 60 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            var notice = Assert.Single(section.Rows);
            Assert.Equal(PlanRowType.TimegatedNotice, notice.RowType);
            Assert.Contains("Obsidian Shard", notice.Label);
            Assert.Contains("Season limit: 20", notice.Label);
            Assert.Contains("plan needs 60", notice.Label);
            Assert.DoesNotContain("Seasonal", notice.Label);
        }

        // --- Vendor caps vs TP liquidity (field issue: an Exordium
        // plan's "Mystic Coin is timegated - Weekly limit: 10 (plan needs
        // 366)" implied a 37-week wait for a freely TP-buyable item).
        // Same filter the Ranker applies in RankerReadinessCalculator's
        // FilterVendorCappedItems: the remainder above the cap is coin,
        // not time. ---
        private static CraftingPlanResult WithPrices(
            CraftingPlanResult result, Dictionary<int, ItemPrice> prices)
        {
            result.SolveContext = new PlanSolveContext { Prices = prices };
            return result;
        }

        [Fact]
        public void TimegatedItems_TpLiquidItem_VendorCapNoticeIsDropped()
        {
            const int mysticCoinLike = 19976;
            var meta = MetaFor((2, "Blade", "blade.png"), (mysticCoinLike, "Mystic Coin", "coin.png"));
            var result = WithPrices(
                MakeResult(
                    metadata: meta,
                    steps: new List<PlanStep>
                    {
                        new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                    },
                    timegatedItems: new List<TimegatedItem>
                    {
                        new TimegatedItem { ItemId = mysticCoinLike, CapType = TimegatedCapType.Weekly, CapValue = 10, NeededCount = 366 },
                    }),
                new Dictionary<int, ItemPrice>
                {
                    [mysticCoinLike] = new ItemPrice { ItemId = mysticCoinLike, BuyInstant = 100, SellInstant = 120 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            var row = Assert.Single(section.Rows);
            Assert.Equal(PlanRowType.CraftStep, row.RowType);
        }

        [Fact]
        public void TimegatedItems_TpLiquidOnlyNotice_NoCraftSteps_NoSection()
        {
            // The notices-only section exists purely to carry notices; a
            // plan whose only candidate notice is filtered away must not
            // render an empty Crafting Steps section.
            const int mysticCoinLike = 19976;
            var result = WithPrices(
                MakeResult(
                    steps: new List<PlanStep>
                    {
                        new PlanStep { ItemId = mysticCoinLike, Quantity = 366, Source = AcquisitionSource.BuyFromVendor },
                    },
                    timegatedItems: new List<TimegatedItem>
                    {
                        new TimegatedItem { ItemId = mysticCoinLike, CapType = TimegatedCapType.Weekly, CapValue = 10, NeededCount = 366 },
                    }),
                new Dictionary<int, ItemPrice>
                {
                    [mysticCoinLike] = new ItemPrice { ItemId = mysticCoinLike, BuyInstant = 100, SellInstant = 120 },
                });
            var vm = _builder.Build(result);

            Assert.DoesNotContain(vm.Sections, s => s.SectionType == PlanSectionType.CraftingSteps);
        }

        [Fact]
        public void TimegatedItems_UnpricedItem_KeepsNoticeWithVendorLimitWording()
        {
            // Present in the price map but with no live orders on either
            // side: the cap genuinely delays the plan, and the label names
            // the constraint for what it is - a vendor purchase limit
            // (same wording as the Ranker's vendor-cap note).
            const int boundItem = 12345;
            var meta = MetaFor((boundItem, "Bound Trophy", "trophy.png"));
            var result = WithPrices(
                MakeResult(
                    metadata: meta,
                    steps: new List<PlanStep>
                    {
                        new PlanStep { ItemId = boundItem, Quantity = 16, Source = AcquisitionSource.BuyFromVendor },
                    },
                    timegatedItems: new List<TimegatedItem>
                    {
                        new TimegatedItem { ItemId = boundItem, CapType = TimegatedCapType.Weekly, CapValue = 10, NeededCount = 16 },
                    }),
                new Dictionary<int, ItemPrice>
                {
                    [boundItem] = new ItemPrice { ItemId = boundItem, BuyInstant = 0, SellInstant = 0 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            var notice = Assert.Single(section.Rows);
            Assert.Equal(PlanRowType.TimegatedNotice, notice.RowType);
            Assert.Equal(
                "Bound Trophy is timegated - vendor Weekly limit: 10 (plan needs 16)",
                notice.Label);
        }

        [Fact]
        public void TimegatedItems_MissingPriceData_KeepsNoticeRatherThanInventingLiquidity()
        {
            // No SolveContext price map at all (MakeResult leaves
            // SolveContext null): the builder must not assume liquidity.
            var meta = MetaFor((9, "Obsidian Shard", "shard.png"));
            var result = MakeResult(
                metadata: meta,
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 9, Quantity = 16, Source = AcquisitionSource.BuyFromVendor },
                },
                timegatedItems: new List<TimegatedItem>
                {
                    new TimegatedItem { ItemId = 9, CapType = TimegatedCapType.Weekly, CapValue = 10, NeededCount = 16 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            var notice = Assert.Single(section.Rows);
            Assert.Equal(PlanRowType.TimegatedNotice, notice.RowType);
        }

        [Fact]
        public void TimegatedItems_MixedLiquidity_OnlyTheUnpricedItemKeepsItsNotice()
        {
            const int liquidItem = 19976;
            const int boundItem = 12345;
            var meta = MetaFor(
                (liquidItem, "Mystic Coin", "coin.png"),
                (boundItem, "Bound Trophy", "trophy.png"));
            var result = WithPrices(
                MakeResult(
                    metadata: meta,
                    steps: new List<PlanStep>
                    {
                        new PlanStep { ItemId = liquidItem, Quantity = 16, Source = AcquisitionSource.BuyFromVendor },
                        new PlanStep { ItemId = boundItem, Quantity = 16, Source = AcquisitionSource.BuyFromVendor },
                    },
                    timegatedItems: new List<TimegatedItem>
                    {
                        new TimegatedItem { ItemId = liquidItem, CapType = TimegatedCapType.Weekly, CapValue = 10, NeededCount = 16 },
                        new TimegatedItem { ItemId = boundItem, CapType = TimegatedCapType.Daily, CapValue = 5, NeededCount = 16 },
                    }),
                new Dictionary<int, ItemPrice>
                {
                    [liquidItem] = new ItemPrice { ItemId = liquidItem, BuyInstant = 100, SellInstant = 120 },
                    [boundItem] = new ItemPrice { ItemId = boundItem, BuyInstant = 0, SellInstant = 0 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            var notice = Assert.Single(section.Rows, r => r.RowType == PlanRowType.TimegatedNotice);
            Assert.Contains("Bound Trophy", notice.Label);
            Assert.Contains("vendor Daily limit: 5", notice.Label);
            Assert.DoesNotContain(section.Rows, r => r.Label != null && r.Label.Contains("Mystic Coin"));
        }

        // --- Required Disciplines ---
        [Fact]
        public void RequiredDisciplines_MapsCorrectly()
        {
            var result = MakeResult(requiredDisciplines: new List<RequiredDiscipline>
            {
                new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 500 },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredDisciplines);
            Assert.Single(section.Rows);
            Assert.Equal(PlanRowType.DisciplineRow, section.Rows[0].RowType);
            Assert.Equal("Weaponsmith", section.Rows[0].Label);
            Assert.Equal("Level 500", section.Rows[0].Sublabel);
        }

        [Fact]
        public void RequiredDisciplines_Empty_NoSection()
        {
            var result = MakeResult(requiredDisciplines: new List<RequiredDiscipline>());
            var vm = _builder.Build(result);

            Assert.DoesNotContain(vm.Sections, s => s.SectionType == PlanSectionType.RequiredDisciplines);
        }

        // --- Per-character discipline display (gw2efficiency
        // parity): character availability text on DisciplineRow rows. ---
        [Fact]
        public void RequiredDisciplines_AllCharactersSufficient_ListsEachWithRating()
        {
            var result = MakeResult(
                requiredDisciplines: new List<RequiredDiscipline>
                {
                    new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 400 },
                },
                characterDisciplines: new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Bob", Discipline = "Weaponsmith", Rating = 400, Active = true },
                    new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Weaponsmith", Rating = 500, Active = true },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredDisciplines);
            // Highest rating first, no "/min" suffix - both meet the
            // required 400.
            Assert.Equal("Anna (500), Bob (400)", section.Rows[0].CharacterAvailabilityText);
        }

        [Fact]
        public void RequiredDisciplines_CharacterBelowRequiredRating_ShowsSlashMinConvention()
        {
            var result = MakeResult(
                requiredDisciplines: new List<RequiredDiscipline>
                {
                    new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 450 },
                },
                characterDisciplines: new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Bob", Discipline = "Weaponsmith", Rating = 400, Active = true },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredDisciplines);
            Assert.Equal("Bob (400/450)", section.Rows[0].CharacterAvailabilityText);
        }

        [Fact]
        public void RequiredDisciplines_NoCharacterHasIt_SaysNotTrainedPlainly()
        {
            var result = MakeResult(
                requiredDisciplines: new List<RequiredDiscipline>
                {
                    new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 400 },
                },
                characterDisciplines: new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Bob", Discipline = "Chef", Rating = 400, Active = true },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredDisciplines);
            Assert.Equal("Not trained on any character", section.Rows[0].CharacterAvailabilityText);
        }

        [Fact]
        public void RequiredDisciplines_NoSnapshotCharacterData_SublabelTextOmitted()
        {
            // characterDisciplines left null (default) - the snapshot never
            // captured this data at all (old snapshot / degraded fetch).
            // Must show nothing extra, never a fabricated "not trained"
            // claim.
            var result = MakeResult(requiredDisciplines: new List<RequiredDiscipline>
            {
                new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 400 },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredDisciplines);
            Assert.Null(section.Rows[0].CharacterAvailabilityText);
        }

        // --- Required Recipes ---
        [Fact]
        public void RequiredRecipes_AutoLearned_IsNotListedAtAll()
        {
            // The game grants an auto-learned recipe with the discipline
            // rating, so there is no unlock the player can be missing. The
            // section drops it for the same reason it drops a Mystic Forge
            // one, through the same predicate the Ranker's Recipes gate
            // counts by - which is what makes the header's total and that
            // cell's denominator one number.
            var result = MakeResult(requiredRecipes: new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = 10,
                    OutputItemId = 1,
                    IsAutoLearned = true,
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    IsMissing = null,
                },
            });
            var vm = _builder.Build(result);

            Assert.DoesNotContain(
                vm.Sections, s => s.SectionType == PlanSectionType.RequiredRecipes);
        }

        [Fact]
        public void RequiredRecipes_Missing_StatusTag()
        {
            var result = MakeResult(requiredRecipes: new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = 10,
                    OutputItemId = 1,
                    IsAutoLearned = false,
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    IsMissing = true,
                },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Equal("Missing!", section.Rows[0].StatusTag);
        }

        [Fact]
        public void RequiredRecipes_Learned_StatusTag()
        {
            var result = MakeResult(requiredRecipes: new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = 10,
                    OutputItemId = 1,
                    IsAutoLearned = false,
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    IsMissing = false,
                },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Equal("Learned", section.Rows[0].StatusTag);
        }

        [Fact]
        public void RequiredRecipes_NullMissing_EmptyStatusTag()
        {
            var result = MakeResult(requiredRecipes: new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = 10,
                    OutputItemId = 1,
                    IsAutoLearned = false,
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    IsMissing = null,
                },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Equal("", section.Rows[0].StatusTag);
        }

        // --- UI-bundle milestone, Feature A (wiki links) ---
        [Fact]
        public void RequiredRecipes_NotLearnedFromItem_WikiTargetIsTheAcquisitionAnchor()
        {
            var meta = MetaFor((1, "Bolt of Damask", "bolt.png"));
            var result = MakeResult(metadata: meta, requiredRecipes: new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = 10,
                    OutputItemId = 1,
                    IsAutoLearned = false,
                    IsLearnedFromItem = false,
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    IsMissing = true,
                },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Bolt_of_Damask#Acquisition",
                section.Rows[0].WikiTarget.BuildUrl());
            Assert.Equal(
                IconWikiTarget.AcquisitionHintText, section.Rows[0].WikiTarget.Hint);
        }

        [Fact]
        public void RequiredRecipes_LearnedFromItem_WikiTargetIsTheRecipeSheetPage()
        {
            var meta = MetaFor((1, "Bolt of Damask", "bolt.png"));
            var result = MakeResult(metadata: meta, requiredRecipes: new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = 10,
                    OutputItemId = 1,
                    IsAutoLearned = false,
                    IsLearnedFromItem = true,
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    IsMissing = true,
                },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Recipe:_Bolt_of_Damask",
                section.Rows[0].WikiTarget.BuildUrl());
        }

        /// <summary>
        /// A row the player has nothing left to unlock still reaches the
        /// wiki: every icon in the module opens a page on right-click, and
        /// an already-learned recipe's page is still the page about it.
        /// </summary>
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        public void RequiredRecipes_LearnedAndMissingRowsStillReachTheWiki(
            bool isAutoLearned, bool isMissing)
        {
            var meta = MetaFor((1, "Bolt of Damask", "bolt.png"));
            var result = MakeResult(metadata: meta, requiredRecipes: new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = 10,
                    OutputItemId = 1,
                    IsAutoLearned = isAutoLearned,
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    IsMissing = isMissing,
                },
            });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Bolt_of_Damask#Acquisition",
                section.Rows[0].WikiTarget.BuildUrl());
        }

        [Fact]
        public void RequiredRecipes_OutputName_FromMetadata()
        {
            var meta = MetaFor((5, "Cool Blade", "blade.png"));
            var result = MakeResult(
                metadata: meta,
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = 10,
                        OutputItemId = 5,
                        IsAutoLearned = false,
                        Disciplines = new List<string> { "Weaponsmith" },
                        MinRating = 400,
                    },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Equal("Cool Blade", section.Rows[0].Label);
            Assert.Equal("blade.png", section.Rows[0].IconUrl);
        }

        // --- Quick win #2: Mystic Forge rows excluded from the
        // Required Recipes SECTION (nothing to learn) ---
        [Fact]
        public void RequiredRecipes_MysticForgeOnly_SectionOmittedEntirely()
        {
            // A plan whose only "recipe" is a sole Mystic Forge combination
            // has nothing to learn at all - the section itself is dropped,
            // not left present with a "(0)" header and no rows.
            var result = MakeResult(requiredRecipes: new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = -100,
                    OutputItemId = 1,
                    IsAutoLearned = true,
                    Disciplines = new List<string> { "MysticForge" },
                    MinRating = 0,
                    IsMissing = false,
                },
            });
            var vm = _builder.Build(result);

            Assert.DoesNotContain(vm.Sections, s => s.SectionType == PlanSectionType.RequiredRecipes);
        }

        [Fact]
        public void RequiredRecipes_MixedMysticForgeAndReal_OnlyRealRecipeShownAndCounted()
        {
            // One sole-Mystic-Forge recipe alongside one real-discipline
            // recipe: only the real one survives, and the header count
            // reflects that post-filter total, not the raw
            // result.RequiredRecipes.Count of 2.
            var meta = MetaFor((1, "Forge Trinket", "f.png"), (2, "Blade", "b.png"));
            var result = MakeResult(
                metadata: meta,
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = -100,
                        OutputItemId = 1,
                        IsAutoLearned = true,
                        Disciplines = new List<string> { "MysticForge" },
                        MinRating = 0,
                        IsMissing = false,
                    },
                    new RequiredRecipe
                    {
                        RecipeId = 10,
                        OutputItemId = 2,
                        IsAutoLearned = false,
                        Disciplines = new List<string> { "Weaponsmith" },
                        MinRating = 400,
                        IsMissing = true,
                    },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Single(section.Rows);
            Assert.Equal("Blade", section.Rows[0].Label);
            Assert.Equal("Required Recipes (1)", section.Title);
        }

        [Fact]
        public void RequiredRecipes_MysticForgeWithRealDiscipline_LeavesSection()
        {
            // PlanResultBuilder forces IsMissing = false as soon as ONE
            // discipline is unlock-free, so a forge-plus-Weaponsmith recipe
            // reaches this builder as permanently known. Listing it drew a
            // row the player can never act on.
            var meta = MetaFor((2, "Blade", "b.png"));
            var result = MakeResult(
                metadata: meta,
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = 10,
                        OutputItemId = 2,
                        IsAutoLearned = false,
                        Disciplines = new List<string> { "MysticForge", "Weaponsmith" },
                        MinRating = 400,
                        IsMissing = false,
                    },
                });
            var vm = _builder.Build(result);

            Assert.DoesNotContain(
                vm.Sections, s => s.SectionType == PlanSectionType.RequiredRecipes);
        }

        [Fact]
        public void RequiredRecipes_MerchantSourceTag_LeavesSection()
        {
            // Neither the "Merchant" nor the "Achievement" tag has an
            // unlock, so both are filtered on the same rule the forge is.
            var meta = MetaFor((1, "Trebuchet Part", "t.png"), (2, "Blade", "b.png"));
            var result = MakeResult(
                metadata: meta,
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = -1595,
                        OutputItemId = 1,
                        IsAutoLearned = false,
                        Disciplines = new List<string> { "Merchant" },
                        MinRating = 0,
                        IsMissing = false,
                    },
                    new RequiredRecipe
                    {
                        RecipeId = 10,
                        OutputItemId = 2,
                        IsAutoLearned = false,
                        Disciplines = new List<string> { "Weaponsmith" },
                        MinRating = 400,
                        IsMissing = true,
                    },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Single(section.Rows);
            Assert.Equal("Blade", section.Rows[0].Label);
            Assert.Equal("Required Recipes (1)", section.Title);
        }

        // --- Section order ---
        [Fact]
        public void SectionOrder_MatchesSpec()
        {
            var meta = MetaFor(
                (1, "Target", "t.png"),
                (2, "Blade", "b.png"),
                (3, "Ore", "o.png"),
                (10, "Used", "u.png"));
            var result = MakeResult(
                targetItemId: 1,
                metadata: meta,
                usedMaterials: new List<UsedMaterial>
                {
                    new UsedMaterial { ItemId = 10, QuantityUsed = 1 },
                },
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 3, Quantity = 5, Source = AcquisitionSource.BuyFromTp, TotalCost = 500 },
                    new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 20 },
                },
                requiredDisciplines: new List<RequiredDiscipline>
                {
                    new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 500 },
                },
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = 20,
                        OutputItemId = 2,
                        IsAutoLearned = false,
                        Disciplines = new List<string> { "Weaponsmith" },
                        MinRating = 500,
                    },
                });
            var vm = _builder.Build(result);

            var types = vm.Sections.Select(s => s.SectionType).ToList();
            Assert.Equal(new[]
            {
                PlanSectionType.Summary,
                PlanSectionType.UsedMaterials,
                PlanSectionType.ShoppingList,
                PlanSectionType.RequiredDisciplines,
                PlanSectionType.RequiredRecipes,
                PlanSectionType.CraftingSteps,
            }, types);
        }

        // --- Mixed steps ---
        [Fact]
        public void MixedSteps_CorrectSectionAssignment()
        {
            var result = MakeResult(steps: new List<PlanStep>
            {
                new PlanStep { ItemId = 1, Quantity = 3, Source = AcquisitionSource.BuyFromTp, TotalCost = 300 },
                new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                new PlanStep { ItemId = 3, Quantity = 2, Source = AcquisitionSource.BuyFromVendor, TotalCost = 200 },
            });
            var vm = _builder.Build(result);

            var shopping = vm.Sections.First(s => s.SectionType == PlanSectionType.ShoppingList);
            Assert.Equal(2, shopping.Rows.Count);
            Assert.Contains(shopping.Rows, r => r.RowType == PlanRowType.ShoppingBuy);
            Assert.Contains(shopping.Rows, r => r.RowType == PlanRowType.ShoppingVendor);

            var crafting = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            Assert.Single(crafting.Rows);
            Assert.Equal(PlanRowType.CraftStep, crafting.Rows[0].RowType);
        }

        // --- Target quantity ---
        [Fact]
        public void TargetQuantity_PassedThrough()
        {
            var result = MakeResult(targetQuantity: 5);
            var vm = _builder.Build(result);

            Assert.Equal(5, vm.TargetQuantity);
        }
    }
}
