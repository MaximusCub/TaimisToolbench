using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>A phrase inside a paragraph, and the page it opens.</summary>
    internal readonly struct LinkPhrase
    {
        public readonly string Phrase;

        public readonly string Url;

        public LinkPhrase(string phrase, string url)
        {
            Phrase = phrase ?? "";
            Url = url;
        }
    }

    /// <summary>
    /// Which character ranges of a paragraph are links, and the segment
    /// list that follows from them (Blish-free, unit-testable).
    /// <para>
    /// The About tab's credit copy is one approved block of prose whose
    /// links are named by the words they sit on, so the ranges have to be
    /// found rather than authored. A run is split off for each match and
    /// the words around it stay plain, which is what lets the view draw a
    /// link that wraps: <see cref="NoteSegmentWrap"/> breaks the segment
    /// list into lines and a phrase may straddle a break.
    /// </para>
    /// <para>
    /// At each index the LONGEST phrase that matches wins, so
    /// "gw2efficiency.com" beats "gw2efficiency" where both start on the
    /// same character. Matching is ordinal and case-sensitive, and every
    /// occurrence of a phrase is linked.
    /// </para>
    /// </summary>
    internal static class LinkPhraseSpans
    {
        /// <summary>One linked range of the text.</summary>
        public readonly struct Span
        {
            public readonly int Start;

            public readonly int Length;

            public readonly string Url;

            public Span(int start, int length, string url)
            {
                Start = start;
                Length = length;
                Url = url;
            }
        }

        /// <summary>
        /// Every linked range of <paramref name="text"/>, in reading order
        /// and never overlapping.
        /// </summary>
        public static IReadOnlyList<Span> Find(string text, IReadOnlyList<LinkPhrase> phrases)
        {
            var spans = new List<Span>();
            if (string.IsNullOrEmpty(text) || phrases == null || phrases.Count == 0)
            {
                return spans;
            }

            int i = 0;
            while (i < text.Length)
            {
                int match = -1;
                int matchLength = 0;
                for (int p = 0; p < phrases.Count; p++)
                {
                    string phrase = phrases[p].Phrase;
                    if (string.IsNullOrEmpty(phrase) || phrase.Length <= matchLength)
                    {
                        continue;
                    }

                    if (i + phrase.Length > text.Length
                        || string.CompareOrdinal(text, i, phrase, 0, phrase.Length) != 0)
                    {
                        continue;
                    }

                    match = p;
                    matchLength = phrase.Length;
                }

                if (match < 0)
                {
                    i++;
                    continue;
                }

                spans.Add(new Span(i, matchLength, phrases[match].Url));
                i += matchLength;
            }

            return spans;
        }

        /// <summary>
        /// <paramref name="text"/> as runs: one linked run per span, plain
        /// runs for the words between them. Empty runs are never emitted.
        /// </summary>
        public static IReadOnlyList<PlanNoteSegment> Split(
            string text, IReadOnlyList<LinkPhrase> phrases)
        {
            var segments = new List<PlanNoteSegment>();
            if (string.IsNullOrEmpty(text))
            {
                return segments;
            }

            int cursor = 0;
            foreach (var span in Find(text, phrases))
            {
                if (span.Start > cursor)
                {
                    segments.Add(PlanNoteSegment.Plain(
                        text.Substring(cursor, span.Start - cursor)));
                }

                segments.Add(PlanNoteSegment.Linked(
                    text.Substring(span.Start, span.Length),
                    IconWikiTarget.ExternalPage(span.Url)));
                cursor = span.Start + span.Length;
            }

            if (cursor < text.Length)
            {
                segments.Add(PlanNoteSegment.Plain(text.Substring(cursor)));
            }

            return segments;
        }
    }
}
