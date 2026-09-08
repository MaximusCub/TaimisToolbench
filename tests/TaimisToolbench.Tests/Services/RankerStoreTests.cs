using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // Real RankerStore against a real temp directory - no fake file I/O.
    // Mirrors SnapshotStoreTests'/PlanStoreTests' own fixture shape.
    public class RankerStoreTests : IDisposable
    {
        private readonly TempDirectory _temp = new TempDirectory();

        public void Dispose()
        {
            _temp.Dispose();
        }

        private string FilePath => Path.Combine(_temp.Path, "ranker.json");

        private static RankerWatchlist SampleWatchlist()
        {
            return new RankerWatchlist
            {
                Entries = new List<RankerWatchlistEntry>
                {
                    new RankerWatchlistEntry { ItemId = 30684, Quantity = 1, Name = "Twilight", IconUrl = "t.png", Rarity = "Legendary" },
                    new RankerWatchlistEntry { ItemId = 30689, Quantity = 2, Name = "Sunrise", IconUrl = "s.png", Rarity = "Legendary" },
                },
            };
        }

        [Fact]
        public void SaveThenLoad_PreservesEveryFieldAndListOrder()
        {
            var store = new RankerStore(_temp.Path);
            Assert.True(store.Save(SampleWatchlist()));

            var loaded = store.Load();

            Assert.Equal(RankerWatchlist.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal(2, loaded.Entries.Count);
            Assert.Equal(30684, loaded.Entries[0].ItemId);
            Assert.Equal("Twilight", loaded.Entries[0].Name);
            Assert.Equal("t.png", loaded.Entries[0].IconUrl);
            Assert.Equal("Legendary", loaded.Entries[0].Rarity);
            Assert.Equal(1, loaded.Entries[0].Quantity);
            Assert.Equal(30689, loaded.Entries[1].ItemId);
            Assert.Equal(2, loaded.Entries[1].Quantity);
        }

        [Fact]
        public void MissingFile_ReturnsEmptyWatchlistWithoutFiringOnError()
        {
            int errors = 0;
            var store = new RankerStore(_temp.Path, (_, __) => errors++);

            var loaded = store.Load();

            Assert.Empty(loaded.Entries);
            Assert.Equal(RankerWatchlist.CurrentSchemaVersion, loaded.SchemaVersion);
            Assert.Equal(0, errors);
        }

        [Fact]
        public void CorruptJson_ReturnsEmptyAndFiresOnErrorOnce_WithoutThrowing()
        {
            File.WriteAllText(FilePath, "{ this is not json");
            int errors = 0;
            var store = new RankerStore(_temp.Path, (_, __) => errors++);

            var loaded = store.Load();

            Assert.Empty(loaded.Entries);
            Assert.Equal(1, errors);
        }

        [Fact]
        public void SchemaVersionMismatch_ReturnsEmptyAndFiresOnErrorOnce()
        {
            var payload = SampleWatchlist();
            payload.SchemaVersion = RankerWatchlist.CurrentSchemaVersion + 1;
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(payload));

            int errors = 0;
            var store = new RankerStore(_temp.Path, (_, __) => errors++);

            Assert.Empty(store.Load().Entries);
            Assert.Equal(1, errors);
        }

        [Fact]
        public void FileOmittingSchemaVersion_DeserializesAsZeroAndIsRejected()
        {
            // This is exactly what the no-property-initializer rule on
            // RankerWatchlist.SchemaVersion buys, so it is asserted directly.
            File.WriteAllText(FilePath, "{ \"Entries\": [ { \"ItemId\": 1, \"Quantity\": 1 } ] }");

            int errors = 0;
            var store = new RankerStore(_temp.Path, (_, __) => errors++);

            Assert.Empty(store.Load().Entries);
            Assert.Equal(1, errors);
        }

        [Fact]
        public void FileAtTheFloorVersion_KeepsItsRowsAndIsRestampedByTheNextSave()
        {
            // The floor is a readable version, not a tolerated one. Today
            // it equals the current version, so this asserts the range's
            // lower end holds when the two later differ.
            var payload = SampleWatchlist();
            payload.SchemaVersion = RankerWatchlist.MinimumReadableSchemaVersion;
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(payload));

            int errors = 0;
            var store = new RankerStore(_temp.Path, (_, __) => errors++);

            var loaded = store.Load();

            Assert.Equal(0, errors);
            Assert.Equal(2, loaded.Entries.Count);
            Assert.Equal(30684, loaded.Entries[0].ItemId);

            store.Save(loaded);
            var written = JObject.Parse(File.ReadAllText(FilePath));
            Assert.Equal(RankerWatchlist.CurrentSchemaVersion, (int)written["SchemaVersion"]);
        }

        [Fact]
        public void FileBelowTheFloorVersion_ReturnsEmptyAndFiresOnErrorOnce()
        {
            var payload = SampleWatchlist();
            payload.SchemaVersion = RankerWatchlist.MinimumReadableSchemaVersion - 1;
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(payload));

            int errors = 0;
            var store = new RankerStore(_temp.Path, (_, __) => errors++);

            Assert.Empty(store.Load().Entries);
            Assert.Equal(1, errors);
        }

        [Fact]
        public void MinimumReadableSchemaVersion_IsNotAboveCurrent()
        {
            Assert.True(
                RankerWatchlist.MinimumReadableSchemaVersion <= RankerWatchlist.CurrentSchemaVersion,
                "RankerWatchlist.MinimumReadableSchemaVersion ("
                + RankerWatchlist.MinimumReadableSchemaVersion
                + ") is above CurrentSchemaVersion (" + RankerWatchlist.CurrentSchemaVersion
                + "), which leaves no readable version at all.");
        }

        [Fact]
        public void Save_WritesSchemaVersionEvenWhenTheCallerLeftItZero()
        {
            var store = new RankerStore(_temp.Path);
            var watchlist = SampleWatchlist();
            watchlist.SchemaVersion = 0;

            store.Save(watchlist);

            var written = JObject.Parse(File.ReadAllText(FilePath));
            Assert.Equal(RankerWatchlist.CurrentSchemaVersion, (int)written["SchemaVersion"]);
        }

        [Fact]
        public void Save_LeavesNoTempFileBehind()
        {
            var store = new RankerStore(_temp.Path);
            store.Save(SampleWatchlist());
            store.Save(SampleWatchlist());

            Assert.False(File.Exists(FilePath + ".tmp"));
            Assert.True(File.Exists(FilePath));
        }

        [Fact]
        public void ConcurrentSaves_LeaveAFileThatStillRoundTrips()
        {
            var store = new RankerStore(_temp.Path);

            Parallel.For(0, 24, i =>
            {
                var watchlist = new RankerWatchlist
                {
                    Entries = new List<RankerWatchlistEntry>
                    {
                        new RankerWatchlistEntry { ItemId = 1000 + i, Quantity = 1, Name = "Item " + i },
                    },
                };
                store.Save(watchlist);
            });

            var loaded = store.Load();
            Assert.Single(loaded.Entries);
            Assert.InRange(loaded.Entries[0].ItemId, 1000, 1023);
        }

        [Fact]
        public void Load_DropsMalformedEntriesAndNormalizesQuantity()
        {
            var payload = new RankerWatchlist
            {
                SchemaVersion = RankerWatchlist.CurrentSchemaVersion,
                Entries = new List<RankerWatchlistEntry>
                {
                    new RankerWatchlistEntry { ItemId = 0, Quantity = 1, Name = "Bogus" },
                    new RankerWatchlistEntry { ItemId = 42, Quantity = 0, Name = "Real" },
                    new RankerWatchlistEntry { ItemId = 43, Quantity = -5, Name = "Also real" },
                },
            };
            File.WriteAllText(FilePath, JsonConvert.SerializeObject(payload));

            var loaded = new RankerStore(_temp.Path).Load();

            Assert.Equal(new[] { 42, 43 }, loaded.Entries.Select(e => e.ItemId).ToArray());
            Assert.All(loaded.Entries, e => Assert.Equal(1, e.Quantity));
        }

        [Fact]
        public void ComparisonMode_RoundTripsThroughSaveAndLoad()
        {
            var store = new RankerStore(_temp.Path);
            var watchlist = SampleWatchlist();
            watchlist.Mode = RankerMode.Independent;

            Assert.True(store.Save(watchlist));

            Assert.Equal(RankerMode.Independent, store.Load().Mode);
        }

        [Fact]
        public void AFileWrittenBeforeTheModeFieldExisted_LoadsAsCascadeWithItsListIntact()
        {
            // The exact shape RankerStore wrote before the Mode field (and
            // before rarity was ever populated): schema 1, entries only.
            // Additive field, no schema bump - old lists must load whole.
            File.WriteAllText(FilePath,
                "{ \"SchemaVersion\": 1, \"Entries\": [ " +
                "{ \"ItemId\": 30684, \"Quantity\": 1, \"Name\": \"Twilight\", \"IconUrl\": \"t.png\" } ] }");

            int errors = 0;
            var loaded = new RankerStore(_temp.Path, (_, __) => errors++).Load();

            Assert.Equal(0, errors);
            Assert.Equal(RankerMode.Cascade, loaded.Mode);
            Assert.Single(loaded.Entries);
            Assert.Equal(30684, loaded.Entries[0].ItemId);
            Assert.Null(loaded.Entries[0].Rarity);
        }

        [Fact]
        public void ARarityAdoptedAfterLoad_SurvivesTheSaveAndTheNextLoad()
        {
            // The full field round trip: a list written before rarity was
            // ever populated, the adoption a refresh performs on it, and the
            // save that has to carry it across sessions.
            File.WriteAllText(FilePath,
                "{ \"SchemaVersion\": 1, \"Entries\": [ " +
                "{ \"ItemId\": 30684, \"Quantity\": 1, \"Name\": \"Twilight\", \"IconUrl\": \"t.png\" } ] }");

            var store = new RankerStore(_temp.Path);
            var loaded = store.Load();
            Assert.Null(loaded.Entries[0].Rarity);

            Assert.True(RankerRarityAdoption.AdoptFromStatCache(
                loaded.Entries, id => new ItemStatBlock { ItemId = id, Rarity = "Legendary" }));
            Assert.True(store.Save(loaded));

            Assert.Equal("Legendary", store.Load().Entries[0].Rarity);
        }

        [Fact]
        public void TheTwoDisplayToggles_PersistIndependentlyAndDefaultToOff()
        {
            var store = new RankerStore(_temp.Path);
            var watchlist = SampleWatchlist();

            Assert.False(watchlist.ShowCategories);
            Assert.False(watchlist.ShowCurrencies);

            // One at a time: a single stored flag driving both halves of the
            // breakdown is exactly the shape this replaced.
            watchlist.ShowCurrencies = true;
            Assert.True(store.Save(watchlist));

            var reloaded = store.Load();
            Assert.True(reloaded.ShowCurrencies);
            Assert.False(reloaded.ShowCategories);

            reloaded.ShowCategories = true;
            Assert.True(store.Save(reloaded));
            Assert.True(store.Load().ShowCategories);
            Assert.True(store.Load().ShowCurrencies);

            // A file carrying neither flag opens on the headline row alone,
            // list intact.
            File.WriteAllText(FilePath,
                "{ \"SchemaVersion\": 1, \"Mode\": 1, \"Entries\": [ " +
                "{ \"ItemId\": 30684, \"Quantity\": 1, \"Name\": \"Twilight\" } ] }");

            var loaded = store.Load();
            Assert.False(loaded.ShowCategories);
            Assert.False(loaded.ShowCurrencies);
            Assert.Equal(RankerMode.Independent, loaded.Mode);
            Assert.Single(loaded.Entries);
        }

        [Fact]
        public void Save_ToAnUnwritablePath_ReportsFailureRatherThanThrowing()
        {
            // A file where the directory should be: every Save path under it
            // fails, which is the "your list could not be saved" case the view
            // surfaces rather than losing the tab.
            string blocked = Path.Combine(_temp.Path, "blocked");
            File.WriteAllText(blocked, "not a directory");

            int errors = 0;
            var store = new RankerStore(Path.Combine(blocked, "nested"), (_, __) => errors++);

            Assert.False(store.Save(SampleWatchlist()));
            Assert.Equal(1, errors);
        }
    }
}
