using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Which required recipes a surface counts, and the plan view's
    /// "Hide Unlocked Recipes" header text. Blish-free by design (works only
    /// on PlanRowViewModel/List/bool) so it can be exercised by a real test
    /// without any Blish HUD dependency - CraftingPlanView calls this from
    /// its RequiredRecipes render branch instead of embedding the filter
    /// predicate inline where it could not be unit-tested.
    ///
    /// A row is "unlocked" here iff its StatusTag is Learned or
    /// Auto-learned. A Missing row is kept, which is the whole point of the
    /// filter. A row with an EMPTY StatusTag is kept too, deliberately: the
    /// account had no recipe permission, so unlock status could not be
    /// determined at all. Hiding it would claim "you have nothing to do
    /// here" for a recipe the module could not check.
    /// </summary>
    internal static class RequiredRecipesVisibility
    {
        /// <summary>
        /// The three status tags a Required Recipes row can carry, plus the
        /// absence of one. PlanViewModelBuilder writes them,
        /// RecipesSectionRenderer colours them and this class reads them, so
        /// they are declared once here rather than spelled out at each site.
        /// An empty tag means the module could not check the recipe.
        /// </summary>
        public const string LearnedStatusTag = "Learned";

        public const string AutoLearnedStatusTag = "Auto-learned";

        public const string MissingStatusTag = "Missing!";

        /// <summary>
        /// Source tags rather than player-levelable GW2 crafting
        /// disciplines. The Mystic Forge is a facility; "Achievement" and
        /// "Merchant" mark seed recipes whose output is handed over once a
        /// condition outside the crafting panel is met. None has a
        /// "learn this recipe" unlock, and none appears in
        /// /v2/account/recipes.
        /// </summary>
        private static readonly HashSet<string> UnlockFreeDisciplines =
            new HashSet<string> { "MysticForge", "Achievement", "Merchant" };

        public static bool IsUnlocked(string statusTag)
        {
            return statusTag == LearnedStatusTag || statusTag == AutoLearnedStatusTag;
        }

        /// <summary>
        /// True when a required recipe carries a discipline that has no
        /// unlock, so the player cannot be missing it and no surface may
        /// count it.
        /// <para>
        /// This is the same "any" test PlanResultBuilder applies when it
        /// forces IsMissing = false, and PlanViewModelBuilder.BuildRecipesSection
        /// and RankerReadinessCalculator.ScoreRecipes both call it. That
        /// makes one invariant: a recipe whose IsMissing is forced false for
        /// want of an unlock is counted by nothing. Counting one padded both
        /// halves of the Ranker's fraction and lifted the Recipes cell above
        /// the plan's own count.
        /// </para>
        /// <para>
        /// Empty or null disciplines is NOT a match: that is absent data,
        /// and dropping the recipe would claim an unlock rule the module
        /// never established.
        /// </para>
        /// </summary>
        public static bool IsUnlockFree(IReadOnlyList<string> disciplines)
        {
            if (disciplines == null || disciplines.Count == 0)
            {
                return false;
            }

            // Indexed rather than foreach: the parameter is an interface, so
            // foreach would heap-allocate an enumerator once per recipe, and
            // the Ranker calls this for every recipe of every watchlist row.
            for (int i = 0; i < disciplines.Count; i++)
            {
                if (UnlockFreeDisciplines.Contains(disciplines[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the rows that should render given the current filter
        /// state: every row when hideUnlocked is false, otherwise every row
        /// that is NOT Learned/Auto-learned. Never mutates the input list -
        /// the section's own Rows list stays the permanent, unfiltered
        /// source of truth across toggles (see CraftingPlanView's
        /// _hideUnlockedRecipes field doc comment).
        /// </summary>
        public static List<PlanRowViewModel> ApplyFilter(
            IReadOnlyList<PlanRowViewModel> rows, bool hideUnlocked)
        {
            if (rows == null)
            {
                return new List<PlanRowViewModel>();
            }

            if (!hideUnlocked)
            {
                return new List<PlanRowViewModel>(rows);
            }

            var visible = new List<PlanRowViewModel>(rows.Count);
            foreach (var row in rows)
            {
                if (!IsUnlocked(row?.StatusTag))
                {
                    visible.Add(row);
                }
            }

            return visible;
        }

        /// <summary>
        /// Section header title. Always states the TOTAL recipe count, after
        /// the Mystic Forge filter BuildRecipesSection applied upstream, so
        /// the header never understates what the plan needs.
        /// <para>
        /// The word "missing" is used only when every visible row is
        /// actually Missing. The filter also keeps rows the module could not
        /// check, and calling one of those missing states a fact the module
        /// does not have. With no recipe permission every row is unchecked,
        /// which used to render as "showing 8 missing of 8" for an account
        /// the module had learned nothing about.
        /// </para>
        /// </summary>
        public static string BuildHeaderTitle(
            IReadOnlyList<PlanRowViewModel> rows,
            IReadOnlyList<PlanRowViewModel> visibleRows,
            bool hideUnlocked)
        {
            int totalCount = rows?.Count ?? 0;
            if (!hideUnlocked || totalCount == 0)
            {
                return $"Required Recipes ({totalCount})";
            }

            int visibleCount = visibleRows?.Count ?? 0;
            if (visibleCount > 0 && AllMissing(visibleRows))
            {
                return $"Required Recipes (showing {visibleCount} missing of {totalCount})";
            }

            return $"Required Recipes (showing {visibleCount} of {totalCount})";
        }

        private static bool AllMissing(IReadOnlyList<PlanRowViewModel> rows)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (rows[i]?.StatusTag != MissingStatusTag)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Friendly single-line replacement for an empty filtered row list -
        /// shown instead of a section that would otherwise render a header
        /// with zero rows beneath it. totalCount is the section's real
        /// (unfiltered) recipe count, matching BuildHeaderTitle's own N.
        /// </summary>
        public static string AllUnlockedMessage(int totalCount)
        {
            return $"All {totalCount} recipes already unlocked.";
        }
    }
}
