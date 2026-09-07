using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The socketed components of one owned stack, resolved from item ids
    /// to the stat blocks a tooltip can actually draw.
    /// <para>
    /// An id the session has no stat block for is DROPPED rather than
    /// drawn as a placeholder: the tooltip would otherwise claim a socket
    /// holds something unnameable, and the block reappears on the next
    /// hover once the background top-up lands.
    /// </para>
    /// </summary>
    internal sealed class SocketedUpgradeView
    {
        private static readonly IReadOnlyList<ItemStatBlock> NoBlocks = new List<ItemStatBlock>();
        private static readonly Dictionary<int, int> NoCounts = new Dictionary<int, int>();

        public static readonly SocketedUpgradeView None =
            new SocketedUpgradeView(NoBlocks, NoBlocks, NoCounts);

        // Upgrade item id -> pieces of it the host's wearer has equipped,
        // for a host that sits in exactly one place and that place is one
        // character's worn gear. Empty for every other host.
        private readonly IReadOnlyDictionary<int, int> _wornCopies;

        private SocketedUpgradeView(
            IReadOnlyList<ItemStatBlock> infusions,
            IReadOnlyList<ItemStatBlock> upgrades,
            IReadOnlyDictionary<int, int> wornCopies)
        {
            Infusions = infusions ?? NoBlocks;
            Upgrades = upgrades ?? NoBlocks;
            _wornCopies = wornCopies ?? NoCounts;
        }

        public IReadOnlyList<ItemStatBlock> Infusions { get; }

        public IReadOnlyList<ItemStatBlock> Upgrades { get; }

        public bool IsEmpty => Infusions.Count == 0 && Upgrades.Count == 0;

        public static SocketedUpgradeView Resolve(
            SocketedUpgradeIds ids, Func<int, ItemStatBlock> getStatBlock)
        {
            return Resolve(ids, getStatBlock, null);
        }

        /// <summary>
        /// The same resolution, plus how many pieces carrying each socketed
        /// component the host's wearer has equipped.
        /// <paramref name="wornCopies"/> answers 0 when the host is not
        /// uniquely worn by one character, which is the only state a rune
        /// set counter can be read from - see
        /// <see cref="EquippedRuneSetIndex"/>. Pass null when the caller
        /// holds no snapshot to count against.
        /// </summary>
        public static SocketedUpgradeView Resolve(
            SocketedUpgradeIds ids, Func<int, ItemStatBlock> getStatBlock, Func<int, int> wornCopies)
        {
            if (ids == null || ids.IsEmpty || getStatBlock == null)
            {
                return None;
            }

            var infusions = Lookup(ids.Infusions, getStatBlock);
            var upgrades = Lookup(ids.Upgrades, getStatBlock);
            if (infusions.Count == 0 && upgrades.Count == 0)
            {
                return None;
            }

            return new SocketedUpgradeView(infusions, upgrades, CountWorn(upgrades, wornCopies));
        }

        /// <summary>
        /// How many pieces of <paramref name="upgradeItemId"/> the host's
        /// wearer has equipped, or 0 when that is not knowable.
        /// </summary>
        public int WornCopies(int upgradeItemId)
        {
            return _wornCopies.TryGetValue(upgradeItemId, out int worn) ? worn : 0;
        }

        private static IReadOnlyDictionary<int, int> CountWorn(
            IReadOnlyList<ItemStatBlock> upgrades, Func<int, int> wornCopies)
        {
            if (wornCopies == null || upgrades.Count == 0)
            {
                return NoCounts;
            }

            var counts = new Dictionary<int, int>(upgrades.Count);
            foreach (var upgrade in upgrades)
            {
                if (upgrade.ItemId > 0 && !counts.ContainsKey(upgrade.ItemId))
                {
                    int worn = wornCopies(upgrade.ItemId);
                    if (worn > 0)
                    {
                        counts[upgrade.ItemId] = worn;
                    }
                }
            }

            return counts.Count == 0 ? NoCounts : counts;
        }

        private static List<ItemStatBlock> Lookup(
            IReadOnlyList<int> ids, Func<int, ItemStatBlock> getStatBlock)
        {
            var blocks = new List<ItemStatBlock>(ids.Count);
            foreach (int id in ids)
            {
                if (id <= 0)
                {
                    continue;
                }

                var block = getStatBlock(id);
                if (block != null)
                {
                    blocks.Add(block);
                }
            }

            return blocks;
        }
    }
}
