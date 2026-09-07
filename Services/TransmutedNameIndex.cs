using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Every stack a Snapshot row may take a skin from, keyed by item id,
    /// plus the two questions a row asks of them.
    /// <para>
    /// The game applies a skin per copy, and a Snapshot row is one item id
    /// summed over every copy of it. So a row takes a skin's name and icon
    /// only when every copy wears that same skin; when they differ it keeps
    /// the item's own name and icon, because a name only one copy wears
    /// would describe items the row also covers.
    /// </para>
    /// <para>
    /// A skin whose name equals the item's own name is not a transmutation
    /// and is read as no skin at all.
    /// </para>
    /// <para>Blish-free, so the whole rule is unit-testable (repo
    /// invariant), same precedent as SocketedUpgradeIndex.</para>
    /// </summary>
    internal static class TransmutedNameIndex
    {
        // Wrapped, not a bare Dictionary: this instance is handed to every
        // caller that asks about a snapshot with no skins in it, and a
        // caller that cast the returned interface back to Dictionary would
        // be writing into all of them. ReadOnlyDictionary throws instead.
        private static readonly IReadOnlyDictionary<int, IReadOnlyList<TransmutedItemCopy>> NoCopies =
            new ReadOnlyDictionary<int, IReadOnlyList<TransmutedItemCopy>>(
                new Dictionary<int, IReadOnlyList<TransmutedItemCopy>>());

        /// <summary>
        /// Every copy of every item id at least one copy of which is
        /// transmuted. Ids no copy of which wears a skin are absent, so a
        /// caller that finds nothing has nothing to decide.
        /// <para>
        /// Drops the same stacks AccountItemIndex drops - no id, no source,
        /// nothing in them - so the copies here and the sources a row is
        /// built from are the same set of stacks.
        /// </para>
        /// </summary>
        public static IReadOnlyDictionary<int, IReadOnlyList<TransmutedItemCopy>> Build(
            IReadOnlyList<SnapshotItemEntry> items)
        {
            if (items == null || items.Count == 0)
            {
                return NoCopies;
            }

            // Ids first, copies second: this walks every row of a
            // snapshot, thousands of them on a full account, and only the
            // handful wearing a skin need a list at all.
            var skinned = new HashSet<int>();
            foreach (var entry in items)
            {
                if (Counts(entry) && SkinOf(entry).IsPresent)
                {
                    skinned.Add(entry.ItemId);
                }
            }

            if (skinned.Count == 0)
            {
                return NoCopies;
            }

            var byItemId = new Dictionary<int, List<TransmutedItemCopy>>(skinned.Count);

            foreach (var entry in items)
            {
                if (!Counts(entry) || !skinned.Contains(entry.ItemId))
                {
                    continue;
                }

                if (!byItemId.TryGetValue(entry.ItemId, out var copies))
                {
                    copies = new List<TransmutedItemCopy>();
                    byItemId[entry.ItemId] = copies;
                }

                copies.Add(new TransmutedItemCopy(entry.Source, SkinOf(entry)));
            }

            var result = new Dictionary<int, IReadOnlyList<TransmutedItemCopy>>(byItemId.Count);
            foreach (var pair in byItemId)
            {
                result[pair.Key] = pair.Value;
            }

            return result;
        }

        /// <summary>
        /// The skin every visible copy wears, or
        /// <see cref="TransmutedSkin.None"/> when they do not all wear the
        /// same one. <paramref name="isVisible"/> answers whether a raw
        /// source key survived the row's source filter; a null one counts
        /// every copy.
        /// </summary>
        public static TransmutedSkin AgreedSkin(
            IReadOnlyList<TransmutedItemCopy> copies, Func<string, bool> isVisible)
        {
            if (copies == null)
            {
                return TransmutedSkin.None;
            }

            var agreed = TransmutedSkin.None;
            bool seen = false;

            for (int i = 0; i < copies.Count; i++)
            {
                var copy = copies[i];
                if (isVisible != null && !isVisible(copy.Source))
                {
                    continue;
                }

                if (!seen)
                {
                    agreed = copy.Skin;
                    seen = true;
                    continue;
                }

                // One bare copy and one skinned copy disagree as much as
                // two differently skinned ones do: "no skin" is an answer,
                // not a missing reading.
                if (!SameSkin(agreed, copy.Skin))
                {
                    return TransmutedSkin.None;
                }
            }

            return agreed;
        }

        /// <summary>
        /// True when <paramref name="search"/> occurs (case-insensitively)
        /// in a skin name a visible copy wears. Search is not held to the
        /// agreement rule: it matches any skin any visible copy wears, so
        /// the name the game shows always finds the item. It is held to the
        /// filter, so a skin worn only where the user has unchecked finds
        /// nothing.
        /// </summary>
        public static bool AnySkinNameMatches(
            IReadOnlyList<TransmutedItemCopy> copies,
            string search,
            Func<string, bool> isVisible)
        {
            if (copies == null || string.IsNullOrEmpty(search))
            {
                return false;
            }

            for (int i = 0; i < copies.Count; i++)
            {
                var copy = copies[i];
                if (!copy.Skin.IsPresent)
                {
                    continue;
                }

                if (isVisible != null && !isVisible(copy.Source))
                {
                    continue;
                }

                if (copy.Skin.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True for a stack AccountItemIndex would also index. Its
        /// constructor drops empty stacks and sourceless ones, and a copy
        /// the row cannot count must not decide the row's name.
        /// </summary>
        private static bool Counts(SnapshotItemEntry entry)
        {
            return entry != null
                && entry.ItemId > 0
                && entry.Count > 0
                && !string.IsNullOrWhiteSpace(entry.Source);
        }

        /// <summary>
        /// What one stack wears, or <see cref="TransmutedSkin.None"/>. The
        /// name and the icon are accepted or rejected together, so a row
        /// can never draw the skin's name over the item's own picture.
        /// </summary>
        private static TransmutedSkin SkinOf(SnapshotItemEntry entry)
        {
            var skin = TransmutedSkin.Of(entry.SkinName, entry.SkinIconUrl);
            return skin.IsPresent
                && !string.Equals(skin.Name, entry.Name ?? "", StringComparison.Ordinal)
                ? skin
                : TransmutedSkin.None;
        }

        private static bool SameSkin(TransmutedSkin left, TransmutedSkin right)
        {
            return string.Equals(left.Name, right.Name, StringComparison.Ordinal)
                && string.Equals(left.IconUrl, right.IconUrl, StringComparison.Ordinal);
        }
    }
}
