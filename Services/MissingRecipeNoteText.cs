using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What the module says about a recipe the plan needs, the account has
    /// not learned, and some merchant sells the sheet for.
    /// <para>
    /// Two surfaces read it and both are here so they cannot drift: the
    /// Required Recipes table's Sold By cell, which is the merchant phrase
    /// alone, and the Plan Notes line, which wraps that phrase in a
    /// sentence and links it to the sheet's merchant list on the wiki.
    /// </para>
    /// <para>
    /// The note states where to buy and nothing about the price. The price
    /// is the same table's Cost cell, on the row this note is about.
    /// </para>
    /// </summary>
    internal static class MissingRecipeNoteText
    {
        /// <summary>The sentence before the merchant link.</summary>
        public const string LeadIn = "Missing Recipe. Buy from ";

        /// <summary>
        /// Who sells the sheet: the merchant this pass named, and how many
        /// others charge the same. Null when the source named no merchant
        /// at all, which is what suppresses both surfaces.
        /// </summary>
        public static string Merchants(MissingRecipeSheetSource source)
        {
            if (source == null || string.IsNullOrEmpty(source.MerchantName))
            {
                return null;
            }

            return source.OtherMerchantCount > 0
                ? source.MerchantName + " and "
                    + StatusText.Count(source.OtherMerchantCount, "other merchant")
                : source.MerchantName;
        }

        /// <summary>
        /// The Plan Notes sentence, split at the link. The whole merchant
        /// phrase is the link, because a reader who wants the merchant this
        /// note does not name wants the wiki's full list -
        /// <paramref name="acquisition"/> is the sheet page's Acquisition
        /// section. Null when there is no merchant to name.
        /// </summary>
        public static List<PlanNoteSegment> Segments(
            MissingRecipeSheetSource source, IconWikiTarget acquisition)
        {
            string merchants = Merchants(source);
            if (merchants == null)
            {
                return null;
            }

            return new List<PlanNoteSegment>(2)
            {
                PlanNoteSegment.Plain(LeadIn),
                PlanNoteSegment.Linked(merchants, acquisition),
            };
        }
    }
}
