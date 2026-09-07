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
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;

        internal UsedMaterialsSectionRenderer(
            ISectionRelayoutSink sink, TableSortState<PlanTableColumn> sortState, Action onSortChanged,
            Func<int, ItemStatBlock> getItemStatBlock = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _sortState = sortState ?? throw new ArgumentNullException(nameof(sortState));
            _onSortChanged = onSortChanged ?? throw new ArgumentNullException(nameof(onSortChanged));
            _getItemStatBlock = getItemStatBlock;
        }

        // Left x of the name column (past the row's tier-2 icon frame at
        // x=8, plus an 8px gap), and the gap the ellipsis budget keeps
        // between the name and the Amount column.
        private const int IconX = 8;
        private const int NameX = IconX + PlanContentHeightMath.RowIconFrameSize + 8;
        private const int NameToQtyGap = 12;

        // Text anchor of the row's single reading line. The tier-2 resize
        // grew the icon frame 34 -> 42, moving its center down 4px; the
        // line keeps its pre-tier-2 offset from that center (9 -> 13).
        // Carried down with the frame when the row gained its padding, so
        // the two stay on the same relation.
        private const int RowTextY = PlanContentHeightMath.IconRowIconY + 13;

        /// <summary>
        /// One-pass pre-scan, as every other plan table has: the widest
        /// rendered "Nx" string, which is the Amount
        /// column's reserved band and therefore the Item column's ellipsis
        /// budget. Data-derived, so it is measured once here and reused by
        /// every row's relayout closure rather than re-measured per resize
        /// tick.
        /// <para>
        /// The band is max(widest data, header label): the "Amount" header
        /// right-aligns onto the same edge as the rows, and at the
        /// ColumnHeader tier it is routinely wider than a short "12x", so
        /// scanning data alone would let a name run under its own header.
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

            // Item/Amount column header. Without it this is the one plan
            // table with a right-hand column nothing names, leaving the
            // reader to infer that a bare "12x" column is a quantity.
            // Unconditional, like the Shopping List's and the two
            // column-header tables', so it can never disagree with
            // PlanContentHeightMath.SectionBodyHeight, which counts it the
            // same way. No rightXForWidth: the Amount column is pinned to
            // the panel edge, ColumnHeaderRowRenderer's own default.
            // rightLabelXForWidth, though: the WORD centres on the
            // quantities, not on the panel's right margin
            // (JustifiedColumnTracks.CenteredOverContent).
            ColumnHeaderRowRenderer.CreateColumnHeaderRow(
                contentFlow, panelWidth,
                "Item", ColumnHeaderLabelMath.LabelX(NameX, IconX),
                "Amount", _sink,
                onLeftClick: () => SortBy(PlanTableColumn.Item),
                onRightClick: () => SortBy(PlanTableColumn.Amount),
                leftSort: _sortState.DirectionFor(PlanTableColumn.Item),
                rightSort: _sortState.DirectionFor(PlanTableColumn.Amount),
                // The quantities pin to the table's own edge, so a header
                // wider than the widest of them has nowhere to centre and
                // right-aligns on that edge - the bound a room never yields.
                rightLabelXForWidth: w => JustifiedColumnTracks.CenteredOverContentRightAligned(
                    PlanRelayoutMath.PinnedRightEdge(w), maxQtyWidth, amountHeaderWidth,
                    PlanRelayoutMath.TrailingColumnHeaderRoom(
                        PlanRelayoutMath.PinnedRightEdge(w), maxQtyWidth, NameToQtyGap)),
                // The Item column is everything left of the Amount band -
                // the name's own ellipsis terms, with the gap split.
                leftColumnEndForWidth: w => PlanRelayoutMath.HeaderSplitBeforeColumn(
                    PlanRelayoutMath.PinnedRightEdge(w), maxQtyWidth, NameToQtyGap),
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

            int qtyRightEdge = PlanRelayoutMath.PinnedRightEdge(panelWidth);
            var font = UiFonts.Body;

            string qtyText = $"{row.Quantity}x";
            int qtyWidth = (int)System.Math.Ceiling(font.MeasureString(qtyText).Width);

            // maxQtyWidth, not this row's own qtyWidth: the Amount column
            // is a reserved band right-aligned on the pinned edge, so the
            // name's budget stops at the band's LEFT edge. Budgeting
            // against one row's short "1x" would let its name run under the
            // column's widest value.
            string fullName = row.Label ?? "";

            // Composed at HOVER time, not here: a plan restored from disk
            // fills its stat cache in the background (Q13), and a snapshot
            // taken now could never show what lands after it. It also
            // keeps the compose work off the render path.
            int itemId = row.ItemId;
            var hover = ItemIconTooltip.ForItem(
                ItemTooltipIdentity.ForItem(fullName, row.IconUrl, row.Rarity),
                _getItemStatBlock == null || itemId <= 0 ? (Func<ItemStatBlock>)null
                    : () => _getItemStatBlock(itemId));

            var nameHandle = IconNameRowHelpers.CreateIconAndEllipsizedName(
                rowPanel, row.IconUrl, row.Rarity,
                IconX, PlanContentHeightMath.IconRowIconY, fullName, font,
                qtyRightEdge, maxQtyWidth, NameToQtyGap, NameX, RowTextY,
                ItemIconTier.BagSidebar, hover);

            var qtyLabel = LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = qtyText,
                    Font = font,
                    TextColor = new Color(200, 200, 200),
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(qtyRightEdge - qtyWidth, RowTextY),
                    Parent = rowPanel,
                });

            // Qty label position is a pure reposition (qtyWidth is
            // font-only); the name is left untouched during drag ticks and
            // only re-ellipsized at settle (RunReellipsis) to avoid a
            // MeasureString call per row per tick.
            //
            // IconRowDividerClearance, not 0: the row height absorbs the
            // clearance pixel in its own derivation, and the simulation
            // behind LabelHelpers.CreateRowDivider (executable in
            // RowDividerScissorSimulationTests) proves this height needs
            // the clearance where the old 36 did not.
            RowRelayoutHelpers.FinishRow(
                rowPanel, panelWidth, rowHeight, isLast,
                PlanContentHeightMath.IconRowDividerClearance, _sink,
                w =>
                {
                    qtyLabel.Location = new Point(
                        PlanRelayoutMath.PinnedRightEdge(w) - qtyWidth, RowTextY);
                });
            // The re-ellipsis no longer re-stamps anything: the tooltip
            // builder above reads the label's CURRENT text when the box is
            // drawn, so a resize that truncates or untruncates the name is
            // already reflected.
            _sink.AddReellipsis(w => IconNameRowHelpers.ReellipsizeName(
                nameHandle, font, PlanRelayoutMath.PinnedRightEdge(w), maxQtyWidth, NameToQtyGap));
        }
    }
}
