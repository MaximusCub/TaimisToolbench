using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// How the Snapshot tab stacks its two titled, column-headed runs: a
    /// run with no rows costs nothing, the wallet run clears everything
    /// above it, and the panel height covers the last row.
    /// </summary>
    public class SnapshotResultLayoutTests
    {
        private const int GridWidth = 1252;
        private const int ItemRow = 56;
        private const int WalletRow = 36;
        private const int TitleBand = 38;
        private const int HeaderBand = 32;

        private static SnapshotResultLayout.Result Compute(int items, int wallet)
        {
            return SnapshotResultLayout.Compute(
                items, wallet, GridWidth, ItemRow, WalletRow, TitleBand, HeaderBand);
        }

        [Fact]
        public void BothRuns_StackTitleThenHeaderThenCells()
        {
            var layout = Compute(items: 2, wallet: 2);

            Assert.True(layout.Items.Present);
            Assert.Equal(0, layout.Items.TitleY);
            Assert.Equal(TitleBand, layout.Items.HeaderY);
            Assert.Equal(TitleBand + HeaderBand, layout.Items.Grid.Cells[0].Y);

            // Two columns at this width, so both item cells share one row.
            Assert.Equal(ItemRow, layout.Items.Grid.Height);
        }

        [Fact]
        public void WalletRun_ClearsTheItemRunAndTheGapBetweenThem()
        {
            var layout = Compute(items: 2, wallet: 2);

            int itemsBottom = TitleBand + HeaderBand + ItemRow;

            Assert.Equal(itemsBottom + SnapshotResultLayout.SectionGapY, layout.Wallet.TitleY);
            Assert.Equal(layout.Wallet.TitleY + TitleBand, layout.Wallet.HeaderY);
            Assert.Equal(layout.Wallet.HeaderY + HeaderBand, layout.Wallet.Grid.Cells[0].Y);
        }

        [Fact]
        public void AnEmptyRun_IsAbsentRatherThanEmpty_AndCostsNoHeight()
        {
            // Wallet filter: the absent item section must not reserve 70px
            // of nothing above the currencies.
            var layout = Compute(items: 0, wallet: 3);

            Assert.False(layout.Items.Present);
            Assert.Equal(0, layout.Items.Grid.Height);
            Assert.True(layout.Wallet.Present);
            Assert.Equal(0, layout.Wallet.TitleY);
        }

        [Fact]
        public void NoGapIsSpentBetweenSectionsWhenOnlyOneIsPresent()
        {
            var itemsOnly = Compute(items: 1, wallet: 0);

            Assert.Equal(
                TitleBand + HeaderBand + ItemRow + SnapshotResultLayout.TrailingClearance,
                itemsOnly.TotalHeight);
        }

        [Fact]
        public void TotalHeight_CoversTheLastRowOfTheLastSection()
        {
            // Nothing auto-sizes here: a short total clips its last row. The
            // total also clears the last row by TrailingClearance, so the
            // panel's bottom edge and a row's never share a y.
            var layout = Compute(items: 3, wallet: 1);

            int walletBottom = layout.Wallet.HeaderY + HeaderBand + layout.Wallet.Grid.Height;

            Assert.Equal(
                walletBottom + SnapshotResultLayout.TrailingClearance, layout.TotalHeight);
            Assert.True(layout.TotalHeight > layout.Wallet.Grid.Cells[0].Y);
        }

        [Fact]
        public void EmptyResultSet_IsZeroHeightAndNeitherSectionIsPresent()
        {
            var layout = Compute(items: 0, wallet: 0);

            Assert.False(layout.Items.Present);
            Assert.False(layout.Wallet.Present);
            Assert.Equal(0, layout.TotalHeight);
        }
    }
}
