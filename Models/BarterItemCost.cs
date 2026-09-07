namespace TaimisToolbench.Models
{
    /// <summary>
    /// One item the plan needs that coin cannot buy, and how many of it.
    /// Two kinds land here: the account-bound tokens a vendor takes in
    /// place of coin, whose units ARE the price, and an item a vendor
    /// trades up from a map currency the module puts no coin value on
    /// (see <see cref="TradeUpCurrencyId"/> below). Both are one number
    /// per item, whatever mix of routes produced them.
    /// Nothing of this is in <see cref="CraftingPlan.TotalCoinCost"/> -
    /// a barter line has no Trading Post price to fold in (see
    /// <see cref="VendorItemCostLine.GoldValue"/>), so a coin total that
    /// omitted this list would report a plan as costing less than it does.
    /// <para>
    /// The Item-id twin of <see cref="CurrencyCost"/>, and a separate type
    /// rather than a shared one because a GW2 item id and a GW2 currency id
    /// are different id spaces that collide numerically - the same reason
    /// Models/BarterItemDecisionDefaults.cs is separate from
    /// Models/CurrencyDecisionDefaults.cs. ItemId is internal-only (repo
    /// invariant); only the resolved name/icon ever reach the UI.
    /// </para>
    /// </summary>
    internal class BarterItemCost
    {
        public int ItemId { get; set; }

        public long Amount { get; set; }

        /// <summary>
        /// The wallet currency a vendor trades up into this item, when the
        /// plan bought some of this item that way and the module puts no
        /// coin value on that currency (see
        /// <see cref="Services.CurrencyTradeUpCoalescing"/>). Those units
        /// are counted in <see cref="Amount"/> above, so the plan states
        /// the item once instead of stating part of it as its currency.
        /// Null on a row no trade-up fed, which is every row that existed
        /// before trade-ups were coalesced here.
        /// </summary>
        public int? TradeUpCurrencyId { get; set; }

        /// <summary>
        /// How much of <see cref="TradeUpCurrencyId"/> one unit of this
        /// item costs at that vendor. Null exactly when that field is.
        /// </summary>
        public int? TradeUpCurrencyPerUnit { get; set; }
    }
}
