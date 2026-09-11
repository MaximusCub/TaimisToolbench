using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Blish-free search/filter/aggregation logic for the Snapshot tab's
    /// account-inventory browser (snapshot search,
    /// dev/proposals/d1-snapshot-about-settings.md Feature 1). MainView.cs is the only
    /// caller; every method here is a pure function over already-loaded
    /// AccountSnapshot/AccountItemIndex data - no I/O, no Blish_HUD/
    /// Gw2Sharp/Microsoft.Xna usings (repo invariant: tests must stay
    /// Blish-free).
    /// <para>
    /// Row grouping reuses AccountItemIndex/GetPrioritizedSources verbatim
    /// (already covered by AccountItemIndexTests) rather than
    /// re-implementing per-source totals - this class only adds the
    /// search-substring match, the source-category filter, and the
    /// display-row shape on top of it.
    /// </para>
    /// </summary>
    internal static class SnapshotSearchResultBuilder
    {
        /// <summary>
        /// Shortest query allowed to match a character label. Item and
        /// currency names keep matching from the first keystroke; only the
        /// character half is held back, because a single letter surfaces
        /// everything a character whose name contains it holds - so the
        /// opening keystrokes of an item search would widen the list
        /// instead of narrowing it (char-search-min2).
        /// </summary>
        private const int MinCharacterSearchLength = 2;

        /// <summary>
        /// The extra line the Snapshot tab's "No items match ..." message
        /// carries when <see cref="MinCharacterSearchLength"/> is the reason the
        /// list is empty, and null in every other case.
        /// <para>
        /// Emitted ONLY on that exact case - a query shorter than the minimum,
        /// and a roster character whose name really would match it at the next
        /// keystroke - so it never appears as boilerplate under an ordinary
        /// empty result. A character the source filter has unchecked is not a
        /// match: typing another letter would still not surface it, and a hint
        /// that promises otherwise is worse than none. That is why the
        /// exclusion set is a parameter rather than assumed empty - it is the
        /// same set <see cref="SnapshotSourceFilter.UncheckedCharacters"/>
        /// carries. No id is involved: the hint names no character at all.
        /// </para>
        /// <para>Why the hold-back needs a hint at all: docs/ARCHITECTURE.md,
        /// "Services Q-Z: relocated design narrative".</para>
        /// </summary>
        public static string ShortQueryCharacterHint(
            string searchText, IReadOnlyList<string> characterNames,
            ICollection<string> uncheckedCharacterNames = null)
        {
            string trimmed = (searchText ?? string.Empty).Trim();
            if (trimmed.Length == 0 || trimmed.Length >= MinCharacterSearchLength)
            {
                return null;
            }

            if (characterNames == null)
            {
                return null;
            }

            foreach (string name in characterNames)
            {
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (uncheckedCharacterNames != null && uncheckedCharacterNames.Contains(name))
                {
                    continue;
                }

                if (name.IndexOf(trimmed, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return ShortQueryCharacterHintText;
                }
            }

            return null;
        }

        /// <summary>
        /// Wording of <see cref="ShortQueryCharacterHint"/>. States the
        /// action, not the rule: "minimum query length" is this class's
        /// vocabulary, "type another letter" is the reader's.
        /// </summary>
        public const string ShortQueryCharacterHintText =
            "Type another letter to match character names.";

        /// <summary>
        /// Builds one representative <see cref="SnapshotItemEntry"/> per
        /// distinct itemId in <paramref name="items"/> (name/icon are
        /// resolved identically for every entry sharing an itemId - see
        /// Gw2AccountSnapshotService.ResolveItemDetailsAsync's shared
        /// per-id cache - so the first one seen is sufficient). Callers
        /// (MainView) build this once per snapshot, alongside their
        /// AccountItemIndex, and reuse the same map across every
        /// <see cref="BuildItemRows"/> call for that snapshot (e.g. once
        /// per search-box keystroke) instead of re-scanning the full raw
        /// entry list - potentially thousands of rows across a large
        /// account's characters/bank/material storage/shared inventory -
        /// on every call. Returns an empty dictionary, never null, for a
        /// null <paramref name="items"/>; null entries within it are
        /// skipped.
        /// </summary>
        public static Dictionary<int, SnapshotItemEntry> BuildRepresentativeIndex(IReadOnlyList<SnapshotItemEntry> items)
        {
            var firstSeenByItemId = new Dictionary<int, SnapshotItemEntry>();

            if (items == null)
            {
                return firstSeenByItemId;
            }

            foreach (var entry in items)
            {
                if (entry == null)
                {
                    continue;
                }

                if (!firstSeenByItemId.ContainsKey(entry.ItemId))
                {
                    firstSeenByItemId[entry.ItemId] = entry;
                }
            }

            return firstSeenByItemId;
        }

        /// <summary>
        /// Which places draw on each Legendary Armory item, keyed by item
        /// id, in the order the capture saw them and with each place listed
        /// once. Built once per snapshot alongside
        /// <see cref="BuildRepresentativeIndex"/>, because
        /// <see cref="BuildItemRows"/> runs once per search-box keystroke
        /// and a scan of the raw pairings per row would cost the roster
        /// times the result set. Returns an empty dictionary, never null.
        /// </summary>
        public static Dictionary<int, List<SnapshotArmoryEquip>> BuildArmoryEquippedIndex(
            AccountSnapshot snapshot)
        {
            var byItemId = new Dictionary<int, List<SnapshotArmoryEquip>>();
            if (snapshot == null || snapshot.LegendaryArmoryEquipped == null)
            {
                return byItemId;
            }

            foreach (var equip in snapshot.LegendaryArmoryEquipped)
            {
                if (equip == null
                    || equip.ItemId <= 0
                    || (string.IsNullOrEmpty(equip.CharacterName)
                        && string.IsNullOrEmpty(equip.Source)))
                {
                    continue;
                }

                if (!byItemId.TryGetValue(equip.ItemId, out var draws))
                {
                    draws = new List<SnapshotArmoryEquip>();
                    byItemId[equip.ItemId] = draws;
                }

                // One character can wear the same legendary in two slots -
                // two entries for one wearer, which must read as one name.
                // Two sockets in two pieces of gear are two places.
                if (!AlreadyDrawn(draws, equip))
                {
                    draws.Add(equip);
                }
            }

            return byItemId;
        }

        private static bool AlreadyDrawn(
            List<SnapshotArmoryEquip> draws, SnapshotArmoryEquip equip)
        {
            for (int i = 0; i < draws.Count; i++)
            {
                if (string.Equals(draws[i].CharacterName, equip.CharacterName, StringComparison.Ordinal)
                    && string.Equals(draws[i].Source, equip.Source, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Builds one <see cref="SnapshotSearchRow"/> per distinct itemId in
        /// <paramref name="itemsById"/> that (a) has a positive total once
        /// <paramref name="sourceFilter"/> has excluded any unchecked sources
        /// and (b) matches <paramref name="searchText"/> by case-insensitive
        /// substring: against the item's own name, against a skin a surviving
        /// copy wears, or against a holding character's name (that last only
        /// for queries of at least <see cref="MinCharacterSearchLength"/>).
        /// <para>
        /// The two compose as a plain AND. Only surviving sources are
        /// consulted, for the skin as much as for the character, so an
        /// unchecked character's rows stay hidden even when its own name or
        /// the skin it alone wears is typed. Such a row still reports the
        /// account-wide total across the checked sources. Rows sort by name
        /// (ordinal, case-insensitive). Returns an empty list, never null.
        /// </para>
        /// <para>What itemsById must be, and what character matching costs:
        /// docs/ARCHITECTURE.md, S2.5. Which name a row takes when its copies
        /// wear a skin: Services.TransmutedNameIndex.</para>
        /// </summary>
        public static List<SnapshotSearchRow> BuildItemRows(
            IReadOnlyDictionary<int, SnapshotItemEntry> itemsById,
            AccountItemIndex index,
            string searchText,
            SnapshotSourceFilter sourceFilter,
            string activeCharacterName,
            IReadOnlyDictionary<int, IReadOnlyList<TransmutedItemCopy>> transmutedCopies = null,
            IReadOnlyDictionary<int, List<SnapshotArmoryEquip>> armoryEquipped = null)
        {
            var rows = new List<SnapshotSearchRow>();

            if (itemsById == null || index == null)
            {
                return rows;
            }

            string trimmedSearch = (searchText ?? string.Empty).Trim();
            bool searching = trimmedSearch.Length > 0;

            // Allocated once per call, not once per row: the skin helpers
            // ask it about a stack's raw source, which is the same question
            // the breakdown loop below already answers for itself.
            Func<string, bool> sourceVisible = source => IsSourceEnabled(source, sourceFilter);

            // Same once-per-call reason. A socket place names the gear it
            // sits in, and this map is the only thing on this path that can
            // turn that gear's id into a name.
            Func<int, string> hostItemName = hostId =>
            {
                SnapshotItemEntry host;
                return itemsById.TryGetValue(hostId, out host) ? host.Name : null;
            };

            foreach (var kvp in itemsById)
            {
                int itemId = kvp.Key;

                // Never display raw item IDs (repo invariant).
                string ownName = string.IsNullOrWhiteSpace(kvp.Value.Name) ? "Unknown Item" : kvp.Value.Name;

                IReadOnlyList<TransmutedItemCopy> copies = null;
                if (transmutedCopies != null)
                {
                    transmutedCopies.TryGetValue(itemId, out copies);
                }

                bool nameMatches = !searching
                    || ownName.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0;

                var prioritizedSources = AccountItemIndex.GetPrioritizedSources(itemId, index, activeCharacterName);
                var breakdown = new List<SnapshotHoldLocation>();
                int total = 0;
                bool characterMatches = false;

                foreach (var source in prioritizedSources)
                {
                    if (!IsSourceEnabled(source, sourceFilter))
                    {
                        continue;
                    }

                    int quantity = index.GetQuantity(itemId, source);
                    if (quantity <= 0)
                    {
                        continue;
                    }

                    if (searching && !nameMatches && !characterMatches)
                    {
                        characterMatches = CharacterNameMatches(source, trimmedSearch);
                    }

                    var location = SnapshotHoldLine.FromSource(source, quantity, hostItemName);
                    if (location.Category == SnapshotHoldCategory.LegendaryArmory)
                    {
                        AttachArmoryDraws(
                            location, itemId, armoryEquipped, sourceFilter, hostItemName);
                    }

                    breakdown.Add(location);
                    total += quantity;
                }

                if (total <= 0)
                {
                    // Every source carrying this item was filtered out (or
                    // the item genuinely has zero quantity everywhere) -
                    // drop the row entirely rather than show a zero total.
                    continue;
                }

                // Both spellings, always: the item's own name, and every
                // skin a copy this row counted wears - which covers the
                // shown name, since that is one of them.
                if (!nameMatches
                    && TransmutedNameIndex.AnySkinNameMatches(
                        copies, trimmedSearch, sourceVisible))
                {
                    nameMatches = true;
                }

                if (!nameMatches && !characterMatches)
                {
                    continue;
                }

                // Read off the copies this row counted, the same set its
                // breakdown lists. A copy the filter hid can neither name
                // the row nor stop another copy from naming it.
                var skin = TransmutedNameIndex.AgreedSkin(copies, sourceVisible);
                string name = skin.IsPresent ? skin.Name : ownName;

                rows.Add(new SnapshotSearchRow
                {
                    ItemId = itemId,
                    Name = name,

                    // Name and icon come off the same value, so a row can
                    // never show one item's name over another's picture.
                    IconUrl = skin.IsPresent ? skin.IconUrl : (kvp.Value.IconUrl ?? string.Empty),

                    // From the first entry seen for this id, like Name and
                    // IconUrl: the same item in a bank slot and on a
                    // character is the same item, so any of its entries
                    // carries the same captured rarity.
                    Rarity = kvp.Value.Rarity ?? string.Empty,
                    Skin = skin,
                    TotalCount = total,
                    Breakdown = breakdown,
                });
            }

            // Secondary key (ItemId) guarantees a fully deterministic order
            // even when two distinct items share the exact same display
            // name - List<T>.Sort is not a stable sort, so without a
            // tiebreaker two same-named items could swap places between
            // otherwise-identical calls (e.g. two rebuilds for the same
            // keystroke) purely due to Dictionary enumeration order, which
            // is not a documented guarantee. Mirrors the same "sorted,
            // deterministic order" bar AccountItemIndex.GetSources already
            // holds itself to (see AccountItemIndexTests.
            // GetSources_ReturnsDeterministicOrder).
            rows.Sort((a, b) =>
            {
                int byName = string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : a.ItemId.CompareTo(b.ItemId);
            });
            return rows;
        }

        /// <summary>
        /// Every character name the snapshot knows about, deduped and sorted
        /// (case-insensitive, with an ordinal tiebreak so two names differing
        /// only by case keep a deterministic order). Drives the Snapshot
        /// tab's per-character source checkboxes, so it deliberately merges
        /// both rosters the snapshot carries: the character-owned
        /// item sources AND CharacterDisciplines - a character holding no
        /// items at all still gets a checkbox as long as the snapshot saw it
        /// somewhere. Zero-count item entries are kept here (unlike
        /// AccountItemIndex, which drops them) for the same reason: the row
        /// lists the roster, not what happens to be carried right now.
        /// Returns an empty list, never null, for a null snapshot.
        /// </summary>
        public static List<string> CollectCharacterNames(AccountSnapshot snapshot)
        {
            var names = new List<string>();
            if (snapshot == null)
            {
                return names;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);

            if (snapshot.Items != null)
            {
                foreach (var entry in snapshot.Items)
                {
                    if (!AccountItemIndex.TryGetCharacterName(entry?.Source, out string name))
                    {
                        continue;
                    }

                    if (name.Length > 0 && seen.Add(name))
                    {
                        names.Add(name);
                    }
                }
            }

            if (snapshot.CharacterDisciplines != null)
            {
                foreach (var discipline in snapshot.CharacterDisciplines)
                {
                    string name = discipline?.CharacterName;
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    {
                        names.Add(name);
                    }
                }
            }

            names.Sort((a, b) =>
            {
                int byName = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.CompareOrdinal(a, b);
            });
            return names;
        }

        /// <summary>
        /// Case-insensitive substring filter over wallet entries by
        /// currency name only (source filtering does not apply to
        /// Wallet - currencies have no per-source breakdown at all).
        /// Returns an empty list, never null, for a null
        /// <paramref name="wallet"/>; null entries within it are skipped.
        /// </summary>
        public static List<SnapshotWalletEntry> FilterWallet(IEnumerable<SnapshotWalletEntry> wallet, string searchText)
        {
            var result = new List<SnapshotWalletEntry>();
            if (wallet == null)
            {
                return result;
            }

            string trimmedSearch = (searchText ?? string.Empty).Trim();

            foreach (var entry in wallet)
            {
                if (entry == null)
                {
                    continue;
                }

                string name = entry.CurrencyName ?? string.Empty;
                if (trimmedSearch.Length == 0 ||
                    name.IndexOf(trimmedSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    result.Add(entry);
                }
            }

            return result;
        }

        /// <summary>
        /// Names the places drawing this armory item that the source filter
        /// still shows. Unchecking a character hides its bags and its worn
        /// gear, and unchecking the Bank hides a banked piece, so neither
        /// may go on being named under the Legendary Armory.
        /// <para>
        /// Wearers stay names; a socket anywhere else becomes a whole place
        /// phrase, because "Equipped" is not what a banked piece is
        /// (Models.SnapshotHoldLocation.SocketedInto). One phrase per place,
        /// so two banked pieces holding it read as one Bank.
        /// </para>
        /// </summary>
        private static void AttachArmoryDraws(
            SnapshotHoldLocation location,
            int itemId,
            IReadOnlyDictionary<int, List<SnapshotArmoryEquip>> armoryEquipped,
            SnapshotSourceFilter filter,
            Func<int, string> hostItemName)
        {
            if (armoryEquipped == null
                || !armoryEquipped.TryGetValue(itemId, out var draws)
                || draws == null)
            {
                return;
            }

            List<string> wearers = null;
            List<string> socketSources = null;
            var excluded = filter == null ? null : filter.UncheckedCharacters;

            for (int i = 0; i < draws.Count; i++)
            {
                var draw = draws[i];
                if (draw == null)
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(draw.Source))
                {
                    if (!IsSourceEnabled(draw.Source, filter))
                    {
                        continue;
                    }

                    if (socketSources == null)
                    {
                        socketSources = new List<string>();
                    }

                    socketSources.Add(draw.Source);
                    continue;
                }

                if (string.IsNullOrEmpty(draw.CharacterName)
                    || (excluded != null && excluded.Contains(draw.CharacterName)))
                {
                    continue;
                }

                if (wearers == null)
                {
                    wearers = new List<string>();
                }

                wearers.Add(draw.CharacterName);
            }

            location.EquippedBy = wearers;
            location.SocketedInto = socketSources == null
                ? null
                : SnapshotHoldLine.PlacePhrases(socketSources, hostItemName);
        }

        /// <summary>
        /// True when <paramref name="search"/> is at least
        /// <see cref="MinCharacterSearchLength"/> characters long and occurs
        /// (case-insensitively) in the character-name half of either
        /// character source encoding. The scan starts past the encoding
        /// prefix, so searching "char" matches a character actually named
        /// e.g. "Charr Hoarder" and never the internal token itself, and it
        /// takes no substring (this runs per source per item on the
        /// keystroke path).
        /// </summary>
        private static bool CharacterNameMatches(string rawSource, string search)
        {
            if (search.Length < MinCharacterSearchLength)
            {
                return false;
            }

            int offset = AccountItemIndex.CharacterNameOffset(rawSource);
            return offset >= 0
                && rawSource.IndexOf(search, offset, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// True when the raw AccountItemIndex source string passes the
        /// currently-checked categories in <paramref name="filter"/>. A
        /// null filter is treated as "show everything" (matches the
        /// controls' own all-checked default), as is a character whose name
        /// is absent from SnapshotSourceFilter.UncheckedCharacters. A raw
        /// source string that matches none of the known shapes
        /// (Bank/MaterialStorage/SharedInventory/LegendaryArmory, or either
        /// character encoding) is shown regardless -
        /// failing open rather than silently hiding real inventory data
        /// the module does not yet recognize (KNOWN-ISSUES #31's "never
        /// silently mask data" posture); there is no such source today.
        /// </summary>
        public static bool IsSourceEnabled(string rawSource, SnapshotSourceFilter filter)
        {
            if (filter == null)
            {
                return true;
            }

            if (string.IsNullOrEmpty(rawSource))
            {
                return false;
            }

            int characterNameOffset = AccountItemIndex.CharacterNameOffset(rawSource);
            if (characterNameOffset >= 0)
            {
                var excluded = filter.UncheckedCharacters;
                if (excluded == null || excluded.Count == 0)
                {
                    return true;
                }

                return !IsExcludedCharacter(rawSource, characterNameOffset, excluded);
            }

            // Matched against the container half of the key, so a socket
            // in a banked piece answers to the Bank checkbox. In-place
            // comparisons: this runs per source per item on the keystroke
            // path, and a plain key is its own container, so the common
            // case still takes no substring.
            if (AccountItemIndex.ContainerIs(rawSource, AccountItemIndex.SourceBank))
            {
                return filter.Bank;
            }

            if (AccountItemIndex.ContainerIs(rawSource, AccountItemIndex.SourceMaterialStorage))
            {
                return filter.MaterialStorage;
            }

            if (AccountItemIndex.ContainerIs(rawSource, AccountItemIndex.SourceSharedInventory))
            {
                return filter.SharedInventory;
            }

            if (AccountItemIndex.ContainerIs(rawSource, AccountItemIndex.SourceLegendaryArmory))
            {
                return filter.LegendaryArmory;
            }

            return true;
        }

        /// <summary>
        /// True when the character-name half of a character source appears
        /// in the exclusion set. One checkbox covers both of that
        /// character's encodings, so unchecking a character hides its bags
        /// and its worn gear together. Compares the name in place
        /// rather than taking a substring (this runs per source per item on
        /// the keystroke path), which trades the set's O(1) lookup for a
        /// scan of it - bounded by the roster, and only reached at all once
        /// the user has unchecked something. Ordinal, matching the
        /// comparer SnapshotSourceFilter's set is created with.
        /// </summary>
        private static bool IsExcludedCharacter(
            string rawSource, int prefixLength, HashSet<string> excluded)
        {
            int nameLength = rawSource.Length - prefixLength;

            foreach (string name in excluded)
            {
                if (name != null
                    && name.Length == nameLength
                    && string.CompareOrdinal(rawSource, prefixLength, name, 0, nameLength) == 0)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
