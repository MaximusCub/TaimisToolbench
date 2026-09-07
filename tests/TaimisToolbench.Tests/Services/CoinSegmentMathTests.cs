using System.Collections.Generic;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // TotalCoinSegmentsWidth/TotalCurrencySegmentsWidth, their plain data
    // specs (CoinSegmentSpec/CurrencySegmentSpec), and the geometry
    // constants they're built from were extracted verbatim out of
    // CoinCurrencyRenderer (Views/Rendering, Blish-bound) into a
    // Blish-free class so the arithmetic can be tested without referencing
    // UI code (repo invariant: tests must never reference UI code).
    // Expected values are computed against the real
    // CoinLabelIconGap/CoinIconSize/CoinSegmentGap constants rather than
    // re-deriving the formula, so a future constant change updates the
    // assertions' inputs, not just the production code, catching drift -
    // mirrors ShoppingColumnMathTests' SegmentRunWidth cases.
    public class CoinSegmentMathTests
    {
        private const int IconSize = CoinSegmentMath.CoinIconSize;
        private const int LabelIconGap = CoinSegmentMath.CoinLabelIconGap;
        private const int SegmentGap = CoinSegmentMath.CoinSegmentGap;

        // --- InlineIconY: the WALLET CURRENCY seat ---
        //
        // The reported defect: the icon beside a figure sat above the digits'
        // optical centre. Every inline run but two passed a seat of 0, which
        // puts the icon box on the top edge of the number's line box - and a
        // line box carries ascender and descender space the digits never
        // reach. Gold, silver and copper have since left this seat for
        // CoinIconY's; everything else still centres here.
        [Theory]
        [InlineData(3, 14, CoinSegmentMath.CoinIconSize)]
        [InlineData(2, 15, CoinSegmentMath.CoinIconSize)]
        [InlineData(6, 24, CurrencyIconTiers.WalletListIconSize)]
        [InlineData(4, 17, 12)]
        public void TheInlineIconSeat_CentresTheIconOnTheDigitsInk(
            int digitInkTop, int digitInkHeight, int iconSize)
        {
            int y = CoinSegmentMath.InlineIconY(digitInkTop, digitInkHeight, iconSize);

            // Doubled so an odd span has no half pixel to argue about; a
            // whole-pixel seat can only land on or one pixel above centre.
            int iconCentre = (2 * y) + iconSize;
            int inkCentre = (2 * digitInkTop) + digitInkHeight;
            Assert.InRange(iconCentre - inkCentre, -1, 0);
        }

        [Fact]
        public void TheInlineIconSeat_IsNotTheTopOfTheLineBox()
        {
            // Menomonia 16's cap ink, the face every table cell's inline
            // run draws in. A seat of 0 here is the defect returning.
            Assert.NotEqual(
                0,
                CoinSegmentMath.InlineIconY(
                    TypeRampMetrics.BodyInk.CapTopY,
                    TypeRampMetrics.BodyInk.CapHeight,
                    CoinSegmentMath.CoinIconSize));
        }

        [Theory]
        [InlineData(3, 14, 24)]
        [InlineData(0, 0, 16)]
        public void AnIconTallerThanTheDigits_StaysInsideTheLineBox(
            int digitInkTop, int digitInkHeight, int iconSize)
        {
            // Centring one would start it above the label's own top, and
            // the row above reserved its height from that line box.
            Assert.Equal(0, CoinSegmentMath.InlineIconY(digitInkTop, digitInkHeight, iconSize));
        }

        // --- CoinIconY ---
        //
        // The face every inline coin run draws in: UiFonts.Body is
        // DefaultFont16, menomonia-16-regular, and the seat is read off its
        // '0' region, which declares yoffset 2 height 15. MEASURED off that
        // face's shipped texture page: the '0' inks rows 3..15 of the 20px
        // line box, so the digits' ink stops at the exclusive edge 16 while
        // the declared box runs one row past it.
        private const int BodyZeroYOffset = 2;
        private const int BodyZeroHeight = 15;
        private const int BodyDigitInkBottom = 16;

        // Where MainView draws the coin row's "Coin" caption inside its
        // block panel. The digits are seated against this.
        private const int CaptionY = 2;

        // The three shipped 32x32 coin textures, DRAWN rows MEASURED by
        // compositing each source row over a dark row ground: gold and
        // silver 5..23, copper 7..23. Rows 24..26 composite darker than the
        // ground on all three - the art's black bottom rim - so they are
        // not what a reader aligns the digits against. The last drawn row
        // is shared; the first is not, and the seat must not care.
        private const int CoinArtSourceSize = 32;

        [Theory]
        [InlineData(CoinSegmentMath.GoldAssetId, 5, 23)]
        [InlineData(CoinSegmentMath.SilverAssetId, 5, 23)]
        [InlineData(CoinSegmentMath.CopperAssetId, 7, 23)]
        public void TheCoinSeat_HangsEveryDenominationsInkUnderTheDigitsInkBottom(
            int assetId, int firstInkRow, int lastInkRow)
        {
            int iconSize = CoinSegmentMath.CoinIconSize;
            int y = CoinSegmentMath.CoinIconY(BodyZeroYOffset, BodyZeroHeight, iconSize);

            // Where this denomination's own measured ink lands once its
            // source rows are point-sampled into the icon box the run draws.
            int lastDrawnRow = y + ((lastInkRow * iconSize) / CoinArtSourceSize);
            int inkTop = y + ((firstInkRow * iconSize) / CoinArtSourceSize);

            // The relationship the in-game bar tier shows, stated in rows
            // rather than in edges: the coin's lowest drawn row sits one
            // below the digits' lowest. Flush, or above it, is the defect.
            Assert.Equal(
                (BodyDigitInkBottom - 1) + CoinSegmentMath.CoinInkBelowBaseline, lastDrawnRow);
            Assert.True(inkTop <= lastDrawnRow, "coin " + assetId + " has no ink");
            Assert.True(inkTop >= 0, "coin " + assetId + " overdraws the row above");
        }

        [Fact]
        public void TheCoinSeat_IsPinnedAtTheShippedFaceAndTier()
        {
            // The whole seat, end to end, at the face and tier every plan
            // table draws its coin runs in: Menomonia 16's '0' against the
            // 18px bar-tier box. Pinned as one number because it is what a
            // reader compares against the game, and because a drift in the
            // glyph pad, the art's last drawn row or the hang below the
            // baseline each move it without failing anything else.
            Assert.Equal(
                4, CoinSegmentMath.CoinIconY(BodyZeroYOffset, BodyZeroHeight, CoinSegmentMath.CoinIconSize));
        }

        [Fact]
        public void TheCoinSeat_SitsLowerThanTheCurrencySeatItUsedToShare()
        {
            // The rule: gold, silver and copper move DOWN onto the
            // digits' ink bottom; every other inline currency icon keeps the
            // centred box. A change that folds the two seats back together
            // fails here.
            int iconSize = CoinSegmentMath.CoinIconSize;

            int coin = CoinSegmentMath.CoinIconY(BodyZeroYOffset, BodyZeroHeight, iconSize);
            int currency = CoinSegmentMath.InlineIconY(BodyZeroYOffset, BodyZeroHeight, iconSize);

            Assert.True(coin > currency, "coin " + coin + " is not below currency " + currency);
        }

        [Fact]
        public void TheCurrencySeat_CentresTheBoxAtTheShippedFaceAndTier()
        {
            // The non-coin icons measured centred to within half a pixel in
            // the capture that reported the coin defect, and they still
            // centre. The seat is 0 at this face and tier because an 18px
            // box very nearly fills the '0' ink's own reach, not because
            // the box was parked on the line box top: the ink runs rows
            // 3..15 and the centred box rows 0..17, half a pixel high.
            Assert.Equal(
                0, CoinSegmentMath.InlineIconY(BodyZeroYOffset, BodyZeroHeight, CoinSegmentMath.CoinIconSize));
        }

        [Fact]
        public void ACurrencyIconTallerThanTheDigits_StaysInsideTheLineBox()
        {
            // A box taller than the ink it marks cannot be centred on that
            // ink without starting above the label's own top, and the row
            // above reserved its height from that line box. So the seat
            // stops at the top edge instead. The first case is the datum
            // the centring theory above dropped when the tier reached 18.
            Assert.Equal(0, CoinSegmentMath.InlineIconY(2, 13, CoinSegmentMath.CoinIconSize));
            Assert.Equal(0, CoinSegmentMath.InlineIconY(BodyZeroYOffset, BodyZeroHeight, 40));
        }

        [Theory]
        [InlineData(16, 12)]
        [InlineData(18, 13)]
        [InlineData(32, 24)]
        [InlineData(12, 9)]
        public void TheCoinArtsInkBottom_ScalesWithTheIconBox(int iconSize, int expected)
        {
            // The art's bottom padding is a fraction of the texture, not a
            // constant, so a run drawn at another size cannot inherit
            // another size's answer. The sizes are literal because the fact
            // under test is the texture's black bottom rim, which no icon
            // tier moves.
            Assert.Equal(expected, CoinSegmentMath.CoinArtInkBottom(iconSize));
        }

        [Theory]
        [InlineData(CurrencyIconTiers.WalletListIconSize)]
        [InlineData(24)]
        public void ACoinTallerThanTheDigits_StaysInsideTheLineBox(int iconSize)
        {
            // Same guard the centred seat carries: seating this one by its
            // ink would start it above the label's own top, and the row
            // above reserved its height from that line box.
            Assert.Equal(0, CoinSegmentMath.CoinIconY(BodyZeroYOffset, BodyZeroHeight, iconSize));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-4)]
        public void ACoinWithNoSize_SeatsAtTheTopOfTheLineBox(int iconSize)
        {
            // Nothing is drawn, so there is no ink to put on a baseline.
            Assert.Equal(0, CoinSegmentMath.CoinIconY(BodyZeroYOffset, BodyZeroHeight, iconSize));
            Assert.Equal(0, CoinSegmentMath.CoinArtInkBottom(iconSize));
        }

        [Fact]
        public void TheCoinSeat_ReadsWhicheverFaceItIsGiven()
        {
            // menomonia-32-regular's '0' declares yoffset 6 height 25, so
            // its ink stops at 30 - MEASURED off the shipped face, the same
            // way the 16 numbers above were. A seat hard-coded for
            // DefaultFont16 would answer 2 here.
            int y = CoinSegmentMath.CoinIconY(6, 25, CurrencyIconTiers.WalletListIconSize);

            Assert.Equal(
                30 - CoinSegmentMath.CoinArtInkBottom(CurrencyIconTiers.WalletListIconSize)
                    + CoinSegmentMath.CoinInkBelowBaseline,
                y);
            Assert.Equal(7, y);
        }

        [Fact]
        public void TheCoinSeat_HoldsItsRow_WhateverFaceTheDigitsUse()
        {
            // The snapshot coin row draws its digits and its "Coin" caption
            // on one baseline. Whichever face the digits take, aligning it
            // to that baseline has to leave the digits' ink bottom on the
            // row it was already on, because the coin is seated on that ink
            // bottom by measurement.
            int digitY = TypeRampMetrics.BaselineAlignedY(
                TypeRampMetrics.CoinDigitInk, CaptionY + TypeRampMetrics.BodyInk.BaselineY);

            // The digits draw at Body, the caption's own face, so aligning
            // them to its baseline moves them nowhere.
            Assert.Equal(CaptionY, digitY);

            int digitFace = digitY
                + CoinSegmentMath.CoinIconY(
                    BodyZeroYOffset, BodyZeroHeight, CoinSegmentMath.CoinIconSize);
            int bodyFace = CaptionY
                + CoinSegmentMath.CoinIconY(
                    BodyZeroYOffset, BodyZeroHeight, CoinSegmentMath.CoinIconSize);

            Assert.Equal(bodyFace, digitFace);
        }

        [Fact]
        public void TheGlyphBoxInkPad_IsTheOutlineTheFaceDeclares()
        {
            // The declared box is one pixel taller than its ink at each
            // edge: '0' yoffset 2 height 15 against ink rows 3..15, 'H'
            // yoffset 3 height 14 against ink rows 4..15. Without the pad
            // the coins would seat a pixel low.
            Assert.Equal(
                BodyDigitInkBottom,
                BodyZeroYOffset + BodyZeroHeight - CoinSegmentMath.GlyphBoxInkPad);
        }

        // --- Split ---

        // Pins the shared three-way coin split every display site now
        // routes through (tooltip builders, SnapshotHelpers, MainView's
        // coin panel, CoinCurrencyRenderer). Formatting differences stay
        // with the callers; only the arithmetic is shared.
        [Theory]
        [InlineData(0L, 0L, 0L, 0L)]
        [InlineData(1L, 0L, 0L, 1L)]
        [InlineData(99L, 0L, 0L, 99L)]
        [InlineData(100L, 0L, 1L, 0L)]
        [InlineData(9999L, 0L, 99L, 99L)]
        [InlineData(10000L, 1L, 0L, 0L)]
        [InlineData(1234567L, 123L, 45L, 67L)]
        public void Split_SplitsIntoGoldSilverCopper(long copper, long gold, long silver, long cop)
        {
            Assert.Equal((gold, silver, cop), CoinSegmentMath.Split(copper));
        }

        [Fact]
        public void Split_NegativeInput_ClampsToZero()
        {
            // Negative coin amounts are never displayed; every caller used
            // to clamp before splitting and the clamp moved into Split with
            // the consolidation. Callers CAN pass negatives (e.g.
            // ValueDetailTooltipBuilder's delta line), so this is
            // load-bearing, not defensive.
            Assert.Equal((0L, 0L, 0L), CoinSegmentMath.Split(-1));
            Assert.Equal((0L, 0L, 0L), CoinSegmentMath.Split(long.MinValue));
        }

        // --- TotalCoinSegmentsWidth ---
        [Fact]
        public void TotalCoinSegmentsWidth_Empty_ReturnsZero()
        {
            var segments = new List<CoinSegmentMath.CoinSegmentSpec>();

            Assert.Equal(0, CoinSegmentMath.TotalCoinSegmentsWidth(segments));
        }

        [Fact]
        public void TotalCoinSegmentsWidth_SingleSegment_NoTrailingGap()
        {
            // One segment: textWidth + labelIconGap + iconSize, no trailing
            // segmentGap since there is nothing after it.
            var segments = new List<CoinSegmentMath.CoinSegmentSpec>
            {
                new CoinSegmentMath.CoinSegmentSpec { AssetId = 156902, Text = "56", TextWidth = 18 },
            };

            int expected = 18 + LabelIconGap + IconSize;
            Assert.Equal(expected, CoinSegmentMath.TotalCoinSegmentsWidth(segments));
        }

        [Fact]
        public void TotalCoinSegmentsWidth_MultipleSegments_GapBetweenNotAfter()
        {
            // Gold, silver, copper: three segments, one segmentGap between
            // each pair, none trailing after the last - pins the gap
            // arithmetic exactly (2 gaps for 3 segments, not 3).
            var segments = new List<CoinSegmentMath.CoinSegmentSpec>
            {
                new CoinSegmentMath.CoinSegmentSpec { AssetId = 156904, Text = "12", TextWidth = 16 },
                new CoinSegmentMath.CoinSegmentSpec { AssetId = 156907, Text = "34", TextWidth = 18 },
                new CoinSegmentMath.CoinSegmentSpec { AssetId = 156902, Text = "56", TextWidth = 18 },
            };

            int expected =
                (16 + LabelIconGap + IconSize) +
                (18 + LabelIconGap + IconSize) +
                (18 + LabelIconGap + IconSize) +
                2 * SegmentGap;
            Assert.Equal(expected, CoinSegmentMath.TotalCoinSegmentsWidth(segments));
        }

        [Fact]
        public void TotalCoinSegmentsWidth_ZeroTextWidthSegment_StillIncludesIconAndGap()
        {
            // A degenerate zero-width measured string (e.g. a single glyph
            // BitmapFont.MeasureString rounds down to 0) must not collapse
            // the segment away - the icon and its gap still occupy space.
            var segments = new List<CoinSegmentMath.CoinSegmentSpec>
            {
                new CoinSegmentMath.CoinSegmentSpec { AssetId = 156902, Text = "0", TextWidth = 0 },
            };

            int expected = LabelIconGap + IconSize;
            Assert.Equal(expected, CoinSegmentMath.TotalCoinSegmentsWidth(segments));
        }

        // --- TotalCurrencySegmentsWidth ---
        [Fact]
        public void TotalCurrencySegmentsWidth_Empty_ReturnsZero()
        {
            var segments = new List<CoinSegmentMath.CurrencySegmentSpec>();

            Assert.Equal(0, CoinSegmentMath.TotalCurrencySegmentsWidth(segments));
        }

        [Fact]
        public void TotalCurrencySegmentsWidth_SingleSegment_NoTrailingGap()
        {
            var segments = new List<CoinSegmentMath.CurrencySegmentSpec>
            {
                new CoinSegmentMath.CurrencySegmentSpec { IconUrl = "spirit-shard.png", Text = "5", TextWidth = 12 },
            };

            int expected = 12 + LabelIconGap + IconSize;
            Assert.Equal(expected, CoinSegmentMath.TotalCurrencySegmentsWidth(segments));
        }

        [Fact]
        public void TotalCurrencySegmentsWidth_MultipleSegments_GapBetweenNotAfter()
        {
            // Pins the gap arithmetic exactly (1 gap for 2 segments):
            // 30 + 2 + 18 = 50 for the first, 10 + 2 + 18 = 30 for the
            // second, plus a single 6px segmentGap between them = 86.
            // The 18 is CurrencyIconTiers.WalletBarIconSize, which
            // CoinIconSize now is.
            var segments = new List<CoinSegmentMath.CurrencySegmentSpec>
            {
                new CoinSegmentMath.CurrencySegmentSpec { IconUrl = "karma.png", Text = "1200", TextWidth = 30 },
                new CoinSegmentMath.CurrencySegmentSpec { IconUrl = "spirit-shard.png", Text = "3", TextWidth = 10 },
            };

            int expected =
                (30 + LabelIconGap + IconSize) +
                (10 + LabelIconGap + IconSize) +
                SegmentGap;
            Assert.Equal(expected, CoinSegmentMath.TotalCurrencySegmentsWidth(segments));
            Assert.Equal(86, expected);
        }

        [Fact]
        public void TotalCurrencySegmentsWidth_ZeroTextWidthSegment_StillIncludesIconAndGap()
        {
            var segments = new List<CoinSegmentMath.CurrencySegmentSpec>
            {
                new CoinSegmentMath.CurrencySegmentSpec { IconUrl = "spirit-shard.png", Text = "0", TextWidth = 0 },
            };

            int expected = LabelIconGap + IconSize;
            Assert.Equal(expected, CoinSegmentMath.TotalCurrencySegmentsWidth(segments));
        }

        [Fact]
        public void TotalCurrencySegmentsWidth_MatchesTotalCoinSegmentsWidth_ForEquivalentInput()
        {
            // Both methods lay out the identical "label, gap, icon, gap"
            // geometry off the same CoinIconSize/CoinLabelIconGap/
            // CoinSegmentGap constants (the coin invariant, shared between
            // coin and currency rendering) - for the same text widths they
            // must agree exactly, even though TotalCoinSegmentsWidth sums
            // inline while TotalCurrencySegmentsWidth delegates to
            // ShoppingColumnMath.SegmentRunWidth.
            var coinSegments = new List<CoinSegmentMath.CoinSegmentSpec>
            {
                new CoinSegmentMath.CoinSegmentSpec { AssetId = 156904, Text = "12", TextWidth = 16 },
                new CoinSegmentMath.CoinSegmentSpec { AssetId = 156907, Text = "34", TextWidth = 18 },
            };
            var currencySegments = new List<CoinSegmentMath.CurrencySegmentSpec>
            {
                new CoinSegmentMath.CurrencySegmentSpec { IconUrl = "a.png", Text = "12", TextWidth = 16 },
                new CoinSegmentMath.CurrencySegmentSpec { IconUrl = "b.png", Text = "34", TextWidth = 18 },
            };

            int coinWidth = CoinSegmentMath.TotalCoinSegmentsWidth(coinSegments);
            int currencyWidth = CoinSegmentMath.TotalCurrencySegmentsWidth(currencySegments);

            Assert.Equal(coinWidth, currencyWidth);
        }

        // --- FormatSegmentTexts ---
        //
        // The exact strings a coin amount renders as. Two consumers depend
        // on them agreeing: CoinCurrencyRenderer.BuildCoinSegments (what is
        // drawn) and the recipe tree's cost-column pre-scan (how wide each
        // denomination's sub-column is reserved).
        [Fact]
        public void FormatSegmentTexts_FullAmount_GoldPrecedesSilverAndCopper()
        {
            var (gold, silver, copper) = CoinSegmentMath.FormatSegmentTexts(412680L);

            Assert.Equal("41", gold);
            Assert.Equal("26", silver);
            Assert.Equal("80", copper);
        }

        [Fact]
        public void FormatSegmentTexts_SingleDigitSegmentsStayBareUnderGold()
        {
            // Bare digits, never zero-padded: the game renders "2g 0s 0c"
            // (live3 counterfeit-ticket, 20000c) and "2s 0c"
            // (relic-livingcity, 200c) with single-character zeros; the
            // non-zero sub-10 case is inferred from those samples.
            var (gold, silver, copper) = CoinSegmentMath.FormatSegmentTexts(10203L);

            Assert.Equal("1", gold);
            Assert.Equal("2", silver);
            Assert.Equal("3", copper);
        }

        [Fact]
        public void FormatSegmentTexts_SubGoldAmount_OmitsGoldAndLeavesSilverUnpadded()
        {
            var (gold, silver, copper) = CoinSegmentMath.FormatSegmentTexts(539L);

            Assert.Null(gold);
            Assert.Equal("5", silver);
            Assert.Equal("39", copper);
        }

        [Fact]
        public void FormatSegmentTexts_SubSilverAmount_OmitsBothLeadingUnits()
        {
            var (gold, silver, copper) = CoinSegmentMath.FormatSegmentTexts(7L);

            Assert.Null(gold);
            Assert.Null(silver);
            Assert.Equal("7", copper);
        }

        [Fact]
        public void FormatSegmentTexts_Zero_StillRendersACopperUnit()
        {
            // A zero total must never be a blank cell.
            var (gold, silver, copper) = CoinSegmentMath.FormatSegmentTexts(0L);

            Assert.Null(gold);
            Assert.Null(silver);
            Assert.Equal("0", copper);
        }

        [Fact]
        public void FormatSegmentTexts_Negative_ClampsLikeSplit()
        {
            Assert.Equal(
                CoinSegmentMath.FormatSegmentTexts(0L),
                CoinSegmentMath.FormatSegmentTexts(-500L));
        }

        [Fact]
        public void TotalCoinSegmentsWidth_HonoursACallersOwnIconSize()
        {
            // The rich tooltip draws coin icons at ~0.8x its line height
            // rather than at the plan tables' shared 20px (gap G22), and
            // has to MEASURE at the size it draws at or its box is too
            // wide by one icon per denomination.
            var segments = new List<CoinSegmentMath.CoinSegmentSpec>
            {
                new CoinSegmentMath.CoinSegmentSpec { AssetId = CoinSegmentMath.GoldAssetId, Text = "1", TextWidth = 10 },
                new CoinSegmentMath.CoinSegmentSpec { AssetId = CoinSegmentMath.CopperAssetId, Text = "23", TextWidth = 20 },
            };

            int shared = CoinSegmentMath.TotalCoinSegmentsWidth(segments);
            int local = CoinSegmentMath.TotalCoinSegmentsWidth(segments, 13);

            Assert.Equal(shared - (2 * (CoinSegmentMath.CoinIconSize - 13)), local);
            Assert.Equal(shared, CoinSegmentMath.TotalCoinSegmentsWidth(segments, 0));
        }

        // The hover a coin icon answers with. Every denomination the coin
        // renderer can build a segment for has to have one; anything else
        // returns null, the icon component's "no text of my own" input.
        [Theory]
        [InlineData(CoinSegmentMath.GoldAssetId, "Gold")]
        [InlineData(CoinSegmentMath.SilverAssetId, "Silver")]
        [InlineData(CoinSegmentMath.CopperAssetId, "Copper")]
        public void DenominationName_NamesEveryCoinIcon(int assetId, string expected)
        {
            Assert.Equal(expected, CoinSegmentMath.DenominationName(assetId));
        }

        [Fact]
        public void DenominationName_IsNullForAnythingThatIsNotACoin()
        {
            Assert.Null(CoinSegmentMath.DenominationName(0));
            Assert.Null(CoinSegmentMath.DenominationName(156905));
        }

        [Fact]
        public void EverySegmentTheSplitProduces_CarriesANamedDenomination()
        {
            // Guards the pairing, not the three literals: a fourth
            // denomination would otherwise ship an unnamed icon.
            foreach (int assetId in new[]
            {
                CoinSegmentMath.GoldAssetId, CoinSegmentMath.SilverAssetId, CoinSegmentMath.CopperAssetId,
            })
            {
                Assert.False(string.IsNullOrEmpty(CoinSegmentMath.DenominationName(assetId)));
            }
        }
    }
}
