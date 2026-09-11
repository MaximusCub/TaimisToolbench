using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class PopoutToolbarLayoutTests
    {
        // "Opacity" at the Status tier, plus the padding a Blish Label
        // reports around it. Given rather than measured: BitmapFont is
        // Blish-bound. The assertions below hold for any width, and the
        // widths either side of this one are covered by their own cases.
        private const int CaptionWidth = 62;

        private const int RefreshButtonWidth = 90;

        /// <summary>
        /// The reported defect: the slider's left end was drawn over the
        /// last letter of "Opacity". The caption was given a fixed 60px
        /// band and the slider was seated off that band rather than off the
        /// word, so a wider word ran under it.
        /// </summary>
        [Fact]
        public void TheSliderOpensPastTheWholeCaption_NotPastAFixedBand()
        {
            Assert.True(
                PopoutToolbarLayout.SliderX(CaptionWidth)
                >= PopoutToolbarLayout.CaptionX + CaptionWidth);
            Assert.Equal(
                PopoutToolbarLayout.SliderGap,
                PopoutToolbarLayout.SliderX(CaptionWidth) - CaptionWidth);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-20)]
        [InlineData(400)]
        public void AnyCaptionWidth_KeepsTheClusterInOrder(int captionWidth)
        {
            int slider = PopoutToolbarLayout.SliderX(captionWidth);
            int readout = PopoutToolbarLayout.ReadoutX(captionWidth);

            Assert.True(slider >= PopoutToolbarLayout.CaptionX);
            Assert.True(readout >= slider + SettingsFormLayout.SliderWidth);
            Assert.Equal(
                readout + SettingsFormLayout.ReadoutWidth,
                PopoutToolbarLayout.OpacityClusterRightEdge(captionWidth));
        }

        /// <summary>
        /// The opacity cluster moved to the left of the strip and Refresh
        /// to the right. At the narrowest window either popout opens at,
        /// the two must still not meet.
        /// </summary>
        [Fact]
        public void AtTheWindowMinimum_RefreshClearsTheOpacityCluster()
        {
            var sections = new[] { PlanSectionType.ShoppingList, PlanSectionType.CraftingSteps };

            foreach (var sectionType in sections)
            {
                int contentWidth = PopoutLayout.MinContentWidth(sectionType);

                Assert.True(
                    PopoutToolbarLayout.RefreshX(contentWidth, RefreshButtonWidth)
                    > PopoutToolbarLayout.OpacityClusterRightEdge(CaptionWidth));
                Assert.True(
                    contentWidth
                    >= PopoutToolbarLayout.MinContentWidth(CaptionWidth, RefreshButtonWidth));
            }
        }

        /// <summary>
        /// The button rules with the table under it, which gives up the
        /// scrollbar strip, rather than with the window frame.
        /// </summary>
        [Fact]
        public void RefreshPinsToTheTablesRightEdge_NotTheContentBoxes()
        {
            const int contentWidth = 900;

            Assert.Equal(
                contentWidth - WindowSizing.ScrollbarAllowance,
                PopoutToolbarLayout.RefreshRightEdge(contentWidth));
            Assert.Equal(
                PopoutToolbarLayout.RefreshRightEdge(contentWidth) - RefreshButtonWidth,
                PopoutToolbarLayout.RefreshX(contentWidth, RefreshButtonWidth));
        }

        [Fact]
        public void AWiderWindow_MovesRefreshByTheWholeIncrease()
        {
            int narrow = PopoutToolbarLayout.RefreshX(900, RefreshButtonWidth);
            int wide = PopoutToolbarLayout.RefreshX(1200, RefreshButtonWidth);

            Assert.Equal(300, wide - narrow);
        }
    }
}
