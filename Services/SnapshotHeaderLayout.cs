namespace TaimisToolbench.Services
{
    /// <summary>
    /// Geometry for the Snapshot tab's header block (Blish-free,
    /// unit-testable), where the source-filter checkboxes share the search row
    /// instead of owning a full-width row of their own.
    /// <para>
    /// They share it only while the whole run fits beside the search box in
    /// ONE row. Past one row the run falls back to its own full-width row
    /// below the search box, which is exactly the layout it had before it
    /// moved up.
    /// </para>
    /// <para>
    /// The checkbox flow itself is unchanged
    /// (<see cref="SourceFilterFlowLayout"/> still wraps it, still caps it,
    /// still scrolls past the cap); it is handed the placement's width here
    /// and positioned at its start offset by the panel that holds it.
    /// </para>
    /// <para>See docs/ARCHITECTURE.md section 4, and "Services Q-Z: relocated
    /// design narrative" for what the one-row limit buys and costs.</para>
    /// </summary>
    internal static class SnapshotHeaderLayout
    {
        /// <summary>
        /// Height of the status row beneath the header buttons. MainView
        /// sizes that panel from here rather than from a literal of its own,
        /// so anything that has to fit inside the row (the inline spinner)
        /// can be checked against the value the row is actually built from.
        /// </summary>
        /// <para>
        /// 26, not 24: the status line sits at the ramp's Status tier (18
        /// bold, lowest ink 23 against Body's 21), drawn at y=2, so the 1px
        /// of clearance the row has always kept needs two more pixels.
        /// </para>
        public const int StatusRowHeight = 26;

        /// <summary>
        /// Y the status label is drawn at inside that row, and so the top
        /// the inline spinner beside it is clamped to
        /// (InlineSpinnerLayout.Place never starts the spinner above the
        /// label). A spinner taller than StatusRowHeight minus this paints
        /// past the bottom of the row.
        /// </summary>
        public const int StatusLabelTopInset = 2;

        /// <summary>Left gutter every element on this tab starts at. Five
        /// things here used to sit at x=0.</summary>
        public const int SnapshotHeaderInset = UiSpacing.Inset;

        /// <summary>Gap between the tab's two right-anchored header buttons -
        /// the module's one button gap, not the 20 they used to keep.</summary>
        public const int HeaderButtonGap = UiSpacing.ButtonGap;

        /// <summary>
        /// Top y of a control seated on the tab's title band. Centred, and
        /// floored at 0 so a band shorter than its own control still places
        /// it inside itself rather than above the band's top edge.
        /// </summary>
        public static int BandControlY(int bandHeight, int controlHeight)
        {
            int y = (bandHeight - controlHeight) / 2;
            return y > 0 ? y : 0;
        }

        /// <summary>Gap the result line keeps clear of the coin block beside
        /// it - the module's one name-to-column gap.</summary>
        public const int ResultLineToCoinGap = SnapshotItemGridLayout.CellAmountGap;

        /// <summary>
        /// The right edge every NON-SCROLLING element on this tab pins to.
        /// It is derived from the SCROLLING grid's own width, not from the
        /// container's, so the header buttons, the coin block and the grid's
        /// rightmost column land on one vertical line.
        /// <para>
        /// The buttons used to pin to containerWidth - 10 while the grid's
        /// last column ended at containerWidth - 28: eighteen pixels apart,
        /// on the same tab, at every width.
        /// </para>
        /// </summary>
        public static int ChromeRightEdge(int containerWidth)
        {
            return PlanRelayoutMath.PinnedRightEdge(
                SnapshotItemGridLayout.ComputeGridWidth(containerWidth));
        }

        /// <summary>Left edge of the right-pinned coin block (its caption,
        /// gap and coin run laid out as one unit).</summary>
        public static int CoinBlockX(int containerWidth, int coinBlockWidth)
        {
            return PlanRelayoutMath.RightAlignedX(
                ChromeRightEdge(containerWidth), coinBlockWidth > 0 ? coinBlockWidth : 0);
        }

        /// <summary>
        /// Width the coin row's result line may occupy before the coin block
        /// pinned to its right - the plan tables' rule, applied to a summary
        /// row.
        /// </summary>
        public static int ResultLineMaxWidth(int containerWidth, int coinBlockWidth)
        {
            return PlanRelayoutMath.NameMaxWidthBeforeColumn(
                ChromeRightEdge(containerWidth),
                coinBlockWidth > 0 ? coinBlockWidth : 0,
                ResultLineToCoinGap,
                SnapshotHeaderInset);
        }

        /// <summary>
        /// Width a status line may occupy: the chrome edge, less the inset
        /// it starts at and whatever the inline spinner trailing it needs.
        /// </summary>
        public static int StatusMaxWidth(int containerWidth, int spinnerReserve)
        {
            int width = ChromeRightEdge(containerWidth) - SnapshotHeaderInset
                - (spinnerReserve > 0 ? spinnerReserve : 0);
            return width > 20 ? width : 20;
        }

        /// <summary>
        /// Width available to the source-filter flow once it starts at
        /// startX instead of the panel's left edge. Floors at 0 rather than
        /// going negative - SourceFilterFlowLayout already degrades a
        /// too-narrow run to one cell per row, which keeps every filter
        /// reachable (wrapped, then scrolled) instead of clipped.
        /// </summary>
        public static int SourceFilterWidth(int panelWidth, int startX)
        {
            // Ends on the tab's shared chrome right edge, not at the raw
            // container width: the run is the one element here whose width
            // is content-driven, and running it past that edge also pushes
            // its own wrap threshold past where the checkboxes may wrap.
            int width = ChromeRightEdge(panelWidth) - startX;
            return width > 0 ? width : 0;
        }

        /// <summary>
        /// Whether a run that flowed into <paramref name="flowedRowCount"/>
        /// rows beside the search box may stay there. One row (or none at
        /// all, before the first snapshot) shares; anything that wrapped
        /// takes its own full-width row instead - see the class doc comment.
        /// </summary>
        public static bool SharesSearchRow(int flowedRowCount)
        {
            return flowedRowCount <= 1;
        }

        /// <summary>
        /// Where the source-filter run sits, and how much width it has to
        /// flow into, in each of the two modes. OffsetY is measured from the
        /// search row's own y.
        /// </summary>
        public readonly struct SourceFilterPlacement
        {
            public readonly bool SharesSearchRow;
            public readonly int X;
            public readonly int OffsetY;
            public readonly int Width;

            public SourceFilterPlacement(bool sharesSearchRow, int x, int offsetY, int width)
            {
                SharesSearchRow = sharesSearchRow;
                X = x;
                OffsetY = offsetY;
                Width = width;
            }
        }

        /// <summary>
        /// The placement for one mode. Callers flow the run at the shared
        /// placement's width first and re-place it here when
        /// <see cref="SharesSearchRow(int)"/> rejects the resulting row
        /// count - the mode cannot be decided before the flow, since it IS
        /// the flow's outcome.
        /// </summary>
        public static SourceFilterPlacement PlaceSourceFilterRun(
            int panelWidth, int startX, int searchRowHeight, int rowGap, bool sharesSearchRow)
        {
            if (sharesSearchRow)
            {
                return new SourceFilterPlacement(true, startX, 0, SourceFilterWidth(panelWidth, startX));
            }

            // Own-row mode starts at the same gutter as the search box
            // above it, and ends on the same right edge as every other
            // chrome element - it used to start at x=0, sixteen pixels
            // left of the box it sits under.
            return new SourceFilterPlacement(
                false, SnapshotHeaderInset, searchRowHeight + rowGap, SourceFilterWidth(panelWidth, SnapshotHeaderInset));
        }

        /// <summary>
        /// Height of the search band. A shared run sits beside the search
        /// box, so the band is as tall as the taller of the two and a run
        /// that fits there costs the header exactly nothing - which is the
        /// whole saving. A run on its own row costs the search row plus the
        /// gap plus itself, i.e. exactly what the header spent before.
        /// </summary>
        public static int SearchBandHeight(
            int searchRowHeight, int sourceFilterHeight, SourceFilterPlacement placement)
        {
            if (!placement.SharesSearchRow)
            {
                return placement.OffsetY + sourceFilterHeight;
            }

            return searchRowHeight > sourceFilterHeight ? searchRowHeight : sourceFilterHeight;
        }
    }
}
