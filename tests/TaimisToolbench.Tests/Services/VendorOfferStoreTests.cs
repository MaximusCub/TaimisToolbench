using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RepoFileLocator;

namespace TaimisToolbench.Tests.Services
{
    public class VendorOfferStoreTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly VendorOfferLoader _loader;

        public VendorOfferStoreTests()
        {
            _tempDir = Path.Combine(
                Path.GetTempPath(),
                "TaimisToolbench_Tests_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
            _loader = new VendorOfferLoader();
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }

        private MemoryStream MakeDatasetStream(params VendorOffer[] offers)
        {
            var dataset = new VendorOfferDataset
            {
                SchemaVersion = 1,
                GeneratedAt = "2026-01-01T00:00:00Z",
                Source = "test",
                Offers = new List<VendorOffer>(offers),
            };
            string json = _loader.Serialize(dataset);
            return new MemoryStream(Encoding.UTF8.GetBytes(json));
        }

        private VendorOffer MakeOffer(string offerId, int outputItemId, int coinCost)
        {
            return new VendorOffer
            {
                OfferId = offerId,
                OutputItemId = outputItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = coinCost },
                },
                MerchantName = "TestMerchant",
            };
        }

        [Fact]
        public void LoadBaseline_FromStream_PopulatesOffers()
        {
            var store = new VendorOfferStore(_tempDir, _loader);

            using (var stream = MakeDatasetStream(MakeOffer("aaa", 100, 50)))
            {
                store.LoadBaseline(stream);
            }

            var offers = store.GetOffersForItem(100);
            Assert.Single(offers);
            Assert.Equal("aaa", offers[0].OfferId);
            Assert.Equal(100, offers[0].OutputItemId);
        }

        [Fact]
        public void LoadBaseline_NullStream_ReturnsEmptyDataset()
        {
            var store = new VendorOfferStore(_tempDir, _loader);
            store.LoadBaseline(null);

            Assert.Empty(store.GetOffersForItem(100));
            Assert.False(store.HasAnyOffer(100));
        }

        [Fact]
        public void OverlayWinsByOfferId()
        {
            var store = new VendorOfferStore(_tempDir, _loader);

            var baselineOffer = MakeOffer("shared-id", 100, 50);
            baselineOffer.MerchantName = "BaselineMerchant";
            using (var stream = MakeDatasetStream(baselineOffer))
            {
                store.LoadBaseline(stream);
            }

            var overlayOffer = MakeOffer("shared-id", 100, 25);
            overlayOffer.MerchantName = "OverlayMerchant";
            var overlayDataset = new VendorOfferDataset
            {
                SchemaVersion = 1,
                GeneratedAt = "2026-01-02T00:00:00Z",
                Source = "overlay",
                Offers = new List<VendorOffer> { overlayOffer },
            };
            store.SaveOverlay(overlayDataset);
            store.LoadOverlay();

            var offers = store.GetOffersForItem(100);
            Assert.Single(offers);
            Assert.Equal("OverlayMerchant", offers[0].MerchantName);
            Assert.Equal(25, offers[0].CostLines[0].Count);
        }

        [Fact]
        public void SaveOverlay_RoundTrips()
        {
            var store = new VendorOfferStore(_tempDir, _loader);

            var dataset = new VendorOfferDataset
            {
                SchemaVersion = 1,
                GeneratedAt = "2026-01-01T00:00:00Z",
                Source = "overlay",
                Offers = new List<VendorOffer>
                {
                    MakeOffer("offer-1", 100, 50),
                    MakeOffer("offer-2", 200, 75),
                },
            };
            store.SaveOverlay(dataset);

            var store2 = new VendorOfferStore(_tempDir, _loader);
            store2.LoadOverlay();

            Assert.Single(store2.GetOffersForItem(100));
            Assert.Single(store2.GetOffersForItem(200));
            Assert.Equal("offer-1", store2.GetOffersForItem(100)[0].OfferId);
            Assert.Equal("offer-2", store2.GetOffersForItem(200)[0].OfferId);
        }

        [Fact]
        public void AddOffersToOverlay_AppendsAndDedupes()
        {
            var store = new VendorOfferStore(_tempDir, _loader);

            store.AddOffersToOverlay(new[]
            {
                MakeOffer("offer-1", 100, 50),
                MakeOffer("offer-2", 200, 75),
            });

            Assert.True(store.HasAnyOffer(100));
            Assert.True(store.HasAnyOffer(200));

            var updatedOffer = MakeOffer("offer-1", 100, 30);
            updatedOffer.MerchantName = "Updated";
            store.AddOffersToOverlay(new[]
            {
                updatedOffer,
                MakeOffer("offer-3", 300, 90),
            });

            Assert.Equal("Updated", store.GetOffersForItem(100)[0].MerchantName);
            Assert.Equal(30, store.GetOffersForItem(100)[0].CostLines[0].Count);
            Assert.True(store.HasAnyOffer(300));
        }

        [Fact]
        public void AddOffersToOverlay_SkipsNullOrEmptyOfferId()
        {
            var store = new VendorOfferStore(_tempDir, _loader);

            var nullIdOffer = MakeOffer(null, 100, 50);
            var emptyIdOffer = MakeOffer("", 200, 75);
            var validOffer = MakeOffer("valid", 300, 90);

            store.AddOffersToOverlay(new[] { nullIdOffer, emptyIdOffer, validOffer });

            Assert.False(store.HasAnyOffer(100));
            Assert.False(store.HasAnyOffer(200));
            Assert.True(store.HasAnyOffer(300));
        }

        [Fact]
        public void GetOffersForItems_ReturnsCorrectSubset()
        {
            var store = new VendorOfferStore(_tempDir, _loader);
            using (var stream = MakeDatasetStream(
                MakeOffer("a", 100, 10),
                MakeOffer("b", 200, 20),
                MakeOffer("c", 300, 30)))
            {
                store.LoadBaseline(stream);
            }

            var result = store.GetOffersForItems(new[] { 100, 200, 999 });

            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey(100));
            Assert.True(result.ContainsKey(200));
            Assert.False(result.ContainsKey(999));
        }

        [Fact]
        public void EmptyBaselineAndOverlay_ReturnsEmpty()
        {
            var store = new VendorOfferStore(_tempDir, _loader);
            store.LoadBaseline(null);
            store.LoadOverlay();

            Assert.Empty(store.GetOffersForItem(100));
            Assert.False(store.HasAnyOffer(100));
            Assert.Empty(store.GetOffersForItems(new[] { 100, 200 }));
        }

        [Fact]
        public void MultipleOffersForSameItem_SortedByOfferId()
        {
            var store = new VendorOfferStore(_tempDir, _loader);
            using (var stream = MakeDatasetStream(
                MakeOffer("zzz", 100, 50),
                MakeOffer("aaa", 100, 25),
                MakeOffer("mmm", 100, 75)))
            {
                store.LoadBaseline(stream);
            }

            var offers = store.GetOffersForItem(100);
            Assert.Equal(3, offers.Count);
            Assert.Equal("aaa", offers[0].OfferId);
            Assert.Equal("mmm", offers[1].OfferId);
            Assert.Equal("zzz", offers[2].OfferId);
        }

        [Fact]
        public void RebuildIndex_SkipsOffersWithNullOfferId()
        {
            var store = new VendorOfferStore(_tempDir, _loader);

            var dataset = new VendorOfferDataset
            {
                SchemaVersion = 1,
                GeneratedAt = "2026-01-01T00:00:00Z",
                Source = "test",
                Offers = new List<VendorOffer>
                {
                    new VendorOffer
                    {
                        OfferId = null,
                        OutputItemId = 100,
                        OutputCount = 1,
                        CostLines = new List<CostLine>(),
                    },
                    MakeOffer("valid", 200, 50),
                },
            };
            string json = _loader.Serialize(dataset);
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                store.LoadBaseline(stream);
            }

            Assert.False(store.HasAnyOffer(100));
            Assert.True(store.HasAnyOffer(200));
        }

        // --- Shipped Homestead Refinement tier
        // data invariant. VendorBatchSolver.EvaluateVendorOffers admits any offer
        // whose HomesteadTier is null at every configured tier setting -
        // correct for the 21 one-time "Upgrade" purchase rows the same
        // three merchant pages also sell, but WRONG for a material-
        // conversion row, which would then silently reintroduce the
        // always-max-tier defect PR #57 fixed. The solver has no
        // independent way to catch a mistagged row - this test is the
        // only defense against a future wiki re-scrape shipping one, since
        // otherwise the only existing check is a dev-time console warning
        // in tools/VendorOfferUpdater/Program.cs. Loads the REAL shipped
        // ref/vendor_offers.json through the production VendorOfferLoader,
        // following the FindRepoFile convention established in
        // AcquisitionHintServiceTests.
        [Fact]
        public void ShippedSeedFile_HomesteadRefinementMaterialRows_AllHaveNonNullTierInRange()
        {
            string path = FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/vendor_offers.json by walking up from the test assembly's directory.");

            using (var stream = File.OpenRead(path))
            {
                var dataset = _loader.Load(stream);

                var homesteadOffers = dataset.Offers
                    .Where(o => !string.IsNullOrEmpty(o.MerchantName) &&
                                o.MerchantName.Contains("Homestead Refinement"))
                    .ToList();

                var materialRows = homesteadOffers
                    .Where(o => Gw2Constants.HomesteadRefinementMaterialIds.Contains(o.OutputItemId))
                    .ToList();
                var nonMaterialRows = homesteadOffers
                    .Where(o => !Gw2Constants.HomesteadRefinementMaterialIds.Contains(o.OutputItemId))
                    .ToList();

                // Sanity bands, not exact literals. The tripwire for "the
                // shipped rows changed" is the sha256 in
                // ref/vendor_offers_manifest.json, asserted in
                // Load_ShippedDataset_... below; it covers every row in the
                // file rather than these three subsets, and only a
                // VendorOfferUpdater run can move it. What these bands are
                // for is the shape: a re-scrape that halved the Homestead
                // section, or one that left the 21 one-time Upgrade rows
                // outnumbering the material conversions, is wrong however
                // legitimately it was generated.
                Assert.InRange(homesteadOffers.Count, 150, 400);
                Assert.InRange(materialRows.Count, 150, 350);
                Assert.InRange(nonMaterialRows.Count, 10, 50);
                Assert.Equal(
                    homesteadOffers.Count, materialRows.Count + nonMaterialRows.Count);
                Assert.True(materialRows.Count > nonMaterialRows.Count);

                foreach (var offer in materialRows)
                {
                    Assert.True(
                        offer.HomesteadTier.HasValue,
                        $"Homestead Refinement material-conversion offer '{offer.OfferId}' " +
                        $"(output item {offer.OutputItemId}) has a null HomesteadTier. " +
                        "VendorBatchSolver.EvaluateVendorOffers admits a null-tier offer at every " +
                        "configured tier by design (that is correct for the 21 one-time " +
                        "Upgrade-purchase rows, not for a material conversion row), so this " +
                        "would silently reintroduce the always-max-tier defect PR #57 fixed.");
                    Assert.InRange(offer.HomesteadTier.Value, 0, 2);
                }

                // The 21 non-material rows are expected to be null-tier
                // (one-time Upgrade purchases are tier-independent by
                // design) - documented here so this test also notices if
                // that population unexpectedly starts carrying tiers.
                Assert.All(nonMaterialRows, o => Assert.Null(o.HomesteadTier));
            }
        }

        // --- The shipped ref/vendor_offers.json
        // (13.3MB) was already exercised through the production
        // VendorOfferLoader by the Homestead test above, but only for a
        // 237-row subset - this pins the loader's parse of the *entire*
        // file, guarding the ReadToEnd->DeserializeAsync(Stream)
        // switch against silent drift (this file has no leading BOM;
        // the switch here is purely the perf P2a change, see
        // VendorOfferLoader.Load).
        [Fact]
        public void ShippedSeedFile_VendorOfferLoader_ParsesAllOffers()
        {
            string path = FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/vendor_offers.json by walking up from the test assembly's directory.");

            using (var stream = File.OpenRead(path))
            {
                var dataset = _loader.Load(stream);

                Assert.Equal(1, dataset.SchemaVersion);
                // Astral Acclaim package: a scoped
                // Wizard's Vault re-scrape (--query + --merge-into) net
                // added 7 offers (100 removed/replaced, 107 added) while
                // seeding SeasonalCap - see the package's commit for the
                // full accounting.
                //
                // Festival-vendor auto-tagging follow-up: a
                // second scoped re-scrape (--query + --tag-seasonal-festivals
                // + --merge-into) targeting the six known festival vendors
                // OTHER than Candy Corn Vendor (Weekly) - Dragon Bash
                // Merchant (Weekly), Wintersday Trader (Weekly), Festival
                // Rewards Vendor (Weekly), Gauntlet Ticket Vendor, New Year
                // Vendor, Super Adventure Box Weekly Trader - reported net
                // +2 offers (52 removed/replaced, 54 added) while seeding
                // SeasonalFestival for those merchants (see
                // VendorOfferUpdater.Tests.SeasonalFestivalRoundTripTests
                // for the full seasonalFestival-tag accounting), but that
                // count concealed a real DATA LOSS: the scoped run's own
                // wiki_vendor_cache.json had 9 rows with GameId 0 (a wiki-
                // query defect, not the wiki actually dropping the items -
                // live-reconfirmed the game ids still resolve), and
                // MergeIntoBaseline's wholesale per-merchant replacement
                // silently deleted the 6 baseline offers those rows would
                // have replaced (outputItemId 64736/79431/86804, each for
                // both Wintersday Trader (Weekly) and Festival Rewards
                // Vendor (Weekly)) with no content-equivalent successor.
                // Restored here byte-for-byte from the pre-rescrape
                // baseline (merge-base 4735064) rather than re-guessed.
                // Offer count and bytes against what VendorOfferUpdater
                // recorded in ref/vendor_offers_manifest.json. The exact
                // literal that used to sit here, under a changelog of the
                // 53,544 -> 59,414 re-scrape, tripped on any change but
                // could not tell a legitimate refresh from a hand edit -
                // see ShippedSeedManifest. One row is refused by hand: see
                // ref/vendor_offer_exclusions.json.
                ShippedSeedManifest.AssertVendorOffersMatch(dataset.Offers.Count);
                Assert.InRange(dataset.Offers.Count, 40000, 120000);

                Assert.All(dataset.Offers, o =>
                {
                    Assert.False(string.IsNullOrEmpty(o.OfferId));
                    Assert.True(o.OutputItemId > 0);
                    Assert.True(o.OutputCount > 0);

                    // CostLines can legitimately be empty (e.g. free
                    // Jukebox tracks), so only shape/non-null is checked.
                    Assert.NotNull(o.CostLines);
                });

                // Keyed by CONTENT, not by OfferId. The 2026-08-25
                // from-scratch refresh proved an OfferId is NOT stable
                // across a full re-scrape: this exact row came back with
                // identical item, count, cost lines, merchant and location
                // under a different hash, because VendorOfferHasher's own
                // doc comment says a recompute appends hash segments
                // (homesteadTier, seasonalCap) that the baseline predates -
                // rows only kept their ids while --merge-into copied
                // untouched baseline objects through. Pinning the hash
                // therefore tripped on a migration rather than on the
                // content change this wire exists to catch.
                var knownOffer = dataset.Offers.Single(o =>
                    o.OutputItemId == 84618 &&
                    o.MerchantName == "Drojkor, Spirit Squall");
                Assert.Equal(1, knownOffer.OutputCount);
                Assert.Equal(
                    new[] { (2, 2800), (34, 20) },
                    knownOffer.CostLines
                        .Select(c => (c.Id, c.Count))
                        .OrderBy(c => c.Id)
                        .ToArray());
                Assert.Equal(64, knownOffer.OfferId.Length);
            }
        }

        // Neither suite previously
        // tied the shipped data's festival keys to the module's own
        // known-key/display-name table - VendorOfferUpdater.Tests.
        // SeasonalFestivalRoundTripTests only checks a hard-coded string
        // array local to that test project, and the assertion above only
        // pins the total offer count. Nothing would have caught a
        // seasonalFestival value shipping with no
        // Gw2Constants.FestivalDisplayNames entry (exactly Critical #2:
        // dragonbash/wintersday/festivalofthefourwinds/lunarnewyear/
        // superadventurefestival all fell through
        // ResolveFestivalDisplayName's raw-key fallback before that fix).
        // The six-key list below is independently measured the same way
        // as FestivalDisplayNames itself (see that field's own doc
        // comment) - kept as its own literal, not copied from
        // FestivalDisplayNames.Keys, so a future accidental deletion from
        // that dictionary is still caught rather than the test silently
        // agreeing with whatever the dictionary currently contains.
        [Fact]
        public void ShippedSeedFile_EveryDistinctSeasonalFestivalValue_HasDisplayNameAndIsKnownFestivalKey()
        {
            var knownFestivalContextKeys = new HashSet<string>(StringComparer.Ordinal)
            {
                "halloween", "dragonbash", "wintersday",
                "festivalofthefourwinds", "lunarnewyear", "superadventurefestival",
            };

            string path = FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/vendor_offers.json by walking up from the test assembly's directory.");

            using (var stream = File.OpenRead(path))
            {
                var dataset = _loader.Load(stream);

                var distinctFestivals = dataset.Offers
                    .Select(o => o.SeasonalFestival)
                    .Where(f => !string.IsNullOrEmpty(f))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                Assert.NotEmpty(distinctFestivals);

                Assert.All(distinctFestivals, festival =>
                {
                    Assert.True(
                        Gw2Constants.FestivalDisplayNames.ContainsKey(festival),
                        $"Shipped seasonalFestival value \"{festival}\" has no " +
                        "Gw2Constants.FestivalDisplayNames entry - a Plan Notes tip for " +
                        "it would render the raw internal key verbatim.");
                    Assert.True(
                        knownFestivalContextKeys.Contains(festival),
                        $"Shipped seasonalFestival value \"{festival}\" is not one of the " +
                        "six known Blish HUD FestivalContext keys.");
                });
            }
        }

        // --- onError callback: real IO failure. ---
        [Fact]
        public void LoadBaseline_StreamThrows_InvokesOnErrorInsteadOfThrowing()
        {
            string capturedMessage = null;
            Exception capturedException = null;
            var store = new VendorOfferStore(_tempDir, _loader, (message, ex) =>
            {
                capturedMessage = message;
                capturedException = ex;
            });

            using (var badStream = new MemoryStream(Encoding.UTF8.GetBytes("not valid json")))
            {
                store.LoadBaseline(badStream);
            }

            Assert.NotNull(capturedMessage);
            Assert.NotNull(capturedException);
            Assert.False(store.HasAnyOffer(100));
        }

        [Fact]
        public void LoadBaseline_NoOnErrorProvided_DoesNotThrowOnFailure()
        {
            var store = new VendorOfferStore(_tempDir, _loader);

            using (var badStream = new MemoryStream(Encoding.UTF8.GetBytes("not valid json")))
            {
                store.LoadBaseline(badStream); // no-op onError default - must not throw
            }
        }

        // Astral Acclaim package: pins SeasonalCap for
        // the two task-named Wizard's Vault rows, mirroring the guard
        // docs/research/m37-r4-vendor-caps.md section 4f recommended for
        // the analogous daily/weekly case (item 28's Candy-Corn-Ecto
        // WeeklyCap pin). The count assertion above only catches a row
        // being added/removed, not its SeasonalCap value silently
        // changing or dropping to null on a future scoped re-scrape - the
        // exact class of bug this same package's own Lesser Essence of
        // Gold gap proved possible for one row. Keyed by stable OfferId
        // per this file's established convention.
        [Fact]
        public void ShippedSeedFile_WizardsVaultSeasonalCaps_MysticCloverAndMysticCoin()
        {
            string path = FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/vendor_offers.json by walking up from the test assembly's directory.");

            using (var stream = File.OpenRead(path))
            {
                var dataset = _loader.Load(stream);

                var mysticClover = dataset.Offers.Single(o =>
                    o.OfferId == "a30ae3708b74c8c4675f733cd5f0abe6737683eaf8fe5740ebba3bbc9c3c3ec7");
                Assert.Equal(19675, mysticClover.OutputItemId);
                Assert.Equal("Wizard's Vault", mysticClover.MerchantName);
                Assert.Equal(20, mysticClover.SeasonalCap);

                var mysticCoin = dataset.Offers.Single(o =>
                    o.OfferId == "e1721409d2879cf8bc6063fb449e21a5fbbf1649ad1d2ea84a3d679903c3b8ef");
                Assert.Equal(19976, mysticCoin.OutputItemId);
                Assert.Equal("Wizard's Vault", mysticCoin.MerchantName);
                Assert.Equal(60, mysticCoin.SeasonalCap);
            }
        }

        // FindRepoFile comes from Helpers/RepoFileLocator.cs.
    }
}
