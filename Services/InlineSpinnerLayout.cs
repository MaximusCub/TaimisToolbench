namespace TaimisToolbench.Services
{
    /// <summary>
    /// Where an inline loading spinner sits relative to the status label it
    /// trails (Blish-free, unit-testable). Both status rows that show one
    /// are a fixed-height strip with an auto-width label pinned to the left
    /// edge, so the spinner's position is pure arithmetic over the label's
    /// live bounds - which is the whole of what the view has to get right.
    /// <para>See docs/ARCHITECTURE.md section 4.</para>
    /// </summary>
    internal static class InlineSpinnerLayout
    {
        /// <summary>
        /// Spinner edge for the plan tab's status row. That row is
        /// <see cref="TopRegionLayoutMath.StatusToSeparatorGap"/> logical
        /// pixels tall, so 20 leaves clearance above and below without the
        /// spinner ever reaching the separator beneath it.
        /// <para>
        /// 20, not the 18 it sat at beside a Body status line: the spinner
        /// is centred on the label's line box, so it scales with the label
        /// - TypeRampMetrics.StatusInk's box is 23 tall against Body's 20.
        /// Proportion against the spinner ART is by eye and wants a live
        /// check; the clearance is arithmetic and is asserted.
        /// </para>
        /// </summary>
        public const int PlanStripSize = 20;

        /// <summary>
        /// Spinner edge for the Snapshot tab's status row, which is its own
        /// panel of <see cref="SnapshotHeaderLayout.StatusRowHeight"/>.
        /// Also used by the Ranker and Plan History toolbars, which are 40
        /// tall and so are not the binding constraint.
        /// <para>
        /// 22 is the largest even size that still clears the Snapshot row:
        /// the label is drawn at
        /// <see cref="SnapshotHeaderLayout.StatusLabelTopInset"/> and
        /// <see cref="Place"/> clamps the spinner's top to the label's, so
        /// 2 + 22 leaves two pixels under a 26-tall row.
        /// </para>
        /// </summary>
        public const int SnapshotStatusSize = 22;

        /// <summary>
        /// Gap between the label's right edge and the spinner.
        /// </summary>
        public const int LabelGap = 6;

        /// <summary>
        /// Places a square spinner of <paramref name="spinnerSize"/> to the
        /// right of a label occupying the given bounds. The spinner is
        /// vertically centered on the label, then clamped so it never
        /// starts above the label's own top: the label is top-aligned in
        /// its row, so that clamp is what keeps the spinner inside the row
        /// band whenever the spinner is the taller of the two.
        /// </summary>
        public static SpinnerPlacement Place(
            int labelX, int labelY, int labelWidth, int labelHeight, int spinnerSize, int gap)
        {
            int x = labelX + labelWidth + gap;
            int y = labelY + (labelHeight - spinnerSize) / 2;
            if (y < labelY)
            {
                y = labelY;
            }

            return new SpinnerPlacement(x, y);
        }
    }

    internal readonly struct SpinnerPlacement
    {
        public readonly int X;
        public readonly int Y;

        public SpinnerPlacement(int x, int y)
        {
            X = x;
            Y = y;
        }
    }
}
