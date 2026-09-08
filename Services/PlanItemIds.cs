using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Every item id a finished plan touches, read off its solved tree.
    /// <para>
    /// Two callers want the same set. One warms trading post prices for the
    /// plan a session restored, so the next Generate does not pay for that
    /// fetch. The other decides whether a failed account refresh could
    /// matter to the plan on screen.
    /// </para>
    /// <para>
    /// A multi-item plan carries its roots in MultiItemRoots and leaves
    /// CraftingTree null; a single-item plan does the opposite. Both are
    /// walked, so a caller never has to know which shape it holds.
    /// </para>
    /// </summary>
    internal static class PlanItemIds
    {
        /// <summary>
        /// Distinct positive item ids across the result's whole tree, in
        /// first-seen order. Never null; empty for a null result or one
        /// with no tree at all.
        /// </summary>
        public static IReadOnlyList<int> ForResult(CraftingPlanResult result)
        {
            var ids = new List<int>();
            if (result == null)
            {
                return ids;
            }

            var seen = new HashSet<int>();
            Walk(result.CraftingTree, ids, seen);

            if (result.MultiItemRoots != null)
            {
                foreach (var root in result.MultiItemRoots)
                {
                    Walk(root, ids, seen);
                }
            }

            return ids;
        }

        private static void Walk(CraftingTreeNode node, List<int> ids, HashSet<int> seen)
        {
            if (node == null)
            {
                return;
            }

            if (node.ItemId > 0 && seen.Add(node.ItemId))
            {
                ids.Add(node.ItemId);
            }

            var children = node.Children;
            if (children == null)
            {
                return;
            }

            foreach (var child in children)
            {
                Walk(child, ids, seen);
            }
        }
    }
}
