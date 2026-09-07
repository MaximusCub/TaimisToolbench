using System;
using System.Collections.Generic;
using System.IO;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using Xunit.Abstractions;

namespace TaimisToolbench.Tests.Services
{
    public class SnapshotStoreTests : IDisposable
    {
        private const int RealisticStackCount = 4000;

        private readonly string _tempDir;
        private readonly SnapshotStore _store;
        private readonly ITestOutputHelper _output;

        public SnapshotStoreTests(ITestOutputHelper output)
        {
            _output = output;
            _tempDir = Path.Combine(Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _store = new SnapshotStore(_tempDir);
        }

        private string SnapshotPath => Path.Combine(_tempDir, "snapshot.json");

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
        }

        private static AccountSnapshot CreateSnapshot(int coinCopper = 0)
        {
            return new AccountSnapshot
            {
                CapturedAt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc),
                CoinCopper = coinCopper,
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 1, Name = "Item", Count = 5, Source = "Bank" },
                },
                Wallet = new List<SnapshotWalletEntry>
                {
                    new SnapshotWalletEntry { CurrencyId = 2, CurrencyName = "Karma", Value = 1000 },
                },
            };
        }

        [Fact]
        public void Save_Load_PreservesCapturedAtAndCoinCopper()
        {
            var snapshot = CreateSnapshot(123456);
            _store.Save(snapshot);

            var loaded = _store.LoadLatest();
            Assert.NotNull(loaded);
            Assert.Equal(snapshot.CapturedAt, loaded.CapturedAt);
            Assert.Equal(123456, loaded.CoinCopper);
        }

        [Fact]
        public void Save_Load_ProducesNewInstance()
        {
            var snapshot = CreateSnapshot();
            _store.Save(snapshot);

            var loaded = _store.LoadLatest();
            Assert.NotNull(loaded);
            Assert.NotSame(snapshot, loaded);
        }

        [Fact]
        public void Save_Overwrite_LoadReturnsLatest()
        {
            _store.Save(CreateSnapshot(100));
            _store.Save(CreateSnapshot(999));

            var loaded = _store.LoadLatest();
            Assert.NotNull(loaded);
            Assert.Equal(999, loaded.CoinCopper);
        }

        [Fact]
        public void Delete_RemovesSnapshot_LoadReturnsNull()
        {
            _store.Save(CreateSnapshot());
            Assert.NotNull(_store.LoadLatest());

            _store.Delete();
            Assert.Null(_store.LoadLatest());
        }

        [Fact]
        public void Delete_NoFile_DoesNotThrow()
        {
            _store.Delete();
        }

        // --- One-store convention: atomic .tmp+Replace, matching
        // StatusStore/VendorOfferStore - previously a plain, non-atomic
        // File.WriteAllText (dev/proposals/tab-roadmap-proposal.md Section 2.2's
        // correction). ---
        [Fact]
        public void Save_LeavesNoTmpFileBehind()
        {
            _store.Save(CreateSnapshot(42));

            string tmpPath = Path.Combine(_tempDir, "snapshot.json.tmp");
            Assert.False(File.Exists(tmpPath));
        }

        [Fact]
        public void Save_Overwrite_LeavesNoTmpFileBehindEither()
        {
            _store.Save(CreateSnapshot(1));
            _store.Save(CreateSnapshot(2));

            string tmpPath = Path.Combine(_tempDir, "snapshot.json.tmp");
            Assert.False(File.Exists(tmpPath));
            Assert.Equal(2, _store.LoadLatest().CoinCopper);
        }

        // --- onError callback: real IO failure. ---
        [Fact]
        public void Save_DirectoryCreationFails_InvokesOnErrorInsteadOfThrowing()
        {
            string blockingPath = Path.Combine(_tempDir, "blocked-data-dir");
            File.WriteAllText(blockingPath, "not a directory");

            string capturedMessage = null;
            Exception capturedException = null;
            var store = new SnapshotStore(blockingPath, (message, ex) =>
            {
                capturedMessage = message;
                capturedException = ex;
            });

            store.Save(CreateSnapshot());

            Assert.NotNull(capturedMessage);
            Assert.NotNull(capturedException);
        }

        // --- Per-character discipline display: real SnapshotStore
        // round-trip with the per-character discipline data. ---
        [Fact]
        public void Save_Load_RoundTripsCharacterDisciplines()
        {
            var snapshot = CreateSnapshot();
            snapshot.CharacterDisciplines = new List<SnapshotCharacterDiscipline>
            {
                new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Weaponsmith", Rating = 500, Active = true },
                new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Huntsman", Rating = 400, Active = false },
                new SnapshotCharacterDiscipline { CharacterName = "Bob", Discipline = "Weaponsmith", Rating = 250, Active = true },
            };
            _store.Save(snapshot);

            var loaded = _store.LoadLatest();

            Assert.NotNull(loaded);
            Assert.NotNull(loaded.CharacterDisciplines);
            Assert.Equal(3, loaded.CharacterDisciplines.Count);

            var annaWeaponsmith = loaded.CharacterDisciplines.Find(
                cd => cd.CharacterName == "Anna" && cd.Discipline == "Weaponsmith");
            Assert.NotNull(annaWeaponsmith);
            Assert.Equal(500, annaWeaponsmith.Rating);
            Assert.True(annaWeaponsmith.Active);

            var annaHuntsman = loaded.CharacterDisciplines.Find(
                cd => cd.CharacterName == "Anna" && cd.Discipline == "Huntsman");
            Assert.NotNull(annaHuntsman);
            Assert.Equal(400, annaHuntsman.Rating);
            Assert.False(annaHuntsman.Active);
        }

        [Fact]
        public void Save_Load_NullCharacterDisciplines_RoundTripsAsNull()
        {
            // CreateSnapshot() never sets CharacterDisciplines - mirrors a
            // legacy snapshot object built by code that has not been
            // updated to populate it (distinct from a newer capture
            // that legitimately came back empty - see AccountSnapshot.
            // CharacterDisciplines' own doc comment).
            var snapshot = CreateSnapshot();
            _store.Save(snapshot);

            var loaded = _store.LoadLatest();

            Assert.NotNull(loaded);
            Assert.Null(loaded.CharacterDisciplines);
        }

        // --- Socketed upgrades and infusions. ---
        [Fact]
        public void Save_Load_RoundTripsSocketedIds()
        {
            var snapshot = CreateSnapshot();
            snapshot.Items.Add(new SnapshotItemEntry
            {
                ItemId = 2,
                Name = "Sword",
                Count = 1,
                Source = "Character:Anna",
                Upgrades = new List<int> { 24615, 24554 },
                Infusions = new List<int> { 37131 },
            });

            _store.Save(snapshot);
            var loaded = _store.LoadLatest();

            Assert.NotNull(loaded);
            var socketed = loaded.Items.Find(i => i.ItemId == 2);
            Assert.NotNull(socketed);

            // Order is the socket order the API reports, so this is
            // sequence equality rather than set equality.
            Assert.Equal(new[] { 24615, 24554 }, socketed.Upgrades);
            Assert.Equal(new[] { 37131 }, socketed.Infusions);

            var unsocketed = loaded.Items.Find(i => i.ItemId == 1);
            Assert.NotNull(unsocketed);
            Assert.Null(unsocketed.Upgrades);
            Assert.Null(unsocketed.Infusions);
        }

        [Fact]
        public void Load_FileWithoutTheSocketFields_LoadsWithThemNull()
        {
            // A snapshot.json exactly as the build before these fields
            // shipped wrote it. It must load rather than be discarded: the
            // snapshot is the account's only offline record, and the format
            // carries no version stamp to fall back on.
            File.WriteAllText(SnapshotPath, @"{
  ""CapturedAt"": ""2025-06-15T12:00:00Z"",
  ""CoinCopper"": 4242,
  ""Items"": [
    { ""ItemId"": 1, ""Name"": ""Item"", ""IconUrl"": """", ""Rarity"": ""Basic"", ""Count"": 5, ""Source"": ""Bank"" }
  ],
  ""Wallet"": []
}");

            var loaded = _store.LoadLatest();

            Assert.NotNull(loaded);
            Assert.Equal(4242, loaded.CoinCopper);
            Assert.Single(loaded.Items);
            Assert.Equal(5, loaded.Items[0].Count);
            Assert.Null(loaded.Items[0].Upgrades);
            Assert.Null(loaded.Items[0].Infusions);
        }

        // --- The skin a stack wears. ---
        [Fact]
        public void Save_Load_RoundTripsTheSkinAStackWears()
        {
            var snapshot = CreateSnapshot();
            snapshot.Items.Add(new SnapshotItemEntry
            {
                ItemId = 2,
                Name = "Zojja's Blade",
                Count = 1,
                Source = "Equipped:Anna",
                SkinId = 5432,
                SkinName = "Glyphic Edge",
                SkinIconUrl = "https://render.guildwars2.com/file/skin.png",
            });

            _store.Save(snapshot);
            string json = File.ReadAllText(SnapshotPath);
            var loaded = _store.LoadLatest();

            Assert.NotNull(loaded);
            var skinned = loaded.Items.Find(i => i.ItemId == 2);
            Assert.NotNull(skinned);
            Assert.Equal(5432, skinned.SkinId);
            Assert.Equal("Glyphic Edge", skinned.SkinName);
            Assert.Equal("https://render.guildwars2.com/file/skin.png", skinned.SkinIconUrl);

            // A stack wearing its own look writes neither field, the way a
            // stack with nothing socketed writes no socket ids.
            var bare = loaded.Items.Find(i => i.ItemId == 1);
            Assert.NotNull(bare);
            Assert.Equal(0, bare.SkinId);
            Assert.Equal("", bare.SkinName);
            Assert.Equal("", bare.SkinIconUrl);
            Assert.Equal(1, CountOccurrences(json, "SkinId"));
            Assert.Equal(1, CountOccurrences(json, "SkinName"));
            Assert.Equal(1, CountOccurrences(json, "SkinIconUrl"));
        }

        [Fact]
        public void Load_FileWithoutTheSkinFields_LoadsWithNoSkin()
        {
            // A snapshot.json exactly as the build before these fields
            // shipped wrote it, for the same reason
            // Load_FileWithoutTheSocketFields_LoadsWithThemNull gives.
            File.WriteAllText(SnapshotPath, @"{
  ""CapturedAt"": ""2025-06-15T12:00:00Z"",
  ""CoinCopper"": 4242,
  ""Items"": [
    { ""ItemId"": 1, ""Name"": ""Item"", ""IconUrl"": """", ""Rarity"": ""Basic"", ""Count"": 5, ""Source"": ""Bank"" }
  ],
  ""Wallet"": []
}");

            var loaded = _store.LoadLatest();

            Assert.NotNull(loaded);
            Assert.Single(loaded.Items);
            Assert.Equal(0, loaded.Items[0].SkinId);
            Assert.Equal("", loaded.Items[0].SkinName);
            Assert.Equal("", loaded.Items[0].SkinIconUrl);
        }

        private static int CountOccurrences(string haystack, string needle)
        {
            int count = 0;
            for (int i = haystack.IndexOf(needle, StringComparison.Ordinal); i >= 0;
                i = haystack.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        [Fact]
        public void Load_SnapshotJsonWrittenBeforeEquipmentWasCaptured_StillLoads()
        {
            // A file on disk from a build that read only bags, bank, shared
            // inventory and material storage. Equipped items are ordinary
            // rows under the same "Character:<name>" source a bag row uses,
            // so nothing about this file's shape changed and it must load
            // whole rather than being discarded.
            File.WriteAllText(
                SnapshotPath,
                "{\"CapturedAt\":\"2026-03-01T09:30:00Z\",\"CoinCopper\":4242," +
                "\"Items\":[" +
                "{\"ItemId\":19721,\"Name\":\"Glob of Ectoplasm\",\"Count\":37,\"Source\":\"MaterialStorage\"}," +
                "{\"ItemId\":19685,\"Name\":\"Orichalcum Ore\",\"Count\":12,\"Source\":\"Character:Taimi\"}]," +
                "\"Wallet\":[{\"CurrencyId\":2,\"CurrencyName\":\"Karma\",\"Value\":8000}]}");

            var loaded = _store.LoadLatest();

            Assert.NotNull(loaded);
            Assert.Equal(4242, loaded.CoinCopper);
            Assert.Equal(2, loaded.Items.Count);
            Assert.Equal(19721, loaded.Items[0].ItemId);
            Assert.Equal(37, loaded.Items[0].Count);
            Assert.Equal("Character:Taimi", loaded.Items[1].Source);
            Assert.Single(loaded.Wallet);
            Assert.Null(loaded.CharacterDisciplines);
        }

        [Fact]
        public void Save_StacksWithNothingSocketed_CostNoBytes()
        {
            _store.Save(RealisticAccountSnapshot(socketedStacks: 0));
            long unsocketedBytes = new FileInfo(SnapshotPath).Length;
            string unsocketedJson = File.ReadAllText(SnapshotPath);

            const int SocketedStacks = 200;
            _store.Save(RealisticAccountSnapshot(SocketedStacks));
            long socketedBytes = new FileInfo(SnapshotPath).Length;

            _output.WriteLine("snapshot.json, " + RealisticStackCount
                + " stacks, none socketed: " + unsocketedBytes + " bytes");
            _output.WriteLine("snapshot.json, " + RealisticStackCount + " stacks, "
                + SocketedStacks + " socketed: " + socketedBytes + " bytes");

            // The point of the null-means-nothing-socketed shape: a stack
            // with empty sockets writes the bytes it wrote before the two
            // fields existed.
            Assert.DoesNotContain("Upgrades", unsocketedJson);
            Assert.DoesNotContain("Infusions", unsocketedJson);

            // What it does cost is confined to the rows that carry
            // something. A per-row ceiling, not a measurement: the
            // measurement is the two lines printed above.
            Assert.True(socketedBytes > unsocketedBytes);
            Assert.True(socketedBytes - unsocketedBytes < SocketedStacks * 256,
                "socketed ids cost " + (socketedBytes - unsocketedBytes)
                + " bytes across " + SocketedStacks + " stacks");
        }

        // Stack count and field shape in the range a full account produces
        // across bank, shared inventory, material storage and every
        // character's bags, with the name, icon URL and rarity the fetch
        // resolves onto every row.
        private static AccountSnapshot RealisticAccountSnapshot(int socketedStacks)
        {
            var snapshot = new AccountSnapshot
            {
                CapturedAt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc),
                CoinCopper = 12345678,
                Items = new List<SnapshotItemEntry>(RealisticStackCount),
            };

            for (int i = 0; i < RealisticStackCount; i++)
            {
                var entry = new SnapshotItemEntry
                {
                    ItemId = 10000 + i,
                    Name = "Mithril Ore",
                    IconUrl = "https://render.guildwars2.com/file/"
                        + "0FA3A0C0E1B1A2C3D4E5F60718293A4B5C6D7E8F/" + (60000 + i) + ".png",
                    Rarity = "Basic",
                    Count = 250,
                    Source = i % 2 == 0 ? "Bank" : "Character:Anna",
                };

                if (i < socketedStacks)
                {
                    entry.Upgrades = new List<int> { 24615, 24554 };
                    entry.Infusions = new List<int> { 37131 };
                }

                snapshot.Items.Add(entry);
            }

            return snapshot;
        }
    }
}
