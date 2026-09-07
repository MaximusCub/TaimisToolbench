using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Builds a Recipe Tree row's extra tooltip lines (unit-price line(s), the
    /// TP price-side-fallback caveat, the Unknown/GuildUpgrade acquisition
    /// hint, the receipt/what-if caption, and the wiki right-click
    /// affordance line), for <c>TreeSectionController.RenderTreeNode</c>. Pure,
    /// Blish-free string shaping so this row-tooltip logic is directly
    /// unit-testable without a live <c>Panel</c>/<c>BasicTooltipText</c>,
    /// matching this repo's pattern for tree-rendering text
    /// (DecisionPillPlanner, ValueDetailTooltipBuilder, ShoppingRowTooltipFormatter).
    ///
    /// Deliberately excludes the wiki-link RIGHT-CLICK WIRING itself
    /// (<c>rowPanel.RightMouseButtonPressed</c>/<c>MouseLeft</c>/
    /// <c>RightMouseButtonReleased</c>) - that is Blish-bound event wiring and
    /// stays in <c>TreeSectionController.RenderTreeNode</c>, gated by the
    /// identical <see cref="WikiLinkBuilder.HasWikiPage"/> predicate this class
    /// also calls to decide whether to append the tooltip line. Calling that
    /// cheap pure predicate twice per row is intentional: it keeps this class
    /// free of any Blish dependency rather than threading a bool back out for
    /// one call site.
    /// </summary>
    internal static class TreeRowTooltipComposer
    {
        /// <summary>
        /// The right-click affordance line. Shared with
        /// <c>Views/Rendering/RecipesSectionRenderer</c>, which offers the
        /// same right-click on its own rows and must not word it
        /// differently.
        /// </summary>
        public const string WikiHintText = "Right-click to open the wiki page.";

        /// <summary>The same affordance on a row whose acquisition is left
        /// to the player, where the link lands on the page's Acquisition
        /// section rather than its top.</summary>
        public const string WikiAcquisitionHintText =
            "Right-click to see how to get this on the wiki.";

        /// <summary>
        /// A row's item stat block as tooltip content, or empty content
        /// when the session has no stats for it - or when the row's id is
        /// not an item id in the first place (see
        /// <see cref="RowIdIsAnItemId"/>). Never fetches; the lookup is a
        /// read of an already-populated session cache.
        /// </summary>
        public static TooltipContent BuildStatTooltipContent(
            CraftingTreeNode node,
            Func<int, ItemStatBlock> getStatBlock)
        {
            if (getStatBlock == null || !RowIdIsAnItemId(node))
            {
                return TooltipContent.Empty;
            }

            return ItemStatTooltipComposer.BuildContent(getStatBlock(node.ItemId));
        }

        /// <summary>
        /// Whether <see cref="CraftingTreeNode.ItemId"/> holds a real item
        /// id on this row. It is one numeric slot shared by three id spaces
        /// (see <see cref="CraftingDecision"/>): item ids, wallet currency
        /// ids, and guild upgrade ids. Id 24 is BOTH a real item and the
        /// currency "Pristine Fractal Relics", and the widened metadata
        /// fetch can put the item's entry in the very dictionary the stat
        /// cache is filled from - so an item-keyed stat lookup on a
        /// currency row is the same cross-domain collision
        /// <see cref="CraftingTreeBuilder"/> already guards icon and rarity
        /// against, only worse: a stat block's FIRST line is the item's
        /// name in its rarity colour, and it displaces the row's own name
        /// line.
        /// </summary>
        public static bool RowIdIsAnItemId(CraftingTreeNode node)
        {
            if (node == null || node.ItemId <= 0)
            {
                return false;
            }

            if (node.Decision == CraftingDecision.Currency ||
                node.Decision == CraftingDecision.GuildUpgrade ||
                node.Decision == CraftingDecision.UnrecognizedIngredient)
            {
                return false;
            }

            // A vendor cost-component leaf carries Decision ==
            // BuyFromVendor whether it is a barter ITEM or a CURRENCY; only
            // the item half gets a SubtreeCost (its gold value), because a
            // currency's cost cell is deliberately blank. Same
            // discriminator the currency cost-component pill tooltip uses.
            return !(node.IsCostComponent && !node.SubtreeCost.HasValue);
        }

        /// <summary>
        /// A row's extra tooltip lines as content - the second box of the
        /// row's tooltip. Returns fresh, never-null content (empty when
        /// nothing applies) so the caller can capture the SAME instance in
        /// its settle re-ellipsis closure: nothing here depends on the
        /// row's width, so a resize never has to recompose it.
        /// <para>
        /// The unit-price line keeps its gold figure as a coin span so the rich
        /// tooltip surface can draw it with real coin icons instead of spelling
        /// it "1g 23s 45c".
        /// </para>
        /// <para>
        /// Returned UNWRAPPED - including the vendor price-side caveats, which
        /// run to 83 characters. The surface measures and wraps against a real
        /// font at a real pixel width.
        /// </para>
        /// </summary>
        public static TooltipContent BuildExtraTooltipContent(
            CraftingTreeNode node,
            string captionText,
            PlanViewModel currentPlan)
        {
            var extraTooltipLines = new List<TooltipLine>();
            if (node == null)
            {
                return TooltipContent.Empty;
            }

            if (node.Quantity > 1 &&
                (node.Decision == CraftingDecision.BuyFromTp ||
                 node.Decision == CraftingDecision.BuyFromVendor))
            {
                // In-game finding B: a pure-currency vendor offer
                // (spirit shards, karma, ...) has UnitCost == 0 (not null -
                // see CraftingTreeBuilder.BuildNode), which used to render a
                // misleading "0g 0s 0c" instead of the real per-unit
                // currency cost; a mixed coin+currency offer still shows
                // both lines below. The coin line is suppressed only when
                // it is genuinely zero AND a currency cost exists to show
                // instead of it.
                bool hasCurrencyCosts = node.VendorCurrencyCosts != null && node.VendorCurrencyCosts.Count > 0;

                // A pure-barter offer reaches the same 0-coin UnitCost by
                // the other route (its cost is an untradeable item's units,
                // not a currency), so it must suppress the coin line for
                // the same reason - see CraftingTreeNode's own doc comment.
                bool hasNonCoinCosts = hasCurrencyCosts || node.VendorHasBarterItemCost;
                if (node.UnitCost.HasValue && !(node.UnitCost.Value == 0 && hasNonCoinCosts))
                {
                    extraTooltipLines.Add(TooltipContent.Line(
                        TooltipSpan.FromText("Unit price: "),
                        TooltipSpan.FromCoin(node.UnitCost.Value, CoinSegmentMath.GameStyleText(node.UnitCost.Value))));
                }

                if (node.Decision == CraftingDecision.BuyFromVendor && hasCurrencyCosts)
                {
                    var unitCurrencyAmounts = CurrencyDisplayResolver.ResolveTreeNodeUnitAmounts(
                        node.VendorCurrencyCosts, node.Quantity, currentPlan?.CurrencyMetadata);
                    if (unitCurrencyAmounts != null)
                    {
                        foreach (var amount in unitCurrencyAmounts)
                        {
                            string amountText = amount.BundleLabel ?? amount.Amount.ToString();
                            extraTooltipLines.Add(TooltipContent.TextLine($"Unit price: {amountText} {amount.Name}"));
                        }
                    }
                }
            }

            // Price-side fallback caveat: this node's TP unit price came
            // from the item's NON-preferred side because the preferred side
            // had no listings (CraftingTreeBuilder.BuildNode /
            // SolverDecision.PriceSideFellBack), so the number shown must
            // not read as an ordinary preferred-side price. Outside the
            // node.Quantity > 1 gate above on purpose: the caveat is about
            // WHICH TP side priced the node, not about a qty=1 row showing
            // its total as the unit price.
            //
            // Three producers set the flag: a BuyFromTp node, a
            // BuyFromVendor cost-component leaf whose TP-valued barter price
            // fell back (VendorItemCostLine.PriceSideFellBack), and a
            // BuyFromVendor PARENT, whose flag is an OR across its
            // VendorItemCosts - folded up so the caveat is still reachable
            // when the offending leaf renders collapsed
            // (PlanContentHeightMath.IsNodeExpanded caps default expansion
            // at depth < 2).
            //
            // The parent therefore gets a DIFFERENT sentence: its own item
            // was never bought on the TP, so the leaf wording would assert
            // something false about a row whose item may have a healthy TP
            // presence. BuyFromTp and cost-component leaves keep the
            // original sentence, where the flag does describe the row's own
            // price. With a null currentPlan neither ternary can know the
            // PriceBasis, so that case gets a basis-agnostic sentence rather
            // than an unearned claim about which side fell back.
            if (node.PriceSideFellBack &&
                (node.Decision == CraftingDecision.BuyFromTp || node.IsCostComponent))
            {
                extraTooltipLines.Add(TooltipContent.TextLine(currentPlan == null
                    ? "The other trading post price side is shown."
                    : currentPlan.PriceBasis == PriceBasis.BuyOrder
                        ? "The buy-order price is unavailable, so the instant-buy price is shown."
                        : "The instant-buy price is unavailable, so the buy-order price is shown."));
            }
            else if (node.PriceSideFellBack && node.Decision == CraftingDecision.BuyFromVendor)
            {
                extraTooltipLines.Add(TooltipContent.TextLine(currentPlan == null
                    ? "A vendor cost item shows its other trading post price side."
                    : currentPlan.PriceBasis == PriceBasis.BuyOrder
                        ? "A vendor cost item's buy-order price is unavailable, so its instant-buy price is used."
                        : "A vendor cost item's instant-buy price is unavailable, so its buy-order price is used."));
            }

            // guildupgrade-ingredients fix: a GuildUpgrade node's
            // acquisition-hint-style explanation (see CraftingTreeBuilder's
            // "GuildUpgrade" branch) shares this same tooltip line as the
            // Unknown case - both are "no priceable source, here is why"
            // text, just for a different reason.
            if ((node.Decision == CraftingDecision.Unknown || node.Decision == CraftingDecision.GuildUpgrade) &&
                !string.IsNullOrEmpty(node.AcquisitionHint))
            {
                extraTooltipLines.Add(TooltipContent.TextLine(node.AcquisitionHint));
            }

            // First, ahead of any unit-price or caveat line already here:
            // the caption says which group of children the row belongs to,
            // and the rest of the box is about the row itself.
            if (!string.IsNullOrEmpty(captionText))
            {
                extraTooltipLines.Insert(0, TooltipContent.TextLine(captionText));
            }

            // The tooltip-line
            // half of the affordance only - see this class's own doc
            // comment for why the actual right-click wiring stays in
            // TreeSectionController. WikiLinkBuilder.HasWikiPage/
            // BuildItemPageUrl additionally suppress the affordance
            // entirely for the known placeholder names (see
            // WikiLinkBuilder's SentinelNames), which never resolve to a
            // real page at all.
            if (WikiLinkBuilder.HasWikiPage(node.Name))
            {
                extraTooltipLines.Add(TooltipContent.TextLine(
                    CurrencyTradeUpRow.Matches(node)
                        ? WikiAcquisitionHintText
                        : WikiHintText));
            }

            return TooltipContent.FromLines(extraTooltipLines);
        }

        /// <summary>
        /// The page the row's right-click opens. A currency trade-up row
        /// (see <see cref="CurrencyTradeUpRow"/>) opens the item's
        /// Acquisition section, because the module deliberately plans no
        /// route to the currency and that section is where the player's
        /// options are listed. Every other row opens the plain item page.
        /// Null when the name has no wiki page, which is the same test the
        /// tooltip line above uses, so the affordance and the link can
        /// never disagree.
        /// </summary>
        public static string BuildWikiUrl(CraftingTreeNode node)
        {
            if (node == null)
            {
                return null;
            }

            return CurrencyTradeUpRow.Matches(node)
                ? WikiLinkBuilder.BuildItemAcquisitionUrl(node.Name)
                : WikiLinkBuilder.BuildItemPageUrl(node.Name);
        }
    }
}
