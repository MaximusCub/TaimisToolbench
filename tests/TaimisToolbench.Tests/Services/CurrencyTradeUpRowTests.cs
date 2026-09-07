using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// A row the plan buys from a vendor for one wallet currency and
    /// nothing else, and the three things that row shows because of it:
    /// the "BUYS n/m NEEDED" pill, that pill's tooltip, and a right-click
    /// that opens the wiki's Acquisition section rather than the page top.
    /// </summary>
    public class CurrencyTradeUpRowTests
    {
        private const int CalcifiedGasp = 75;

        private static CraftingTreeNode TradeUpNode(
            int quantity = 18,
            int currencyPerUnit = 250,
            string name = "Clot of Congealed Screams")
        {
            return new CraftingTreeNode
            {
                ItemId = 100098,
                NodeId = 1,
                Name = name,
                Quantity = quantity,
                Decision = CraftingDecision.BuyFromVendor,
                CanBuyVendor = true,
                SubtreeCost = 0,
                VendorCurrencyCosts = new List<CostLine>
                {
                    new CostLine
                    {
                        Type = "Currency",
                        Id = CalcifiedGasp,
                        Count = quantity * currencyPerUnit,
                    },
                },
            };
        }

        [Fact]
        public void APureCurrencyVendorRow_Matches()
        {
            Assert.True(CurrencyTradeUpRow.Matches(TradeUpNode()));
        }

        [Fact]
        public void ARowWithACoinPart_DoesNotMatch()
        {
            // Any coin the offer charges lands in SubtreeCost, so the row
            // is not a straight currency trade and the module can price it
            // the ordinary way.
            var node = TradeUpNode();
            node.SubtreeCost = 4200;

            Assert.False(CurrencyTradeUpRow.Matches(node));
        }

        [Fact]
        public void ARowWithABarterItemCost_DoesNotMatch()
        {
            var node = TradeUpNode();
            node.VendorHasBarterItemCost = true;

            Assert.False(CurrencyTradeUpRow.Matches(node));
        }

        [Fact]
        public void ARowChargingTwoCurrencies_DoesNotMatch()
        {
            var node = TradeUpNode();
            var lines = node.VendorCurrencyCosts.ToList();
            lines.Add(new CostLine { Type = "Currency", Id = 29, Count = 3 });
            node.VendorCurrencyCosts = lines;

            Assert.False(CurrencyTradeUpRow.Matches(node));
        }

        [Fact]
        public void ACraftedRow_DoesNotMatch()
        {
            var node = TradeUpNode();
            node.Decision = CraftingDecision.Craft;

            Assert.False(CurrencyTradeUpRow.Matches(node));
        }

        [Theory]
        // 1,200 Calcified Gasp against 18 x 250: 4 whole trades.
        [InlineData(1200, 4)]
        [InlineData(0, 0)]
        [InlineData(249, 0)]
        [InlineData(250, 1)]
        // More than the row needs is clamped to the row's own quantity.
        [InlineData(9000, 18)]
        [InlineData(int.MaxValue, 18)]
        public void AffordableNow_CountsWholeTradesAndNeverExceedsTheRow(int held, int expected)
        {
            var owned = new Dictionary<int, int> { { CalcifiedGasp, held } };

            Assert.True(CurrencyTradeUpRow.TryGetAffordableNow(TradeUpNode(), owned, out int affordable));
            Assert.Equal(expected, affordable);
        }

        [Fact]
        public void AffordableNow_RoundsDown_WhenTheRowCostDoesNotDivideEvenly()
        {
            // 7 units for 1,000: 142.857 per unit. A holding of 900 buys 6
            // whole units, not 7, and not the 6.3 a rounded per-unit price
            // would suggest.
            var node = TradeUpNode(quantity: 7);
            node.VendorCurrencyCosts = new List<CostLine>
            {
                new CostLine { Type = "Currency", Id = CalcifiedGasp, Count = 1000 },
            };
            var owned = new Dictionary<int, int> { { CalcifiedGasp, 900 } };

            Assert.True(CurrencyTradeUpRow.TryGetAffordableNow(node, owned, out int affordable));
            Assert.Equal(6, affordable);
        }

        [Fact]
        public void AffordableNow_IsUnknown_WithNoWalletSnapshot()
        {
            Assert.False(CurrencyTradeUpRow.TryGetAffordableNow(TradeUpNode(), null, out _));
        }

        [Fact]
        public void AffordableNow_IsUnknown_WhenTheWalletDoesNotCarryThatCurrency()
        {
            var owned = new Dictionary<int, int> { { 29, 500 } };

            Assert.False(CurrencyTradeUpRow.TryGetAffordableNow(TradeUpNode(), owned, out _));
        }

        [Fact]
        public void ThePill_IsNotDrawnOnARowAtAll()
        {
            // Reported in game: the pill appeared on a craft-to-vendor
            // toggle and vanished on the reverse, so it widened the pill
            // column, and that width ratchets for the rest of the plan's
            // life. Every row's pills and name text shifted left.
            var owned = new Dictionary<int, int> { { CalcifiedGasp, 1200 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(TradeUpNode(), null, owned);

            Assert.DoesNotContain(
                specs, s => s.Text.StartsWith(DecisionPillPlanner.CurrencyTradeUpPillPrefix));
        }

        [Fact]
        public void ThePill_IsOmitted_WithNoWalletSnapshot()
        {
            var specs = DecisionPillPlanner.BuildPillSpecs(TradeUpNode(), null, null);

            Assert.DoesNotContain(specs, s => s.Text.StartsWith("BUYS "));
        }

        [Fact]
        public void ThePill_IsOmitted_OnARowThatIsNotAStraightCurrencyTrade()
        {
            var node = TradeUpNode();
            node.SubtreeCost = 4200;
            var owned = new Dictionary<int, int> { { CalcifiedGasp, 1200 } };

            var specs = DecisionPillPlanner.BuildPillSpecs(node, null, owned);

            Assert.DoesNotContain(specs, s => s.Text.StartsWith("BUYS "));
        }

        [Fact]
        public void ThePillTooltip_SaysHowManyAreLeftToAcquire()
        {
            // The spec is built here rather than taken from a row: the
            // pill is not drawn today, and the wording it would carry is
            // still worth pinning for when it comes back.
            var node = TradeUpNode();
            var owned = new Dictionary<int, int> { { CalcifiedGasp, 1200 } };
            var spec = new PillSpec(
                DecisionPillPlanner.CurrencyTradeUpPillPrefix + "4/18 NEEDED",
                null,
                PillKind.OwnedInfo);

            var plan = PillTooltipTextComposer.Compose(
                spec, node, interactive: false, ignoreInteractive: false,
                currencyPlanTotals: null, ownedCurrencyAmounts: owned);

            Assert.Equal(
                "Your held currency buys 4 of the 18 this row needs - 14 still to acquire",
                plan.Text);
        }

        [Fact]
        public void ThePillTooltip_DoesNotStealTheOwnedMaterialsWording()
        {
            // The two annotations share PillKind.OwnedInfo, so the
            // currency branch must not swallow the item-stock pill that
            // can sit on the same row.
            var node = TradeUpNode();
            node.OwnedQuantityUsed = 3;
            var owned = new Dictionary<int, int> { { CalcifiedGasp, 1200 } };
            var spec = DecisionPillPlanner.BuildPillSpecs(node, null, owned)
                .Single(s => s.Text.EndsWith("NEEDED") && !s.Text.StartsWith("BUYS "));

            var plan = PillTooltipTextComposer.Compose(
                spec, node, interactive: false, ignoreInteractive: false,
                currencyPlanTotals: null, ownedCurrencyAmounts: owned);

            Assert.Equal(
                "Needs 21 total - 3 covered by your materials, 18 left to acquire",
                plan.Text);
        }

        [Fact]
        public void TheRightClick_OpensTheWikiAcquisitionSection()
        {
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Clot_of_Congealed_Screams#Acquisition",
                TreeRowTooltipComposer.BuildWikiUrl(TradeUpNode()));
        }

        [Fact]
        public void TheRightClick_OpensThePlainPage_OnEveryOtherRow()
        {
            var node = TradeUpNode();
            node.Decision = CraftingDecision.Craft;

            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Clot_of_Congealed_Screams",
                TreeRowTooltipComposer.BuildWikiUrl(node));
        }

        [Fact]
        public void TheTooltipAffordanceLine_NamesTheAcquisitionSection()
        {
            var lines = TreeRowTooltipComposer
                .BuildExtraTooltipContent(TradeUpNode(), null, null)
                .ToPlainLines();

            Assert.Contains(TreeRowTooltipComposer.WikiAcquisitionHintText, lines);
            Assert.DoesNotContain(TreeRowTooltipComposer.WikiHintText, lines);
        }
    }
}
