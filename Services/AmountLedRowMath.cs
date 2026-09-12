namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure column arithmetic (Blish-free, unit-testable) for the prefix a
    /// plan row that reads AMOUNT FIRST opens with: an Amount band on the
    /// row's left inset, then the icon, then the name. Both the Used
    /// Materials table and the Shopping List draw it.
    /// <para>
    /// Both insets are the Snapshot cell's own, so every table that reads
    /// the amount before the name cannot drift apart. The Amount band is
    /// the widest value floored at the header BLOCK, which is why every
    /// method here takes it rather than deriving it: the widths come from
    /// BitmapFont.MeasureString, which is Blish-bound.
    /// </para>
    /// </summary>
    internal static class AmountLedRowMath
    {
        /// <summary>Left edge of the Amount band.</summary>
        public const int AmountX = SnapshotItemGridLayout.CellAmountX;

        /// <summary>Gap between the Amount band and the icon after it.</summary>
        public const int AmountToNameGap = SnapshotItemGridLayout.CellAmountGap;

        /// <summary>
        /// Gap a plan row keeps between its icon frame and the name beside
        /// it. Derived from the icon-led rail rather than restated, because
        /// the amount-led tables stack on one panel with the icon-led ones
        /// and a reader sees one rule down the names.
        /// </summary>
        public const int IconToNameGap =
            PlanRelayoutMath.IconLedRowNameX - PlanRelayoutMath.IconLedRowIconX
                - PlanContentHeightMath.RowIconFrameSize;

        /// <summary>Left edge of the row's tier-2 icon frame.</summary>
        public static int IconX(int amountBandWidth)
        {
            return AmountX + (amountBandWidth > 0 ? amountBandWidth : 0) + AmountToNameGap;
        }

        /// <summary>Left edge of the row's name text, past the icon.</summary>
        public static int NameX(int amountBandWidth)
        {
            return IconX(amountBandWidth) + PlanContentHeightMath.RowIconFrameSize + IconToNameGap;
        }

        /// <summary>
        /// Where one piece of text sits inside the Amount band: centred, so
        /// a short "1x" and a long "4250x" read as one column. The header
        /// word takes this same seat, which is what puts it over its own
        /// digits.
        /// </summary>
        public static int AmountTextX(int amountBandWidth, int textWidth)
        {
            return JustifiedColumnTracks.CenteredInBand(AmountX, amountBandWidth, textWidth);
        }

        /// <summary>
        /// Where the Amount header cell ends and the Item one begins.
        /// Everything left of it is fixed for the render, so unlike the
        /// flexing name column this boundary does not move with the panel.
        /// </summary>
        public static int HeaderSplitX(int amountBandWidth)
        {
            return IconX(amountBandWidth) - (AmountToNameGap / 2);
        }
    }
}
