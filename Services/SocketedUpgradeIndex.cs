using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The socket contents a Snapshot row may show, keyed by item id.
    /// <para>
    /// A Snapshot row is one item id summed over every stack that holds it,
    /// so a row can stand for several physically different objects: two
    /// copies of the same gloves can carry different runes. This index
    /// therefore reports a socket set ONLY when every stack of that id
    /// carries the same one, in the same order, and omits the id entirely
    /// otherwise. Picking one stack's sockets would describe items the row
    /// also covers but does not match.
    /// </para>
    /// <para>
    /// Blish-free, so the whole rule is unit-testable (repo invariant).
    /// </para>
    /// </summary>
    internal static class SocketedUpgradeIndex
    {
        private static readonly Dictionary<int, SocketedUpgradeIds> NoSockets =
            new Dictionary<int, SocketedUpgradeIds>();

        public static IReadOnlyDictionary<int, SocketedUpgradeIds> Build(
            IReadOnlyList<SnapshotItemEntry> items)
        {
            if (items == null || items.Count == 0)
            {
                return NoSockets;
            }

            var agreed = new Dictionary<int, SocketedUpgradeIds>();
            var conflicting = new HashSet<int>();

            foreach (var entry in items)
            {
                if (entry == null || entry.ItemId <= 0 || conflicting.Contains(entry.ItemId))
                {
                    continue;
                }

                // None rather than a fresh pair for the bare case: this
                // walks every row of a snapshot, and the large majority of
                // an account's thousands of rows have nothing socketed.
                var sockets = IsBare(entry)
                    ? SocketedUpgradeIds.None
                    : new SocketedUpgradeIds(entry.Infusions, entry.Upgrades);
                if (!agreed.TryGetValue(entry.ItemId, out var seen))
                {
                    agreed[entry.ItemId] = sockets;
                    continue;
                }

                // A stack that disagrees retires the id for good, including
                // the "one stack is socketed, another is bare" case: an
                // empty set is a set, not a missing reading.
                if (!SameIds(seen.Infusions, sockets.Infusions)
                    || !SameIds(seen.Upgrades, sockets.Upgrades))
                {
                    agreed.Remove(entry.ItemId);
                    conflicting.Add(entry.ItemId);
                }
            }

            var result = new Dictionary<int, SocketedUpgradeIds>(agreed.Count);
            foreach (var pair in agreed)
            {
                if (!pair.Value.IsEmpty)
                {
                    result[pair.Key] = pair.Value;
                }
            }

            return result;
        }

        /// <summary>
        /// Every item id whose stat block this index needs before a hover
        /// can draw it: the socketed components themselves, and their host
        /// items, whose own stat block is what the socket blocks are drawn
        /// inside. Bounded by the number of socketed objects an account
        /// holds outside its equipped gear, not by the size of the
        /// snapshot.
        /// </summary>
        public static IReadOnlyList<int> ItemIdsToResolve(
            IReadOnlyDictionary<int, SocketedUpgradeIds> index)
        {
            var ids = new List<int>();
            if (index == null)
            {
                return ids;
            }

            var seen = new HashSet<int>();
            foreach (var pair in index)
            {
                Add(ids, seen, pair.Key);
                foreach (int id in pair.Value.Infusions)
                {
                    Add(ids, seen, id);
                }

                foreach (int id in pair.Value.Upgrades)
                {
                    Add(ids, seen, id);
                }
            }

            return ids;
        }

        private static bool IsBare(SnapshotItemEntry entry)
        {
            return (entry.Infusions == null || entry.Infusions.Count == 0)
                && (entry.Upgrades == null || entry.Upgrades.Count == 0);
        }

        private static void Add(List<int> ids, HashSet<int> seen, int id)
        {
            if (id > 0 && seen.Add(id))
            {
                ids.Add(id);
            }
        }

        private static bool SameIds(IReadOnlyList<int> left, IReadOnlyList<int> right)
        {
            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (left[i] != right[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
