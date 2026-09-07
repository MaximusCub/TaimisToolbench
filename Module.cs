using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Blish_HUD.Modules;
using Blish_HUD.Modules.Managers;
using Blish_HUD.Settings;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TaimisToolbench.Contracts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Services.Recipes;
using TaimisToolbench.Views;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench
{
    internal class ContentsManagerRecipeSource : IMysticForgeRecipeSource
    {
        private readonly ContentsManager _contents;

        public ContentsManagerRecipeSource(ContentsManager contents)
        {
            _contents = contents;
        }

        public System.IO.Stream Open()
        {
            return _contents.GetFileStream("mystic_forge_recipes.json");
        }
    }

    [Export(typeof(Blish_HUD.Modules.Module))]
    public class Module : Blish_HUD.Modules.Module
    {
        private static readonly Logger Logger = Logger.GetLogger<Module>();

        internal ContentsManager ContentsManager => this.ModuleParameters.ContentsManager;

        internal DirectoriesManager DirectoriesManager => this.ModuleParameters.DirectoriesManager;

        internal Gw2ApiManager Gw2ApiManager => this.ModuleParameters.Gw2ApiManager;

        private CornerIcon _cornerIcon;
        private ResizableTabbedWindow _mainWindow;
        private ModalDialog _modalDialog;
        private ApiAccessDialog _apiAccessDialog;
        private MainView _snapshotContent;
        private CraftingPlanView _craftingContent;
        private LogTabContent _logContent;
        private Tab _logTab;
        private Tab _settingsTab;

        // The Log tab's "Clear View" floor lives on Module, not
        // LogTabContent: Blish reconstructs LogTabContent on every tab
        // selection, so a field there cannot survive a tab switch (a
        // cleared view resurrected on reopen). A plain long is enough -
        // every read/write of this field is main-thread-only; the one
        // ThreadPool touch (the view-factory closure) only copies
        // delegates, never dereferences the field.
        private long _logViewClearedBeforeVersion;
        private SettingsTabContent _settingsContent;
        private AboutTabContent _aboutContent;

        private ModuleSettings _settings;
        private RankerStore _rankerStore;
        private RankerTabContent _rankerContent;

        // Held so Update can poll the Ranker for a snapshot change ONLY
        // while it is the tab on screen; compared by reference, like
        // _logTab/_settingsTab.
        private Tab _rankerTab;

        // Carried from BuildViews to BuildTabs so the Ranker's rows serve
        // the same session stat-cache tooltips the plan and snapshot rows
        // do. Never a fetch (see ItemMetadataService.GetCachedStatBlock).
        private Func<int, ItemStatBlock> _getItemStatBlock;

        // Its twin, and the half a tab needs to show the same item tooltip
        // the game does rather than the identity-only fallback: the
        // accessor above only ever reports what the session already holds,
        // so a tab that draws items nothing has fetched has to ask for them
        // (Views/Rendering/ItemStatWarmer.cs). Carried alongside it so a
        // tab built in BuildTabs can take both.
        private Func<IReadOnlyList<int>, Task<int>> _warmItemStatsAsync;

        // Plan History: the index is held in memory for the module's
        // lifetime and mutated under _planHistoryLock from two sides -
        // the capture path's ThreadPool continuation
        // (CaptureHistoryEntry) and the tab's main-thread mutations
        // (MutateHistoryIndex). Every mutation persists before the lock
        // is released, so the in-memory index and plan_history.json can
        // never disagree for longer than one write.
        private PlanHistoryStore _planHistoryStore;
        private PlanHistoryBlobStore _planHistoryBlobStore;
        private PlanHistoryIndex _planHistoryIndex =
            new PlanHistoryIndex { SchemaVersion = PlanHistoryIndex.CurrentSchemaVersion };

        private readonly object _planHistoryLock = new object();
        private PlanHistoryTabContent _planHistoryContent;
        private Tab _planHistoryTab;

        // Held so OpenHistoryEntry/ResolveHistoryEntryAsync can switch
        // the window to the Crafting Plan tab; compared by reference,
        // like _logTab/_settingsTab.
        private Tab _craftingPlanTab;

        // Held so the icon prime below can stand down while the tab it
        // primes for is the one on screen, running its own faster prime.
        private Tab _snapshotTab;

        // The Snapshot tab's icon art, asked for a few pictures a frame
        // while the player is on some other tab, so the tab is warm the
        // first time it is opened. Both are frame-thread only: the queue is
        // fed from Update's snapshot drain and drained from Update itself.
        // The batch list is a reused scratch buffer, so a prime frame
        // allocates nothing.
        private readonly IconPrimeQueue _iconPrime = new IconPrimeQueue();
        private readonly List<string> _iconPrimeBatch = new List<string>();

        // Set the first time the Snapshot tab is opened, and never cleared.
        // From then on the tab's own prime owns the list, so this one stops
        // for good rather than pausing - which is what keeps a picture from
        // being asked for by both.
        private bool _iconPrimeHandedOver;

        private SnapshotStore _snapshotStore;
        private StatusStore _statusStore;
        private Gw2AccountSnapshotService _snapshotService;
        private AccountSnapshot _currentSnapshot;
        private AccountSnapshot _pendingSnapshot;
        private bool _snapshotDirty;
        private string _lastStatus;
        // Drained in Update() using the same dirty-flag polling pattern as
        // _snapshotDirty above, rather than MainThreadMarshal, so a status
        // saved from a ThreadPool continuation reaches the main thread the
        // same way an already-established mechanism does - one polling
        // path instead of two competing ways to get back to the UI thread.
        private bool _statusDirty;

        // A generated plan loaded from disk at LoadAsync time, applied to
        // _craftingContent from Update() - same dirty-flag drain shape as
        // _pendingSnapshot/_snapshotDirty. Written once in LoadAsync and
        // drained (never re-armed) the first time Update() sees the flag.
        private PersistedPlanLoad _pendingPlanRestore;
        private bool _planRestoreDirty;

        // The original request/timestamp behind the most recently
        // persisted plan - the last successful Generate, or the restored
        // plan. A ResolveWithOverrides persist reuses this as-is: a local
        // re-solve must not advance GeneratedAt/request just because a
        // pill was clicked. One immutable object published through a
        // single volatile field: a reader always sees all four values
        // from the same publish, never a torn mix - four separate fields
        // written from a ThreadPool continuation raced main-thread reads.
        private volatile PersistedPlanMetadata _lastPersistedPlanMetadata;

        // Guards the compound "check _generateCompletedThisSession, then
        // publish restore metadata" sequence in Update()'s drain against
        // PersistAfterGenerateAsync's "set completed, then publish
        // generate metadata" sequence - a bare volatile bool leaves a
        // TOCTOU window where the restore's stale metadata could pair
        // with a just-generated Result, or clobber the live view. Held
        // only across the cheap field read/write pair, never disk I/O or
        // rendering.
        private readonly object _generateCompletionLock = new object();

        // True once a real Generate has completed this session - distinct
        // from _lastPersistedPlanMetadata being non-null, which a
        // restored plan also sets. Guards Update()'s restore drain: a
        // Generate can complete while LoadAsync is still awaiting its
        // refresh, and without this guard the drain would overwrite the
        // just-generated plan with the stale on-disk one. Every access
        // goes through _generateCompletionLock.
        private bool _generateCompletedThisSession;

        // Mirrors CraftingPlanView's ++_generateSequence "only the newest
        // generation may act" convention, scoped to Module's own
        // disk-write decision. A second Generate CAN start while an
        // earlier one's persist is pending (the modal-confirm path fires
        // one gated only on _currentPlan != null). Incremented
        // synchronously on the main thread in lockstep with the view's
        // own myGen bump; volatile because the post-await continuation
        // compares it from a ThreadPool thread.
        private volatile int _persistGenerateSequence;

        private HttpClient _httpClient;
        private CraftingPlanPipeline _craftingPipeline;

        // Held apart from the pipeline that owns it so the Settings tab's
        // currency icons can read the same session-cached list the plan
        // rows do, instead of opening a second one - see
        // WarmCurrencyMetadataForSettings.
        private CurrencyMetadataService _currencyMetadataService;
        private PlanStore _planStore;

        // Lives here rather than on CraftingPlanView so it survives a
        // view build cycle (see PlanStripStatusBoard). Thread-safe and
        // constructor-injected once - CraftingPlanView is a singleton
        // Module constructs exactly once.
        private readonly PlanStripStatusBoard _planStripStatusBoard = new PlanStripStatusBoard();
        private VendorOfferStore _vendorOfferStore;
        private OverlayRecipeCacheStore _recipeOverlay;

        // Held so a later build-id attempt can stamp both stores, the way
        // the startup attempt does. See FetchLiveBuildIdAsync.
        private SeededRecipeCacheStore _recipeSeed;
        private Gw2BuildApiClient _buildApi;

        // At most one build-id fetch in flight. Retried rather than tried
        // once per session: without the id nothing verifies the corpus, so
        // a launch that raced the network stayed unverified until Blish was
        // restarted.
        private int _buildIdFetchRunning;
        private IItemSearchProvider _itemSearchProvider;
        private Texture2D _moduleIconTexture;
        private Texture2D _cornerIconTexture;
        private Texture2D _emblemTexture;

        // Cancelled FIRST in Unload, before anything this module owns is
        // disposed. Everything that outlives a single frame runs under it:
        // plan generation, the typed-name search, the restored-plan stat
        // top-up and the startup build-id fetch. Without it, disabling the
        // module mid-generation left a continuation chain running against
        // the HttpClient Unload had just disposed, finishing by writing the
        // dead instance's plan.json - which the next enable would restore.
        //
        // Deliberately never disposed: the token is captured by in-flight
        // work and by CreateLinkedTokenSource callers, and disposing the
        // source while they still hold it buys nothing (a cancelled source
        // with no live registrations holds no unmanaged state) at the cost
        // of turning an orderly cancel into an ObjectDisposedException.
        private CancellationTokenSource _lifetimeCts;

        // The single-fetch slot: the claim that decides which caller gets
        // to refresh, and the CancellationTokenSource that refresh runs
        // under. Both used to be bare fields here (a volatile bool checked
        // then set, and a source cancelled/disposed/reassigned in three
        // unsynchronized statements) - see SnapshotRefreshSlot's own doc
        // comment for the race that shape allowed.
        private readonly SnapshotRefreshSlot _refreshSlot = new SnapshotRefreshSlot();

        // Cancels the background /v2/build lookup and the corpus probe
        // behind it - both retry/run across several seconds and hold
        // _httpClient, which Unload disposes.
        private readonly CancellationTokenSource _buildIdCts = new CancellationTokenSource();

        // The corpus probe (RecipeCorpusVerifier): one id-list request per
        // game build, run in the background and retried at the start of a
        // generation when an earlier attempt failed. _corpusProbeRunning
        // keeps at most one in flight; _liveGw2BuildId is 0 until the
        // /v2/build fetch lands (no probe can run without it).
        private CompositeRecipeCacheStore _recipeCacheStore;
        private RecipeCorpusVerifier _recipeCorpusVerifier;
        private int _liveGw2BuildId;
        private int _corpusProbeRunning;

        // The corpus sweep (RecipeCorpusRefresher), phase 2: refetches the
        // content of every held recipe once per build, so an in-place
        // ingredient change is repaired rather than served stale forever.
        // Sequenced after a green probe and never awaited by anything.
        private RecipeCorpusRefresher _recipeCorpusRefresher;
        private int _corpusRefreshRunning;

        // Bumped only by ClearCache; a
        // fetch that captured an older epoch before starting must discard
        // its result rather than commit over a cleared cache. A bare
        // volatile counter (the original fix) left the check and the
        // commit as separate unsynchronized steps, so the gate now owns
        // both the epoch and the lock that makes ClearCache's bump/clear
        // and FetchAndSaveSnapshotAsync's check/commit mutually exclusive
        // - see SnapshotCommitGate's own doc comment for the full race.
        private readonly SnapshotCommitGate _snapshotCommitGate = new SnapshotCommitGate();

        // UTC ticks of the most
        // recent FAILED background refresh, or 0 if none. api-F1 made a
        // failed FetchSnapshotAsync throw instead of stamping
        // _currentSnapshot.CapturedAt with a fresh timestamp - correct
        // (no more silent data corruption), but that stamping used to be
        // the only thing making Update()'s staleness gate wait before
        // retrying. Without this, a persistent failure would re-arm the
        // gate on every single Update() tick instead of waiting out
        // RefreshFailureBackoff - see RefreshSnapshotInBackgroundAsync.
        // long, not DateTime, because C# disallows `volatile` on 64-bit
        // primitives; Interlocked.Read/Exchange give it the same cross-thread
        // visibility _refreshSlot gets from its own Interlocked claim
        // (_snapshotCommitGate below gets it from its own internal lock
        // instead), without needing a lock of its own here.
        private long _lastFailedRefreshAttemptTicks;

        // Minimum wait after a failed background
        // refresh before RefreshSnapshotInBackgroundAsync is allowed to
        // auto-retrigger again. Deliberately does NOT gate UserRefreshAsync
        // (the explicit "Refresh Now" button) - a user-initiated retry
        // should never be throttled by an earlier automatic failure.
        private static readonly TimeSpan RefreshFailureBackoff = TimeSpan.FromSeconds(60);

        // One-shot: the automatic fetch for an install with NO cached
        // snapshot fires at most once per armed shot (see
        // FirstLoadSnapshotGate), re-armed by ClearCache, which puts the
        // module back into exactly the state the shot exists for.
        // Main-thread only - Update() and the Clear Cache click handler.
        private bool _firstLoadRefreshAttempted;

        // How often Update() may re-read the live inputs of that shot's
        // gate (see FirstLoadSnapshotGate.ShouldCheckNow). Coarse on
        // purpose: a granted subtoken already reaches the fetch through
        // OnSubtokenUpdated, so this poll is the backstop for the case
        // where that event fired before the handler was attached, and a
        // second or two of extra latency on it is invisible.
        private static readonly TimeSpan FirstLoadGateCheckInterval = TimeSpan.FromSeconds(2);

        // Seeded full so the first Update() after load checks immediately,
        // and reset the same way whenever the shot is re-armed.
        private TimeSpan _sinceFirstLoadGateCheck = FirstLoadGateCheckInterval;

        // Whether the timer-driven auto-refresh is running right now.
        // Written from RefreshSnapshotInBackgroundAsync, which starts on the
        // main thread but whose finally may resume on a ThreadPool
        // continuation (Blish's XNA host installs no SynchronizationContext)
        // - hence volatile, and hence applied to the view from Update()
        // rather than written to a control here, the same shape
        // SaveStatusThreadSafe already uses for status text.
        //
        // NOT the same flag as _refreshSlot's claim: that one gates whether
        // a refresh may START (and covers the clicked path too), while this
        // one is only ever about the SPINNER for the automatic path. Keeping
        // them apart is what lets the clicked path own its own spinner
        // without either path switching the other's off.
        private volatile bool _backgroundRefreshInFlight;

        // Last value actually pushed to the view, so the drain below pushes
        // on CHANGE rather than every frame. Only advanced when a view
        // existed to receive the push: the very first refresh runs at module
        // load, and marking it applied against a null view would lose the
        // state instead of retrying on the next tick.
        private bool _backgroundRefreshSpinnerApplied;

        [ImportingConstructor]
        public Module([Import("ModuleParameters")] ModuleParameters moduleParameters)
            : base(moduleParameters)
        {
        }

        protected override void DefineSettings(SettingCollection settings)
        {
            _settings = new ModuleSettings(settings);
        }

        /// <summary>
        /// Composition root, split by lifecycle: configure logging, build
        /// the service graph, load textures, build the views, build the
        /// window, add its tabs, wire the events. The order below is the
        /// order those steps depend on each other in - see each step's own
        /// remarks for what it must come after.
        /// </summary>
        protected override void Initialize()
        {
            // First line of Initialize, so every construction below can hand
            // its token to work that must not outlive this module instance.
            _lifetimeCts = new CancellationTokenSource();
            var lifetimeToken = _lifetimeCts.Token;

            string dataDir = DirectoriesManager.GetFullDirectoryPath("data");

            ConfigureLogging(dataDir);
            var itemMetadataService = BuildServices(dataDir, lifetimeToken);
            LoadTextures();
            BuildViews(dataDir, lifetimeToken, itemMetadataService);
            BuildWindow();
            BuildTabs();
            WireEvents();
        }

        /// <summary>
        /// Attaches the log store and the settings subscriptions that feed
        /// it. Runs before every other step so their onError callbacks can
        /// reach the Log tab no matter which one fails first.
        /// </summary>
        private void ConfigureLogging(string dataDir)
        {
            // Configured before any other store so their
            // onError callbacks (BuildServices) can always reach ModuleLog.Shared
            // regardless of construction order - Write() is always safe to
            // call even before Configure() attaches the file store (writes
            // just stay ring-only until then). The log store's OWN IO
            // failure is wired straight to Blish's Logger, never back into
            // ModuleLog - see ModuleLogStore's doc comment on why
            // (unbounded recursion into the sink whose own write just
            // failed).
            var logStore = new ModuleLogStore(dataDir, (message, ex) => Logger.Warn(ex, message));
            ModuleLog.Shared.Configure(logStore, _settings.GetClampedLogMaxSizeBytes(), (message, ex) => Logger.Warn(ex, message));
            // Blish renders each SettingEntry a second time in Manage
            // Modules, and those controls only write the setting - these
            // subscriptions carry a change live no matter which UI made it.
            _settings.LogDiagnosticsEnabled.SettingChanged += OnLogDiagnosticsEnabledChanged;
            ModuleLog.Shared.DiagnosticsEnabled = _settings.LogDiagnosticsEnabled.Value;
            _settings.LogMaxSizeBytes.SettingChanged += OnLogMaxSizeBytesChanged;
            _settings.ClickSoundVolumePercent.SettingChanged += OnClickSoundVolumeChanged;
            Views.Rendering.ClickSound.VolumePercent = _settings.GetClampedClickSoundVolumePercent();

            // Once-per-session age-based retention enforcement, BEFORE the
            // ring is seeded from the file below - the ring then mirrors
            // exactly what survived retention, rather than briefly showing
            // an entry this same call is about to prune from disk. Both run
            // here (Initialize, not LoadAsync) and before any other store
            // is even constructed, so the seeded pre-session history always
            // sorts before this session's own first log line - see
            // ModuleLog.SeedFromStore's own doc comment.
            ModuleLog.Shared.PruneOlderThan(_settings.GetClampedLogRetentionDays());
            ModuleLog.Shared.SeedFromStore();
        }

        /// <summary>
        /// Builds the stores, API clients, seeds and the crafting pipeline.
        /// Returns the <see cref="ItemMetadataService"/> because the views
        /// read its session stat cache for tooltips - the same instance the
        /// pipeline fills, so a hover shows what the plan already fetched.
        /// </summary>
        private ItemMetadataService BuildServices(string dataDir, CancellationToken lifetimeToken)
        {
            // Every store's IO-failure callback routes to ModuleLog so a
            // store failure is visible in the Log tab, not just in an
            // attached debugger.
            Action<string, Exception> onStoreError = (message, ex) =>
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "store", $"{message}: {ex.GetType().Name} - {ex.Message}");

            // PlanStore alone also reports a non-failure: a saved plan
            // written by a build at an older shipped schema version. It is
            // expected, benign and repaired by the next Generate, so it
            // must read as routine in the log rather than as damage - see
            // PlanStore's own doc comment.
            Action<string> onStoreInfo = message =>
                ModuleLog.Shared.Write(ModuleLogLevel.Info, "store", message);

            _snapshotStore = new SnapshotStore(dataDir, onStoreError);
            _statusStore = new StatusStore(dataDir, onStoreError);
            _rankerStore = new RankerStore(dataDir, onStoreError);
            _planHistoryStore = new PlanHistoryStore(dataDir, onStoreError);
            _planHistoryBlobStore = new PlanHistoryBlobStore(dataDir, onStoreError);
            _planStore = new PlanStore(dataDir, onStoreError, onStoreInfo);
            _snapshotService = new Gw2AccountSnapshotService(Gw2ApiManager);
            _lastStatus = _statusStore.Load();

            _httpClient = new HttpClient();
            Gw2ApiUserAgent.Apply(_httpClient, Gw2ApiUserAgent.ModuleProduct,
                Gw2ApiUserAgent.ReadManifestVersion(ModuleParameters?.Manifest));
            var rawRecipeApi = new Gw2RecipeApiClient(_httpClient);
            var mfSource = new ContentsManagerRecipeSource(ContentsManager);
            var mfData = RecipeClientFactory.LoadData(mfSource);
            var recipeApi = RecipeClientFactory.Create(rawRecipeApi, mfData);
            var priceApi = new Gw2PriceApiClient(_httpClient);
            var itemApi = new Gw2ItemApiClient(_httpClient);

            var vendorLoader = new VendorOfferLoader();
            _vendorOfferStore = new VendorOfferStore(dataDir, vendorLoader, onStoreError);
            try
            {
                using (var baselineStream = ContentsManager.GetFileStream("vendor_offers.json"))
                {
                    _vendorOfferStore.LoadBaseline(baselineStream);
                }
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Vendor baseline load failed, starting with an empty baseline: {ex.GetType().Name} - {ex.Message}");
                _vendorOfferStore.LoadBaseline(null);
            }

            _vendorOfferStore.LoadOverlay();

            // Recipe cache: seed + overlay
            var recipeSeed = new SeededRecipeCacheStore();
            try
            {
                using (var searchStream = ContentsManager.GetFileStream("recipe_search_seed.json"))
                using (var recipesStream = ContentsManager.GetFileStream("recipes_seed.json"))
                {
                    recipeSeed.Load(searchStream, recipesStream);
                }
            }
            catch (Exception ex)
            {
                // No seed files yet - graceful degradation. Previously a
                // fully silent bare catch (dev/proposals/d2-log-system.md Section 8: "a
                // real gap the migration closes, not just a routing
                // change") - now visible in the Log tab at Warn.
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Recipe seed load failed, starting with an empty seed cache: {ex.GetType().Name} - {ex.Message}");
            }

            // Wiki-sourced Mystic Forge recipes are seed content, not an
            // API fallback: folding them in here means a seed row saying
            // "the API knows no recipe for this item" can never shadow one,
            // and no game build id can affect whether they are found - see
            // SeededRecipeCacheStore.MergeMysticForgeRecipes.
            recipeSeed.MergeMysticForgeRecipes(mfData);
            recipeSeed.FinalizeIndex();

            try
            {
                using (var manifestStream = ContentsManager.GetFileStream("recipe_seed_manifest.json"))
                {
                    recipeSeed.LoadManifest(manifestStream);
                }
            }
            catch (Exception ex)
            {
                // No manifest - staleness detection disabled. Previously a
                // fully silent bare catch - see the recipe-seed catch above.
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Recipe seed manifest load failed, staleness detection disabled: {ex.GetType().Name} - {ex.Message}");
            }

            // Item name seed for search provider; the parsed seed is also
            // reused as the metadata fallback for ids the live API drops.
            ItemNameSeedData itemNameSeed = null;
            try
            {
                using (var nameStream = ContentsManager.GetFileStream("item_name_seed.json"))
                {
                    _itemSearchProvider = ItemSearchProviderFactory.Create(
                        nameStream, out string fallbackReason, out itemNameSeed);
                    if (fallbackReason != null)
                    {
                        Logger.Info("Item search fallback to static provider: {0}", fallbackReason);
                        ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Item search fallback to static provider: {fallbackReason}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Info("Item search fallback to static provider: [{0}] {1}", ex.GetType().Name, ex.Message);
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Item search fallback to static provider: [{ex.GetType().Name}] {ex.Message}");
                _itemSearchProvider = new StaticItemSearchProvider();
            }

            // Static-local-file seeds, no async fetch needed - loaded once
            // here and passed straight to the pipeline (simpler than
            // CurrencyMetadataService, which hits a live API).
            // Acquisition hints: wiki-derived guidance for items with no
            // priceable source (docs/KNOWN-ISSUES #8).
            IReadOnlyDictionary<int, AcquisitionHint> acquisitionHints = LoadSeedOrNull(
                "acquisition_hints_seed.json", "Acquisition hints unavailable",
                AcquisitionHintService.Load);

            // Daily craft-cooldown seed: wiki-verified recipes whose
            // crafting action itself is server-capped.
            IReadOnlyDictionary<int, DailyCooldownItem> dailyCooldownItems = LoadSeedOrNull(
                "daily_cooldown_items.json", "Daily cooldown items unavailable",
                DailyCooldownItemService.Load);

            // Recipe-sheet seed: recipe id -> unlocking recipe-sheet item
            // id for RecipeSheetSavingsCalculator.
            IReadOnlyDictionary<int, int> recipeSheetItemIdByRecipeId = LoadSeedOrNull(
                "recipe_sheet_items.json", "Recipe sheet items unavailable",
                RecipeSheetItemSeedService.Load);

            var recipeOverlay = new OverlayRecipeCacheStore(dataDir, onStoreError);
            _recipeOverlay = recipeOverlay;
            recipeOverlay.Load();
            if (recipeOverlay.DroppedLearnedNegatives > 0)
            {
                // The one-time v1 migration, logged so a support report can
                // show it happened.
                ModuleLog.Shared.Write(ModuleLogLevel.Info, "startup", $"Recipe overlay migration: dropped {recipeOverlay.DroppedLearnedNegatives} learned negative row(s); learned positives kept.");
            }

            // Composite + service built before the build-id task below so
            // the probe it kicks can repair the store and invalidate the
            // service's session memo.
            var recipeCacheStore = new CompositeRecipeCacheStore(recipeSeed, recipeOverlay);
            _recipeCacheStore = recipeCacheStore;
            var recipeService = new RecipeService(recipeApi, cacheStore: recipeCacheStore);
            _recipeCorpusVerifier = new RecipeCorpusVerifier(
                _httpClient, recipeCacheStore, recipeService.InvalidateSearch);
            _recipeCorpusRefresher = new RecipeCorpusRefresher(
                _httpClient,
                recipeCacheStore,
                recipeService.InvalidateSearch,
                message => ModuleLog.Shared.Write(ModuleLogLevel.Debug, "startup", message));

            // Async build ID fetch: stamps provenance and licenses the
            // corpus probe - never a wipe. The overlay is already loaded
            // and serving above.
            _recipeSeed = recipeSeed;
            _buildApi = new Gw2BuildApiClient(_httpClient);
            KickBuildIdFetch();

            // Hoisted out of the pipeline's argument list so the plan view
            // can read its session item-stat cache for tooltips - the same
            // instance, so the stats the plan already fetched are the ones
            // a hover reads. Never a fetch (GetCachedStatBlock).
            var itemMetadataService = new ItemMetadataService(itemApi, itemNameSeed);

            _currencyMetadataService = new CurrencyMetadataService(_httpClient);

            _craftingPipeline = new CraftingPlanPipeline(
                recipeService,
                new TradingPostService(priceApi),
                new PlanSolver(),
                itemMetadataService,
                _vendorOfferStore,
                reducer: new InventoryReducer(),
                accountRecipeClient: new Gw2AccountRecipeClient(Gw2ApiManager),
                currencyMetadataService: _currencyMetadataService,
                acquisitionHints: acquisitionHints,
                dailyCooldownItems: dailyCooldownItems,
                recipeSheetItemIdByRecipeId: recipeSheetItemIdByRecipeId,
                activeFestivalNames: ReadActiveFestivalNames);

            return itemMetadataService;
        }

        /// <summary>
        /// Fetches the live game build id in the background and stamps both
        /// recipe stores with it. A no-op once the id is known, and while an
        /// attempt is already running.
        /// <para>
        /// Called at startup and again from
        /// <see cref="KickCorpusVerification"/>, so a launch with no network
        /// picks the id up at a later plan generation rather than staying
        /// unverified until Blish restarts.
        /// </para>
        /// </summary>
        private void KickBuildIdFetch()
        {
            var buildApi = _buildApi;
            if (buildApi == null || Volatile.Read(ref _liveGw2BuildId) != 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _buildIdFetchRunning, 1, 0) != 0)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var build = await buildApi.TryGetBuildIdAsync(_buildIdCts.Token);

                    if (!build.BuildId.HasValue)
                    {
                        // The cache still serves; only the build stamp and
                        // the corpus verification are lost for now, so
                        // recipes a newer build added may render UNKNOWN.
                        string reason = build.LastError == null
                            ? "no response"
                            : $"[{build.LastError.GetType().Name}] {build.LastError.Message}";
                        Logger.Warn("GW2 build ID unavailable after {0} attempts - recipe data cannot be verified against the live build yet: {1}", build.Attempts, reason);
                        ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"GW2 build ID unavailable after {build.Attempts} attempts - recipe data cannot be verified against the live build yet, retrying at the next plan generation: {reason}");
                        return;
                    }

                    _recipeOverlay?.SetCurrentBuildId(build.BuildId.Value);
                    _recipeSeed?.SetCurrentBuildId(build.BuildId.Value);

                    Volatile.Write(ref _liveGw2BuildId, build.BuildId.Value);
                    KickCorpusVerification();
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    // Unloaded mid-fetch: _buildIdCts is cancelled and
                    // _httpClient disposed before this task can finish.
                }
                finally
                {
                    Volatile.Write(ref _buildIdFetchRunning, 0);
                }
            });
        }

        /// <summary>
        /// Runs the corpus probe in the background: one /v2/recipes id-list
        /// request per game build, the license for serving derived
        /// negatives as exact (see RecipeCorpusVerifier). Never awaited by
        /// plan generation. A no-op while a probe is already in flight, or -
        /// via the verifier's own manifest cheap-out - when this build and
        /// corpus are already verified (0 requests on a same-patch
        /// relaunch). While the live build is unknown it retries the build
        /// fetch instead.
        /// </summary>
        private void KickCorpusVerification()
        {
            int buildId = Volatile.Read(ref _liveGw2BuildId);
            var store = _recipeCacheStore;
            var verifier = _recipeCorpusVerifier;
            if (buildId == 0)
            {
                // Nothing can be verified without the id, and the startup
                // attempt may have run before the network was up. Try again
                // now; the next plan generation calls back here.
                KickBuildIdFetch();
                return;
            }

            if (store == null || verifier == null || !store.CorpusUsable)
            {
                return;
            }

            if (store.NegativesVerifiedBuildId == buildId)
            {
                // Cheapest exit for the by-far-common case; the verifier
                // re-checks with the corpus count for the seed-swap case.
                if (store.VerifiedKnownRecipeCount == store.GetKnownPositiveRecipeIds().Count)
                {
                    KickCorpusRefresh(buildId);
                    return;
                }
            }

            if (Interlocked.CompareExchange(ref _corpusProbeRunning, 1, 0) != 0)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var result = await verifier.VerifyAsync(
                        buildId, store.GetKnownPositiveRecipeIds(), _buildIdCts.Token);
                    switch (result.Status)
                    {
                        case CorpusVerificationStatus.Verified:
                            ModuleLog.Shared.Write(ModuleLogLevel.Info, "startup", $"Recipe corpus verified at build {buildId}: {result.AddedRecipeIds.Count} recipe(s) added, {result.RemovedRecipeIds.Count} removed.");
                            break;
                        case CorpusVerificationStatus.Failed:
                            string lastVerified = store.NegativesVerifiedBuildId > 0
                                ? $"build {store.NegativesVerifiedBuildId}"
                                : "the shipped seed";
                            ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Recipe corpus verification failed ({result.Error?.GetType().Name} - {result.Error?.Message}); recipes added since {lastVerified} may show as UNKNOWN. Retrying at the next plan generation.");
                            break;
                    }

                    if (result.Status != CorpusVerificationStatus.Failed)
                    {
                        // Only behind a reachable API: a sweep launched
                        // straight after a failed probe would spend 67
                        // requests failing at the first one.
                        KickCorpusRefresh(buildId);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    // Unloaded mid-probe: _buildIdCts is cancelled and
                    // _httpClient disposed before this task can finish.
                }
                finally
                {
                    Volatile.Write(ref _corpusProbeRunning, 0);
                }
            });
        }

        /// <summary>
        /// Phase 2 of corpus maintenance, after a green probe: refetches
        /// the content of every positive recipe the module holds, so a
        /// recipe whose ingredients changed in place (KNOWN-ISSUES #48)
        /// stops being served stale. Resumable across launches, one run at
        /// a time, and awaited by nothing - plan generation keeps using the
        /// best corpus it has while this improves it underneath.
        /// </summary>
        private void KickCorpusRefresh(int buildId)
        {
            var store = _recipeCacheStore;
            var refresher = _recipeCorpusRefresher;
            if (buildId == 0 || store == null || refresher == null || !store.CorpusUsable)
            {
                return;
            }

            if (store.CorpusRefreshBuildId == buildId && store.CorpusRefreshComplete)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _corpusRefreshRunning, 1, 0) != 0)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var priority = PriorityRecipeIds.FromItemIds(store, ReadPriorityItemIds());
                    var result = await refresher.RefreshAsync(
                        buildId,
                        store.GetKnownPositiveRecipeIds(),
                        priority,
                        _buildIdCts.Token);

                    switch (result.Status)
                    {
                        case CorpusRefreshStatus.Completed:
                            ModuleLog.Shared.Write(ModuleLogLevel.Info, "startup", $"Recipe corpus content refreshed at build {buildId}: {result.RecipesUpdated} recipe(s) changed since the last build, {result.RecipesFetched} refetched over {result.RequestCount} request(s).");
                            break;
                        case CorpusRefreshStatus.Interrupted:
                            ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Recipe corpus content refresh interrupted after {result.RequestCount} request(s) ({result.Error?.GetType().Name} - {result.Error?.Message}); {result.RecipesUpdated} recipe(s) already repaired, resuming at the next launch.");
                            break;
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    // Unloaded mid-sweep: _buildIdCts is cancelled and
                    // _httpClient disposed before this task can finish. The
                    // cursor written after the last completed batch is what
                    // the next launch resumes from.
                }
                finally
                {
                    Volatile.Write(ref _corpusRefreshRunning, 0);
                }
            });
        }

        /// <summary>
        /// The item ids the user actually depends on, for the sweep's
        /// ordering: Ranker watchlist, the restored plan, plan history.
        /// Read off disk rather than off the loaded fields, because the
        /// sweep starts from a background build-id fetch that can beat
        /// LoadAsync to them. Ordering input only - the sweep covers every
        /// held recipe regardless, so a source that reads as empty costs
        /// priority, never coverage.
        /// </summary>
        private IReadOnlyList<int> ReadPriorityItemIds()
        {
            var itemIds = new List<int>();
            try
            {
                var watchlist = _rankerStore?.Load();
                if (watchlist?.Entries != null)
                {
                    foreach (var entry in watchlist.Entries)
                    {
                        itemIds.Add(entry.ItemId);
                    }
                }

                // The already-restored plan, not a second LoadLatest():
                // that call reports a discarded result to the user, and
                // reading the file twice would say it twice.
                var plan = _pendingPlanRestore?.Plan;
                if (plan?.RequestItems != null)
                {
                    foreach (var item in plan.RequestItems)
                    {
                        itemIds.Add(item.ItemId);
                    }
                }

                var history = _planHistoryStore?.Load();
                if (history?.Entries != null)
                {
                    itemIds.AddRange(PlanHistoryItemIds.ForEntries(history.Entries));
                }
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Debug, "startup", $"Corpus sweep ordering inputs unavailable, sweeping in id order: {ex.GetType().Name} - {ex.Message}");
            }

            return itemIds;
        }

        /// <summary>
        /// Loads the three module textures, each falling back rather than
        /// failing the load.
        /// </summary>
        private void LoadTextures()
        {
            try
            {
                _moduleIconTexture = ContentsManager.GetTexture("icon.png");
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Module icon texture load failed, using the fallback error texture: {ex.GetType().Name} - {ex.Message}");
                _moduleIconTexture = ContentService.Textures.Error;
            }

            try
            {
                // The strip variant: same hammer, re-padded so the glyph
                // fills ~72% of the canvas like the game's own top-row
                // icons (icon.png fills 84% - correct for the module list
                // and emblem, visibly oversized between GW2's menu icons;
                // measured against a live top-row capture 2026-08-23).
                _cornerIconTexture = ContentsManager.GetTexture("corner-icon.png");
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Corner icon texture load failed, using the module icon: {ex.GetType().Name} - {ex.Message}");
                _cornerIconTexture = _moduleIconTexture;
            }

            try
            {
                _emblemTexture = ContentsManager.GetTexture("emblem.png");
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Emblem texture load failed, reusing the module icon: {ex.GetType().Name} - {ex.Message}");
                _emblemTexture = _moduleIconTexture;
            }

            LoadGlyphFont();
        }

        /// <summary>
        /// Seats the module's own glyph font (ref/glyphs.fnt and its atlas
        /// page) on <see cref="UiFonts"/>, so a sortable header can carry a
        /// sort mark that Blish's one text face does not contain.
        /// <para>
        /// Falls back rather than failing the load, like the three textures
        /// above: every seat that draws a glyph degrades to the ASCII it
        /// replaced (Services.UiGlyphs.AsciiFallback), which is worse
        /// typography and no lost information. Logged at Warn because a
        /// missing ref file is a broken install, not a user-visible fault.
        /// </para>
        /// </summary>
        private void LoadGlyphFont()
        {
            try
            {
                GlyphFontDescriptor descriptor;
                using (var stream = ContentsManager.GetFileStream("glyphs.fnt"))
                {
                    descriptor = GlyphFontDescriptor.Parse(stream);
                }

                // GetTexture never throws for a missing file - it answers
                // with ContentService.Textures.Error, whose regions would
                // draw noise at the glyphs' atlas coordinates. Compare
                // against it rather than trusting the call.
                var page = ContentsManager.GetTexture(descriptor.PageFile);
                if (page == null || page == ContentService.Textures.Error)
                {
                    throw new InvalidOperationException(
                        "glyph atlas '" + descriptor.PageFile + "' did not load.");
                }

                UiFonts.InstallGlyphs(descriptor, page);
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"Glyph font load failed, sort indicators fall back to ASCII: {ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// Builds the dialogs and the five tab views. Runs after
        /// <see cref="LoadTextures"/> (the About view takes the module
        /// icon) and before <see cref="BuildWindow"/>, which parents them.
        /// </summary>
        private void BuildViews(string dataDir, CancellationToken lifetimeToken, ItemMetadataService itemMetadataService)
        {
            // BuildWindow runs after this, so the blocked surface is handed
            // over as a lambda rather than a reference - see ModalBackdrop
            // for what it does with it.
            _modalDialog = new ModalDialog(_settings, () => _mainWindow);
            _apiAccessDialog = new ApiAccessDialog();
            _getItemStatBlock = itemMetadataService.GetCachedStatBlock;
            _warmItemStatsAsync = ids => itemMetadataService.WarmStatBlocksAsync(ids, lifetimeToken);

            _snapshotContent = new MainView(
                _currentSnapshot,
                _lastStatus,
                UserRefreshAsync,
                _apiAccessDialog,
                _modalDialog,
                _settings,
                ClearCache,
                SaveStatus,
                SaveStatusThreadSafe,
                itemMetadataService.GetCachedStatBlock,
                // The wallet rows' currency tooltips read the same
                // session cache WarmCurrencyMetadataForSettings fills, so
                // the description arrives without a second fetch.
                _currencyMetadataService.GetCached,
                // A snapshot item's stat block has no other source: the
                // capture reads name, icon and rarity out of Gw2Sharp and
                // keeps nothing else, and the socketed runes, sigils and
                // infusions a row can name are components no plan ever
                // asks for. Without this the tab shows an identity-only
                // tooltip for every item no plan happened to touch.
                _warmItemStatsAsync
            );

            // The generate callback is always routed through the list
            // overload - a single-entry list short-circuits to the
            // single-item method inside the pipeline, so the lambda needs
            // no single-vs-multi branch of its own.
            _craftingContent = new CraftingPlanView(
                (items, useOwn, valueOwnMaterials, priceBasis, ct, progress, phaseProgress, requestLabel) =>
                    StartGenerateAsync(
                        items, useOwn, valueOwnMaterials, priceBasis, ct,
                        progress, phaseProgress, requestLabel, lifetimeToken),
                _modalDialog,
                _itemSearchProvider,
                _settings,
                _planStripStatusBoard,
                (ctx, overrides, ignoredItemIds) =>
                {
                    var result = _craftingPipeline.ResolveWithOverrides(ctx, overrides, ignoredItemIds);
                    // Persist overrides/ignoredItemIds alongside the
                    // result so a restored session's decision pills start
                    // from the same baseline, not empty (see
                    // PersistResolvedPlanInBackground).
                    PersistResolvedPlanInBackground(result, overrides, ignoredItemIds);

                    // Off the main thread: this writes the history index,
                    // and the caller is a pill Click chain.
                    int overrideCount = overrides?.Count ?? 0;
                    int ignoredCount = ignoredItemIds?.Count ?? 0;
                    Task.Run(() => MarkHistoryResolvedWithOverrides(overrideCount, ignoredCount));
                    return result;
                },
                itemMetadataService.GetCachedStatBlock,
                // Q13: a restored plan fetches its items' stat blocks in
                // the background so its rows can show item tooltips at
                // all. Fills only the session stat side table - see
                // ItemMetadataService.WarmStatBlocksAsync for why it is
                // not GetMetadataAsync.
                _warmItemStatsAsync,
                () => lifetimeToken,
                () => _currentSnapshot
            );

            _settingsContent = new SettingsTabContent(
                _settings,
                // The valuation grid's barter rows are items and hover like
                // items. WarmBarterItemMetadataForSettings below fills this
                // same service's stat side table on the /v2/items fetch it
                // already makes for their icons, so those hovers need no
                // ItemStatWarmer of their own.
                itemMetadataService.GetCachedStatBlock,
                _modalDialog);
            WarmCurrencyMetadataForSettings(lifetimeToken);
            WarmBarterItemMetadataForSettings(itemMetadataService, lifetimeToken);

            // dataDir is threaded in as a parameter and _moduleIconTexture
            // is already loaded (LoadTextures runs first) - trivial
            // plumbing, no new fields needed on Module itself beyond the
            // view instance.
            _aboutContent = new AboutTabContent(this.ModuleParameters, dataDir, _moduleIconTexture);
        }

        /// <summary>
        /// Resolves the currency name/icon list once in the background and
        /// hands it to the Settings tab, whose Currency Valuations rows draw
        /// a currency icon per row.
        /// <para>
        /// Background and never awaited: a fetch on the UI thread would
        /// stall the frame the tab is built in. The service caches the whole
        /// list for the session, so this warms the same cache the first plan
        /// generation would otherwise pay for; a failure costs the icons and
        /// nothing else (the rows render name-and-value without them), and
        /// leaves the cache empty so the next plan retries.
        /// </para>
        /// </summary>
        private void WarmCurrencyMetadataForSettings(CancellationToken lifetimeToken)
        {
            var service = _currencyMetadataService;
            var settingsContent = _settingsContent;
            if (service == null || settingsContent == null)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var metadata = await service.GetAllAsync(lifetimeToken);
                    if (metadata == null || metadata.Count == 0)
                    {
                        return;
                    }

                    MainThreadMarshal.Run(() => settingsContent.SetCurrencyMetadata(metadata));
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException)
                {
                    // Unloaded mid-fetch: _lifetimeCts is cancelled and
                    // _httpClient disposed before this task can finish.
                }
            });
        }

        /// <summary>
        /// The barter-item half of <see cref="WarmCurrencyMetadataForSettings"/>:
        /// the Vendor Cost Valuations grid's item rows carry an icon on the
        /// same terms its currency rows do, and this resolves them through
        /// the module's ONE <see cref="ItemMetadataService"/> - the same
        /// cache every plan generation reads - rather than a second fetch of
        /// its own. Roughly two dozen ids, so one batched /v2/items request.
        /// <para>
        /// Background and never awaited, for the reason the currency warm
        /// gives. Failure costs the item icons and nothing else; unlike
        /// CurrencyMetadataService, GetMetadataAsync rethrows when every
        /// batch of a wave failed, so a total outage arrives here as an
        /// ordinary exception and is swallowed here rather than left
        /// unobserved on a Task nobody awaits.
        /// </para>
        /// </summary>
        private void WarmBarterItemMetadataForSettings(
            ItemMetadataService service, CancellationToken lifetimeToken)
        {
            var settingsContent = _settingsContent;
            var ids = SettingsTabContent.BarterItemIconIds;
            if (service == null || settingsContent == null || ids.Count == 0)
            {
                return;
            }

            Task.Run(async () =>
            {
                try
                {
                    var metadata = await service.GetMetadataAsync(ids, lifetimeToken);
                    if (metadata == null || metadata.Count == 0)
                    {
                        return;
                    }

                    MainThreadMarshal.Run(() => settingsContent.SetBarterItemMetadata(metadata));
                }
                catch (Exception)
                {
                    // Cancellation, a disposed HttpClient on unload, and a
                    // /v2/items outage all land here and all mean the same
                    // thing to the grid: the item rows keep the empty slot
                    // the cell already reserves.
                }
            });
        }

        /// <summary>
        /// Constructs the module window itself. Sizing rationale is in
        /// WindowSizing; the texture-space regions below are explained
        /// inline.
        /// </summary>
        private void BuildWindow()
        {
            // SpriteScreen is the GW2 CLIENT area, not the monitor, so a
            // windowed player can legitimately be narrower than the minimum.
            // Enforcing the full minimum there would put the window's right
            // edge - cost column, Generate button, and the bottom-right
            // resize grip - off-screen with no way to drag it back, so on
            // such a client the enforced minimum falls back to the client's
            // own width and deep rows ellipsize as they used to.
            int minWindowWidth = WindowSizing.EffectiveMinWindowWidth(
                GameService.Graphics.SpriteScreen.Width);

            // The window/content regions below stay at the 930x710 pair the
            // 1024x1024 background texture (502049) was authored against -
            // they are texture-space regions, and Blish grows the content
            // region by the same delta it grows the window by, so the 46px
            // horizontal chrome they encode holds at every size. Only the
            // minimum (WindowSizing) moved; the window opens at it because
            // ResizableTabbedWindow clamps the constructed size up, on the
            // same paths that clamp a drag and a size persisted by an
            // earlier session.
            // Validated in-game to align with Event Table / Blish HUD's own
            // TabbedWindow dimensions.
            // The vertical terms of both rectangles live in WindowSizing,
            // which owns the bottom margin they leave Blish and the panel
            // height that falls out of it; the horizontal ones stay here,
            // accounted for by WindowSizing.WindowToTabPanelChrome.
            _mainWindow = new ResizableTabbedWindow(
                AsyncTexture2D.FromAssetId(502049),
                new Rectangle(
                    35, WindowSizing.WindowRegionTop, 930, WindowSizing.WindowRegionHeight),
                new Rectangle(
                    81,
                    WindowSizing.WindowContentRegionTop,
                    884,
                    WindowSizing.WindowContentRegionHeight),
                new Point(WindowSizing.MinWindowWidth, WindowSizing.MinWindowHeight))
            {
                Parent = GameService.Graphics.SpriteScreen,
                Title = "Taimi's Toolbench",
                Emblem = new AsyncTexture2D(_emblemTexture),
                Id = $"{nameof(Module)}_MainWindow",

                // Clamped at 0: on a client narrower than even the 930
                // fallback a negative centered x would put the title bar
                // (and its close button) off the left edge with no way to
                // drag it back.
                Location = new Point(
                    Math.Max(0, (GameService.Graphics.SpriteScreen.Width - minWindowWidth) / 2),
                    Math.Max(0, (GameService.Graphics.SpriteScreen.Height - WindowSizing.MinWindowHeight) / 2)),
                SavesPosition = true,
            };
        }

        /// <summary>
        /// Adds the window's tabs in strip order. Tab order is the user-
        /// visible contract; the DEBUG-gated pair is dev-only.
        /// </summary>
        private void BuildTabs()
        {
            // Crafting Plan first, and Blish opens on the first tab. It is
            // the one tab that works with no API key at all - recipes,
            // prices and vendor offers are public data - whereas Snapshot
            // can only say "No snapshot available. Click Refresh Now.",
            // which is not an instruction a key-less user can carry out.
            // Nothing reads a tab by index; the two tabs held as fields
            // (_logTab, _settingsTab) are compared by reference.
            _craftingPlanTab = new Tab(
                AsyncTexture2D.FromAssetId(156711),
                () => new ViewAdapter("Crafting Plan", c => _craftingContent.Build(c)),
                "Crafting Plan");
            _mainWindow.Tabs.Add(_craftingPlanTab);

            _planHistoryContent = new PlanHistoryTabContent(
                SnapshotHistoryEntries,
                MutateHistoryIndex,
                OpenHistoryEntry,
                ResolveHistoryEntryAsync,
                _modalDialog,
                _settings,
                _getItemStatBlock,
                // The same top-up the Crafting Plan tab gets (Q13). Without
                // it every history hover degrades to the identity-only
                // form, because the accessor above never fetches.
                _warmItemStatsAsync);

            _planHistoryTab = new Tab(
                AsyncTexture2D.FromAssetId(156691),
                () =>
                {
                    _planHistoryContent.BeginRebuild();
                    return new ViewAdapter("Plan History", c => _planHistoryContent.Build(c));
                },
                "Plan History");
            _mainWindow.Tabs.Add(_planHistoryTab);

            _snapshotTab = new Tab(
                AsyncTexture2D.FromAssetId(156699),
                () => new ViewAdapter(
                    "Account Snapshot",
                    c => _snapshotContent.Build(c),
                    b => _snapshotContent.BuildHeaderActions(b)),
                "Account Snapshot");
            _mainWindow.Tabs.Add(_snapshotTab);

            _rankerContent = new RankerTabContent(
                _craftingPipeline,
                _itemSearchProvider,
                _settings,
                _rankerStore,
                () => _currentSnapshot,
                TryGetActiveCharacterName,
                _getItemStatBlock,
                _warmItemStatsAsync);

            _rankerTab = new Tab(
                AsyncTexture2D.FromAssetId(156686),
                () =>
                {
                    _rankerContent.BeginRebuild();
                    return new ViewAdapter("Crafting Ranker", c => _rankerContent.Build(c));
                },
                "Crafting Ranker");
            _mainWindow.Tabs.Add(_rankerTab);

            _logTab = new Tab(
                AsyncTexture2D.FromAssetId(156701),
                () => new ViewAdapter("Log", c =>
                {
                    _logContent = new LogTabContent(
                        ModuleLog.Shared,
                        _modalDialog,
                        () => _logViewClearedBeforeVersion,
                        v => _logViewClearedBeforeVersion = v);
                    _logContent.Build(c);
                }),
                "Log");
            _mainWindow.Tabs.Add(_logTab);

            _settingsTab = new Tab(
                AsyncTexture2D.FromAssetId(156736),
                () =>
                {
                    // Main thread, and the last step before Blish queues the
                    // off-thread Build - which is the whole point of doing it
                    // here rather than in Build (see BeginRebuild).
                    _settingsContent.BeginRebuild();
                    return new ViewAdapter("Settings", c => _settingsContent.Build(c));
                },
                "Settings");
            _mainWindow.Tabs.Add(_settingsTab);

            _mainWindow.Tabs.Add(new Tab(
                AsyncTexture2D.FromAssetId(157097),
                () =>
                {
                    _aboutContent.BeginRebuild();
                    return new ViewAdapter("About", c => _aboutContent.Build(c));
                },
                "About"));
        }

        /// <summary>
        /// Wires the window's tab-change handler and the corner icon.
        /// Last, so every object these handlers touch already exists.
        /// </summary>
        private void WireEvents()
        {
            // Refresh log content when switching to the Log tab
            _mainWindow.TabChanged += (s, e) =>
            {
                // AFTER the switch, not before it: TabbedWindow2 exposes no
                // cancellable pre-change hook - see PromptForUnsavedSettings.
                if (e.PreviousValue == _settingsTab)
                {
                    PromptForUnsavedSettings();
                }

                if (_mainWindow.SelectedTab == _logTab && _logContent != null)
                {
                    _logContent.Refresh();
                }

                // View-only: never starts a solve on a tab switch.
                _rankerContent?.Refresh();

                if (_mainWindow.SelectedTab == _planHistoryTab)
                {
                    _planHistoryContent?.Refresh();
                }
            };

            _cornerIcon = new CornerIcon()
            {
                IconName = "Taimi's Toolbench",
                Icon = new AsyncTexture2D(_cornerIconTexture),
                Priority = 1245846523,
                Parent = GameService.Graphics.SpriteScreen,
            };

            _cornerIcon.Click += (s, e) =>
            {
                _mainWindow.ToggleWindow();
            };
        }

        /// <summary>
        /// One Generate click: reads the active character and the effective
        /// settings, starts the pipeline, and hands the task to
        /// <see cref="PersistAfterGenerateAsync"/>.
        /// </summary>
        /// <param name="lifetimeToken">
        /// Passed in rather than read from <c>_lifetimeCts</c> so the token
        /// is the one captured when the view was built - a later Initialize
        /// installs a different source, and Unload disposes this one.
        /// </param>
        private Task<CraftingPlanResult> StartGenerateAsync(
            IReadOnlyList<PlanRequestItem> items,
            bool useOwn,
            bool valueOwnMaterials,
            PriceBasis priceBasis,
            CancellationToken ct,
            IProgress<PlanStatus> progress,
            IProgress<PlanPhaseEvent> phaseProgress,
            string requestLabel,
            CancellationToken lifetimeToken)
        {
            // The corpus probe retries here when its startup run failed
            // (offline launch): fire-and-forget, a no-op when already
            // verified, never awaited by the generation itself.
            KickCorpusVerification();

            string activeChar = null;
            try
            {
                var mumble = GameService.Gw2Mumble;
                if (mumble != null &&
                    mumble.PlayerCharacter != null &&
                    !string.IsNullOrEmpty(mumble.PlayerCharacter.Name))
                {
                    activeChar = mumble.PlayerCharacter.Name;
                }
            }
            catch (Exception ex)
            {
                // Gw2Mumble unavailable - graceful fallback. Debug,
                // not Warn: this runs once per Generate click (a
                // human-paced action, not a hot loop), but a user
                // running Blish without Mumble wired up would hit
                // it every single click - Debug (ring-always,
                // file-only-when-diagnostics-on) keeps that from
                // becoming routine file noise for a purely
                // cosmetic fallback (active-character is only used
                // for account-bound recipe checks).
                ModuleLog.Shared.Write(ModuleLogLevel.Debug, "plan", $"Gw2Mumble unavailable, active character unknown: {ex.GetType().Name} - {ex.Message}");
            }

            // The EFFECTIVE
            // valuation (user overrides + CurrencyDecisionDefaults'
            // curated defaults, minus anything explicitly cleared -
            // see ModuleSettings.GetEffectiveCurrencyValuation's own
            // doc comment) - not the raw GetCurrencyValuation the
            // Settings tab itself reads, which must stay default-
            // free so it can tell a real user override apart from
            // an applied default.
            var currencyValuation = _settings.GetEffectiveCurrencyValuation();
            // The per-plan valueOwnMaterials parameter drives this
            // directly, matching how priceBasis/useOwn are also
            // per-plan rather than read from ModuleSettings.
            var ownMaterialsMode = valueOwnMaterials
                ? OwnMaterialsMode.Valued
                : OwnMaterialsMode.Free;
            var homesteadTiers = _settings.GetHomesteadEfficiencyTiers();

            // characterDisciplines is passed explicitly so the
            // useOwn:false branch (snapshot: null, disabling
            // reduction) still feeds the discipline tiebreak the
            // same list useOwn:true does - the reported discipline
            // must not change with the toggle.
            // PersistAfterGenerateAsync awaits the pipeline call
            // and saves on success only; a cancelled/failed
            // generation propagates unchanged. myPersistGen is
            // stamped here, synchronously, before generateTask is
            // created - in lockstep with the view's myGen bump.
            int myPersistGen = ++_persistGenerateSequence;

            // The view already passes the lifetime token, but the
            // parameter is public surface: linking here means a
            // future caller cannot start an untethered generation by
            // passing None, and costs one source per Generate click.
            var generateCts = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetimeToken);
            ct = generateCts.Token;

            Task<CraftingPlanResult> generateTask = useOwn
                ? _craftingPipeline.GenerateStructuredAsync(
                    items, _currentSnapshot, ct, progress,
                    activeChar, priceBasis, currencyValuation, ownMaterialsMode,
                    homesteadTiers, phaseProgress, requestLabel,
                    characterDisciplines: _currentSnapshot?.CharacterDisciplines)
                : _craftingPipeline.GenerateStructuredAsync(
                    items, null, ct, progress,
                    null, priceBasis, currencyValuation, ownMaterialsMode,
                    homesteadTiers, phaseProgress, requestLabel,
                    characterDisciplines: _currentSnapshot?.CharacterDisciplines);

            return PersistAfterGenerateAsync(generateTask, items, useOwn, priceBasis, valueOwnMaterials, myPersistGen, ct, generateCts);
        }

        /// <summary>
        /// Best-effort active character name. Debug, not Warn: a user running
        /// Blish without Mumble wired up would hit this on every plan
        /// generation and every ranker refresh, and the fallback is purely
        /// cosmetic (active-character is only used for account-bound recipe
        /// checks).
        /// </summary>
        private string TryGetActiveCharacterName()
        {
            try
            {
                var mumble = GameService.Gw2Mumble;
                if (mumble != null &&
                    mumble.PlayerCharacter != null &&
                    !string.IsNullOrEmpty(mumble.PlayerCharacter.Name))
                {
                    return mumble.PlayerCharacter.Name;
                }
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Debug, "plan", $"Gw2Mumble unavailable, active character unknown: {ex.GetType().Name} - {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Asks whether to keep or drop unsaved Settings edits, once the
        /// user has already left the tab. Blish 1.3.0 raises TabChanged
        /// only after the switch has happened and offers nothing to veto
        /// it, so there is no earlier point to ask from (KNOWN-ISSUES #51).
        ///
        /// The outgoing view's controls are unparented, not disposed, so
        /// the Settings TextBoxes still hold the user's typed text when
        /// this runs and Save persists exactly what was on screen.
        ///
        /// Only the tab path is hooked; the window's own Hidden event
        /// deliberately is NOT, because Blish fires it on entering combat
        /// and on loading screens, which would pop a modal over gameplay.
        /// The measured call chains behind all three: docs/ARCHITECTURE.md
        /// section 1.
        /// </summary>
        private void PromptForUnsavedSettings()
        {
            if (_settingsContent == null || _modalDialog == null)
            {
                return;
            }

            int unsaved = _settingsContent.UnsavedChangeCount();
            if (unsaved <= 0)
            {
                return;
            }

            string changeWord = unsaved == 1 ? "change" : "changes";
            _modalDialog.Show(
                $"You have {unsaved} unsaved {changeWord} on the Settings tab. Save now, or discard and keep the last saved values?",
                () => ReportSaveOutcome(_settingsContent.SaveAll()),
                () => _settingsContent.DiscardChanges(),
                "Save",
                "Discard");
        }

        // The tab's own save bar reports the outcome of a Save, but saving
        // from the prompt above happens after the view was torn down - its
        // status label is unparented by then and renders nowhere, so a
        // rejected entry would vanish with no explanation at all. Raising a
        // second dialog from the first one's callback is supported:
        // ModalDialog.Dismiss clears its pending state before running the
        // callback and skips its own Hide when the callback re-armed it, so
        // this message lands in the window that is already on screen.
        private void ReportSaveOutcome(SettingsTabContent.SaveOutcome outcome)
        {
            if (outcome.AllSaved)
            {
                return;
            }

            string message;
            if (outcome.WriteFailed)
            {
                message = "Your Settings changes could not be saved - the module log has the details. Open the Settings tab to try again?";
            }
            else
            {
                string entryWord = outcome.InvalidCount == 1 ? "entry" : "entries";
                string subject = outcome.InvalidCount == 1 ? "it" : "them";
                message = $"{outcome.InvalidCount} Settings {entryWord} could not be saved - the value was not a valid number. Everything else was saved. Open the Settings tab to re-enter {subject}?";
            }

            _modalDialog.Show(
                message,
                () => _mainWindow.SelectedTab = _settingsTab,
                null,
                "Open Settings",
                "Dismiss");
        }

        // Reads Blish's FestivalContext and projects it to plain strings,
        // invoked lazily at plan-generation time - an eager
        // Initialize()-time read could observe NotReady and silently
        // disable the feature for the session. Non-Available outcomes log
        // Info so "disabled by <availability>" is distinguishable from
        // "no festival active" (which logs nothing). This method is the
        // one place in the pipeline's call chain that touches
        // GameService.Contexts - everything below it stays Blish-free.
        private IReadOnlyList<string> ReadActiveFestivalNames()
        {
            var activeFestivalNames = new List<string>();
            try
            {
                var festivalContext = GameService.Contexts.GetContext<Blish_HUD.Contexts.FestivalContext>();
                if (festivalContext == null)
                {
                    ModuleLog.Shared.Write(ModuleLogLevel.Info, "plan", "Festival context not registered - seasonal vendor tips disabled for this plan.");
                    return activeFestivalNames;
                }

                var availability = festivalContext.TryGetActiveFestivals(out var festivalResult);
                if (availability != Blish_HUD.Contexts.ContextAvailability.Available)
                {
                    ModuleLog.Shared.Write(ModuleLogLevel.Info, "plan", $"Festival context not available ({availability}) - seasonal vendor tips disabled for this plan.");
                    return activeFestivalNames;
                }

                if (festivalResult.Value != null)
                {
                    foreach (var festival in festivalResult.Value)
                    {
                        if (!string.IsNullOrEmpty(festival.Name))
                        {
                            activeFestivalNames.Add(festival.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "plan", $"Festival context unavailable, seasonal vendor tips disabled: {ex.GetType().Name} - {ex.Message}");
            }

            return activeFestivalNames;
        }

        /// <summary>
        /// Shared shape for the static seed loads in Initialize(): open the
        /// packaged file, hand the stream to the loader, and on ANY failure
        /// log at Warn and return null so the module starts without that
        /// seed. The broad catch is deliberate - a bad or missing seed file
        /// must never block module load.
        /// </summary>
        private T LoadSeedOrNull<T>(string fileName, string unavailableLabel, Func<System.IO.Stream, T> load)
            where T : class
        {
            try
            {
                using (var stream = ContentsManager.GetFileStream(fileName))
                {
                    return load(stream);
                }
            }
            catch (Exception ex)
            {
                Logger.Info("{0}: [{1}] {2}", unavailableLabel, ex.GetType().Name, ex.Message);
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "startup", $"{unavailableLabel}: [{ex.GetType().Name}] {ex.Message}");
                return null;
            }
        }

        protected override async Task LoadAsync()
        {
            // A disk-restored snapshot routes through the same drain and
            // commit gate as a network refresh, so a Clear Cache racing
            // this load composes exactly like it does against a fetch.
            int loadEpoch = _snapshotCommitGate.Epoch;
            var loadedSnapshot = _snapshotStore.LoadLatest();

            if (loadedSnapshot != null)
            {
                _snapshotCommitGate.TryCommit(loadEpoch, () =>
                {
                    _currentSnapshot = loadedSnapshot;
                    _pendingSnapshot = loadedSnapshot;
                    _snapshotDirty = true;
                });
            }

            // Same dirty-flag drain shape as the snapshot restore above -
            // _craftingContent (built in Initialize() with no plan at all)
            // is only ever pushed to from Update(), never touched directly
            // here (see Update()'s own "Applying restored plan to view"
            // block). A missing file is silent (LoadLatest returns null,
            // nothing to restore); an unreadable RESULT already logged
            // inside PlanStore.LoadLatest (Warn if corrupt, Info if merely
            // written by another schema version) and comes back as a
            // request-only load, which restores the user's items without a
            // plan - never a crash, and never a lost request.
            var loadedPlan = _planStore.LoadLatest();
            if (loadedPlan != null)
            {
                _pendingPlanRestore = loadedPlan;
                _planRestoreDirty = true;
            }

            // Plan History index: a single small synchronous read, held
            // in memory for the session. A missing file is the ordinary
            // first run; a corrupt one already logged its one Warn inside
            // the store and came back empty. The orphan sweep then drops
            // any blob no surviving row links to (e.g. rows lost to a
            // corrupt index).
            var loadedHistory = _planHistoryStore.Load();
            var keepIds = new List<string>();
            lock (_planHistoryLock)
            {
                _planHistoryIndex = loadedHistory;
                foreach (var entry in loadedHistory.Entries)
                {
                    if (entry?.EntryId != null)
                    {
                        keepIds.Add(entry.EntryId);
                    }
                }
            }

            _planHistoryBlobStore.DeleteOrphans(keepIds);

            Gw2ApiManager.SubtokenUpdated += OnSubtokenUpdated;

            if (_snapshotService.HasRequiredPermissions())
            {
                await RefreshSnapshotInBackgroundAsync();
            }
        }

        protected override void Update(GameTime gameTime)
        {
            bool statusApplied = false;

            // Not folded into the _snapshotDirty drain below: Clear Cache
            // drops the snapshot without setting that flag, and the plan
            // tab must stop offering "Use Own Materials" the moment there
            // is nothing to subtract. A reference read plus a bool compare
            // per tick; the view early-returns when nothing changed.
            _craftingContent?.SetAccountDataAvailable(_currentSnapshot != null);

            if (_snapshotDirty)
            {
                Logger.Info("Applying snapshot to view CapturedAt={0:o}", _pendingSnapshot?.CapturedAt);
                _snapshotDirty = false;
                _snapshotContent?.SetSnapshot(_pendingSnapshot);
                _snapshotContent?.SetStatus(_lastStatus);
                statusApplied = true;

                // Here rather than where the snapshot is committed: this is
                // the frame thread, and it only runs once the module is
                // loaded and the snapshot is the one the tab will draw.
                int primeUrls = _iconPrimeHandedOver ? 0 : _iconPrime.Enqueue(_pendingSnapshot);
                if (primeUrls > 0)
                {
                    ModuleLog.Shared.Write(
                        ModuleLogLevel.Debug, "ui",
                        $"Snapshot icon prime: {primeUrls} new pictures over about "
                        + $"{SnapshotIconWindow.PrimeFrames(primeUrls, SnapshotIconWindow.BackgroundPrimePerFrame)} frames.");
                }
            }

            // Status updates saved from a ThreadPool continuation (Blish's
            // XNA host has no SynchronizationContext) land here instead of
            // touching the view directly - see SaveStatusThreadSafe. Skipped
            // when the snapshot branch above already applied _lastStatus
            // this tick (both flags can be set together, e.g. a background
            // refresh updates both), so SetStatus runs at most once per tick.
            if (_statusDirty)
            {
                _statusDirty = false;
                if (!statusApplied)
                {
                    _snapshotContent?.SetStatus(_lastStatus);
                }
            }

            PrimeSnapshotIcons();

            // The Log tab's own poll, run
            // only while it is the selected tab - a cheap Version compare
            // when nothing changed, not a full rebuild every frame. This is
            // the "PLUS a poll" half of the refresh design; TabChanged
            // above already covers "just switched to this tab".
            if (_logContent != null && _mainWindow?.SelectedTab == _logTab)
            {
                _logContent.PollForUpdates();
            }

            // The Ranker's own poll, on the same terms and for the same
            // reason - a nullable-DateTime compare when nothing changed.
            // Gated on a VISIBLE window as well as the selected tab,
            // unlike the Log's: what this poll can start is a rebuild of
            // every row, and it runs where the user can watch it happen or
            // not at all (Views/RankerTabContent.PollForSnapshotChange).
            if (_rankerContent != null
                && _mainWindow != null
                && _mainWindow.Visible
                && _mainWindow.SelectedTab == _rankerTab)
            {
                _rankerContent.PollForSnapshotChange();
            }

            // "Applying restored plan to view" - mirrors the
            // _snapshotDirty block above. Runs at most once per session
            // and must stay ahead of the early returns below (a fresh
            // account with no snapshot must still restore its plan).
            // Guarded by !_generateCompletedThisSession under
            // _generateCompletionLock - see those fields' comments; only
            // the cheap check/write is inside the lock, the rendering
            // work runs outside it.
            if (_planRestoreDirty)
            {
                _planRestoreDirty = false;
                if (_pendingPlanRestore != null)
                {
                    bool shouldApplyRestore;
                    lock (_generateCompletionLock)
                    {
                        shouldApplyRestore = !_generateCompletedThisSession;
                        if (shouldApplyRestore)
                        {
                            // Only a restored RESULT publishes this: it is
                            // what a later override re-solve persists
                            // against, and there is no result to re-solve
                            // when only the request came back.
                            if (_pendingPlanRestore.HasResult)
                            {
                                _lastPersistedPlanMetadata = new PersistedPlanMetadata(
                                    _pendingPlanRestore.Plan.GeneratedAt,
                                    _pendingPlanRestore.Plan.RequestItems,
                                    _pendingPlanRestore.Plan.UseOwnMaterials,
                                    _pendingPlanRestore.Plan.PriceBasis,
                                    _pendingPlanRestore.Plan.ValueOwnMaterials);
                            }
                        }
                    }

                    if (shouldApplyRestore)
                    {
                        var restored = _pendingPlanRestore.Plan;
                        if (_pendingPlanRestore.HasResult)
                        {
                            _craftingContent?.ApplyRestoredPlan(
                                restored.Result,
                                restored.GeneratedAt,
                                restored.NodeOverrides,
                                restored.IgnoredItemIds,
                                restored.ValueOwnMaterials,
                                restored.RequestItems,
                                restored.UseOwnMaterials,
                                restored.PriceBasis);
                        }
                        else
                        {
                            _craftingContent?.ApplyRestoredRequest(
                                restored.GeneratedAt,
                                restored.RequestItems,
                                restored.UseOwnMaterials,
                                restored.PriceBasis,
                                restored.ValueOwnMaterials);
                        }
                    }
                }
            }

            // The auto-refresh spinner, drained on change. Above the
            // early return below on purpose: the refresh slot is claimed
            // for the whole of the refresh whose spinner this is, so a drain
            // underneath it would only ever get to switch the spinner ON
            // once the refresh had already finished.
            bool backgroundRefreshing = _backgroundRefreshInFlight;
            if (backgroundRefreshing != _backgroundRefreshSpinnerApplied && _snapshotContent != null)
            {
                _backgroundRefreshSpinnerApplied = backgroundRefreshing;
                _snapshotContent.SetBackgroundRefreshInFlight(backgroundRefreshing);
            }

            if (_refreshSlot.IsClaimed)
            {
                return;
            }

            if (_currentSnapshot == null)
            {
                // Nothing cached at all: the staleness tick below has no
                // snapshot to age, so this is the only automatic route to
                // a first one - see FirstLoadSnapshotGate. This branch is
                // reached every frame for as long as nothing is cached
                // (forever, with no API key configured), so both guards
                // ahead of the gate are load-bearing: the spent-shot flag
                // silences it once the shot has been used, and the
                // interval throttle silences it while the shot is still
                // armed - without which the gate's arguments would take a
                // live permission probe (an enumerator allocation) and a
                // UtcNow read per frame, on the UI thread.
                if (!_firstLoadRefreshAttempted
                    && FirstLoadSnapshotGate.ShouldCheckNow(
                        _sinceFirstLoadGateCheck,
                        gameTime.ElapsedGameTime,
                        FirstLoadGateCheckInterval,
                        out _sinceFirstLoadGateCheck)
                    && FirstLoadSnapshotGate.ShouldRefreshNow(
                        hasCachedSnapshot: false,
                        apiReady: _snapshotService.HasRequiredPermissions(),
                        alreadyAttempted: _firstLoadRefreshAttempted,
                        refreshInProgress: false,
                        inFailureBackoff: IsInRefreshFailureBackoff()))
                {
                    _firstLoadRefreshAttempted = true;
                    _ = RefreshSnapshotInBackgroundAsync();
                }

                return;
            }

            // Reads the
            // clamped setting fresh on every tick (cheap - a single
            // SettingEntry read plus two int comparisons, no I/O) rather
            // than caching it, so a Settings tab save takes effect on the
            // very next Update() without any separate live-push plumbing.
            var staleThreshold = TimeSpan.FromMinutes(_settings.GetClampedSnapshotRefreshIntervalMinutes());
            if (!StatusText.IsStale(DateTime.UtcNow - _currentSnapshot.CapturedAt, staleThreshold))
            {
                return;
            }

            if (!_snapshotService.HasRequiredPermissions())
            {
                return;
            }

            _ = RefreshSnapshotInBackgroundAsync();
        }

        /// <summary>
        /// Asks Blish for a few of the Snapshot tab's pictures, so the tab
        /// is warm the first time it is opened. One frame's worth per call,
        /// from Update.
        /// <para>
        /// It hands over for good the moment that tab is opened. The tab
        /// runs its own prime over the cells it built, four times faster and
        /// in the order the reader scrolls; stopping rather than pausing is
        /// what stops the two from asking for the same picture.
        /// </para>
        /// </summary>
        private void PrimeSnapshotIcons()
        {
            if (_iconPrimeHandedOver)
            {
                return;
            }

            if (_snapshotTab != null && _mainWindow?.SelectedTab == _snapshotTab)
            {
                _iconPrimeHandedOver = true;
                return;
            }

            _iconPrimeBatch.Clear();
            _iconPrime.Take(SnapshotIconWindow.BackgroundPrimePerFrame, _iconPrimeBatch);
            if (_iconPrimeBatch.Count > 0)
            {
                IconAssetConnectionLimit.Apply();
            }

            for (int i = 0; i < _iconPrimeBatch.Count; i++)
            {
                try
                {
                    GameService.Content.GetRenderServiceTexture(_iconPrimeBatch[i]);
                }
                catch (Exception ex)
                {
                    // GetRenderServiceTexture throws on a url carrying no
                    // signature/file-id pair. At most one line per url,
                    // ever: the queue hands none of them out twice.
                    ModuleLog.Shared.Write(
                        ModuleLogLevel.Debug, "ui",
                        $"Icon prime request failed: {ex.GetType().Name} - {ex.Message}");
                }
            }
        }

        protected override void Unload()
        {
            // FIRST, before any disposal below: every in-flight await this
            // module owns is running against objects the next few lines are
            // about to destroy - the HttpClient most of all.
            _lifetimeCts?.Cancel();

            // The glyph fonts hold TextureRegion2Ds over a Texture2D the
            // ContentsManager disposes with this module. UiFonts is static,
            // so a re-enable in the same process would otherwise find them
            // still pointing at it.
            UiFonts.ResetGlyphs();

            Gw2ApiManager.SubtokenUpdated -= OnSubtokenUpdated;

            // The SettingEntry objects outlive this module instance
            // (DefineSetting returns the existing entry on re-enable), so a
            // leftover handler would root each dead Module in turn.
            _settings.LogDiagnosticsEnabled.SettingChanged -= OnLogDiagnosticsEnabledChanged;
            _settings.LogMaxSizeBytes.SettingChanged -= OnLogMaxSizeBytesChanged;
            _settings.ClickSoundVolumePercent.SettingChanged -= OnClickSoundVolumeChanged;

            _refreshSlot.CancelCurrent();

            _buildIdCts.Cancel();
            _buildIdCts.Dispose();

            // RecipeService persists off the plan path, so the last plan's
            // discoveries can still be in memory when the module goes away.
            // A no-op unless something is actually unwritten.
            _recipeOverlay?.Flush(force: true);

            // The scroll-verify/resize-debounce/wheel-wrap-verify tickers
            // are parented to the SpriteScreen, not this view's control
            // tree, so nothing else tears them down on unload - this must
            // be called explicitly before disposing the host window.
            _craftingContent?.StopLiveTickers();

            // Same reasoning, same ownership: one screen-parented popup per
            // item row, each holding a global mouse subscription.
            _craftingContent?.DisposeSuggestionPanels();

            _httpClient?.Dispose();
            _modalDialog?.Dispose();
            _apiAccessDialog?.Dispose();
            _cornerIcon?.Dispose();
            _mainWindow?.Dispose();

            // The module's ONE rich tooltip surface. Like the tickers
            // above it is parented to the SpriteScreen (only while
            // visible), never to a view's control tree, so disposing the
            // window does not reach it - see Views/Rendering/
            // TooltipFacility for why there is one instance rather than
            // one per tooltip'd control.
            Views.Rendering.TooltipFacility.Shutdown();

            Views.Rendering.ClickSound.Unload();
            _rankerContent?.Teardown();
            _planHistoryContent?.Teardown();
            _settingsContent?.Teardown();
            _aboutContent?.Teardown();

            // Module-level log system (dev/proposals/d2-log-system.md Section 7): the
            // file-sink append/trim now happens on a background flush
            // queue, never on the calling thread (see ModuleLog's own class
            // doc comment) - give any writes already queued (e.g. from a
            // scrolldiag burst moments before unload) a brief, bounded
            // chance to land on disk before the ring is cleared. Best
            // effort only: Unload must never hang on a stuck flush (a
            // locked/very slow disk), so this is capped short rather than
            // waited on indefinitely.
            ModuleLog.Shared.WaitForPendingFileWrites(ModuleLog.FlushDrainBudget);

            // The in-memory ring is cleared only here (process exit / module
            // disable) - never by any in-tab user action. The on-disk file
            // is untouched (survives across sessions by design).
            ModuleLog.Shared.Clear();
        }

        private void OnClickSoundVolumeChanged(object sender, ValueChangedEventArgs<int> e)
        {
            Views.Rendering.ClickSound.VolumePercent = e.NewValue;
        }

        private void OnLogDiagnosticsEnabledChanged(object sender, ValueChangedEventArgs<bool> e)
        {
            ModuleLog.Shared.DiagnosticsEnabled = e.NewValue;
        }

        // Reads the clamped accessor, not e.NewValue: Blish's Manage Modules
        // bar spans 0 to the persisted value, so it can deliver a byte count
        // below the 1 MB floor the tab's own parser enforces.
        private void OnLogMaxSizeBytesChanged(object sender, ValueChangedEventArgs<int> e)
        {
            ModuleLog.Shared.MaxFileSizeBytes = _settings.GetClampedLogMaxSizeBytes();
        }

        private void OnSubtokenUpdated(object sender, ValueEventArgs<IEnumerable<Gw2Sharp.WebApi.V2.Models.TokenPermission>> e)
        {
            if (_snapshotService.HasRequiredPermissions())
            {
                _ = RefreshSnapshotInBackgroundAsync();
            }
        }

        private async Task<AccountSnapshot> FetchAndSaveSnapshotAsync(CancellationToken ct)
        {
            Logger.Info("Refreshing account snapshot...");
            ModuleLog.Shared.Write(ModuleLogLevel.Info, "snapshot", "Refreshing account snapshot...");

            // Captured before the fetch starts (main thread - see the
            // field's own comment) so the post-await commit below can
            // detect a Clear Cache that ran while this fetch was still in
            // flight (KNOWN-ISSUES #31/31a-F1).
            int myEpoch = _snapshotCommitGate.Epoch;

            AccountSnapshot snapshot;

            // Bounds the whole multi-step fetch so a full network outage
            // fails fast instead of stacking several ~100s HTTP timeouts
            // (KNOWN-ISSUES #31/api-degradation F6). The budget starts at
            // SnapshotFetchBudget's floor and is re-sized the moment the
            // fetch reports how many characters the account has, because
            // the per-character work is what makes a large account cost
            // more than a small one.
            var budget = SnapshotFetchBudget.For(0);
            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                timeoutCts.CancelAfter(budget);
                try
                {
                    snapshot = await _snapshotService.FetchSnapshotAsync(
                        timeoutCts.Token,
                        characterCount =>
                        {
                            budget = SnapshotFetchBudget.For(characterCount);
                            timeoutCts.CancelAfter(budget);
                        });
                }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                {
                    // The internal timeout fired, not the caller's own
                    // token - a genuine fetch failure, not a cancellation.
                    // Re-thrown as a plain Exception so callers' "cancelled"
                    // catch (which must stay silent) does not swallow it.
                    throw new TimeoutException(
                        $"Account snapshot fetch exceeded {budget.TotalSeconds:0}s.");
                }
            }

            // Re-check and commit run inside SnapshotCommitGate's lock -
            // the same lock ClearCache's own bump+clear runs under below -
            // so the two can never interleave (KNOWN-ISSUES #31/31a-F1
            // audit-of-fix; see SnapshotCommitGate's doc comment).
            bool committed = _snapshotCommitGate.TryCommit(myEpoch, () =>
            {
                _currentSnapshot = snapshot;
                _snapshotStore.Save(snapshot);

                _pendingSnapshot = snapshot;
                _snapshotDirty = true;
            });

            if (!committed)
            {
                // Clear Cache ran (fully, atomically) either before or
                // during this check; committing now would resurrect data
                // the user explicitly cleared (KNOWN-ISSUES #31/31a-F1). Drop
                // the result - _currentSnapshot, _pendingSnapshot,
                // _snapshotDirty, and the on-disk file are all left
                // untouched by this call.
                Logger.Info("Discarding snapshot fetch superseded by Clear Cache (epoch {0})", myEpoch);
                ModuleLog.Shared.Write(ModuleLogLevel.Info, "snapshot", $"Discarding snapshot fetch superseded by Clear Cache (epoch {myEpoch})");
                return null;
            }

            // Null CharacterDisciplines ("never captured") must stay
            // distinguishable in the log from an empty list ("captured,
            // nobody has any"), so the null case gets its own wording.
            string disciplinesLogText = snapshot.CharacterDisciplines != null
                ? $"{snapshot.CharacterDisciplines.Count} character disciplines"
                : "disciplines not captured";

            Logger.Info("Fetched snapshot CapturedAt={0:o} items={1} wallet={2} coin={3}, {4}",
                snapshot.CapturedAt, snapshot.Items.Count, snapshot.Wallet.Count, snapshot.CoinCopper, disciplinesLogText);
            ModuleLog.Shared.Write(ModuleLogLevel.Info, "snapshot",
                $"Fetched snapshot CapturedAt={snapshot.CapturedAt:o} items={snapshot.Items.Count} wallet={snapshot.Wallet.Count} coin={snapshot.CoinCopper}, {disciplinesLogText}");

            return snapshot;
        }

        /// <summary>
        /// True while a prior failed refresh's backoff window is still
        /// open. Read by RefreshSnapshotInBackgroundAsync itself and by
        /// Update()'s first-load gate, which must not spend its one shot
        /// on a call this window would turn away.
        /// </summary>
        private bool IsInRefreshFailureBackoff()
        {
            var lastFailedTicks = Interlocked.Read(ref _lastFailedRefreshAttemptTicks);
            return lastFailedTicks != 0 &&
                DateTime.UtcNow - new DateTime(lastFailedTicks, DateTimeKind.Utc) < RefreshFailureBackoff;
        }

        private async Task RefreshSnapshotInBackgroundAsync()
        {
            if (_refreshSlot.IsClaimed)
            {
                // Advisory pre-check, kept ahead of the backoff test so a
                // call that loses to a running refresh still does not log the
                // backoff line. TryClaim below is the real gate.
                return;
            }

            // Refuse to auto-retrigger again so
            // soon after a failed attempt - see _lastFailedRefreshAttemptTicks'
            // own doc comment. Both callers of this method (Update()'s
            // staleness tick and OnSubtokenUpdated) can otherwise re-fire
            // far faster than any real transient failure needs to be
            // retried at.
            if (IsInRefreshFailureBackoff())
            {
                Logger.Debug("Skipping snapshot refresh retry - within backoff window after a prior failure");
                return;
            }

            if (!_refreshSlot.TryClaim())
            {
                return;
            }

            // Set only past both early returns above, so a tick that
            // declines to refresh (already running, or inside the failure
            // backoff) never spins a spinner over nothing.
            _backgroundRefreshInFlight = true;

            try
            {
                var snapshot = await FetchAndSaveSnapshotAsync(_refreshSlot.BeginFetch());
                Interlocked.Exchange(ref _lastFailedRefreshAttemptTicks, 0);
                if (snapshot != null)
                {
                    var status = StatusText.Stamp("Updated", snapshot.CapturedAt.ToLocalTime());
                    SaveStatusThreadSafe(status);
                }

                // else: superseded by Clear Cache while this fetch was in
                // flight (KNOWN-ISSUES #31/31a-F1, see SnapshotEpochGuard) -
                // Clear Cache already wrote its own status; nothing further
                // to report here.
            }
            catch (OperationCanceledException)
            {
                Logger.Debug("Snapshot refresh cancelled");
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "Failed to refresh account snapshot");
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "snapshot", $"Failed to refresh account snapshot: {ex.GetType().Name} - {ex.Message}");
                Interlocked.Exchange(ref _lastFailedRefreshAttemptTicks, DateTime.UtcNow.Ticks);

                // KNOWN-ISSUES #37 follow-up: status-text parity with
                // MainView.RefreshNowAsync's own catch block - this
                // background/auto-refresh path (module load, every stale-
                // snapshot Update() tick, OnSubtokenUpdated) can hit the
                // exact same InvalidAccessTokenException root cause as the
                // manual Refresh Now button, so it must not fall back to
                // the old bare, uninformative "Refresh failed" label.
                // Deliberately does NOT pop ApiAccessDialog here - see
                // KNOWN-ISSUES #37 follow-up for why unprompted background
                // popups are a separate, deferred UX call.
                var classification = SnapshotFailureClassifier.Classify(ex);
                string cause = StatusText.ForRefreshFailure(classification.Kind, classification.FailedSourceCount, classification.TotalSourceCount);
                var status = StatusText.Stamp(cause, DateTime.Now);
                SaveStatusThreadSafe(status);
            }
            finally
            {
                _backgroundRefreshInFlight = false;
                _refreshSlot.Release();
            }
        }

        private async Task<AccountSnapshot> UserRefreshAsync()
        {
            if (!_refreshSlot.TryClaim())
            {
                return null;
            }

            try
            {
                // BeginFetch is inside the try, unlike the old inline
                // cancel/dispose/assign: a throw out of it (a registered
                // cancellation callback rethrowing out of Cancel(), say) used
                // to leave the claim set forever, and with it every later
                // refresh - automatic and clicked - silently declined for the
                // rest of the session.
                return await FetchAndSaveSnapshotAsync(_refreshSlot.BeginFetch());
            }
            finally
            {
                _refreshSlot.Release();
            }
        }

        private void ClearCache()
        {
            _refreshSlot.CancelCurrent();

            // Epoch bump + on-disk delete + field resets all run inside
            // SnapshotCommitGate's lock so a snapshot fetch already in
            // flight (which captured an epoch before this call ran) either
            // commits fully before this runs, or has its post-fetch commit
            // check fail atomically against this bump - no interleaving,
            // no torn field state (KNOWN-ISSUES #31/31a-F1 audit-of-fix; see
            // SnapshotCommitGate's own doc comment).
            _snapshotCommitGate.Clear(() =>
            {
                _snapshotStore.Delete();
                _currentSnapshot = null;
                _pendingSnapshot = null;
                _snapshotDirty = false;

                // Back to the state the one shot exists for - nothing
                // cached and no staleness tick able to fetch anything - so
                // it is re-armed here rather than leaving Clear Cache with
                // no automatic route to a snapshot until Blish restarts.
                _firstLoadRefreshAttempted = false;
                _sinceFirstLoadGateCheck = FirstLoadGateCheckInterval;

                // Recipe overlay too: the only manual route out of a bad
                // learned overlay now that build changes never wipe one
                // (staleness policy). Costs one cold rebuild; the shipped
                // seed is untouched.
                _recipeOverlay?.Clear();
                ModuleLog.Shared.Write(ModuleLogLevel.Info, "store", "Clear Cache: recipe overlay cleared; seed data retained.");
            });

            // Outside the gate's lock: restamp provenance if the live build
            // is known, and re-verify the now-seed-only corpus in the
            // background (Clear zeroed the stamp, so the probe re-arms).
            int liveBuild = Volatile.Read(ref _liveGw2BuildId);
            if (liveBuild != 0)
            {
                _recipeOverlay?.SetCurrentBuildId(liveBuild);
            }

            KickCorpusVerification();
        }

        private void PersistStatus(string status)
        {
            _lastStatus = status ?? "";
            _statusStore.Save(_lastStatus);
        }

        // Called directly from a context already known to be on the main
        // thread: MainView's Clear Cache click handler (synchronous, no
        // await). MainView's async Refresh Now handler persists via
        // SaveStatusThreadSafe instead, because its continuation may resume
        // on a ThreadPool thread and the _snapshotContent.SetStatus call
        // below is a control mutation - not safe to run off the UI thread.
        private void SaveStatus(string status)
        {
            PersistStatus(status);
            _snapshotContent?.SetStatus(_lastStatus);
        }

        // Thread-safe variant for callers that may run on a ThreadPool
        // continuation (Blish HUD's XNA host installs no
        // SynchronizationContext, so await continuations do not resume on
        // the main thread) - used by the background auto-refresh path below
        // and wired into MainView as its async Refresh Now handler's
        // persistence callback. Persists the status immediately - file I/O
        // is safe off the UI thread - but defers the control mutation to
        // Update() via the same dirty-flag polling pattern already used for
        // snapshots, rather than touching _snapshotContent here.
        private void SaveStatusThreadSafe(string status)
        {
            PersistStatus(status);
            _statusDirty = true;
        }

        /// <summary>
        /// The four values the persist paths and Update()'s restore drain
        /// must agree on atomically (see _lastPersistedPlanMetadata). A
        /// plain immutable data holder, private to Module.
        /// </summary>
        private sealed class PersistedPlanMetadata
        {
            public DateTime GeneratedAt { get; }

            public IReadOnlyList<PlanRequestItem> RequestItems { get; }

            public bool UseOwnMaterials { get; }

            public PriceBasis PriceBasis { get; }

            public bool ValueOwnMaterials { get; }

            public PersistedPlanMetadata(
                DateTime generatedAt,
                IReadOnlyList<PlanRequestItem> requestItems,
                bool useOwnMaterials,
                PriceBasis priceBasis,
                bool valueOwnMaterials)
            {
                GeneratedAt = generatedAt;
                RequestItems = requestItems;
                UseOwnMaterials = useOwnMaterials;
                PriceBasis = priceBasis;
                ValueOwnMaterials = valueOwnMaterials;
            }
        }

        /// <summary>
        /// Awaits a Generate call and, only on success, persists the full
        /// result alongside the original request and a fresh timestamp.
        /// No Task.Run needed: the post-await continuation already
        /// resumes on a ThreadPool thread (Blish's XNA host installs no
        /// SynchronizationContext). A cancelled/failed generateTask
        /// propagates unchanged and persistence never runs.
        /// <para>
        /// <paramref name="myPersistGen"/> is this call's stamp from
        /// _persistGenerateSequence - a second Generate CAN start while
        /// this await is pending, so the disk write is skipped when a
        /// newer call has since started.
        /// </para>
        /// </summary>
        private async Task<CraftingPlanResult> PersistAfterGenerateAsync(
            Task<CraftingPlanResult> generateTask,
            IReadOnlyList<PlanRequestItem> requestItems,
            bool useOwnMaterials,
            PriceBasis priceBasis,
            bool valueOwnMaterials,
            int myPersistGen,
            CancellationToken ct,
            CancellationTokenSource generateCts)
        {
            try
            {
                return await PersistOrSkipAsync(
                    generateTask, requestItems, useOwnMaterials, priceBasis, valueOwnMaterials, myPersistGen, ct);
            }
            finally
            {
                // The linked source created per Generate click, owned by this
                // continuation because it is the last thing still using it.
                generateCts?.Dispose();
            }
        }

        private async Task<CraftingPlanResult> PersistOrSkipAsync(
            Task<CraftingPlanResult> generateTask,
            IReadOnlyList<PlanRequestItem> requestItems,
            bool useOwnMaterials,
            PriceBasis priceBasis,
            bool valueOwnMaterials,
            int myPersistGen,
            CancellationToken ct)
        {
            var result = await generateTask;

            // See _generateCompletedThisSession/_generateCompletionLock's
            // own doc comments for why this is set unconditionally, under
            // the lock, before the stale-generation check below.
            lock (_generateCompletionLock)
            {
                _generateCompletedThisSession = true;
            }

            if (myPersistGen != _persistGenerateSequence)
            {
                return result;
            }

            // A generation that completed as the module was being unloaded
            // must not write plan.json: the module is gone, and the next
            // enable would restore a plan the user never saw finish.
            if (ct.IsCancellationRequested)
            {
                return result;
            }

            var generatedAt = DateTime.Now;
            var metadata = new PersistedPlanMetadata(
                generatedAt, requestItems, useOwnMaterials, priceBasis, valueOwnMaterials);
            lock (_generateCompletionLock)
            {
                _lastPersistedPlanMetadata = metadata;
            }

            // A fresh Generate always starts the override loop clean (see
            // TreeSectionController.ResetForNewPlan) - the persisted
            // overrides/ignored-item-ids for a plan that was JUST generated
            // are therefore always empty, never whatever an earlier
            // (now-superseded) plan's own overrides happened to be.
            _planStore.Save(new PersistedPlan
            {
                // Set explicitly, never via a property initializer - see
                // PersistedPlan.SchemaVersion.
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                RequestSchemaVersion = PersistedPlan.CurrentRequestSchemaVersion,
                GeneratedAt = generatedAt,
                RequestItems = requestItems,
                UseOwnMaterials = useOwnMaterials,
                PriceBasis = priceBasis,
                ValueOwnMaterials = valueOwnMaterials,
                Result = result,
                NodeOverrides = new Dictionary<int, AcquisitionSource>(),
                IgnoredItemIds = new List<int>(),
            });

            // Same thread, same superseded-generation guard: every
            // successful Generate lands (or dedup-bumps) a history row.
            // A capture failure must never fail the Generate whose result
            // is already on screen.
            try
            {
                CaptureHistoryEntry(
                    generatedAt, requestItems, useOwnMaterials, priceBasis, valueOwnMaterials, result);
            }
            catch (Exception ex)
            {
                ModuleLog.Shared.Write(ModuleLogLevel.Warn, "store",
                    $"Plan history capture failed: {ex.GetType().Name} - {ex.Message}");
            }

            return result;
        }

        // Latest-write-wins coalescing for
        // PersistResolvedPlanInBackground's disk writes: a full persist
        // is multi-hundred-KB, and rapid pill clicking must not queue one
        // write per click. Only the newest pending write is kept; a
        // superseded one is dropped before it reaches PlanStore.Save.
        private readonly object _pendingPlanSaveLock = new object();
        private PersistedPlan _pendingPlanSave;
        private bool _planSaveWorkerRunning;

        /// <summary>
        /// Persists an override-updated result "in place" - same
        /// GeneratedAt/original request as the last full Generate (or the
        /// restored plan), only Result/NodeOverrides/IgnoredItemIds
        /// updated. The caller runs on the MAIN thread (a pill Click
        /// chain), so the file write is dispatched to a background worker
        /// - no file I/O on the UI thread. Fire-and-forget: a slow or
        /// failing write never delays the click's own re-solve/render.
        /// Can race PersistAfterGenerateAsync's write; PlanStore.Save's
        /// internal lock prevents corruption and whichever write lands
        /// last wins. No-ops if nothing was ever persisted this session
        /// (a defensive guard, unreachable in practice).
        /// <para>
        /// <paramref name="overrides"/>/<paramref name="ignoredItemIds"/>
        /// are the same mutable collections TreeSectionController holds -
        /// copied here, synchronously, before this method returns, so the
        /// background write never holds a live reference a later pill
        /// click could still be mutating.
        /// </para>
        /// </summary>
        private void PersistResolvedPlanInBackground(
            CraftingPlanResult result,
            IReadOnlyDictionary<int, AcquisitionSource> overrides,
            ISet<int> ignoredItemIds)
        {
            var metadata = _lastPersistedPlanMetadata;
            if (metadata == null)
            {
                return;
            }

            // Copied via an explicit loop, not the Dictionary(IDictionary<>)
            // constructor - overrides is IReadOnlyDictionary<>, which does
            // not implement IDictionary<> (a separate interface), and this
            // project's target framework has no Dictionary constructor
            // overload accepting a bare IEnumerable<KeyValuePair<>> either.
            var overridesSnapshot = new Dictionary<int, AcquisitionSource>();
            if (overrides != null)
            {
                foreach (var kvp in overrides)
                {
                    overridesSnapshot[kvp.Key] = kvp.Value;
                }
            }

            var ignoredSnapshot = new List<int>();
            if (ignoredItemIds != null)
            {
                foreach (int itemId in ignoredItemIds)
                {
                    ignoredSnapshot.Add(itemId);
                }
            }

            var persisted = new PersistedPlan
            {
                // Set explicitly, never via a property initializer - see
                // PersistedPlan.SchemaVersion.
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                RequestSchemaVersion = PersistedPlan.CurrentRequestSchemaVersion,
                GeneratedAt = metadata.GeneratedAt,
                RequestItems = metadata.RequestItems,
                UseOwnMaterials = metadata.UseOwnMaterials,
                PriceBasis = metadata.PriceBasis,
                ValueOwnMaterials = metadata.ValueOwnMaterials,
                Result = result,
                NodeOverrides = overridesSnapshot,
                IgnoredItemIds = ignoredSnapshot,
            };

            lock (_pendingPlanSaveLock)
            {
                _pendingPlanSave = persisted;
                if (_planSaveWorkerRunning)
                {
                    return;
                }

                _planSaveWorkerRunning = true;
            }

            Task.Run(() => DrainPendingPlanSaves());
        }

        /// <summary>
        /// Background worker loop for <see cref="PersistResolvedPlanInBackground"/>'s
        /// coalescing - see <see cref="_pendingPlanSaveLock"/>'s own doc
        /// comment. Runs until no further write is pending, saving only the
        /// LATEST one queued at each pass; a write that lands while a
        /// previous one is already mid-Save is picked up by the next loop
        /// iteration instead of spawning a second concurrent worker.
        /// </summary>
        private void DrainPendingPlanSaves()
        {
            while (true)
            {
                PersistedPlan next;
                lock (_pendingPlanSaveLock)
                {
                    next = _pendingPlanSave;
                    _pendingPlanSave = null;
                    if (next == null)
                    {
                        _planSaveWorkerRunning = false;
                        return;
                    }
                }

                _planStore.Save(next);
            }
        }

        /// <summary>
        /// A copy of the index rows for the Plan History tab, taken under
        /// the lock so a capture landing mid-render cannot resize the
        /// list out from under an enumeration.
        /// </summary>
        private IReadOnlyList<PlanHistoryEntry> SnapshotHistoryEntries()
        {
            lock (_planHistoryLock)
            {
                return new List<PlanHistoryEntry>(_planHistoryIndex.Entries);
            }
        }

        /// <summary>
        /// Runs one tab-side index mutation (pin, delete, clear) under
        /// the lock, deletes the blob of every row the mutation removed
        /// (diffed by entry id, so the tab never has to know about the
        /// blob store), and persists the index before returning.
        /// </summary>
        private void MutateHistoryIndex(Action<PlanHistoryIndex> mutation)
        {
            if (mutation == null)
            {
                return;
            }

            lock (_planHistoryLock)
            {
                var index = _planHistoryIndex;
                var removedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in index.Entries)
                {
                    if (entry?.EntryId != null)
                    {
                        removedIds.Add(entry.EntryId);
                    }
                }

                mutation(index);

                foreach (var entry in index.Entries)
                {
                    if (entry?.EntryId != null)
                    {
                        removedIds.Remove(entry.EntryId);
                    }
                }

                foreach (string removedId in removedIds)
                {
                    _planHistoryBlobStore.Delete(removedId);
                }

                _planHistoryStore.Save(index);
            }
        }

        /// <summary>
        /// Marks the history row for the plan the tab is showing, after a
        /// decision pill re-solved it. See
        /// PlanHistoryIndexEdits.MarkOverrides for what the mark says and
        /// why the row's Cost is left alone.
        /// </summary>
        private void MarkHistoryResolvedWithOverrides(int overrideCount, int ignoredCount)
        {
            var metadata = _lastPersistedPlanMetadata;
            if (metadata == null || _planHistoryStore == null)
            {
                return;
            }

            string key = PlanHistoryDedupKey.Compute(
                metadata.RequestItems,
                metadata.UseOwnMaterials,
                metadata.PriceBasis,
                metadata.ValueOwnMaterials,
                null);

            lock (_planHistoryLock)
            {
                var index = _planHistoryIndex;
                var entry = PlanHistoryIndexEdits.FindByDedupKey(index, key);
                if (PlanHistoryIndexEdits.MarkOverrides(entry, overrideCount, ignoredCount))
                {
                    _planHistoryStore.Save(index);
                }
            }
        }

        private PlanHistoryEntry FindHistoryEntry(PlanHistoryIndex index, string entryId)
        {
            foreach (var entry in index.Entries)
            {
                if (entry != null && string.Equals(entry.EntryId, entryId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>
        /// Captures (or dedup-bumps) a history row for a successful
        /// Generate. Runs on the persist continuation's ThreadPool
        /// thread, inside the same superseded-generation guard as the
        /// plan.json write, so a superseded generation never lands a row.
        /// Retention runs here, before the Save, never lazily at render.
        /// </summary>
        private void CaptureHistoryEntry(
            DateTime generatedAt,
            IReadOnlyList<PlanRequestItem> requestItems,
            bool useOwnMaterials,
            PriceBasis priceBasis,
            bool valueOwnMaterials,
            CraftingPlanResult result)
        {
            if (result?.Plan == null || _planHistoryStore == null || _planHistoryBlobStore == null)
            {
                return;
            }

            // A fresh Generate always starts with no overrides and no
            // ignores (TreeSectionController.ResetForNewPlan), so the
            // entry's ignore set is empty by construction here.
            string key = PlanHistoryDedupKey.Compute(
                requestItems, useOwnMaterials, priceBasis, valueOwnMaterials, null);

            lock (_planHistoryLock)
            {
                var index = _planHistoryIndex;

                var entry = PlanHistoryIndexEdits.FindByDedupKey(index, key);
                if (entry == null)
                {
                    entry = new PlanHistoryEntry
                    {
                        EntryId = Guid.NewGuid().ToString("N"),
                        CreatedAtUtc = DateTime.UtcNow,
                    };
                    index.Entries.Add(entry);
                }

                entry.LastGeneratedAtUtc = DateTime.UtcNow;
                entry.RequestItems = CopyRequestItems(requestItems);
                entry.UseOwnMaterials = useOwnMaterials;
                entry.PriceBasis = priceBasis;
                entry.ValueOwnMaterials = valueOwnMaterials;
                entry.IgnoredItemIds = new List<int>();
                entry.ItemSummaries = BuildItemSummaries(requestItems, result);
                entry.TotalCoinCostAtGeneration = result.Plan.TotalCoinCost;
                entry.OverrideCountAtGeneration = 0;
                entry.IgnoredCountAtGeneration = 0;

                var samples = entry.CostSamples != null
                    ? new List<PlanHistorySample>(entry.CostSamples)
                    : new List<PlanHistorySample>();
                samples.Add(new PlanHistorySample
                {
                    TimestampUtc = DateTime.UtcNow,
                    TotalCoinCost = result.Plan.TotalCoinCost,
                });
                while (samples.Count > PlanHistoryRetention.MaxCostSamples)
                {
                    samples.RemoveAt(0);
                }

                entry.CostSamples = samples;

                bool blobSaved = _planHistoryBlobStore.Save(entry.EntryId, new PersistedPlan
                {
                    // Set explicitly, never via a property initializer -
                    // see PersistedPlan.SchemaVersion.
                    SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                    RequestSchemaVersion = PersistedPlan.CurrentRequestSchemaVersion,
                    GeneratedAt = generatedAt,
                    RequestItems = requestItems,
                    UseOwnMaterials = useOwnMaterials,
                    PriceBasis = priceBasis,
                    ValueOwnMaterials = valueOwnMaterials,
                    Result = result,
                    NodeOverrides = new Dictionary<int, AcquisitionSource>(),
                    IgnoredItemIds = new List<int>(),
                });
                entry.BlobPresent = blobSaved;
                entry.BlobSchemaVersion = blobSaved ? PersistedPlan.CurrentSchemaVersion : 0;

                // Blob-only cap first (the row degrades to Re-solve),
                // then the row cap (row and blob both go).
                foreach (string evictId in PlanHistoryRetention.SelectForBlobEviction(
                    index.Entries, PlanHistoryRetention.PlanHistoryBlobCap))
                {
                    _planHistoryBlobStore.Delete(evictId);
                    var evicted = FindHistoryEntry(index, evictId);
                    if (evicted != null)
                    {
                        evicted.BlobPresent = false;
                    }
                }

                foreach (string evictId in PlanHistoryRetention.SelectForEviction(
                    index.Entries, _settings.GetClampedPlanHistoryMaxEntries()))
                {
                    _planHistoryBlobStore.Delete(evictId);
                    index.Entries.RemoveAll(e =>
                        e != null && string.Equals(e.EntryId, evictId, StringComparison.Ordinal));
                }

                _planHistoryStore.Save(index);
            }

            MainThreadMarshal.Run(() => _planHistoryContent?.Refresh());
        }

        private static List<PlanRequestItem> CopyRequestItems(IReadOnlyList<PlanRequestItem> requestItems)
        {
            var copy = new List<PlanRequestItem>();
            if (requestItems == null)
            {
                return copy;
            }

            foreach (var item in requestItems)
            {
                if (item != null)
                {
                    copy.Add(new PlanRequestItem
                    {
                        ItemId = item.ItemId,
                        Quantity = item.Quantity,
                        Name = item.Name,
                    });
                }
            }

            return copy;
        }

        private static List<PlanHistoryItemSummary> BuildItemSummaries(
            IReadOnlyList<PlanRequestItem> requestItems, CraftingPlanResult result)
        {
            var summaries = new List<PlanHistoryItemSummary>();
            if (requestItems == null)
            {
                return summaries;
            }

            foreach (var item in requestItems)
            {
                if (item == null)
                {
                    continue;
                }

                ItemMetadata metadata = null;
                if (result.ItemMetadata != null)
                {
                    result.ItemMetadata.TryGetValue(item.ItemId, out metadata);
                }

                summaries.Add(new PlanHistoryItemSummary
                {
                    ItemId = item.ItemId,
                    Name = metadata?.Name,
                    IconUrl = metadata?.IconUrl,
                    Rarity = metadata?.Rarity,
                    Quantity = item.Quantity,
                });
            }

            return summaries;
        }

        /// <summary>
        /// "Open": restores a history entry's exact saved plan into the
        /// Crafting Plan tab, pills live, zero network - mirroring the
        /// startup restore drain in Update() exactly, including the
        /// _lastPersistedPlanMetadata publication that keeps the NEXT
        /// pill click persisting under the right request/timestamp.
        /// Main thread only (a button Click). Returns false when the blob
        /// could not be read; the row's BlobPresent is cleared and
        /// persisted so it degrades to Re-solve.
        /// </summary>
        private bool OpenHistoryEntry(PlanHistoryEntry entry)
        {
            if (entry == null)
            {
                return false;
            }

            var plan = _planHistoryBlobStore.Load(entry.EntryId);
            if (plan == null)
            {
                lock (_planHistoryLock)
                {
                    var row = FindHistoryEntry(_planHistoryIndex, entry.EntryId);
                    if (row != null && row.BlobPresent)
                    {
                        row.BlobPresent = false;
                        _planHistoryStore.Save(_planHistoryIndex);
                    }
                }

                return false;
            }

            lock (_generateCompletionLock)
            {
                _lastPersistedPlanMetadata = new PersistedPlanMetadata(
                    plan.GeneratedAt,
                    plan.RequestItems,
                    plan.UseOwnMaterials,
                    plan.PriceBasis,
                    plan.ValueOwnMaterials);
            }

            _craftingContent?.ApplyRestoredPlan(
                plan.Result,
                plan.GeneratedAt,
                plan.NodeOverrides,
                plan.IgnoredItemIds,
                plan.ValueOwnMaterials,
                plan.RequestItems,
                plan.UseOwnMaterials,
                plan.PriceBasis);

            // The opened entry IS the current plan now. Off the UI
            // thread, like every other plan.json write; PlanStore.Save's
            // internal lock makes a race with a persist-in-flight safe.
            Task.Run(() => _planStore.Save(plan));

            _mainWindow.SelectedTab = _craftingPlanTab;
            return true;
        }

        /// <summary>
        /// "Re-solve": runs the entry's request back through the SAME
        /// Generate path a Crafting Plan click uses - including
        /// PersistAfterGenerateAsync, so the result becomes the current
        /// plan on disk and dedup-bumps this same history row - then
        /// renders it through ApplyRestoredPlan (which also reseeds the
        /// request inputs) and switches tabs. IgnoredItemIds ARE
        /// replayed through the restore path; NodeOverrides never are
        /// (they are only valid against the exact result they were
        /// captured with). Returns null on success, an error message on
        /// failure; a cancellation propagates as
        /// OperationCanceledException.
        /// </summary>
        private async Task<string> ResolveHistoryEntryAsync(
            PlanHistoryEntry entry, CancellationToken ct)
        {
            if (entry == null)
            {
                return "No entry.";
            }

            // Copied before the await: the capture bump mutates the same
            // entry object on another thread once the generate lands.
            var requestItems = CopyRequestItems(entry.RequestItems);
            bool useOwnMaterials = entry.UseOwnMaterials;
            var priceBasis = entry.PriceBasis;
            bool valueOwnMaterials = entry.ValueOwnMaterials;
            var ignoredItemIds = entry.IgnoredItemIds != null
                ? new List<int>(entry.IgnoredItemIds)
                : new List<int>();
            string requestLabel = PlanHistoryLabels.RowLabel(entry);

            if (requestItems.Count == 0)
            {
                return "This entry has no request to re-solve.";
            }

            // StartGenerateAsync reads Gw2Mumble and per-plan settings,
            // so it is invoked on the main thread exactly like a Generate
            // click; only the await runs out here.
            var startTcs = new TaskCompletionSource<Task<CraftingPlanResult>>();
            MainThreadMarshal.Run(() =>
            {
                try
                {
                    startTcs.SetResult(StartGenerateAsync(
                        requestItems, useOwnMaterials, valueOwnMaterials, priceBasis,
                        ct, null, null, requestLabel, _lifetimeCts.Token));
                }
                catch (Exception ex)
                {
                    startTcs.SetException(ex);
                }
            });

            CraftingPlanResult result;
            try
            {
                var generateTask = await startTcs.Task.ConfigureAwait(false);
                result = await generateTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }

            if (result == null)
            {
                return "Generation produced no plan.";
            }

            var generatedAt = DateTime.Now;
            MainThreadMarshal.Run(() =>
            {
                _craftingContent?.ApplyRestoredPlan(
                    result,
                    generatedAt,
                    new Dictionary<int, AcquisitionSource>(),
                    ignoredItemIds,
                    valueOwnMaterials,
                    requestItems,
                    useOwnMaterials,
                    priceBasis);
                _mainWindow.SelectedTab = _craftingPlanTab;
            });

            return null;
        }
    }
}
