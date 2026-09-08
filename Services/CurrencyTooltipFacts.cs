using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Everything the game's own currency tooltip shows, in the four
    /// fields it shows them in. The currency-space twin of
    /// <see cref="ItemTooltipIdentity"/> plus the two body facts a
    /// currency has and an item does not: what the wallet holds, and the
    /// /v2/currencies prose.
    ///
    /// <para>
    /// A separate type from the item identity ON PURPOSE. Item and currency
    /// ids collide numerically: id 24 is both a real item and the currency
    /// "Pristine Fractal Relics". Two types make the choice a compile-time
    /// one rather than a name lookup.
    /// </para>
    /// <para>
    /// There is no way to build one field by field. Every surface passes an
    /// id and gets all four fields, which is what stopped the Recipe Tree
    /// showing a bare name where the Settings grid showed the full box.
    /// </para>
    /// </summary>
    internal readonly struct CurrencyTooltipFacts
    {
        private readonly string _name;
        private readonly string _iconUrl;
        private readonly string _description;
        private readonly int? _walletQuantity;

        private CurrencyTooltipFacts(
            string name, string iconUrl, string description, int? walletQuantity)
        {
            _name = name;
            _iconUrl = iconUrl;
            _description = description;
            _walletQuantity = walletQuantity;
        }

        internal string Name
        {
            get { return _name; }
        }

        internal string IconUrl
        {
            get { return _iconUrl; }
        }

        /// <summary>
        /// The /v2/currencies <c>description</c> for this currency, or
        /// null/empty when the session never fetched one. Never invented:
        /// an absent description drops the paragraph rather than
        /// substituting prose of the module's own.
        /// </summary>
        internal string Description
        {
            get { return _description; }
        }

        /// <summary>
        /// The account's wallet holding. Null when no wallet snapshot was
        /// read at all, which is a different statement from a holding of
        /// zero and drops the line rather than claiming the player has
        /// none.
        /// </summary>
        internal int? WalletQuantity
        {
            get { return _walletQuantity; }
        }

        /// <summary>Whether there is a subject to head the tooltip with.</summary>
        internal bool HasSubject
        {
            get { return !string.IsNullOrEmpty(_name); }
        }

        /// <summary>
        /// THE currency tooltip's facts, from the id and the two
        /// dictionaries a surface already holds. The wallet holding is
        /// looked up here; an id the dictionary does not carry reads as
        /// "not known", which is not a holding of zero.
        /// </summary>
        internal static CurrencyTooltipFacts ForCurrencyId(
            int currencyId,
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata,
            IReadOnlyDictionary<int, int> walletAmounts)
        {
            int? held = null;
            if (walletAmounts != null && walletAmounts.TryGetValue(currencyId, out int amount))
            {
                held = amount;
            }

            return ForCurrencyId(currencyId, currencyMetadata, held);
        }

        /// <summary>
        /// The same facts for a surface that knows the holding from
        /// somewhere other than a wallet dictionary - a plan row already
        /// carrying the figure its own column prints, or a tab with no
        /// wallet snapshot at all, which passes null.
        /// </summary>
        internal static CurrencyTooltipFacts ForCurrencyId(
            int currencyId,
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata,
            int? walletQuantity)
        {
            CurrencyMetadata entry = null;
            if (currencyMetadata != null)
            {
                currencyMetadata.TryGetValue(currencyId, out entry);
            }

            return ForCurrencyEntry(currencyId, entry, walletQuantity);
        }

        /// <summary>
        /// The same facts for a surface whose accessor answers one entry at
        /// a time rather than handing over a dictionary - the Snapshot
        /// tab's wallet rows, which look one id up per row.
        /// </summary>
        internal static CurrencyTooltipFacts ForCurrencyEntry(
            int currencyId, CurrencyMetadata entry, int? walletQuantity)
        {
            // Name falls back to the module's own id-to-name table, the
            // same fallback CurrencyDisplayResolver.ResolveName applies, so
            // a currency /v2/currencies has not answered for is still
            // named rather than blank. Icon and description have no
            // fallback and are simply absent.
            string name = entry != null && !string.IsNullOrEmpty(entry.Name)
                ? entry.Name
                : Gw2Constants.ResolveCurrencyName(currencyId);
            string iconUrl = entry == null || string.IsNullOrEmpty(entry.IconUrl)
                ? null
                : entry.IconUrl;
            string description = entry == null || string.IsNullOrEmpty(entry.Description)
                ? null
                : entry.Description;

            return new CurrencyTooltipFacts(name, iconUrl, description, walletQuantity);
        }
    }
}
