using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal class PlanResultBuilder
    {
        // Disciplines that are informational source tags, not real,
        // player-levelable GW2 crafting disciplines: a recipe carrying one
        // of these is inherently available whenever its ingredients are,
        // with no "learn this recipe" unlock concept at all (mirrors the
        // pre-existing Mystic Forge treatment below).
        private static readonly HashSet<string> InherentlyAvailableDisciplines =
            new HashSet<string> { "MysticForge", "Achievement", "Merchant" };

        // "Achievement"/"Merchant" are informational source tags on seed
        // recipes, and the Mystic Forge is a facility with no rating or
        // unlock concept - none are player-levelable disciplines, so all
        // are filtered out of Required Disciplines. Each recipe's own
        // Disciplines field stays untouched (accurate metadata about that
        // recipe's source).
        private static readonly HashSet<string> NonCraftingDisciplines =
            new HashSet<string> { "Achievement", "Merchant", "MysticForge" };

        public CraftingPlanResult Build(
            CraftingPlan plan,
            RecipeNode treeUsedForSolve,
            IReadOnlyDictionary<int, ItemMetadata> metadata,
            List<UsedMaterial> usedMaterials,
            ISet<int> learnedRecipeIds,
            // Which disciplines the account actually has, used only to
            // break the Pass 2 greedy-cover tie below. Null falls back to
            // the coverage-then-alphabetical order.
            IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines = null)
        {
            var debugLog = new List<string>();

            // A discipline the account has any character trained in is
            // preferred over an equally-covering one nobody has. This
            // never changes which or how many disciplines are required -
            // only, among ties, which name is reported - so it cannot
            // affect cost or decisions. Empty (not null) when no snapshot,
            // preserving the alphabetical fallback.
            var accountDisciplineNames = characterDisciplines != null
                ? new HashSet<string>(
                    characterDisciplines.Where(cd => cd != null).Select(cd => cd.Discipline),
                    StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);

            // Debug: reduction summary
            if (usedMaterials == null)
            {
                debugLog.Add("No inventory reduction (snapshot not provided)");
            }
            else if (usedMaterials.Count == 0)
            {
                debugLog.Add("No inventory reduction (no owned items matched)");
            }
            else
            {
                // Manual loop instead of Select().ToList() - one fewer
                // LINQ iterator per Build().
                var parts = new List<string>(usedMaterials.Count);
                foreach (var used in usedMaterials)
                {
                    parts.Add($"{used.QuantityUsed} of item {used.ItemId}");
                }

                debugLog.Add($"Reduced: used {usedMaterials.Count} owned items ({string.Join(", ", parts)})");
            }

            // Debug: source decisions
            foreach (var step in plan.Steps)
            {
                switch (step.Source)
                {
                    case AcquisitionSource.Craft:
                        debugLog.Add($"Item {step.ItemId} (qty {step.Quantity}): Craft via recipe {step.RecipeId}");
                        break;
                    case AcquisitionSource.BuyFromTp:
                        debugLog.Add($"Item {step.ItemId} (qty {step.Quantity}): BuyFromTp @ {step.UnitCost}c");
                        break;
                    case AcquisitionSource.BuyFromVendor:
                        debugLog.Add($"Item {step.ItemId} (qty {step.Quantity}): BuyFromVendor @ {step.UnitCost}c");
                        break;
                    case AcquisitionSource.Currency:
                        debugLog.Add($"Item {step.ItemId} (qty {step.Quantity}): Currency");
                        break;
                    default:
                        debugLog.Add($"Item {step.ItemId} (qty {step.Quantity}): {step.Source}");
                        break;
                }
            }

            // Derive required disciplines from Craft steps.
            // Goal: produce a minimal, actionable set of disciplines the user needs.
            //   Pass 1 - single-discipline recipes are must-use (no choice).
            //   Pre-cover - remove multi-discipline recipes already coverable by
            //               a Pass 1 discipline; update max rating if needed.
            //   Pass 2 - greedy set cover for remaining uncovered recipes:
            //            highest coverage count, prefer already-selected, alpha.
            // Max MinRating is tracked per selected discipline.
            var craftSteps = plan.Steps.Where(s => s.Source == AcquisitionSource.Craft).ToList();
            var disciplineMap = new Dictionary<string, int>(); // discipline -> max rating

            // One tree walk indexed by RecipeId (see
            // BuildRecipeOptionIndex). Only walk when at least one Craft
            // step exists: with zero craft steps the index is never
            // consulted, and a null tree must not throw then.
            var recipeOptionIndex = craftSteps.Count > 0
                ? BuildRecipeOptionIndex(treeUsedForSolve)
                : new Dictionary<int, RecipeOption>();

            // Resolve craft step options (deduplicated by RecipeId)
            var seenOptionIds = new HashSet<int>();
            var stepOptions = new List<RecipeOption>();
            foreach (var step in craftSteps)
            {
                if (!seenOptionIds.Add(step.RecipeId))
                {
                    continue;
                }

                if (!recipeOptionIndex.TryGetValue(step.RecipeId, out var option))
                {
                    continue;
                }

                // Manual loop instead of Where().ToList() - one fewer
                // LINQ iterator per craft step.
                var realDisciplines = new List<string>();
                foreach (var discipline in option.Disciplines)
                {
                    if (!NonCraftingDisciplines.Contains(discipline))
                    {
                        realDisciplines.Add(discipline);
                    }
                }

                if (realDisciplines.Count == 0)
                {
                    continue;
                }

                stepOptions.Add(new RecipeOption
                {
                    RecipeId = option.RecipeId,
                    Disciplines = realDisciplines,
                    MinRating = option.MinRating,
                });
            }

            // Pass 1: single-discipline recipes (must-use)
            foreach (var option in stepOptions)
            {
                if (option.Disciplines.Count == 1)
                {
                    var disc = option.Disciplines[0];
                    if (!disciplineMap.ContainsKey(disc) || option.MinRating > disciplineMap[disc])
                    {
                        disciplineMap[disc] = option.MinRating;
                    }
                }
            }

            // Pre-cover: multi-discipline recipes already coverable by a Pass 1
            // discipline do not need greedy selection. Pick exactly one covering
            // discipline per recipe (highest current rating, then alpha) and
            // update only that discipline's max rating.
            var uncovered = new List<RecipeOption>(
                stepOptions.Where(o => o.Disciplines.Count > 1));

            uncovered.RemoveAll(o =>
            {
                var covering = o.Disciplines
                    .Where(d => disciplineMap.ContainsKey(d))
                    .OrderByDescending(d => disciplineMap[d])
                    .ThenBy(d => d)
                    .FirstOrDefault();

                if (covering == null)
                {
                    return false;
                }

                if (o.MinRating > disciplineMap[covering])
                {
                    disciplineMap[covering] = o.MinRating;
                }

                return true;
            });

            // Pass 2: greedy set cover for remaining uncovered recipes.
            // Highest coverage count, then prefer already-selected, then alpha.
            while (uncovered.Count > 0)
            {
                // Count how many uncovered recipes each discipline covers
                var freq = new Dictionary<string, int>();
                foreach (var opt in uncovered)
                {
                    foreach (var d in opt.Disciplines)
                    {
                        freq[d] = freq.ContainsKey(d) ? freq[d] + 1 : 1;
                    }
                }

                // Best discipline: highest coverage, then prefer already-
                // selected, then prefer a discipline the account actually
                // has, then alpha.
                string best = freq.Keys
                    .OrderByDescending(d => freq[d])
                    .ThenByDescending(d => disciplineMap.ContainsKey(d) ? 1 : 0)
                    .ThenByDescending(d => accountDisciplineNames.Contains(d) ? 1 : 0)
                    .ThenBy(d => d)
                    .First();

                // Track max MinRating across covered recipes for this discipline
                foreach (var opt in uncovered)
                {
                    if (opt.Disciplines.Contains(best))
                    {
                        if (!disciplineMap.ContainsKey(best) || opt.MinRating > disciplineMap[best])
                        {
                            disciplineMap[best] = opt.MinRating;
                        }
                    }
                }

                uncovered.RemoveAll(o => o.Disciplines.Contains(best));
            }

            var requiredDisciplines = disciplineMap
                .OrderBy(kv => kv.Key)
                .Select(kv => new RequiredDiscipline
                {
                    Discipline = kv.Key,
                    MinRating = kv.Value,
                })
                .ToList();

            // Debug: required disciplines
            if (requiredDisciplines.Count > 0)
            {
                var discParts = requiredDisciplines.Select(d => $"{d.Discipline} ({d.MinRating})");
                debugLog.Add($"Required disciplines: {string.Join(", ", discParts)}");
            }

            // Derive required recipes from Craft steps
            var seenRecipeIds = new HashSet<int>();
            var requiredRecipes = new List<RequiredRecipe>();

            // Output item ids of every fractional-yield Mystic Forge
            // combine chosen in this plan, deduplicated by output id.
            // True multi-outcome gambles never reach the solved tree, so
            // there is nothing here to find for them.
            var seenForgeOutputItemIds = new HashSet<int>();
            var probabilisticForgeOutputItemIds = new List<int>();

            foreach (var step in craftSteps)
            {
                if (!seenRecipeIds.Add(step.RecipeId))
                {
                    continue;
                }

                if (!recipeOptionIndex.TryGetValue(step.RecipeId, out var option))
                {
                    continue;
                }

                if (option.Disciplines.Contains("MysticForge") &&
                    option.ExpectedOutputCount > 0 &&
                    option.ExpectedOutputCount < option.OutputCount &&
                    seenForgeOutputItemIds.Add(step.ItemId))
                {
                    probabilisticForgeOutputItemIds.Add(step.ItemId);
                }

                bool isAutoLearned = option.Flags.Contains("AutoLearned");
                // GW2 API recipe flags include "LearnedFromItem" for a
                // recipe unlocked via a consumable recipe sheet.
                bool isLearnedFromItem = option.Flags.Contains("LearnedFromItem");
                bool? isMissing;
                if (option.Disciplines.Any(d => InherentlyAvailableDisciplines.Contains(d)))
                {
                    // Membership check on the recipe's declared
                    // Disciplines, not a "recipeId < 0" sign check - the
                    // achievement/merchant seed recipes also use negative
                    // ids. All are inherently available - no unlock.
                    isMissing = false;
                }
                else
                {
                    isMissing = learnedRecipeIds != null
                        ? (bool?)!learnedRecipeIds.Contains(step.RecipeId)
                        : null;
                }

                requiredRecipes.Add(new RequiredRecipe
                {
                    RecipeId = step.RecipeId,
                    OutputItemId = step.ItemId,
                    IsAutoLearned = isAutoLearned,
                    IsLearnedFromItem = isLearnedFromItem,
                    MinRating = option.MinRating,
                    Disciplines = new List<string>(option.Disciplines),
                    IsMissing = isMissing,
                });
            }

            // A vendor offer can be gated behind a recipe sheet the account
            // must own before that vendor will trade at all (see
            // VendorOffer.UnlockRecipeItemId). The route is never hidden or
            // repriced for it - the sheet is listed here and checked
            // against the same learned-recipe set a craft recipe is.
            foreach (var step in plan.Steps)
            {
                if (step.Source != AcquisitionSource.BuyFromVendor ||
                    !step.VendorUnlockRecipeId.HasValue ||
                    !step.VendorUnlockRecipeItemId.HasValue)
                {
                    continue;
                }

                int unlockRecipeId = step.VendorUnlockRecipeId.Value;
                if (!seenRecipeIds.Add(unlockRecipeId))
                {
                    continue;
                }

                requiredRecipes.Add(new RequiredRecipe
                {
                    RecipeId = unlockRecipeId,
                    // The SHEET item, not the recipe's own output, so the
                    // row names what the player has to go and buy.
                    OutputItemId = step.VendorUnlockRecipeItemId.Value,
                    IsAutoLearned = false,
                    // False despite the sheet being a consumable: that flag
                    // makes the Missing! link prefix "Recipe: " onto the
                    // OutputItemId's name (WikiLinkBuilder), and this row's
                    // name already starts with one. False links to the
                    // sheet's own page, which is the right page.
                    IsLearnedFromItem = false,
                    MinRating = 0,
                    Disciplines = new List<string>(),
                    IsMissing = learnedRecipeIds != null
                        ? (bool?)!learnedRecipeIds.Contains(unlockRecipeId)
                        : null,
                });
            }

            // Debug: missing recipes
            if (learnedRecipeIds != null)
            {
                var missing = requiredRecipes.Where(r => r.IsMissing == true).ToList();
                if (missing.Count > 0)
                {
                    var parts = missing.Select(r =>
                    {
                        var disc = r.Disciplines.Count > 0 ? r.Disciplines[0] : "Unknown";
                        return $"{r.RecipeId} ({disc} {r.MinRating})";
                    });
                    debugLog.Add($"Missing recipes: {string.Join(", ", parts)}");
                }
            }
            else
            {
                debugLog.Add("Recipe permission not available");
            }

            return new CraftingPlanResult
            {
                Plan = plan,
                ItemMetadata = metadata,
                UsedMaterials = usedMaterials ?? new List<UsedMaterial>(),
                RequiredDisciplines = requiredDisciplines,
                RequiredRecipes = requiredRecipes,
                ProbabilisticForgeOutputItemIds = probabilisticForgeOutputItemIds,
                DebugLog = debugLog,
            };
        }

        // Builds a RecipeId -> RecipeOption index with a single tree walk.
        // Deliberately no null check on root: guarding would swap a
        // fail-loud crash for a quietly-empty index that drops every
        // craft step's recipe/discipline data with no error.
        private static Dictionary<int, RecipeOption> BuildRecipeOptionIndex(RecipeNode root)
        {
            var index = new Dictionary<int, RecipeOption>();
            IndexRecipeOptions(root, index);
            return index;
        }

        // Preorder DFS; first write per RecipeId wins, so when the same
        // RecipeId appears at more than one tree position the indexed
        // option is the first-visited occurrence.
        private static void IndexRecipeOptions(RecipeNode node, Dictionary<int, RecipeOption> index)
        {
            foreach (var option in node.Recipes)
            {
                if (!index.ContainsKey(option.RecipeId))
                {
                    index[option.RecipeId] = option;
                }

                foreach (var ingredient in option.Ingredients)
                {
                    IndexRecipeOptions(ingredient, index);
                }
            }
        }
    }
}
