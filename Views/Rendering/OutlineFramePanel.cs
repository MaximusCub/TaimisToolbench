using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// A frame that paints a border RING and leaves its interior alone -
    /// what an <see cref="ItemIconFrame.IsOutline"/> frame asks for.
    /// Deliberately not a filled Panel: a fill covers the whole box, and
    /// currency art is mostly transparent, so it shows through as a
    /// background rather than as a border.
    /// <para>
    /// The ring is four <c>DrawOnCtrl</c> calls inside ONE control rather
    /// than four child Panels. Four children per icon would triple the
    /// control count of every inline coin run and give the 1px edges their
    /// own hover, which Blish would resolve in preference to the frame's.
    /// </para>
    /// <para>
    /// Based on <see cref="ClippedPanel"/> because an icon frame is built
    /// inside the scrolling viewport and must re-assert its cutoff.
    /// </para>
    /// </summary>
    internal sealed class OutlineFramePanel : ClippedPanel
    {
        private Color _borderColor = Color.Transparent;
        private int _borderThickness;

        /// <summary>The ring's colour.</summary>
        public Color BorderColor
        {
            get { return _borderColor; }
            set { SetProperty(ref _borderColor, value); }
        }

        /// <summary>The ring's width, in pixels, on every side.</summary>
        public int BorderThickness
        {
            get { return _borderThickness; }
            set { SetProperty(ref _borderThickness, value); }
        }

        public override void PaintBeforeChildren(SpriteBatch spriteBatch, Rectangle bounds)
        {
            base.PaintBeforeChildren(spriteBatch, bounds);

            foreach (var edge in IconFrameGeometry.OutlineEdges(bounds.Width, bounds.Height, _borderThickness))
            {
                spriteBatch.DrawOnCtrl(
                    this,
                    ContentService.Textures.Pixel,
                    new Rectangle(edge.X, edge.Y, edge.Width, edge.Height),
                    _borderColor);
            }
        }
    }
}
