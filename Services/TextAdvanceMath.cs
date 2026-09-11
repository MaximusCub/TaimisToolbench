using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Where the pen ends up after a string, from a font measurement that
    /// reports something else (Blish-free, unit-testable).
    /// <para>
    /// A BitmapFont measurement is the right edge of the string's last
    /// DRAWN glyph box, not the pen position after it. Menomonia's space
    /// region is blank, so a string ending in a space measures no wider
    /// than the word before it, and every other string falls short of its
    /// pen by its last glyph's right bearing.
    /// </para>
    /// <para>
    /// Any layout that puts two labels side by side in one sentence has to
    /// use the pen instead, or the space between them disappears. MEASURED
    /// in game 2026-09-10: a Plan Notes link drew 1px from the word before
    /// it, and a craft step's item name 2px from the quantity before it,
    /// against 5px between ordinary words on the same rows.
    /// </para>
    /// </summary>
    internal static class TextAdvanceMath
    {
        /// <summary>
        /// The glyph appended to recover a pen position. A digit, because
        /// its box is no narrower than its advance in every shipped
        /// Menomonia face, so it is always the measured right edge of the
        /// string it ends.
        /// </summary>
        public const string PenProbe = "0";

        /// <summary>
        /// Turns a right-edge measurement into a pen advance: the probe is
        /// drawn AT the pen, so subtracting its own measured width leaves
        /// the pen. One probe measurement is taken up front.
        /// </summary>
        public static Func<string, int> AdvanceWith(Func<string, int> measure)
        {
            if (measure == null)
            {
                throw new ArgumentNullException(nameof(measure));
            }

            int probe = measure(PenProbe);
            return s => measure((s ?? "") + PenProbe) - probe;
        }
    }
}
