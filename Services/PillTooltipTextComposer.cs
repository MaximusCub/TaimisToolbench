using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What a decision pill's tooltip says at its head, before the renderer
    /// appends the rich blocks (subduing "why it loses", value detail, the
    /// dimmed dead-click line). Pure text from the spec and the node, per
    /// CONTRIBUTING.md's STANDING RULE - the branches below were the bulk
    /// of TreeSectionController.RenderDecisionPills and none of them
    /// needed a control to decide.
    /// </summary>
    internal static class PillTooltipTextComposer
    {
        /// <param name="interactive">
        /// The pill's click is wired to a source override - a live row with
        /// a source and a re-solve callback.
        /// </param>
        /// <param name="ignoreInteractive">
        /// The pill is the live IGNORE toggle.
        /// </param>
        public static PillTooltipPlan Compose(
            PillSpec spec,
            CraftingTreeNode node,
            bool interactive,
            bool ignoreInteractive,
            IReadOnlyDictionary<int, long> currencyPlanTotals,
            IReadOnlyDictionary<int, int> ownedCurrencyAmounts)
        {
            if (interactive)
            {
                // A decisively-losing pill (Kind == Subdued) stays
                // clickable - only its tooltip gains the "why" explanation,
                // appended after this line rather than replacing it, since
                // clicking still does exactly what it says.
                return new PillTooltipPlan($"Switch to {spec.Text}", appendSubduing: true);
            }

            if (ignoreInteractive)
            {
                // Toggles this ITEM id (not just this node) in or out of the
                // ignore set, matching gw2e's own tree-wide-by-item-id
                // "Ignore" semantics.
                //
                // Leads with the STATE, because the control itself no
                // longer carries a word - it is a remove mark in a raised
                // or pressed key (Views/Rendering/TreeSectionController).
                // This is the only place the toggle is named, so the two
                // texts DecisionPillPlanner owns are spent here.
                return new PillTooltipPlan(
                    node.IsIgnored
                        ? DecisionPillPlanner.IgnoredPillText +
                            " - click to stop treating this item as fully in-hand"
                        : DecisionPillPlanner.IgnorePillText +
                            " - treat this item as fully in-hand (ignore its owned-stock requirement)",
                    appendSubduing: false);
            }

            if (spec.Kind == PillKind.Subdued)
            {
                // Reached only when the click is NOT wired - a dimmed row,
                // or no re-solve callback at all. The pill still shows why
                // this option loses; the dead-click line the renderer adds
                // comes after it, never over it.
                return new PillTooltipPlan(null, appendSubduing: true);
            }

            if (spec.Kind == PillKind.Locked)
            {
                return new PillTooltipPlan(LockedPillText(node), appendSubduing: false);
            }

            if (spec.Kind == PillKind.Selected)
            {
                // The currently-committed source pill is non-interactive
                // (clicking it would be a no-op re-solve), but still gets a
                // tooltip.
                return new PillTooltipPlan($"Current source: {spec.Text}", appendSubduing: false);
            }

            if ((spec.Kind == PillKind.Have || spec.Kind == PillKind.OwnedInfo) &&
                (node.Decision == CraftingDecision.Currency ||
                 (node.IsCostComponent && !node.SubtreeCost.HasValue)))
            {
                // The plan-scope HAVE/TOTAL pill reuses the same
                // Have/OwnedInfo kinds the item-ownership pills use, so it
                // must be intercepted BEFORE the ordinary branches below,
                // whose item-ownership wording means nothing for a currency
                // leaf. The pill text is plan-scope only; the tooltip adds
                // what the pill text cannot: this row's own need.
                return new PillTooltipPlan(
                    CurrencyCoverageText(node, currencyPlanTotals, ownedCurrencyAmounts), appendSubduing: false);
            }

            if (spec.Kind == PillKind.Have)
            {
                // An ignored node wears the same Have pill but keeps its
                // Quantity and owns none of it, so the covered-by-materials
                // wording put "Needs 0" beside a row still reading "10x".
                // CraftingTreeBuilder returns early for these without
                // touching Quantity.
                if (node.IsIgnored)
                {
                    return new PillTooltipPlan(
                        $"Ignored - the plan leaves all {node.Quantity} to you", appendSubduing: false);
                }

                // An ITEM cost-component leaf can never reach here (it gets
                // only badges, never PillKind.Have); a currency leaf CAN,
                // but is always intercepted above - so this wording only
                // needs the ordinary-item case. For a genuinely-owned Have
                // node, Quantity is 0, so OwnedQuantityUsed alone is the
                // original total demand.
                return new PillTooltipPlan(
                    $"Needs {node.OwnedQuantityUsed} - all covered by your materials", appendSubduing: false);
            }

            if (spec.Kind == PillKind.OwnedInfo &&
                spec.Text != null &&
                spec.Text.StartsWith(DecisionPillPlanner.CurrencyTradeUpPillPrefix, StringComparison.Ordinal) &&
                CurrencyTradeUpRow.TryGetAffordableNow(node, ownedCurrencyAmounts, out int affordable))
            {
                // The row is bought for one wallet currency, and the
                // module does not plan how that currency is earned (see
                // CurrencyTradeUpRow). The pill states what the holding
                // buys; the tooltip adds what is left after that.
                int remaining = node.Quantity - affordable;
                return new PillTooltipPlan(
                    $"Your held currency buys {affordable} of the {node.Quantity} this row needs - " +
                    $"{remaining} still to acquire",
                    appendSubduing: false);
            }

            if (spec.Kind == PillKind.OwnedInfo)
            {
                if (node.IsCostComponent)
                {
                    // Owning some of a cost component never reduces what
                    // must be handed over or this line's cost; stated
                    // explicitly so it is never mistaken for the "reduced
                    // the plan" vocabulary used elsewhere.
                    return new PillTooltipPlan(
                        $"You own {node.ComponentOwnedQuantity} - informational only, " +
                        "does not change the plan cost",
                        appendSubduing: false);
                }

                if (node.OwnedQuantityUsed == 0)
                {
                    // The badge only reaches zero when the plan drew this
                    // item's owned stock at some OTHER node (see
                    // DecisionPillPlanner.AppendOwnershipPills), so the
                    // tooltip can say where it went. The plain wording
                    // below would read "0 covered by your materials" and
                    // leave the reader guessing why the badge is there.
                    return new PillTooltipPlan(
                        $"Needs {node.Quantity} - your materials went to other rows of this item",
                        appendSubduing: false);
                }

                // Matches the "HAVE {used}/{total} NEEDED" pill wording;
                // remaining (node.Quantity) is total minus used.
                int totalDemand = node.OwnedQuantityUsed + node.Quantity;
                return new PillTooltipPlan(
                    $"Needs {totalDemand} total - {node.OwnedQuantityUsed} covered by your materials, " +
                    $"{node.Quantity} left to acquire",
                    appendSubduing: false);
            }

            if (spec.Kind == PillKind.AchievementBitDeduped)
            {
                // KNOWN-ISSUES #26: explains the "COUNTED ELSEWHERE"
                // semantics - nothing here is actually owned, this exact
                // occurrence is just already required elsewhere in the tree.
                return new PillTooltipPlan(
                    "Already counted elsewhere in the tree - this item is obtained once, not needed again here",
                    appendSubduing: false);
            }

            return new PillTooltipPlan(null, appendSubduing: false);
        }

        private static string LockedPillText(CraftingTreeNode node)
        {
            // A cost-component leaf's "CURRENCY" badge - its cost cell is
            // deliberately blank because the quantity itself IS the cost.
            // Never a "no source" situation like the other Locked pills, so
            // it gets its own tooltip first.
            if (node.IsCostComponent)
            {
                return "Paid in a non-coin currency - no gold value to show here";
            }

            // The UNKNOWN pill (no feasible source at all) is a different
            // situation from every other locked pill (exactly one feasible
            // source, just not a choice): "Only available source" is
            // misleading there since there IS no available source. Prefer
            // the seeded wiki hint when one exists.
            if (node.Decision == CraftingDecision.Unknown)
            {
                return !string.IsNullOrEmpty(node.AcquisitionHint)
                    ? node.AcquisitionHint
                    : "No known acquisition source";
            }

            // guildupgrade-ingredients fix: the GUILD UPGRADE pill is the
            // same "no available source" situation as UNKNOWN above, just
            // with its own always-populated AcquisitionHint (see
            // CraftingTreeBuilder's "GuildUpgrade" branch) instead of a
            // seeded wiki hint.
            if (node.Decision == CraftingDecision.GuildUpgrade)
            {
                return !string.IsNullOrEmpty(node.AcquisitionHint)
                    ? node.AcquisitionHint
                    : "Requires a claimed Guild Hall upgrade";
            }

            // Also "no available source", not "exactly one feasible source"
            // - without this branch it falls into the misleading default.
            // node.AcquisitionHint is always null here (the builder returns
            // before ApplyAcquisitionHint).
            if (node.Decision == CraftingDecision.UnrecognizedIngredient)
            {
                return "Unrecognized ingredient type - no known acquisition source";
            }

            // A currency ingredient is paid from the wallet, so no "source"
            // wording applies.
            if (node.Decision == CraftingDecision.Currency)
            {
                return "Paid from your wallet as a game currency - no purchase or crafting source applies";
            }

            return "Only available source";
        }

        private static string CurrencyCoverageText(
            CraftingTreeNode node,
            IReadOnlyDictionary<int, long> currencyPlanTotals,
            IReadOnlyDictionary<int, int> ownedCurrencyAmounts)
        {
            int have = 0;
            ownedCurrencyAmounts?.TryGetValue(node.ItemId, out have);
            long planTotal = 0;
            currencyPlanTotals?.TryGetValue(node.ItemId, out planTotal);
            long shortfall = planTotal > have ? planTotal - have : 0;
            return shortfall > 0
                ? $"Plan needs {planTotal} total, you have {have} - short {shortfall}. This row needs {node.Quantity}."
                : $"Plan needs {planTotal} total, you have {have} - fully covered. This row needs {node.Quantity}.";
        }
    }

    internal readonly struct PillTooltipPlan
    {
        /// <summary>The head line; null when this pill kind has none.</summary>
        public readonly string Text;

        /// <summary>
        /// Whether a subduing "why it loses" block belongs after the head
        /// line. Only ever true for a Subdued pill, but stated separately
        /// so the renderer never has to infer it from the block's presence.
        /// </summary>
        public readonly bool AppendSubduing;

        public PillTooltipPlan(string text, bool appendSubduing)
        {
            Text = text;
            AppendSubduing = appendSubduing;
        }
    }
}
