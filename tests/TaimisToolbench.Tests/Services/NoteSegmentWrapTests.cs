using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Wrapping a note whose sentence carries a link. What has to survive
    /// the wrap is which runs are links and what each of them opens: the
    /// view draws one label per run and rules a line under the linked ones,
    /// so a run that lost its target would draw as ordinary text and a run
    /// that gained one would underline the wrong words.
    /// </summary>
    public class NoteSegmentWrapTests
    {
        // A fixed-width font: every character is 10px wide, so a budget is
        // a character count and every case below reads as one.
        private static readonly Func<string, int> Fixed10 = s => (s ?? "").Length * 10;

        private static readonly IconWikiTarget Achievement =
            IconWikiTarget.ItemPage("Supply Line Management");

        private static List<PlanNoteSegment> Sentence()
        {
            return new List<PlanNoteSegment>
            {
                PlanNoteSegment.Plain("The vendor requires the "),
                PlanNoteSegment.Linked("Supply Line Management", Achievement),
                PlanNoteSegment.Plain(" achievement."),
            };
        }

        private static string TextOf(IReadOnlyList<PlanNoteSegment> line)
        {
            return string.Concat(line.Select(p => p.Text));
        }

        [Fact]
        public void AWideEnoughLine_KeepsTheThreeRunsAsWritten()
        {
            var wrapped = NoteSegmentWrap.Wrap(Sentence(), 2000, 2000, Fixed10, 24);

            var line = Assert.Single(wrapped.Lines);
            Assert.False(wrapped.Truncated);
            Assert.Equal(3, line.Count);
            Assert.Equal(
                "The vendor requires the Supply Line Management achievement.", TextOf(line));
            Assert.Equal(new[] { false, true, false }, line.Select(p => p.IsLink).ToArray());
        }

        /// <summary>
        /// The whole point: a link broken over two lines is still a link on
        /// both, and both halves open the same page.
        /// </summary>
        [Fact]
        public void ALinkBrokenAcrossLines_StaysALinkOnEveryLineItReaches()
        {
            var wrapped = NoteSegmentWrap.Wrap(Sentence(), 300, 300, Fixed10, 24);

            Assert.True(wrapped.Lines.Count > 1);
            var linked = wrapped.Lines
                .SelectMany(line => line)
                .Where(p => p.IsLink)
                .ToArray();

            Assert.True(linked.Length >= 2, "the link did not break across lines");
            Assert.Equal(
                "Supply Line Management",
                string.Join(" ", linked.Select(p => p.Text.Trim())));
            foreach (var piece in linked)
            {
                Assert.Equal(Achievement.BuildUrl(), piece.Link.BuildUrl());
            }
        }

        /// <summary>
        /// No line may end in the space the wrap broke on, or the underline
        /// under a link would run past its last letter.
        /// </summary>
        [Fact]
        public void NoWrappedLineKeepsTheSpaceItBrokeOn()
        {
            var wrapped = NoteSegmentWrap.Wrap(Sentence(), 300, 300, Fixed10, 24);

            foreach (var line in wrapped.Lines)
            {
                Assert.Equal(TextOf(line).TrimEnd(), TextOf(line));
                Assert.Equal(TextOf(line).TrimStart(), TextOf(line));
            }
        }

        /// <summary>
        /// Nothing is dropped by the wrap itself. The words are what the
        /// reader came for and the row's own hover carries only what the
        /// LINE CAP took.
        /// </summary>
        [Fact]
        public void EveryWordSurvivesTheWrap()
        {
            var wrapped = NoteSegmentWrap.Wrap(Sentence(), 240, 180, Fixed10, 24);

            Assert.False(wrapped.Truncated);
            Assert.Equal(
                "The vendor requires the Supply Line Management achievement.",
                string.Join(" ", wrapped.Lines.Select(TextOf)));
        }

        [Fact]
        public void TheFirstLineIsBudgetedSeparatelyFromTheRest()
        {
            // 60px of first line is six characters; the rest of the note
            // gets 240.
            var wrapped = NoteSegmentWrap.Wrap(Sentence(), 60, 240, Fixed10, 24);

            Assert.Equal("The", TextOf(wrapped.Lines[0]));
            Assert.True(TextOf(wrapped.Lines[1]).Length > 3);
        }

        /// <summary>
        /// Runs of the same segment merge, so a line costs one label per
        /// colour rather than one per word.
        /// </summary>
        [Fact]
        public void ConsecutiveWordsOfOneSegmentBecomeOneRun()
        {
            var wrapped = NoteSegmentWrap.Wrap(
                new List<PlanNoteSegment> { PlanNoteSegment.Plain("one two three four") },
                2000, 2000, Fixed10, 24);

            var line = Assert.Single(wrapped.Lines);
            var piece = Assert.Single(line);
            Assert.Equal("one two three four", piece.Text);
        }

        [Fact]
        public void AWordWiderThanTheWholeLineEllipsizesRatherThanOverhanging()
        {
            var wrapped = NoteSegmentWrap.Wrap(
                new List<PlanNoteSegment> { PlanNoteSegment.Plain("Antediluvianism") },
                60, 60, Fixed10, 24);

            var piece = Assert.Single(Assert.Single(wrapped.Lines));
            Assert.Equal(6, piece.Text.Length);
            Assert.EndsWith(TextWrapMath.Ellipsis, piece.Text);
        }

        [Fact]
        public void PastTheLineCap_TheTailIsMarkedTruncated()
        {
            var wrapped = NoteSegmentWrap.Wrap(Sentence(), 60, 60, Fixed10, 2);

            Assert.True(wrapped.Truncated);
            Assert.Equal(2, wrapped.Lines.Count);
            Assert.EndsWith(TextWrapMath.Ellipsis, TextOf(wrapped.Lines[1]));
        }

        [Fact]
        public void AnEmptyNoteStillProducesTheOneRowItsCallerWillDraw()
        {
            var wrapped = NoteSegmentWrap.Wrap(
                new List<PlanNoteSegment>(), 200, 200, Fixed10, 24);

            Assert.Single(wrapped.Lines);
            Assert.Empty(wrapped.Lines[0]);
        }

        [Fact]
        public void ANullMeasurementIsRejectedRatherThanGuessed()
        {
            Assert.Throws<ArgumentNullException>(
                () => NoteSegmentWrap.Wrap(Sentence(), 200, 200, null, 24));
        }

        /// <summary>
        /// A segment whose target names no page is plain text, which is how
        /// a note about a subject the module cannot resolve degrades.
        /// </summary>
        [Fact]
        public void ASegmentWithNoPageIsNotALink()
        {
            var wrapped = NoteSegmentWrap.Wrap(
                new List<PlanNoteSegment>
                {
                    PlanNoteSegment.Linked("Unknown Item", IconWikiTarget.ItemPage("Unknown Item")),
                },
                2000, 2000, Fixed10, 24);

            Assert.False(Assert.Single(Assert.Single(wrapped.Lines)).IsLink);
        }
    }
}
