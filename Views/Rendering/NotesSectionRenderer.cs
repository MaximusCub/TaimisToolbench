using System;
using System.Collections.Generic;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // Renders every row of
    // PlanSectionType.Notes - all PlanRowType.NoteLine - as WRAPPED text
    // with an optional right-aligned coin cell on its first line.
    // RenderValueCellRightAligned/RepositionValueCellRightAligned are the
    // same helpers that give every shopping/tree value cell, drawn ONLY
    // when row.CoinValue > 0 (the CreateCollapsibleSection default fallback
    // would call plain TextRowRenderer.CreateTextRow for every row instead,
    // which never renders a coin value at all - silently dropping every
    // reclaim amount this section shows - so this section needs its own
    // case in that switch rather than falling through to the default).
    //
    // Row-height discipline is load-bearing: a note is greedily wrapped into k
    // lines by NotesSectionLayoutMath.WrapNote, and each LINE gets its own
    // PlanContentHeightMath.FallbackTextRowHeight-tall Panel. So every
    // panel this class builds is still exactly that height, the DEBUG
    // assert at the end of CreateNoteRow still polices it, and the only
    // thing that changed is how many of them a note produces. What did
    // change is where the section's total height comes from: rows.Count is
    // no longer the line count, so Render returns the height it actually
    // built (sum over notes of lines * FallbackTextRowHeight, via
    // NotesSectionLayoutMath.BodyHeight) and CreateCollapsibleSection uses
    // that instead of PlanContentHeightMath.SectionBodyHeight's per-row
    // default arm - the same special-casing Summary already has, with the
    // stronger property that the number cannot drift from what was built
    // because it IS what was built.
    //
    // Wrapping replaced single-line ellipsis truncation: at ~830px
    // usable a note was cut near 100 characters into a
    // hover-only tooltip, and the module's UI rule routes every
    // opportunity and complex consideration into this section. Ellipsis
    // survives only as the last-resort tail of a note that exceeds
    // TextWrapMath.MaxWrappedLines, which keeps the full text on the row
    // tooltip.
    //
    // Resize: the settle-time re-wrap (AddReellipsis) re-wraps at the
    // settled width and writes the new text back into the row Panels built
    // here - but ONLY while the line count is unchanged, since neither
    // RunReellipsis nor ReplayRelayout may change a row height (see
    // CraftingPlanView's _relayoutActions field comment) and this section
    // spends one row per line. When the count moves, the closure requests
    // one deferred rebuild instead (ISectionRelayoutSink.
    // RequestRerenderAfterSettle), which re-wraps and re-heights the whole
    // section from scratch in the same frame. Mid-drag the text is simply
    // stale, exactly as every other section's ellipsized name is.
    internal sealed class NotesSectionRenderer
    {
        private readonly ISectionRelayoutSink _sink;

        /// <summary>Everything one currency's tooltip shows, from its id.
        /// Required, not defaulted: an optional resolver is how one
        /// surface came to hand its currency icons less than another.</summary>
        private readonly Func<int, CurrencyTooltipFacts> _getCurrencyFacts;

        internal NotesSectionRenderer(
            ISectionRelayoutSink sink, Func<int, CurrencyTooltipFacts> getCurrencyFacts)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _getCurrencyFacts = getCurrencyFacts
                ?? throw new ArgumentNullException(nameof(getCurrencyFacts));
        }

        /// <summary>
        /// Renders every note row and returns the section body height that
        /// was actually built - see this class's doc comment for why the
        /// caller uses this instead of PlanContentHeightMath.
        /// </summary>
        internal int Render(PlanSectionViewModel section, FlowPanel contentFlow, int panelWidth)
        {
            int totalLines = 0;
            foreach (var row in section.Rows)
            {
                totalLines += CreateNoteRow(row, contentFlow, panelWidth);
            }

            return NotesSectionLayoutMath.BodyHeight(totalLines);
        }

        /// <summary>Returns how many line rows this note produced.</summary>
        private int CreateNoteRow(PlanRowViewModel row, FlowPanel parent, int panelWidth)
        {
            const int rowHeight = PlanContentHeightMath.FallbackTextRowHeight;
            const int labelX = NotesSectionLayoutMath.LabelX;
            var font = UiFonts.Body;
            var measure = LabelHelpers.MeasureWith(font);

            string fullText = row.Label ?? "";
            bool hasCoin = row.CoinValue > 0;

            // Coin cell only when CoinValue > 0 - mirrors
            // CoinCurrencyRenderer's own "hasCoin = copper > 0" convention,
            // but unlike RenderValueCellRightAligned's own dash fallback, a
            // plain-text NoteLine (competency/forge-scope) renders NO value
            // cell at all rather than an unpriced dash - there is no price
            // concept for those lines to begin with.
            //
            // MeasureValueWidth is called BEFORE the wrap so the FIRST
            // line's own budget can reserve room for it - mirrors
            // MeasureValueWidth's own documented shopping-list pre-scan
            // use, same "measure-then-build" ordering.
            int coinCellWidth = hasCoin ? CoinCurrencyRenderer.MeasureValueWidth(row.CoinValue, null, font) : 0;

            var wrapped = NotesSectionLayoutMath.WrapNote(fullText, panelWidth, coinCellWidth, measure);
            int lineCount = wrapped.Lines.Count;

            var linePanels = new List<Panel>(lineCount);
            var lineLabels = new List<Label>(lineCount);
            CoinCurrencyRenderer.ValueCellHandle coinHandle = null;

            for (int i = 0; i < lineCount; i++)
            {
                var linePanel = new ClippedPanel() { Size = new Point(panelWidth, rowHeight), Parent = parent };
                var label = LabelHelpers.WithDescenderClearance(new Label()
                {
                    Text = wrapped.Lines[i],
                    Font = font,
                    AutoSizeWidth = true,
                    AutoSizeHeight = true,
                    Location = new Point(labelX, 4),
                    Parent = linePanel,
                });

                if (i == 0 && hasCoin)
                {
                    coinHandle = CoinCurrencyRenderer.RenderValueCellRightAligned(
                        linePanel, row.CoinValue, null,
                        panelWidth - NotesSectionLayoutMath.RightPadding, 4, font, _getCurrencyFacts);
                }

                linePanels.Add(linePanel);
                lineLabels.Add(label);

#if DEBUG
                // Load-bearing per this class's own doc comment: the Notes
                // section's height is one FallbackTextRowHeight row per
                // wrapped LINE, which is only correct when every line panel
                // renders at exactly that height. The real ways a note line
                // could break the contract are a child control growing
                // taller than the row (a future WrapText/larger-font change
                // to the label, or a coin cell taller than rowHeight - 4),
                // so assert on the CHILDREN's own extents - re-reading
                // linePanel.Height, set from the same const two statements
                // above, would guard nothing.
                foreach (var child in linePanel.Children)
                {
                    System.Diagnostics.Debug.Assert(
                        child.Bottom <= rowHeight,
                        "NotesSectionRenderer: every note line row's child controls must fit within " +
                        "PlanContentHeightMath.FallbackTextRowHeight - see this class's own doc comment.");
                }
#endif
            }

            ApplyTooltip(linePanels, wrapped.Truncated ? fullText : null);

            var capturedCoinHandle = coinHandle;
            _sink.AddRelayout(w =>
            {
                foreach (var linePanel in linePanels)
                {
                    linePanel.Size = new Point(w, rowHeight);
                }

                if (capturedCoinHandle != null)
                {
                    CoinCurrencyRenderer.RepositionValueCellRightAligned(
                        capturedCoinHandle, w - NotesSectionLayoutMath.RightPadding, 4);
                }
            });

            _sink.AddReellipsis(w =>
            {
                int newCoinCellWidth = hasCoin
                    ? CoinCurrencyRenderer.MeasureValueWidth(row.CoinValue, null, font)
                    : 0;
                var rewrapped = NotesSectionLayoutMath.WrapNote(fullText, w, newCoinCellWidth, measure);

                if (rewrapped.Lines.Count != lineLabels.Count)
                {
                    // The note needs a different number of 28px rows than
                    // it was built with, which is a HEIGHT change - the one
                    // thing a re-ellipsis closure may not do (see
                    // CraftingPlanView's _relayoutActions field comment).
                    // Hand it to the rebuild path instead of forcing the
                    // text into the wrong slot count: padding to fit would
                    // leave blank rows sitting INSIDE the section until the
                    // next render, and squeezing to fit would ellipsize
                    // text that does fit at this width. The rebuild runs in
                    // this same frame, before anything paints.
                    _sink.RequestRerenderAfterSettle();
                    return;
                }

                for (int i = 0; i < lineLabels.Count; i++)
                {
                    // Same "only touch Text when the displayed string
                    // actually changed" gate IconNameRowHelpers.
                    // ReellipsizeName uses.
                    if (lineLabels[i].Text != rewrapped.Lines[i])
                    {
                        lineLabels[i].Text = rewrapped.Lines[i];
                    }
                }

                ApplyTooltip(linePanels, rewrapped.Truncated ? fullText : null);
            });

            return lineCount;
        }

        // Every line of a truncated note carries the full text, so a hover
        // anywhere on the note reads the whole thing - not only its last
        // line, which is the one that lost text.
        private static void ApplyTooltip(List<Panel> linePanels, string tooltip)
        {
            foreach (var linePanel in linePanels)
            {
                TooltipFacility.ApplyPlain(linePanel, tooltip);
            }
        }
    }
}
