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
    // The Required Recipes table: Recipe (flex) | Discipline | Status,
    // every row one line at RecipeRowHeight.
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
        private readonly Func<int, ItemStatBlock> _getItemStatBlock;

        internal RecipesSectionRenderer(
            ISectionRelayoutSink sink, Func<int, ItemStatBlock> getItemStatBlock = null)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _getItemStatBlock = getItemStatBlock;
        }

        // Left x of the name column (past the row's tier-2 framed icon at
        // x=8, plus an 8px gap), shared by the header and every row.
        private const int IconX = 8;
        private const int NameX = IconX + PlanContentHeightMath.RowIconFrameSize + 8;
        private const string RecipeHeaderText = "Recipe";
        private const string StatusHeaderText = "Status";
        private const string DisciplineHeaderText = "Discipline";

        /// <summary>
        /// One pass over the rows for the two right-hand BAND widths, then
        /// the header and the rows, all anchored through the same
        /// RecipesColumnMath call.
        /// <para>
        /// Each band is max(widest data, its own header label): the header
        /// centres over the band its own cells occupy, and at the
        /// ColumnHeader tier "Discipline" out-measures a short "Chef 400" -
        /// a band narrower than its own header would let the column beside
        /// it run underneath that header. The Discipline
        /// column is reserved only when some row actually has one (a
        /// mystic-forge-only recipe list has no disciplines at all), the
        /// same gate Required Disciplines puts on its Characters column.
        /// </para>
        /// </summary>
        internal void Render(PlanSectionViewModel section, FlowPanel contentFlow, int panelWidth)
        {
            var font = UiFonts.Body;
            var headerFont = HeaderBands.Font;

            int statusInk = 0;
            int disciplineInk = 0;
            bool anyDiscipline = false;
            for (int i = 0; i < section.Rows.Count; i++)
            {
                var row = section.Rows[i];

                int statusWidth = MeasureWidth(font, row.StatusTag);
                if (statusWidth > statusInk)
                {
                    statusInk = statusWidth;
                }

                if (string.IsNullOrEmpty(row.Sublabel))
                {
                    continue;
                }

                anyDiscipline = true;
                int disciplineWidth = MeasureWidth(font, row.Sublabel);
                if (disciplineWidth > disciplineInk)
                {
                    disciplineInk = disciplineWidth;
                }
            }

            // Bands stay floored at their own header label - a band
            // narrower than the word over it would let the column beside it
            // run underneath - while the ink above stays unfloored, because
            // the ink is what the headers centre over.
            int statusColumnWidth = Max(statusInk, MeasureWidth(headerFont, StatusHeaderText));
            int disciplineColumnWidth = anyDiscipline
                ? Max(disciplineInk, MeasureWidth(headerFont, DisciplineHeaderText))
                : 0;

            var scan = new ColumnScan(statusColumnWidth, disciplineColumnWidth);

            // Both data headers centre over the INK their own cells cover
            // rather than sharing an edge with them, and are bounded by the
            // columns either side rather than by their own bands - the
            // module's header law, JustifiedColumnTracks.HeaderRoom. Only
            // Recipe stays on a rule: it is the flexing column, and its
            // rows start with the icon its header rules on
            // (Services/ColumnHeaderLabelMath).
            int disciplineHeaderWidth = MeasureWidth(headerFont, DisciplineHeaderText);
            int statusHeaderWidth = MeasureWidth(headerFont, StatusHeaderText);
            Func<int, int> statusLabelX = w =>
            {
                var e = scan.EdgesFor(w);
                RecipesColumnMath.HeaderRooms(e, disciplineInk, statusInk, out _, out var statusRoom);
                return JustifiedColumnTracks.CenteredOverContentRightAligned(
                    e.StatusRightEdge, statusInk, statusHeaderWidth, statusRoom);
            };

            Func<int> rowsHeight =
                () => section.Rows.Count * PlanContentHeightMath.RecipeRowHeight;

            if (anyDiscipline)
            {
                ColumnHeaderRowRenderer.CreateColumnHeaderRow(
                    contentFlow, panelWidth, RecipeHeaderText,
                    ColumnHeaderLabelMath.LabelX(NameX, IconX), StatusHeaderText, _sink,
                    middleLabel: DisciplineHeaderText,
                    middleXForWidth: w =>
                    {
                        var e = scan.EdgesFor(w);
                        RecipesColumnMath.HeaderRooms(
                            e, disciplineInk, statusInk, out var disciplineRoom, out _);
                        return JustifiedColumnTracks.CenteredOverContent(
                            e.DisciplineX, disciplineInk, disciplineHeaderWidth, disciplineRoom);
                    },
                    rightLabelXForWidth: statusLabelX,
                    rowsHeight: rowsHeight);
            }
            else
            {
                ColumnHeaderRowRenderer.CreateColumnHeaderRow(
                    contentFlow, panelWidth, RecipeHeaderText,
                    ColumnHeaderLabelMath.LabelX(NameX, IconX), StatusHeaderText, _sink,
                    rightLabelXForWidth: statusLabelX,
                    rowsHeight: rowsHeight);
            }

            for (int i = 0; i < section.Rows.Count; i++)
            {
                CreateRecipeRow(
                    section.Rows[i], contentFlow, panelWidth, scan, i == section.Rows.Count - 1);
            }
        }

        /// <summary>
        /// The two data-derived (panelWidth-invariant) band widths every
        /// row and header closure needs to recompute its column edges -
        /// grouped so a third cannot be added to one call site and
        /// forgotten at another. Mirrors the Shopping List's own ColumnScan.
        /// </summary>
        private readonly struct ColumnScan
        {
            private readonly int _statusColumnWidth;
            private readonly int _disciplineColumnWidth;

            internal ColumnScan(int statusColumnWidth, int disciplineColumnWidth)
            {
                _statusColumnWidth = statusColumnWidth;
                _disciplineColumnWidth = disciplineColumnWidth;
            }

            internal RecipesColumnMath.ColumnEdges EdgesFor(int panelWidth)
            {
                return RecipesColumnMath.ComputeEdges(
                    panelWidth, _statusColumnWidth, _disciplineColumnWidth, NameX);
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

            // A context action
            // (right-click), not a visible icon - the row already packs an
            // icon/name/optional sublabel/right-aligned status tag into a
            // fixed height with no spare column.
            // Right-click also cannot collide with the row's existing
            // interactions (this row has none - unlike the Recipe Tree,
            // Required Recipes rows are not expand/collapse toggles), and
            // is naturally low-accidental-click-risk for a click that
            // steals focus into the default browser.
            //
            // Fix-pass (right-click-as-camera-drag): mirrors
            // TreeSectionController's identical fix - GW2's own right-drag
            // is the camera-rotate gesture, so firing on button-DOWN alone
            // opened the browser and yanked focus out of a fullscreen game
            // the instant a drag begun over this row went down, with no
            // way to abort. A bare switch to RightMouseButtonReleased is
            // not sufficient either: Blish routes the release event to
            // whichever row is under the cursor at release time, so a drag
            // that started on a DIFFERENT row would open THIS row's page.
            // Pairing press+release on this SAME rowPanel closes that:
            // press arms a per-row flag, and only this row's own Released
            // handler (which only fires when the release also lands on
            // this row) can consume it; MouseLeft disarms the flag as soon
            // as the cursor leaves this row after a press, so a stale arm
            // from an earlier aborted drag can't be replayed by an
            // unrelated release later landing back on this row.
            string wikiHint = null;
            if (!string.IsNullOrEmpty(row.WikiUrl))
            {
                string wikiUrl = row.WikiUrl;
                bool wikiLinkArmed = false;
                rowPanel.RightMouseButtonPressed += (_, __) => wikiLinkArmed = true;
                rowPanel.MouseLeft += (_, __) => wikiLinkArmed = false;
                rowPanel.RightMouseButtonReleased += (_, __) =>
                {
                    if (wikiLinkArmed)
                    {
                        wikiLinkArmed = false;
                        WikiLinkLauncher.Open(wikiUrl);
                    }
                };
                wikiHint = TreeRowTooltipComposer.WikiHintText;
            }

            int itemId = row.ItemId;
            string hintLine = wikiHint;
            var hover = ItemIconTooltip.ForItem(
                ItemTooltipIdentity.ForItem(row.Label ?? "", row.IconUrl, row.Rarity),
                _getItemStatBlock == null || itemId <= 0 ? (Func<ItemStatBlock>)null
                    : () => _getItemStatBlock(itemId),
                () => hintLine == null ? null : new List<string> { hintLine });

            IconControls.CreateItemIcon(
                rowPanel, row.IconUrl, ItemIconFrame.ForRarity(row.Rarity),
                IconX, PlanContentHeightMath.IconRowIconY, ItemIconTier.BagSidebar, hover);

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

            Label statusLabel = null;
            if (!string.IsNullOrEmpty(row.StatusTag))
            {
                Color statusColor = Color.White;
                if (row.StatusTag == "Missing!")
                {
                    statusColor = new Color(255, 100, 100);
                }
                else if (row.StatusTag == "Auto-learned")
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

        // 12, not the pre-tier-2 8: the icon frame's center moved down 4px
        // with the 34 -> 42 resize, and the reading line (name, discipline,
        // status) keeps its offset from that center.
        private const int NameY = PlanContentHeightMath.IconRowIconY + 12;
    }
}
