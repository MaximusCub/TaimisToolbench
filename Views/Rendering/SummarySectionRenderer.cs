using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // The Summary/Total Cost section: two formula-band tile rows (CreateFormulaBand), a
    // column-header table for the plan's non-coin currency costs
    // (CreateCurrencyTable), the multi-item batch MultiItemNote banner row
    // (via TextRowRenderer), and a subdued footnote row (CreateFootnoteRow).
    // Height agreement for this shape lives in
    // Services/SummarySectionLayoutMath.BodyHeight, not
    // PlanContentHeightMath, shared infrastructure this package must not
    // reshape (see docs/KNOWN-ISSUES.md's policy on code pinned by
    // expensive evidence) - see that class's doc comment for the rationale.
    internal sealed class SummarySectionRenderer
    {
        private readonly ISectionRelayoutSink _sink;
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;

        // Registers one control as a scroll anchor under a stable key
        // (Services/ScrollAnchorMath). Optional - a null one simply leaves
        // the non-coin table without anchors of its own.
        private readonly Action<string, Control> _registerScrollAnchor;

        internal SummarySectionRenderer(
            ISectionRelayoutSink sink, Func<int, ItemStatBlock> getItemStatBlock = null,
            Action<string, Control> registerScrollAnchor = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _getItemStatBlock = getItemStatBlock;
            _registerScrollAnchor = registerScrollAnchor;
        }

        internal void Render(PlanSectionViewModel section, FlowPanel contentFlow, int panelWidth)
        {
            var costBandRows = new List<PlanRowViewModel>();
            var profitBandRows = new List<PlanRowViewModel>();
            var currencyRows = new List<PlanRowViewModel>();
            var noteRows = new List<PlanRowViewModel>();
            // A List, like noteRows - not a single "last row
            // wins" variable. SummarySectionLayoutMath.BodyHeight sums
            // FallbackTextRowHeight per SummaryFootnote row it counts
            // (its own doc comment: "summed rather than assumed so a
            // null/absent footnote degrades gracefully instead of
            // desyncing height from what actually rendered"); a renderer
            // that only drew the LAST such row while BodyHeight kept
            // counting all of them would silently reserve dead space (or
            // too little) the moment a second footnote row ever existed -
            // rendering every row it is handed keeps the two in agreement
            // by construction, the same way noteRows already does for
            // MultiItemNote.
            var footnoteRows = new List<PlanRowViewModel>();

            foreach (var row in section.Rows)
            {
                switch (row.RowType)
                {
                    case PlanRowType.CostFormulaTile:
                        costBandRows.Add(row);
                        break;
                    case PlanRowType.ProfitFormulaTile:
                        profitBandRows.Add(row);
                        break;
                    case PlanRowType.CurrencyCost:
                        currencyRows.Add(row);
                        break;
                    case PlanRowType.MultiItemNote:
                        noteRows.Add(row);
                        break;
                    case PlanRowType.SummaryFootnote:
                        footnoteRows.Add(row);
                        break;
                }
            }

            if (costBandRows.Count > 0)
            {
                // The cost band is the plan's headline: its result tile is
                // boxed, plus the "the gold figure is not the whole cost"
                // disclosure line whenever currency rows follow it. Both
                // its row height and the height BodyHeight counts for it
                // come from SummarySectionLayoutMath.CostBandHeight with
                // the SAME currencyRows.Count > 0 condition that decides
                // whether the line is drawn.
                CreateFormulaBand(
                    costBandRows, contentFlow, panelWidth,
                    SummarySectionLayoutMath.CostBandHeight(currencyRows.Count > 0),
                    highlightResult: true,
                    currencyNoteText: SummarySectionLayoutMath.CurrencyRequirementNote(currencyRows),
                    currencyNoteTooltip: SummarySectionLayoutMath.CurrencyRequirementNoteTooltip(currencyRows));
            }

            if (profitBandRows.Count > 0)
            {
                // Derived stats, not the headline - no highlight box and
                // the unchanged CostTileRowHeight band.
                CreateFormulaBand(
                    profitBandRows, contentFlow, panelWidth,
                    PlanContentHeightMath.CostTileRowHeight,
                    highlightResult: false);
            }

            if (currencyRows.Count > 0)
            {
                CreateCurrencyTable(currencyRows, contentFlow, panelWidth);
            }

            foreach (var row in noteRows)
            {
                // CreateTextRow moved to
                // Views/Rendering/TextRowRenderer (see that class's doc
                // comment - it has two call sites still living in
                // CraftingPlanView, this one and the default fallback case
                // in CreateCollapsibleSection, so it could not move
                // wholesale into a single extracted renderer).
                TextRowRenderer.CreateTextRow(row.Label, contentFlow, panelWidth, _sink);
            }

            foreach (var row in footnoteRows)
            {
                CreateFootnoteRow(row.Label, contentFlow, panelWidth);
            }
        }

        /// <summary>
        /// One tile's already-created controls, cached for relayout - m2
        /// 3.5's [FANOUT] case: unlike a single-anchor row, every tile's
        /// caption AND coin segments are independently re-centered inside
        /// their own tileWidth-wide slice on every drag tick.
        /// </summary>
        private sealed class CostTileHandle
        {
            public Label CaptionLabel;
            public CoinCurrencyRenderer.SegmentLayoutHandle Segments;
            public Label NoteLabel;

            // Band-space y of this tile's amount run and disclosure line.
            public int AmountY;
            public int NoteY;

            // The highlight box, on the result tile of a highlighted band
            // only. Its caption/note/amount are its CHILDREN, laid out
            // once against the box's own width, so a resize moves the box
            // and nothing inside it - see CreateFormulaBand.
            public Panel Box;
        }

        // Left edge a collapsed one-tile band's contents start at - the
        // same 8px content gutter the currency table's icon column and the
        // footnote row already use, so the section reads as one left edge.
        private const int LoneTileContentX = 8;

        // Aliased, not duplicated: this and CostTileRowHeight are one piece
        // of arithmetic - see PlanContentHeightMath. A highlighted band
        // starts its caption at SummarySectionLayoutMath's box-derived y
        // instead - see CreateFormulaBand.
        private const int BandCaptionY = PlanContentHeightMath.CostTileCaptionY;

        private static readonly Color BandCaptionColor = new Color(153, 153, 153);

        // The result tile's caption is the one a user is actually looking
        // for; it stays the same size as its siblings (so the band still
        // reads as one formula) but is lifted out of the dim grey.
        private static readonly Color HighlightedCaptionColor = new Color(235, 235, 235);

        // The result tile's highlight box. Warm gold, the coin band's own
        // hue - the eye lands on it without a second colour entering the
        // section. Both are scaled down from one tint rather than written
        // as two literals, the same premultiplied "Color * f" idiom
        // every pill fill in the module uses: Blish composites a Panel's
        // BackgroundColor over whatever is behind it, so the window's
        // parchment texture still reads through the fill at 86%. The frame
        // paints ON the fill (see CreateHighlightBox), so an edge lands at
        // 1 - 0.5 * 0.86 ~= 0.57 against that 0.14 interior - the ring is
        // four times the interior's density, which is what makes it read
        // as an edge at the frame's hairline width.
        private static readonly Color ResultHighlightTint = new Color(214, 176, 96);
        private static readonly Color ResultHighlightFill = ResultHighlightTint * 0.14f;
        private static readonly Color ResultHighlightBorder = ResultHighlightTint * 0.5f;

        // Warm, not red: the disclosure is a caveat about scope, not an
        // error, and it must not read as an alarm on an ordinary plan.
        private static readonly Color CurrencyNoteColor = new Color(206, 170, 92);

        /// <summary>
        /// Vertical space an amount run occupies: its own text height, or
        /// the coin icon's if that is taller (at DefaultFont16 the 20px
        /// icons are the tallest thing in the row).
        /// </summary>
        private static int AmountBlockHeight(BitmapFont font)
        {
            int textHeight = (int)System.Math.Ceiling(font.MeasureString("0").Height);
            return textHeight > CoinSegmentMath.CoinIconSize ? textHeight : CoinSegmentMath.CoinIconSize;
        }

        /// <summary>
        /// A formula band - N equal-width stat tiles reading left-to-right as a
        /// formula ("Sell Value - Total Materials Value = Profit if Sold").
        /// Callers pass the rows of exactly ONE band, and rowHeight is the
        /// caller's, so BodyHeight and the row panel built here are the same
        /// number by construction - see SummarySectionLayoutMath.BodyHeight,
        /// which must also account for currencyNoteText.
        /// <para>
        /// Geometry matches ComputeCostTileGeometry EXCEPT for a collapsed
        /// one-tile band, left-aligned at the section's content gutter instead
        /// of centred. row.TooltipText goes on captionLabel, not rowPanel.
        /// </para>
        /// <para>
        /// The final boundary's symbol is not an unconditional "=": it reads the
        /// rightmost tile's PlanRowViewModel.FormulaResultIsExact and draws
        /// NeutralResultSeparator when false, because the profit band's loss
        /// case shows Math.Abs(profit) and "=" would be false as drawn.
        /// </para>
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        private void CreateFormulaBand(
            List<PlanRowViewModel> tileRows, FlowPanel parent, int panelWidth,
            int rowHeight, bool highlightResult,
            string currencyNoteText = null, string currencyNoteTooltip = null)
        {
            int tileCount = tileRows.Count;
            if (tileCount == 0)
            {
                return;
            }

            const int totalMargin = 40;
            const int minTileWidth = 80;

            // Drawn at the final boundary instead of
            // "=" when the rightmost tile's FormulaResultIsExact is false -
            // see this method's own doc comment. Deliberately not "-"
            // (would misread as a second subtraction) and not "=" (would
            // repeat the exact claim this fix removes); a colon reads as
            // plain, non-asserting punctuation grouping the two sides.
            const string NeutralResultSeparator = ":";
            bool lone = tileCount == 1;
            var geometry = PlanRelayoutMath.ComputeCostTileGeometry(panelWidth, tileCount, totalMargin, minTileWidth);

            var rowPanel = new ClippedPanel()
            {
                Size = new Point(panelWidth, rowHeight),
                Parent = parent,
            };

            // The tile caption is this band's column header - it names the
            // number under it exactly as a table header names its column -
            // so it sits in the same tier. The disclosure line stays
            // Caption: it is fine print qualifying one number, not a
            // heading.
            var captionFont = UiFonts.ColumnHeader;
            var noteFont = UiFonts.Caption;
            var amountFont = UiFonts.Body;

            // A highlighted band carries its result tile's box, so its
            // caption starts one box margin+padding down; that y is the one
            // CostBandHeight reserved room for. Everything below the
            // caption follows from it and the shared label-to-value gap, so
            // the band needs no second (bottom-anchored) pad of its own.
            int captionY = highlightResult ? SummarySectionLayoutMath.CostBandCaptionY : BandCaptionY;

            int captionHeight = (int)System.Math.Ceiling(captionFont.MeasureString("0").Height);
            int noteHeight = (int)System.Math.Ceiling(noteFont.MeasureString("0").Height);

            int amountHeight = AmountBlockHeight(amountFont);

            // One amountY for the whole band, hung the SAME
            // label-to-value gap under the caption in both bands (the
            // profit band's amount used to be bottom-anchored inside its
            // row height, which left it 1px under its caption). The
            // disclosure line is a footnote BELOW this run and so is not a
            // term of it: all three tiles' coin runs sit on one line
            // whether or not the result tile carries a note.
            int amountY = SummarySectionLayoutMath.BandAmountY(captionY + captionHeight);
            int noteY = SummarySectionLayoutMath.BandNoteY(amountY, amountHeight);

            int boxTop = SummarySectionLayoutMath.CostBandBoxTop;
            int boxContentBottom = currencyNoteText != null
                ? noteY + noteHeight
                : amountY + amountHeight;
            int boxHeight = SummarySectionLayoutMath.CostBandBoxHeight(boxContentBottom);

            var tiles = new List<CostTileHandle>(tileCount);
            for (int i = 0; i < tileCount; i++)
            {
                int tileX = geometry.StartX + i * geometry.TileWidth;
                var row = tileRows[i];
                bool isResult = i == tileCount - 1;
                bool boxed = isResult && highlightResult;

                // The disclosure line sits under the RESULT tile's caption -
                // it qualifies that tile's number, not the band as a whole.
                string noteText = isResult ? currencyNoteText : null;

                string caption = row.Label ?? "";
                int captionWidth = (int)System.Math.Ceiling(captionFont.MeasureString(caption).Width);
                int noteWidth = noteText != null
                    ? (int)System.Math.Ceiling(noteFont.MeasureString(noteText).Width)
                    : 0;
                var segments = CoinCurrencyRenderer.BuildCoinSegments(row.CoinValue, amountFont);
                int segmentsWidth = CoinCurrencyRenderer.TotalCoinSegmentsWidth(segments);

                // Where this tile's runs are centred, and what they are
                // centred INSIDE: the box for a boxed tile (whose own x
                // then carries the tile centring), the tile slice
                // otherwise. Never clamped to the tile width - Blish clips
                // a container's children, so a box narrower than its
                // content would cut the amount off where an unboxed tile
                // merely overlaps its neighbour.
                Panel box = null;
                Panel host = rowPanel;
                int hostTop = 0;
                int hostWidth = 0;
                if (boxed)
                {
                    int widest = captionWidth;
                    if (noteWidth > widest)
                    {
                        widest = noteWidth;
                    }

                    if (segmentsWidth > widest)
                    {
                        widest = segmentsWidth;
                    }

                    int boxWidth = SummarySectionLayoutMath.CostBandBoxWidth(widest);

                    box = CreateHighlightBox(
                        rowPanel,
                        ContentX(lone, tileX, geometry.TileWidth, boxWidth),
                        boxTop, boxWidth, boxHeight);
                    host = box;
                    hostTop = boxTop;
                    hostWidth = boxWidth;
                }

                var captionLabel = LabelHelpers.WithDescenderClearance(new Label()
                {
                    Text = caption,
                    Font = captionFont,
                    TextColor = boxed ? HighlightedCaptionColor : BandCaptionColor,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(
                        TileContentX(boxed, hostWidth, lone, tileX, geometry.TileWidth, captionWidth),
                        captionY - hostTop),
                    Parent = host,
                });
                TooltipFacility.ApplyPlain(captionLabel, row.TooltipText);

                var segmentHandle = CoinCurrencyRenderer.LayoutCoinSegments(
                    host, segments,
                    TileContentX(boxed, hostWidth, lone, tileX, geometry.TileWidth, segmentsWidth),
                    amountY - hostTop, amountFont);

                // Built after the amount because it hangs UNDER it: the
                // disclosure is a footnote on this tile's number, not a
                // second caption over it.
                Label noteLabel = null;
                if (noteText != null)
                {
                    noteLabel = LabelHelpers.WithDescenderClearance(new Label()
                    {
                        Text = noteText,
                        Font = noteFont,
                        TextColor = CurrencyNoteColor,
                        AutoSizeWidth = true,
                        AutoSizeHeight = true,
                        Location = new Point(
                            TileContentX(boxed, hostWidth, lone, tileX, geometry.TileWidth, noteWidth),
                            noteY - hostTop),
                        Parent = host,
                    });
                    TooltipFacility.ApplyPlain(noteLabel, currencyNoteTooltip);
                }

                tiles.Add(new CostTileHandle
                {
                    CaptionLabel = captionLabel,
                    Segments = segmentHandle,
                    NoteLabel = noteLabel,
                    AmountY = amountY,
                    NoteY = noteY,
                    Box = box,
                });
            }

            // One operator per boundary between two tiles: "-" for every
            // boundary except the last, which reads "=" - except the
            // profit band's loss case, which gets NeutralResultSeparator.
            // Centered on the boundary x where tile i+1 begins. Never drawn
            // for a lone tile (there is no boundary).
            List<Label> operatorLabels = null;
            if (tileCount > 1)
            {
                operatorLabels = new List<Label>(tileCount - 1);
                for (int i = 1; i < tileCount; i++)
                {
                    bool isFinalBoundary = i == tileCount - 1;
                    string symbol = isFinalBoundary
                        ? (tileRows[i].FormulaResultIsExact ? "=" : NeutralResultSeparator)
                        : "-";
                    int boundaryX = geometry.StartX + i * geometry.TileWidth;
                    int symbolWidth = (int)System.Math.Ceiling(amountFont.MeasureString(symbol).Width);
                    var operatorLabel = new Label()
                    {
                        Text = symbol,
                        Font = amountFont,
                        TextColor = BandCaptionColor,
                        AutoSizeWidth = true,
                        AutoSizeHeight = true,
                        Location = new Point(boundaryX - symbolWidth / 2, amountY),
                        Parent = rowPanel,
                    };
                    operatorLabels.Add(operatorLabel);
                }
            }

            // [FANOUT]: every tile's caption + coin segments are
            // font-only (invariant to panelWidth) - only tileWidth/startX
            // and each tile's own centering offset move. No MeasureString.
            // A boxed tile is one control: its runs are centred inside the
            // box, whose width never changes, so moving the box moves them.
            // A lone tile's anchor is a constant, so its closure is a
            // no-op beyond the row's own width.
            _sink.AddRelayout(w =>
            {
                rowPanel.Size = new Point(w, rowHeight);
                var g = PlanRelayoutMath.ComputeCostTileGeometry(w, tileCount, totalMargin, minTileWidth);
                for (int i = 0; i < tiles.Count; i++)
                {
                    int tileX = g.StartX + i * g.TileWidth;
                    var tile = tiles[i];

                    if (tile.Box != null)
                    {
                        tile.Box.Location = new Point(
                            ContentX(lone, tileX, g.TileWidth, tile.Box.Width), boxTop);
                        continue;
                    }

                    tile.CaptionLabel.Location = new Point(
                        ContentX(lone, tileX, g.TileWidth, tile.CaptionLabel.Width), captionY);

                    if (tile.NoteLabel != null)
                    {
                        tile.NoteLabel.Location = new Point(
                            ContentX(lone, tileX, g.TileWidth, tile.NoteLabel.Width), tile.NoteY);
                    }

                    int segmentsWidth = ShoppingColumnMath.SegmentRunWidth(tile.Segments.TextWidths, CoinSegmentMath.CoinIconSize, CoinSegmentMath.CoinLabelIconGap, CoinSegmentMath.CoinSegmentGap);
                    int coinStartX = ContentX(lone, tileX, g.TileWidth, segmentsWidth);
                    CoinCurrencyRenderer.RepositionSegments(tile.Segments, coinStartX, tile.AmountY);
                }

                if (operatorLabels != null)
                {
                    for (int i = 0; i < operatorLabels.Count; i++)
                    {
                        int boundaryX = g.StartX + (i + 1) * g.TileWidth;
                        var operatorLabel = operatorLabels[i];
                        operatorLabel.Location = new Point(boundaryX - operatorLabel.Width / 2, amountY);
                    }
                }
            });

#if DEBUG
            // The band's own rowHeight came from
            // SummarySectionLayoutMath (CostBandHeight / CostTileRowHeight)
            // and is the SAME number BodyHeight counted for this band. Its
            // contents are placed from measured font metrics, so this is
            // the one place the two can diverge - fail loud here rather
            // than let contentFlow reserve a height the band overflows.
            // The highlight box is the taller of the two, so a highlighted
            // band is asserted on the box's own bottom edge.
            System.Diagnostics.Debug.Assert(
                (highlightResult ? boxTop + boxHeight : boxContentBottom) <= rowHeight,
                "SummarySectionRenderer: a formula band's amounts (and its highlight box) must fit " +
                "inside the row height SummarySectionLayoutMath reserved for it - see CostBandHeight.");
#endif
        }

        /// <summary>
        /// Width of the highlight box's frame. TWO logical pixels, for the
        /// reason LabelHelpers.CreateRowDivider is 2px: Blish applies the
        /// GW2 UI scale as a real GPU scale matrix, so a 1px logical quad
        /// rasterizes to floor(scale) = 0 guaranteed physical pixels and
        /// vanishes or survives according to its own sub-pixel phase
        /// (KNOWN-ISSUES #23). The band's y is a scroll offset away and the
        /// box's x follows the panel width, so the four edges each land on
        /// a different phase - in game the box drew its top and
        /// bottom and neither side. At 2, floor(2 * 0.81) = 1 covered
        /// physical pixel at the smallest supported UI scale, on all four
        /// edges. Content is inset by CostBandBoxPadX (14) / PadY (6), so
        /// the wider frame still cannot reach it.
        /// </summary>
        private const int HighlightBoxBorder = 2;

        /// <summary>
        /// The result tile's highlight box, and the container its caption,
        /// disclosure line and amount are parented to. The box IS the fill
        /// panel - nothing paints beneath it, so the parchment behind the
        /// section shows through at exactly ResultHighlightFill's alpha.
        /// The frame is four HighlightBoxBorder-wide edge panels drawn ON
        /// the fill (so an edge composites to fill+border, visibly denser
        /// than the interior);
        /// deliberately NOT the LabelHelpers.CreateSmallTag idiom of a
        /// border-coloured panel with the fill inset inside it, which every
        /// other caller gets away with only because its border is opaque.
        /// A translucent border there would under-paint the whole interior.
        ///
        /// The edges are siblings of the tile's labels but can never
        /// overlap them: content is inset by CostBandBoxPadX/PadY, both of
        /// which exceed this border width.
        /// </summary>
        private static Panel CreateHighlightBox(
            Panel parent, int x, int y, int width, int height)
        {
            var box = new ClippedPanel()
            {
                Size = new Point(width, height),
                Location = new Point(x, y),
                BackgroundColor = ResultHighlightFill,
                Parent = parent,
            };

            const int b = HighlightBoxBorder;
            AddHighlightBoxEdge(box, 0, 0, width, b);
            AddHighlightBoxEdge(box, 0, height - b, width, b);
            AddHighlightBoxEdge(box, 0, b, b, height - 2 * b);
            AddHighlightBoxEdge(box, width - b, b, b, height - 2 * b);

            return box;
        }

        private static void AddHighlightBoxEdge(Panel box, int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            new ClippedPanel()
            {
                Size = new Point(width, height),
                Location = new Point(x, y),
                BackgroundColor = ResultHighlightBorder,
                Parent = box,
            };
        }

        /// <summary>
        /// Left x of one run inside its tile: centred in the highlight box
        /// for a boxed tile (the box's own x carries the tile centring),
        /// otherwise <see cref="ContentX"/> against the tile slice.
        /// </summary>
        private static int TileContentX(
            bool boxed, int boxContentWidth, bool lone, int tileX, int tileWidth, int contentWidth)
        {
            return boxed
                ? PlanRelayoutMath.CenterX(boxContentWidth, contentWidth)
                : ContentX(lone, tileX, tileWidth, contentWidth);
        }

        /// <summary>
        /// Left x of one tile's content: the shared gutter for a lone
        /// (left-aligned) tile, otherwise centred inside its own tile
        /// slice. One helper so the build pass and the relayout closure
        /// cannot anchor a band differently.
        /// </summary>
        private static int ContentX(bool lone, int tileX, int tileWidth, int contentWidth)
        {
            return lone ? LoneTileContentX : tileX + PlanRelayoutMath.CenterX(tileWidth, contentWidth);
        }

        // --- Currency table ---
        //
        // Every row - header included - is one full-width panel
        // (CreateCurrencyRowPanel). Column coordinates are panel-relative,
        // which is exactly panel-width-relative now that the table
        // justifies to the panel.
        //
        // 4 columns (Currency | Required | Have | Needed) do not fit
        // ColumnHeaderRowRenderer's left/middle/right (3-slot) shape, so this
        // hand-rolls its own header row - the same precedent
        // ShoppingListSectionRenderer.CreateShoppingListHeaderRow already
        // set for its own 4-column (Item/Amount/Each/Total) header, rather
        // than stretching ColumnHeaderRowRenderer's signature to fit a shape
        // it was not designed for.
        private const int CurrencyRowHeight = PlanContentHeightMath.CurrencyRowHeight;

        private void CreateCurrencyTable(List<PlanRowViewModel> rows, FlowPanel parent, int panelWidth)
        {
            // Pre-scan the actual widest rendered
            // Required/Have/Needed value across every row this render -
            // mirrors ShoppingListSectionRenderer.Render's own maxEachWidth/
            // maxTotalWidth pre-scan (see SummarySectionLayoutMath's
            // EffectiveCurrencyNumberColumnWidth doc comment for why the
            // fixed 60px floor alone is not always enough: an unclamped
            // Have value for a currency like Karma can run 6-7 digits).
            // One pass over rows (a plan's currency list is short - a
            // handful of entries in practice) with the SAME font both the
            // header and every data row already use.
            // The number bands are never narrower than their own header
            // labels: at the ColumnHeader tier "Required"/"Have"/"Needed"
            // routinely out-measure a short value, and a band sized to the
            // value alone would let the currency name run under the header
            // above it.
            var scan = ScanCurrencyColumns(rows);

            // Wallet currencies and barter items are checked in two
            // different places, so the table draws them as two groups, each
            // under a heading naming the place. The grouping is derived
            // here rather than carried as extra rows: a heading is not a
            // cost, and a heading ROW would be one missed RowType check
            // away from being counted as one by the plan-level totals, the
            // column scan or the height math.
            var groups = SummarySectionLayoutMath.GroupNonCoinRows(rows);

            CreateCurrencyTableTopGap(parent, panelWidth);
            CreateCurrencyTableHeaderRow(
                parent, panelWidth, scan, SummarySectionLayoutMath.NonCoinNameHeader(rows),
                SummarySectionLayoutMath.NonCoinTableRowsHeight(groups));
            for (int g = 0; g < groups.Count; g++)
            {
                // The table's own scroll anchors. Without them the
                // nearest anchor to a line inside this table is the
                // Summary section header, which sits ABOVE the rows whose
                // count changes, so a restore that holds it still moves
                // every section below by the table's growth.
                var headingRow = CreateNonCoinGroupHeadingRow(
                    groups[g].Heading, parent, panelWidth);
                RegisterScrollAnchor(
                    SummarySectionLayoutMath.NonCoinGroupAnchorKey(groups[g].IsInventoryGroup),
                    headingRow);

                var groupRows = groups[g].Rows;
                for (int i = 0; i < groupRows.Count; i++)
                {
                    var costRow = CreateCurrencyTableRow(groupRows[i], parent, panelWidth, scan);
                    RegisterScrollAnchor(
                        SummarySectionLayoutMath.NonCoinRowAnchorKey(groupRows[i]), costRow);
                }
            }
        }

        private void RegisterScrollAnchor(string key, Control control)
        {
            if (_registerScrollAnchor != null && !string.IsNullOrEmpty(key) && control != null)
            {
                _registerScrollAnchor(key, control);
            }
        }

        /// <summary>
        /// One group heading inside the non-coin table: the face and colour
        /// the column headers above it already wear, on no band of its own.
        /// A heading has to outrank the rows it labels, and the band is
        /// what still separates it from the header row that outranks it -
        /// which tiers get a band is HeaderBands' own doc comment.
        /// Left-anchored on the table's icon gutter, so the section keeps
        /// one left edge, and drawn at the height
        /// SummarySectionLayoutMath.NonCoinTableRowsHeight prices it at.
        /// </summary>
        private Panel CreateNonCoinGroupHeadingRow(
            string text, FlowPanel parent, int panelWidth)
        {
            const int rowHeight = SummarySectionLayoutMath.NonCoinGroupHeadingHeight;
            var rowPanel = new ClippedPanel()
            {
                Size = new Point(panelWidth, rowHeight),
                Parent = parent,
            };
            LabelHelpers.WithDescenderClearance(new Label()
            {
                Text = text,
                Font = HeaderBands.Font,
                TextColor = HeaderBands.LabelColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(
                    SummarySectionLayoutMath.CurrencyIconX,
                    SummarySectionLayoutMath.NonCoinGroupHeadingTextY),
                Parent = rowPanel,
            });

            // Fixed left-anchored text: only the row's own cosmetic width
            // tracks the panel, the same as CreateFootnoteRow.
            _sink.AddRelayout(w => rowPanel.Size = new Point(w, rowHeight));
            return rowPanel;
        }

        /// <summary>
        /// The panelWidth-invariant measurements the header row, every data
        /// row and all of their resize closures re-derive their edges from,
        /// grouped so a sixth cannot be added at one call site and forgotten
        /// at another (ShoppingListSectionRenderer.ColumnScan's shape).
        /// <para>
        /// The three Ink widths are what each header centres OVER; the
        /// shared band places the cells and nothing else. They differ per
        /// column - a wallet Have of 1,204,882 beside a Required of 40 -
        /// which is why one shared width cannot place all three headers.
        /// </para>
        /// </summary>
        private readonly struct CurrencyColumnScan
        {
            internal readonly int NumberBandWidth;
            internal readonly int RequiredInk;
            internal readonly int HaveInk;
            internal readonly int NeededInk;
            internal readonly int MarkerInk;
            internal readonly int MarkerBandWidth;

            internal CurrencyColumnScan(
                int numberBandWidth, int requiredInk, int haveInk, int neededInk,
                int markerInk, int markerBandWidth)
            {
                NumberBandWidth = numberBandWidth;
                RequiredInk = requiredInk;
                HaveInk = haveInk;
                NeededInk = neededInk;
                MarkerInk = markerInk;
                MarkerBandWidth = markerBandWidth;
            }

            internal SummarySectionLayoutMath.CurrencyColumnEdges EdgesFor(int panelWidth)
            {
                return SummarySectionLayoutMath.ComputeCurrencyColumnEdges(
                    panelWidth, NumberBandWidth, MarkerBandWidth);
            }
        }

        /// <summary>
        /// One pass over the rows (a plan's currency list runs to a handful
        /// of entries) in the SAME font the data rows draw in. The shared
        /// number band is floored at the widest of the three header labels
        /// as well as at the widest number in any of them: at the
        /// ColumnHeader tier "Required" routinely out-measures the value
        /// under it, and a band narrower than its own header would let the
        /// currency name run under that header.
        /// </summary>
        private static CurrencyColumnScan ScanCurrencyColumns(List<PlanRowViewModel> rows)
        {
            var font = UiFonts.Body;
            int required = 0, have = 0, needed = 0;
            bool anyCovered = false;
            foreach (var row in rows)
            {
                required = Max(required, MeasureNumber(font, row.Quantity.ToString()));
                have = Max(have, MeasureNumber(font, CurrencyHaveText(row)));
                needed = Max(needed, MeasureNumber(font, CurrencyNeededText(row)));
                anyCovered |= row.CurrencyFullyCovered;
            }

            int band = Max(WidestCurrencyHeaderLabel(), Max(required, Max(have, needed)));
            int markerInk = anyCovered ? LabelHelpers.FullCoverageMarkerWidth() : 0;
            int markerBand = SummarySectionLayoutMath.EffectiveCurrencyMarkerWidth(
                Max(markerInk, MeasureHeader(HeaderBands.Font, StatusHeaderText)));

            return new CurrencyColumnScan(band, required, have, needed, markerInk, markerBand);
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }

        private static int MeasureNumber(BitmapFont font, string text)
        {
            return (int)System.Math.Ceiling(font.MeasureString(text ?? "").Width);
        }

        /// <summary>
        /// Have/Needed already read "-" rather than a fabricated number when
        /// no account snapshot exists - see
        /// PlanRowViewModel.CurrencyOwnedQuantity's doc comment. Spelled
        /// once so the pre-scan and the row can never disagree about what
        /// the column actually draws.
        /// </summary>
        private static string CurrencyHaveText(PlanRowViewModel row)
        {
            return row.CurrencyOwnedQuantity.HasValue ? row.CurrencyOwnedQuantity.Value.ToString() : "-";
        }

        private static string CurrencyNeededText(PlanRowViewModel row)
        {
            return row.CurrencyNeededQuantity.HasValue ? row.CurrencyNeededQuantity.Value.ToString() : "-";
        }

        private const string RequiredHeaderText = "Required";
        private const string HaveHeaderText = "Have";
        private const string NeededHeaderText = "Needed";

        /// <summary>
        /// Names the trailing full-coverage column, which shipped
        /// unlabelled: a reader met a green pill in a column no header
        /// claimed. Not one of CurrencyHeaderLabels below - those three
        /// share one number band and this one has its own.
        /// </summary>
        private const string StatusHeaderText = "Status";

        // The same three strings the header row draws, so the floor they
        // set can never be measured from a label that is no longer there.
        private static readonly string[] CurrencyHeaderLabels =
            {
                RequiredHeaderText, HaveHeaderText, NeededHeaderText,
            };

        /// <summary>
        /// Widest of the currency table's three number-column headers, in
        /// the header font - the floor every number band shares.
        /// </summary>
        private static int WidestCurrencyHeaderLabel()
        {
            var font = HeaderBands.Font;
            int widest = 0;
            for (int i = 0; i < CurrencyHeaderLabels.Length; i++)
            {
                int width = MeasureHeader(font, CurrencyHeaderLabels[i]);
                if (width > widest)
                {
                    widest = width;
                }
            }

            return widest;
        }

        /// <summary>
        /// One currency-table row - header band included - as a single
        /// full-width panel. It used to be two nested panels because the
        /// inner one was the table's centred slice; the table justifies to
        /// the panel now, so the slice was the same size as the row around
        /// it and cost every row a control for nothing.
        /// </summary>
        private static Panel CreateCurrencyRowPanel(
            FlowPanel parent, int panelWidth, int rowHeight, Color background)
        {
            return new ClippedPanel()
            {
                Size = new Point(panelWidth, rowHeight),
                BackgroundColor = background,
                Parent = parent,
            };
        }

        /// <summary>
        /// Open space between the formula bands and the currency table's
        /// header band. An empty Panel, the same way
        /// CraftingPlanView.CreateCollapsibleSection spaces two whole
        /// sections apart: this FlowPanel is SingleTopToBottom with no
        /// control padding, so a gap has to be a row. Counted by
        /// SummarySectionLayoutMath.BodyHeight under the SAME "at least one
        /// CurrencyCost row" condition that gets it built.
        /// </summary>
        private void CreateCurrencyTableTopGap(FlowPanel parent, int panelWidth)
        {
            var gap = new ClippedPanel()
            {
                Size = new Point(panelWidth, SummarySectionLayoutMath.CurrencyTableTopGap),
                Parent = parent,
            };
            _sink.AddRelayout(w =>
                gap.Size = new Point(w, SummarySectionLayoutMath.CurrencyTableTopGap));
        }

        private void CreateCurrencyTableHeaderRow(
            FlowPanel parent, int panelWidth, CurrencyColumnScan scan, string nameHeaderText,
            int rowsHeight)
        {
            var flowBand = HeaderBands.CreateColumnHeaderBandInFlow(parent, panelWidth);
            var band = flowBand.Band;
            var font = HeaderBands.Font;
            LabelHelpers.WithDescenderClearance(new Label()
            {
                Text = nameHeaderText, Font = font, TextColor = HeaderBands.LabelColor,
                AutoSizeWidth = true, AutoSizeHeight = true,
                Location = new Point(
                    ColumnHeaderLabelMath.LabelX(
                        SummarySectionLayoutMath.CurrencyNameX,
                        SummarySectionLayoutMath.CurrencyIconX),
                    HeaderBands.LabelY),
                Parent = band,
            });

            var edges = scan.EdgesFor(panelWidth);

            // Each header centres over its OWN column's widest number and
            // is bounded by the gap to the column beside it, not by the
            // band all three share - see JustifiedColumnTracks.HeaderRoom.
            // Header widths measured from the strings, which are fixed, so
            // the resize closure below neither measures nor reads a Blish
            // Label's Width (not settled until its next layout pass).
            int requiredHeaderWidth = MeasureHeader(font, RequiredHeaderText);
            int haveHeaderWidth = MeasureHeader(font, HaveHeaderText);
            int neededHeaderWidth = MeasureHeader(font, NeededHeaderText);
            int statusHeaderWidth = MeasureHeader(font, StatusHeaderText);
            var xs = new CurrencyHeaderXs(
                edges, scan, requiredHeaderWidth, haveHeaderWidth, neededHeaderWidth, statusHeaderWidth);
            var requiredLabel = CreateHeaderLabelAt(band, RequiredHeaderText, font, xs.Required);
            var haveLabel = CreateHeaderLabelAt(band, HaveHeaderText, font, xs.Have);
            var neededLabel = CreateHeaderLabelAt(band, NeededHeaderText, font, xs.Needed);
            var statusLabel = CreateHeaderLabelAt(band, StatusHeaderText, font, xs.Status);

            // The scan is data-derived, not panelWidth-derived, so it never
            // needs to re-run on resize (same reasoning as
            // ShoppingListSectionRenderer's own cached column scan).
            _sink.AddRelayout(w =>
            {
                flowBand.Resize(w);
                var moved = new CurrencyHeaderXs(
                    scan.EdgesFor(w), scan,
                    requiredHeaderWidth, haveHeaderWidth, neededHeaderWidth, statusHeaderWidth);
                requiredLabel.Location = new Point(moved.Required, HeaderBands.LabelY);
                haveLabel.Location = new Point(moved.Have, HeaderBands.LabelY);
                neededLabel.Location = new Point(moved.Needed, HeaderBands.LabelY);
                statusLabel.Location = new Point(moved.Status, HeaderBands.LabelY);
            });

            // Unlike every other plan table this one does NOT run to the
            // end of its section: the multi-item note and the footnotes
            // flow below it, so the band has to leave with its own last
            // currency row rather than with the section. The height is
            // captured, not measured live: nothing in this table changes
            // height without a rebuild, and the sink reads this closure on
            // every placed frame.
            _sink.TrackStickyBand(flowBand, () => rowsHeight);
        }

        /// <summary>
        /// The four header x's of one render. Each centres over its own
        /// column's widest value and is bounded by the columns either side
        /// of it, never by the band the three number columns share - that
        /// band ends on each column's own right edge, so bounding a header
        /// by it right-aligns the word on its own numbers. Status is the
        /// mirror case: its pills are LEFT-ruled on MarkerX, so its ink
        /// runs rightward from there.
        /// </summary>
        private readonly struct CurrencyHeaderXs
        {
            internal readonly int Required;
            internal readonly int Have;
            internal readonly int Needed;
            internal readonly int Status;

            internal CurrencyHeaderXs(
                SummarySectionLayoutMath.CurrencyColumnEdges edges, CurrencyColumnScan scan,
                int requiredHeaderWidth, int haveHeaderWidth, int neededHeaderWidth,
                int statusHeaderWidth)
            {
                var rooms = SummarySectionLayoutMath.CurrencyHeaderRoomsFor(
                    edges, scan.RequiredInk, scan.HaveInk, scan.NeededInk);
                Required = JustifiedColumnTracks.CenteredOverContentRightAligned(
                    edges.RequiredRightEdge, scan.RequiredInk, requiredHeaderWidth, rooms.Required);
                Have = JustifiedColumnTracks.CenteredOverContentRightAligned(
                    edges.HaveRightEdge, scan.HaveInk, haveHeaderWidth, rooms.Have);
                Needed = JustifiedColumnTracks.CenteredOverContentRightAligned(
                    edges.NeededRightEdge, scan.NeededInk, neededHeaderWidth, rooms.Needed);
                Status = JustifiedColumnTracks.CenteredOverContent(
                    edges.MarkerX,
                    SummarySectionLayoutMath.CurrencyStatusInk(edges, scan.MarkerInk),
                    statusHeaderWidth,
                    rooms.Status);
            }
        }

        private static int MeasureHeader(BitmapFont font, string text)
        {
            return (int)System.Math.Ceiling(font.MeasureString(text ?? "").Width);
        }

        private static Label CreateHeaderLabelAt(Panel band, string text, BitmapFont font, int x)
        {
            return LabelHelpers.WithDescenderClearance(new Label()
            {
                Text = text,
                Font = font,
                TextColor = HeaderBands.LabelColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(x, HeaderBands.LabelY),
                Parent = band,
            });
        }

        private Panel CreateCurrencyTableRow(
            PlanRowViewModel row, FlowPanel parent, int panelWidth, CurrencyColumnScan scan)
        {
            const int rowHeight = CurrencyRowHeight;
            var rowPanel = CreateCurrencyRowPanel(
                parent, panelWidth, rowHeight, Color.Transparent);
            var font = UiFonts.Body;

            if (!string.IsNullOrEmpty(row.IconUrl))
            {
                int iconY = (rowHeight - SummarySectionLayoutMath.CurrencyIconSize) / 2;
                // Framed like every other icon in the module, with the art
                // inset inside the box the unframed icon occupied so the
                // currency column's own x's do not move. The two row kinds
                // this table carries are framed and hovered by the id space
                // they actually belong to - a barter row's id is an ITEM
                // id, which the wallet and /v2/currencies know nothing
                // about (see PlanRowViewModel.IsBarterItemCost).
                IconControls.CreateItemIcon(
                    rowPanel, row.IconUrl,
                    row.IsBarterItemCost
                        ? ItemIconFrame.ForRarity(row.Rarity)
                        : ItemIconFrame.Currency(),
                    SummarySectionLayoutMath.CurrencyIconX, iconY,
                    ItemIconTier.CurrencyListRow,
                    row.IsBarterItemCost
                        ? ItemIconTooltip.ForItem(
                            ItemTooltipIdentity.ForItem(row.Label ?? "", row.IconUrl, row.Rarity),
                            _getItemStatBlock == null || row.ItemId <= 0
                                ? (Func<ItemStatBlock>)null
                                : () => _getItemStatBlock(row.ItemId))
                        // CurrencyOwnedQuantity is already the raw
                        // unclamped wallet holding the game's tooltip
                        // states.
                        : ItemIconTooltip.ForCurrency(
                            row.Label,
                            () => CurrencyTooltipFacts.For(
                                row.Label, row.IconUrl, row.CurrencyDescription,
                                row.CurrencyOwnedQuantity)));
            }

            const int nameX = SummarySectionLayoutMath.CurrencyNameX;
            var edges = scan.EdgesFor(panelWidth);
            int numberColumnWidth = edges.NumberColumnWidth;
            int nameMaxWidth = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                edges.RequiredRightEdge, numberColumnWidth, SummarySectionLayoutMath.CurrencyColumnGap, nameX);
            string fullName = row.Label ?? "";
            string displayName = LabelHelpers.EllipsizeToWidth(font, fullName, nameMaxWidth);
            var nameLabel = LabelHelpers.WithDescenderClearance(new Label()
            {
                Text = displayName, Font = font, TextColor = Color.White,
                AutoSizeWidth = true, AutoSizeHeight = true,
                Location = new Point(nameX, SummarySectionLayoutMath.CurrencyRowTextY),
                Parent = rowPanel,
            });

            // No tooltip on the name or the row: the currency's icon
            // answers for it, and that is the one control on the row that
            // does.
            var numberColor = new Color(220, 220, 220);
            var requiredLabel = LabelHelpers.CreateRightAlignedLabel(rowPanel, row.Quantity.ToString(), font, numberColor, edges.RequiredRightEdge, SummarySectionLayoutMath.CurrencyRowTextY);
            var haveLabel = LabelHelpers.CreateRightAlignedLabel(rowPanel, CurrencyHaveText(row), font, numberColor, edges.HaveRightEdge, SummarySectionLayoutMath.CurrencyRowTextY);
            var neededLabel = LabelHelpers.CreateRightAlignedLabel(rowPanel, CurrencyNeededText(row), font, numberColor, edges.NeededRightEdge, SummarySectionLayoutMath.CurrencyRowTextY);

            Panel marker = null;
            if (row.CurrencyFullyCovered)
            {
                int markerY = (rowHeight - LabelHelpers.SmallTagHeight) / 2;
                marker = LabelHelpers.CreateFullCoverageMarker(rowPanel, edges.MarkerX, markerY);
                // No BasicTooltipText here: CreateSmallTag's inner fill
                // panel + label cover almost the entire pill, so a
                // tooltip on the returned outer Panel alone would be
                // swallowed, and CreateSmallTag does not expose its inner
                // controls to stamp all three. Left off rather than
                // shipped half-working.
            }

            // No CreateRowDivider here: CurrencyRowHeight was never proven
            // immune to the vanishing-divider defect (only 36px rows are
            // proven immune), and moving it to the wallet-list tier's 42px
            // does not change that - it is still an unproven height, and
            // still draws no divider, so RowDividerScissorSimulationTests
            // has nothing to pin for this section. The header row's dark
            // background already delineates the table - introducing a
            // divider at an unproven row height risks resurrecting that
            // defect for a visual element nothing asked for.
            _sink.AddRelayout(w =>
            {
                rowPanel.Size = new Point(w, rowHeight);
                var e = scan.EdgesFor(w);
                requiredLabel.Location = new Point(PlanRelayoutMath.RightAlignedX(e.RequiredRightEdge, requiredLabel.Width), SummarySectionLayoutMath.CurrencyRowTextY);
                haveLabel.Location = new Point(PlanRelayoutMath.RightAlignedX(e.HaveRightEdge, haveLabel.Width), SummarySectionLayoutMath.CurrencyRowTextY);
                neededLabel.Location = new Point(PlanRelayoutMath.RightAlignedX(e.NeededRightEdge, neededLabel.Width), SummarySectionLayoutMath.CurrencyRowTextY);
                if (marker != null)
                {
                    marker.Location = new Point(
                        e.MarkerX, (rowHeight - LabelHelpers.SmallTagHeight) / 2);
                }
            });
            _sink.AddReellipsis(w =>
            {
                var e = scan.EdgesFor(w);
                int newMaxWidth = PlanRelayoutMath.NameMaxWidthBeforeColumn(
                    e.RequiredRightEdge, numberColumnWidth, SummarySectionLayoutMath.CurrencyColumnGap, nameX);
                string newDisplayName = LabelHelpers.EllipsizeToWidth(font, fullName, newMaxWidth);
                if (nameLabel.Text != newDisplayName)
                {
                    nameLabel.Text = newDisplayName;
                }
            });
            return rowPanel;
        }

        // A single subdued footnote row at the bottom of the section -
        // deliberately smaller/dimmer than the plain
        // MultiItemNote banner (TextRowRenderer.CreateTextRow's default
        // styling) so it reads as fine print, not as plan-specific
        // information.
        private static readonly Color FootnoteColor = new Color(130, 130, 130);

        private void CreateFootnoteRow(string text, FlowPanel parent, int panelWidth)
        {
            var rowPanel = new ClippedPanel()
            {
                Size = new Point(panelWidth, PlanContentHeightMath.FallbackTextRowHeight),
                Parent = parent,
            };
            LabelHelpers.WithDescenderClearance(new Label()
            {
                Text = "  " + text,
                Font = UiFonts.Caption,
                TextColor = FootnoteColor,
                AutoSizeWidth = true,
                AutoSizeHeight = true,
                Location = new Point(8, 7),
                Parent = rowPanel,
            });

            // Not width-dependent beyond the row's own cosmetic width (m2
            // 3.6): fixed left-anchored text, same as TextRowRenderer.
            _sink.AddRelayout(w => rowPanel.Size = new Point(w, PlanContentHeightMath.FallbackTextRowHeight));
        }
    }
}
