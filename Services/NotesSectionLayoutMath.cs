using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The Plan Notes section's own layout arithmetic (Blish-free,
    /// unit-testable): the per-line text budget a note has to work with,
    /// the wrap of one note into physical lines, and the resulting body
    /// height.
    ///
    /// Kept out of PlanContentHeightMath for the same reason
    /// SummarySectionLayoutMath is (see that class's doc comment): Notes is
    /// now the one section whose height is not a function of its row list
    /// alone - it needs the panel width and a font measurement - and
    /// PlanContentHeightMath.SectionBodyHeight's signature deliberately has
    /// neither. Views/CraftingPlanView.CreateCollapsibleSection special-
    /// cases PlanSectionType.Notes to the height its renderer actually
    /// built, exactly as it special-cases Summary.
    ///
    /// No row height is redefined here: NoteHeight reads
    /// PlanContentHeightMath, so one wrapped line is still exactly one
    /// fixed-height row and the DEBUG per-row assert in
    /// NotesSectionRenderer stays the real check.
    /// </summary>
    internal static class NotesSectionLayoutMath
    {
        /// <summary>Left x of a plain note line's label.</summary>
        public const int LabelX = 8;

        /// <summary>
        /// Left x of the icon on a note that has an item subject - the
        /// same gutter every icon-led row in the plan tab opens with.
        /// </summary>
        public const int IconX = 8;

        /// <summary>
        /// Left x of the subject's NAME, past the icon: the same rule the
        /// plan's tables put their name column on. Continuation lines of
        /// the note hang from here too, so a wrapped note reads as one
        /// block under its own name.
        /// </summary>
        public const int NameX = IconX + PlanContentHeightMath.RowIconFrameSize + 8;

        /// <summary>Gap between the subject's name and the note itself.</summary>
        public const int NameToNoteGap = 12;

        /// <summary>
        /// What parts the subject's name from the note beside it. Without
        /// it the two read as one run of prose that changes subject with no
        /// mark: "Gift of the Hylek The vendor who sells this item ...".
        /// </summary>
        public const string SubjectSeparator = ":";

        /// <summary>
        /// The subject's name as it is DRAWN. The name is ellipsized first
        /// and the separator appended after, so a name too long for its
        /// third of the column still shows the mark that parts it from the
        /// note.
        /// </summary>
        public static string SubjectLabel(string subject)
        {
            return string.IsNullOrEmpty(subject) ? "" : subject + SubjectSeparator;
        }

        /// <summary>
        /// Thickness of the rule under a link, in logical pixels. Two, not
        /// one: Blish applies the GW2 UI scale as a real GPU matrix, so a
        /// 1px quad rasterizes to floor(0.81) = 0 physical pixels at the
        /// smallest shipped scale and vanishes (KNOWN-ISSUES #23), which is
        /// the same floor LabelHelpers.CreateRowDivider sits on.
        /// </summary>
        public const int LinkUnderlineThickness = 2;

        /// <summary>
        /// How much of the link colour the rule carries. The fineness a
        /// hyperlink wants is one pixel of ink, and thickness may not drop
        /// below two, so the rule spends the same ink over two rows
        /// instead: alpha times thickness is 1. Alpha changes no geometry,
        /// so the rule still covers a physical pixel at every scale.
        /// </summary>
        public const float LinkUnderlineInkAlpha = 1f / LinkUnderlineThickness;

        /// <summary>
        /// Height of a note's FIRST line when that note has an icon: the
        /// icon-led band every plan table row draws in, without the row
        /// rule's own pixels, because the Notes section draws no rules.
        /// </summary>
        public const int IconLineHeight = PlanContentHeightMath.IconLedRowVisibleHeight;

        /// <summary>Right-edge padding shared with every other section.</summary>
        public const int RightPadding = UiSpacing.SectionRightPad;

        /// <summary>Gap reserved between the text and a coin cell.</summary>
        public const int CoinGap = 12;

        /// <summary>
        /// Leading indent every note line carries, continuation lines
        /// included, so a wrapped note reads as one block rather than as
        /// separate notes.
        /// </summary>
        public const string LineIndent = "  ";

        /// <summary>
        /// Floor for the per-line text budget once the indent is charged
        /// against it - mirrors NameMaxWidthBeforeColumn's own 20px clamp,
        /// and keeps a pathologically narrow panel from producing a
        /// zero/negative budget the wrapper would have to degenerate on.
        /// </summary>
        public const int MinTextBudget = 12;

        /// <summary>
        /// Width available to a note's text on a line that reserves
        /// coinCellWidth px for a right-aligned coin cell (0 on lines that
        /// have none). Same NameMaxWidthBeforeColumn shape every other
        /// label-before-a-trailing-column row in this codebase uses.
        /// </summary>
        public static int TextBudget(int panelWidth, int coinCellWidth)
        {
            return PlanRelayoutMath.NameMaxWidthBeforeColumn(
                panelWidth - RightPadding, coinCellWidth, coinCellWidth > 0 ? CoinGap : 0, LabelX);
        }

        /// <summary>
        /// Wraps one note into indented physical lines. The coin cell only
        /// ever sits on the FIRST line, so only that line's budget is
        /// reduced by it; every later line gets the full width.
        /// </summary>
        public static TextWrapMath.WrappedText WrapNote(
            string label, int panelWidth, int coinCellWidth, Func<string, int> measure)
        {
            if (measure == null)
            {
                throw new ArgumentNullException(nameof(measure));
            }

            int indentWidth = measure(LineIndent);
            int firstBudget = Clamp(TextBudget(panelWidth, coinCellWidth) - indentWidth);
            int restBudget = Clamp(TextBudget(panelWidth, 0) - indentWidth);

            var wrapped = TextWrapMath.Wrap(label ?? "", firstBudget, restBudget, measure);

            var indented = new string[wrapped.Lines.Count];
            for (int i = 0; i < wrapped.Lines.Count; i++)
            {
                // An empty line is a deliberate blank line in the source
                // text, not content - indenting it would put stray
                // whitespace in an otherwise blank row.
                indented[i] = wrapped.Lines[i].Length == 0 ? "" : LineIndent + wrapped.Lines[i];
            }

            return new TextWrapMath.WrappedText(indented, wrapped.Truncated);
        }

        /// <summary>
        /// Height one note occupies: one fixed-height row per wrapped
        /// LINE, and the taller icon band for the first line of a note that
        /// has an item subject. The section's own height is the sum over
        /// its notes, which is why there is no whole-section arithmetic
        /// here - see NotesSectionRenderer's doc comment for why the
        /// section reports the height it actually built.
        /// </summary>
        public static int NoteHeight(int lineCount, bool hasIcon)
        {
            int lines = lineCount > 0 ? lineCount : 0;
            if (!hasIcon)
            {
                return lines * PlanContentHeightMath.FallbackTextRowHeight;
            }

            int tail = lines > 1 ? lines - 1 : 0;
            return IconLineHeight + (tail * PlanContentHeightMath.FallbackTextRowHeight);
        }

        /// <summary>
        /// Width the subject's name may take before it ellipsizes. A third
        /// of the note column: past that the note itself has less room on
        /// its own first line than the name it follows, and an item name is
        /// unbounded.
        /// </summary>
        public static int SubjectMaxWidth(int panelWidth)
        {
            int room = (panelWidth - RightPadding - NameX) / 3;
            return room > MinTextBudget ? room : MinTextBudget;
        }

        /// <summary>
        /// Text budget for the first line of a note with an item subject,
        /// which starts past that name and still has to leave room for any
        /// right-aligned coin cell.
        /// </summary>
        public static int SubjectFirstLineBudget(
            int panelWidth, int coinCellWidth, int subjectWidth)
        {
            return Clamp(PlanRelayoutMath.NameMaxWidthBeforeColumn(
                panelWidth - RightPadding, coinCellWidth, coinCellWidth > 0 ? CoinGap : 0,
                NameX + subjectWidth + NameToNoteGap));
        }

        /// <summary>
        /// Text budget for every later line of such a note, which hangs
        /// from the name's own rule.
        /// </summary>
        public static int SubjectRestBudget(int panelWidth)
        {
            return Clamp(PlanRelayoutMath.NameMaxWidthBeforeColumn(
                panelWidth - RightPadding, 0, 0, NameX));
        }

        private static int Clamp(int budget)
        {
            return budget > MinTextBudget ? budget : MinTextBudget;
        }
    }
}
