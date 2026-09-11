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
    /// Returning a view at all is what stops Blish drawing its own setting
    /// list, so this is also the belt to the sub-collection's braces. See
    /// docs/blish-settings-panel.md.
    /// </para>
    /// </summary>
    internal class BlishSettingsHintView : View
    {
        // Both strings live in ModuleSettingText so a Blish-free test can
        // measure them; StandardButton and Label draw in Menomonia 14 and
        // expose no Font seam to measure through here.
        //
        // The hint is 288px and the button label 86px in that face. Blish
        // 1.3.0 puts a setting's slider at x=185 and makes it 277 wide, so
        // the panel this sits in is at least 462px: the hint fits on one
        // line and is not wrapped. Lengthen it and it clips instead.
        private const int ButtonWidth = 130;

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
