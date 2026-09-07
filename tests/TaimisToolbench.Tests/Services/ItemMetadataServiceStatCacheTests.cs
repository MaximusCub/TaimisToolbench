using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using TaimisToolbench.Services.Recipes;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The stat side cache rides the metadata fetch the plan path already
    /// makes - it must never cost a request of its own, and must answer
    /// null (not an empty block) for anything nothing has fetched.
    /// </summary>
    public class ItemMetadataServiceStatCacheTests
    {
        private static RawItem Armor(int id)
        {
            return new RawItem
            {
                Id = id,
                Name = "Zojja's Warfists",
                Icon = "icon",
                Rarity = "Ascended",
                ItemType = "Armor",
                Level = 80,
                VendorValue = 240,
                Flags = new List<string> { "AccountBound", "AccountBindOnUse" },
                Restrictions = new List<string>(),
                Detail = new RawItemDetail
                {
                    SubType = "Gloves",
                    WeightClass = "Heavy",
                    Defense = 191,
                    InfusionSlots = new List<RawInfusionSlot>
                    {
                        new RawInfusionSlot { Flags = new List<string> { "Infusion" } },
                    },
                    InfixAttributes = new List<RawItemAttribute>
                    {
                        new RawItemAttribute { Attribute = "Power", Modifier = 47 },
                        new RawItemAttribute { Attribute = "CritDamage", Modifier = 34 },
                    },
                    StatChoiceIds = new List<int>(),
                    Bonuses = new List<string>(),
                },
            };
        }

        [Fact]
        public async Task StatBlocksArePopulatedByTheSameFetchThatPopulatesMetadata()
        {
            var api = new InMemoryItemApiClient();
            api.AddItem(Armor(48074));
            var service = new ItemMetadataService(api);

            await service.GetMetadataAsync(new[] { 48074 }, CancellationToken.None);

            // One request total - the stat data rode along in it.
            Assert.Single(api.Calls);

            var block = service.GetCachedStatBlock(48074);
            Assert.NotNull(block);
            Assert.Equal("Zojja's Warfists", block.Name);
            Assert.Equal(191, block.Defense);
            Assert.Equal(new[] { "Account Bound" }, block.Bindings);
            Assert.Equal(240L, block.VendorValue);
            Assert.Equal(
                new[] { "Power", "Ferocity" },
                new[] { block.Attributes[0].DisplayName, block.Attributes[1].DisplayName });
        }

        [Fact]
        public async Task ReadingAStatBlockNeverTriggersAFetch()
        {
            var api = new InMemoryItemApiClient();
            api.AddItem(Armor(48074));
            var service = new ItemMetadataService(api);

            Assert.Null(service.GetCachedStatBlock(48074));
            Assert.Empty(api.Calls);

            await service.GetMetadataAsync(new[] { 48074 }, CancellationToken.None);
            int callsAfterFetch = api.Calls.Count;

            Assert.NotNull(service.GetCachedStatBlock(48074));
            Assert.NotNull(service.GetCachedStatBlock(48074));
            Assert.Equal(callsAfterFetch, api.Calls.Count);
        }

        [Fact]
        public async Task AnItemMissingFromTheApiHasNoStatBlockRatherThanAnEmptyOne()
        {
            var api = new InMemoryItemApiClient();
            var service = new ItemMetadataService(api);

            await service.GetMetadataAsync(new[] { 12345 }, CancellationToken.None);

            Assert.Null(service.GetCachedStatBlock(12345));
        }

        // --- Q13: the restored-plan background top-up ---
        [Fact]
        public async Task WarmStatBlocks_FillsTheCacheForAPlanThatWasRestoredRatherThanGenerated()
        {
            var api = new InMemoryItemApiClient();
            api.AddItem(Armor(48074));
            var service = new ItemMetadataService(api);

            // A restore makes no network call at all, so the hover path
            // finds nothing.
            Assert.Null(service.GetCachedStatBlock(48074));

            int filled = await service.WarmStatBlocksAsync(new[] { 48074 }, CancellationToken.None);

            Assert.Equal(1, filled);
            Assert.Equal("Zojja's Warfists", service.GetCachedStatBlock(48074).Name);
        }

        [Fact]
        public async Task WarmStatBlocks_SkipsIdsThatAlreadyHaveABlock()
        {
            var api = new InMemoryItemApiClient();
            api.AddItem(Armor(48074));
            var service = new ItemMetadataService(api);

            await service.GetMetadataAsync(new[] { 48074 }, CancellationToken.None);
            int callsAfterFetch = api.Calls.Count;

            Assert.Equal(0, await service.WarmStatBlocksAsync(new[] { 48074 }, CancellationToken.None));
            Assert.Equal(callsAfterFetch, api.Calls.Count);
        }

        [Fact]
        public async Task WarmStatBlocks_IsBestEffort_AFailingBatchDoesNotThrow()
        {
            // Failing means the rows keep the plain tooltip they already
            // had, which is not an error worth surfacing.
            var api = new InMemoryItemApiClient { ThrowOnCallNumber = 1 };
            api.AddItem(Armor(48074));
            var service = new ItemMetadataService(api);

            Assert.Equal(0, await service.WarmStatBlocksAsync(new[] { 48074 }, CancellationToken.None));
            Assert.Null(service.GetCachedStatBlock(48074));
        }

        [Fact]
        public async Task WarmStatBlocks_DoesNotDisturbTheMetadataCacheItDoesNotOwn()
        {
            // Deliberately not GetMetadataAsync: that path writes the
            // unlocked metadata dictionary from the plan thread, and a
            // restore-time top-up racing a Generate would be two writers.
            var api = new InMemoryItemApiClient();
            api.AddItem(Armor(48074));
            var service = new ItemMetadataService(api);

            await service.WarmStatBlocksAsync(new[] { 48074 }, CancellationToken.None);
            int callsAfterWarm = api.Calls.Count;

            var metadata = await service.GetMetadataAsync(new[] { 48074 }, CancellationToken.None);

            Assert.Equal("Zojja's Warfists", metadata[48074].Name);
            Assert.True(api.Calls.Count > callsAfterWarm);
        }

        [Fact]
        public async Task SeedFallbackEntriesGetNoStatBlock()
        {
            // The bundled name seed carries name and icon only, so an item
            // served from it must not pretend to have stats.
            var api = new InMemoryItemApiClient();
            var seed = new ItemNameSeedData(new List<ItemNameEntry>
            {
                new ItemNameEntry { Id = 999, Name = "Seeded Thing", Icon = "icon" },
            });
            var service = new ItemMetadataService(api, seed);

            var metadata = await service.GetMetadataAsync(new[] { 999 }, CancellationToken.None);

            Assert.Equal("Seeded Thing", metadata[999].Name);
            Assert.Null(service.GetCachedStatBlock(999));
        }

        /// <summary>
        /// The restored-plan stat top-up is a background fetch that outlives
        /// the click that started it, and Module now runs it under the
        /// module lifetime token rather than CancellationToken.None. Unload
        /// cancels that token before it disposes the HttpClient every API
        /// client was built over, so the top-up has to actually stop when it
        /// is cancelled - not fetch on into disposed objects.
        /// </summary>
        [Fact]
        public async Task WarmStatBlocksStopsWhenItsTokenIsCancelled()
        {
            var api = new InMemoryItemApiClient();
            for (int id = 1; id <= 400; id++)
            {
                api.AddItem(Armor(id));
            }

            var service = new ItemMetadataService(api);
            using (var cts = new CancellationTokenSource())
            {
                cts.Cancel();

                await Assert.ThrowsAnyAsync<OperationCanceledException>(
                    () => service.WarmStatBlocksAsync(new[] { 1, 2, 3 }, cts.Token));
            }

            Assert.Empty(api.Calls);
            Assert.Null(service.GetCachedStatBlock(1));
        }
    }
}
