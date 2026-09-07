using Blish_HUD;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// The type sizes the module draws in, named by ROLE rather than by
    /// point size. Every view and renderer resolves its font through here,
    /// so <c>GameService.Content.DefaultFontNN</c> appears nowhere under
    /// Views/. Which point size each promoted tier sits at is decided in
    /// Services/TypeRampMetrics, beside the measured glyph metrics.
    /// <para>
    /// Two entries of the installed Menomonia inventory are unusable -
    /// 18-regular collapses word gaps, 22-regular is really a 24 - measured
    /// in TypeRampMetrics and ENFORCED in <see cref="Regular"/>.
    /// </para>
    /// <para>
    /// Three control types stay at Blish's own DefaultFont14 because they
    /// expose no Font property or pad against it: Checkbox, TextBox and
    /// Dropdown. Anything MEASURING one of those measures in
    /// <see cref="Caption"/>, the size they actually paint - and so does
    /// anything sizing a tooltip Blish renders (Services.TooltipTextFormat).
    /// </para>
    /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
    /// </summary>
    internal static class UiFonts
    {
        private static BitmapFont _glyphs;
        private static BitmapFont _columnHeader;
        private static BitmapFont _bodyGlyphs;

        /// <summary>Row text, table cells, tooltips - the module's prose.</summary>
        internal static BitmapFont Body => GameService.Content.DefaultFont16;

        /// <summary>
        /// <see cref="Body"/> plus the module's own glyphs, for the
        /// reading-size affordances that draw a caret beside or instead of
        /// body text: the recipe tree's expand/collapse column and the
        /// plan's section headers.
        /// <para>
        /// A MERGED face rather than <see cref="Glyphs"/> on its own,
        /// because these seats sit on layouts measured against Body: the
        /// merge inherits Body's line height, letter spacing and baseline
        /// exactly (GlyphFont.Merged), so a caret label that swaps to it
        /// keeps every y and every band height it already had. Falls back to
        /// plain Body before <see cref="InstallGlyphs"/> has run, which is
        /// the same degraded path <see cref="GlyphsAvailable"/> gates.
        /// </para>
        /// </summary>
        internal static BitmapFont BodyGlyphs => _bodyGlyphs ?? Body;

        /// <summary>Sublabels, pills, tags, footnotes - one step under Body.</summary>
        internal static BitmapFont Caption => GameService.Content.DefaultFont14;

        /// <summary>
        /// The digits in a coin run - the gold, silver and copper numbers
        /// that each carry an icon. Body, the same face as the caption
        /// beside them.
        /// <para>
        /// This was Caption, one step under Body, for one build. MEASURED
        /// in a screenshot holding this row beside the game's own inventory
        /// coin row, so no interface scale is assumed: at Body the digits
        /// ink "841" 19 pixels wide, which is exactly the game's 19, and at
        /// Caption they ink 17. Both faces ink 11 rows tall against the
        /// game's 10, so the step down cost width and bought no height.
        /// The role is kept as its own name so this decision has somewhere
        /// to live.
        /// </para>
        /// </summary>
        internal static BitmapFont CoinDigits => Body;

        /// <summary>
        /// Every column header the module draws, and the Total Cost band's
        /// tile captions. Bold, because headers used to be the same size and
        /// weight as the rows under them.
        /// <para>
        /// Once <see cref="InstallGlyphs"/> has run this is Menomonia Bold 20
        /// PLUS the module's own glyphs, which is what lets a sortable
        /// header carry its indicator INSIDE its own text. Cached, not
        /// rebuilt: merging sweeps the whole BMP once, and this property is
        /// read on every render and by every header's MeasureString.
        /// </para>
        /// </summary>
        internal static BitmapFont ColumnHeader =>
            _columnHeader ?? Bold(TypeRampMetrics.ColumnHeaderPointSize);

        /// <summary>
        /// The module's own glyphs, alone: for a control whose whole label is
        /// one glyph. Null until <see cref="InstallGlyphs"/> has run, so read
        /// <see cref="GlyphsAvailable"/> before seating anything on it.
        /// </summary>
        internal static BitmapFont Glyphs => _glyphs;

        /// <summary>
        /// Whether ref/glyphs.fnt and its atlas loaded. False only on a
        /// corrupt install - and then every glyph seat has to degrade to the
        /// ASCII it replaced (Services.UiGlyphs.AsciiFallback), because an
        /// absent codepoint draws nothing and advances zero pixels.
        /// </summary>
        internal static bool GlyphsAvailable => _glyphs != null;

        /// <summary>
        /// Seats the shipped glyph font. Called once from Module load, after
        /// GameService.Content is up and the ContentsManager has produced the
        /// descriptor and its atlas page.
        /// </summary>
        internal static void InstallGlyphs(GlyphFontDescriptor descriptor, Texture2D page)
        {
            if (descriptor == null || page == null)
            {
                return;
            }

            // Both or neither. GlyphsAvailable is what every glyph seat reads
            // to decide whether to draw a glyph or the ASCII it replaced, so a
            // half-installed pair - glyphs seated but the header font still
            // plain Menomonia - would answer "yes" to a header that then drew
            // nothing at all. Built into locals first so a throw anywhere in
            // here leaves the module on the fully degraded path.
            var glyphs = GlyphFont.Standalone(descriptor, page);
            var columnHeader = GlyphFont.Merged(
                "Menomonia-Bold20+GwchGlyphs",
                Bold(TypeRampMetrics.ColumnHeaderPointSize),
                TypeRampMetrics.ColumnHeaderInk.BaselineY,
                descriptor,
                page);
            var bodyGlyphs = GlyphFont.Merged(
                "Menomonia-Regular16+GwchGlyphs",
                Body,
                TypeRampMetrics.BodyInk.BaselineY,
                descriptor,
                page);

            _glyphs = glyphs;
            _columnHeader = columnHeader;
            _bodyGlyphs = bodyGlyphs;
        }

        /// <summary>
        /// Drops both fonts on module unload. They hold TextureRegion2Ds over
        /// a Texture2D the ContentsManager disposes when the module goes
        /// away; a re-enable in the same process would otherwise find these
        /// statics still pointing at it.
        /// </summary>
        internal static void ResetGlyphs()
        {
            _glyphs = null;
            _columnHeader = null;
            _bodyGlyphs = null;
        }

        /// <summary>Every section title in the module.</summary>
        internal static BitmapFont SectionTitle =>
            Bold(TypeRampMetrics.SectionTitlePointSize);

        /// <summary>
        /// Every tab's status line. Bold for a measured reason, not a
        /// stylistic one - see TypeRampMetrics on 18-regular's space glyph,
        /// which is why nothing here resolves 18-regular.
        /// </summary>
        internal static BitmapFont Status =>
            Bold(TypeRampMetrics.StatusPointSize);

        /// <summary>
        /// The plan header's " x N needed" suffix: regular weight so it
        /// stays subordinate to the Display title beside it.
        /// </summary>
        internal static BitmapFont SmallHeading =>
            Regular(TypeRampMetrics.SmallHeadingPointSize);

        /// <summary>
        /// The craft-step number badge: the bold twin of
        /// <see cref="SmallHeading"/>, digits only.
        /// </summary>
        internal static BitmapFont SmallHeadingBold =>
            Bold(TypeRampMetrics.SmallHeadingPointSize);

        /// <summary>The plan title. No bold exists at this size.</summary>
        internal static BitmapFont Display => GameService.Content.DefaultFont32;

        private static BitmapFont Bold(int pointSize)
        {
            return GameService.Content.GetFont(
                ContentService.FontFace.Menomonia, SizeOf(pointSize), ContentService.FontStyle.Bold);
        }

        /// <summary>
        /// Regular weight, at the promoted sizes that HAVE a usable
        /// regular face. The two that do not are refused here rather than
        /// in <see cref="SizeOf"/> because the ban is on the FACE, not on
        /// the size: 18-bold and 22-bold are both fine, and both are
        /// loaded.
        /// <para>
        /// Without this, a tier seat moved from 20 to 18 would turn
        /// <see cref="SmallHeading"/> into 18-regular and render
        /// " x 42 needed" at exactly the collapsed word gaps this ramp
        /// exists to escape - with no build error, no failing test and
        /// nothing on screen to name the cause. See TypeRampMetrics for
        /// both measurements.
        /// </para>
        /// </summary>
        private static BitmapFont Regular(int pointSize)
        {
            if (!TypeRampMetrics.HasUsableRegularFace(pointSize))
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(pointSize), pointSize,
                    "No usable Menomonia regular face at this size: 18-regular's space glyph "
                        + "advances 4px, and 22-regular is metrically a 24. Use the bold face.");
            }

            return GameService.Content.GetFont(
                ContentService.FontFace.Menomonia, SizeOf(pointSize), ContentService.FontStyle.Regular);
        }

        /// <summary>
        /// The four point sizes the ramp is allowed to name, and nothing
        /// else: an unmapped size is a size TypeRampMetrics has no measured
        /// ink for, so the height constants derived from it would be
        /// guesses. Fail loudly at the seam rather than silently render at
        /// a size no constant was sized for. Weight is <see cref="Bold"/>'s
        /// and <see cref="Regular"/>'s own concern - only one of the two
        /// can load all four.
        /// </summary>
        private static ContentService.FontSize SizeOf(int pointSize)
        {
            switch (pointSize)
            {
                case 18: return ContentService.FontSize.Size18;
                case 20: return ContentService.FontSize.Size20;
                case 22: return ContentService.FontSize.Size22;
                case 24: return ContentService.FontSize.Size24;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(pointSize), pointSize, "No measured TypeRampMetrics ink for this size.");
            }
        }
    }
}
