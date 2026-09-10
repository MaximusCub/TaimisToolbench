using System;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class SnapshotRefreshPolicyTests
    {
        private static readonly DateTime Now = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void TheWindowIsFifteenSeconds()
        {
            Assert.Equal(TimeSpan.FromSeconds(15), SnapshotRefreshPolicy.TabOpenFreshness);
        }

        [Fact]
        public void NoSnapshotAtAll_Refreshes()
        {
            Assert.True(SnapshotRefreshPolicy.ShouldRefreshOnTabOpen(null, Now));
        }

        [Fact]
        public void DataInsideTheWindow_IsLeftAlone()
        {
            var captured = Now - SnapshotRefreshPolicy.TabOpenFreshness + TimeSpan.FromSeconds(1);
            Assert.False(SnapshotRefreshPolicy.ShouldRefreshOnTabOpen(captured, Now));
        }

        [Fact]
        public void DataOnTheWindowBoundary_Refreshes()
        {
            var captured = Now - SnapshotRefreshPolicy.TabOpenFreshness;
            Assert.True(SnapshotRefreshPolicy.ShouldRefreshOnTabOpen(captured, Now));
        }

        [Fact]
        public void DataOlderThanTheWindow_Refreshes()
        {
            Assert.True(SnapshotRefreshPolicy.ShouldRefreshOnTabOpen(Now.AddMinutes(-3), Now));
        }

        [Fact]
        public void AFutureTimestamp_IsLeftAlone()
        {
            // Clock skew, not freshness. Refreshing on it would fire on
            // every tab switch for as long as the skew lasts.
            Assert.False(SnapshotRefreshPolicy.ShouldRefreshOnTabOpen(Now.AddMinutes(5), Now));
        }

        // ---- Generate Plan's own window ----
        [Fact]
        public void TheGenerateWindowIsOneMinute()
        {
            Assert.Equal(TimeSpan.FromSeconds(60), SnapshotRefreshPolicy.GenerateFreshness);
        }

        [Fact]
        public void GenerateWithNoSnapshotAtAll_Refreshes()
        {
            Assert.True(SnapshotRefreshPolicy.ShouldRefreshOnGenerate(null, Now));
        }

        [Fact]
        public void GenerateOnTheWindowBoundary_Refreshes()
        {
            var captured = Now - SnapshotRefreshPolicy.GenerateFreshness;
            Assert.True(SnapshotRefreshPolicy.ShouldRefreshOnGenerate(captured, Now));
        }

        [Fact]
        public void GenerateOverAFutureTimestamp_IsLeftAlone()
        {
            Assert.False(SnapshotRefreshPolicy.ShouldRefreshOnGenerate(Now.AddMinutes(5), Now));
        }

        /// <summary>
        /// The sequence the guard exists for. A press over old data
        /// refreshes, which restamps the snapshot; presses over that stamp
        /// for the rest of the minute do not. Driven as the module drives
        /// it - each decision reads the stamp the previous refresh left.
        /// </summary>
        [Fact]
        public void FivePressesInsideOneMinute_RefreshOnce()
        {
            DateTime? captured = Now.AddMinutes(-10);
            int refreshes = 0;

            for (int press = 0; press < 5; press++)
            {
                var pressedAt = Now.AddSeconds(press * 10);
                if (SnapshotRefreshPolicy.ShouldRefreshOnGenerate(captured, pressedAt))
                {
                    refreshes++;
                    captured = pressedAt;
                }
            }

            Assert.Equal(1, refreshes);
        }

        [Fact]
        public void TwoPressesAMinuteApart_RefreshTwice()
        {
            DateTime? captured = Now.AddMinutes(-10);
            int refreshes = 0;

            foreach (var pressedAt in new[] { Now, Now.AddSeconds(60) })
            {
                if (SnapshotRefreshPolicy.ShouldRefreshOnGenerate(captured, pressedAt))
                {
                    refreshes++;
                    captured = pressedAt;
                }
            }

            Assert.Equal(2, refreshes);
        }

        /// <summary>
        /// A press one second short of the window still uses what is
        /// loaded, which is the half of the rule a boundary test alone
        /// leaves open.
        /// </summary>
        [Fact]
        public void GenerateOneSecondInsideTheWindow_IsLeftAlone()
        {
            var captured = Now - SnapshotRefreshPolicy.GenerateFreshness + TimeSpan.FromSeconds(1);
            Assert.False(SnapshotRefreshPolicy.ShouldRefreshOnGenerate(captured, Now));
        }

        /// <summary>
        /// The two triggers are separate rules and must not be collapsed
        /// into one. Data 30 seconds old is stale to the Snapshot tab and
        /// fresh to Generate Plan, which is the whole gap between them.
        /// </summary>
        [Fact]
        public void ThirtySecondOldData_RefreshesTheTabButNotAPlan()
        {
            var captured = Now.AddSeconds(-30);
            Assert.True(SnapshotRefreshPolicy.ShouldRefreshOnTabOpen(captured, Now));
            Assert.False(SnapshotRefreshPolicy.ShouldRefreshOnGenerate(captured, Now));
        }

        [Fact]
        public void The_api_wait_budget_is_what_is_left_of_the_grace_since_load()
        {
            var loaded = new DateTime(2026, 9, 10, 3, 2, 10, DateTimeKind.Utc);

            Assert.Equal(
                TimeSpan.FromSeconds(21),
                SnapshotRefreshPolicy.ApiWaitBudget(loaded, loaded.AddSeconds(9)));
        }

        [Fact]
        public void The_api_wait_budget_runs_out_and_stays_out()
        {
            var loaded = new DateTime(2026, 9, 10, 3, 2, 10, DateTimeKind.Utc);

            Assert.Equal(TimeSpan.Zero, SnapshotRefreshPolicy.ApiWaitBudget(loaded, loaded.AddSeconds(30)));
            Assert.Equal(TimeSpan.Zero, SnapshotRefreshPolicy.ApiWaitBudget(loaded, loaded.AddHours(5)));
        }

        [Fact]
        public void A_load_stamp_in_the_future_yields_the_whole_grace_and_no_more()
        {
            var loaded = new DateTime(2026, 9, 10, 3, 2, 10, DateTimeKind.Utc);

            Assert.Equal(
                SnapshotRefreshPolicy.ApiReadyGrace,
                SnapshotRefreshPolicy.ApiWaitBudget(loaded, loaded.AddMinutes(-5)));
        }
    }
}
