namespace TaimisToolbench.Services
{
    /// <summary>
    /// The module's ONE column-grid law: how many equal columns of a given
    /// minimum width a panel holds, how wide one of them is, and how many
    /// rows a cell count fills. Blish-free, like every grid that uses it.
    /// <para>
    /// What did NOT move here: each grid's own MinColumnWidth derivation and
    /// its cell-internal x offsets (CellContentRightEdge, CellTagX, ...).
    /// Those are genuinely different per grid - a currency row holds a name
    /// and three controls, a snapshot cell a name and an amount - and
    /// sharing them would couple numbers that only coincide. Nor did the
    /// three placement/result types: their fields differ by real need, and
    /// merging them would widen three public shapes to the union of all
    /// three for no behaviour.
    /// </para>
    /// <para>Derivation: docs/ARCHITECTURE.md section S1.2.</para>
    /// </summary>
    internal static class GridLayout
    {
        /// <summary>
        /// As many whole <paramref name="minColumnWidth"/> columns as fit,
        /// never fewer than one. Not capped by default: the count is derived
        /// from the width the player gave the window, so a wide window gets
        /// three or more columns and every one of them is still at least
        /// minColumnWidth across.
        /// <para>
        /// <paramref name="maxColumns"/> caps the count for a grid whose
        /// content is finite - a column no row ever puts a block in is
        /// stranded space by construction. A non-positive
        /// minColumnWidth or cap yields the single-column fallback rather
        /// than dividing by it.
        /// </para>
        /// </summary>
        public static int ColumnCount(int width, int minColumnWidth, int? maxColumns = null)
        {
            if (minColumnWidth < 1)
            {
                return 1;
            }

            if (maxColumns.HasValue && maxColumns.Value < 1)
            {
                return 1;
            }

            int columns = width / minColumnWidth;
            if (columns < 1)
            {
                columns = 1;
            }

            if (maxColumns.HasValue && columns > maxColumns.Value)
            {
                columns = maxColumns.Value;
            }

            return columns;
        }

        /// <summary>Width of one column: the panel divided evenly, with the
        /// remainder left at the right edge rather than distributed.</summary>
        public static int ColumnWidth(int width, int columnCount)
        {
            return width > 0 && columnCount > 0 ? width / columnCount : 0;
        }

        /// <summary>Rows a cell count fills at this column count, rounding
        /// up. Zero cells is zero rows, not one empty one.</summary>
        public static int RowCount(int count, int columnCount)
        {
            if (count < 1 || columnCount < 1)
            {
                return 0;
            }

            return (count + columnCount - 1) / columnCount;
        }
    }
}
