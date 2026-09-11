using System;
using System.Collections.Generic;
using System.Text;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Turns the places holding one item into the single line the Snapshot
    /// tab prints under that item's name. Blish-free and pure, so a test can
    /// drive the wording directly.
    /// <para>
    /// Counts are printed only when the reader cannot work the distribution
    /// out from the line itself. The row above the line already carries the
    /// account-wide total, so a single place holds all of it, and places
    /// that each hold one are counted by reading their names. Both cases
    /// print no counts. Otherwise every place prints its own count, in
    /// parentheses, including the places holding one.
    /// </para>
    /// </summary>
    internal static class SnapshotHoldLine
    {
        /// <summary>Separates two categories on the line.</summary>
        private const string CategorySeparator = "  ";

        /// <summary>
        /// Opens the list of characters wearing an account-wide item. The
        /// dash keeps it apart from the colon that introduces the
        /// characters HOLDING a category's stock: these characters are
        /// drawing the one copy the category already counted.
        /// </summary>
        private const string EquippedByPrefix = " - Equipped: ";

        /// <summary>Separates two names in that list. They never carry
        /// counts, so a comma is the only thing between them however the
        /// rest of the line is printing.</summary>
        private const string EquippedBySeparator = ", ";

        /// <summary>
        /// Reads a raw AccountItemIndex source key as a place. An
        /// unrecognized key becomes
        /// <see cref="SnapshotHoldCategory.Unknown"/> and keeps its raw text,
        /// so real inventory the module does not yet know about still shows
        /// (KNOWN-ISSUES #31: never silently mask data).
        /// <para>
        /// A socket key names the gear it sits in by item id.
        /// <paramref name="hostItemName"/> is what turns that id into a
        /// name; without one, or when it answers blank, the place prints
        /// the wearer alone rather than an id (repo invariant: item ids are
        /// never shown).
        /// </para>
        /// </summary>
        public static SnapshotHoldLocation FromSource(
            string rawSource, int count, Func<int, string> hostItemName = null)
        {
            var location = new SnapshotHoldLocation
            {
                Count = count,
                RawSource = rawSource ?? "",
            };

            // Ahead of the character read, not inside it: a socket key
            // carries an item id, and a key too damaged to read a name out
            // of must still not fall through to the raw-text case below
            // (repo invariant: item ids are never shown).
            if (AccountItemIndex.IsSocketedSource(rawSource))
            {
                AccountItemIndex.TryGetCharacterName(rawSource, out string wearer);
                location.Category = SnapshotHoldCategory.Equipped;
                location.CharacterName = wearer;
                location.HostItemName = HostNameOf(rawSource, hostItemName);
                return location;
            }

            if (AccountItemIndex.TryGetCharacterName(rawSource, out string characterName))
            {
                location.Category = AccountItemIndex.IsEquipmentSource(rawSource)
                    ? SnapshotHoldCategory.Equipped
                    : SnapshotHoldCategory.Bags;
                location.CharacterName = characterName;
                return location;
            }

            switch (rawSource)
            {
                case AccountItemIndex.SourceSharedInventory:
                    location.Category = SnapshotHoldCategory.SharedInventory;
                    break;
                case AccountItemIndex.SourceBank:
                    location.Category = SnapshotHoldCategory.Bank;
                    break;
                case AccountItemIndex.SourceMaterialStorage:
                    location.Category = SnapshotHoldCategory.MaterialStorage;
                    break;
                case AccountItemIndex.SourceLegendaryArmory:
                    location.Category = SnapshotHoldCategory.LegendaryArmory;
                    break;
                default:
                    location.Category = SnapshotHoldCategory.Unknown;
                    break;
            }

            return location;
        }

        /// <summary>
        /// The whole line, or "" when nothing holds the item. Categories run
        /// in the order of <see cref="SnapshotHoldCategory"/>; characters run
        /// in the order the caller supplied, which is the order
        /// AccountItemIndex.GetPrioritizedSources put them in.
        /// </summary>
        public static string Format(IReadOnlyList<SnapshotHoldLocation> locations)
        {
            if (locations == null || locations.Count == 0)
            {
                return "";
            }

            // Indexed loops throughout, here and in AppendCategory: foreach
            // over IReadOnlyList boxes an enumerator, and a search rebuilds
            // every row on screen on every keystroke.
            int places = 0;
            bool anyPlaceHoldsOtherThanOne = false;
            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location == null)
                {
                    continue;
                }

                places++;
                anyPlaceHoldsOtherThanOne |= location.Count != 1;
            }

            bool showCounts = places > 1 && anyPlaceHoldsOtherThanOne;

            var line = new StringBuilder();

            for (var category = SnapshotHoldCategory.SharedInventory;
                category <= SnapshotHoldCategory.LegendaryArmory;
                category++)
            {
                AppendCategory(line, locations, category, showCounts);
            }

            AppendUnrecognizedPlaces(line, locations, showCounts);

            return line.ToString();
        }

        /// <summary>
        /// Appends every unrecognized place, each under its own raw source
        /// key, in the order the caller supplied. They cannot share the
        /// category loop above: two unrecognized keys are two different
        /// places, and one label over both would hide one of them
        /// (KNOWN-ISSUES #31: never silently mask data).
        /// </summary>
        private static void AppendUnrecognizedPlaces(
            StringBuilder line,
            IReadOnlyList<SnapshotHoldLocation> locations,
            bool showCounts)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location == null
                    || location.Category != SnapshotHoldCategory.Unknown)
                {
                    continue;
                }

                if (line.Length > 0)
                {
                    line.Append(CategorySeparator);
                }

                line.Append(CategoryLabel(location));

                if (showCounts)
                {
                    AppendCount(line, location.Count);
                }
            }
        }

        /// <summary>
        /// Appends one category's places, or nothing when no place is in it.
        /// Scans the list twice rather than collecting the matches: this runs
        /// once per category per row.
        /// </summary>
        private static void AppendCategory(
            StringBuilder line,
            IReadOnlyList<SnapshotHoldLocation> locations,
            SnapshotHoldCategory category,
            bool showCounts)
        {
            SnapshotHoldLocation first = null;
            bool named = false;

            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location == null || location.Category != category)
                {
                    continue;
                }

                if (first == null)
                {
                    first = location;
                }

                named |= HasCharacterName(location);
            }

            if (first == null)
            {
                return;
            }

            if (line.Length > 0)
            {
                line.Append(CategorySeparator);
            }

            line.Append(CategoryLabel(first));

            if (!named)
            {
                // A place that holds for the whole account names nobody, so
                // there is no list for a colon to introduce: "Bank", or
                // "Bank (2)" when the counts are on.
                if (showCounts)
                {
                    AppendEachCount(line, locations, category);
                }

                AppendEquippedBy(line, locations, category);
                return;
            }

            line.Append(": ");

            // With counts on, one space keeps each name beside its own
            // bracketed count. With counts off, a comma is the only thing
            // separating two bare names.
            string separator = showCounts ? " " : ", ";
            bool wrote = false;

            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location == null || location.Category != category)
                {
                    continue;
                }

                if (wrote)
                {
                    line.Append(separator);
                }

                AppendPlace(line, location, showCounts);
                wrote = true;
            }

            AppendEquippedBy(line, locations, category);
        }

        /// <summary>
        /// Appends the characters wearing this category's item, or nothing
        /// when none are named. Names only: a count here would read as
        /// stock, and the whole point of
        /// Models.SnapshotHoldLocation.EquippedBy is that these characters
        /// hold none.
        /// </summary>
        private static void AppendEquippedBy(
            StringBuilder line,
            IReadOnlyList<SnapshotHoldLocation> locations,
            SnapshotHoldCategory category)
        {
            bool wrote = false;

            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location == null
                    || location.Category != category
                    || location.EquippedBy == null)
                {
                    continue;
                }

                var names = location.EquippedBy;
                for (int n = 0; n < names.Count; n++)
                {
                    if (string.IsNullOrEmpty(names[n]))
                    {
                        continue;
                    }

                    line.Append(wrote ? EquippedBySeparator : EquippedByPrefix);
                    line.Append(names[n]);
                    wrote = true;
                }
            }
        }

        /// <summary>
        /// One named place. A category that names anybody can still hold a
        /// place with no name, because a source key of "Character:" with
        /// nothing after it reads as a character whose name is empty; that
        /// place prints its count alone rather than disappearing
        /// (KNOWN-ISSUES #31: never silently mask data).
        /// </summary>
        private static void AppendPlace(
            StringBuilder line, SnapshotHoldLocation location, bool showCounts)
        {
            if (!HasCharacterName(location))
            {
                line.Append("(").Append(location.Count).Append(")");
                return;
            }

            line.Append(location.CharacterName);
            if (!string.IsNullOrEmpty(location.HostItemName))
            {
                line.Append(" (").Append(location.HostItemName).Append(")");
            }

            if (showCounts)
            {
                AppendCount(line, location.Count);
            }
        }

        /// <summary>
        /// The gear a socket key names, or "" when the key carries no
        /// readable id or the caller cannot name it.
        /// </summary>
        private static string HostNameOf(string rawSource, Func<int, string> hostItemName)
        {
            if (hostItemName == null
                || !AccountItemIndex.TryGetSocketedHostItemId(rawSource, out int hostItemId))
            {
                return "";
            }

            string name = hostItemName(hostItemId);
            return string.IsNullOrWhiteSpace(name) ? "" : name;
        }

        /// <summary>Appends one count per place in the category, for the
        /// categories that name nobody. More than one place lands in such a
        /// category only if a future source key maps into it.</summary>
        private static void AppendEachCount(
            StringBuilder line,
            IReadOnlyList<SnapshotHoldLocation> locations,
            SnapshotHoldCategory category)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location != null && location.Category == category)
                {
                    AppendCount(line, location.Count);
                }
            }
        }

        /// <summary>Every count on the line is bracketed, whether its place
        /// names a character or holds for the whole account.</summary>
        private static void AppendCount(StringBuilder line, int count)
        {
            line.Append(" (").Append(count).Append(")");
        }

        private static bool HasCharacterName(SnapshotHoldLocation location)
        {
            return !string.IsNullOrEmpty(location.CharacterName);
        }

        private static string CategoryLabel(SnapshotHoldLocation location)
        {
            switch (location.Category)
            {
                case SnapshotHoldCategory.SharedInventory: return "Shared Inventory";
                case SnapshotHoldCategory.Bags: return "Bags";
                case SnapshotHoldCategory.Equipped: return "Equipped";
                case SnapshotHoldCategory.Bank: return "Bank";
                case SnapshotHoldCategory.MaterialStorage: return "Material Storage";
                case SnapshotHoldCategory.LegendaryArmory: return "Legendary Armory";
                default: return location.RawSource.Length > 0 ? location.RawSource : "Unknown";
            }
        }
    }
}
