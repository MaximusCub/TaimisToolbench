using System.Collections.Generic;
using System.Text;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// One run of a Plan Notes sentence, and whether it is a link.
    /// <para>
    /// A note is a list of these rather than one string because part of a
    /// sentence carries a wiki page: the achievement a vendor demands, the
    /// merchants a recipe sheet is sold by. Blish's Label draws one string
    /// in one colour with no underline, so the view draws one label per
    /// segment and rules a line under the linked ones - see
    /// Views/Rendering/NotesSectionRenderer.
    /// </para>
    /// <para>
    /// The link and the affordance are one value
    /// (<see cref="IconWikiTarget"/>), so a segment cannot advertise a page
    /// it does not open. A target with no page renders as plain text, which
    /// is how a note whose subject the module cannot name a wiki page for
    /// degrades.
    /// </para>
    /// </summary>
    internal readonly struct PlanNoteSegment
    {
        public readonly string Text;

        public readonly IconWikiTarget Link;

        private PlanNoteSegment(string text, IconWikiTarget link)
        {
            Text = text ?? "";
            Link = link;
        }

        /// <summary>Whether this run draws as a link.</summary>
        public bool IsLink
        {
            get { return Link.HasPage; }
        }

        public static PlanNoteSegment Plain(string text)
        {
            return new PlanNoteSegment(text, default(IconWikiTarget));
        }

        public static PlanNoteSegment Linked(string text, IconWikiTarget link)
        {
            return new PlanNoteSegment(text, link);
        }

        /// <summary>
        /// The whole sentence as one string, for the row's own full-text
        /// hover and for every surface that sorts or stores a note rather
        /// than drawing it.
        /// </summary>
        public static string Join(IReadOnlyList<PlanNoteSegment> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                return "";
            }

            var text = new StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                text.Append(segments[i].Text);
            }

            return text.ToString();
        }
    }
}
