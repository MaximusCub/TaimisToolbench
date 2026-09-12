using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Where each run of a wrapped note line starts, and how wide a link's
    /// rule is (Blish-free, unit-testable). Runs are placed on the pen
    /// <see cref="TextAdvanceMath"/> recovers, so a line drawn as several
    /// labels lands exactly where one label drawing the whole line would
    /// put it.
    /// </summary>
    internal static class NoteRunLayout
    {
        /// <summary>One run of a line, and the box it draws in.</summary>
        public readonly struct PlacedRun
        {
            public readonly PlanNoteSegment Piece;

            /// <summary>Left x, in the coordinates of the line panel.</summary>
            public readonly int X;

            /// <summary>
            /// The run's advance, which is what a link's rule spans. Not
            /// the run's ink: an underline that stopped at the last drawn
            /// pixel would end mid-space on a run that ends in one.
            /// </summary>
            public readonly int Width;

            public PlacedRun(PlanNoteSegment piece, int x, int width)
            {
                Piece = piece;
                X = x;
                Width = width;
            }
        }

        /// <summary>
        /// Lays one wrapped line's runs left to right from
        /// <paramref name="startX"/>. Each run is placed at the advance of
        /// the line's own text so far rather than at a running sum of
        /// per-run advances: Blish tracks its fonts at minus one pixel, so
        /// the two differ by a pixel per join.
        /// </summary>
        public static List<PlacedRun> Place(
            IReadOnlyList<PlanNoteSegment> pieces, int startX, Func<string, int> advance)
        {
            return Place(pieces, startX, advance, "");
        }

        /// <summary>
        /// The same placement for a line that opens with text the CALLER
        /// draws - the note's subject name. <paramref name="startX"/> is
        /// where that prefix starts, and the first run lands on the pen the
        /// prefix ends on, so the name and the note are one run of text.
        /// The prefix carries the space that parts them, which is what
        /// makes that space measure the same as a space between two words.
        /// </summary>
        public static List<PlacedRun> Place(
            IReadOnlyList<PlanNoteSegment> pieces, int startX, Func<string, int> advance,
            string prefix)
        {
            if (advance == null)
            {
                throw new ArgumentNullException(nameof(advance));
            }

            var placed = new List<PlacedRun>(pieces == null ? 0 : pieces.Count);
            if (pieces == null)
            {
                return placed;
            }

            string line = prefix ?? "";
            int penAdvance = advance(line);
            foreach (var piece in pieces)
            {
                line += piece.Text;
                int nextAdvance = advance(line);
                placed.Add(new PlacedRun(piece, startX + penAdvance, nextAdvance - penAdvance));
                penAdvance = nextAdvance;
            }

            return placed;
        }
    }
}
