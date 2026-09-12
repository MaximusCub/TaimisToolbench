using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using System;

namespace TaimisToolbench.Views.Rendering
{
    // Factors the "row divider +
    // width-only relayout closure" tail that closes every row builder in
    // CraftStepsSectionRenderer, DisciplinesSectionRenderer,
    // RecipesSectionRenderer, ShoppingListSectionRenderer, and
    // UsedMaterialsSectionRenderer - confirmed byte-identical in shape
    // across all five:
    //   Panel divider = isLast ? null : LabelHelpers.CreateRowDivider(rowPanel, panelWidth, rowHeight, bottomClearance);
    //   sink.AddRelayout(w =>
    //   {
    //       rowPanel.Size = new Point(w, rowHeight);
    //       <the row's own per-control repositioning, whatever it is>
    //       if (divider != null) divider.Size = new Point(w, 2);
    //   });
    // Only the bracketed line varies per row (a different set of controls
    // to reposition) - that stays the caller's own responsibility, passed
    // in as extraRelayout, invoked at the exact same point in the closure
    // every pre-extraction caller already ran it (after the rowPanel resize,
    // before the divider resize). LabelHelpers.CreateRowDivider itself - its
    // divider math and the M36b bottom-clearance calls - is called exactly
    // as before, unedited; this only wraps the
    // surrounding boilerplate, not the divider's own arithmetic.
    //
    // SummarySectionRenderer.CreateCurrencyTableRow adopted this when the
    // Total Cost table gained the rules its neighbours already drew. Its
    // cost-tile band still does not: a tile is not a list row and has no
    // rule between tiles.
    internal static class RowRelayoutHelpers
    {
        /// <summary>
        /// Creates the row's trailing divider (skipped when isLast) and
        /// registers the width-only AddRelayout closure that resizes
        /// rowPanel, runs the row's own extraRelayout repositioning, then
        /// resizes the divider - in that order, matching every
        /// pre-extraction row builder's own closure exactly. extraRelayout
        /// may be null for a row with nothing else to reposition, which is
        /// what the Used Materials row passes: every x on it is derived
        /// from the render's own Amount band, not from the panel width.
        /// <para>
        /// The rule spans the whole row: every table's right-hand block is
        /// pinned one PlanRelayoutMath.TableRightMargin in from the panel
        /// edge, so a full-width rule ends exactly where the table does.
        /// </para>
        /// </summary>
        internal static void FinishRow(
            Panel rowPanel, int panelWidth, int rowHeight, bool isLast, int bottomClearance,
            ISectionRelayoutSink sink, Action<int> extraRelayout)
        {
            Panel divider = isLast
                ? null
                : LabelHelpers.CreateRowDivider(rowPanel, panelWidth, rowHeight, bottomClearance);
            sink.AddRelayout(w =>
            {
                rowPanel.Size = new Point(w, rowHeight);
                extraRelayout?.Invoke(w);
                if (divider != null)
                {
                    divider.Size = new Point(w, 2);
                }
            });
        }
    }
}
