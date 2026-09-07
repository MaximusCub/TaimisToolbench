using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.CraftingPlanResultBuilders;

namespace TaimisToolbench.Tests.Services
{
    public class PlanViewModelBuilderSummaryTests
    {
        private readonly PlanViewModelBuilder _builder = new PlanViewModelBuilder();

        // --- Empty plan ---
        [Fact]
        public void EmptyPlan_ReturnsSummarySectionOnly()
        {
            var result = MakeResult();
            var vm = _builder.Build(result);

            Assert.Single(vm.Sections);
            Assert.Equal(PlanSectionType.Summary, vm.Sections[0].SectionType);

            var rows = vm.Sections[0].Rows;
            // A zero-cost plan does NOT collapse: the full three-tile cost
            // band renders at 0 (see BuildCostFormulaBand) + the
            // always-present footnote row - no profit band (no sell
            // price), no currency rows (no currency costs).
            Assert.Equal(4, rows.Count);
            Assert.All(rows.Take(3), r => Assert.Equal(PlanRowType.CostFormulaTile, r.RowType));
            Assert.All(rows.Take(3), r => Assert.Equal(0L, r.CoinValue));
            Assert.Equal("Actual Cost to Craft", rows[2].Label);
            Assert.Equal(PlanRowType.SummaryFootnote, rows[3].RowType);
        }

        // --- Target item resolution ---
        [Fact]
        public void TargetItem_ResolvesNameAndIcon()
        {
            var meta = MetaFor((1, "Zojja's Claymore", "claymore.png"));
            var result = MakeResult(targetItemId: 1, metadata: meta);
            var vm = _builder.Build(result);

            Assert.Equal("Zojja's Claymore", vm.TargetItemName);
            Assert.Equal("claymore.png", vm.TargetIconUrl);
        }

        [Fact]
        public void TargetItem_MissingMetadata_FallsBack()
        {
            var result = MakeResult(targetItemId: 999);
            var vm = _builder.Build(result);

            Assert.Equal("Unknown Item", vm.TargetItemName);
            Assert.Null(vm.TargetIconUrl);
            Assert.Null(vm.TargetRarity);
        }

        [Fact]
        public void TargetItem_ResolvesRarity()
        {
            var meta = new Dictionary<int, ItemMetadata>
            {
                [1] = new ItemMetadata { ItemId = 1, Name = "Zojja's Claymore", IconUrl = "c.png", Rarity = "Exotic" },
            };
            var result = MakeResult(targetItemId: 1, metadata: meta);
            var vm = _builder.Build(result);

            Assert.Equal("Exotic", vm.TargetRarity);
        }

        // --- Cost formula band (collapse rule + arithmetic) ---
        [Fact]
        public void CostBand_NoMaterialsUsed_StillRendersTheWholeFormula()
        {
            // The rule, settled twice against a live plan: a term worth
            // zero shows a zero. Owning none of what a plan
            // needs is the ordinary case, and it used to hide two thirds
            // of the section.
            var result = MakeResult(totalCoinCost: 123456);
            var vm = _builder.Build(result);

            var summary = vm.Sections.First(s => s.SectionType == PlanSectionType.Summary);
            var costTiles = summary.Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.Equal("Total Materials Value", costTiles[0].Label);
            Assert.Equal(123456L, costTiles[0].CoinValue);
            Assert.Equal("Your Materials Used", costTiles[1].Label);
            Assert.Equal(0L, costTiles[1].CoinValue);
            Assert.Equal("Actual Cost to Craft", costTiles[2].Label);
            Assert.Equal(123456L, costTiles[2].CoinValue);
            Assert.All(costTiles, tile => Assert.False(string.IsNullOrEmpty(tile.TooltipText)));
        }

        [Fact]
        public void CostBand_MaterialOpportunityCostZero_RendersZeroRatherThanVanishing()
        {
            // A material with no instant-sell price contributes 0, not
            // null. That zero is now shown, not used as a reason to drop
            // the surrounding formula.
            var result = MakeResult(totalCoinCost: 200);
            result.MaterialOpportunityCost = 0;

            var vm = _builder.Build(result);
            var costTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.Equal(0L, costTiles[1].CoinValue);
            Assert.Equal("Actual Cost to Craft", costTiles[2].Label);
            Assert.Equal(200L, costTiles[2].CoinValue);
        }

        [Fact]
        public void CostBand_MaterialsUsedPositive_ExpandsToThreeTilesWithCorrectArithmetic()
        {
            var result = MakeResult(totalCoinCost: 200);
            result.MaterialOpportunityCost = 25;

            var vm = _builder.Build(result);
            var costTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.Equal("Total Materials Value", costTiles[0].Label);
            Assert.Equal(225L, costTiles[0].CoinValue); // 200 + 25
            Assert.Equal("Your Materials Used", costTiles[1].Label);
            Assert.Equal(25L, costTiles[1].CoinValue);
            Assert.Equal("Actual Cost to Craft", costTiles[2].Label);
            Assert.Equal(200L, costTiles[2].CoinValue);

            // User-mandated tooltips: every tile header has
            // its own non-empty tooltip.
            Assert.All(costTiles, t => Assert.False(string.IsNullOrEmpty(t.TooltipText)));

            // The cost band's three non-negative
            // terms always balance exactly (225 - 25 == 200), so its
            // final-boundary operator is always the true "=" - the
            // FormulaResultIsExact escape hatch exists for the profit
            // band's loss case only.
            Assert.True(costTiles[2].FormulaResultIsExact);
        }

        [Fact]
        public void CostBand_ZeroCostAndNoMaterialsUsed_RendersFullBandAtZero()
        {
            // The collapse rule is about there being no MIDDLE term to
            // show, and it only reads as a deliberate layout when the
            // remaining tile carries a real number. A plan that costs
            // nothing (every node ignored or already in hand) collapsed to
            // a lone "0c" tile with the rest of the band gone, which reads
            // as a broken section - it renders the whole formula at 0
            // instead.
            var result = MakeResult(totalCoinCost: 0);
            result.MaterialOpportunityCost = 0;

            var vm = _builder.Build(result);
            var costTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.Equal("Total Materials Value", costTiles[0].Label);
            Assert.Equal("Your Materials Used", costTiles[1].Label);
            Assert.Equal("Actual Cost to Craft", costTiles[2].Label);
            Assert.All(costTiles, t => Assert.Equal(0L, t.CoinValue));
            Assert.All(costTiles, t => Assert.False(string.IsNullOrEmpty(t.TooltipText)));
            // 0 - 0 == 0 balances, so the band still ends in a true "=".
            Assert.True(costTiles[2].FormulaResultIsExact);
        }

        [Fact]
        public void CostBand_ZeroCostButMaterialsConsumedUnvalued_SaysSoInTheTooltip()
        {
            // OwnMaterialsMode.Free leaves MaterialOpportunityCost null BY
            // CONTRACT (see SellSideEconomics) even though owned materials
            // really were consumed - "Use Own Materials" on with "Value Own
            // Materials" off. The tile still renders its 0 (the band no
            // longer disappears), so the "nobody priced these" fact moves
            // into the tooltip instead of being implied by a missing tile.
            var result = MakeResult(
                totalCoinCost: 0,
                usedMaterials: new List<UsedMaterial>
                {
                    new UsedMaterial { ItemId = 7, QuantityUsed = 3 },
                });
            Assert.Null(result.MaterialOpportunityCost);

            var vm = _builder.Build(result);
            var costTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            var materialsUsedTile = costTiles[1];
            Assert.Equal("Your Materials Used", materialsUsedTile.Label);
            Assert.Equal(0L, materialsUsedTile.CoinValue);
            Assert.Equal(
                PlanViewModelBuilder.UnvaluedMaterialsTooltip,
                materialsUsedTile.TooltipText);

            // The measured-zero counterpart keeps the ordinary tooltip -
            // the two zeros must not read identically.
            var valued = MakeResult(
                totalCoinCost: 0,
                usedMaterials: new List<UsedMaterial>
                {
                    new UsedMaterial { ItemId = 7, QuantityUsed = 3 },
                });
            valued.MaterialOpportunityCost = 0;
            var valuedTiles = _builder.Build(valued).Sections[0].Rows
                .Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();
            Assert.Equal(
                PlanViewModelBuilder.YourMaterialsUsedTooltip,
                valuedTiles[1].TooltipText);
        }

        [Fact]
        public void CostBand_ZeroCostWithMaterialsConsumedAndValuedAtZero_RendersFullBand()
        {
            // The Valued-mode counterpart: materials were consumed AND
            // priced, and the priced total genuinely came out 0 (nothing
            // consumed had a sell price). That IS a known zero, so the
            // band renders - the distinction is measured-vs-unmeasured,
            // not consumed-vs-not.
            var result = MakeResult(
                totalCoinCost: 0,
                usedMaterials: new List<UsedMaterial>
                {
                    new UsedMaterial { ItemId = 7, QuantityUsed = 3 },
                });
            result.MaterialOpportunityCost = 0;

            var vm = _builder.Build(result);
            var costTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.All(costTiles, t => Assert.Equal(0L, t.CoinValue));
        }

        [Fact]
        public void CostBand_ZeroCostWithMaterialsUsed_StillRendersFullBand()
        {
            // Nothing to pay out of pocket because owned materials cover
            // the whole plan: the middle term is real, so this was already
            // the uncollapsed shape - pinned here so the zero-plan rule
            // above cannot be "simplified" into overwriting it.
            var result = MakeResult(totalCoinCost: 0);
            result.MaterialOpportunityCost = 75;

            var vm = _builder.Build(result);
            var costTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.Equal(75L, costTiles[0].CoinValue);
            Assert.Equal(75L, costTiles[1].CoinValue);
            Assert.Equal(0L, costTiles[2].CoinValue);
        }

        [Fact]
        public void CostBand_ZeroCostWithCurrencyOnlyPlan_StillRendersFullBand()
        {
            // A plan paid entirely in a non-coin currency has a genuine 0
            // coin cost. The currency table below the band carries the
            // real numbers; the band itself must not be the odd one out.
            var result = MakeResult(
                totalCoinCost: 0,
                currencyCosts: new List<CurrencyCost>
                {
                    new CurrencyCost { CurrencyId = 1, Amount = 500 },
                });

            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;

            Assert.Equal(3, rows.Count(r => r.RowType == PlanRowType.CostFormulaTile));
            Assert.Single(rows.Where(r => r.RowType == PlanRowType.CurrencyCost));
        }

        [Fact]
        public void CostBand_ZeroCostFromAnUnpricedItem_RendersMarkedBand()
        {
            // This plan totals 0 because nothing in it could be priced,
            // not because it is free. The band keeps its cells at 0 and
            // states the difference: marker on every tile caption, the
            // suffix in every tooltip, and the unpriced footnote row.
            var result = MakeResult(totalCoinCost: 0);
            result.MaterialOpportunityCost = 0;
            result.CraftingTree = new CraftingTreeNode
            {
                ItemId = 1,
                NodeId = 1,
                Quantity = 1,
                Decision = CraftingDecision.Craft,
                Children = new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 2,
                        NodeId = 2,
                        Quantity = 1,
                        Decision = CraftingDecision.Unknown,
                    },
                },
            };

            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;
            var costTiles = rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.All(costTiles, t => Assert.Equal(0L, t.CoinValue));
            Assert.Equal("Total Materials Value*", costTiles[0].Label);
            Assert.Equal("Your Materials Used*", costTiles[1].Label);
            Assert.Equal("Actual Cost to Craft*", costTiles[2].Label);
            Assert.All(costTiles, t => Assert.Contains(
                PlanViewModelBuilder.UnpricedTooltipSuffix, t.TooltipText));

            // The footnote is what a marked tile points AT - both rows
            // render (SummarySectionRenderer draws every footnote row it
            // is handed), unpriced line first.
            var footnotes = rows.Where(r => r.RowType == PlanRowType.SummaryFootnote).ToList();
            Assert.Equal(2, footnotes.Count);
            Assert.Equal(PlanViewModelBuilder.UnpricedFootnoteText, footnotes[0].Label);
            Assert.Equal(PlanViewModelBuilder.FootnoteText, footnotes[1].Label);
        }

        [Fact]
        public void CostBand_KnownZero_CarriesNoUnpricedMarkerOrFootnote()
        {
            // The discriminator the marker exists for: a measured zero
            // must stay visually distinct from an unmeasured one, so a
            // plain zero plan gets neither mark nor extra footnote.
            var result = MakeResult(totalCoinCost: 0);
            result.MaterialOpportunityCost = 0;

            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;
            var costTiles = rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.All(costTiles, t => Assert.DoesNotContain(
                PlanViewModelBuilder.UnpricedTileMarker, t.Label));
            Assert.All(costTiles, t => Assert.DoesNotContain(
                PlanViewModelBuilder.UnpricedTooltipSuffix, t.TooltipText));
            Assert.Equal(
                PlanViewModelBuilder.FootnoteText,
                Assert.Single(rows.Where(r => r.RowType == PlanRowType.SummaryFootnote)).Label);
        }

        [Fact]
        public void CostBand_NonZeroCostWithAnUnpricedNode_IsMarkedAndDisclosed()
        {
            // The disclosure used to be gated on TotalCoinCost == 0, so a
            // priced plan carrying an item nothing could cost showed its
            // total as if it were the whole answer. A nonzero total is
            // exactly where a reader mistakes it for one.
            var result = MakeResult(totalCoinCost: 500);
            result.CraftingTree = UnpricedChildTree();

            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;

            Assert.All(
                rows.Where(r => r.RowType == PlanRowType.CostFormulaTile),
                t => Assert.Contains(PlanViewModelBuilder.UnpricedTileMarker, t.Label));
            Assert.Contains(
                rows,
                r => r.RowType == PlanRowType.SummaryFootnote
                    && r.Label == PlanViewModelBuilder.UnpricedFootnoteText);
        }

        [Fact]
        public void CostBand_NonZeroCostWithAnUnpricedNode_KeepsTheProfitBand()
        {
            // Only the ZERO-total case suppresses the profit band (with
            // materials at 0 the "profit" would be the whole sale price).
            // Widening the floor disclosure must not widen that.
            var result = MakeResult(totalCoinCost: 500);
            result.CraftingTree = UnpricedChildTree();
            result.NetSaleValue = 900;

            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;

            Assert.Equal(3, rows.Count(r => r.RowType == PlanRowType.ProfitFormulaTile));
            Assert.DoesNotContain(
                rows,
                r => r.RowType == PlanRowType.SummaryFootnote
                    && r.Label == PlanViewModelBuilder.ProfitSuppressedFootnoteText);
        }

        [Fact]
        public void CostBand_ZeroCostWithUnpricedNodeOnlyInAReferenceBranch_RendersFullBand()
        {
            // A reference branch is the dimmed "what it would cost to
            // craft instead" comparison, not part of the plan - an
            // unpriced ingredient down there costs the user nothing and
            // must not suppress the band.
            var result = MakeResult(totalCoinCost: 0);
            result.MaterialOpportunityCost = 0;
            result.CraftingTree = new CraftingTreeNode
            {
                ItemId = 1,
                NodeId = 1,
                Quantity = 1,
                Decision = CraftingDecision.Have,
                IsReferenceBranch = true,
                Children = new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 2,
                        NodeId = 2,
                        Quantity = 1,
                        Decision = CraftingDecision.Unknown,
                    },
                },
            };

            var vm = _builder.Build(result);
            var costTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.All(costTiles, t => Assert.Equal(0L, t.CoinValue));
        }

        [Fact]
        public void CostBand_ZeroCostFromIgnoredChildren_RendersFullBand()
        {
            // An ignored node collapses to Have + IsIgnored, never to
            // Unknown (CraftingTreeBuilder.BuildNode), so the unpriced
            // guard cannot swallow the case this rule exists for.
            var result = MakeResult(totalCoinCost: 0);
            result.MaterialOpportunityCost = 0;
            result.CraftingTree = new CraftingTreeNode
            {
                ItemId = 1,
                NodeId = 1,
                Quantity = 1,
                Decision = CraftingDecision.Craft,
                Children = new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 2,
                        NodeId = 2,
                        Quantity = 1,
                        Decision = CraftingDecision.Have,
                        IsIgnored = true,
                    },
                },
            };

            var vm = _builder.Build(result);
            var costTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.All(costTiles, t => Assert.Equal(0L, t.CoinValue));
        }

        [Fact]
        public void CostBand_ZeroCostWithAnUnpricedMultiItemRoot_RendersMarkedBand()
        {
            // A batch exposes its N roots through MultiItemRoots and
            // leaves CraftingTree null, so the walk has to cover both -
            // an unpriced root there marks the band exactly as a
            // single-item tree's unpriced child does.
            var result = MakeResult(
                totalCoinCost: 0,
                requestedItems: new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 1, Quantity = 1 },
                    new PlanRequestItem { ItemId = 2, Quantity = 1 },
                },
                multiItemRoots: new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 1,
                        NodeId = 1,
                        Quantity = 1,
                        Decision = CraftingDecision.Have,
                    },
                    new CraftingTreeNode
                    {
                        ItemId = 2,
                        NodeId = 2,
                        Quantity = 1,
                        Decision = CraftingDecision.Unknown,
                    },
                });
            result.MaterialOpportunityCost = 0;

            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;
            var costTiles = rows.Where(r => r.RowType == PlanRowType.CostFormulaTile).ToList();

            Assert.Equal(3, costTiles.Count);
            Assert.All(costTiles, t => Assert.Equal(0L, t.CoinValue));
            Assert.All(costTiles, t => Assert.EndsWith(
                PlanViewModelBuilder.UnpricedTileMarker, t.Label));
            Assert.Contains(
                rows,
                r => r.RowType == PlanRowType.SummaryFootnote
                    && r.Label == PlanViewModelBuilder.UnpricedFootnoteText);
        }

        [Fact]
        public void CostBand_BuyOrderBasis_QualifierMovesToActualCostTooltip()
        {
            var result = MakeResult(totalCoinCost: 100);
            result.PriceBasis = PriceBasis.BuyOrder;

            var vm = _builder.Build(result);
            // Last of the three tiles: the formula's "= Actual Cost to
            // Craft" term.
            var costTile = vm.Sections[0].Rows.Last(r => r.RowType == PlanRowType.CostFormulaTile);

            // Caption stays short (no qualifier baked into the Label) - the
            // basis qualifier now lives in the tooltip only.
            Assert.Equal("Actual Cost to Craft", costTile.Label);
            Assert.Contains("buy-order prices", costTile.TooltipText);
        }

        // --- Profit formula band (presence/absence, arithmetic, sign) ---
        [Fact]
        public void ProfitBand_NoSellPrice_Absent()
        {
            var result = MakeResult(totalCoinCost: 500);
            var vm = _builder.Build(result);

            Assert.DoesNotContain(vm.Sections[0].Rows, r => r.RowType == PlanRowType.ProfitFormulaTile);
        }

        [Fact]
        public void ProfitBand_UnpricedZeroWithSellPrice_AbsentAndAccountedForInText()
        {
            // The cost band keeps its cells at 0 here; the profit band
            // cannot (its tiles would not be zeros), so the section has to
            // say why those cells are gone rather than just drop them.
            var result = MakeResult(totalCoinCost: 0);
            result.MaterialOpportunityCost = 0;
            result.NetSaleValue = 340;
            result.CraftingProfit = 340;
            result.CraftingTree = new CraftingTreeNode
            {
                ItemId = 1,
                NodeId = 1,
                Quantity = 1,
                Decision = CraftingDecision.Craft,
                Children = new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 2,
                        NodeId = 2,
                        Quantity = 1,
                        Decision = CraftingDecision.Unknown,
                    },
                },
            };

            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;

            Assert.DoesNotContain(rows, r => r.RowType == PlanRowType.ProfitFormulaTile);

            var footnotes = rows.Where(r => r.RowType == PlanRowType.SummaryFootnote).ToList();
            Assert.Equal(3, footnotes.Count);
            Assert.Equal(PlanViewModelBuilder.UnpricedFootnoteText, footnotes[0].Label);
            Assert.Equal(PlanViewModelBuilder.ProfitSuppressedFootnoteText, footnotes[1].Label);
            Assert.Equal(PlanViewModelBuilder.FootnoteText, footnotes[2].Label);
        }

        [Fact]
        public void ProfitBand_UnpricedZeroWithoutSellPrice_CarriesNoSuppressionNote()
        {
            // Nothing was suppressed - the band was never going to render
            // without a sell price - so the note must not appear and claim
            // otherwise.
            var result = MakeResult(totalCoinCost: 0);
            result.MaterialOpportunityCost = 0;
            result.CraftingTree = new CraftingTreeNode
            {
                ItemId = 1,
                NodeId = 1,
                Quantity = 1,
                Decision = CraftingDecision.Craft,
                Children = new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 2,
                        NodeId = 2,
                        Quantity = 1,
                        Decision = CraftingDecision.Unknown,
                    },
                },
            };

            var vm = _builder.Build(result);

            Assert.DoesNotContain(
                vm.Sections[0].Rows,
                r => r.Label == PlanViewModelBuilder.ProfitSuppressedFootnoteText);
        }

        [Fact]
        public void ProfitBand_SellPricePresent_ThreeTilesWithIdentityArithmetic()
        {
            var result = MakeResult(totalCoinCost: 300);
            result.TargetUnitSellPrice = 400;
            result.NetSaleValue = 340;
            result.CraftingProfit = 40;

            var vm = _builder.Build(result);
            var profitTiles = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.ProfitFormulaTile).ToList();

            Assert.Equal(3, profitTiles.Count);
            Assert.Equal("Sell Value", profitTiles[0].Label);
            Assert.Equal(340L, profitTiles[0].CoinValue);
            Assert.Equal("Total Materials Value", profitTiles[1].Label);
            // Single-item identity: NetSaleValue - CraftingProfit == 340 - 40 == 300 == TotalCoinCost.
            Assert.Equal(300L, profitTiles[1].CoinValue);
            Assert.Equal("Profit if Sold", profitTiles[2].Label);
            Assert.Equal(40L, profitTiles[2].CoinValue);
            Assert.All(profitTiles, t => Assert.False(string.IsNullOrEmpty(t.TooltipText)));

            // A non-negative profit means the drawn
            // "Sell Value - Total Materials Value = Profit if Sold"
            // equation is literally true (340 - 300 == 40), so the
            // renderer's final-boundary operator stays "=".
            Assert.True(profitTiles[2].FormulaResultIsExact);
        }

        [Fact]
        public void ProfitBand_NegativeProfit_LabeledLossWithAbsoluteValue()
        {
            var result = MakeResult(totalCoinCost: 500);
            result.NetSaleValue = 340;
            result.CraftingProfit = -160;

            var vm = _builder.Build(result);
            var profitTile = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.ProfitFormulaTile && r.Label.Contains("Loss"));

            Assert.Equal("Loss if Sold", profitTile.Label);
            Assert.Equal(160L, profitTile.CoinValue);

            // The abs-value "Loss if Sold" display
            // makes "Sell Value - Total Materials Value = Loss if Sold"
            // (340 - 500 = 160) arithmetically false - the true right-hand
            // side is -160, not 160. FormulaResultIsExact false tells
            // SummarySectionRenderer.CreateFormulaBand to draw a neutral
            // separator instead of an asserting "=" for this band's final
            // boundary.
            Assert.False(profitTile.FormulaResultIsExact);
        }

        [Fact]
        public void ProfitBand_ZeroProfit_FormulaResultIsExactTrue()
        {
            // profit == 0 is the boundary of the ">= 0" check in
            // BuildProfitFormulaBand - Label stays "Profit if Sold" (not
            // "Loss") and the equation (340 - 340 = 0) is exactly true, so
            // FormulaResultIsExact must be true here too, not just for a
            // strictly positive profit.
            var result = MakeResult(totalCoinCost: 340);
            result.NetSaleValue = 340;
            result.CraftingProfit = 0;

            var vm = _builder.Build(result);
            var profitTile = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.ProfitFormulaTile && r.Label.Contains("Profit"));

            Assert.Equal("Profit if Sold", profitTile.Label);
            Assert.Equal(0L, profitTile.CoinValue);
            Assert.True(profitTile.FormulaResultIsExact);
        }

        [Fact]
        public void ProfitBand_TotalMaterialsValueMatchesCostBand_ForSingleItemPlan()
        {
            // The identity (SellSideEconomics.ApplySellSideEconomics) makes
            // Band 2's derived Total Materials Value equal Band 1's, for
            // every single-item plan.
            var result = MakeResult(totalCoinCost: 200);
            result.MaterialOpportunityCost = 25;
            result.NetSaleValue = 340;
            result.CraftingProfit = 115; // 340 - 200 - 25

            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;

            long costBandTotalMaterialsValue = rows.First(r => r.RowType == PlanRowType.CostFormulaTile && r.Label == "Total Materials Value").CoinValue;
            long profitBandTotalMaterialsValue = rows.First(r => r.RowType == PlanRowType.ProfitFormulaTile && r.Label == "Total Materials Value").CoinValue;

            Assert.Equal(225L, costBandTotalMaterialsValue);
            Assert.Equal(costBandTotalMaterialsValue, profitBandTotalMaterialsValue);
        }

        [Fact]
        public void ProfitBand_CurrencyCostsPresent_CoinOnlyQualifierInTooltip()
        {
            var result = MakeResult(
                totalCoinCost: 100,
                currencyCosts: new List<CurrencyCost> { new CurrencyCost { CurrencyId = 2, Amount = 50 } });
            result.NetSaleValue = 340;
            result.CraftingProfit = 240;

            var vm = _builder.Build(result);
            var profitTile = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.ProfitFormulaTile && r.Label.Contains("Profit"));

            Assert.Equal("Profit if Sold", profitTile.Label);
            Assert.Contains("coin costs only", profitTile.TooltipText);
        }

        [Fact]
        public void ProfitBand_Overproduced_QualifierInSellTooltipNotLabel()
        {
            var result = MakeResult(targetQuantity: 1, totalCoinCost: 300);
            result.SellableQuantity = 5;
            result.NetSaleValue = 1700;
            result.CraftingProfit = 1400;

            var vm = _builder.Build(result);
            var sellTile = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.ProfitFormulaTile && r.Label == "Sell Value");

            Assert.Equal("Sell Value", sellTile.Label);
            Assert.Contains("5x", sellTile.TooltipText);
            Assert.Contains("overproduction", sellTile.TooltipText);
        }

        // --- Currency table rows (alphabetical, Required/Have/Needed) ---
        [Fact]
        public void CurrencyTable_RowsSortedAlphabeticallyByName()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 78, Amount = 250 }, // Fine Rift Essence
                new CurrencyCost { CurrencyId = 79, Amount = 50 },  // Rare Rift Essence
                new CurrencyCost { CurrencyId = 80, Amount = 100 }, // Masterwork Rift Essence,
            });
            var vm = _builder.Build(result);

            var ccRows = vm.Sections[0].Rows.Where(r => r.RowType == PlanRowType.CurrencyCost).ToList();
            Assert.Equal(3, ccRows.Count);
            Assert.Equal("Fine Rift Essence", ccRows[0].Label);
            Assert.Equal("Masterwork Rift Essence", ccRows[1].Label);
            Assert.Equal("Rare Rift Essence", ccRows[2].Label);
        }

        [Fact]
        public void CurrencyTable_LabelIsNameOnly_RequiredMovedToQuantity()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 50 },
            });
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("Spirit Shards", ccRow.Label);
            Assert.Equal(50, ccRow.Quantity);
        }

        // The paragraph the game's own currency tooltip shows under the
        // wallet balance. The renderer holds no currency id (see
        // PlanRowViewModel.CurrencyDescription), so if the builder does not
        // resolve it here nothing downstream can.
        [Fact]
        public void CurrencyTable_DescriptionResolvedFromCurrencyMetadata()
        {
            var result = MakeResult(
                currencyCosts: new List<CurrencyCost>
                {
                    new CurrencyCost { CurrencyId = 23, Amount = 50 },
                },
                currencyMetadata: new Dictionary<int, CurrencyMetadata>
                {
                    {
                        23,
                        new CurrencyMetadata
                        {
                            CurrencyId = 23,
                            Name = "Spirit Shard",
                            IconUrl = "shard.png",
                            Description = "Earned by gaining experience past level 80.",
                        }
                    },
                });

            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("Earned by gaining experience past level 80.", ccRow.CurrencyDescription);
        }

        // No offline description table exists, and one the module wrote
        // itself would be invented data - the hover drops the paragraph.
        [Fact]
        public void CurrencyTable_NoMetadata_DescriptionIsNull()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 50 },
            });

            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("Spirit Shards", ccRow.Label);
            Assert.Null(ccRow.CurrencyDescription);
        }

        // --- currency-ux-package (Feature 2): plan-scope passthrough for
        // the Recipe Tree's per-leaf currency pill ---
        [Fact]
        public void CurrencyPlanTotals_PopulatedFromPlanCurrencyCosts()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 2, Amount = 50 },
                new CurrencyCost { CurrencyId = 23, Amount = 3600 },
            });

            var vm = _builder.Build(result);

            Assert.NotNull(vm.CurrencyPlanTotals);
            Assert.Equal(50, vm.CurrencyPlanTotals[2]);
            Assert.Equal(3600, vm.CurrencyPlanTotals[23]);
        }

        [Fact]
        public void CurrencyPlanTotals_NoCurrencyCosts_IsNull()
        {
            var result = MakeResult();

            var vm = _builder.Build(result);

            Assert.Null(vm.CurrencyPlanTotals);
        }

        [Fact]
        public void OwnedCurrencyAmounts_PassesThroughResultUnchanged()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 2, Amount = 50 },
            });
            result.OwnedCurrencyAmounts = new Dictionary<int, int> { { 2, 10 } };

            var vm = _builder.Build(result);

            Assert.NotNull(vm.OwnedCurrencyAmounts);
            Assert.Equal(10, vm.OwnedCurrencyAmounts[2]);
        }

        [Fact]
        public void OwnedCurrencyAmounts_NoSnapshot_IsNull()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 2, Amount = 50 },
            });

            var vm = _builder.Build(result);

            Assert.Null(vm.OwnedCurrencyAmounts);
        }

        // Regression: BuildCurrencyTableRows used to narrow
        // CurrencyCost.Amount (long) to int with a plain unchecked
        // `(int)` cast. An Amount past int.MaxValue silently wraps to a
        // NEGATIVE required quantity, which then made
        // `fullyCovered = owned >= required` true for almost any owned
        // amount (even 0) - a plan that in reality needs billions of a
        // currency would have displayed as fully covered. Fixed with a
        // clamp to int.MaxValue (same convention as VendorBatchSolver.
        // ClampToInt) rather than a raw cast.
        [Fact]
        public void CurrencyTable_AmountExceedsIntRange_ClampsRatherThanWrapsNegative()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 2, Amount = 3_000_000_000L },
            });
            result.OwnedCurrencyAmounts = new Dictionary<int, int> { { 2, 0 } };

            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal(int.MaxValue, ccRow.Quantity);
            // The bug's own failure mode: a wrapped-negative required
            // quantity made this true even with 0 owned.
            Assert.False(ccRow.CurrencyFullyCovered);
            Assert.Equal(int.MaxValue, ccRow.CurrencyNeededQuantity);
        }

        [Fact]
        public void VendorCapsByItemId_PopulatedFromPlanTimegatedItems()
        {
            var result = MakeResult(timegatedItems: new List<TimegatedItem>
            {
                new TimegatedItem { ItemId = 5, CapType = TimegatedCapType.Daily, CapValue = 3, NeededCount = 10 },
            });

            var vm = _builder.Build(result);

            Assert.NotNull(vm.VendorCapsByItemId);
            Assert.True(vm.VendorCapsByItemId.TryGetValue(5, out var cap));
            Assert.Equal(TimegatedCapType.Daily, cap.CapType);
            Assert.Equal(3, cap.CapValue);
        }

        [Fact]
        public void VendorCapsByItemId_NoTimegatedItems_IsNull()
        {
            var result = MakeResult();

            var vm = _builder.Build(result);

            Assert.Null(vm.VendorCapsByItemId);
        }

        [Fact]
        public void CurrencyTable_HaveIsUnclamped_ExceedsRequired()
        {
            // The old behavior clamped this to
            // 500 (the Required amount) - the redesigned "Have" column
            // must show the REAL holding instead.
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 500 },
            });
            result.OwnedCurrencyAmounts = new Dictionary<int, int> { { 23, 999999 } };

            var vm = _builder.Build(result);
            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);

            Assert.Equal(999999, ccRow.CurrencyOwnedQuantity);
            Assert.Equal(500, ccRow.Quantity);
        }

        [Fact]
        public void CurrencyTable_HaveCoversRequired_NeededZeroAndFullyCoveredTrue()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 500 },
            });
            result.OwnedCurrencyAmounts = new Dictionary<int, int> { { 23, 999999 } };

            var vm = _builder.Build(result);
            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);

            Assert.Equal(0, ccRow.CurrencyNeededQuantity);
            Assert.True(ccRow.CurrencyFullyCovered);
        }

        [Fact]
        public void CurrencyTable_HaveExactlyEqualsRequired_FullyCoveredTrue()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 200 },
            });
            result.OwnedCurrencyAmounts = new Dictionary<int, int> { { 23, 200 } };

            var vm = _builder.Build(result);
            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);

            Assert.Equal(0, ccRow.CurrencyNeededQuantity);
            Assert.True(ccRow.CurrencyFullyCovered);
        }

        [Fact]
        public void CurrencyTable_HaveBelowRequired_NeededIsGapAndNotCovered()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 500 },
            });
            result.OwnedCurrencyAmounts = new Dictionary<int, int> { { 23, 200 } };

            var vm = _builder.Build(result);
            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);

            Assert.Equal(200, ccRow.CurrencyOwnedQuantity);
            Assert.Equal(300, ccRow.CurrencyNeededQuantity);
            Assert.False(ccRow.CurrencyFullyCovered);
        }

        [Fact]
        public void CurrencyTable_NoWalletData_HaveAndNeededNullNotCovered()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 500 },
            });

            var vm = _builder.Build(result);
            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);

            Assert.Null(ccRow.CurrencyOwnedQuantity);
            Assert.Null(ccRow.CurrencyNeededQuantity);
            Assert.False(ccRow.CurrencyFullyCovered);
        }

        [Fact]
        public void CurrencyTable_WalletMissingThisCurrencyId_HaveAndNeededNull()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 500 },
            });
            result.OwnedCurrencyAmounts = new Dictionary<int, int> { { 2, 100 } }; // different currency id

            var vm = _builder.Build(result);
            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);

            Assert.Null(ccRow.CurrencyOwnedQuantity);
            Assert.Null(ccRow.CurrencyNeededQuantity);
            Assert.False(ccRow.CurrencyFullyCovered);
        }

        [Fact]
        public void CurrencyTable_AstralAcclaim_CorrectName()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 63, Amount = 375 },
            });
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("Astral Acclaim", ccRow.Label);
            Assert.Equal(375, ccRow.Quantity);
        }

        [Fact]
        public void CurrencyTable_UnknownCurrency_NoIdDisplayed()
        {
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 99999, Amount = 10 },
            });
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("Currency", ccRow.Label);
            Assert.DoesNotContain("99999", ccRow.Label);
            Assert.Equal(10, ccRow.Quantity);
        }

        // --- Currency icons ---
        [Fact]
        public void CurrencyTable_IconUrlFromMetadata_WhenPresent()
        {
            var currencyMeta = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "Spirit Shards", IconUrl = "spirit_shard.png" },
            };
            var result = MakeResult(
                currencyCosts: new List<CurrencyCost> { new CurrencyCost { CurrencyId = 23, Amount = 50 } },
                currencyMetadata: currencyMeta);
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("spirit_shard.png", ccRow.IconUrl);
            Assert.Equal("Spirit Shards", ccRow.Label);
        }

        [Fact]
        public void CurrencyTable_IconUrlNull_WhenMetadataAbsent()
        {
            // No CurrencyMetadata supplied at all (e.g. the pipeline was not
            // wired with a CurrencyMetadataService) - row must render
            // exactly as it did before icons existed: text-only, no guess.
            var result = MakeResult(currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 50 },
            });
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Null(ccRow.IconUrl);
            Assert.Equal("Spirit Shards", ccRow.Label);
        }

        [Fact]
        public void CurrencyTable_IconUrlNull_WhenIdMissingFromMetadata()
        {
            // Metadata dictionary is present (fetch succeeded) but does not
            // contain this particular currency id - still no placeholder.
            var currencyMeta = new Dictionary<int, CurrencyMetadata>
            {
                [2] = new CurrencyMetadata { CurrencyId = 2, Name = "Karma", IconUrl = "karma.png" },
            };
            var result = MakeResult(
                currencyCosts: new List<CurrencyCost> { new CurrencyCost { CurrencyId = 23, Amount = 50 } },
                currencyMetadata: currencyMeta);
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Null(ccRow.IconUrl);
            Assert.Equal("Spirit Shards", ccRow.Label);
        }

        [Fact]
        public void CurrencyTable_NamePrefersMetadataOverConstantsFallback()
        {
            // Metadata name deliberately differs from the Gw2Constants
            // offline table to prove the live-fetched name wins.
            var currencyMeta = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "Spirit Shard (Live)", IconUrl = "s.png" },
            };
            var result = MakeResult(
                currencyCosts: new List<CurrencyCost> { new CurrencyCost { CurrencyId = 23, Amount = 7 } },
                currencyMetadata: currencyMeta);
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("Spirit Shard (Live)", ccRow.Label);
            Assert.Equal(7, ccRow.Quantity);
        }

        [Fact]
        public void CurrencyTable_IdPresentButEmptyNameAndIcon_FallsBackToConstantsAndNullIcon()
        {
            // Id IS in the metadata dictionary (fetch succeeded and covers
            // this currency), but the entry's Name/IconUrl are both empty
            // strings (e.g. an API payload with a blank name) - the
            // !string.IsNullOrEmpty guards in ResolveCurrencyName/
            // ResolveCurrencyIconUrl must treat that the same as "absent"
            // rather than rendering a blank label or a bogus icon.
            var currencyMeta = new Dictionary<int, CurrencyMetadata>
            {
                [23] = new CurrencyMetadata { CurrencyId = 23, Name = "", IconUrl = "" },
            };
            var result = MakeResult(
                currencyCosts: new List<CurrencyCost> { new CurrencyCost { CurrencyId = 23, Amount = 50 } },
                currencyMetadata: currencyMeta);
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("Spirit Shards", ccRow.Label);
            Assert.Null(ccRow.IconUrl);
        }

        [Fact]
        public void CurrencyTable_UnknownId_FallsBackToGeneric_EvenWithMetadataPresent()
        {
            var currencyMeta = new Dictionary<int, CurrencyMetadata>
            {
                [2] = new CurrencyMetadata { CurrencyId = 2, Name = "Karma", IconUrl = "karma.png" },
            };
            var result = MakeResult(
                currencyCosts: new List<CurrencyCost> { new CurrencyCost { CurrencyId = 99999, Amount = 10 } },
                currencyMetadata: currencyMeta);
            var vm = _builder.Build(result);

            var ccRow = vm.Sections[0].Rows.Single(r => r.RowType == PlanRowType.CurrencyCost);
            Assert.Equal("Currency", ccRow.Label);
            Assert.Null(ccRow.IconUrl);
        }

        // --- Footnote row ---
        [Fact]
        public void Footnote_AlwaysPresentAsLastRow()
        {
            var result = MakeResult(totalCoinCost: 500, currencyCosts: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 23, Amount = 50 },
            });
            var vm = _builder.Build(result);
            var rows = vm.Sections[0].Rows;

            Assert.Equal(PlanRowType.SummaryFootnote, rows[rows.Count - 1].RowType);
            Assert.Equal(
                "Prices are Trading Post data - actual purchase and sale prices are likely to vary.",
                rows[rows.Count - 1].Label);
        }

        // --- Price-side fallback (TEST GAP): PlanViewModel.PriceBasis pass-through ---
        //
        // PlanViewModelBuilder.Build's `PriceBasis = result.PriceBasis`
        // assignment is the SOLE feed for TreeSectionController's fell-
        // back-price tooltip caveat, which renders one of two OPPOSITE
        // sentences depending on this value: the buy-order price being
        // unavailable, or the instant-buy price being unavailable.
        // Nothing previously asserted the
        // assignment itself, so deleting it (or any future refactor that
        // silently drops it) would leave every BuyOrder-basis plan
        // rendering the InstantBuy-basis sentence - the exact inverse
        // claim - with no test failing. BuyOrder is asserted explicitly
        // (not the enum's own default of InstantBuy = 0) so a dropped
        // assignment, which would leave vm.PriceBasis at its own default
        // of InstantBuy, cannot coincidentally satisfy the assertion.
        [Fact]
        public void Build_PriceBasisBuyOrder_PassedThroughToViewModel()
        {
            var result = MakeResult();
            result.PriceBasis = PriceBasis.BuyOrder;

            var vm = _builder.Build(result);

            Assert.Equal(PriceBasis.BuyOrder, vm.PriceBasis);
        }

        [Fact]
        public void Build_PriceBasisInstantBuy_PassedThroughToViewModel()
        {
            var result = MakeResult();
            result.PriceBasis = PriceBasis.InstantBuy;

            var vm = _builder.Build(result);

            Assert.Equal(PriceBasis.InstantBuy, vm.PriceBasis);
        }

        // A crafted root over one node the pipeline could neither craft
        // nor price - CraftingDecision.Unknown, the same shape
        // CraftingTreeBuilder.BuildNode produces for such an item.
        private static CraftingTreeNode UnpricedChildTree()
        {
            return new CraftingTreeNode
            {
                ItemId = 1,
                NodeId = 1,
                Quantity = 1,
                Decision = CraftingDecision.Craft,
                Children = new List<CraftingTreeNode>
                {
                    new CraftingTreeNode
                    {
                        ItemId = 2,
                        NodeId = 2,
                        Quantity = 1,
                        Decision = CraftingDecision.Unknown,
                    },
                },
            };
        }

        // --- Non-coin cost row identity (the scroll anchor's key) ---
        [Fact]
        public void CurrencyTable_EveryRowCarriesAnIdentity_AndTheTwoIdSpacesDoNotCollide()
        {
            // Id 24 is a real item AND the currency "Pristine Fractal
            // Relics". Both rows are in one table, so the id alone cannot
            // identify a row; the scroll anchor registry would key them
            // both to whichever control registered last.
            var result = MakeResult(
                currencyCosts: new List<CurrencyCost>
                {
                    new CurrencyCost { CurrencyId = 24, Amount = 5 },
                });
            result.Plan.BarterItemCosts = new List<BarterItemCost>
            {
                new BarterItemCost { ItemId = 24, Amount = 3 },
            };

            var rows = _builder.Build(result).Sections[0].Rows
                .Where(r => r.RowType == PlanRowType.CurrencyCost)
                .ToList();

            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.False(string.IsNullOrEmpty(r.NonCoinCostKey)));
            Assert.Equal(2, rows.Select(r => r.NonCoinCostKey).Distinct().Count());
        }

        [Fact]
        public void CurrencyTable_ReSolveGainingARow_LeavesTheOtherRowsKeysAlone()
        {
            // What the scroll anchor depends on: a row's key must survive
            // the re-solve that adds rows around it, or the restore has no
            // element to put back on the line the user was reading.
            var before = _builder.Build(MakeResult(
                currencyCosts: new List<CurrencyCost>
                {
                    new CurrencyCost { CurrencyId = 3, Amount = 5 },
                }));
            var after = _builder.Build(MakeResult(
                currencyCosts: new List<CurrencyCost>
                {
                    new CurrencyCost { CurrencyId = 2, Amount = 9 },
                    new CurrencyCost { CurrencyId = 3, Amount = 5 },
                }));

            string keyBefore = before.Sections[0].Rows
                .Single(r => r.RowType == PlanRowType.CurrencyCost).NonCoinCostKey;
            var keysAfter = after.Sections[0].Rows
                .Where(r => r.RowType == PlanRowType.CurrencyCost)
                .Select(r => r.NonCoinCostKey)
                .ToList();

            Assert.Equal(2, keysAfter.Count);
            Assert.Contains(keyBefore, keysAfter);
        }

        [Fact]
        public void CurrencyTable_RowsOtherThanCostRows_CarryNoIdentity()
        {
            var result = MakeResult(
                currencyCosts: new List<CurrencyCost>
                {
                    new CurrencyCost { CurrencyId = 3, Amount = 5 },
                });

            var rows = _builder.Build(result).Sections[0].Rows;

            Assert.All(
                rows.Where(r => r.RowType != PlanRowType.CurrencyCost),
                r => Assert.Null(r.NonCoinCostKey));
        }
    }
}
