using System.Collections.Generic;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // The one place the coin-icon-right-of-number invariant (CLAUDE.md) is
    // implemented: every coin amount the module draws goes through here.
    internal static class CoinCurrencyRenderer
    {
        // --- Coin display helpers ---
        //
        // gw2e's Coins component renders NumberFormat(gold) -> icon ->
        // NumberFormat(silver, zero-padded once gold precedes it) -> icon ->
        // NumberFormat(copper, zero-padded once silver precedes it) -> icon,
        // omitting leading all-zero units (a sub-1-gold amount starts at
        // silver, un-padded). Segments are measured up front so the same
        // spec list can be laid out left-anchored, right-anchored (table
        // price columns), or centered (cost tiles) without re-measuring.

        // CoinIconSize/CoinLabelIconGap/CoinSegmentGap,
        // CoinSegmentSpec/CurrencySegmentSpec, and the TotalCoinSegmentsWidth/
        // TotalCurrencySegmentsWidth arithmetic moved to Services/CoinSegmentMath.cs
        // (Blish-free, unit-tested) so the width formulas can be tested without
        // referencing UI code. Everything below that still touches Label/Panel/
        // BitmapFont references CoinSegmentMath's constants/structs directly.
        internal static List<CoinSegmentMath.CoinSegmentSpec> BuildCoinSegments(long copper, BitmapFont font)
        {
            // Which units show, and how they are padded, is
            // CoinSegmentMath.FormatSegmentTexts' call - the recipe tree's
            // per-denomination column pre-scan measures the same strings,
            // so neither can drift from the other.
            var (goldText, silverText, copperText) = CoinSegmentMath.FormatSegmentTexts(copper);

            var segments = new List<CoinSegmentMath.CoinSegmentSpec>(3);
            if (goldText != null)
            {
                AddSegmentSpec(segments, font, CoinSegmentMath.GoldAssetId, goldText);
            }

            if (silverText != null)
            {
                AddSegmentSpec(segments, font, CoinSegmentMath.SilverAssetId, silverText);
            }

            AddSegmentSpec(segments, font, CoinSegmentMath.CopperAssetId, copperText);
            return segments;
        }

        // Internal because MainView builds its own 3-segment
        // gold/silver/copper spec list (show-all, no padding) and would
        // otherwise duplicate this measure-and-wrap. MainView ->
        // Views/Rendering is a forward dependency; the reverse direction
        // stays closed (docs/ARCHITECTURE.md section 5).
        internal static void AddSegmentSpec(List<CoinSegmentMath.CoinSegmentSpec> segments, BitmapFont font, int assetId, string text)
        {
            int width = (int)System.Math.Ceiling(font.MeasureString(text).Width);
            segments.Add(new CoinSegmentMath.CoinSegmentSpec { AssetId = assetId, Text = text, TextWidth = width });
        }

        internal static int TotalCoinSegmentsWidth(List<CoinSegmentMath.CoinSegmentSpec> segments)
        {
            return CoinSegmentMath.TotalCoinSegmentsWidth(segments);
        }

        /// <summary>
        /// A coin/currency segment run's already-created controls
        /// plus each segment's cached (font-only, panelWidth-invariant)
        /// text width, so a relayout closure can reposition the whole run
        /// at a new x without ever calling MeasureString again - see
        /// RepositionSegments. Controls/TextWidths are always the same
        /// length and share indices.
        /// </summary>
        internal struct SegmentLayoutHandle
        {
            public (Label Label, Panel Icon)[] Controls;
            public int[] TextWidths;

            /// <summary>
            /// Extra y applied to the ICONS only, so an icon seats against
            /// the digits rather than against the top edge of the number's
            /// line box - a coin run by <see cref="CoinDigitSeat"/>, a
            /// wallet-currency run by <see cref="DigitSeat"/>. Cached on the
            /// handle so RepositionSegments reproduces the same offset
            /// without the caller having to remember it.
            /// </summary>
            public int IconYOffset;

            /// <summary>
            /// Each coin segment's denomination asset id, parallel to
            /// Controls - the recipe tree's per-denomination sub-columns
            /// need to map an already-built control back to the column it
            /// belongs in when repositioning. Null for currency runs and
            /// for coin runs laid out as one contiguous run, neither of
            /// which repositions per denomination.
            /// </summary>
            public int[] AssetIds;

            /// <summary>
            /// This run's own icon size, 0 meaning the shared
            /// CoinSegmentMath.CoinIconSize. Only the rich tooltip sets it
            /// (gap G22); it is cached here so RepositionSegments advances
            /// by the size the run was actually drawn at.
            /// </summary>
            public int IconSize;

            public int EffectiveIconSize => IconSize > 0 ? IconSize : CoinSegmentMath.CoinIconSize;

            public static readonly SegmentLayoutHandle Empty =
                new SegmentLayoutHandle { Controls = System.Array.Empty<(Label, Panel)>(), TextWidths = System.Array.Empty<int>() };
        }

        /// <summary>
        /// Lays out coin segments left-to-right starting at x. alphaScale
        /// dims the number labels (not the icons - Panel has no tint
        /// property) for dimmed not-crafted subtree rows. The icons' own y
        /// is this method's to decide, not the caller's - see
        /// <see cref="CoinDigitSeat"/>.
        /// </summary>
        internal static SegmentLayoutHandle LayoutCoinSegments(
            Panel parent, List<CoinSegmentMath.CoinSegmentSpec> segments, int startX, int y, BitmapFont font,
            float alphaScale = 1f, bool showShadow = false, int iconSize = 0)
        {
            var controls = new (Label, Panel)[segments.Count];
            var widths = new int[segments.Count];
            int effectiveIcon = iconSize > 0 ? iconSize : CoinSegmentMath.CoinIconSize;
            int iconYOffset = CoinDigitSeat(font, effectiveIcon);
            int x = startX;
            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                controls[i] = CreateCoinSegment(
                    parent, seg, x, y, font, alphaScale, iconYOffset, showShadow, effectiveIcon);
                widths[i] = seg.TextWidth;
                x += seg.TextWidth + CoinSegmentMath.CoinLabelIconGap + effectiveIcon + CoinSegmentMath.CoinSegmentGap;
            }

            return new SegmentLayoutHandle
            {
                Controls = controls,
                TextWidths = widths,
                IconYOffset = iconYOffset,
                IconSize = iconSize,
            };
        }

        /// <summary>
        /// Where a WALLET CURRENCY run's icons sit against its digits. THE
        /// one place a non-coin inline icon's y is decided, so every value
        /// cell in the module seats identically and no call site can leave
        /// its icons stuck to the top edge of the number's line box - which
        /// is what all but two of them did.
        /// <para>
        /// Read off the face itself, not off a table: the digits' ink top
        /// and height come from the '0' region, so a run drawn at any size
        /// seats correctly without anything re-measuring Menomonia. A face
        /// with no '0' is a broken install, not a layout case - it falls
        /// back to the line box, which is what a font reports for a glyph
        /// it does not have.
        /// </para>
        /// </summary>
        private static int DigitSeat(BitmapFont font, int iconSize)
        {
            if (font == null)
            {
                return 0;
            }

            var zero = font.GetCharacterRegion('0');
            return zero == null
                ? CoinSegmentMath.InlineIconY(0, font.LineHeight, iconSize)
                : CoinSegmentMath.InlineIconY(zero.YOffset, zero.Height, iconSize);
        }

        /// <summary>
        /// <see cref="DigitSeat"/>'s twin for a GOLD, SILVER or COPPER run,
        /// which seats the coin's art on the digits' ink bottom rather than
        /// centring its box (<see cref="CoinSegmentMath.CoinIconY"/>). The
        /// coins alone move; every other inline currency icon keeps the
        /// centred seat, so folding the two back together undoes a decision
        /// rather than tidying a duplicate.
        /// <para>
        /// A face with no '0' keeps the CENTRED fallback: a bottom seat is a
        /// statement about the baseline, and a line box - all a font reports
        /// for a glyph it does not have - carries none.
        /// </para>
        /// </summary>
        private static int CoinDigitSeat(BitmapFont font, int iconSize)
        {
            var zero = font?.GetCharacterRegion('0');
            return zero == null
                ? DigitSeat(font, iconSize)
                : CoinSegmentMath.CoinIconY(zero.YOffset, zero.Height, iconSize);
        }

        /// <summary>
        /// One coin segment's two controls at an explicit x - the shared
        /// body of the contiguous-run and per-denomination-sub-column
        /// layouts, so the two can never disagree about how a segment is
        /// built (colour, dimming, or the coin invariant's icon-right-of-
        /// number geometry).
        /// </summary>
        private static (Label Label, Panel Icon) CreateCoinSegment(
            Panel parent, CoinSegmentMath.CoinSegmentSpec seg, int x, int y, BitmapFont font,
            float alphaScale, int iconYOffset, bool showShadow, int iconSize)
        {
            Color textColor = GetCoinColor(seg.AssetId);
            if (alphaScale < 1f)
            {
                textColor *= alphaScale;
            }

            var label = new Label()
            {
                Text = seg.Text,
                Font = font,
                TextColor = textColor,
                // Off everywhere but the rich tooltip, whose every glyph
                // carries the game's dark halo (gap G8); the plan tables
                // render coin runs on their own flat rows.
                ShowShadow = showShadow,
                ShadowColor = Color.Black * 0.8f,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(x, y),
                Parent = parent,
            };

            // Through the icon component, like the currency half of this
            // file - but deliberately UNFRAMED where a wallet currency is
            // framed (IconControls.CreateCurrencyIcon). A denomination is a
            // unit marker on a number rather than a subject, and its size
            // here is the caller's, not a tier's: the rich tooltip draws
            // these larger. With no tier there is nothing to inset the art
            // inside, so a frame would add 2px to every segment's advance -
            // a term in the minimum-window-width derivation.
            var icon = IconControls.CreateAssetIcon(
                parent, seg.AssetId,
                x + seg.TextWidth + CoinSegmentMath.CoinLabelIconGap, y + iconYOffset,
                iconSize, CoinSegmentMath.DenominationName(seg.AssetId));

            return (label, icon);
        }

        /// <summary>
        /// A MEASURED zero, drawn the way the game draws one: "0" against
        /// the copper icon. Deliberately NOT
        /// <see cref="RenderValueCellRightAligned"/>'s dash, which claims a
        /// value could not be priced at all - a row that has been solved and
        /// owes nothing has a real answer, and a dash hides it behind the
        /// same mark an unpriceable row gets.
        /// </summary>
        internal static ValueCellHandle RenderZeroValueCellRightAligned(
            Panel parent, int rightEdgeX, int y, BitmapFont font)
        {
            var segments = BuildCoinSegments(0, font);
            int width = TotalCoinSegmentsWidth(segments);
            return new ValueCellHandle
            {
                CoinSegments = LayoutCoinSegments(parent, segments, rightEdgeX - width, y, font),
                CurrencySegments = SegmentLayoutHandle.Empty,
            };
        }

        /// <summary>Width of <see cref="RenderZeroValueCellRightAligned"/>'s cell.</summary>
        internal static int MeasureZeroValueWidth(BitmapFont font)
        {
            return TotalCoinSegmentsWidth(BuildCoinSegments(0, font));
        }

        /// <summary>
        /// Non-allocating reposition twin to LayoutCoinSegments/
        /// LayoutCurrencySegments (m2 3.7/4) - moves EXISTING segment
        /// controls to new x-positions using the cached TextWidths, never
        /// creating a control or calling MeasureString. Shared by both coin
        /// and currency segment runs since they follow the identical
        /// "label, gap, icon, gap" geometry (same CoinSegmentMath.CoinIconSize/
        /// CoinLabelIconGap/CoinSegmentGap constants).
        /// </summary>
        internal static void RepositionSegments(SegmentLayoutHandle handle, int startX, int y)
        {
            int x = startX;
            int effectiveIcon = handle.EffectiveIconSize;
            for (int i = 0; i < handle.Controls.Length; i++)
            {
                var (label, icon) = handle.Controls[i];
                int textWidth = handle.TextWidths[i];
                label.Location = new Point(x, y);
                icon.Location = new Point(x + textWidth + CoinSegmentMath.CoinLabelIconGap, y + handle.IconYOffset);
                x += textWidth + CoinSegmentMath.CoinLabelIconGap + effectiveIcon + CoinSegmentMath.CoinSegmentGap;
            }
        }

        // The game's own coin-number tints, MEASURED on live3 (2026-08-26)
        // as saturating ink peaks - the first modern samples, replacing
        // constants that only a 2012 capture had ever anchored:
        // silver #AAA (170,170,170) and copper #B62 (187,102,34) both cap
        // exactly there on TWO captures each (vials "39s 96c" and
        // fury-scorched "81s 92c"); gold #DB5 (221,187,85) is a WEAKER
        // read - one capture (counterfeit-ticket "2g"), 25 ink pixels,
        // p95 (221,187,88) - but its direction (duller than the old
        // #FFCC00) matches both zero-padding-era notes, and #DB5 keeps the
        // exact-shorthand pattern every other measured game colour shows.
        private static Color GetCoinColor(int assetId)
        {
            switch (assetId)
            {
                case CoinSegmentMath.GoldAssetId: return new Color(221, 187, 85);
                case CoinSegmentMath.SilverAssetId: return new Color(170, 170, 170);
                case CoinSegmentMath.CopperAssetId: return new Color(187, 102, 34);
                default: return Color.White;
            }
        }

        // --- Currency + mixed value display helpers ---
        //
        // A BuyFromVendor decision can be priced wholly or partly in a
        // non-coin currency (spirit shards, karma, ...). CurrencyAmountViewModel
        // (shopping rows, via PlanViewModelBuilder) and CraftingTreeNode.
        // VendorCurrencyCosts (tree, resolved here via CurrencyDisplayResolver)
        // both feed the same rendering below, so the two sibling sites named
        // in KNOWN-ISSUES #16 (shopping Each/Total cells and the tree cost
        // column) can never drift apart. Currency segments follow the exact
        // same "amount label, then icon to the RIGHT" convention as coin
        // segments (the coin invariant) and reuse its icon size/gaps; a
        // mixed value renders coin segments first, then currency segments.
        // A value with neither a coin price nor a currency cost is
        // genuinely unpriceable (gw2e: "Not sold or crafted") and renders a
        // plain dash - never a blank cell, never an invented "0".

        // ASCII-only source rule: em dash via escape, never a raw pasted
        // Unicode character - this IS the gw2e-style unpriceable dash
        // itself, not incidental prose.
        private const string UnpricedDashText = "\u2014";
        private static readonly Color UnpricedDashColor = new Color(140, 140, 140);

        internal static List<CoinSegmentMath.CurrencySegmentSpec> BuildCurrencySegments(
            IReadOnlyList<CurrencyAmountViewModel> amounts, BitmapFont font)
        {
            var segments = new List<CoinSegmentMath.CurrencySegmentSpec>();
            if (amounts == null)
            {
                return segments;
            }

            foreach (var amount in amounts)
            {
                // A fractional-per-unit "Each" amount carries a
                // literal "N for M" bundle label instead of a whole-number
                // Amount (CurrencyDisplayResolver.ResolveUnitAmounts) -
                // render that text verbatim rather than the numeric amount.
                string text = amount.BundleLabel ?? amount.Amount.ToString();
                int width = (int)System.Math.Ceiling(font.MeasureString(text).Width);
                segments.Add(new CoinSegmentMath.CurrencySegmentSpec
                {
                    IconUrl = amount.IconUrl,
                    Text = text,
                    TextWidth = width,
                    Name = amount.Name,
                });
            }

            return segments;
        }

        /// <summary>
        /// The actual width arithmetic lives in CoinSegmentMath (Blish-free,
        /// tested), which itself delegates to ShoppingColumnMath, so the
        /// pre-scan (MeasureValueWidth) and the real layout
        /// (LayoutValueSegmentsRightAligned) below can never drift apart;
        /// only the per-segment text measurement (BitmapFont.MeasureString)
        /// is Blish-bound and stays here.
        /// </summary>
        internal static int TotalCurrencySegmentsWidth(List<CoinSegmentMath.CurrencySegmentSpec> segments)
        {
            return CoinSegmentMath.TotalCurrencySegmentsWidth(segments);
        }

        internal static SegmentLayoutHandle LayoutCurrencySegments(
            Panel parent, List<CoinSegmentMath.CurrencySegmentSpec> segments, int startX, int y, BitmapFont font, float alphaScale = 1f)
        {
            var controls = new (Label, Panel)[segments.Count];
            var widths = new int[segments.Count];
            int iconYOffset = DigitSeat(font, CoinSegmentMath.CoinIconSize);
            int x = startX;
            Color textColor = new Color(220, 220, 220);
            if (alphaScale < 1f)
            {
                textColor *= alphaScale;
            }

            for (int i = 0; i < segments.Count; i++)
            {
                var seg = segments[i];
                var label = new Label()
                {
                    Text = seg.Text,
                    Font = font,
                    TextColor = textColor,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(x, y),
                    Parent = parent,
                };

                // A currency icon carries no visible
                // name text anywhere in this cell (unlike SummarySectionRenderer.
                // CreateCurrencyRow, which prints the name as a label before
                // the icon) - a hover tooltip is the only way to identify it.
                // Frame-less at the bar tier: beside the digits this icon is
                // a currency symbol in the coin denominations' role, and
                // they take no border (IconFrameGeometry.CurrencyIsFramed).
                // It still occupies the whole measured bar-tier window, so
                // this segment's advance below is unchanged by the border
                // coming off.
                var icon = IconControls.CreateCurrencyIcon(
                    parent, seg.IconUrl, x + seg.TextWidth + CoinSegmentMath.CoinLabelIconGap,
                    y + iconYOffset, ItemIconTier.CurrencyBarRun, seg.Name);

                controls[i] = (label, icon);
                widths[i] = seg.TextWidth;
                x += seg.TextWidth + CoinSegmentMath.CoinLabelIconGap + CoinSegmentMath.CoinIconSize + CoinSegmentMath.CoinSegmentGap;
            }

            return new SegmentLayoutHandle
            {
                Controls = controls,
                TextWidths = widths,
                IconYOffset = iconYOffset,
            };
        }

        /// <summary>
        /// Pixel width a coin/currency/mixed value would occupy if laid out
        /// via LayoutValueSegmentsRightAligned - built from the exact same
        /// BuildCoinSegments/BuildCurrencySegments + Total*SegmentsWidth
        /// path that layout call uses, so the shopping list's pre-scan
        /// (CreateShoppingListBody) can never drift from what actually
        /// renders. copper == 0 with at least one currency amount is a
        /// valid, currency-only case (not a "zero width" special case).
        /// </summary>
        internal static int MeasureValueWidth(
            long copper, IReadOnlyList<CurrencyAmountViewModel> currencyAmounts, BitmapFont font)
        {
            int coinWidth = copper > 0 ? TotalCoinSegmentsWidth(BuildCoinSegments(copper, font)) : 0;
            int currencyWidth = TotalCurrencySegmentsWidth(BuildCurrencySegments(currencyAmounts, font));
            return (coinWidth > 0 && currencyWidth > 0) ? coinWidth + CoinSegmentMath.CoinSegmentGap + currencyWidth : coinWidth + currencyWidth;
        }

        /// <summary>
        /// Everything a relayout closure needs to reposition an
        /// already-rendered value cell (RenderValueCellRightAligned's
        /// result) at a new rightEdgeX without any MeasureString call -
        /// either DashLabel is set (genuinely unpriceable row) or the two
        /// SegmentLayoutHandles are (each individually empty when that half
        /// of the mix is absent - e.g. CoinSegments.Controls.Length == 0
        /// for a currency-only row).
        /// </summary>
        internal sealed class ValueCellHandle
        {
            public SegmentLayoutHandle CoinSegments;
            public SegmentLayoutHandle CurrencySegments;
            public Label DashLabel;
        }

        /// <summary>
        /// Right-aligns coin segments (if copper &gt; 0) followed by
        /// currency segments (if any) to rightEdgeX - the "mixed
        /// coin+currency renders coin segments then currency segments"
        /// rule. Callers must not invoke this for a value with neither
        /// (RenderValueCellRightAligned handles that dash case instead).
        /// </summary>
        internal static ValueCellHandle LayoutValueSegmentsRightAligned(
            Panel parent, long copper, IReadOnlyList<CurrencyAmountViewModel> currencyAmounts,
            int rightEdgeX, int y, BitmapFont font, float alphaScale = 1f)
        {
            var coinSegments = copper > 0 ? BuildCoinSegments(copper, font) : new List<CoinSegmentMath.CoinSegmentSpec>();
            var currencySegments = BuildCurrencySegments(currencyAmounts, font);
            int coinWidth = TotalCoinSegmentsWidth(coinSegments);
            int currencyWidth = TotalCurrencySegmentsWidth(currencySegments);
            int gap = (coinWidth > 0 && currencyWidth > 0) ? CoinSegmentMath.CoinSegmentGap : 0;

            int startX = rightEdgeX - (coinWidth + gap + currencyWidth);
            var coinHandle = LayoutCoinSegments(parent, coinSegments, startX, y, font, alphaScale);
            var currencyHandle = LayoutCurrencySegments(parent, currencySegments, startX + coinWidth + gap, y, font, alphaScale);

            return new ValueCellHandle { CoinSegments = coinHandle, CurrencySegments = currencyHandle };
        }

        /// <summary>
        /// Single entry point for a shopping/tree value cell: coin-only,
        /// currency-only, and mixed all render via
        /// LayoutValueSegmentsRightAligned unchanged from (or, for
        /// currency/mixed, newly matching) the coin invariant; a value with
        /// neither a coin price nor a currency cost renders a plain dash
        /// instead of a blank cell or an invented "0".
        /// Returns a handle so a relayout closure can reposition the cell
        /// at a new rightEdgeX later - see RepositionValueCellRightAligned.
        /// </summary>
        internal static ValueCellHandle RenderValueCellRightAligned(
            Panel parent, long copper, IReadOnlyList<CurrencyAmountViewModel> currencyAmounts,
            int rightEdgeX, int y, BitmapFont font, float alphaScale = 1f)
        {
            bool hasCoin = copper > 0;
            bool hasCurrency = currencyAmounts != null && currencyAmounts.Count > 0;

            if (!hasCoin && !hasCurrency)
            {
                Color dashColor = alphaScale < 1f ? UnpricedDashColor * alphaScale : UnpricedDashColor;
                var dashLabel = LabelHelpers.CreateRightAlignedLabel(parent, UnpricedDashText, font, dashColor, rightEdgeX, y);
                return new ValueCellHandle
                {
                    CoinSegments = SegmentLayoutHandle.Empty,
                    CurrencySegments = SegmentLayoutHandle.Empty,
                    DashLabel = dashLabel,
                };
            }

            return LayoutValueSegmentsRightAligned(parent, copper, currencyAmounts, rightEdgeX, y, font, alphaScale);
        }

        /// <summary>
        /// Non-allocating reposition twin to
        /// RenderValueCellRightAligned - moves an EXISTING value cell's
        /// controls to a new rightEdgeX, using only the cached per-segment
        /// TextWidths (ShoppingColumnMath.SegmentRunWidth, the same pure
        /// function the shopping column pre-scan uses, so the width this
        /// computes can never drift from what LayoutValueSegmentsRightAligned
        /// actually laid out). No MeasureString, no new controls.
        /// </summary>
        internal static void RepositionValueCellRightAligned(ValueCellHandle handle, int rightEdgeX, int y)
        {
            if (handle.DashLabel != null)
            {
                handle.DashLabel.Location = new Point(rightEdgeX - handle.DashLabel.Width, y);
                return;
            }

            int coinWidth = ShoppingColumnMath.SegmentRunWidth(handle.CoinSegments.TextWidths, CoinSegmentMath.CoinIconSize, CoinSegmentMath.CoinLabelIconGap, CoinSegmentMath.CoinSegmentGap);
            int currencyWidth = ShoppingColumnMath.SegmentRunWidth(handle.CurrencySegments.TextWidths, CoinSegmentMath.CoinIconSize, CoinSegmentMath.CoinLabelIconGap, CoinSegmentMath.CoinSegmentGap);
            int gap = (coinWidth > 0 && currencyWidth > 0) ? CoinSegmentMath.CoinSegmentGap : 0;

            int startX = rightEdgeX - (coinWidth + gap + currencyWidth);
            RepositionSegments(handle.CoinSegments, startX, y);
            RepositionSegments(handle.CurrencySegments, startX + coinWidth + gap, y);
        }

        // --- Per-denomination sub-columns (recipe tree cost column) ---
        //
        // The tree's cost column right-aligns each DENOMINATION in its own
        // sub-column instead of right-aligning the whole run, so the coin
        // icons line up vertically down the tree instead of sitting
        // wherever the digit counts happen to leave them. Column widths
        // come from a per-render pre-scan of the whole tree
        // (Services/TreeCostColumnMath) - never re-measured per row.
        //
        // Everything below reuses the same segment builders, colours and
        // dash fallback as the right-aligned path above; only the x of
        // each already-built segment differs.
        private static int SubColumnRightEdge(TreeCostColumnMath.CostSubColumnEdges edges, int assetId)
        {
            switch (assetId)
            {
                case CoinSegmentMath.GoldAssetId: return edges.GoldRightEdge;
                case CoinSegmentMath.SilverAssetId: return edges.SilverRightEdge;
                default: return edges.CopperRightEdge;
            }
        }

        /// <summary>
        /// Sub-column twin of RenderValueCellRightAligned: coin segments
        /// land in their own denomination's sub-column, any currency run
        /// right-aligns to the trailing currency sub-column, and a value
        /// with neither still renders the unpriceable dash - aligned to
        /// the copper sub-column rather than the raw column edge, so a
        /// dash sits under the coin figures it stands in for rather than
        /// out past the currency band.
        /// </summary>
        internal static ValueCellHandle RenderValueCellInSubColumns(
            Panel parent, long copper, IReadOnlyList<CurrencyAmountViewModel> currencyAmounts,
            TreeCostColumnMath.CostSubColumnEdges edges, int y, BitmapFont font, float alphaScale = 1f)
        {
            bool hasCoin = copper > 0;
            bool hasCurrency = currencyAmounts != null && currencyAmounts.Count > 0;

            if (!hasCoin && !hasCurrency)
            {
                Color dashColor = alphaScale < 1f ? UnpricedDashColor * alphaScale : UnpricedDashColor;
                var dashLabel = LabelHelpers.CreateRightAlignedLabel(
                    parent, UnpricedDashText, font, dashColor, edges.CopperRightEdge, y);
                return new ValueCellHandle
                {
                    CoinSegments = SegmentLayoutHandle.Empty,
                    CurrencySegments = SegmentLayoutHandle.Empty,
                    DashLabel = dashLabel,
                };
            }

            var coinHandle = SegmentLayoutHandle.Empty;
            if (hasCoin)
            {
                var coinSegments = BuildCoinSegments(copper, font);
                var controls = new (Label, Panel)[coinSegments.Count];
                var widths = new int[coinSegments.Count];
                var assetIds = new int[coinSegments.Count];
                int iconYOffset = CoinDigitSeat(font, CoinSegmentMath.CoinIconSize);
                for (int i = 0; i < coinSegments.Count; i++)
                {
                    var seg = coinSegments[i];
                    int x = SubColumnRightEdge(edges, seg.AssetId) - TreeCostColumnMath.SegmentWidth(seg.TextWidth);
                    controls[i] = CreateCoinSegment(
                        parent, seg, x, y, font, alphaScale, iconYOffset, false, CoinSegmentMath.CoinIconSize);
                    widths[i] = seg.TextWidth;
                    assetIds[i] = seg.AssetId;
                }

                coinHandle = new SegmentLayoutHandle
                {
                    Controls = controls,
                    TextWidths = widths,
                    AssetIds = assetIds,
                    IconYOffset = iconYOffset,
                };
            }

            var currencySegments = BuildCurrencySegments(currencyAmounts, font);
            int currencyRunWidth = TotalCurrencySegmentsWidth(currencySegments);
            var currencyHandle = LayoutCurrencySegments(
                parent, currencySegments, edges.CurrencyRightEdge - currencyRunWidth, y, font, alphaScale);

            return new ValueCellHandle { CoinSegments = coinHandle, CurrencySegments = currencyHandle };
        }

        /// <summary>
        /// Non-allocating reposition twin to RenderValueCellInSubColumns -
        /// moves an existing cell to a new set of sub-column edges using
        /// only the cached per-segment text widths and denominations. No
        /// MeasureString, no new controls.
        /// </summary>
        internal static void RepositionValueCellInSubColumns(
            ValueCellHandle handle, TreeCostColumnMath.CostSubColumnEdges edges, int y)
        {
            if (handle.DashLabel != null)
            {
                handle.DashLabel.Location = new Point(edges.CopperRightEdge - handle.DashLabel.Width, y);
                return;
            }

            var coin = handle.CoinSegments;
            if (coin.AssetIds != null)
            {
                for (int i = 0; i < coin.Controls.Length; i++)
                {
                    int textWidth = coin.TextWidths[i];
                    int x = SubColumnRightEdge(edges, coin.AssetIds[i]) - TreeCostColumnMath.SegmentWidth(textWidth);
                    var (label, icon) = coin.Controls[i];
                    label.Location = new Point(x, y);
                    icon.Location = new Point(x + textWidth + CoinSegmentMath.CoinLabelIconGap, y + coin.IconYOffset);
                }
            }

            int currencyRunWidth = ShoppingColumnMath.SegmentRunWidth(
                handle.CurrencySegments.TextWidths, CoinSegmentMath.CoinIconSize,
                CoinSegmentMath.CoinLabelIconGap, CoinSegmentMath.CoinSegmentGap);
            RepositionSegments(handle.CurrencySegments, edges.CurrencyRightEdge - currencyRunWidth, y);
        }
    }
}
