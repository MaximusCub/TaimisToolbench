using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// A kind of place an account can hold an item. The order of the members
    /// is the order they are read out in, and
    /// Services.SnapshotHoldLine sorts by it.
    /// </summary>
    internal enum SnapshotHoldCategory
    {
        SharedInventory = 0,
        Bags = 1,
        Equipped = 2,
        EquipmentTemplate = 3,
        Bank = 4,
        MaterialStorage = 5,
        LegendaryArmory = 6,
        Unknown = 7,
    }

    /// <summary>
    /// One place holding some of a <see cref="SnapshotSearchRow"/>'s total,
    /// as category plus character plus count rather than a finished label.
    /// Whether a count is printed at all depends on the other places in the
    /// same row, so no single entry can format itself; Services.SnapshotHoldLine
    /// makes that decision for the whole row at once.
    /// </summary>
    internal class SnapshotHoldLocation
    {
        public SnapshotHoldCategory Category { get; set; }

        /// <summary>
        /// The character holding the item, for Bags, Equipped and
        /// EquipmentTemplate. Empty for the account-wide categories, which
        /// name no character.
        /// </summary>
        public string CharacterName { get; set; } = "";

        public int Count { get; set; }

        /// <summary>
        /// Characters wearing this item, for a place that holds it for the
        /// whole account. Null or empty everywhere else.
        /// <para>
        /// Only <see cref="SnapshotHoldCategory.LegendaryArmory"/> fills
        /// this in today. These characters are NOT holders: they draw the
        /// one account-wide copy <see cref="Count"/> already counts, so
        /// naming them must never add to a total.
        /// </para>
        /// </summary>
        public IReadOnlyList<string> EquippedBy { get; set; }

        /// <summary>
        /// The gear this item is socketed into, for a place that is a
        /// socket rather than a loose stack. Empty everywhere else, and
        /// empty as well when the capture could not name the gear, so a
        /// reader is never shown a host it cannot identify.
        /// </summary>
        public string HostItemName { get; set; } = "";

        /// <summary>
        /// Places holding gear that draws on this account-wide copy, each
        /// already formatted as a whole phrase ("Bank (in Dusk,
        /// Carcharias)"), one per place. Null or empty everywhere else.
        /// <para>
        /// The same rule as <see cref="EquippedBy"/>: these places hold
        /// none of the item themselves, so naming them must never add to a
        /// total. They are apart from it because a banked piece is not
        /// equipped and cannot be read out under that word.
        /// </para>
        /// </summary>
        public IReadOnlyList<string> SocketedInto { get; set; }

        /// <summary>
        /// The place half of the source key, kept for
        /// <see cref="SnapshotHoldCategory.Unknown"/> so a source the module
        /// does not yet recognize still reads as something rather than
        /// disappearing. The half, not the whole key, because a socket key
        /// carries an item id and this one is printed.
        /// </summary>
        public string RawSource { get; set; } = "";
    }
}
