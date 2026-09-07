using System;
using System.Collections.Generic;

namespace VendorOfferUpdater.Models
{
    public static class Gw2Constants
    {
        public const int CoinCurrencyId = 1;

        public const int CopperPerSilver = 100;

        public const int CopperPerGold = 10000;

        /// <summary>
        /// How many copper one unit of a wiki coin price is worth, or false
        /// for a name that is not a coin price at all.
        /// <para>
        /// A wiki vendor row writes a coin price under whichever of "Coin",
        /// "Coins", "Copper", "Silver" or "Gold" the page's editor chose.
        /// Currency id 1 is always counted in copper, so a "Gold" value has
        /// to be multiplied by 10000 and a "Silver" value by 100 before it
        /// becomes a cost line. This is also the list
        /// Gw2ApiHelper.ResolveCurrencyId answers currency id 1 for, so the
        /// two never disagree about which names are coin.
        /// </para>
        /// </summary>
        public static bool TryGetCopperPerUnit(string? currencyName, out int copperPerUnit)
        {
            copperPerUnit = 0;

            if (string.IsNullOrEmpty(currencyName))
            {
                return false;
            }

            if (string.Equals(currencyName, "Coin", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currencyName, "Coins", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(currencyName, "Copper", StringComparison.OrdinalIgnoreCase))
            {
                copperPerUnit = 1;
                return true;
            }

            if (string.Equals(currencyName, "Silver", StringComparison.OrdinalIgnoreCase))
            {
                copperPerUnit = CopperPerSilver;
                return true;
            }

            if (string.Equals(currencyName, "Gold", StringComparison.OrdinalIgnoreCase))
            {
                copperPerUnit = CopperPerGold;
                return true;
            }

            return false;
        }

        // The three Homestead Refinement output
        // materials. The main app's Models/Gw2Constants.cs declares the
        // same three ids; that class is much larger and the two have
        // otherwise diverged. Kept as a separate copy because this tool
        // does not reference the main app's assembly (net8.0 here, net48
        // there) - the same reason Models/VendorOffer.cs is duplicated.
        // These ids come from the GW2 API and do not change; nothing
        // enforces that the two files agree, and nothing needs to, since
        // only the hash contract is consequential when it drifts.
        public const int RefinedHomesteadFiberItemId = 102306;
        public const int RefinedHomesteadMetalItemId = 102205;
        public const int RefinedHomesteadWoodItemId = 103049;

        public static bool IsHomesteadRefinementMaterialId(int itemId)
        {
            return itemId == RefinedHomesteadFiberItemId ||
                   itemId == RefinedHomesteadMetalItemId ||
                   itemId == RefinedHomesteadWoodItemId;
        }

        /// <summary>
        /// The GW2 Wiki's display-name text for each of the six festivals
        /// Blish HUD's FestivalContext recognizes, as it appears in a
        /// vendor page's {{Temporary|...|seasonal=...}} (or event=)
        /// template parameter, mapped to the internal lowercase festival
        /// key. Matched EXACTLY, never fuzzy: a wiki value not listed here
        /// must leave the offer untagged (ResolveSeasonalFestivalKey).
        /// Both sides of every mapping were measured (live wiki fetches
        /// and a raw string scan of Blish HUD.exe), not invented - add a
        /// new entry only with both sides verified.
        /// </summary>
        public static readonly Dictionary<string, string> FestivalKeysByWikiDisplayName =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Halloween", "halloween" },
                { "Dragon Bash", "dragonbash" },
                { "Wintersday", "wintersday" },
                { "Festival of the Four Winds", "festivalofthefourwinds" },
                { "Lunar New Year", "lunarnewyear" },
                { "Super Adventure Festival", "superadventurefestival" },
            };

        /// <summary>
        /// Resolves a raw wiki-page seasonal/event display-name string
        /// (from TemporaryTemplateParser.ExtractSeasonalOrEventParameter)
        /// to the internal festival name key this module compares
        /// against, or null if the value is not one of the six known
        /// festivals - e.g. a one-off non-festival event such as "Fractal
        /// Rush" or "Fractal Incursion" (both confirmed live on real
        /// vendor NPC pages: "Consortium Trader (Fractal Rush)",
        /// "Starter Equipment Vendor"), or a page with no seasonal/event
        /// parameter at all. Callers must leave the offer untagged (never
        /// guess a festival) and log a warning for a non-null-but-
        /// unrecognized value specifically, per repo invariant (no
        /// invented data) - see ConvertToOffer in Program.cs.
        /// </summary>
        public static string? ResolveSeasonalFestivalKey(string? wikiDisplayName)
        {
            if (string.IsNullOrWhiteSpace(wikiDisplayName))
            {
                return null;
            }

            string trimmed = wikiDisplayName.Trim();
            return FestivalKeysByWikiDisplayName.TryGetValue(trimmed, out var key) ? key : null;
        }
    }
}
