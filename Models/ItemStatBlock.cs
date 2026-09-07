using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// An item's tooltip-ready facts: already-resolved attribute lines,
    /// already-decided binding wording, the description as the API wrote it.
    /// The API soup (infix_upgrade, stat_choices, attribute_adjustment) stops
    /// at <see cref="Services.ItemStatBlockFactory"/>.
    /// <para>
    /// DELIBERATELY NOT A MEMBER OF <see cref="ItemMetadata"/>, and
    /// deliberately unreachable from <see cref="PersistedPlan"/>: hanging
    /// stats off ItemMetadata would put them inside the graph
    /// PersistedPlanSchemaMemberSetTests guards, forcing a schema bump. Stat
    /// blocks are a session-scoped side channel held by ItemMetadataService
    /// alone, so a restored plan simply has none until something re-fetches
    /// (KNOWN-ISSUES #40). See docs/ARCHITECTURE.md section S1.4.
    /// </para>
    /// </summary>
    internal sealed class ItemStatBlock
    {
        public int ItemId { get; set; }

        public string Name { get; set; }

        /// <summary>GW2 API rarity string; null/empty = unknown.</summary>
        public string Rarity { get; set; }

        /// <summary>
        /// The item's render-service icon URL, for the tooltip's own
        /// header row. Same field ItemMetadata.IconUrl is filled from, out
        /// of the same /v2/items response - carried here rather than read
        /// back off the metadata dictionary so the composer stays a pure
        /// function of one stat block.
        /// </summary>
        public string IconUrl { get; set; }

        /// <summary>Top-level /v2/items "type" ("Armor", "Weapon", ...).</summary>
        public string ItemType { get; set; }

        /// <summary>details.type ("Gloves", "Sword", "Rune"); null when the
        /// item has no details block at all.</summary>
        public string SubType { get; set; }

        public string WeightClass { get; set; }

        /// <summary>Null when the item has no defense figure. A genuine 0
        /// (every weapon reports defense 0) is NOT rendered - see
        /// ItemStatBlockFactory.</summary>
        public int? Defense { get; set; }

        public int? MinPower { get; set; }

        public int? MaxPower { get; set; }

        public string DamageType { get; set; }

        public int RequiredLevel { get; set; }

        /// <summary>Fixed-stat attribute lines. Empty for a stat-selectable
        /// item, which reports <see cref="StatChoiceCount"/> instead.</summary>
        public IReadOnlyList<ItemAttributeLine> Attributes { get; set; }

        /// <summary>
        /// The equipment slots this DEFINITION leaves empty, in the order
        /// the game lists them: upgrade slots, then infusion and enrichment
        /// slots in API order. A slot the definition ships something in is
        /// absent, because it is not unused. Empty, never null, from
        /// <see cref="Services.ItemStatBlockFactory"/>.
        /// </summary>
        public IReadOnlyList<ItemSlotKind> UnusedSlots { get; set; }

        /// <summary>A rune's bonus lines, verbatim API text.</summary>
        public IReadOnlyList<string> UpgradeBonuses { get; set; }

        /// <summary>A sigil/infusion/jewel's effect line, verbatim API text.</summary>
        public string BuffDescription { get; set; }

        /// <summary>How many stat combinations this item can be acquired
        /// with; 0 for a fixed-stat item. WHICH combination is a judgment
        /// call left open - see KNOWN-ISSUES #40 (Q4) - so no numbers
        /// are computed from it here.</summary>
        public int StatChoiceCount { get; set; }

        public string NourishmentDescription { get; set; }

        public int? NourishmentDurationMs { get; set; }

        /// <summary>The consumable effect's own name - details.name
        /// ("Nourishment", "Sugar Rush") - which leads the game's effect
        /// block; null when the details block carries none.</summary>
        public string EffectName { get; set; }

        /// <summary>The effect's icon URL (details.icon), for the inline
        /// icon the game draws beside the effect block; null when the API
        /// carries none, in which case no icon is drawn.</summary>
        public string EffectIconUrl { get; set; }

        /// <summary>
        /// Display wording for the item's binding flags, one line each in
        /// render order; empty, never null. Account binding and
        /// soulbinding are independent dimensions, not a most-specific
        /// ladder: live3/relic-livingcity (2026-08-26) shows
        /// "Account Bound" AND "Soulbound on Use" stacked on one item
        /// (104938, flags AccountBound + SoulBindOnUse), so up to one line
        /// per dimension is carried.
        /// </summary>
        public IReadOnlyList<string> Bindings { get; set; }

        /// <summary>Profession/race restrictions; empty, never null.</summary>
        public IReadOnlyList<string> Restrictions { get; set; }

        /// <summary>The Unique flag, which the game prints on its own line
        /// above the binding line.</summary>
        public bool IsUnique { get; set; }

        /// <summary>Vendor sale value in copper, or null when the item
        /// carries NoSell - which is exactly when the game shows no value
        /// either.</summary>
        public long? VendorValue { get; set; }

        /// <summary>
        /// The API's own description string, markup INTACT. The
        /// <c>&lt;c=@...&gt;</c> runs are the only thing that tells plain
        /// description prose (white) apart from flavour (teal) inside one
        /// string, so the split into roles happens at compose time via
        /// <see cref="Services.ItemDescriptionSanitizer.SanitizeToSpans"/>
        /// rather than being flattened away here.
        /// </summary>
        public string Description { get; set; }
    }
}
