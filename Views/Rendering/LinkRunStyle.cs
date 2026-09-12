using Microsoft.Xna.Framework;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// The module's one link style: the colour and the rule that say a run
    /// of text opens a page. Two channels, not one - the surfaces that draw
    /// links already spend colour on de-emphasis, so the underline is what
    /// makes a link a link.
    /// </summary>
    internal static class LinkRunStyle
    {
        public static readonly Color LinkColor = new Color(114, 178, 255);

        /// <summary>The rule's own colour - the link's, at the ink alpha
        /// NotesSectionLayoutMath derives.</summary>
        public static readonly Color UnderlineColor =
            LinkColor * NotesSectionLayoutMath.LinkUnderlineInkAlpha;

        public const int UnderlineHeight =
            NotesSectionLayoutMath.LinkUnderlineThickness;
    }
}
