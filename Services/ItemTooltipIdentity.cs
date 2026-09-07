namespace TaimisToolbench.Services
{
    /// <summary>
    /// Who an item tooltip is ABOUT, in the three fields its header row
    /// needs: name, icon url, resolved rarity. Every surface that can draw
    /// an item icon already holds all three - that is what it drew the icon
    /// from - so this is never a fetch and never null-by-omission.
    ///
    /// <para>
    /// It exists because the header used to be reachable only through
    /// <see cref="Models.ItemStatBlock"/>, which is a SESSION cache
    /// (<c>ItemMetadataService.GetCachedStatBlock</c> never fetches). An
    /// item nothing had looked up this session composed to a bare text
    /// line, so two adjacent rows on the same tab - same rarity, same type
    /// - showed one tooltip with an icon and one without, entirely
    /// according to whether some earlier plan had touched that id. The
    /// identity is what the row KNOWS; the stat block only ever enriches
    /// the body below it.
    /// </para>
    /// </summary>
    internal readonly struct ItemTooltipIdentity
    {
        private readonly string _name;
        private readonly string _iconUrl;
        private readonly string _rarity;
        private readonly bool _currency;

        private ItemTooltipIdentity(string name, string iconUrl, string rarity, bool currency)
        {
            _name = name;
            _iconUrl = iconUrl;
            _rarity = rarity;
            _currency = currency;
        }

        internal string Name
        {
            get { return _name; }
        }

        internal string IconUrl
        {
            get { return _iconUrl; }
        }

        /// <summary>What <c>ItemRarityResolution.Resolve</c> returned; null
        /// is a legitimately unknown rarity and colours the name
        /// neutral.</summary>
        internal string Rarity
        {
            get { return _rarity; }
        }

        /// <summary>Whether there is a subject to head the tooltip with. A
        /// nameless identity heads nothing rather than opening on an icon
        /// beside an empty string.</summary>
        internal bool HasSubject
        {
            get { return !string.IsNullOrEmpty(_name); }
        }

        /// <summary>
        /// WHO the header row is about, which is what decides the frame
        /// drawn around its icon. A currency takes the ring its mostly
        /// transparent art needs; an item takes the plate its full-bleed
        /// art fills. Read off the id space the row drew its icon from, so
        /// a currency row cannot reach the header as an item.
        /// </summary>
        internal TooltipHeaderSubject Subject
        {
            get
            {
                return _currency
                    ? TooltipHeaderSubject.Currency()
                    : TooltipHeaderSubject.ItemOfRarity(_rarity);
            }
        }

        /// <summary>
        /// The row's own three fields. <paramref name="resolvedRarity"/> is
        /// the ONE value that also coloured the row's frame and name - see
        /// <see cref="ItemRarityResolution.Resolve"/>; resolving it twice
        /// is how they came to disagree.
        /// </summary>
        internal static ItemTooltipIdentity ForItem(string name, string iconUrl, string resolvedRarity)
        {
            return new ItemTooltipIdentity(name, iconUrl, resolvedRarity, currency: false);
        }

        /// <summary>
        /// A CURRENCY row's subject. There is no rarity to resolve, and the
        /// header icon takes the ring frame rather than the plate that
        /// would show through its art as a grey background. The call site
        /// says which it means from the id space it drew the icon from,
        /// never from the name.
        /// </summary>
        internal static ItemTooltipIdentity ForCurrency(string name, string iconUrl)
        {
            return new ItemTooltipIdentity(name, iconUrl, null, currency: true);
        }

        /// <summary>
        /// A tooltip with no named subject of its own - the body is the
        /// whole of it. The call site is on record that it looked and there
        /// is nothing to head with, rather than having passed nothing.
        /// </summary>
        internal static ItemTooltipIdentity Unnamed()
        {
            return new ItemTooltipIdentity(null, null, null, currency: false);
        }
    }
}
