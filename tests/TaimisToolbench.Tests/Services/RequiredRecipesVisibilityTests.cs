using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class RequiredRecipesVisibilityTests
    {
        private static PlanRowViewModel Row(string statusTag, string label = "Item")
        {
            return new PlanRowViewModel { RowType = PlanRowType.RecipeRow, Label = label, StatusTag = statusTag };
        }

        // --- IsUnlocked ---
        [Theory]
        [InlineData("Learned", true)]
        [InlineData("Auto-learned", true)]
        [InlineData("Missing!", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void IsUnlocked_MatchesExactStatusTags(string statusTag, bool expected)
        {
            Assert.Equal(expected, RequiredRecipesVisibility.IsUnlocked(statusTag));
        }

        // --- ApplyFilter ---
        [Fact]
        public void ApplyFilter_HideUnlockedFalse_ReturnsAllRows()
        {
            var rows = new List<PlanRowViewModel> { Row("Learned"), Row("Missing!"), Row("Auto-learned") };

            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: false);

            Assert.Equal(3, visible.Count);
        }

        [Fact]
        public void ApplyFilter_HideUnlockedTrue_KeepsOnlyMissing()
        {
            var missing = Row("Missing!", "Ecto");
            var rows = new List<PlanRowViewModel> { Row("Learned"), missing, Row("Auto-learned") };

            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);

            var kept = Assert.Single(visible);
            Assert.Same(missing, kept);
        }

        [Fact]
        public void ApplyFilter_HideUnlockedTrue_KeepsUnknownStatusRows()
        {
            // Unknown status ("" - recipe permission not available) is NOT
            // the same as "already unlocked" - hiding it would wrongly
            // claim the recipe needs no attention when this module simply
            // could not check.
            var unknown = Row("", "Mystery");
            var rows = new List<PlanRowViewModel> { Row("Learned"), unknown };

            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);

            var kept = Assert.Single(visible);
            Assert.Same(unknown, kept);
        }

        [Fact]
        public void ApplyFilter_AllUnlocked_ReturnsEmptyList()
        {
            var rows = new List<PlanRowViewModel> { Row("Learned"), Row("Auto-learned") };

            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);

            Assert.Empty(visible);
        }

        [Fact]
        public void ApplyFilter_NullRows_ReturnsEmptyList()
        {
            Assert.Empty(RequiredRecipesVisibility.ApplyFilter(null, hideUnlocked: true));
            Assert.Empty(RequiredRecipesVisibility.ApplyFilter(null, hideUnlocked: false));
        }

        [Fact]
        public void ApplyFilter_EmptyRows_ReturnsEmptyList()
        {
            Assert.Empty(RequiredRecipesVisibility.ApplyFilter(new List<PlanRowViewModel>(), hideUnlocked: true));
        }

        [Fact]
        public void ApplyFilter_DoesNotMutateInputList()
        {
            var rows = new List<PlanRowViewModel> { Row("Learned"), Row("Missing!") };

            RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);

            Assert.Equal(2, rows.Count);
        }

        // --- BuildHeaderTitle ---
        private static List<PlanRowViewModel> Rows(params string[] statusTags)
        {
            var rows = new List<PlanRowViewModel>(statusTags.Length);
            foreach (var tag in statusTags)
            {
                rows.Add(Row(tag));
            }

            return rows;
        }

        [Fact]
        public void BuildHeaderTitle_FilterOff_ShowsBareTotal()
        {
            var rows = Rows("Learned", "Learned", "Missing!", "Missing!", "Missing!");

            Assert.Equal(
                "Required Recipes (5)",
                RequiredRecipesVisibility.BuildHeaderTitle(rows, rows, hideUnlocked: false));
        }

        [Fact]
        public void BuildHeaderTitle_FilterOn_EveryVisibleRowMissing_SaysMissing()
        {
            var rows = Rows("Learned", "Learned", "Learned", "Missing!", "Missing!");
            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);

            Assert.Equal(
                "Required Recipes (showing 2 missing of 5)",
                RequiredRecipesVisibility.BuildHeaderTitle(rows, visible, hideUnlocked: true));
        }

        [Fact]
        public void BuildHeaderTitle_FilterOn_ZeroTotal_ShowsBareZero()
        {
            // Nothing to filter at all - avoid a confusing "(showing 0
            // missing of 0)" when the section would not even exist.
            var rows = new List<PlanRowViewModel>();

            Assert.Equal(
                "Required Recipes (0)",
                RequiredRecipesVisibility.BuildHeaderTitle(rows, rows, hideUnlocked: true));
        }

        [Fact]
        public void BuildHeaderTitle_FilterOn_NothingHidden_StillShowsShowingFormat()
        {
            // Honest even when the filter happens not to hide anything -
            // K == N is stated plainly rather than collapsing to the bare
            // form.
            var rows = Rows("Missing!", "Missing!", "Missing!", "Missing!", "Missing!");
            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);

            Assert.Equal(
                "Required Recipes (showing 5 missing of 5)",
                RequiredRecipesVisibility.BuildHeaderTitle(rows, visible, hideUnlocked: true));
        }

        [Fact]
        public void BuildHeaderTitle_NoRecipePermission_NeverClaimsAnythingIsMissing()
        {
            // Every row carries an empty status tag: the account had no
            // recipe permission, so the module checked nothing. The header
            // used to read "showing 8 missing of 8".
            var rows = Rows("", "", "", "", "", "", "", "");
            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);

            Assert.Equal(
                "Required Recipes (showing 8 of 8)",
                RequiredRecipesVisibility.BuildHeaderTitle(rows, visible, hideUnlocked: true));
        }

        [Fact]
        public void BuildHeaderTitle_OneUncheckedRowAmongMissing_DropsTheWordMissing()
        {
            var rows = Rows("Learned", "Missing!", "");
            var visible = RequiredRecipesVisibility.ApplyFilter(rows, hideUnlocked: true);

            Assert.Equal(
                "Required Recipes (showing 2 of 3)",
                RequiredRecipesVisibility.BuildHeaderTitle(rows, visible, hideUnlocked: true));
        }

        [Fact]
        public void BuildHeaderTitle_NullRows_DoesNotThrow()
        {
            Assert.Equal(
                "Required Recipes (0)",
                RequiredRecipesVisibility.BuildHeaderTitle(null, null, hideUnlocked: true));
        }

        // --- AllUnlockedMessage ---
        [Fact]
        public void AllUnlockedMessage_FormatsCount()
        {
            Assert.Equal("All 7 recipes already unlocked.", RequiredRecipesVisibility.AllUnlockedMessage(7));
        }
    }
}
