using System;
using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    public enum RankerReadinessKind
    {
        /// <summary>At least one gate applies and produced a real 0..1 figure.</summary>
        Measured,

        /// <summary>No gate applies but something is still outstanding.</summary>
        NotMeasurable,

        /// <summary>Nothing left to acquire on any gate.</summary>
        NothingLeft,
    }

    /// <summary>
    /// The six independent barriers between the player and a finished item.
    /// Each is measured only against itself, in its own units - the model
    /// never converts one into another, because the GW2 API publishes no rate
    /// between days, currencies, tokens and coin and the repo forbids
    /// inventing one.
    /// <para>
    /// BarterItems is the item-id twin of Currencies, split for the reason
    /// Models/BarterItemCost.cs gives: an item id and a currency id are
    /// different id spaces that collide numerically. Without it a vendor
    /// bill paid in account-bound tokens was scored by no gate at all.
    /// </para>
    /// </summary>
    public enum RankerGate
    {
        Materials,
        Currencies,
        TimeGates,
        Disciplines,
        Recipes,
        BarterItems,
    }

    /// <summary>
    /// Why a gate is or is not a term of the headline. The distinction the
    /// bool it replaced could not draw: an item with no such barrier and an
    /// item whose barrier the module could not look at are both excluded
    /// from the blend, and only the first of them is finished.
    /// </summary>
    internal enum RankerGateStatus
    {
        /// <summary>Scored from real data. The only status that joins the blend.</summary>
        Scored,

        /// <summary>This item has no such barrier, so nothing is outstanding behind it.</summary>
        NoBarrier,

        /// <summary>
        /// The barrier exists and the ACCOUNT data needed to score it does
        /// not - an API key without the recipes permission, a snapshot that
        /// captured no character disciplines. A shipped seed that is not
        /// wired is NOT this: that is NoBarrier, matching
        /// PlanViewModelBuilder's own treatment of a missing cooldown seed.
        /// </summary>
        Unmeasured,
    }

    internal class RankerGateScore
    {
        public RankerGate Gate { get; set; }

        public RankerGateStatus Status { get; set; }

        /// <summary>
        /// True only for <see cref="RankerGateStatus.Scored"/>. A gate that
        /// is not scored is excluded from the headline (the weights
        /// renormalise over the ones that are) rather than entered at 1.0,
        /// which would hand simple items free credit.
        /// </summary>
        public bool Applies
        {
            get { return Status == RankerGateStatus.Scored; }
        }

        /// <summary>0..1, meaningful only when Applies.</summary>
        public double Completion { get; set; }

        /// <summary>The fixed weight from RankerReadinessWeights, for the tooltip.</summary>
        public double Weight { get; set; }
    }

    /// <summary>
    /// One barter item this plan still owes, and how much of it the account
    /// holds. The item-id twin of <see cref="RankerCurrencyShortfall"/>.
    /// </summary>
    internal class RankerBarterItemShortfall
    {
        public int ItemId { get; set; }

        /// <summary>What the plan still needs, gross - holdings are never netted into the cost.</summary>
        public long Needed { get; set; }

        /// <summary>Account-wide holding, from CraftingPlanResult.OwnedVendorItemAmounts.</summary>
        public long Held { get; set; }

        /// <summary>max(0, Needed - Held).</summary>
        public long Short { get; set; }

        /// <summary>The from-scratch need, the denominator of this item's completion.</summary>
        public long BaselineNeeded { get; set; }
    }

    internal class RankerCurrencyShortfall
    {
        public int CurrencyId { get; set; }

        /// <summary>What the plan still needs, gross - the solver never nets the wallet.</summary>
        public long Needed { get; set; }

        /// <summary>Wallet amount left after higher-priority slots took theirs.</summary>
        public long Held { get; set; }

        /// <summary>max(0, Needed - Held).</summary>
        public long Short { get; set; }

        /// <summary>The from-scratch need, the denominator of this currency's completion.</summary>
        public long BaselineNeeded { get; set; }
    }

    internal class RankerDisciplineGap
    {
        public string Discipline { get; set; }

        public int RequiredRating { get; set; }

        /// <summary>Best rating any character has in this discipline; 0 when never learned.</summary>
        public int BestRating { get; set; }

        /// <summary>Which character holds BestRating; null when none does.</summary>
        public string BestCharacterName { get; set; }
    }

    /// <summary>
    /// Everything one Crafting Ranker row displays, computed off two solves
    /// plus the slot's cascade availability. Display-ready: the view does no
    /// arithmetic and handles no edge cases.
    /// </summary>
    internal class RankerRowMetrics
    {
        public RankerReadinessKind Kind { get; set; }

        /// <summary>The headline, 0..1. Meaningful only when Kind is Measured.</summary>
        public double Readiness { get; set; }

        /// <summary>One entry per gate, always all six, in enum order. Never null.</summary>
        public IReadOnlyList<RankerGateScore> Gates { get; set; }

        public long RemainingCoinCost { get; set; }

        public long BaselineCoinCost { get; set; }

        /// <summary>Empty, never null.</summary>
        public IReadOnlyList<RankerCurrencyShortfall> CurrencyShortfalls { get; set; }

        /// <summary>Empty, never null. Descending by Short, like the currency list.</summary>
        public IReadOnlyList<RankerBarterItemShortfall> BarterItemShortfalls { get; set; }

        /// <summary>Vendor purchase caps - informational only, never scored. Empty, never null.</summary>
        public IReadOnlyList<TimegatedItem> VendorCappedItems { get; set; }

        /// <summary>
        /// Currency ids this plan spends that have no coin valuation, so the
        /// materials gate left them out of both halves of its ratio rather
        /// than pricing them at zero. Ascending, empty, never null. Every one
        /// is still scored by the currencies gate, in its own units.
        /// </summary>
        public IReadOnlyList<int> MaterialsUnpricedCurrencyIds { get; set; }

        /// <summary>Empty, never null. Includes satisfied disciplines so the row can say so.</summary>
        public IReadOnlyList<RankerDisciplineGap> DisciplineGaps { get; set; }

        /// <summary>
        /// Earliest day this item could finish, counting from now, given that
        /// higher-priority items have first claim on the shared daily crafts.
        /// 0 when nothing in the tree is recipe-timegated.
        /// </summary>
        public int DaysRemaining { get; set; }

        /// <summary>Days this item alone would take from what is owned now, ignoring the queue.</summary>
        public int DaysAlone { get; set; }

        /// <summary>Days a from-scratch, own-nothing, alone build would take. The time gate's denominator.</summary>
        public int DaysFromScratch { get; set; }

        /// <summary>Item ids a higher-priority slot took that this slot's plan must now acquire.</summary>
        public int ContestedItemCount { get; set; }

        /// <summary>Currency ids a higher-priority slot spent that this slot still needs.</summary>
        public int ContestedCurrencyCount { get; set; }

        public bool AffordableNow { get; set; }

        /// <summary>0 when affordable. Measured against coin left after higher-priority slots.</summary>
        public long ShortfallCoin { get; set; }

        public bool HasSnapshot { get; set; }

        /// <summary>
        /// The list position this was computed at. A row whose index no
        /// longer matches is showing a number for a slot it does not occupy.
        /// </summary>
        public int PriorityIndex { get; set; }

        /// <summary>
        /// The comparison mode this was computed under. Cascade numbers are
        /// meaningless in Independent mode and vice versa, so a mode toggle
        /// stales metrics exactly as a reorder does - see
        /// RankerPriorityOrdering.MetricsAreCurrent.
        /// </summary>
        public RankerMode Mode { get; set; }

        public DateTime ComputedAtUtc { get; set; }
    }
}
