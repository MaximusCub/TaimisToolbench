using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// A tree row the plan buys from a vendor for one wallet currency and
    /// nothing else: no coin, no items, no barter line. Three Secrets of
    /// the Obscure materials work this way (a flat 250 map currency each),
    /// and so does any future item a vendor prices the same way.
    /// <para>
    /// The module deliberately does not plan how the player earns the
    /// currency. That is a playstyle choice with several routes and no
    /// cheapest answer, so the row states what the held currency buys now
    /// and leaves the earning to the player.
    /// </para>
    /// </summary>
    internal static class CurrencyTradeUpRow
    {
        /// <summary>
        /// True when <paramref name="node"/> is bought from a vendor for a
        /// single wallet currency and nothing else. A zero SubtreeCost is
        /// part of the test: any coin part, or any item cost the module
        /// could price, lands in SubtreeCost and makes the row something
        /// other than a straight currency trade.
        /// </summary>
        internal static bool Matches(CraftingTreeNode node)
        {
            return node != null &&
                node.Decision == CraftingDecision.BuyFromVendor &&
                !node.VendorHasBarterItemCost &&
                node.SubtreeCost.HasValue &&
                node.SubtreeCost.Value == 0 &&
                node.Quantity > 0 &&
                node.VendorCurrencyCosts != null &&
                node.VendorCurrencyCosts.Count == 1 &&
                node.VendorCurrencyCosts[0] != null &&
                node.VendorCurrencyCosts[0].Count > 0;
        }

        /// <summary>
        /// How many of this row's items the player's held currency pays
        /// for right now, never more than the row asks for. False when the
        /// row is not a currency trade or the holding is unknown, which is
        /// not the same as a holding of zero.
        /// </summary>
        internal static bool TryGetAffordableNow(
            CraftingTreeNode node,
            IReadOnlyDictionary<int, int> ownedCurrencyAmounts,
            out int affordable)
        {
            affordable = 0;
            if (!Matches(node) || ownedCurrencyAmounts == null)
            {
                return false;
            }

            var cost = node.VendorCurrencyCosts[0];
            if (!ownedCurrencyAmounts.TryGetValue(cost.Id, out int held) || held < 0)
            {
                return false;
            }

            // Multiply before dividing rather than deriving a per-unit
            // price first: the row's cost need not divide evenly by its
            // quantity, and a rounded per-unit price would overstate what
            // the holding buys. Widened to long because held x quantity
            // overflows int well inside real wallet sizes.
            long buys = (long)held * node.Quantity / cost.Count;
            affordable = buys >= node.Quantity ? node.Quantity : (int)buys;
            return true;
        }
    }
}
