using System;

namespace VendorOfferUpdater
{
    /// <summary>
    /// Reads a wiki vendor row's raw "Has requirement" text and returns the
    /// name of the recipe sheet the account must own, or null.
    /// <para>
    /// The property is free-form prose written by editors and is used for
    /// every kind of gate the wiki records - masteries ("Nuhoch Language"),
    /// achievements ("Supply Line Management"), expansions, festivals,
    /// wardrobe skins, renown hearts. Only one shape is accepted here: the
    /// whole value, once an enclosing wiki link is stripped, must be a
    /// title in the "Recipe:" namespace. Measured against the full
    /// 70,644-row wiki scrape, that rule matches 18 rows, all of them
    /// Lyhr's Obsidian armour exchange behind "Recipe: Legendary Obsidian
    /// Armor", and nothing else. Anything it does not recognize is left
    /// untagged rather than guessed at.
    /// </para>
    /// </summary>
    public static class VendorUnlockRequirementParser
    {
        private const string RecipeNamespacePrefix = "Recipe:";

        /// <summary>
        /// Returns the sheet's wiki title (which is also its item name, so
        /// it resolves through the same name-to-id map cost lines use), or
        /// null when <paramref name="requirement"/> names no recipe sheet.
        /// </summary>
        public static string? ExtractRecipeSheetName(string? requirement)
        {
            if (string.IsNullOrWhiteSpace(requirement))
            {
                return null;
            }

            string text = StripSingleWikiLink(requirement!.Trim());

            if (!text.StartsWith(RecipeNamespacePrefix, StringComparison.Ordinal))
            {
                return null;
            }

            // "Recipe:" with nothing after it is a title with no page.
            return text.Length > RecipeNamespacePrefix.Length ? text : null;
        }

        /// <summary>
        /// Unwraps a value that is exactly one wiki link and nothing else -
        /// "[[Recipe: X]]" or "[[Recipe: X|display]]" both give "Recipe: X".
        /// Text with a link plus any surrounding prose is returned unchanged,
        /// so it goes on to fail the namespace test above rather than having
        /// a fragment of itself accepted.
        /// </summary>
        private static string StripSingleWikiLink(string text)
        {
            if (!text.StartsWith("[[", StringComparison.Ordinal) ||
                !text.EndsWith("]]", StringComparison.Ordinal) ||
                text.Length <= 4)
            {
                return text;
            }

            string inner = text.Substring(2, text.Length - 4);

            // A second link anywhere means this was prose, not one link.
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
    }
}
