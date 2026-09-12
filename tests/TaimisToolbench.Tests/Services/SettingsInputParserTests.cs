using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class SettingsInputParserTests
    {
        [Theory]
        [InlineData("1")]
        [InlineData("150")]
        [InlineData("007")]
        [InlineData("  42  ")]
        [InlineData("9223372036854775807")] // long.MaxValue
        public void TryParseCopperValue_ValidPositiveIntegerText_ReturnsTrueWithValue(string text)
        {
            bool ok = SettingsInputParser.TryParseCopperValue(text, out long value);

            Assert.True(ok);
            Assert.True(value > 0);
        }

        [Fact]
        public void TryParseCopperValue_TypicalValue_ParsesExactAmount()
        {
            bool ok = SettingsInputParser.TryParseCopperValue("1200", out long value);

            Assert.True(ok);
            Assert.Equal(1200, value);
        }

        [Fact]
        public void TryParseCopperValue_SurroundingWhitespace_IsTrimmed()
        {
            bool ok = SettingsInputParser.TryParseCopperValue("  250  ", out long value);

            Assert.True(ok);
            Assert.Equal(250, value);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryParseCopperValue_NullOrBlank_ReturnsFalseWithZero(string text)
        {
            bool ok = SettingsInputParser.TryParseCopperValue(text, out long value);

            Assert.False(ok);
            Assert.Equal(0, value);
        }

        [Fact]
        public void TryParseCopperValue_Zero_ReturnsFalse()
        {
            bool ok = SettingsInputParser.TryParseCopperValue("0", out long value);

            Assert.False(ok);
            Assert.Equal(0, value);
        }

        [Theory]
        [InlineData("-5")]
        [InlineData("-1")]
        public void TryParseCopperValue_Negative_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParseCopperValue(text, out long value);

            Assert.False(ok);
            Assert.Equal(0, value);
        }

        [Theory]
        [InlineData("1.5")]
        [InlineData("1,000")]
        [InlineData("+5")]
        [InlineData("abc")]
        [InlineData("5g")]
        [InlineData("5 5")]
        [InlineData("1e5")]
        public void TryParseCopperValue_NonIntegerOrMalformedText_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParseCopperValue(text, out long value);

            Assert.False(ok);
            Assert.Equal(0, value);
        }

        [Fact]
        public void TryParseCopperValue_OverflowsLong_ReturnsFalse()
        {
            // long.MaxValue + a trailing digit
            bool ok = SettingsInputParser.TryParseCopperValue("92233720368547758070", out long value);

            Assert.False(ok);
            Assert.Equal(0, value);
        }

        // --- TryParseLogMaxSizeMb (log system) ---
        [Theory]
        [InlineData("1", 1)]
        [InlineData("2", 2)]
        [InlineData("1000", 1000)]
        [InlineData("  50  ", 50)]
        public void TryParseLogMaxSizeMb_ValidRange_ReturnsTrueWithByteCount(string text, int expectedMb)
        {
            bool ok = SettingsInputParser.TryParseLogMaxSizeMb(text, out long maxSizeBytes);

            Assert.True(ok);
            Assert.Equal((long)expectedMb * 1024 * 1024, maxSizeBytes);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryParseLogMaxSizeMb_NullOrBlank_ReturnsFalseWithZero(string text)
        {
            bool ok = SettingsInputParser.TryParseLogMaxSizeMb(text, out long maxSizeBytes);

            Assert.False(ok);
            Assert.Equal(0, maxSizeBytes);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("1001")]
        [InlineData("99999")]
        public void TryParseLogMaxSizeMb_OutOfRange_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParseLogMaxSizeMb(text, out long maxSizeBytes);

            Assert.False(ok);
            Assert.Equal(0, maxSizeBytes);
        }

        [Theory]
        [InlineData("1.5")]
        [InlineData("+1")]
        [InlineData("abc")]
        [InlineData("2MB")]
        public void TryParseLogMaxSizeMb_MalformedText_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParseLogMaxSizeMb(text, out long maxSizeBytes);

            Assert.False(ok);
            Assert.Equal(0, maxSizeBytes);
        }

        // --- TryParseRetentionDays (log system) ---
        [Theory]
        [InlineData("1", 1)]
        [InlineData("14", 14)]
        [InlineData("365", 365)]
        [InlineData("  7  ", 7)]
        public void TryParseRetentionDays_ValidRange_ReturnsTrueWithValue(string text, int expected)
        {
            bool ok = SettingsInputParser.TryParseRetentionDays(text, out int days);

            Assert.True(ok);
            Assert.Equal(expected, days);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryParseRetentionDays_NullOrBlank_ReturnsFalseWithZero(string text)
        {
            bool ok = SettingsInputParser.TryParseRetentionDays(text, out int days);

            Assert.False(ok);
            Assert.Equal(0, days);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("366")]
        public void TryParseRetentionDays_OutOfRange_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParseRetentionDays(text, out int days);

            Assert.False(ok);
            Assert.Equal(0, days);
        }

        [Theory]
        [InlineData("1.5")]
        [InlineData("+1")]
        [InlineData("abc")]
        [InlineData("14 days")]
        public void TryParseRetentionDays_MalformedText_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParseRetentionDays(text, out int days);

            Assert.False(ok);
            Assert.Equal(0, days);
        }

        // --- TryParseRefreshIntervalMinutes (dev/proposals/d1-snapshot-about-settings.md Feature 3) ---
        [Theory]
        [InlineData("1", 1)]
        [InlineData("10", 10)]
        [InlineData("120", 120)]
        [InlineData("  30  ", 30)]
        public void TryParseRefreshIntervalMinutes_ValidRange_ReturnsTrueWithValue(string text, int expected)
        {
            bool ok = SettingsInputParser.TryParseRefreshIntervalMinutes(text, out int minutes);

            Assert.True(ok);
            Assert.Equal(expected, minutes);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void TryParseRefreshIntervalMinutes_NullOrBlank_ReturnsFalseWithZero(string text)
        {
            bool ok = SettingsInputParser.TryParseRefreshIntervalMinutes(text, out int minutes);

            Assert.False(ok);
            Assert.Equal(0, minutes);
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-1")]
        [InlineData("121")]
        [InlineData("99999")]
        public void TryParseRefreshIntervalMinutes_OutOfRange_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParseRefreshIntervalMinutes(text, out int minutes);

            Assert.False(ok);
            Assert.Equal(0, minutes);
        }

        [Theory]
        [InlineData("1.5")]
        [InlineData("+1")]
        [InlineData("abc")]
        [InlineData("10 minutes")]
        public void TryParseRefreshIntervalMinutes_MalformedText_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParseRefreshIntervalMinutes(text, out int minutes);

            Assert.False(ok);
            Assert.Equal(0, minutes);
        }

        [Theory]
        [InlineData("5", 5)]
        [InlineData("25", 25)]
        [InlineData("200", 200)]
        [InlineData(" 25 ", 25)]
        public void TryParsePlanHistoryMaxEntries_ValidText_ReturnsValue(string text, int expected)
        {
            bool ok = SettingsInputParser.TryParsePlanHistoryMaxEntries(text, out int maxEntries);

            Assert.True(ok);
            Assert.Equal(expected, maxEntries);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("4")]
        [InlineData("0")]
        [InlineData("201")]
        [InlineData("-25")]
        [InlineData("2.5")]
        [InlineData("abc")]
        [InlineData("25 plans")]
        public void TryParsePlanHistoryMaxEntries_InvalidText_ReturnsFalse(string text)
        {
            bool ok = SettingsInputParser.TryParsePlanHistoryMaxEntries(text, out int maxEntries);

            Assert.False(ok);
            Assert.Equal(0, maxEntries);
        }
    }
}
