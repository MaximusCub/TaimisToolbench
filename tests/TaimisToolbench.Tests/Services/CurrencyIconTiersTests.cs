using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // The currency counterpart of the item-icon tier pins: the two sizes
    // themselves, which module surface is on which tier, and the height
    // arithmetic that has to keep holding what those tiers draw.
    public class CurrencyIconTiersTests
    {
        // --- The measured pair ---
        //
        // Pinned absolutely, the same way the other geometry proofs in this
        // suite are, so a change here is a claim about the GAME rather than
        // about the module, and has to be re-measured rather than
        // re-derived. See Services/CurrencyIconTiers for each measurement.
        [Fact]
        public void WalletTiers_ArePinnedToTheMeasuredCaptures()
        {
            Assert.Equal(32, CurrencyIconTiers.WalletListIconSize);

            // 18, from our bar beside the game's in one screenshot: the
            // game's gold disc measures wider than ours at every threshold
            // tried. The tiers are NOT 2:1 - an earlier template match of
            // the 32x32 texture read the bar tier as 16, and the
            // side-by-side capture supersedes it. Do not assert that ratio
            // again.
            Assert.Equal(18, CurrencyIconTiers.WalletBarIconSize);
        }

        // --- Which surface sits on which tier ---
        [Fact]
        public void InlineCoinRuns_DrawAtTheBarTier()
        {
            // Every "number then unit icon" run - shopping Each/Total, the
            // recipe tree's cost sub-columns, the Ranker's remaining cost,
            // the Snapshot header's wallet total, plan history's cost
            // column - measures and draws through CoinSegmentMath, so this
            // one equality is what puts all of them on the bar tier.
            Assert.Equal(CurrencyIconTiers.WalletBarIconSize, CoinSegmentMath.CoinIconSize);

            // The currency half of such a run is frame-less now too (beside
            // digits it is a symbol, like the coins, not a
            // subject) but draws at the same measured window the framed
            // square used to occupy. That equality is what let the border
            // go on, and come off, without moving a single segment
            // advance - CoinIconSize is a term in the minimum-window-width
            // derivation.
            Assert.Equal(
                CoinSegmentMath.CoinIconSize,
                ItemIconTiers.FrameSize(ItemIconTier.CurrencyBarRun));
        }

        [Fact]
        public void CurrencyTableRows_DrawAtTheListTier()
        {
            // The Summary's currency table is a wallet list - one row per
            // currency, icon as the row's subject - so it takes the larger
            // tier, unlike the inline runs directly above it in the same
            // section.
            Assert.Equal(
                CurrencyIconTiers.WalletListIconSize,
                SummarySectionLayoutMath.CurrencyIconSize);

            // The renderer insets the art by the module's 1px frame either
            // side (CreateItemIcon with CurrencyIconSize - 2 and border 1),
            // so the FRAMED box occupies exactly the measured window rather
            // than overflowing it - the same rule ItemIconTiers states.
            const int borderThickness = 1;
            int artSize = SummarySectionLayoutMath.CurrencyIconSize - (2 * borderThickness);
            Assert.Equal(
                CurrencyIconTiers.WalletListIconSize,
                artSize + (2 * borderThickness));
        }

        [Fact]
        public void RankerCurrencyShortfallLines_DrawAtTheListTier()
        {
            // Superseding this line's original bar-tier seat: the Ranker's
            // breakdown is a currency LIST - a
            // grid of named entries with their own amounts - and it reads at
            // the size the game's wallet list uses. The line carries its own
            // taller pitch to hold it.
            Assert.Equal(CurrencyIconTiers.WalletListIconSize, RankerRowLayout.CurrencyIconSize);
            Assert.True(RankerRowLayout.CurrencyLineHeight >= RankerRowLayout.CurrencyIconSize);

            // The line reserves the FRAMED box, and the tier insets its art
            // inside the measured window - so the constant the view lays the
            // name out against is the same number the icon actually
            // occupies. It was 34 against a reserved 32 while the frame was
            // added outside the measurement.
            Assert.Equal(
                RankerRowLayout.CurrencyIconSize,
                ItemIconTiers.FrameSize(ItemIconTier.CurrencyListRow));
        }

        [Fact]
        public void SettingsCurrencyValuationRows_DrawAtTheListTier()
        {
            // The config panel's Currency
            // Valuations grid is the same one-row-per-currency table, so its
            // icon reads at the same size as the Summary's, and the row was
            // grown to hold it rather than the icon shrunk to fit the row.
            Assert.Equal(
                CurrencyIconTiers.WalletListIconSize, SettingsCurrencyGridLayout.CellIconSize);
            Assert.Equal(
                PlanContentHeightMath.CurrencyRowHeight,
                SettingsCurrencyGridLayout.CurrencyRowHeight);
        }

        // --- The heights that carry them ---
        [Fact]
        public void CurrencyRowHeight_HoldsItsListTierIcon_CentredAsTheGameCentresIt()
        {
            Assert.True(
                PlanContentHeightMath.CurrencyRowHeight >= CurrencyIconTiers.WalletListIconSize,
                "a currency row cannot be shorter than the icon it draws");

            // Even difference, so the centring the renderer computes as
            // (rowHeight - iconSize) / 2 is exact rather than rounded, and
            // lands on the 5px the game's own 42px wallet row leaves above
            // and below its 32px icon.
            int slack = PlanContentHeightMath.CurrencyRowHeight - CurrencyIconTiers.WalletListIconSize;
            Assert.Equal(0, slack % 2);
            Assert.Equal(PlanContentHeightMath.CurrencyRowIconPad, slack / 2);
            Assert.Equal(5, PlanContentHeightMath.CurrencyRowIconPad);
        }

        [Fact]
        public void AmountRunHeight_IsNeverShorterThanEitherThingTheRunHolds()
        {
            // The regression this exists for: the formula bands used to
            // reserve their amount run as "the coin icon size", which was
            // only ever right because an inline coin happened to draw at
            // the amount text's own line height. Moving the coins onto the
            // shorter bar tier broke that coincidence, and a reserve that
            // followed the icon down would have stopped the band
            // bottom-anchoring its amount.
            Assert.True(PlanContentHeightMath.AmountRunHeight >= CoinSegmentMath.CoinIconSize);
            Assert.True(PlanContentHeightMath.AmountRunHeight >= PlanContentHeightMath.AmountTextLineHeight);
            Assert.Equal(
                PlanContentHeightMath.AmountTextLineHeight,
                TypeRampMetrics.Regular16.LineHeight);
        }
    }
}
