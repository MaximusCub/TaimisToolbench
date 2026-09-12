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
    /// out from the line itself. The row above carries the account-wide
    /// total, so a single place holds all of it; a place that holds one per
    /// piece of gear it names holds what its names already say. Both print
    /// no counts. Otherwise every place prints its own, in parentheses.
    /// </para>
    /// <para>
    /// One character reads out once per category, however many pieces of
    /// its gear hold the item: the pieces share one bracket. A loose stack
    /// stays apart from the sockets beside it, because its count belongs to
    /// no piece.
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
        /// Opens the bracket naming the gear a place's copies sit in. The
        /// word "in" is what keeps it apart from a count, which is
        /// bracketed too.
        /// </summary>
        private const string HostPrefix = " (in ";

        /// <summary>Separates two pieces of gear inside that bracket.</summary>
        private const string HostSeparator = ", ";

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
                location.Category = CharacterPlaceOf(rawSource);
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
        /// Which of a character's three places a source key names. Gear the
        /// character is wearing and gear parked in one of its other saved
        /// equipment templates are separate categories, because "Equipped"
        /// over both told a player they were wearing a spare set.
        /// </summary>
        private static SnapshotHoldCategory CharacterPlaceOf(string rawSource)
        {
            if (AccountItemIndex.IsWornGearPlace(rawSource))
            {
                return SnapshotHoldCategory.Equipped;
            }

            return AccountItemIndex.IsTemplateGearPlace(rawSource)
                ? SnapshotHoldCategory.EquipmentTemplate
                : SnapshotHoldCategory.Bags;
        }

        /// <summary>
        /// The places a set of source keys names, each as the line would
        /// print it with no other place beside it and no count: "Bank",
        /// "Bank (in Dusk, Carcharias)", "Equipped: Divineaxe (in Obsidian
        /// Helm)". One phrase per place, so a character holding the item in
        /// three pieces of gear is named once. Returns an empty list, never
        /// null.
        /// </summary>
        public static List<string> PlacePhrases(
            IReadOnlyList<string> rawSources, Func<int, string> hostItemName)
        {
            var phrases = new List<string>();
            if (rawSources == null)
            {
                return phrases;
            }

            var places = new List<SnapshotHoldLocation>(rawSources.Count);
            for (int i = 0; i < rawSources.Count; i++)
            {
                places.Add(FromSource(rawSources[i], 0, hostItemName));
            }

            for (int i = 0; i < places.Count; i++)
            {
                if (IsRepeatedPlace(places, i))
                {
                    continue;
                }

                var line = new StringBuilder();
                line.Append(CategoryLabel(places[i]));

                if (HasCharacterName(places[i]))
                {
                    line.Append(": ").Append(places[i].CharacterName);
                }

                AppendHosts(line, places, i);
                phrases.Add(line.ToString());
            }

            return phrases;
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
            bool anyPlaceNeedsItsCount = false;
            for (int i = 0; i < locations.Count; i++)
            {
                if (locations[i] == null || IsRepeatedPlace(locations, i))
                {
                    continue;
                }

                places++;
                anyPlaceNeedsItsCount |= !GroupReadsOffItsNames(locations, i);
            }

            bool showCounts = places > 1 && anyPlaceNeedsItsCount;

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
                    || location.Category != SnapshotHoldCategory.Unknown
                    || IsRepeatedPlace(locations, i))
                {
                    continue;
                }

                if (line.Length > 0)
                {
                    line.Append(CategorySeparator);
                }

                line.Append(CategoryLabel(location));
                AppendHosts(line, locations, i);

                if (showCounts)
                {
                    AppendCount(line, GroupCount(locations, i));
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
                if (location == null
                    || location.Category != category
                    || IsRepeatedPlace(locations, i))
                {
                    continue;
                }

                if (wrote)
                {
                    line.Append(separator);
                }

                AppendPlace(line, locations, i, showCounts);
                wrote = true;
            }

            AppendEquippedBy(line, locations, category);
            AppendSocketedInto(line, locations, category);
        }

        /// <summary>
        /// Appends every place in a category that names no character. The
        /// caller has already written the first one's label, so this adds
        /// the gear and the count to that one and a whole label to each
        /// place after it. A category holds two such places only when one
        /// is a loose stack and the other a socket.
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
                if (location == null
                    || location.Category != category
                    || IsRepeatedPlace(locations, i))
                {
                    continue;
                }

                if (wrote)
                {
                    line.Append(CategorySeparator).Append(CategoryLabel(location));
                }

                AppendHosts(line, locations, i);

                if (showCounts)
                {
                    AppendCount(line, GroupCount(locations, i));
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
            StringBuilder line,
            IReadOnlyList<SnapshotHoldLocation> locations,
            int index,
            bool showCounts)
        {
            var location = locations[index];
            if (!HasCharacterName(location))
            {
                line.Append("(").Append(GroupCount(locations, index)).Append(")");
                return;
            }

            line.Append(location.CharacterName);
            AppendHosts(line, locations, index);

            if (showCounts)
            {
                AppendCount(line, GroupCount(locations, index));
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
        /// Appends every piece of gear the places grouped at
        /// <paramref name="index"/> sit in, in one bracket, or nothing when
        /// the group is a loose stack or the capture could not name the
        /// gear.
        /// </summary>
        private static void AppendHosts(
            StringBuilder line, IReadOnlyList<SnapshotHoldLocation> locations, int index)
        {
            bool wrote = false;

            for (int i = index; i < locations.Count; i++)
            {
                if (!SameGroup(locations[index], locations[i]))
                {
                    continue;
                }

                string host = locations[i].HostItemName;
                if (string.IsNullOrEmpty(host))
                {
                    continue;
                }

                line.Append(wrote ? HostSeparator : HostPrefix).Append(host);
                wrote = true;
            }

            if (wrote)
            {
                line.Append(")");
            }
        }

        /// <summary>
        /// True when the group at <paramref name="index"/> holds exactly
        /// what its own text already says: one copy per piece of gear it
        /// names, or a single copy where it names no gear.
        /// </summary>
        private static bool GroupReadsOffItsNames(
            IReadOnlyList<SnapshotHoldLocation> locations, int index)
        {
            int named = 0;

            for (int i = index; i < locations.Count; i++)
            {
                if (SameGroup(locations[index], locations[i])
                    && !string.IsNullOrEmpty(locations[i].HostItemName))
                {
                    named++;
                }
            }

            return GroupCount(locations, index) == (named > 0 ? named : 1);
        }

        /// <summary>
        /// How many the places grouped at <paramref name="index"/> hold
        /// between them. The group starts there, so nothing before it can
        /// belong to it.
        /// </summary>
        private static int GroupCount(
            IReadOnlyList<SnapshotHoldLocation> locations, int index)
        {
            int count = 0;

            for (int i = index; i < locations.Count; i++)
            {
                if (SameGroup(locations[index], locations[i]))
                {
                    count += locations[i].Count;
                }
            }

            return count;
        }

        /// <summary>
        /// True when an earlier place already printed this one's group, so
        /// this one has nothing left of its own to write. Quadratic in the
        /// places on ONE row, which a roster times its gear slots bounds.
        /// </summary>
        private static bool IsRepeatedPlace(
            IReadOnlyList<SnapshotHoldLocation> locations, int index)
        {
            for (int i = 0; i < index; i++)
            {
                if (SameGroup(locations[i], locations[index]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// True when two places read out as one: same category, same
        /// character, and both either sockets or loose stacks. A loose stack
        /// is kept apart so its count is never read as belonging to the gear
        /// named beside it. Two unrecognized keys stay two places whatever
        /// else they share (KNOWN-ISSUES #31: never silently mask data).
        /// </summary>
        private static bool SameGroup(
            SnapshotHoldLocation first, SnapshotHoldLocation second)
        {
            if (first == null || second == null || first.Category != second.Category)
            {
                return false;
            }

            if (!string.Equals(
                    first.CharacterName ?? "",
                    second.CharacterName ?? "",
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (string.IsNullOrEmpty(first.HostItemName)
                != string.IsNullOrEmpty(second.HostItemName))
            {
                return false;
            }

            return first.Category != SnapshotHoldCategory.Unknown
                || string.Equals(
                    first.RawSource ?? "", second.RawSource ?? "", StringComparison.Ordinal);
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
                case SnapshotHoldCategory.EquipmentTemplate: return "Equipment Templates";
                case SnapshotHoldCategory.Bank: return "Bank";
                case SnapshotHoldCategory.MaterialStorage: return "Material Storage";
                case SnapshotHoldCategory.LegendaryArmory: return "Legendary Armory";
                default: return location.RawSource.Length > 0 ? location.RawSource : "Unknown";
            }
        }
    }
}
