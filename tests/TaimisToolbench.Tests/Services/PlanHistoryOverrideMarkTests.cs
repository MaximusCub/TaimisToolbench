using System;
using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// A Plan History row's Cost is the cost at Generate. Moving a decision
    /// pill on the plan tab re-solves and moves the Total Cost table, and
    /// the row keeps its own number. These pin the mark that says so, and
    /// that the mark cannot cost the row its identity.
    /// <para>
    /// Real PlanHistoryStore against a real temp directory, so the mark is
    /// checked after a save and load rather than in memory only.
    /// </para>
    /// </summary>
    public class PlanHistoryOverrideMarkTests : IDisposable
    {
        private readonly TempDirectory _temp = new TempDirectory();

        public void Dispose()
        {
            _temp.Dispose();
        }

        private static PlanHistoryIndex IndexWithOneRow()
        {
            return new PlanHistoryIndex
            {
                Entries = new List<PlanHistoryEntry>
                {
                    new PlanHistoryEntry
                    {
                        EntryId = "0123456789abcdef0123456789abcdef",
                        CreatedAtUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
                        LastGeneratedAtUtc = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc),
                        RequestItems = new List<PlanRequestItem>
                        {
                            new PlanRequestItem { ItemId = 30684, Quantity = 1 },
                        },
                        UseOwnMaterials = true,
                        PriceBasis = PriceBasis.BuyOrder,
                        ValueOwnMaterials = false,
                        IgnoredItemIds = new List<int>(),
                        TotalCoinCostAtGeneration = 300,
                        OverrideCountAtGeneration = 0,
                        IgnoredCountAtGeneration = 0,
                    },
                },
            };
        }

        private static string KeyForTheGenerate()
        {
            return PlanHistoryDedupKey.Compute(
                new List<PlanRequestItem> { new PlanRequestItem { ItemId = 30684, Quantity = 1 } },
                useOwnMaterials: true,
                priceBasis: PriceBasis.BuyOrder,
                valueOwnMaterials: false,
                ignoredItemIds: null);
        }

        [Fact]
        public void TheRowIsFoundByTheKeyItsOwnGenerateWouldCompute()
        {
            var index = IndexWithOneRow();

            var entry = PlanHistoryIndexEdits.FindByDedupKey(index, KeyForTheGenerate());

            Assert.NotNull(entry);
            Assert.Equal("0123456789abcdef0123456789abcdef", entry.EntryId);
        }

        [Fact]
        public void MarkingSurvivesASaveAndReload()
        {
            var store = new PlanHistoryStore(_temp.Path);
            var index = IndexWithOneRow();
            var entry = PlanHistoryIndexEdits.FindByDedupKey(index, KeyForTheGenerate());

            Assert.True(PlanHistoryIndexEdits.MarkOverrides(entry, 2, 1));
            store.Save(index);

            var reloaded = store.Load();
            var row = reloaded.Entries[0];
            Assert.Equal(2, row.OverrideCountAtGeneration);
            Assert.Equal(1, row.IgnoredCountAtGeneration);

            // The record keeps the cost it was generated at.
            Assert.Equal(300L, row.TotalCoinCostAtGeneration);
        }

        [Fact]
        public void AMarkedRowIsStillFoundByTheNextGenerate()
        {
            // PlanHistoryDedupKey.ForEntry reads IgnoredItemIds. Writing
            // the ignored ids alongside the count would move the row's key
            // and leave the next Generate to create a duplicate.
            var index = IndexWithOneRow();
            var entry = PlanHistoryIndexEdits.FindByDedupKey(index, KeyForTheGenerate());
            PlanHistoryIndexEdits.MarkOverrides(entry, 3, 4);

            Assert.Same(entry, PlanHistoryIndexEdits.FindByDedupKey(index, KeyForTheGenerate()));
        }

        [Fact]
        public void MarkingTheSameCountsAgainReportsNoChange()
        {
            // Every pill click re-solves, and the index must not be written
            // to disk when nothing about the row moved.
            var index = IndexWithOneRow();
            var entry = PlanHistoryIndexEdits.FindByDedupKey(index, KeyForTheGenerate());

            Assert.True(PlanHistoryIndexEdits.MarkOverrides(entry, 1, 0));
            Assert.False(PlanHistoryIndexEdits.MarkOverrides(entry, 1, 0));
        }

        [Fact]
        public void ClearingEveryDecisionClearsTheMark()
        {
            var index = IndexWithOneRow();
            var entry = PlanHistoryIndexEdits.FindByDedupKey(index, KeyForTheGenerate());
            PlanHistoryIndexEdits.MarkOverrides(entry, 2, 2);

            Assert.True(PlanHistoryIndexEdits.MarkOverrides(entry, 0, 0));
            Assert.Equal(0, entry.OverrideCountAtGeneration);
            Assert.Equal(0, entry.IgnoredCountAtGeneration);
        }

        [Fact]
        public void NullsAndAMissingRowAreTolerated()
        {
            Assert.Null(PlanHistoryIndexEdits.FindByDedupKey(null, "k"));
            Assert.Null(PlanHistoryIndexEdits.FindByDedupKey(IndexWithOneRow(), null));
            Assert.Null(PlanHistoryIndexEdits.FindByDedupKey(IndexWithOneRow(), "not-a-key"));
            Assert.False(PlanHistoryIndexEdits.MarkOverrides(null, 1, 1));
        }
    }
}
