using System;
using Blish_HUD.Controls;
using Blish_HUD.Graphics.UI;
using Microsoft.Xna.Framework;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// What Blish's Manage Modules panel shows for this module. Every setting
    /// this module defines sits in a sub-collection Blish is told not to
    /// render, so without this view the panel is blank and a player is never
    /// told where the settings went.
    /// <para>
    /// Returning a view at all also stops Blish drawing its own setting
    /// list, so the panel stays empty even if a setting is ever defined
    /// outside the sub-collection. See docs/blish-settings-panel.md.
    /// </para>
    /// </summary>
    internal class BlishSettingsHintView : View
    {
        // Label and StandardButton both draw in Menomonia 14 and expose no
        // Font seam to measure through, so the two strings live in
        // ModuleSettingText where a Blish-free test can measure them.

        // Fits the 86px button label with room for the button's own padding.
        private const int ButtonWidth = 130;

        // One line. The hint measures 288px, and Blish 1.3.0 seats a
        // setting's slider at x=185 and makes it 277 wide, so the panel is
        // at least 462px. A longer hint clips rather than wraps.
        private const int LineHeight = 20;

        private readonly Action _openSettings;

        public BlishSettingsHintView(Action openSettings)
        {
            _openSettings = openSettings ?? throw new ArgumentNullException(nameof(openSettings));
        }

        protected override void Build(Container buildPanel)
        {
            int width = Math.Max(0, buildPanel.ContentRegion.Width - (2 * UiSpacing.Inset));

            var hint = new Label()
            {
                Parent = buildPanel,
                Location = new Point(UiSpacing.Inset, UiSpacing.Inset),
                Size = new Point(width, LineHeight),
                Text = ModuleSettingText.PanelHintText,
            };

            var openSettings = new StandardButton()
            {
                Parent = buildPanel,
                Location = new Point(UiSpacing.Inset, hint.Bottom + UiSpacing.ButtonGap),
                Size = new Point(ButtonWidth, UiMetrics.ButtonHeight),
                Text = ModuleSettingText.PanelHintButtonText,
            };

            openSettings.Click += (s, e) => _openSettings();
        }
    }
}
