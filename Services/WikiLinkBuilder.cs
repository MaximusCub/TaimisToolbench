using System;
using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Pure GW2 wiki URL construction (UI-bundle milestone, Feature A -
    /// wiki links). Blish-free by design so the title-encoding rules
    /// (spaces to underscores, percent-encoding for apostrophes and other
    /// reserved characters) can be exercised by a real unit test without
    /// any Control/Process dependency - the actual browser launch is a
    /// separate, untested, side-effecting call (see WikiLinkLauncher).
    /// <para>
    /// Mirrors standard MediaWiki title normalization: a space becomes an
    /// underscore, then the remaining title is percent-encoded via
    /// <see cref="Uri.EscapeDataString(string)"/> (RFC 3986 "unreserved"
    /// characters - letters, digits, '-', '.', '_', '~' - are left
    /// literal, so the underscores just inserted survive the encode step
    /// unchanged). The GW2 wiki's own recipe "sheet" pages use a literal
    /// namespace-style colon prefix (e.g. "Recipe:_Bolt_of_Damask") which
    /// this deliberately does NOT run through EscapeDataString (that would
    /// turn the colon into "%3A", which does not match the site's real
    /// URLs) - see BuildRecipeSheetUrl.
    /// </para>
    /// </summary>
    internal static class WikiLinkBuilder
    {
        private const string BaseUrl = "https://wiki.guildwars2.com/wiki/";
        private const string RecipeNamespacePrefix = "Recipe:_";
        private const string AcquisitionAnchor = "#Acquisition";

        // Fix-pass (dead-link placeholder names): every one of these is a
        // literal, exact-string name-resolution fallback used elsewhere in
        // this module when the real name could not be resolved -
        // CraftingTreeBuilder.ResolveName ("Unknown Item"), the
        // GuildUpgrade branch ("Guild upgrade (unresolved)"), the
        // non-Item/non-Currency branch ("Unrecognized ingredient type"),
        // Gw2Constants.ResolveCurrencyName's unknown-id fallback
        // ("Currency"), and PlanViewModelBuilder.ResolveName ("Unknown
        // Item" again). None of these describe a real wiki page - a row
        // carrying one of them still advertises the right-click wiki
        // affordance and, on click, opens a guaranteed-404 URL while stealing
        // focus into the browser. Centralized here (rather than at each of
        // the several call sites that build a link from a resolved name)
        // so every BuildXxxUrl method below returns null for these names
        // and every caller's existing "wikiUrl != null" gate suppresses
        // both the click handler and the tooltip hint together.
        private static readonly HashSet<string> SentinelNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Unknown Item",
            "Guild upgrade (unresolved)",
            "Unrecognized ingredient type",
            "Currency",
        };

        /// <summary>
        /// The item's own wiki page, e.g. "Zojja's Claymore" -&gt;
        /// ".../wiki/Zojja%27s_Claymore".
        /// </summary>
        public static string BuildItemPageUrl(string itemName)
        {
            string title = EncodeTitle(itemName);
            return title.Length == 0 ? null : BaseUrl + title;
        }

        /// <summary>
        /// The item's wiki page with a "#Acquisition" anchor appended -
        /// degrades gracefully to the page top on a wiki page that has no
        /// such section (page titles match item names via wiki redirects,
        /// per the feature spec).
        /// </summary>
        public static string BuildItemAcquisitionUrl(string itemName)
        {
            string url = BuildItemPageUrl(itemName);
            return url == null ? null : url + AcquisitionAnchor;
        }

        /// <summary>
        /// The recipe's own "Recipe: &lt;output item name&gt;" sheet page -
        /// only meaningful for a recipe unlocked via LearnedFromItem (a
        /// consumable recipe sheet), which is the only kind of recipe that
        /// has one.
        /// <para>
        /// Assumes the sheet page title is always exactly "Recipe: " plus
        /// the output item's name - true for the overwhelming majority of
        /// GW2 recipe sheets, but not a documented site-wide guarantee.
        /// This module has no in-module data source to validate the sheet
        /// title against the real wiki, so an output item whose sheet page
        /// is titled differently still produces a URL here and 404s when
        /// followed.
        /// </para>
        /// </summary>
        public static string BuildRecipeSheetUrl(string outputItemName)
        {
            string title = EncodeTitle(outputItemName);
            return title.Length == 0 ? null : BaseUrl + RecipeNamespacePrefix + title;
        }

        /// <summary>
        /// Required Recipes Missing! row link target (flag-based per the
        /// feature spec): a recipe unlocked via a LearnedFromItem
        /// consumable links to its own "Recipe: &lt;name&gt;" sheet page;
        /// every other recipe links to the output item's own page with an
        /// "#Acquisition" anchor.
        /// </summary>
        public static string BuildRequiredRecipeUrl(string outputItemName, bool isLearnedFromItem)
        {
            return isLearnedFromItem
                ? BuildRecipeSheetUrl(outputItemName)
                : BuildItemAcquisitionUrl(outputItemName);
        }

        /// <summary>
        /// Cheap pre-check for whether <paramref name="itemName"/> would
        /// resolve to a real wiki link (non-blank and not one of the
        /// <see cref="SentinelNames"/> placeholder fallbacks) - without the
        /// Trim/Replace/Uri.EscapeDataString work EncodeTitle does to
        /// actually construct a URL. Intended for a hot render path (e.g.
        /// one call per tree row, rebuilt on every lazy expand) that only
        /// needs to know whether to show the click affordance at all; the
        /// real URL is then built lazily, only inside the click handler.
        /// </summary>
        public static bool HasWikiPage(string itemName)
        {
            return !string.IsNullOrWhiteSpace(itemName) && !SentinelNames.Contains(itemName.Trim());
        }

        private static string EncodeTitle(string name)
        {
            if (!HasWikiPage(name))
            {
                return "";
            }

            string underscored = name.Trim().Replace(' ', '_');
            return Uri.EscapeDataString(underscored);
        }
    }
}
