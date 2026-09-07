using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VendorOfferUpdater;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    /// <summary>
    /// A cached miss is permanent: the resolution pass never asks about a
    /// name already in this cache. Recording one for a name the wiki never
    /// answered about therefore retires a real item for good, which is how
    /// six siege blueprints came to be sentinelled together. These tests hold
    /// the line between an answered absence and an unanswered question.
    /// </summary>
    public class ItemIdCacheTests : IDisposable
    {
        private readonly string _dir;

        public ItemIdCacheTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "vou-itemcache-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
            {
                Directory.Delete(_dir, true);
            }
        }

        private string Path_(string name) => Path.Combine(_dir, name);

        private static ItemIdResolution Resolution(
            IEnumerable<string> answered, params (string Name, int Id)[] resolved)
        {
            var resolution = new ItemIdResolution();
            foreach (var name in answered)
            {
                resolution.Answered.Add(name);
            }

            foreach (var (name, id) in resolved)
            {
                resolution.Resolved[name] = id;
            }

            return resolution;
        }

        // -- The defect ---------------------------------------------
        [Fact]
        public void AnUnansweredNameIsNotCachedAtAll()
        {
            var cache = new ItemIdCache();
            var requested = new[] { "Arrow Cart Blueprints", "Ballista Blueprints" };

            // The batch was refused: nothing answered, nothing resolved.
            var update = cache.Record(requested, Resolution(Array.Empty<string>()), DateTime.UtcNow);

            Assert.Equal(0, update.Hits);
            Assert.Equal(0, update.Misses);
            Assert.Equal(requested, update.Deferred);

            // The next run must ask again.
            Assert.False(cache.Contains("Arrow Cart Blueprints"));
            Assert.False(cache.Contains("Ballista Blueprints"));
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void AnAnsweredNameWithNoIdIsCachedAsAMiss()
        {
            var cache = new ItemIdCache();
            var when = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);

            var update = cache.Record(
                new[] { "Ancient  Coin" },
                Resolution(new[] { "Ancient  Coin" }),
                when);

            Assert.Equal(1, update.Misses);
            Assert.Empty(update.Deferred);
            Assert.True(cache.Contains("Ancient  Coin"));
            Assert.False(cache.Ids.ContainsKey("Ancient  Coin"));
            Assert.Equal(when, cache.Misses["Ancient  Coin"]);
        }

        [Fact]
        public void OneRefusedBatchDoesNotSentinelTheNamesTheOtherBatchAnswered()
        {
            var cache = new ItemIdCache();
            var requested = new[] { "Arrow Cart Blueprints", "Mystic Coin", "Not An Item" };

            // The wiki answered for two of the three. It resolved one of
            // those, and had no page for the other.
            var resolution = Resolution(
                new[] { "Mystic Coin", "Not An Item" },
                ("Mystic Coin", 19976));

            var update = cache.Record(requested, resolution, DateTime.UtcNow);

            Assert.Equal(1, update.Hits);
            Assert.Equal(1, update.Misses);
            Assert.Equal(new[] { "Arrow Cart Blueprints" }, update.Deferred);
            Assert.Equal(19976, cache.Ids["Mystic Coin"]);
            Assert.True(cache.Misses.ContainsKey("Not An Item"));
            Assert.False(cache.Contains("Arrow Cart Blueprints"));
        }

        [Fact]
        public void AResolvedNameClearsAnEarlierMiss()
        {
            var cache = new ItemIdCache();
            cache.RecordMiss("Arrow Cart Blueprints", DateTime.UtcNow);

            cache.Record(
                new[] { "Arrow Cart Blueprints" },
                Resolution(new[] { "Arrow Cart Blueprints" }, ("Arrow Cart Blueprints", 70754)),
                DateTime.UtcNow);

            Assert.Equal(70754, cache.Ids["Arrow Cart Blueprints"]);
            Assert.Empty(cache.Misses);
        }

        [Fact]
        public void ANameIsCachedUnderTheStringTheCostLineSpells()
        {
            var cache = new ItemIdCache();

            // The wiki was asked about the redirect's target; the cost line
            // still spells the name the redirect came from.
            var asked = new[]
            {
                new KeyValuePair<string, string>("Ectoplasm", "Glob of Ectoplasm"),
            };

            var update = cache.Record(
                asked,
                Resolution(new[] { "Glob of Ectoplasm" }, ("Glob of Ectoplasm", 19721)),
                DateTime.UtcNow);

            Assert.Equal(1, update.Hits);
            Assert.Equal(19721, cache.Ids["Ectoplasm"]);
            Assert.False(cache.Ids.ContainsKey("Glob of Ectoplasm"));
        }

        [Fact]
        public void AnUnansweredTargetDefersTheNameThatAskedAboutIt()
        {
            var cache = new ItemIdCache();
            var asked = new[]
            {
                new KeyValuePair<string, string>("Ectoplasm", "Glob of Ectoplasm"),
            };

            var update = cache.Record(asked, Resolution(Array.Empty<string>()), DateTime.UtcNow);

            Assert.Equal(new[] { "Ectoplasm" }, update.Deferred);
            Assert.False(cache.Contains("Ectoplasm"));
        }

        // -- Currency names -----------------------------------------
        [Fact]
        public void ACurrencyNameSettlesTheNameAndClearsAnEarlierMiss()
        {
            var cache = new ItemIdCache();
            cache.RecordMiss("Tale of Dungeon Delving", DateTime.UtcNow);

            cache.RecordCurrencyName("Tale of Dungeon Delving", "Tales of Dungeon Delving");

            Assert.True(cache.Contains("Tale of Dungeon Delving"));
            Assert.Empty(cache.Misses);
            Assert.Equal(
                "Tales of Dungeon Delving", cache.CurrencyNames["Tale of Dungeon Delving"]);
        }

        [Fact]
        public void AMissIsNotRecordedOverANamedCurrency()
        {
            var cache = new ItemIdCache();
            cache.RecordCurrencyName("Tale of Dungeon Delving", "Tales of Dungeon Delving");

            cache.RecordMiss("Tale of Dungeon Delving", DateTime.UtcNow);

            Assert.Empty(cache.Misses);
        }

        [Fact]
        public void ForgetCurrencyNameSendsTheNameBackToTheWiki()
        {
            var cache = new ItemIdCache();
            cache.RecordCurrencyName("Gaeting Crystal", "Gaeting Crystal");

            Assert.True(cache.ForgetCurrencyName("Gaeting Crystal"));
            Assert.False(cache.Contains("Gaeting Crystal"));
            Assert.False(cache.ForgetCurrencyName("Gaeting Crystal"));
        }

        [Fact]
        public void CurrencyNamesSurviveSaveAndLoad()
        {
            string path = Path_("currencies.json");

            var written = new ItemIdCache();
            written.RecordHit("Mystic Coin", 19976);
            written.RecordCurrencyName("Tale of Dungeon Delving", "Tales of Dungeon Delving");
            written.RecordMiss("Glory", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));
            written.Save(path);

            var read = ItemIdCache.Load(path);

            Assert.Equal(19976, read.Ids["Mystic Coin"]);
            Assert.Equal(
                "Tales of Dungeon Delving", read.CurrencyNames["Tale of Dungeon Delving"]);
            Assert.True(read.Misses.ContainsKey("Glory"));
            Assert.Equal(3, read.Count);
        }

        [Fact]
        public void ACacheWrittenBeforeCurrencyNamesExistedStillLoads()
        {
            string path = Path_("v2.json");
            File.WriteAllText(
                path,
                "{\n  \"cacheVersion\": 2,\n  \"ids\": { \"Mystic Coin\": 19976 },\n" +
                "  \"misses\": { \"Glory\": null }\n}");

            var cache = ItemIdCache.Load(path);

            Assert.Equal(19976, cache.Ids["Mystic Coin"]);
            Assert.True(cache.Misses.ContainsKey("Glory"));
            Assert.Empty(cache.CurrencyNames);
        }

        // -- Re-checking --------------------------------------------
        [Fact]
        public void ForgetMissesDropsMissesAndKeepsIds()
        {
            var cache = new ItemIdCache();
            cache.RecordHit("Mystic Coin", 19976);
            cache.RecordMiss("Ancient  Coin", DateTime.UtcNow);
            cache.RecordMiss("Ballista Blueprints", DateTime.UtcNow);

            Assert.Equal(2, cache.ForgetMisses());

            Assert.False(cache.Contains("Ancient  Coin"));
            Assert.False(cache.Contains("Ballista Blueprints"));
            Assert.Equal(19976, cache.Ids["Mystic Coin"]);
        }

        [Fact]
        public void OldestMissAgeIsReportedFromTheRecordedDates()
        {
            var cache = new ItemIdCache();
            var now = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
            cache.RecordMiss("Recent", now.AddDays(-3));
            cache.RecordMiss("Old", now.AddDays(-90));

            Assert.Equal(90, cache.OldestMissAge(now)!.Value.TotalDays, 3);
            Assert.Equal(0, cache.UndatedMissCount);
        }

        // -- On-disk shape ------------------------------------------
        [Fact]
        public void MissingFileIsAColdStartNotAnError()
        {
            var cache = ItemIdCache.Load(Path_("nothing-here.json"));

            Assert.Equal(0, cache.Count);
            Assert.Empty(cache.Ids);
            Assert.Empty(cache.Misses);
        }

        [Fact]
        public void UnparseableFileIsIgnoredRatherThanThrown()
        {
            string path = Path_("corrupt.json");
            File.WriteAllText(path, "{ this is not json");

            var cache = ItemIdCache.Load(path);

            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void TheOldFlatFormatIsMigratedRatherThanDiscarded()
        {
            string path = Path_("v1.json");
            File.WriteAllText(
                path,
                "{\n  \"Arrow Cart Blueprints\": -1,\n  \"Mystic Coin\": 19976\n}");

            var cache = ItemIdCache.Load(path);

            Assert.Equal(19976, cache.Ids["Mystic Coin"]);
            Assert.True(cache.Misses.ContainsKey("Arrow Cart Blueprints"));

            // The old format recorded no date, so the age is unknown rather
            // than assumed to be today.
            Assert.Null(cache.Misses["Arrow Cart Blueprints"]);
            Assert.Equal(1, cache.UndatedMissCount);
            Assert.Null(cache.OldestMissAge(DateTime.UtcNow));
        }

        [Fact]
        public void SaveAndLoadRoundTripIdsAndDatedMisses()
        {
            string path = Path_("round-trip.json");
            var when = new DateTime(2026, 7, 1, 8, 30, 0, DateTimeKind.Utc);

            var written = new ItemIdCache();
            written.RecordHit("Mystic Coin", 19976);
            written.RecordMiss("Ancient  Coin", when);
            written.Save(path);

            var read = ItemIdCache.Load(path);

            Assert.Equal(19976, read.Ids["Mystic Coin"]);
            Assert.Equal(when, read.Misses["Ancient  Coin"]);
        }

        [Fact]
        public void TheWrittenFileStaysReadable()
        {
            var cache = new ItemIdCache();
            cache.RecordHit("Mystic Coin", 19976);
            cache.RecordHit("Ancient Coin", 19975);
            cache.RecordMiss("Ancient  Coin", new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc));

            using var doc = JsonDocument.Parse(cache.Serialize());
            var root = doc.RootElement;

            Assert.Equal(ItemIdCache.CurrentVersion, root.GetProperty("cacheVersion").GetInt32());
            Assert.Equal(19976, root.GetProperty("ids").GetProperty("Mystic Coin").GetInt32());
            Assert.Equal(
                JsonValueKind.String,
                root.GetProperty("misses").GetProperty("Ancient  Coin").ValueKind);

            // Sorted, so a diff of this file reads as a diff and not a reshuffle.
            var ids = root.GetProperty("ids").EnumerateObject().Select(p => p.Name).ToList();
            Assert.Equal(new[] { "Ancient Coin", "Mystic Coin" }, ids);
        }
    }
}
