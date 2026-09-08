using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.CraftingPlanResultBuilders;

namespace TaimisToolbench.Tests.Services
{
    public class PlanViewModelBuilderSublabelTests
    {
        private readonly PlanViewModelBuilder _builder = new PlanViewModelBuilder();

        // --- FormatDisciplineSublabel ---
        [Fact]
        public void FormatDisciplineSublabel_SingleDiscipline()
        {
            var planDiscNames = new HashSet<string> { "Weaponsmith" };
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string> { "Weaponsmith" }, 400, planDiscNames);

            Assert.Equal("Weaponsmith 400", result);
        }

        [Fact]
        public void FormatDisciplineSublabel_MultiDiscipline_FiltersToRelevant()
        {
            var planDiscNames = new HashSet<string> { "Weaponsmith" };
            // Recipe has 4 disciplines, but plan only uses Weaponsmith
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string> { "Weaponsmith", "Armorsmith", "Huntsman", "Artificer" },
                400, planDiscNames);

            Assert.Equal("Weaponsmith 400", result);
        }

        [Fact]
        public void FormatDisciplineSublabel_MultiDiscipline_MultiRelevant()
        {
            var planDiscNames = new HashSet<string> { "Armorsmith", "Weaponsmith" };
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string> { "Weaponsmith", "Armorsmith", "Huntsman" },
                400, planDiscNames);

            Assert.Equal("Armorsmith / Weaponsmith 400", result);
        }

        [Fact]
        public void FormatDisciplineSublabel_NoDisciplines_EmptyString()
        {
            var planDiscNames = new HashSet<string> { "Weaponsmith" };
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string>(), 0, planDiscNames);

            Assert.Equal("", result);
        }

        [Fact]
        public void FormatDisciplineSublabel_NullDisciplines_EmptyString()
        {
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                null, 0, new HashSet<string>());

            Assert.Equal("", result);
        }

        [Fact]
        public void FormatDisciplineSublabel_NoIntersection_FallbackToAll()
        {
            // Plan disciplines don't overlap with recipe disciplines
            var planDiscNames = new HashSet<string> { "Leatherworker" };
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string> { "Weaponsmith", "Armorsmith" },
                300, planDiscNames);

            Assert.Equal("Armorsmith / Weaponsmith 300", result);
        }

        [Fact]
        public void FormatDisciplineSublabel_NullPlanDiscNames_ShowsAll()
        {
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string> { "Weaponsmith", "Armorsmith" }, 400, null);

            Assert.Equal("Armorsmith / Weaponsmith 400", result);
        }

        // --- In-game finding E: Mystic Forge is a facility, not a
        // discipline - its sublabel shows the facility name with no level
        // number instead of the internal "MysticForge 0" id string. ---
        [Fact]
        public void FormatDisciplineSublabel_SoleMysticForge_ShowsFacilityName_NoLevel()
        {
            // planDiscNames never contains "MysticForge" in production
            // (PlanResultBuilder.NonCraftingDisciplines excludes it from
            // RequiredDisciplines), which triggers the "no intersection ->
            // fallback to all recipe disciplines" branch above - this pins
            // that fallback's MysticForge-only output.
            var planDiscNames = new HashSet<string> { "Weaponsmith" };
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string> { "MysticForge" }, 0, planDiscNames);

            Assert.Equal("Mystic Forge", result);
        }

        [Fact]
        public void FormatDisciplineSublabel_SoleMysticForge_NullPlanDiscNames_ShowsFacilityName_NoLevel()
        {
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string> { "MysticForge" }, 0, null);

            Assert.Equal("Mystic Forge", result);
        }

        [Fact]
        public void FormatDisciplineSublabel_MysticForgeWithRealDiscipline_RelabelsButKeepsLevel()
        {
            // Not seen in real game data today, but not structurally
            // impossible - the other discipline's rating is still
            // meaningful, so the level number stays. planDiscNames is the
            // REAL production shape (never contains "MysticForge" -
            // PlanResultBuilder.NonCraftingDisciplines strips it out of
            // every option's Disciplines list before disciplineMap/
            // RequiredDisciplines is built, so BuildPlanDiscNames can never
            // produce a set containing it). This pins that MysticForge
            // survives the planDiscNames intersection on its own, not just
            // when the caller happens to include it in planDiscNames too.
            var planDiscNames = new HashSet<string> { "Weaponsmith" };
            var result = PlanViewModelBuilder.FormatDisciplineSublabel(
                new List<string> { "MysticForge", "Weaponsmith" }, 400, planDiscNames);

            Assert.Equal("Mystic Forge / Weaponsmith 400", result);
        }

        // --- Recipe sublabel integration ---
        [Fact]
        public void RequiredRecipes_Sublabel_ShowsRelevantDisciplines()
        {
            var result = MakeResult(
                requiredDisciplines: new List<RequiredDiscipline>
                {
                    new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 500 },
                },
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = 10,
                        OutputItemId = 1,
                        IsAutoLearned = false,
                        Disciplines = new List<string> { "Weaponsmith", "Armorsmith", "Huntsman" },
                        MinRating = 400,
                    },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.RequiredRecipes);
            Assert.Equal("Weaponsmith 400", section.Rows[0].Sublabel);
        }

        [Fact]
        public void CraftingSteps_Sublabel_ShowsRelevantDisciplines()
        {
            var result = MakeResult(
                requiredDisciplines: new List<RequiredDiscipline>
                {
                    new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 500 },
                },
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = 10,
                        OutputItemId = 2,
                        IsAutoLearned = true,
                        Disciplines = new List<string> { "Weaponsmith", "Armorsmith", "Huntsman", "Artificer" },
                        MinRating = 400,
                    },
                },
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = 10 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            Assert.Equal("Weaponsmith 400", section.Rows[0].Sublabel);
        }

        [Fact]
        public void CraftingSteps_Sublabel_MysticForge_ShowsFacilityNameNoLevel()
        {
            // End-to-end: RequiredDisciplines is empty (MysticForge excluded
            // per PlanResultBuilder.NonCraftingDisciplines - see MakeResult
            // below simulating that), so BuildPlanDiscNames yields an empty
            // planDiscNames and FormatDisciplineSublabel's "no filtering"
            // branch shows the recipe's own MysticForge discipline verbatim
            // - relabeled, with no level number.
            var result = MakeResult(
                requiredDisciplines: new List<RequiredDiscipline>(),
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = -100,
                        OutputItemId = 2,
                        IsAutoLearned = true,
                        Disciplines = new List<string> { "MysticForge" },
                        MinRating = 0,
                    },
                },
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = -100 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            Assert.Equal("Mystic Forge", section.Rows[0].Sublabel);
        }

        [Fact]
        public void CraftingSteps_Sublabel_MysticForgeWithRealDiscipline_RelabelsButKeepsLevel()
        {
            // End-to-end companion to the MysticForgeWithRealDiscipline unit
            // test above, going through the real BuildPlanDiscNames path
            // instead of a hand-fed planDiscNames set. RequiredDisciplines
            // contains only "Weaponsmith" (simulating PlanResultBuilder.
            // NonCraftingDisciplines having stripped "MysticForge" out of
            // disciplineMap), while the recipe's own Disciplines field keeps
            // both (simulating RequiredRecipe.Disciplines being left
            // unfiltered) - the same shape the real production pipeline
            // produces for a recipe combining the forge with a genuine
            // leveled discipline.
            var result = MakeResult(
                requiredDisciplines: new List<RequiredDiscipline>
                {
                    new RequiredDiscipline { Discipline = "Weaponsmith", MinRating = 400 },
                },
                requiredRecipes: new List<RequiredRecipe>
                {
                    new RequiredRecipe
                    {
                        RecipeId = -101,
                        OutputItemId = 2,
                        IsAutoLearned = true,
                        Disciplines = new List<string> { "MysticForge", "Weaponsmith" },
                        MinRating = 400,
                    },
                },
                steps: new List<PlanStep>
                {
                    new PlanStep { ItemId = 2, Quantity = 1, Source = AcquisitionSource.Craft, RecipeId = -101 },
                });
            var vm = _builder.Build(result);

            var section = vm.Sections.First(s => s.SectionType == PlanSectionType.CraftingSteps);
            Assert.Equal("Mystic Forge / Weaponsmith 400", section.Rows[0].Sublabel);
        }
    }
}
