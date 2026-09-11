using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal class AccountItemIndex
    {
        public const string SourceMaterialStorage = "MaterialStorage";
        public const string SourceSharedInventory = "SharedInventory";
        public const string SourceBank = "Bank";

        // The account-wide Legendary Armory, read from its own endpoint.
        // A slot drawing a legendary out of the armory is reported once per
        // slot per character, so the equipment fetch drops those and this
        // source carries the account's real, already-deduplicated count.
        public const string SourceLegendaryArmory = "LegendaryArmory";

        // A character's bag contents are stored as "Character:<name>" and the
        // gear worn on that character as "Equipped:<name>" (see
        // Gw2AccountSnapshotService). Both prefixes contain a colon, which no
        // GW2 character name may contain, so a character named e.g. "Bank"
        // can never collide with a storage-location source key.
        public const string CharacterSourcePrefix = "Character:";
        public const string CharacterEquipmentSourcePrefix = "Equipped:";

        // One upgrade component or infusion sitting in a socket of one
        // piece of gear, as "Socketed:<host item id>:<container>", where
        // the container is the plain source key of the gear itself: Bank,
        // SharedInventory, "Character:<name>" or "Equipped:<name>". The
        // host id is what keeps two pieces in one container apart, so six
        // copies of a rune across six armour slots stay six places rather
        // than collapsing into one.
        //
        // The container half is a whole source key, so a character named
        // "Bank" carries its own prefix and can no more collide here than
        // it can above. A character name still runs to the end of the key,
        // which is what lets the filter and the search compare it in place.
        public const string SocketedSourcePrefix = "Socketed:";

        private static readonly IReadOnlyList<string> EmptySources = Array.Empty<string>();

        // itemId -> source -> count
        private readonly Dictionary<int, Dictionary<string, int>> _index;

        public AccountItemIndex(IReadOnlyList<SnapshotItemEntry> items)
        {
            _index = new Dictionary<int, Dictionary<string, int>>();

            if (items == null)
            {
                return;
            }

            foreach (var entry in items)
            {
                if (entry.Count <= 0)
                {
                    continue;
                }

                string source = entry.Source;
                if (string.IsNullOrWhiteSpace(source))
                {
                    continue;
                }

                if (!_index.TryGetValue(entry.ItemId, out var sourceMap))
                {
                    sourceMap = new Dictionary<string, int>(StringComparer.Ordinal);
                    _index[entry.ItemId] = sourceMap;
                }

                if (sourceMap.TryGetValue(source, out int existing))
                {
                    sourceMap[source] = existing + entry.Count;
                }
                else
                {
                    sourceMap[source] = entry.Count;
                }
            }
        }

        public int GetQuantity(int itemId, string source)
        {
            if (source == null)
            {
                return 0;
            }

            if (_index.TryGetValue(itemId, out var sourceMap) &&
                sourceMap.TryGetValue(source, out int count))
            {
                return count;
            }

            return 0;
        }

        /// <summary>
        /// How many distinct items the account actually holds. The
        /// constructor drops an entry with a count of zero or less and one
        /// with a blank source, so this counts exactly the ids that can
        /// produce a Snapshot tab row when no filter is applied.
        /// <para>
        /// The Snapshot tab's "N of M items" line reads this. It used to
        /// read the representative-entry map, which keys on every id the
        /// capture mentioned, so a zero-count entry raised M without ever
        /// being showable and no filter change could reveal it.
        /// </para>
        /// </summary>
        public int DistinctItemCount
        {
            get { return _index.Count; }
        }

        public IReadOnlyList<string> GetSources(int itemId)
        {
            if (_index.TryGetValue(itemId, out var sourceMap))
            {
                var keys = sourceMap.Keys.ToList();
                keys.Sort(StringComparer.Ordinal);
                return keys;
            }

            return EmptySources;
        }

        /// <summary>
        /// Where a character's name starts inside a source key, or -1 when
        /// the key does not belong to a character. Both character encodings
        /// answer here, so a caller never tests a prefix itself and can
        /// never handle bags while forgetting worn gear. Returns an offset
        /// rather than the name so callers on the keystroke path can compare
        /// in place without allocating a substring.
        /// </summary>
        public static int CharacterNameOffset(string source)
        {
            int container = ContainerOffset(source);
            if (container < 0)
            {
                return -1;
            }

            if (StartsAt(source, container, CharacterSourcePrefix))
            {
                return container + CharacterSourcePrefix.Length;
            }

            if (StartsAt(source, container, CharacterEquipmentSourcePrefix))
            {
                return container + CharacterEquipmentSourcePrefix.Length;
            }

            return -1;
        }

        /// <summary>
        /// Where the container half of a source key begins: 0 for a plain
        /// key, which is its own container, and the offset past the host id
        /// for a socket key. -1 when a socket key carries no readable
        /// container. Returned as an offset rather than a substring so
        /// callers on the keystroke path allocate nothing.
        /// </summary>
        public static int ContainerOffset(string source)
        {
            if (source == null)
            {
                return -1;
            }

            if (!source.StartsWith(SocketedSourcePrefix, StringComparison.Ordinal))
            {
                return 0;
            }

            int separator = source.IndexOf(':', SocketedSourcePrefix.Length);
            return separator < 0 ? -1 : separator + 1;
        }

        /// <summary>
        /// True when the container half of <paramref name="source"/> is
        /// exactly <paramref name="container"/>. A plain key is its own
        /// container, so this is plain equality for one.
        /// </summary>
        public static bool ContainerIs(string source, string container)
        {
            int offset = ContainerOffset(source);
            if (offset < 0 || container == null
                || source.Length - offset != container.Length)
            {
                return false;
            }

            return string.CompareOrdinal(source, offset, container, 0, container.Length) == 0;
        }

        /// <summary>
        /// The container half of a source key as its own string, or "" when
        /// the key carries none. Allocates, so the keystroke path uses
        /// <see cref="ContainerIs"/> instead.
        /// </summary>
        public static string ContainerSource(string source)
        {
            int offset = ContainerOffset(source);
            if (offset < 0)
            {
                return "";
            }

            return offset == 0 ? source : source.Substring(offset);
        }

        /// <summary>
        /// The source key for one socketed item, built from the gear it
        /// sits in and the plain source key of wherever that gear is.
        /// </summary>
        public static string SocketedSource(int hostItemId, string containerSource)
        {
            return SocketedSourcePrefix
                + hostItemId.ToString(CultureInfo.InvariantCulture)
                + ":"
                + (containerSource ?? "");
        }

        /// <summary>
        /// True when the source key is an item socketed into worn gear.
        /// </summary>
        public static bool IsSocketedSource(string source)
        {
            return source != null
                && source.StartsWith(SocketedSourcePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// The gear a socketed source key names, or false when the key is
        /// not one or carries no readable id. A caller that gets false has
        /// no host to name and must say nothing about one.
        /// </summary>
        public static bool TryGetSocketedHostItemId(string source, out int hostItemId)
        {
            hostItemId = 0;
            if (!IsSocketedSource(source))
            {
                return false;
            }

            int separator = source.IndexOf(':', SocketedSourcePrefix.Length);
            if (separator < 0)
            {
                return false;
            }

            string digits = source.Substring(
                SocketedSourcePrefix.Length, separator - SocketedSourcePrefix.Length);
            return int.TryParse(
                digits, NumberStyles.None, CultureInfo.InvariantCulture, out hostItemId)
                && hostItemId > 0;
        }

        /// <summary>
        /// True when the source key belongs to a character, with that
        /// character's bare name in <paramref name="characterName"/>. The
        /// name is "" when the key is not a character key.
        /// </summary>
        public static bool TryGetCharacterName(string source, out string characterName)
        {
            int offset = CharacterNameOffset(source);
            if (offset < 0)
            {
                characterName = "";
                return false;
            }

            characterName = source.Substring(offset);
            return true;
        }

        /// <summary>
        /// True when the source key is a stack of gear worn on a character,
        /// rather than that character's bag contents.
        /// <para>
        /// False for a socket key, even one whose container is worn gear: a
        /// socket row is not a worn stack, and
        /// Services.EquippedRuneSetIndex decides where a rune id lives from
        /// this answer. <see cref="IsWornGearPlace"/> is the broader
        /// question.
        /// </para>
        /// </summary>
        public static bool IsEquipmentSource(string source)
        {
            return source != null
                && source.StartsWith(CharacterEquipmentSourcePrefix, StringComparison.Ordinal);
        }

        /// <summary>
        /// True when the place a source key names is a character's worn
        /// gear, whether the key is that gear's own stack or something
        /// socketed into it.
        /// </summary>
        public static bool IsWornGearPlace(string source)
        {
            int container = ContainerOffset(source);
            return container >= 0
                && StartsAt(source, container, CharacterEquipmentSourcePrefix);
        }

        private static bool StartsAt(string source, int offset, string prefix)
        {
            return source.Length - offset >= prefix.Length
                && string.CompareOrdinal(source, offset, prefix, 0, prefix.Length) == 0;
        }

        public static IReadOnlyList<string> GetPrioritizedSources(
            int itemId,
            AccountItemIndex index,
            string activeCharacterName)
        {
            var allSources = index.GetSources(itemId);
            if (allSources.Count == 0)
            {
                return allSources;
            }

            var sourceSet = new HashSet<string>(allSources, StringComparer.Ordinal);
            var result = new List<string>();

            // Priority 1: MaterialStorage
            if (sourceSet.Remove(SourceMaterialStorage))
            {
                result.Add(SourceMaterialStorage);
            }

            // Priority 2: Active character, bags before worn gear. Callers
            // pass the bare character name; index sources carry one of the
            // two character encodings.
            if (!string.IsNullOrEmpty(activeCharacterName))
            {
                string activeBags = CharacterSourcePrefix + activeCharacterName;
                if (sourceSet.Remove(activeBags))
                {
                    result.Add(activeBags);
                }

                string activeEquipped = CharacterEquipmentSourcePrefix + activeCharacterName;
                if (sourceSet.Remove(activeEquipped))
                {
                    result.Add(activeEquipped);
                }
            }

            // Priority 3: SharedInventory
            if (sourceSet.Remove(SourceSharedInventory))
            {
                result.Add(SourceSharedInventory);
            }

            // Priority 4: Bank
            if (sourceSet.Remove(SourceBank))
            {
                result.Add(SourceBank);
            }

            // Priority 5: Legendary Armory
            if (sourceSet.Remove(SourceLegendaryArmory))
            {
                result.Add(SourceLegendaryArmory);
            }

            // Priority 6: Remaining sources (other characters), then every
            // socket, ordered by SpendRank and then ordinally.
            if (sourceSet.Count > 0)
            {
                var remaining = sourceSet.ToList();
                remaining.Sort((a, b) =>
                {
                    int byRank = SpendRank(a).CompareTo(SpendRank(b));
                    return byRank != 0 ? byRank : string.CompareOrdinal(a, b);
                });
                result.AddRange(remaining);
            }

            return result;
        }

        /// <summary>
        /// How reluctantly a source gives its copy up, within the bucket of
        /// sources no earlier priority claimed. A loose copy anywhere is
        /// spent before one that has to be pulled out of a piece of gear,
        /// and a socket in stored gear is spent before one in gear a
        /// character is wearing: pulling an upgrade out of a worn set
        /// breaks something the player is using right now, and pulling one
        /// out of a banked piece breaks nothing.
        /// </summary>
        private static int SpendRank(string source)
        {
            if (!IsSocketedSource(source))
            {
                return 0;
            }

            if (ContainerIs(source, SourceBank))
            {
                return 1;
            }

            if (ContainerIs(source, SourceSharedInventory))
            {
                return 2;
            }

            return IsWornGearPlace(source) ? 4 : 3;
        }
    }
}
