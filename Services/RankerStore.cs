using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Persists the Crafting Ranker's priority list to data/ranker.json.
    /// Atomic .tmp + Replace under a save lock, matching PlanStore.
    ///
    /// Schema mismatch is FORGIVING, unlike PlanStore's throw: an unreadable
    /// watchlist should cost the user their list, not their tab. One Warn
    /// through onError, then an empty list the next Save overwrites.
    ///
    /// No solve results are stored. Every readiness number is ephemeral and
    /// session-scoped - they go stale the moment Trading Post prices move,
    /// and a persisted one would render as if it were current.
    /// </summary>
    public class RankerStore
    {
        private readonly string _filePath;
        private readonly Action<string, Exception> _onError;
        private readonly object _saveLock = new object();

        public RankerStore(string dataDirectoryPath, Action<string, Exception> onError = null)
        {
            _filePath = Path.Combine(dataDirectoryPath, "ranker.json");
            _onError = onError;
        }

        /// <summary>
        /// Never null, never throws. A missing file is first run, not a
        /// failure, and does not fire onError.
        /// </summary>
        public RankerWatchlist Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return new RankerWatchlist { SchemaVersion = RankerWatchlist.CurrentSchemaVersion };
                }

                string json = File.ReadAllText(_filePath);
                var loaded = JsonConvert.DeserializeObject<RankerWatchlist>(json);

                if (loaded == null || !IsReadableVersion(loaded.SchemaVersion))
                {
                    // A RANGE, not an equality, for the reason
                    // PlanHistoryStore.Load's own comment gives: an
                    // equality makes the next bump cost the user their
                    // list. Save restamps whatever the range accepted.
                    _onError?.Invoke(
                        $"Ranker list at {_filePath} is schema {loaded?.SchemaVersion.ToString() ?? "unreadable"}, and this module reads {RankerWatchlist.MinimumReadableSchemaVersion} to {RankerWatchlist.CurrentSchemaVersion}; starting from an empty list",
                        null);
                    return new RankerWatchlist { SchemaVersion = RankerWatchlist.CurrentSchemaVersion };
                }

                if (loaded.Entries == null)
                {
                    loaded.Entries = new List<RankerWatchlistEntry>();
                }

                // A malformed entry would render as a blank row that no
                // solve can resolve; drop it rather than carry it.
                loaded.Entries.RemoveAll(e => e == null || e.ItemId <= 0);
                foreach (var entry in loaded.Entries)
                {
                    if (entry.Quantity < 1)
                    {
                        entry.Quantity = 1;
                    }
                }

                return loaded;
            }
            catch (Exception ex)
            {
                _onError?.Invoke($"Failed to load the ranker list from {_filePath}", ex);
                return new RankerWatchlist { SchemaVersion = RankerWatchlist.CurrentSchemaVersion };
            }
        }

        /// <summary>Returns false when the write failed, so the view can say so.</summary>
        public bool Save(RankerWatchlist watchlist)
        {
            if (watchlist == null)
            {
                return false;
            }

            // Set explicitly rather than relying on an initializer, so a
            // watchlist built by deserialization cannot carry a stale value
            // forward.
            watchlist.SchemaVersion = RankerWatchlist.CurrentSchemaVersion;

            lock (_saveLock)
            {
                try
                {
                    string dir = Path.GetDirectoryName(_filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    // Indented: the file is a few hundred bytes and follows
                    // SnapshotHelpers/StatusStore's precedent, not
                    // PlanStoreHelpers' compact one (which exists only
                    // because a PersistedPlan is hundreds of KB).
                    string json = JsonConvert.SerializeObject(watchlist, Formatting.Indented);
                    string tmpPath = _filePath + ".tmp";
                    File.WriteAllText(tmpPath, json);

                    if (File.Exists(_filePath))
                    {
                        File.Replace(tmpPath, _filePath, null);
                    }
                    else
                    {
                        File.Move(tmpPath, _filePath);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    _onError?.Invoke($"Failed to save the ranker list to {_filePath}", ex);
                    return false;
                }
            }
        }

        private static bool IsReadableVersion(int schemaVersion)
        {
            return schemaVersion >= RankerWatchlist.MinimumReadableSchemaVersion
                && schemaVersion <= RankerWatchlist.CurrentSchemaVersion;
        }
    }
}
