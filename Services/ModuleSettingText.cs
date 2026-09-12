using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Every setting this module registers with Blish HUD, as plain data:
    /// the storage key, a name and description, and whether Blish's own
    /// Manage Modules panel may draw the setting.
    /// <para>
    /// Every descriptor answers no. The module draws its own Settings tab,
    /// so Blish's panel shows nothing for it. The flag and the width budget
    /// stay because re-surfacing a setting is a one-word change here, and
    /// the name has to fit the moment that happens.
    /// </para>
    /// <para>
    /// Blish-free on purpose. ModuleSettings takes a SettingCollection and
    /// cannot be unit-tested, so what is worth pinning lives here instead.
    /// See docs/blish-settings-panel.md for how the panel lays a setting row
    /// out and where <see cref="MaxDisplayNameWidth"/> comes from.
    /// </para>
    /// </summary>
    internal static class ModuleSettingText
    {
        /// <summary>
        /// Widest a display name may be, in pixels, measured in Menomonia 14
        /// regular. Blish 1.3.0 puts a setting's name label at x=5 and its
        /// slider at x=185, and never moves either, so a name wider than 180
        /// is drawn over the slider. This budget is 175, which leaves the
        /// same 5px gap Blish puts between its own controls. No setting is
        /// drawn there now, so this binds only a setting turned back on.
        /// </summary>
        public const int MaxDisplayNameWidth = 175;

        /// <summary>
        /// The line Blish's panel shows in place of the setting list it would
        /// otherwise draw, and the label on the button beside it. Built by
        /// Views/BlishSettingsHintView.cs.
        /// </summary>
        public const string PanelHintText = "Settings for this module are in its own window.";

        public const string PanelHintButtonText = "Open Settings";

        /// <summary>One setting's key and the text Blish shows for it.</summary>
        internal sealed class Descriptor
        {
            public Descriptor(string key, string displayName, string description, bool shownInBlishPanel)
            {
                Key = key;
                DisplayName = displayName;
                Description = description;
                ShownInBlishPanel = shownInBlishPanel;
            }

            /// <summary>
            /// The persisted key. Renaming one orphans every already-saved
            /// value, so a key here is fixed for the life of the setting.
            /// </summary>
            public string Key { get; }

            public string DisplayName { get; }

            public string Description { get; }

            /// <summary>
            /// Whether Blish's own panel may draw this setting. False on
            /// every descriptor today. ModuleSettings puts a false one in a
            /// sub-collection Blish is told not to render, which is the only
            /// supported way to keep a setting out of that panel.
            /// </summary>
            public bool ShownInBlishPanel { get; }
        }

        public static readonly Descriptor ModalDialogX = new Descriptor(
            "ModalDialogX",
            "Modal dialog X",
            "Where this module's dialog box was last dragged, in pixels from the left.",
            shownInBlishPanel: false);

        public static readonly Descriptor ModalDialogY = new Descriptor(
            "ModalDialogY",
            "Modal dialog Y",
            "Where this module's dialog box was last dragged, in pixels from the top.",
            shownInBlishPanel: false);

        public static readonly Descriptor PopoutShoppingListOpacityPercent = new Descriptor(
            "PopoutShoppingListOpacityPercent",
            "Shopping list opacity",
            "How solid the Shopping List pop-out window is, as a percent. Set it on that window's own slider.",
            shownInBlishPanel: false);

        public static readonly Descriptor PopoutCraftingStepsOpacityPercent = new Descriptor(
            "PopoutCraftingStepsOpacityPercent",
            "Crafting steps opacity",
            "How solid the Crafting Steps pop-out window is, as a percent. Set it on that window's own slider.",
            shownInBlishPanel: false);

        public static readonly Descriptor CurrencyValuationsJson = new Descriptor(
            "CurrencyValuationsJson",
            "Vendor cost valuations",
            "Your coin values for karma, laurels and other non-coin vendor costs. Edit them on this module's Settings tab.",
            shownInBlishPanel: false);

        public static readonly Descriptor ValueOwnMaterials = new Descriptor(
            "ValueOwnMaterials",
            "Value own materials",
            "Superseded. The live control is the per-plan checkbox on the Crafting Plan tab.",
            shownInBlishPanel: false);

        public static readonly Descriptor ScrollDiagnosticsEnabled = new Descriptor(
            "ScrollDiagnosticsEnabled",
            "Scroll diagnostics",
            "Superseded by Diagnostics logging, which now covers scroll events too.",
            shownInBlishPanel: false);

        public static readonly Descriptor HomesteadFiberTier = new Descriptor(
            "HomesteadFiberTier",
            "Homestead fiber tier",
            "How many Farm refinement efficiency upgrades you own. 0, 1 or 2.",
            shownInBlishPanel: false);

        public static readonly Descriptor HomesteadMetalTier = new Descriptor(
            "HomesteadMetalTier",
            "Homestead metal tier",
            "How many Metal Forge refinement efficiency upgrades you own. 0, 1 or 2.",
            shownInBlishPanel: false);

        public static readonly Descriptor HomesteadWoodTier = new Descriptor(
            "HomesteadWoodTier",
            "Homestead wood tier",
            "How many Lumber Mill refinement efficiency upgrades you own. 0, 1 or 2.",
            shownInBlishPanel: false);

        public static readonly Descriptor LogMaxSizeBytes = new Descriptor(
            "LogMaxSizeBytes",
            "Log max size (bytes)",
            "How large the module log file may grow before the oldest entries are trimmed.",
            shownInBlishPanel: false);

        public static readonly Descriptor LogRetentionDays = new Descriptor(
            "LogRetentionDays",
            "Log retention (days)",
            "How many days of module log history to keep on disk. Applied once when the module starts.",
            shownInBlishPanel: false);

        public static readonly Descriptor LogDiagnosticsEnabled = new Descriptor(
            "LogDiagnosticsEnabled",
            "Diagnostics logging",
            "Log fine-grained diagnostic events, including scroll machinery, to the Log tab and the log file.",
            shownInBlishPanel: false);

        public static readonly Descriptor PlanHistoryMaxEntries = new Descriptor(
            "PlanHistoryMaxEntries",
            "Plan history entries kept",
            "How many generated plans the Plan History tab keeps. Pinned entries are never removed.",
            shownInBlishPanel: false);

        public static readonly Descriptor SnapshotRefreshIntervalMinutes = new Descriptor(
            "SnapshotRefreshIntervalMinutes",
            "Refresh interval (minutes)",
            "How long a cached account snapshot may sit before the module refreshes it in the background.",
            shownInBlishPanel: false);

        public static readonly Descriptor ClickSoundVolumePercent = new Descriptor(
            "ClickSoundVolumePercent",
            "Click volume",
            "How loud this module's own click plays when you press its buttons, rows and pills. 0 is off, 100 is loudest. Checkboxes keep Blish HUD's own click sound.",
            shownInBlishPanel: false);

        /// <summary>
        /// Every descriptor, in the order ModuleSettings defines them.
        /// </summary>
        public static IReadOnlyList<Descriptor> All { get; } = new[]
        {
            ModalDialogX,
            ModalDialogY,
            PopoutShoppingListOpacityPercent,
            PopoutCraftingStepsOpacityPercent,
            CurrencyValuationsJson,
            ValueOwnMaterials,
            ScrollDiagnosticsEnabled,
            HomesteadFiberTier,
            HomesteadMetalTier,
            HomesteadWoodTier,
            LogMaxSizeBytes,
            LogRetentionDays,
            LogDiagnosticsEnabled,
            PlanHistoryMaxEntries,
            SnapshotRefreshIntervalMinutes,
            ClickSoundVolumePercent,
        };
    }
}
