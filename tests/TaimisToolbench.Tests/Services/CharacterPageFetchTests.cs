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
    /// The paged character fetch: how many pages a roster needs, and what
    /// the harvest says when a page does not come back.
    /// </summary>
    public class CharacterPageFetchTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 1)]
        [InlineData(3, 1)]
        [InlineData(4, 2)]
        [InlineData(6, 2)]
        [InlineData(7, 3)]
        [InlineData(14, 5)]
        public void PageCount_CoversTheRosterWithoutAnEmptyTrailingPage(
            int characters, int expected)
        {
            Assert.Equal(expected, CharacterPagePlan.PageCount(characters));
        }

        // An empty roster asks for no pages. /v2/characters already said the
        // account has none, and page 0 of nothing costs a request plus its
        // retries to be told the page is out of range.
        [Fact]
        public void PageCount_AsksForNothingWhenTheRosterIsEmpty()
        {
            Assert.Equal(0, CharacterPagePlan.PageCount(0));
            Assert.Equal(0, CharacterPagePlan.PageCount(-4));
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(9)]
        [InlineData(14)]
        [InlineData(70)]
        public void PageCount_TimesPageSize_HoldsTheWholeRoster(int characters)
        {
            int capacity = CharacterPagePlan.PageCount(characters) * CharacterPagePlan.PageSize;

            Assert.True(capacity >= characters);
            Assert.True(capacity - characters < CharacterPagePlan.PageSize);
        }

        // Fanning out wider than the socket ceiling only queues the extra
        // requests, so the two bounds have to stay in that order.
        [Fact]
        public void PagesInFlight_FitInsideTheConnectionLimit()
        {
            Assert.True(CharacterPagePlan.MaxPagesInFlight >= 1);
            Assert.True(
                CharacterPagePlan.MaxPagesInFlight <= Gw2ApiConnectionLimit.ConnectionsPerServer);
        }

        // The GW2 API caps a bulk page at 200, so a larger page size would
        // silently return fewer characters than the page count assumes.
        [Fact]
        public void PageSize_StaysWithinTheBulkLimitTheApiAllows()
        {
            Assert.True(CharacterPagePlan.PageSize >= 1);
            Assert.True(CharacterPagePlan.PageSize <= 200);
        }

        // A page that fails leaves its characters out of the projected map,
        // and the fetch hands the fold a null part for each of them. This is
        // the shape Gw2AccountSnapshotService.PartFor produces, run through
        // the real collector.
        [Fact]
        public async Task AFailedPage_MarksEveryCharacterItCarriedIncomplete()
        {
            var roster = new[] { "Ayn", "Bex", "Cyd", "Dov", "Eir", "Fen" };
            var delivered = Delivered(roster.Take(3));

            var harvest = await CollectAsync(roster, delivered);

            Assert.False(harvest.IsComplete);
            Assert.Equal(3, harvest.IncompleteCharacterCount);
            Assert.Equal(new[] { "Dov", "Eir", "Fen" }, harvest.IncompleteCharacterNames);
            Assert.Equal(roster.Length, harvest.CharacterCount);
        }

        // The disciplines rule is all or nothing: a partial list would read
        // as an affirmative "not trained" claim for the characters missing
        // from it.
        [Fact]
        public async Task AFailedPage_DiscardsEveryCharactersDisciplines()
        {
            var roster = new[] { "Ayn", "Bex", "Cyd", "Dov" };
            var delivered = Delivered(roster.Take(3));

            var harvest = await CollectAsync(roster, delivered);

            Assert.Null(harvest.Disciplines);
        }

        // The rows a surviving page delivered are still real, so they stay
        // in the harvest. What stops the commit is IsComplete, not an empty
        // harvest.
        [Fact]
        public async Task AFailedPage_KeepsTheRowsTheOtherPagesDelivered()
        {
            var roster = new[] { "Ayn", "Bex", "Cyd", "Dov" };
            var delivered = Delivered(roster.Take(3));

            var harvest = await CollectAsync(roster, delivered);

            Assert.Equal(3, harvest.Items.Count);
            Assert.Equal(
                new[] { "Ayn", "Bex", "Cyd" },
                harvest.Items.Select(i => i.Source).ToArray());
        }

        [Fact]
        public async Task EveryPageArriving_LeavesTheHarvestComplete()
        {
            var roster = new[] { "Ayn", "Bex", "Cyd", "Dov" };

            var harvest = await CollectAsync(roster, Delivered(roster));

            Assert.True(harvest.IsComplete);
            Assert.Equal(0, harvest.IncompleteCharacterCount);
            Assert.NotNull(harvest.Disciplines);
            Assert.Equal(roster.Length, harvest.Disciplines.Count);
        }

        // Every page failing is the whole roster missing, which is the case
        // the refusal exists for.
        [Fact]
        public async Task EveryPageFailing_LeavesNothingCommittable()
        {
            var roster = new[] { "Ayn", "Bex", "Cyd" };

            var harvest = await CollectAsync(roster, new Dictionary<string, CharacterSnapshotPart>());

            Assert.False(harvest.IsComplete);
            Assert.Equal(roster.Length, harvest.IncompleteCharacterCount);
            Assert.Empty(harvest.Items);
            Assert.Null(harvest.Disciplines);
        }

        private static Task<CharacterSnapshotHarvest> CollectAsync(
            IReadOnlyList<string> roster, Dictionary<string, CharacterSnapshotPart> delivered)
        {
            return CharacterSnapshotCollector.CollectAsync(
                roster,
                Math.Max(1, roster.Count),
                name =>
                {
                    CharacterSnapshotPart part;
                    delivered.TryGetValue(name, out part);
                    return Task.FromResult(part);
                },
                CancellationToken.None);
        }

        private static Dictionary<string, CharacterSnapshotPart> Delivered(
            IEnumerable<string> names)
        {
            var delivered = new Dictionary<string, CharacterSnapshotPart>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                var part = new CharacterSnapshotPart();
                part.Items.Add(new SnapshotItemEntry { ItemId = 1, Count = 1, Source = name });
                part.Disciplines.Add(new SnapshotCharacterDiscipline
                {
                    CharacterName = name,
                    Discipline = "Armorsmith",
                    Rating = 500,
                    Active = true,
                });
                delivered[name] = part;
            }

            return delivered;
        }
    }
}
