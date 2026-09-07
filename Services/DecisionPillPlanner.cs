using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal enum PillKind
    {
        Selected,
        Available,
        Locked,
        Have,

        // Non-interactive "HAVE N/M NEEDED" annotation - informational
        // only, never clickable, coexists alongside whichever source
        // pill(s) this node already has.
        OwnedInfo,

        // Interactive per-item "IGNORE"/"IGNORED" toggle - clicking marks
        // this item id as fully in-hand tree-wide for this session's
        // re-solves. Text alone carries the current state; Kind is shared
        // so the view styles both states from one switch arm.
        Ignore,

        // Non-interactive "COUNTED ELSEWHERE" annotation - the sole pill
        // on a node AchievementBitDedupPrePass zeroed. Replaces the plain
        // HAVE pill entirely: nothing here is actually owned, and a
        // genuinely-owned node's HAVE display must never be confused with
        // "needed once, just not counted twice".
        AchievementBitDeduped,

        // A non-selected pill that PillSubduingEvaluator found decisively
        // loses to the selected pill. Reuses Locked's muted-gray color but
        // NOT Locked itself: Source stays non-null (still a clickable
        // override), and TreeSectionController assumes Selected/Locked are
        // the only Kinds a committed decision can carry. Never applied to
        // the Selected pill.
        Subdued,
    }

    internal readonly struct PillSpec
    {
        public readonly string Text;
        public readonly AcquisitionSource? Source; // non-null => clickable
        public readonly PillKind Kind;

        // Non-null only when Kind == Subdued - the structured (id-only)
        // reason, so the View layer builds the "why" tooltip text without
        // this Blish-free class formatting display text or exposing a raw
        // id (repo invariant).
        public readonly PillSubduingResult SubduingResult;

        public PillSpec(string text, AcquisitionSource? source, PillKind kind, PillSubduingResult subduingResult = null)
        {
            Text = text;
            Source = source;
            Kind = kind;
            SubduingResult = subduingResult;
        }
    }

    /// <summary>
    /// Pure decision-to-pill mapping for the recipe tree - Blish-free so
    /// every CanCraft/CanBuyTp/CanBuyVendor combination is directly unit
    /// testable; the view only turns these specs into controls, never
    /// decides which pills exist or which is selected.
    /// </summary>
    internal static class DecisionPillPlanner
    {
        // The module-owned source badge texts, shared with
        // ShoppingSourceBadge (the other renderer of a seeded
        // AcquisitionHint badge). A source badge means the item has a real
        // source whose cost is in Plan.TotalCoinCost; a hint badge means
        // the opposite (Decision == Unknown, contributes 0 and still
        // counts as unpriced - see PlanViewModelBuilder.HasUnpricedNode),
        // so a hint badge is never allowed to render one of these strings.
        private const string CraftPillText = "CRAFT";
        private const string TpPillText = "TP";
        private const string VendorPillText = "VENDOR";
        private const string CurrencyPillText = "CURRENCY";

        /// <summary>
        /// The Ignore toggle's two STATE names. Not what the control
        /// draws - it draws a remove mark in a raised or pressed key, so
        /// that there is nothing on it to translate - but what its tooltip
        /// leads with (<see cref="PillTooltipTextComposer"/>), which is the
        /// only place a reader can ask which state a row is in.
        /// </summary>
        public const string IgnorePillText = "IGNORE";

        /// <summary>The toggled state of <see cref="IgnorePillText"/>.</summary>
        public const string IgnoredPillText = "IGNORED";

        /// <summary>
        /// Leading word of the currency trade-up pill. Two OwnedInfo pills
        /// can share a row, so this prefix is how
        /// <see cref="PillTooltipTextComposer"/> tells them apart - the
        /// same job the Ignore toggle's text does for its two states.
        /// </summary>
        internal const string CurrencyTradeUpPillPrefix = "BUYS ";

        /// <summary>
        /// True when <paramref name="text"/> would render identically to a
        /// module-owned source badge. Case-insensitive: nothing
        /// upper-cases a badge on the way to the screen, so "Vendor"
        /// collides just as badly.
        /// </summary>
        public static bool IsReservedSourceBadgeText(string text)
        {
            return string.Equals(text, CraftPillText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, TpPillText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, VendorPillText, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, CurrencyPillText, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// One pill per feasible acquisition source: 2-3 pills means a real
        /// choice, exactly 1 means the source is locked - the pill count is
        /// the affordance. HAVE/CURRENCY/GUILD UPGRADE/UNRECOGNIZED are
        /// always single, non-interactive pills; UNKNOWN alone also gets
        /// the interactive IGNORE toggle, except on a plan root (see
        /// AppendOwnershipPills). The selected pill always matches
        /// node.Decision - the solver's committed Source, never a guess.
        /// Derivation: docs/ARCHITECTURE.md section S1.6.
        /// </summary>
        /// <param name="node">The tree node to build pills for.</param>
        /// <param name="currencyPlanTotals">The whole plan's real currency
        /// need, keyed by currency id. Null omits the currency pill.</param>
        /// <param name="ownedCurrencyAmounts">Raw wallet holding, keyed by
        /// currency id. Null (no snapshot) suppresses the HAVE/TOTAL pill
        /// rather than implying 0 owned.</param>
        /// <param name="ownedStockDrawnItemIds">Every item id the plan drew
        /// owned stock for - see AppendOwnershipPills.</param>
        public static List<PillSpec> BuildPillSpecs(
            CraftingTreeNode node,
            IReadOnlyDictionary<int, long> currencyPlanTotals = null,
            IReadOnlyDictionary<int, int> ownedCurrencyAmounts = null,
            ISet<int> ownedStockDrawnItemIds = null)
        {
            var specs = new List<PillSpec>(3);

            // Checked before every other branch: a component leaf is a
            // fact about a price, not an acquisition decision, so it never
            // gets a decision pill or the Ignore toggle, and never falls
            // into the Have/OwnedInfo path (whose OwnedQuantityUsed this
            // leaf never populates).
            //
            // Uses a subdued "OWN n" badge, not HAVE: HAVE means "your
            // stock reduced the plan cost" everywhere else, but a
            // component leaf's ownership never reduces this line's cost.
            // Shown only when holding > 0 - no "OWN 0" clutter.
            //
            // A currency-type component (blank cost cell by design) also
            // gets a "CURRENCY" badge so a glance explains why no gold
            // value is shown; the two badges are independent.
            if (node.IsCostComponent)
            {
                if (!node.SubtreeCost.HasValue)
                {
                    // A currency-type component's row-scope OWN badge is
                    // replaced by the plan-scope HAVE/TOTAL pill every
                    // ordinary currency leaf gets (see
                    // AppendCurrencyOwnershipPill); an item-type component
                    // keeps its OWN badge.
                    specs.Add(new PillSpec(CurrencyPillText, null, PillKind.Locked));
                    AppendCurrencyOwnershipPill(specs, node.ItemId, currencyPlanTotals, ownedCurrencyAmounts);
                }
                else if (node.ComponentOwnedQuantity > 0)
                {
                    specs.Add(new PillSpec(
                        $"OWN {node.ComponentOwnedQuantity}",
                        null,
                        PillKind.OwnedInfo));
                }

                return specs;
            }

            if (node.Decision == CraftingDecision.Have)
            {
                // A dedup-zeroed node gets only the "COUNTED ELSEWHERE"
                // pill, never HAVE - nothing here is actually owned.
                if (node.IsAchievementBitDeduped)
                {
                    specs.Add(new PillSpec("COUNTED ELSEWHERE", null, PillKind.AchievementBitDeduped));
                    return specs;
                }

                specs.Add(new PillSpec("HAVE", null, PillKind.Have));
                // A node collapses to Have both for genuine full ownership
                // and for a manually "Ignore"-d item - only the latter
                // gets the extra toggle pill, so it can be un-ignored.
                // Offered on a plan root too, unlike the "IGNORE" half
                // (AppendOwnershipPills): ignores are keyed by item id and
                // apply tree-wide, so in a multi-item batch ignoring an
                // ingredient occurrence also flips a sibling root of that
                // same item to ignored - this pill is the only way back.
                if (node.IsIgnored)
                {
                    specs.Add(new PillSpec(IgnoredPillText, null, PillKind.Ignore));
                }

                return specs;
            }

            if (node.Decision == CraftingDecision.Currency)
            {
                specs.Add(new PillSpec(CurrencyPillText, null, PillKind.Locked));
                // Every ordinary currency leaf gets the same plan-scope
                // HAVE/TOTAL pill the cost-component branch above adds.
                AppendCurrencyOwnershipPill(specs, node.ItemId, currencyPlanTotals, ownedCurrencyAmounts);
                return specs;
            }

            // A distinct locked pill from CURRENCY (see
            // CraftingDecision.GuildUpgrade) - no AcquisitionSource
            // represents it, so it is single and non-interactive.
            if (node.Decision == CraftingDecision.GuildUpgrade)
            {
                specs.Add(new PillSpec("GUILD UPGRADE", null, PillKind.Locked));
                return specs;
            }

            // A distinct locked pill from UNKNOWN: sharing a Decision
            // value once routed this leaf to the interactive IGNORE pill,
            // keyed on a raw non-item id that could silently zero an
            // unrelated "Item" node tree-wide. Short-circuiting here means
            // this leaf never reaches that branch.
            if (node.Decision == CraftingDecision.UnrecognizedIngredient)
            {
                specs.Add(new PillSpec("UNRECOGNIZED", null, PillKind.Locked));
                return specs;
            }

            var options = new List<(AcquisitionSource src, string text)>(3);
            if (node.CanCraft)
            {
                options.Add((AcquisitionSource.Craft, CraftPillText));
            }

            if (node.CanBuyTp)
            {
                options.Add((AcquisitionSource.BuyFromTp, TpPillText));
            }

            if (node.CanBuyVendor)
            {
                options.Add((AcquisitionSource.BuyFromVendor, VendorPillText));
            }

            if (options.Count == 0)
            {
                // Prefer the seeded wiki hint's badge (e.g. "SALVAGE",
                // "EXPLORE") when one exists - "UNKNOWN" remains the
                // fallback for no-source items with no seeded hint at all,
                // and for a badge that collides with a source pill text
                // (see those consts): ref/acquisition_hints_seed.json is
                // hand-maintained, and a badge reading VENDOR next to a
                // real single-source VENDOR row is indistinguishable while
                // meaning the opposite. The hint TEXT still reaches the
                // row tooltip either way.
                string badgeText = !string.IsNullOrEmpty(node.AcquisitionBadge) &&
                        !IsReservedSourceBadgeText(node.AcquisitionBadge)
                    ? node.AcquisitionBadge
                    : "UNKNOWN";
                specs.Add(new PillSpec(badgeText, null, PillKind.Locked));
                AppendOwnershipPills(specs, node, ownedStockDrawnItemIds);
                return specs;
            }

            if (options.Count == 1)
            {
                specs.Add(new PillSpec(options[0].text, null, PillKind.Locked));
                AppendOwnershipPills(specs, node, ownedStockDrawnItemIds);
                return specs;
            }

            AcquisitionSource current;
            switch (node.Decision)
            {
                case CraftingDecision.Craft: current = AcquisitionSource.Craft; break;
                case CraftingDecision.BuyFromTp: current = AcquisitionSource.BuyFromTp; break;
                case CraftingDecision.BuyFromVendor: current = AcquisitionSource.BuyFromVendor; break;
                default: current = options[0].src; break; // defensive; solver always matches one of the options
            }

            // The selected pill's own breakdown, compared against each
            // losing option's below; never subdued itself.
            //
            // Suppressed entirely when node.VendorComponentCostsUnreliable
            // is true - the same conservative posture
            // ValueDetailTooltipBuilder and BuildVendorCostComponentLeaves
            // take: the breakdown's pre-merge numbers can disagree with
            // the corrected TotalCost, and here VENDOR is the selected
            // pill acting as every other pill's comparison baseline.
            bool subduingSuppressed = node.VendorComponentCostsUnreliable;
            var selectedBreakdown = GetCostBreakdown(node, current);

            foreach (var opt in options)
            {
                bool selected = opt.src == current;
                PillKind kind = PillKind.Available;
                PillSubduingResult subduingResult = null;
                if (!selected && !subduingSuppressed)
                {
                    var optionBreakdown = GetCostBreakdown(node, opt.src);
                    var result = PillSubduingEvaluator.Evaluate(selectedBreakdown, optionBreakdown);
                    if (result.Rule != PillSubduingRule.None)
                    {
                        kind = PillKind.Subdued;
                        subduingResult = result;
                    }
                }

                specs.Add(new PillSpec(
                    opt.text,
                    // The selected pill is already the active choice, so
                    // it is non-interactive; a Subdued pill stays
                    // clickable - only its styling/tooltip changed.
                    selected ? (AcquisitionSource?)null : opt.src,
                    selected ? PillKind.Selected : kind,
                    subduingResult));
            }

            AppendOwnershipPills(specs, node, ownedStockDrawnItemIds);
            return specs;
        }

        /// <summary>
        /// Whether this pill is a click target on a live (non-dimmed) row:
        /// a source pill the user can switch to, or the Ignore toggle.
        /// Every other pill is an annotation and has never done anything on
        /// click.
        /// <para>
        /// The view needs this twice and the two readings must agree: to
        /// decide whether to wire a handler, and - on a dimmed reference
        /// branch, where no handler is wired at all - to decide whether the
        /// pill needs a tooltip explaining why the click it advertises does
        /// nothing. Splitting that predicate across the two sites is how
        /// the dimmed set ended up drawing full clickable-looking pills
        /// with no tooltip at all.
        /// </para>
        /// </summary>
        public static bool IsInteractive(PillSpec spec)
        {
            return spec.Source.HasValue || spec.Kind == PillKind.Ignore;
        }

        /// <summary>
        /// The node's raw cost breakdown for one source. The defensive
        /// default arm returns an unavailable breakdown rather than
        /// throwing, so a future regression degrades to "never subdued"
        /// instead of crashing pill rendering.
        /// </summary>
        private static PillSourceCostBreakdown GetCostBreakdown(CraftingTreeNode node, AcquisitionSource source)
        {
            switch (source)
            {
                case AcquisitionSource.Craft: return node.CraftCostBreakdown;
                case AcquisitionSource.BuyFromTp: return node.BuyFromTpCostBreakdown;
                case AcquisitionSource.BuyFromVendor: return node.BuyFromVendorCostBreakdown;
                default: return null;
            }
        }

        /// <summary>
        /// Appends the plan-scope "HAVE {have}/{planTotal} TOTAL" pill
        /// shared by every currency leaf. Both numbers are plan-scope
        /// facts, deliberately not this row's own need, so the identical
        /// pill text is truthful at every occurrence of the same currency
        /// id. Omitted when <paramref name="ownedCurrencyAmounts"/> is
        /// null or lacks the id - "have" is genuinely unknown, not zero.
        /// Full coverage collapses to the plain "HAVE" pill (counts move
        /// into the tooltip); partial coverage keeps the counts, with a
        /// "TOTAL" suffix distinguishing this plan-scope fact from the
        /// item pills' row-scope "NEEDED".
        /// </summary>
        private static void AppendCurrencyOwnershipPill(
            List<PillSpec> specs,
            int currencyId,
            IReadOnlyDictionary<int, long> currencyPlanTotals,
            IReadOnlyDictionary<int, int> ownedCurrencyAmounts)
        {
            if (ownedCurrencyAmounts == null || !ownedCurrencyAmounts.TryGetValue(currencyId, out int have))
            {
                return;
            }

            // A real, positive plan total is required first: the two
            // dictionaries have different key sets by construction (owned
            // amounts are widened to every vendor offer), so a currency
            // reachable only through a reference branch can have an owned
            // entry with no plan total - a zero default would render a
            // "fully covered" HAVE pill for a currency the plan never
            // asked for.
            if (currencyPlanTotals == null ||
                !currencyPlanTotals.TryGetValue(currencyId, out long planTotal) ||
                planTotal <= 0)
            {
                return;
            }

            if (have >= planTotal)
            {
                specs.Add(new PillSpec("HAVE", null, PillKind.Have));
            }
            else
            {
                specs.Add(new PillSpec($"HAVE {have}/{planTotal} TOTAL", null, PillKind.OwnedInfo));
            }
        }

        /// <summary>
        /// Appends the annotation pills shared by every non-Have/
        /// non-Currency return path: the non-interactive
        /// "HAVE {used}/{total} NEEDED" annotation and the interactive
        /// "IGNORE" toggle. A node reaching this method is never already
        /// ignored, so it starts as "IGNORE".
        /// <para>
        /// The badge goes on every node of an item the plan drew owned
        /// stock for, not only the nodes that received some. One item can
        /// sit at several places in one tree and the reducer gives the
        /// stock to whichever it reaches first, so later nodes read
        /// "HAVE 0/{total} NEEDED". Total is OwnedQuantityUsed +
        /// Quantity, the pre-reduction demand.</para>
        /// <para>
        /// The toggle is not offered on a plan root (see
        /// CraftingTreeNode.IsPlanRoot): ignoring the item you asked to
        /// plan zeroes the whole plan. The un-ignore half is NOT
        /// suppressed - see the Have branch in BuildPillSpecs.
        /// </para>
        /// </summary>
        private static void AppendOwnershipPills(
            List<PillSpec> specs,
            CraftingTreeNode node,
            ISet<int> ownedStockDrawnItemIds)
        {
            bool drawnElsewhere = ownedStockDrawnItemIds != null &&
                ownedStockDrawnItemIds.Contains(node.ItemId);
            if (node.OwnedQuantityUsed > 0 || drawnElsewhere)
            {
                int totalDemand = node.OwnedQuantityUsed + node.Quantity;
                specs.Add(new PillSpec(
                    $"HAVE {node.OwnedQuantityUsed}/{totalDemand} NEEDED",
                    null,
                    PillKind.OwnedInfo));
            }

            if (!node.IsPlanRoot)
            {
                specs.Add(new PillSpec(IgnorePillText, null, PillKind.Ignore));
            }
        }
    }
}
