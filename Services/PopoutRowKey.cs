using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Identity for one popout row, so a tick follows its row across a
    /// re-sort and a re-render instead of staying on a screen position.
    /// <para>
    /// Built from the row's kind, its item id and its label, plus an ordinal
    /// that separates rows those three cannot tell apart. The ordinal is
    /// taken over the section's own emission order, which the sort does not
    /// change (Services/PlanTableSorter reorders the same instances), so the
    /// same row gets the same key whichever column the table is sorted by.
    /// </para>
    /// <para>
    /// The row model carries no recipe id, so two craft steps for one item
    /// made by two different recipes are separated by the ordinal alone.
    /// That holds for as long as a plan emits its steps in a stable order,
    /// which is all this key is asked to survive: a refresh clears every
    /// tick, and a new plan starts a new set.
    /// </para>
    /// </summary>
    internal static class PopoutRowKey
    {
        /// <summary>
        /// A key per row, keyed by the row instance. Reference identity is
        /// the lookup: <see cref="PlanRowViewModel"/> overrides neither
        /// Equals nor GetHashCode, and the sorter hands back the very
        /// instances it was given.
        /// </summary>
        public static Dictionary<PlanRowViewModel, string> Assign(
            IReadOnlyList<PlanRowViewModel> rows)
        {
            var keys = new Dictionary<PlanRowViewModel, string>();
            if (rows == null)
            {
                return keys;
            }

            var seen = new Dictionary<string, int>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null || keys.ContainsKey(row))
                {
                    continue;
                }

                string stem = Stem(row);
                int ordinal;
                seen.TryGetValue(stem, out ordinal);
                seen[stem] = ordinal + 1;
                keys[row] = stem + "#" + ordinal.ToString();
            }

            return keys;
        }

        private static string Stem(PlanRowViewModel row)
        {
            return ((int)row.RowType).ToString()
                + "|" + row.ItemId.ToString()
                + "|" + (row.Label ?? string.Empty);
        }
    }
}
