using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace VendorOfferUpdater
{
    /// <summary>
    /// What one pass over a batch of item names recorded in the cache.
    /// </summary>
    internal sealed class ItemIdCacheUpdate
    {
        public int Hits { get; set; }

        public int Misses { get; set; }

        /// <summary>
        /// Names the wiki never answered for. Nothing is cached for these, so
        /// the next run asks again.
        /// </summary>
        public List<string> Deferred { get; } = new List<string>();
    }

    /// <summary>
    /// Settles a wiki cost name three ways: an item's GW2 game id, the name
    /// of the wallet currency it turns out to be, or a remembered miss so the
    /// name is not asked about again on every run.
    /// <para>
    /// A miss is only ever recorded for a name the wiki actually answered. A
    /// name in a batch that was refused, failed, or never sent is left out of
    /// the cache entirely: it is a question still to ask, not a known absence.
    /// Every miss carries the date it was recorded, and --recheck-misses drops
    /// them so they are asked again.
    /// </para>
    /// </summary>
    internal sealed class ItemIdCache
    {
        internal const int CurrentVersion = 3;

        private readonly Dictionary<string, int> _ids =
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // A cost name that names a currency page, mapped to the API's own
        // name for that currency. The name and not the id, because the API is
        // loaded fresh every run and is the authority on which currencies are
        // live: a currency retired from the game leaves an entry that
        // resolves to nothing and is dropped, where a cached id would go on
        // pricing offers in a currency no account can hold.
        private readonly Dictionary<string, string> _currencyNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Value is the UTC date the miss was recorded, or null for one
        // migrated from the version 1 format, which stored misses as -1 with
        // no date at all.
        private readonly Dictionary<string, DateTime?> _misses =
            new Dictionary<string, DateTime?>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Resolved names and their game ids. Never holds a miss.</summary>
        internal IReadOnlyDictionary<string, int> Ids => _ids;

        internal IReadOnlyDictionary<string, DateTime?> Misses => _misses;

        /// <summary>Cost names that name a currency, and the API's name for it.</summary>
        internal IReadOnlyDictionary<string, string> CurrencyNames => _currencyNames;

        internal int Count => _ids.Count + _misses.Count + _currencyNames.Count;

        /// <summary>
        /// Whether this name has been settled, any of the three ways. A run
        /// only asks the wiki about names this returns false for.
        /// </summary>
        internal bool Contains(string name)
        {
            return _ids.ContainsKey(name)
                || _currencyNames.ContainsKey(name)
                || _misses.ContainsKey(name);
        }

        internal void RecordHit(string name, int gameId)
        {
            _currencyNames.Remove(name);
            _misses.Remove(name);
            _ids[name] = gameId;
        }

        /// <summary>
        /// Records that this cost name is the currency the API calls
        /// <paramref name="currencyName"/>, not an item.
        /// </summary>
        internal void RecordCurrencyName(string name, string currencyName)
        {
            _ids.Remove(name);
            _misses.Remove(name);
            _currencyNames[name] = currencyName;
        }

        /// <summary>
        /// Drops a remembered currency name so the next run asks about it
        /// again. Returns whether there was one.
        /// </summary>
        internal bool ForgetCurrencyName(string name)
        {
            return _currencyNames.Remove(name);
        }

        internal void RecordMiss(string name, DateTime recordedUtc)
        {
            if (_ids.ContainsKey(name) || _currencyNames.ContainsKey(name))
            {
                return;
            }

            _misses[name] = recordedUtc;
        }

        /// <summary>
        /// Drops every remembered miss so the next resolution pass asks about
        /// those names again. Resolved ids are untouched.
        /// </summary>
        internal int ForgetMisses()
        {
            int count = _misses.Count;
            _misses.Clear();
            return count;
        }

        /// <summary>
        /// Records a resolution pass. A name the wiki answered for gets an id
        /// or a dated miss; a name in a batch that went unanswered gets
        /// nothing, and is returned in
        /// <see cref="ItemIdCacheUpdate.Deferred"/>.
        /// </summary>
        internal ItemIdCacheUpdate Record(
            IEnumerable<string> requested, ItemIdResolution resolution, DateTime recordedUtc)
        {
            return Record(
                requested.Select(name => new KeyValuePair<string, string>(name, name)),
                resolution,
                recordedUtc);
        }

        /// <summary>
        /// Records a pass whose queried titles are not the names being
        /// cached. The key of each pair is the wiki's display string, which
        /// is how a cost line spells the name and therefore how the cache is
        /// keyed; the value is the page title the wiki was actually asked
        /// about. The two differ wherever the display string is a redirect.
        /// </summary>
        internal ItemIdCacheUpdate Record(
            IEnumerable<KeyValuePair<string, string>> requested,
            ItemIdResolution resolution,
            DateTime recordedUtc)
        {
            var update = new ItemIdCacheUpdate();

            foreach (var pair in requested)
            {
                if (resolution.Resolved.TryGetValue(pair.Value, out int id) && id > 0)
                {
                    RecordHit(pair.Key, id);
                    update.Hits++;
                }
                else if (resolution.Answered.Contains(pair.Value))
                {
                    RecordMiss(pair.Key, recordedUtc);
                    update.Misses++;
                }
                else
                {
                    update.Deferred.Add(pair.Key);
                }
            }

            return update;
        }

        /// <summary>
        /// The age of the oldest dated miss, or null when there are none.
        /// Undated misses (migrated from version 1) are reported separately by
        /// <see cref="UndatedMissCount"/>, since their age is not known.
        /// </summary>
        internal TimeSpan? OldestMissAge(DateTime utcNow)
        {
            DateTime? oldest = null;
            foreach (var recorded in _misses.Values)
            {
                if (recorded.HasValue && (oldest == null || recorded.Value < oldest.Value))
                {
                    oldest = recorded.Value;
                }
            }

            return oldest == null ? null : utcNow - oldest.Value;
        }

        internal int UndatedMissCount => _misses.Values.Count(v => !v.HasValue);

        /// <summary>
        /// Reads the cache. A file that is not there is a cold start, not an
        /// error: every name simply resolves fresh. A file in the version 1
        /// format (a flat name-to-id map, with -1 for a miss) is migrated,
        /// with its misses left undated because that format recorded no date.
        /// </summary>
        internal static ItemIdCache Load(string path)
        {
            var cache = new ItemIdCache();

            if (!File.Exists(path))
            {
                Console.WriteLine(
                    $"No item ID cache at {path} - starting cold, every name resolves fresh.");
                return cache;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    Console.WriteLine(
                        $"WARNING: item ID cache at {path} is not a JSON object - ignoring it.");
                    return cache;
                }

                if (root.TryGetProperty("ids", out _) ||
                    root.TryGetProperty("currencies", out _) ||
                    root.TryGetProperty("misses", out _))
                {
                    ReadCurrent(cache, root);
                }
                else
                {
                    ReadVersionOne(cache, root);
                }

                Console.WriteLine(
                    $"Loaded item ID cache from {path}: {cache._ids.Count} resolved, " +
                    $"{cache._currencyNames.Count} currency name(s), " +
                    $"{cache._misses.Count} remembered misses.");
            }
            catch (JsonException ex)
            {
                Console.WriteLine(
                    $"WARNING: item ID cache at {path} did not parse ({ex.Message}). " +
                    "Ignoring it; every name resolves fresh.");
                return new ItemIdCache();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Console.WriteLine(
                    $"WARNING: item ID cache at {path} could not be read ({ex.Message}). " +
                    "Ignoring it; every name resolves fresh.");
                return new ItemIdCache();
            }

            return cache;
        }

        internal string Serialize()
        {
            var document = new Dictionary<string, object>
            {
                ["cacheVersion"] = CurrentVersion,
                ["ids"] = _ids
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                ["currencies"] = _currencyNames
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(kv => kv.Key, kv => kv.Value),
                ["misses"] = _misses
                    .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        kv => kv.Key,
                        kv => kv.Value?.ToString("o", CultureInfo.InvariantCulture)),
            };

            var options = new JsonSerializerOptions { WriteIndented = true };
            return JsonSerializer.Serialize(document, options);
        }

        internal void Save(string path)
        {
            File.WriteAllText(path, Serialize());
            Console.WriteLine(
                $"  Saved item ID cache to {path}: {_ids.Count} resolved, " +
                $"{_currencyNames.Count} currency name(s), " +
                $"{_misses.Count} remembered misses.");
        }

        private static void ReadCurrent(ItemIdCache cache, JsonElement root)
        {
            if (root.TryGetProperty("ids", out var ids) && ids.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in ids.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.Number &&
                        prop.Value.TryGetInt32(out int id) && id > 0)
                    {
                        cache._ids[prop.Name] = id;
                    }
                }
            }

            if (root.TryGetProperty("currencies", out var currencies) &&
                currencies.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in currencies.EnumerateObject())
                {
                    if (prop.Value.ValueKind == JsonValueKind.String &&
                        !cache._ids.ContainsKey(prop.Name))
                    {
                        cache._currencyNames[prop.Name] = prop.Value.GetString()!;
                    }
                }
            }

            if (root.TryGetProperty("misses", out var misses) &&
                misses.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in misses.EnumerateObject())
                {
                    // A hand-edited file could list a name in more than one
                    // section. A resolved id or a named currency is the
                    // stronger statement, so either wins over a miss.
                    if (cache._ids.ContainsKey(prop.Name) ||
                        cache._currencyNames.ContainsKey(prop.Name))
                    {
                        continue;
                    }

                    DateTime? recorded = null;
                    if (prop.Value.ValueKind == JsonValueKind.String &&
                        DateTime.TryParse(
                            prop.Value.GetString(),
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                            out var parsed))
                    {
                        recorded = parsed;
                    }

                    cache._misses[prop.Name] = recorded;
                }
            }
        }

        private static void ReadVersionOne(ItemIdCache cache, JsonElement root)
        {
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Number ||
                    !prop.Value.TryGetInt32(out int value))
                {
                    continue;
                }

                if (value > 0)
                {
                    cache._ids[prop.Name] = value;
                }
                else
                {
                    cache._misses[prop.Name] = null;
                }
            }
        }
    }
}
