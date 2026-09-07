using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // Each column-header table's section renderer (Disciplines/Recipes)
    // calls this from inside its own Render(), the way
    // ShoppingListSectionRenderer owns CreateShoppingListHeaderRow.
    //
    // Optional middleLabel/middleX so Required Disciplines
    // can honestly label its per-character availability text with a
    // "Characters" header once that text lines up on a fixed column (see
    // DisciplinesSectionRenderer's own comment on Render() for why the
    // column has to be fixed, not per-row). Defaulted to null/0 so the
    // Required Recipes call site (still a plain left/right header) needs no
    // change. Like leftLabel, middleLabel sits at a fixed X computed once
    // by the caller before this row is built.
    //
    // middleXForWidth exists for the ONE caller whose middle column is not
    // fixed: the Recipe Tree's "Source" header sits over the decision-pill
    // column, whose x is derived from the panel width
    // (PlanRelayoutMath.ComputeTreeColumnEdges), so a build-time constant
    // would strand it the moment the window is dragged. Supplying it opts
    // that label into the AddRelayout closure below; omitting it keeps the
    // previous fixed-x behaviour for every other caller. Still
    // position-only, per the interface's "position/width-only" contract -
    // see ISectionRelayoutSink.AddRelayout's doc comment.
    //
    // rightXForWidth is the same escape hatch for the right label: the
    // Recipe Tree's "Cost" header sits over a column whose x is derived
    // through PlanRelayoutMath.ComputeTreeColumnEdges rather than straight
    // off the panel edge, so it tracks the same arithmetic its rows do.
    // Omitting it anchors the label at PlanRelayoutMath.PinnedRightEdge,
    // which is what every flat table wants.
    //
    // rightLabelXForWidth places the right label OUTRIGHT rather than
    // right-aligning it, for the caller whose header has to centre over the
    // band its values occupy instead of merely ending where they end (see
    // Services/JustifiedColumnTracks). rightXForWidth still owns the
    // column's own right edge, and so the band width and the cell split;
    // only where the word is drawn changes.
    //
    // Chrome (band color, font, label color, height, label y) comes from
    // the shared HeaderBands - see that class for the inventory and
    // the reason the band, rather than the Shopping List's lighter
    // treatment, is the one every plan table now uses.
    //
    // It also sizes the header BAND, which spans the panel width. The band
    // is the table's own background, and every column the table draws is
    // inside that panel - including a trailing action column that no header
    // word sits over.
    // leftColumnEndForWidth: where the flexing name column really ends,
    // so its header cell reaches the band pinned to its right rather than
    // stopping between two words (HeaderCellMath.LabelExtent). Omitted by
    // the inert headers, whose cells answer nothing.
    // onLeftClick/onRightClick turn those two labels into sort controls
    // for the one caller that has a sortable table (Used Materials).
    // Omitted everywhere else, which leaves the label inert exactly as
    // before. leftSort/rightSort seat that column's persistent sort
    // indicator (Views/Rendering/SortIndicator) beside the word; the
    // label's own x-tracking below right-aligns off the measured BLOCK
    // width, which is the same in all three sort states.
    // The registered relayout closure is also RETURNED, for the one caller
    // that has to re-run the header's placement between resizes: the
    // Recipe Tree's "Source" header centres over the ink its decision
    // pills cover, and that is only known once the rows below it have
    // been built. Re-running it repositions from the same arithmetic a
    // resize would, so there is no second placement path to keep in step.
    internal static class ColumnHeaderRowRenderer
    {
        internal static Action<int> CreateColumnHeaderRow(
            FlowPanel parent, int panelWidth, string leftLabel, int leftX, string rightLabel, ISectionRelayoutSink sink,
            string middleLabel = null, int middleX = 0, Func<int, int> middleXForWidth = null,
            Func<int, int> rightXForWidth = null, Action onLeftClick = null, Action onRightClick = null,
            Func<int, int> leftColumnEndForWidth = null, Func<int, int> rightLabelXForWidth = null,
            TableSortDirection? leftSort = null, TableSortDirection? rightSort = null,
            Func<int> rowsHeight = null)
        {
            var flowBand = HeaderBands.CreateColumnHeaderBandInFlow(parent, panelWidth);
            var rowPanel = flowBand.Band;
            var font = HeaderBands.Font;
            var leftBlock = SortableHeaderBlock.Create(
                rowPanel, font, HeaderBands.LabelColor, HeaderBands.LabelY, leftLabel, leftSort);
            leftBlock.MoveTo(leftX);
            Label middleLabelControl = null;
            if (!string.IsNullOrEmpty(middleLabel))
            {
                middleLabelControl = LabelHelpers.WithDescenderClearance(new Label()
                {
                    Text = middleLabel, Font = font, TextColor = HeaderBands.LabelColor,
                    AutoSizeWidth = true, AutoSizeHeight = true,
                    Location = new Point(
                        middleXForWidth != null ? middleXForWidth(panelWidth) : middleX, HeaderBands.LabelY),
                    Parent = rowPanel,
                });
            }

            var rightBlock = SortableHeaderBlock.Create(
                rowPanel, font, HeaderBands.LabelColor, HeaderBands.LabelY, rightLabel, rightSort);
            rightBlock.MoveTo(
                RightLabelX(panelWidth, rightXForWidth, rightLabelXForWidth, rightBlock.Width));

            // The hit area is the whole cell (SortableHeaderCells); the
            // labels only carry the note, which they would swallow.
            if (onLeftClick != null)
            {
                SortableHeaderLabel.MarkSortable(leftBlock.Title);
                SortableHeaderLabel.MarkSortable(leftBlock.IndicatorLabel);
            }

            if (onRightClick != null)
            {
                SortableHeaderLabel.MarkSortable(rightBlock.Title);
                SortableHeaderLabel.MarkSortable(rightBlock.IndicatorLabel);
            }

            // Everything the split needs that does NOT move with the panel
            // width, resolved once, so the closure below neither measures
            // a string nor allocates.
            var plan = new HeaderCellPlan(
                middleLabelControl == null ? 2 : 3, new SortableHeaderCells(rowPanel));
            plan.Set(0, leftBlock.Title, leftBlock.Width, onLeftClick, leftBlock.IndicatorLabel);
            if (middleLabelControl != null)
            {
                plan.Set(1, middleLabelControl, Measure(font, middleLabel), null);
            }

            plan.Set(
                plan.Count - 1, rightBlock.Title, rightBlock.Width, onRightClick,
                rightBlock.IndicatorLabel);
            if (leftColumnEndForWidth != null)
            {
                plan.SetBoundary(0, leftColumnEndForWidth(panelWidth));
            }

            plan.Sync(rowPanel.Width);

            Action<int> relayout = w =>
            {
                flowBand.Resize(w);
                rightBlock.MoveTo(
                    RightLabelX(w, rightXForWidth, rightLabelXForWidth, rightBlock.Width));
                if (middleLabelControl != null && middleXForWidth != null)
                {
                    middleLabelControl.Location = new Point(middleXForWidth(w), HeaderBands.LabelY);
                }

                // A right-pinned column's edge moves with the panel.
                if (leftColumnEndForWidth != null)
                {
                    plan.SetBoundary(0, leftColumnEndForWidth(w));
                }

                plan.Sync(rowPanel.Width);
            };
            sink.AddRelayout(relayout);
            if (rowsHeight != null)
            {
                sink.TrackStickyBand(flowBand, rowsHeight);
            }

            return relayout;
        }

        /// <summary>
        /// Left edge of the right header BLOCK - its word plus any indicator
        /// - so a sortable header right-aligns on the same edge its cells do
        /// rather than hanging its indicator past it.
        /// </summary>
        private static int RightLabelX(
            int panelWidth, Func<int, int> rightXForWidth, Func<int, int> rightLabelXForWidth,
            int blockWidth)
        {
            if (rightLabelXForWidth != null)
            {
                return rightLabelXForWidth(panelWidth);
            }

            int rightEdge = rightXForWidth != null
                ? rightXForWidth(panelWidth)
                : panelWidth - PlanRelayoutMath.TableRightMargin;
            return PlanRelayoutMath.RightAlignedX(rightEdge, blockWidth);
        }

        /// <summary>Measured from the string, not read off the control: a
        /// Blish Label's Width is not settled until its next layout pass,
        /// and these cells are described as the label is created.</summary>
        private static int Measure(BitmapFont font, string text)
        {
            return (int)Math.Ceiling(font.MeasureString(text ?? "").Width);
        }
    }
}
