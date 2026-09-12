namespace TaimisToolbench.Services
{
    /// <summary>
    /// Horizontal seats on a popout window's toolbar strip: the opacity
    /// caption, its slider and its readout ruled off the left edge, and the
    /// Refresh button pinned to the right. Blish-free, so the seats are
    /// asserted at the window's own minimum width rather than trusted.
    /// </summary>
    internal static class PopoutToolbarLayout
    {
        /// <summary>Left rule of the strip, which the caption takes.</summary>
        public const int CaptionX = 0;

        /// <summary>
        /// Gap between the caption and the slider, and between the slider
        /// and its readout: the module's one name-to-control gap. A Blish
        /// TrackBar paints its nub past its own left edge, so the caption
        /// needs more clearance than a plain button gap gives it.
        /// </summary>
        public const int SliderGap = SettingsFormLayout.NameToControlGap;

        /// <summary>
        /// Left edge of the slider, past a caption of the measured width.
        /// A negative or zero caption width is treated as no caption at
        /// all, which is what a font that failed to load leaves behind.
        /// </summary>
        public static int SliderX(int captionWidth)
        {
            return CaptionX + (captionWidth > 0 ? captionWidth : 0) + SliderGap;
        }

        /// <summary>Left edge of the percent readout, past the slider.</summary>
        public static int ReadoutX(int captionWidth)
        {
            return SliderX(captionWidth) + SettingsFormLayout.SliderWidth + SliderGap;
        }

        /// <summary>Right edge of the whole opacity cluster.</summary>
        public static int OpacityClusterRightEdge(int captionWidth)
        {
            return ReadoutX(captionWidth) + SettingsFormLayout.ReadoutWidth;
        }

        /// <summary>
        /// Right edge the Refresh button pins to: the content box less the
        /// scrollbar strip the table below the strip gives up, so the
        /// button rules with that table's own right edge rather than with
        /// the window frame.
        /// </summary>
        public static int RefreshRightEdge(int contentWidth)
        {
            return contentWidth - WindowSizing.ScrollbarAllowance;
        }

        /// <summary>Left edge of a Refresh button of this width.</summary>
        public static int RefreshX(int contentWidth, int buttonWidth)
        {
            return RefreshRightEdge(contentWidth) - buttonWidth;
        }

        /// <summary>
        /// Narrowest content box the strip holds without the button
        /// touching the readout beside it.
        /// </summary>
        public static int MinContentWidth(int captionWidth, int buttonWidth)
        {
            return OpacityClusterRightEdge(captionWidth) + SliderGap + buttonWidth
                + WindowSizing.ScrollbarAllowance;
        }
    }
}
