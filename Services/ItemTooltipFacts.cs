using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Everything an item icon draws and everything its tooltip shows, from
    /// its id. The item-space twin of <see cref="CurrencyTooltipFacts"/>.
    ///
    /// <para>
    /// There is no way to build one field by field. A surface passes an id
    /// and gets all of it, so two surfaces cannot show different boxes for
    /// the same item the way the Recipe Tree and the Settings grid once did
    /// for the same currency.
    /// </para>
    /// </summary>
    internal readonly struct ItemTooltipFacts
    {
        private readonly string _name;
        private readonly string _iconUrl;
        private readonly string _rarity;
        private readonly ItemStatBlock _stats;
        private readonly SocketedUpgradeView _sockets;
        private readonly TransmutedSkin _skin;

        private ItemTooltipFacts(
            string name, string iconUrl, string rarity, ItemStatBlock stats,
            SocketedUpgradeView sockets, TransmutedSkin skin)
        {
            _name = name;
            _iconUrl = iconUrl;
            _rarity = rarity;
            _stats = stats;
            _sockets = sockets;
            _skin = skin;
        }

        internal string Name
        {
            get { return _name; }
        }

        internal string IconUrl
        {
            get { return _iconUrl; }
        }

        /// <summary>What <see cref="ItemRarityResolution"/> settled on. Null
        /// is a legitimately unknown rarity and draws the neutral frame; it
        /// is never a guess.</summary>
        internal string Rarity
        {
            get { return _rarity; }
        }

        /// <summary>The item's own stat block, or null when this session
        /// has not fetched it yet. Absent is not empty: the tooltip still
        /// heads on the icon and name.</summary>
        internal ItemStatBlock Stats
        {
            get { return _stats; }
        }

        internal SocketedUpgradeView Sockets
        {
            get { return _sockets ?? SocketedUpgradeView.None; }
        }

        internal TransmutedSkin Skin
        {
            get { return _skin ?? TransmutedSkin.None; }
        }

        internal ItemTooltipIdentity Identity
        {
            get { return ItemTooltipIdentity.ForItem(_name, _iconUrl, _rarity); }
        }

        /// <summary>
        /// THE item tooltip's facts. <paramref name="metadata"/> and
        /// <paramref name="stats"/> both come from the session store keyed
        /// by the same id, so the icon and the box can never be about
        /// different items.
        /// </summary>
        internal static ItemTooltipFacts ForItemId(
            int itemId, ItemMetadata metadata, ItemStatBlock stats)
        {
            return ForItemId(itemId, metadata, stats, null, null);
        }

        /// <summary>
        /// The same facts for a stack the account actually holds, whose
        /// socketed components and worn skin the snapshot recorded.
        /// </summary>
        internal static ItemTooltipFacts ForItemId(
            int itemId,
            ItemMetadata metadata,
            ItemStatBlock stats,
            SocketedUpgradeView sockets,
            TransmutedSkin skin)
        {
            // The id itself is never drawn. It is here so a caller cannot
            // pass facts about one item under another's id.
            _ = itemId;

            // Name and icon prefer the stat block, which came from the same
            // /v2/items reply as the metadata and is the richer record.
            // Rarity goes through the module's one resolution policy.
            string name = FirstNonEmpty(stats?.Name, metadata?.Name);
            string iconUrl = FirstNonEmpty(stats?.IconUrl, metadata?.IconUrl);
            string rarity = ItemRarityResolution.Resolve(metadata?.Rarity, stats?.Rarity);

            return new ItemTooltipFacts(name, iconUrl, rarity, stats, sockets, skin);
        }

        /// <summary>
        /// The facts a surface has for an item it drew from its own capture
        /// rather than from the session store - a saved plan's row, a
        /// watchlist entry, a search result. The captured name, icon and
        /// rarity lead, and the session's stat block fills the body when it
        /// has one.
        /// </summary>
        internal static ItemTooltipFacts ForCapturedItem(
            string name, string iconUrl, string resolvedRarity, ItemStatBlock stats)
        {
            return ForCapturedItem(name, iconUrl, resolvedRarity, stats, null, null);
        }

        /// <summary>The same captured item, with the socketed components
        /// and worn skin a snapshot recorded for the stack.</summary>
        internal static ItemTooltipFacts ForCapturedItem(
            string name,
            string iconUrl,
            string resolvedRarity,
            ItemStatBlock stats,
            SocketedUpgradeView sockets,
            TransmutedSkin skin)
        {
            return new ItemTooltipFacts(
                FirstNonEmpty(name, stats?.Name),
                FirstNonEmpty(iconUrl, stats?.IconUrl),
                resolvedRarity,
                stats,
                sockets,
                skin);
        }

        /// <summary>A subject this surface cannot name - a saved plan row
        /// whose item summaries were never captured. The body is the whole
        /// of the tooltip.</summary>
        internal static ItemTooltipFacts Unnamed()
        {
            return new ItemTooltipFacts(null, null, null, null, null, null);
        }

        private static string FirstNonEmpty(string first, string second)
        {
            return string.IsNullOrEmpty(first) ? second : first;
        }
    }
}
