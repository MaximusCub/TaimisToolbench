using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The acquisition subtrees behind a vendor offer's Item cost lines, one
    /// per item id, each built at quantity 1 so the solver can price a line
    /// as (unit cost x count) exactly the way a Trading Post-priced line is
    /// already priced.
    /// <para>
    /// A vendor offer's cost lines are not recipe ingredients, so nothing in
    /// the plan tree describes how the player obtains them; before this the
    /// solver costed a line with no TP price at zero and an offer that
    /// mirrors a recipe and adds a fee looked cheaper than crafting. See
    /// docs/ARCHITECTURE.md section 7.4.
    /// </para>
    /// <para>
    /// Deliberately a SIDE TABLE rather than a field on
    /// <see cref="RecipeNode"/>: that type is reachable from
    /// <see cref="Models.PersistedPlan"/>, so hanging subtrees off it would
    /// move the persisted graph's shape hash, and a shape change that is a
    /// rename or a retype costs every saved result written before it.
    /// </para>
    /// </summary>
    internal sealed class VendorCostLineSubtrees
    {
        /// <summary>
        /// Ceiling on how many distinct item ids get a subtree. Measured on
        /// the Obsidian Heavy Breastplate (item 101521), the deepest shape
        /// the shipped corpus offers: expanding EVERY Item cost line, at
        /// every depth, reaches a fixpoint at 78 item ids / 4,216 nodes on
        /// top of an 842-node plan tree. 256 leaves that case ~3x of head
        /// room while still bounding an unknown future corpus.
        /// </summary>
        public const int DefaultMaxDistinctItems = 256;

        /// <summary>
        /// Ceiling on how deeply one cost-line resolution may nest into
        /// further cost lines. The same measurement above closes at depth 3
        /// (depths 3 and 8 expand the identical 78 ids), so 16 is well past
        /// what the real data needs and exists only to bound a corpus that
        /// grows a longer chain.
        /// </summary>
        public const int DefaultMaxResolutionDepth = 16;

        private readonly Dictionary<int, RecipeNode> _byItemId;

        private VendorCostLineSubtrees(Dictionary<int, RecipeNode> byItemId)
        {
            _byItemId = byItemId;
        }

        public IReadOnlyDictionary<int, RecipeNode> ByItemId => _byItemId;

        public int Count => _byItemId.Count;

        /// <summary>
        /// Takes ownership of <paramref name="subtreesByItemId"/> and numbers
        /// every node across all of them from one sequence. Roots are
        /// numbered in ascending item-id order so the ids are deterministic
        /// regardless of how the caller's dictionary enumerates.
        /// </summary>
        public static VendorCostLineSubtrees Create(IDictionary<int, RecipeNode> subtreesByItemId)
        {
            if (subtreesByItemId == null || subtreesByItemId.Count == 0)
            {
                return null;
            }

            var itemIds = new List<int>(subtreesByItemId.Keys);
            itemIds.Sort();

            var owned = new Dictionary<int, RecipeNode>(subtreesByItemId.Count);
            int nextNodeId = 0;
            foreach (int itemId in itemIds)
            {
                var root = subtreesByItemId[itemId];
                if (root == null)
                {
                    continue;
                }

                RecipeNodeIds.Assign(root, ref nextNodeId);
                owned[itemId] = root;
            }

            return owned.Count == 0 ? null : new VendorCostLineSubtrees(owned);
        }

        /// <summary>
        /// Every Item cost-line id used by any of <paramref name="offers"/>
        /// that <paramref name="alreadyPriced"/> has no usable Trading Post
        /// price for - the lines that cost nothing at all today. An id
        /// already in <paramref name="exclude"/> is skipped, which is how the
        /// caller stops re-expanding what it has already built.
        /// </summary>
        public static HashSet<int> CollectUnpricedCostItemIds(
            IEnumerable<IReadOnlyList<VendorOffer>> offers,
            IReadOnlyDictionary<int, ItemPrice> alreadyPriced,
            PriceBasis priceBasis,
            ICollection<int> exclude)
        {
            var result = new HashSet<int>();
            if (offers == null)
            {
                return result;
            }

            foreach (var offerList in offers)
            {
                if (offerList == null)
                {
                    continue;
                }

                foreach (var offer in offerList)
                {
                    if (offer?.CostLines == null)
                    {
                        continue;
                    }

                    foreach (var cost in offer.CostLines)
                    {
                        if (cost == null ||
                            cost.Count <= 0 ||
                            !string.Equals(cost.Type, "Item", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (exclude != null && exclude.Contains(cost.Id))
                        {
                            continue;
                        }

                        if (alreadyPriced != null &&
                            alreadyPriced.TryGetValue(cost.Id, out var price) &&
                            PlanSolver.GetUnitPrice(price, priceBasis) > 0)
                        {
                            // Already money: it folds into the offer's real
                            // coin cost and needs no acquisition subtree.
                            continue;
                        }

                        result.Add(cost.Id);
                    }
                }
            }

            return result;
        }
    }
}
