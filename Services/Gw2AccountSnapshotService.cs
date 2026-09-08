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

        // How many bulk /v2/items and /v2/skins requests a resolve pass has
        // in flight. Two passes run together, so the ceiling is 8 requests.
        private const int MaxDetailRequestsInFlight = 4;

        // Gw2Sharp's HTTP client carries the .NET 100s default request
        // timeout, which outlasts the whole snapshot budget, so one stuck
        // request could spend the entire allowance on its own. A GW2 API
        // call was measured at roughly 1.5s on this account size.
        private static readonly TimeSpan PerCallTimeout = TimeSpan.FromSeconds(20);

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
        // success/failure. A per-character failure is not one of these; it
        // is named by character instead, and it refuses the fetch the same
        // way a failed source does.
        private const int SourceCount = 6;

        // /v2/account/legendaryarmory needs the "unlocks" scope, which
        // manifest.json declares optional. A token without it cannot serve
        // that endpoint, so the fetch is not attempted and the account has
        // one source fewer rather than one failed source.
        private static readonly TokenPermission[] LegendaryArmoryPermissions =
        {
            TokenPermission.Unlocks,
        };

        /// <summary>
        /// Fetches the whole account snapshot. Calls
        /// <paramref name="onCharacterCountKnown"/> once, as soon as the
        /// character list arrives, so the caller can size its own deadline
        /// against the roster (see <see cref="SnapshotFetchBudget"/>); it
        /// is never called if the character list itself fails.
        /// </summary>
        public async Task<AccountSnapshot> FetchSnapshotAsync(CancellationToken ct, Action<int> onCharacterCountKnown = null)
        {
            Gw2ApiConnectionLimit.Apply();

            var snapshot = new AccountSnapshot();
            int failedSources = 0;
            bool canReadLegendaryArmory = _apiManager.HasPermissions(LegendaryArmoryPermissions);
            int totalSources = canReadLegendaryArmory ? SourceCount : SourceCount - 1;

            // Per-source failure type names, captured here (where Gw2Sharp
            // exception types are in scope) as plain strings so the
            // Blish-free classifier never needs a Gw2Sharp reference.
            var failedSourceExceptionTypeNames = new List<string>();

            // Which reads failed, alongside how many. A caller deciding
            // whether a stale snapshot matters to the plan in front of it
            // needs the names, not the tally.
            var failedSourceNames = new List<AccountDataSource>();

            // Every account-wide request is started before the first await,
            // so all six run together instead of one round trip after
            // another. The results are then applied in a fixed order on
            // this thread, which keeps snapshot.Items single-threaded and
            // its ordering unchanged.
            var walletTask = RetriedCallAsync(c => _apiManager.Gw2ApiClient.V2.Account.Wallet.GetAsync(c), ct);
            var bankTask = RetriedCallAsync(c => _apiManager.Gw2ApiClient.V2.Account.Bank.GetAsync(c), ct);
            var sharedTask = RetriedCallAsync(c => _apiManager.Gw2ApiClient.V2.Account.Inventory.GetAsync(c), ct);
            var materialsTask = RetriedCallAsync(c => _apiManager.Gw2ApiClient.V2.Account.Materials.GetAsync(c), ct);
            var namesTask = RetriedCallAsync(c => _apiManager.Gw2ApiClient.V2.Characters.IdsAsync(c), ct);
            var armoryTask = canReadLegendaryArmory
                ? RetriedCallAsync(c => _apiManager.Gw2ApiClient.V2.Account.LegendaryArmory.GetAsync(c), ct)
                : null;

            await SettleAsync(walletTask, bankTask, sharedTask, materialsTask, namesTask, armoryTask);

            // Wallet (also extracts coins as currency ID 1)
            try
            {
                var wallet = await walletTask;
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
                failedSourceNames.Add(AccountDataSource.Wallet);
            }

            // Bank
            try
            {
                var bank = await bankTask;
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
                        Upgrades = CharacterRecordProjection.SocketedIds(item.Upgrades),
                        Infusions = CharacterRecordProjection.SocketedIds(item.Infusions),
                        SkinId = CharacterRecordProjection.SkinIdOf(item.Skin),
                    });
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch bank");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch bank: {ex.GetType().Name} - {ex.Message}");
                failedSources++;
                failedSourceExceptionTypeNames.Add(ex.GetType().Name);
                failedSourceNames.Add(AccountDataSource.Bank);
            }

            // Shared inventory
            try
            {
                var shared = await sharedTask;
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
                        Upgrades = CharacterRecordProjection.SocketedIds(item.Upgrades),
                        Infusions = CharacterRecordProjection.SocketedIds(item.Infusions),
                        SkinId = CharacterRecordProjection.SkinIdOf(item.Skin),
                    });
                }
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch shared inventory");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch shared inventory: {ex.GetType().Name} - {ex.Message}");
                failedSources++;
                failedSourceExceptionTypeNames.Add(ex.GetType().Name);
                failedSourceNames.Add(AccountDataSource.SharedInventory);
            }

            // Material storage
            try
            {
                var materials = await materialsTask;
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
                failedSourceNames.Add(AccountDataSource.MaterialStorage);
            }

            // Legendary Armory
            //
            // Read from its own endpoint rather than inferred from
            // equipment slots. A slot drawing a legendary out of the armory
            // reports it once per slot per character, so the equipment
            // fetch drops those entries (IsHeldByCharacter); this endpoint
            // reports each item once for the whole account, with a count of
            // how many an equipment template can draw at a time.
            if (armoryTask != null)
            {
                try
                {
                    var armory = await armoryTask;
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
                    failedSourceNames.Add(AccountDataSource.LegendaryArmory);
                }
            }

            // Character inventories, equipment and crafting disciplines.
            // CharacterSnapshotCollector owns the rule that one failed
            // character discards every discipline. The fan-out moved to
            // CharacterPagePlan when the fetch stopped being per character.
            CharacterSnapshotHarvest harvest = null;
            try
            {
                var characterNames = await namesTask;
                var names = characterNames == null ? new List<string>() : characterNames.ToList();
                onCharacterCountKnown?.Invoke(names.Count);

                var parts = await FetchCharacterPagesAsync(names.Count, ct);

                // The pages are already in hand, so the bound here governs
                // no requests. It stays above the roster so the fold runs
                // over every character.
                harvest = await CharacterSnapshotCollector.CollectAsync(
                    names,
                    Math.Max(1, names.Count),
                    name => Task.FromResult(PartFor(name, parts)),
                    ct);

                snapshot.Items.AddRange(harvest.Items);
                snapshot.LegendaryArmoryEquipped.AddRange(harvest.ArmoryEquipped);
                snapshot.CharacterDisciplines = harvest.Disciplines;
                snapshot.CharacterCount = harvest.CharacterCount;
                snapshot.IncompleteCharacterCount = harvest.IncompleteCharacterCount;
            }
            catch (Exception ex) when (!(ex is OperationCanceledException))
            {
                Logger.Warn(ex, "Failed to fetch character list");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch character list: {ex.GetType().Name} - {ex.Message}");
                failedSources++;
                failedSourceExceptionTypeNames.Add(ex.GetType().Name);
                failedSourceNames.Add(AccountDataSource.Characters);

                // A partially populated list would read as an affirmative
                // "not trained" claim for characters never reached.
                snapshot.CharacterDisciplines = null;
            }

            // Stamped here, not at the top: every holding above has landed
            // and nothing below reads one. Stamping at the start dated the
            // snapshot from when the fetch began, so a fetch that ran the
            // whole budget produced data already a minute older than its own
            // timestamp claimed. The resolve passes below only attach names
            // and icons, so they do not move the capture moment.
            snapshot.CapturedAt = DateTime.UtcNow;

            // A partial failure must never masquerade as a full snapshot:
            // throw instead of returning a snapshot with holes relative to
            // a prior good fetch (see SnapshotFetchFailedException). A
            // character whose bags or equipment could not be read is such a
            // hole, and a plan acts on it by telling the user to buy an
            // item their own bags already hold.
            bool charactersIncomplete = harvest != null && !harvest.IsComplete;
            if (failedSources > 0 || charactersIncomplete)
            {
                throw new SnapshotFetchFailedException(
                    failedSources,
                    totalSources,
                    failedSourceExceptionTypeNames,
                    harvest?.IncompleteCharacterNames,
                    failedSourceNames);
            }

            // The three resolve passes write disjoint fields (item
            // name/icon/rarity, skin name/icon, currency name/icon) and
            // share only _cacheLock, so they run together. None of them
            // throws except on cancellation.
            await Task.WhenAll(
                ResolveItemDetailsAsync(snapshot.Items, ct),
                ResolveSkinDetailsAsync(snapshot.Items, ct),
                ResolveCurrencyDetailsAsync(snapshot.Wallet, ct));

            return snapshot;
        }

        /// <summary>
        /// One GW2 API call under its own deadline, reported as a
        /// <see cref="TimeoutException"/> rather than an
        /// <see cref="OperationCanceledException"/> so a per-source catch
        /// filter counts it as a failed source instead of letting it end
        /// the whole fetch as a cancellation.
        /// </summary>
        private static async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken ct)
        {
            using (var callCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                callCts.CancelAfter(PerCallTimeout);
                try
                {
                    return await call(callCts.Token);
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    throw new TimeoutException($"GW2 API request exceeded {PerCallTimeout.TotalSeconds:0}s.");
                }
            }
        }

        /// <summary>
        /// One GW2 API call, repeated in place when the failure is worth
        /// another attempt (see <see cref="SnapshotCallRetry"/>). Every
        /// account-wide and per-character call goes through here, so a
        /// single transient refusal is paid for inside this fetch rather
        /// than costing the whole snapshot and the caller's failure
        /// backoff. <paramref name="isUsable"/> rejects a result the caller
        /// cannot use even though the call returned.
        /// </summary>
        private static Task<T> RetriedCallAsync<T>(
            Func<CancellationToken, Task<T>> call,
            CancellationToken ct,
            Func<T, bool> isUsable = null)
        {
            return SnapshotCallRetry.RunAsync(token => CallAsync(call, token), isUsable, null, ct);
        }

        /// <summary>
        /// Waits for every started request to finish and swallows the
        /// result. Task.WhenAll marks all of their exceptions observed but
        /// reports only one, so each task is awaited again afterwards,
        /// where its own catch block can record it as a failed source.
        /// </summary>
        private static async Task SettleAsync(params Task[] tasks)
        {
            try
            {
                await Task.WhenAll(tasks.Where(t => t != null));
            }
            catch (Exception)
            {
                // Every failure is re-raised by the per-source await below.
            }
        }

        /// <summary>
        /// This character's rows, or null when no page returned it. The fold
        /// reads null as holdings the fetch could not get, which is what
        /// stops an under-counted snapshot being committed.
        /// </summary>
        private static CharacterSnapshotPart PartFor(
            string characterName, Dictionary<string, CharacterSnapshotPart> parts)
        {
            CharacterSnapshotPart part;
            parts.TryGetValue(characterName, out part);
            return part;
        }

        /// <summary>
        /// Every character the roster names, as whole records fetched a page
        /// at a time. A page that fails leaves its characters out of the
        /// result, which <see cref="BuildCharacterPart"/> reports as unread.
        /// </summary>
        /// <remarks>
        /// Retry is per page now, so one refused request re-asks for up to
        /// <see cref="CharacterPagePlan.PageSize"/> characters instead of
        /// one endpoint. That is the cheap side of the trade: without the
        /// retry the whole snapshot is refused, because a character nobody
        /// read makes the harvest incomplete and an incomplete harvest is
        /// never committed.
        /// </remarks>
        private async Task<Dictionary<string, CharacterSnapshotPart>> FetchCharacterPagesAsync(
            int characterCount, CancellationToken ct)
        {
            var parts = new Dictionary<string, CharacterSnapshotPart>(StringComparer.Ordinal);
            var sync = new object();

            await BoundedConcurrency.ForEachAsync(
                Enumerable.Range(0, CharacterPagePlan.PageCount(characterCount)),
                CharacterPagePlan.MaxPagesInFlight,
                async page =>
                {
                    try
                    {
                        var fetched = await RetriedCallAsync(
                            c => _apiManager.Gw2ApiClient.V2.Characters.PageAsync(
                                page, CharacterPagePlan.PageSize, c),
                            ct);
                        if (fetched == null)
                        {
                            return;
                        }

                        // Projected as each page lands, so only the pages
                        // in flight are held rather than every record at
                        // once. A record carries payload this module never
                        // reads, and a large roster would hold all of it.
                        foreach (var record in fetched)
                        {
                            if (record == null || string.IsNullOrEmpty(record.Name))
                            {
                                continue;
                            }

                            var part = CharacterRecordProjection.Build(record.Name, record);
                            lock (sync)
                            {
                                parts[record.Name] = part;
                            }
                        }
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        Logger.Warn(ex, "Failed to fetch character page {Page}", page);
                        ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot-fetch", $"Failed to fetch character page {page}: {ex.GetType().Name} - {ex.Message}");
                    }
                },
                ct);

            return parts;
        }

        /// <summary>
        /// Runs one bulk-detail pass over <paramref name="ids"/> in
        /// ItemBulkLimit-sized requests, several at a time, and reports how
        /// many requests failed along with the first failure.
        /// <para>
        /// The catch is per request, not around the whole pass. A full
        /// account resolves thousands of ids, and one request the API
        /// refuses used to skip every later request AND the apply pass, so
        /// a single bad id cost the names of every item after it.
        /// </para>
        /// </summary>
        private static async Task<(int FailedRequests, Exception First)> ResolveInChunksAsync(
            List<int> ids,
            Func<List<int>, CancellationToken, Task> fetchChunk,
            CancellationToken ct)
        {
            int failedRequests = 0;
            Exception firstFailure = null;

            var chunks = new List<List<int>>();
            for (int i = 0; i < ids.Count; i += ItemBulkLimit)
            {
                chunks.Add(ids.Skip(i).Take(ItemBulkLimit).ToList());
            }

            await BoundedConcurrency.ForEachAsync(
                chunks,
                MaxDetailRequestsInFlight,
                async chunk =>
                {
                    try
                    {
                        await fetchChunk(chunk, ct);
                    }
                    catch (Exception ex) when (!(ex is OperationCanceledException))
                    {
                        Interlocked.Increment(ref failedRequests);
                        Interlocked.CompareExchange(ref firstFailure, ex, null);
                    }
                },
                ct);

            return (failedRequests, firstFailure);
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

                var outcome = await ResolveInChunksAsync(
                    uncachedIds,
                    async (chunk, token) =>
                    {
                        var fetched = await CallAsync(c => _apiManager.Gw2ApiClient.V2.Items.ManyAsync(chunk, c), token);
                        lock (_cacheLock)
                        {
                            foreach (var item in fetched)
                            {
                                var url = item.Icon.Url;
                                _itemCache[item.Id] =
                                    (item.Name ?? "", url != null ? url.AbsoluteUri : "", RarityOf(item));
                            }
                        }
                    },
                    ct);

                if (outcome.First != null)
                {
                    LogChunkFailures("item names/icons", outcome.FailedRequests, outcome.First);
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

                var outcome = await ResolveInChunksAsync(
                    uncachedIds,
                    async (chunk, token) =>
                    {
                        var fetched = await CallAsync(c => _apiManager.Gw2ApiClient.V2.Skins.ManyAsync(chunk, c), token);
                        lock (_cacheLock)
                        {
                            foreach (var skin in fetched)
                            {
                                var url = skin.Icon.Url;
                                _skinCache[skin.Id] =
                                    (skin.Name ?? "", url != null ? url.AbsoluteUri : "");
                            }
                        }
                    },
                    ct);

                if (outcome.First != null)
                {
                    LogChunkFailures("skin names", outcome.FailedRequests, outcome.First);
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
                    var currencies = await CallAsync(c => _apiManager.Gw2ApiClient.V2.Currencies.AllAsync(c), ct);
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
