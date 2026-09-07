using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure placement arithmetic (Blish-free, unit-testable) for the
    /// Snapshot tab's result list: how many columns the content panel can
    /// hold, how wide one of them is, and where each result cell sits in
    /// the grid. The view (MainView.RebuildContent/LayoutResultGrid) only
    /// copies the results onto controls.
    /// <para>
    /// Same shape as <see cref="SettingsCurrencyGridLayout"/> - fixed cell
    /// height, reading order, a one-column fallback below a minimum column
    /// width - with two differences the Snapshot tab's data forces: cells
    /// are never individually hidden here (a search rebuilds the row set
    /// rather than filtering an always-present one, so the input is a
    /// count, not a name list), and the item and wallet runs are laid out
    /// as two grids because their row heights differ.
    /// </para>
    /// </summary>
    internal static class SnapshotItemGridLayout
    {
        /// <summary>Left edge of a cell's content, which is the Amount
        /// column: the amount reads before the name, so a long name pushes
        /// nothing away from where a short one puts it.</summary>
        public const int CellAmountX = 2;

        /// <summary>
        /// Width of a cell's icon gutter: the frame (art + 1px border each
        /// side) plus its right gap. It lives here, derived from
        /// <see cref="ItemIconTiers.BagSlotIconSize"/>, so
        /// <see cref="SnapshotMinColumnWidth"/> is derived from the geometry
        /// the cells are actually built with and cannot drift from it.
        /// </summary>
        public const int IconGutterWidth = ItemIconTiers.BagSlotIconSize + 2 + 6;

        /// <summary>Gap kept clear of a cell's right edge.</summary>
        public const int CellTextRightPad = 8;

        /// <summary>
        /// Aliased to <see cref="WindowSizing.ScrollbarAllowance"/>, which
        /// is where this module's one scrollbar allowance is stated. Kept
        /// as a name here because the grid-width derivation below reads in
        /// these terms.
        /// </summary>
        public const int ScrollbarAllowance = WindowSizing.ScrollbarAllowance;

        /// <summary>
        /// Upper bound on one character of the body font, which averages
        /// ~8.4px on item names at Font16 (measured: "Thermocatalytic
        /// Reagent" is 192px over 23 characters). Rounding up is what pays
        /// for the cell's breathing room. Was 8 against Font14's ~7.6px.
        /// </summary>
        public const int MaxCharWidthPx = 9;

        /// <summary>
        /// The run the narrowest column is sized to hold without
        /// ellipsizing: an item's NAME, 45 characters. Was 52, until the
        /// count prefix became a column of its own budgeted by
        /// <see cref="AmountColumnFloor"/>.
        /// <para>
        /// The BREAKDOWN line below it is deliberately NOT part of this
        /// budget. A full source breakdown ("Character: &lt;name&gt; 250
        /// Bank 250   Material Storage 2000") is unbounded in the roster's
        /// name lengths and already ellipsizes with the full text on the
        /// row's tooltip at every width; sizing a column to it would price
        /// the second column out of every window a player actually uses.
        /// Per column it simply ellipsizes earlier.
        /// </para>
        /// </summary>
        public const int SnapshotNameRunChars = 45;

        /// <summary>Gap between the Amount column and the icon after it -
        /// the same 12px the plan's name columns keep.</summary>
        public const int CellAmountGap = 12;

        /// <summary>Left edge of a cell's tier-1 bag-slot icon frame, and so
        /// of the name column - the rule that column's header word takes
        /// (<see cref="ColumnHeaderLabelMath"/>).</summary>
        public static int CellIconX(int amountBandWidth)
        {
            return CellAmountX + (amountBandWidth > 0 ? amountBandWidth : 0) + CellAmountGap;
        }

        /// <summary>Left edge of a cell's text, past the icon gutter. Both
        /// of a cell's lines start here.</summary>
        public static int CellTextX(int amountBandWidth)
        {
            return CellIconX(amountBandWidth) + IconGutterWidth;
        }

        /// <summary>
        /// Width the Amount column is assumed to want in the minimum-column
        /// derivation. MEASURED: "Amount" is 79px at 20-bold, and a run with
        /// wider digits indents the name a little further.
        /// <para>
        /// Plus the persistent sort indicator this header now carries at all
        /// times: <see cref="SortIndicatorLayout.Gap"/> and a slot sized for
        /// the wider of the pair. 12, not the shipped pair's 9 xadvance
        /// (ref/glyphs.fnt), because a corrupt install degrades to Menomonia
        /// "^"/"v" at 20-bold and the column may not shrink under it.
        /// </para>
        /// </summary>
        public const int AmountColumnFloor = 79 + SortIndicatorLayout.Gap + 12;

        /// <summary>
        /// Narrowest column a cell fits in. Below twice this the grid falls
        /// back to a single column rather than clipping the name line.
        /// <para>
        /// 586px - the cell's whole width, term by term: the Amount
        /// column's own floor, the gap after it, the icon gutter, a
        /// 45-character name, and the cell's right pad. Two columns fit
        /// inside the 1252px grid the 1378px window minimum leaves (626px
        /// each) and a third only once the window reaches 1884px.
        /// </para>
        /// </summary>
        public const int SnapshotMinColumnWidth =
            CellAmountX + AmountColumnFloor + CellAmountGap + IconGutterWidth
                + (SnapshotNameRunChars * MaxCharWidthPx) + CellTextRightPad;

        /// <summary>Right edge every cell's text stops at. A cell justifies
        /// like a plan table row: this edge is a function of the cell width
        /// alone, and the name is what flexes.</summary>
        public static int CellContentRightEdge(int columnWidth)
        {
            return columnWidth - CellTextRightPad;
        }

        /// <summary>Width the Amount column reserves: the widest amount,
        /// floored at its header label, which routinely out-measures the
        /// digits under it ("Amount" 79px vs "12x" 32px).</summary>
        public static int CellAmountBandWidth(int widestAmountWidth, int headerLabelWidth)
        {
            int band = widestAmountWidth > headerLabelWidth ? widestAmountWidth : headerLabelWidth;
            return band > 0 ? band : 0;
        }

        /// <summary>
        /// Where one piece of text sits inside the Amount column: centred in
        /// the band, so the amounts read as one column whatever their
        /// digits measure. Never left of the band.
        /// </summary>
        public static int CellAmountTextX(int amountBandWidth, int textWidth)
        {
            int slack = amountBandWidth - textWidth;
            return slack > 0 ? CellAmountX + (slack / 2) : CellAmountX;
        }

        /// <summary>
        /// Where a cell's Amount header cell ends and its Name one begins.
        /// Everything left of it is fixed-width, so unlike the name column
        /// this boundary does not move with the cell.
        /// </summary>
        public static int CellHeaderSplitX(int amountBandWidth)
        {
            return CellIconX(amountBandWidth) - (CellAmountGap / 2);
        }

        /// <summary>Width either of a cell's text lines may occupy: what is
        /// left of the cell once the Amount column and the icon gutter have
        /// taken theirs. Clamped to 20px so a very narrow column never
        /// yields a zero ellipsis width.</summary>
        public static int CellTextMaxWidth(int columnWidth, int amountBandWidth)
        {
            int width = CellContentRightEdge(columnWidth) - CellTextX(amountBandWidth);
            return width > 20 ? width : 20;
        }

        public readonly struct CellPlacement
        {
            public readonly int X;
            public readonly int Y;
            public readonly int Column;
            public readonly int Row;

            public CellPlacement(int x, int y, int column, int row)
            {
                X = x;
                Y = y;
                Column = column;
                Row = row;
            }
        }

        public sealed class Grid
        {
            public IReadOnlyList<CellPlacement> Cells { get; }

            public int ColumnCount { get; }

            public int ColumnWidth { get; }

            public int RowCount { get; }

            public int Height { get; }

            internal Grid(
                IReadOnlyList<CellPlacement> cells, int columnCount, int columnWidth, int rowCount, int height)
            {
                Cells = cells;
                ColumnCount = columnCount;
                ColumnWidth = columnWidth;
                RowCount = rowCount;
                Height = height;
            }
        }

        /// <summary>
        /// Width the grid is laid out in, given the scrolling content
        /// panel's own width - see <see cref="ScrollbarAllowance"/>.
        /// </summary>
        public static int ComputeGridWidth(int contentWidth)
        {
            int width = contentWidth - ScrollbarAllowance;
            return width > 0 ? width : 0;
        }

        /// <summary>The shared grid law at this grid's own
        /// <see cref="SnapshotMinColumnWidth"/> - see <see cref="GridLayout"/>.
        /// Uncapped: the count is derived from the width the player gave the
        /// window, so a wide window gets three or more columns and every one
        /// of them is still at least SnapshotMinColumnWidth across.</summary>
        public static int ComputeColumnCount(int gridWidth)
        {
            return GridLayout.ColumnCount(gridWidth, SnapshotMinColumnWidth);
        }

        public static int ComputeColumnWidth(int gridWidth)
        {
            return GridLayout.ColumnWidth(gridWidth, ComputeColumnCount(gridWidth));
        }

        public static int ComputeHeight(int count, int gridWidth, int rowHeight)
        {
            int rowCount = GridLayout.RowCount(count, ComputeColumnCount(gridWidth));
            return rowCount * (rowHeight > 0 ? rowHeight : 0);
        }

        /// <summary>
        /// One placement per cell, in input order, packed left-to-right then
        /// top-to-bottom - reading order, so the single-column list the tab
        /// shipped with is exactly the one-column case of this grid.
        /// </summary>
        /// <param name="offsetY">
        /// Y the section starts at. The wallet run is laid out at the item
        /// run's <see cref="Grid.Height"/> so it still reads after the items
        /// above it, in the same grid panel and at the same column count.
        /// <see cref="Grid.Height"/> is the section's OWN height and never
        /// includes this offset.
        /// </param>
        public static Grid Compute(int count, int gridWidth, int rowHeight, int offsetY = 0)
        {
            int columnCount = ComputeColumnCount(gridWidth);
            int columnWidth = ComputeColumnWidth(gridWidth);
            int safeRowHeight = rowHeight > 0 ? rowHeight : 0;
            int safeCount = count > 0 ? count : 0;

            var cells = new CellPlacement[safeCount];
            for (int i = 0; i < safeCount; i++)
            {
                int row = i / columnCount;
                int column = i % columnCount;
                cells[i] = new CellPlacement(
                    column * columnWidth, offsetY + (row * safeRowHeight), column, row);
            }

            int rowCount = GridLayout.RowCount(safeCount, columnCount);
            return new Grid(cells, columnCount, columnWidth, rowCount, rowCount * safeRowHeight);
        }
    }
}
