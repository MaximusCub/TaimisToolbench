using System;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class SnapshotFetchBudgetTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(10)]
        public void SmallRosters_KeepTheOldFlatAllowance(int characterCount)
        {
            Assert.Equal(SnapshotFetchBudget.Floor, SnapshotFetchBudget.For(characterCount));
        }

        [Fact]
        public void ANegativeCountIsTreatedAsNoCharacters()
        {
            Assert.Equal(SnapshotFetchBudget.Floor, SnapshotFetchBudget.For(-5));
            Assert.Equal(SnapshotFetchBudget.Floor, SnapshotFetchBudget.For(int.MinValue));
        }

        [Fact]
        public void TheBudgetGrowsWithTheRoster()
        {
            // The 14-character account that could not refresh at all under
            // the old flat 60s.
            Assert.Equal(TimeSpan.FromSeconds(72), SnapshotFetchBudget.For(14));
            Assert.True(SnapshotFetchBudget.For(30) > SnapshotFetchBudget.For(14));
        }

        [Theory]
        [InlineData(50)]
        [InlineData(60)]
        [InlineData(int.MaxValue)]
        public void ALargeRosterStopsAtTheCeiling(int characterCount)
        {
            Assert.Equal(SnapshotFetchBudget.Ceiling, SnapshotFetchBudget.For(characterCount));
        }
    }
}
