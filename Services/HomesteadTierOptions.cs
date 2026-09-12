using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The three choices the Settings tab offers for one Homestead
    /// Refinement material, and the mapping between an option's on-screen
    /// text and the tier integer ModuleSettings persists.
    ///
    /// <para>
    /// The stored value stays an integer 0-2, so an account configured
    /// before this list existed keeps the tier it had. Blish-free, so the
    /// mapping is covered by a real test.
    /// </para>
    /// </summary>
    internal static class HomesteadTierOptions
    {
        public const string None = "None";
        public const string OneUpgrade = "One upgrade";
        public const string BothUpgrades = "Both upgrades";

        private static readonly string[] OptionsByTier = { None, OneUpgrade, BothUpgrades };

        /// <summary>The options in tier order, tier 0 first.</summary>
        public static IReadOnlyList<string> All
        {
            get { return Array.AsReadOnly(OptionsByTier); }
        }

        /// <summary>
        /// Option text for a stored tier. A value outside 0-2 is clamped to
        /// the nearest end, matching ModuleSettings.GetHomesteadEfficiencyTiers
        /// so a hand-edited settings file shows the tier the plan will use.
        /// </summary>
        public static string TextForTier(int tier)
        {
            if (tier < 0)
            {
                return OptionsByTier[0];
            }

            if (tier >= OptionsByTier.Length)
            {
                return OptionsByTier[OptionsByTier.Length - 1];
            }

            return OptionsByTier[tier];
        }

        /// <summary>
        /// Stored tier for an option's text. Null or unrecognised text maps
        /// to 0, so a dropdown with nothing selected cannot write a tier the
        /// user never chose.
        /// </summary>
        public static int TierForText(string text)
        {
            for (int i = 0; i < OptionsByTier.Length; i++)
            {
                if (string.Equals(OptionsByTier[i], text, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return 0;
        }
    }
}
