using Blish_HUD;
using Blish_HUD.Content;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// THE item-icon component: every item, currency and search-result icon
    /// is built here, so all get one frame (the rarity palette for an item,
    /// neutral at unknown rarity and never guessed; one shared grey for a
    /// currency), one empty-slot placeholder, and one hover wiring stamped
    /// on every control in the icon's tree - Blish resolves a tooltip on the
    /// deepest control and never bubbles.
    /// <see cref="CreateUnframedIcon"/> and <see cref="CreateAssetIcon"/>
    /// are the only unframed paths, and say why themselves.
    /// <para>
    /// THE RULE, stated once: every framed item icon goes through
    /// <see cref="CreateItemIcon"/> at a named <see cref="ItemIconTier"/>,
    /// with an <see cref="ItemIconFrame"/> that says why it is the colour it
    /// is and an <see cref="ItemIconTooltip"/> that says what it shows on
    /// hover. No pixel size, no rarity string and no hover reaches this file
    /// from a call site that did not name one. A currency has no rarity to
    /// name, so it takes <see cref="CreateCurrencyIcon"/> instead - one
    /// entry point, its border decided by the tier's context, not a call site.
    /// </para>
    /// </summary>
    internal static class IconControls
    {
        // --- Icon helper ---

        /// <summary>
        /// THE item icon. Every framed icon in the module is built here, at a
        /// NAMED <see cref="ItemIconTier"/>, with an explicit
        /// <see cref="ItemIconFrame"/> and an explicit
        /// <see cref="ItemIconTooltip"/> - no defaults of any kind.
        /// <para>
        /// The frame's thickness comes from the tier
        /// (<c>ItemIconTiers.BorderThickness</c>), never from the caller, so two
        /// icons at the same tier cannot differ. Reserve room with
        /// <c>ItemIconTiers.FrameSize(tier)</c> - art plus both borders.
        /// </para>
        /// <para>
        /// Returns the outer frame Panel so a caller whose icon position depends
        /// on panelWidth (currently only the plan header's centered title) can
        /// reposition it without recreating it. The hover is stamped on the
        /// WHOLE tree here, so a caller cannot build an icon and forget it - it
        /// can only decide, out loud, that the icon stays silent.
        /// </para>
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        internal static Panel CreateItemIcon(
            Panel parent, string iconUrl, ItemIconFrame frame, int x, int y,
            ItemIconTier tier, ItemIconTooltip tooltip)
        {
            return CreateFramedIcon(
                parent, iconUrl, frame, x, y,
                ItemIconTiers.ArtSize(tier), ItemIconTiers.BorderThickness(tier), tooltip);
        }

        /// <summary>
        /// The same framed item icon, built WITHOUT asking for its picture.
        /// The caller gets a <see cref="DeferredIconArt"/> and decides when
        /// the picture is worth fetching - see that type for the per-icon
        /// cost this avoids.
        /// <para>
        /// Everything else is identical, the hover included: the frame and
        /// its square are built here and stamped here, so a deferred icon is
        /// never a hole in a row's hover. Only <c>BackgroundTexture</c>
        /// waits.
        /// </para>
        /// </summary>
        internal static DeferredIconArt CreateItemIconDeferredArt(
            Panel parent, string iconUrl, ItemIconFrame frame, int x, int y,
            ItemIconTier tier, ItemIconTooltip tooltip)
        {
            var panel = CreateFrame(
                parent, iconUrl, frame, x, y,
                ItemIconTiers.ArtSize(tier), ItemIconTiers.BorderThickness(tier),
                tooltip.PlainText, deferArt: true, artSquare: out Panel artSquare);

            tooltip.StampOnIconTree(panel);
            return new DeferredIconArt(artSquare, iconUrl);
        }

        /// <summary>
        /// The pre-tier signature, kept ONLY so the one row builder still
        /// owned by an in-flight branch keeps compiling until it migrates -
        /// Views/Rendering/IconNameRowHelpers.cs, which forwards the size
        /// its own pre-tier caller passed. The defaults are gone so nothing
        /// new can drift into it by accident, and the tests workflow's
        /// "Every item icon renders at a named tier" step allow-lists
        /// exactly that file and fails on any other caller.
        /// </summary>
        internal static Panel CreateItemIcon(
            Panel parent, string iconUrl, string rarity, int x, int y,
            int iconSize, int borderThickness, ItemIconTooltip tooltip)
        {
            return CreateFramedIcon(
                parent, iconUrl, ItemIconFrame.ForRarity(rarity), x, y,
                iconSize, borderThickness, tooltip);
        }

        /// <summary>
        /// THE currency icon: a wallet currency, in the module's one
        /// currency treatment so no surface has to choose a border of its
        /// own. Takes one of the two CURRENCY tiers, and the tier carries
        /// the context: <see cref="IconFrameGeometry.CurrencyIsFramed"/>
        /// frames a CurrencyListRow icon (a row's subject) in the module's
        /// one currency grey (<see cref="ItemIconFrame.Currency"/>) and
        /// draws a CurrencyBarRun icon (a currency symbol seated after
        /// digits, in the coins' role) with no frame at all.
        /// <para>
        /// Pixel-neutral by construction: either way the icon occupies the
        /// tier's measured window - the framed square insets its art inside
        /// it, the frame-less one fills it - so an inline currency run's
        /// advance, a term in the minimum-window-width derivation, does not
        /// move.
        /// </para>
        /// <paramref name="tooltip"/> is an <see cref="ItemIconTooltip"/>
        /// like every other icon's, not a bare name string: these icons
        /// mostly draw with no name text beside them, so the hover is the
        /// only thing that can identify one, and it earns the same second
        /// box and the same right-click as the rest.
        /// </summary>
        internal static Panel CreateCurrencyIcon(
            Panel parent, string iconUrl, int x, int y, ItemIconTier tier, ItemIconTooltip tooltip)
        {
            Panel panel = IconFrameGeometry.CurrencyIsFramed(tier)
                ? CreateFrame(
                    parent, iconUrl, ItemIconFrame.Currency(), x, y,
                    ItemIconTiers.ArtSize(tier), ItemIconTiers.BorderThickness(tier),
                    tooltip.PlainText, deferArt: false, artSquare: out _)
                : CreateUnframedIcon(
                    parent, iconUrl, x, y, ItemIconTiers.FrameSize(tier),
                    tooltip.PlainText, deferArt: false);

            tooltip.StampOnIconTree(panel);
            return panel;
        }

        private static Panel CreateFramedIcon(
            Panel parent, string iconUrl, ItemIconFrame frame, int x, int y,
            int iconSize, int borderThickness, ItemIconTooltip tooltip)
        {
            var panel = CreateFrame(
                parent, iconUrl, frame, x, y, iconSize, borderThickness, tooltip.PlainText,
                deferArt: false, artSquare: out _);

            // The rich half goes on last and on the whole tree, over the
            // plain notes just written: a builder that composes nothing
            // keeps them as its fallback (TooltipFacility.Register).
            tooltip.StampOnIconTree(panel);
            return panel;
        }

        /// <summary>The frame and its art, with the plain half of the
        /// hover on both - an unstamped frame is a hole in the icon's hover,
        /// and it resolves from the SAME rule the square gets.</summary>
        /// <param name="artSquare">The icon's art panel, or null when the
        /// entry has no url and drew the empty-slot placeholder instead.
        /// Only a deferring caller needs it.</param>
        private static Panel CreateFrame(
            Panel parent, string iconUrl, ItemIconFrame frame, int x, int y,
            int iconSize, int borderThickness, string plainText, bool deferArt, out Panel artSquare)
        {
            int frameSize = iconSize + borderThickness * 2;
            // A PLATE for an item, a border RING for a currency. Which one
            // is the frame's own statement (ItemIconFrame.IsOutline), not
            // this method's, so no call site can pick the wrong shape for
            // the colour it asked for.
            Panel panel = frame.IsOutline
                ? new OutlineFramePanel()
                {
                    BorderColor = frame.Color,
                    BorderThickness = borderThickness,
                }
                : new ClippedPanel() { BackgroundColor = frame.Color };
            panel.Size = new Point(frameSize, frameSize);
            panel.Location = new Point(x, y);
            panel.Parent = parent;

            var square = CreateUnframedIcon(
                panel, iconUrl, borderThickness, borderThickness, iconSize, plainText, deferArt);
            artSquare = HasArt(iconUrl) ? square : null;
            TooltipFacility.ApplyPlain(panel, ResolveTooltip(iconUrl, plainText));
            return panel;
        }

        // What a missing icon says instead of an item name. Assigned only
        // when the caller supplied no tooltip of its own, so a currency
        // icon still names its currency.
        private const string NoIconTooltip = "No icon available for this entry.";

        // The placeholder's mark. ASCII, per this repo's standing finding
        // that the Blish font does not reliably render the glyphs an
        // "empty slot" would otherwise want (see CraftingPlanView's
        // caret comment).
        private const string NoIconGlyph = "-";

        /// <summary>The frame's interior, and the unframed icon for
        /// CoinCurrencyRenderer's inline runs, where a frame would add 2px
        /// to every segment's advance - a term in the minimum-window-width
        /// derivation - around something with no rarity.</summary>
        internal static Panel CreateUnframedIcon(
            Panel parent, string iconUrl, int x, int y, int size = 32, string tooltipText = null)
        {
            return CreateUnframedIcon(parent, iconUrl, x, y, size, tooltipText, deferArt: false);
        }

        /// <summary><paramref name="deferArt"/> builds the square with no
        /// BackgroundTexture, for a caller holding a
        /// <see cref="DeferredIconArt"/> that will assign one later.</summary>
        private static Panel CreateUnframedIcon(
            Panel parent, string iconUrl, int x, int y, int size, string tooltipText, bool deferArt)
        {
            // Missing icon: render a neutral empty-slot square, not the
            // alarming red error texture - a data gap is not a failure.
            bool missing = !HasArt(iconUrl);
            if (!missing && !deferArt)
            {
                IconAssetConnectionLimit.Apply();
            }

            Panel icon = missing
                ? new ClippedPanel()
                {
                    Size = new Point(size, size),
                    Location = new Point(x, y),
                    BackgroundColor = new Color(45, 45, 45),
                    Parent = parent,
                }
                : new ClippedPanel()
                {
                    Size = new Point(size, size),
                    Location = new Point(x, y),
                    BackgroundTexture = deferArt
                        ? null
                        : GameService.Content.GetRenderServiceTexture(iconUrl),
                    Parent = parent,
                };

            // The bare square reads as a HOLE in the icon column rather
            // than as an entry without an icon - the reported Snapshot
            // "Spirit Shards" row. A dim centered mark plus a tooltip says
            // which it is. Deliberately marks the square rather than
            // collapsing the column for that row: an un-iconed row whose
            // text starts 32px left of every other row's is a worse
            // artifact than a quiet placeholder, and the plan's tables
            // derive their name column from a fixed x that a per-row
            // collapse would break.
            //
            // Built only on the missing path, so the common case allocates
            // nothing extra.
            Label placeholderMark = null;
            if (missing && size > 0)
            {
                var font = UiFonts.Body;
                var glyphSize = font.MeasureString(NoIconGlyph);
                placeholderMark = new Label()
                {
                    Text = NoIconGlyph,
                    Font = font,
                    TextColor = new Color(110, 110, 110),
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(
                        (size - (int)System.Math.Ceiling(glyphSize.Width)) / 2,
                        (size - (int)System.Math.Ceiling(glyphSize.Height)) / 2),
                    Parent = icon,
                };
            }

            // An icon's tooltip is almost always an item name, which is
            // unbounded - through the facility, not assigned raw. Stamped
            // on the placeholder mark as well as the square: Blish resolves
            // a tooltip on the deepest control under the cursor and never
            // bubbles to the parent, so the mark would otherwise swallow
            // the hover in the exact middle of the square.
            string resolvedTooltip = ResolveTooltip(iconUrl, tooltipText);
            TooltipFacility.ApplyPlain(icon, resolvedTooltip);
            if (placeholderMark != null)
            {
                TooltipFacility.ApplyPlain(placeholderMark, resolvedTooltip);
            }

            return icon;
        }

        /// <summary>The unframed path for art that ships with the game: a
        /// coin denomination, by asset id. No missing-art branch - an asset
        /// id is a constant, so there is no data gap to degrade.</summary>
        internal static Panel CreateAssetIcon(
            Panel parent, int assetId, int x, int y, int size, string tooltipText)
        {
            IconAssetConnectionLimit.Apply();
            var icon = new ClippedPanel()
            {
                Size = new Point(size, size),
                Location = new Point(x, y),
                BackgroundTexture = AsyncTexture2D.FromAssetId(assetId),
                Parent = parent,
            };

            TooltipFacility.ApplyPlain(icon, tooltipText);
            return icon;
        }

        /// <summary>Whether an entry has a picture to show at all. Stated
        /// once because two places have to agree: the branch that draws the
        /// empty-slot placeholder instead, and the deferred-art handle. If
        /// they disagreed, an icon with no url would sit waiting for a
        /// picture nothing was ever going to ask for.</summary>
        private static bool HasArt(string iconUrl)
        {
            return !string.IsNullOrEmpty(iconUrl);
        }

        /// <summary>What an icon says on hover: the caller's text, or the
        /// missing-icon note when there is neither art nor text. One rule,
        /// so frame and square cannot disagree.</summary>
        private static string ResolveTooltip(string iconUrl, string tooltipText)
        {
            return string.IsNullOrEmpty(iconUrl) && string.IsNullOrEmpty(tooltipText)
                ? NoIconTooltip
                : tooltipText;
        }

        /// <summary>
        /// Stamps a deferred rich builder on a framed icon AND everything nested
        /// inside it. Blish resolves a tooltip on the deepest control under the
        /// cursor and never bubbles to the parent, so stamping the frame alone
        /// leaves the hover swallowed by the icon square that covers all but its
        /// border - and the square swallowed in turn by its missing-icon
        /// placeholder mark.
        /// <para>
        /// It cannot skip an empty payload, because nothing is composed yet - a
        /// row having a real item id does NOT make its builder non-empty. What
        /// keeps the icon's own note from being replaced with silence is
        /// <c>TooltipFacility</c>, which captures each control's plain text as
        /// the builder's fallback.
        /// </para>
        /// Reached through <see cref="ItemIconTooltip.StampOnIconTree"/>; there
        /// is deliberately no eager or plain-text twin. Why:
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        internal static void ApplyRichDeferredToIconTree(Control control, System.Func<TooltipContent> build)
        {
            if (control == null || build == null)
            {
                return;
            }

            TooltipFacility.ApplyRichDeferred(control, build);

            if (control is Container container)
            {
                foreach (var child in container.Children)
                {
                    ApplyRichDeferredToIconTree(child, build);
                }
            }
        }
    }
}
