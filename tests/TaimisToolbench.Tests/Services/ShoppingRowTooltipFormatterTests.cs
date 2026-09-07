using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Pins the exact wording ShoppingListSectionRenderer's tooltip
    /// depends on. Nothing observed these strings before, so a regression
    /// straight back to a banned phrasing passed the full suite. Two rules
    /// hold for every line: it must say the cost is THIS row's, never the
    /// whole plan's requirement for that currency id, and it must read as
    /// a sentence rather than shout.
    /// </summary>
    public class ShoppingRowTooltipFormatterTests
    {
        [Fact]
        public void BuildCurrencyLines_EveryLineReadsAsASentence()
        {
            // The second box's wording rule: no shouting, no dashes doing
            // a comma's job, and every line closes.
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel
                {
                    Amount = 3660, Name = "Trade Contract", OwnedQuantity = 2812, RawOwnedQuantity = 2812,
                },
                new CurrencyAmountViewModel
                {
                    Amount = 100, Name = "Karma", OwnedQuantity = 100, RawOwnedQuantity = 100,
                },
                new CurrencyAmountViewModel
                {
                    Amount = 10, Name = "Spirit Shards", OwnedQuantity = 10, RawOwnedQuantity = 40,
                },
            };

            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(costs);

            Assert.Equal(3, lines.Count);
            foreach (string line in lines)
            {
                Assert.EndsWith(".", line);
                Assert.DoesNotContain("\u2014", line);
                Assert.DoesNotContain(" - ", line);
                Assert.DoesNotContain(
                    line.Split(' ').Where(w => w.Length > 1 && w.All(char.IsLetter)),
                    w => w == w.ToUpperInvariant());
                Assert.Contains("this row costs", line);
            }
        }

        [Fact]
        public void BuildCurrencyLines_NullList_ReturnsEmptyList()
        {
            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(null);

            Assert.Empty(lines);
        }

        [Fact]
        public void BuildCurrencyLines_NoWalletData_LineSkipped()
        {
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel { Amount = 100, Name = "Karma", OwnedQuantity = null },
            };

            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(costs);

            Assert.Empty(lines);
        }

        [Fact]
        public void BuildCurrencyLines_ZeroAmount_LineSkipped()
        {
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel { Amount = 0, Name = "Karma", OwnedQuantity = 0, RawOwnedQuantity = 0 },
            };

            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(costs);

            Assert.Empty(lines);
        }

        [Fact]
        public void BuildCurrencyLines_Shortfall_NamesTheRowCostTheWalletHoldingAndTheGap()
        {
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel
                {
                    Amount = 500,
                    Name = "Karma",
                    OwnedQuantity = 200,
                    RawOwnedQuantity = 200,
                },
            };

            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(costs);

            Assert.Equal(
                new[] { "Karma: this row costs 500. You have 200 in your wallet and need 300 more." },
                lines);
        }

        [Fact]
        public void BuildCurrencyLines_ExactlyCovered_SaysTheWalletIsEnoughAndNamesNoSurplus()
        {
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel
                {
                    Amount = 500,
                    Name = "Spirit Shards",
                    OwnedQuantity = 500,
                    RawOwnedQuantity = 500,
                },
            };

            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(costs);

            Assert.Equal(
                new[] { "Spirit Shards: this row costs 500. You have enough in your wallet." },
                lines);
        }

        [Fact]
        public void BuildCurrencyLines_CoveredWithSurplus_NamesTheUnclampedWalletHolding()
        {
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel
                {
                    Amount = 500,
                    Name = "Spirit Shards",
                    OwnedQuantity = 500, // clamped by CurrencyDisplayResolver
                    RawOwnedQuantity = 999999,
                },
            };

            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(costs);

            Assert.Equal(
                new[] { "Spirit Shards: this row costs 500. Your wallet holds 999999." },
                lines);
        }

        [Fact]
        public void BuildCurrencyLines_RawOwnedQuantityNull_FallsBackToOwnedQuantity_NoAside()
        {
            // Guards the ?? fallback for a hypothetical future caller that
            // constructs the view model directly with only OwnedQuantity
            // set (RawOwnedQuantity left null) - must never crash, and must
            // never fabricate a surplus that was not actually reported.
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel
                {
                    Amount = 500,
                    Name = "Karma",
                    OwnedQuantity = 500,
                    RawOwnedQuantity = null,
                },
            };

            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(costs);

            Assert.Equal(
                new[] { "Karma: this row costs 500. You have enough in your wallet." },
                lines);
        }

        [Fact]
        public void BuildCurrencyLines_MultipleCurrencies_OneLinePerCurrencyInOrder()
        {
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel { Amount = 500, Name = "Karma", OwnedQuantity = 200, RawOwnedQuantity = 200 },
                new CurrencyAmountViewModel { Amount = 100, Name = "Spirit Shards", OwnedQuantity = 100, RawOwnedQuantity = 250 },
                new CurrencyAmountViewModel { Amount = 50, Name = "Unresolved Currency", OwnedQuantity = null },
            };

            var lines = ShoppingRowTooltipFormatter.BuildCurrencyLines(costs);

            Assert.Equal(new[]
            {
                "Karma: this row costs 500. You have 200 in your wallet and need 300 more.",
                "Spirit Shards: this row costs 100. Your wallet holds 250.",
            }, lines);
        }

        [Fact]
        public void BuildRowContent_KeepsTheRowsOwnLinesOutOfTheItemsBox()
        {
            var costs = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel
                {
                    Amount = 100, Name = "Karma", OwnedQuantity = 40, RawOwnedQuantity = 40,
                },
            };

            var content = ShoppingRowTooltipFormatter.BuildRowContent(
                new ItemStatBlock { ItemId = 1, Name = "Bag of Stuff", Rarity = "Fine", VendorValue = 7 },
                ItemTooltipIdentity.ForItem("Bag of Stuff", "icon://bag", "Fine"),
                hintText: "Salvage from level 80 gear.",
                currencyCosts: costs);

            var lines = content.ToPlainLines();

            // The stat block opens the tooltip, so the full-name line it
            // would otherwise duplicate is gone.
            Assert.Equal("Bag of Stuff", lines[0]);
            Assert.Equal(1, lines.Count(l => l == "Bag of Stuff"));
            Assert.DoesNotContain("Salvage from level 80 gear.", lines);

            Assert.Equal(
                new[]
                {
                    "Salvage from level 80 gear.",
                    "Karma: this row costs 100. You have 40 in your wallet and need 60 more.",
                },
                content.ToExtraLines());
        }

        [Fact]
        public void BuildRowContent_WithoutStats_StillHeadsWithTheRowsOwnIconAndName()
        {
            var content = ShoppingRowTooltipFormatter.BuildRowContent(
                null,
                ItemTooltipIdentity.ForItem("A Very Long Item Name", "icon://long", "Rare"),
                "A hint.",
                null);

            Assert.Equal(TooltipLineKind.Header, content.Lines[0].Kind);
            Assert.Equal("icon://long", content.Lines[0].IconUrl);
            Assert.Equal(new[] { "A Very Long Item Name" }, content.ToPlainLines());
            Assert.Equal(new[] { "A hint." }, content.ToExtraLines());
        }
}
}
