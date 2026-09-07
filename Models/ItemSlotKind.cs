namespace TaimisToolbench.Models
{
    /// <summary>
    /// What an equipment slot on an item DEFINITION accepts, and therefore
    /// which line the game's tooltip prints for it while it is empty.
    /// <para>
    /// <see cref="Infusion"/> and <see cref="Enrichment"/> are the whole
    /// <c>details.infusion_slots[].flags</c> vocabulary: a census of all
    /// 74,072 /v2/items entries (2026-09-05) found 6,507 slots flagged
    /// <c>Infusion</c>, 94 flagged <c>Enrichment</c>, no other string, no
    /// slot carrying more than one flag and none carrying an empty array.
    /// The API's own schema says the same
    /// (<c>https://wiki.guildwars2.com/wiki/API:2/items</c>: "The array
    /// contains a maximum of one value").
    /// </para>
    /// <para>
    /// <see cref="Upgrade"/> has no API field at all - see
    /// <see cref="Services.ItemSlotFacts.UpgradeSlotCount"/>.
    /// </para>
    /// </summary>
    internal enum ItemSlotKind
    {
        /// <summary>Takes a sigil, a rune or a jewel.</summary>
        Upgrade,

        /// <summary>Takes an infusion.</summary>
        Infusion,

        /// <summary>Takes an enrichment - an ascended or legendary amulet's
        /// slot, which no other item type has.</summary>
        Enrichment,
    }
}
