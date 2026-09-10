using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// One class-level structural walk of the ENTIRE object graph a
    /// deserialized <see cref="PersistedPlan"/> carries, run at the
    /// deserialization boundary
    /// (<see cref="PlanStoreHelpers.DeserializePersistedPlan"/>) before the
    /// file is accepted at all.
    /// <para>
    /// Every check below exists because a specific production path
    /// dereferences that exact field with no null guard, on an assumption
    /// that holds for every solver-BUILT result. See each check's own
    /// inline comment for the site it protects.
    /// </para>
    /// <para>
    /// Validation failure is the corrupt-file path: the caller throws,
    /// <see cref="PlanStore.LoadLatest"/> catches, logs one Warn and
    /// returns null. Never a partial accept - one invalid field rejects the
    /// whole file.
    /// </para>
    /// <para>Derivation: docs/ARCHITECTURE.md section 12.</para>
    /// </summary>
    internal static class PlanStructuralValidator
    {
        // 10x+ any realistic GW2 crafting tree depth (real trees observed
        // during development top out around a dozen levels - raw material
        // -> refined material -> component -> sub-assembly -> final item).
        // The JSON reader's MaxDepth is raised to 512 in
        // PlanStoreHelpers (the default 64 rejected the game's deepest
        // real chain, +24 Agony Infusion at 23 recipe levels x ~3 JSON
        // levels per node), so this walk enforces the domain-level bound
        // itself rather than relying on any upstream protection. A
        // depth this shallow is also nowhere near a real stack-overflow
        // risk (a few hundred bytes per frame at most), so this exists
        // purely to fail loudly and reject the file rather than to guard
        // against an actual crash.
        private const int MaxTreeDepth = 200;

        /// <summary>
        /// True when every null-assuming invariant the restore-render path
        /// and the local override re-solve path rely on holds for the
        /// entire <paramref name="plan"/> graph. <paramref name="reason"/>
        /// is a short, human-readable (Warn-log-only, never user-facing)
        /// description of the first violation found; callers must treat any
        /// false result as "reject the whole file", never a partial accept.
        /// </summary>
        internal static bool IsStructurallyValid(PersistedPlan plan, out string reason)
        {
            // PersistedPlan.ValueOwnMaterials
            // (a non-nullable bool, same shape as UseOwnMaterials/
            // PriceBasis above) is intentionally NOT checked here - a plain bool
            // can never produce the null-dereference class of bug this validator
            // exists to catch, so it needs no entry, same as its two siblings.
            reason = null;
            var result = plan?.Result;
            var craftingPlan = result?.Plan;
            if (craftingPlan == null)
            {
                // Already checked by DeserializePersistedPlan's own
                // structural gate before this runs - re-checked here too so
                // this method stays safe to call (and test) in isolation.
                reason = "missing Result.Plan";
                return false;
            }

            // PlanViewModelBuilder.Build reads Plan.Steps unconditionally
            // (result.Plan.Steps.Where(...)/.Select(...), no null guard) -
            // the list itself must be non-null, and PlanResultBuilder.Build
            // (foreach (var step in plan.Steps) { switch (step.Source) ... })
            // dereferences every entry with no per-entry null check either.
            if (craftingPlan.Steps == null)
            {
                reason = "Plan.Steps is null";
                return false;
            }

            if (!NoNullEntries(craftingPlan.Steps, "Plan.Steps", out reason))
            {
                return false;
            }

            // PlanViewModelBuilder.BuildShoppingListSection/
            // BuildCraftingStepsSection pass these straight into
            // CurrencyDisplayResolver.ResolveAmounts/ResolveUnitAmounts,
            // which iterate every line with no per-entry null check.
            foreach (var step in craftingPlan.Steps)
            {
                if (!NoNullEntries(step.VendorCurrencyCosts, "PlanStep.VendorCurrencyCosts", out reason))
                {
                    return false;
                }

                if (!NoNullEntries(step.VendorOfferCurrencyCostLinesPerBatch, "PlanStep.VendorOfferCurrencyCostLinesPerBatch", out reason))
                {
                    return false;
                }

                if (!NoNullEntries(step.VendorBarterItemCosts, "PlanStep.VendorBarterItemCosts", out reason))
                {
                    return false;
                }
            }

            // PlanViewModelBuilder.BuildSummarySection/BuildCraftingStepsSection
            // both null-check the LIST before iterating, but dereference
            // every entry (cc.CurrencyId, timegated.ItemId, ...) with no
            // per-entry null check.
            if (!NoNullEntries(craftingPlan.CurrencyCosts, "Plan.CurrencyCosts", out reason))
            {
                return false;
            }

            if (!NoNullEntries(craftingPlan.BarterItemCosts, "Plan.BarterItemCosts", out reason))
            {
                return false;
            }

            if (!NoNullEntries(craftingPlan.TimegatedItems, "Plan.TimegatedItems", out reason))
            {
                return false;
            }

            // PlanViewModelBuilder.BuildUsedMaterialsSection/
            // BuildDisciplinesSection/BuildRecipesSection/BuildCraftingStepsSection
            // all null-check the LIST before iterating, but dereference
            // every entry with no per-entry null check (um.ItemId,
            // disc.Discipline, recipe.Disciplines, ...).
            if (!NoNullEntries(result.UsedMaterials, "UsedMaterials", out reason))
            {
                return false;
            }

            if (!NoNullEntries(result.RequiredDisciplines, "RequiredDisciplines", out reason))
            {
                return false;
            }

            if (!NoNullEntries(result.RequiredRecipes, "RequiredRecipes", out reason))
            {
                return false;
            }

            // Quality-audit B2 (KNOWN-ISSUES #53): these four lists are
            // not recomputed on the restore path and had the same per-entry
            // gap as the checks above - BuildNotesSection dereferences each
            // entry unguarded.
            if (!NoNullEntries(result.CompetencyOpportunities, "CompetencyOpportunities", out reason))
            {
                return false;
            }

            if (!NoNullEntries(result.ExcessCraftOutputs, "ExcessCraftOutputs", out reason))
            {
                return false;
            }

            if (!NoNullEntries(result.RecipeSheetSavingsOpportunities, "RecipeSheetSavingsOpportunities", out reason))
            {
                return false;
            }

            if (!NoNullEntries(result.MissingRecipeSheetSources, "MissingRecipeSheetSources", out reason))
            {
                return false;
            }

            if (!NoNullEntries(result.SeasonalVendorTips, "SeasonalVendorTips", out reason))
            {
                return false;
            }

            // PlanViewModelBuilder.BuildMultiItemTitle dereferences
            // items[0].ItemId with no null check once isMultiItem gates on
            // Count > 1.
            if (!NoNullEntries(result.RequestedItems, "RequestedItems", out reason))
            {
                return false;
            }

            // PlanViewModelBuilder.ResolveName/ResolveIconUrl/ResolveRarity
            // and CraftingTreeBuilder's own copies all call
            // metadata.TryGetValue(id, out var meta) then dereference
            // meta.Name/meta.IconUrl/meta.Rarity with no null check on meta
            // itself - a dictionary VALUE of null (distinct from a missing
            // key, which is already handled) would NRE.
            if (!NoNullValues(result.ItemMetadata, "ItemMetadata", out reason))
            {
                return false;
            }

            // CurrencyDisplayResolver.ResolveName/ResolveIconUrl have the
            // exact same meta-value-null gap as ItemMetadata above.
            if (!NoNullValues(result.CurrencyMetadata, "CurrencyMetadata", out reason))
            {
                return false;
            }

            // The primary reported bug: a null entry inside
            // CraftingTreeNode.Children at any depth is invisible to
            // PlanViewModelBuilder's reference-copying vm build (TreeRoot =
            // result.CraftingTree) and only ever dereferenced once
            // RenderTreeNode actually walks that far - which, for a
            // default-collapsed depth-2+ node, can happen long after every
            // existing try/catch has already returned, from an unguarded
            // "Expand All"/per-node-toggle Click handler.
            if (result.CraftingTree != null &&
                !IsValidCraftingTreeNode(result.CraftingTree, 0, "CraftingTree", out reason))
            {
                return false;
            }

            // Multi-item plans: the same tree, N times over - never
            // touched by PlanViewModelBuilder except by reference either.
            if (!NoNullEntries(result.MultiItemRoots, "MultiItemRoots", out reason))
            {
                return false;
            }

            if (result.MultiItemRoots != null)
            {
                for (int i = 0; i < result.MultiItemRoots.Count; i++)
                {
                    if (!IsValidCraftingTreeNode(result.MultiItemRoots[i], 0, $"MultiItemRoots[{i}]", out reason))
                    {
                        return false;
                    }
                }
            }

            // The local override re-solve path (a plain pill click, a
            // Best Path/Craft All/Buy All preset) needs its own graph -
            // see IsValidSolveContext's own doc comment.
            if (result.SolveContext != null && !IsValidSolveContext(result.SolveContext, out reason))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Recursively validates one <see cref="CraftingTreeNode"/> subtree
        /// (the display tree - <see cref="CraftingPlanResult.CraftingTree"/>
        /// or one element of <see cref="CraftingPlanResult.MultiItemRoots"/>).
        /// <see cref="CraftingTreeNode.Children"/>'s own setter already
        /// coerces a null value to <c>Array.Empty</c> (see that property's
        /// doc comment), so a literal null Children LIST can never actually
        /// exist on a deserialized instance - only a null ENTRY within an
        /// otherwise non-null Children list is reachable, which is exactly
        /// what <c>Views/Rendering/TreeSectionController.cs</c>'s
        /// <c>node.Children.Count</c>/<c>foreach (var child in
        /// state.Node.Children)</c> call sites (the Expand All button,
        /// ~line 416; the per-node toggle, ~line 835) crash on.
        /// </summary>
        private static bool IsValidCraftingTreeNode(CraftingTreeNode node, int depth, string path, out string reason)
        {
            reason = null;
            if (node == null)
            {
                reason = $"{path} is null";
                return false;
            }

            if (depth > MaxTreeDepth)
            {
                reason = $"{path} exceeds max tree depth ({MaxTreeDepth})";
                return false;
            }

            // TreeSectionController.RenderTreeNode passes this straight into
            // CurrencyDisplayResolver.ResolveAmounts/ResolveTreeNodeUnitAmounts,
            // which iterate every line with no per-entry null check.
            if (!NoNullEntries(node.VendorCurrencyCosts, $"{path}.VendorCurrencyCosts", out reason))
            {
                return false;
            }

            var children = node.Children;
            if (children == null)
            {
                return true; // Defensive only - see this method's own doc comment.
            }

            for (int i = 0; i < children.Count; i++)
            {
                if (!IsValidCraftingTreeNode(children[i], depth + 1, $"{path}.Children[{i}]", out reason))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Validates a <see cref="PlanSolveContext"/> - everything a local
        /// override re-solve (<c>CraftingPlanPipeline.ResolveWithOverrides</c>,
        /// reached from a plain decision-pill click or the Best Path preset)
        /// or a preset build (<c>CraftingPlanPipeline.BuildPresetOverrides</c>,
        /// reached from the Craft All/Buy All buttons, UNGUARDED - it runs
        /// before <c>TreeSectionController.ApplyOverridesAndResolve</c>'s own
        /// try/catch is ever entered) dereferences without a null check.
        /// </summary>
        private static bool IsValidSolveContext(PlanSolveContext context, out string reason)
        {
            reason = null;

            // PlanSolver.Evaluate/CraftingTreeBuilder.BuildNode/
            // CraftingPlanPipeline.CollectPresetOverrides all walk
            // node.Recipes/recipe.Ingredients unconditionally, for the
            // WHOLE tree, on every single override re-solve (not gated on
            // there being any Craft step) - so Tree must always be a fully
            // valid graph whenever a SolveContext is present at all.
            if (!IsValidRecipeNode(context.Tree, 0, "SolveContext.Tree", out reason))
            {
                return false;
            }

            // PlanSolver.GetBuyCost (called from Evaluate on every node) and
            // CraftingPlanPipeline.CollectPresetOverrides both call
            // prices.TryGetValue(...) with no null check on the dictionary
            // itself - a null Prices would NRE on the very first node of
            // the very first override click. A found entry whose VALUE is
            // null then NREs inside PlanSolver.GetUnitPrice (price.SellInstant/
            // price.BuyInstant), also with no null check.
            if (context.Prices == null)
            {
                reason = "SolveContext.Prices is null";
                return false;
            }

            if (!NoNullValues(context.Prices, "SolveContext.Prices", out reason))
            {
                return false;
            }

            // VendorBatchSolver.EvaluateVendorOffers already treats a null
            // VendorOffers DICTIONARY as "no vendor offers" (explicit null
            // check) - but a null LIST value for a present key, or a null
            // VendorOffer entry within an otherwise non-null list, both NRE
            // at its own "foreach (var offer in offers) { offer.OutputCount
            // ... }" with no per-entry guard. The dictionary is keyed by
            // item id, so - same reasoning as NoNullValues' own doc comment
            // - the key is deliberately left out of reason (a Warn-level
            // ModuleLog line the Log tab shows the user).
            if (context.VendorOffers != null)
            {
                foreach (var kvp in context.VendorOffers)
                {
                    if (kvp.Value == null)
                    {
                        reason = "SolveContext.VendorOffers has a null offer list for one item";
                        return false;
                    }

                    if (!NoNullEntries(kvp.Value, "SolveContext.VendorOffers[...]", out reason))
                    {
                        return false;
                    }
                }
            }

            // CraftingTreeBuilder.ResolveName/ResolveIconUrl/ResolveRarity
            // have the exact same meta-value-null gap as
            // CraftingPlanResult.ItemMetadata above - reached on every
            // override re-solve, not just the original Generate.
            if (!NoNullValues(context.Metadata, "SolveContext.Metadata", out reason))
            {
                return false;
            }

            if (!NoNullValues(context.CurrencyMetadata, "SolveContext.CurrencyMetadata", out reason))
            {
                return false;
            }

            // Carried forward verbatim into the NEXT result.RequestedItems
            // by ResolveWithOverrides (result.RequestedItems =
            // context.RequestedItems) - see the matching check on
            // CraftingPlanResult.RequestedItems above for why a null entry
            // there NREs.
            if (!NoNullEntries(context.RequestedItems, "SolveContext.RequestedItems", out reason))
            {
                return false;
            }

            // SolveContext.UsedMaterials is a
            // SEPARATELY serialized copy of the same list as
            // CraftingPlanResult.UsedMaterials above (Newtonsoft writes no
            // $ref by default; PlanStoreHelpers' reader settings raise
            // only MaxDepth and leave reference handling alone) - a plan.json
            // with a clean Result.UsedMaterials but a null entry inside
            // Result.SolveContext.UsedMaterials sails through the check
            // above untouched. Every override re-solve
            // (ResolveWithOverrides) passes context.UsedMaterials straight
            // into PlanResultBuilder.Build ("foreach (var used in
            // usedMaterials) { ... used.ItemId ... }", no per-entry null
            // check) and, for a single-item context, into
            // SellSideEconomics.ComputeMaterialOpportunityCost
            // ("used.ItemId"/"used.QuantityUsed", also no per-entry check) -
            // both reachable from a plain decision-pill click, not just the
            // Craft All/Buy All presets this doc comment already covers.
            if (!NoNullEntries(context.UsedMaterials, "SolveContext.UsedMaterials", out reason))
            {
                return false;
            }

            // UnreducedTree is walked by
            // ResolveWithOverrides' guideSolve (_solver.Solve) and
            // re-reduction (_reducer.Reduce) on EVERY override re-solve of
            // a restored plan whenever it is set (see
            // PlanSolveContext.UnreducedTree's own doc comment) - the exact
            // same unconditional Recipes/Ingredients walk as Tree above.
            // Null is valid here (the force-buy pre-pass didn't run at
            // generation time), so only validate when present.
            if (context.UnreducedTree != null &&
                !IsValidRecipeNode(context.UnreducedTree, 0, "SolveContext.UnreducedTree", out reason))
            {
                return false;
            }

            // AccountItemIndex's constructor (Services/AccountItemIndex.cs)
            // null-checks the LIST but not each entry - "entry.Count" on a
            // null entry NREs on the very first ResolveWithOverrides call
            // that re-reduces (see the UnreducedTree check above). A null
            // list itself is fine: AccountItemIndex(null) treats it as "no
            // owned items".
            if (!NoNullEntries(context.AccountItems, "SolveContext.AccountItems", out reason))
            {
                return false;
            }

            // UnreducedTree and AccountItems are always set together at
            // generation time (both gated on useForceBuyPrePass - see
            // CraftingPlanPipeline's two matching UnreducedTree/AccountItems
            // assignments). A restored file with UnreducedTree set but
            // AccountItems null would otherwise degrade SILENTLY instead of
            // crashing: AccountItemIndex(null) builds an empty index, so
            // every re-reduction re-prices owned materials as if none were
            // owned. Reject the file instead, same as any other
            // null-dereference class this validator exists to catch.
            if (context.UnreducedTree != null && context.AccountItems == null)
            {
                reason = "SolveContext.UnreducedTree is set but SolveContext.AccountItems is null";
                return false;
            }

            return true;
        }

        /// <summary>
        /// Recursively validates one <see cref="RecipeNode"/> subtree (the
        /// solve tree - <see cref="PlanSolveContext.Tree"/>). Unlike
        /// <see cref="CraftingTreeNode.Children"/>, neither
        /// <see cref="RecipeNode.Recipes"/> nor
        /// <see cref="RecipeOption.Ingredients"/>/<see cref="RecipeOption.Disciplines"/>/
        /// <see cref="RecipeOption.Flags"/> have a null-coalescing setter (they
        /// are plain auto-properties with a <c>= new List&lt;T&gt;()</c>
        /// initializer that Newtonsoft overwrites verbatim for an explicit
        /// JSON <c>null</c>) - so a literal null LIST is genuinely reachable
        /// on any of these, not just a null entry within one.
        /// </summary>
        private static bool IsValidRecipeNode(RecipeNode node, int depth, string path, out string reason)
        {
            reason = null;
            if (node == null)
            {
                reason = $"{path} is null";
                return false;
            }

            if (depth > MaxTreeDepth)
            {
                reason = $"{path} exceeds max tree depth ({MaxTreeDepth})";
                return false;
            }

            // PlanSolver.Evaluate/IndexRecipeOptions/CraftingTreeBuilder.
            // BuildNode/CollectPresetOverrides all do "foreach (var recipe
            // in node.Recipes)" with no null check on the list itself.
            if (node.Recipes == null)
            {
                reason = $"{path}.Recipes is null";
                return false;
            }

            for (int i = 0; i < node.Recipes.Count; i++)
            {
                var option = node.Recipes[i];
                string optionPath = $"{path}.Recipes[{i}]";
                if (option == null)
                {
                    reason = $"{optionPath} is null";
                    return false;
                }

                // PlanResultBuilder.Build reads option.Disciplines/
                // option.Flags unconditionally (foreach (var discipline in
                // option.Disciplines), option.Flags.Contains("AutoLearned"))
                // once a Craft step resolves to this exact RecipeOption -
                // reachable from a restored plan whose Steps includes any
                // Craft-sourced step.
                if (option.Disciplines == null)
                {
                    reason = $"{optionPath}.Disciplines is null";
                    return false;
                }

                if (option.Flags == null)
                {
                    reason = $"{optionPath}.Flags is null";
                    return false;
                }

                // PlanSolver.Evaluate/IndexRecipeOptions/CraftingTreeBuilder.
                // BuildChildren/CollectPresetOverrides all do "foreach (var
                // ingredient in recipe.Ingredients)" with no null check on
                // the list itself.
                if (option.Ingredients == null)
                {
                    reason = $"{optionPath}.Ingredients is null";
                    return false;
                }

                for (int j = 0; j < option.Ingredients.Count; j++)
                {
                    if (!IsValidRecipeNode(option.Ingredients[j], depth + 1, $"{optionPath}.Ingredients[{j}]", out reason))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// True when <paramref name="list"/> is null (every call site above
        /// that uses this has already separately rejected a null list where
        /// non-null is actually required - this helper only ever runs on a
        /// field the caller has decided is optional) or contains no null
        /// entries.
        /// </summary>
        private static bool NoNullEntries<T>(IReadOnlyList<T> list, string fieldName, out string reason)
            where T : class
        {
            reason = null;
            if (list == null)
            {
                return true;
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] == null)
                {
                    reason = $"{fieldName}[{i}] is null";
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when <paramref name="dict"/> is null or contains no null
        /// VALUES (a missing key is never a problem - every real reader
        /// below this validator already guards a missing key with its own
        /// TryGetValue check; only a present key whose value is null is the
        /// unguarded case). Deliberately does NOT include the offending key
        /// in <paramref name="reason"/>: every dictionary this is called on
        /// (ItemMetadata/CurrencyMetadata/Prices/VendorOffers/Metadata) is
        /// keyed by an item or currency id, and this reason string is a
        /// Warn-level ModuleLog line the Log tab shows the user - the repo
        /// invariant that item/currency/vendor ids are internal-only applies
        /// there exactly as much as to any other UI surface.
        /// </summary>
        private static bool NoNullValues<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> dict, string fieldName, out string reason)
            where TValue : class
        {
            reason = null;
            if (dict == null)
            {
                return true;
            }

            foreach (var kvp in dict)
            {
                if (kvp.Value == null)
                {
                    reason = $"{fieldName} has a null value for one entry";
                    return false;
                }
            }

            return true;
        }
    }
}
