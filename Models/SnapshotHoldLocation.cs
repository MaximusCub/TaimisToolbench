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
        Bank = 3,
        MaterialStorage = 4,
        LegendaryArmory = 5,
        Unknown = 6,
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
        /// The character holding the item, for Bags and Equipped. Empty for
        /// the account-wide categories, which name no character.
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
        /// socket rather than a bag or a slot. Empty everywhere else, and
        /// empty as well when the capture could not name the gear, so a
        /// reader is never shown a host it cannot identify.
        /// </summary>
        public string HostItemName { get; set; } = "";

        /// <summary>
        /// The raw source key, kept for <see cref="SnapshotHoldCategory.Unknown"/>
        /// so a source the module does not yet recognize still reads as
        /// something rather than disappearing.
        /// </summary>
        public string RawSource { get; set; } = "";
    }
}
