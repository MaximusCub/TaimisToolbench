using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Modules.Managers;
using Gw2Sharp.WebApi.V2.Models;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal class Gw2AccountSnapshotService
    {
        private static readonly Logger Logger = Logger.GetLogger<Gw2AccountSnapshotService>();

        private static readonly TokenPermission[] RequiredPermissions =
        {
            TokenPermission.Account,
            TokenPermission.Characters,
            TokenPermission.Inventories,
            TokenPermission.Wallet,
        };

        private const int ItemBulkLimit = 200;

        private readonly Gw2ApiManager _apiManager;
        private readonly Dictionary<int, (string Name, string IconUrl, string Rarity)> _itemCache =
            new Dictionary<int, (string, string, string)>();

        private readonly Dictionary<int, (string Name, string IconUrl)> _currencyCache = new Dictionary<int, (string, string)>();

        // Skin id -> what the game shows for an item wearing it.
        // /v2/skins needs no API key and an account draws on few skins, so
        // this outlives a single fetch the way _itemCache does.
        private readonly Dictionary<int, (string Name, string IconUrl)> _skinCache =
            new Dictionary<int, (string, string)>();

        private readonly object _cacheLock = new object();

        public Gw2AccountSnapshotService(Gw2ApiManager apiManager)
        {
            _apiManager = apiManager;
        }

        public bool HasRequiredPermissions()
        {
            return _apiManager.HasPermissions(RequiredPermissions);
        }

        // The 6 independent top-level account-data sources tallied for
        // success/failure. Per-character inventory and equipment failures
        // are not counted individually - they are tolerated as a partial
        // Characters-source degradation.
        private const int SourceCount = 6;

        // /v2/account/legendaryarmory needs the "unlocks" scope, which
        // manifest.json declares optional. A token without it cannot serve
        // that endpoint, so the fetch is not attempted and the account has
        // one source fewer rather than one failed source.
        private static readonly TokenPermission[] LegendaryArmoryPermissions =
        {
            TokenPermission.Unlocks,
        };

        public async Task<AccountSnapshot> FetchSnapshotAsync(CancellationToken ct)
        {
            var snapshot = new AccountSnapshot { CapturedAt = DateTime.UtcNow };
            int failedSources = 0;
            bool canReadLegendaryArmory = _apiManager.HasPermissions(LegendaryArmoryPermissions);
            int totalSources = canReadLegendaryArmory ? SourceCount : SourceCount - 1;

            // Per-source failure type names, captured here (where Gw2Sharp
            // exception types are in scope) as plain strings so the
            // Blish-free classifier never needs a Gw2Sharp reference.
            var failedSourceExceptionTypeNames = new List<string>();

            // Wallet (also extracts coins as currency ID 1)
            try
            {
                var wallet = await _apiManager.Gw2ApiClient.V2.Account.Wallet.GetAsync(ct);
                foreach (var entry in wallet)
                {
                    if (entry.Id == 1)
                    {
                        snapshot.CoinCopper = entry.Value;
                    }
                    else
                    {
                        snapshot.Wallet.Add(new SnapshotWalletEntry
                        {
                            CurrencyId = entry.Id,
                            CurrencyName = "",
                            Value = entry.Value,
                        });
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch wallet");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch wallet: {ex.GetType().Name} - {ex.Message}");
                failedSources++;
                failedSourceExceptionTypeNames.Add(ex.GetType().Name);
            }

            ct.ThrowIfCancellationRequested();

            // Bank
            try
            {
                var bank = await _apiManager.Gw2ApiClient.V2.Account.Bank.GetAsync(ct);
                foreach (var item in bank)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    snapshot.Items.Add(new SnapshotItemEntry
                    {
                        ItemId = item.Id,
                        Count = item.Count,
                        Source = "Bank",
                        Upgrades = SocketedIds(item.Upgrades),
                        Infusions = SocketedIds(item.Infusions),
                        SkinId = SkinIdOf(item.Skin),
                    });
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch bank");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch bank: {ex.GetType().Name} - {ex.Message}");
                failedSources++;
                failedSourceExceptionTypeNames.Add(ex.GetType().Name);
            }

            ct.ThrowIfCancellationRequested();

            // Shared inventory
            try
            {
                var shared = await _apiManager.Gw2ApiClient.V2.Account.Inventory.GetAsync(ct);
                foreach (var item in shared)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    snapshot.Items.Add(new SnapshotItemEntry
                    {
                        ItemId = item.Id,
                        Count = item.Count,
                        Source = "SharedInventory",
                        Upgrades = SocketedIds(item.Upgrades),
                        Infusions = SocketedIds(item.Infusions),
                        SkinId = SkinIdOf(item.Skin),
                    });
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch shared inventory");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch shared inventory: {ex.GetType().Name} - {ex.Message}");
                failedSources++;
                failedSourceExceptionTypeNames.Add(ex.GetType().Name);
            }

            ct.ThrowIfCancellationRequested();

            // Material storage
            try
            {
                var materials = await _apiManager.Gw2ApiClient.V2.Account.Materials.GetAsync(ct);
                foreach (var mat in materials)
                {
                    if (mat.Count <= 0)
                    {
                        continue;
                    }

                    snapshot.Items.Add(new SnapshotItemEntry
                    {
                        ItemId = mat.Id,
                        Count = mat.Count,
                        Source = "MaterialStorage",
                    });
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch material storage");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch material storage: {ex.GetType().Name} - {ex.Message}");
                failedSources++;
                failedSourceExceptionTypeNames.Add(ex.GetType().Name);
            }

            ct.ThrowIfCancellationRequested();

            // Legendary Armory
            //
            // Read from its own endpoint rather than inferred from
            // equipment slots. A slot drawing a legendary out of the armory
            // reports it once per slot per character, so the equipment
            // fetch drops those entries (IsHeldByCharacter); this endpoint
            // reports each item once for the whole account, with a count of
            // how many an equipment template can draw at a time.
            if (canReadLegendaryArmory)
            {
                try
                {
                    var armory = await _apiManager.Gw2ApiClient.V2.Account.LegendaryArmory.GetAsync(ct);
                    foreach (var entry in armory)
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
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    Logger.Warn(ex, "Failed to fetch legendary armory");
                    ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch legendary armory: {ex.GetType().Name} - {ex.Message}");
                    failedSources++;
                    failedSourceExceptionTypeNames.Add(ex.GetType().Name);
                }

                ct.ThrowIfCancellationRequested();
            }

            // Character inventories + crafting disciplines
            try
            {
                var characterNames = await _apiManager.Gw2ApiClient.V2.Characters.IdsAsync(ct);

                // Non-null as soon as the character list is obtained (null
                // vs empty is meaningful - see
                // AccountSnapshot.CharacterDisciplines). Reset to null
                // below if any single character's crafting fetch fails: a
                // partial list would read as an affirmative "not trained
                // on any character" claim for characters never reached
                // (never invent data).
                snapshot.CharacterDisciplines = new List<SnapshotCharacterDiscipline>();
                bool characterDisciplineDataDegraded = false;

                foreach (var name in characterNames)
                {
                    ct.ThrowIfCancellationRequested();

                    // Inventory, equipment and crafting are fired
                    // concurrently so the wall-clock cost stays roughly one
                    // round trip per character within the hard snapshot
                    // timeout. Each task catches its own failures
                    // internally, so Task.WhenAll only faults on genuine
                    // cancellation.
                    var inventoryTask = FetchCharacterInventoryItemsAsync(name, ct);
                    var equipmentTask = FetchCharacterEquipmentItemsAsync(name, ct);
                    var craftingTask = FetchCharacterCraftingAsync(name, ct);
                    await Task.WhenAll(inventoryTask, equipmentTask, craftingTask);

                    // Inventory and equipment: a failure is tolerated -
                    // this character's items are simply missing, a
                    // conservative under-count (inflates buy cost, never
                    // fabricates a claim). Does not set
                    // characterDisciplineDataDegraded; the two are
                    // independent signals.
                    snapshot.Items.AddRange(inventoryTask.Result);

                    var equipment = equipmentTask.Result;
                    snapshot.Items.AddRange(equipment.Items);
                    foreach (int armoryItemId in equipment.ArmoryItemIds)
                    {
                        snapshot.LegendaryArmoryEquipped.Add(new SnapshotArmoryEquip
                        {
                            ItemId = armoryItemId,
                            CharacterName = name,
                        });
                    }

                    // Crafting disciplines: unlike Inventory, ANY failure
                    // flips characterDisciplineDataDegraded so the whole
                    // snapshot's CharacterDisciplines is discarded - a
                    // partial list is unacceptable here (see the flag's
                    // comment) even though it is fine for Inventory.
                    var craftingOutcome = craftingTask.Result;
                    if (craftingOutcome.Degraded)
                    {
                        characterDisciplineDataDegraded = true;
                    }
                    else
                    {
                        snapshot.CharacterDisciplines.AddRange(craftingOutcome.Disciplines);
                    }
                }

                if (characterDisciplineDataDegraded)
                {
                    snapshot.CharacterDisciplines = null;
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch character list");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch character list: {ex.GetType().Name} - {ex.Message}");
                failedSources++;
                failedSourceExceptionTypeNames.Add(ex.GetType().Name);
                // If anything escapes the per-character loop,
                // snapshot.CharacterDisciplines can already be a partially
                // populated list, which would read as an affirmative "not
                // trained" claim for characters never reached. Null it
                // here too - same "degraded fetch -> show nothing"
                // contract.
                snapshot.CharacterDisciplines = null;
            }

            // A partial failure must never masquerade as a full snapshot:
            // throw instead of returning a snapshot with holes relative to
            // a prior good fetch (see SnapshotFetchFailedException).
            if (failedSources > 0)
            {
                throw new SnapshotFetchFailedException(failedSources, totalSources, failedSourceExceptionTypeNames);
            }

            // Resolve display names and icon URLs
            await ResolveItemDetailsAsync(snapshot.Items, ct);
            await ResolveSkinDetailsAsync(snapshot.Items, ct);
            await ResolveCurrencyDetailsAsync(snapshot.Wallet, ct);

            return snapshot;
        }

        // Uses the narrow per-character inventory endpoint, not the full
        // character record - the full record pulls in payloads never used
        // here and widens this feature's failure blast radius. Never
        // throws, so the caller can Task.WhenAll it with
        // FetchCharacterCraftingAsync without either short-circuiting the
        // other.
        private async Task<List<SnapshotItemEntry>> FetchCharacterInventoryItemsAsync(string characterName, CancellationToken ct)
        {
            var items = new List<SnapshotItemEntry>();
            try
            {
                var inventory = await _apiManager.Gw2ApiClient.V2.Characters[characterName].Inventory.GetAsync(ct);
                if (inventory?.Bags != null)
                {
                    foreach (var bag in inventory.Bags)
                    {
                        if (bag?.Inventory == null)
                        {
                            continue;
                        }

                        foreach (var item in bag.Inventory)
                        {
                            if (item == null)
                            {
                                continue;
                            }

                            items.Add(new SnapshotItemEntry
                            {
                                ItemId = item.Id,
                                Count = item.Count,
                                Source = AccountItemIndex.CharacterSourcePrefix + characterName,
                                Upgrades = SocketedIds(item.Upgrades),
                                Infusions = SocketedIds(item.Infusions),
                                SkinId = SkinIdOf(item.Skin),
                            });
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch inventory for character {CharacterName}", characterName);
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch inventory for character {characterName}: {ex.GetType().Name} - {ex.Message}");
            }

            return items;
        }

        /// <summary>
        /// What this character is wearing, plus what its saved equipment
        /// tabs hold, as one entry per physical item under the
        /// "Equipped:&lt;name&gt;" source, which is not the source its bags
        /// use. The second half of the result is the item ids this
        /// character is wearing out of the account-wide Legendary Armory,
        /// which are named but never counted (Models.SnapshotArmoryEquip).
        /// <para>
        /// /v2/characters/:id/equipment returns each physical item once and
        /// names every tab it sits in, so an item shared by three loadouts
        /// is one entry, not three (/v2/characters/:id/equipmenttabs is the
        /// per-tab view that would repeat it). Which store a slot draws
        /// from is Services.EquipmentLocationPolicy's decision. Never
        /// throws, like the inventory fetch.
        /// </para>
        /// </summary>
        private async Task<(List<SnapshotItemEntry> Items, List<int> ArmoryItemIds)>
            FetchCharacterEquipmentItemsAsync(string characterName, CancellationToken ct)
        {
            var items = new List<SnapshotItemEntry>();
            var armoryItemIds = new List<int>();
            try
            {
                var equipment = await _apiManager.Gw2ApiClient.V2.Characters[characterName].Equipment.GetAsync(ct);
                if (equipment?.Equipment != null)
                {
                    foreach (var item in equipment.Equipment)
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
                                armoryItemIds.Add(item.Id);
                            }

                            continue;
                        }

                        items.Add(new SnapshotItemEntry
                        {
                            ItemId = item.Id,
                            Count = 1,

                            // Worn gear gets its own source encoding so the
                            // snapshot can tell it apart from this same
                            // character's bag contents.
                            Source = AccountItemIndex.CharacterEquipmentSourcePrefix + characterName,
                            Upgrades = SocketedIds(item.Upgrades),
                            Infusions = SocketedIds(item.Infusions),
                            SkinId = SkinIdOf(item.Skin),
                        });
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch equipment for character {CharacterName}", characterName);
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch equipment for character {characterName}: {ex.GetType().Name} - {ex.Message}");
            }

            return (items, armoryItemIds);
        }

        /// <summary>
        /// The wire string in an equipment slot's "location" field, which is
        /// what Services.EquipmentLocationPolicy reads. Taken raw the same
        /// way RarityOf reads rarity, so a value Gw2Sharp's enum does not
        /// recognise reaches the policy as itself rather than as the enum's
        /// fallback member. "" when the slot carries no location at all.
        /// </summary>
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

        // One bounded retry: the all-or-nothing rule means a single
        // transient 429/500 on one character would otherwise wipe
        // CharacterDisciplines for the whole account. Never throws (except
        // genuine cancellation) - the Degraded flag reports failure.
        private async Task<(bool Degraded, List<SnapshotCharacterDiscipline> Disciplines)> FetchCharacterCraftingAsync(string characterName, CancellationToken ct)
        {
            var disciplines = new List<SnapshotCharacterDiscipline>();
            const int maxAttempts = 2;
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var crafting = await _apiManager.Gw2ApiClient.V2.Characters[characterName].Crafting.GetAsync(ct);
                    if (crafting?.Crafting == null)
                    {
                        if (attempt < maxAttempts)
                        {
                            continue;
                        }

                        return (true, disciplines);
                    }

                    foreach (var cd in crafting.Crafting)
                    {
                        if (cd == null)
                        {
                            continue;
                        }

                        disciplines.Add(new SnapshotCharacterDiscipline
                        {
                            CharacterName = characterName,
                            // RawValue preserves the literal API string
                            // even for a discipline Gw2Sharp's enum does
                            // not recognize, matching the plain-string
                            // shape RequiredDiscipline.Discipline uses.
                            Discipline = cd.Discipline?.RawValue ?? "",
                            Rating = cd.Rating,
                            Active = cd.Active,
                        });
                    }

                    return (false, disciplines);
                }
                catch (Exception ex) when (!(ex is OperationCanceledException))
                {
                    if (attempt < maxAttempts)
                    {
                        continue;
                    }

                    Logger.Warn(ex, "Failed to fetch crafting disciplines for character {CharacterName}", characterName);
                    ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch crafting disciplines for character {characterName}: {ex.GetType().Name} - {ex.Message}");
                    return (true, disciplines);
                }
            }

            return (true, disciplines);
        }

        /// <summary>
        /// One stack's socketed item ids in the shape
        /// <see cref="SnapshotItemEntry.Upgrades"/> documents. Gw2Sharp
        /// surfaces the API's omitted field as null; an empty list is
        /// folded to null as well, so only one of the two ever reaches
        /// disk.
        /// </summary>
        private static List<int> SocketedIds(IEnumerable<int> ids)
        {
            if (ids == null)
            {
                return null;
            }

            var copied = new List<int>(ids);
            return copied.Count > 0 ? copied : null;
        }

        private async Task ResolveItemDetailsAsync(List<SnapshotItemEntry> items, CancellationToken ct)
        {
            try
            {
                List<int> uncachedIds;
                lock (_cacheLock)
                {
                    uncachedIds = items
                        .Select(i => i.ItemId)
                        .Distinct()
                        .Where(id => !_itemCache.ContainsKey(id))
                        .ToList();
                }

                // The catch is per chunk, not around the loop. A full
                // account resolves thousands of ids in ItemBulkLimit-sized
                // requests, and one request the API refuses used to skip
                // every later chunk AND the apply pass below, so a single
                // bad id cost the names of every item after it.
                int failedChunks = 0;
                Exception firstChunkFailure = null;

                for (int i = 0; i < uncachedIds.Count; i += ItemBulkLimit)
                {
                    ct.ThrowIfCancellationRequested();
                    var chunk = uncachedIds.Skip(i).Take(ItemBulkLimit);
                    try
                    {
                        var fetched = await _apiManager.Gw2ApiClient.V2.Items.ManyAsync(chunk, ct);
                        lock (_cacheLock)
                        {
                            foreach (var item in fetched)
                            {
                                var url = item.Icon.Url;
                                _itemCache[item.Id] =
                                    (item.Name ?? "", url != null ? url.AbsoluteUri : "", RarityOf(item));
                            }
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        failedChunks++;
                        if (firstChunkFailure == null)
                        {
                            firstChunkFailure = ex;
                        }
                    }
                }

                if (firstChunkFailure != null)
                {
                    LogChunkFailures("item names/icons", failedChunks, firstChunkFailure);
                }

                lock (_cacheLock)
                {
                    foreach (var entry in items)
                    {
                        if (_itemCache.TryGetValue(entry.ItemId, out var cached))
                        {
                            entry.Name = cached.Name;
                            entry.IconUrl = cached.IconUrl;
                            entry.Rarity = cached.Rarity;
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to resolve item names/icons");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to resolve item names/icons: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// The rarity string for a fetched item, in the spelling
        /// RarityColors switches on, or "" when the API sent one this
        /// module does not know. Gw2Sharp models rarity as an ApiEnum, which
        /// keeps the wire string in RawValue and falls back to the parsed
        /// enum name; either is run past the rarity policy so an
        /// unrecognised value degrades to unknown rather than to a wrong
        /// colour.
        /// </summary>
        private static string RarityOf(Gw2Sharp.WebApi.V2.Models.Item item)
        {
            var rarity = item?.Rarity;
            if (rarity == null)
            {
                return "";
            }

            string raw = rarity.RawValue;
            if (string.IsNullOrEmpty(raw))
            {
                raw = rarity.Value.ToString();
            }

            return ItemRarityResolution.Normalize(raw) ?? "";
        }

        /// <summary>
        /// One stack's applied skin as a plain id, or 0 for none. Gw2Sharp
        /// surfaces the API's omitted field as null. Material storage and
        /// the Legendary Armory carry no skin field at all, so their rows
        /// never reach here.
        /// </summary>
        private static int SkinIdOf(int? skin)
        {
            return skin.HasValue && skin.Value > 0 ? skin.Value : 0;
        }

        /// <summary>
        /// Fills in <see cref="SnapshotItemEntry.SkinName"/> and
        /// <see cref="SnapshotItemEntry.SkinIconUrl"/> for every stack
        /// wearing a skin, batched and cached exactly the way
        /// <see cref="ResolveItemDetailsAsync"/> resolves item names and
        /// icons. A failure leaves both empty, which reads as "not
        /// transmuted": an under-report, never a wrong name.
        /// </summary>
        private async Task ResolveSkinDetailsAsync(List<SnapshotItemEntry> items, CancellationToken ct)
        {
            try
            {
                List<int> uncachedIds;
                lock (_cacheLock)
                {
                    uncachedIds = items
                        .Select(i => i.SkinId)
                        .Where(id => id > 0)
                        .Distinct()
                        .Where(id => !_skinCache.ContainsKey(id))
                        .ToList();
                }

                // Per chunk, for the reason ResolveItemDetailsAsync gives.
                int failedChunks = 0;
                Exception firstChunkFailure = null;

                for (int i = 0; i < uncachedIds.Count; i += ItemBulkLimit)
                {
                    ct.ThrowIfCancellationRequested();
                    var chunk = uncachedIds.Skip(i).Take(ItemBulkLimit);
                    try
                    {
                        var fetched = await _apiManager.Gw2ApiClient.V2.Skins.ManyAsync(chunk, ct);
                        lock (_cacheLock)
                        {
                            foreach (var skin in fetched)
                            {
                                var url = skin.Icon.Url;
                                _skinCache[skin.Id] =
                                    (skin.Name ?? "", url != null ? url.AbsoluteUri : "");
                            }
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        failedChunks++;
                        if (firstChunkFailure == null)
                        {
                            firstChunkFailure = ex;
                        }
                    }
                }

                if (firstChunkFailure != null)
                {
                    LogChunkFailures("skin names", failedChunks, firstChunkFailure);
                }

                lock (_cacheLock)
                {
                    foreach (var entry in items)
                    {
                        if (entry.SkinId > 0 && _skinCache.TryGetValue(entry.SkinId, out var cached))
                        {
                            entry.SkinName = cached.Name;
                            entry.SkinIconUrl = cached.IconUrl;
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to resolve skin names");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to resolve skin names: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// One warning for a whole resolve pass, naming how many bulk
        /// requests failed and carrying the first exception. Per-chunk
        /// logging would put one line per request in the module log, and a
        /// full account issues tens of them.
        /// </summary>
        private static void LogChunkFailures(string what, int failedChunks, Exception first)
        {
            Logger.Warn(first, "Failed to resolve " + what + " for " + failedChunks + " request(s)");
            ModuleLog.Shared.Write(
                ModuleLogLevel.Warn,
                "snapshot-fetch",
                $"Failed to resolve {what} for {failedChunks} request(s): {first.GetType().Name} - {first.Message}");
        }

        private async Task ResolveCurrencyDetailsAsync(List<SnapshotWalletEntry> wallet, CancellationToken ct)
        {
            try
            {
                bool needsFetch;
                lock (_cacheLock)
                {
                    needsFetch = _currencyCache.Count == 0;
                }

                if (needsFetch)
                {
                    ct.ThrowIfCancellationRequested();
                    var currencies = await _apiManager.Gw2ApiClient.V2.Currencies.AllAsync(ct);
                    lock (_cacheLock)
                    {
                        foreach (var c in currencies)
                        {
                            var url = c.Icon.Url;
                            _currencyCache[c.Id] = (c.Name ?? "", url != null ? url.AbsoluteUri : "");
                        }
                    }
                }

                lock (_cacheLock)
                {
                    foreach (var entry in wallet)
                    {
                        if (_currencyCache.TryGetValue(entry.CurrencyId, out var cached))
                        {
                            entry.CurrencyName = cached.Name;
                            entry.IconUrl = cached.IconUrl;
                        }
                    }
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to resolve currency names/icons");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to resolve currency names/icons: {ex.GetType().Name} - {ex.Message}");
            }
        }
    }
}
