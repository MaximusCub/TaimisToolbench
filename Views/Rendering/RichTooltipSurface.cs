using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Blish_HUD.Input;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// The module's ONE rich tooltip window. Deliberately a single shared
    /// instance repointed on hover rather than an instance per tooltip'd
    /// control - see KNOWN-ISSUES #41 for the
    /// decompiled evidence: <c>Control.Dispose</c> never touches its
    /// <c>_tooltip</c> field, and a Tooltip is not a child of its owner
    /// (<c>Tooltip.Show</c> reparents it to the SpriteScreen), so
    /// <c>Container.DisposeControl</c>'s descendant sweep never reaches it
    /// either. A per-control instance on tree rows and pills - controls
    /// this module rebuilds wholesale on every render - would leak one
    /// undisposed container and its whole child tree per row per render.
    ///
    /// Everything interesting happens in <see cref="TooltipLayoutMath"/>;
    /// this class is the thin Blish-coupled shell that turns a laid-out
    /// row into Labels and coin runs, paints the game's own canvas in
    /// place of Blish's tooltip art, and keeps the box on screen.
    /// </summary>
    internal sealed class RichTooltipSurface : Tooltip
    {
        /// <summary>
        /// Blish's own <c>Tooltip._contentEdgeBuffer</c> horizontal total
        /// (left 6 + right 4), the chrome <c>Tooltip.RecalculateLayout</c>
        /// adds around whatever this surface's content panel measures.
        /// <c>Tooltip.EnableTooltips</c> builds the buffer as
        /// <c>new Thickness(4f, 4f, 3f, 6f)</c>, and that overload's argument
        /// order is (top, right, bottom, left): the 4 and 3 that look like a
        /// horizontal pair are the VERTICAL edges.
        /// </summary>
        private const int ChromeWidth = 10;

        /// <summary>
        /// The vertical halves of that same <c>Thickness(4f, 4f, 3f, 6f)</c>:
        /// 4px above the content and 3px below it. Needed only because the
        /// second box has to know where the first one's frame ends.
        /// </summary>
        private const int ChromeTop = 4;

        private const int ChromeBottom = 3;

        /// <summary>
        /// The transparent gap between the two boxes. The game's own
        /// stacked "Currently Equipped" comparison boxes measure 18 here:
        /// the upper box's bottom border sits on row 322 and the lower
        /// box's top border on row 341, at the game's 16px line pitch.
        /// Ours is deliberately half of that. 9 was chosen after looking
        /// at the module's own two boxes in game, so do not "restore" this
        /// to the measured 18.
        /// </summary>
        private const int BoxGap = 9;

        /// <summary>
        /// FALLBACK canvas only: the game-derived art's median tint at its
        /// measured coverage, used when the "tooltip" texture cannot be
        /// loaded, and for the strips of a pathological box that outruns
        /// the 942px art. The normal path draws the texture itself - see
        /// <see cref="PaintBeforeChildren"/>.
        /// </summary>
        /// <remarks>
        /// Named for the surface rather than plainly <c>BackgroundColor</c>: at
        /// that name the field hid the inherited <c>Control.BackgroundColor</c>
        /// property (CS0108), so which member an unqualified mention bound to
        /// depended on whether it sat in this class or in an initializer for
        /// some other control.
        /// </remarks>
        private static readonly Color SurfaceBackgroundColor = new Color(25, 32, 34) * 0.82f;

        /// <summary>
        /// Blish's own multiplier on the "tooltip" texture (decompiled 1.3.0),
        /// and independently the live client's - fitted from clean interior
        /// patches of a live capture, residual ~1 quantisation level.
        /// <para>
        /// THE BOX IS MEANT TO BE SEE-THROUGH. The game's own tooltip is
        /// semi-transparent (the art's alpha channel, mean ~0.80) and so is
        /// this one; scene showing through it in a side-by-side against an
        /// in-game capture is the MATCH, not the defect. Repeatedly
        /// mis-diagnosed as lost opacity - do not raise the coverage to
        /// "fix" it.
        /// </para>
        /// The fit, and why the audit's 0.82 does not belong here:
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        private const float CanvasArtOpacity = 0.98f;

        /// <summary>
        /// The game's tooltip canvas, via Blish's content service - the
        /// module ships no copy of the art. Null only if the ref archive
        /// fails to yield it, in which case the flat fallback tint paints
        /// instead (Blish caches the miss, so this stays null for the
        /// session); cached here because <c>GetTexture</c> is a dictionary
        /// probe per call and this runs every paint.
        /// </summary>
        private static Texture2D _canvasArt;

        /// <summary>1px, near-black, all four edges - measured on column
        /// x=0 of the xyaren capture, whose x=1 is already interior (G2).</summary>
        private static readonly Color BorderColor = new Color(6, 10, 12);

        // The two Black*0.3f/0.15f vignette rings that used to sit here
        // reproduced the measured inward darkening over the FLAT fill. The
        // 2026-08-26 live2 captures show that fall-off is baked into the
        // canvas art's own left/top crop edge (live left-edge profile of
        // k-2 matches the raw asset columns at (3+inset) to ~1 level), and
        // that the game adds NOTHING on the right/bottom - interiors run
        // flat to the border there. Drawing the art makes the rings a
        // double-darkening on two edges and an invention on the other two,
        // so they are gone; Blish's eight Black*0.5/0.6 edge bands were
        // Blish's addition, not the game's, and stay suppressed.

        /// <summary>
        /// Every glyph in a game tooltip carries a dark halo (measured at
        /// 3x, KNOWN-ISSUES #42, gap G8). Same pair the module's own row
        /// labels already use.
        /// </summary>
        private static readonly Color ShadowColor = Color.Black * 0.8f;

        /// <summary>
        /// The header icon's box, read off the tier table rather than
        /// restated here: the row height and the name indent below have to
        /// be the number IconControls actually draws the icon at.
        /// </summary>
        private static readonly int HeaderIconFrameSize =
            ItemIconTiers.FrameSize(ItemIconTier.TooltipHeader);

        /// <summary>The gap between that box and the name, measured off the
        /// xyaren capture (KNOWN-ISSUES #42, gap G11).</summary>
        private const int HeaderIconGap = 5;

        /// <summary>
        /// The game's frame around a tooltip header icon: an exact
        /// (229,229,229) on all four edges of a lossless currency-tooltip
        /// capture. Supersedes (166,175,174), one edge sample off the
        /// xyaren item capture that landed flagged as a judgment call.
        /// </summary>
        private static readonly Color HeaderIconFrameColor = new Color(229, 229, 229);

        /// <summary>
        /// The inline effect icon beside a consumable's effect block:
        /// ~26px square with the text column starting ~31px in, both
        /// measured on live3 soul-pastries (icon columns 21-47, text at 51
        /// with the content edge at 21) and candy-corn (2026-08-26). The
        /// game's absolute pixel sizes, not multiples of the line height:
        /// they do not move when the text face does.
        /// </summary>
        private const int EffectIconSize = 26;

        private const int EffectTextIndent = 31;

        /// <summary>
        /// The equipment slot glyph and the text column beside it. MEASURED
        /// on a live ascended-staff tooltip at the game's 16px line pitch:
        /// the glyph's box is 16px square at the tooltip's content edge -
        /// its 14px of ink sits inside a 1px transparent border, which is
        /// what the four upgrade and infusion lines' ink columns resolve to
        /// - and the text starts 21px in. Absolute game pixels, not
        /// multiples of the line height, for the reason
        /// <see cref="EffectIconSize"/> gives.
        /// </summary>
        private const int SlotIconSize = 16;

        private const int SlotTextIndent = 21;

        /// <summary>
        /// The game's coin icon is ~0.8x its line height (~13px on a 16px
        /// line, measured on the steak capture) - not the module's shared
        /// 18px table icon, which under the +2pt font wave reads small on
        /// a 22px line and tall on a 16px one. TOOLTIP-LOCAL for the same
        /// reason the item wrap cap is its own constant:
        /// <c>CoinSegmentMath.CoinIconSize</c>
        /// is the plan tables' constant and stays theirs (gap G22).
        /// </summary>
        private static int CoinIconSizeFor(int lineHeight)
        {
            return System.Math.Max(8, (lineHeight * 4) / 5);
        }

        private readonly Func<Control, TooltipContent> _resolveContent;

        private Panel _contentPanel;

        /// <summary>
        /// The first box's full height, chrome included, in this control's
        /// own coordinates. The whole height when there is no second box.
        /// </summary>
        private int _firstBoxHeight;

        /// <summary>Top of the second box, or 0 when there is only one.</summary>
        private int _secondBoxTop;

        internal RichTooltipSurface(Func<Control, TooltipContent> resolveContent)
        {
            _resolveContent = resolveContent ?? throw new ArgumentNullException(nameof(resolveContent));
        }

        /// <summary>
        /// The surface is invisible to the mouse, every child included.
        /// Without this a tooltip that the four-edge clamp moves under the
        /// cursor would win the hit test (Blish's default
        /// <c>CaptureType.Mouse</c> on Container/Label), become the active
        /// control, and fire <c>ActiveControlChanged</c> - which
        /// <c>Tooltip.ControlOnActiveControlChanged</c> answers by hiding
        /// the current tooltip, producing a show/hide flicker loop. Blish's
        /// own tooltips avoid it only by never being placed under the
        /// cursor, which is precisely the constraint the clamp relaxes.
        /// </summary>
        public override Control TriggerMouseInput(MouseEventType mouseEventType, MouseState ms)
        {
            return null;
        }

        /// <summary>
        /// The game's canvas, drawn the way the game itself draws it: the
        /// "tooltip" art cropped 1:1 from (3,4) at 0.98 - the exact call Blish's
        /// own override makes (decompiled, 1.3.0), which the live client
        /// provably shares. What is still replaced relative to Blish: its eight
        /// dark edge bands (the game has none) give way to the single measured
        /// border pixel.
        /// <para>
        /// Blish's content edge buffer - <c>Thickness(4 top, 4 right, 3 bottom,
        /// 6 left)</c>, which <c>RecalculateLayout</c> turns into the
        /// ContentRegion every child is positioned inside - already IS the
        /// game's measured 6px left padding with 3-4px on the other edges
        /// (KNOWN-ISSUES #42, gap G23), so the padding needs no work of its own
        /// once the art underneath it is gone.
        /// </para>
        /// The correlation figures behind "provably": docs/ARCHITECTURE.md,
        /// "Views: relocated design narrative".
        /// </summary>
        public override void PaintBeforeChildren(SpriteBatch spriteBatch, Rectangle bounds)
        {
            var pixel = ContentService.Textures.Pixel;
            var art = _canvasArt;
            if (art == null || art.IsDisposed)
            {
                art = _canvasArt = GameService.Content.GetTexture("tooltip", null);
            }

            if (_secondBoxTop <= 0 || _secondBoxTop >= bounds.Height)
            {
                PaintBox(spriteBatch, pixel, art, bounds);
                return;
            }

            // Two frames, one hover: the game's own content above, this
            // module's additions below. Each box crops the canvas art from
            // its own top-left, which is how the game composites a box.
            PaintBox(
                spriteBatch, pixel, art,
                new Rectangle(bounds.X, bounds.Y, bounds.Width, _firstBoxHeight));
            PaintBox(
                spriteBatch, pixel, art,
                new Rectangle(
                    bounds.X, bounds.Y + _secondBoxTop,
                    bounds.Width, bounds.Height - _secondBoxTop));
        }

        private void PaintBox(SpriteBatch spriteBatch, Texture2D pixel, Texture2D art, Rectangle bounds)
        {
            if (art == null)
            {
                spriteBatch.DrawOnCtrl(this, pixel, bounds, SurfaceBackgroundColor);
            }
            else
            {
                int srcW = TooltipLayoutMath.CanvasArtSourceLength(
                    bounds.Width, art.Width, TooltipLayoutMath.CanvasArtSourceX);
                int srcH = TooltipLayoutMath.CanvasArtSourceLength(
                    bounds.Height, art.Height, TooltipLayoutMath.CanvasArtSourceY);

                if (srcW > 0 && srcH > 0)
                {
                    spriteBatch.DrawOnCtrl(
                        this, art,
                        new Rectangle(bounds.X, bounds.Y, srcW, srcH),
                        new Rectangle(
                            TooltipLayoutMath.CanvasArtSourceX,
                            TooltipLayoutMath.CanvasArtSourceY,
                            srcW, srcH),
                        Color.White * CanvasArtOpacity);
                }

                // A box that outruns the 939x938 the crop can source
                // (never seen - the item cap plus chrome) gets
                // the fallback tint on the uncovered strips rather than
                // a stretch: only ever right/bottom, since the crop is
                // anchored to the box's top-left like the game's.
                if (srcW < bounds.Width)
                {
                    spriteBatch.DrawOnCtrl(
                        this, pixel,
                        new Rectangle(bounds.X + srcW, bounds.Y, bounds.Width - srcW, bounds.Height),
                        SurfaceBackgroundColor);
                }

                if (srcH < bounds.Height)
                {
                    spriteBatch.DrawOnCtrl(
                        this, pixel,
                        new Rectangle(bounds.X, bounds.Y + srcH, srcW, bounds.Height - srcH),
                        SurfaceBackgroundColor);
                }
            }

            DrawEdges(spriteBatch, pixel, bounds, BorderColor);
        }

        private void DrawEdges(SpriteBatch spriteBatch, Texture2D pixel, Rectangle bounds, Color color)
        {
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), color);
            spriteBatch.DrawOnCtrl(
                this, pixel, new Rectangle(bounds.X, bounds.Bottom - 1, bounds.Width, 1), color);
            spriteBatch.DrawOnCtrl(this, pixel, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), color);
            spriteBatch.DrawOnCtrl(
                this, pixel, new Rectangle(bounds.Right - 1, bounds.Y, 1, bounds.Height), color);
        }

        public override void Show()
        {
            var content = CurrentControl == null ? null : _resolveContent(CurrentControl);
            if (content == null || content.IsEmpty)
            {
                // Nothing registered for the hovered control: stay hidden
                // rather than flash an empty frame. Blish retries on the
                // next mouse move, so a late registration still shows.
                return;
            }

            BuildContent(content);
            base.Show();
            Reposition();
        }

        /// <summary>
        /// Redraws the box when the content registered for the control it is
        /// already showing has been replaced. Blish's plain path refreshes
        /// itself on a content change - the <c>BasicTooltipText</c> setter
        /// either writes the new text straight into the live
        /// <c>BasicTooltipView</c> or drops <c>_tooltip</c> so the next hover
        /// rebuilds - and <c>Tooltip.HandleMouseMoved</c> calls <c>Show</c>
        /// only while the tooltip is HIDDEN, so without this the rich path
        /// would keep drawing the previous content for as long as the cursor
        /// stayed on the control.
        /// </summary>
        internal void RefreshShowing(Control control)
        {
            if (control == null || !Visible || CurrentControl != control)
            {
                return;
            }

            var content = _resolveContent(control);
            if (content == null || content.IsEmpty)
            {
                Hide();
                return;
            }

            BuildContent(content);
            Reposition();
        }

        /// <summary>
        /// Redraws the box for the control it is currently showing. The
        /// content itself is unchanged - what changed is an INPUT the
        /// deferred builder reads (the session stat cache gaining the
        /// hovered item's block, Q13).
        /// </summary>
        internal void RefreshCurrent()
        {
            if (Visible && CurrentControl != null)
            {
                RefreshShowing(CurrentControl);
            }
        }

        public override void UpdateContainer(GameTime gameTime)
        {
            // base re-runs Blish's own unclamped positioning on every tick
            // while visible, so the clamp has to run after it every tick
            // too - not once at Show.
            base.UpdateContainer(gameTime);
            if (Visible)
            {
                Reposition();
            }
        }

        private void Reposition()
        {
            var screen = GameService.Graphics.SpriteScreen;
            var mouse = GameService.Input.Mouse.Position;
            TooltipLayoutMath.Place(
                mouse.X, mouse.Y, Width, Height, screen.Width, screen.Height, out int x, out int y);
            if (x != Location.X || y != Location.Y)
            {
                Location = new Point(x, y);
            }
        }

        private void BuildContent(TooltipContent content)
        {
            // Body (Menomonia 16), the module's reading size.
            // TooltipLayoutMath.ItemTooltipMaxContentWidth is derived by
            // measuring the game's own wrap decisions THROUGH this face,
            // so the two move together: change one without re-deriving the
            // other and the box stops breaking lines where the game does.
            var font = UiFonts.Body;
            int lineHeight = font.LineHeight;
            int coinIconSize = CoinIconSizeFor(lineHeight);

            DisposeContent();

            // TOOLTIP-LOCAL, deliberately: Blish's own 500 stays the
            // preferred width for every plain tooltip in the module (gap
            // G24). The item cap is derived from the game's captured wrap
            // decisions - see TooltipLayoutMath.ItemTooltipMaxContentWidth,
            // where it lives so the derivation stays Blish-free and pinned.
            int maxWidth = TooltipLayoutMath.MaxContentWidth(
                GameService.Graphics.SpriteScreen.Width,
                ChromeWidth,
                TooltipLayoutMath.ItemTooltipMaxContentWidth);

            var layout = BuildLayout(content, maxWidth, font, lineHeight, coinIconSize);

            // The second box NEVER measures itself. It wraps to the width
            // the first box arrived at, so the two read as one column
            // whatever this module has to add.
            //
            // Its lines are unrelated statements - a unit price, a wallet
            // holding, the right-click affordance - so once the first box
            // hands over a width narrow enough to wrap one of them, the
            // rows run together and a reader cannot tell where a statement
            // ends. The first box is not separated this way: that one is
            // the game's own block, whose lines belong together.
            TooltipLayoutMath.Layout extraLayout = content.HasExtra
                ? BuildLayout(
                    content.Extra, System.Math.Max(1, layout.Width),
                    font, lineHeight, coinIconSize, separateEntriesWhenAnyWraps: true)
                : null;

            var boxes = TooltipLayoutMath.Stack(
                layout.Height, extraLayout == null ? 0 : extraLayout.Height,
                ChromeTop, ChromeBottom, BoxGap);
            _firstBoxHeight = boxes.FirstBoxHeight;
            _secondBoxTop = boxes.SecondBoxTop;

            // A width the wrap could not honour - a coin run is atomic and
            // never splits - would otherwise clip, so both boxes take the
            // wider of the two and stay equal.
            int panelWidth = System.Math.Max(
                layout.Width, extraLayout == null ? 0 : extraLayout.Width);

            _contentPanel = new Panel()
            {
                Size = new Point(System.Math.Max(1, panelWidth), System.Math.Max(1, boxes.PanelHeight)),
                Location = Point.Zero,
                // No fill of its own: PaintBeforeChildren paints the
                // canvas across each box, so a second fill here would be
                // the stacked-translucency case that matches neither the
                // game nor H6.
                BackgroundColor = Color.Transparent,
                ShowBorder = false,
                Parent = this,
            };

            RenderRows(layout.Rows, 0, font, lineHeight, coinIconSize);
            if (extraLayout != null)
            {
                RenderRows(extraLayout.Rows, boxes.ExtraContentTop, font, lineHeight, coinIconSize);
            }

            // Sized NOW rather than on the next update tick. The content
            // panel's extent is explicit, so the base RecalculateLayout has
            // everything it needs, and Show()'s Reposition below would
            // otherwise clamp against the PREVIOUS hover's size for one
            // frame - Blish only recalculates a container's layout while it
            // is parented, and this one is parented by Show().
            RecalculateLayout();
        }

        private TooltipLayoutMath.Layout BuildLayout(
            TooltipContent content, int maxWidth, BitmapFont font, int lineHeight, int coinIconSize,
            bool separateEntriesWhenAnyWraps = false)
        {
            return TooltipLayoutMath.LayoutContent(
                content, maxWidth, lineHeight,
                s => (int)System.Math.Ceiling(font.MeasureString(s).Width),
                copper => CoinSegmentMath.TotalCoinSegmentsWidth(
                    CoinCurrencyRenderer.BuildCoinSegments(copper, font), coinIconSize),
                // Only a coin row needs icon clearance, and only a header
                // row is icon-tall; a prose row is one line pitch, as the
                // game's 16px pitch is (gap G21).
                coinRowHeight: System.Math.Max(lineHeight, coinIconSize),
                headerRowHeight: System.Math.Max(lineHeight, HeaderIconFrameSize),
                headerIndent: HeaderIconFrameSize + HeaderIconGap,
                effectIndent: EffectTextIndent,
                slotIndent: SlotTextIndent,
                separateEntriesWhenAnyWraps: separateEntriesWhenAnyWraps);
        }

        private void RenderRows(
            IReadOnlyList<TooltipLayoutMath.LaidOutRow> rows, int yOffset,
            BitmapFont font, int lineHeight, int coinIconSize)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                RenderRow(rows, i, yOffset, font, lineHeight, coinIconSize);
            }
        }

        private void RenderRow(
            IReadOnlyList<TooltipLayoutMath.LaidOutRow> rows, int index, int yOffset,
            BitmapFont font, int lineHeight, int coinIconSize)
        {
            var row = rows[index];
            int rowY = row.Y + yOffset;
            if (row.IconAssetId != 0)
            {
                // The game's own slot art, by asset id rather than by
                // render-service URL - the path the coin denominations
                // already take. Bare, like the effect icon: the game frames
                // only the header icon. Silent on hover, because the text
                // one gap to its right already names the slot.
                int size = System.Math.Min(SlotIconSize, row.Height);
                IconControls.CreateAssetIcon(
                    _contentPanel, row.IconAssetId, 0,
                    rowY + System.Math.Max(0, (row.Height - size) / 2), size, null);
            }
            else if (row.IconUrl != null && row.Kind == TooltipLineKind.Effect)
            {
                // The effect block's inline icon: bare (the game frames
                // only the header icon), ~26px spanning into the block's
                // second row (live3 soul-pastries: the apple runs beside
                // the first two effect lines). Clamped to the block's own
                // height so a one-line effect never overhangs the
                // unindented line under it.
                int blockBottom = row.Y + row.Height;
                for (int j = index + 1;
                    j < rows.Count && rows[j].Kind == TooltipLineKind.Effect; j++)
                {
                    blockBottom = rows[j].Y + rows[j].Height;
                }

                int size = System.Math.Min(EffectIconSize, blockBottom - row.Y);
                IconControls.CreateUnframedIcon(_contentPanel, row.IconUrl, 0, rowY, size);
            }
            else if (row.IconUrl != null)
            {
                // The game frames the icon in the light grey named on
                // HeaderIconFrameColor rather than in the rarity colour the
                // module frames its ROWS with - the name beside it already
                // carries the rarity. One colour, two shapes: a plate
                // behind a currency's transparent art shows through as a
                // background, so a currency takes the ring
                // (ItemIconFrame.IsOutline).
                ItemIconFrame frame = row.HeaderSubject.IsCurrency
                    ? ItemIconFrame.ExplicitOutline(HeaderIconFrameColor)
                    : ItemIconFrame.Explicit(HeaderIconFrameColor);
                IconControls.CreateItemIcon(
                    _contentPanel, row.IconUrl, frame,
                    0, rowY, ItemIconTier.TooltipHeader,
                    ItemIconTooltip.None(ItemIconSilence.DrawnInsideATooltip));
            }

            // The name is centred on the icon, not top-aligned (measured,
            // KNOWN-ISSUES #42); every other row kind sits at its top.
            int textY = rowY + System.Math.Max(0, (row.Height - lineHeight) / 2);

            foreach (var placed in row.Spans)
            {
                if (placed.Span.IsCoin)
                {
                    // The coin invariant's own renderer, so a tooltip coin
                    // run is the same "number then icon" geometry as every
                    // table cell in the module.
                    CoinCurrencyRenderer.LayoutCoinSegments(
                        _contentPanel,
                        CoinCurrencyRenderer.BuildCoinSegments(placed.Span.CoinCopper, font),
                        placed.X,
                        textY,
                        font,
                        1f,
                        showShadow: true,
                        iconSize: coinIconSize);
                    continue;
                }

                if (placed.Span.Text.Length == 0)
                {
                    continue;
                }

                // Same class as the plan's row labels: these spans carry
                // item and character names, so they need the descender
                // clearance too. Height is never read back here (the panel
                // is sized from the line layout), so pinning it is inert
                // beyond the two pixels.
                _ = LabelHelpers.WithDescenderClearance(new Label()
                {
                    Text = placed.Span.Text,
                    Font = font,
                    TextColor = ResolveColor(placed.Span),
                    ShowShadow = true,
                    ShadowColor = ShadowColor,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(placed.X, textY),
                    Parent = _contentPanel,
                });
            }
        }

        /// <summary>
        /// The one place a tooltip span's semantic role becomes a colour.
        /// The roles live in Blish-free <c>Services/TooltipContent.cs</c>
        /// precisely so no composer has to reference XNA to say "this line
        /// is an item name".
        /// </summary>
        private static Color ResolveColor(TooltipSpan span)
        {
            switch (span.Role)
            {
                // Same palette the plan's own item-name labels use, so a
                // name reads identically on the row and in its tooltip.
                case TooltipSpanRole.Rarity:
                    return RarityColors.GetRarityNameColor(span.RarityKey);

                // Blish's own constant for the game's warm tan, and it is
                // the measured currency-name colour to the unit.
                case TooltipSpanRole.CurrencyName:
                    return ContentService.Colors.Chardonnay;

                // MEASURED on live/eq-weapon-full.png (2026-08-25,
                // lossless) across six independent lines - two sigil
                // names, a sigil description, the +8 Agony Infusion pair,
                // and s07's "Fine" word - all reading (81..85, 145..153,
                // 240..255) with peak ink (85,153,255). Supersedes the
                // spec's "recommendation, not a measurement" triple
                // (120,170,235), which was too pale and too grey; FWDekker's
                // #5599ff replica value was right. Same blue the game uses
                // for the Fine rarity.
                case TooltipSpanRole.Bonus:
                    return new Color(85, 153, 255);

                // #AAA - a tier above the wearer's equipped count. MEASURED
                // on a rune socketed at (0/4): the four bonus lines are
                // neutral grey with a hard ceiling at 170-176 where the
                // white lines of the same capture reach 255. The earlier
                // 150 was a guess made while the role was unreachable.
                case TooltipSpanRole.BonusInactive:
                    return new Color(170, 170, 170);

                // #9ED - MEASURED saturating peak (p95 == max ==
                // (153,238,221)) on three independent live3 flavour runs:
                // eyes-of-kormir, heart-of-destroyer and wings, 2026-08-26.
                // Another exact 3-digit-hex value, like the whole measured
                // rarity palette. Supersedes xyaren's JPEG-era #B1D7D2
                // median. Upright, not italic - the game does not
                // italicise flavour.
                case TooltipSpanRole.Flavor:
                    return new Color(153, 238, 221);

                // #FE8 - MEASURED saturating peak (255,238,136) on the
                // live3 sigil-rage "Element:" run (2026-08-26), replacing
                // gw2efficiency's inferred #FEA.
                case TooltipSpanRole.AbilityType:
                    return new Color(255, 238, 136);

                // Warning red: ink medians read (240,2,2) on live3
                // sigil-rage and q-food2, the same family as the
                // discipline-level red; full-red constant kept (medians
                // sit ~15 under peaks on every measured colour).
                case TooltipSpanRole.Warning:
                    return new Color(255, 0, 0);

                // #AAA - MEASURED saturating peak (170,170,170) on the
                // live3 sigil-rage "(Cooldown: 20 Seconds)" reminder run
                // (2026-08-26). Same grey as Muted below; the role stays
                // separate because its source is the API's own <c=@reminder>
                // markup rather than a composer decision.
                case TooltipSpanRole.Reminder:
                    return new Color(170, 170, 170);

                // #AAA - the game's annotation grey, MEASURED as a
                // saturating peak (170,170,170) on live3: the sigil
                // cooldown, the effect-block text of soul-pastries, and
                // vials' inactive discipline names all cap there
                // (2026-08-26). The earlier 160 came from ink MEDIANS of
                // the same lines (eq-weapon-full's "(Two-Handed)" at
                // 160-162), which sit under the peak by exactly the edge
                // blending every measured colour shows. The storage-line
                // grey is a DIFFERENT, darker value (#999, measured on
                // vials/candy-corn) that no module line uses today.
                case TooltipSpanRole.Muted:
                    return new Color(170, 170, 170);

                default:
                    return Color.White;
            }
        }

        private void DisposeContent()
        {
            // Container.ClearChildren only detaches (Parent = null) - it
            // does not dispose - so the previous hover's Labels and coin
            // icons have to be disposed explicitly or every hover for the
            // session accumulates another content tree. Snapshot via the
            // collection's own ToArray: ControlCollection.CopyTo throws by
            // design, so a List built from it crashes on the second hover.
            foreach (var child in Children.ToArray())
            {
                child.Dispose();
            }

            _contentPanel = null;
            _firstBoxHeight = 0;
            _secondBoxTop = 0;
        }

        protected override void DisposeControl()
        {
            DisposeContent();
            base.DisposeControl();
        }
    }
}
