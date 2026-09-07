using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // SummarySectionLayoutMath is the
    // redesigned Summary section's own layout arithmetic, deliberately kept
    // separate from PlanContentHeightMath/PlanRelayoutMath (both DO-NOT-
    // TOUCH for this package) - see that class's own doc comment.
    public class SummarySectionLayoutMathTests
    {
        private static PlanRowViewModel Row(PlanRowType type)
        {
            return new PlanRowViewModel { RowType = type };
        }

        private static PlanRowViewModel BarterRow()
        {
            return new PlanRowViewModel
            {
                RowType = PlanRowType.CurrencyCost,
                IsBarterItemCost = true,
            };
        }

        // --- BodyHeight ---
        [Fact]
        public void BodyHeight_Null_ReturnsZero()
        {
            Assert.Equal(0, SummarySectionLayoutMath.BodyHeight(null));
        }

        [Fact]
        public void BodyHeight_Empty_ReturnsZero()
        {
            Assert.Equal(0, SummarySectionLayoutMath.BodyHeight(new List<PlanRowViewModel>()));
        }

        [Fact]
        public void BodyHeight_CollapsedCostBandPlusFootnote()
        {
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile), // collapsed - still just ONE tile ROW
                Row(PlanRowType.SummaryFootnote),
            };

            int expected = SummarySectionLayoutMath.CostBandHeight(false) + PlanContentHeightMath.FallbackTextRowHeight;
            Assert.Equal(expected, SummarySectionLayoutMath.BodyHeight(rows));
        }

        [Fact]
        public void BodyHeight_ExpandedCostBand_StillOneTileRowRegardlessOfTileCount()
        {
            // 3 CostFormulaTile rows (the uncollapsed band) render as ONE
            // CostTileRowHeight-tall row of 3 tiles, not 3 separate rows.
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.SummaryFootnote),
            };

            int expected = SummarySectionLayoutMath.CostBandHeight(false) + PlanContentHeightMath.FallbackTextRowHeight;
            Assert.Equal(expected, SummarySectionLayoutMath.BodyHeight(rows));
        }

        [Fact]
        public void BodyHeight_BothBandsPresent_TwoSeparateTileRows()
        {
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.SummaryFootnote),
            };

            int expected = SummarySectionLayoutMath.CostBandHeight(false)
                + PlanContentHeightMath.CostTileRowHeight
                + PlanContentHeightMath.FallbackTextRowHeight;
            Assert.Equal(expected, SummarySectionLayoutMath.BodyHeight(rows));
        }

        [Fact]
        public void BodyHeight_CurrencyRows_HeaderPlusGroupHeadingPlusOnePerRow()
        {
            // Three wallet currencies are ONE group, so the table costs one
            // group heading however many rows sit under it.
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.SummaryFootnote),
            };

            int expected = SummarySectionLayoutMath.CostBandHeight(true)
                + SummarySectionLayoutMath.CurrencyTableTopGap
                + PlanContentHeightMath.ColumnHeaderRowHeight
                + SummarySectionLayoutMath.NonCoinGroupHeadingHeight
                + 3 * PlanContentHeightMath.CurrencyRowHeight
                + PlanContentHeightMath.FallbackTextRowHeight;
            Assert.Equal(expected, SummarySectionLayoutMath.BodyHeight(rows));
        }

        [Fact]
        public void BodyHeight_BothKinds_ReservesOneHeadingPerGroup()
        {
            // The same two rows, one of each kind: two groups, so two
            // headings - the height a one-group table would have plus one
            // more heading.
            var oneGroup = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.CurrencyCost),
            };
            var twoGroups = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CurrencyCost),
                BarterRow(),
            };

            Assert.Equal(
                SummarySectionLayoutMath.BodyHeight(oneGroup)
                    + SummarySectionLayoutMath.NonCoinGroupHeadingHeight,
                SummarySectionLayoutMath.BodyHeight(twoGroups));
        }

        [Fact]
        public void BodyHeight_NoCurrencyRows_NoHeaderHeightCounted()
        {
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.SummaryFootnote),
            };

            int expected = SummarySectionLayoutMath.CostBandHeight(false) + PlanContentHeightMath.FallbackTextRowHeight;
            Assert.Equal(expected, SummarySectionLayoutMath.BodyHeight(rows));
        }

        [Fact]
        public void BodyHeight_MultiItemNoteRow_AddsOneFallbackTextRow()
        {
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.MultiItemNote),
                Row(PlanRowType.SummaryFootnote),
            };

            int expected = SummarySectionLayoutMath.CostBandHeight(false)
                + PlanContentHeightMath.CostTileRowHeight
                + 2 * PlanContentHeightMath.FallbackTextRowHeight;
            Assert.Equal(expected, SummarySectionLayoutMath.BodyHeight(rows));
        }

        [Fact]
        public void BodyHeight_FullSection_EveryElementPresent()
        {
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.MultiItemNote),
                Row(PlanRowType.SummaryFootnote),
            };

            int expected = SummarySectionLayoutMath.CostBandHeight(true)
                + PlanContentHeightMath.CostTileRowHeight
                + SummarySectionLayoutMath.CurrencyTableTopGap
                + PlanContentHeightMath.ColumnHeaderRowHeight
                + SummarySectionLayoutMath.NonCoinGroupHeadingHeight
                + 2 * PlanContentHeightMath.CurrencyRowHeight
                + 2 * PlanContentHeightMath.FallbackTextRowHeight;
            Assert.Equal(expected, SummarySectionLayoutMath.BodyHeight(rows));
        }

        // --- CostBandHeight + the currency disclosure line ---
        //
        // Re-baselined for the cost-band restyle: the result tile no
        // longer carries a promoted display-font amount (which is what
        // made the band 76 tall), it carries a highlight box at the band's
        // one shared amount font, so the band's height is now the box's
        // margin+padding around a caption line, an optional disclosure
        // line and one coin run. When the plan has currency costs the band
        // still grows by exactly that disclosure line. Both numbers are
        // pinned absolutely here (not recomputed from the same constants
        // the production formula reads, which could never fail): a
        // deliberate geometry change re-baselines these literals.
        [Fact]
        public void CostBandHeight_NoCurrencyNote_IsTheBoxedCaptionPlusAmountBand()
        {
            // 6 margin + 6 pad + 29 caption line + 8 label-to-value gap
            // + 20 amount run + 6 pad + 6 margin. The amount run is 20
            // because the amount TEXT is 20; it read as "the coin icon" only
            // while inline coins also drew at 20, and stayed 20 at the 18px
            // wallet BAR tier.
            Assert.Equal(81, SummarySectionLayoutMath.CostBandHeight(false));
        }

        [Fact]
        public void CostBandHeight_WithCurrencyNote_AddsExactlyOneNoteLine()
        {
            Assert.Equal(81 + 23, SummarySectionLayoutMath.CostBandHeight(true));
            Assert.Equal(
                SummarySectionLayoutMath.CostBandHeight(false) + SummarySectionLayoutMath.CostBandCurrencyNoteHeight,
                SummarySectionLayoutMath.CostBandHeight(true));
        }

        // --- The label-to-value gap (defect: "'Sell Value' and the gold
        // line are a little cramped") ---
        [Fact]
        public void LabelToValueGap_IsOneConstantSharedByBothBands()
        {
            // The whole point of the constant: the cost band and the profit
            // band read the SAME number, so no future edit can leave one
            // breathing and the other cramped.
            Assert.Equal(
                PlanContentHeightMath.CostTileLabelToValueGap,
                SummarySectionLayoutMath.CostBandCaptionToAmountGap);
        }

        [Fact]
        public void LabelToValueGap_IsSizedFromTheCaptionTiersOwnMetrics()
        {
            // Derived, not eyeballed: a label and its value read as one
            // group while the space between them stays under about one cap
            // height, and read as touching well below half of it. The gap
            // is the 4pt-scale step at half the caption tier's cap height.
            var caption = TypeRampMetrics.ColumnHeaderInk;
            int gap = PlanContentHeightMath.CostTileLabelToValueGap;

            // Half the tier's cap height, snapped down to the 4pt scale.
            Assert.Equal(caption.CapHeight / 2 / 4 * 4, gap);

            // And it must actually clear the caption's descenders, which
            // hang one pixel past its own line box at this tier.
            Assert.True(
                gap > caption.LowestInk - caption.LineHeight,
                $"a {gap}px gap does not clear a {caption.LowestInk - caption.LineHeight}px descender overhang");
        }

        [Fact]
        public void BandAmountY_HangsTheAmountTheGapUnderTheCaption_WhateverTheBandIs()
        {
            // Both bands call this with their own measured caption bottom
            // and get the same distance - it takes no row height and no
            // bottom pad, which is exactly what made the two drift apart
            // when each bottom-anchored inside its own fixed row.
            Assert.Equal(
                31 + PlanContentHeightMath.CostTileLabelToValueGap,
                SummarySectionLayoutMath.BandAmountY(31));
            Assert.Equal(
                12 + PlanContentHeightMath.CostTileLabelToValueGap,
                SummarySectionLayoutMath.BandAmountY(12));
        }

        // --- The disclosure line moved BELOW the amount (defect: "the dead
        // space between 'Total Materials Value' and the currency data below
        // it looks odd") ---
        [Fact]
        public void BandNoteY_HangsTheDisclosureUnderTheAmount_NotAboveIt()
        {
            int amountY = SummarySectionLayoutMath.BandAmountY(37);
            int noteY = SummarySectionLayoutMath.BandNoteY(amountY, PlanContentHeightMath.AmountRunHeight);

            Assert.Equal(
                amountY + PlanContentHeightMath.AmountRunHeight + SummarySectionLayoutMath.CostBandAmountToNoteGap,
                noteY);
            Assert.True(noteY > amountY + PlanContentHeightMath.AmountRunHeight);
        }

        [Fact]
        public void CurrencyNote_DoesNotMoveTheAmountRun_SoAllThreeTilesShareOneCoinLine()
        {
            // The defect this replaced: the note was counted between the
            // caption and a BOTTOM-anchored amount, so a currency-bearing
            // plan pushed every tile's coin run down by the note's height
            // while only the result tile had anything in the space it left.
            // BandAmountY now has no note term to pass - the structural
            // guarantee - and the band grows DOWNWARD for the note instead.
            int amountBottom = SummarySectionLayoutMath.BandAmountY(
                SummarySectionLayoutMath.CostBandCaptionY + TypeRampMetrics.ColumnHeaderInk.LineHeight)
                + PlanContentHeightMath.AmountRunHeight;

            Assert.True(
                amountBottom + SummarySectionLayoutMath.CostBandAmountBottomPad
                    <= SummarySectionLayoutMath.CostBandHeight(false),
                "the note-free band must already hold the amount run at the shared y");
            Assert.True(
                amountBottom + SummarySectionLayoutMath.CostBandCurrencyNoteHeight
                    + SummarySectionLayoutMath.CostBandAmountBottomPad
                    <= SummarySectionLayoutMath.CostBandHeight(true),
                "the note-bearing band holds the same run PLUS the footnote under it");
            Assert.Equal(
                SummarySectionLayoutMath.CostBandCurrencyNoteHeight,
                SummarySectionLayoutMath.CostBandHeight(true) - SummarySectionLayoutMath.CostBandHeight(false));
        }

        [Fact]
        public void CostBandBoxHeight_MeasuredOffTheNote_EnclosesIt()
        {
            // Blish clips a container's children, so a box measured off the
            // amount alone would crop the footnote hanging under it.
            int amountY = SummarySectionLayoutMath.BandAmountY(37);
            int amountBottom = amountY + PlanContentHeightMath.AmountRunHeight;
            int noteBottom = SummarySectionLayoutMath.BandNoteY(amountY, PlanContentHeightMath.AmountRunHeight)
                + TypeRampMetrics.CaptionInk.LineHeight;

            int amountOnlyBox = SummarySectionLayoutMath.CostBandBoxHeight(amountBottom);
            int withNoteBox = SummarySectionLayoutMath.CostBandBoxHeight(noteBottom);

            Assert.True(withNoteBox > amountOnlyBox);
            Assert.Equal(noteBottom - amountBottom, withNoteBox - amountOnlyBox);
        }

        // --- The section-separation gap above the currency table (defect:
        // "there isn't enough padding or open space between the bottom of
        // the Total cost section before the currency table's header row") ---
        [Fact]
        public void CurrencyTableTopGap_SeparatesLessThanAWholeSectionBoundary()
        {
            // On the 4pt scale, and deliberately one step under the 16px
            // CraftingPlanView.SectionSpacing puts between two SECTIONS:
            // this is a boundary inside one, so it must read as the lesser
            // of the two. (16 is aliased here rather than referenced - a
            // Blish-free Services test may not reach into a view.)
            const int sectionSpacing = 16;

            Assert.Equal(0, SummarySectionLayoutMath.CurrencyTableTopGap % 4);
            Assert.InRange(SummarySectionLayoutMath.CurrencyTableTopGap, 8, sectionSpacing - 4);
        }

        [Fact]
        public void BodyHeight_CurrencyTable_ReservesItsSeparationGapExactlyOnce()
        {
            var oneRow = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CurrencyCost),
            };
            var threeRows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.CurrencyCost),
            };

            // The gap is a property of the BOUNDARY, not of the rows: two
            // more currency rows add two row heights and nothing else.
            Assert.Equal(
                2 * PlanContentHeightMath.CurrencyRowHeight,
                SummarySectionLayoutMath.BodyHeight(threeRows) - SummarySectionLayoutMath.BodyHeight(oneRow));

            Assert.Equal(
                SummarySectionLayoutMath.CostBandHeight(true)
                    + SummarySectionLayoutMath.CurrencyTableTopGap
                    + PlanContentHeightMath.ColumnHeaderRowHeight
                    + SummarySectionLayoutMath.NonCoinGroupHeadingHeight
                    + PlanContentHeightMath.CurrencyRowHeight,
                SummarySectionLayoutMath.BodyHeight(oneRow));
        }

        [Fact]
        public void CostBandCaptionReserve_CoversTheCaptionTierItActuallyDraws()
        {
            // The reserve is deliberately above the real metric: the
            // renderer places the caption from live font metrics and clamps
            // the amount below it, so a reserve under the real line height
            // makes the band clip its own amount (its DEBUG assert is what
            // catches that at runtime - this catches it at build time).
            Assert.True(
                TypeRampMetrics.ColumnHeaderInk.LineHeight
                    <= SummarySectionLayoutMath.CostBandCaptionLineHeight,
                $"caption line box {TypeRampMetrics.ColumnHeaderInk.LineHeight} exceeds the "
                    + $"{SummarySectionLayoutMath.CostBandCaptionLineHeight}px reserve");

            // The disclosure line under the AMOUNT stays Caption, and its
            // own reserve is that tier's lowest ink plus the gap above it,
            // so a descender on "+ 2 Currencies Required" lands inside the
            // band rather than on the row under it.
            Assert.Equal(
                SummarySectionLayoutMath.CostBandCurrencyNoteHeight,
                SummarySectionLayoutMath.CostBandAmountToNoteGap + TypeRampMetrics.CaptionInk.LowestInk);
        }

        [Fact]
        public void CostBandHeight_IsTallerThanTheProfitBand_ByTheHighlightBoxsOwnRoom()
        {
            // The whole reason the cost band is still the taller of the
            // two: its result tile is boxed and the box needs its own
            // margin and padding, top and bottom. Nothing about the amount
            // font differs between the bands any more, and since the
            // label-to-value gap became one shared constant, nothing about
            // their internal spacing does either - the two bands now differ
            // by exactly the box's own room.
            Assert.True(SummarySectionLayoutMath.CostBandHeight(false) > PlanContentHeightMath.CostTileRowHeight);
            Assert.Equal(
                2 * (SummarySectionLayoutMath.CostBandBoxMarginY + SummarySectionLayoutMath.CostBandBoxPadY),
                SummarySectionLayoutMath.CostBandHeight(false)
                    - (SummarySectionLayoutMath.CostBandCaptionLineHeight
                        + SummarySectionLayoutMath.CostBandCaptionToAmountGap
                        + PlanContentHeightMath.AmountRunHeight));

            Assert.Equal(
                2 * SummarySectionLayoutMath.CostBandBoxMarginY
                    + 2 * SummarySectionLayoutMath.CostBandBoxPadY
                    - PlanContentHeightMath.CostTileCaptionY
                    - PlanContentHeightMath.CostTileAmountBottomPad,
                SummarySectionLayoutMath.CostBandHeight(false) - PlanContentHeightMath.CostTileRowHeight);
        }

        [Fact]
        public void CostBandHeight_LeavesTheHighlightBoxInsideTheBand()
        {
            // The geometry the renderer actually builds, through the same
            // functions it calls: BandAmountY hangs the amount under the
            // measured caption, BandNoteY hangs the footnote under that,
            // the box spans CostBandBoxTop to one pad below the lowest of
            // them, and that bottom edge - the band's lowest ink - must sit
            // inside the height the math reserved. The renderer's DEBUG
            // assert fails loud otherwise, but only at runtime and only in
            // DEBUG.
            //
            // captionBlockBottom is a MEASURED input (the caption font's
            // real metrics), so it is swept from the tightest plausible
            // value up to the full reserve rather than assumed. The reserve
            // is what the band's own height was sized from, so a caption
            // anywhere inside it must still leave the box enclosed.
            foreach (bool hasNote in new[] { false, true })
            {
                int rowHeight = SummarySectionLayoutMath.CostBandHeight(hasNote);
                int reserve = SummarySectionLayoutMath.CostBandCaptionY
                    + SummarySectionLayoutMath.CostBandCaptionLineHeight;

                Assert.True(SummarySectionLayoutMath.CostBandBoxTop >= 0);

                for (int captionBlockBottom = SummarySectionLayoutMath.CostBandCaptionY;
                    captionBlockBottom <= reserve;
                    captionBlockBottom++)
                {
                    int amountY = SummarySectionLayoutMath.BandAmountY(captionBlockBottom);
                    int contentBottom = amountY + PlanContentHeightMath.AmountRunHeight;
                    if (hasNote)
                    {
                        contentBottom = SummarySectionLayoutMath.BandNoteY(
                            amountY, PlanContentHeightMath.AmountRunHeight)
                            + TypeRampMetrics.CaptionInk.LineHeight;
                    }

                    int boxHeight = SummarySectionLayoutMath.CostBandBoxHeight(contentBottom);

                    // The caption block must clear the amount run rather
                    // than be overprinted by it.
                    Assert.True(amountY >= captionBlockBottom);
                    Assert.True(
                        SummarySectionLayoutMath.CostBandBoxTop + boxHeight <= rowHeight,
                        $"box overflows a {rowHeight}px band (hasNote={hasNote}, "
                            + $"captionBlockBottom={captionBlockBottom})");
                }
            }
        }

        [Fact]
        public void CostBandBoxWidth_AddsExactlyOnePadEachSide_AndFitsItsTileSlice()
        {
            // The box is never clamped to its tile slice (Blish clips a
            // container's children, so a narrow box would cut the amount
            // off), which makes the padding its only margin for error.
            // These are the band's own arguments to ComputeCostTileGeometry
            // - see Views/Rendering/SummarySectionRenderer.CreateFormulaBand.
            const int totalMargin = 40;
            const int minTileWidth = 80;

            // Deliberately narrower than any band the module can now
            // present: the plan panel at the 1436px window minimum is
            // 1310px wide (see PlanRelayoutMathTests' chrome derivation),
            // so 860 leaves the three-tile arithmetic tested with ~450px in
            // hand rather than at the boundary.
            var geometry = PlanRelayoutMath.ComputeCostTileGeometry(860, 3, totalMargin, minTileWidth);

            Assert.Equal(
                2 * SummarySectionLayoutMath.CostBandBoxPadX,
                SummarySectionLayoutMath.CostBandBoxWidth(0));

            // The widest run a real result tile carries is the disclosure
            // line ("+ N Currencies Required") at the caption font, which
            // measures well under 160px; the box must clear that with both
            // pads even in the narrowest tile slice.
            Assert.True(SummarySectionLayoutMath.CostBandBoxWidth(160) <= geometry.TileWidth);

            // And the boundary itself, so a later pad bump is caught.
            int widestThatFits = geometry.TileWidth - 2 * SummarySectionLayoutMath.CostBandBoxPadX;
            Assert.True(SummarySectionLayoutMath.CostBandBoxWidth(widestThatFits) <= geometry.TileWidth);
            Assert.True(SummarySectionLayoutMath.CostBandBoxWidth(widestThatFits + 1) > geometry.TileWidth);
        }

        [Fact]
        public void BodyHeight_CurrencyRowsPresent_CostBandGrowsByTheNoteLine()
        {
            // The exact coupling the disclosure line depends on: the same
            // "at least one CurrencyCost row" condition that makes the
            // renderer draw the line must make BodyHeight reserve room for
            // it, or the section body clips its own headline figure.
            var withoutCurrency = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.SummaryFootnote),
            };
            var withCurrency = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CurrencyCost),
                Row(PlanRowType.SummaryFootnote),
            };

            int currencyTableHeight =
                SummarySectionLayoutMath.CurrencyTableTopGap
                + PlanContentHeightMath.ColumnHeaderRowHeight
                + SummarySectionLayoutMath.NonCoinGroupHeadingHeight
                + PlanContentHeightMath.CurrencyRowHeight;

            Assert.Equal(
                SummarySectionLayoutMath.BodyHeight(withoutCurrency)
                    + currencyTableHeight
                    + SummarySectionLayoutMath.CostBandCurrencyNoteHeight,
                SummarySectionLayoutMath.BodyHeight(withCurrency));
        }

        [Fact]
        public void CurrencyRequirementNote_NoCurrencies_IsNull()
        {
            Assert.Null(SummarySectionLayoutMath.CurrencyRequirementNote(null));
            Assert.Null(SummarySectionLayoutMath.CurrencyRequirementNote(new List<PlanRowViewModel>()));
        }

        [Fact]
        public void CurrencyRequirementNote_OneCurrency_ReadsSingular()
        {
            Assert.Equal("+ 1 Currency Required",
                SummarySectionLayoutMath.CurrencyRequirementNote(NonCoinRows(1, 0)));
        }

        [Fact]
        public void CurrencyRequirementNote_ManyCurrencies_StatesTheCount()
        {
            Assert.Equal("+ 3 Currencies Required",
                SummarySectionLayoutMath.CurrencyRequirementNote(NonCoinRows(3, 0)));
        }

        // A barter item is not a currency, and the line that counts it
        // must not call it one - the whole reason this took a row list.
        [Fact]
        public void CurrencyRequirementNote_BarterItemsOnly_ReadsAsItems()
        {
            Assert.Equal("+ 1 Item Required",
                SummarySectionLayoutMath.CurrencyRequirementNote(NonCoinRows(0, 1)));
            Assert.Equal("+ 4 Items Required",
                SummarySectionLayoutMath.CurrencyRequirementNote(NonCoinRows(0, 4)));
        }

        [Fact]
        public void CurrencyRequirementNote_BothKinds_CountsThemTogether()
        {
            Assert.Equal("+ 5 Currencies and Items Required",
                SummarySectionLayoutMath.CurrencyRequirementNote(NonCoinRows(3, 2)));
        }

        // --- GroupNonCoinRows ---
        [Fact]
        public void GroupNonCoinRows_WalletCurrenciesLeadTheBarterItems()
        {
            var rows = new List<PlanRowViewModel>
            {
                new PlanRowViewModel
                {
                    RowType = PlanRowType.CurrencyCost, IsBarterItemCost = true, Label = "Token",
                },
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, Label = "Karma" },
            };

            var groups = SummarySectionLayoutMath.GroupNonCoinRows(rows);

            Assert.Equal(2, groups.Count);
            Assert.Equal(SummarySectionLayoutMath.WalletGroupHeading, groups[0].Heading);
            Assert.Equal("Karma", Assert.Single(groups[0].Rows).Label);
            Assert.Equal(SummarySectionLayoutMath.InventoryGroupHeading, groups[1].Heading);
            Assert.Equal("Token", Assert.Single(groups[1].Rows).Label);
        }

        [Fact]
        public void GroupNonCoinRows_KeepsTheOrderItWasHandedInsideAGroup()
        {
            // The builder sorts once across both kinds; a grouping that did
            // not preserve relative order would cost each group that sort.
            var rows = new List<PlanRowViewModel>
            {
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, Label = "Ascalonian Tears" },
                new PlanRowViewModel
                {
                    RowType = PlanRowType.CurrencyCost, IsBarterItemCost = true, Label = "Ancient Coin",
                },
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, Label = "Spirit Shards" },
                new PlanRowViewModel
                {
                    RowType = PlanRowType.CurrencyCost, IsBarterItemCost = true, Label = "Blue Prophet Shard",
                },
            };

            var groups = SummarySectionLayoutMath.GroupNonCoinRows(rows);

            Assert.Equal(
                new[] { "Ascalonian Tears", "Spirit Shards" },
                groups[0].Rows.Select(r => r.Label).ToArray());
            Assert.Equal(
                new[] { "Ancient Coin", "Blue Prophet Shard" },
                groups[1].Rows.Select(r => r.Label).ToArray());
        }

        [Fact]
        public void GroupNonCoinRows_OneKindOnly_YieldsOnlyThatGroup()
        {
            var walletOnly = SummarySectionLayoutMath.GroupNonCoinRows(NonCoinRows(2, 0));
            Assert.Equal(
                SummarySectionLayoutMath.WalletGroupHeading,
                Assert.Single(walletOnly).Heading);

            var inventoryOnly = SummarySectionLayoutMath.GroupNonCoinRows(NonCoinRows(0, 2));
            Assert.Equal(
                SummarySectionLayoutMath.InventoryGroupHeading,
                Assert.Single(inventoryOnly).Heading);
        }

        [Fact]
        public void GroupNonCoinRows_SameNameEitherSide_StillLandsInItsOwnGroup()
        {
            // A currency and a barter item can resolve to the same display
            // name; the group a row belongs to is its KIND, never its label.
            var rows = new List<PlanRowViewModel>
            {
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, Label = "Spirit Shards" },
                new PlanRowViewModel
                {
                    RowType = PlanRowType.CurrencyCost, IsBarterItemCost = true, Label = "Spirit Shards",
                },
            };

            var groups = SummarySectionLayoutMath.GroupNonCoinRows(rows);

            Assert.Equal(2, groups.Count);
            Assert.False(Assert.Single(groups[0].Rows).IsBarterItemCost);
            Assert.True(Assert.Single(groups[1].Rows).IsBarterItemCost);
        }

        [Fact]
        public void GroupNonCoinRows_IgnoresEveryOtherRowKind()
        {
            // BodyHeight hands this a whole section, footnotes and formula
            // tiles included - none of which is a cost row.
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.ProfitFormulaTile),
                Row(PlanRowType.MultiItemNote),
                Row(PlanRowType.SummaryFootnote),
                null,
            };

            Assert.Empty(SummarySectionLayoutMath.GroupNonCoinRows(rows));
            Assert.Empty(SummarySectionLayoutMath.GroupNonCoinRows(null));
            Assert.Empty(SummarySectionLayoutMath.GroupNonCoinRows(new List<PlanRowViewModel>()));
        }

        [Fact]
        public void NonCoinTableRowsHeight_IsOneHeadingPerGroupPlusOneRowPerCostRow()
        {
            var groups = SummarySectionLayoutMath.GroupNonCoinRows(NonCoinRows(2, 1));

            Assert.Equal(
                2 * SummarySectionLayoutMath.NonCoinGroupHeadingHeight
                    + 3 * PlanContentHeightMath.CurrencyRowHeight,
                SummarySectionLayoutMath.NonCoinTableRowsHeight(groups));

            Assert.Equal(0, SummarySectionLayoutMath.NonCoinTableRowsHeight(null));
            Assert.Equal(
                0,
                SummarySectionLayoutMath.NonCoinTableRowsHeight(
                    SummarySectionLayoutMath.GroupNonCoinRows(null)));
        }

        [Fact]
        public void NonCoinNameHeader_NamesOnlyTheKindsPresent()
        {
            Assert.Equal("Currency", SummarySectionLayoutMath.NonCoinNameHeader(null));
            Assert.Equal("Currency", SummarySectionLayoutMath.NonCoinNameHeader(NonCoinRows(2, 0)));
            Assert.Equal("Item", SummarySectionLayoutMath.NonCoinNameHeader(NonCoinRows(0, 2)));
            Assert.Equal("Currency or Item", SummarySectionLayoutMath.NonCoinNameHeader(NonCoinRows(1, 1)));
        }

        private static List<PlanRowViewModel> NonCoinRows(int currencies, int barterItems)
        {
            var rows = new List<PlanRowViewModel>();
            for (int i = 0; i < currencies; i++)
            {
                rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.CurrencyCost,
                    Label = "Currency " + i,
                });
            }

            for (int i = 0; i < barterItems; i++)
            {
                rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.CurrencyCost,
                    IsBarterItemCost = true,
                    Label = "Token " + i,
                });
            }

            return rows;
        }

        [Fact]
        public void CurrencyRequirementNoteTooltip_ListsEveryCurrencyName()
        {
            var rows = new List<PlanRowViewModel>
            {
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, Label = "Blue Prophet Shard" },
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, Label = "Fractal Relic" },
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, Label = "Spirit Shard" },
            };

            string tooltip = SummarySectionLayoutMath.CurrencyRequirementNoteTooltip(rows);

            Assert.StartsWith("Blue Prophet Shard, Fractal Relic, Spirit Shard", tooltip);
            Assert.Contains("see the table below", tooltip);
        }

        [Fact]
        public void CurrencyRequirementNoteTooltip_NoRowsOrNoNames_IsNull()
        {
            Assert.Null(SummarySectionLayoutMath.CurrencyRequirementNoteTooltip(null));
            Assert.Null(SummarySectionLayoutMath.CurrencyRequirementNoteTooltip(new List<PlanRowViewModel>()));
            Assert.Null(SummarySectionLayoutMath.CurrencyRequirementNoteTooltip(
                new List<PlanRowViewModel> { Row(PlanRowType.CurrencyCost) }));
        }

        // --- ComputeCurrencyColumnEdges ---

        // Expected values below are hard-coded pixel numbers for two panel
        // widths - a conscious re-baseline point. A test that recomputed
        // the formula from the same public constants it was verifying could
        // never fail unless both sides moved together. If a deliberate
        // geometry change moves these, recompute by hand from
        // SummarySectionLayoutMath.cs's CurrencyMarkerWidth/
        // CurrencyColumnGap/CurrencyNameX/CurrencyNameColumnWidth/
        // CurrencyNumberColumnWidth constants (widestNumberWidth defaults
        // to 0) and update the literals here.
        [Fact]
        public void ComputeCurrencyColumnEdges_SpreadsTheColumnsOnEqualGaps()
        {
            // 800: pinned right edge 792, marker 758. From CurrencyNameX 48
            // (8 gutter + the 32px wallet-LIST icon + 8) to the marker is
            // 710px, of which the name's 210 and three 60px number bands
            // reserve 390. The 320 left over is four gaps of 80, so
            // Required ends at 48+210+80+60 = 398, Have at 538, Needed at
            // 678.
            var edges800 = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(800);
            Assert.Equal(398, edges800.RequiredRightEdge);
            Assert.Equal(538, edges800.HaveRightEdge);
            Assert.Equal(678, edges800.NeededRightEdge);
            Assert.Equal(758, edges800.MarkerX);

            // 1200: marker 1158, span 1110, 720 left over, gaps of 180.
            var edges1200 = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200);
            Assert.Equal(498, edges1200.RequiredRightEdge);
            Assert.Equal(738, edges1200.HaveRightEdge);
            Assert.Equal(978, edges1200.NeededRightEdge);
            Assert.Equal(1158, edges1200.MarkerX);
        }

        [Fact]
        public void EveryGapBetweenTwoColumns_IsTheSameWidth()
        {
            // The law this table lays out by: each column reserves what its
            // own widest cell needs and the rest of the row is split into
            // equal gaps. Checked with a Note column present, which is the
            // case that used to leave the note one minimum gap from Status
            // while the number columns held hundreds of pixels of padding.
            // The widths this table actually measures: "Required" at the
            // 20-bold header face, a note of "1592", its 18px icon and
            // "Buys 6" at the body face, and the "Status" header.
            const int Band = 88;
            const int Note = 112;
            const int Marker = 66;
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1310, Band, Marker, Note);

            int nameGap = edges.RequiredBandX
                - (SummarySectionLayoutMath.CurrencyNameX
                    + SummarySectionLayoutMath.CurrencyNameColumnWidth);
            int requiredGap = edges.HaveBandX - edges.RequiredRightEdge;
            int haveGap = edges.NeededBandX - edges.HaveRightEdge;
            int neededGap = edges.NoteX - edges.NeededRightEdge;
            int noteGap = edges.MarkerX - (edges.NoteX + edges.NoteWidth);

            Assert.Equal(nameGap, requiredGap);
            Assert.Equal(nameGap, haveGap);
            Assert.Equal(nameGap, neededGap);

            // The last gap carries what integer division leaves over, so it
            // is the only one allowed to differ, and by under a pixel per
            // gap.
            Assert.InRange(noteGap - nameGap, 0, 4);

            // And it is real clearance, not the 14px minimum the note used
            // to sit at.
            Assert.True(noteGap > 3 * SummarySectionLayoutMath.CurrencyColumnGap);
        }

        [Fact]
        public void NumberColumns_NoLongerHoldMoreRoomThanTheNoteColumn()
        {
            // The defect: "Note column is jammed up against status and
            // required, have and needed are swimming in padding space". A
            // number column's share of the row is now its own band plus one
            // gap, the same share the note gets.
            const int Band = 88;
            const int Note = 112;
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1310, Band, 66, Note);

            int numberPitch = edges.HaveRightEdge - edges.RequiredRightEdge;
            int notePitch = edges.MarkerX - edges.NoteX;

            Assert.InRange(notePitch - (numberPitch - Band + Note), 0, 4);
        }

        [Fact]
        public void WithNoNote_TheTableIsExactlyWhatItWasBeforeTheColumnExisted()
        {
            var without = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 90, 40);
            var zeroNote = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 90, 40, 0);

            Assert.Equal(without.RequiredRightEdge, zeroNote.RequiredRightEdge);
            Assert.Equal(without.HaveRightEdge, zeroNote.HaveRightEdge);
            Assert.Equal(without.NeededRightEdge, zeroNote.NeededRightEdge);
            Assert.Equal(without.MarkerX, zeroNote.MarkerX);
            Assert.Equal(0, zeroNote.NoteWidth);
            Assert.Equal(zeroNote.MarkerX, zeroNote.NoteX);

            // A Status header still measures its room from Needed, not from
            // a note band that is not there.
            var rooms = SummarySectionLayoutMath.CurrencyHeaderRoomsFor(zeroNote, 20, 20, 20);
            var before = SummarySectionLayoutMath.CurrencyHeaderRoomsFor(without, 20, 20, 20);
            Assert.Equal(before.Status.Left, rooms.Status.Left);
            Assert.Equal(before.Status.Right, rooms.Status.Right);
        }

        [Fact]
        public void ANoteColumnSitsBetweenNeededAndStatus()
        {
            const int NoteWidth = 120;
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 60, 34, NoteWidth);

            Assert.Equal(NoteWidth, edges.NoteWidth);
            Assert.True(edges.NoteX > edges.NeededRightEdge);
            Assert.True(edges.NoteX + NoteWidth < edges.MarkerX);

            // Every number column keeps clear of the note's left rule, the
            // way they used to keep clear of the marker's.
            Assert.True(
                edges.NeededRightEdge <= edges.NoteX - SummarySectionLayoutMath.CurrencyColumnGap);
            Assert.True(edges.HaveRightEdge < edges.NeededRightEdge);
            Assert.True(edges.RequiredRightEdge < edges.HaveRightEdge);

            // And the whole table moves left by exactly what the column
            // took, rather than the note overlapping Status.
            var without = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 60, 34);
            Assert.Equal(without.MarkerX, edges.MarkerX);
            Assert.True(edges.NeededRightEdge < without.NeededRightEdge);
        }

        /// <summary>
        /// The pre-scan and the drawn cell place a note from the same
        /// arithmetic, so the "Buys N" half always ends exactly on the
        /// note's own right edge.
        /// </summary>
        [Fact]
        public void ANotesPartsFillExactlyTheWidthItReserves()
        {
            const int NoteX = 500;
            const int HeldBand = 30;
            const int BuysBand = 44;

            int width = SummarySectionLayoutMath.TradeUpNoteWidth(HeldBand, BuysBand);
            Assert.Equal(
                NoteX + width,
                SummarySectionLayoutMath.TradeUpNoteBuysX(NoteX, HeldBand, HeldBand) + BuysBand);
            Assert.True(
                SummarySectionLayoutMath.TradeUpNoteIconX(NoteX, HeldBand, HeldBand)
                    >= NoteX + HeldBand);
            Assert.Equal(0, SummarySectionLayoutMath.TradeUpNoteWidth(0, 0));
        }

        [Fact]
        public void EveryRowsNoteIcon_SitsAtTheSameX()
        {
            // The defect: the icon followed the number, so a three-digit
            // holding and a four-digit one put it in different places and
            // the icons staggered down the column. Held amounts right-align
            // in the widest one's width instead.
            const int NoteX = 500;
            const int HeldBand = 40;

            int wideIcon = SummarySectionLayoutMath.TradeUpNoteIconX(NoteX, HeldBand, HeldBand);
            int narrowIcon = SummarySectionLayoutMath.TradeUpNoteIconX(NoteX, HeldBand, 22);

            Assert.Equal(wideIcon, narrowIcon);
            Assert.Equal(
                SummarySectionLayoutMath.TradeUpNoteBuysX(NoteX, HeldBand, HeldBand),
                SummarySectionLayoutMath.TradeUpNoteBuysX(NoteX, HeldBand, 22));

            // The narrower number is pushed right to end where the wide one
            // ends, which is what leaves the icon where it was.
            Assert.Equal(NoteX, SummarySectionLayoutMath.TradeUpNoteHeldX(NoteX, HeldBand, HeldBand));
            Assert.Equal(
                NoteX + HeldBand,
                SummarySectionLayoutMath.TradeUpNoteHeldX(NoteX, HeldBand, 22) + 22);
        }

        [Fact]
        public void ANumberWiderThanTheHeldBand_IsNotTruncated()
        {
            // Only reachable if a caller's band is not the table's own
            // widest held amount. The number keeps the note's left rule and
            // pushes its own icon right, so it stays readable and only that
            // row leaves the shared icon x.
            const int NoteX = 500;
            const int HeldBand = 40;
            const int TooWide = 55;

            Assert.Equal(NoteX, SummarySectionLayoutMath.TradeUpNoteHeldX(NoteX, HeldBand, TooWide));
            Assert.True(
                SummarySectionLayoutMath.TradeUpNoteIconX(NoteX, HeldBand, TooWide)
                    >= NoteX + TooWide);
        }

        [Fact]
        public void ARowWithNoHeldSubCurrencyHasNoNoteText()
        {
            var row = new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, Quantity = 18 };

            Assert.Null(SummarySectionLayoutMath.TradeUpNoteHeldText(row));
            Assert.Null(SummarySectionLayoutMath.TradeUpNoteBuysText(row));
            Assert.False(SummarySectionLayoutMath.AnyTradeUpNote(new[] { row }));
        }

        [Fact]
        public void ARowWithAHeldSubCurrencyReadsAsAnAmountAndWhatItBuys()
        {
            var row = new PlanRowViewModel
            {
                RowType = PlanRowType.CurrencyCost,
                Quantity = 18,
                TradeUpCurrencyHeld = 1013,
                TradeUpBuysQuantity = 4,
            };

            Assert.Equal("1013", SummarySectionLayoutMath.TradeUpNoteHeldText(row));
            Assert.Equal("Buys 4", SummarySectionLayoutMath.TradeUpNoteBuysText(row));
            Assert.True(SummarySectionLayoutMath.AnyTradeUpNote(new[] { row }));
        }

        [Fact]
        public void CurrencyColumnEdges_CarryTheBandEveryHeaderCentresOver()
        {
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 90);

            // Floored at the widest of the three header labels by the
            // caller's pre-scan; widened here to 90, which is what a
            // 7-digit Karma balance measures.
            Assert.Equal(90, edges.NumberColumnWidth);
            Assert.Equal(edges.RequiredRightEdge - 90, edges.RequiredBandX);
            Assert.Equal(edges.HaveRightEdge - 90, edges.HaveBandX);
            Assert.Equal(edges.NeededRightEdge - 90, edges.NeededBandX);
        }

        [Fact]
        public void CurrencyHeader_AndTheNumbersUnderIt_ShareTheTracksCentreLine()
        {
            // The law: header and cells centre on the same axis. The
            // numbers right-align inside the band, so the band's centre is
            // the axis both have to agree on.
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200);
            const int headerWidth = 44;

            int headerX = JustifiedColumnTracks.CenteredInBand(
                edges.HaveBandX, edges.NumberColumnWidth, headerWidth);

            Assert.Equal(
                edges.HaveRightEdge - (edges.NumberColumnWidth / 2),
                headerX + (headerWidth / 2));
        }

        [Fact]
        public void CurrencyRowTextY_CentresTheRowsTextTheSameWayItsIconIsCentred()
        {
            // The row's icon and coverage marker centre themselves in the
            // row; its name and numbers used a hard-coded 4, which centred
            // a Body line box only in the 28px row this table drew before
            // the icon took the wallet-LIST tier. Derived from the row it
            // is actually drawn in, the two cannot part company again.
            int rowHeight = PlanContentHeightMath.CurrencyRowHeight;
            int textY = SummarySectionLayoutMath.CurrencyRowTextY;

            Assert.Equal(
                (rowHeight - TypeRampMetrics.BodyInk.LineHeight) / 2,
                textY);

            // Same centring rule the icon beside it uses, within the pixel
            // integer division can cost.
            int iconY = (rowHeight - SummarySectionLayoutMath.CurrencyIconSize) / 2;
            Assert.InRange(
                (textY + TypeRampMetrics.BodyInk.LineHeight / 2)
                    - (iconY + SummarySectionLayoutMath.CurrencyIconSize / 2),
                -1, 1);

            // And the text's own ink stays inside the row.
            Assert.True(textY >= 0);
            Assert.True(textY + TypeRampMetrics.BodyInk.LowestInk <= rowHeight);
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_TracksAreEvenlySpaced_NotPackedRight()
        {
            // The defect: "the currency name all the way left aligned with
            // the columns with their data all the way right aligned.. its
            // hard to track which label belongs to which row with so much
            // wide distance in between". The fix is a regular pitch - the
            // three numeric anchors sit one band and one gap apart, so the
            // eye has something to land on between the name and the last
            // column.
            foreach (int panelWidth in new[] { 800, 1200, 1310, 1920 })
            {
                var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(panelWidth);

                int band = SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(0);
                int firstPitch = edges.HaveRightEdge - edges.RequiredRightEdge;
                int secondPitch = edges.NeededRightEdge - edges.HaveRightEdge;

                Assert.Equal(firstPitch, secondPitch);

                // The gap between the name's reserve and the first number
                // is that same pitch less the band, so the columns run at
                // one rhythm from the name onwards.
                int nameGap = edges.RequiredBandX
                    - (SummarySectionLayoutMath.CurrencyNameX
                        + SummarySectionLayoutMath.CurrencyNameColumnWidth);
                Assert.Equal(firstPitch - band, nameGap);

                // And the first number lands near the middle of the row
                // rather than out at its right edge, which is what the
                // packed stack did.
                Assert.InRange(
                    edges.RequiredRightEdge,
                    panelWidth / 4,
                    panelWidth / 2 + SummarySectionLayoutMath.CurrencyNameX);
            }
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_NameColumnStaysReadable()
        {
            // Equal gaps are only right while the name keeps a real
            // budget: its own reserve plus the gap after it, less the
            // clearance the number column needs. At every width the plan
            // panel can present, that is at least the whole reserve.
            foreach (int panelWidth in new[] { 800, 1310, 1920 })
            {
                var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(panelWidth);
                int nameBudget = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    edges.RequiredRightEdge,
                    SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(0),
                    SummarySectionLayoutMath.CurrencyColumnGap,
                    SummarySectionLayoutMath.CurrencyNameX);

                Assert.True(nameBudget >= 200, $"name budget {nameBudget} at panel {panelWidth}");
            }
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_NarrowPanel_FallsBackToThePackedStack()
        {
            // Below the width the columns and four minimum gaps need there
            // is nothing to share out, and spreading anyway would overlap
            // them. 300: right edge 292, marker 258, and 210px from the
            // name's left edge cannot hold a 210px name reserve, three 60px
            // bands and four 14px gaps - so the columns pack right-to-left
            // as they always did.
            //
            // That threshold is a ~536px panel, far under the module's own
            // window minimum (a 1436px window leaves a 1310px plan panel),
            // so the shared-gap layout is what actually ships and this
            // branch exists to degrade rather than overlap.
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(300);

            Assert.Equal(258, edges.MarkerX);
            Assert.Equal(244, edges.NeededRightEdge);
            Assert.Equal(170, edges.HaveRightEdge);
            Assert.Equal(96, edges.RequiredRightEdge);
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_EveryRegime_KeepsColumnsOutOfEachOther()
        {
            // The invariant both regimes exist to hold: a value right-
            // aligned on its own edge grows LEFTWARD by up to the reserved
            // band width, and must never reach the column to its left. Swept
            // across the regime boundary and across a reserve wide enough
            // for a 7-digit Karma balance.
            foreach (int widest in new[] { 0, 60, 90, 140 })
            {
                int band = SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(widest);
                for (int panelWidth = 200; panelWidth <= 2000; panelWidth += 7)
                {
                    var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(panelWidth, widest);

                    Assert.True(
                        edges.HaveRightEdge - band >= edges.RequiredRightEdge,
                        $"Have intrudes on Required at panel {panelWidth}, band {band}");
                    Assert.True(
                        edges.NeededRightEdge - band >= edges.HaveRightEdge,
                        $"Needed intrudes on Have at panel {panelWidth}, band {band}");
                    Assert.True(edges.NeededRightEdge < edges.MarkerX);
                }
            }
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_ColumnsOrderedLeftToRight_RequiredHaveNeededMarker()
        {
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(800);

            Assert.True(edges.RequiredRightEdge < edges.HaveRightEdge);
            Assert.True(edges.HaveRightEdge < edges.NeededRightEdge);
            Assert.True(edges.NeededRightEdge < edges.MarkerX);
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_WiderPanel_ShiftsEdgesRight()
        {
            var narrow = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(600);
            var wide = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(900);

            Assert.True(wide.MarkerX > narrow.MarkerX);
            Assert.True(wide.RequiredRightEdge > narrow.RequiredRightEdge);
        }

        // --- The justified-width invariant (replaces the pull-in and
        // centring pair CurrencyHeaderBandWidth/CurrencyTableOffsetX built
        // on, both deleted with the machinery) ---
        [Fact]
        public void ComputeCurrencyColumnEdges_MarkerEndsAtThePinnedPanelEdge()
        {
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(800);

            Assert.Equal(
                PlanRelayoutMath.PinnedRightEdge(800),
                edges.MarkerX + SummarySectionLayoutMath.CurrencyMarkerWidth);
            Assert.Equal(SummarySectionLayoutMath.CurrencyMarkerWidth, edges.MarkerWidth);
        }

        // --- The Status column (the marker column shipped unlabelled; its
        // band now has to hold a header too) ---
        [Fact]
        public void EffectiveCurrencyMarkerWidth_TakesTheWiderOfItsFloorAndTheCallersMeasurement()
        {
            Assert.Equal(
                SummarySectionLayoutMath.CurrencyMarkerWidth,
                SummarySectionLayoutMath.EffectiveCurrencyMarkerWidth(0));
            Assert.Equal(
                SummarySectionLayoutMath.CurrencyMarkerWidth,
                SummarySectionLayoutMath.EffectiveCurrencyMarkerWidth(
                    SummarySectionLayoutMath.CurrencyMarkerWidth - 1));
            Assert.Equal(70, SummarySectionLayoutMath.EffectiveCurrencyMarkerWidth(70));
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_AWiderMarkerBand_StillEndsOnThePinnedEdge()
        {
            // A "Status" header out-measures the 34px pill under it, so the
            // band widens leftward: the pinned edge is the one thing that
            // cannot move, and the number columns give up the difference.
            var floor = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 90);
            var widened = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 90, 70);

            Assert.Equal(70, widened.MarkerWidth);
            Assert.Equal(PlanRelayoutMath.PinnedRightEdge(1200), widened.MarkerX + widened.MarkerWidth);
            Assert.Equal(
                floor.MarkerX - (70 - SummarySectionLayoutMath.CurrencyMarkerWidth),
                widened.MarkerX);
            Assert.True(widened.NeededRightEdge < floor.NeededRightEdge);
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_AMarkerBandUnderTheFloor_ChangesNothing()
        {
            // A table with no covered row at all measures no pill, and the
            // column is reserved anyway - a reserve that came and went with
            // the data would shift every other column the moment one
            // currency crossed into full coverage.
            var floor = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 90);
            var unmeasured = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 90, 0);

            Assert.Equal(floor.MarkerX, unmeasured.MarkerX);
            Assert.Equal(floor.NeededRightEdge, unmeasured.NeededRightEdge);
            Assert.Equal(floor.HaveRightEdge, unmeasured.HaveRightEdge);
            Assert.Equal(floor.RequiredRightEdge, unmeasured.RequiredRightEdge);
        }

        // The defect the header law fixes, stated against the real edges:
        // the three columns share ONE band, so a column whose own numbers
        // are narrow than the widest of the three has its header land off
        // its own ink unless the ink is what the header centres over.
        [Fact]
        public void CurrencyHeaders_CentreOnTheirOwnColumnsInk_NotOnTheSharedBand()
        {
            // Have carries a 7-digit Karma balance and sizes the shared
            // 120px band on its own; Required's counts reach 80px.
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 120);
            const int requiredInk = 80;
            const int headerWidth = 56;
            var rooms = SummarySectionLayoutMath.CurrencyHeaderRoomsFor(edges, requiredInk, 120, 120);

            int overInk = JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.RequiredRightEdge, requiredInk, headerWidth, rooms.Required);
            int overBand = JustifiedColumnTracks.CenteredInBand(
                edges.RequiredBandX, edges.NumberColumnWidth, headerWidth);

            // Centred on the two-digit numbers themselves...
            Assert.Equal(
                edges.RequiredRightEdge - (requiredInk / 2),
                overInk + (headerWidth / 2));

            // ...which is 20px right of where the band put it.
            Assert.Equal(20, overInk - overBand);
        }

        // A in-game screenshot, re-derived. Every one of the
        // three number headers out-measured the values under it, and the
        // band clamp answered that by pinning the header's RIGHT edge to
        // the values' right edge - right-alignment, the exact thing the
        // centring was added to remove. Header minus ink was 34, 24 and 30
        // px, so the headers sat 17, 12 and 15px left of their ink.
        [Theory]
        [InlineData(20, 54)]
        [InlineData(8, 32)]
        [InlineData(24, 54)]
        public void CurrencyHeaders_NarrowInkUnderAWideHeader_AreNotRightAligned(
            int ink, int headerWidth)
        {
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1750, 120, 40);
            var rooms = SummarySectionLayoutMath.CurrencyHeaderRoomsFor(edges, ink, ink, ink);

            int required = JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.RequiredRightEdge, ink, headerWidth, rooms.Required);
            int have = JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.HaveRightEdge, ink, headerWidth, rooms.Have);
            int needed = JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.NeededRightEdge, ink, headerWidth, rooms.Needed);

            // Centre on centre, stated in doubled units so the assertion
            // does not turn on which way an odd width truncates.
            Assert.Equal(2 * edges.RequiredRightEdge - ink, 2 * required + headerWidth);
            Assert.Equal(2 * edges.HaveRightEdge - ink, 2 * have + headerWidth);
            Assert.Equal(2 * edges.NeededRightEdge - ink, 2 * needed + headerWidth);

            // And the header now overhangs its own column's ink on both
            // sides rather than ending flush with it.
            Assert.Equal((headerWidth - ink) / 2, required + headerWidth - edges.RequiredRightEdge);
            Assert.Equal((headerWidth - ink) / 2, edges.RequiredRightEdge - ink - required);
            Assert.NotEqual(edges.RequiredRightEdge, required + headerWidth);
        }

        [Fact]
        public void CurrencyHeaderRooms_LeaveTheNumberColumnsHundredsOfPixelsOfSlack()
        {
            // Why no clamp fires on a real panel: the columns are a band
            // and a gap apart, so the room around each one dwarfs any
            // header.
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1750, 120, 40);
            var rooms = SummarySectionLayoutMath.CurrencyHeaderRoomsFor(edges, 20, 8, 24);

            Assert.True(rooms.Required.Width > 200, $"Required room {rooms.Required.Width}");
            Assert.True(rooms.Have.Width > 200, $"Have room {rooms.Have.Width}");
            Assert.True(rooms.Needed.Width > 200, $"Needed room {rooms.Needed.Width}");

            // Adjacent rooms never overlap: the gutter is the whole of what
            // separates two headers that both run to their bound.
            Assert.Equal(
                JustifiedColumnTracks.HeaderGutter, rooms.Have.Left - rooms.Required.Right);
            Assert.Equal(
                JustifiedColumnTracks.HeaderGutter, rooms.Needed.Left - rooms.Have.Right);
            Assert.Equal(
                JustifiedColumnTracks.HeaderGutter, rooms.Status.Left - rooms.Needed.Right);
        }

        [Fact]
        public void CurrencyHeaderRooms_PackedNarrowPanel_StopAtTheNeighbourRatherThanOverlapIt()
        {
            // Below the shared-gap threshold the columns pack 14px apart,
            // and there genuinely is nowhere for a wide header to go. It
            // pins to its room's left bound and overhangs rightward only -
            // the one direction that does not reach the column before it,
            // and it still stops short of the marker.
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(420, 60, 34);
            var rooms = SummarySectionLayoutMath.CurrencyHeaderRoomsFor(edges, 20, 20, 20);

            int needed = JustifiedColumnTracks.CenteredOverContentRightAligned(
                edges.NeededRightEdge, 20, 54, rooms.Needed);

            Assert.True(rooms.Needed.Width < 54);
            Assert.Equal(rooms.Needed.Left, needed);
            Assert.True(
                needed + 54 <= edges.MarkerX,
                $"header right {needed + 54} past marker {edges.MarkerX}");
        }

        [Fact]
        public void CurrencyStatusHeader_NoCoveredRow_CentresInTheReservedBand()
        {
            // No pill measured this render, so the reserved band stands in
            // for the ink and the header holds still whether or not a
            // currency crosses into full coverage.
            var edges = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1750, 120, 40);
            var rooms = SummarySectionLayoutMath.CurrencyHeaderRoomsFor(edges, 20, 8, 24);

            int x = JustifiedColumnTracks.CenteredOverContent(
                edges.MarkerX,
                SummarySectionLayoutMath.CurrencyStatusInk(edges, 0),
                34,
                rooms.Status);

            Assert.Equal(edges.MarkerWidth, SummarySectionLayoutMath.CurrencyStatusInk(edges, 0));
            Assert.Equal(
                JustifiedColumnTracks.CenteredInBand(edges.MarkerX, edges.MarkerWidth, 34), x);
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_WiderPanel_SharesTheIncreaseAcrossEveryGap()
        {
            // The right-hand block used to move by the whole increase with
            // the name column absorbing all of it. The four gaps take an
            // equal share instead, so a wider panel spreads the columns
            // rather than dragging them further from the name. 400px of
            // panel is 100px per gap, so column i moves by i+1 of them: the
            // marker alone still tracks the panel edge by the full 400,
            // because it is pinned to that edge rather than placed on gaps.
            var narrow = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200, 90);
            var wide = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1600, 90);

            Assert.Equal(400, wide.MarkerX - narrow.MarkerX);
            Assert.Equal(300, wide.NeededRightEdge - narrow.NeededRightEdge);
            Assert.Equal(200, wide.HaveRightEdge - narrow.HaveRightEdge);
            Assert.Equal(100, wide.RequiredRightEdge - narrow.RequiredRightEdge);
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_NameBudgetTakesItsOwnShareOfTheIncrease()
        {
            const int nameX = SummarySectionLayoutMath.CurrencyNameX;

            int narrow = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1200).RequiredRightEdge,
                SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(0),
                SummarySectionLayoutMath.CurrencyColumnGap,
                nameX);
            int wide = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                SummarySectionLayoutMath.ComputeCurrencyColumnEdges(1600).RequiredRightEdge,
                SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(0),
                SummarySectionLayoutMath.CurrencyColumnGap,
                nameX);

            // The name's budget stops one gap before the Required BAND, so
            // it grows by exactly the gap's share of the 400px increase -
            // the same share the three gaps right of it take.
            Assert.Equal(100, wide - narrow);
        }

        // --- Regression: EffectiveCurrencyNumberColumnWidth / widened
        // ComputeCurrencyColumnEdges (a large unclamped Have value, e.g. a
        // 6-7 digit Karma balance, must not intrude into the Required
        // column - see the class doc comment above the currency-table
        // geometry region) ---
        [Fact]
        public void EffectiveCurrencyNumberColumnWidth_BelowFloor_ReturnsFixedFloor()
        {
            Assert.Equal(
                SummarySectionLayoutMath.CurrencyNumberColumnWidth,
                SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(10));
        }

        [Fact]
        public void EffectiveCurrencyNumberColumnWidth_Zero_ReturnsFixedFloor()
        {
            Assert.Equal(
                SummarySectionLayoutMath.CurrencyNumberColumnWidth,
                SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(0));
        }

        [Fact]
        public void EffectiveCurrencyNumberColumnWidth_AboveFloor_ReturnsMeasuredWidth()
        {
            // A plausible width for a 7-digit Karma balance rendered at
            // the body font - comfortably past the 60px fixed floor.
            Assert.Equal(90, SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(90));
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_WidestNumberWidth_TakesItsRoomFromTheGaps()
        {
            // A wider reserve is paid for out of the shared gaps, in equal
            // parts, and the table's right-hand edge does not move. The
            // three number columns take 180px more between them, so each of
            // the four gaps gives up 45.
            const int panelWidth = 800;
            var fixedFloor = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(panelWidth);
            var widened = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(panelWidth, 120);
            int floorBand = SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(0);
            int widerBand = SummarySectionLayoutMath.EffectiveCurrencyNumberColumnWidth(120);

            int floorGap = fixedFloor.HaveRightEdge - fixedFloor.RequiredRightEdge - floorBand;
            int widerGap = widened.HaveRightEdge - widened.RequiredRightEdge - widerBand;

            Assert.Equal(fixedFloor.MarkerX, widened.MarkerX);
            Assert.Equal(45, floorGap - widerGap);
            Assert.True(widerGap >= SummarySectionLayoutMath.CurrencyColumnGap);

            // The band really did widen - the assertions above would also
            // hold if nothing had changed at all.
            Assert.True(widened.RequiredRightEdge > fixedFloor.RequiredRightEdge);
        }

        [Fact]
        public void ComputeCurrencyColumnEdges_WidestNumberWidth_PacksTheStackWhenTheGapsRunOut()
        {
            // The reserve DOES decide the geometry at the boundary: a band
            // wide enough that the four gaps can no longer reach their 14px
            // minimum drops the row back to the packed stack, where the
            // widened bands push Have and Required left as they always did.
            const int panelWidth = 700;
            var floor = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(panelWidth);
            var widened = SummarySectionLayoutMath.ComputeCurrencyColumnEdges(panelWidth, 140);

            Assert.Equal(floor.MarkerX, widened.MarkerX);

            // Packed, the last column IS the table's right edge (marker
            // less one gap); on shared gaps it stops a whole gap short of
            // it. That is how the two regimes are told apart from the
            // outside.
            Assert.Equal(
                widened.MarkerX - SummarySectionLayoutMath.CurrencyColumnGap,
                widened.NeededRightEdge);
            Assert.True(floor.NeededRightEdge < widened.NeededRightEdge);

            Assert.Equal(widened.NeededRightEdge - 140 - SummarySectionLayoutMath.CurrencyColumnGap,
                widened.HaveRightEdge);
            Assert.Equal(widened.HaveRightEdge - 140 - SummarySectionLayoutMath.CurrencyColumnGap,
                widened.RequiredRightEdge);
        }

        // --- Non-coin cost identity and its scroll anchor keys ---
        [Fact]
        public void CostKeys_SameNumberInBothIdSpaces_AreDifferentKeys()
        {
            // Id 24 is both a real item and the wallet currency "Pristine
            // Fractal Relics". A bare number would give the two rows one
            // key, and a restore would put the wrong row on the line.
            Assert.NotEqual(
                SummarySectionLayoutMath.WalletCurrencyCostKey(24),
                SummarySectionLayoutMath.BarterItemCostKey(24));
        }

        [Fact]
        public void CostKeys_SameId_ProduceTheSameKeyEveryCall()
        {
            Assert.Equal(
                SummarySectionLayoutMath.WalletCurrencyCostKey(3),
                SummarySectionLayoutMath.WalletCurrencyCostKey(3));
            Assert.Equal(
                SummarySectionLayoutMath.BarterItemCostKey(19721),
                SummarySectionLayoutMath.BarterItemCostKey(19721));
            Assert.NotEqual(
                SummarySectionLayoutMath.WalletCurrencyCostKey(3),
                SummarySectionLayoutMath.WalletCurrencyCostKey(4));
        }

        [Fact]
        public void NonCoinRowAnchorKey_CarriesTheRowsOwnIdentity()
        {
            var wallet = new PlanRowViewModel
            {
                RowType = PlanRowType.CurrencyCost,
                NonCoinCostKey = SummarySectionLayoutMath.WalletCurrencyCostKey(24),
            };
            var barter = new PlanRowViewModel
            {
                RowType = PlanRowType.CurrencyCost,
                IsBarterItemCost = true,
                NonCoinCostKey = SummarySectionLayoutMath.BarterItemCostKey(24),
            };

            string walletKey = SummarySectionLayoutMath.NonCoinRowAnchorKey(wallet);
            Assert.False(string.IsNullOrEmpty(walletKey));
            Assert.NotEqual(walletKey, SummarySectionLayoutMath.NonCoinRowAnchorKey(barter));
        }

        [Fact]
        public void NonCoinRowAnchorKey_NoIdentity_IsNull()
        {
            // A null key is what tells the renderer not to register the
            // row at all, so a row without an identity can never take a
            // key another row would answer to.
            Assert.Null(SummarySectionLayoutMath.NonCoinRowAnchorKey(null));
            Assert.Null(SummarySectionLayoutMath.NonCoinRowAnchorKey(
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost }));
            Assert.Null(SummarySectionLayoutMath.NonCoinRowAnchorKey(
                new PlanRowViewModel { RowType = PlanRowType.CurrencyCost, NonCoinCostKey = "" }));
        }

        [Fact]
        public void GroupAnchorKeys_TheTwoGroupsDoNotShareAKey()
        {
            Assert.NotEqual(
                SummarySectionLayoutMath.NonCoinGroupAnchorKey(isInventoryGroup: false),
                SummarySectionLayoutMath.NonCoinGroupAnchorKey(isInventoryGroup: true));
        }

        [Fact]
        public void GroupAnchorKeys_DoNotCollideWithARowsKey()
        {
            var row = new PlanRowViewModel
            {
                RowType = PlanRowType.CurrencyCost,
                NonCoinCostKey = SummarySectionLayoutMath.WalletCurrencyCostKey(1),
            };
            string rowKey = SummarySectionLayoutMath.NonCoinRowAnchorKey(row);

            Assert.NotEqual(rowKey, SummarySectionLayoutMath.NonCoinGroupAnchorKey(false));
            Assert.NotEqual(rowKey, SummarySectionLayoutMath.NonCoinGroupAnchorKey(true));
        }

        [Fact]
        public void GroupNonCoinRows_MarksWhichGroupIsTheInventoryOne()
        {
            var rows = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CurrencyCost),
                BarterRow(),
            };

            var groups = SummarySectionLayoutMath.GroupNonCoinRows(rows);

            Assert.Equal(2, groups.Count);
            Assert.False(groups[0].IsInventoryGroup);
            Assert.True(groups[1].IsInventoryGroup);
        }

        [Fact]
        public void BodyHeight_FirstNonCoinRow_AddsThe141PixelTable()
        {
            // The measured jump the scroll anchors have to absorb: the
            // cost band's disclosure line (23), the table's top gap (12),
            // its column header (32), one group heading (32) and the row
            // itself (42).
            var withoutTable = new List<PlanRowViewModel> { Row(PlanRowType.CostFormulaTile) };
            var withOneRow = new List<PlanRowViewModel>
            {
                Row(PlanRowType.CostFormulaTile),
                Row(PlanRowType.CurrencyCost),
            };

            Assert.Equal(
                141,
                SummarySectionLayoutMath.BodyHeight(withOneRow)
                    - SummarySectionLayoutMath.BodyHeight(withoutTable));
        }
    }
}
