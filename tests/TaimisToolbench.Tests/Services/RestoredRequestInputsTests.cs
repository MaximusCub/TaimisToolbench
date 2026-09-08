using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // The restore -> inputs mapping (PersistedPlan -> what the input
    // strip's rows should show). Round-trip tests go through a REAL
    // PlanStore against a real temp directory, mirroring PlanStoreTests'
    // shape - the seeds must be buildable from exactly what LoadLatest
    // hands Module on restore, not from a hand-held in-memory plan.
    public class RestoredRequestInputsTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly PlanStore _store;

        public RestoredRequestInputsTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _store = new PlanStore(_tempDir);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
            }
        }

        private static PersistedPlan MultiItemPlan()
        {
            return new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                GeneratedAt = new DateTime(2026, 8, 26, 20, 0, 0, DateTimeKind.Local),
                RequestItems = new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 5, Quantity = 3 },
                    new PlanRequestItem { ItemId = 6, Quantity = 250 },
                },
                UseOwnMaterials = false,
                PriceBasis = PriceBasis.InstantBuy,
                Result = new CraftingPlanResult
                {
                    Plan = new CraftingPlan { TargetItemId = 5, TargetQuantity = 3 },
                    ItemMetadata = new Dictionary<int, ItemMetadata>
                    {
                        { 5, new ItemMetadata { ItemId = 5, Name = "Bolt of Gossamer" } },
                        { 6, new ItemMetadata { ItemId = 6, Name = "Cured Thick Leather Square" } },
                    },
                },
            };
        }

        [Fact]
        public void BuildRowSeeds_AfterRealStoreRoundTrip_RestoresMultiItemRequestWithQuantities()
        {
            _store.Save(MultiItemPlan());
            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);

            var seeds = RestoredRequestInputs.BuildRowSeeds(loaded.RequestItems, loaded.Result.ItemMetadata);

            Assert.Equal(2, seeds.Count);
            Assert.Equal(new[] { 5, 6 }, seeds.Select(s => s.ItemId));
            Assert.Equal(new[] { "Bolt of Gossamer", "Cured Thick Leather Square" }, seeds.Select(s => s.ItemName));
            Assert.Equal(new[] { "3", "250" }, seeds.Select(s => s.QuantityText));
        }

        [Fact]
        public void BuildRowSeeds_AfterRealStoreRoundTrip_NonDefaultTogglesSurviveAlongsideTheSeeds()
        {
            // The two settings the seeds travel with: the live checkbox
            // defaults true and the live dropdown defaults BuyOrder, so
            // both are persisted here in the OPPOSITE direction - a restore
            // wiring slip that fell back to the control's default would
            // pass a default-direction assertion.
            _store.Save(MultiItemPlan());
            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);

            Assert.False(loaded.UseOwnMaterials);
            Assert.Equal(PriceBasis.InstantBuy, loaded.PriceBasis);
            Assert.NotEmpty(RestoredRequestInputs.BuildRowSeeds(loaded.RequestItems, loaded.Result.ItemMetadata));
        }

        [Fact]
        public void BuildRowSeeds_AfterAResultOnlyLoss_StillNamesEveryRowFromTheRequest()
        {
            // The screen this exists for. A schema bump discards the
            // result, so the ItemMetadata the seeds normally read is gone -
            // and "Unnamed item x3" on the very screen that is trying to
            // say nothing was lost reads as breakage. PlanRequestItem.Name
            // is the request layer's own copy, written at Generate.
            var plan = MultiItemPlan();
            plan.RequestItems = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 5, Quantity = 3, Name = "Bolt of Gossamer" },
                new PlanRequestItem { ItemId = 6, Quantity = 250, Name = "Cured Thick Leather Square" },
            };
            _store.Save(plan);

            // Age the file below this build's readable range, through the
            // real store: exactly what a user upgrading across a bump that
            // moved PersistedPlan.MinimumReadableSchemaVersion has on disk.
            string path = Path.Combine(_tempDir, "plan.json");
            var onDisk = JObject.Parse(GzipJsonFile.DecompressToJson(File.ReadAllBytes(path)));
            onDisk["SchemaVersion"] = PersistedPlan.MinimumReadableSchemaVersion - 1;
            File.WriteAllBytes(path, GzipJsonFile.Compress(onDisk.ToString(Formatting.None)));

            var load = _store.LoadLatest();

            Assert.NotNull(load);
            Assert.False(load.HasResult);
            Assert.Null(load.Plan.Result);

            // No metadata to read - the null argument is the point.
            var seeds = RestoredRequestInputs.BuildRowSeeds(load.Plan.RequestItems, null);

            Assert.Equal(
                new[] { "Bolt of Gossamer", "Cured Thick Leather Square" },
                seeds.Select(s => s.ItemName));
            Assert.Equal(new[] { "3", "250" }, seeds.Select(s => s.QuantityText));
        }

        [Fact]
        public void BuildRowSeeds_RestoredMetadataWins_WhenBothItAndTheRequestNameAPI()
        {
            // The result's metadata is the fresher of the two (an item can
            // be renamed between the Generate that stamped the request and
            // the restore), so it is read first and the request's own copy
            // is the fallback, never an override.
            var plan = MultiItemPlan();
            plan.RequestItems = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 5, Quantity = 3, Name = "Stale Name" },
                new PlanRequestItem { ItemId = 6, Quantity = 250, Name = "Also Stale" },
            };
            _store.Save(plan);
            var loaded = _store.LoadLatest()?.Plan;

            var seeds = RestoredRequestInputs.BuildRowSeeds(loaded.RequestItems, loaded.Result.ItemMetadata);

            Assert.Equal(
                new[] { "Bolt of Gossamer", "Cured Thick Leather Square" },
                seeds.Select(s => s.ItemName));
        }

        [Fact]
        public void BuildRowSeeds_NameMissingFromRestoredMetadata_SeedsNullNameAndKeepsTheItem()
        {
            var plan = MultiItemPlan();
            // Item 6 loses its metadata entry entirely; item 5 keeps only a
            // whitespace name - both must fall back the same way.
            plan.Result.ItemMetadata = new Dictionary<int, ItemMetadata>
            {
                { 5, new ItemMetadata { ItemId = 5, Name = "   " } },
            };
            _store.Save(plan);
            var loaded = _store.LoadLatest()?.Plan;
            Assert.NotNull(loaded);

            var seeds = RestoredRequestInputs.BuildRowSeeds(loaded.RequestItems, loaded.Result.ItemMetadata);

            Assert.Equal(2, seeds.Count);
            Assert.All(seeds, s => Assert.Null(s.ItemName));
            Assert.Equal(new[] { 5, 6 }, seeds.Select(s => s.ItemId));
            Assert.Equal(new[] { "3", "250" }, seeds.Select(s => s.QuantityText));

            // The id stays internal-only: the placeholder an unnamed row
            // shows is a fixed neutral string with no room for an id to
            // leak through.
            Assert.False(string.IsNullOrWhiteSpace(RestoredRequestInputs.UnnamedRowPlaceholder));
            Assert.DoesNotContain(RestoredRequestInputs.UnnamedRowPlaceholder, c => char.IsDigit(c));
        }

        [Fact]
        public void BuildRowSeeds_NullMetadataDictionary_SeedsAllRowsUnnamed()
        {
            var seeds = RestoredRequestInputs.BuildRowSeeds(
                new List<PlanRequestItem> { new PlanRequestItem { ItemId = 5, Quantity = 2 } },
                null);

            var seed = Assert.Single(seeds);
            Assert.Equal(5, seed.ItemId);
            Assert.Null(seed.ItemName);
            Assert.Equal("2", seed.QuantityText);
        }

        [Fact]
        public void BuildRowSeeds_NullOrEmptyRequest_ReturnsEmptyNeverNull()
        {
            Assert.Empty(RestoredRequestInputs.BuildRowSeeds(null, null));
            Assert.Empty(RestoredRequestInputs.BuildRowSeeds(new List<PlanRequestItem>(), null));
        }

        [Fact]
        public void BuildRowSeeds_QuantityBelowOne_ClampsToOne()
        {
            // No shipped persist path writes a sub-1 quantity
            // (ItemRowRequestBuilder.Build clamps first), but a hand-edited
            // plan.json can - restore mirrors the same clamp instead of
            // seeding a quantity box Generate would then have to correct.
            var seeds = RestoredRequestInputs.BuildRowSeeds(
                new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 5, Quantity = 0 },
                    new PlanRequestItem { ItemId = 6, Quantity = -3 },
                },
                null);

            Assert.Equal(new[] { "1", "1" }, seeds.Select(s => s.QuantityText));
        }

        [Fact]
        public void BuildRowSeeds_NullEntryInRequestList_IsSkipped()
        {
            var seeds = RestoredRequestInputs.BuildRowSeeds(
                new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 5, Quantity = 1 },
                    null,
                },
                null);

            Assert.Equal(5, Assert.Single(seeds).ItemId);
        }
    }
}
