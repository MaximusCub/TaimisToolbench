using System;
using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// TextAdvanceMath's pen recovery, and where a note's runs land when
    /// one of them is a link. The renderer spends one label per run, so a
    /// run placed at the wrong x moves words apart on screen even though
    /// the wrap put them on one line. The craft-step row lays three labels
    /// out the same way and reads the same pen.
    /// <para>
    /// The measurement below is a bitmap face rather than a character
    /// count, because the defect lives in what a font measurement IS. A
    /// glyph occupies its own box and then advances a smaller amount, and
    /// Menomonia's space region is blank, so a measured width stops at the
    /// last DRAWN glyph and loses a trailing space entirely.
    /// </para>
    /// </summary>
    public class NoteRunLayoutTests
    {
        private const int LetterSpacing = -1;

        private struct Glyph
        {
            internal int XOffset;
            internal int Width;
            internal int XAdvance;
            internal bool Draws;
        }

        private static Glyph GlyphFor(char c)
        {
            switch (c)
            {
                case ' ':
                    return new Glyph { XOffset = -2, Width = 5, XAdvance = 7, Draws = false };
                case '0':
                    return new Glyph { XOffset = -1, Width = 12, XAdvance = 10, Draws = true };
                case '.':
                    return new Glyph { XOffset = -1, Width = 5, XAdvance = 4, Draws = true };
                default:
                    return new Glyph { XOffset = -1, Width = 11, XAdvance = 9, Draws = true };
            }
        }

        /// <summary>What BitmapFont.MeasureString reports: the right edge
        /// of the last glyph box that draws.</summary>
        private static int Measure(string text)
        {
            int pen = 0;
            int right = 0;
            foreach (char c in text ?? "")
            {
                var glyph = GlyphFor(c);
                if (glyph.Draws && pen + glyph.XOffset + glyph.Width > right)
                {
                    right = pen + glyph.XOffset + glyph.Width;
                }

                pen += glyph.XAdvance + LetterSpacing;
            }

            return right;
        }

        /// <summary>Where the pen ends up, which is where the next glyph of
        /// a single-label render would be drawn.</summary>
        private static int Pen(string text)
        {
            int pen = 0;
            foreach (char c in text ?? "")
            {
                pen += GlyphFor(c).XAdvance + LetterSpacing;
            }

            return pen;
        }

        private const string Lead = "The vendor requires the ";
        private const string Link = "Supply Line Management";
        private const string Tail = " achievement.";

        private static List<PlanNoteSegment> Line()
        {
            return new List<PlanNoteSegment>
            {
                PlanNoteSegment.Plain(Lead),
                PlanNoteSegment.Linked(Link, IconWikiTarget.ItemPage(Link)),
                PlanNoteSegment.Plain(Tail),
            };
        }

        [Fact]
        public void AMeasuredWidthIsNotAPenPosition()
        {
            // The premise the rest of this class rests on. A trailing space
            // adds nothing at all, and a string that ends in a letter still
            // measures short of its pen by that letter's right bearing.
            Assert.Equal(Measure("The vendor requires the"), Measure(Lead));
            Assert.True(Measure("the") != Pen("the"));
        }

        [Fact]
        public void AdvanceWith_RecoversThePenTheMeasurementLost()
        {
            var advance = TextAdvanceMath.AdvanceWith(Measure);

            Assert.Equal(0, advance(""));
            Assert.Equal(Pen("the"), advance("the"));
            Assert.Equal(Pen(Lead), advance(Lead));
            Assert.Equal(Pen(Lead + Link), advance(Lead + Link));
        }

        [Fact]
        public void Place_PutsEveryRunWhereOneLabelDrawingTheWholeLineWould()
        {
            var placed = NoteRunLayout.Place(Line(), 40, TextAdvanceMath.AdvanceWith(Measure));

            Assert.Equal(3, placed.Count);
            Assert.Equal(40, placed[0].X);
            Assert.Equal(40 + Pen(Lead), placed[1].X);
            Assert.Equal(40 + Pen(Lead + Link), placed[2].X);
        }

        /// <summary>
        /// The reported defect: the link sat hard against the word before
        /// it and a full space away from the word after it.
        /// </summary>
        [Fact]
        public void Place_ALinkKeepsOneSpaceOnEachSideOfIt()
        {
            var placed = NoteRunLayout.Place(Line(), 0, TextAdvanceMath.AdvanceWith(Measure));

            // The lead-in carries the space before the link; the tail
            // carries the one after it. Both land one space wide.
            int spaceAdvance = Pen(" ");
            int before = placed[1].X - Pen("The vendor requires the");
            int after = placed[2].X + spaceAdvance - (placed[1].X + placed[1].Width);

            Assert.Equal(spaceAdvance, before);
            Assert.Equal(spaceAdvance, after);
        }

        [Fact]
        public void Place_ALinksRuleSpansTheRunsOwnAdvance()
        {
            var placed = NoteRunLayout.Place(Line(), 0, TextAdvanceMath.AdvanceWith(Measure));

            Assert.Equal(Pen(Lead + Link) - Pen(Lead), placed[1].Width);
        }

        [Fact]
        public void Place_NoPieces_PlacesNothing()
        {
            Assert.Empty(NoteRunLayout.Place(null, 10, TextAdvanceMath.AdvanceWith(Measure)));
            Assert.Empty(NoteRunLayout.Place(
                new List<PlanNoteSegment>(), 10, TextAdvanceMath.AdvanceWith(Measure)));
        }

        [Fact]
        public void AdvanceWith_NoMeasurement_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => TextAdvanceMath.AdvanceWith(null));
        }
    }
}
