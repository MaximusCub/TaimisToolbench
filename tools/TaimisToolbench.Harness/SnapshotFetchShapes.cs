using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Gw2Sharp;
using Gw2Sharp.WebApi.V2;
using Gw2Sharp.WebApi.V2.Models;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Harness
{
    /// <summary>
    /// Builds a real <see cref="AccountSnapshot"/> the way
    /// Services/Gw2AccountSnapshotService.cs builds one, for whichever fetch
    /// approach a <see cref="FetchConfig"/> names.
    /// </summary>
    /// <remarks>
    /// The service itself takes a Blish HUD Gw2ApiManager and cannot be
    /// constructed outside a loaded module, so its orchestration is repeated
    /// here over a bare Gw2Sharp client. Everything below the orchestration
    /// is the module's own code: CharacterSnapshotCollector's fan-out and
    /// fold, EquipmentLocationPolicy's slot rules, AccountItemIndex's source
    /// strings, BoundedConcurrency's bound and the Models types. What is
    /// measured is therefore the module's work, not the endpoint's.
    /// </remarks>
    internal static class SnapshotFetchShapes
    {
        private const int ItemBulkLimit = 200;

        private const int MaxDetailRequestsInFlight = 4;

        public static async Task<AccountSnapshot> BuildAsync(
            Gw2Client client,
            FetchConfig config,
            bool includeArmory,
            int characterCount,
            CancellationToken ct)
        {
            var v2 = client.WebApi.V2;
            var snapshot = new AccountSnapshot();

            var bankTask = v2.Account.Bank.GetAsync(ct);
            var sharedTask = v2.Account.Inventory.GetAsync(ct);
            var materialsTask = v2.Account.Materials.GetAsync(ct);
            var walletTask = v2.Account.Wallet.GetAsync(ct);
            var armoryTask = includeArmory ? v2.Account.LegendaryArmory.GetAsync(ct) : null;
            var namesTask = config.Approach == FetchApproach.Narrow
                ? v2.Characters.IdsAsync(ct)
                : null;

            if (config.SettleAccountWideFirst)
            {
                await SettleAsync(bankTask, sharedTask, materialsTask, walletTask, armoryTask, namesTask);
            }

            var characterWork = CollectCharactersAsync(v2, config, characterCount, namesTask, ct);

            foreach (var entry in await walletTask)
            {
                if (entry.Id == 1)
                {
                    snapshot.CoinCopper = entry.Value;
                    continue;
                }

                snapshot.Wallet.Add(new SnapshotWalletEntry
                {
                    CurrencyId = entry.Id,
                    CurrencyName = "",
                    Value = entry.Value,
                });
            }

            AddStacks(snapshot.Items, await bankTask, "Bank");
            AddStacks(snapshot.Items, await sharedTask, "SharedInventory");

            foreach (var material in await materialsTask)
            {
                if (material.Count <= 0)
                {
                    continue;
                }

                snapshot.Items.Add(new SnapshotItemEntry
                {
                    ItemId = material.Id,
                    Count = material.Count,
                    Source = "MaterialStorage",
                });
            }

            if (armoryTask != null)
            {
                foreach (var entry in await armoryTask)
                {
                    if (entry == null || entry.Count <= 0)
                    {
                        continue;
                    }

                    snapshot.Items.Add(new SnapshotItemEntry
                    {
                        ItemId = entry.Id,
                        Count = entry.Count,
                        Source = AccountItemIndex.SourceLegendaryArmory,
                    });
                }
            }

            var harvest = await characterWork;
            snapshot.Items.AddRange(harvest.Items);
            snapshot.LegendaryArmoryEquipped.AddRange(harvest.ArmoryEquipped);
            snapshot.CharacterDisciplines = harvest.Disciplines;
            snapshot.CharacterCount = harvest.CharacterCount;
            snapshot.IncompleteCharacterCount = harvest.IncompleteCharacterCount;

            await Task.WhenAll(
                ResolveItemsAsync(v2, snapshot.Items, ct),
                ResolveSkinsAsync(v2, snapshot.Items, ct),
                ResolveCurrenciesAsync(v2, snapshot.Wallet, ct));

            return snapshot;
        }

        private static async Task<CharacterSnapshotHarvest> CollectCharactersAsync(
            IGw2WebApiV2Client v2,
            FetchConfig config,
            int characterCount,
            Task<IApiV2ObjectList<string>> namesTask,
            CancellationToken ct)
        {
            if (config.Approach == FetchApproach.Narrow)
            {
                var roster = PadRoster(await namesTask, config, characterCount);
                int parallelism = config.CharacterParallelism > 0
                    ? config.CharacterParallelism
                    : Math.Max(1, roster.Count);
                return await CharacterSnapshotCollector.CollectAsync(
                    roster, parallelism, name => FetchCharacterAsync(v2, name, ct), ct);
            }

            var records = await FetchFullRecordsAsync(v2, config, characterCount, ct);

            // The same fold, over parts already in hand, so a full-record run
            // and a narrow run agree on what a complete harvest looks like.
            var names = records.Select(r => r.Name ?? string.Empty).ToList();
            var byName = new Dictionary<string, Character>();
            foreach (var record in records)
            {
                byName[record.Name ?? string.Empty] = record;
            }

            return await CharacterSnapshotCollector.CollectAsync(
                names,
                Math.Max(1, names.Count),
                name => Task.FromResult(PartFromRecord(byName[name])),
                ct);
        }

        private static async Task<List<Character>> FetchFullRecordsAsync(
            IGw2WebApiV2Client v2, FetchConfig config, int characterCount, CancellationToken ct)
        {
            if (config.Approach == FetchApproach.FullAll)
            {
                return (await v2.Characters.AllAsync(ct)).ToList();
            }

            // The page count comes from the roster size the probe learned. In
            // the module it would come off page 0's X-Page-Total header, so
            // it costs no extra request either way.
            int pages = Math.Max(1, (characterCount + config.PageSize - 1) / config.PageSize);
            var tasks = new List<Task<IApiV2ObjectList<Character>>>();
            for (int page = 0; page < pages; page++)
            {
                tasks.Add(v2.Characters.PageAsync(page, config.PageSize, ct));
            }

            await Task.WhenAll(tasks);
            var records = new List<Character>();
            foreach (var task in tasks)
            {
                records.AddRange(task.Result);
            }

            return records;
        }

        private static List<string> PadRoster(
            IApiV2ObjectList<string> names, FetchConfig config, int characterCount)
        {
            var roster = new List<string>(names);
            int target = config.RosterSize(characterCount);
            for (int i = 0; roster.Count < target && names.Count > 0; i++)
            {
                roster.Add(names[i % names.Count]);
            }

            return roster;
        }

        private static async Task<CharacterSnapshotPart> FetchCharacterAsync(
            IGw2WebApiV2Client v2, string characterName, CancellationToken ct)
        {
            var part = new CharacterSnapshotPart();
            var inventoryTask = v2.Characters[characterName].Inventory.GetAsync(ct);
            var equipmentTask = v2.Characters[characterName].Equipment.GetAsync(ct);
            var craftingTask = v2.Characters[characterName].Crafting.GetAsync(ct);
            await SettleAsync(inventoryTask, equipmentTask, craftingTask);

            try
            {
                var inventory = await inventoryTask;
                AddBags(part.Items, inventory?.Bags, characterName);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                part.ItemsDegraded = true;
            }

            try
            {
                var equipment = await equipmentTask;
                AddEquipment(part, equipment?.Equipment, characterName);
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                part.ItemsDegraded = true;
            }

            try
            {
                var crafting = await craftingTask;
                if (crafting?.Crafting == null)
                {
                    part.DisciplinesDegraded = true;
                }
                else
                {
                    AddDisciplines(part, crafting.Crafting, characterName);
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                part.DisciplinesDegraded = true;
            }

            return part;
        }

        private static CharacterSnapshotPart PartFromRecord(Character record)
        {
            var part = new CharacterSnapshotPart();
            string name = record.Name ?? string.Empty;
            AddBags(part.Items, record.Bags, name);
            AddEquipment(part, record.Equipment, name);
            if (record.Crafting == null)
            {
                part.DisciplinesDegraded = true;
            }
            else
            {
                AddDisciplines(part, record.Crafting, name);
            }

            return part;
        }

        private static void AddStacks(
            List<SnapshotItemEntry> rows, IEnumerable<AccountItem> stacks, string source)
        {
            foreach (var item in stacks)
            {
                if (item == null)
                {
                    continue;
                }

                rows.Add(new SnapshotItemEntry
                {
                    ItemId = item.Id,
                    Count = item.Count,
                    Source = source,
                    Upgrades = SocketedIds(item.Upgrades),
                    Infusions = SocketedIds(item.Infusions),
                    SkinId = SkinIdOf(item.Skin),
                });
            }
        }

        private static void AddBags(
            List<SnapshotItemEntry> rows,
            IEnumerable<CharacterInventoryBag> bags,
            string characterName)
        {
            if (bags == null)
            {
                return;
            }

            string source = AccountItemIndex.CharacterSourcePrefix + characterName;
            foreach (var bag in bags)
            {
                if (bag?.Inventory == null)
                {
                    continue;
                }

                AddStacks(rows, bag.Inventory, source);
            }
        }

        private static void AddEquipment(
            CharacterSnapshotPart part,
            IEnumerable<CharacterEquipmentItem> equipment,
            string characterName)
        {
            if (equipment == null)
            {
                return;
            }

            string source = AccountItemIndex.CharacterEquipmentSourcePrefix + characterName;
            foreach (var item in equipment)
            {
                if (item == null)
                {
                    continue;
                }

                string location = RawLocation(item);
                if (!EquipmentLocationPolicy.IsHeldByCharacter(location))
                {
                    if (item.Id > 0
                        && EquipmentLocationPolicy.IsEquippedFromLegendaryArmory(location))
                    {
                        part.ArmoryItemIds.Add(item.Id);
                    }

                    continue;
                }

                part.Items.Add(new SnapshotItemEntry
                {
                    ItemId = item.Id,
                    Count = 1,
                    Source = source,
                    Upgrades = SocketedIds(item.Upgrades),
                    Infusions = SocketedIds(item.Infusions),
                    SkinId = SkinIdOf(item.Skin),
                });
            }
        }

        private static void AddDisciplines(
            CharacterSnapshotPart part,
            IEnumerable<CharacterCraftingDiscipline> crafting,
            string characterName)
        {
            foreach (var discipline in crafting)
            {
                if (discipline == null)
                {
                    continue;
                }

                part.Disciplines.Add(new SnapshotCharacterDiscipline
                {
                    CharacterName = characterName,
                    Discipline = discipline.Discipline?.RawValue ?? "",
                    Rating = discipline.Rating,
                    Active = discipline.Active,
                });
            }
        }

        private static async Task ResolveItemsAsync(
            IGw2WebApiV2Client v2, List<SnapshotItemEntry> items, CancellationToken ct)
        {
            var ids = items.Select(i => i.ItemId).Distinct().ToList();
            var resolved = new Dictionary<int, Item>();
            var sync = new object();
            await InChunksAsync(
                ids,
                async chunk =>
                {
                    var fetched = await v2.Items.ManyAsync(chunk, ct);
                    lock (sync)
                    {
                        foreach (var item in fetched)
                        {
                            resolved[item.Id] = item;
                        }
                    }
                },
                ct);

            foreach (var entry in items)
            {
                Item item;
                if (!resolved.TryGetValue(entry.ItemId, out item))
                {
                    continue;
                }

                var url = item.Icon.Url;
                entry.Name = item.Name ?? "";
                entry.IconUrl = url != null ? url.AbsoluteUri : "";
                entry.Rarity = item.Rarity == null ? "" : item.Rarity.RawValue ?? "";
            }
        }

        private static async Task ResolveSkinsAsync(
            IGw2WebApiV2Client v2, List<SnapshotItemEntry> items, CancellationToken ct)
        {
            var ids = items.Where(i => i.SkinId > 0).Select(i => i.SkinId).Distinct().ToList();
            if (ids.Count == 0)
            {
                return;
            }

            var resolved = new Dictionary<int, Skin>();
            var sync = new object();
            await InChunksAsync(
                ids,
                async chunk =>
                {
                    var fetched = await v2.Skins.ManyAsync(chunk, ct);
                    lock (sync)
                    {
                        foreach (var skin in fetched)
                        {
                            resolved[skin.Id] = skin;
                        }
                    }
                },
                ct);

            foreach (var entry in items)
            {
                Skin skin;
                if (entry.SkinId <= 0 || !resolved.TryGetValue(entry.SkinId, out skin))
                {
                    continue;
                }

                var url = skin.Icon.Url;
                entry.SkinName = skin.Name ?? "";
                entry.SkinIconUrl = url != null ? url.AbsoluteUri : "";
            }
        }

        private static async Task ResolveCurrenciesAsync(
            IGw2WebApiV2Client v2, List<SnapshotWalletEntry> wallet, CancellationToken ct)
        {
            var currencies = await v2.Currencies.AllAsync(ct);
            var byId = currencies.ToDictionary(c => c.Id, c => c);
            foreach (var entry in wallet)
            {
                Currency currency;
                if (!byId.TryGetValue(entry.CurrencyId, out currency))
                {
                    continue;
                }

                var url = currency.Icon.Url;
                entry.CurrencyName = currency.Name ?? "";
                entry.IconUrl = url != null ? url.AbsoluteUri : "";
            }
        }

        private static Task InChunksAsync(
            List<int> ids, Func<List<int>, Task> fetchChunk, CancellationToken ct)
        {
            var chunks = new List<List<int>>();
            for (int i = 0; i < ids.Count; i += ItemBulkLimit)
            {
                chunks.Add(ids.Skip(i).Take(ItemBulkLimit).ToList());
            }

            return BoundedConcurrency.ForEachAsync(
                chunks, MaxDetailRequestsInFlight, fetchChunk, ct);
        }

        private static string RawLocation(CharacterEquipmentItem item)
        {
            var location = item.Location;
            if (location == null)
            {
                return "";
            }

            string raw = location.RawValue;
            return string.IsNullOrEmpty(raw) ? location.Value.ToString() : raw;
        }

        private static List<int> SocketedIds(IEnumerable<int> ids)
        {
            if (ids == null)
            {
                return null;
            }

            var copied = new List<int>(ids);
            return copied.Count > 0 ? copied : null;
        }

        private static int SkinIdOf(int? skin)
        {
            return skin.HasValue && skin.Value > 0 ? skin.Value : 0;
        }

        private static async Task SettleAsync(params Task[] tasks)
        {
            foreach (var task in tasks)
            {
                if (task == null)
                {
                    continue;
                }

                try
                {
                    await task;
                }
                catch (Exception)
                {
                }
            }
        }
    }
}
