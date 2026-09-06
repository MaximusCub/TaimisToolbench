using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// The scrolling viewport's hard top cutoff: one absolute logical y that
    /// every container inside the viewport re-asserts on the clip rectangle
    /// it was handed, so no descendant can paint above the viewport's own top
    /// edge whatever its nesting depth.
    /// <para>
    /// Why a re-assertion at every container rather than a taller strip: the
    /// derivation, and the measured single-container budget the line is
    /// offset by, are in <see cref="ClipCutoffMath"/>.
    /// </para>
    /// <para>
    /// The line lives in a static because <c>Container.Paint</c> is
    /// <c>sealed</c> and gives a subclass no seam to pass anything down its
    /// own subtree; painting is a single depth-first walk on the game's
    /// update thread, so <see cref="Enter"/>/<see cref="Exit"/> nest like a
    /// stack of one. Nothing outside a paint walk reads it.
    /// </para>
    /// </summary>
    internal static class ClipCutoff
    {
        private const int Inactive = int.MinValue;

        private static int _cutoffTop = Inactive;

        /// <summary>
        /// Puts <paramref name="cutoffTop"/> in force and returns the value
        /// to hand back to <see cref="Exit"/>. Nested viewports (a scrolling
        /// panel inside another tab's scrolling panel) therefore restore the
        /// outer line rather than clearing it.
        /// </summary>
        internal static int Enter(int cutoffTop)
        {
            int previous = _cutoffTop;
            _cutoffTop = cutoffTop;
            return previous;
        }

        internal static void Exit(int previous)
        {
            _cutoffTop = previous;
        }

        /// <summary>
        /// <see cref="Enter"/> for an authority that protects
        /// <paramref name="protectedEdge"/>: the line sits one live slip
        /// budget below that edge, so the round trip between the authority
        /// and its children lands ON the edge rather than above it.
        /// </summary>
        internal static int EnterFor(int protectedEdge)
        {
            return Enter(
                ClipCutoffMath.CutoffTopFor(
                    protectedEdge, GameService.Graphics.UIScaleMultiplier));
        }

        /// <summary>
        /// The clip a container should use in place of the one it inherited.
        /// A no-op when no viewport is painting, which is what makes the
        /// clamping control types below safe to use on any tab.
        /// </summary>
        internal static Rectangle Clamp(Rectangle scissor)
        {
            int cutoffTop = _cutoffTop;
            if (cutoffTop == Inactive)
            {
                return scissor;
            }

            int top = ClipCutoffMath.ClampTop(scissor.Y, cutoffTop);
            if (top == scissor.Y)
            {
                return scissor;
            }

            int height = scissor.Bottom - top;
            return new Rectangle(scissor.X, top, scissor.Width, height > 0 ? height : 0);
        }
    }

    /// <summary>
    /// A <see cref="Panel"/> that re-asserts <see cref="ClipCutoff"/> on the
    /// clip it is drawn with. Use it for every container built inside a
    /// scrolling viewport: a plain <c>Panel</c> in the chain hands its own
    /// children an edge that has drifted one round trip further up, and the
    /// drift is what accumulates with depth.
    /// </summary>
    internal class ClippedPanel : Panel
    {
        public override void Draw(SpriteBatch spriteBatch, Rectangle drawBounds, Rectangle scissor)
        {
            base.Draw(spriteBatch, drawBounds, ClipCutoff.Clamp(scissor));
        }
    }

    /// <summary>
    /// <see cref="ClippedPanel"/>'s flowing twin - see that type.
    /// </summary>
    internal class ClippedFlowPanel : FlowPanel
    {
        public override void Draw(SpriteBatch spriteBatch, Rectangle drawBounds, Rectangle scissor)
        {
            base.Draw(spriteBatch, drawBounds, ClipCutoff.Clamp(scissor));
        }
    }

    /// <summary>
    /// A <see cref="ClippedPanel"/> the mouse WHEEL passes straight
    /// through, for a container drawn on top of a scrolling panel it must
    /// not steal the wheel from. Dropping the MouseWheel capture flag is
    /// what does it: <c>Control.TriggerMouseInput</c> discriminates by
    /// event type where <c>Container</c> does not, so this answers a click
    /// and declines a wheel, and the parent's hit-test loop steps past it
    /// to the scrolling panel behind.
    /// <para>
    /// EVERY container between this one and the cursor has to answer the
    /// same way or the walk breaks inside it, which is why the sticky
    /// header band and its hover washes are this type too. The vendor
    /// mechanism this is read off, and why a ZIndex alone could not satisfy
    /// both asks: docs/ARCHITECTURE.md section V.26.2.
    /// </para>
    /// </summary>
    internal class WheelTransparentClippedPanel : ClippedPanel
    {
        protected override CaptureType CapturesInput()
        {
            return CaptureType.Mouse;
        }
    }

    /// <summary>
    /// A wheel-transparent clip that publishes <see cref="ClipCutoff"/> for
    /// its own subtree, for a clip drawn OUTSIDE any scrolling viewport.
    /// <para>
    /// The sticky header host's clip is a sibling of the viewport, so no
    /// line is in force while it paints, and a band part way through being
    /// pushed out by the end of its table sits higher than the clip showing
    /// it. Two containers separate the clip's own edge from a header
    /// label's ink, so without a line the label paints 3 logical pixels
    /// above the clip at GW2 UI Size Small. Measured on the plan tab, where
    /// that is 3px above the separator rule.
    /// </para>
    /// <para>
    /// Its own drawing is unclamped, for the reason
    /// <see cref="ClipAuthorityFlowPanel"/>'s is: the line comes from its
    /// own bounds, so clamping itself against it would only shrink it.
    /// </para>
    /// </summary>
    internal sealed class WheelTransparentClipAuthorityPanel : Panel
    {
        /// <summary>Drops the wheel flag for the reason
        /// <see cref="WheelTransparentClippedPanel"/> does.</summary>
        protected override CaptureType CapturesInput()
        {
            return CaptureType.Mouse;
        }

        public override void Draw(
            SpriteBatch spriteBatch, Rectangle drawBounds, Rectangle scissor)
        {
            int previous = ClipCutoff.EnterFor(AbsoluteBounds.Y);
            try
            {
                base.Draw(spriteBatch, drawBounds, scissor);
            }
            finally
            {
                ClipCutoff.Exit(previous);
            }
        }
    }

    /// <summary>
    /// The scrolling viewport itself: it publishes the cutoff for the whole
    /// of its own subtree's paint, then restores whatever was in force.
    /// Its own drawing is unclamped - the line is derived from its bounds, so
    /// clamping itself against it would only shrink the viewport.
    /// </summary>
    internal class ClipAuthorityFlowPanel : FlowPanel
    {
        /// <summary>
        /// The edge the published line is derived from. Its own top by
        /// default; a subclass overrides it to protect something else.
        /// </summary>
        protected virtual int ProtectedEdge => AbsoluteBounds.Y;

        public sealed override void Draw(
            SpriteBatch spriteBatch, Rectangle drawBounds, Rectangle scissor)
        {
            int previous = ClipCutoff.EnterFor(ProtectedEdge);
            try
            {
                base.Draw(spriteBatch, drawBounds, scissor);
            }
            finally
            {
                ClipCutoff.Exit(previous);
            }
        }
    }

    /// <summary>
    /// A scrolling viewport whose published line comes from its sticky header
    /// host instead of from its own top edge alone: while a band is pinned
    /// the line rides that band's live bottom edge, and when none is it is
    /// the ordinary viewport top, as <see cref="ClipAuthorityFlowPanel"/>
    /// derives it.
    /// <para>
    /// The clip does now out-rank this panel in paint order too
    /// (StickyHeaderHost.ClipZIndex), but the scissor bound is what the
    /// band rests on: it is order-independent, biting during the content's
    /// own walk rather than after it, so a band stays clean however the
    /// two are sorted. The re-assertion arithmetic that makes it hold at
    /// any nesting depth is docs/ARCHITECTURE.md section V.26.1's, via
    /// <see cref="ClipCutoffMath.CutoffTopFor"/>.
    /// </para>
    /// </summary>
    internal sealed class StickyClipAuthorityFlowPanel : ClipAuthorityFlowPanel
    {
        private readonly Func<int?> _pinnedBandBottom;

        /// <summary>
        /// <paramref name="pinnedBandBottom"/> is read once per paint, on the
        /// paint thread; a null return means no band is pinned. It may hand
        /// back null for as long as the host itself does not exist yet.
        /// </summary>
        internal StickyClipAuthorityFlowPanel(Func<int?> pinnedBandBottom)
        {
            _pinnedBandBottom = pinnedBandBottom ?? throw new ArgumentNullException(nameof(pinnedBandBottom));
        }

        protected override int ProtectedEdge => _pinnedBandBottom() ?? base.ProtectedEdge;
    }
}
