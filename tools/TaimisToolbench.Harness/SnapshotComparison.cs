using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Harness
{
    /// <summary>
    /// Compares two <see cref="AccountSnapshot"/> objects field by field.
    /// </summary>
    /// <remarks>
    /// Written to answer one question: does the paged full-record fetch
    /// produce the snapshot the three narrow calls per character produced?
    /// Every stored field takes part, because a difference in a name or a
    /// socketed id is as much a regression as a missing row.
    /// </remarks>
    internal static class SnapshotComparison
    {
        public static List<string> Differences(AccountSnapshot left, AccountSnapshot right)
        {
            var differences = new List<string>();
            if (left == null || right == null)
            {
                differences.Add("one side is null");
                return differences;
            }

            Scalar(differences, "CoinCopper", left.CoinCopper, right.CoinCopper);
            Scalar(differences, "CharacterCount", left.CharacterCount, right.CharacterCount);
            Scalar(
                differences,
                "IncompleteCharacterCount",
                left.IncompleteCharacterCount,
                right.IncompleteCharacterCount);

            Rows(differences, "Items", left.Items.Select(ItemKey), right.Items.Select(ItemKey));
            Rows(
                differences,
                "Wallet",
                left.Wallet.Select(WalletKey),
                right.Wallet.Select(WalletKey));
            Rows(
                differences,
                "LegendaryArmoryEquipped",
                left.LegendaryArmoryEquipped.Select(ArmoryKey),
                right.LegendaryArmoryEquipped.Select(ArmoryKey));

            if ((left.CharacterDisciplines == null) != (right.CharacterDisciplines == null))
            {
                differences.Add(
                    "CharacterDisciplines: one side is null and the other is a list");
            }
            else if (left.CharacterDisciplines != null)
            {
                Rows(
                    differences,
                    "CharacterDisciplines",
                    left.CharacterDisciplines.Select(DisciplineKey),
                    right.CharacterDisciplines.Select(DisciplineKey));
            }

            return differences;
        }

        private static void Scalar<T>(List<string> differences, string name, T left, T right)
        {
            if (!Equals(left, right))
            {
                differences.Add(name + ": " + left + " against " + right);
            }
        }

        /// <summary>
        /// Reports rows present on one side only, and separately reports a
        /// difference in order. Order is not a correctness property of a
        /// snapshot, so the two are named apart rather than lumped together.
        /// </summary>
        private static void Rows(
            List<string> differences,
            string name,
            IEnumerable<string> left,
            IEnumerable<string> right)
        {
            var leftRows = left.ToList();
            var rightRows = right.ToList();
            var leftSorted = leftRows.OrderBy(r => r, StringComparer.Ordinal).ToList();
            var rightSorted = rightRows.OrderBy(r => r, StringComparer.Ordinal).ToList();

            var onlyLeft = Surplus(leftSorted, rightSorted);
            var onlyRight = Surplus(rightSorted, leftSorted);
            foreach (string row in onlyLeft.Take(5))
            {
                differences.Add(name + ": only on the left: " + row);
            }

            foreach (string row in onlyRight.Take(5))
            {
                differences.Add(name + ": only on the right: " + row);
            }

            if (onlyLeft.Count > 5 || onlyRight.Count > 5)
            {
                differences.Add(
                    name + ": " + onlyLeft.Count + " rows only on the left and "
                    + onlyRight.Count + " only on the right, first five of each shown");
            }

            if (onlyLeft.Count == 0 && onlyRight.Count == 0
                && !leftRows.SequenceEqual(rightRows, StringComparer.Ordinal))
            {
                differences.Add(name + ": same rows in a different order");
            }
        }

        /// <summary>
        /// Rows in <paramref name="from"/> that <paramref name="other"/> does
        /// not also hold, counting duplicates. Both inputs are sorted.
        /// </summary>
        private static List<string> Surplus(List<string> from, List<string> other)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string row in other)
            {
                int seen;
                counts.TryGetValue(row, out seen);
                counts[row] = seen + 1;
            }

            var surplus = new List<string>();
            foreach (string row in from)
            {
                int seen;
                if (counts.TryGetValue(row, out seen) && seen > 0)
                {
                    counts[row] = seen - 1;
                    continue;
                }

                surplus.Add(row);
            }

            return surplus;
        }

        private static string ItemKey(SnapshotItemEntry entry)
        {
            return string.Join(
                "|",
                entry.ItemId.ToString(CultureInfo.InvariantCulture),
                entry.Count.ToString(CultureInfo.InvariantCulture),
                entry.Source,
                Ids(entry.Upgrades),
                Ids(entry.Infusions),
                entry.SkinId.ToString(CultureInfo.InvariantCulture),
                entry.Name,
                entry.IconUrl,
                entry.Rarity,
                entry.SkinName,
                entry.SkinIconUrl);
        }

        private static string WalletKey(SnapshotWalletEntry entry)
        {
            return string.Join(
                "|",
                entry.CurrencyId.ToString(CultureInfo.InvariantCulture),
                entry.Value.ToString(CultureInfo.InvariantCulture),
                entry.CurrencyName,
                entry.IconUrl);
        }

        private static string ArmoryKey(SnapshotArmoryEquip entry)
        {
            return entry.ItemId.ToString(CultureInfo.InvariantCulture) + "|" + entry.CharacterName;
        }

        private static string DisciplineKey(SnapshotCharacterDiscipline entry)
        {
            return string.Join(
                "|",
                entry.CharacterName,
                entry.Discipline,
                entry.Rating.ToString(CultureInfo.InvariantCulture),
                entry.Active.ToString());
        }

        // Null and empty are different states on disk, so they read
        // differently here too.
        private static string Ids(List<int> ids)
        {
            if (ids == null)
            {
                return "null";
            }

            return string.Join(",", ids.Select(i => i.ToString(CultureInfo.InvariantCulture)));
        }
    }
}
