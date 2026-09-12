using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    internal static class Gw2Constants
    {
        /// <summary>
        /// GW2 wallet currency ID for coins (gold/silver/copper).
        /// </summary>
        public const int CoinCurrencyId = 1;

        /// <summary>
        /// Reserved item id for the synthetic multi-item "wrapper"
        /// RecipeNode root. Real GW2 item ids are always positive, so this
        /// can never collide. The wrapper must never be displayed to the
        /// user or surface in any public model.
        /// </summary>
        public const int MultiItemWrapperItemId = int.MinValue;

        /// <summary>
        /// Reserved recipe id for the multi-item wrapper's single synthetic
        /// "recipe" (whose Ingredients are the N selected items' own real
        /// trees, each already carrying its own requested amount as its
        /// ingredient quantity - gw2e's mechanism verbatim). Distinct from
        /// both real recipe ids (positive) and the negative synthetic
        /// ids ref/mystic_forge_recipes.json assigns to Mystic Forge
        /// recipes (moot in practice - the wrapper's own step is never
        /// collected into a plan at all, see PlanSolver.Collect - but kept
        /// numerically distinct anyway).
        /// </summary>
        public const int MultiItemWrapperRecipeId = int.MinValue;

        /// <summary>
        /// GW2 item ids of the three Homestead Refinement output
        /// materials - exactly the ids gw2efficiency hardcodes for its
        /// per-material efficiency-tier gate.
        /// </summary>
        public const int RefinedHomesteadFiberItemId = 102306;
        public const int RefinedHomesteadMetalItemId = 102205;
        public const int RefinedHomesteadWoodItemId = 103049;

        /// <summary>
        /// The three Homestead Refinement material ids, for iteration
        /// (e.g. validating a HomesteadEfficiencyTiers map's keys).
        /// </summary>
        public static readonly IReadOnlyList<int> HomesteadRefinementMaterialIds =
            new[] { RefinedHomesteadFiberItemId, RefinedHomesteadMetalItemId, RefinedHomesteadWoodItemId };

        /// <summary>
        /// The festival name key for Halloween - Blish's runtime
        /// Festival.Halloween.Name value (lowercase "halloween", NOT the
        /// capitalized DisplayName), matching the seeded
        /// "seasonalFestival" values in ref/vendor_offers.json.
        /// </summary>
        public const string HalloweenFestivalName = "halloween";

        /// <summary>
        /// Internal festival name key (Blish's Festival.Name) ->
        /// human-readable display name (Festival.DisplayName). A curated
        /// table, NOT a generic capitalizer: DisplayName is not a simple
        /// uppercase of Name for every festival ("superadventurefestival"
        /// -> "Super Adventure Festival"), so add a new festival only with
        /// its measured display string. Covers all six festivals Blish
        /// recognizes today; an unlisted key falls back to the raw key.
        /// </summary>
        public static readonly Dictionary<string, string> FestivalDisplayNames = new Dictionary<string, string>
        {
            { HalloweenFestivalName, "Halloween" },
            { "dragonbash", "Dragon Bash" },
            { "wintersday", "Wintersday" },
            { "festivalofthefourwinds", "Festival of the Four Winds" },
            { "lunarnewyear", "Lunar New Year" },
            { "superadventurefestival", "Super Adventure Festival" },
        };

        public static string ResolveFestivalDisplayName(string festivalName)
        {
            if (festivalName != null && FestivalDisplayNames.TryGetValue(festivalName, out var display))
            {
                return display;
            }

            return festivalName;
        }

        // Currency names are sourced from api.guildwars2.com/v2/currencies;
        // adjacent ids have carried each other's names before, so verify
        // every entry against the official API when broadening coverage.
        public static readonly Dictionary<int, string> KnownCurrencyNames = new Dictionary<int, string>
        {
            { 2, "Karma" },
            { 3, "Laurels" },
            { 4, "Gems" },
            { 5, "Ascalonian Tears" },
            { 6, "Shards of Zhaitan" },
            { 7, "Fractal Relics" },
            { 9, "Seals of Beetletun" },
            { 10, "Manifesto of the Moletariate" },
            { 11, "Deadly Blooms" },
            { 12, "Symbols of Koda" },
            { 13, "Flame Legion Charr Carvings" },
            { 14, "Knowledge Crystals" },
            { 15, "Badges of Honor" },
            { 16, "Guild Commendations" },
            { 18, "Transmutation Charges" },
            { 19, "Airship Parts" },
            { 20, "Ley Line Crystals" },
            { 22, "Lumps of Aurillium" },
            { 23, "Spirit Shards" },
            { 24, "Pristine Fractal Relics" },
            { 25, "Geodes" },
            { 26, "WvW Skirmish Claim Tickets" },
            { 27, "Bandit Crests" },
            { 28, "Magnetite Shards" },
            { 29, "Provisioner Tokens" },
            { 30, "PvP League Tickets" },
            // Names cited from CurrencyDecisionDefaults' inline comments
            // and cross-checked against the live GW2 API - not invented.
            { 31, "Proof of Heroics" },
            { 32, "Unbound Magic" },
            { 33, "Ascended Shards of Glory" },
            { 34, "Trade Contracts" },
            { 35, "Elegy Mosaic" },
            { 36, "Testimony of Desert Heroics" },
            { 45, "Volatile Magic" },
            { 47, "Racing Medallions" },
            { 49, "Mistborn Keys" },
            { 50, "Festival Tokens" },
            { 53, "Green Prophet Shard" },
            { 57, "Blue Prophet Shard" },
            { 58, "War Supplies" },
            { 59, "Unstable Fractal Essence" },
            { 60, "Tyrian Defense Seals" },
            { 61, "Research Notes" },
            { 62, "Unusual Coins" },
            { 63, "Astral Acclaim" },
            { 64, "Jade Sliver" },
            { 65, "Testimony of Jade Heroics" },
            { 66, "Ancient Coins" },
            { 67, "Canach Coins" },
            // Live name is the singular "Imperial Favor".
            { 68, "Imperial Favor" },
            { 69, "Tales of Dungeon Delving" },
            { 76, "Ursus Oblige" },
            // The live API also names retired currency 39 "Gaeting Crystal";
            // 77 is the live one. docs/ARCHITECTURE.md section 8.3.
            { 77, "Gaeting Crystal" },
            { 78, "Fine Rift Essence" },
            { 79, "Rare Rift Essence" },
            { 80, "Masterwork Rift Essence" },
            { 82, "Testimony of Castoran Heroics" },
        };

        public static string ResolveCurrencyName(int currencyId)
        {
            if (KnownCurrencyNames.TryGetValue(currencyId, out var name))
            {
                return name;
            }

            return "Currency";
        }
    }
}
