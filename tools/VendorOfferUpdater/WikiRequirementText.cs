using System;
using System.Text;

namespace VendorOfferUpdater
{
    /// <summary>
    /// Turns a wiki "Has requirement" value into the two forms the rest of
    /// the pipeline needs: the page title it names, and the prose a player
    /// reads.
    /// <para>
    /// Measured over the 70,644-row scrape: once links are reduced to their
    /// display text, the only wiki markup left in any of the 1,236 distinct
    /// values is the "#anchor" of a page title, and every character is one
    /// the shipped Blish font can draw (docs/font-codepoints.txt). So no
    /// entity decoding or italic stripping is needed here, and none is done
    /// - an unrecognized construct is left visible rather than mangled.
    /// </para>
    /// </summary>
    public static class WikiRequirementText
    {
        /// <summary>
        /// The page title a value names, for matching against API name
        /// lists. A value that is exactly one wiki link gives that link's
        /// TARGET ("[[Recipe: X|sheet]]" gives "Recipe: X"); anything else,
        /// including a link with prose around it, is returned trimmed and
        /// otherwise unchanged, so it goes on to fail an exact-name test
        /// rather than having a fragment of itself accepted.
        /// </summary>
        public static string? LinkTarget(string? requirement)
        {
            if (string.IsNullOrWhiteSpace(requirement))
            {
                return null;
            }

            string text = requirement!.Trim();

            if (!text.StartsWith("[[", StringComparison.Ordinal) ||
                !text.EndsWith("]]", StringComparison.Ordinal) ||
                text.Length <= 4)
            {
                return text;
            }

            string inner = text.Substring(2, text.Length - 4);

            // A second bracket anywhere means this was prose, not one link.
            if (inner.IndexOf('[') >= 0 || inner.IndexOf(']') >= 0)
            {
                return text;
            }

            int pipe = inner.IndexOf('|');
            if (pipe >= 0)
            {
                inner = inner.Substring(0, pipe);
            }

            return inner.Trim();
        }

        /// <summary>
        /// The value as a player should read it: every "[[target|display]]"
        /// reduced to its display text, "[[target]]" to its target, and all
        /// runs of whitespace collapsed to one space.
        /// </summary>
        public static string? ToDisplayText(string? requirement)
        {
            if (string.IsNullOrWhiteSpace(requirement))
            {
                return null;
            }

            var sb = new StringBuilder(requirement!.Length);
            int i = 0;
            while (i < requirement.Length)
            {
                int open = requirement.IndexOf("[[", i, StringComparison.Ordinal);
                if (open < 0)
                {
                    sb.Append(requirement, i, requirement.Length - i);
                    break;
                }

                int close = requirement.IndexOf("]]", open + 2, StringComparison.Ordinal);
                if (close < 0)
                {
                    sb.Append(requirement, i, requirement.Length - i);
                    break;
                }

                sb.Append(requirement, i, open - i);

                string inner = requirement.Substring(open + 2, close - open - 2);
                int pipe = inner.LastIndexOf('|');
                sb.Append((pipe >= 0 ? inner.Substring(pipe + 1) : inner).Trim());

                i = close + 2;
            }

            return CollapseWhitespace(sb.ToString());
        }

        private static string? CollapseWhitespace(string text)
        {
            var sb = new StringBuilder(text.Length);
            bool pendingSpace = false;
            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }

                sb.Append(c);
            }

            return sb.Length == 0 ? null : sb.ToString();
        }
    }
}
