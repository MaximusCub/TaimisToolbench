using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The rich tooltip surface's arithmetic: how a
    /// <see cref="TooltipContent"/> breaks into rendered rows at a real
    /// pixel width, and where the finished box goes so it stays on screen.
    /// Blish-free and expressed against caller-supplied measurement
    /// functions, the same seam <see cref="TextWrapMath"/> uses, so the
    /// surface's Blish-coupled shell stays thin enough to be uninteresting.
    ///
    /// The placement half is the fix Blish itself does not have. Measured
    /// against BlishHUD 1.3.0 (see KNOWN-ISSUES #41):
    /// <c>Tooltip.UpdateTooltipPosition</c> flips above/below the cursor to
    /// protect the TOP edge and shifts left to protect the RIGHT edge, and
    /// clamps neither result - a tall tooltip placed below the cursor runs
    /// off the BOTTOM of the screen, and the left shift can produce a
    /// negative X. <see cref="Place"/> keeps Blish's above-when-it-fits
    /// preference - which is also the game's - hugs the cursor from above
    /// at the measured game gap rather than Blish's uniform 36
    /// (<see cref="CursorGapAbove"/>), and clamps all four edges.
    /// </summary>
    internal static class TooltipLayoutMath
    {
        /// <summary>
        /// The gap between the cursor and a box placed ABOVE it. The game
        /// hugs the cursor here: on every live3 storage hover
        /// (vials/fury/candy-corn/almonds, 2026-08-26) the box bottom sits
        /// 3-8px above the hovered slot's TOP edge, with the cursor inside
        /// the slot below - so the true cursor-to-box gap is small, and
        /// Blish's uniform 36 (its MOUSE_VERTICAL_MARGIN) is what made a
        /// bottom-of-window hover read as a detached box. 8 is the
        /// measured slot-edge bound; the exact cursor pixel is not visible
        /// in any capture, so the value within [3..8] is INFERRED. Nothing
        /// above the cursor needs clearing - the cursor sprite hangs
        /// down-right of its hotspot.
        /// </summary>
        public const int CursorGapAbove = 8;

        /// <summary>
        /// The gap between the cursor and a box placed BELOW it - Blish's
        /// own <c>Tooltip.MOUSE_VERTICAL_MARGIN</c> (measured, decompiled
        /// 1.3.0), kept: it is what clears the cursor sprite, and no
        /// capture measures the game's below-placement gap.
        /// </summary>
        public const int CursorGapBelow = 36;

        /// <summary>Breathing room kept between the box and every screen edge.</summary>
        public const int ScreenEdgeMargin = 4;

        /// <summary>
        /// Blish's <c>BasicTooltipView.MAX_WIDTH</c> (measured, a hard
        /// 500 that knows nothing about the screen). Kept as the PREFERRED
        /// width so a rich tooltip reads the same as every plain one, but
        /// <see cref="MaxContentWidth"/> narrows it on a screen that cannot
        /// afford it - which is the part Blish's constant cannot do.
        /// </summary>
        public const int PreferredMaxContentWidth = 500;

        /// <summary>
        /// Floor for <see cref="MaxContentWidth"/>. Below this the wrap
        /// degenerates into a column of hard-split fragments; a screen that
        /// narrow is broken in ways a tooltip cannot fix, so the box is
        /// allowed to exceed the margin instead of shredding its text.
        /// </summary>
        public const int MinContentWidth = 120;

        /// <summary>
        /// The item tooltip's own wrap maximum, in the width units
        /// <c>BitmapFont.MeasureString</c> reports for the shipped Menomonia 16
        /// face with Blish's <c>LetterSpacing = -1</c> - the face
        /// <c>RichTooltipSurface.BuildContent</c> draws the box in. It is NOT
        /// transferable to another face: re-derive it whenever the font changes.
        /// <para>
        /// Derived from the game's own break decisions over a corpus of live
        /// captures, not from a game-pixel cap converted by a scale factor: a
        /// mean font ratio hides a real per-string spread (0.99x to 1.03x at
        /// Menomonia 14). Every constraint but one intersects at [372, 381),
        /// and 376 is its midpoint, so no decision sits within 4px of flipping.
        /// The one outlier - fury-scorched 86967, the corpus's widest-measuring
        /// string in this face - wraps one word early, a recorded, measured
        /// cost of rendering the game's text at a face the game does not ship.
        /// </para>
        /// <para>The corpus and the arithmetic over it: docs/ARCHITECTURE.md,
        /// "Services Q-Z: relocated design narrative".</para>
        /// </summary>
        public const int ItemTooltipMaxContentWidth = 376;

        /// <summary>
        /// The width a tooltip may wrap at on this screen.
        /// <paramref name="preferredWidth"/> defaults to Blish's own 500 so
        /// every existing caller reads the same as every plain tooltip; a
        /// caller with a measured cap of its own - the item tooltip, at
        /// <see cref="ItemTooltipMaxContentWidth"/> - passes it and does
        /// not move the shared constant out from under the rest.
        /// </summary>
        public static int MaxContentWidth(int screenWidth, int chromeWidth, int preferredWidth = 0)
        {
            int preferred = preferredWidth > 0 ? preferredWidth : PreferredMaxContentWidth;
            int usable = screenWidth - (2 * ScreenEdgeMargin) - Math.Max(0, chromeWidth);
            if (usable >= preferred)
            {
                return preferred;
            }

            return Math.Max(MinContentWidth, usable);
        }

        /// <summary>A span placed at an x offset within its rendered row.</summary>
        public readonly struct PlacedSpan
        {
            public PlacedSpan(TooltipSpan span, int x, int width)
            {
                Span = span;
                X = x;
                Width = width;
            }

            public TooltipSpan Span { get; }

            public int X { get; }

            public int Width { get; }
        }

        public sealed class LaidOutRow
        {
            internal LaidOutRow(
                IReadOnlyList<PlacedSpan> spans, int width, int y, int height, string iconUrl,
                TooltipLineKind kind = TooltipLineKind.Text,
                TooltipHeaderSubject subject = default(TooltipHeaderSubject),
                int iconAssetId = 0)
            {
                Spans = spans;
                Width = width;
                Y = y;
                Height = height;
                IconUrl = iconUrl;
                Kind = kind;
                HeaderSubject = subject;
                IconAssetId = iconAssetId;
            }

            public IReadOnlyList<PlacedSpan> Spans { get; }

            public int Width { get; }

            /// <summary>Top of this row inside the content, in pixels.</summary>
            public int Y { get; }

            /// <summary>
            /// This row's own height. Prose rows are one line pitch; only
            /// a coin row needs icon clearance and only a header row is
            /// icon-tall (gap G21) - a uniform height taken from the
            /// tallest kind pads every prose row in the box.
            /// </summary>
            public int Height { get; }

            /// <summary>
            /// The header icon, on the FIRST row of a header line only - a
            /// name that wraps must not draw its icon again. On an
            /// <see cref="TooltipLineKind.Effect"/> row, the effect's own
            /// inline icon instead, same first-row-only rule.
            /// </summary>
            public string IconUrl { get; }

            /// <summary>
            /// The line kind this row renders, continuation rows included -
            /// how the surface tells a framed 32px header icon from the
            /// bare ~26px effect icon that shares <see cref="IconUrl"/>.
            /// </summary>
            public TooltipLineKind Kind { get; }

            /// <summary>
            /// The header line's subject, carried through from
            /// <see cref="TooltipLine.HeaderSubject"/> - what the surface
            /// frames <see cref="IconUrl"/> by on a header row.
            /// </summary>
            public TooltipHeaderSubject HeaderSubject { get; }

            /// <summary>
            /// The slot glyph on the first row of a
            /// <see cref="TooltipLineKind.Slot"/> line, as a GW2 asset id;
            /// 0 everywhere else, same first-row-only rule as
            /// <see cref="IconUrl"/>.
            /// </summary>
            public int IconAssetId { get; }
        }

        public sealed class Layout
        {
            internal Layout(IReadOnlyList<LaidOutRow> rows, int width, int height)
            {
                Rows = rows;
                Width = width;
                Height = height;
            }

            public IReadOnlyList<LaidOutRow> Rows { get; }

            public int Width { get; }

            public int Height { get; }
        }

        /// <summary>
        /// Breaks content into rendered rows no wider than
        /// <paramref name="maxWidth"/>.
        ///
        /// Prose is wrapped by <see cref="TextWrapMath.Wrap"/> - the same
        /// tested wrapper the character-budget seam
        /// (<see cref="TooltipTextFormat"/>) uses, called here with a real
        /// font measurement and with the current row's remaining width as
        /// the first-line budget, so a prose span that follows a coin span
        /// wraps against what is actually left of the row. A coin span is
        /// ATOMIC: it moves to the next row whole rather than being split,
        /// because half a coin run is not a number.
        ///
        /// A line with no spans is a deliberate blank separator and still
        /// produces a row, so vertical rhythm survives the layout.
        /// </summary>
        /// <param name="separateEntriesWhenAnyWraps">
        /// For a box whose lines are unrelated statements rather than one
        /// passage - see <see cref="SeparateEntries"/>.
        /// </param>
        public static Layout LayoutContent(
            TooltipContent content,
            int maxWidth,
            int rowHeight,
            Func<string, int> measureText,
            Func<long, int> measureCoin,
            int coinRowHeight = 0,
            int headerRowHeight = 0,
            int headerIndent = 0,
            int effectIndent = 0,
            int slotIndent = 0,
            bool separateEntriesWhenAnyWraps = false)
        {
            if (measureText == null)
            {
                throw new ArgumentNullException(nameof(measureText));
            }

            if (measureCoin == null)
            {
                throw new ArgumentNullException(nameof(measureCoin));
            }

            var rows = new List<LaidOutRow>();
            if (content == null || content.IsEmpty)
            {
                return new Layout(rows, 0, 0);
            }

            // Both default to the prose pitch, so a caller that has only
            // one row kind - every test, and every non-item tooltip -
            // still gets the uniform box it always got.
            int coinHeight = coinRowHeight > 0 ? coinRowHeight : rowHeight;
            int headerHeight = headerRowHeight > 0 ? headerRowHeight : rowHeight;

            int effectiveMax = Math.Max(1, maxWidth);
            int y = 0;
            var lineStarts = new List<int>();
            foreach (var line in content.Lines)
            {
                lineStarts.Add(rows.Count);
                bool isHeader = line.Kind == TooltipLineKind.Header;
                bool isEffect = line.Kind == TooltipLineKind.Effect;
                bool isSlot = line.Kind == TooltipLineKind.Slot;
                // The name column of a header row starts past the icon,
                // and a wrapped continuation of it stays in that column.
                // An effect row is indented past its inline icon the same
                // way (measured: the game's effect text column starts
                // ~31px in, live3 soul-pastries/candy-corn), and a slot row
                // past its own narrower glyph.
                int indent = isHeader ? Math.Max(0, headerIndent)
                    : isEffect ? Math.Max(0, effectIndent)
                    : isSlot ? Math.Max(0, slotIndent) : 0;
                int lineHeight = isHeader ? headerHeight : rowHeight;
                string iconUrl = isHeader || isEffect ? line.IconUrl : null;
                int iconAssetId = isSlot ? line.IconAssetId : 0;

                var current = new List<PlacedSpan>();
                int x = indent;

                // Commits the row being built and starts the next one -
                // the icon rides the first row of its line only.
                void BreakRow()
                {
                    rows.Add(new LaidOutRow(
                        current, x, y, lineHeight, iconUrl, line.Kind, line.HeaderSubject,
                        iconAssetId));
                    y += lineHeight;
                    // Continuations are ordinary text rows: only the FIRST
                    // row of a header line carries the icon and its height.
                    lineHeight = rowHeight;
                    iconUrl = null;
                    iconAssetId = 0;
                    current = new List<PlacedSpan>();
                    x = indent;
                }

                foreach (var span in line.Spans)
                {
                    if (span.IsCoin)
                    {
                        int coinWidth = Math.Max(0, measureCoin(span.CoinCopper));
                        if (x > indent && x + coinWidth > effectiveMax)
                        {
                            BreakRow();
                        }

                        // A coin run makes the row it actually lands on -
                        // never the one it was pushed off - the taller
                        // coin kind.
                        lineHeight = Math.Max(lineHeight, coinHeight);
                        current.Add(new PlacedSpan(span, x, coinWidth));
                        x += coinWidth;
                        continue;
                    }

                    if (span.Text.Length == 0)
                    {
                        continue;
                    }

                    // TextWrapMath drops the space run a line ends on -
                    // right for a standalone line, wrong for a span whose
                    // trailing space is the separator before the coin run
                    // that follows it ("Cost: " + 1g 23s 45c). Held out of
                    // the wrap and restored below, but only when it still
                    // fits, so the wrap's own width guarantee stands.
                    string core = span.Text.TrimEnd(' ');
                    string trailingSpaces = core.Length == span.Text.Length ? null : span.Text.Substring(core.Length);

                    // Continuation rows start at the indent too, so their
                    // budget is the box minus it - otherwise a wrapped
                    // header name runs past the right edge by one icon.
                    var wrapped = TextWrapMath.Wrap(
                        core, Math.Max(1, effectiveMax - x), Math.Max(1, effectiveMax - indent),
                        measureText).Lines;
                    for (int i = 0; i < wrapped.Count; i++)
                    {
                        if (i > 0)
                        {
                            BreakRow();
                        }

                        string piece = wrapped[i];
                        if (piece.Length == 0)
                        {
                            continue;
                        }

                        int pieceWidth = Math.Max(0, measureText(piece));
                        // WithText, not FromText: a wrapped piece keeps the
                        // original span's role, so a long rarity-coloured
                        // item name stays coloured past its first line.
                        current.Add(new PlacedSpan(span.WithText(piece), x, pieceWidth));
                        x += pieceWidth;
                    }

                    if (trailingSpaces != null)
                    {
                        int trailingWidth = Math.Max(0, measureText(trailingSpaces));
                        if (x + trailingWidth <= effectiveMax)
                        {
                            current.Add(new PlacedSpan(span.WithText(trailingSpaces), x, trailingWidth));
                            x += trailingWidth;
                        }
                    }
                }

                BreakRow();
            }

            if (separateEntriesWhenAnyWraps)
            {
                rows = SeparateEntries(rows, lineStarts, rowHeight, out y);
            }

            int width = 0;
            foreach (var row in rows)
            {
                if (row.Width > width)
                {
                    width = row.Width;
                }
            }

            return new Layout(rows, width, Math.Max(0, y));
        }

        /// <summary>
        /// A blank row between every pair of adjacent entries, and the
        /// height that leaves, when at least one entry wrapped onto a
        /// second row. A reader cannot tell a continuation row from the
        /// next entry, and the rows are unrelated statements.
        /// <para>
        /// All or nothing: separating only the pairs that wrap would leave
        /// two unwrapped entries adjacent, reading as one wrapped entry.
        /// A neighbour that is already a blank line gets no second blank.
        /// </para>
        /// </summary>
        private static List<LaidOutRow> SeparateEntries(
            List<LaidOutRow> rows, List<int> lineStarts, int rowHeight, out int height)
        {
            height = 0;
            foreach (var row in rows)
            {
                height = Math.Max(height, row.Y + row.Height);
            }

            bool anyWrapped = false;
            for (int i = 0; i < lineStarts.Count && !anyWrapped; i++)
            {
                int end = i + 1 < lineStarts.Count ? lineStarts[i + 1] : rows.Count;
                anyWrapped = end - lineStarts[i] > 1;
            }

            if (!anyWrapped || lineStarts.Count < 2)
            {
                return rows;
            }

            var spaced = new List<LaidOutRow>(rows.Count + lineStarts.Count);
            int y = 0;
            for (int i = 0; i < lineStarts.Count; i++)
            {
                int start = lineStarts[i];
                int end = i + 1 < lineStarts.Count ? lineStarts[i + 1] : rows.Count;
                if (i > 0 && !IsBlank(rows, lineStarts, i) && !IsBlank(rows, lineStarts, i - 1))
                {
                    spaced.Add(new LaidOutRow(new List<PlacedSpan>(), 0, y, rowHeight, null));
                    y += rowHeight;
                }

                for (int r = start; r < end; r++)
                {
                    var row = rows[r];
                    spaced.Add(new LaidOutRow(
                        row.Spans, row.Width, y, row.Height, row.IconUrl, row.Kind,
                        row.HeaderSubject, row.IconAssetId));
                    y += row.Height;
                }
            }

            height = y;
            return spaced;
        }

        /// <summary>Whether one content line laid out as a single empty
        /// row - the deliberate blank separator.</summary>
        private static bool IsBlank(List<LaidOutRow> rows, List<int> lineStarts, int index)
        {
            int start = lineStarts[index];
            int end = index + 1 < lineStarts.Count ? lineStarts[index + 1] : rows.Count;
            return end - start == 1 && rows[start].Spans.Count == 0;
        }

        /// <summary>
        /// The two stacked boxes' geometry - see <see cref="Stack"/>.
        /// Heights and tops in the surface's own coordinates, so a caller
        /// that has laid out both contents needs no arithmetic of its own.
        /// </summary>
        public readonly struct StackedBoxes
        {
            internal StackedBoxes(int panelHeight, int extraContentTop, int firstBoxHeight, int secondBoxTop)
            {
                PanelHeight = panelHeight;
                ExtraContentTop = extraContentTop;
                FirstBoxHeight = firstBoxHeight;
                SecondBoxTop = secondBoxTop;
            }

            /// <summary>Both contents plus the run between them, which is
            /// what the one content panel spanning both boxes measures.</summary>
            public int PanelHeight { get; }

            /// <summary>Top of the second box's CONTENT inside that panel,
            /// 0 when there is no second box.</summary>
            public int ExtraContentTop { get; }

            /// <summary>The first box's full height, its chrome included.</summary>
            public int FirstBoxHeight { get; }

            /// <summary>Top of the second box's FRAME, 0 when there is no
            /// second box.</summary>
            public int SecondBoxTop { get; }
        }

        /// <summary>
        /// Stacks the module's own second box under the game's first one:
        /// the first box's chrome closes, <paramref name="gap"/> pixels of
        /// nothing follow, and the second box opens with chrome of its own.
        /// A non-positive <paramref name="extraHeight"/> means there is
        /// only one box, and every second-box figure comes back 0.
        /// <para>
        /// The two boxes are one hover and are placed as one unit, so the
        /// gap is inside the placed rectangle rather than between two
        /// independently placed ones.
        /// </para>
        /// </summary>
        public static StackedBoxes Stack(
            int firstHeight, int extraHeight, int chromeTop, int chromeBottom, int gap)
        {
            int first = Math.Max(0, firstHeight);
            int extra = Math.Max(0, extraHeight);
            int top = Math.Max(0, chromeTop);
            int bottom = Math.Max(0, chromeBottom);
            int firstBox = top + first + bottom;

            if (extra <= 0)
            {
                return new StackedBoxes(first, 0, firstBox, 0);
            }

            int extraContentTop = first + bottom + Math.Max(0, gap) + top;
            return new StackedBoxes(
                extraContentTop + extra, extraContentTop, firstBox, firstBox + Math.Max(0, gap));
        }

        /// <summary>
        /// Where the finished box goes, given the cursor and the screen.
        ///
        /// Horizontal: at the cursor, flipped to the cursor's left when the
        /// box would cross the right edge (Blish's rule), then clamped so
        /// the result cannot be negative (Blish's is not).
        ///
        /// Vertical: above the cursor when it fits - hugging it at the
        /// measured <see cref="CursorGapAbove"/>, which is the game's own
        /// preference (every live3 capture grows up from just above the
        /// cursor) - else below at <see cref="CursorGapBelow"/>, then
        /// clamped to the bottom edge (Blish never clamps it).
        /// When neither side can hold the box with its cursor gap the box
        /// takes the roomier side and is clamped into the screen - the only
        /// case where it may reach across the cursor, and it needs a
        /// tooltip taller than the screen minus the gap to happen at all.
        /// </summary>
        public static void Place(
            int mouseX, int mouseY,
            int width, int height,
            int screenWidth, int screenHeight,
            out int x, out int y)
        {
            x = mouseX;
            if (x + width > screenWidth - ScreenEdgeMargin)
            {
                x = mouseX - width;
            }

            x = ClampAxis(x, width, screenWidth);

            int above = mouseY - CursorGapAbove - height;
            int below = mouseY + CursorGapBelow;
            if (above >= ScreenEdgeMargin)
            {
                y = above;
                return;
            }

            if (below + height <= screenHeight - ScreenEdgeMargin)
            {
                y = below;
                return;
            }

            int roomAbove = mouseY - CursorGapAbove - ScreenEdgeMargin;
            int roomBelow = screenHeight - ScreenEdgeMargin - CursorGapBelow - mouseY;
            y = ClampAxis(roomAbove >= roomBelow ? ScreenEdgeMargin : screenHeight - height, height, screenHeight);
        }

        /// <summary>
        /// Clamps a box of <paramref name="size"/> into
        /// [<see cref="ScreenEdgeMargin"/>, extent - margin]. A box larger
        /// than the whole extent is pinned to the near edge rather than
        /// pushed off the far one, so its start - where the reader's eye
        /// begins - is always the part that stays visible.
        /// </summary>
        public static int ClampAxis(int desired, int size, int extent)
        {
            int min = ScreenEdgeMargin;
            int max = extent - ScreenEdgeMargin - size;
            if (max <= min)
            {
                return min;
            }

            return desired < min ? min : (desired > max ? max : desired);
        }

        /// <summary>
        /// Where the game's tooltip canvas art starts inside Blish's
        /// 942x942 "tooltip" texture. Blish's own draw (decompiled 1.3.0)
        /// crops from (3,4) to skip the art's baked border, and the
        /// 2026-08-26 live captures confirm the game itself composites
        /// exactly this crop 1:1: the interior of live2/k-2 correlates with
        /// the texture at r=0.983 when aligned to this origin
        /// (fidelity-audit, 8.4 closure).
        /// </summary>
        public const int CanvasArtSourceX = 3;

        public const int CanvasArtSourceY = 4;

        /// <summary>
        /// How much of one axis of the canvas art a box of
        /// <paramref name="boxLength"/> can source starting at
        /// <paramref name="offset"/>: the box length, clamped to what the
        /// texture has left past the offset, never negative. The 942px
        /// texture leaves 939x938 - a rich tooltip is
        /// <see cref="ItemTooltipMaxContentWidth"/> plus chrome at its
        /// widest and never approaches it, so the clamp exists for the
        /// pathological box, not the common one.
        /// </summary>
        public static int CanvasArtSourceLength(int boxLength, int textureLength, int offset)
        {
            int available = textureLength - offset;
            if (available <= 0 || boxLength <= 0)
            {
                return 0;
            }

            return boxLength < available ? boxLength : available;
        }
    }
}
