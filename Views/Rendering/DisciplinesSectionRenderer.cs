using System;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // The Required Disciplines row list, plus the column header row this
    // renderer owns directly.
    //
    // The "Discipline"/"Level" column header call (ColumnHeaderRowRenderer,
    // shared with Required Recipes) lives in this class's Render() below;
    // CraftingPlanView.CreateCollapsibleSection no longer references the
    // column header row for either section.
    //
    // CreateDisciplineRow's
    // divider+relayout tail goes through RowRelayoutHelpers.FinishRow -
    // the shared "row panel resize + extra reposition + divider resize"
    // shape identical across all five extracted renderers' row
    // builders (see that class's doc comment). This row has no icon and no
    // name column at all (just plain Body labels - name, level and the
    // character run), so it does
    // not match IconNameRowHelpers and stays
    // hand-rolled - see IconNameRowHelpers' own doc comment.
    internal sealed class DisciplinesSectionRenderer
    {
        private readonly ISectionRelayoutSink _sink;

        internal DisciplinesSectionRenderer(ISectionRelayoutSink sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>
        /// Column geometry comes from DisciplinesColumnMath, so the header
        /// row, every data row and all of their resize closures anchor the
        /// table identically. The character run needs ONE column X for the
        /// whole section - a per-row X could never line up with a single
        /// header label - and that X clears the widest discipline name in
        /// the section by construction, so it can never make a charLabel
        /// overlap its own nameLabel. The "Characters" header is only added
        /// when at least one row has availability text under it.
        /// <para>
        /// Each header centres over the INK its own cells cover rather than
        /// over the band around it (Services/JustifiedColumnTracks);
        /// Discipline is the exception every table makes for its leftmost
        /// flexing column, whose header stays on the rule its names keep.
        /// </para>
        /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
        /// </summary>
        internal void Render(PlanSectionViewModel section, FlowPanel contentFlow, int panelWidth)
        {
            var font = UiFonts.Body;

            // Body, not Caption: character names were the one text in this
            // row that was both smaller AND greyer than its neighbours, and
            // a name a user reads letter by letter is the worst thing to
            // shrink.
            var charFont = UiFonts.Body;
            int maxNameInk = 0;
            int maxCharInk = 0;
            int maxLevelInk = 0;
            bool anyCharacterText = false;
            for (int i = 0; i < section.Rows.Count; i++)
            {
                maxNameInk = Max(maxNameInk, MeasureWidth(font, section.Rows[i].Label));
                maxLevelInk = Max(maxLevelInk, MeasureWidth(font, section.Rows[i].Sublabel));
                if (!string.IsNullOrEmpty(section.Rows[i].CharacterAvailabilityText))
                {
                    anyCharacterText = true;
                    maxCharInk = Max(
                        maxCharInk, MeasureWidth(charFont, section.Rows[i].CharacterAvailabilityText));
                }
            }

            // Header labels are part of their own columns' BAND widths: at
            // the ColumnHeader tier a header is routinely wider than the
            // data under it, and a column narrower than its own header lets
            // the neighbouring column run under that header. The ink widths
            // above stay unfloored - they are what each header centres over.
            int charHeaderWidth = MeasureWidth(HeaderBands.Font, CharactersHeaderText);
            int levelHeaderWidth = MeasureWidth(HeaderBands.Font, LevelHeaderText);
            int disciplineColumnWidth = Max(
                maxNameInk, MeasureWidth(HeaderBands.Font, DisciplineHeaderText));
            int charColumnWidth = anyCharacterText ? Max(maxCharInk, charHeaderWidth) : 0;

            // The Level band is reserved even when no row carries a level:
            // its header still sits on the pinned edge, and the Characters
            // column's ellipsis budget has to stop short of it.
            int levelColumnWidth = Max(maxLevelInk, levelHeaderWidth);

            Func<int, DisciplinesColumnMath.ColumnEdges> edgesFor = w =>
                DisciplinesColumnMath.ComputeEdges(
                    w, disciplineColumnWidth, charColumnWidth, levelColumnWidth);

            Func<int, int> levelLabelX = w =>
            {
                var e = edgesFor(w);
                DisciplinesColumnMath.HeaderRooms(
                    e, maxNameInk, maxCharInk, maxLevelInk, out _, out var levelRoom);
                return JustifiedColumnTracks.CenteredOverContentRightAligned(
                    e.LevelRightEdge, maxLevelInk, levelHeaderWidth, levelRoom);
            };

            Func<int> rowsHeight =
                () => section.Rows.Count * PlanContentHeightMath.DisciplineRowHeight;

            if (anyCharacterText)
            {
                ColumnHeaderRowRenderer.CreateColumnHeaderRow(
                    contentFlow, panelWidth, DisciplineHeaderText, 8, LevelHeaderText, _sink,
                    middleHeaders: new[]
                    {
                        new ColumnHeaderRowRenderer.MiddleHeader(CharactersHeaderText, w =>
                        {
                            var e = edgesFor(w);
                            DisciplinesColumnMath.HeaderRooms(
                                e, maxNameInk, maxCharInk, maxLevelInk, out var charRoom, out _);
                            return JustifiedColumnTracks.CenteredOverContent(
                                e.CharX, maxCharInk, charHeaderWidth, charRoom);
                        }),
                    },
                    rightLabelXForWidth: levelLabelX,
                    rowsHeight: rowsHeight);
            }
            else
            {
                ColumnHeaderRowRenderer.CreateColumnHeaderRow(
                    contentFlow, panelWidth, DisciplineHeaderText, 8, LevelHeaderText, _sink,
                    rightLabelXForWidth: levelLabelX,
                    rowsHeight: rowsHeight);
            }

            for (int i = 0; i < section.Rows.Count; i++)
            {
                CreateDisciplineRow(
                    section.Rows[i], contentFlow, panelWidth, i == section.Rows.Count - 1, edgesFor);
            }
        }

        private static int Max(int a, int b)
        {
            return a > b ? a : b;
        }

        private const string LevelHeaderText = "Level";
        private const string CharactersHeaderText = "Characters";
        private const string DisciplineHeaderText = "Discipline";

        private static int MeasureWidth(BitmapFont font, string text)
        {
            return (int)Math.Ceiling(font.MeasureString(text ?? "").Width);
        }

        // The character-availability label sits between the discipline
        // name and the Level column, on the one column X Render() derived
        // for the whole section - see its doc comment. That X clears the
        // widest discipline name in the section, so charLabel can never
        // overlap nameLabel here.
        private void CreateDisciplineRow(
            PlanRowViewModel row, FlowPanel parent, int panelWidth, bool isLast,
            Func<int, DisciplinesColumnMath.ColumnEdges> edgesFor)
        {
            const int rowHeight = PlanContentHeightMath.DisciplineRowHeight;
            var rowPanel = new ClippedPanel() { Size = new Point(panelWidth, rowHeight), Parent = parent };
            var font = UiFonts.Body;
            var edges = edgesFor(panelWidth);

            LabelHelpers.WithDescenderClearance(
                new Label()
                {
                    Text = row.Label ?? "", Font = font,
                    AutoSizeWidth = true, AutoSizeHeight = true,
                    Location = new Point(DisciplinesColumnMath.NameX, 7), Parent = rowPanel,
                });
            var levelLabel = LabelHelpers.CreateRightAlignedLabel(
                rowPanel, row.Sublabel, font, Color.White, edges.LevelRightEdge, 7);

            // "Anna (500), Bob (400/450)" - secondary text sitting
            // between the discipline name and the right-aligned Level
            // column, ellipsized to whatever room is left (same
            // EllipsizeToWidth + tooltip-on-truncate convention as
            // UsedMaterialsSectionRenderer.CreateUsedMaterialRow's name
            // column). Both its x and its budget move with the panel now
            // that the columns distribute, so it repositions as well as
            // re-ellipsizes. Entirely skipped when
            // row.CharacterAvailabilityText is null (the snapshot never
            // captured this data - see that field's own doc comment): no
            // label, no tooltip, no claim either way.
            var charFont = UiFonts.Body;
            string fullCharText = row.CharacterAvailabilityText;
            Label charLabel = null;
            if (!string.IsNullOrEmpty(fullCharText))
            {
                string charDisplayText = LabelHelpers.EllipsizeToWidth(
                    charFont, fullCharText, edges.CharBandWidth);
                // White, like every other content cell in the module's
                // tables: these are character names, the only text in this
                // row a user picks the letters out of, and the row already
                // reads as secondary without dimming them.
                charLabel = LabelHelpers.WithDescenderClearance(
                    new Label()
                    {
                        Text = charDisplayText, Font = charFont, TextColor = Color.White,
                        AutoSizeWidth = true, AutoSizeHeight = true,
                        Location = new Point(edges.CharX, 9), Parent = rowPanel,
                    });
                if (charLabel.Text != fullCharText)
                {
                    // Both controls: the label captures the hover before
                    // the row panel under it is ever reached, so a tooltip
                    // on the panel alone only fires on the blank strip
                    // beside the very text it exists to expand.
                    StampCharacterTooltip(rowPanel, charLabel, fullCharText);
                }
            }

            // M36b: bottomClearance 1 - DisciplineRowHeight was 32, which
            // that investigation's simulation found VULNERABLE to the
            // Container.Paint round-trip defect (see
            // LabelHelpers.CreateRowDivider's doc comment) at the same
            // ~10.2% vanish rate as the 44px rows, then omitted from its
            // fix list by oversight. The +2pt body bump raised the row to
            // 36, which the same simulation proves immune, so the clearance
            // is now belt-and-braces rather than the fix. It stays because
            // it costs nothing: no icon in this row (a Body name and level
            // at y=7, a Body character line at y=9, lowest ink y=30), so
            // the divider top (rowHeight - 3 = 33) sits well clear either
            // way.
            RowRelayoutHelpers.FinishRow(
                rowPanel, panelWidth, rowHeight, isLast, 1, _sink,
                w =>
                {
                    var e = edgesFor(w);
                    levelLabel.Location = new Point(
                        PlanRelayoutMath.RightAlignedX(e.LevelRightEdge, levelLabel.Width), 7);
                    if (charLabel != null)
                    {
                        charLabel.Location = new Point(e.CharX, 9);
                    }
                });

            if (charLabel != null)
            {
                _sink.AddReellipsis(w =>
                {
                    string newDisplayText = LabelHelpers.EllipsizeToWidth(
                        charFont, fullCharText, edgesFor(w).CharBandWidth);
                    if (charLabel.Text != newDisplayText)
                    {
                        charLabel.Text = newDisplayText;
                        StampCharacterTooltip(
                            rowPanel, charLabel, newDisplayText != fullCharText ? fullCharText : null);
                    }
                });
            }
        }

        /// <summary>
        /// The truncated character run's full text, on the label AND the
        /// row panel under it. Null is a deliberate clear (see
        /// TooltipFacility.ApplyPlain), which is what an untruncated run
        /// after a widening drag needs.
        /// </summary>
        private static void StampCharacterTooltip(Panel rowPanel, Label charLabel, string fullCharText)
        {
            TooltipFacility.ApplyPlain(rowPanel, fullCharText);
            TooltipFacility.ApplyPlain(charLabel, fullCharText);
        }
    }
}
