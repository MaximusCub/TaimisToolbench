using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// source-selection-simplification (redesign,
    /// docs/gw2e-considerations.md): pure, Blish-free competency check - can
    /// the account actually craft a given recipe, i.e. does some character
    /// have one of the recipe's Disciplines at or above its MinRating? Used
    /// by PlanSolver.Evaluate to decide whether a Craft decision that WOULD
    /// win the automatic buy-vs-craft-vs-vendor comparison on cost alone is
    /// also actually craftable by this account before letting it win by
    /// default - see PlanSolver.Decision's "craftExcludedFromAutoPick" doc
    /// comment for the seam this feeds. Never touches cost/comparison math
    /// itself (kept entirely separate from PickCheapest) and never affects
    /// CanCraft or a manual override - only whether Craft may win
    /// AUTOMATICALLY.
    /// </summary>
    internal static class CraftCompetencyEvaluator
    {
        // Recipe "Disciplines" tags that are informational source facts, not
        // real, player-levelable GW2 crafting disciplines - a recipe
        // carrying only these is inherently available whenever its
        // ingredients are, with no "train this discipline" concept and
        // therefore nothing for competency to gate on. The same three tags
        // appear in PlanResultBuilder's NonCraftingDisciplines and in
        // RequiredRecipesVisibility's UnlockFreeDisciplines - kept as an
        // independent copy here rather than a new cross-file dependency,
        // since PlanSolver's Services layer should not reach into a
        // display-adjacent internal for a solver-path decision.
        private static readonly HashSet<string> NonLevelableDisciplineTags =
            new HashSet<string>(StringComparer.Ordinal) { "MysticForge", "Achievement", "Merchant" };

        /// <summary>
        /// The account's best rating per real (player-levelable) crafting
        /// discipline, built once per solve from the raw per-character
        /// snapshot list so PlanSolver.Evaluate's recipe loop (run once per
        /// tree node, for every recipe candidate) never re-scans the raw
        /// list per lookup. Null when <paramref name="characterDisciplines"/>
        /// itself is null - "no snapshot captured this data at all", the
        /// SAME null contract PlanViewModelBuilder.MatchingCharacterDisciplines
        /// already uses - so AccountCanCraft can distinguish "unknown,
        /// never penalize" from "known, and nobody has it" (an empty, non-
        /// null dictionary).
        /// </summary>
        public static Dictionary<string, int> BuildBestRatingByDiscipline(
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines)
        {
            if (characterDisciplines == null)
            {
                return null;
            }

            var best = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var cd in characterDisciplines)
            {
                if (cd == null || cd.Discipline == null)
                {
                    continue;
                }

                if (!best.TryGetValue(cd.Discipline, out int existing) || cd.Rating > existing)
                {
                    best[cd.Discipline] = cd.Rating;
                }
            }

            return best;
        }

        /// <summary>
        /// True when the account can actually craft a recipe with the given
        /// Disciplines/MinRating (RecipeOption's own fields) - some
        /// character's best rating in at least one of those disciplines
        /// meets or exceeds minRating. Three cases are always true,
        /// deliberately never "blocked":
        /// - <paramref name="bestRatingByDiscipline"/> is null (no snapshot
        ///   discipline data at all - competency UNKNOWN, never penalize
        ///   craft on missing data).
        /// - disciplines is null/empty (defensive; a real RecipeOption
        ///   always has at least one entry).
        /// - every entry in disciplines is a non-levelable tag (MysticForge/
        ///   Achievement/Merchant) - inherently available, nothing to rate.
        /// </summary>
        public static bool AccountCanCraft(
            IReadOnlyList<string> disciplines,
            int minRating,
            IReadOnlyDictionary<string, int> bestRatingByDiscipline)
        {
            if (bestRatingByDiscipline == null)
            {
                return true;
            }

            if (disciplines == null || disciplines.Count == 0)
            {
                return true;
            }

            bool anyRealDiscipline = false;
            foreach (var discipline in disciplines)
            {
                if (discipline == null || NonLevelableDisciplineTags.Contains(discipline))
                {
                    continue;
                }

                anyRealDiscipline = true;
                if (bestRatingByDiscipline.TryGetValue(discipline, out int rating) && rating >= minRating)
                {
                    return true;
                }
            }

            // Every declared discipline was a non-levelable tag - inherently
            // available, not "blocked" (see class doc comment).
            return !anyRealDiscipline;
        }
    }
}
