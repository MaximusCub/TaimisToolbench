using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The golden corpus behind the compatibility contract in
    /// docs/ARCHITECTURE.md section 12: a file written by ANY shipped
    /// build must still restore the user's request in every later build.
    /// Every fixture under tests/shared/plan_fixtures/ goes through the
    /// real PlanStore, the real gzip container and the real deserializer -
    /// there is no second reader here to keep in step.
    /// <para>
    /// What each fixture must produce is read back out of the fixture
    /// itself with a plain JObject, so nothing about the request layer is
    /// restated in C#. The assertion is "the loader surfaces exactly what
    /// the bytes say", which a hand-maintained expectations file could
    /// only weaken.
    /// </para>
    /// </summary>
    public class PlanCompatibilityFixtureTests : IDisposable
    {
        // Named once here and once in the "Saved plans from older builds
        // still load" step of .github/workflows/tests.yml, which enforces
        // the corpus is COMPLETE without needing a Windows build.
        private const string FixtureDirRelativePath = "tests/shared/plan_fixtures";

        private readonly string _tempDir;

        public PlanCompatibilityFixtureTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "TaimisToolbench_Tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
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

        public static IEnumerable<object[]> PlanFixtures()
        {
            return Directory.GetFiles(FixtureDir(), "plan-v*.json")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new object[] { name });
        }

        public static IEnumerable<object[]> PlanHistoryIndexFixtures()
        {
            return Directory.GetFiles(FixtureDir(), "plan-history-index-v*.json")
                .Select(Path.GetFileName)
                .OrderBy(name => name, StringComparer.Ordinal)
                .Select(name => new object[] { name });
        }

        [Theory]
        [MemberData(nameof(PlanFixtures))]
        public void SavedPlanFixture_RestoresItsRequestAndGivesTheRightResultVerdict(string fixtureName)
        {
            string json = File.ReadAllText(Path.Combine(FixtureDir(), fixtureName));
            var onDisk = JObject.Parse(json);

            // Through the gzip container Save writes today, not the plain
            // bytes on disk: the fixtures are checked in uncompressed so a
            // reviewer can read the diff, and LoadLatest's magic-number
            // sniff means both forms are the same supported file.
            File.WriteAllBytes(Path.Combine(_tempDir, "plan.json"), GzipJsonFile.Compress(json));

            string info = null;
            string error = null;
            var store = new PlanStore(_tempDir, (message, ex) => error = message, message => info = message);

            var load = store.LoadLatest();

            Assert.True(load != null, fixtureName + " no longer restores anything at all.");
            AssertRequestLayerMatches(fixtureName, onDisk, load.Plan);

            int fixtureVersion = onDisk.Value<int>("SchemaVersion");
            bool inReadableRange =
                fixtureVersion >= PersistedPlan.MinimumReadableSchemaVersion
                && fixtureVersion <= PersistedPlan.CurrentSchemaVersion;

            if (inReadableRange)
            {
                Assert.True(load.HasResult,
                    fixtureName + " is stamped at schema " + fixtureVersion
                    + ", inside this build's readable range, and must load whole; "
                    + "the result was discarded instead: " + (info ?? error));
                Assert.NotNull(load.Plan.Result.Plan);
            }
            else
            {
                Assert.False(load.HasResult,
                    fixtureName + " is stamped at schema " + fixtureVersion
                    + ", outside this build's readable range, so its result must be "
                    + "discarded unread.");
                Assert.Null(load.Plan.Result);
            }
        }

        [Theory]
        [MemberData(nameof(PlanHistoryIndexFixtures))]
        public void PlanHistoryIndexFixture_StillLoadsEveryRow(string fixtureName)
        {
            string json = File.ReadAllText(Path.Combine(FixtureDir(), fixtureName));
            var onDisk = JObject.Parse(json);
            File.WriteAllText(Path.Combine(_tempDir, "plan_history.json"), json);

            string error = null;
            var index = new PlanHistoryStore(_tempDir, (message, ex) => error = message).Load();

            var expectedRows = (JArray)onDisk["Entries"];
            Assert.True(expectedRows.Count == index.Entries.Count,
                fixtureName + " lost rows on load (" + error + ").");
            for (int i = 0; i < expectedRows.Count; i++)
            {
                Assert.Equal(expectedRows[i].Value<string>("EntryId"), index.Entries[i].EntryId);
                Assert.Equal(
                    ((JArray)expectedRows[i]["RequestItems"]).Count,
                    index.Entries[i].RequestItems.Count);
            }
        }

        [Fact]
        public async Task CurrentSchemaVersions_HaveAFixture_CapturingOneIfMissing()
        {
            var captured = new List<string>();
            string planFixture = "plan-v" + PersistedPlan.CurrentSchemaVersion + ".json";
            if (!File.Exists(Path.Combine(FixtureDir(), planFixture)))
            {
                await CaptureCurrentPlanFixtureAsync(planFixture);
                captured.Add(planFixture);
            }

            string indexFixture = "plan-history-index-v" + PlanHistoryIndex.CurrentSchemaVersion + ".json";
            if (!File.Exists(Path.Combine(FixtureDir(), indexFixture)))
            {
                CaptureCurrentPlanHistoryIndexFixture(indexFixture);
                captured.Add(indexFixture);
            }

            Assert.True(captured.Count == 0,
                "A schema version shipped with no golden fixture, so one was just captured from the "
                + "real serializer: " + string.Join(", ", captured) + " under " + FixtureDirRelativePath
                + ". Review the diff and `git add` it in the same commit as the bump - that file is what "
                + "proves the NEXT build can still restore plans written by this one.");

            for (int version = 1; version <= PersistedPlan.CurrentSchemaVersion; version++)
            {
                string name = "plan-v" + version + ".json";
                Assert.True(File.Exists(Path.Combine(FixtureDir(), name)),
                    "Schema version " + version + " shipped but " + FixtureDirRelativePath + "/" + name
                    + " is gone. A fixture is only ever added, never removed: deleting one retires a "
                    + "promise to the users still running that build.");
            }
        }

        [Fact]
        public void EveryPersistedPlanMember_BelongsToExactlyOneLayer()
        {
            string[] declared = typeof(PersistedPlan)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

            var classified = new List<string>();
            classified.AddRange(PlanStoreHelpers.VersionMembers);
            classified.AddRange(PlanStoreHelpers.RequestLayerMembers);
            classified.AddRange(PlanStoreHelpers.ResultGraphMembers);

            // Distinct() would hide a member listed in two layers, which is
            // the more dangerous of the two mistakes: the request read
            // would skip a member the restore then expects to have.
            Assert.Equal(
                string.Join("\n", declared),
                string.Join("\n", classified.OrderBy(name => name, StringComparer.Ordinal)));
        }

        private static void AssertRequestLayerMatches(string fixtureName, JObject onDisk, PersistedPlan plan)
        {
            var expectedItems = (JArray)onDisk["RequestItems"];
            Assert.True(expectedItems.Count == (plan.RequestItems?.Count ?? 0),
                fixtureName + " lost request rows: " + expectedItems.Count + " on disk, "
                + (plan.RequestItems?.Count ?? 0) + " restored.");
            for (int i = 0; i < expectedItems.Count; i++)
            {
                Assert.Equal(expectedItems[i].Value<int>("ItemId"), plan.RequestItems[i].ItemId);
                Assert.Equal(expectedItems[i].Value<int>("Quantity"), plan.RequestItems[i].Quantity);
            }

            Assert.Equal(onDisk.Value<DateTime>("GeneratedAt"), plan.GeneratedAt);
            Assert.Equal(onDisk.Value<bool>("UseOwnMaterials"), plan.UseOwnMaterials);
            Assert.Equal(onDisk.Value<bool>("ValueOwnMaterials"), plan.ValueOwnMaterials);
            Assert.Equal(onDisk["PriceBasis"].ToObject<PriceBasis>(), plan.PriceBasis);

            var expectedIgnored = (JArray)onDisk["IgnoredItemIds"] ?? new JArray();
            Assert.Equal(
                expectedIgnored.Select(token => (int)token).ToArray(),
                (plan.IgnoredItemIds ?? new int[0]).ToArray());
        }

        private static async Task CaptureCurrentPlanFixtureAsync(string fixtureName)
        {
            var plan = await BuildRealPlanAsync();
            string json = PlanStoreHelpers.SerializePersistedPlan(plan);
            WriteFixture(fixtureName, JObject.Parse(json).ToString(Formatting.Indented));
        }

        private static void CaptureCurrentPlanHistoryIndexFixture(string fixtureName)
        {
            string dir = Path.Combine(Path.GetTempPath(), "TaimisToolbench_Capture_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                new PlanHistoryStore(dir).Save(new PlanHistoryIndex
                {
                    SchemaVersion = PlanHistoryIndex.CurrentSchemaVersion,
                    Entries = new List<PlanHistoryEntry> { BuildRealHistoryEntry() },
                });
                WriteFixture(fixtureName, File.ReadAllText(Path.Combine(dir, "plan_history.json")));
            }
            finally
            {
                try
                {
                    Directory.Delete(dir, true);
                }
                catch
                {
                }
            }
        }

        private static void WriteFixture(string fixtureName, string content)
        {
            File.WriteAllText(Path.Combine(FixtureDir(), fixtureName), content.Replace("\r\n", "\n") + "\n");
        }

        /// <summary>
        /// A two-item request solved by the real pipeline against the same
        /// in-memory API clients CraftingPlanPipelineTests uses. Two items,
        /// both flags off their defaults and one ignored id, so a fixture
        /// exercises every request-layer member rather than the members
        /// that happen to differ from zero.
        /// </summary>
        private static async Task<PersistedPlan> BuildRealPlanAsync()
        {
            var recipeApi = new InMemoryRecipeApiClient();
            recipeApi.AddSearchResult(1, 10);
            recipeApi.AddRecipe(new RawRecipe
            {
                Id = 10,
                OutputItemId = 1,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 2, Count = 3 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });

            var priceApi = new InMemoryPriceApiClient();
            priceApi.AddPrice(2, 15, 12);

            var itemApi = new InMemoryItemApiClient();
            itemApi.AddItem(1, "Target", "t.png");
            itemApi.AddItem(2, "Ingredient", "i.png");

            var pipeline = new CraftingPlanPipeline(
                new RecipeService(recipeApi),
                new TradingPostService(priceApi),
                new PlanSolver(),
                new ItemMetadataService(itemApi));

            var requestItems = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = 1, Quantity = 2 },
                new PlanRequestItem { ItemId = 2, Quantity = 7 },
            };

            var result = await pipeline.GenerateStructuredAsync(
                requestItems, null, CancellationToken.None, priceBasis: PriceBasis.BuyOrder);

            return new PersistedPlan
            {
                SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                RequestSchemaVersion = PersistedPlan.CurrentRequestSchemaVersion,
                GeneratedAt = new DateTime(2026, 8, 28, 9, 15, 0, DateTimeKind.Utc),
                RequestItems = requestItems,
                UseOwnMaterials = true,
                PriceBasis = PriceBasis.BuyOrder,
                ValueOwnMaterials = true,
                Result = result,
                NodeOverrides = new Dictionary<int, AcquisitionSource>(),
                IgnoredItemIds = new List<int> { 2 },
            };
        }

        private static PlanHistoryEntry BuildRealHistoryEntry()
        {
            return new PlanHistoryEntry
            {
                EntryId = "0123456789abcdef0123456789abcdef",
                CreatedAtUtc = new DateTime(2026, 8, 28, 9, 15, 0, DateTimeKind.Utc),
                LastGeneratedAtUtc = new DateTime(2026, 8, 28, 9, 15, 0, DateTimeKind.Utc),
                Pinned = true,
                RequestItems = new List<PlanRequestItem>
                {
                    new PlanRequestItem { ItemId = 1, Quantity = 2 },
                },
                UseOwnMaterials = true,
                PriceBasis = PriceBasis.BuyOrder,
                ValueOwnMaterials = true,
                IgnoredItemIds = new List<int> { 2 },
                ItemSummaries = new List<PlanHistoryItemSummary>
                {
                    new PlanHistoryItemSummary
                    {
                        ItemId = 1,
                        Name = "Target",
                        IconUrl = "t.png",
                        Rarity = "Fine",
                        Quantity = 2,
                    },
                },
                TotalCoinCostAtGeneration = 1234,
                OverrideCountAtGeneration = 0,
                IgnoredCountAtGeneration = 1,
                BlobPresent = true,
                BlobSchemaVersion = PersistedPlan.CurrentSchemaVersion,
                CostSamples = new List<PlanHistorySample>
                {
                    new PlanHistorySample
                    {
                        TimestampUtc = new DateTime(2026, 8, 28, 9, 15, 0, DateTimeKind.Utc),
                        TotalCoinCost = 1234,
                    },
                },
            };
        }

        private static string FixtureDir()
        {
            string anchor = RepoFileLocator.FindRepoFile(
                Path.Combine("tests", "shared", "plan_fixtures", "README.md"));
            if (string.IsNullOrEmpty(anchor))
            {
                throw new FileNotFoundException(
                    "Could not locate " + FixtureDirRelativePath
                    + "/README.md by walking up from the test assembly's directory.");
            }

            return Path.GetDirectoryName(anchor);
        }
    }
}
