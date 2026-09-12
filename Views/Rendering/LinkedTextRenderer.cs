using System;
using System.Collections.Generic;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// Draws a sentence whose middle carries a link. Blish's Label draws
    /// one string in one colour with no underline, so a line is spent one
    /// label per run, each placed where <see cref="NoteRunLayout"/> puts
    /// it and a link ruled underneath.
    /// <para>
    /// The Plan tab's Notes section draws its own lines the same way and
    /// shares this file's style through <see cref="LinkRunStyle"/>. Which
    /// runs are links, and what each opens, are decided in Services, where
    /// a test can reach them.
    /// </para>
    /// </summary>
    internal static class LinkedTextRenderer
    {
        /// <summary>
        /// One wrapped line's runs, laid left to right from
        /// <paramref name="x"/>. Every run is drawn in the SAME face as the
        /// sentence around it; a link differs by colour and by its rule
        /// alone. Plain runs are appended to
        /// <paramref name="plainLabels"/> when the caller has a full-text
        /// hover to put on them. Returns the bottom of what was drawn: a
        /// line box is taller than the pitch successive lines sit at, so a
        /// caller stacking lines sizes its host from this rather than from
        /// a count of pitches.
        /// </summary>
        public static int DrawLine(
            Container parent, IReadOnlyList<PlanNoteSegment> pieces, int x, int y,
            BitmapFont font, Color plainColor, Func<string, int> advance,
            List<Label> plainLabels = null)
        {
            int bottom = y;
            foreach (var run in NoteRunLayout.Place(pieces, x, advance))
            {
                var piece = run.Piece;
                var label = LabelHelpers.WithDescenderClearance(new Label()
                {
                    Text = piece.Text,
                    Font = font,
                    TextColor = piece.IsLink ? LinkRunStyle.LinkColor : plainColor,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(run.X, y),
                    Parent = parent,
                });

                if (label.Bottom > bottom)
                {
                    bottom = label.Bottom;
                }

                if (!piece.IsLink)
                {
                    if (plainLabels != null)
                    {
                        plainLabels.Add(label);
                    }

                    continue;
                }

                // Inside the label's own box, over the descender clearance
                // rather than below it, so the rule cannot push a run past
                // the height its row was built at.
                new ClippedPanel()
                {
                    Size = new Point(run.Width, LinkRunStyle.UnderlineHeight),
                    Location = new Point(
                        run.X, y + label.Height - LinkRunStyle.UnderlineHeight),
                    BackgroundColor = LinkRunStyle.UnderlineColor,
                    Parent = parent,
                };

                TooltipFacility.ApplyPlain(label, piece.Link.Hint);
                IconWikiClick.ApplyToLink(label, piece.Link);
            }

            return bottom;
        }

        /// <summary>
        /// Paragraphs of linked prose, wrapped to
        /// <paramref name="budget"/> and stacked from y=0. Returns the
        /// height drawn, which is what the caller sizes the host to.
        /// <para>
        /// <paramref name="linePitch"/> is the distance between successive
        /// lines, NOT the height of one: a line box carries descender
        /// clearance below the pitch, so the height returned is measured
        /// from the labels rather than counted in pitches.
        /// </para>
        /// </summary>
        public static int DrawParagraphs(
            Container parent, IReadOnlyList<IReadOnlyList<PlanNoteSegment>> paragraphs,
            int budget, int linePitch, int paragraphGap, BitmapFont font, Color plainColor)
        {
            var measure = LabelHelpers.MeasureWith(font);
            var advance = TextAdvanceMath.AdvanceWith(measure);

            int y = 0;
            int bottom = 0;
            bool first = true;
            foreach (var paragraph in paragraphs)
            {
                if (!first)
                {
                    y += paragraphGap;
                }

                first = false;
                var wrapped = NoteSegmentWrap.Wrap(
                    paragraph, budget, budget, measure, TextWrapMath.MaxWrappedLines);
                foreach (var line in wrapped.Lines)
                {
                    int lineBottom = DrawLine(parent, line, 0, y, font, plainColor, advance);
                    if (lineBottom > bottom)
                    {
                        bottom = lineBottom;
                    }

                    y += linePitch;
                }
            }

            return bottom;
        }

        /// <summary>
        /// Linked runs on ONE line, ellipsized to
        /// <paramref name="budget"/> - what a fixed-height table row gets.
        /// Returns whether text was dropped, so the caller can put
        /// <paramref name="fullText"/> on the row's own hover.
        /// <para>
        /// The runs carry that hover too: Blish resolves a tooltip on the
        /// deepest control under the cursor and never bubbles, so a label
        /// would otherwise swallow the hover over the words themselves. A
        /// link run is left alone - its own hover names the page it opens.
        /// </para>
        /// </summary>
        public static bool DrawEllipsizedLine(
            Container parent, IReadOnlyList<PlanNoteSegment> segments, int x, int y,
            int budget, BitmapFont font, Color plainColor, string fullText)
        {
            var measure = LabelHelpers.MeasureWith(font);
            var plainLabels = new List<Label>();

            var wrapped = NoteSegmentWrap.Wrap(segments, budget, budget, measure, maxLines: 1);
            DrawLine(
                parent, wrapped.Lines[0], x, y, font, plainColor,
                TextAdvanceMath.AdvanceWith(measure), plainLabels);

            string hover = wrapped.Truncated ? fullText : null;
            TooltipFacility.ApplyPlain(parent, hover);
            foreach (var label in plainLabels)
            {
                TooltipFacility.ApplyPlain(label, hover);
            }

            return wrapped.Truncated;
        }
    }
}
