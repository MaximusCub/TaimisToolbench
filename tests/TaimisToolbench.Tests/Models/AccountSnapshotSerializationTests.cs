using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using TaimisToolbench.Models;
using Xunit;

namespace TaimisToolbench.Tests.Models
{
    public class AccountSnapshotSerializationTests
    {
        [Fact]
        public void RoundTrip_PreservesAllFields()
        {
            var original = new AccountSnapshot
            {
                CapturedAt = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc),
                CoinCopper = 1234567,
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry { ItemId = 100, Name = "Iron Ore", Count = 50, Source = "Bank", Rarity = "Basic" },
                    new SnapshotItemEntry { ItemId = 200, Name = "Gold Ore", Count = 10, Source = "Character:Ranger", Rarity = "Fine" },
                },
                Wallet = new List<SnapshotWalletEntry>
                {
                    new SnapshotWalletEntry { CurrencyId = 2, CurrencyName = "Karma", Value = 9999 },
                },
                // This test's name is a
                // promise ("preserves ALL fields") that CharacterDisciplines
                // broke when it was added without updating this fixture -
                // two entries here exercise every field on
                // SnapshotCharacterDiscipline (CharacterName/Discipline/
                // Rating/Active), including one Active=true and one
                // Active=false so the round-trip can't pass by coincidence
                // of a default value. This is JsonConvert directly (this
                // file's own established convention), not
                // SnapshotHelpers.SerializeSnapshot/DeserializeSnapshot -
                // SnapshotSerializationTests.cs already covers that path's
                // CharacterDisciplines round-trip and the legacy
                // missing-field/null-defaulting behavior; this test only
                // needs to stop lying about which fields it preserves.
                CharacterDisciplines = new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Anna", Discipline = "Weaponsmith", Rating = 500, Active = true },
                    new SnapshotCharacterDiscipline { CharacterName = "Bob", Discipline = "Chef", Rating = 400, Active = false },
                },
                LegendaryArmoryEquipped = new List<SnapshotArmoryEquip>
                {
                    new SnapshotArmoryEquip { ItemId = 300, CharacterName = "Anna" },
                    new SnapshotArmoryEquip { ItemId = 300, CharacterName = "Bob" },
                },
            };

            string json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<AccountSnapshot>(json);

            Assert.Equal(original.CapturedAt, deserialized.CapturedAt);
            Assert.Equal(original.CoinCopper, deserialized.CoinCopper);
            Assert.Equal(original.Items.Count, deserialized.Items.Count);
            Assert.Equal(original.Items[0].ItemId, deserialized.Items[0].ItemId);
            Assert.Equal(original.Items[0].Name, deserialized.Items[0].Name);
            Assert.Equal(original.Items[0].Count, deserialized.Items[0].Count);
            Assert.Equal(original.Items[0].Source, deserialized.Items[0].Source);
            Assert.Equal(original.Items[0].Rarity, deserialized.Items[0].Rarity);
            Assert.Equal(original.Items[1].ItemId, deserialized.Items[1].ItemId);
            Assert.Equal(original.Items[1].Rarity, deserialized.Items[1].Rarity);
            Assert.Equal(original.Wallet.Count, deserialized.Wallet.Count);
            Assert.Equal(original.Wallet[0].CurrencyId, deserialized.Wallet[0].CurrencyId);
            Assert.Equal(original.Wallet[0].CurrencyName, deserialized.Wallet[0].CurrencyName);
            Assert.Equal(original.Wallet[0].Value, deserialized.Wallet[0].Value);
            Assert.Equal(original.CharacterDisciplines.Count, deserialized.CharacterDisciplines.Count);
            Assert.Equal(original.CharacterDisciplines[0].CharacterName, deserialized.CharacterDisciplines[0].CharacterName);
            Assert.Equal(original.CharacterDisciplines[0].Discipline, deserialized.CharacterDisciplines[0].Discipline);
            Assert.Equal(original.CharacterDisciplines[0].Rating, deserialized.CharacterDisciplines[0].Rating);
            Assert.Equal(original.CharacterDisciplines[0].Active, deserialized.CharacterDisciplines[0].Active);
            Assert.Equal(original.CharacterDisciplines[1].CharacterName, deserialized.CharacterDisciplines[1].CharacterName);
            Assert.Equal(original.CharacterDisciplines[1].Discipline, deserialized.CharacterDisciplines[1].Discipline);
            Assert.Equal(original.CharacterDisciplines[1].Rating, deserialized.CharacterDisciplines[1].Rating);
            Assert.Equal(original.CharacterDisciplines[1].Active, deserialized.CharacterDisciplines[1].Active);
            Assert.Equal(original.LegendaryArmoryEquipped.Count, deserialized.LegendaryArmoryEquipped.Count);
            Assert.Equal(original.LegendaryArmoryEquipped[0].ItemId, deserialized.LegendaryArmoryEquipped[0].ItemId);
            Assert.Equal(original.LegendaryArmoryEquipped[0].CharacterName, deserialized.LegendaryArmoryEquipped[0].CharacterName);
            Assert.Equal(original.LegendaryArmoryEquipped[1].ItemId, deserialized.LegendaryArmoryEquipped[1].ItemId);
            Assert.Equal(original.LegendaryArmoryEquipped[1].CharacterName, deserialized.LegendaryArmoryEquipped[1].CharacterName);
        }

        [Fact]
        public void ASnapshotWrittenBeforeWearersWereCaptured_LoadsWithNobodyNamed()
        {
            // The field is additive. An older snapshot.json carries no
            // "LegendaryArmoryEquipped" at all, and must read as "nobody
            // named" rather than leaving a null the row builder walks.
            var deserialized = JsonConvert.DeserializeObject<AccountSnapshot>(
                "{\"CapturedAt\":\"2025-06-15T12:00:00Z\",\"CoinCopper\":5}");

            Assert.NotNull(deserialized.LegendaryArmoryEquipped);
            Assert.Empty(deserialized.LegendaryArmoryEquipped);
        }

        [Fact]
        public void RoundTrip_EmptyItemsAndWallet_PreservesEmptyLists()
        {
            var original = new AccountSnapshot
            {
                CapturedAt = DateTime.UtcNow,
                CoinCopper = 0,
                Items = new List<SnapshotItemEntry>(),
                Wallet = new List<SnapshotWalletEntry>(),
            };

            string json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<AccountSnapshot>(json);

            Assert.Empty(deserialized.Items);
            Assert.Empty(deserialized.Wallet);
            Assert.Equal(0, deserialized.CoinCopper);
        }

        [Fact]
        public void CoinCopper_Preserved()
        {
            var original = new AccountSnapshot { CoinCopper = int.MaxValue };

            string json = JsonConvert.SerializeObject(original);
            var deserialized = JsonConvert.DeserializeObject<AccountSnapshot>(json);

            Assert.Equal(int.MaxValue, deserialized.CoinCopper);
        }
    }
}
