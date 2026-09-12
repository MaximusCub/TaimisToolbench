using System.Globalization;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Parses user-entered currency valuation text from the Settings tab
    /// into a positive copper-per-unit value. Kept separate from
    /// SettingsTabContent (Blish-coupled, untestable per repo invariant) so
    /// the actual validation logic is covered by a real, Blish-free test.
    /// Mirrors the constraint CurrencyValuation's constructor enforces
    /// (positive value, no coin-keyed entries handled by the caller) so a
    /// value that parses here is always safe to hand to that constructor.
    /// </summary>
    internal static class SettingsInputParser
    {
        /// <summary>
        /// Attempts to parse <paramref name="text"/> as a positive integer
        /// copper-per-unit valuation. Digits only - no sign, decimal point,
        /// or thousands separators. Returns false (with
        /// <paramref name="copperPerUnit"/> set to 0) for null, blank,
        /// non-numeric, zero, negative, decimal, or overflowing input.
        /// An empty/blank string is treated as "unset" by callers, not as
        /// an error - this method only judges whether non-blank text is a
        /// valid positive integer.
        /// </summary>
        public static bool TryParseCopperValue(string text, out long copperPerUnit)
        {
            copperPerUnit = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (!long.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed))
            {
                return false;
            }

            if (parsed <= 0)
            {
                return false;
            }

            copperPerUnit = parsed;
            return true;
        }

        // The Settings tab
        // accepts a human-friendly MB value for LogMaxSizeBytes ("2", not a
        // raw byte count) and converts here. 1-1000 MB is a generous but
        // still sane bound - large enough that no realistic retention
        // policy is blocked, small enough to catch an obvious typo (e.g. an
        // extra zero) before it is persisted.
        private const int MinLogSizeMb = 1;
        private const int MaxLogSizeMb = 1000;
        private const long BytesPerMb = 1024L * 1024L;

        /// <summary>
        /// Attempts to parse <paramref name="text"/> as a positive integer
        /// megabyte value (1-1000) and converts it to a byte count. Returns
        /// false (with <paramref name="maxSizeBytes"/> set to 0) for null,
        /// blank, non-numeric, or out-of-range input - mirrors
        /// TryParseCopperValue's own shape.
        /// </summary>
        public static bool TryParseLogMaxSizeMb(string text, out long maxSizeBytes)
        {
            maxSizeBytes = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsedMb))
            {
                return false;
            }

            if (parsedMb < MinLogSizeMb || parsedMb > MaxLogSizeMb)
            {
                return false;
            }

            maxSizeBytes = parsedMb * BytesPerMb;
            return true;
        }

        // "Clamp 1-365" for LogRetentionDays (dev/proposals/d2-log-system.md Section 5).
        private const int MinRetentionDays = 1;
        private const int MaxRetentionDays = 365;

        /// <summary>
        /// Attempts to parse <paramref name="text"/> as a positive integer
        /// retention-day count (1-365). Returns false (with
        /// <paramref name="retentionDays"/> set to 0) for null, blank,
        /// non-numeric, or out-of-range input.
        /// </summary>
        public static bool TryParseRetentionDays(string text, out int retentionDays)
        {
            retentionDays = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                return false;
            }

            if (parsed < MinRetentionDays || parsed > MaxRetentionDays)
            {
                return false;
            }

            retentionDays = parsed;
            return true;
        }

        // The Settings tab's
        // new "Snapshot" section accepts the refresh interval in minutes
        // (1-120) - same shape as TryParseRetentionDays above, just a
        // different range.
        private const int MinRefreshIntervalMinutes = 1;
        private const int MaxRefreshIntervalMinutes = 120;

        /// <summary>
        /// Attempts to parse <paramref name="text"/> as a positive integer
        /// snapshot refresh interval, in minutes (1-120). Returns false
        /// (with <paramref name="minutes"/> set to 0) for null, blank,
        /// non-numeric, or out-of-range input - mirrors TryParseRetentionDays'
        /// own shape.
        /// </summary>
        public static bool TryParseRefreshIntervalMinutes(string text, out int minutes)
        {
            minutes = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                return false;
            }

            if (parsed < MinRefreshIntervalMinutes || parsed > MaxRefreshIntervalMinutes)
            {
                return false;
            }

            minutes = parsed;
            return true;
        }

        // Mirrors ModuleSettings.GetClampedPlanHistoryMaxEntries' 5-200
        // bound - the parser rejects what the accessor would clamp, so a
        // typed value is either stored verbatim or refused with an error.
        private const int MinPlanHistoryMaxEntries = 5;
        private const int MaxPlanHistoryMaxEntries = 200;

        /// <summary>
        /// Attempts to parse <paramref name="text"/> as a Plan History
        /// entry cap (5-200). Returns false (with
        /// <paramref name="maxEntries"/> set to 0) for null, blank,
        /// non-numeric, or out-of-range input - mirrors
        /// TryParseRetentionDays' own shape.
        /// </summary>
        public static bool TryParsePlanHistoryMaxEntries(string text, out int maxEntries)
        {
            maxEntries = 0;

            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string trimmed = text.Trim();

            if (!int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed))
            {
                return false;
            }

            if (parsed < MinPlanHistoryMaxEntries || parsed > MaxPlanHistoryMaxEntries)
            {
                return false;
            }

            maxEntries = parsed;
            return true;
        }
    }
}
