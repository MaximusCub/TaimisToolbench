namespace TaimisToolbench.Services
{
    /// <summary>
    /// The popout window's transparency scale, as a whole percent.
    /// Blish-free so the floor is unit-tested rather than trusted.
    /// <para>
    /// The floor is 50 because the module's own most-faded live surface is
    /// the stale-plan wash at 0.45, and that wash exists to read as "these
    /// numbers are not current". A popout the player is reading mid-fight
    /// has to stay above the tier the module already spends on degraded
    /// content, so the floor sits one step over it.
    /// </para>
    /// </summary>
    internal static class PopoutOpacity
    {
        public const int MinPercent = 50;

        public const int MaxPercent = 100;

        public const int DefaultPercent = 100;

        /// <summary>
        /// The percent a stored or hand-edited value is worth. Out-of-range
        /// input clamps into the band rather than throwing: settings.json is
        /// editable by hand, and a bad number must not be able to make the
        /// window invisible.
        /// </summary>
        public static int Clamp(int percent)
        {
            if (percent < MinPercent)
            {
                return MinPercent;
            }

            return percent > MaxPercent ? MaxPercent : percent;
        }

        /// <summary>
        /// The clamped percent as the 0-1 factor a control's Opacity takes.
        /// </summary>
        public static float ToFactor(int percent)
        {
            return Clamp(percent) / 100f;
        }

        /// <summary>
        /// The percent a slider's float value means, or false when the drag
        /// landed off the band entirely. Mirrors
        /// <see cref="ClickSoundVolume.TryPercentFromSliderValue"/>: a Blish
        /// TrackBar reports a float, and rounding it here keeps the fraction
        /// out of the persisted setting.
        /// </summary>
        public static bool TryPercentFromSliderValue(float value, out int percent)
        {
            percent = DefaultPercent;
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return false;
            }

            percent = Clamp((int)System.Math.Round(value));
            return true;
        }
    }
}
