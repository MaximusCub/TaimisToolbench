using System;
using System.Threading.Tasks;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Services;
using TaimisToolbench.Views.Rendering;

namespace TaimisToolbench.Views
{
    /// <summary>
    /// A popout's own chrome: the Refresh button, the transparency slider
    /// and its readout, and the status line the two report through.
    /// <para>
    /// The three are the only parts of a popout with no equivalent on the
    /// Crafting Plan tab, so they are the only parts drawn by new code. Each
    /// is built from a control the module already uses: the button is the
    /// Account Snapshot tab's Refresh Now shape (a FeedbackButton with a
    /// status label and a trailing spinner), and the slider is the Settings
    /// tab's volume row (a TrackBar with a percent readout beside it).
    /// </para>
    /// </summary>
    internal sealed class PopoutToolbar
    {
        private const int RefreshButtonWidth = 90;

        private const int OpacityCaptionWidth = 60;

        private const int SliderHeight = 16;

        private const int CaptionY = 6;

        private const int SliderY = 8;

        private const int SeparatorZIndex = 11;

        private readonly Action<int> _setOpacityPercent;
        private readonly Action<int> _applyOpacity;

        private readonly Panel _bar;
        private readonly Panel _separator;
        private readonly FeedbackButton _refreshButton;
        private readonly Label _caption;
        private readonly Label _readout;
        private readonly Label _status;
        private readonly LoadingSpinner _spinner;

        private TrackBar _slider;

        internal PopoutToolbar(
            Container parent,
            int contentWidth,
            int opacityPercent,
            Action<int> setOpacityPercent,
            Action<int> applyOpacity,
            Func<Task> onRefresh)
        {
            _setOpacityPercent = setOpacityPercent ?? throw new ArgumentNullException(nameof(setOpacityPercent));
            _applyOpacity = applyOpacity ?? throw new ArgumentNullException(nameof(applyOpacity));

            _bar = new Panel()
            {
                Size = new Point(contentWidth, PopoutLayout.ToolbarHeight),
                Parent = parent,
            };

            _refreshButton = new FeedbackButton()
            {
                Text = "Refresh",
                Size = new Point(RefreshButtonWidth, UiMetrics.ButtonHeight),
                Location = Point.Zero,
                Parent = _bar,
            };
            _refreshButton.Click += async (_, __) => await onRefresh();
            TooltipFacility.ApplyPlain(
                _refreshButton,
                "Syncs your account, then takes off this list what you have picked up since "
                + "the last sync. Clears every tick. It does not rebuild the plan.");

            int percent = PopoutOpacity.Clamp(opacityPercent);
            _caption = new Label()
            {
                Text = "Opacity",
                Font = UiFonts.Status,
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Size = new Point(OpacityCaptionWidth, PopoutLayout.ToolbarHeight),
                Location = new Point(CaptionX(contentWidth), CaptionY),
                Parent = _bar,
            };

            // MinValue and MaxValue are assigned even though they match the
            // vendor defaults: the setters are what fill the snap table a
            // Ctrl+drag aggregates over, and a TrackBar that never had
            // either assigned throws on one - see the identical note on
            // Views/SettingsTabContent's volume slider.
            _slider = new TrackBar()
            {
                MinValue = PopoutOpacity.MinPercent,
                MaxValue = PopoutOpacity.MaxPercent,
                Value = percent,
                Size = new Point(SettingsFormLayout.SliderWidth, SliderHeight),
                Location = new Point(SliderX(contentWidth), SliderY),
                BasicTooltipText =
                    "Fades the window over the game. It stops at "
                    + PopoutOpacity.MinPercent + "% so it can never vanish.",
                Parent = _bar,
            };

            _readout = new Label()
            {
                Text = percent + "%",
                Font = UiFonts.Status,
                AutoSizeWidth = false,
                AutoSizeHeight = true,
                Size = new Point(SettingsFormLayout.ReadoutWidth, PopoutLayout.ToolbarHeight),
                Location = new Point(ReadoutX(contentWidth), CaptionY),
                Parent = _bar,
            };

            // Subscribed after the initial Value assignment above, so it
            // does not fire while the window is still being built.
            _slider.ValueChanged += OnSliderValueChanged;

            _status = new Label()
            {
                Font = UiFonts.Status,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(0, PopoutLayout.ToolbarHeight + 4),
                Parent = parent,
            };
            _spinner = InlineSpinner.Create(parent, InlineSpinnerLayout.PlanStripSize);

            // Above the scrolling panel below it and above a pinned column
            // header's clip (StickyHeaderHost.ClipZIndex is 10), so the rule
            // paints last: the viewport's cutoff reaches one slip budget
            // above the line it publishes, and at the UI Sizes whose clip
            // round trip loses pixels a scrolled row can otherwise notch it.
            _separator = new Panel()
            {
                Size = new Point(contentWidth, PopoutLayout.SeparatorHeight),
                Location = new Point(0, PopoutLayout.ToolbarHeight + PopoutLayout.StatusRowHeight),
                BackgroundColor = new Color(180, 180, 180),
                ZIndex = SeparatorZIndex,
                Parent = parent,
            };
        }

        private void OnSliderValueChanged(object sender, ValueEventArgs<float> e)
        {
            int percent;
            if (!PopoutOpacity.TryPercentFromSliderValue(e.Value, out percent))
            {
                return;
            }

            _setOpacityPercent(percent);
            ShowPercent(percent);
            _applyOpacity(percent);
        }

        internal void ShowPercent(int percent)
        {
            _readout.Text = PopoutOpacity.Clamp(percent) + "%";
        }

        /// <summary>
        /// One line under the toolbar, with the module's inline spinner
        /// trailing it while a sync runs. Re-anchored on every write: a
        /// Blish Label with AutoSizeWidth recalculates its own width inside
        /// the Text setter.
        /// </summary>
        internal void SetStatus(string text)
        {
            _status.Text = text ?? string.Empty;
            InlineSpinner.PlaceAfter(_spinner, _status, InlineSpinnerLayout.LabelGap);
        }

        /// <summary>
        /// Six seconds of an unchanged button reads as a broken one, so the
        /// press disables it and shows the spinner for as long as the fetch
        /// takes.
        /// </summary>
        internal void SetBusy(bool busy)
        {
            _refreshButton.Enabled = !busy;
            _spinner.Visible = busy;
        }

        internal void Relayout(int contentWidth)
        {
            _bar.Size = new Point(contentWidth, PopoutLayout.ToolbarHeight);
            _separator.Size = new Point(contentWidth, PopoutLayout.SeparatorHeight);
            _caption.Location = new Point(CaptionX(contentWidth), CaptionY);
            _readout.Location = new Point(ReadoutX(contentWidth), CaptionY);
            if (_slider != null)
            {
                _slider.Location = new Point(SliderX(contentWidth), SliderY);
            }
        }

        /// <summary>
        /// Disposed rather than dropped, and taken out of the tree first so
        /// the window's own child sweep cannot reach it a second time: a
        /// TrackBar hooks the static input handler and leaks if it is only
        /// unparented, which is why Views/SettingsTabContent disposes its
        /// own.
        /// </summary>
        internal void Dispose()
        {
            if (_slider == null)
            {
                return;
            }

            _slider.ValueChanged -= OnSliderValueChanged;
            _slider.Parent = null;
            _slider.Dispose();
            _slider = null;
        }

        private static int ReadoutX(int contentWidth)
        {
            return contentWidth - WindowSizing.ScrollbarAllowance - SettingsFormLayout.ReadoutWidth;
        }

        private static int SliderX(int contentWidth)
        {
            return ReadoutX(contentWidth) - UiSpacing.ButtonGap - SettingsFormLayout.SliderWidth;
        }

        private static int CaptionX(int contentWidth)
        {
            return SliderX(contentWidth) - UiSpacing.ButtonGap - OpacityCaptionWidth;
        }
    }
}
