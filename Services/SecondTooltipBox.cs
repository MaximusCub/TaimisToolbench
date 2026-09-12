using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The second box of an icon hover: what this module has to say about
    /// the subject, under the box holding the subject's own game content.
    /// <para>
    /// The order is fixed here and nowhere else. This surface's own tips
    /// come first, in the order it listed them. The right-click affordance
    /// comes last, after one blank line, and with no blank line before it
    /// when it is the only thing in the box. No call site writes that line,
    /// so no call site can misorder it or leave it out.
    /// </para>
    /// <para>
    /// Blish-free (repo invariant), so the ordering and the blank-line rule
    /// are unit-testable without a live control.
    /// </para>
    /// </summary>
    internal static class SecondTooltipBox
    {
        /// <summary>
        /// The box, from tips that are CONTENT - a line carrying a coin
        /// amount keeps its coin span instead of being spelled out.
        /// Returns empty content when there is neither a tip nor a page.
        /// </summary>
        public static TooltipContent Compose(TooltipContent tips, string wikiHint)
        {
            bool hasTips = tips != null && !tips.IsEmpty;
            if (string.IsNullOrEmpty(wikiHint))
            {
                return hasTips ? tips : TooltipContent.Empty;
            }

            var builder = new TooltipContentBuilder();
            if (hasTips)
            {
                builder.Append(tips);
                builder.Separator();
            }

            return builder.Text(wikiHint).EndLine().Build();
        }

        /// <summary>The same box from prose tips, the shape most surfaces
        /// have. A null or blank tip is dropped rather than drawn as a
        /// blank row.</summary>
        public static TooltipContent Compose(IReadOnlyList<string> tips, string wikiHint)
        {
            var builder = new TooltipContentBuilder();
            if (tips != null)
            {
                foreach (var tip in tips)
                {
                    if (!string.IsNullOrEmpty(tip))
                    {
                        builder.Text(tip).EndLine();
                    }
                }
            }

            return Compose(builder.Build(), wikiHint);
        }

        /// <summary>
        /// Hangs <paramref name="secondBox"/> under
        /// <paramref name="firstBox"/>. A second box under an EMPTY first
        /// one would render as a blank frame above the only lines there
        /// are, so it becomes the first box instead.
        /// </summary>
        public static TooltipContent Attach(TooltipContent firstBox, TooltipContent secondBox)
        {
            bool hasSecond = secondBox != null && !secondBox.IsEmpty;
            if (firstBox == null || firstBox.IsEmpty)
            {
                return hasSecond ? secondBox : TooltipContent.Empty;
            }

            return hasSecond ? firstBox.WithExtra(secondBox) : firstBox;
        }
    }
}
