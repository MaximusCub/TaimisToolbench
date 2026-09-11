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
        /// Opens one place holding gear that draws on an account-wide item.
        /// Same lead-in as the wearers, because it answers the same
        /// question; a separate one because the place carries its own
        /// label and cannot sit inside their comma list.
        /// </summary>
        private const string DrawnFromPrefix = " - ";

        /// <summary>
        /// Reads a raw AccountItemIndex source key as a place. An
        /// unrecognized key becomes
        /// <see cref="SnapshotHoldCategory.Unknown"/> and keeps the place
        /// half of its text, so real inventory the module does not yet know
        /// about still shows (KNOWN-ISSUES #31: never silently mask data).
        /// <para>
        /// A socket key names the gear it sits in by item id, and names its
        /// place with the container half of the key, so a socket reads as
        /// the same place a loose stack there would.
        /// <paramref name="hostItemName"/> is what turns that id into a
        /// name; without one, or when it answers blank, the place prints
        /// without the gear rather than with an id (repo invariant: item
        /// ids are never shown).
        /// </para>
        /// </summary>
        public static SnapshotHoldLocation FromSource(
            string rawSource, int count, Func<int, string> hostItemName = null)
        {
            var location = new SnapshotHoldLocation { Count = count };

            if (AccountItemIndex.IsSocketedSource(rawSource))
            {
                location.HostItemName = HostNameOf(rawSource, hostItemName);
            }

            if (AccountItemIndex.TryGetCharacterName(rawSource, out string characterName))
            {
                location.Category = AccountItemIndex.IsWornGearPlace(rawSource)
                    ? SnapshotHoldCategory.Equipped
                    : SnapshotHoldCategory.Bags;
                location.CharacterName = characterName;
                return location;
            }

            if (AccountItemIndex.ContainerIs(rawSource, AccountItemIndex.SourceSharedInventory))
            {
                location.Category = SnapshotHoldCategory.SharedInventory;
            }
            else if (AccountItemIndex.ContainerIs(rawSource, AccountItemIndex.SourceBank))
            {
                location.Category = SnapshotHoldCategory.Bank;
            }
            else if (AccountItemIndex.ContainerIs(rawSource, AccountItemIndex.SourceMaterialStorage))
            {
                location.Category = SnapshotHoldCategory.MaterialStorage;
            }
            else if (AccountItemIndex.ContainerIs(rawSource, AccountItemIndex.SourceLegendaryArmory))
            {
                location.Category = SnapshotHoldCategory.LegendaryArmory;
            }
            else
            {
                // Only the Unknown label prints this, so only the Unknown
                // case pays for it: this runs per source per row on the
                // keystroke path. The container half, not the whole key,
                // because a socket key carries an item id (repo invariant:
                // item ids are never shown).
                location.Category = SnapshotHoldCategory.Unknown;
                location.RawSource = AccountItemIndex.ContainerSource(rawSource);
            }

            return location;
        }

        /// <summary>
        /// One place on its own, as the line would print it with no other
        /// place beside it and no count: "Bank", "Bank (in Dusk)",
        /// "Equipped: Divineaxe (in Obsidian Helm)".
        /// </summary>
        public static string PlacePhrase(string rawSource, Func<int, string> hostItemName)
        {
            var line = new StringBuilder();
            AppendPlaceLabel(line, FromSource(rawSource, 0, hostItemName));
            return line.ToString();
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
                // "Bank (2)" when the counts are on. Two such places print
                // the label twice, because each carries its own gear.
                AppendNamelessPlaces(line, locations, category, showCounts);
                AppendEquippedBy(line, locations, category);
                AppendSocketedInto(line, locations, category);
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
            AppendSocketedInto(line, locations, category);
        }

        /// <summary>
        /// Appends every place in a category that names no character. The
        /// caller has already written the first one's label, so this adds
        /// the gear and the count to that one and a whole label to each
        /// place after it.
        /// </summary>
        private static void AppendNamelessPlaces(
            StringBuilder line,
            IReadOnlyList<SnapshotHoldLocation> locations,
            SnapshotHoldCategory category,
            bool showCounts)
        {
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
                    line.Append(CategorySeparator).Append(CategoryLabel(location));
                }

                AppendHost(line, location);

                if (showCounts)
                {
                    AppendCount(line, location.Count);
                }

                wrote = true;
            }
        }

        /// <summary>
        /// Appends the places holding gear that draws on this account-wide
        /// copy, each on the same lead-in the wearers use. A banked piece is
        /// not equipped, so it cannot go in that list.
        /// </summary>
        private static void AppendSocketedInto(
            StringBuilder line,
            IReadOnlyList<SnapshotHoldLocation> locations,
            SnapshotHoldCategory category)
        {
            for (int i = 0; i < locations.Count; i++)
            {
                var location = locations[i];
                if (location == null
                    || location.Category != category
                    || location.SocketedInto == null)
                {
                    continue;
                }

                var places = location.SocketedInto;
                for (int p = 0; p < places.Count; p++)
                {
                    if (!string.IsNullOrEmpty(places[p]))
                    {
                        line.Append(DrawnFromPrefix).Append(places[p]);
                    }
                }
            }
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
            AppendHost(line, location);

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

        /// <summary>
        /// Appends the gear a place is inside, or nothing when the place is
        /// not a socket or the capture could not name the gear. The word
        /// "in" is what keeps the gear apart from a bracketed count.
        /// </summary>
        private static void AppendHost(StringBuilder line, SnapshotHoldLocation location)
        {
            if (!string.IsNullOrEmpty(location.HostItemName))
            {
                line.Append(" (in ").Append(location.HostItemName).Append(")");
            }
        }

        /// <summary>One place's whole label, with no count.</summary>
        private static void AppendPlaceLabel(
            StringBuilder line, SnapshotHoldLocation location)
        {
            line.Append(CategoryLabel(location));

            if (HasCharacterName(location))
            {
                line.Append(": ").Append(location.CharacterName);
            }

            AppendHost(line, location);
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
