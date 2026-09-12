using System.Collections.Generic;
using Blish_HUD.Settings;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal class ModuleSettings
    {
        public SettingEntry<int> ModalDialogX { get; private set; }

        public SettingEntry<int> ModalDialogY { get; private set; }

        // How far a popout window is faded over the game, per popout. Two
        // entries rather than one, because a player who wants the shopping
        // list solid while they stand at a vendor may still want the
        // crafting steps ghosted beside it. PopoutOpacity owns the floor
        // that stops either reaching zero.
        public SettingEntry<int> PopoutShoppingListOpacityPercent { get; private set; }

        public SettingEntry<int> PopoutCraftingStepsOpacityPercent { get; private set; }

        // User-provided coin valuations for the non-coin currencies (karma,
        // laurels, ...) and untradeable barter items a vendor takes, stored
        // as JSON (see CurrencyValuationSerializer for the shape). The
        // setting KEY stays "CurrencyValuationsJson": renaming it would
        // orphan every already-persisted value.
        // SettingCollection has no built-in support for a CurrencyValuation
        // object, so it is persisted as a raw string; CurrencyValuationSerializer
        // (Blish-free) does the actual conversion so that logic is unit-testable.
        public SettingEntry<string> CurrencyValuationsJson { get; private set; }

        // gw2efficiency-style "value own materials" (upgraded over time
        // from a display-only opportunity-cost tweak into a real
        // force-buy pre-pass - see OwnedMaterialsForceBuyPrePass - and
        // then into a full decision-invariant reduction, see
        // InventoryReducer's zeroOwnedDecisions doc comment): when enabled,
        // a node is
        // force-excluded from crafting whenever buying it outright costs
        // less than 85% of its own components' fresh buy cost
        // (gw2efficiency's getCheaperToBuyItemIds), owned stock only ever
        // discounts the recipe option a zero-owned baseline would actually
        // choose (never a never-chosen branch), and the plan's profit
        // figure is reduced by owned materials' sell opportunity cost.
        //
        // SUPERSEDED: this setting is kept defined
        // ONLY for backward compatibility with an already-persisted
        // settings.json value (mirroring the ScrollDiagnosticsEnabled
        // precedent below) - it is no longer read on the live Module.cs
        // call path. The real control is now Views/CraftingPlanView.cs's
        // per-plan `_valueOwnMaterials` checkbox (session state, exactly
        // like its `_useOwnMaterials`/`_priceBasis` neighbors - never
        // read from/written to this setting), because the whole point of
        // moving it inline is that it is a per-generation choice like
        // those two. The Settings tab now shows an info line instead of a
        // live checkbox for this setting - see SettingsTabContent.
        public SettingEntry<bool> ValueOwnMaterials { get; private set; }

        // Per-material Homestead
        // Refinement efficiency tier (0/1/2), echoing gw2efficiency's own
        // per-output-material userEfficiencyTiers setting exactly - three
        // independent settings, not one combined toggle, matching gw2e's
        // own three-material shape (docs/research/m37-r1-homestead.md
        // Section 1.2). Default 0 for all three: gw2e's own hardcoded
        // default AND its own no-API-key fallback, and matches this repo's
        // "no invented data" posture better than assuming any upgrade
        // level. Deliberately NO master "do you even own Homestead" gate -
        // gw2e has none either; see KNOWN-ISSUES #24 for that
        // recorded, deferred divergence option.
        public SettingEntry<int> HomesteadFiberTier { get; private set; }

        public SettingEntry<int> HomesteadMetalTier { get; private set; }

        public SettingEntry<int> HomesteadWoodTier { get; private set; }

        // Gates the scroll-machinery diagnostic
        // logging in CraftingPlanView (wheel events, restore/guard writes
        // and state transitions). Default false; instrumentation only -
        // never changes scroll/guard/restore behavior.
        // SUBSUMED by LogDiagnosticsEnabled below per the
        // tab-roadmap-proposal synthesis (Section 2.1) - the Settings tab
        // now ships exactly ONE diagnostics checkbox (LogDiagnosticsEnabled),
        // not two. This setting is kept defined (not removed) purely for
        // backward compatibility with any already-persisted value: renaming
        // the key outright would silently drop a hand-set true for existing
        // users, whereas CraftingPlanView.ScrollDiagEnabled now reads BOTH
        // this and LogDiagnosticsEnabled (a plain bool OR - trivially cheap,
        // no extra I/O) so an old persisted true still gates the
        // [scrolldiag] channel exactly as before. No UI checkbox for this
        // one; new users only ever see LogDiagnosticsEnabled.
        public SettingEntry<bool> ScrollDiagnosticsEnabled { get; private set; }

        // Size cap for the
        // module log file (data/module_log.jsonl), in bytes. Default 2 MB.
        // Checked on every ModuleLog write (self-trimming) - see
        // ModuleLogStore.AppendLine.
        public SettingEntry<int> LogMaxSizeBytes { get; private set; }

        // Age-based retention
        // for the module log file, in days. Default 14. Enforced once per
        // session at Module.LoadAsync - see ModuleLogStore.PruneOlderThan.
        public SettingEntry<int> LogRetentionDays { get; private set; }

        // The ONE diagnostics toggle for the whole module
        // (dev/proposals/d2-log-system.md Section 5) -
        // subsumes ScrollDiagnosticsEnabled above and additionally gates
        // whether Debug-level ModuleLog entries reach the file sink (they
        // always still land in the in-memory ring regardless - see
        // ModuleLog's own policy). Default false, matching
        // ScrollDiagnosticsEnabled's own prior default. Has a real Settings
        // tab checkbox (idiom (a), immediate-apply, no Save button - see
        // SettingsTabContent).
        public SettingEntry<bool> LogDiagnosticsEnabled { get; private set; }

        // How many previously-generated plans the Plan History tab keeps.
        // Default 25 (each entry owns a gzipped PersistedPlan blob, so the
        // cap bounds disk use to single-digit MB). Pinned entries are
        // never removed by the cap. Read through
        // GetClampedPlanHistoryMaxEntries below - same hand-edited-file
        // contract as every clamped accessor here.
        public SettingEntry<int> PlanHistoryMaxEntries { get; private set; }

        // Replaces Module.cs's
        // previously-hardcoded `StaleThreshold` constant. Default 10 minutes
        // (matching the constant it replaces), clamped 1-120. Read directly
        // by Module.Update()'s own staleness check via
        // GetClampedSnapshotRefreshIntervalMinutes below - a hand-edited
        // settings file with an out-of-range value must clamp, never crash
        // or disable the auto-refresh gate (same contract as
        // GetClampedLogMaxSizeBytes/GetClampedLogRetentionDays above).
        public SettingEntry<int> SnapshotRefreshIntervalMinutes { get; private set; }

        // How loud this module's own UI click plays, 0-100. Reaches only the
        // clicks this module plays itself - see PressFeedback.PlayClick.
        public SettingEntry<int> ClickSoundVolumePercent { get; private set; }

        public ModuleSettings(SettingCollection settings)
        {
            var hidden = settings.AddSubCollection(
                HiddenCollectionKey, renderInUi: false, lazyLoaded: false, displayNameFunc: null);

            ModalDialogX = Define(settings, hidden, ModuleSettingText.ModalDialogX, -1);
            ModalDialogY = Define(settings, hidden, ModuleSettingText.ModalDialogY, -1);

            PopoutShoppingListOpacityPercent = Define(
                settings, hidden, ModuleSettingText.PopoutShoppingListOpacityPercent, PopoutOpacity.DefaultPercent);
            PopoutCraftingStepsOpacityPercent = Define(
                settings, hidden, ModuleSettingText.PopoutCraftingStepsOpacityPercent, PopoutOpacity.DefaultPercent);

            CurrencyValuationsJson = Define(
                settings, hidden, ModuleSettingText.CurrencyValuationsJson, string.Empty);
            ValueOwnMaterials = Define(settings, hidden, ModuleSettingText.ValueOwnMaterials, true);
            ScrollDiagnosticsEnabled = Define(
                settings, hidden, ModuleSettingText.ScrollDiagnosticsEnabled, false);

            HomesteadFiberTier = Define(settings, hidden, ModuleSettingText.HomesteadFiberTier, 0);
            HomesteadMetalTier = Define(settings, hidden, ModuleSettingText.HomesteadMetalTier, 0);
            HomesteadWoodTier = Define(settings, hidden, ModuleSettingText.HomesteadWoodTier, 0);

            LogMaxSizeBytes = Define(settings, hidden, ModuleSettingText.LogMaxSizeBytes, 2 * 1024 * 1024);
            LogRetentionDays = Define(settings, hidden, ModuleSettingText.LogRetentionDays, 14);
            LogDiagnosticsEnabled = Define(settings, hidden, ModuleSettingText.LogDiagnosticsEnabled, false);

            PlanHistoryMaxEntries = Define(settings, hidden, ModuleSettingText.PlanHistoryMaxEntries, 25);
            SnapshotRefreshIntervalMinutes = Define(
                settings, hidden, ModuleSettingText.SnapshotRefreshIntervalMinutes, 10);
            ClickSoundVolumePercent = Define(
                settings, hidden, ModuleSettingText.ClickSoundVolumePercent, ClickSoundVolume.DefaultPercent);
        }

        // Blish renders every SettingEntry a module defines into its own
        // Manage Modules panel, and offers exactly one way to opt out: a
        // sub-collection whose RenderInUi is false, which
        // Blish_HUD.Settings.UI.Views.SettingView.FromType skips whole. The
        // key names a real JSON object inside the module's settings, so it
        // is fixed for the life of the module.
        private const string HiddenCollectionKey = "Internal";

        /// <summary>
        /// Defines one setting where its descriptor says it belongs: the root
        /// collection when Blish's panel may draw it, the non-rendered
        /// sub-collection when it may not.
        /// </summary>
        private static SettingEntry<T> Define<T>(
            SettingCollection root, SettingCollection hidden,
            ModuleSettingText.Descriptor descriptor, T defaultValue)
        {
            return descriptor.ShownInBlishPanel
                ? DefineIn(root, descriptor, defaultValue)
                : DefineHidden(root, hidden, descriptor, defaultValue);
        }

        private static SettingEntry<T> DefineIn<T>(
            SettingCollection collection, ModuleSettingText.Descriptor descriptor, T defaultValue)
        {
            return collection.DefineSetting(
                descriptor.Key, defaultValue,
                () => descriptor.DisplayName,
                () => descriptor.Description);
        }

        /// <summary>
        /// Defines a setting into the non-rendered sub-collection, carrying
        /// any value already saved under the old top-level key across. A
        /// sub-collection is a nested JSON object, so the value moves even
        /// though the key does not; without this, a player who had set a log
        /// size or a click volume gets the default back. The top-level copy
        /// is undefined afterwards, so no stale second figure is left behind.
        /// See docs/blish-settings-panel.md for the storage shape.
        /// </summary>
        private static SettingEntry<T> DefineHidden<T>(
            SettingCollection root, SettingCollection hidden,
            ModuleSettingText.Descriptor descriptor, T defaultValue)
        {
            bool alreadyMoved = hidden.ContainsSetting(descriptor.Key);
            var entry = DefineIn(hidden, descriptor, defaultValue);

            if (!alreadyMoved && root.TryGetSetting(descriptor.Key, out SettingEntry<T> legacy))
            {
                entry.Value = legacy.Value;
            }

            root.UndefineSetting(descriptor.Key);
            return entry;
        }

        /// <summary>
        /// Reads the persisted Homestead Refinement efficiency tiers.
        /// Values outside 0-2 (possible only via a hand-edited settings
        /// file, since the Settings tab offers the three valid choices and
        /// nothing else) are clamped rather than thrown - a corrupt/out-of-range
        /// persisted value must never crash plan generation. See
        /// HomesteadEfficiencyTiers' own constructor for why clamping
        /// happens here rather than there: that constructor fails loudly by
        /// design for a directly-constructed caller.
        /// </summary>
        public HomesteadEfficiencyTiers GetHomesteadEfficiencyTiers()
        {
            var map = new Dictionary<int, int>
            {
                { Gw2Constants.RefinedHomesteadFiberItemId, ClampTier(HomesteadFiberTier.Value) },
                { Gw2Constants.RefinedHomesteadMetalItemId, ClampTier(HomesteadMetalTier.Value) },
                { Gw2Constants.RefinedHomesteadWoodItemId, ClampTier(HomesteadWoodTier.Value) },
            };
            return new HomesteadEfficiencyTiers(map);
        }

        private static int ClampTier(int tier)
        {
            if (tier < 0)
            {
                return 0;
            }

            if (tier > 2)
            {
                return 2;
            }

            return tier;
        }

        // Mirrors SettingsInputParser.TryParseLogMaxSizeMb's own 1-1000 MB
        // bound (same deliberate duplication as ClampTier's own 0-2 range
        // above, and for the same reason). A persisted
        // value outside this range is reachable only via a hand-edited
        // settings.json - the Settings tab's own parser rejects it before
        // it is ever assigned - but ModuleLogStore.AppendLine's self-trim
        // check is `if (maxSizeBytes > 0)`: a persisted 0 or negative value
        // would silently disable the size cap for the whole session, which
        // is the exact "endless crap on disk" outcome this feature exists
        // to prevent.
        private const int MinLogMaxSizeBytes = 1 * 1024 * 1024;
        private const int MaxLogMaxSizeBytes = 1000 * 1024 * 1024;

        private static int ClampLogMaxSizeBytes(int maxSizeBytes)
        {
            if (maxSizeBytes < MinLogMaxSizeBytes)
            {
                return MinLogMaxSizeBytes;
            }

            if (maxSizeBytes > MaxLogMaxSizeBytes)
            {
                return MaxLogMaxSizeBytes;
            }

            return maxSizeBytes;
        }

        // Mirrors SettingsInputParser.TryParseRetentionDays's own 1-365 day
        // bound - see ClampLogMaxSizeBytes' own comment for why the
        // duplication is deliberate and why a persisted value must never
        // bypass this. ModuleLogStore.PruneOlderThan's own no-op guard is
        // `if (retentionDays <= 0) return;`, so a persisted 0/negative
        // value would silently disable age-based retention entirely.
        private const int MinLogRetentionDays = 1;
        private const int MaxLogRetentionDays = 365;

        private static int ClampRetentionDays(int retentionDays)
        {
            if (retentionDays < MinLogRetentionDays)
            {
                return MinLogRetentionDays;
            }

            if (retentionDays > MaxLogRetentionDays)
            {
                return MaxLogRetentionDays;
            }

            return retentionDays;
        }

        /// <summary>
        /// Clamped LogMaxSizeBytes for actual use - see
        /// ClampLogMaxSizeBytes' own comment. Callers (Module.cs's
        /// Configure call, and SettingsTabContent's live-push after a save)
        /// should always read this instead of LogMaxSizeBytes.Value
        /// directly, the same way GetHomesteadEfficiencyTiers already
        /// clamps rather than exposing HomesteadFiberTier.Value raw.
        /// </summary>
        public int GetClampedLogMaxSizeBytes()
        {
            return ClampLogMaxSizeBytes(LogMaxSizeBytes.Value);
        }

        /// <summary>
        /// Clamped LogRetentionDays for actual use - see
        /// ClampRetentionDays' own comment.
        /// </summary>
        public int GetClampedLogRetentionDays()
        {
            return ClampRetentionDays(LogRetentionDays.Value);
        }

        // Mirrors SettingsInputParser.TryParsePlanHistoryMaxEntries'
        // 5-200 bound - the duplication is deliberate, see
        // ClampLogMaxSizeBytes' own comment. A hand-edited settings file
        // must never break the Plan History tab: 0 or a negative value
        // would evict every row on the next capture.
        private const int MinPlanHistoryMaxEntries = 5;
        private const int MaxPlanHistoryMaxEntries = 200;

        /// <summary>
        /// Clamped PlanHistoryMaxEntries for actual use - see the field's
        /// own comment.
        /// </summary>
        public int GetClampedPlanHistoryMaxEntries()
        {
            int value = PlanHistoryMaxEntries.Value;
            if (value < MinPlanHistoryMaxEntries)
            {
                return MinPlanHistoryMaxEntries;
            }

            if (value > MaxPlanHistoryMaxEntries)
            {
                return MaxPlanHistoryMaxEntries;
            }

            return value;
        }

        // Mirrors SettingsInputParser.TryParseRefreshIntervalMinutes' own
        // 1-120 minute bound - see ClampLogMaxSizeBytes' own comment above
        // for why the duplication is deliberate. Module.Update()'s
        // staleness check must never see a persisted 0/negative value (that
        // would make every tick immediately "stale", defeating the point of
        // the backoff/throttling already in place around
        // RefreshSnapshotInBackgroundAsync) or an absurdly large one (that
        // would silently disable auto-refresh for a hand-edited settings
        // file).
        private const int MinSnapshotRefreshIntervalMinutes = 1;
        private const int MaxSnapshotRefreshIntervalMinutes = 120;

        private static int ClampSnapshotRefreshIntervalMinutes(int minutes)
        {
            if (minutes < MinSnapshotRefreshIntervalMinutes)
            {
                return MinSnapshotRefreshIntervalMinutes;
            }

            if (minutes > MaxSnapshotRefreshIntervalMinutes)
            {
                return MaxSnapshotRefreshIntervalMinutes;
            }

            return minutes;
        }

        /// <summary>
        /// Clamped SnapshotRefreshIntervalMinutes for actual use - see
        /// ClampSnapshotRefreshIntervalMinutes' own comment. Module.Update()
        /// should always read this instead of
        /// SnapshotRefreshIntervalMinutes.Value directly.
        /// </summary>
        public int GetClampedSnapshotRefreshIntervalMinutes()
        {
            return ClampSnapshotRefreshIntervalMinutes(SnapshotRefreshIntervalMinutes.Value);
        }

        /// <summary>
        /// Clamped ClickSoundVolumePercent for actual use - same contract
        /// as the clamped accessors above.
        /// </summary>
        public int GetClampedClickSoundVolumePercent()
        {
            return ClickSoundVolume.Clamp(ClickSoundVolumePercent.Value);
        }

        /// <summary>
        /// Clamped popout opacity for actual use - same contract as the
        /// clamped accessors above. A hand-edited settings file can hold any
        /// figure, so the floor is applied on the way out and not only by the
        /// slider that normally sets them.
        /// </summary>
        public int GetClampedPopoutOpacityPercent(PlanSectionType sectionType)
        {
            return PopoutOpacity.Clamp(EntryFor(sectionType).Value);
        }

        public void SetPopoutOpacityPercent(PlanSectionType sectionType, int percent)
        {
            EntryFor(sectionType).Value = PopoutOpacity.Clamp(percent);
        }

        private SettingEntry<int> EntryFor(PlanSectionType sectionType)
        {
            return sectionType == PlanSectionType.CraftingSteps
                ? PopoutCraftingStepsOpacityPercent
                : PopoutShoppingListOpacityPercent;
        }

        /// <summary>
        /// Reads the RAW persisted currency valuations - user-set overrides
        /// and explicit clears only, with no CurrencyDecisionDefaults
        /// default folded in. Returns CurrencyValuation.None when nothing
        /// has been configured or the stored value cannot be parsed. Used
        /// by the Settings tab (SettingsTabContent), which must be able to
        /// tell "the user typed this" apart from "this is just the curated
        /// default" - see GetEffectiveCurrencyValuation for the solver-
        /// facing counterpart that DOES fold defaults in.
        /// </summary>
        public CurrencyValuation GetCurrencyValuation()
        {
            return CurrencyValuationSerializer.Deserialize(CurrencyValuationsJson.Value);
        }

        /// <summary>
        /// The solver-facing counterpart
        /// of GetCurrencyValuation - same raw persisted overrides/clears,
        /// PLUS every CurrencyDecisionDefaults entry that is neither
        /// explicitly overridden nor explicitly cleared, via
        /// CurrencyValuation.TryGetEffectiveCopperValue (the one place the
        /// three-state precedence is implemented). This is the ONLY
        /// production call site that should ever see defaults applied -
        /// Module.cs is this method's sole caller, threading the result
        /// into CraftingPlanPipeline.GenerateStructuredAsync. Every other
        /// consumer of a CurrencyValuation (a directly-constructed test
        /// instance, or GetCurrencyValuation's raw read above) sees only
        /// what was actually persisted, by design - defaults are applied
        /// exactly once, here, rather than inside the solver itself, so a
        /// bare PlanSolver.Solve/CraftingPlanPipeline call with an
        /// explicit CurrencyValuation (as most of this repo's solver tests
        /// make) is never silently reshaped by a curated default it never
        /// asked for.
        /// </summary>
        public CurrencyValuation GetEffectiveCurrencyValuation()
        {
            // the
            // merge itself now lives on CurrencyValuation.WithDefaults (a
            // Blish-free Models type, therefore unit-testable) instead of
            // being inlined here - this class stays the sole production
            // caller, unchanged in every other respect.
            return CurrencyValuation.WithDefaults(GetCurrencyValuation());
        }

        /// <summary>
        /// Persists the given currency valuations.
        /// </summary>
        public void SetCurrencyValuation(CurrencyValuation valuation)
        {
            CurrencyValuationsJson.Value = CurrencyValuationSerializer.Serialize(valuation);
        }
    }
}
