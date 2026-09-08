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
    }
}
