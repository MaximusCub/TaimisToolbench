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

            string? text = WikiRequirementText.LinkTarget(requirement);

            if (text == null || !text.StartsWith(RecipeNamespacePrefix, StringComparison.Ordinal))
            {
                return null;
            }

            // "Recipe:" with nothing after it is a title with no page.
            return text.Length > RecipeNamespacePrefix.Length ? text : null;
        }
    }
}
