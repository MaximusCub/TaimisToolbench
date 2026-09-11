using System;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // NotesSectionLayoutMath is the Plan Notes section's own wrap/height
    // arithmetic - the production seam Views/Rendering/NotesSectionRenderer
    // calls once per note at build and again at resize settle. Tests drive
    // it through the same Func<string,int> measurement the renderer passes
    // a BitmapFont through.
    public class NotesSectionLayoutMathTests
    {
        private static readonly Func<string, int> Fixed10 = s => (s ?? "").Length * 10;

        // Roughly the live plan width: the sandbox screenshot the audit was
        // taken from had ~830px usable.
        private const int LivePanelWidth = 830;

        [Fact]
        public void TextBudget_ReservesTheCoinCellAndItsGap()
        {
            int plain = NotesSectionLayoutMath.TextBudget(LivePanelWidth, 0);
            int valued = NotesSectionLayoutMath.TextBudget(LivePanelWidth, 90);

            Assert.Equal(plain - 90 - NotesSectionLayoutMath.CoinGap, valued);
        }

        [Fact]
        public void TextBudget_MatchesTheSharedNameColumnFormula()
        {
            // Same PlanRelayoutMath shape every other label-before-a-
            // trailing-column row uses - not a second copy of the
            // arithmetic.
            Assert.Equal(
                PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    LivePanelWidth - NotesSectionLayoutMath.RightPadding,
                    90,
                    NotesSectionLayoutMath.CoinGap,
                    NotesSectionLayoutMath.LabelX),
                NotesSectionLayoutMath.TextBudget(LivePanelWidth, 90));
        }

        [Fact]
        public void WrapNote_ShortNote_IsOneIndentedLine()
        {
            var wrapped = NotesSectionLayoutMath.WrapNote("Total reclaimable value", LivePanelWidth, 0, Fixed10);

            Assert.Single(wrapped.Lines);
            Assert.Equal("  Total reclaimable value", wrapped.Lines[0]);
            Assert.False(wrapped.Truncated);
        }

        [Fact]
        public void WrapNote_EmptyNote_IsStillOneRow()
        {
            var wrapped = NotesSectionLayoutMath.WrapNote("", LivePanelWidth, 0, Fixed10);

            Assert.Single(wrapped.Lines);
            Assert.Equal("", wrapped.Lines[0]);
        }

        [Fact]
        public void WrapNote_NullNote_IsStillOneRow()
        {
            Assert.Single(NotesSectionLayoutMath.WrapNote(null, LivePanelWidth, 0, Fixed10).Lines);
        }

        [Fact]
        public void WrapNote_LongNote_WrapsInsteadOfEllipsizing()
        {
            // The forge-scope note the live capture showed, as ONE note.
            // Before the fix this was cut to a single ~100-character line
            // plus a hover-only tooltip.
            const string note = "This plan includes a Mystic Clover-style Mystic Forge yield - its " +
                "expected output is already probability-adjusted. True multi-outcome Mystic Forge " +
                "gambles (e.g. precursor forging) are a different mechanic.";

            var wrapped = NotesSectionLayoutMath.WrapNote(note, LivePanelWidth, 0, Fixed10);

            Assert.True(wrapped.Lines.Count > 1);
            Assert.False(wrapped.Truncated);
            Assert.All(wrapped.Lines, line => Assert.DoesNotContain("...", line));
            // Every word survives - the point of the fix.
            Assert.Contains("mechanic.", string.Join(" ", wrapped.Lines));
        }

        [Fact]
        public void WrapNote_EveryLineIsIndentedSoAWrappedNoteReadsAsOneBlock()
        {
            const string note = "Buy the Mystic Clover recipe to craft it instead of buying it - " +
                "recipe costs a great deal more than the audit expected it to";

            var wrapped = NotesSectionLayoutMath.WrapNote(note, LivePanelWidth, 0, Fixed10);

            Assert.True(wrapped.Lines.Count > 1);
            Assert.All(wrapped.Lines, line => Assert.StartsWith(NotesSectionLayoutMath.LineIndent, line));
        }

        [Fact]
        public void WrapNote_ValuedNote_FirstLineIsShorterThanTheRest()
        {
            // The coin cell sits on the FIRST line only, so only that line
            // pays for it.
            const string note = "Excess: 12x Glob of Ectoplasm reclaimable at the trading post today";

            var plain = NotesSectionLayoutMath.WrapNote(note, LivePanelWidth, 0, Fixed10);
            var valued = NotesSectionLayoutMath.WrapNote(note, LivePanelWidth, 200, Fixed10);

            Assert.True(valued.Lines[0].Length < plain.Lines[0].Length);
            Assert.All(valued.Lines, line => Assert.True(Fixed10(line) <= NotesSectionLayoutMath.TextBudget(LivePanelWidth, 0), line));
            Assert.True(Fixed10(valued.Lines[0]) <= NotesSectionLayoutMath.TextBudget(LivePanelWidth, 200));
        }

        [Fact]
        public void WrapNote_ExplicitLineBreaks_ComposeWithWidthWrapping()
        {
            // A note whose own text carries breaks must keep them AND
            // still width-wrap each piece.
            const string note = "First sentence that is quite long and will not fit on a single line here.\n" +
                "Second.";

            var wrapped = NotesSectionLayoutMath.WrapNote(note, 300, 0, Fixed10);

            Assert.True(wrapped.Lines.Count > 2);
            Assert.Equal("  Second.", wrapped.Lines[wrapped.Lines.Count - 1]);
        }

        [Fact]
        public void WrapNote_NarrowingThenWideningRecoversTheOriginalLines()
        {
            // The resize path: the renderer re-wraps at the settled width
            // and rebuilds the section whenever the line count moved, so
            // the wrap at a given width must not depend on the width it
            // was previously wrapped at - no blank padding, no leftover
            // ellipsis.
            const string note = "Excess: 12x Glob of Ectoplasm reclaimable at the trading post today";

            var wide = NotesSectionLayoutMath.WrapNote(note, LivePanelWidth, 0, Fixed10);
            var narrow = NotesSectionLayoutMath.WrapNote(note, 200, 0, Fixed10);
            var widenedBack = NotesSectionLayoutMath.WrapNote(note, LivePanelWidth, 0, Fixed10);

            Assert.True(narrow.Lines.Count > wide.Lines.Count);
            Assert.Equal(wide.Lines, widenedBack.Lines);
            Assert.All(widenedBack.Lines, line => Assert.NotEqual("", line));
            Assert.All(widenedBack.Lines, line => Assert.DoesNotContain("...", line));
        }

        [Fact]
        public void WrapNote_NarrowPanel_StillProducesLinesRatherThanDegenerating()
        {
            var wrapped = NotesSectionLayoutMath.WrapNote("alpha beta gamma", 30, 0, Fixed10);

            Assert.NotEmpty(wrapped.Lines);
            Assert.All(wrapped.Lines, line => Assert.NotNull(line));
        }

        [Fact]
        public void WrapNote_NullMeasure_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => NotesSectionLayoutMath.WrapNote("abc", LivePanelWidth, 0, null));
        }

        // --- NoteHeight: the arm that counts LINES, not note rows ---
        [Fact]
        public void NoteHeight_CountsWrappedLinesNotNoteRows()
        {
            const string note = "This plan includes a Mystic Clover-style Mystic Forge yield - its " +
                "expected output is already probability-adjusted.";

            int lines = NotesSectionLayoutMath.WrapNote(note, LivePanelWidth, 0, Fixed10).Lines.Count;

            Assert.True(lines > 1);
            Assert.Equal(
                lines * PlanContentHeightMath.FallbackTextRowHeight,
                NotesSectionLayoutMath.NoteHeight(lines, hasIcon: false));
            // The pre-fix arm (one row per note) would have undercounted.
            Assert.True(
                NotesSectionLayoutMath.NoteHeight(lines, hasIcon: false)
                    > PlanContentHeightMath.FallbackTextRowHeight);
        }

        [Fact]
        public void NoteHeight_ZeroLines_IsZero()
        {
            Assert.Equal(0, NotesSectionLayoutMath.NoteHeight(0, hasIcon: false));
            Assert.Equal(0, NotesSectionLayoutMath.NoteHeight(-3, hasIcon: false));
        }

        [Fact]
        public void NoteHeight_UsesTheSharedFixedRowHeightConstant()
        {
            Assert.Equal(
                PlanContentHeightMath.FallbackTextRowHeight,
                NotesSectionLayoutMath.NoteHeight(1, hasIcon: false));
        }

        /// <summary>
        /// A note that names an item spends the icon-led band on its FIRST
        /// line and a plain text row on every line after it, so its height
        /// is not a multiple of either.
        /// </summary>
        [Fact]
        public void NoteHeight_WithAnIcon_SpendsTheIconBandOnTheFirstLineOnly()
        {
            Assert.Equal(
                NotesSectionLayoutMath.IconLineHeight,
                NotesSectionLayoutMath.NoteHeight(1, hasIcon: true));
            Assert.Equal(
                NotesSectionLayoutMath.IconLineHeight
                    + (2 * PlanContentHeightMath.FallbackTextRowHeight),
                NotesSectionLayoutMath.NoteHeight(3, hasIcon: true));
            Assert.True(
                NotesSectionLayoutMath.IconLineHeight
                    > PlanContentHeightMath.FallbackTextRowHeight,
                "an icon-led line has to be taller than a text line to hold the icon");
        }

        /// <summary>
        /// The subject's name eats into its own note's first line and
        /// nothing else: later lines hang from the name's rule and get the
        /// full column.
        /// </summary>
        [Fact]
        public void SubjectBudgets_OnlyTheFirstLinePaysForTheNameAndTheCoinCell()
        {
            int first = NotesSectionLayoutMath.SubjectFirstLineBudget(LivePanelWidth, 0, 100);
            int rest = NotesSectionLayoutMath.SubjectRestBudget(LivePanelWidth);

            Assert.Equal(rest - 100 - NotesSectionLayoutMath.NameToNoteGap, first);
            Assert.True(
                NotesSectionLayoutMath.SubjectFirstLineBudget(LivePanelWidth, 60, 100) < first,
                "a coin cell has to narrow the first line it sits on");
        }

        [Fact]
        public void SubjectMaxWidth_HoldsTheFloorOnAPathologicallyNarrowPanel()
        {
            Assert.Equal(
                NotesSectionLayoutMath.MinTextBudget,
                NotesSectionLayoutMath.SubjectMaxWidth(40));
            Assert.True(
                NotesSectionLayoutMath.SubjectMaxWidth(LivePanelWidth)
                    < NotesSectionLayoutMath.SubjectRestBudget(LivePanelWidth),
                "a name may never take the whole note column");
        }
    }
}
