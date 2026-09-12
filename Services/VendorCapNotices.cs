using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Which vendor purchase caps are genuinely a wait rather than a route.
    /// <para>
    /// The solver emits a TimegatedItem whenever the plan buys a capped item
    /// from that vendor. A TP-listed item can cover the remainder with coin
    /// instead. The field case is Mystic Coin behind a weekly-capped vendor.
    /// That is a price, not a time gate, so it gets no cap notice. A result
    /// with no price data keeps every notice rather than inventing liquidity.
    /// </para>
    /// <para>
    /// PlanViewModelBuilder's Plan Notes and the Crafting Ranker's row notes
    /// both call this. They used to hold one copy each, and nothing checked
    /// that the two stayed the same. Distinct from
    /// PlanViewModel.VendorCapsByItemId, which stays unfiltered: the
    /// value-detail tooltip states the cap only on a node the plan routes
    /// through that vendor, where the fact is still worth stating.
    /// </para>
    /// </summary>
    internal static class VendorCapNotices
    {
        public static IReadOnlyList<TimegatedItem> Filter(CraftingPlanResult result)
        {
            var capped = result?.Plan?.TimegatedItems;
            if (capped == null || capped.Count == 0)
            {
                return Array.Empty<TimegatedItem>();
            }

            var prices = result.SolveContext?.Prices;
            if (prices == null)
            {
                return capped;
            }

            var kept = new List<TimegatedItem>(capped.Count);
            foreach (var item in capped)
            {
                if (item == null)
                {
                    continue;
                }

                bool tpLiquid = prices.TryGetValue(item.ItemId, out var price) &&
                    price != null && (price.BuyInstant > 0 || price.SellInstant > 0);
                if (!tpLiquid)
                {
                    kept.Add(item);
                }
            }

            return kept;
        }
    }
}
