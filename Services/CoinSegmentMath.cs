using System.Collections.Generic;
using System.Text;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure coin/currency segment-width arithmetic (Blish-free,
    /// unit-testable) - moved verbatim out of CoinCurrencyRenderer
    /// together with the plain data specs
    /// (CoinSegmentSpec/CurrencySegmentSpec) and the geometry constants
    /// (CoinIconSize/CoinLabelIconGap/CoinSegmentGap) the arithmetic is
    /// built from - the specs and constants have no meaning apart from the
    /// formulas below, so they moved with them. The extraction exists so
    /// the arithmetic is testable without a test referencing UI code
    /// (repo invariant: tests must never reference UI code).
    /// CoinCurrencyRenderer.TotalCoinSegmentsWidth/TotalCurrencySegmentsWidth
    /// now call straight through to this class; everything else that
    /// touches Label/Panel/BitmapFont (BuildCoinSegments, LayoutCoinSegments,
    /// control creation, ...) stays in CoinCurrencyRenderer since it is
    /// genuinely Blish-bound.
    /// </summary>
    internal static class CoinSegmentMath
    {
        /// <summary>
        /// Size of the icon in an inline coin/currency SEGMENT - a unit
        /// marker sitting to the right of a number inside a cell or a
        /// sentence, which is the in-game wallet summary bar's tier, not the
        /// wallet list's. Defined as that tier rather than as a number of its
        /// own: see <see cref="CurrencyIconTiers"/> for the measurement, and
        /// use <see cref="CurrencyIconTiers.WalletListIconSize"/> instead
        /// wherever the icon is a currency TABLE ROW's subject (the Summary
        /// currency table, the Snapshot wallet rows).
        /// </summary>
        public const int CoinIconSize = CurrencyIconTiers.WalletBarIconSize;

        /// <summary>
        /// Gap between a number and the icon that marks it, applied from
        /// the number's MEASURED width - the right edge of its last glyph
        /// box, which is one column past the last column that draws (every
        /// Menomonia region's first and last column is fully transparent).
        /// So the drawn gap is this plus one: 4 logical pixels, one wider
        /// than the game's own 3 at bar tier (CurrencyIconTiers). The extra
        /// pixel is deliberate and is the whole of this constant's job;
        /// matching the game exactly reads as the number touching its icon.
        /// </summary>
        public const int CoinLabelIconGap = 3;

        public const int CoinSegmentGap = 6;

        /// <summary>
        /// Where a WALLET CURRENCY's inline icon BOX sits inside the line box
        /// of the number it marks, given the digits' own declared ink.
        /// <para>
        /// A Label's line box carries ascender and descender space that
        /// digits never occupy, so an icon seated at the top of it rides
        /// high against the figures beside it. The seat is the centred half
        /// of <see cref="CurrencyIconTiers.VerticalAlignmentRule"/>: the icon
        /// box is centred on the number's ink. The three coin denominations
        /// take <see cref="CoinIconY"/> instead.
        /// </para>
        /// <para>
        /// Never negative. An icon taller than the digits' ink would
        /// otherwise start above the line box and overdraw the row above,
        /// whose height was reserved from that box.
        /// </para>
        /// </summary>
        public static int InlineIconY(int digitInkTop, int digitInkHeight, int iconSize)
        {
            int offset = ((2 * digitInkTop) + digitInkHeight - iconSize) / 2;
            return offset < 0 ? 0 : offset;
        }

        /// <summary>
        /// Empty pixels a Menomonia glyph box carries above and below the ink
        /// it holds. The faces are built with <c>outline="1" spacing="1,1"</c>,
        /// so a declared box is one pixel taller than its ink at each edge:
        /// MEASURED off the shipped texture pages of menomonia 14-regular,
        /// 16-regular, 20-bold and 32-regular, every '0' inks exactly rows
        /// 1..height-2 of its own box. At 16-regular that puts the '0' box's
        /// declared yoffset 2 height 15 over ink in line-box rows 3..15, and
        /// 'H' (yoffset 3 height 14) over rows 4..15 - one shared ink bottom,
        /// which is the baseline. A seat computed from the declared box alone
        /// therefore sits a pixel high.
        /// </summary>
        public const int GlyphBoxInkPad = 1;

        // The coin art's own padding, MEASURED off the three 32x32 textures
        // Blish fetches by asset id, each source row composited over a dark
        // row ground: rows 24, 25 and 26 come out DARKER than it on gold
        // 156904, silver 156907 and copper 156902 alike, being the art's
        // black bottom rim rather than coin. The last row that DRAWS is 23
        // on all three, so a bottom seat has one number to read.
        private const int CoinArtTextureSize = 32;
        private const int CoinArtLastInkRow = 23;

        /// <summary>
        /// Where the coin art's lowest ink lands inside an icon box of
        /// <paramref name="iconSize"/> pixels, as an exclusive bottom edge.
        /// Point-sampled: a destination row carries ink while the source rows
        /// it covers still reach <c>CoinArtLastInkRow</c>, so the last inked
        /// destination row is <c>23 * size / 32</c> and the edge below it is
        /// one more. 0 for a non-positive size, which draws nothing.
        /// </summary>
        public static int CoinArtInkBottom(int iconSize)
        {
            return iconSize > 0 ? ((CoinArtLastInkRow * iconSize) / CoinArtTextureSize) + 1 : 0;
        }

        /// <summary>
        /// Rows the game hangs a coin's ink BELOW the digits' ink bottom.
        /// MEASURED ink against ink WITHIN one bar tier capture, so the UI
        /// size it was taken at cancels and the count reads straight as a
        /// logical row: the coin's lowest drawn row is one below the
        /// digits' lowest. Not read off the icon box's matched position,
        /// which is a template fit and does not resolve to the row.
        /// </summary>
        public const int CoinInkBelowBaseline = 1;

        /// <summary>
        /// Where a GOLD, SILVER or COPPER icon's BOX sits inside the line box
        /// of the number it marks: low enough that the coin's INK rests on the
        /// digits' ink bottom, which is the baseline.
        /// <para>
        /// The coins alone. Every other inline currency icon keeps
        /// <see cref="InlineIconY"/>'s centred seat: only gold, silver and
        /// copper move, because the non-coin icons already measure centred
        /// to within half a pixel in an in-game screenshot.
        /// </para>
        /// <para>
        /// Neither edge is a number the caller holds: the glyph box runs
        /// <see cref="GlyphBoxInkPad"/> past the digits' ink, and the icon
        /// box runs past the coin art (<see cref="CoinArtInkBottom"/>). Then
        /// <see cref="CoinInkBelowBaseline"/> further, because the game hangs
        /// the coin under the baseline rather than resting it on one. Never
        /// negative, for the reason <see cref="InlineIconY"/> gives.
        /// </para>
        /// </summary>
        public static int CoinIconY(int digitInkTop, int digitInkHeight, int iconSize)
        {
            if (iconSize <= 0)
            {
                return 0;
            }

            int digitInkBottom = digitInkTop + digitInkHeight - GlyphBoxInkPad;
            int offset = digitInkBottom - CoinArtInkBottom(iconSize) + CoinInkBelowBaseline;
            return offset < 0 ? 0 : offset;
        }

        // GW2 coin asset ids (repo CLAUDE.md). Named here because the
        // recipe tree's per-denomination cost columns have to map an
        // already-built segment back to its denomination, which the raw
        // literals made unreadable at that call site.
        public const int GoldAssetId = 156904;
        public const int SilverAssetId = 156907;
        public const int CopperAssetId = 156902;

        /// <summary>What a coin icon says on hover - the one icon whose
        /// subject is a colour and a shape rather than a word. Null for
        /// anything else, the icon component's "no text of my own"
        /// input.</summary>
        public static string DenominationName(int assetId)
        {
            switch (assetId)
            {
                case GoldAssetId: return "Gold";
                case SilverAssetId: return "Silver";
                case CopperAssetId: return "Copper";
                default: return null;
            }
        }

        /// <summary>
        /// The three-way coin split every display site uses: 10000 copper
        /// per gold, 100 per silver. Negative input clamps to 0, matching
        /// the clamp every caller applied before this consolidation - a
        /// negative coin amount is never displayed.
        /// </summary>
        public static (long Gold, long Silver, long Copper) Split(long copper)
        {
            if (copper < 0)
            {
                copper = 0;
            }

            return (copper / 10000, (copper % 10000) / 100, copper % 100);
        }

        /// <summary>
        /// The exact strings a coin amount renders as, per denomination -
        /// null for a leading all-zero unit that is omitted entirely (a
        /// sub-1-gold amount starts at silver; copper always renders, even
        /// "0", so a zero total is never a blank cell). No zero-padding
        /// anywhere: the game renders trailing units as bare digits -
        /// MEASURED "2g 0s 0c" on live3 counterfeit-ticket (20000 copper)
        /// and "2s 0c" on relic-livingcity (200 copper), 2026-08-26, where
        /// the old "D2" would have printed "00". A non-zero sub-10 segment
        /// ("2s 5c") has no capture; bare digits are INFERRED from the
        /// zero samples. CoinCurrencyRenderer.BuildCoinSegments builds its
        /// specs from this, and the recipe tree's cost-column pre-scan
        /// measures the same strings, so the widths a column reserves can
        /// never differ from the text that lands in it.
        /// </summary>
        public static (string Gold, string Silver, string Copper) FormatSegmentTexts(long copper)
        {
            var (gold, silver, cop) = Split(copper);

            bool showGold = gold > 0;
            bool showSilver = showGold || silver > 0;

            return (
                showGold ? gold.ToString() : null,
                showSilver ? silver.ToString() : null,
                cop.ToString());
        }

        /// <summary>
        /// A coin amount as one plain string, spelled exactly the way the
        /// icons spell it: leading all-zero units omitted, trailing units
        /// as bare digits. 1005 copper is "10s 5c", never "0g 10s 05c" -
        /// the game prints "10c", not "0g 0s 10c", and "2g 0s 0c" with
        /// single zeros (measured, live3 counterfeit-ticket).
        ///
        /// <para>
        /// The module's ONE plain coin format. Four composers used to keep
        /// a private copy of this and three of them spelled it differently;
        /// each copy cited the same reason, that CoinCurrencyRenderer lives
        /// in Views.Rendering and a Blish-free class cannot reference it.
        /// The layer rule is real - it is why this method is here and not
        /// there - but the conclusion was not: nothing stopped the format
        /// living beside the split it is derived from.
        /// </para>
        /// </summary>
        public static string GameStyleText(long copper)
        {
            var (gold, silver, cop) = FormatSegmentTexts(copper);
            var sb = new StringBuilder(16);
            if (gold != null)
            {
                sb.Append(gold).Append("g ");
            }

            if (silver != null)
            {
                sb.Append(silver).Append("s ");
            }

            return sb.Append(cop).Append('c').ToString();
        }

        public struct CoinSegmentSpec
        {
            public int AssetId;
            public string Text;
            public int TextWidth;
        }

        public struct CurrencySegmentSpec
        {
            public string IconUrl;
            public string Text;
            public int TextWidth;

            // Display name of this currency - never rendered as text here
            // (width-neutral), only surfaced through the icon's hover in
            // LayoutCurrencySegments.
            public string Name;

            // The wallet currency this segment is of. The icon resolves
            // its whole tooltip from this id, the same way every other
            // currency icon in the module does, so an inline symbol beside
            // a number says as much as a row's own icon.
            public int CurrencyId;
        }

        /// <summary>
        /// One bartered ITEM in an inline price run - the number of units
        /// handed over, marked by that item's own icon. Same shape and same
        /// advance as <see cref="CurrencySegmentSpec"/>, in a separate type
        /// because the id is an ITEM id: one numeric slot shared by two id
        /// spaces is how id 24 came to open an unrelated item's tooltip on
        /// a currency row (see PlanRowViewModel.ItemId).
        /// </summary>
        public struct BarterSegmentSpec
        {
            public int ItemId;
            public string Text;
            public int TextWidth;
        }

        /// <summary>
        /// Width of a whole barter run. Same formula as
        /// <see cref="TotalCurrencySegmentsWidth"/>, because the two runs
        /// draw at the same icon size and the same gaps.
        /// </summary>
        public static int TotalBarterSegmentsWidth(List<BarterSegmentSpec> segments)
        {
            var widths = new List<int>(segments.Count);
            foreach (var seg in segments)
            {
                widths.Add(seg.TextWidth);
            }

            return ShoppingColumnMath.SegmentRunWidth(widths, CoinIconSize, CoinLabelIconGap, CoinSegmentGap);
        }

        /// <summary>
        /// Width of a whole coin run. iconSize defaults to CoinIconSize, the
        /// bar tier every plan table draws inline runs at; the rich tooltip
        /// passes its own line-height-derived size (gap G22) and must measure
        /// with the same number it draws with. That derived size converges on
        /// the same tier - a 20px line height gives 16 - so the override is
        /// now a font-tracking refinement of the bar tier rather than a
        /// departure from it.
        /// </summary>
        public static int TotalCoinSegmentsWidth(List<CoinSegmentSpec> segments, int iconSize = 0)
        {
            if (segments.Count == 0)
            {
                return 0;
            }

            int effectiveIcon = iconSize > 0 ? iconSize : CoinIconSize;
            int width = 0;
            foreach (var seg in segments)
            {
                width += seg.TextWidth + CoinLabelIconGap + effectiveIcon + CoinSegmentGap;
            }

            return width - CoinSegmentGap;
        }

        /// <summary>
        /// Delegates to ShoppingColumnMath.SegmentRunWidth (the same
        /// "label, gap, icon, gap" formula as TotalCoinSegmentsWidth above,
        /// parameterized) - verbatim from CoinCurrencyRenderer, unchanged.
        /// </summary>
        public static int TotalCurrencySegmentsWidth(List<CurrencySegmentSpec> segments)
        {
            var widths = new List<int>(segments.Count);
            foreach (var seg in segments)
            {
                widths.Add(seg.TextWidth);
            }

            return ShoppingColumnMath.SegmentRunWidth(widths, CoinIconSize, CoinLabelIconGap, CoinSegmentGap);
        }
    }
}
