namespace TaimisToolbench.Services
{
    /// <summary>
    /// The measured Menomonia glyph metrics behind every vertical constant in
    /// the plan view, and the ramp tier each chrome role sits in (Blish-free,
    /// unit-testable). Views resolve the actual BitmapFont objects through
    /// Views/Rendering/UiFonts; the arithmetic that decides how tall a band
    /// has to be to hold one lives here, with the numbers it is decided from.
    ///
    /// Two defects in the shipped font inventory constrain every choice below
    /// and must not be re-litigated by measurement-free reasoning: 18-REGULAR
    /// is unusable for prose (its space glyph advances 4px, against 7 at
    /// 16-regular and 9 at 18-bold, so word gaps collapse), and 22-REGULAR is
    /// metrically identical to 24-regular, so there is no regular-weight step
    /// between 20 and 24 and 22-regular must never be loaded. 22-BOLD is a
    /// genuine intermediate.
    ///
    /// Measurement method and the rest of the ramp: docs/ARCHITECTURE.md
    /// section 11.
    /// </summary>
    internal static class TypeRampMetrics
    {
        /// <summary>
        /// One font's measured vertical metrics, in the units every layout
        /// constant in this module is written in: pixels from the top of
        /// the line box a Label draws its text in.
        /// </summary>
        public readonly struct FontInk
        {
            /// <summary>Line box height - what an autosized Label reports.</summary>
            public readonly int LineHeight;

            /// <summary>Ink height of a capital (H/M).</summary>
            public readonly int CapHeight;

            /// <summary>Top of capital ink inside the line box.</summary>
            public readonly int CapTopY;

            /// <summary>Baseline inside the line box.</summary>
            public readonly int BaselineY;

            /// <summary>
            /// Lowest ink of any printable ASCII glyph (the descenders -
            /// j p q y, and Q/g where the font has them). The number every
            /// divider-clearance and band-height constant is derived from.
            /// </summary>
            public readonly int LowestInk;

            public FontInk(int lineHeight, int capHeight, int capTopY, int baselineY, int lowestInk)
            {
                LineHeight = lineHeight;
                CapHeight = capHeight;
                CapTopY = capTopY;
                BaselineY = baselineY;
                LowestInk = lowestInk;
            }
        }

        // Measured, in the order the ramp uses them. Sizes the module does
        // not load are deliberately absent rather than listed "for
        // completeness" - a metric with no caller is a metric nothing
        // re-measures when it drifts.
        public static readonly FontInk Regular14 = new FontInk(18, 13, 2, 15, 19);
        public static readonly FontInk Regular16 = new FontInk(20, 14, 3, 17, 21);
        public static readonly FontInk Bold18 = new FontInk(23, 16, 3, 19, 23);
        public static readonly FontInk Regular20 = new FontInk(25, 17, 4, 21, 26);
        public static readonly FontInk Bold20 = new FontInk(25, 17, 4, 21, 26);
        public static readonly FontInk Bold22 = new FontInk(27, 18, 4, 23, 27);
        public static readonly FontInk Bold24 = new FontInk(29, 20, 4, 25, 30);
        public static readonly FontInk Regular32 = new FontInk(36, 24, 6, 31, 37);

        // --- Tier seats ---
        //
        // The two promoted tiers are named ONCE, here, so a retreat from
        // 20/24 to 18/22 is a constant swap rather than a hunt through
        // renderers. The retreat,
        // MEASURED by applying it and running the suite - six constants,
        // no test edits, every band height unchanged:
        //     ColumnHeaderPointSize 20 -> 18, ColumnHeaderInk Bold20 -> Bold18
        //     SectionTitlePointSize 24 -> 22, SectionTitleInk Bold24 -> Bold22
        //     PlanContentHeightMath.ColumnHeaderLabelY   4 -> 5
        //     PlanContentHeightMath.SectionHeaderCaretY 10 -> 9
        // The last two are not free-standing choices: a label y is one
        // half of a band's arithmetic and the shorter font's cap top and
        // baseline both move, so both follow from the seat. Each is named
        // by the assertion that fails without it, so a retreat that
        // forgets one is told which number to write.
        //
        // No absolute point size is asserted anywhere. A test that pinned
        // 20 as a floor would read as an invariant while really encoding
        // one of the two candidate seats, and it would fail by
        // construction on the documented fallback above.

        /// <summary>Every column header, and the Total Cost tile captions.</summary>
        public const int ColumnHeaderPointSize = 20;

        /// <summary>The eight section titles.</summary>
        public const int SectionTitlePointSize = 24;

        /// <summary>
        /// The status line. Bold, not regular: at this size regular is the
        /// collapsed-space defect, and status text is always multi-word.
        /// </summary>
        public const int StatusPointSize = 18;

        /// <summary>
        /// The plan header's " x N needed" suffix (regular - a subordinate
        /// annotation beside a 32pt title) and the craft-step number badge
        /// (bold, digits only). Both exist to retire 18-regular entirely.
        /// </summary>
        public const int SmallHeadingPointSize = 20;

        /// <summary>
        /// Whether the installed Menomonia REGULAR face at this size can
        /// be drawn with at all. The two exclusions are the measured
        /// defects in this class's own doc comment, named ONCE here:
        /// Views/Rendering/UiFonts.Regular refuses the same two at the
        /// seam, and the ramp's tests refuse to seat a regular-weight
        /// role on one.
        /// </summary>
        public static bool HasUsableRegularFace(int pointSize)
        {
            return pointSize != 18 && pointSize != 22;
        }

        public static FontInk ColumnHeaderInk => Bold20;

        public static FontInk SectionTitleInk => Bold24;

        public static FontInk StatusInk => Bold18;

        /// <summary>Body rows, everywhere. Not part of the ramp change.</summary>
        public static FontInk BodyInk => Regular16;

        /// <summary>Pills, tags, footnotes. The floor - nothing goes below it.</summary>
        public static FontInk CaptionInk => Regular14;

        /// <summary>
        /// The digits in a coin run, sized against the 18px coin icon
        /// rather than against the prose around them - the measurement is
        /// on Views/Rendering/UiFonts.CoinDigits, the face this ink belongs
        /// to. Named for the role so a caller putting a coin run on one
        /// baseline with Body text reads the ink the run is drawn in.
        /// </summary>
        public static FontInk CoinDigitInk => Regular16;

        /// <summary>
        /// Where the lowest ink of a line drawn at <paramref name="labelY"/>
        /// lands. Every band height and divider clearance in the plan view
        /// is a statement about this number.
        /// </summary>
        public static int InkBottom(FontInk font, int labelY)
        {
            return labelY + font.LowestInk;
        }

        /// <summary>
        /// The y a line has to be drawn at for its baseline to land on
        /// <paramref name="baseline"/> - how two different fonts on one
        /// reading line are aligned (the section header's caret against its
        /// title, the plan header's qty suffix against its name).
        /// </summary>
        public static int BaselineAlignedY(FontInk font, int baseline)
        {
            return baseline - font.BaselineY;
        }
    }
}
