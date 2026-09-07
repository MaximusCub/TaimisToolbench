using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Services
{
    internal class RawItem
    {
        public int Id { get; set; }

        public string Name { get; set; }

        public string Icon { get; set; }

        public string Rarity { get; set; }

        // Raw /v2/items "flags" strings (e.g. "AccountBound",
        // "SoulBindOnAcquire", "NoSell") - see Gw2ItemApiClient.GetItemsAsync.
        // Never null from that production parser (empty list when the API
        // response has no flags array); ItemMetadataService.
        // FetchBatchIntoCacheAsync reads this to set ItemMetadata.
        // IsAccountBound.
        public List<string> Flags { get; set; }

        // Top-level /v2/items fields the response already carried and the
        // parser used to discard. Present on EVERY item, including the ones
        // whose "details" block is absent (every crafting material) - which
        // is why they live here and not on RawItemDetail.
        public string ItemType { get; set; }

        public int Level { get; set; }

        public int VendorValue { get; set; }

        public string Description { get; set; }

        // Profession/race restriction strings; empty (never null) from the
        // production parser, matching the Flags convention above.
        public List<string> Restrictions { get; set; }

        // The "details" block. NULL for every detail-less item - measured
        // on Mithril Ore (19700), Orichalcum Ingot (19685) and Crystalline
        // Ingot (46683), i.e. the bulk of any plan's rows - so every
        // consumer must read null as "this item has name/rarity/vendor
        // value and nothing more", never as a parse error.
        public RawItemDetail Detail { get; set; }
    }

    /// <summary>
    /// The /v2/items "details" block, flattened to the fields an item
    /// tooltip can actually show. Every member is optional in the API and
    /// therefore nullable or empty here; nothing in this type is inferred
    /// or defaulted to a plausible value.
    /// </summary>
    internal class RawItemDetail
    {
        /// <summary>details.type - the SUBTYPE ("Gloves", "Sword", "Rune",
        /// "Food"), not the item's top-level type.</summary>
        public string SubType { get; set; }

        public string WeightClass { get; set; }

        public int? Defense { get; set; }

        public int? MinPower { get; set; }

        public int? MaxPower { get; set; }

        public string DamageType { get; set; }

        /// <summary>details.infusion_slots, one entry per slot in API
        /// order. Empty (never null) from the production parser, matching
        /// the Flags convention on RawItem.</summary>
        public List<RawInfusionSlot> InfusionSlots { get; set; }

        /// <summary>
        /// How many upgrades the DEFINITION already ships socketed:
        /// details.suffix_item_id plus details.secondary_suffix_item_id, 0
        /// to 2. The game prints the socketed upgrade where it would
        /// otherwise print an unused-slot line, so this is what stops the
        /// tooltip calling a filled slot empty. WHICH upgrade it is needs a
        /// second /v2/items request and is not carried.
        /// </summary>
        public int SocketedUpgradeCount { get; set; }

        /// <summary>
        /// details.attribute_adjustment - the per-item scalar the stat
        /// multipliers in /v2/itemstats are applied to (see
        /// ItemStatMath.AttributeValue). 0 when absent.
        /// </summary>
        public double AttributeAdjustment { get; set; }

        /// <summary>details.infix_upgrade.id - an /v2/itemstats entry id.
        /// Null when the item has no infix upgrade at all.</summary>
        public int? InfixStatId { get; set; }

        /// <summary>
        /// details.infix_upgrade.attributes - already-resolved attribute
        /// values for a FIXED-stat item. Empty for a stat-selectable item
        /// (which carries StatChoiceIds instead) and for runes/sigils.
        /// </summary>
        public List<RawItemAttribute> InfixAttributes { get; set; }

        /// <summary>details.infix_upgrade.buff.description - a sigil,
        /// infusion or jewel's effect text, verbatim.</summary>
        public string BuffDescription { get; set; }

        /// <summary>details.bonuses - a rune's 1-6 bonus lines, already
        /// formatted by the API ("+25 Power").</summary>
        public List<string> Bonuses { get; set; }

        /// <summary>details.stat_choices - /v2/itemstats ids selectable on
        /// this item. Non-empty exactly when the item's stats are chosen at
        /// acquisition rather than fixed.</summary>
        public List<int> StatChoiceIds { get; set; }

        public int? NourishmentDurationMs { get; set; }

        /// <summary>details.description - the food/utility effect block.
        /// Absent on ascended food (measured on 91805), which returns
        /// details:{type:Food} and nothing else.</summary>
        public string NourishmentDescription { get; set; }

        /// <summary>details.name - the consumable effect's own name
        /// ("Nourishment" on 89002, "Sugar Rush" on 36041). The game
        /// renders it as the effect block's lead-in.</summary>
        public string EffectName { get; set; }

        /// <summary>details.icon - the effect's render-service icon URL
        /// (the apple on food, measured on 89002/12452). Carried so the
        /// tooltip can draw the game's inline effect icon without a
        /// second request.</summary>
        public string EffectIconUrl { get; set; }
    }

    /// <summary>One details.infusion_slots entry.</summary>
    internal class RawInfusionSlot
    {
        /// <summary>The slot's own flags; empty (never null) from the
        /// production parser.</summary>
        public List<string> Flags { get; set; }

        /// <summary>
        /// Whether the entry carries an item_id, which means the definition
        /// ships this slot already filled - so the game shows what is in it
        /// rather than an unused-slot line. Rare: 102 slots across the
        /// 2026-09-05 corpus, all of them legacy "(Infused)" back items.
        /// </summary>
        public bool IsFilled { get; set; }
    }

    internal class RawItemAttribute
    {
        public string Attribute { get; set; }

        public int Modifier { get; set; }
    }

    internal interface IItemApiClient
    {
        Task<IReadOnlyList<RawItem>> GetItemsAsync(IReadOnlyList<int> itemIds, CancellationToken ct);
    }
}
