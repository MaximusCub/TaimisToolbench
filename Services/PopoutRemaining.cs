using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What a popout still has to show after an account sync: the same plan
    /// rows with the quantities the player has since picked up taken off
    /// them, and the finished rows dropped.
    /// <para>
    /// No solve runs. Craft-or-buy decisions, prices and row order are the
    /// plan's, exactly as generated. This only subtracts.
    /// </para>
    /// <para>
    /// Progress is measured as a DELTA against the holding at the previous
    /// sync, not against an absolute count, and the baseline moves forward
    /// every time. Crafting spends the very materials a shopping row was
    /// ticked off for, so an absolute count would bring a finished row back
    /// the moment its item was consumed. A delta never can: a fall in the
    /// holding contributes nothing, and the row stays where the previous
    /// sync left it. Derivation: docs/ARCHITECTURE.md, "Popout windows".
    /// </para>
    /// </summary>
    internal static class PopoutRemaining
    {
        /// <summary>
        /// One sync's outcome: the rows to show now, and the baseline the
        /// next sync measures against.
        /// </summary>
        internal sealed class Result
        {
            public Result(
                IReadOnlyList<PlanRowViewModel> rows, IReadOnlyDictionary<int, int> baseline)
            {
                Rows = rows;
                Baseline = baseline;
            }

            public IReadOnlyList<PlanRowViewModel> Rows { get; private set; }

            public IReadOnlyDictionary<int, int> Baseline { get; private set; }
        }

        /// <summary>
        /// How many of each item the account holds, summed over every
        /// storage location the capture reported. Locations are not
        /// separated on purpose: this feeds a delta, and an item bought into
        /// a bag and later moved to material storage must not read as a
        /// second acquisition.
        /// </summary>
        public static IReadOnlyDictionary<int, int> CountItems(IReadOnlyList<SnapshotItemEntry> items)
        {
            var counts = new Dictionary<int, int>();
            if (items == null)
            {
                return counts;
            }

            foreach (var entry in items)
            {
                if (entry == null || entry.ItemId <= 0 || entry.Count <= 0)
                {
                    continue;
                }

                int running;
                counts.TryGetValue(entry.ItemId, out running);
                counts[entry.ItemId] = running + entry.Count;
            }

            return counts;
        }

        /// <summary>
        /// Applies one sync. Rows whose item the player has acquired enough
        /// of are dropped; a partly-acquired row comes back with the
        /// outstanding quantity and its money columns scaled to it. A row
        /// with no item id of its own is never touched.
        /// </summary>
        public static Result Apply(
            IReadOnlyList<PlanRowViewModel> rows,
            IReadOnlyDictionary<int, int> baseline,
            IReadOnlyDictionary<int, int> current)
        {
            var kept = new List<PlanRowViewModel>();
            if (rows == null)
            {
                return new Result(kept, current ?? new Dictionary<int, int>());
            }

            // One item can head more than one row, so the gain is spent
            // down the section in emission order rather than offered to
            // every row that names the item.
            var unspent = new Dictionary<int, int>();
            foreach (var row in rows)
            {
                if (row == null)
                {
                    continue;
                }

                int itemId = row.ItemId;
                if (itemId <= 0 || row.Quantity <= 0)
                {
                    kept.Add(row);
                    continue;
                }

                if (!unspent.ContainsKey(itemId))
                {
                    unspent[itemId] = Gained(itemId, baseline, current);
                }

                int available = unspent[itemId];
                int spend = available >= row.Quantity ? row.Quantity : available;
                unspent[itemId] = available - spend;

                int remaining = row.Quantity - spend;
                if (remaining <= 0)
                {
                    continue;
                }

                kept.Add(spend == 0 ? row : Scaled(row, remaining));
            }

            return new Result(kept, current ?? new Dictionary<int, int>());
        }

        /// <summary>
        /// What the account has gained since the baseline. A fall in the
        /// holding is zero, never a negative that would put quantity back on
        /// a row.
        /// </summary>
        private static int Gained(
            int itemId,
            IReadOnlyDictionary<int, int> baseline,
            IReadOnlyDictionary<int, int> current)
        {
            int before = Count(baseline, itemId);
            int now = Count(current, itemId);
            int gained = now - before;
            return gained > 0 ? gained : 0;
        }

        private static int Count(IReadOnlyDictionary<int, int> counts, int itemId)
        {
            int value;
            if (counts != null && counts.TryGetValue(itemId, out value))
            {
                return value;
            }

            return 0;
        }

        /// <summary>
        /// The row at a smaller quantity. Money is taken pro rata off the
        /// row's own total rather than recomputed from its Each column: a
        /// bundle price ("2 for 5") has no exact per-unit figure, and
        /// multiplying one out would print a total the row's own columns
        /// contradict.
        /// </summary>
        private static PlanRowViewModel Scaled(PlanRowViewModel row, int remaining)
        {
            var copy = CopyRow(row);
            copy.Quantity = remaining;
            copy.CoinValue = ScaleAmount(row.CoinValue, remaining, row.Quantity);
            copy.CurrencyCosts = ScaleAmounts(row.CurrencyCosts, remaining, row.Quantity);
            return copy;
        }

        private static long ScaleAmount(long amount, int remaining, int quantity)
        {
            if (quantity <= 0 || amount == 0)
            {
                return amount;
            }

            return amount * remaining / quantity;
        }

        private static List<CurrencyAmountViewModel> ScaleAmounts(
            List<CurrencyAmountViewModel> amounts, int remaining, int quantity)
        {
            if (amounts == null)
            {
                return null;
            }

            var scaled = new List<CurrencyAmountViewModel>(amounts.Count);
            foreach (var amount in amounts)
            {
                if (amount == null)
                {
                    continue;
                }

                scaled.Add(new CurrencyAmountViewModel
                {
                    Amount = ScaleAmount(amount.Amount, remaining, quantity),
                    Name = amount.Name,
                    IconUrl = amount.IconUrl,
                    BundleLabel = amount.BundleLabel,
                    UnitRate = amount.UnitRate,
                    OwnedQuantity = amount.OwnedQuantity,
                    RawOwnedQuantity = amount.RawOwnedQuantity,
                });
            }

            return scaled;
        }

        /// <summary>
        /// Every field of the row, so a scaled row differs from the plan's
        /// in exactly the two the caller changes. Pinned field-by-field by
        /// PopoutRemainingTests.ScaledRowCarriesEveryField, which reflects
        /// over the model: a property added to PlanRowViewModel and not
        /// copied here fails that test rather than going silently blank in
        /// the popout.
        /// </summary>
        private static PlanRowViewModel CopyRow(PlanRowViewModel row)
        {
            return new PlanRowViewModel
            {
                RowType = row.RowType,
                ItemId = row.ItemId,
                Label = row.Label,
                Sublabel = row.Sublabel,
                IconUrl = row.IconUrl,
                Rarity = row.Rarity,
                Quantity = row.Quantity,
                CoinValue = row.CoinValue,
                UnitCoinValue = row.UnitCoinValue,
                UnitCoinBundleQuantity = row.UnitCoinBundleQuantity,
                StatusTag = row.StatusTag,
                HintText = row.HintText,
                WikiTarget = row.WikiTarget,
                BadgeText = row.BadgeText,
                CurrencyCosts = row.CurrencyCosts,
                SheetBarterText = row.SheetBarterText,
                UnitCurrencyCosts = row.UnitCurrencyCosts,
                CurrencyOwnedQuantity = row.CurrencyOwnedQuantity,
                CurrencyNeededQuantity = row.CurrencyNeededQuantity,
                CurrencyFullyCovered = row.CurrencyFullyCovered,
                IsBarterItemCost = row.IsBarterItemCost,
                TradeUpCurrencyId = row.TradeUpCurrencyId,
                TradeUpCurrencyName = row.TradeUpCurrencyName,
                TradeUpCurrencyHeld = row.TradeUpCurrencyHeld,
                TradeUpBuysQuantity = row.TradeUpBuysQuantity,
                CurrencyId = row.CurrencyId,
                NonCoinCostKey = row.NonCoinCostKey,
                TooltipText = row.TooltipText,
                FormulaResultIsExact = row.FormulaResultIsExact,
                CharacterAvailabilityText = row.CharacterAvailabilityText,
            };
        }
    }
}
