using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Builds the shopping-row tooltip's per-currency wallet line(s) from an
    /// already-resolved CurrencyAmountViewModel list - the same list
    /// ShoppingListSectionRenderer.CreateShoppingRow renders in the Total cell
    /// (row.CurrencyCosts). Pure, Blish-free string shaping so both
    /// CreateShoppingRow's initial tooltip build and its AddReellipsis rebuild
    /// call the identical code path: a diverging rebuild silently drops every
    /// currency line on the first resize/settle.
    ///
    /// cc.Amount is this ROW's own currency total (one PlanStep's
    /// VendorCurrencyCosts - see PlanViewModelBuilder.BuildShoppingListSection),
    /// never the whole plan's requirement for that currency id (that figure is
    /// PlanViewModel.CurrencyPlanTotals, which this renderer is never handed),
    /// so the wording below never claims "plan requires"; it states only what
    /// this row's own numbers actually mean.
    /// </summary>
    internal static class ShoppingRowTooltipFormatter
    {
        /// <summary>
        /// One line per currency cost with a resolved wallet holding
        /// (cc.OwnedQuantity.HasValue); a currency with no wallet data at all,
        /// or a non-positive Amount, is silently skipped. Never returns null -
        /// an empty list for "nothing to say" lets callers AddRange without a
        /// null check.
        ///
        /// Every line has to say that the cost is THIS ROW's and that the
        /// holding is the whole WALLET's, because the two are different
        /// scopes and a reader who reads the cost as the plan's total will
        /// buy the wrong things. Why, and the plan-scope pill it mirrors:
        /// docs/ARCHITECTURE.md, "Services Q-Z: relocated design narrative".
        /// On a shortfall row the OwnedQuantity clamp is inert, so
        /// OwnedQuantity already IS the real unclamped holding; on a covered
        /// row the clamp hides any surplus, and RawOwnedQuantity is the only
        /// place it survives.
        /// </summary>
        public static IReadOnlyList<string> BuildCurrencyLines(IReadOnlyList<CurrencyAmountViewModel> currencyCosts)
        {
            if (currencyCosts == null || currencyCosts.Count == 0)
            {
                // Shared empty instance for the common no-cost row (every
                // TP-buy row) - avoids a per-row List<string> allocation
                // that AddReellipsis's closure would otherwise retain for
                // the plan's lifetime for no reason (build-time only, not a
                // hot per-frame path, but free to avoid).
                return System.Array.Empty<string>();
            }

            var lines = new List<string>();
            foreach (var cc in currencyCosts)
            {
                if (!cc.OwnedQuantity.HasValue || cc.Amount <= 0)
                {
                    continue;
                }

                long needed = cc.Amount - cc.OwnedQuantity.Value;
                if (needed > 0)
                {
                    lines.Add(
                        $"{cc.Name}: this row costs {cc.Amount}. You have {cc.OwnedQuantity.Value}"
                        + $" in your wallet and need {needed} more.");
                    continue;
                }

                // RawOwnedQuantity is always set alongside OwnedQuantity by
                // CurrencyDisplayResolver.ResolveAmounts; the ?? fallback
                // only guards against a future caller constructing this
                // view model directly with just OwnedQuantity set.
                long rawHeld = cc.RawOwnedQuantity ?? cc.OwnedQuantity.Value;
                lines.Add(rawHeld > cc.Amount
                    ? $"{cc.Name}: this row costs {cc.Amount}. Your wallet holds {rawHeld}."
                    : $"{cc.Name}: this row costs {cc.Amount}. You have enough in your wallet.");
            }

            return lines;
        }
    }
}
