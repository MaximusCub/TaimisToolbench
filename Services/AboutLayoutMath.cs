using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The About tab's two-column document (Blish-free, unit-testable): an
    /// identity card on the left, the two prose blocks on the right, and a
    /// reading measure every line of text on the tab is capped at.
    ///
    /// <para>
    /// ACCEPTED DIVERGENCE from the module's width-usage rule, declared here
    /// rather than hidden: past roughly a 1100px panel this tab stops using
    /// its width. A document does not stretch - a 280-character line at a
    /// 2560 window is a worse artefact than white space, and the plan tab's
    /// own tooltip work already respects the same rule. Every other surface
    /// in the module uses all of its width. The lever is one constant,
    /// <see cref="ProseMeasure"/>: raising it to 720 widens every block on
    /// the tab in one edit.
    /// </para>
    /// </summary>
    internal static class AboutLayoutMath
    {
        /// <summary>Left gutter of both columns - the module's content
        /// inset.</summary>
        public const int AboutInset = UiSpacing.Inset;

        /// <summary>Gutter between the facts column and the prose column.
        /// Wider than a within-column gap so the two read as two documents
        /// rather than as four columns.</summary>
        public const int ColumnGutter = 32;

        /// <summary>
        /// The measure, DERIVED not picked: 66 characters at the module's
        /// own measured Body-16 average of ~8.4px/character (see
        /// <see cref="SnapshotItemGridLayout.MaxCharWidthPx"/>'s doc
        /// comment). 66 * 8.4 = 554, rounded to the nearest multiple of the
        /// module's 8px spacing unit. Inside the 45-75 character reading
        /// band.
        /// </summary>
        public const int ProseTargetChars = 66;

        public const int ProseMeasure = 560;

        public const string SourceLabel = "Source";
        public const string AuthorLabel = "Author";
        public const string BuiltWithLabel = "Built with";
        public const string LicenseLabel = "License";
        public const string DataDirectoryLabel = "Data directory";

        /// <summary>
        /// The five fact labels the tab ships, in render order. They live
        /// here rather than inline in the view so
        /// <see cref="LabelRunChars"/> can be checked against the strings it
        /// exists to hold - a floor derived from one label copied into a
        /// test proves nothing about the other four.
        /// </summary>
        public static readonly IReadOnlyList<string> FactLabels = new[]
        {
            SourceLabel,
            AuthorLabel,
            BuiltWithLabel,
            LicenseLabel,
            DataDirectoryLabel,
        };

        /// <summary>
        /// Floor for the facts column's label band: 14 characters ("Data
        /// directory") at the same upper-bound-per-character rule the grids
        /// use. The band itself is MEASURED across the five labels at build;
        /// this is what it cannot go below.
        /// </summary>
        public const int LabelRunChars = 14;

        public const int LabelFloor = LabelRunChars * SnapshotItemGridLayout.MaxCharWidthPx;

        /// <summary>Gap between a fact's label and its value - the module's
        /// one name-to-column gap.</summary>
        public const int LabelToValueGap = SnapshotItemGridLayout.CellAmountGap;

        /// <summary>Floor for a value, copyable or not.</summary>
        public const int ValueFloor = 200;

        public const int FactsMinWidth =
            AboutInset + LabelFloor + LabelToValueGap + ValueFloor + PlanRelayoutMath.TableRightMargin;

        /// <summary>Two columns need both minimums plus the gutter.</summary>
        public const int TwoColumnThreshold = FactsMinWidth + ColumnGutter + ProseMeasure;

        public static int ColumnCount(int panelWidth)
        {
            return panelWidth >= TwoColumnThreshold ? 2 : 1;
        }

        public static int ColumnWidth(int panelWidth)
        {
            if (panelWidth <= 0)
            {
                return 0;
            }

            return ColumnCount(panelWidth) == 1 ? panelWidth : (panelWidth - ColumnGutter) / 2;
        }

        /// <summary>Left edge of the second column, which is where the prose
        /// blocks start once the tab is two columns wide.</summary>
        public static int SecondColumnX(int panelWidth)
        {
            return ColumnWidth(panelWidth) + ColumnGutter;
        }

        /// <summary>
        /// Width a run of text inside a column may occupy: the column's own
        /// content width, capped at <see cref="ProseMeasure"/> however wide
        /// the column gets.
        /// </summary>
        public static int TextBudget(int columnWidth)
        {
            int available = PlanRelayoutMath.PinnedRightEdge(columnWidth) - AboutInset;
            if (available < 20)
            {
                available = 20;
            }

            return available < ProseMeasure ? available : ProseMeasure;
        }

        /// <summary>Left edge of the facts table's value column, given the
        /// band its six labels measured to.</summary>
        public static int ValueX(int labelBandWidth)
        {
            int band = labelBandWidth > LabelFloor ? labelBandWidth : LabelFloor;
            return AboutInset + band + LabelToValueGap;
        }

        /// <summary>
        /// Width a fact's value may occupy - the plan tables' rule, with the
        /// value as the flexing part because the label band is fixed by its
        /// own six strings.
        /// </summary>
        public static int ValueMaxWidth(int columnWidth, int labelBandWidth)
        {
            return PlanRelayoutMath.NameMaxWidthBeforeColumn(
                PlanRelayoutMath.PinnedRightEdge(columnWidth), 0, 0, ValueX(labelBandWidth));
        }

        /// <summary>
        /// A copyable value's TextBox is capped at the measure too: a 2300px
        /// box holding a URL is the same defect as a 2300px paragraph.
        /// </summary>
        public static int CopyBoxWidth(int columnWidth, int labelBandWidth)
        {
            int width = ValueMaxWidth(columnWidth, labelBandWidth);
            if (width < ValueFloor)
            {
                width = ValueFloor;
            }

            return width < ProseMeasure ? width : ProseMeasure;
        }
    }
}
