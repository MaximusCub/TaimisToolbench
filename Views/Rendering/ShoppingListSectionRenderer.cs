using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // The Shopping List row list and its header row.
    //
    // CreateShoppingRow's
    // icon+ellipsized-name construction and its divider+relayout tail
    // go through the two shared row-shape helpers - IconNameRowHelpers
    // (build via CreateIconAndEllipsizedName, re-ellipsize via
    // ReellipsizeName) and RowRelayoutHelpers.FinishRow - both extracted
    // from this row and UsedMaterialsSectionRenderer.CreateUsedMaterialRow,
    // the only two rows across the extracted renderers that actually share
    // the ellipsis shape (see IconNameRowHelpers' own doc comment for why).
    // Everything this row does AFTER the name label - the tooltip-parts
    // build, the source badge, the qty label, the Each/Total coin cells -
    // is hand-rolled here (it does not match either shared shape).
    internal sealed class ShoppingListSectionRenderer
    {
        // Gap the name's ellipsis budget keeps before the Source column.
        private const int NameToQtyGap = 12;

        // The name column's own x's live with the column arithmetic: NameX
        // is the left end of the distributed track span, so the two cannot
        // be defined apart.
        private const int IconX = ShoppingColumnMath.IconX;
        private const int NameX = ShoppingColumnMath.NameX;

        // Text anchor of the row's single reading line - see the identical
        // derivation note on UsedMaterialsSectionRenderer.RowTextY.
        private const int RowTextY = PlanContentHeightMath.IconRowIconY + 13;

        private readonly ISectionRelayoutSink _sink;

        // Clickable column headers - see the identical fields on
        // UsedMaterialsSectionRenderer. This table sorts on all five of
        // its columns; Each/Total are coin+currency mixes, whose ordering
        // rule lives in PlanTableSorter.CompareValue, and Source orders by
        // the badge TEXT the column actually shows.
        private readonly TableSortState<PlanTableColumn> _sortState;
        private readonly Action _onSortChanged;

        // See the identical field on UsedMaterialsSectionRenderer: the
        // session item-stat lookup, optional, degrading to the row's
        // pre-stats tooltip when it has nothing for this item.
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;

        internal ShoppingListSectionRenderer(
            ISectionRelayoutSink sink, TableSortState<PlanTableColumn> sortState, Action onSortChanged,
            Func<int, ItemStatBlock> getItemStatBlock = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _sortState = sortState ?? throw new ArgumentNullException(nameof(sortState));
            _onSortChanged = onSortChanged ?? throw new ArgumentNullException(nameof(onSortChanged));
            _getItemStatBlock = getItemStatBlock;
        }

        private void SortBy(PlanTableColumn column)
        {
            _sortState.Cycle(column);
            _onSortChanged();
        }

        // Right-aligned price columns for the shopping list's Each and
        // Total prices: both anchor to a fixed right edge and grow
        // LEFTWARD, so a gold-value amount in either column can never grow
        // into the other's space. Previously each column reserved a fixed
        // width (150/90) regardless of content; a 3+ digit gold value in
        // Each or Total could still exceed its fixed band and bleed into
        // the Amount column to its left. Column widths are now derived from
        // the actual widest rendered value per column, clamped to those
        // same fixed minimums so short/low-value lists don't look cramped -
        // see ShoppingColumnMath (Blish-free, unit-tested arithmetic).
        //
        internal void Render(PlanSectionViewModel section, FlowPanel contentFlow, int panelWidth)
        {
            var coinFont = UiFonts.Body;

            // Pre-scan: widest actual coin+currency value width per column
            // this render (CoinCurrencyRenderer.MeasureValueWidth accounts for a currency-only
            // or mixed row's icon(s) too, not just coin - KNOWN-ISSUES
            // #16). One pass over the section's rows (shopping lists run to
            // maybe 50-60 rows in practice) - negligible next to the
            // per-row control creation this method already does.
            // The same pass measures the widest "Nx" amount string, the
            // widest source badge and the widest item NAME - the last of
            // which sizes the Item column's own reserve, so the four data
            // columns divide everything it does not need.
            // Row ORDER only - the pre-scan sees the same rows either way,
            // so every column edge (and the row count PlanContentHeightMath
            // measures this section by) is identical sorted or not.
            var rows = PlanTableSorter.Sort(section.Rows, _sortState);

            int maxEachWidth = 0;
            int maxTotalWidth = 0;
            int maxQtyInk = 0;
            int maxSourceInk = 0;
            int maxNameWidth = 0;
            foreach (var row in rows)
            {
                int nameW = (int)System.Math.Ceiling(coinFont.MeasureString(row.Label ?? "").Width);
                if (nameW > maxNameWidth)
                {
                    maxNameWidth = nameW;
                }

                int eachW = CoinCurrencyRenderer.MeasureValueWidth(row.UnitCoinValue, row.UnitCurrencyCosts, coinFont);
                if (eachW > maxEachWidth)
                {
                    maxEachWidth = eachW;
                }

                int totalW = CoinCurrencyRenderer.MeasureValueWidth(row.CoinValue, row.CurrencyCosts, coinFont);
                if (totalW > maxTotalWidth)
                {
                    maxTotalWidth = totalW;
                }

                int qtyW = (int)System.Math.Ceiling(coinFont.MeasureString($"{row.Quantity}x").Width);
                if (qtyW > maxQtyInk)
                {
                    maxQtyInk = qtyW;
                }

                string badge = ShoppingSourceBadge.ForRow(row);
                if (string.IsNullOrEmpty(badge))
                {
                    continue;
                }

                int badgeW = LabelHelpers.MeasureSmallTagWidth(badge);
                if (badgeW > maxSourceInk)
                {
                    maxSourceInk = badgeW;
                }
            }

            // Every band's floor is its own header label: at the
            // ColumnHeader tier a header routinely out-measures the data,
            // and a band narrower than its own header would let the
            // neighbouring column run under that header. The Source band
            // is floored for the mirror-image reason the right-aligned ones
            // are: it is LEFT-ruled, so an over-wide header would overhang
            // to the RIGHT, into the Amount column beside it. The INK
            // widths stay separate - they are what each header centres
            // over, and the floor is exactly the difference.
            int maxQtyWidth = Max(
                maxQtyInk, SortIndicator.BlockWidthFor(HeaderBands.Font, "Amount"));
            int sourceColumnWidth = Max(
                maxSourceInk, SortIndicator.BlockWidthFor(HeaderBands.Font, "Source"));

            // The header and every data row derive their build-time edges
            // from this SAME scan, and their relayout closures re-derive
            // them from it too - the pre-scan depends only on row data,
            // never on panelWidth, so it does not need to re-run on resize
            // at all and no two rows can anchor the table differently.
            var scan = new ColumnScan(
                maxEachWidth, maxTotalWidth, maxQtyWidth, sourceColumnWidth, maxNameWidth,
                maxQtyInk, maxSourceInk);
            CreateShoppingListHeaderRow(contentFlow, panelWidth, scan, rows.Count);
            for (int i = 0; i < rows.Count; i++)
            {
                CreateShoppingRow(rows[i], contentFlow, panelWidth, scan, i == rows.Count - 1);
            }
        }

        // The data-derived (panelWidth-invariant) measurements every row
        // and header closure needs to recompute its column edges - grouped
        // so another cannot be added to one call site and forgotten at
        // another. The Max* fields are BAND widths (floored at their own
        // header); the *Ink fields are the same columns' unfloored content,
        // which is what a header centres over. Each/Total need no separate
        // ink field: their measured value IS the ink, and the band is
        // EffectiveEach/TotalWidth of it.
        private readonly struct ColumnScan
        {
            internal readonly int MaxEachWidth;
            internal readonly int MaxTotalWidth;
            internal readonly int MaxQtyWidth;
            internal readonly int SourceColumnWidth;
            internal readonly int MaxNameWidth;
            internal readonly int QtyInk;
            internal readonly int SourceInk;

            internal ColumnScan(
                int maxEachWidth, int maxTotalWidth, int maxQtyWidth, int sourceColumnWidth,
                int maxNameWidth, int qtyInk, int sourceInk)
            {
                MaxEachWidth = maxEachWidth;
                MaxTotalWidth = maxTotalWidth;
                MaxQtyWidth = maxQtyWidth;
                SourceColumnWidth = sourceColumnWidth;
                MaxNameWidth = maxNameWidth;
                QtyInk = qtyInk;
                SourceInk = sourceInk;
            }

            internal ShoppingColumnMath.ColumnEdges EdgesFor(int panelWidth)
            {
                return ShoppingColumnMath.ComputeEdgesForPanel(
                    panelWidth, MaxEachWidth, MaxTotalWidth, MaxQtyWidth, SourceColumnWidth,
                    MaxNameWidth);
            }
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }

        // Column edges come from Render()'s shared pre-scan, so the header
        // lands on the same x as the rows below it.
        private void CreateShoppingListHeaderRow(
            FlowPanel parent, int panelWidth, ColumnScan scan, int rowCount)
        {
            var edges = scan.EdgesFor(panelWidth);
            var flowBand = HeaderBands.CreateColumnHeaderBandInFlow(parent, panelWidth);
            var rowPanel = flowBand.Band;
            var font = HeaderBands.Font;
            var color = HeaderBands.LabelColor;

            // The Item column flexes and its cells rule left, so its header
            // stays on that rule - at the icon its rows open with, not at
            // the name beside it (Services/ColumnHeaderLabelMath). Every
            // other header CENTRES over the INK its own cells cover,
            // bounded by the columns either side of it and not by the band
            // around that ink - see JustifiedColumnTracks.HeaderRoom. Every
            // one of the five carries a persistent sort indicator, and the
            // block width that covers it is what the placement below is
            // handed, so a sort click moves no column.
            var columns = new[]
            {
                PlanTableColumn.Item, PlanTableColumn.Source, PlanTableColumn.Amount,
                PlanTableColumn.Each, PlanTableColumn.Total,
            };
            var titles = new[] { "Item", "Source", "Amount", "Each", "Total" };
            var blocks = new SortableHeaderBlock[columns.Length];
            for (int i = 0; i < columns.Length; i++)
            {
                blocks[i] = SortableHeaderBlock.Create(
                    rowPanel, font, color, HeaderBands.LabelY, titles[i],
                    _sortState.DirectionFor(columns[i]));
            }

            int sourceHeaderWidth = blocks[1].Width;
            int amountHeaderWidth = blocks[2].Width;
            int eachHeaderWidth = blocks[3].Width;
            int totalHeaderWidth = blocks[4].Width;

            blocks[0].MoveTo(ColumnHeaderLabelMath.LabelX(NameX, IconX));
            PlaceDataHeaders(
                blocks, scan, edges,
                sourceHeaderWidth, amountHeaderWidth, eachHeaderWidth, totalHeaderWidth);

            // The hit area is each column's whole header CELL (see
            // SortableHeaderCells); the labels carry only the note.
            var plan = new HeaderCellPlan(blocks.Length, new SortableHeaderCells(rowPanel));
            for (int i = 0; i < blocks.Length; i++)
            {
                var column = columns[i];
                SortableHeaderLabel.MarkSortable(blocks[i].Title);
                SortableHeaderLabel.MarkSortable(blocks[i].IndicatorLabel);
                plan.Set(i, blocks[i].Title, blocks[i].Width, () => SortBy(column), blocks[i].IndicatorLabel);
            }

            // Each cell owns its COLUMN, not the pixels its word covers,
            // from the same pre-scan the columns come from. The buffer is
            // the closure's and is never reallocated.
            var boundaries = new int[blocks.Length - 1];
            ApplyHeaderBoundaries(plan, scan, panelWidth, boundaries);
            plan.Sync(rowPanel.Width);

            // Header column labels are font-only (fixed text) -
            // pure reposition on every drag tick, recomputing edges from
            // the SAME cached pre-scan ComputeEdgesForPanel was built with
            // (ShoppingColumnMath is the single source of truth both paths
            // call).
            _sink.AddRelayout(w =>
            {
                flowBand.Resize(w);
                PlaceDataHeaders(
                    blocks, scan, scan.EdgesFor(w),
                    sourceHeaderWidth, amountHeaderWidth, eachHeaderWidth, totalHeaderWidth);

                // Every data column's x is width-derived - a track under
                // distribution, the pinned right edge under the packed
                // fallback - so their cells move with the panel.
                ApplyHeaderBoundaries(plan, scan, w, boundaries);
                plan.Sync(rowPanel.Width);
            });

            _sink.TrackStickyBand(
                flowBand, () => rowCount * PlanContentHeightMath.ShoppingRowHeight);
        }

        /// <summary>
        /// Seats the four data headers over the ink their own cells cover.
        /// One method for the build and for every resize tick, so the two
        /// cannot answer differently; position only, and no measurement.
        /// </summary>
        private static void PlaceDataHeaders(
            SortableHeaderBlock[] blocks, ColumnScan scan, ShoppingColumnMath.ColumnEdges edges,
            int sourceHeaderWidth, int amountHeaderWidth, int eachHeaderWidth, int totalHeaderWidth)
        {
            var rooms = HeaderRoomsFor(edges, scan);
            blocks[1].MoveTo(SourceHeaderX(edges, scan, sourceHeaderWidth, rooms.Source));
            blocks[2].MoveTo(JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.QtyRightEdge, scan.QtyInk, amountHeaderWidth, rooms.Amount));
            blocks[3].MoveTo(JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.EachRightEdge, scan.MaxEachWidth, eachHeaderWidth, rooms.Each));
            blocks[4].MoveTo(JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.TotalRightEdge, scan.MaxTotalWidth, totalHeaderWidth, rooms.Total));
        }

        /// <summary>
        /// The Source header's x. Its badges are LEFT-ruled on SourceX, so
        /// the ink runs rightward from there - the mirror of the three
        /// right-aligned columns beside it.
        /// </summary>
        private static int SourceHeaderX(
            ShoppingColumnMath.ColumnEdges edges, ColumnScan scan, int headerWidth,
            JustifiedColumnTracks.HeaderRoom room)
        {
            return JustifiedColumnTracks.CenteredOverContent(
                edges.SourceX, scan.SourceInk, headerWidth, room);
        }

        private static ShoppingColumnMath.HeaderRooms HeaderRoomsFor(
            ShoppingColumnMath.ColumnEdges edges, ColumnScan scan)
        {
            return ShoppingColumnMath.HeaderRoomsFor(
                edges, NameToQtyGap, scan.SourceInk, scan.QtyInk,
                scan.MaxEachWidth, scan.MaxTotalWidth);
        }

        private static void ApplyHeaderBoundaries(
            HeaderCellPlan plan, ColumnScan scan, int panelWidth, int[] boundaries)
        {
            ShoppingColumnMath.HeaderCellBoundaries(
                scan.EdgesFor(panelWidth), NameToQtyGap, boundaries);
            for (int i = 0; i < boundaries.Length; i++)
            {
                plan.SetBoundary(i, boundaries[i]);
            }
        }

        // A ValueCellHandle's own
        // controls (the coin/currency icon+label segments, or the single
        // DashLabel for an unpriceable row) have no BasicTooltipText of
        // their own, so they silently swallow the row's tooltip exactly
        // like nameLabel does - see CreateShoppingRow's BuildTooltip doc
        // comment. Segment counts are always small (one row's worth of
        // coin/currency denominations), so this is cheap even called on
        // every BuildTooltip rebuild.
        private void CreateShoppingRow(
            PlanRowViewModel row, FlowPanel parent, int panelWidth, ColumnScan scan, bool isLast)
        {
            var edges = scan.EdgesFor(panelWidth);
            const int rowHeight = PlanContentHeightMath.ShoppingRowHeight;
            var rowPanel = new ClippedPanel() { Size = new Point(panelWidth, rowHeight), Parent = parent };

            var font = UiFonts.Body;

            string qtyText = $"{row.Quantity}x";
            int qtyWidth = (int)System.Math.Ceiling(font.MeasureString(qtyText).Width);

            // Icon y=0 - see the identical flush-fit note in
            // CreateUsedMaterialRow; same 45px rowHeight / tier-2 icon
            // frame shape.
            //
            // The name's budget stops at the SOURCE column's left edge now,
            // not at the Amount band with this row's own badge width
            // subtracted: the badge is a column, so it no longer moves with
            // the name and no longer has to be reserved out of it.
            string fullName = row.Label ?? "";
            string hintText = row.HintText;

            // Composed at HOVER time (see UsedMaterialsSectionRenderer's
            // matching note): a stat block that lands after this render
            // (Q13) is picked up on the next hover, and the compose work
            // stays off the render path.
            // ONE box, not two. The second box carried this row's currency
            // holdings and its acquisition hint, and the table already
            // prints both in its own columns, so the box restated them in
            // sentences and added nothing.
            int itemId = row.ItemId;
            var hover = ItemIconTooltip.ForItem(
                ItemTooltipIdentity.ForItem(fullName, row.IconUrl, row.Rarity),
                _getItemStatBlock == null || itemId <= 0 ? (Func<ItemStatBlock>)null
                    : () => _getItemStatBlock(itemId));

            var nameHandle = IconNameRowHelpers.CreateIconAndEllipsizedName(
                rowPanel, row.IconUrl, row.Rarity,
                IconX, PlanContentHeightMath.IconRowIconY, fullName, font,
                edges.SourceX, 0, NameToQtyGap, NameX, RowTextY,
                ItemIconTier.BagSidebar, hover);
            var nameLabel = nameHandle.NameLabel;

            string sourceTag = ShoppingSourceBadge.ForRow(row);
            Panel tagPanel = null;
            if (!string.IsNullOrEmpty(sourceTag))
            {
                ShoppingBadgeColors.For(row.RowType, out Color tagBorder, out Color tagFill);
                tagPanel = LabelHelpers.CreateSmallTag(
                    rowPanel, sourceTag, edges.SourceX, RowTextY, tagBorder, tagFill);
            }

            var qtyLabel = LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = qtyText,
                    Font = font,
                    TextColor = new Color(200, 200, 200),
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(edges.QtyRightEdge - qtyWidth, RowTextY),
                    Parent = rowPanel,
                });

            // Each/Total cells: coin-only rows render exactly as before;
            // a row priced wholly or partly in a non-coin currency (e.g. a
            // vendor offer paid in spirit shards) renders currency segments
            // alongside/instead of coin; a row with neither (genuinely
            // unpriceable - gw2e: "Not sold or crafted") renders a dash,
            // never a blank cell.
            var eachCell = CoinCurrencyRenderer.RenderValueCellRightAligned(rowPanel, row.UnitCoinValue, row.UnitCurrencyCosts, edges.EachRightEdge, RowTextY, font);
            var totalCell = CoinCurrencyRenderer.RenderValueCellRightAligned(rowPanel, row.CoinValue, row.CurrencyCosts, edges.TotalRightEdge, RowTextY, font);

            // An UNKNOWN row's dash takes the badge's own red, so "no
            // source" and "no price" read as one statement about the row
            // rather than two unrelated marks. Only the dash: the item name
            // keeps its rarity colour at full strength, because an unknown
            // source is a fact about acquisition, not a defect of the item.
            if (row.RowType == PlanRowType.ShoppingUnknown)
            {
                TintUnpricedDash(eachCell);
                TintUnpricedDash(totalCell);
            }

            // TOOLTIP SWALLOWED BY CHILD CONTROLS: a container's tooltip
            // never fires when a child control with no tooltip of its own
            // covers the hover point - the row's children (the icon,
            // nameLabel, qtyLabel, the Each/Total cells) all capture the
            // mouse before rowPanel's own tooltip is reached, so every one
            // of them carries the row's tooltip. The icon and the name are
            // stamped by the row builder above; the rest is stamped here,
            // after those controls exist. The source badge is the one
            // child that carries something else - its own prose, below.
            // The badge gets its OWN hover, not the row's: four capital
            // letters name the source only to a reader who already knows
            // the vocabulary, and the badge is the one control on the row
            // whose whole job is to answer "where do I get this?".
            LabelHelpers.ApplyTagTooltip(tagPanel, ShoppingSourceBadge.TooltipForRow(row));

            // Source badge + qty + Each/Total cells reposition every drag
            // tick (no MeasureString - the badge's width is data-fixed, and
            // CoinCurrencyRenderer.RepositionValueCellRightAligned uses
            // only cached segment text widths). The badge moved from the
            // settle pass to here when it became a column: its x is
            // width-derived now rather than trailing the name's ellipsis.
            //
            // IconRowDividerClearance, not 0 - ShoppingRowHeight (45)
            // absorbs the clearance pixel in its own derivation, keeping
            // the divider flush under the icon frame; see the identical
            // note in CreateUsedMaterialRow.
            RowRelayoutHelpers.FinishRow(
                rowPanel, panelWidth, rowHeight, isLast,
                PlanContentHeightMath.IconRowDividerClearance, _sink,
                w =>
                {
                    var e = scan.EdgesFor(w);
                    if (tagPanel != null)
                    {
                        tagPanel.Location = new Point(e.SourceX, RowTextY);
                    }

                    qtyLabel.Location = new Point(e.QtyRightEdge - qtyWidth, RowTextY);
                    CoinCurrencyRenderer.RepositionValueCellRightAligned(eachCell, e.EachRightEdge, RowTextY);
                    CoinCurrencyRenderer.RepositionValueCellRightAligned(totalCell, e.TotalRightEdge, RowTextY);
                });
            // No tooltip re-stamp on settle: the deferred builder reads
            // the label's current text when the box is drawn.
            _sink.AddReellipsis(w => IconNameRowHelpers.ReellipsizeName(
                nameHandle, font, scan.EdgesFor(w).SourceX, 0, NameToQtyGap));
        }

        /// <summary>
        /// Recolors an unpriceable cell's dash to the UNKNOWN badge's hue.
        /// A no-op for a cell that renders real segments - only a cell with
        /// nothing to price has a dash at all.
        /// </summary>
        private static void TintUnpricedDash(CoinCurrencyRenderer.ValueCellHandle cell)
        {
            if (cell?.DashLabel == null)
            {
                return;
            }

            cell.DashLabel.TextColor = ShoppingBadgeColors.UnknownBorder;
        }
    }
}
