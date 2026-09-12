using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Greedy word wrap over a note's <see cref="PlanNoteSegment"/> list
    /// (Blish-free, unit-testable): the same shape
    /// <see cref="TextWrapMath.Wrap"/> has, for text whose middle carries a
    /// link and so cannot be one string.
    /// <para>
    /// Every candidate is measured as the WHOLE line rather than as a sum
    /// of per-piece widths. Blish tracks its fonts at minus one pixel, so
    /// measure("a") + measure("b") overstates measure("ab") by a pixel per
    /// join, and a sentence of ten pieces would reserve ten pixels it never
    /// draws. <see cref="NoteRunLayout"/> places each piece from the whole
    /// prefix for the same reason.
    /// </para>
    /// <para>
    /// A line is a list of pieces, each carrying the link of the segment it
    /// came from. Consecutive pieces of one segment are merged, so a line
    /// costs one label per run of the same colour rather than one per word.
    /// </para>
    /// </summary>
    internal static class NoteSegmentWrap
    {
        /// <summary>
        /// A wrapped note: its physical lines, and whether the line cap
        /// dropped text the caller has to keep on a hover.
        /// </summary>
        public readonly struct WrappedNote
        {
            public readonly IReadOnlyList<IReadOnlyList<PlanNoteSegment>> Lines;
            public readonly bool Truncated;

            public WrappedNote(
                IReadOnlyList<IReadOnlyList<PlanNoteSegment>> lines, bool truncated)
            {
                Lines = lines;
                Truncated = truncated;
            }
        }

        /// <summary>
        /// Wraps one note. The FIRST line is budgeted against
        /// <paramref name="firstLineMaxWidth"/>, which the row's icon, its
        /// item name and any right-aligned coin cell all eat into; every
        /// later line gets <paramref name="maxWidth"/>. Always returns at
        /// least one line, so a caller that spends one fixed-height row per
        /// line still emits a row for an empty note.
        /// </summary>
        public static WrappedNote Wrap(
            IReadOnlyList<PlanNoteSegment> segments, int firstLineMaxWidth, int maxWidth,
            Func<string, int> measure, int maxLines)
        {
            if (measure == null)
            {
                throw new ArgumentNullException(nameof(measure));
            }

            if (maxLines < 1)
            {
                maxLines = 1;
            }

            var lines = new List<IReadOnlyList<PlanNoteSegment>>();
            var line = new LineBuilder();
            string pendingSpace = null;
            int pendingSegment = 0;

            foreach (var token in Tokenize(segments))
            {
                if (token.IsSpace)
                {
                    // Held back so a line never ends in trailing spaces and
                    // the run is dropped where it lands on a wrap point.
                    pendingSpace = token.Text;
                    pendingSegment = token.SegmentIndex;
                    continue;
                }

                int budget = lines.Count == 0 ? firstLineMaxWidth : maxWidth;
                if (line.IsEmpty)
                {
                    // Nothing committed on this line, so there is no wrap
                    // point to break at. A word wider than the whole line
                    // ellipsizes rather than overhanging the panel; the
                    // caller's full-text hover keeps what it lost.
                    line.Add(segments, token.SegmentIndex,
                        TextWrapMath.Ellipsize(token.Text, budget, measure));
                    pendingSpace = null;
                    continue;
                }

                string candidate = line.Text + (pendingSpace ?? "") + token.Text;
                if (measure(candidate) <= budget)
                {
                    if (pendingSpace != null)
                    {
                        line.Add(segments, pendingSegment, pendingSpace);
                    }

                    line.Add(segments, token.SegmentIndex, token.Text);
                    pendingSpace = null;
                    continue;
                }

                lines.Add(line.Pieces);
                line = new LineBuilder();
                pendingSpace = null;
                line.Add(segments, token.SegmentIndex,
                    TextWrapMath.Ellipsize(
                        token.Text, lines.Count == 0 ? firstLineMaxWidth : maxWidth, measure));
            }

            lines.Add(line.Pieces);

            bool truncated = lines.Count > maxLines;
            if (truncated)
            {
                // The tail is dropped rather than re-flowed: the cap is
                // reached only by a note hundreds of words long, and the
                // caller puts the whole text on the row's hover.
                lines.RemoveRange(maxLines, lines.Count - maxLines);
                ((List<PlanNoteSegment>)lines[maxLines - 1])
                    .Add(PlanNoteSegment.Plain(TextWrapMath.Ellipsis));
            }

            return new WrappedNote(lines, truncated);
        }

        /// <summary>
        /// One line under construction: its pieces, the text measured
        /// against the budget, and which segment the last piece came from,
        /// which is what decides whether the next run merges into it.
        /// </summary>
        private sealed class LineBuilder
        {
            internal readonly List<PlanNoteSegment> Pieces = new List<PlanNoteSegment>();

            internal string Text = "";

            private int _lastSegment = -1;

            internal bool IsEmpty
            {
                get { return Pieces.Count == 0; }
            }

            internal void Add(
                IReadOnlyList<PlanNoteSegment> segments, int segmentIndex, string text)
            {
                if (string.IsNullOrEmpty(text))
                {
                    return;
                }

                Text += text;
                var source = segments[segmentIndex];
                if (segmentIndex == _lastSegment)
                {
                    var previous = Pieces[Pieces.Count - 1];
                    Pieces[Pieces.Count - 1] = source.IsLink
                        ? PlanNoteSegment.Linked(previous.Text + text, source.Link)
                        : PlanNoteSegment.Plain(previous.Text + text);
                    return;
                }

                Pieces.Add(source.IsLink
                    ? PlanNoteSegment.Linked(text, source.Link)
                    : PlanNoteSegment.Plain(text));
                _lastSegment = segmentIndex;
            }
        }

        private readonly struct Token
        {
            public readonly string Text;
            public readonly int SegmentIndex;
            public readonly bool IsSpace;

            public Token(string text, int segmentIndex, bool isSpace)
            {
                Text = text;
                SegmentIndex = segmentIndex;
                IsSpace = isSpace;
            }
        }

        /// <summary>
        /// Splits every segment into alternating word and space runs,
        /// keeping the segment each run came from. A run never spans two
        /// segments, so a link's first and last characters stay inside the
        /// link even when the sentence puts no space beside them.
        /// </summary>
        private static IEnumerable<Token> Tokenize(IReadOnlyList<PlanNoteSegment> segments)
        {
            if (segments == null)
            {
                yield break;
            }

            for (int s = 0; s < segments.Count; s++)
            {
                string text = segments[s].Text ?? "";
                int i = 0;
                while (i < text.Length)
                {
                    bool isSpace = text[i] == ' ';
                    int j = i;
                    while (j < text.Length && (text[j] == ' ') == isSpace)
                    {
                        j++;
                    }

                    yield return new Token(text.Substring(i, j - i), s, isSpace);
                    i = j;
                }
            }
        }
    }
}
