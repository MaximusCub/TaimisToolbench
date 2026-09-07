using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using TaimisToolbench.Services.Recipes;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class RecipeCacheStoreTests
    {
        // ---- SeedMayBeStale. A /v2/build fetch that failed leaves the
        // module unable to vouch for the seed. Reading that as fresh
        // silenced the staleness status for the whole of any session that
        // launched offline, which is the one session most likely to be
        // serving old data.
        private static SeededRecipeCacheStore SeedWithManifestBuild(int? seedBuildId)
        {
            var store = new SeededRecipeCacheStore();
            string searchJson = RecipeCacheSerializer.SerializeSearches(
                new Dictionary<int, IReadOnlyList<int>> { { 100, new List<int> { 1 } } });
            string recipeJson = RecipeCacheSerializer.SerializeRecipes(
                new Dictionary<int, RawRecipe>
                {
                    { 1, new RawRecipe { Id = 1, OutputItemId = 100, OutputItemCount = 1 } },
                });

            using (var s1 = new MemoryStream(Encoding.UTF8.GetBytes(searchJson)))
            using (var s2 = new MemoryStream(Encoding.UTF8.GetBytes(recipeJson)))
            {
                store.Load(s1, s2);
            }

            if (seedBuildId.HasValue)
            {
                string manifestJson = RecipeCacheSerializer.SerializeManifest(
                    new RecipeSeedManifest { SeedVersion = 1, Gw2BuildId = seedBuildId.Value });
                using (var m = new MemoryStream(Encoding.UTF8.GetBytes(manifestJson)))
                {
                    store.LoadManifest(m);
                }
            }

            return store;
        }

        [Fact]
        public void SeedMayBeStale_LiveBuildUnknown_DoesNotReadAsFresh()
        {
            var store = SeedWithManifestBuild(205780);

            // SetCurrentBuildId is never called: this is the offline launch.
            Assert.True(store.SeedMayBeStale);
        }

        [Fact]
        public void SeedMayBeStale_LiveBuildMatches_ReadsAsFresh()
        {
            var store = SeedWithManifestBuild(205780);
            store.SetCurrentBuildId(205780);

            Assert.False(store.SeedMayBeStale);
        }

        [Fact]
        public void SeedMayBeStale_LiveBuildDiffers_StaysTrue()
        {
            var store = SeedWithManifestBuild(205505);
            store.SetCurrentBuildId(205780);

            Assert.True(store.SeedMayBeStale);
        }

        [Fact]
        public void SeedMayBeStale_SeedCarriesNoBuildId_MakesNoClaim()
        {
            // Without a seed build there is nothing to compare, so the
            // status line would have no build number to name.
            var store = SeedWithManifestBuild(null);

            Assert.False(store.SeedMayBeStale);
        }

        [Fact]
        public void SeedMayBeStale_LateBuildIdArrival_ClearsTheWarning()
        {
            // The build fetch is retried off the plan path, so a store can
            // go from unknown to known inside one session.
            var store = SeedWithManifestBuild(205780);
            Assert.True(store.SeedMayBeStale);

            store.SetCurrentBuildId(205780);
            Assert.False(store.SeedMayBeStale);
        }

        [Fact]
        public void SeededStore_LoadsAndServes()
        {
            // Build in-memory JSON streams
            var searches = new Dictionary<int, IReadOnlyList<int>>
            {
                { 100, new List<int> { 1, 2 } },
                { 200, new List<int> { 3 } },
            };
            var recipes = new Dictionary<int, RawRecipe>
            {
                {
                    1, new RawRecipe
                    {
                        Id = 1,
                        OutputItemId = 100,
                        OutputItemCount = 1,
                        Ingredients = new List<RawIngredient>
                        {
                            new RawIngredient { Type = "Item", Id = 200, Count = 2 },
                        },
                        Disciplines = new List<string> { "Weaponsmith" },
                        MinRating = 400,
                        Flags = new List<string> { "AutoLearned" },
                    }
                },
            };

            string searchJson = RecipeCacheSerializer.SerializeSearches(searches);
            string recipeJson = RecipeCacheSerializer.SerializeRecipes(recipes);

            var store = new SeededRecipeCacheStore();
            using (var s1 = new MemoryStream(Encoding.UTF8.GetBytes(searchJson)))
            using (var s2 = new MemoryStream(Encoding.UTF8.GetBytes(recipeJson)))
            {
                store.Load(s1, s2);
            }

            // Hits
            var searchResult = store.TryGetSearch(100);
            Assert.NotNull(searchResult);
            Assert.Equal(2, searchResult.Count);
            Assert.Equal(1, searchResult[0]);
            Assert.Equal(2, searchResult[1]);

            var recipeResult = store.TryGetRecipe(1);
            Assert.NotNull(recipeResult);
            Assert.Equal(100, recipeResult.OutputItemId);
            Assert.Single(recipeResult.Ingredients);

            // Misses
            Assert.Null(store.TryGetSearch(999));
            Assert.Null(store.TryGetRecipe(999));

            // Stats
            Assert.Equal(1, store.Stats.SearchHits);
            Assert.Equal(1, store.Stats.SearchMisses);
            Assert.Equal(1, store.Stats.RecipeHits);
            Assert.Equal(1, store.Stats.RecipeMisses);

            // Put is no-op on seeded store
            store.PutSearch(999, new List<int> { 10 });
            Assert.Null(store.TryGetSearch(999));
        }

        [Fact]
        public void CompositeCache_OverlayTakesPrecedence()
        {
            using (var tmp = new TempDirectory())
            {
                string tempDir = tmp.Path;

                // Seed has search for item 100
                var searches = new Dictionary<int, IReadOnlyList<int>>
                {
                    { 100, new List<int> { 1 } },
                };
                var recipes = new Dictionary<int, RawRecipe>
                {
                    {
                        1, new RawRecipe
                        {
                            Id = 1,
                            OutputItemId = 100,
                            OutputItemCount = 1,
                            Ingredients = new List<RawIngredient>(),
                            Disciplines = new List<string>(),
                            MinRating = 0,
                            Flags = new List<string>(),
                        }
                    },
                };

                var seed = new SeededRecipeCacheStore();
                using (var s1 = new MemoryStream(
                    Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeSearches(searches))))
                using (var s2 = new MemoryStream(
                    Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeRecipes(recipes))))
                {
                    seed.Load(s1, s2);
                }

                var overlay = new OverlayRecipeCacheStore(tempDir);
                overlay.Load();

                var composite = new CompositeRecipeCacheStore(seed, overlay);

                // Seeded item returns hit via seed layer
                var result = composite.TryGetSearch(100);
                Assert.NotNull(result);
                Assert.Single(result);

                // POLICY CHANGE: an item the loaded corpus knows no recipe
                // for is an authoritative EMPTY answer now (previously a
                // null miss that fell through to the API).
                var unknown = composite.TryGetSearch(500);
                Assert.NotNull(unknown);
                Assert.Empty(unknown);

                // Put goes to overlay
                composite.PutSearch(500, new List<int> { 10 });
                var overlayResult = composite.TryGetSearch(500);
                Assert.NotNull(overlayResult);
                Assert.Single(overlayResult);
                Assert.Equal(10, overlayResult[0]);

                // Overlay takes precedence over seed
                composite.PutSearch(100, new List<int> { 99, 98 });
                var overridden = composite.TryGetSearch(100);
                Assert.NotNull(overridden);
                Assert.Equal(2, overridden.Count);
                Assert.Equal(99, overridden[0]);
            }
        }

        // POLICY CHANGE (recipe cache staleness policy): this evolves
        // Overlay_Invalidates_OnBuildChange and (since Load no longer takes
        // a build id, collapsing the harness path onto the module path)
        // Overlay_Load_WithMismatchedBuildId_ClearsOverlay, which pinned the
        // wipe: a build mismatch cleared the maps and deleted all three
        // files. The wipe destroyed learned positives at exactly the moment
        // they became useful - a new game build is what makes the shipped
        // seed stale - for data measured byte-identical across a 275-build
        // gap. A mismatch now only restamps the manifest.
        [Fact]
        public void Overlay_SurvivesBuildChange_AndRestamps()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildId = 205780;
                string tempDir = tmp.Path;

                var overlay = new OverlayRecipeCacheStore(tempDir);
                overlay.Load();
                overlay.SetCurrentBuildId(buildId);
                overlay.PutSearch(100, new List<int> { 1 });
                overlay.PutRecipe(1, NewRecipe(1, 100));
                overlay.Flush(force: true);

                string cacheDir = Path.Combine(tempDir, "recipe_cache");
                Assert.True(File.Exists(Path.Combine(cacheDir, "search_overlay.json")));
                Assert.True(File.Exists(Path.Combine(cacheDir, "recipes_overlay.json")));
                Assert.True(File.Exists(Path.Combine(cacheDir, "overlay_manifest.json")));
                Assert.Equal(buildId, ReadOverlayManifestBuildId(tempDir));

                // Reload at the same build - data preserved.
                var overlay2 = new OverlayRecipeCacheStore(tempDir);
                overlay2.Load();
                overlay2.SetCurrentBuildId(buildId);
                Assert.NotNull(overlay2.TryGetSearch(100));
                Assert.NotNull(overlay2.TryGetRecipe(1));

                // Reload at a DIFFERENT build - data preserved, all three
                // files intact, and the manifest restamps on the next flush.
                var overlay3 = new OverlayRecipeCacheStore(tempDir);
                overlay3.Load();
                overlay3.SetCurrentBuildId(buildId + 1);
                Assert.NotNull(overlay3.TryGetSearch(100));
                Assert.NotNull(overlay3.TryGetRecipe(1));
                Assert.True(File.Exists(Path.Combine(cacheDir, "search_overlay.json")));
                Assert.True(File.Exists(Path.Combine(cacheDir, "recipes_overlay.json")));
                Assert.True(File.Exists(Path.Combine(cacheDir, "overlay_manifest.json")));

                overlay3.Flush(force: true);
                Assert.Equal(buildId + 1, ReadOverlayManifestBuildId(tempDir));
            }
        }

        // Walks four "sessions" in Module.cs's own order - Load(), then
        // SetCurrentBuildId(build) once the async build check lands. Keeps
        // its stamping assertions; the "cleared at a different build"
        // assertions it used to make are retired with the wipe (see
        // Overlay_SurvivesBuildChange_AndRestamps).
        [Fact]
        public void Overlay_SurvivesRestart_AtSameBuild_AndRestampsAfterBuildChange()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildA = 205780;
                const int buildB = 205781;
                string tempDir = tmp.Path;

                var session1 = new OverlayRecipeCacheStore(tempDir);
                session1.Load();
                session1.SetCurrentBuildId(buildA);
                session1.PutSearch(100, new List<int> { 1 });
                session1.PutRecipe(1, NewRecipe(1, 100));
                session1.Flush(force: true);
                Assert.Equal(buildA, ReadOverlayManifestBuildId(tempDir));

                // Restart at the same build: the overlay is served from disk,
                // and adding to it keeps the stamp.
                var session2 = new OverlayRecipeCacheStore(tempDir);
                session2.Load();
                session2.SetCurrentBuildId(buildA);
                var search = session2.TryGetSearch(100);
                Assert.NotNull(search);
                Assert.Equal(1, search[0]);
                Assert.Equal(100, session2.TryGetRecipe(1).OutputItemId);
                session2.PutSearch(200, new List<int> { 2 });
                session2.PutRecipe(2, NewRecipe(2, 200));
                session2.Flush(force: true);
                Assert.Equal(buildA, ReadOverlayManifestBuildId(tempDir));

                // Restart on a new game build: everything learned so far is
                // kept and served, and what the session adds is stamped with
                // the NEW build.
                var session3 = new OverlayRecipeCacheStore(tempDir);
                session3.Load();
                session3.SetCurrentBuildId(buildB);
                Assert.NotNull(session3.TryGetSearch(100));
                Assert.NotNull(session3.TryGetRecipe(1));
                session3.PutSearch(300, new List<int> { 3 });
                session3.PutRecipe(3, NewRecipe(3, 300));
                session3.Flush(force: true);
                Assert.Equal(buildB, ReadOverlayManifestBuildId(tempDir));

                var session4 = new OverlayRecipeCacheStore(tempDir);
                session4.Load();
                session4.SetCurrentBuildId(buildB);
                Assert.NotNull(session4.TryGetSearch(100));
                Assert.NotNull(session4.TryGetSearch(200));
                Assert.NotNull(session4.TryGetSearch(300));
                Assert.NotNull(session4.TryGetRecipe(3));
            }
        }

        // POLICY CHANGE: evolved from
        // Overlay_WithheldFromReads_UntilTheBuildCheckResolves, which pinned
        // the deferred-load mechanism - persisted entries were unreadable
        // until the async /v2/build check proved their vintage. Positives
        // are now servable whatever build they were cached from, so the
        // deferral is retired and Load reads the files immediately.
        [Fact]
        public void Overlay_ServesPersistedPositives_BeforeTheBuildCheckResolves()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildId = 205780;
                string tempDir = tmp.Path;

                var session1 = new OverlayRecipeCacheStore(tempDir);
                session1.Load();
                session1.SetCurrentBuildId(buildId);
                session1.PutSearch(100, new List<int> { 1 });
                session1.PutRecipe(1, NewRecipe(1, 100));
                session1.Flush(force: true);

                // No build id this session yet - served regardless.
                var session2 = new OverlayRecipeCacheStore(tempDir);
                session2.Load();
                Assert.NotNull(session2.TryGetSearch(100));
                Assert.Equal(100, session2.TryGetRecipe(1).OutputItemId);

                session2.PutSearch(200, new List<int> { 2 });
                Assert.NotNull(session2.TryGetSearch(200));
            }
        }

        // POLICY CHANGE: evolved from
        // Overlay_BuildCheckNeverResolves_LeavesPersistedOverlayUntouched,
        // which pinned that a session with no build id could neither read
        // nor write the persisted overlay. What it learns is now persisted
        // too - a learned positive is true whatever build it was fetched
        // under - and the manifest keeps the last stamped build, since this
        // session has nothing to restamp with.
        [Fact]
        public void Overlay_BuildCheckNeverResolves_StillServesAndPersists()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildId = 205780;
                string tempDir = tmp.Path;

                var session1 = new OverlayRecipeCacheStore(tempDir);
                session1.Load();
                session1.SetCurrentBuildId(buildId);
                session1.PutSearch(100, new List<int> { 1 });
                session1.Flush(force: true);

                var session2 = new OverlayRecipeCacheStore(tempDir);
                session2.Load();
                Assert.NotNull(session2.TryGetSearch(100));
                session2.PutSearch(300, new List<int> { 3 });
                session2.Flush(force: true);

                Assert.Equal(buildId, ReadOverlayManifestBuildId(tempDir));

                var session3 = new OverlayRecipeCacheStore(tempDir);
                session3.Load();
                Assert.NotNull(session3.TryGetSearch(100));
                Assert.NotNull(session3.TryGetSearch(300));
            }
        }

        // Spec 2.3: the composite's final branch. With a loaded corpus,
        // "no known recipe outputs this item" is exact and counts as a
        // search HIT (so the "Discovering recipes from API..." heuristic
        // does not fire for derived negatives).
        [Fact]
        public void Composite_LoadedCorpus_DerivesAnAuthoritativeNegative_AsAHit()
        {
            using (var tmp = new TempDirectory())
            {
                var seed = NewSeedWithOneRecipe();
                var overlay = new OverlayRecipeCacheStore(tmp.Path);
                overlay.Load();
                var composite = new CompositeRecipeCacheStore(seed, overlay);

                var negative = composite.TryGetSearch(999);
                Assert.NotNull(negative);
                Assert.Empty(negative);
                Assert.Equal(1, composite.Stats.SearchHits);
                Assert.Equal(0, composite.Stats.SearchMisses);
            }
        }

        // The empty-corpus guard: Module.cs's seed-load catch can leave an
        // empty seed, and an empty corpus proves nothing - the miss (and
        // with it the API fallback) must be preserved.
        [Fact]
        public void Composite_EmptyCorpus_ReturnsNullForUnknownItem()
        {
            using (var tmp = new TempDirectory())
            {
                var seed = new SeededRecipeCacheStore();
                var overlay = new OverlayRecipeCacheStore(tmp.Path);
                overlay.Load();
                var composite = new CompositeRecipeCacheStore(seed, overlay);

                Assert.Null(composite.TryGetSearch(999));
                Assert.Equal(1, composite.Stats.SearchMisses);
            }
        }

        // Spec step 4's evidence: a plan whose items are all in the seed
        // corpus asks the search endpoint for NOTHING - raw materials
        // resolve as derived negatives - and an item the API would answer
        // empty for leaves no row on disk. Red against the old code twice
        // over: the miss for item 200 used to go to the API, and the empty
        // answer used to be persisted.
        [Fact]
        public async Task RecipeService_OverACompositeCorpus_AnswersNegativesLocally_AndPersistsNoEmptyRow()
        {
            using (var tmp = new TempDirectory())
            {
                var seed = NewSeedWithOneRecipe();
                var overlay = new OverlayRecipeCacheStore(tmp.Path);
                overlay.Load();
                var composite = new CompositeRecipeCacheStore(seed, overlay);

                var api = new CountingRecipeApiClient();
                var service = new RecipeService(api, cacheStore: composite);
                var tree = await service.BuildTreeAsync(100, 1, CancellationToken.None);
                await service.PendingCacheFlush;

                // Item 100 crafts from the seed; ingredient 200 is a leaf
                // answered by the corpus, not the API.
                Assert.Single(tree.Recipes);
                Assert.Empty(tree.Recipes[0].Ingredients[0].Recipes);
                Assert.Equal(0, api.SearchCallCount);
                Assert.Equal(0, api.RecipeCallCount);

                // Nothing was learned, so nothing was written - least of
                // all an empty row for item 200.
                string searchPath = Path.Combine(
                    tmp.Path, "recipe_cache", "search_overlay.json");
                Assert.False(File.Exists(searchPath));

                var inspect = new OverlayRecipeCacheStore(tmp.Path);
                inspect.Load();
                Assert.Null(inspect.TryGetSearch(200));
            }
        }

        // Spec step 6 (Clear Cache): the only manual route out of a bad
        // overlay once wipe-on-mismatch is gone. All three files go, a
        // later flush does not resurrect them, a fresh Load yields an
        // empty overlay, and the seed keeps serving through the composite.
        [Fact]
        public void Overlay_Clear_DeletesAllFiles_AndSeedStillServes()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildId = 205780;
                string cacheDir = Path.Combine(tmp.Path, "recipe_cache");

                var overlay = new OverlayRecipeCacheStore(tmp.Path);
                overlay.Load();
                overlay.SetCurrentBuildId(buildId);
                overlay.PutSearch(700, new List<int> { 7 });
                overlay.PutRecipe(7, NewRecipe(7, 700));
                overlay.SetCorpusVerified(buildId, 3);
                overlay.Flush(force: true);
                Assert.True(File.Exists(Path.Combine(cacheDir, "overlay_manifest.json")));

                overlay.Clear();

                Assert.False(File.Exists(Path.Combine(cacheDir, "search_overlay.json")));
                Assert.False(File.Exists(Path.Combine(cacheDir, "recipes_overlay.json")));
                Assert.False(File.Exists(Path.Combine(cacheDir, "overlay_manifest.json")));
                Assert.Null(overlay.TryGetSearch(700));
                Assert.Null(overlay.TryGetRecipe(7));

                // The verification stamp is zeroed, so the probe re-arms.
                Assert.Equal(0, overlay.NegativesVerifiedBuildId);

                // Nothing dirty survives Clear for a later flush to
                // resurrect.
                overlay.Flush(force: true);
                Assert.False(File.Exists(Path.Combine(cacheDir, "overlay_manifest.json")));

                var reloaded = new OverlayRecipeCacheStore(tmp.Path);
                reloaded.Load();
                Assert.Null(reloaded.TryGetSearch(700));

                var composite = new CompositeRecipeCacheStore(
                    NewSeedWithOneRecipe(), reloaded);
                Assert.Equal(new[] { 1 }, composite.TryGetSearch(100));
                Assert.Empty(composite.TryGetSearch(700));
            }
        }

        // Spec 2.6: the session search memo has no invalidation of its
        // own, so a corpus repair landing mid-session would stay invisible
        // for item ids already resolved this session. InvalidateSearch is
        // the repair path's hook to close that for the repaired items.
        [Fact]
        public async Task RecipeService_InvalidateSearch_MakesARepairVisibleMidSession()
        {
            var store = new InMemoryRecipeCacheStore();
            var api = new CountingRecipeApiClient();
            var service = new RecipeService(api, cacheStore: store);

            var before = await service.BuildTreeAsync(100, 1, CancellationToken.None);
            Assert.Empty(before.Recipes);

            // The repair: a recipe for item 100 lands in the store.
            store.PutSearch(100, new List<int> { 1 });
            store.PutRecipe(1, NewRecipe(1, 100));

            // Without invalidation, the session memo still answers "no
            // recipe".
            var shadowed = await service.BuildTreeAsync(100, 1, CancellationToken.None);
            Assert.Empty(shadowed.Recipes);

            service.InvalidateSearch(100);
            var repaired = await service.BuildTreeAsync(100, 1, CancellationToken.None);
            Assert.Single(repaired.Recipes);
            Assert.Equal(1, repaired.Recipes[0].RecipeId);
        }

        // Seed corpus: recipe 1 makes item 100 from 2x item 200.
        private static SeededRecipeCacheStore NewSeedWithOneRecipe()
        {
            var searches = new Dictionary<int, IReadOnlyList<int>>
            {
                { 100, new List<int> { 1 } },
            };
            var recipes = new Dictionary<int, RawRecipe>
            {
                {
                    1, new RawRecipe
                    {
                        Id = 1,
                        OutputItemId = 100,
                        OutputItemCount = 1,
                        Ingredients = new List<RawIngredient>
                        {
                            new RawIngredient { Type = "Item", Id = 200, Count = 2 },
                        },
                        Disciplines = new List<string> { "Weaponsmith" },
                        MinRating = 400,
                        Flags = new List<string>(),
                    }
                },
            };

            var seed = new SeededRecipeCacheStore();
            using (var s1 = new MemoryStream(
                Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeSearches(searches))))
            using (var s2 = new MemoryStream(
                Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeRecipes(recipes))))
            {
                seed.Load(s1, s2);
            }

            seed.FinalizeIndex();
            return seed;
        }

        private static RawRecipe NewRecipe(int recipeId, int outputItemId)
        {
            return new RawRecipe
            {
                Id = recipeId,
                OutputItemId = outputItemId,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>(),
                Disciplines = new List<string>(),
                MinRating = 0,
                Flags = new List<string>(),
            };
        }

        // A plan that learns a search and no new recipe - what a
        // build-current seed makes the common case - must leave the recipe
        // cache file alone. Deleting it behind the store's back is the
        // deterministic way to see whether the next flush touches it.
        [Fact]
        public void Overlay_FlushRewritesOnlyTheCacheThatChanged()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildId = 205780;
                var overlay = new OverlayRecipeCacheStore(tmp.Path);
                overlay.Load();
                overlay.SetCurrentBuildId(buildId);
                overlay.PutSearch(100, new List<int> { 1 });
                overlay.PutRecipe(1, NewRecipe(1, 100));
                overlay.Flush(force: true);

                string recipesPath = Path.Combine(
                    tmp.Path, "recipe_cache", "recipes_overlay.json");
                File.Delete(recipesPath);

                overlay.PutSearch(200, new List<int> { 2 });
                overlay.Flush(force: true);

                Assert.False(File.Exists(recipesPath));
                Assert.Equal(buildId, ReadOverlayManifestBuildId(tmp.Path));

                var reloaded = new OverlayRecipeCacheStore(tmp.Path);
                reloaded.Load();
                Assert.NotNull(reloaded.TryGetSearch(100));
                Assert.NotNull(reloaded.TryGetSearch(200));
            }
        }

        // Load replaces the maps with what disk holds, so entries put before
        // it are gone and their dirty flags cleared; a flush afterwards must
        // not resurrect them. (Evolved from
        // Overlay_LoadAtNewBuild_LeavesNothingForALaterFlushToWrite, whose
        // deleted-files assertions pinned the retired build-mismatch wipe;
        // the replace-and-clear contract it also pinned is unchanged.)
        [Fact]
        public void Overlay_Load_ReplacesUnflushedPuts_AndClearsTheirDirtyFlags()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildA = 205780;

                var first = new OverlayRecipeCacheStore(tmp.Path);
                first.Load();
                first.SetCurrentBuildId(buildA);
                first.PutSearch(100, new List<int> { 1 });
                first.Flush(force: true);
                Assert.Equal(buildA, ReadOverlayManifestBuildId(tmp.Path));

                var second = new OverlayRecipeCacheStore(tmp.Path);
                second.Load();
                second.PutSearch(200, new List<int> { 2 });
                second.Load();
                second.Flush(force: true);

                Assert.NotNull(second.TryGetSearch(100));
                Assert.Null(second.TryGetSearch(200));

                var third = new OverlayRecipeCacheStore(tmp.Path);
                third.Load();
                Assert.NotNull(third.TryGetSearch(100));
                Assert.Null(third.TryGetSearch(200));
                Assert.Equal(buildA, ReadOverlayManifestBuildId(tmp.Path));
            }
        }

        private static int ReadOverlayManifestBuildId(string dataDir)
        {
            string manifestPath = Path.Combine(dataDir, "recipe_cache", "overlay_manifest.json");
            using (var fs = File.OpenRead(manifestPath))
            {
                return RecipeCacheSerializer
                    .LoadManifest<RecipeOverlayManifest>(fs)
                    .Gw2BuildId;
            }
        }

        [Fact]
        public async Task RecipeService_UsesCache_SkipsApiForCachedItems()
        {
            // Pre-populate a cache store with search + recipe data
            var cacheStore = new InMemoryRecipeCacheStore();

            // Root item 100 -> recipe 1 -> ingredient 200 (leaf)
            cacheStore.PutSearch(100, new List<int> { 1 });
            cacheStore.PutRecipe(1, new RawRecipe
            {
                Id = 1,
                OutputItemId = 100,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 200, Count = 2 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });
            cacheStore.PutSearch(200, Array.Empty<int>());

            // API client tracks call count
            var api = new CountingRecipeApiClient();

            var service = new RecipeService(api, cacheStore: cacheStore);
            var tree = await service.BuildTreeAsync(100, 1, CancellationToken.None);

            // Tree built correctly from cache
            Assert.Equal(100, tree.Id);
            Assert.Single(tree.Recipes);
            Assert.Equal(2, tree.Recipes[0].Ingredients[0].Quantity);

            // API was NOT called - everything came from cache
            Assert.Equal(0, api.SearchCallCount);
            Assert.Equal(0, api.RecipeCallCount);
        }

        [Fact]
        public async Task RecipeService_FallsThrough_ToApiForUncachedItems()
        {
            // Cache store has root search but NOT recipe details or leaf search
            var cacheStore = new InMemoryRecipeCacheStore();
            cacheStore.PutSearch(100, new List<int> { 1 });
            // recipe 1 and search for 200 are NOT in cache
            var api = new InMemoryRecipeApiClient();
            api.AddRecipe(new RawRecipe
            {
                Id = 1,
                OutputItemId = 100,
                OutputItemCount = 1,
                Ingredients = new List<RawIngredient>
                {
                    new RawIngredient { Type = "Item", Id = 200, Count = 3 },
                },
                Disciplines = new List<string> { "Weaponsmith" },
                MinRating = 400,
                Flags = new List<string> { "AutoLearned" },
            });
            // 200 is a leaf - no search results registered
            var service = new RecipeService(api, cacheStore: cacheStore);
            var tree = await service.BuildTreeAsync(100, 1, CancellationToken.None);

            // Tree built correctly via API fallback
            Assert.Equal(100, tree.Id);
            Assert.Single(tree.Recipes);
            Assert.Equal(200, tree.Recipes[0].Ingredients[0].Id);
            Assert.Equal(3, tree.Recipes[0].Ingredients[0].Quantity);

            // API was called for the uncached recipe + leaf search
            // Recipe 1 was fetched from API
            var cachedRecipe = cacheStore.TryGetRecipe(1);
            Assert.NotNull(cachedRecipe);
            Assert.Equal(100, cachedRecipe.OutputItemId);

            // POLICY CHANGE: the empty leaf answer for 200 used to be Put
            // into the store; empty answers are session-only now (the
            // endpoint lies for 15 real craftable items), so the store
            // gains no row.
            Assert.Null(cacheStore.TryGetSearch(200));
        }

        // POLICY CHANGE: evolved from
        // RecipeService_ProvenEmptySearch_IsServedFromDisk_NextSession,
        // which pinned the learned-negative optimization - an API-answered
        // empty search persisted to the overlay and served across sessions.
        // The live search endpoint demonstrably lies (15 real craftable
        // items return an empty search while their recipe is fetchable by
        // id), so a persisted empty row is a poisoned fact; the migration
        // pass drops every one at the next load. Cross-session "no recipe"
        // answers now come from the corpus derivation in
        // CompositeRecipeCacheStore instead of from disk.
        [Fact]
        public async Task RecipeService_ProvenEmptySearch_DoesNotSurviveAcrossSessions()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildId = 205780;

                var overlay1 = new OverlayRecipeCacheStore(tmp.Path);
                overlay1.Load();
                overlay1.SetCurrentBuildId(buildId);

                var api1 = new CountingRecipeApiClient();
                var session1 = new RecipeService(api1, cacheStore: overlay1);
                var tree1 = await session1.BuildTreeAsync(100, 1, CancellationToken.None);

                // The persist runs off the build's own path, so the next
                // session may only read disk once it has landed.
                await session1.PendingCacheFlush;

                Assert.Empty(tree1.Recipes);
                Assert.Equal(1, api1.SearchCallCount);

                var overlay2 = new OverlayRecipeCacheStore(tmp.Path);
                overlay2.Load();
                overlay2.SetCurrentBuildId(buildId);

                // The empty row did not carry over; a bare overlay (no seed
                // corpus to derive from) asks the endpoint again.
                Assert.Null(overlay2.TryGetSearch(100));

                var api2 = new CountingRecipeApiClient();
                var tree2 = await new RecipeService(api2, cacheStore: overlay2)
                    .BuildTreeAsync(100, 1, CancellationToken.None);

                Assert.Empty(tree2.Recipes);
                Assert.Equal(1, api2.SearchCallCount);
            }
        }

        // The v1 -> v2 overlay migration, against files written byte-for-
        // byte in the v1 shape (learned negatives included, manifest with
        // the old two fields and the never-stamped gw2BuildId: 0 defect):
        // positive search rows and recipes carry over whatever the stored
        // build id, empty rows are dropped unconditionally, a recipe whose
        // output lost its row is re-indexed, and the reflushed manifest
        // reads back at schemaVersion 2 with the verification fields.
        [Fact]
        public void Overlay_V1Migration_KeepsPositives_DropsNegatives_RewritesManifestAtSchema2()
        {
            using (var tmp = new TempDirectory())
            {
                string cacheDir = Path.Combine(tmp.Path, "recipe_cache");
                Directory.CreateDirectory(cacheDir);

                File.WriteAllText(
                    Path.Combine(cacheDir, "search_overlay.json"),
                    "{\"schemaVersion\":1,\"searches\":{" +
                    "\"100\":[1],\"200\":[],\"300\":[2],\"400\":[]}}",
                    Encoding.UTF8);

                // Recipe 5 outputs item 500, which has NO search row - the
                // fill half of the pass must repair that.
                File.WriteAllText(
                    Path.Combine(cacheDir, "recipes_overlay.json"),
                    "{\"schemaVersion\":1,\"recipes\":[" +
                    "{\"id\":1,\"outputItemId\":100,\"outputItemCount\":1," +
                    "\"minRating\":0,\"ingredients\":[],\"disciplines\":[],\"flags\":[]}," +
                    "{\"id\":5,\"outputItemId\":500,\"outputItemCount\":1," +
                    "\"minRating\":0,\"ingredients\":[],\"disciplines\":[],\"flags\":[]}]}",
                    Encoding.UTF8);

                File.WriteAllText(
                    Path.Combine(cacheDir, "overlay_manifest.json"),
                    "{\"gw2BuildId\":0,\"updatedUtc\":\"2026-01-01T00:00:00Z\"}",
                    Encoding.UTF8);

                var overlay = new OverlayRecipeCacheStore(tmp.Path);
                overlay.Load();

                // Positives carried over; learned negatives are gone (null,
                // not an empty list); the rowless output was re-indexed.
                Assert.Equal(new[] { 1 }, overlay.TryGetSearch(100));
                Assert.Equal(new[] { 2 }, overlay.TryGetSearch(300));
                Assert.Null(overlay.TryGetSearch(200));
                Assert.Null(overlay.TryGetSearch(400));
                Assert.Equal(new[] { 5 }, overlay.TryGetSearch(500));
                Assert.NotNull(overlay.TryGetRecipe(1));
                Assert.NotNull(overlay.TryGetRecipe(5));
                Assert.Equal(2, overlay.DroppedLearnedNegatives);

                // The migration marked the store dirty, so the cleanup and
                // the schema bump land on the next flush without any new
                // learning happening first.
                overlay.Flush(force: true);

                RecipeOverlayManifest manifest;
                using (var fs = File.OpenRead(Path.Combine(cacheDir, "overlay_manifest.json")))
                {
                    manifest = RecipeCacheSerializer.LoadManifest<RecipeOverlayManifest>(fs);
                }

                Assert.Equal(2, manifest.SchemaVersion);
                Assert.Equal(0, manifest.Gw2BuildId);
                Assert.Equal(0, manifest.NegativesVerifiedBuildId);
                Assert.Equal(0, manifest.VerifiedKnownRecipeCount);

                // A second load sees the migrated file: nothing left to drop.
                var reloaded = new OverlayRecipeCacheStore(tmp.Path);
                reloaded.Load();
                Assert.Equal(0, reloaded.DroppedLearnedNegatives);
                Assert.Equal(new[] { 1 }, reloaded.TryGetSearch(100));
                Assert.Null(reloaded.TryGetSearch(200));
            }
        }

        // The other half: a 404 from /v2/recipes/search is empty without
        // being an answer, and the overlay now outlives the session, so
        // persisting one would record a craftable item as an uncraftable
        // leaf in every plan until ArenaNet ships a new game build.
        [Fact]
        public async Task RecipeService_UnprovenEmptySearch_IsNotPersisted_NorRepeatedNextSession()
        {
            using (var tmp = new TempDirectory())
            {
                const int buildId = 205780;

                var overlay1 = new OverlayRecipeCacheStore(tmp.Path);
                overlay1.Load();
                overlay1.SetCurrentBuildId(buildId);

                var outage = new InMemoryRecipeApiClient();
                outage.Return404ForSearch.Add(100);
                var degradedSession = new RecipeService(outage, cacheStore: overlay1);
                var degradedTree = await degradedSession
                    .BuildTreeAsync(100, 1, CancellationToken.None);
                await degradedSession.PendingCacheFlush;

                // The plan still degrades to a leaf for this run - the
                // recipes are genuinely unknown - it just leaves no record.
                Assert.Empty(degradedTree.Recipes);

                var inspect = new OverlayRecipeCacheStore(tmp.Path);
                inspect.Load();
                Assert.Null(inspect.TryGetSearch(100));

                // A later session with a healthy endpoint asks it again and
                // gets the real tree.
                var overlay2 = new OverlayRecipeCacheStore(tmp.Path);
                overlay2.Load();
                overlay2.SetCurrentBuildId(buildId);

                var healthy = new InMemoryRecipeApiClient();
                healthy.AddSearchResult(100, 1);
                healthy.AddRecipe(new RawRecipe
                {
                    Id = 1,
                    OutputItemId = 100,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = 200, Count = 2 },
                    },
                    Disciplines = new List<string> { "Weaponsmith" },
                    MinRating = 400,
                    Flags = new List<string>(),
                });

                var tree = await new RecipeService(healthy, cacheStore: overlay2)
                    .BuildTreeAsync(100, 1, CancellationToken.None);

                Assert.Single(tree.Recipes);
                Assert.Equal(200, tree.Recipes[0].Ingredients[0].Id);
            }
        }

        // Within one session too: a transient 404 must not freeze the item
        // as a leaf for every later Generate the way an answered empty does.
        [Fact]
        public async Task RecipeService_UnprovenEmptySearch_IsNotHeldInTheSessionCache()
        {
            var api = new InMemoryRecipeApiClient();
            api.Return404ForSearch.Add(100);

            var service = new RecipeService(api, cacheStore: new InMemoryRecipeCacheStore());
            await service.BuildTreeAsync(100, 1, CancellationToken.None);
            int afterFirstBuild = api.SearchCallCount;

            await service.BuildTreeAsync(100, 1, CancellationToken.None);

            Assert.True(api.SearchCallCount > afterFirstBuild);
        }

        // POLICY CHANGE (recipe cache staleness policy): this replaces
        // SeededStore_NegativeEntry_ReturnsNull_WhenSeedStale and
        // SeededStore_NegativeEntry_ReturnsEmptyList_WhenSeedFresh, which
        // pinned the old stored-negative behaviour - an empty seed row
        // served as an authoritative hit at the seed's build and turned
        // into an API miss when the build moved. Negatives are no longer
        // stored at all: FinalizeIndex drops every empty row at load, on
        // both sides of a build bump, and "no recipe" is derived by
        // CompositeRecipeCacheStore from the corpus instead.
        [Theory]
        [InlineData(100)]
        [InlineData(200)]
        public void SeededStore_EmptyRow_IsAbsentAfterFinalizeIndex_AtAnyBuildId(
            int currentBuildId)
        {
            // Positive entry (100 -> [1]) and stored negative (300 -> []).
            var searches = new Dictionary<int, IReadOnlyList<int>>
            {
                { 100, new List<int> { 1 } },
                { 300, new List<int>() },
            };
            var recipes = new Dictionary<int, RawRecipe>
            {
                { 1, NewRecipe(1, 100) },
            };

            var store = new SeededRecipeCacheStore();
            using (var s1 = new MemoryStream(
                Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeSearches(searches))))
            using (var s2 = new MemoryStream(
                Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeRecipes(recipes))))
            {
                store.Load(s1, s2);
            }

            store.FinalizeIndex();

            var manifest = new RecipeSeedManifest
            {
                SeedVersion = 1,
                Gw2BuildId = 100,
                CreatedUtc = "2026-01-01T00:00:00Z",
            };
            using (var ms = new MemoryStream(
                Encoding.UTF8.GetBytes(RecipeCacheSerializer.SerializeManifest(manifest))))
            {
                store.LoadManifest(ms);
            }

            store.SetCurrentBuildId(currentBuildId);

            // The empty row is gone - a genuine miss, never an empty hit.
            Assert.Null(store.TryGetSearch(300));

            // Positives are untouched by the pass and by the build id.
            var positiveResult = store.TryGetSearch(100);
            Assert.NotNull(positiveResult);
            Assert.Single(positiveResult);
            Assert.Equal(1, positiveResult[0]);

            Assert.Equal(1, store.Stats.SearchHits);
            Assert.Equal(1, store.Stats.SearchMisses);
        }

        // Minimal API client that counts calls
        private class CountingRecipeApiClient : IRecipeApiClient
        {
            private int _searchCallCount;
            private int _recipeCallCount;

            public int SearchCallCount => _searchCallCount;

            public int RecipeCallCount => _recipeCallCount;

            public Task<RecipeSearchResult> SearchByOutputAsync(
                int itemId, CancellationToken ct)
            {
                Interlocked.Increment(ref _searchCallCount);
                return Task.FromResult(
                    new RecipeSearchResult(Array.Empty<int>(), absenceProven: true));
            }

            public Task<RawRecipe> GetRecipeAsync(int recipeId, CancellationToken ct)
            {
                Interlocked.Increment(ref _recipeCallCount);
                return Task.FromResult<RawRecipe>(null);
            }
        }
    }
}
