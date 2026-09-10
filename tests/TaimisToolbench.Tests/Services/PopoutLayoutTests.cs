using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class PopoutLayoutTests
    {
        private static List<PlanRowViewModel> ShoppingRows(int count)
        {
            var rows = new List<PlanRowViewModel>();
            for (int i = 0; i < count; i++)
            {
                rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.ShoppingBuy,
                    ItemId = 19700 + i,
                    Label = "Row " + i,
                    Quantity = 1,
                });
            }

            return rows;
        }

        /// <summary>
        /// The floor exists so the table inside a popout is the same table
        /// the plan tab draws. Below the width ShoppingColumnMath can
        /// distribute four data tracks over, the columns pack right to left
        /// instead, which is a different picture.
        /// </summary>
        [Fact]
        public void MinContentWidth_KeepsTheShoppingTableInItsDistributedRegime()
        {
            int tableWidth = PopoutLayout.TableWidth(
                PopoutLayout.MinContentWidth(PlanSectionType.ShoppingList));

            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                tableWidth,
                ShoppingColumnMath.EachMinWidth,
                ShoppingColumnMath.TotalMinWidth,
                ShoppingColumnMath.TotalMinWidth,
                ShoppingColumnMath.TotalMinWidth,
                0);

            Assert.True(edges.Distributed);
            Assert.True(edges.NameColumnWidth >= ShoppingColumnMath.NameMinWidth);
        }

        [Fact]
        public void MinContentWidth_OneLessPixelDropsTheTableOutOfIt()
        {
            int tableWidth = PopoutLayout.TableWidth(
                PopoutLayout.MinContentWidth(PlanSectionType.ShoppingList)) - 1;

            var edges = ShoppingColumnMath.ComputeEdgesForPanel(
                tableWidth,
                ShoppingColumnMath.EachMinWidth,
                ShoppingColumnMath.TotalMinWidth,
                ShoppingColumnMath.TotalMinWidth,
                ShoppingColumnMath.TotalMinWidth,
                0);

            Assert.False(edges.Distributed);
        }

        /// <summary>
        /// The renderers are handed this width as their panel width, so a
        /// table laid out at it must end inside the content box with the
        /// tick column and the scrollbar strip both still clear.
        /// </summary>
        [Fact]
        public void TableWidth_LeavesTheTickColumnAndTheScrollbarStripClear()
        {
            const int Content = 1200;
            int table = PopoutLayout.TableWidth(Content);

            Assert.Equal(
                Content,
                table + PopoutLayout.CheckColumnWidth + WindowSizing.ScrollbarAllowance);
        }

        [Fact]
        public void TableWidth_NeverGoesNegativeOnACollapsedWindow()
        {
            Assert.Equal(0, PopoutLayout.TableWidth(0));
            Assert.Equal(0, PopoutLayout.TableWidth(10));
        }

        [Fact]
        public void MinContentWidth_TheCraftingStepsFloorIsNarrowerThanTheShoppingOne()
        {
            Assert.True(
                PopoutLayout.MinContentWidth(PlanSectionType.CraftingSteps)
                < PopoutLayout.MinContentWidth(PlanSectionType.ShoppingList));
        }

        [Fact]
        public void DefaultContentHeight_OpensAtTenRowsForALongList()
        {
            int height = PopoutLayout.DefaultContentHeight(
                PlanSectionType.ShoppingList, ShoppingRows(60));

            int expected = PopoutLayout.ChromeHeight
                + PlanContentHeightMath.ColumnHeaderRowHeight
                + (PopoutLayout.DefaultVisibleRows * PlanContentHeightMath.ShoppingRowHeight);

            Assert.Equal(expected, height);
        }

        [Fact]
        public void DefaultContentHeight_OpensShortForAShortList()
        {
            int height = PopoutLayout.DefaultContentHeight(
                PlanSectionType.ShoppingList, ShoppingRows(3));

            int expected = PopoutLayout.ChromeHeight
                + PlanContentHeightMath.ColumnHeaderRowHeight
                + (3 * PlanContentHeightMath.ShoppingRowHeight);

            Assert.Equal(expected, height);
        }

        /// <summary>
        /// Every box has to land on the row it belongs to, so the last one's
        /// offset plus a box is inside the table body the renderer builds.
        /// </summary>
        [Fact]
        public void CheckboxYOffsets_LandInsideTheTableTheRendererDraws()
        {
            var rows = ShoppingRows(8);
            var offsets = PopoutLayout.CheckboxYOffsets(PlanSectionType.ShoppingList, rows);
            int bodyHeight = PlanContentHeightMath.SectionBodyHeight(
                PlanSectionType.ShoppingList, rows);

            Assert.Equal(rows.Count, offsets.Count);
            for (int i = 0; i < offsets.Count; i++)
            {
                int rowTop = PlanContentHeightMath.ColumnHeaderRowHeight
                    + (i * PlanContentHeightMath.ShoppingRowHeight);

                Assert.True(offsets[i] >= rowTop);
                Assert.True(
                    offsets[i] + PopoutLayout.CheckboxSize
                    <= rowTop + PlanContentHeightMath.ShoppingRowHeight);
            }

            Assert.True(offsets[offsets.Count - 1] + PopoutLayout.CheckboxSize <= bodyHeight);
        }

        /// <summary>
        /// A Crafting Steps section can carry a plain notice line among its
        /// numbered steps. A notice is prose, not a step, so it takes no
        /// box - but the steps under it still have to keep their own rows.
        /// </summary>
        [Fact]
        public void CheckboxYOffsets_SkipANoticeLineAndKeepTheStepsBelowItAligned()
        {
            var rows = new List<PlanRowViewModel>
            {
                new PlanRowViewModel { RowType = PlanRowType.CraftStep, Label = "Craft A" },
                new PlanRowViewModel { RowType = PlanRowType.TimegatedNotice, Label = "Capped" },
                new PlanRowViewModel { RowType = PlanRowType.CraftStep, Label = "Craft B" },
            };

            var offsets = PopoutLayout.CheckboxYOffsets(PlanSectionType.CraftingSteps, rows);

            Assert.True(offsets[0] >= 0);
            Assert.Equal(-1, offsets[1]);

            int thirdRowTop = PlanContentHeightMath.CraftStepRowHeight
                + PlanContentHeightMath.FallbackTextRowHeight;
            Assert.True(offsets[2] >= thirdRowTop);
            Assert.True(
                offsets[2] + PopoutLayout.CheckboxSize
                <= thirdRowTop + PlanContentHeightMath.CraftStepRowHeight);
        }

        [Fact]
        public void CheckboxYOffsets_HandleAnEmptyList()
        {
            Assert.Empty(PopoutLayout.CheckboxYOffsets(
                PlanSectionType.ShoppingList, new List<PlanRowViewModel>()));
            Assert.Empty(PopoutLayout.CheckboxYOffsets(PlanSectionType.ShoppingList, null));
        }
    }
}
