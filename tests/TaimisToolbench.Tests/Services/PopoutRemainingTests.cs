using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class PopoutRemainingTests
    {
        private const int MithrilOre = 19700;
        private const int ElderWoodLog = 19722;

        private static PlanRowViewModel Buy(int itemId, string label, int quantity, long coin)
        {
            return new PlanRowViewModel
            {
                RowType = PlanRowType.ShoppingBuy,
                ItemId = itemId,
                Label = label,
                Quantity = quantity,
                CoinValue = coin,
                UnitCoinValue = quantity > 0 ? coin / quantity : 0,
            };
        }

        private static SnapshotItemEntry Held(int itemId, int count, string source = "Bank")
        {
            return new SnapshotItemEntry { ItemId = itemId, Count = count, Source = source };
        }

        [Fact]
        public void CountItems_AddsUpEveryStorageLocation()
        {
            var counts = PopoutRemaining.CountItems(new List<SnapshotItemEntry>
            {
                Held(MithrilOre, 40, "Bank"),
                Held(MithrilOre, 60, "MaterialStorage"),
                Held(ElderWoodLog, 5, "Character:Anna"),
            });

            Assert.Equal(100, counts[MithrilOre]);
            Assert.Equal(5, counts[ElderWoodLog]);
        }

        [Fact]
        public void CountItems_IgnoresAnEntryThatHoldsNothing()
        {
            var counts = PopoutRemaining.CountItems(new List<SnapshotItemEntry>
            {
                Held(MithrilOre, 0),
                Held(ElderWoodLog, -3),
            });

            Assert.Empty(counts);
        }

        [Fact]
        public void Apply_DropsARowThePlayerHasNowBought()
        {
            var rows = new List<PlanRowViewModel> { Buy(MithrilOre, "Mithril Ore", 250, 5000) };
            var before = PopoutRemaining.CountItems(new List<SnapshotItemEntry>());
            var after = PopoutRemaining.CountItems(new List<SnapshotItemEntry> { Held(MithrilOre, 250) });

            var result = PopoutRemaining.Apply(rows, before, after);

            Assert.Empty(result.Rows);
        }

        [Fact]
        public void Apply_LeavesTheOutstandingQuantityOnAPartlyBoughtRow()
        {
            var rows = new List<PlanRowViewModel> { Buy(MithrilOre, "Mithril Ore", 250, 5000) };
            var before = PopoutRemaining.CountItems(new List<SnapshotItemEntry>());
            var after = PopoutRemaining.CountItems(new List<SnapshotItemEntry> { Held(MithrilOre, 100) });

            var result = PopoutRemaining.Apply(rows, before, after);

            var row = Assert.Single(result.Rows);
            Assert.Equal(150, row.Quantity);
            Assert.Equal(3000, row.CoinValue);
            Assert.Equal(20, row.UnitCoinValue);
        }

        [Fact]
        public void Apply_LeavesARowAloneWhenNothingWasAcquired()
        {
            var original = Buy(MithrilOre, "Mithril Ore", 250, 5000);
            var counts = PopoutRemaining.CountItems(new List<SnapshotItemEntry> { Held(MithrilOre, 12) });

            var result = PopoutRemaining.Apply(
                new List<PlanRowViewModel> { original }, counts, counts);

            Assert.Same(original, Assert.Single(result.Rows));
        }

        /// <summary>
        /// The reason progress is a delta and not an absolute count.
        /// Crafting spends the very materials a shopping row was cleared
        /// for, and a fall in the holding must not put the row back.
        /// </summary>
        [Fact]
        public void Apply_DoesNotResurrectARowWhenTheMaterialsAreSpent()
        {
            var rows = new List<PlanRowViewModel> { Buy(MithrilOre, "Mithril Ore", 250, 5000) };
            var atPlan = PopoutRemaining.CountItems(new List<SnapshotItemEntry>());
            var bought = PopoutRemaining.CountItems(new List<SnapshotItemEntry> { Held(MithrilOre, 250) });

            var afterBuying = PopoutRemaining.Apply(rows, atPlan, bought);
            Assert.Empty(afterBuying.Rows);

            var spent = PopoutRemaining.CountItems(new List<SnapshotItemEntry>());
            var afterCrafting = PopoutRemaining.Apply(afterBuying.Rows, afterBuying.Baseline, spent);

            Assert.Empty(afterCrafting.Rows);
        }

        [Fact]
        public void Apply_SpendsOneItemsGainDownTheSectionRatherThanOfferingItTwice()
        {
            var rows = new List<PlanRowViewModel>
            {
                Buy(MithrilOre, "Mithril Ore", 100, 2000),
                Buy(MithrilOre, "Mithril Ore", 100, 2000),
            };
            var before = PopoutRemaining.CountItems(new List<SnapshotItemEntry>());
            var after = PopoutRemaining.CountItems(new List<SnapshotItemEntry> { Held(MithrilOre, 120) });

            var result = PopoutRemaining.Apply(rows, before, after);

            var row = Assert.Single(result.Rows);
            Assert.Equal(80, row.Quantity);
        }

        [Fact]
        public void Apply_NeverTouchesARowThatNamesNoItem()
        {
            var currencyRow = new PlanRowViewModel
            {
                RowType = PlanRowType.ShoppingCurrency,
                ItemId = 0,
                Label = "Spirit Shards",
                Quantity = 40,
            };
            var counts = PopoutRemaining.CountItems(new List<SnapshotItemEntry> { Held(MithrilOre, 999) });

            var result = PopoutRemaining.Apply(
                new List<PlanRowViewModel> { currencyRow },
                PopoutRemaining.CountItems(new List<SnapshotItemEntry>()),
                counts);

            Assert.Same(currencyRow, Assert.Single(result.Rows));
        }

        [Fact]
        public void Apply_ScalesTheCurrencyHalfOfAVendorRowsTotal()
        {
            var row = Buy(MithrilOre, "Vendor Thing", 10, 0);
            row.RowType = PlanRowType.ShoppingVendor;
            row.CurrencyCosts = new List<CurrencyAmountViewModel>
            {
                new CurrencyAmountViewModel { Amount = 100, Name = "Karma" },
            };

            var result = PopoutRemaining.Apply(
                new List<PlanRowViewModel> { row },
                PopoutRemaining.CountItems(new List<SnapshotItemEntry>()),
                PopoutRemaining.CountItems(new List<SnapshotItemEntry> { Held(MithrilOre, 6) }));

            var scaled = Assert.Single(result.Rows);
            Assert.Equal(4, scaled.Quantity);
            Assert.Equal(40, Assert.Single(scaled.CurrencyCosts).Amount);
            Assert.Equal(100, Assert.Single(row.CurrencyCosts).Amount);
        }

        /// <summary>
        /// A scaled row is a copy, and a field the copy forgets goes blank
        /// in the popout while the plan tab still shows it. Every public
        /// property is given a distinctive value here and checked on the way
        /// out, so a property added to the model without being carried fails
        /// this rather than shipping.
        /// </summary>
        [Fact]
        public void Apply_ScaledRowCarriesEveryFieldTheModelHas()
        {
            var source = Buy(MithrilOre, "Mithril Ore", 10, 1000);
            var properties = typeof(PlanRowViewModel)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .ToList();

            var changed = new HashSet<string> { "Quantity", "CoinValue", "CurrencyCosts" };
            foreach (var property in properties)
            {
                if (property.Name == "ItemId" || property.Name == "RowType"
                    || changed.Contains(property.Name))
                {
                    continue;
                }

                property.SetValue(source, DistinctiveValue(property));
            }

            var result = PopoutRemaining.Apply(
                new List<PlanRowViewModel> { source },
                PopoutRemaining.CountItems(new List<SnapshotItemEntry>()),
                PopoutRemaining.CountItems(new List<SnapshotItemEntry> { Held(MithrilOre, 4) }));

            var scaled = Assert.Single(result.Rows);
            Assert.NotSame(source, scaled);
            foreach (var property in properties)
            {
                if (changed.Contains(property.Name))
                {
                    continue;
                }

                Assert.Equal(property.GetValue(source), property.GetValue(scaled));
            }
        }

        /// <summary>
        /// A value no default constructor would produce, per property type.
        /// An unhandled type throws on purpose: a new kind of field on the
        /// row model has to be looked at rather than silently skipped.
        /// </summary>
        private static object DistinctiveValue(PropertyInfo property)
        {
            var type = property.PropertyType;
            if (type == typeof(string))
            {
                return "value-of-" + property.Name;
            }

            if (type == typeof(int))
            {
                return 7;
            }

            if (type == typeof(long))
            {
                return 11L;
            }

            if (type == typeof(bool))
            {
                return !(bool)property.GetValue(new PlanRowViewModel());
            }

            if (type == typeof(int?))
            {
                return 13;
            }

            if (type == typeof(double?))
            {
                return 17.5d;
            }

            if (type == typeof(List<CurrencyAmountViewModel>))
            {
                return new List<CurrencyAmountViewModel>
                {
                    new CurrencyAmountViewModel { Amount = 3, Name = property.Name },
                };
            }

            if (type == typeof(IconWikiTarget))
            {
                return IconWikiTarget.ItemPage("Mithril Ore");
            }

            throw new Xunit.Sdk.XunitException(
                "PlanRowViewModel." + property.Name + " is a " + type.Name
                + ", which this test does not know how to fill. Give it a value here and"
                + " make sure PopoutRemaining carries it.");
        }
    }
}
