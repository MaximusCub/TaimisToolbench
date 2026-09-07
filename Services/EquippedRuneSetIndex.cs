using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// How many pieces of one rune a character wears, for the rows where
    /// that question has a single answer.
    /// <para>
    /// A rune set counter is character state, not item state. A Snapshot
    /// row is one item id summed over every stack that holds it, so the
    /// count is knowable only when every stack of that id sits in ONE
    /// place and that place is a named character's worn gear. Then the
    /// character is unambiguous and the tooltip can count that character's
    /// equipped pieces. An id held anywhere else, or in more than one
    /// place, is absent from this index and its tooltip says nothing about
    /// a set.
    /// </para>
    /// <para>
    /// Blish-free, so the whole rule is unit-testable (repo invariant),
    /// same precedent as SocketedUpgradeIndex.
    /// </para>
    /// </summary>
    internal sealed class EquippedRuneSetIndex
    {
        public static readonly EquippedRuneSetIndex Empty = new EquippedRuneSetIndex(null);

        // Item id -> the one "Equipped:<name>" source every stack of it
        // came from. Bounded by the account's worn gear, not by the size of
        // the snapshot: an id is added only when a stack of it is worn, and
        // removed the moment a stack turns up anywhere else.
        private readonly Dictionary<int, string> _soleEquipmentSource;

        // "Equipped:<name>" -> rune item id -> pieces that character wears
        // carrying it. One inner map per character.
        private readonly Dictionary<string, Dictionary<int, int>> _wornCounts;

        public EquippedRuneSetIndex(IReadOnlyList<SnapshotItemEntry> items)
        {
            _soleEquipmentSource = new Dictionary<int, string>();
            _wornCounts = new Dictionary<string, Dictionary<int, int>>(StringComparer.Ordinal);

            if (items == null || items.Count == 0)
            {
                return;
            }

            foreach (var entry in items)
            {
                if (!Counts(entry) || !AccountItemIndex.IsEquipmentSource(entry.Source))
                {
                    continue;
                }

                _soleEquipmentSource[entry.ItemId] = entry.Source;
                AddWornUpgrades(entry);
            }

            if (_soleEquipmentSource.Count == 0)
            {
                return;
            }

            foreach (var entry in items)
            {
                if (!Counts(entry))
                {
                    continue;
                }

                if (_soleEquipmentSource.TryGetValue(entry.ItemId, out string worn)
                    && !string.Equals(worn, entry.Source, StringComparison.Ordinal))
                {
                    _soleEquipmentSource.Remove(entry.ItemId);
                }
            }
        }

        /// <summary>
        /// How many pieces the wearer of <paramref name="hostItemId"/> has
        /// equipped carrying <paramref name="runeItemId"/>, or 0 when the
        /// host is not uniquely worn by one character. 0 means "not
        /// knowable" as much as it means "none", and both cases draw the
        /// tooltip the module drew before this index existed.
        /// <para>
        /// A lower bound, not always the exact figure. An equipment slot
        /// drawing its item from the account-wide Legendary Armory is left
        /// out of the snapshot entirely, because counting it would multiply
        /// one legendary by the number of slots using it - see
        /// Gw2AccountSnapshotService.FetchCharacterEquipmentItemsAsync. So
        /// a character in mixed legendary and non-legendary armour reports
        /// only the pieces the snapshot can see, and the tooltip lights too
        /// few tiers rather than too many.
        /// </para>
        /// </summary>
        public int WornCopies(int hostItemId, int runeItemId)
        {
            if (runeItemId <= 0
                || !_soleEquipmentSource.TryGetValue(hostItemId, out string source)
                || !_wornCounts.TryGetValue(source, out var counts))
            {
                return 0;
            }

            return counts.TryGetValue(runeItemId, out int worn) ? worn : 0;
        }

        private void AddWornUpgrades(SnapshotItemEntry entry)
        {
            var upgrades = entry.Upgrades;
            if (upgrades == null || upgrades.Count == 0)
            {
                return;
            }

            if (!_wornCounts.TryGetValue(entry.Source, out var counts))
            {
                counts = new Dictionary<int, int>();
                _wornCounts[entry.Source] = counts;
            }

            foreach (int id in upgrades)
            {
                if (id <= 0)
                {
                    continue;
                }

                // Every copy in the stack carries these sockets, and an
                // equipment stack is one slot holding one piece, so the
                // stack's own count is how many pieces this adds.
                counts[id] = (counts.TryGetValue(id, out int seen) ? seen : 0) + entry.Count;
            }
        }

        /// <summary>
        /// True for a stack AccountItemIndex would also index. Its
        /// constructor drops empty and sourceless stacks, and a stack the
        /// row cannot count must not decide where the row lives.
        /// </summary>
        private static bool Counts(SnapshotItemEntry entry)
        {
            return entry != null
                && entry.ItemId > 0
                && entry.Count > 0
                && !string.IsNullOrWhiteSpace(entry.Source);
        }
    }
}
