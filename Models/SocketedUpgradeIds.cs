using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// What one owned stack has socketed into it, as item ids: the
    /// infusions and enrichments in its infusion slots, and the runes,
    /// sigils or jewels in its upgrade slots.
    /// <para>
    /// The two stay apart because the game's tooltip keeps them apart -
    /// the infusion block sits above the rune block on ascended armour -
    /// and because only the infusion list can be checked against the item
    /// definition's own <c>infusion_slots</c> count. Both are empty rather
    /// than null, so a caller never has to null-check a socket list.
    /// </para>
    /// </summary>
    internal sealed class SocketedUpgradeIds
    {
        private static readonly IReadOnlyList<int> NoIds = new List<int>();

        public static readonly SocketedUpgradeIds None = new SocketedUpgradeIds(NoIds, NoIds);

        internal SocketedUpgradeIds(IReadOnlyList<int> infusions, IReadOnlyList<int> upgrades)
        {
            Infusions = infusions ?? NoIds;
            Upgrades = upgrades ?? NoIds;
        }

        public IReadOnlyList<int> Infusions { get; }

        public IReadOnlyList<int> Upgrades { get; }

        public bool IsEmpty => Infusions.Count == 0 && Upgrades.Count == 0;
    }
}
