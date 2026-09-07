using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaimisToolbench.Services;

namespace TaimisToolbench.Tests.Helpers
{
    /// <summary>
    /// Flattens a <see cref="TooltipContent"/> to plain strings, for tests
    /// that want to assert on wording rather than on span structure.
    /// <para>
    /// This lives in the test project, not on TooltipContent, because
    /// production has no plain-text consumer of composed content: every
    /// composer's output reaches the screen through the rich surface, which
    /// draws spans (and draws a coin span as gold/silver/copper ICONS - the
    /// reason the structured form exists at all). The projection used to be
    /// TooltipContent.ToPlainText/ToPlainLines, kept alive only by three
    /// wrapper methods no production code called.
    /// </para>
    /// <para>
    /// Assertions on this string therefore prove wording, not layout. A test
    /// that cares whether a gold figure survives as a coin span must assert
    /// on <see cref="TooltipContent.Lines"/> spans directly - see
    /// <see cref="CoinValues"/>.
    /// </para>
    /// </summary>
    internal static class TooltipContentPlainText
    {
        /// <summary>Every line joined by '\n' - one flat string.</summary>
        public static string ToPlainText(this TooltipContent content)
        {
            if (content == null)
            {
                return null;
            }

            var sb = new StringBuilder();
            var lines = content.Lines;
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                AppendLine(sb, lines[i]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// One plain string per line of the SECOND box - what this module
        /// adds under the game's own content. Empty when the tooltip is a
        /// single box, which is what a test asserting "this line does not
        /// belong in the item's own box" checks against.
        /// </summary>
        public static List<string> ToExtraLines(this TooltipContent content)
        {
            return content == null ? new List<string>() : content.Extra.ToPlainLines();
        }

        /// <summary>One plain string per line of the first box.</summary>
        public static List<string> ToPlainLines(this TooltipContent content)
        {
            var result = new List<string>();
            if (content == null)
            {
                return result;
            }

            foreach (var line in content.Lines)
            {
                var sb = new StringBuilder();
                AppendLine(sb, line);
                result.Add(sb.ToString());
            }

            return result;
        }

        /// <summary>
        /// The copper value of every coin span, in order. The property the
        /// rich tooltip surface actually depends on: a figure that arrives
        /// here as a coin span gets drawn with real coin icons, and one that
        /// has decayed into prose is spelled out as "1g 23s 45c" instead.
        /// </summary>
        public static long[] CoinValues(this TooltipContent content)
        {
            if (content == null)
            {
                return new long[0];
            }

            return content.Lines
                .SelectMany(l => l.Spans)
                .Where(s => s.IsCoin)
                .Select(s => s.CoinCopper)
                .ToArray();
        }

        private static void AppendLine(StringBuilder sb, TooltipLine line)
        {
            foreach (var span in line.Spans)
            {
                sb.Append(span.Text);
            }
        }
    }
}
