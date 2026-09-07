using System;
using System.Collections.Generic;
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
            if (source == null)
            {
                return -1;
            }

            if (source.StartsWith(CharacterSourcePrefix, StringComparison.Ordinal))
            {
                return CharacterSourcePrefix.Length;
            }

            if (source.StartsWith(CharacterEquipmentSourcePrefix, StringComparison.Ordinal))
            {
                return CharacterEquipmentSourcePrefix.Length;
            }

            return -1;
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
        /// True when the source key is gear worn on a character, rather than
        /// that character's bag contents.
        /// </summary>
        public static bool IsEquipmentSource(string source)
        {
            return source != null
                && source.StartsWith(CharacterEquipmentSourcePrefix, StringComparison.Ordinal);
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

            // Priority 6: Remaining sources (other characters), sorted
            if (sourceSet.Count > 0)
            {
                var remaining = sourceSet.ToList();
                remaining.Sort(StringComparer.Ordinal);
                result.AddRange(remaining);
            }

            return result;
        }
    }
}
