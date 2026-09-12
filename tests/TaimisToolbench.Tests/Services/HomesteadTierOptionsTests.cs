using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class HomesteadTierOptionsTests
    {
        [Fact]
        public void All_ListsThreeOptionsInTierOrder()
        {
            IReadOnlyList<string> options = HomesteadTierOptions.All;

            Assert.Equal(3, options.Count);
            Assert.Equal(HomesteadTierOptions.None, options[0]);
            Assert.Equal(HomesteadTierOptions.OneUpgrade, options[1]);
            Assert.Equal(HomesteadTierOptions.BothUpgrades, options[2]);
        }

        [Theory]
        [InlineData(0, "None")]
        [InlineData(1, "One upgrade")]
        [InlineData(2, "Both upgrades")]
        public void TextForTier_StoredTier_ReturnsItsOption(int tier, string expected)
        {
            Assert.Equal(expected, HomesteadTierOptions.TextForTier(tier));
        }

        [Theory]
        [InlineData(-1, "None")]
        [InlineData(3, "Both upgrades")]
        [InlineData(150, "Both upgrades")]
        public void TextForTier_OutOfRange_ClampsToNearestEnd(int tier, string expected)
        {
            Assert.Equal(expected, HomesteadTierOptions.TextForTier(tier));
        }

        [Theory]
        [InlineData("None", 0)]
        [InlineData("One upgrade", 1)]
        [InlineData("Both upgrades", 2)]
        public void TierForText_OptionText_ReturnsItsStoredTier(string text, int expected)
        {
            Assert.Equal(expected, HomesteadTierOptions.TierForText(text));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("none")]
        [InlineData("2")]
        public void TierForText_UnrecognisedText_ReturnsZero(string text)
        {
            Assert.Equal(0, HomesteadTierOptions.TierForText(text));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void TierRoundTripsThroughItsOptionText(int tier)
        {
            Assert.Equal(tier, HomesteadTierOptions.TierForText(HomesteadTierOptions.TextForTier(tier)));
        }
    }
}
