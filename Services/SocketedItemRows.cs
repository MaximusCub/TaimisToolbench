using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Turns what is fitted into a piece of worn gear into snapshot rows of
    /// its own.
    /// <para>
    /// A rune, sigil, jewel or infusion is a separate item the account
    /// owns. Held only on its host stack's socket lists it is searchable
    /// nowhere and counted nowhere, so a plan that needs one tells the
    /// player to buy another. One row per socket answers both, and the
    /// count is right by construction: six armour slots carrying one rune
    /// are six runes, and four characters wearing that set are twenty-four.
    /// </para>
    /// <para>
    /// Blish-free, so the whole rule is unit-testable (repo invariant).
    /// </para>
    /// </summary>
    internal static class SocketedItemRows
    {
        /// <summary>
        /// Appends one row per distinct item socketed into one stack of
        /// gear. Rows keep the order the sockets were read in, and a socket
        /// list that names the same item twice becomes one row of two
        /// rather than two rows. Nothing is appended for a stack with
        /// nothing socketed, so a slot the capture could not read produces
        /// no claim at all.
        /// </summary>
        /// <param name="hostCount">
        /// How many copies of the gear this stack holds. Every copy carries
        /// these sockets, so it multiplies each socket's count. Equipment
        /// stacks are always one.
        /// </param>
        public static void AddFor(
            List<SnapshotItemEntry> rows,
            int hostItemId,
            string characterName,
            int hostCount,
            IEnumerable<int> upgrades,
            IEnumerable<int> infusions)
        {
            if (rows == null || hostItemId <= 0 || hostCount <= 0)
            {
                return;
            }

            int start = rows.Count;
            string source = AccountItemIndex.SocketedSource(hostItemId, characterName);
            Append(rows, start, source, upgrades, hostCount);
            Append(rows, start, source, infusions, hostCount);
        }

        /// <summary>
        /// Drops every socket row for an item the Legendary Armory already
        /// counts, and names the wearer under the armory's own row instead.
        /// <para>
        /// Legendary runes and sigils sit in the armory the way legendary
        /// gear does: one account-wide entry with its own count, drawn into
        /// as many slots as the account has copies. A row per socket would
        /// multiply that entry by the number of slots using it, which is
        /// the same mistake Services.EquipmentLocationPolicy keeps the gear
        /// itself out of.
        /// </para>
        /// </summary>
        public static void SettleArmoryOwned(
            List<SnapshotItemEntry> items,
            List<SnapshotArmoryEquip> armoryEquipped,
            ISet<int> armoryItemIds)
        {
            if (items == null || armoryItemIds == null || armoryItemIds.Count == 0)
            {
                return;
            }

            int kept = 0;
            for (int i = 0; i < items.Count; i++)
            {
                var entry = items[i];
                if (entry != null
                    && AccountItemIndex.IsSocketedSource(entry.Source)
                    && armoryItemIds.Contains(entry.ItemId))
                {
                    NameWearer(armoryEquipped, entry);
                    continue;
                }

                items[kept] = entry;
                kept++;
            }

            items.RemoveRange(kept, items.Count - kept);
        }

        private static void NameWearer(
            List<SnapshotArmoryEquip> armoryEquipped, SnapshotItemEntry entry)
        {
            if (armoryEquipped == null
                || !AccountItemIndex.TryGetCharacterName(entry.Source, out string wearer)
                || wearer.Length == 0)
            {
                return;
            }

            armoryEquipped.Add(new SnapshotArmoryEquip
            {
                ItemId = entry.ItemId,
                CharacterName = wearer,
            });
        }

        /// <summary>
        /// Adds one socket list to the rows this call has already appended.
        /// The scan is over those rows alone, which is at most the number of
        /// sockets in one piece of gear.
        /// </summary>
        private static void Append(
            List<SnapshotItemEntry> rows,
            int start,
            string source,
            IEnumerable<int> socketed,
            int hostCount)
        {
            if (socketed == null)
            {
                return;
            }

            foreach (int itemId in socketed)
            {
                if (itemId <= 0)
                {
                    continue;
                }

                bool merged = false;
                for (int i = start; i < rows.Count; i++)
                {
                    if (rows[i].ItemId == itemId)
                    {
                        rows[i].Count += hostCount;
                        merged = true;
                        break;
                    }
                }

                if (!merged)
                {
                    rows.Add(new SnapshotItemEntry
                    {
                        ItemId = itemId,
                        Count = hostCount,
                        Source = source,
                    });
                }
            }
        }
    }
}
