using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    internal class PlanViewModelBuilder
    {
        public PlanViewModel Build(CraftingPlanResult result)
        {
            // RequestedItems is populated only for a genuine multi-item
            // batch (2+); a single-item request always has it null and
            // takes the single-item branch.
            bool isMultiItem = result.RequestedItems != null && result.RequestedItems.Count > 1;

            var vm = new PlanViewModel
            {
                TargetQuantity = isMultiItem ? 0 : result.Plan.TargetQuantity,
                TreeRoot = isMultiItem ? null : result.CraftingTree,
                MultiItemRoots = isMultiItem ? result.MultiItemRoots : null,
                CurrencyMetadata = result.CurrencyMetadata,
                ItemMetadata = result.ItemMetadata,
                PriceBasis = result.PriceBasis,
                // Whole-plan currency totals/holding; result.Plan is
                // already the single combined Plan for a batch, so no
                // branching is needed.
                CurrencyPlanTotals = BuildCurrencyPlanTotals(result.Plan.CurrencyCosts),
                OwnedCurrencyAmounts = result.OwnedCurrencyAmounts,
                OwnedStockDrawnItemIds = BuildOwnedStockDrawnItemIds(result.UsedMaterials),
                VendorCapsByItemId = BuildVendorCapsByItemId(result.Plan.TimegatedItems),
            };

            if (isMultiItem)
            {
                // A batch heads on its FIRST requested item - name, icon
                // and rarity - with the rest of the batch named by an
                // uncoloured suffix and stacked as icons after it. Only
                // TargetQuantity stays suppressed: a combined "x{qty}"
                // across different items is not a number.
                var first = result.RequestedItems[0];
                vm.TargetItemName = ResolveName(first.ItemId, result.ItemMetadata);
                vm.TargetIconUrl = ResolveIconUrl(first.ItemId, result.ItemMetadata);
                vm.TargetRarity = ResolveRarity(first.ItemId, result.ItemMetadata);
                vm.TargetNameSuffix = BuildMultiItemNameSuffix(result.RequestedItems.Count);
                vm.AdditionalTargetItems = BuildAdditionalTargetItems(
                    result.RequestedItems, result.ItemMetadata);
            }
            else if (result.ItemMetadata != null &&
                result.ItemMetadata.TryGetValue(result.Plan.TargetItemId, out var targetMeta))
            {
                vm.TargetItemName = !string.IsNullOrEmpty(targetMeta.Name)
                    ? targetMeta.Name
                    : "Unknown Item";
                vm.TargetIconUrl = targetMeta.IconUrl;
                vm.TargetRarity = targetMeta.Rarity;
            }
            else
            {
                vm.TargetItemName = "Unknown Item";
                vm.TargetIconUrl = null;
                vm.TargetRarity = null;
            }

            // Section emission order mirrors gw2efficiency's calculator page
            // (header/cost-breakdown -> recipe-tree -> used-owned-materials ->
            // shopping-list -> required-disciplines -> required-recipes ->
            // crafting-steps). The recipe tree itself is not in this list
            // (it renders from vm.TreeRoot, positioned second by the view);
            // everything else below is exactly the gw2e ordering.

            // The Total Cost table's Note and the Shopping List's row for a
            // traded-up item are one number. It is worked out once, here,
            // while the Note is seated, and read back below. Two
            // derivations of it could disagree; one cannot.
            var tradeUpPurchases = new Dictionary<int, TradeUpPurchase>();

            // 1. Total Cost section (always present)
            var summarySection = BuildSummarySection(result, isMultiItem, tradeUpPurchases);
            vm.Sections.Add(summarySection);
            vm.NonCoinCostTotals = BuildNonCoinCostTotals(summarySection);

            // 2. Used Materials section (only if non-null and non-empty)
            if (result.UsedMaterials != null && result.UsedMaterials.Count > 0)
            {
                vm.Sections.Add(BuildUsedMaterialsSection(result));
            }

            // Partition steps by source
            var shoppingSteps = result.Plan.Steps
                .Where(s => s.Source != AcquisitionSource.Craft)
                .ToList();
            var craftSteps = result.Plan.Steps
                .Where(s => s.Source == AcquisitionSource.Craft)
                .ToList();

            // 3. Shopping List section (only if non-empty)
            if (shoppingSteps.Count > 0)
            {
                var shoppingSection = BuildShoppingListSection(shoppingSteps, result, tradeUpPurchases);
                if (shoppingSection.Rows.Count > 0)
                {
                    vm.Sections.Add(shoppingSection);
                }
            }

            // 4. Required Disciplines section (only if non-empty)
            if (result.RequiredDisciplines != null && result.RequiredDisciplines.Count > 0)
            {
                vm.Sections.Add(BuildDisciplinesSection(result));
            }

            // 5. Required Recipes section (only if at least one recipe
            // remains once BuildRecipesSection filters the unlock-free
            // rows out)
            if (result.RequiredRecipes != null && result.RequiredRecipes.Count > 0)
            {
                var recipesSection = BuildRecipesSection(result);
                if (recipesSection.Rows.Count > 0)
                {
                    vm.Sections.Add(recipesSection);
                }
            }

            // 6. Crafting Steps section (only if non-empty, or there is a
            // timegated notice to show) - last, per gw2e order. Vendor-cap
            // notices are pre-filtered so a plan whose only "notice" is a
            // TP-liquid item's vendor cap gets no notices-only section.
            var vendorCapNotices = VendorCapNotices.Filter(result);
            var vendorRequirementNotices = result.VendorRequirementNotices
                ?? (IReadOnlyList<VendorRequirementNotice>)Array.Empty<VendorRequirementNotice>();
            if (craftSteps.Count > 0 || vendorCapNotices.Count > 0 ||
                vendorRequirementNotices.Count > 0)
            {
                vm.Sections.Add(BuildCraftingStepsSection(
                    craftSteps, vendorCapNotices, vendorRequirementNotices, result));
            }

            // 7. Notes section - only if it has at least one note. Last:
            // every note kind is a caveat about facts shown in an earlier
            // section.
            var notesSection = BuildNotesSection(result);
            if (notesSection.Rows.Count > 0)
            {
                vm.Sections.Add(notesSection);
            }

            return vm;
        }

        /// <summary>
        /// What follows the first item's name in a batch heading:
        /// " + 2 others". Deliberately NOT gw2e's " and N others"
        /// document-title wording - the heading is a name plus a running
        /// count of icons beside it, and " + " is what reads as that
        /// rather than as a second title. Null when there is no remainder.
        /// </summary>
        private static string BuildMultiItemNameSuffix(int requestedItemCount)
        {
            int rest = requestedItemCount - 1;
            return rest <= 0 ? null : " + " + StatusText.Count(rest, "other");
        }

        /// <summary>
        /// The batch's items after the first, in request order, resolved to
        /// the display fields the header's icon run draws and hovers.
        /// Returns null (not an empty list) for a batch with no remainder,
        /// so the header's "is there a run at all" test is one null check.
        /// </summary>
        private static List<PlanHeaderItem> BuildAdditionalTargetItems(
            IReadOnlyList<PlanRequestItem> items, IReadOnlyDictionary<int, ItemMetadata> metadata)
        {
            if (items == null || items.Count <= 1)
            {
                return null;
            }

            var additional = new List<PlanHeaderItem>(items.Count - 1);
            for (int i = 1; i < items.Count; i++)
            {
                int itemId = items[i].ItemId;
                additional.Add(new PlanHeaderItem
                {
                    ItemId = itemId,
                    Name = ResolveName(itemId, metadata),
                    IconUrl = ResolveIconUrl(itemId, metadata),
                    Rarity = ResolveRarity(itemId, metadata),
                });
            }

            return additional;
        }

        // Tooltip bodies shared between the collapsed and uncollapsed
        // cost-band branches and reused verbatim by the tests, so the
        // exact wording lives in one place.
        internal const string TotalMaterialsValueTooltip =
            "Full market value of everything this craft consumes - coins you spend plus the sell value of your own materials used.";

        internal const string YourMaterialsUsedTooltip =
            "Instant-sell value (after 15% TP fees) of materials you already own that this plan consumes - what you give up by using them instead of selling them.";

        internal const string UnvaluedMaterialsTooltip =
            "This plan was not asked to value the materials you already own, so this term is 0. Turn on \"Value Own Materials\" to price them.";

        internal const string ActualCostTooltip =
            "What you still pay out of pocket - materials you already own are subtracted before pricing.";

        internal const string SellValueTooltip =
            "Instant-sell revenue after 15% TP fees.";

        internal const string ProfitTooltip =
            "Sell Value minus Total Materials Value.";

        internal const string FootnoteText =
            "Prices are Trading Post data - actual purchase and sale prices are likely to vary.";

        // The floor disclosure. A plan can carry a real cost that reaches
        // the coin total as a zero - a node nothing could price or craft,
        // or a barter item whose units are the price - and the band still
        // renders every tile (a band that loses its cells reads as a
        // broken section), so the fact that one of those
        // figures was never measured has to be stated instead of implied
        // by a missing tile. UnpricedTileMarker ties the marked tiles to
        // the footnote that explains them. The wording says "nothing that
        // could cost them" rather than naming a missing recipe: a barter
        // line is exactly a line neither the Trading Post nor a solve of
        // its own could price, whether or not it has a recipe.
        internal const string UnpricedTileMarker = "*";
        internal const string UnpricedFootnoteText =
            "* Some items in this plan have no Trading Post price and nothing that could cost them, so they count as 0 here. These totals are a floor, not a measured cost.";

        internal const string UnpricedTooltipSuffix =
            "\nSome items in this plan could not be priced and count as 0, so this figure is a floor rather than a measured total.";

        // The profit band is the one part of this section that still
        // disappears on an unpriced zero (see BuildProfitFormulaBand for
        // why its tiles cannot honestly render at 0). Missing cells with
        // nothing accounting for them are the complaint the marker above
        // exists to answer, so the absence gets its own sentence.
        internal const string ProfitSuppressedFootnoteText =
            "Sell Value and Profit if Sold are hidden here - with materials at 0, the profit shown would be the whole sale price.";

        // Distinct caption for Band 2's middle tile in a multi-item batch
        // (see BuildProfitFormulaBand): two identically-captioned tiles
        // showing different numbers reads as a bug, not a scoping nuance.
        internal const string MaterialsValueSellableLabel = "Materials Value (sellable)";

        private PlanSectionViewModel BuildSummarySection(
            CraftingPlanResult result, bool isMultiItem,
            Dictionary<int, TradeUpPurchase> tradeUpPurchases)
        {
            var section = new PlanSectionViewModel
            {
                SectionType = PlanSectionType.Summary,
                Title = "Total Cost",
                IsDefaultExpanded = true,
            };

            // The plan holds a real cost the coin total counts as 0: a
            // node nothing could price or craft, or a barter item whose
            // units ARE the price. A non-coin CURRENCY does NOT qualify -
            // it is reported in full, at its own quantity, in its own
            // table, and was never counted as 0 anything. Deliberately not
            // gated on a zero total, which is where the disclosure matters
            // least. Derivation: docs/ARCHITECTURE.md section 7.5.
            //
            // Ordered so the cheap test short-circuits HasUnpricedNode's
            // whole-display-tree walk.
            bool coinTotalIsFloor =
                (result.Plan.BarterItemCosts != null && result.Plan.BarterItemCosts.Count > 0) ||
                HasUnpricedNode(result);

            // The narrower "the whole band reads 0 and one of those zeros
            // was never measured" case, which additionally suppresses the
            // profit band (see BuildProfitFormulaBand).
            bool unpricedZero = result.Plan.TotalCoinCost == 0 && coinTotalIsFloor;

            BuildCostFormulaBand(section, result, coinTotalIsFloor);
            BuildProfitFormulaBand(section, result, isMultiItem, unpricedZero);
            BuildCurrencyTableRows(section, result, tradeUpPurchases);

            // Gated on the same conditions the Sell value/Profit rows
            // themselves are, so this note never scopes a figure not
            // actually on the page - including the unpriced zero, where
            // the band is suppressed and its own footnote says so. The
            // wording is not gw2e's verbatim banner text: this module's
            // rollup has no craft-vs-buy filter, so "crafted recipes"
            // would be inaccurate.
            if (isMultiItem && result.NetSaleValue.HasValue && !unpricedZero)
            {
                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.MultiItemNote,
                    Label = "Sell value and profit are the sum across every requested item that has a live Trading Post sell price.",
                });
            }

            if (coinTotalIsFloor)
            {
                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.SummaryFootnote,
                    Label = UnpricedFootnoteText,
                });

                // Same condition BuildProfitFormulaBand returns on, so the
                // note appears exactly when a band the plan would otherwise
                // have shown is missing.
                if (unpricedZero && result.NetSaleValue.HasValue)
                {
                    section.Rows.Add(new PlanRowViewModel
                    {
                        RowType = PlanRowType.SummaryFootnote,
                        Label = ProfitSuppressedFootnoteText,
                    });
                }
            }

            // A single subdued footnote, always present, at the very
            // bottom of the section.
            section.Rows.Add(new PlanRowViewModel
            {
                RowType = PlanRowType.SummaryFootnote,
                Label = FootnoteText,
            });

            return section;
        }

        /// <summary>
        /// Formula band 1 ("Total Materials Value - Your Materials Used
        /// = Actual Cost to Craft"). All three tiles ALWAYS render: a term
        /// worth zero shows a zero rather than removing itself, because a
        /// lone tile with the
        /// formula gone around it reads as a broken section. Actual Cost
        /// to Craft is result.Plan.TotalCoinCost; the price-basis
        /// qualifier lives in this tile's tooltip.
        /// <para>
        /// coinTotalIsFloor (the plan holds a real cost the coin total
        /// counts as 0 - see BuildSummarySection) does NOT suppress any
        /// tile: it marks every tile it renders and adds the section's
        /// floor footnote, so a total that understates the plan still
        /// reads differently from a measured one. It is deliberately NOT
        /// gated on a zero total, which is where the disclosure matters
        /// least.
        /// </para>
        /// </summary>
        private static void BuildCostFormulaBand(
            PlanSectionViewModel section, CraftingPlanResult result, bool coinTotalIsFloor)
        {
            long actualCost = result.Plan.TotalCoinCost;
            string mark = coinTotalIsFloor ? UnpricedTileMarker : "";
            string unpricedSuffix = coinTotalIsFloor ? UnpricedTooltipSuffix : "";

            // The per-item TP price-side fallback means not every item in
            // this total priced on the preferred side; the suffix says so
            // instead of overclaiming.
            string actualCostTooltip = result.PriceBasis == PriceBasis.BuyOrder
                ? ActualCostTooltip + " (buy-order prices, or instant-buy where an item has none)"
                : ActualCostTooltip;
            var actualCostTile = new PlanRowViewModel
            {
                RowType = PlanRowType.CostFormulaTile,
                Label = "Actual Cost to Craft" + mark,
                CoinValue = actualCost,
                TooltipText = actualCostTooltip + unpricedSuffix,
            };

            long materialsUsed = result.MaterialOpportunityCost.HasValue && result.MaterialOpportunityCost.Value > 0
                ? result.MaterialOpportunityCost.Value
                : 0L;

            // MaterialOpportunityCost is null by contract outside
            // OwnMaterialsMode.Valued (see SellSideEconomics), so a
            // Free-mode plan that consumed owned materials has a middle
            // term nobody computed - printing it as 0 would assert a
            // valuation the pipeline deliberately declined to make. Only a
            // KNOWN zero (Valued mode computed 0, or nothing was consumed
            // at all) qualifies.
            bool materialsUsedIsKnownZero =
                result.MaterialOpportunityCost.HasValue ||
                result.UsedMaterials == null ||
                result.UsedMaterials.Count == 0;

            // A middle term nobody computed still shows 0 - the tile
            // stays, and its tooltip says the plan was not asked to value
            // owned materials rather than letting a bare 0 assert a
            // valuation. (Free mode leaves MaterialOpportunityCost null by
            // contract; see SellSideEconomics.)
            string materialsUsedTooltip = materialsUsedIsKnownZero
                ? YourMaterialsUsedTooltip
                : UnvaluedMaterialsTooltip;

            section.Rows.Add(new PlanRowViewModel
            {
                RowType = PlanRowType.CostFormulaTile,
                Label = "Total Materials Value" + mark,
                CoinValue = actualCost + materialsUsed,
                TooltipText = TotalMaterialsValueTooltip + unpricedSuffix,
            });
            section.Rows.Add(new PlanRowViewModel
            {
                RowType = PlanRowType.CostFormulaTile,
                Label = "Your Materials Used" + mark,
                CoinValue = materialsUsed,
                TooltipText = materialsUsedTooltip + unpricedSuffix,
            });

            // The formula's rightmost ("= Actual Cost to Craft") term,
            // added last.
            section.Rows.Add(actualCostTile);
        }

        /// <summary>
        /// True when the display tree holds an item the pipeline could
        /// neither craft nor price, so a zero total is an unmeasured zero
        /// rather than a free plan. CraftingDecision.Unknown is exactly
        /// that state - an ignored node collapses to Have + IsIgnored
        /// instead (see CraftingTreeBuilder.BuildNode), so ignoring every
        /// child still reads as a genuine zero.
        /// <para>
        /// Walked once per section build, and now on every plan rather
        /// than only a zero-total one - that is the point of the widened
        /// gate (see BuildSummarySection). One O(nodes) walk per BUILD, on
        /// a tree the builder already walks several times; nothing here
        /// runs per frame. It is short-circuited behind the cheap
        /// barter-cost test, so a plan already known to be a floor never
        /// pays for it.
        /// </para>
        /// </summary>
        private static bool HasUnpricedNode(CraftingPlanResult result)
        {
            if (HasUnpricedNode(result.CraftingTree, insideReferenceBranch: false))
            {
                return true;
            }

            if (result.MultiItemRoots != null)
            {
                foreach (var root in result.MultiItemRoots)
                {
                    if (HasUnpricedNode(root, insideReferenceBranch: false))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // insideReferenceBranch: a reference branch is the dimmed "what it
        // would cost to craft instead" comparison, not part of the plan -
        // an unpriced ingredient down there costs the user nothing.
        private static bool HasUnpricedNode(CraftingTreeNode node, bool insideReferenceBranch)
        {
            if (node == null)
            {
                return false;
            }

            if (!insideReferenceBranch && node.Decision == CraftingDecision.Unknown)
            {
                return true;
            }

            if (node.Children == null)
            {
                return false;
            }

            bool childInsideReferenceBranch = insideReferenceBranch || node.IsReferenceBranch;
            foreach (var child in node.Children)
            {
                if (HasUnpricedNode(child, childInsideReferenceBranch))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Formula band 2 ("Sell Value (after fees) - Total Materials
        /// Value = Profit if Sold"), only when NetSaleValue.HasValue - the
        /// profit formula is meaningless with no sell price.
        ///
        /// The middle tile is derived as NetSaleValue - CraftingProfit
        /// rather than Band 1's Plan.TotalCoinCost + MaterialOpportunityCost:
        /// the single-item identity (CraftingProfit == NetSaleValue -
        /// TotalCoinCost - MaterialOpportunityCost) does not hold for a
        /// multi-item batch, whose CraftingProfit subtracts only the
        /// sellable roots' cost. For single-item plans the two derivations
        /// are algebraically identical; for a batch with unsellable roots
        /// the bands legitimately differ, so the tile's label changes to
        /// MaterialsValueSellableLabel there and the tooltip flags it.
        /// </summary>
        private static void BuildProfitFormulaBand(
            PlanSectionViewModel section, CraftingPlanResult result, bool isMultiItem, bool unpricedZero)
        {
            if (!result.NetSaleValue.HasValue)
            {
                return;
            }

            // The cost band above now renders its zeros with a marker
            // rather than dropping tiles, but this band still suppresses:
            // its tiles would not be zeros. An unmeasured 0 materials
            // value here produces "Profit if Sold" equal to the ENTIRE
            // sale price - a large, confident, invented number, which no
            // footnote makes safe. The absence is stated instead, by
            // ProfitSuppressedFootnoteText. Plans with a real nonzero cost
            // keep the band even when some node is unpriced (the
            // pre-existing partial-pricing behavior, out of this round's
            // scope).
            if (unpricedZero)
            {
                return;
            }

            long netSaleValue = result.NetSaleValue.Value;
            long profit = result.CraftingProfit ?? 0L;
            long totalMaterialsValue = netSaleValue - profit;

            string sellQualifier;
            if (isMultiItem)
            {
                sellQualifier = " (batch total across every requested item with a live sell price)";
            }
            else
            {
                sellQualifier = result.SellableQuantity > result.Plan.TargetQuantity
                    ? $" ({result.SellableQuantity}x, overproduction)"
                    : "";
            }

            bool hasCurrencyCosts = result.Plan.CurrencyCosts != null && result.Plan.CurrencyCosts.Count > 0;
            string profitQualifier;
            if (isMultiItem)
            {
                profitQualifier = hasCurrencyCosts ? " (batch total, coin costs only)" : " (batch total)";
            }
            else
            {
                profitQualifier = hasCurrencyCosts ? " (coin costs only)" : "";
            }

            // Only a multi-item batch can make this band's figure diverge
            // from Band 1's, so only that case gets the extra clause.
            string totalMaterialsValueTooltip = isMultiItem
                ? TotalMaterialsValueTooltip + " (this band only covers items with a live sell price)"
                : TotalMaterialsValueTooltip;

            string totalMaterialsValueLabel = isMultiItem
                ? MaterialsValueSellableLabel
                : "Total Materials Value";

            section.Rows.Add(new PlanRowViewModel
            {
                RowType = PlanRowType.ProfitFormulaTile,
                Label = "Sell Value",
                CoinValue = netSaleValue,
                TooltipText = SellValueTooltip + sellQualifier,
            });
            section.Rows.Add(new PlanRowViewModel
            {
                RowType = PlanRowType.ProfitFormulaTile,
                Label = totalMaterialsValueLabel,
                CoinValue = totalMaterialsValue,
                TooltipText = totalMaterialsValueTooltip,
            });

            // FormulaResultIsExact is false exactly when profit < 0 - the
            // only row in either band where it is ever set false.
            section.Rows.Add(new PlanRowViewModel
            {
                RowType = PlanRowType.ProfitFormulaTile,
                Label = profit >= 0 ? "Profit if Sold" : "Loss if Sold",
                CoinValue = Math.Abs(profit),
                TooltipText = ProfitTooltip + profitQualifier,
                FormulaResultIsExact = profit >= 0,
            });
        }

        /// <summary>
        /// The Total Cost section's non-coin table: one row per wallet
        /// currency AND one per barter item the plan spends. Label is the
        /// resolved name, CurrencyOwnedQuantity the raw unclamped holding -
        /// a wallet balance for a currency, an account-wide item count for
        /// a barter item; Needed/FullyCovered are derived here so the table
        /// renderer stays a dumb read of computed fields.
        /// <para>
        /// Both kinds share <see cref="PlanRowType.CurrencyCost"/> and this
        /// one table because they are one statement to the reader - what
        /// this plan costs that is not coin - and because the coin total
        /// excludes both for the same reason. IsBarterItemCost keeps them
        /// apart where it matters: an item id is not a currency id, only a
        /// currency has a wallet holding, and it is what
        /// SummarySectionLayoutMath.GroupNonCoinRows groups the table by.
        /// Emitting the rows already grouped keeps BuildNonCoinCostTotals,
        /// which projects in row order, in the order the table shows.
        /// </para>
        /// </summary>
        private static void BuildCurrencyTableRows(
            PlanSectionViewModel section, CraftingPlanResult result,
            Dictionary<int, TradeUpPurchase> tradeUpPurchases)
        {
            var currencyRows = new List<PlanRowViewModel>();
            AddCurrencyCostRows(currencyRows, result);
            AddBarterItemCostRows(currencyRows, result, tradeUpPurchases);
            if (currencyRows.Count == 0)
            {
                return;
            }

            // OrderBy (stable), not List.Sort (unstable) - two different
            // unknown currency ids both fall back to the same generic
            // "Currency" display name (CurrencyDisplayResolver), and an
            // unstable sort could reorder that tied pair nondeterministically
            // run to run. Grouping preserves relative order, so sorting once
            // across both kinds leaves each group alphabetical.
            var ordered = currencyRows.OrderBy(r => r.Label, StringComparer.Ordinal).ToList();
            foreach (var group in SummarySectionLayoutMath.GroupNonCoinRows(ordered))
            {
                section.Rows.AddRange(group.Rows);
            }
        }

        private static void AddCurrencyCostRows(
            List<PlanRowViewModel> currencyRows, CraftingPlanResult result)
        {
            if (result.Plan.CurrencyCosts == null || result.Plan.CurrencyCosts.Count == 0)
            {
                return;
            }

            foreach (var cc in result.Plan.CurrencyCosts)
            {
                string currencyName = CurrencyDisplayResolver.ResolveName(cc.CurrencyId, result.CurrencyMetadata);
                string iconUrl = CurrencyDisplayResolver.ResolveIconUrl(cc.CurrencyId, result.CurrencyMetadata);
                // A plain `(int)cc.Amount` cast wraps negative past
                // int.MaxValue, making `owned >= required` true for almost
                // any owned amount; ClampToInt keeps the ordering correct.
                int required = ClampToInt(cc.Amount);

                var row = new PlanRowViewModel
                {
                    RowType = PlanRowType.CurrencyCost,
                    Label = currencyName,
                    Quantity = required,
                    IconUrl = iconUrl,
                    CurrencyId = cc.CurrencyId,
                    NonCoinCostKey = SummarySectionLayoutMath.WalletCurrencyCostKey(cc.CurrencyId),
                };
                ApplyOwnedSplit(row, LookupOwned(result.OwnedCurrencyAmounts, cc.CurrencyId));
                currencyRows.Add(row);
            }
        }

        /// <summary>
        /// The barter half of the non-coin table. Its holding comes from
        /// CraftingPlanResult.OwnedVendorItemAmounts - the same
        /// account-wide item count the Recipe Tree's cost-component leaf
        /// draws its own OWN badge from, so the table and the tree can
        /// never state different holdings for one token. Null (not 0) when
        /// there is no account snapshot, which is the only case where
        /// Have/Needed are genuinely unknown.
        /// </summary>
        private static void AddBarterItemCostRows(
            List<PlanRowViewModel> currencyRows, CraftingPlanResult result,
            Dictionary<int, TradeUpPurchase> tradeUpPurchases)
        {
            if (result.Plan.BarterItemCosts == null || result.Plan.BarterItemCosts.Count == 0)
            {
                return;
            }

            foreach (var bc in result.Plan.BarterItemCosts)
            {
                var row = new PlanRowViewModel
                {
                    RowType = PlanRowType.CurrencyCost,
                    IsBarterItemCost = true,
                    ItemId = bc.ItemId,
                    NonCoinCostKey = SummarySectionLayoutMath.BarterItemCostKey(bc.ItemId),
                    Label = ResolveName(bc.ItemId, result.ItemMetadata),
                    Quantity = ClampToInt(bc.Amount),
                    IconUrl = ResolveIconUrl(bc.ItemId, result.ItemMetadata),
                    Rarity = ResolveRarity(bc.ItemId, result.ItemMetadata),
                };
                int? held = LookupOwned(result.OwnedVendorItemAmounts, bc.ItemId);
                ApplyOwnedSplit(row, held, ApplyTradeUpNote(row, bc, held, result, tradeUpPurchases));
                currencyRows.Add(row);
            }
        }

        /// <summary>
        /// The raw holding of one id, or null when the map has no answer.
        /// Null is load-bearing: it is what separates "no snapshot" from
        /// "holds none", and every field derived below preserves it.
        /// </summary>
        private static int? LookupOwned(IReadOnlyDictionary<int, int> ownedAmounts, int id)
        {
            if (ownedAmounts != null && ownedAmounts.TryGetValue(id, out int owned))
            {
                return owned;
            }

            return null;
        }

        /// <summary>
        /// Seats the Have/Needed/Status trio on a non-coin row from its
        /// raw, unclamped holding. One derivation for both halves of the
        /// table, so a wallet currency and a barter item can never answer
        /// the same coverage question differently. Reads row.Quantity, so
        /// the caller sets it first.
        /// </summary>
        private static void ApplyOwnedSplit(PlanRowViewModel row, int? owned, int convertible = 0)
        {
            row.CurrencyOwnedQuantity = owned;
            row.CurrencyNeededQuantity = owned.HasValue
                ? Math.Max(0, row.Quantity - owned.Value - convertible)
                : (int?)null;
            row.CurrencyFullyCovered = owned.HasValue && owned.Value + convertible >= row.Quantity;
        }

        /// <summary>
        /// Seats the trade-up note on a coalesced item row and answers how
        /// many of the row's item the held sub-currency buys - the amount
        /// ApplyOwnedSplit then takes off Needed, because a player holding
        /// the currency converts it up before acquiring the rest any other
        /// way.
        /// <para>
        /// The subtraction does not depend on currency metadata. It used to
        /// be skipped whenever the currency resolved no icon, so the same
        /// plan showed a different Needed before and after /v2/currencies
        /// answered. The note is still drawn: the renderer seats an icon
        /// frame whether or not art resolved, and that frame carries the
        /// currency's name on hover.
        /// </para>
        /// </summary>
        private static int ApplyTradeUpNote(
            PlanRowViewModel row, BarterItemCost cost, int? ownedItems, CraftingPlanResult result,
            Dictionary<int, TradeUpPurchase> tradeUpPurchases)
        {
            if (!cost.TradeUpCurrencyId.HasValue || !cost.TradeUpCurrencyPerUnit.HasValue)
            {
                return 0;
            }

            int outstanding = Math.Max(0, row.Quantity - (ownedItems ?? 0));
            int buys = SeatTradeUpNote(row, cost, outstanding, result);
            tradeUpPurchases[cost.ItemId] = new TradeUpPurchase
            {
                Outstanding = outstanding,
                Buys = buys,
                CurrencyId = cost.TradeUpCurrencyId.Value,
                CurrencyPerUnit = cost.TradeUpCurrencyPerUnit.Value,
            };
            return buys;
        }

        /// <summary>
        /// The note itself, for a row already known to be a trade-up. 0
        /// whenever no note is seated, so the number the note states and
        /// the number taken off Needed are always the same number.
        /// <para>
        /// A holding of 0 is a fact the wallet reports, so it seats a note
        /// reading 0 rather than the blank a missing snapshot leaves.
        /// </para>
        /// </summary>
        private static int SeatTradeUpNote(
            PlanRowViewModel row, BarterItemCost cost, int outstanding, CraftingPlanResult result)
        {
            int currencyId = cost.TradeUpCurrencyId.Value;
            int? heldCurrency = LookupOwned(result.OwnedCurrencyAmounts, currencyId);
            if (!heldCurrency.HasValue || heldCurrency.Value < 0)
            {
                return 0;
            }

            int buys = CurrencyTradeUpCoalescing.BuysNow(
                heldCurrency.Value, cost.TradeUpCurrencyPerUnit.Value, outstanding);

            row.TradeUpCurrencyId = currencyId;
            row.TradeUpCurrencyName = CurrencyDisplayResolver.ResolveName(currencyId, result.CurrencyMetadata);
            row.TradeUpCurrencyHeld = heldCurrency.Value;
            row.TradeUpBuysQuantity = buys;
            return buys;
        }

        /// <summary>
        /// One traded-up item's requirement, what the held map currency
        /// buys of it right now, and the vendor rate behind both. The
        /// Shopping List reads Outstanding rather than the plan step's own
        /// quantity, which is only the part of the requirement the solver
        /// routed through the vendor.
        /// </summary>
        private struct TradeUpPurchase
        {
            /// <summary>
            /// The Total Cost row's whole requirement less the items
            /// already held: what the player still has to hand currency
            /// over for. The Shopping List states this and the Total Cost
            /// table states this, so the two cannot disagree.
            /// </summary>
            public int Outstanding;

            public int Buys;

            public int CurrencyId;

            public int CurrencyPerUnit;
        }

        /// <summary>
        /// PlanViewModel.NonCoinCostTotals, projected from the non-coin
        /// table rows BuildCurrencyTableRows just put in the Total Cost
        /// section rather than re-aggregated from the plan - one
        /// aggregation, so the plan-level figure and the table a reader
        /// checks it against cannot drift apart. Null (not empty) when the
        /// plan spends nothing but coin.
        /// <para>
        /// Grouping the table changed the ORDER these rows arrive in and
        /// nothing else. A second aggregation - one per group - is what
        /// would put that drift back.
        /// </para>
        /// </summary>
        private static IReadOnlyList<CurrencyAmountViewModel> BuildNonCoinCostTotals(
            PlanSectionViewModel summarySection)
        {
            List<CurrencyAmountViewModel> totals = null;
            foreach (var row in summarySection.Rows)
            {
                if (row.RowType != PlanRowType.CurrencyCost)
                {
                    continue;
                }

                if (totals == null)
                {
                    totals = new List<CurrencyAmountViewModel>();
                }

                totals.Add(new CurrencyAmountViewModel
                {
                    Amount = row.Quantity,
                    Name = row.Label,
                    IconUrl = row.IconUrl,

                    // Clamped/raw exactly as CurrencyAmountViewModel's own
                    // two fields define them; both stay null on any row
                    // whose holding is unknown rather than zero.
                    OwnedQuantity = row.CurrencyOwnedQuantity.HasValue
                        ? Math.Min(row.CurrencyOwnedQuantity.Value, row.Quantity)
                        : (int?)null,
                    RawOwnedQuantity = row.CurrencyOwnedQuantity,
                });
            }

            return totals;
        }

        /// <summary>
        /// Converts the plan's currency cost list into a currency-id-keyed
        /// dictionary - the Recipe Tree's per-leaf pill needs O(1) lookup.
        /// </summary>
        private static IReadOnlyDictionary<int, long> BuildCurrencyPlanTotals(List<CurrencyCost> currencyCosts)
        {
            if (currencyCosts == null || currencyCosts.Count == 0)
            {
                return null;
            }

            var totals = new Dictionary<int, long>(currencyCosts.Count);
            foreach (var cc in currencyCosts)
            {
                totals[cc.CurrencyId] = cc.Amount;
            }

            return totals;
        }

        /// <summary>
        /// Projects the item ids out of the reducer's used-materials list.
        /// The list is already aggregated per item id over the whole tree,
        /// so this is the plan-scope answer to "was any owned stock of
        /// this item drawn", which the Recipe Tree's HAVE badge needs per
        /// node in O(1). Null when nothing was drawn, matching how the
        /// other plan-scope pill lookups here report "nothing to show".
        /// </summary>
        private static ISet<int> BuildOwnedStockDrawnItemIds(List<UsedMaterial> usedMaterials)
        {
            if (usedMaterials == null || usedMaterials.Count == 0)
            {
                return null;
            }

            var itemIds = new HashSet<int>();
            foreach (var used in usedMaterials)
            {
                if (used != null && used.QuantityUsed > 0)
                {
                    itemIds.Add(used.ItemId);
                }
            }

            return itemIds.Count > 0 ? itemIds : null;
        }

        /// <summary>
        /// Re-indexes the plan's timegated-cap notices by ItemId - a pure
        /// reindex; VendorBatchSolver owns the cap computation.
        /// </summary>
        private static IReadOnlyDictionary<int, TimegatedItem> BuildVendorCapsByItemId(
            List<TimegatedItem> timegatedItems)
        {
            if (timegatedItems == null || timegatedItems.Count == 0)
            {
                return null;
            }

            var byItemId = new Dictionary<int, TimegatedItem>(timegatedItems.Count);
            foreach (var item in timegatedItems)
            {
                byItemId[item.ItemId] = item;
            }

            return byItemId;
        }

        private PlanSectionViewModel BuildUsedMaterialsSection(CraftingPlanResult result)
        {
            var section = new PlanSectionViewModel
            {
                SectionType = PlanSectionType.UsedMaterials,
                Title = $"Used Materials ({result.UsedMaterials.Count})",
                IsDefaultExpanded = true,
            };

            foreach (var um in result.UsedMaterials)
            {
                string name = ResolveName(um.ItemId, result.ItemMetadata);
                string iconUrl = ResolveIconUrl(um.ItemId, result.ItemMetadata);
                string rarity = ResolveRarity(um.ItemId, result.ItemMetadata);

                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.UsedMaterial,
                    ItemId = um.ItemId,
                    Label = name,
                    IconUrl = iconUrl,
                    Rarity = rarity,
                    Quantity = um.QuantityUsed,
                });
            }

            return section;
        }

        /// <summary>
        /// The Shopping List: what to go and buy, and what it costs. A
        /// traded-up item is listed at the whole outstanding requirement
        /// and priced at the map currency that requirement really costs,
        /// which is the same number the Total Cost table states for it. The
        /// plan step's own quantity is only the part of the requirement the
        /// solver routed through the vendor, so it is not what to buy.
        /// What the wallet can convert right now is the Total Cost table's
        /// Note, not a smaller shopping directive: a list that shrank to
        /// the affordable part omitted most of the plan's cost.
        /// </summary>
        private PlanSectionViewModel BuildShoppingListSection(
            List<PlanStep> steps, CraftingPlanResult result,
            Dictionary<int, TradeUpPurchase> tradeUpPurchases)
        {
            var section = new PlanSectionViewModel
            {
                SectionType = PlanSectionType.ShoppingList,
                IsDefaultExpanded = true,
            };

            foreach (var step in steps)
            {
                TradeUpPurchase purchase = default(TradeUpPurchase);
                bool isTradeUp = step.Source == AcquisitionSource.BuyFromVendor &&
                    tradeUpPurchases.TryGetValue(step.ItemId, out purchase);
                if (isTradeUp && purchase.Outstanding <= 0)
                {
                    continue;
                }

                string name = ResolveName(step.ItemId, result.ItemMetadata);
                string iconUrl = ResolveIconUrl(step.ItemId, result.ItemMetadata);
                string rarity = ResolveRarity(step.ItemId, result.ItemMetadata);
                PlanRowType rowType = MapShoppingRowType(step.Source);
                ResolveUnitCoin(step, out long unitCoin, out int unitCoinBundle);

                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = rowType,
                    // 0 on a currency row - see PlanRowViewModel.ItemId.
                    ItemId = rowType == PlanRowType.ShoppingCurrency ? 0 : step.ItemId,
                    Label = name,
                    IconUrl = iconUrl,
                    Rarity = rarity,
                    Quantity = isTradeUp ? purchase.Outstanding : step.Quantity,
                    CoinValue = step.TotalCost,
                    UnitCoinValue = unitCoin,
                    UnitCoinBundleQuantity = unitCoinBundle,
                    HintText = ResolveHintText(rowType, step.ItemId, result.AcquisitionHints),
                    BadgeText = ResolveBadgeText(rowType, step.ItemId, result.AcquisitionHints),
                    // Owned/needed split, cosmetic only - Total column
                    // only, never Each (a per-unit rate has no ownership
                    // concept).
                    CurrencyCosts = CurrencyDisplayResolver.ResolveAmounts(
                        isTradeUp ? TradeUpCurrencyCost(purchase) : step.VendorCurrencyCosts,
                        result.CurrencyMetadata,
                        result.OwnedCurrencyAmounts),
                    UnitCurrencyCosts = CurrencyDisplayResolver.ResolveUnitAmounts(
                        step.VendorOfferOutputCount, step.VendorOfferCurrencyCostLinesPerBatch, result.CurrencyMetadata),
                });
            }

            section.Title = $"Shopping List ({section.Rows.Count})";
            return section;
        }

        /// <summary>
        /// What the listed number of a traded-up item costs at the vendor.
        /// Re-derived from the listed quantity, not carried over from the
        /// plan step, whose currency total is for the step's own smaller
        /// quantity. The product is taken in long and clamped, because a
        /// requirement large enough to overflow an int at 250 units each is
        /// reachable from a big enough target quantity.
        /// </summary>
        private static List<CostLine> TradeUpCurrencyCost(TradeUpPurchase purchase)
        {
            return new List<CostLine>
            {
                new CostLine
                {
                    Type = "Currency",
                    Id = purchase.CurrencyId,
                    Count = ClampToInt((long)purchase.Outstanding * purchase.CurrencyPerUnit),
                },
            };
        }

        /// <summary>
        /// What the shopping list's coin "Each" cell says: a per-unit price,
        /// or a price and the number of units that price buys.
        /// <para>
        /// A remainder means no whole coin value is the per-unit price, so
        /// the pair is reported rather than a truncated division. Same rule
        /// and same shape as CurrencyDisplayResolver.ResolveDividedAmounts,
        /// which the currency half of this same cell already uses.
        /// </para>
        /// </summary>
        private static void ResolveUnitCoin(PlanStep step, out long unitCoin, out int bundleQuantity)
        {
            long total = step.TotalCost;
            int divisor = step.Quantity;

            // A uniform vendor step's total is the offer's per-purchase coin
            // cost times whole purchases, so dividing by the purchase count
            // recovers that cost exactly. The offer's own batch is what the
            // player is charged; the step's quantity can sit inside a
            // part-used purchase, and dividing by that instead invents a
            // rate no purchase of this offer ever charges. VendorBatchSolver
            // sets VendorOfferOutputCount only when every occurrence merged
            // into this step resolved to one offer.
            if (step.VendorOfferOutputCount > 0 && step.Quantity > 0)
            {
                int purchases = (step.Quantity + step.VendorOfferOutputCount - 1)
                    / step.VendorOfferOutputCount;
                if (purchases > 0)
                {
                    total = step.TotalCost / purchases;
                    divisor = step.VendorOfferOutputCount;
                }
            }

            if (divisor <= 1 || total <= 0)
            {
                unitCoin = divisor == 0 ? 0 : total;
                bundleQuantity = 0;
                return;
            }

            if (total % divisor == 0)
            {
                unitCoin = total / divisor;
                bundleQuantity = 0;
                return;
            }

            unitCoin = total;
            bundleQuantity = divisor;
        }

        /// <summary>
        /// Acquisition-hint tooltip text for shopping rows. Only ever
        /// populated for ShoppingUnknown rows - a hint entry existing for
        /// an item that actually has a priced/vendor source must not bleed
        /// onto that row's tooltip.
        /// </summary>
        private static string ResolveHintText(
            PlanRowType rowType,
            int itemId,
            IReadOnlyDictionary<int, AcquisitionHint> acquisitionHints)
        {
            if (rowType != PlanRowType.ShoppingUnknown || acquisitionHints == null)
            {
                return null;
            }

            if (acquisitionHints.TryGetValue(itemId, out var hint) &&
                hint != null && !string.IsNullOrEmpty(hint.Hint))
            {
                return hint.Hint;
            }

            return null;
        }

        /// <summary>
        /// Badge/pill-tag text for shopping rows, same "ShoppingUnknown
        /// only" guard as ResolveHintText - a badge existing for an item
        /// that actually has a priced/vendor source must not bleed onto
        /// that row's tag.
        /// </summary>
        private static string ResolveBadgeText(
            PlanRowType rowType,
            int itemId,
            IReadOnlyDictionary<int, AcquisitionHint> acquisitionHints)
        {
            if (rowType != PlanRowType.ShoppingUnknown || acquisitionHints == null)
            {
                return null;
            }

            if (acquisitionHints.TryGetValue(itemId, out var hint) &&
                hint != null && !string.IsNullOrEmpty(hint.Badge))
            {
                return hint.Badge;
            }

            return null;
        }

        private PlanSectionViewModel BuildCraftingStepsSection(
            List<PlanStep> steps, IReadOnlyList<TimegatedItem> vendorCapNotices,
            IReadOnlyList<VendorRequirementNotice> vendorRequirementNotices,
            CraftingPlanResult result)
        {
            var section = new PlanSectionViewModel
            {
                SectionType = PlanSectionType.CraftingSteps,
                Title = $"Crafting Steps ({steps.Count})",
                IsDefaultExpanded = true,
            };

            var planDiscNames = BuildPlanDiscNames(result);

            foreach (var step in steps)
            {
                string name = ResolveName(step.ItemId, result.ItemMetadata);
                string iconUrl = ResolveIconUrl(step.ItemId, result.ItemMetadata);
                string rarity = ResolveRarity(step.ItemId, result.ItemMetadata);

                // Find discipline info from RequiredRecipes
                string sublabel = "";
                if (result.RequiredRecipes != null)
                {
                    var recipe = result.RequiredRecipes
                        .FirstOrDefault(r => r.RecipeId == step.RecipeId);
                    if (recipe != null)
                    {
                        sublabel = FormatDisciplineSublabel(
                            recipe.Disciplines, recipe.MinRating, planDiscNames);
                    }
                }

                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.CraftStep,
                    // Carried so the row's hover can look the item's stats
                    // up. Without it the id reads 0, the renderer skips the
                    // lookup, and every Crafting Steps tooltip shows a name
                    // and nothing else while every other section shows the
                    // whole item.
                    ItemId = step.ItemId,
                    Label = name,
                    Sublabel = sublabel,
                    IconUrl = iconUrl,
                    Rarity = rarity,
                    Quantity = step.Quantity,
                });
            }

            // Timegated (vendor purchase cap) notices - caps are surfaced,
            // never solved around. Appended after the real craft steps so
            // a notices-only section still renders correctly. The list
            // arrives pre-filtered (see VendorCapNotices.Filter): a cap on
            // a TP-liquid item never reaches this loop. The label names
            // the vendor limit for what it is - same wording as the
            // Ranker's vendor-cap note (RankerTabContent).
            foreach (var timegated in vendorCapNotices)
            {
                string itemName = ResolveName(timegated.ItemId, result.ItemMetadata);

                // Seasonal uses the noun "Season" (gw2e's Wizard's
                // Vault wording), keeping the "{CapLabel} limit: N"
                // shape of Daily/Weekly.
                string capLabel;
                if (timegated.CapType == TimegatedCapType.Daily)
                {
                    capLabel = "Daily";
                }
                else if (timegated.CapType == TimegatedCapType.Weekly)
                {
                    capLabel = "Weekly";
                }
                else
                {
                    capLabel = "Season";
                }

                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.TimegatedNotice,
                    Label = $"{itemName} is timegated - vendor {capLabel} limit: {timegated.CapValue} (plan needs {timegated.NeededCount})",
                });
            }

            // Vendor-requirement notices: what a vendor the plan buys from
            // wants of the account. A requirement the account MEETS reaches
            // no row at all (PlanResultBuilder drops it), so only two
            // wordings exist here, and neither says a requirement is unmet
            // when it was merely not checked.
            foreach (var requirement in vendorRequirementNotices)
            {
                string itemName = ResolveName(requirement.ItemId, result.ItemMetadata);
                string tail = requirement.Status == VendorRequirementStatus.NotMet
                    ? "which your account does not have"
                    : "which was not checked";

                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.VendorRequirementNotice,
                    Label = $"{itemName} - this vendor requires {requirement.RequirementText}, {tail}",
                });
            }

            // Daily craft-cooldown notices: informational-only pass over
            // the craft steps, keyed on the wiki-verified seed. Never
            // touches the solver or Plan.TimegatedItems - reuses only the
            // notice ROW SHAPE, not the vendor-cap model type, so a
            // recipe-level cooldown can never be confused with a vendor
            // purchase cap by anything reading Plan.TimegatedItems.
            AppendDailyCooldownNotices(section, steps, result);

            return section;
        }

        /// <summary>
        /// Appends one notice row per Craft-source step whose aggregate
        /// Quantity exceeds the seed's PerDayCap. A step at or under the
        /// cap gets no notice. Null DailyCooldownItems (seed not wired)
        /// makes this a no-op.
        /// </summary>
        private static void AppendDailyCooldownNotices(
            PlanSectionViewModel section, List<PlanStep> craftSteps, CraftingPlanResult result)
        {
            if (result.DailyCooldownItems == null || result.DailyCooldownItems.Count == 0)
            {
                return;
            }

            // The "runs in parallel" clause only makes sense with 2+
            // notices; collect first so it can be gated on the real count.
            var pending = new List<(string ItemName, int PerDayCap, int Quantity, int Days)>();

            foreach (var step in craftSteps)
            {
                if (!result.DailyCooldownItems.TryGetValue(step.ItemId, out var cooldown) ||
                    cooldown == null || cooldown.PerDayCap <= 0 || step.Quantity <= cooldown.PerDayCap)
                {
                    continue;
                }

                string itemName = ResolveName(step.ItemId, result.ItemMetadata);
                int days = (int)Math.Ceiling((double)step.Quantity / cooldown.PerDayCap);
                pending.Add((itemName, cooldown.PerDayCap, step.Quantity, days));
            }

            bool showsParallelClause = pending.Count >= 2;

            foreach (var notice in pending)
            {
                // Every notice reaching this point has Quantity >
                // PerDayCap, so days is always >= 2 - always plural.
                string label = $"{notice.ItemName} is timegated - {notice.PerDayCap} per day per account - " +
                    $"crafting {notice.Quantity} will take about {notice.Days} days";

                // The real floor across several gated items is max(days),
                // not the sum - per-account daily caps run independently.
                // The clause names only other daily-CRAFTED items, the
                // population this count actually measures, never the
                // separate Daily-cap vendor notices.
                if (showsParallelClause)
                {
                    label += " (runs in parallel with other daily-crafted items)";
                }

                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.TimegatedNotice,
                    Label = label,
                });
            }
        }

        private PlanSectionViewModel BuildDisciplinesSection(CraftingPlanResult result)
        {
            var section = new PlanSectionViewModel
            {
                SectionType = PlanSectionType.RequiredDisciplines,
                Title = $"Required Disciplines ({result.RequiredDisciplines.Count})",
                IsDefaultExpanded = true,
            };

            foreach (var disc in result.RequiredDisciplines)
            {
                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.DisciplineRow,
                    Label = disc.Discipline,
                    Sublabel = $"Level {disc.MinRating}",
                    CharacterAvailabilityText = BuildCharacterAvailabilityText(disc, result.CharacterDisciplines),
                });
            }

            return section;
        }

        /// <summary>
        /// Which characters have `disc`, and at what rating. Null
        /// characterDisciplines means the snapshot never captured this
        /// data - never to be conflated with "captured, and nobody has it".
        /// </summary>
        private static string BuildCharacterAvailabilityText(
            RequiredDiscipline disc, IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines)
        {
            var matches = MatchingCharacterDisciplines(disc.Discipline, characterDisciplines);
            if (matches == null)
            {
                return null;
            }

            if (matches.Count == 0)
            {
                return "Not trained on any character";
            }

            var parts = matches.Select(cd => cd.Rating < disc.MinRating
                ? $"{cd.CharacterName} ({cd.Rating}/{disc.MinRating})"
                : $"{cd.CharacterName} ({cd.Rating})");

            return string.Join(", ", parts);
        }

        /// <summary>
        /// Shared filter/sort for BuildCharacterAvailabilityText and
        /// BestCharacterRating, extracted so the two cannot drift. Null
        /// (not empty) when characterDisciplines is null - "no data",
        /// never "nobody has it". Highest rating first, then name.
        /// </summary>
        private static List<SnapshotCharacterDiscipline> MatchingCharacterDisciplines(
            string discipline, IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines)
        {
            if (characterDisciplines == null)
            {
                return null;
            }

            return characterDisciplines
                .Where(cd => cd != null && string.Equals(cd.Discipline, discipline, StringComparison.Ordinal))
                .OrderByDescending(cd => cd.Rating)
                .ThenBy(cd => cd.CharacterName, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// The account's best rating for `discipline`, plus which
        /// character achieved it. Null both for "no snapshot" and "no
        /// character has it" - callers needing the distinction read
        /// characterDisciplines == null directly.
        /// </summary>
        private static (int Rating, string CharacterName)? BestCharacterRating(
            string discipline, IReadOnlyList<SnapshotCharacterDiscipline> characterDisciplines)
        {
            var matches = MatchingCharacterDisciplines(discipline, characterDisciplines);
            if (matches == null || matches.Count == 0)
            {
                return null;
            }

            return (matches[0].Rating, matches[0].CharacterName);
        }

        /// <summary>
        /// Assembles Notes rows in a fixed order - excess/reclaim, total
        /// (2+ excess lines only), competency, competency opportunity,
        /// recipe-sheet savings, seasonal vendor tips, forge-scope - so
        /// re-solves and screenshots stay diffable. Returns zero rows when
        /// every kind is empty; the caller skips the section then.
        /// </summary>
        private PlanSectionViewModel BuildNotesSection(CraftingPlanResult result)
        {
            var section = new PlanSectionViewModel
            {
                SectionType = PlanSectionType.Notes,
                IsDefaultExpanded = true,
            };

            // "(N)" counts real entries, not rollup or continuation rows.
            int noteEntryCount = 0;

            // 1. Excess/reclaim lines, alphabetical by resolved item name
            // (not the composed Label, whose "Excess: <qty>x " prefix
            // would sort quantity digits ahead of the name).
            if (result.ExcessCraftOutputs != null && result.ExcessCraftOutputs.Count > 0)
            {
                var excessRows = new List<(string Name, PlanRowViewModel Row)>(result.ExcessCraftOutputs.Count);
                long totalReclaim = 0;
                // An unpriced row (no live SellInstant) must not render
                // like a genuinely worthless one or silently understate
                // the total - flag it on the row and on the total.
                bool anyUnpriced = false;
                foreach (var excess in result.ExcessCraftOutputs)
                {
                    string name = ResolveName(excess.ItemId, result.ItemMetadata);
                    long coinValue = excess.ReclaimValue ?? 0;
                    totalReclaim += coinValue;

                    bool unpriced = !excess.IsAccountBound && !excess.ReclaimValue.HasValue;
                    if (unpriced)
                    {
                        anyUnpriced = true;
                    }

                    string suffix = excess.IsAccountBound
                        ? " (account-bound, not sellable)"
                        : unpriced
                            ? " (no sell price)"
                            : string.Empty;

                    excessRows.Add((name, new PlanRowViewModel
                    {
                        RowType = PlanRowType.NoteLine,
                        Label = $"Excess: {excess.ExcessQuantity}x {name}{suffix}",
                        CoinValue = coinValue,
                    }));
                    noteEntryCount++;
                }

                section.Rows.AddRange(excessRows
                    .OrderBy(r => r.Name, StringComparer.Ordinal)
                    .Select(r => r.Row));

                // A single excess line is already its own total - matches
                // SummarySectionRenderer's own "don't show a redundant
                // single-item rollup" instinct.
                if (result.ExcessCraftOutputs.Count > 1)
                {
                    section.Rows.Add(new PlanRowViewModel
                    {
                        RowType = PlanRowType.NoteLine,
                        Label = anyUnpriced
                            ? "Total reclaimable value (excludes unpriced items)"
                            : "Total reclaimable value",
                        CoinValue = totalReclaim,
                    });
                }
            }

            // 2. Competency lines, alphabetical by discipline. "Blocked"
            // requires a real snapshot AND a missing/insufficient best
            // rating - no snapshot must never produce a false "blocked"
            // claim.
            if (result.CharacterDisciplines != null && result.RequiredDisciplines != null)
            {
                foreach (var disc in result.RequiredDisciplines)
                {
                    var best = BestCharacterRating(disc.Discipline, result.CharacterDisciplines);
                    bool blocked = best == null || best.Value.Rating < disc.MinRating;
                    if (!blocked)
                    {
                        continue;
                    }

                    string label = best == null
                        ? $"{disc.Discipline} {disc.MinRating} required - not trained on any character"
                        : $"{disc.Discipline} {disc.MinRating} required - highest on this account: {best.Value.Rating} ({best.Value.CharacterName})";

                    section.Rows.Add(new PlanRowViewModel
                    {
                        RowType = PlanRowType.NoteLine,
                        Label = label,
                    });
                    noteEntryCount++;
                }
            }

            // 2b. Competency OPPORTUNITY lines, alphabetical by item name.
            // Distinct from block 2: that explains disciplines the plan
            // already needs; this surfaces disciplines that would make the
            // plan cost LESS. Every entry is a genuine, concrete "train
            // this and save N" opportunity (the calculator filters the
            // rest).
            if (result.CompetencyOpportunities != null && result.CompetencyOpportunities.Count > 0)
            {
                var opportunityRows = new List<(string Name, PlanRowViewModel Row)>(
                    result.CompetencyOpportunities.Count);
                foreach (var opportunity in result.CompetencyOpportunities)
                {
                    string name = ResolveName(opportunity.ItemId, result.ItemMetadata);
                    string disciplines = opportunity.Disciplines != null && opportunity.Disciplines.Count > 0
                        ? string.Join(" or ", opportunity.Disciplines)
                        : "the required discipline";

                    opportunityRows.Add((name, new PlanRowViewModel
                    {
                        RowType = PlanRowType.NoteLine,
                        Label = $"{name}: could be crafted for less - no character has " +
                            $"{disciplines} {opportunity.MinRating}",
                        CoinValue = opportunity.DeltaCost,
                    }));
                    noteEntryCount++;
                }

                section.Rows.AddRange(opportunityRows
                    .OrderBy(r => r.Name, StringComparer.Ordinal)
                    .Select(r => r.Row));
            }

            // 3. Recipe-sheet savings opportunities, alphabetical by item
            // name. Two physical rows per opportunity - NoteLine has one
            // CoinValue slot per row and this note carries two numbers.
            if (result.RecipeSheetSavingsOpportunities != null && result.RecipeSheetSavingsOpportunities.Count > 0)
            {
                var sheetRows = new List<(string Name, List<PlanRowViewModel> Rows)>(
                    result.RecipeSheetSavingsOpportunities.Count);
                foreach (var opp in result.RecipeSheetSavingsOpportunities)
                {
                    string itemName = ResolveName(opp.ItemId, result.ItemMetadata);
                    var rows = new List<PlanRowViewModel>(2);

                    string leadIn = opp.DisciplineBlocked && !string.IsNullOrEmpty(opp.Discipline)
                        ? $"Train {opp.Discipline} to {opp.RequiredRating} and buy the {itemName} recipe to craft it instead - recipe costs"
                        : $"Buy the {itemName} recipe to craft it instead of buying it - recipe costs";

                    rows.Add(new PlanRowViewModel
                    {
                        RowType = PlanRowType.NoteLine,
                        Label = leadIn,
                        CoinValue = opp.SheetCost,
                    });
                    rows.Add(new PlanRowViewModel
                    {
                        RowType = PlanRowType.NoteLine,
                        Label = "  Saves per unit crafted",
                        CoinValue = opp.SavingsPerUnit,
                    });

                    sheetRows.Add((itemName, rows));
                    noteEntryCount++;
                }

                foreach (var entry in sheetRows.OrderBy(r => r.Name, StringComparer.Ordinal))
                {
                    section.Rows.AddRange(entry.Rows);
                }
            }

            // 3b. Where to buy the sheet for a recipe the plan needs and
            // the account has not learned, alphabetical by sheet name. One
            // row each: the coin part rides CoinValue so the view draws
            // icons, and everything else is named in the label.
            if (result.MissingRecipeSheetSources != null && result.MissingRecipeSheetSources.Count > 0)
            {
                var sourceRows = new List<(string Name, PlanRowViewModel Row)>(
                    result.MissingRecipeSheetSources.Count);
                foreach (var source in result.MissingRecipeSheetSources)
                {
                    string sheetName = ResolvedNameOrNull(source.SheetItemId, result.ItemMetadata);
                    if (sheetName == null)
                    {
                        continue;
                    }

                    bool hasNonCoin = source.NonCoinCostLines != null &&
                        source.NonCoinCostLines.Count > 0;
                    if (!hasNonCoin && !source.CoinCost.HasValue)
                    {
                        // A restored plan can carry a row with neither half
                        // of a price. The note's whole content is the price.
                        continue;
                    }

                    string costText = hasNonCoin
                        ? BuildSheetBarterDescription(
                            source.NonCoinCostLines, result.ItemMetadata, result.CurrencyMetadata)
                        : null;
                    if (hasNonCoin && costText == null)
                    {
                        continue;
                    }

                    string where = source.OtherMerchantCount > 0
                        ? $"{source.MerchantName} and {StatusText.Count(source.OtherMerchantCount, "other merchant")}"
                        : source.MerchantName;

                    string label = $"Missing recipe - buy {sheetName} from {where} for";
                    if (costText != null)
                    {
                        label += " " + costText;
                        if (source.CoinCost.HasValue)
                        {
                            label += " plus";
                        }
                    }

                    sourceRows.Add((sheetName, new PlanRowViewModel
                    {
                        RowType = PlanRowType.NoteLine,
                        Label = label,
                        CoinValue = source.CoinCost ?? 0,
                    }));
                    noteEntryCount++;
                }

                section.Rows.AddRange(sourceRows
                    .OrderBy(r => r.Name, StringComparer.Ordinal)
                    .Select(r => r.Row));
            }

            // 4. Seasonal vendor tip opportunities, alphabetical by item
            // name. Two physical rows per tip: a single combined label
            // ellipsizes at the panel edge and cuts exactly the clause
            // that explains the coin number; splitting also lets "per
            // unit" sit on the row that carries the CoinValue. The cost
            // description uses only Item-type cost lines (see
            // BuildSeasonalCostDescription).
            if (result.SeasonalVendorTips != null && result.SeasonalVendorTips.Count > 0)
            {
                var tipRows = new List<(string Name, List<PlanRowViewModel> Rows)>(result.SeasonalVendorTips.Count);
                foreach (var tip in result.SeasonalVendorTips)
                {
                    string costDescription = BuildSeasonalCostDescription(tip.CostLines, result.ItemMetadata);
                    if (costDescription == null)
                    {
                        continue;
                    }

                    string itemName = ResolveName(tip.ItemId, result.ItemMetadata);
                    // The cap number is a PURCHASE limit, not an
                    // output-unit limit ("capped N/week" right after
                    // "Nx <item>" misread as N items/week, off by a factor
                    // of OutputCount); word it as a purchase count.
                    string capClause = tip.WeeklyCap.HasValue
                        ? $" (limit {tip.WeeklyCap.Value} purchase{(tip.WeeklyCap.Value == 1 ? "" : "s")}/week)"
                        : tip.DailyCap.HasValue
                            ? $" (limit {tip.DailyCap.Value} purchase{(tip.DailyCap.Value == 1 ? "" : "s")}/day)"
                            : "";

                    string festivalDisplayName = Gw2Constants.ResolveFestivalDisplayName(tip.Festival);
                    string tradeLabel = $"During {festivalDisplayName}: {tip.MerchantName} trades {costDescription} for " +
                        $"{tip.OutputCount}x {itemName}{capClause}";

                    var rows = new List<PlanRowViewModel>(2)
                    {
                        new PlanRowViewModel
                        {
                            RowType = PlanRowType.NoteLine,
                            Label = tradeLabel,
                        },
                        new PlanRowViewModel
                        {
                            RowType = PlanRowType.NoteLine,
                            Label = "  Cheaper than this plan's price per unit",
                            CoinValue = tip.PlanUnitPrice,
                        },
                    };

                    tipRows.Add((itemName, rows));
                    noteEntryCount++;
                }

                foreach (var entry in tipRows.OrderBy(r => r.Name, StringComparer.Ordinal))
                {
                    section.Rows.AddRange(entry.Rows);
                }
            }

            // 5. Gambling-forge scope note (0 or 1 logical entry). The
            // wording distinguishes fractional EV yield (already priced
            // in) from true multi-outcome gambles, which this module never
            // represents. One row: NotesSectionRenderer width-wraps a note
            // across as many fixed-height rows as it needs, so the builder
            // does not hand-split sentences to keep long text on screen.
            if (result.ProbabilisticForgeOutputItemIds != null &&
                result.ProbabilisticForgeOutputItemIds.Count > 0)
            {
                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.NoteLine,
                    Label = "This plan includes a Mystic Clover-style Mystic Forge yield - its expected " +
                        "output is already probability-adjusted. True multi-outcome Mystic Forge gambles " +
                        "(e.g. precursor forging) are a different mechanic. This plan never models or " +
                        "shows them.",
                });
                noteEntryCount++;
            }

            // Matches every other section's "Title (N)" convention, but
            // counts logical note entries, not physical rows - rollup and
            // continuation rows would inflate the count.
            section.Title = $"Notes ({noteEntryCount})";

            return section;
        }

        private PlanSectionViewModel BuildRecipesSection(CraftingPlanResult result)
        {
            var section = new PlanSectionViewModel
            {
                SectionType = PlanSectionType.RequiredRecipes,
                IsDefaultExpanded = true,
            };

            var planDiscNames = BuildPlanDiscNames(result);

            foreach (var recipe in result.RequiredRecipes)
            {
                // A recipe with no unlock has nothing to learn, so it is
                // skipped rather than shown as an always-"Learned" row. The
                // rule lives in RequiredRecipesVisibility.HasNoUnlockBarrier,
                // which the Ranker's Recipes gate calls too so the header's
                // total and that cell's denominator are one number.
                // Touches only this section's row list - a Mystic Forge
                // craft STEP keeps its location sublabel, and an auto-learned
                // recipe still gets its Crafting Steps row.
                if (RequiredRecipesVisibility.HasNoUnlockBarrier(
                        recipe.IsAutoLearned, recipe.Disciplines))
                {
                    continue;
                }

                // The row's subject is the recipe SHEET where the module
                // knows which one unlocks this recipe: a sheet is what the
                // player buys and consumes, and the crafted item is not
                // sold as a recipe. Only when the sheet's own metadata
                // arrived, so a fetch that missed it names the crafted
                // item rather than "Unknown Item".
                string craftedName = ResolveName(recipe.OutputItemId, result.ItemMetadata);
                string sheetName = recipe.SheetItemId > 0
                    ? ResolvedNameOrNull(recipe.SheetItemId, result.ItemMetadata)
                    : null;
                bool namesTheSheet = sheetName != null;
                int subjectItemId = namesTheSheet ? recipe.SheetItemId : recipe.OutputItemId;

                string name = namesTheSheet ? sheetName : craftedName;
                string iconUrl = ResolveIconUrl(subjectItemId, result.ItemMetadata);
                string rarity = ResolveRarity(subjectItemId, result.ItemMetadata);

                string statusTag;
                if (recipe.IsAutoLearned)
                {
                    statusTag = RequiredRecipesVisibility.AutoLearnedStatusTag;
                }
                else if (recipe.IsMissing == true)
                {
                    statusTag = RequiredRecipesVisibility.MissingStatusTag;
                }
                else if (recipe.IsMissing == false)
                {
                    statusTag = RequiredRecipesVisibility.LearnedStatusTag;
                }
                else
                {
                    statusTag = "";
                }

                string sublabel = FormatDisciplineSublabel(
                    recipe.Disciplines, recipe.MinRating, planDiscNames);

                // Every row gets a page, Learned rows included: the icon
                // standard is that a right-click always reaches the wiki.
                // A sheet-named row opens the sheet's own page; a recipe
                // learned from an item whose sheet the module cannot name
                // opens the same "Recipe: <crafted name>" page it always
                // did; anything else opens the crafted item's Acquisition
                // section, where the player's unlock options are listed.
                IconWikiTarget wikiTarget;
                if (namesTheSheet || recipe.IsLearnedFromItem)
                {
                    wikiTarget = IconWikiTarget.RecipeSheet(craftedName);
                }
                else
                {
                    wikiTarget = IconWikiTarget.Acquisition(craftedName);
                }

                section.Rows.Add(new PlanRowViewModel
                {
                    RowType = PlanRowType.RecipeRow,
                    ItemId = subjectItemId,
                    Label = name,
                    Sublabel = sublabel,
                    IconUrl = iconUrl,
                    Rarity = rarity,
                    StatusTag = statusTag,
                    WikiTarget = wikiTarget,
                    // Named only on a sheet row, where the sheet's name is
                    // all the reader sees and the crafted item it unlocks
                    // is the thing they were planning.
                    HintText = namesTheSheet ? $"Unlocks {craftedName}." : null,
                });
            }

            // Title reflects the count AFTER the Mystic-Forge filter, so
            // the header is honest about what is listed. The view
            // recomputes its own header title at render time from
            // Rows.Count plus live filter state; this remains the correct
            // filter-off baseline.
            section.Title = $"Required Recipes ({section.Rows.Count})";
            return section;
        }

        /// <summary>
        /// Renders an offer's cost lines as a plain "{Count}x {Name}"
        /// phrase. Returns null (never a partial description) when any
        /// non-Item line exists: a coin cost line cannot render inline as
        /// raw text without violating the coin-icon invariant, and the
        /// row's one CoinValue slot is already spent.
        /// </summary>
        private static string BuildSeasonalCostDescription(
            List<CostLine> costLines, IReadOnlyDictionary<int, ItemMetadata> metadata)
        {
            if (costLines == null || costLines.Count == 0)
            {
                return null;
            }

            var parts = new List<string>(costLines.Count);
            foreach (var line in costLines)
            {
                if (line == null || !string.Equals(line.Type, "Item", StringComparison.Ordinal))
                {
                    return null;
                }

                parts.Add($"{line.Count}x {ResolveName(line.Id, metadata)}");
            }

            return string.Join(" + ", parts);
        }

        /// <summary>
        /// The non-coin half of a recipe sheet's price as text: "5x Charm
        /// of Skill", "350 Karma", or both joined by " + ". Null for a
        /// null or empty list, and null when any item line's name is not
        /// in metadata - an "Unknown Item" here would understate what the
        /// player has to hand over, so the caller drops the note instead.
        /// </summary>
        private static string BuildSheetBarterDescription(
            IReadOnlyList<CostLine> costLines,
            IReadOnlyDictionary<int, ItemMetadata> metadata,
            IReadOnlyDictionary<int, CurrencyMetadata> currencyMetadata)
        {
            if (costLines == null || costLines.Count == 0)
            {
                return null;
            }

            var parts = new List<string>(costLines.Count);
            foreach (var line in costLines)
            {
                if (line == null)
                {
                    return null;
                }

                if (string.Equals(line.Type, "Item", StringComparison.Ordinal))
                {
                    string name = ResolvedNameOrNull(line.Id, metadata);
                    if (name == null)
                    {
                        return null;
                    }

                    parts.Add($"{line.Count}x {name}");
                }
                else if (string.Equals(line.Type, "Currency", StringComparison.Ordinal))
                {
                    parts.Add(
                        $"{line.Count} {CurrencyDisplayResolver.ResolveName(line.Id, currencyMetadata)}");
                }
                else
                {
                    return null;
                }
            }

            return string.Join(" + ", parts);
        }

        private static PlanRowType MapShoppingRowType(AcquisitionSource source)
        {
            switch (source)
            {
                case AcquisitionSource.BuyFromTp: return PlanRowType.ShoppingBuy;
                case AcquisitionSource.BuyFromVendor: return PlanRowType.ShoppingVendor;
                case AcquisitionSource.Currency: return PlanRowType.ShoppingCurrency;
                default: return PlanRowType.ShoppingUnknown;
            }
        }

        // Internal so TreeSectionController can resolve a Subdued pill's
        // item-kind delta to a display name too.
        /// <summary>The item's real name, or null when the fetch did not
        /// return one. For a caller that has a better fallback than the
        /// "Unknown Item" placeholder <see cref="ResolveName"/> hands
        /// back.</summary>
        private static string ResolvedNameOrNull(
            int itemId, IReadOnlyDictionary<int, ItemMetadata> metadata)
        {
            return metadata != null &&
                metadata.TryGetValue(itemId, out var meta) &&
                !string.IsNullOrEmpty(meta.Name)
                    ? meta.Name
                    : null;
        }

        internal static string ResolveName(
            int itemId, IReadOnlyDictionary<int, ItemMetadata> metadata)
        {
            if (metadata != null &&
                metadata.TryGetValue(itemId, out var meta) &&
                !string.IsNullOrEmpty(meta.Name))
            {
                return meta.Name;
            }

            return "Unknown Item";
        }

        private static string ResolveIconUrl(
            int itemId, IReadOnlyDictionary<int, ItemMetadata> metadata)
        {
            if (metadata != null &&
                metadata.TryGetValue(itemId, out var meta))
            {
                return meta.IconUrl;
            }

            return null;
        }

        private static string ResolveRarity(
            int itemId, IReadOnlyDictionary<int, ItemMetadata> metadata)
        {
            if (metadata != null &&
                metadata.TryGetValue(itemId, out var meta))
            {
                return meta.Rarity;
            }

            return null;
        }

        private static HashSet<string> BuildPlanDiscNames(CraftingPlanResult result)
        {
            if (result.RequiredDisciplines == null || result.RequiredDisciplines.Count == 0)
            {
                return new HashSet<string>();
            }

            return new HashSet<string>(result.RequiredDisciplines.Select(d => d.Discipline));
        }

        internal static string FormatDisciplineSublabel(
            List<string> recipeDisciplines,
            int recipeMinRating,
            ISet<string> planDiscNames)
        {
            if (recipeDisciplines == null || recipeDisciplines.Count == 0)
            {
                return "";
            }

            // The forge is a facility, not a discipline: a sole-
            // MysticForge recipe shows the facility's real name with no
            // level number (never "MysticForge 0"). The MysticForge flag
            // is split out before the planDiscNames intersection and
            // always re-prepended, so it can never be silently dropped
            // when a real leveled discipline is also present - that
            // discipline's rating stays.
            bool hasMysticForge = recipeDisciplines.Contains("MysticForge");
            List<string> otherDisciplines = hasMysticForge
                ? recipeDisciplines.Where(d => d != "MysticForge").ToList()
                : recipeDisciplines;

            List<string> relevant;
            if (otherDisciplines.Count == 0)
            {
                // Sole facility - nothing else to filter or fall back to.
                relevant = new List<string>();
            }
            else if (planDiscNames == null || planDiscNames.Count == 0)
            {
                // No filtering - show all of the recipe's real disciplines
                relevant = new List<string>(otherDisciplines);
            }
            else
            {
                relevant = otherDisciplines.Where(d => planDiscNames.Contains(d)).ToList();

                // Fallback: if no intersection, show all recipe disciplines
                if (relevant.Count == 0)
                {
                    relevant = new List<string>(otherDisciplines);
                }
            }

            relevant.Sort();

            if (hasMysticForge && relevant.Count == 0)
            {
                return "Mystic Forge";
            }

            var displayParts = new List<string>(relevant);
            if (hasMysticForge)
            {
                displayParts.Insert(0, "Mystic Forge");
            }

            string displayText = string.Join(" / ", displayParts);
            return $"{displayText} {recipeMinRating}";
        }

        /// <summary>
        /// Clamps a long to int.MaxValue rather than letting a plain
        /// `(int)` cast wrap negative, which downstream owned-vs-required
        /// comparisons would misread as fully covered.
        /// </summary>
        private static int ClampToInt(long value)
        {
            return value > int.MaxValue ? int.MaxValue : (int)value;
        }
    }
}
