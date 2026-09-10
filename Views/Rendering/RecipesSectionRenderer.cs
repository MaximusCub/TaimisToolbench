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
    // The Required Recipes table:
    // Recipe (flex) | Discipline | Sheet cost | Status, every row one line
    // at RecipeRowHeight.
    //
    // The discipline used to be row.Sublabel, a second Caption line under
    // the name, which is why this section carried a second (48px) row
    // height. It is a real column now - Body, never smaller than the name
    // beside it - so the tall row variant and its height constant are
    // both gone, and the section is shorter despite the taller chrome
    // above it. RecipesColumnMath owns the edge arithmetic (Blish-free,
    // tested); this file only measures the bands it is handed.
    //
    // Render() calls ColumnHeaderRowRenderer (the shared header, also used by
    // Required Disciplines) directly, exactly as DisciplinesSectionRenderer
    // does - see that class's doc comment.
    //
    // CreateRecipeRow's
    // divider+relayout tail goes through RowRelayoutHelpers.FinishRow -
    // the shared "row panel resize + extra reposition + divider resize"
    // shape identical across all five extracted renderers' row
    // builders (see that class's doc comment). This row's name label is
    // NOT run through IconNameRowHelpers: this row's name budget stops at
    // the Discipline column rather than at a right-aligned value band, and
    // its truncation tooltip composes with a wiki hint the shared helper
    // knows nothing about - see IconNameRowHelpers' own doc comment.
    internal sealed class RecipesSectionRenderer
    {
        private readonly ISectionRelayoutSink _sink;
        private readonly Func<int, ItemTooltipFacts> _getItemFacts;
        private readonly Func<int, CurrencyTooltipFacts> _getCurrencyFacts;

        internal RecipesSectionRenderer(
            ISectionRelayoutSink sink, Func<int, ItemTooltipFacts> getItemFacts,
            Func<int, CurrencyTooltipFacts> getCurrencyFacts)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _getItemFacts = getItemFacts ?? throw new ArgumentNullException(nameof(getItemFacts));
            _getCurrencyFacts = getCurrencyFacts ?? throw new ArgumentNullException(nameof(getCurrencyFacts));
        }

        // Left x of the name column (past the row's tier-2 framed icon at
        // x=8, plus an 8px gap), shared by the header and every row.
        private const int IconX = 8;
        private const int NameX = IconX + PlanContentHeightMath.RowIconFrameSize + 8;
        private const string RecipeHeaderText = "Recipe";
        private const string StatusHeaderText = "Status";
        private const string DisciplineHeaderText = "Discipline";
        private const string SheetCostHeaderText = "Sheet cost";

        // Gap between the bartered-item words and the coin/currency run
        // that follows them in the same cell, on the one sheet whose price
        // is both. The between-segment gap a coin run already keeps
        // internally, so the whole cell reads at one rhythm.
        private const int SheetCostPartGap = CoinSegmentMath.CoinSegmentGap;

        /// <summary>
        /// One pass over the rows for every data-derived BAND width and the
        /// widest recipe name, then the header and the rows, all anchored
        /// through the same RecipesColumnMath call.
        /// <para>
        /// Each band is max(widest data, its own header label): the header
        /// centres over the band its own cells occupy, and at the
        /// ColumnHeader tier "Discipline" out-measures a short "Chef 400" -
        /// a band narrower than its own header would let the column beside
        /// it run underneath that header. Discipline and Sheet cost are
        /// each reserved only when some row actually fills them (a
        /// mystic-forge-only recipe list has no disciplines at all; a plan
        /// missing no recipe has no sheet to price), the same gate Required
        /// Disciplines puts on its Characters column.
        /// </para>
        /// </summary>
        internal void Render(PlanSectionViewModel section, FlowPanel contentFlow, int panelWidth)
        {
            var font = UiFonts.Body;
            var headerFont = HeaderBands.Font;

            int statusInk = 0;
            int disciplineInk = 0;
            int sheetCostInk = 0;
            int nameInk = 0;
            bool anyDiscipline = false;
            bool anySheetCost = false;
            for (int i = 0; i < section.Rows.Count; i++)
            {
                var row = section.Rows[i];

                statusInk = Max(statusInk, MeasureWidth(font, row.StatusTag));
                nameInk = Max(nameInk, MeasureWidth(font, row.Label));

                int sheetCostWidth = SheetCostWidth(row, font);
                if (sheetCostWidth > 0)
                {
                    anySheetCost = true;
                    sheetCostInk = Max(sheetCostInk, sheetCostWidth);
                }

                if (string.IsNullOrEmpty(row.Sublabel))
                {
                    continue;
                }

                anyDiscipline = true;
                disciplineInk = Max(disciplineInk, MeasureWidth(font, row.Sublabel));
            }

            // Bands stay floored at their own header label - a band
            // narrower than the word over it would let the column beside it
            // run underneath - while the ink above stays unfloored, because
            // the ink is what the headers centre over.
            int statusColumnWidth = Max(statusInk, MeasureWidth(headerFont, StatusHeaderText));
            int disciplineColumnWidth = anyDiscipline
                ? Max(disciplineInk, MeasureWidth(headerFont, DisciplineHeaderText))
                : 0;
            int sheetCostColumnWidth = anySheetCost
                ? Max(sheetCostInk, MeasureWidth(headerFont, SheetCostHeaderText))
                : 0;

            var scan = new ColumnScan(
                statusColumnWidth, disciplineColumnWidth, sheetCostColumnWidth, nameInk);

            // Every data header centres over the INK its own cells cover
            // rather than sharing an edge with them, and is bounded by the
            // columns either side rather than by its own band - the
            // module's header law, JustifiedColumnTracks.HeaderRoom. Only
            // Recipe stays on a rule: it is the flexing column, and its
            // rows start with the icon its header rules on
            // (Services/ColumnHeaderLabelMath).
            int disciplineHeaderWidth = MeasureWidth(headerFont, DisciplineHeaderText);
            int sheetCostHeaderWidth = MeasureWidth(headerFont, SheetCostHeaderText);
            int statusHeaderWidth = MeasureWidth(headerFont, StatusHeaderText);
            Func<int, int> statusLabelX = w =>
            {
                var e = scan.EdgesFor(w);
                RecipesColumnMath.HeaderRooms(
                    e, disciplineInk, sheetCostInk, statusInk, out _, out _, out var statusRoom);
                return JustifiedColumnTracks.CenteredOverContentRightAligned(
                    e.StatusRightEdge, statusInk, statusHeaderWidth, statusRoom);
            };

            var middleHeaders = new List<ColumnHeaderRowRenderer.MiddleHeader>(2);
            if (anyDiscipline)
            {
                middleHeaders.Add(new ColumnHeaderRowRenderer.MiddleHeader(
                    DisciplineHeaderText,
                    w =>
                    {
                        var e = scan.EdgesFor(w);
                        RecipesColumnMath.HeaderRooms(
                            e, disciplineInk, sheetCostInk, statusInk,
                            out var disciplineRoom, out _, out _);
                        return JustifiedColumnTracks.CenteredOverContent(
                            e.DisciplineX, disciplineInk, disciplineHeaderWidth, disciplineRoom);
                    }));
            }

            if (anySheetCost)
            {
                middleHeaders.Add(new ColumnHeaderRowRenderer.MiddleHeader(
                    SheetCostHeaderText,
                    w =>
                    {
                        var e = scan.EdgesFor(w);
                        RecipesColumnMath.HeaderRooms(
                            e, disciplineInk, sheetCostInk, statusInk,
                            out _, out var sheetCostRoom, out _);
                        return JustifiedColumnTracks.CenteredOverContentRightAligned(
                            e.SheetCostRightEdge, sheetCostInk, sheetCostHeaderWidth, sheetCostRoom);
                    }));
            }

            Func<int> rowsHeight =
                () => section.Rows.Count * PlanContentHeightMath.RecipeRowHeight;

            ColumnHeaderRowRenderer.CreateColumnHeaderRow(
                contentFlow, panelWidth, RecipeHeaderText,
                ColumnHeaderLabelMath.LabelX(NameX, IconX), StatusHeaderText, _sink,
                middleHeaders: middleHeaders.Count > 0 ? middleHeaders : null,
                rightLabelXForWidth: statusLabelX,
                rowsHeight: rowsHeight);

            for (int i = 0; i < section.Rows.Count; i++)
            {
                CreateRecipeRow(
                    section.Rows[i], contentFlow, panelWidth, scan, i == section.Rows.Count - 1);
            }
        }

        /// <summary>
        /// Width of one row's whole Sheet cost cell: the bartered-item
        /// words, then the coin and currency run, with a gap between them
        /// when the price is both. 0 for a row that has no sheet price.
        /// Measured through CoinCurrencyRenderer.MeasureValueWidth, the
        /// same path the cell is laid out by, so the band this reserves can
        /// never differ from the run that lands in it.
        /// </summary>
        private static int SheetCostWidth(PlanRowViewModel row, BitmapFont font)
        {
            int valueWidth = SheetValueWidth(row, font);
            int barterWidth = MeasureWidth(font, row.SheetBarterText);
            if (valueWidth > 0 && barterWidth > 0)
            {
                return barterWidth + SheetCostPartGap + valueWidth;
            }

            return valueWidth + barterWidth;
        }

        /// <summary>
        /// Width of the coin and currency half alone, 0 when the sheet
        /// costs neither. Resolved ONCE per row at build time and carried
        /// into the relayout closure: MeasureValueWidth builds segment
        /// lists and calls MeasureString, and that closure is replayed on
        /// every frame of a resize drag.
        /// </summary>
        private static int SheetValueWidth(PlanRowViewModel row, BitmapFont font)
        {
            return row.CoinValue > 0 || (row.CurrencyCosts != null && row.CurrencyCosts.Count > 0)
                ? CoinCurrencyRenderer.MeasureValueWidth(row.CoinValue, row.CurrencyCosts, font)
                : 0;
        }

        /// <summary>
        /// The data-derived (panelWidth-invariant) band widths every row
        /// and header closure needs to recompute its column edges -
        /// grouped so one cannot be added to a call site and forgotten at
        /// another. Mirrors the Shopping List's own ColumnScan.
        /// </summary>
        private readonly struct ColumnScan
        {
            private readonly int _statusColumnWidth;
            private readonly int _disciplineColumnWidth;
            private readonly int _sheetCostColumnWidth;
            private readonly int _maxNameWidth;

            internal ColumnScan(
                int statusColumnWidth, int disciplineColumnWidth, int sheetCostColumnWidth,
                int maxNameWidth)
            {
                _statusColumnWidth = statusColumnWidth;
                _disciplineColumnWidth = disciplineColumnWidth;
                _sheetCostColumnWidth = sheetCostColumnWidth;
                _maxNameWidth = maxNameWidth;
            }

            internal RecipesColumnMath.ColumnEdges EdgesFor(int panelWidth)
            {
                return RecipesColumnMath.ComputeEdges(
                    panelWidth, _statusColumnWidth, _disciplineColumnWidth,
                    _sheetCostColumnWidth, _maxNameWidth, NameX);
            }
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }

        private static int MeasureWidth(BitmapFont font, string text)
        {
            return (int)Math.Ceiling(font.MeasureString(text ?? "").Width);
        }

        // rowHeight 45 = the tier-2 rarity-framed icon (42) at y=0 plus
        // the 2px divider plus the clearance pixel the height derivation
        // absorbs: an exact, non-overlapping fit, the same one Used
        // Materials and the Shopping List have. There is no second
        // row height any more - the discipline is a column, so no row is
        // two lines tall.
        private void CreateRecipeRow(
            PlanRowViewModel row, FlowPanel parent, int panelWidth,
            ColumnScan scan, bool isLast)
        {
            const int rowHeight = PlanContentHeightMath.RecipeRowHeight;
            var edges = scan.EdgesFor(panelWidth);

            var rowPanel = new ClippedPanel() { Size = new Point(panelWidth, rowHeight), Parent = parent };

            string hintLine = row.HintText;
            IconControls.DrawItemIcon(
                rowPanel, row.ItemId, IconX, PlanContentHeightMath.IconRowIconY,
                ItemIconTier.BagSidebar, _getItemFacts,
                () => string.IsNullOrEmpty(hintLine) ? null : new List<string> { hintLine });

            var font = UiFonts.Body;
            string fullName = row.Label ?? "";
            var nameLabel = LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = LabelHelpers.EllipsizeToWidth(font, fullName, edges.NameMaxWidth),
                    Font = font,
                    TextColor = RarityColors.GetRarityNameColor(row.Rarity),
                    ShowShadow = true,
                    ShadowColor = Color.Black * 0.8f,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(NameX, NameY),
                    Parent = rowPanel,
                });

            Label disciplineLabel = null;
            if (!string.IsNullOrEmpty(row.Sublabel))
            {
                // Body and left-ruled at the column's x, not a Caption
                // line under the name: a discipline is a name the reader
                // picks the letters of, and the locked rule is that such
                // text is never smaller than the text beside it. It keeps
                // its muted colour - one channel of de-emphasis, not two.
                disciplineLabel = LabelHelpers.WithDescenderClearance(
                    new Label()
                    {
                        Text = row.Sublabel,
                        Font = font,
                        TextColor = new Color(170, 170, 170),
                        AutoSizeWidth = true,
                        AutoSizeHeight = true,
                        Location = new Point(edges.DisciplineX, NameY),
                        Parent = rowPanel,
                    });
            }

            // Right-aligned on the column's own edge: the bartered items
            // as words, then the coin and currency run, so the coin icons
            // stay to the RIGHT of their numbers as the repo requires and
            // the words never sit between a number and its icon.
            Label barterLabel = null;
            CoinCurrencyRenderer.ValueCellHandle sheetCostCell = null;
            int sheetValueWidth = SheetValueWidth(row, font);
            if (edges.HasSheetCost)
            {
                if (sheetValueWidth > 0)
                {
                    sheetCostCell = CoinCurrencyRenderer.RenderValueCellRightAligned(
                        rowPanel, row.CoinValue, row.CurrencyCosts,
                        edges.SheetCostRightEdge, NameY, font, _getCurrencyFacts);
                }

                if (!string.IsNullOrEmpty(row.SheetBarterText))
                {
                    barterLabel = LabelHelpers.CreateRightAlignedLabel(
                        rowPanel, row.SheetBarterText, font, Color.White,
                        BarterRightEdge(edges.SheetCostRightEdge, sheetValueWidth), NameY);
                }
            }

            Label statusLabel = null;
            if (!string.IsNullOrEmpty(row.StatusTag))
            {
                Color statusColor = Color.White;
                if (row.StatusTag == RequiredRecipesVisibility.MissingStatusTag)
                {
                    statusColor = new Color(255, 100, 100);
                }
                else if (row.StatusTag == RequiredRecipesVisibility.AutoLearnedStatusTag)
                {
                    statusColor = new Color(150, 200, 150);
                }

                statusLabel = LabelHelpers.CreateRightAlignedLabel(
                    rowPanel, row.StatusTag, font, statusColor, edges.StatusRightEdge, NameY);
            }

            // IconRowDividerClearance: RecipeRowHeight (45) absorbs the
            // clearance pixel in its own derivation, so the divider
            // (42..44) sits exactly flush under the 0..42 icon frame - see
            // the identical note in CreateUsedMaterialRow and the re-run
            // simulation behind LabelHelpers.CreateRowDivider.
            RowRelayoutHelpers.FinishRow(
                rowPanel, panelWidth, rowHeight, isLast,
                PlanContentHeightMath.IconRowDividerClearance, _sink,
                w =>
                {
                    var e = scan.EdgesFor(w);
                    if (disciplineLabel != null)
                    {
                        disciplineLabel.Location = new Point(e.DisciplineX, NameY);
                    }

                    if (sheetCostCell != null)
                    {
                        CoinCurrencyRenderer.RepositionValueCellRightAligned(
                            sheetCostCell, e.SheetCostRightEdge, NameY);
                    }

                    if (barterLabel != null)
                    {
                        barterLabel.Location = new Point(
                            PlanRelayoutMath.RightAlignedX(
                                BarterRightEdge(e.SheetCostRightEdge, sheetValueWidth),
                                barterLabel.Width),
                            NameY);
                    }

                    if (statusLabel != null)
                    {
                        statusLabel.Location = new Point(
                            PlanRelayoutMath.RightAlignedX(e.StatusRightEdge, statusLabel.Width), NameY);
                    }
                });
            _sink.AddReellipsis(w =>
            {
                string newDisplayName = LabelHelpers.EllipsizeToWidth(
                    font, fullName, scan.EdgesFor(w).NameMaxWidth);
                if (nameLabel.Text != newDisplayName)
                {
                    nameLabel.Text = newDisplayName;
                }
            });
        }

        /// <summary>
        /// Right edge the bartered-item words end on: the cell's own right
        /// edge, less the coin and currency run that follows them and the
        /// gap between the two halves. Off the SAME
        /// <see cref="SheetValueWidth"/> the band was reserved from, so the
        /// two halves can never overlap.
        /// </summary>
        private static int BarterRightEdge(int sheetCostRightEdge, int sheetValueWidth)
        {
            return sheetValueWidth > 0
                ? sheetCostRightEdge - sheetValueWidth - SheetCostPartGap
                : sheetCostRightEdge;
        }

        // 12, not the pre-tier-2 8: the icon frame's center moved down 4px
        // with the 34 -> 42 resize, and the reading line (name, discipline,
        // status) keeps its offset from that center.
        private const int NameY = PlanContentHeightMath.IconRowIconY + 12;
    }
}
