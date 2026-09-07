using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal class InventoryReducer
    {
        public ReducedTreeResult Reduce(
            RecipeNode tree,
            AccountItemIndex index,
            string activeCharacterName,
            // The Decisions
            // dictionary from a throwaway zero-owned PlanSolver.Solve on the
            // SAME unreduced tree (with forceBuyOnlyNodeIds already applied)
            // - see CraftingPlanPipeline.RunPipelineAsync's zero-owned solve.
            // Keyed
            // by RecipeNode.NodeId, which must already be assigned on
            // `tree` (RecipeNodeIds.Assign) before this call, since it is
            // what CloneNode below preserves onto the clone this method
            // walks. Null reproduces the legacy i==0-primary-option
            // heuristic - see ReduceNodeSourced's own doc comment.
            IReadOnlyDictionary<int, SolverDecision> zeroOwnedDecisions = null)
        {
            // Build a mutable consumption pool: itemId -> source -> remaining
            var pool = new Dictionary<int, Dictionary<string, int>>();
            var usedRaw = new List<UsedMaterial>();
            var ownedUsageByNode = new Dictionary<RecipeNode, int>();

            var clone = CloneNode(tree);

            // Identified on the clone, because that is what the walk below
            // visits. Reference identity: RecipeNode overrides neither
            // Equals nor GetHashCode, same as ownedUsageByNode above.
            var planRoots = new HashSet<RecipeNode>(PlanRootNodes.Of(clone));

            ReduceNodeSourced(clone, index, activeCharacterName, pool, usedRaw, ownedUsageByNode, consumeFromPool: true, zeroOwnedDecisions, planRoots);

            var aggregated = usedRaw
                .GroupBy(u => u.ItemId)
                .Select(g =>
                {
                    var allSources = g
                        .Where(u => u.Sources != null)
                        .SelectMany(u => u.Sources)
                        .GroupBy(s => s.Source, StringComparer.Ordinal)
                        .Select(sg => new MaterialSourceAllocation
                        {
                            Source = sg.Key,
                            Quantity = sg.Sum(a => a.Quantity),
                        })
                        .Where(a => a.Quantity > 0)
                        .OrderBy(a => a.Source, StringComparer.Ordinal)
                        .ToList();

                    return new UsedMaterial
                    {
                        ItemId = g.Key,
                        QuantityUsed = g.Sum(u => u.QuantityUsed),
                        Sources = allSources,
                    };
                })
                .Where(u => u.QuantityUsed > 0)
                .ToList();

            return new ReducedTreeResult
            {
                ReducedTree = clone,
                UsedMaterials = aggregated,
                OwnedQuantityUsedByNode = ownedUsageByNode,
            };
        }

        /// <summary>
        /// Reduces <paramref name="node"/> and its descendants against the
        /// shared <paramref name="pool"/>, lazily initialized per
        /// item/source from <paramref name="index"/>.
        /// <para>
        /// <paramref name="consumeFromPool"/> decides whether THIS node's
        /// own Quantity may be discounted (inherited from the caller) and,
        /// with <paramref name="zeroOwnedDecisions"/>, which recipe option's
        /// descendants may consume the pool. Under a decision guide only the
        /// option that guide chose to Craft may; a node it decided
        /// Buy/Vendor/Unknown lets NO option consume. With no guide (null,
        /// or this node absent from it) the legacy heuristic applies: the
        /// root, then each node's PRIMARY option only. Once an option is
        /// excluded, that holds for its whole subtree. Every option's
        /// CraftsNeeded/ingredient Quantity is still rescaled regardless,
        /// because PlanSolver evaluates every option.
        /// KNOWN RESIDUAL, not guarded or tested: KNOWN-ISSUES #20.
        /// Derivation: docs/ARCHITECTURE.md section 8.2.
        /// </para>
        /// </summary>
        private void ReduceNodeSourced(
            RecipeNode node,
            AccountItemIndex index,
            string activeCharacterName,
            Dictionary<int, Dictionary<string, int>> pool,
            List<UsedMaterial> used,
            Dictionary<RecipeNode, int> ownedUsageByNode,
            bool consumeFromPool,
            IReadOnlyDictionary<int, SolverDecision> zeroOwnedDecisions,
            ISet<RecipeNode> planRootNodes)
        {
            // In the current GW2 recipe model, only "Item" nodes are consumable
            // from inventory and can have recipes. Currency nodes are leaves.
            if (node.IngredientType != "Item")
            {
                return;
            }

            int needed = node.Quantity;
            if (needed <= 0)
            {
                return;
            }

            // A plan root never discounts its own Quantity, whatever the
            // account holds: the user asked for that item and wants it
            // made. Which nodes those are: PlanRootNodes. Descendants are
            // unaffected and reduce as usual.
            if (consumeFromPool && !planRootNodes.Contains(node))
            {
                var prioritized = AccountItemIndex.GetPrioritizedSources(
                    node.Id, index, activeCharacterName);

                var allocations = new List<MaterialSourceAllocation>();
                int totalConsumed = 0;
                int remaining = needed;

                foreach (var source in prioritized)
                {
                    if (remaining <= 0)
                    {
                        break;
                    }

                    int available = GetPoolRemaining(pool, index, node.Id, source);
                    int consume = Math.Min(available, remaining);

                    if (consume > 0)
                    {
                        ConsumeFromPool(pool, index, node.Id, source, consume);
                        allocations.Add(new MaterialSourceAllocation
                        {
                            Source = source,
                            Quantity = consume,
                        });
                        totalConsumed += consume;
                        remaining -= consume;
                    }
                }

                if (totalConsumed > 0)
                {
                    used.Add(new UsedMaterial
                    {
                        ItemId = node.Id,
                        QuantityUsed = totalConsumed,
                        Sources = allocations,
                    });
                    ownedUsageByNode[node] = totalConsumed;
                    node.Quantity -= totalConsumed;
                }
            }

            if (node.Quantity <= 0)
            {
                node.Quantity = 0;
                node.Recipes.Clear();
                return;
            }

            if (node.Recipes.Count == 0)
            {
                return;
            }

            SolverDecision guideDecision = null;
            // Reduce is public API with an IReadOnlyDictionary
            // parameter - PlanSolver never emits a null VALUE, but nothing
            // stops a caller from doing so. TryGetValue alone returns true
            // for an entry whose value IS null, and the code below
            // dereferences guideDecision.Source unconditionally whenever
            // hasGuide is true - the extra null check keeps a
            // maliciously/accidentally null-valued entry falling back to
            // the safe legacy heuristic instead of throwing.
            bool hasGuide = zeroOwnedDecisions != null &&
                zeroOwnedDecisions.TryGetValue(node.NodeId, out guideDecision) &&
                guideDecision != null;

            for (int i = 0; i < node.Recipes.Count; i++)
            {
                var option = node.Recipes[i];
                int origCraftsNeeded = option.CraftsNeeded;
                int newCraftsNeeded = ComputeCraftsNeeded(node.Quantity, option);
                option.CraftsNeeded = newCraftsNeeded;

                bool optionConsumes = hasGuide
                    ? consumeFromPool &&
                        guideDecision.Source == AcquisitionSource.Craft &&
                        option.RecipeId == guideDecision.RecipeId
                    : consumeFromPool && i == 0;

                foreach (var ingredient in option.Ingredients)
                {
                    int perCraft = (ingredient.Quantity + origCraftsNeeded - 1) / origCraftsNeeded;
                    ingredient.Quantity = perCraft * newCraftsNeeded;

                    ReduceNodeSourced(ingredient, index, activeCharacterName, pool, used, ownedUsageByNode, optionConsumes, zeroOwnedDecisions, planRootNodes);
                }
            }
        }

        /// <summary>
        /// Recomputes how many crafting attempts are needed to produce
        /// <paramref name="quantity"/> of a node, using the SAME basis
        /// RecipeService used when it first built this option's
        /// CraftsNeeded/ingredient quantities: the fractional
        /// ExpectedOutputCount when the recipe has one (Mystic Clover-style
        /// EV recipes), falling back to the nominal integer OutputCount
        /// otherwise (a no-op for every ordinary recipe, where the two are
        /// equal). Using a DIFFERENT basis here than RecipeService used
        /// originally would desync origCraftsNeeded's per-craft ingredient
        /// ratio from the reduced tree's new crafts count - CloneOption
        /// once dropped ExpectedOutputCount and silently disabled EV
        /// pricing whenever a snapshot triggered this reduction path.
        /// </summary>
        private static int ComputeCraftsNeeded(int quantity, RecipeOption option)
        {
            double effectiveOutputCount = option.ExpectedOutputCount > 0
                ? option.ExpectedOutputCount
                : option.OutputCount;

            try
            {
                return checked((int)Math.Ceiling((double)quantity / effectiveOutputCount));
            }
            catch (OverflowException)
            {
                // Malformed seed data (an absurdly tiny ExpectedOutputCount)
                // - fall back to the nominal integer output rather than
                // crash the whole reduction.
                return (int)Math.Ceiling((double)quantity / option.OutputCount);
            }
        }

        private static int GetPoolRemaining(
            Dictionary<int, Dictionary<string, int>> pool,
            AccountItemIndex index,
            int itemId,
            string source)
        {
            if (pool.TryGetValue(itemId, out var sourcePool) &&
                sourcePool.TryGetValue(source, out int remaining))
            {
                return Math.Max(0, remaining);
            }

            // First access: initialize from index
            return index.GetQuantity(itemId, source);
        }

        private static void ConsumeFromPool(
            Dictionary<int, Dictionary<string, int>> pool,
            AccountItemIndex index,
            int itemId,
            string source,
            int amount)
        {
            if (!pool.TryGetValue(itemId, out var sourcePool))
            {
                sourcePool = new Dictionary<string, int>(StringComparer.Ordinal);
                pool[itemId] = sourcePool;
            }

            if (!sourcePool.TryGetValue(source, out int remaining))
            {
                remaining = index.GetQuantity(itemId, source);
            }

            sourcePool[source] = Math.Max(0, remaining - amount);
        }

        private static RecipeNode CloneNode(RecipeNode node)
        {
            var clone = new RecipeNode
            {
                Id = node.Id,
                IngredientType = node.IngredientType,
                Quantity = node.Quantity,
                NodeId = node.NodeId,
                // Must be copied explicitly, same as every other field
                // here - see the ExpectedOutputCount comment in CloneOption
                // below for why a field silently missing from this clone is
                // a real, previously-hit bug class in this codebase.
                // AchievementBitDedupPrePass runs on the pre-reduction tree
                // (before this clone is made), so IsAchievementBitDeduped
                // must survive onto the tree PlanSolver/CraftingTreeBuilder
                // actually consume.
                AchievementId = node.AchievementId,
                AchievementBit = node.AchievementBit,
                IsAchievementBitDeduped = node.IsAchievementBitDeduped,
            };

            foreach (var option in node.Recipes)
            {
                clone.Recipes.Add(CloneOption(option));
            }

            return clone;
        }

        private static RecipeOption CloneOption(RecipeOption option)
        {
            var clone = new RecipeOption
            {
                RecipeId = option.RecipeId,
                OutputCount = option.OutputCount,
                CraftsNeeded = option.CraftsNeeded,
                // This field was once silently dropped here, zeroing it
                // out (C# default) on every cloned option - defeating EV
                // pricing (PlanSolver/RecipeService's
                // ExpectedOutputCount-based math) whenever an account
                // snapshot triggered a Reduce() clone, i.e. the normal
                // own-materials path for a real plan.
                ExpectedOutputCount = option.ExpectedOutputCount,
                Disciplines = new List<string>(option.Disciplines),
                MinRating = option.MinRating,
                Flags = new List<string>(option.Flags),
            };

            foreach (var ingredient in option.Ingredients)
            {
                clone.Ingredients.Add(CloneNode(ingredient));
            }

            return clone;
        }
    }
}
