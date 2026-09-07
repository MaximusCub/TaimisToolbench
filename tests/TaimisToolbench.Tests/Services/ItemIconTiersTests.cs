using System;
using System.Linq;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The icon-tier vocabulary is what stops a call site inventing a pixel
    /// size, so the vocabulary itself has to be total: every member answers,
    /// and the layout math that reserves room for an icon has to agree with
    /// the table the view draws from. The two used to be separate numbers.
    /// <para>
    /// Swept in a loop rather than as a [Theory]: ItemIconTier is internal
    /// (the module's default), and an xunit theory would have to put it on a
    /// public signature.
    /// </para>
    /// </summary>
    public class ItemIconTiersTests
    {
        [Fact]
        public void EveryTierHasAPositiveArtSizeAndBorder()
        {
            // A member added without a size would otherwise reach the throw
            // only when someone drew that icon at runtime.
            foreach (var tier in Enum.GetValues(typeof(ItemIconTier)).Cast<ItemIconTier>())
            {
                Assert.True(ItemIconTiers.ArtSize(tier) > 0, tier.ToString());
                Assert.True(ItemIconTiers.BorderThickness(tier) > 0, tier.ToString());
            }
        }

        [Fact]
        public void FrameSizeIsArtPlusBothBorders()
        {
            foreach (var tier in Enum.GetValues(typeof(ItemIconTier)).Cast<ItemIconTier>())
            {
                Assert.Equal(
                    ItemIconTiers.ArtSize(tier) + (2 * ItemIconTiers.BorderThickness(tier)),
                    ItemIconTiers.FrameSize(tier));
            }
        }

        [Fact]
        public void AnUnnamedTierThrowsRatherThanGuessingASize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ItemIconTiers.ArtSize((ItemIconTier)999));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => ItemIconTiers.BorderThickness((ItemIconTier)999));
        }

        [Fact]
        public void TheTwoGovernedTiersAreTheMeasuredInGameSizes()
        {
            Assert.Equal(ItemIconTiers.BagSlotIconSize, ItemIconTiers.ArtSize(ItemIconTier.BagSlot));
            Assert.Equal(ItemIconTiers.BagSidebarIconSize, ItemIconTiers.ArtSize(ItemIconTier.BagSidebar));
        }

        [Fact]
        public void PlanTabRowMathAgreesWithTheBagSidebarTier()
        {
            // PlanContentHeightMath derives every plan-tab icon row height
            // from these two numbers. If the tier table and the row math
            // ever disagree, the icon overflows or floats inside its row.
            Assert.Equal(
                PlanContentHeightMath.RowIconBorder,
                ItemIconTiers.BorderThickness(ItemIconTier.BagSidebar));
            Assert.Equal(
                PlanContentHeightMath.RowIconFrameSize,
                ItemIconTiers.FrameSize(ItemIconTier.BagSidebar));
        }

        [Fact]
        public void TreeRowShapeAgreesWithTheBagSidebarTier()
        {
            // The tree's name column is offset by the icon FRAME, so a
            // divergence here misaligns every tree row against every table
            // row beneath it.
            Assert.Equal(
                TreeRowShapePlanner.IconFrameSize,
                ItemIconTiers.FrameSize(ItemIconTier.BagSidebar));
        }

        [Fact]
        public void SnapshotAndRankerRowsAgreeWithTheBagSlotTier()
        {
            Assert.Equal(RankerRowLayout.IconSize, ItemIconTiers.ArtSize(ItemIconTier.BagSlot));

            // The Snapshot grid's text starts past the art, then 2px of
            // frame and 6px of gap - the icon is the term that has to track
            // the tier.
            Assert.Equal(
                SnapshotItemGridLayout.IconGutterWidth,
                ItemIconTiers.ArtSize(ItemIconTier.BagSlot) + 2 + 6);
        }

        [Fact]
        public void TheSearchSuggestionTiersFrameFillsItsRowBox()
        {
            // SuggestionPanel insets the art inside the 24px box the row
            // already reserved, rather than growing the box. 22 + 2 = 24.
            Assert.Equal(24, ItemIconTiers.FrameSize(ItemIconTier.SearchSuggestion));
        }

        [Fact]
        public void TheTooltipHeaderTierReservesTheGamesOwnHeaderBox()
        {
            // The game's tooltip header icon measures 34x34 physical on a
            // capture taken at the "Normal" GW2 UI size, and Blish paints
            // this tier's logical box through that size's 0.897 scale. A
            // tier set to the physical 34 painted 31 and read 3px small on
            // each axis beside the game's own tooltip.
            Assert.Equal(38, ItemIconTiers.FrameSize(ItemIconTier.TooltipHeader));
            Assert.Equal(
                34,
                (int)Math.Round(ItemIconTiers.FrameSize(ItemIconTier.TooltipHeader) * 0.897));
        }

        [Fact]
        public void TheCurrencyTiersFramedBoxIsTheMeasuredWalletWindow()
        {
            // The one place the item tiers and the currency tiers differ:
            // a bag slot's measurement is the ART (the game draws no frame
            // of its own, so the module's 1px sits outside it), while a
            // wallet icon's measurement is the whole BOX. So the currency
            // tiers inset, and their FRAME - not their art - is what has to
            // equal CurrencyIconTiers. Shipping a second copy of 32 and 16
            // is what this asserts against.
            Assert.Equal(
                CurrencyIconTiers.WalletListIconSize,
                ItemIconTiers.FrameSize(ItemIconTier.CurrencyListRow));
            Assert.Equal(
                CurrencyIconTiers.WalletBarIconSize,
                ItemIconTiers.FrameSize(ItemIconTier.CurrencyBarRun));
        }

        [Fact]
        public void TheSummaryCurrencyTableReservesTheCurrencyListTiersBox()
        {
            // SummarySectionLayoutMath lays the currency table's name column
            // out past this width. It and the tier must be the same number,
            // or the icon and the column it is measured against drift.
            Assert.Equal(
                SummarySectionLayoutMath.CurrencyIconSize,
                ItemIconTiers.FrameSize(ItemIconTier.CurrencyListRow));
        }

        [Fact]
        public void PlanHistoryRowsReserveTheItemTiersTheyDraw()
        {
            // Both Plan History tiers came from the merged history-parity
            // work, which put its rows on the shared item tiers. These
            // assertions are what keeps the tab's row-height arithmetic and
            // the icons it actually draws from parting company again.
            Assert.Equal(
                PlanHistoryRowLayout.IconTotal,
                ItemIconTiers.FrameSize(ItemIconTier.BagSlot));
            Assert.Equal(
                PlanHistoryRowLayout.DetailIconTotal,
                ItemIconTiers.FrameSize(ItemIconTier.BagSidebar));
        }
    }
}
