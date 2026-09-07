using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The module's column-grid law, swept width by width across the whole
    /// range a player can drag the window to, for all three grids at once.
    ///
    /// The three used to state the law three times - SnapshotItemGridLayout,
    /// SettingsCurrencyGridLayout and ColumnBoardLayout each had their own
    /// ComputeColumnCount and ComputeColumnWidth - and they had already
    /// drifted once, holding 454px of content in a 1210px column at a wide
    /// window. This golden was captured against those three separate
    /// implementations BEFORE they were collapsed onto Services/GridLayout,
    /// so an identical sweep afterwards is proof the collapse changed no
    /// pixel at any width, not merely that the older assertions still pass.
    ///
    /// Two columns have been re-captured since, each because a CELL's
    /// minimum width grew: the currency ones when the Settings cell gained
    /// its leading icon, the snapshot ones when the Amount header gained a
    /// persistent sort indicator and again when the Amount column took an
    /// 8px left inset. The board columns are still the original
    /// pre-collapse capture, and the law itself has never moved.
    /// </summary>
    public class GridLawGoldenTests
    {
        private const int MinWidth = 400;
        private const int MaxWidth = 2400;

        // Representative cell counts and row heights: enough to exercise
        // the row-count division at every column count in range without
        // making the golden about the counts.
        private const int CellCount = 17;
        private const int SnapshotRowHeight = 56;
        private const int CurrencyRowHeight = 30;

        [Fact]
        public void TheSweepIsUnchanged()
        {
            string goldenPath = Path.Combine(AppContext.BaseDirectory, "Goldens", "grid-law-sweep.txt");
            Assert.True(File.Exists(goldenPath), "Golden not found at " + goldenPath);

            var expected = File.ReadAllLines(goldenPath);
            var actual = BuildSweep();

            Assert.Equal(expected.Length, actual.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                // Line-by-line so a failure names the width that moved
                // rather than dumping 2001 rows.
                Assert.Equal(expected[i], actual[i]);
            }
        }

        internal static List<string> BuildSweep()
        {
            var lines = new List<string>(MaxWidth - MinWidth + 2)
            {
                "width|snapCols|snapColW|snapH|curCols|curColW|curH|boardCols4|boardColW4|boardH4|boardCols1",
            };

            var blockHeights = new[] { 100, 60, 140, 80 };

            for (int width = MinWidth; width <= MaxWidth; width++)
            {
                int snapCols = SnapshotItemGridLayout.ComputeColumnCount(width);
                int curCols = SettingsCurrencyGridLayout.ComputeColumnCount(width);
                int boardCols4 = ColumnBoardLayout.ComputeColumnCount(
                    width, SettingsFormLayout.SettingsFormMinColumnWidth, blockHeights.Length);
                var board = ColumnBoardLayout.Compute(
                    blockHeights, width, SettingsFormLayout.SettingsFormMinColumnWidth,
                    SettingsFormLayout.SettingsRowGap);

                var sb = new StringBuilder(64);
                sb.Append(width).Append('|')
                    .Append(snapCols).Append('|')
                    .Append(SnapshotItemGridLayout.ComputeColumnWidth(width)).Append('|')
                    .Append(SnapshotItemGridLayout.ComputeHeight(CellCount, width, SnapshotRowHeight)).Append('|')
                    .Append(curCols).Append('|')
                    .Append(SettingsCurrencyGridLayout.ComputeColumnWidth(width)).Append('|')
                    .Append(SettingsCurrencyGridLayout.ComputeHeight(CellCount, width, CurrencyRowHeight)).Append('|')
                    .Append(boardCols4).Append('|')
                    .Append(ColumnBoardLayout.ComputeColumnWidth(width, boardCols4)).Append('|')
                    .Append(board.Height).Append('|')
                    .Append(ColumnBoardLayout.ComputeColumnCount(width, SettingsFormLayout.SettingsFormMinColumnWidth, 1));

                lines.Add(sb.ToString());
            }

            return lines;
        }
    }
}
