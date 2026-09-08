using System;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The seam that lets a Generate Plan click refresh the account without
    /// the user waiting for it twice over.
    /// </summary>
    public class CraftingPlanPipelineAccountRefreshTests
    {
        private static PipelineBuilder PricedCraft()
        {
            var builder = PipelineBuilder.SingleRecipeTree(5).WithInventoryReducer();
            builder.WithPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            builder.WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);
            return builder;
        }

        [Fact]
        public async Task AccountData_IsCollectedAfterThePricesAreFetched()
        {
            // What makes the overlap real: the pipeline does not touch the
            // account until the tree and the prices are done, so a refresh
            // started alongside them costs only what it outlasts.
            var builder = PricedCraft();
            int priceCallsWhenCollected = -1;

            var result = await builder.Build().GenerateStructuredAsync(
                1, 1, snapshot: null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy,
                accountDataAsync: () =>
                {
                    priceCallsWhenCollected = builder.PriceApi.Calls.Count;
                    return Task.FromResult(new PlanAccountData
                    {
                        Snapshot = PipelineBuilder.OwnIngredient(3),
                    });
                });

            Assert.True(priceCallsWhenCollected >= 1, "prices were still unfetched when the account was read");

            // The deferred snapshot drove the reduction even though the
            // by-value parameter was null.
            Assert.Single(result.UsedMaterials);
            Assert.Equal(3, result.UsedMaterials[0].QuantityUsed);
            Assert.Equal(200, result.Plan.TotalCoinCost);
        }

        [Fact]
        public async Task AFailedRefresh_StillProducesAPlanFromTheSnapshotOnDisk()
        {
            // What Module does when a refresh fails: it hands the pipeline
            // the snapshot already saved, and the plan is produced from it.
            using (var tmp = new TempDirectory())
            {
                var store = new SnapshotStore(tmp.Path);
                store.Save(PipelineBuilder.OwnIngredient(3));
                var onDisk = store.LoadLatest();
                Assert.NotNull(onDisk);

                var result = await PricedCraft().Build().GenerateStructuredAsync(
                    1, 1, snapshot: null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy,
                    accountDataAsync: () => Task.FromResult(new PlanAccountData
                    {
                        Snapshot = onDisk,
                        CharacterDisciplines = onDisk.CharacterDisciplines,
                    }));

                Assert.NotNull(result.Plan);
                Assert.Equal(200, result.Plan.TotalCoinCost);
                Assert.Single(result.UsedMaterials);
            }
        }

        [Fact]
        public async Task NoAccountDataAtAll_StillProducesAPlan()
        {
            // A first run whose only refresh failed has no snapshot to fall
            // back to. The plan is still produced, priced as if the account
            // owns none of it.
            var result = await PricedCraft().Build().GenerateStructuredAsync(
                1, 1, snapshot: null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy,
                accountDataAsync: () => Task.FromResult(new PlanAccountData()));

            Assert.NotNull(result.Plan);
            Assert.Equal(500, result.Plan.TotalCoinCost);
            Assert.Empty(result.UsedMaterials);
        }

        [Fact]
        public async Task DeferredDisciplines_ReachTheSolveWhenNonePassedByValue()
        {
            // Module now passes characterDisciplines: null and lets the
            // refresh supply them, so a plan generated after a successful
            // refresh reports the disciplines that refresh read.
            var result = await PricedCraft().Build().GenerateStructuredAsync(
                1, 1, snapshot: null, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy,
                characterDisciplines: null,
                accountDataAsync: () => Task.FromResult(new PlanAccountData
                {
                    CharacterDisciplines = new[]
                    {
                        new SnapshotCharacterDiscipline
                        {
                            CharacterName = "Taimi",
                            Discipline = "Weaponsmith",
                            Rating = 400,
                            Active = true,
                        },
                    },
                }));

            Assert.NotNull(result.CharacterDisciplines);
            Assert.Single(result.CharacterDisciplines);
            Assert.Equal("Weaponsmith", result.CharacterDisciplines[0].Discipline);
        }

        [Fact]
        public async Task ACancelledRefresh_FailsTheGenerationRatherThanSolvingWithoutIt()
        {
            // Cancellation is the one thing the refresh propagates: a
            // generation the user abandoned must not finish.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                PricedCraft().Build().GenerateStructuredAsync(
                    1, 1, snapshot: null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy,
                    accountDataAsync: () =>
                    {
                        var cancelled = new TaskCompletionSource<PlanAccountData>();
                        cancelled.SetCanceled();
                        return cancelled.Task;
                    }));
        }
    }
}
