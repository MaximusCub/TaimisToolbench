using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What equipment slots an item DEFINITION has, and which of the game's
    /// own slot glyphs marks each one. Blish-free, so the whole rule is
    /// directly testable (repo invariant).
    /// <para>
    /// Infusion and enrichment slots are read straight out of
    /// <c>details.infusion_slots</c>; upgrade slots are not in /v2/items at
    /// all and have to be derived from the item's type. Sources for both,
    /// and the corpus the derivation was checked against:
    /// docs/ARCHITECTURE.md section S1.4.
    /// </para>
    /// </summary>
    internal static class ItemSlotFacts
    {
        /// <summary>The empty upgrade socket, /v2/files
        /// <c>ui_upgrade_slot_open</c>.</summary>
        public const int UpgradeSlotAssetId = 517197;

        /// <summary>
        /// The empty infusion socket, /v2/files
        /// <c>ui_infusion_slot_defensive</c>.
        /// <para>
        /// /v2/files publishes four infusion glyphs, for a slot taxonomy
        /// the game dropped in 2016, and the API emits no flag that selects
        /// among them. This is the one the client paints beside "Unused
        /// Infusion Slot": MEASURED against a live tooltip capture of an
        /// ascended staff, where it is the closest of the five glyphs on
        /// all four slot lines. Its art is byte-identical to
        /// <c>ui_infusion_slot_agony</c> (683590), so the choice between
        /// those two is cosmetically empty.
        /// </para>
        /// </summary>
        public const int InfusionSlotAssetId = 517202;

        /// <summary>
        /// The empty enrichment socket, /v2/files
        /// <c>ui_infusion_slot_utility</c>. The file name is pre-2016:
        /// utility infusions were renamed enrichments when the infusion
        /// subtypes were merged away, and the glyph kept its old id.
        /// </summary>
        public const int EnrichmentSlotAssetId = 517204;

        // The API's whole infusion-slot flag vocabulary; see ItemSlotKind.
        private const string EnrichmentFlag = "Enrichment";

        // The /v2/items weapon details.type values the API's own schema
        // files under "Two-handed" and "Aquatic"; aquatic weapons are
        // two-handed as well. details.type is the only field that carries
        // handedness - there is no wield or hand field.
        private static readonly HashSet<string> TwoHandedWeapons = new HashSet<string>
        {
            "Greatsword", "Hammer", "LongBow", "Rifle", "ShortBow", "Staff",
            "Harpoon", "Speargun", "Trident",
        };

        // The weapon details.type values the schema files under "Other".
        // No sigil is flagged for any of them, so none takes an upgrade.
        private static readonly HashSet<string> NonEquipmentWeapons = new HashSet<string>
        {
            "LargeBundle", "SmallBundle", "Toy", "ToyTwoHanded",
        };

        /// <summary>Which glyph marks a slot of <paramref name="kind"/>.</summary>
        public static int SlotArtAssetId(ItemSlotKind kind)
        {
            switch (kind)
            {
                case ItemSlotKind.Upgrade: return UpgradeSlotAssetId;
                case ItemSlotKind.Enrichment: return EnrichmentSlotAssetId;
                default: return InfusionSlotAssetId;
            }
        }

        /// <summary>
        /// What one <c>details.infusion_slots</c> entry accepts, from its
        /// <c>flags</c>. An entry with no recognised flag is still a real
        /// slot - the entry's existence is what says so - and reads as a
        /// plain infusion slot rather than being dropped.
        /// </summary>
        public static ItemSlotKind InfusionSlotKind(IReadOnlyList<string> flags)
        {
            if (flags != null)
            {
                for (int i = 0; i < flags.Count; i++)
                {
                    if (flags[i] == EnrichmentFlag)
                    {
                        return ItemSlotKind.Enrichment;
                    }
                }
            }

            return ItemSlotKind.Infusion;
        }

        /// <summary>
        /// How many sigil/rune/jewel slots an item has: two per two-handed
        /// weapon, one per one-handed weapon, armour piece, trinket or back
        /// item, none for anything else. A weapon type this rule has never
        /// seen reads as one-handed, the commoner of the two.
        /// <para>
        /// <paramref name="notUpgradeable"/> is the item's /v2/items
        /// <c>NotUpgradeable</c> flag, and it is what makes rarity
        /// irrelevant here: an ascended trinket or back item has a jewel's
        /// stats baked in and no slot to put one in, and it carries that
        /// flag - as do the Bloodbound and Dreambound weapon families at
        /// ordinary rarities. Keying on rarity would get both groups wrong.
        /// </para>
        /// </summary>
        public static int UpgradeSlotCount(string itemType, string subType, bool notUpgradeable)
        {
            if (notUpgradeable)
            {
                return 0;
            }

            switch (itemType)
            {
                case "Weapon":
                    if (subType == null || NonEquipmentWeapons.Contains(subType))
                    {
                        return 0;
                    }

                    return TwoHandedWeapons.Contains(subType) ? 2 : 1;
                case "Armor":
                case "Trinket":
                case "Back":
                    return 1;
                default:
                    return 0;
            }
        }
    }
}
