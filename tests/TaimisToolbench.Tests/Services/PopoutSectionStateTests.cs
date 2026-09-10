using System;
using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class PopoutSectionStateTests
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

        private static SnapshotItemEntry Held(int itemId, int count)
        {
            return new SnapshotItemEntry { ItemId = itemId, Count = count, Source = "Bank" };
        }

        private static PopoutSectionState WithRows(params PlanRowViewModel[] rows)
        {
            var state = new PopoutSectionState(PlanSectionType.ShoppingList);
            state.Adopt(new List<PlanRowViewModel>(rows), new Dictionary<int, int>());
            return state;
        }

        [Fact]
        public void SetChecked_MarksTheRowDoneAndLeavesItOnTheList()
        {
            var ore = Buy(MithrilOre, "Mithril Ore", 250, 5000);
            var wood = Buy(ElderWoodLog, "Elder Wood Log", 100, 900);
            var state = WithRows(ore, wood);

            state.SetChecked(ore, true);

            Assert.True(state.IsChecked(ore));
            Assert.False(state.IsChecked(wood));
            Assert.Equal(2, state.Rows.Count);
            Assert.Contains(ore, state.Rows);
        }

        [Fact]
        public void SetChecked_UntickingPutsTheRowBack()
        {
            var ore = Buy(MithrilOre, "Mithril Ore", 250, 5000);
            var state = WithRows(ore);

            state.SetChecked(ore, true);
            state.SetChecked(ore, false);

            Assert.False(state.IsChecked(ore));
            Assert.Equal(0, state.CheckedCount);
        }

        /// <summary>
        /// A tick belongs to a row, not to a position: the sort reorders the
        /// same instances, and the tick has to travel with the one it was
        /// put on.
        /// </summary>
        [Fact]
        public void IsChecked_FollowsTheRowAcrossASort()
        {
            var ore = Buy(MithrilOre, "Mithril Ore", 250, 5000);
            var wood = Buy(ElderWoodLog, "Elder Wood Log", 100, 900);
            var state = WithRows(ore, wood);
            state.SetChecked(wood, true);

            state.Sort.Cycle(PlanTableColumn.Amount);
            var sorted = PlanTableSorter.Sort(state.Rows, state.Sort);

            Assert.Same(wood, sorted[0]);
            Assert.True(state.IsChecked(sorted[0]));
            Assert.False(state.IsChecked(sorted[1]));
        }

        /// <summary>
        /// The tick column and the Shopping List renderer each sort the
        /// section for themselves, so the renderer sorts a list that is
        /// already in its own order. Sorting twice has to land where sorting
        /// once did, or a tick sits beside the wrong row. It does because
        /// the sort is stable and breaks ties on the original index, but
        /// nothing said so until this.
        /// </summary>
        [Fact]
        public void PlanTableSorter_SortingAnAlreadySortedTableChangesNothing()
        {
            var rows = new List<PlanRowViewModel>
            {
                Buy(MithrilOre, "Mithril Ore", 250, 5000),
                Buy(ElderWoodLog, "Elder Wood Log", 250, 900),
                Buy(24, "Thermocatalytic Reagent", 10, 900),
                Buy(19701, "Orichalcum Ore", 10, 20000),
            };

            foreach (PlanTableColumn column in Enum.GetValues(typeof(PlanTableColumn)))
            {
                var sort = new TableSortState<PlanTableColumn>();
                for (int click = 0; click < 3; click++)
                {
                    sort.Cycle(column);
                    var once = PlanTableSorter.Sort(rows, sort);
                    var twice = PlanTableSorter.Sort(once, sort);
                    Assert.Equal(once.Count, twice.Count);
                    for (int i = 0; i < once.Count; i++)
                    {
                        Assert.Same(once[i], twice[i]);
                    }
                }
            }
        }

        [Fact]
        public void IsChecked_TellsTwoRowsOfTheSameItemApart()
        {
            var first = Buy(MithrilOre, "Mithril Ore", 100, 2000);
            var second = Buy(MithrilOre, "Mithril Ore", 100, 2000);
            var state = WithRows(first, second);

            state.SetChecked(second, true);

            Assert.False(state.IsChecked(first));
            Assert.True(state.IsChecked(second));
        }

        [Fact]
        public void ApplyRefresh_ClearsEveryTick()
        {
            var ore = Buy(MithrilOre, "Mithril Ore", 250, 5000);
            var wood = Buy(ElderWoodLog, "Elder Wood Log", 100, 900);
            var state = WithRows(ore, wood);
            state.SetChecked(ore, true);
            state.SetChecked(wood, true);

            state.ApplyRefresh(new List<SnapshotItemEntry>());

            Assert.Equal(0, state.CheckedCount);
            Assert.Equal(2, state.Rows.Count);
            foreach (var row in state.Rows)
            {
                Assert.False(state.IsChecked(row));
            }
        }

        [Fact]
        public void ApplyRefresh_TakesOffTheRowsThePlayerNowOwns()
        {
            var ore = Buy(MithrilOre, "Mithril Ore", 250, 5000);
            var wood = Buy(ElderWoodLog, "Elder Wood Log", 100, 900);
            var state = WithRows(ore, wood);

            state.ApplyRefresh(new List<SnapshotItemEntry> { Held(MithrilOre, 250) });

            var left = Assert.Single(state.Rows);
            Assert.Equal("Elder Wood Log", left.Label);
        }

        [Fact]
        public void ApplyRefresh_MovesTheBaselineOnSoTheNextSyncMeasuresFromHere()
        {
            var ore = Buy(MithrilOre, "Mithril Ore", 250, 5000);
            var state = WithRows(ore);

            state.ApplyRefresh(new List<SnapshotItemEntry> { Held(MithrilOre, 100) });
            Assert.Equal(150, Assert.Single(state.Rows).Quantity);

            // The same holding again is no progress at all, so the row must
            // not shrink a second time off one purchase.
            state.ApplyRefresh(new List<SnapshotItemEntry> { Held(MithrilOre, 100) });
            Assert.Equal(150, Assert.Single(state.Rows).Quantity);

            state.ApplyRefresh(new List<SnapshotItemEntry> { Held(MithrilOre, 150) });
            Assert.Equal(100, Assert.Single(state.Rows).Quantity);
        }

        /// <summary>
        /// The failure path applies nothing at all. The window prints this
        /// line instead of a synced-at stamp, so an unchanged list can never
        /// read as a confirmed one.
        /// </summary>
        [Fact]
        public void RefreshFailedStatus_SaysTheListIsUnchangedAndNamesNoTime()
        {
            string status = PopoutSectionState.RefreshFailedStatus();

            Assert.Contains("failed", status, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("unchanged", status, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Synced", status, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void RefreshedStatus_NamesTheCaptureTimeSoAStaleWindowCannotReadAsLive()
        {
            string status = PopoutSectionState.RefreshedStatus(
                new DateTime(2026, 9, 9, 14, 5, 0, DateTimeKind.Local), 3);

            Assert.Contains("3 items left", status);
            Assert.Contains("14:05", status);
        }

        [Fact]
        public void Adopt_StartsAFreshPlanWithNoTicksAndNoSort()
        {
            var ore = Buy(MithrilOre, "Mithril Ore", 250, 5000);
            var state = WithRows(ore);
            state.SetChecked(ore, true);
            state.Sort.Cycle(PlanTableColumn.Total);

            var wood = Buy(ElderWoodLog, "Elder Wood Log", 100, 900);
            state.Adopt(new List<PlanRowViewModel> { wood }, new Dictionary<int, int>());

            Assert.Equal(0, state.CheckedCount);
            Assert.Equal(TableSortDirection.None, state.Sort.Direction);
            Assert.Same(wood, Assert.Single(state.Rows));
        }

        [Fact]
        public void SetChecked_IgnoresARowThisStateHasNeverSeen()
        {
            var state = WithRows(Buy(MithrilOre, "Mithril Ore", 250, 5000));

            state.SetChecked(Buy(ElderWoodLog, "Elder Wood Log", 1, 1), true);

            Assert.Equal(0, state.CheckedCount);
        }
    }
}
