using System;
using System.Collections.Generic;
using System.Linq;
using Blish_HUD;
using Blish_HUD.Controls;
using Microsoft.Xna.Framework;
using MonoGame.Extended.BitmapFonts;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    // Renders every row of PlanSectionType.Notes - all
    // PlanRowType.NoteLine - as WRAPPED text with an optional right-aligned
    // coin cell on its first line.
    //
    // A note that names ONE item leads with that item's icon and its name,
    // then the note itself, which is the shape every table in this tab
    // already opens a row with. That note's first line is an icon-led band
    // (NotesSectionLayoutMath.IconLineHeight) and its wrapped continuation
    // lines hang from the name's own rule.
    //
    // A note whose sentence carries a LINK - the achievement a vendor
    // demands, the merchants a recipe sheet is sold by - arrives as a
    // PlanNoteSegment list rather than one string, because Blish's Label
    // draws one string in one colour with no underline. The view spends one
    // label per segment RUN on each line, places each where
    // NoteRunLayout puts it, and rules a line under the linked ones
    // at NotesSectionLayoutMath's thickness. Which runs are links, and what
    // each opens, are decided in Services (NoteSegmentWrap,
    // MissingRecipeNoteText, VendorRequirementNoticeText) where a test can
    // reach them.
    //
    // RenderValueCellRightAligned/RepositionValueCellRightAligned are the
    // same helpers that give every shopping/tree value cell, drawn ONLY
    // when row.CoinValue > 0 (the CreateCollapsibleSection default fallback
    // would call plain TextRowRenderer.CreateTextRow for every row instead,
    // which never renders a coin value at all - silently dropping every
    // reclaim amount this section shows - so this section needs its own
    // case in that switch rather than falling through to the default).
    //
    // Row-height discipline is load-bearing: a note is greedily wrapped
    // into k lines, and each LINE gets its own fixed-height Panel. The
    // section's total height is the sum over its notes of
    // NotesSectionLayoutMath.NoteHeight, which Render returns, and
    // CreateCollapsibleSection uses that instead of
    // PlanContentHeightMath.SectionBodyHeight's per-row default - the same
    // special-casing Summary already has, with the stronger property that
    // the number cannot drift from what was built because it IS what was
    // built.
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
    // here - but ONLY for a plain note whose line count is unchanged, since
    // neither RunReellipsis nor ReplayRelayout may change a row height (see
    // CraftingPlanView's _relayoutActions field comment). A segmented note
    // re-wraps into a different set of labels rather than a different
    // string, so any change at all hands it to the rebuild path. When the
    // count moves, or a segmented note re-wraps differently, the closure
    // requests one deferred rebuild instead (ISectionRelayoutSink.
    // RequestRerenderAfterSettle), which re-wraps and re-heights the whole
    // section from scratch in the same frame. Mid-drag the text is simply
    // stale, exactly as every other section's ellipsized name is.
    internal sealed class NotesSectionRenderer
    {
        private readonly ISectionRelayoutSink _sink;

        /// <summary>Everything one item's tooltip shows, from its id -
        /// what the leading icon on a note with an item subject resolves
        /// its whole hover from.</summary>
        private readonly Func<int, ItemTooltipFacts> _getItemFacts;

        /// <summary>Everything one currency's tooltip shows, from its id.
        /// Required, not defaulted: an optional resolver is how one
        /// surface came to hand its currency icons less than another.</summary>
        private readonly Func<int, CurrencyTooltipFacts> _getCurrencyFacts;

        internal NotesSectionRenderer(
            ISectionRelayoutSink sink, Func<int, ItemTooltipFacts> getItemFacts,
            Func<int, CurrencyTooltipFacts> getCurrencyFacts)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _getItemFacts = getItemFacts ?? throw new ArgumentNullException(nameof(getItemFacts));
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
            int height = 0;
            foreach (var row in section.Rows)
            {
                height += CreateNoteRow(row, contentFlow, panelWidth);
            }

            return height;
        }

        /// <summary>Returns the pixel height this note produced.</summary>
        private int CreateNoteRow(PlanRowViewModel row, FlowPanel parent, int panelWidth)
        {
            // Segments route here even with no item subject: they are the
            // only path that draws a link, and a note that lost its subject
            // must not quietly lose its links with it.
            return row.NoteSegments != null || HasSubject(row)
                ? CreateSubjectNote(row, parent, panelWidth)
                : CreatePlainNote(row, parent, panelWidth);
        }

        private static bool HasSubject(PlanRowViewModel row)
        {
            return row.ItemId > 0 && !string.IsNullOrEmpty(row.NoteSubject);
        }

        private int CreateSubjectNote(PlanRowViewModel row, FlowPanel parent, int panelWidth)
        {
            var font = UiFonts.Body;
            var measure = LabelHelpers.MeasureWith(font);
            var advance = TextAdvanceMath.AdvanceWith(measure);
            var segments = row.NoteSegments
                ?? new List<PlanNoteSegment> { PlanNoteSegment.Plain(row.Label ?? "") };

            bool hasIcon = HasSubject(row);
            bool hasCoin = row.CoinValue > 0;
            int coinCellWidth = hasCoin
                ? CoinCurrencyRenderer.MeasureValueWidth(row.CoinValue, null, font)
                : 0;

            string subject = hasIcon
                ? LabelHelpers.EllipsizeToWidth(
                    font, row.NoteSubject, NotesSectionLayoutMath.SubjectMaxWidth(panelWidth))
                : "";
            string subjectLabel = hasIcon
                ? NotesSectionLayoutMath.SubjectLabel(subject)
                : "";
            string subjectRun = hasIcon
                ? NotesSectionLayoutMath.SubjectRun(subject)
                : "";

            // The advance, not the measured right edge: the note's first
            // run is drawn at the pen the name and its space end on, and a
            // measurement stops at the last drawn glyph instead.
            int subjectRunWidth = hasIcon ? advance(subjectRun) : 0;
            int restX = hasIcon ? NotesSectionLayoutMath.NameX : NotesSectionLayoutMath.LabelX;

            var wrapped = WrapAt(
                segments, panelWidth, coinCellWidth, subjectRunWidth, hasIcon, measure);
            var linePanels = new List<Panel>(wrapped.Lines.Count);
            var plainLabels = new List<Label>();
            CoinCurrencyRenderer.ValueCellHandle coinHandle = null;
            int firstLineY = hasIcon ? SubjectTextY : PlainTextY;

            for (int i = 0; i < wrapped.Lines.Count; i++)
            {
                bool isFirst = i == 0;
                int rowHeight = isFirst && hasIcon
                    ? NotesSectionLayoutMath.IconLineHeight
                    : PlanContentHeightMath.FallbackTextRowHeight;
                int textY = isFirst ? firstLineY : PlainTextY;

                var linePanel = new ClippedPanel()
                {
                    Size = new Point(panelWidth, rowHeight),
                    Parent = parent,
                };

                if (isFirst && hasIcon)
                {
                    IconControls.DrawItemIcon(
                        linePanel, row.ItemId, NotesSectionLayoutMath.IconX,
                        PlanContentHeightMath.IconRowIconY, ItemIconTier.BagSidebar, _getItemFacts);

                    plainLabels.Add(LabelHelpers.WithDescenderClearance(new Label()
                    {
                        Text = subjectLabel,
                        Font = font,
                        TextColor = Color.White,
                        AutoSizeWidth = true,
                        AutoSizeHeight = true,
                        Location = new Point(restX, textY),
                        Parent = linePanel,
                    }));
                }

                if (isFirst && hasCoin)
                {
                    coinHandle = CoinCurrencyRenderer.RenderValueCellRightAligned(
                        linePanel, row.CoinValue, null,
                        panelWidth - NotesSectionLayoutMath.RightPadding, textY, font,
                        _getCurrencyFacts);
                }

                DrawLine(
                    linePanel, wrapped.Lines[i], restX, isFirst ? subjectRun : "", textY, font,
                    advance, plainLabels);

                linePanels.Add(linePanel);
                AssertLineFits(linePanel, rowHeight);
            }

            ApplyTooltip(linePanels, plainLabels, wrapped.Truncated ? row.Label : null);

            var capturedCoinHandle = coinHandle;
            _sink.AddRelayout(w =>
            {
                for (int i = 0; i < linePanels.Count; i++)
                {
                    linePanels[i].Size = new Point(w, linePanels[i].Height);
                }

                if (capturedCoinHandle != null)
                {
                    CoinCurrencyRenderer.RepositionValueCellRightAligned(
                        capturedCoinHandle, w - NotesSectionLayoutMath.RightPadding, firstLineY);
                }
            });

            // A segmented note re-wraps into a different set of LABELS, not
            // a different string, so there is nothing to write back in
            // place: ANY change - a different line count, different words
            // on a line, or a name that ellipsizes differently - goes to
            // the rebuild path, which runs in this same frame before
            // anything paints.
            var builtLines = LineTexts(wrapped);
            _sink.AddReellipsis(w =>
            {
                string newSubject = hasIcon
                    ? LabelHelpers.EllipsizeToWidth(
                        font, row.NoteSubject, NotesSectionLayoutMath.SubjectMaxWidth(w))
                    : "";
                int newSubjectRunWidth = hasIcon
                    ? advance(NotesSectionLayoutMath.SubjectRun(newSubject))
                    : 0;
                var rewrapped = WrapAt(
                    segments, w, coinCellWidth, newSubjectRunWidth, hasIcon, measure);

                if (!string.Equals(newSubject, subject, StringComparison.Ordinal)
                    || !LineTexts(rewrapped).SequenceEqual(builtLines, StringComparer.Ordinal))
                {
                    _sink.RequestRerenderAfterSettle();
                }
            });

            return NotesSectionLayoutMath.NoteHeight(wrapped.Lines.Count, hasIcon);
        }

        /// <summary>
        /// The note's wrap at one panel width. Called at build time and
        /// again at settle time, so the two can never budget differently.
        /// </summary>
        private static NoteSegmentWrap.WrappedNote WrapAt(
            IReadOnlyList<PlanNoteSegment> segments, int panelWidth, int coinCellWidth,
            int subjectRunWidth, bool hasIcon, Func<string, int> measure)
        {
            int first = hasIcon
                ? NotesSectionLayoutMath.SubjectFirstLineBudget(
                    panelWidth, coinCellWidth, subjectRunWidth)
                : NotesSectionLayoutMath.TextBudget(panelWidth, coinCellWidth);
            int rest = hasIcon
                ? NotesSectionLayoutMath.SubjectRestBudget(panelWidth)
                : NotesSectionLayoutMath.TextBudget(panelWidth, 0);

            return NoteSegmentWrap.Wrap(
                segments, first, rest, measure, TextWrapMath.MaxWrappedLines);
        }

        /// <summary>Each wrapped line as one string, which is what a
        /// settle-time re-wrap is compared against.</summary>
        private static string[] LineTexts(NoteSegmentWrap.WrappedNote wrapped)
        {
            var texts = new string[wrapped.Lines.Count];
            for (int i = 0; i < wrapped.Lines.Count; i++)
            {
                var line = wrapped.Lines[i];
                var text = new System.Text.StringBuilder();
                for (int j = 0; j < line.Count; j++)
                {
                    text.Append(line[j].Text);
                }

                texts[i] = text.ToString();
            }

            return texts;
        }

        /// <summary>
        /// One wrapped line's runs, in the module's one link style.
        /// A note draws its plain words white, and hands them back so the
        /// row's full-text hover can be put on each.
        /// <paramref name="prefix"/> is the text the caller already drew at
        /// <paramref name="startX"/> - the subject's name on a note's first
        /// line, empty on every other line.
        /// </summary>
        private static void DrawLine(
            Panel linePanel, IReadOnlyList<PlanNoteSegment> pieces, int startX, string prefix,
            int y, BitmapFont font, Func<string, int> advance, List<Label> plainLabels)
        {
            LinkedTextRenderer.DrawLine(
                linePanel, pieces, startX, y, font, Color.White, advance, plainLabels, prefix);
        }

        private int CreatePlainNote(PlanRowViewModel row, FlowPanel parent, int panelWidth)
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
                    Location = new Point(labelX, PlainTextY),
                    Parent = linePanel,
                });

                if (i == 0 && hasCoin)
                {
                    coinHandle = CoinCurrencyRenderer.RenderValueCellRightAligned(
                        linePanel, row.CoinValue, null,
                        panelWidth - NotesSectionLayoutMath.RightPadding, PlainTextY, font,
                        _getCurrencyFacts);
                }

                linePanels.Add(linePanel);
                lineLabels.Add(label);
                AssertLineFits(linePanel, rowHeight);
            }

            ApplyTooltip(linePanels, lineLabels, wrapped.Truncated ? fullText : null);

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
                        capturedCoinHandle, w - NotesSectionLayoutMath.RightPadding, PlainTextY);
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
                    // The note needs a different number of rows than it was
                    // built with, which is a HEIGHT change - the one thing a
                    // re-ellipsis closure may not do (see CraftingPlanView's
                    // _relayoutActions field comment). Hand it to the
                    // rebuild path instead of forcing the text into the
                    // wrong slot count: padding to fit would leave blank
                    // rows sitting INSIDE the section until the next render,
                    // and squeezing to fit would ellipsize text that does
                    // fit at this width. The rebuild runs in this same
                    // frame, before anything paints.
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

                ApplyTooltip(linePanels, lineLabels, rewrapped.Truncated ? fullText : null);
            });

            return NotesSectionLayoutMath.NoteHeight(lineCount, hasIcon: false);
        }

        // The y a body-face run needs for its baseline to land on the
        // line's own baseline. Every control on a note line is seated from
        // that baseline rather than from a shared box top, so the name and
        // the note text sit on one line of letters.
        private static readonly int SubjectTextY = TypeRampMetrics.BaselineAlignedY(
            TypeRampMetrics.BodyInk, NotesSectionLayoutMath.SubjectLineBaseline);

        private static readonly int PlainTextY = TypeRampMetrics.BaselineAlignedY(
            TypeRampMetrics.BodyInk, NotesSectionLayoutMath.TextLineBaseline);

        // Every line of a truncated note carries the full text, so a hover
        // anywhere on the note reads the whole thing - not only its last
        // line, which is the one that lost text. The LABELS carry it too:
        // Blish resolves a tooltip on the deepest control under the cursor
        // and never bubbles, so a label would otherwise swallow the hover
        // over the words themselves. A link label is left alone - its own
        // hover names the page it opens.
        private static void ApplyTooltip(
            List<Panel> linePanels, List<Label> plainLabels, string tooltip)
        {
            foreach (var linePanel in linePanels)
            {
                TooltipFacility.ApplyPlain(linePanel, tooltip);
            }

            foreach (var label in plainLabels)
            {
                TooltipFacility.ApplyPlain(label, tooltip);
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private static void AssertLineFits(Panel linePanel, int rowHeight)
        {
            // Load-bearing per this class's own doc comment: the Notes
            // section's height is the sum of its lines' own row heights,
            // which is only correct when every line panel's contents render
            // inside the height it was given. The real ways a note line
            // could break the contract are a child control growing taller
            // than the row (a future WrapText/larger-font change to a
            // label, a link's underline quad, or a coin cell taller than
            // the row), so assert on the CHILDREN's own extents -
            // re-reading linePanel.Height, set from the same number the
            // caller passed, would guard nothing.
            foreach (var child in linePanel.Children)
            {
                System.Diagnostics.Debug.Assert(
                    child.Bottom <= rowHeight,
                    "NotesSectionRenderer: every note line row's child controls must fit within "
                    + "the height that line was built at - see this class's own doc comment.");
            }
        }
    }
}
