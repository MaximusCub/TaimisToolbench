using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // The Used Materials row list.
    //
    // CreateUsedMaterialRow's
    // icon+ellipsized-name construction and its divider+relayout tail
    // go through the two shared row-shape helpers - IconNameRowHelpers
    // (build via CreateIconAndEllipsizedName, re-ellipsize via
    // ReellipsizeName) and RowRelayoutHelpers.FinishRow - both extracted
    // from this row and ShoppingListSectionRenderer.CreateShoppingRow, the
    // only two rows across the extracted renderers that actually share the
    // ellipsis shape (see IconNameRowHelpers' own doc comment for why
    // Crafting Steps/Disciplines/Recipes rows do not).
    //
    // sortState/onSortChanged carry the clickable
    // column headers: this renderer only reads the state (to order its
    // rows and mark the active header) and asks the view to re-render when
    // a header is clicked - the state itself outlives every render, so a
    // regenerate keeps the sort the user chose.
    internal sealed class UsedMaterialsSectionRenderer
    {
        private readonly ISectionRelayoutSink _sink;
        private readonly TableSortState<PlanTableColumn> _sortState;
        private readonly Action _onSortChanged;

        // Session item-stat lookup (ItemMetadataService's own cache), so a
        // Used Materials row hovers the same rich item tooltip a tree row
        // does. Optional: a null lookup, or a null answer from it, degrades
        // to the full-name-when-truncated tooltip this row always had.
        private readonly Func<int, ItemTooltipFacts> _getItemFacts;

        internal UsedMaterialsSectionRenderer(
            ISectionRelayoutSink sink, TableSortState<PlanTableColumn> sortState, Action onSortChanged,
            Func<int, ItemTooltipFacts> getItemFacts)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _sortState = sortState ?? throw new ArgumentNullException(nameof(sortState));
            _onSortChanged = onSortChanged ?? throw new ArgumentNullException(nameof(onSortChanged));
            _getItemFacts = getItemFacts ?? throw new ArgumentNullException(nameof(getItemFacts));
        }

        // Text anchor of the row's single reading line. The tier-2 resize
        // grew the icon frame 34 -> 42, moving its center down 4px; the
        // line keeps its pre-tier-2 offset from that center (9 -> 13).
        // Carried down with the frame when the row gained its padding, so
        // the two stay on the same relation.
        private const int RowTextY = PlanContentHeightMath.IconRowIconY + 13;

        /// <summary>
        /// One-pass pre-scan, as every other plan table has: the widest
        /// rendered "Nx" string, which is the Amount column's reserved band
        /// and so where the Item column beside it starts. Data-derived, so
        /// it is measured once here and reused by every row's relayout
        /// closure rather than re-measured per resize tick.
        /// <para>
        /// The band is max(widest data, header label): the "Amount" header
        /// shares the band's centre line with the rows, and at the
        /// ColumnHeader tier it is routinely wider than a short "12x", so
        /// scanning data alone would seat the word over nothing.
        /// </para>
        /// </summary>
        internal void Render(PlanSectionViewModel section, FlowPanel contentFlow, int panelWidth)
        {
            var font = UiFonts.Body;

            // Row ORDER only - the pre-scan below sees the same rows either
            // way, so every column edge (and PlanContentHeightMath's row
            // count) is identical sorted or not.
            var rows = PlanTableSorter.Sort(section.Rows, _sortState);

            // The band is floored at the header BLOCK - word plus the
            // indicator slot beside it - which is fixed in every sort state,
            // so a click never re-flows the column it was aimed at.
            int amountHeaderWidth = SortIndicator.BlockWidthFor(HeaderBands.Font, "Amount");
            int maxQtyWidth = amountHeaderWidth;
            foreach (var row in rows)
            {
                int qtyWidth = (int)System.Math.Ceiling(font.MeasureString($"{row.Quantity}x").Width);
                if (qtyWidth > maxQtyWidth)
                {
                    maxQtyWidth = qtyWidth;
                }
            }

            // Amount/Item column header. Unconditional, like the Shopping
            // List's and the two column-header tables', so it can never
            // disagree with PlanContentHeightMath.SectionBodyHeight, which
            // counts it the same way. Both words sit at a fixed x: the
            // Amount band is the row's left inset and the Item column
            // starts where that band ends, so neither moves with the panel
            // and the width the closures are handed is unused.
            int itemHeaderX = ColumnHeaderLabelMath.LabelX(
                UsedMaterialsColumnMath.NameX(maxQtyWidth),
                UsedMaterialsColumnMath.IconX(maxQtyWidth));
            ColumnHeaderRowRenderer.CreateColumnHeaderRow(
                contentFlow, panelWidth,
                "Amount", UsedMaterialsColumnMath.AmountTextX(maxQtyWidth, amountHeaderWidth),
                "Item", _sink,
                onLeftClick: () => SortBy(PlanTableColumn.Amount),
                onRightClick: () => SortBy(PlanTableColumn.Item),
                leftSort: _sortState.DirectionFor(PlanTableColumn.Amount),
                rightSort: _sortState.DirectionFor(PlanTableColumn.Item),
                // rightLabelXForWidth, not the default: the Item word rules
                // LEFT with its own icons, and the default would right-align
                // it onto the panel's margin.
                rightLabelXForWidth: w => itemHeaderX,
                leftColumnEndForWidth: w => UsedMaterialsColumnMath.HeaderSplitX(maxQtyWidth),
                rowsHeight: () => rows.Count * PlanContentHeightMath.UsedMaterialRowHeight);

            for (int i = 0; i < rows.Count; i++)
            {
                CreateUsedMaterialRow(
                    rows[i], contentFlow, panelWidth, maxQtyWidth,
                    i == rows.Count - 1);
            }
        }

        private void SortBy(PlanTableColumn column)
        {
            _sortState.Cycle(column);
            _onSortChanged();
        }

        private void CreateUsedMaterialRow(
            PlanRowViewModel row, FlowPanel parent, int panelWidth,
            int maxQtyWidth, bool isLast)
        {
            const int rowHeight = PlanContentHeightMath.UsedMaterialRowHeight;
            var rowPanel = new ClippedPanel() { Size = new Point(panelWidth, rowHeight), Parent = parent };

            var font = UiFonts.Body;

            string qtyText = $"{row.Quantity}x";
            int qtyWidth = (int)System.Math.Ceiling(font.MeasureString(qtyText).Width);

            // maxQtyWidth, not this row's own qtyWidth: the Amount band is
            // the whole column's, so a short "1x" row must open its icon on
            // the same rule the widest row does.
            int iconX = UsedMaterialsColumnMath.IconX(maxQtyWidth);
            int nameX = UsedMaterialsColumnMath.NameX(maxQtyWidth);
            string fullName = row.Label ?? "";

            // Composed at HOVER time, not here: a plan restored from disk
            // fills its stat cache in the background (Q13), and a snapshot
            // taken now could never show what lands after it. It also
            // keeps the compose work off the render path.
            //
            // 0 trailing column and 0 gap: the Item column is the last one
            // on the row, so its budget runs to the table's pinned edge.
            var nameHandle = IconNameRowHelpers.DrawIconAndName(
                rowPanel, row.ItemId,
                iconX, PlanContentHeightMath.IconRowIconY, fullName, font,
                PlanRelayoutMath.PinnedRightEdge(panelWidth), 0, 0, nameX, RowTextY,
                ItemIconTier.BagSidebar, _getItemFacts);

            LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = qtyText,
                    Font = font,
                    TextColor = new Color(200, 200, 200),
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(
                        UsedMaterialsColumnMath.AmountTextX(maxQtyWidth, qtyWidth), RowTextY),
                    Parent = rowPanel,
                });

            // No extraRelayout: every x on this row is data-derived now, so
            // a resize moves nothing but the row panel and its divider. The
            // name is left untouched during drag ticks and only
            // re-ellipsized at settle (RunReellipsis), to avoid a
            // MeasureString call per row per tick.
            //
            // IconRowDividerClearance, not 0: the row height absorbs the
            // clearance pixel in its own derivation, and the simulation
            // behind LabelHelpers.CreateRowDivider (executable in
            // RowDividerScissorSimulationTests) proves this height needs
            // the clearance where the old 36 did not.
            RowRelayoutHelpers.FinishRow(
                rowPanel, panelWidth, rowHeight, isLast,
                PlanContentHeightMath.IconRowDividerClearance, _sink, null);

            // The re-ellipsis no longer re-stamps anything: the tooltip
            // builder above reads the label's CURRENT text when the box is
            // drawn, so a resize that truncates or untruncates the name is
            // already reflected.
            _sink.AddReellipsis(w => IconNameRowHelpers.ReellipsizeName(
                nameHandle, font, PlanRelayoutMath.PinnedRightEdge(w), 0, 0));
        }
    }
}
