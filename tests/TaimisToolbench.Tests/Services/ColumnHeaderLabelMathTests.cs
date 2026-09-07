using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The rule every table's left-hand header word reads: a column that
    /// opens its rows with an icon owns that icon, so the word rules on the
    /// gutter and not on the text beside it. Asserted against the shipped
    /// geometry of the four surfaces rather than against fixtures, so a
    /// column that moves its gutter cannot leave its header behind.
    /// </summary>
    public class ColumnHeaderLabelMathTests
    {
        /// <summary>
        /// One (textX, iconGutterX) pair per shipped column that draws an
        /// icon before its text, with the gutter width the rule recovers.
        /// </summary>
        public static readonly object[][] IconColumns =
        {
            new object[] { ShoppingColumnMath.NameX, ShoppingColumnMath.IconX },
            new object[]
            {
                SnapshotItemGridLayout.CellTextX(SnapshotItemGridLayout.AmountColumnFloor),
                SnapshotItemGridLayout.CellIconX(SnapshotItemGridLayout.AmountColumnFloor),
            },
            new object[] { SettingsCurrencyGridLayout.CellNameX, SettingsCurrencyGridLayout.CellIconX },
            new object[]
            {
                SummarySectionLayoutMath.CurrencyNameX, SummarySectionLayoutMath.CurrencyIconX,
            },
        };

        [Theory]
        [MemberData(nameof(IconColumns))]
        public void AColumnWithAnIconGutter_RulesOnTheGutter(int textX, int iconGutterX)
        {
            Assert.True(iconGutterX < textX, "the shipped column really does reserve a gutter");
            Assert.Equal(iconGutterX, ColumnHeaderLabelMath.LabelX(textX, iconGutterX));
        }

        /// <summary>
        /// Required Disciplines heads a column of plain text. Its header
        /// was already on its column's left edge and the rule must leave it
        /// exactly there.
        /// </summary>
        [Fact]
        public void AColumnWithNoIconGutter_KeepsItsTextRule()
        {
            Assert.Equal(
                DisciplinesColumnMath.NameX,
                ColumnHeaderLabelMath.LabelX(
                    DisciplinesColumnMath.NameX, ColumnHeaderLabelMath.NoIconGutter));
        }

        /// <summary>
        /// A gutter at or right of the text is not one the column draws
        /// through: honouring it would push the word right of its own text
        /// rule, and on the last column of a band that is out of the column
        /// altogether.
        /// </summary>
        [Theory]
        [InlineData(58, 58)]
        [InlineData(58, 100)]
        public void AGutterAtOrRightOfTheText_IsIgnored(int textX, int iconGutterX)
        {
            Assert.Equal(textX, ColumnHeaderLabelMath.LabelX(textX, iconGutterX));
        }

        /// <summary>
        /// The Ranker's variance: a rank column sits left of the Item
        /// column, and it is a column of its own. Item's word may reach its
        /// own gutter and no further left, at every width the module ships.
        /// </summary>
        [Theory]
        [MemberData(nameof(RankerRowLayoutTests.RealWidths), MemberType = typeof(RankerRowLayoutTests))]
        public void TheRankersItemHeader_StopsAtItsGutter_NotAtTheRankColumn(int rowWidth)
        {
            var bands = RankerRowLayout.Compute(rowWidth, remainingCellWidth: 120);

            int itemHeaderX = ColumnHeaderLabelMath.LabelX(bands.NameX, bands.IconX);

            Assert.Equal(bands.IconX, itemHeaderX);
            Assert.True(
                itemHeaderX >= bands.RankX + RankerRowLayout.RankWidth,
                "the rank column keeps its own band");
        }

        /// <summary>
        /// The hit area still agrees with the word. A sortable header's
        /// cell is the whole column (Services/HeaderCellMath), and the
        /// leftmost cell of a band starts at 0 - so pulling the word left
        /// to its gutter moves it further INTO its cell, never out of one.
        /// Modelled on Used Materials, whose Item column ends where its own
        /// ellipsis budget does.
        /// </summary>
        [Theory]
        [InlineData(600)]
        [InlineData(900)]
        [InlineData(1400)]
        public void TheWordStaysInsideTheCellThatSortsIt(int panelWidth)
        {
            const int amountBand = 60;
            const int nameToQtyGap = 12;
            int labelX = ColumnHeaderLabelMath.LabelX(
                ShoppingColumnMath.NameX, ShoppingColumnMath.IconX);
            int itemCellEnd = PlanRelayoutMath.HeaderSplitBeforeColumn(
                PlanRelayoutMath.PinnedRightEdge(panelWidth), amountBand, nameToQtyGap);

            var ranges = HeaderCellMath.Partition(
                panelWidth,
                new[]
                {
                    new HeaderCellMath.LabelExtent(labelX, 30, itemCellEnd),
                    new HeaderCellMath.LabelExtent(itemCellEnd + 20, 50),
                });

            Assert.Equal(0, ranges[0].X);
            Assert.True(labelX >= ranges[0].X);
            Assert.True(labelX < ranges[0].X + ranges[0].Width);
        }
    }
}
