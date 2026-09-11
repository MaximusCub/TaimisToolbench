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
        /// the line's own PREFIX rather than at a running sum of per-run
        /// advances: Blish tracks its fonts at minus one pixel, so the two
        /// differ by a pixel per join.
        /// </summary>
        public static List<PlacedRun> Place(
            IReadOnlyList<PlanNoteSegment> pieces, int startX, Func<string, int> advance)
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

            string prefix = "";
            int prefixAdvance = 0;
            foreach (var piece in pieces)
            {
                prefix += piece.Text;
                int nextAdvance = advance(prefix);
                placed.Add(new PlacedRun(piece, startX + prefixAdvance, nextAdvance - prefixAdvance));
                prefixAdvance = nextAdvance;
            }

            return placed;
        }
    }
}
