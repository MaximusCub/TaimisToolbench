using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The snapshot fetch runs several characters at once. These tests pin
    /// the two rules that survive that: a failed character costs only its
    /// own items, and a failed character costs every character's crafting
    /// disciplines.
    /// </summary>
    public class CharacterSnapshotCollectorTests
    {
        private static readonly string[] FourNames = { "Ayn", "Bex", "Cyd", "Dov" };

        [Fact]
        public async Task NeverFetchesMoreCharactersThanTheBound()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            int inFlight = 0;
            int observedMax = 0;

            Task<CharacterSnapshotHarvest> run = CharacterSnapshotCollector.CollectAsync(
                FourNames,
                2,
                async name =>
                {
                    int now = Interlocked.Increment(ref inFlight);
                    RaiseMax(ref observedMax, now);
                    await gate.Task;
                    Interlocked.Decrement(ref inFlight);
                    return PartFor(name);
                },
                CancellationToken.None);

            // CollectAsync starts its whole task list synchronously, so the
            // bound is already reached the instant it hands the Task back.
            Assert.Equal(2, Volatile.Read(ref inFlight));

            gate.SetResult(true);
            var harvest = await run;

            Assert.Equal(2, Volatile.Read(ref observedMax));
            Assert.Equal(4, harvest.Items.Count);
        }

        [Fact]
        public async Task OneFailingCharacterStillLeavesTheOthersCollected()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name => name == "Bex"
                    ? ThrowAsync("Bex")
                    : Task.FromResult(PartFor(name)),
                CancellationToken.None);

            Assert.Equal(
                new[] { "Ayn", "Cyd", "Dov" },
                harvest.Items.Select(i => i.Source).ToArray());
            Assert.Equal(
                new[] { "Ayn", "Cyd", "Dov" },
                harvest.ArmoryEquipped.Select(a => a.CharacterName).ToArray());
        }

        [Fact]
        public async Task EveryCharacterFailing_StillReturnsAnEmptyHarvestRatherThanThrowing()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name => ThrowAsync(name),
                CancellationToken.None);

            Assert.Empty(harvest.Items);
            Assert.Empty(harvest.ArmoryEquipped);
            Assert.Null(harvest.Disciplines);
        }

        [Fact]
        public async Task AllCharactersSucceeding_KeepsEveryDiscipline()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                2,
                name => Task.FromResult(PartFor(name)),
                CancellationToken.None);

            Assert.NotNull(harvest.Disciplines);
            Assert.Equal(FourNames, harvest.Disciplines.Select(d => d.CharacterName).ToArray());
        }

        [Fact]
        public async Task OneReportedDisciplineFailure_DiscardsEveryCharactersDisciplines()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name =>
                {
                    var part = PartFor(name);
                    if (name == "Cyd")
                    {
                        part.Disciplines.Clear();
                        part.DisciplinesDegraded = true;
                    }

                    return Task.FromResult(part);
                },
                CancellationToken.None);

            // The three that answered are still not a list: it would read as
            // an affirmative "not trained" claim for Cyd.
            Assert.Null(harvest.Disciplines);

            // Items are the opposite rule - Cyd's still count.
            Assert.Equal(4, harvest.Items.Count);
        }

        [Fact]
        public async Task ConcurrentFailuresStillDiscardEveryDiscipline()
        {
            // Two characters fail two different ways at the same time: one
            // throws, one reports Degraded. The bound lets all four run at
            // once, so the two failures land on different threads.
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Task<CharacterSnapshotHarvest> run = CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                async name =>
                {
                    await release.Task;
                    if (name == "Ayn")
                    {
                        throw new InvalidOperationException("Ayn");
                    }

                    var part = PartFor(name);
                    if (name == "Dov")
                    {
                        part.Disciplines.Clear();
                        part.DisciplinesDegraded = true;
                    }

                    return part;
                },
                CancellationToken.None);

            release.SetResult(true);
            var harvest = await run;

            Assert.Null(harvest.Disciplines);
            Assert.Equal(new[] { "Bex", "Cyd", "Dov" }, harvest.Items.Select(i => i.Source).ToArray());
        }

        [Fact]
        public async Task ResultsFollowTheCharacterListEvenWhenTheFetchesFinishBackwards()
        {
            var gates = FourNames
                .Select(_ => new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously))
                .ToList();

            Task<CharacterSnapshotHarvest> run = CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                async name =>
                {
                    await gates[Array.IndexOf(FourNames, name)].Task;
                    return PartFor(name);
                },
                CancellationToken.None);

            for (int i = gates.Count - 1; i >= 0; i--)
            {
                gates[i].SetResult(true);
            }

            var harvest = await run;

            Assert.Equal(FourNames, harvest.Items.Select(i => i.Source).ToArray());
            Assert.Equal(FourNames, harvest.ArmoryEquipped.Select(a => a.CharacterName).ToArray());
            Assert.Equal(FourNames, harvest.Disciplines.Select(d => d.CharacterName).ToArray());
        }

        [Fact]
        public async Task NoCharacters_ReportsAnEmptyDisciplineListRatherThanNull()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                new string[0],
                4,
                name => Task.FromResult(PartFor(name)),
                CancellationToken.None);

            Assert.NotNull(harvest.Disciplines);
            Assert.Empty(harvest.Disciplines);
            Assert.Empty(harvest.Items);
        }

        [Fact]
        public async Task CancellingWhileCharactersAreParked_SurfacesTheCancellationInsteadOfHanging()
        {
            var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            using (var cts = new CancellationTokenSource())
            {
                Task<CharacterSnapshotHarvest> run = CharacterSnapshotCollector.CollectAsync(
                    FourNames,
                    1,
                    async name =>
                    {
                        await gate.Task;
                        cts.Token.ThrowIfCancellationRequested();
                        return PartFor(name);
                    },
                    cts.Token);

                cts.Cancel();
                gate.SetResult(true);

                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
            }
        }

        [Fact]
        public async Task NullFetchDelegate_ThrowsRatherThanReturningAnEmptyHarvest()
        {
            var ex = await Assert.ThrowsAsync<ArgumentNullException>(
                () => CharacterSnapshotCollector.CollectAsync(FourNames, 4, null, CancellationToken.None));

            Assert.Equal("fetchCharacter", ex.ParamName);
        }

        // ---- IncompleteCharacterCount: the snapshot has to say it is
        // missing holdings, not only be missing them. An under-count is
        // conservative for cost and wrong for advice - it makes the plan
        // recommend buying an item the account already holds.
        [Fact]
        public async Task AllCharactersSucceeding_ReportsNoneIncomplete()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name => Task.FromResult(PartFor(name)),
                CancellationToken.None);

            Assert.Equal(4, harvest.CharacterCount);
            Assert.Equal(0, harvest.IncompleteCharacterCount);
        }

        [Fact]
        public async Task DegradedItems_CountAsIncompleteButKeepWhatWasRead()
        {
            // The bags that DID answer are real holdings, so they stay.
            // What changes is that the harvest admits Bex is short.
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name =>
                {
                    var part = PartFor(name);
                    if (name == "Bex")
                    {
                        part.ItemsDegraded = true;
                    }

                    return Task.FromResult(part);
                },
                CancellationToken.None);

            Assert.Equal(4, harvest.CharacterCount);
            Assert.Equal(1, harvest.IncompleteCharacterCount);
            Assert.Equal(
                new[] { "Ayn", "Bex", "Cyd", "Dov" },
                harvest.Items.Select(i => i.Source).ToArray());

            // Bags are tolerated one at a time, so disciplines survive.
            Assert.NotNull(harvest.Disciplines);
        }

        [Fact]
        public async Task DegradedDisciplines_CountAsIncompleteToo()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name =>
                {
                    var part = PartFor(name);
                    if (name == "Cyd")
                    {
                        part.DisciplinesDegraded = true;
                    }

                    return Task.FromResult(part);
                },
                CancellationToken.None);

            Assert.Equal(1, harvest.IncompleteCharacterCount);
            Assert.Null(harvest.Disciplines);
        }

        [Fact]
        public async Task BothFaultsOnOneCharacter_CountThatCharacterOnce()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name =>
                {
                    var part = PartFor(name);
                    if (name == "Dov")
                    {
                        part.ItemsDegraded = true;
                        part.DisciplinesDegraded = true;
                    }

                    return Task.FromResult(part);
                },
                CancellationToken.None);

            Assert.Equal(1, harvest.IncompleteCharacterCount);
        }

        [Fact]
        public async Task AThrowingCharacterCountsAsIncomplete()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name => name == "Bex" ? ThrowAsync("Bex") : Task.FromResult(PartFor(name)),
                CancellationToken.None);

            Assert.Equal(4, harvest.CharacterCount);
            Assert.Equal(1, harvest.IncompleteCharacterCount);
        }

        [Fact]
        public async Task EveryCharacterFailing_ReportsEveryOneIncomplete()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                FourNames,
                4,
                name => ThrowAsync(name),
                CancellationToken.None);

            Assert.Equal(4, harvest.CharacterCount);
            Assert.Equal(4, harvest.IncompleteCharacterCount);
        }

        [Fact]
        public async Task NoCharacters_ReportsNeitherACountNorAFault()
        {
            var harvest = await CharacterSnapshotCollector.CollectAsync(
                new string[0],
                4,
                name => Task.FromResult(PartFor(name)),
                CancellationToken.None);

            Assert.Equal(0, harvest.CharacterCount);
            Assert.Equal(0, harvest.IncompleteCharacterCount);
        }

        private static Task<CharacterSnapshotPart> ThrowAsync(string name)
        {
            var failed = new TaskCompletionSource<CharacterSnapshotPart>();
            failed.SetException(new InvalidOperationException(name));
            return failed.Task;
        }

        /// <summary>
        /// One character carrying one of everything the collector folds, all
        /// tagged with the character's own name so a test can read the order
        /// straight off the result.
        /// </summary>
        private static CharacterSnapshotPart PartFor(string name)
        {
            var part = new CharacterSnapshotPart();
            part.Items.Add(new SnapshotItemEntry { ItemId = 1, Count = 1, Source = name });
            part.ArmoryItemIds.Add(100);
            part.Disciplines.Add(new SnapshotCharacterDiscipline
            {
                CharacterName = name,
                Discipline = "Armorsmith",
                Rating = 500,
                Active = true,
            });
            return part;
        }

        private static void RaiseMax(ref int max, int candidate)
        {
            int seen = Volatile.Read(ref max);
            while (candidate > seen)
            {
                int previous = Interlocked.CompareExchange(ref max, candidate, seen);
                if (previous == seen)
                {
                    return;
                }

                seen = previous;
            }
        }
    }
}
