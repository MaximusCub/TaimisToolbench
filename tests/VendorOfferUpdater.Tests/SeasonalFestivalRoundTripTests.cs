using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using VendorOfferUpdater;
using VendorOfferUpdater.Models;
using Xunit;
// FindRepoFile comes from Helpers/RepoFileLocator.cs.
using static VendorOfferUpdater.Tests.Helpers.RepoFileLocator;

namespace VendorOfferUpdater.Tests
{
    /// <summary>
    /// Guards the exact regression: VendorOfferUpdater.Models.VendorOffer used
    /// to have no SeasonalFestival property, so a --merge-into run
    /// (Program.MergeIntoBaseline, reached via Program.cs's
    /// deserialize-the-whole-baseline-through-this-model then re-serialize
    /// flow) silently dropped the tag from every offer it re-serialized, even
    /// one scraping an unrelated merchant - with no OfferId change to make the
    /// loss noticeable, since SeasonalFestival is deliberately not hashed by
    /// VendorOfferHasher.
    ///
    /// Loads the REAL shipped ref/vendor_offers.json (not a fixture) through
    /// the tool's own model, exactly like Program.cs's --merge-into baseline
    /// read, merges in an unrelated fresh merchant's offer, and asserts every
    /// known tagged offer still carries seasonalFestival afterward - including
    /// three Candy Corn Vendor (Weekly) offer IDs that must survive
    /// byte-identically. Which vendors that baseline has tagged, how they were
    /// tagged, and why coverage is deliberately partial: KNOWN-ISSUES #63 and
    /// docs/ARCHITECTURE.md section T.8.
    /// </summary>
    public class SeasonalFestivalRoundTripTests
    {
        private static readonly JsonSerializerOptions ReadOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        // The three ORIGINAL hand-tagged Candy Corn Vendor (Weekly) ecto
        // offer IDs - deliberately excluded from the festival-vendor
        // auto-tagging live run (see class doc comment) so these three
        // exact hashes must never change.
        // Taken from the shipped baseline at run time rather than pinned
        // as literals. The 2026-08-25 from-scratch refresh recomputed every
        // hash (VendorOfferHasher's doc comment: a recompute appends hash
        // segments the old baseline predates), so pinned ids tripped on a
        // migration instead of on the thing this test guards - that these
        // rows survive an unrelated --merge-into run carrying their tag.
        private static string[] CandyCornOfferIds(VendorOfferDataset baseline)
        {
            return baseline.Offers
                .Where(o => o.MerchantName == "Candy Corn Vendor (Weekly)" &&
                            !string.IsNullOrEmpty(o.SeasonalFestival))
                .Select(o => o.OfferId)
                .ToArray();
        }

        [Fact]
        public void ShippedBaseline_SeasonalFestivalTag_SurvivesUnrelatedMergeIntoRun()
        {
            string path = FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/vendor_offers.json by walking up from the test assembly's directory.");

            string baselineJson = File.ReadAllText(path);
            var baseline = JsonSerializer.Deserialize<VendorOfferDataset>(baselineJson, ReadOptions);
            Assert.NotNull(baseline);

            var seasonalBefore = baseline!.Offers
                .Where(o => !string.IsNullOrEmpty(o.SeasonalFestival))
                .ToList();

            // Pin the exact known count (a drop here means the
            // DESERIALIZE side already lost tags, before MergeIntoBaseline
            // is even involved) and the exact set of festival keys now
            // seeded - see class doc comment for the six-festival live run
            // that produced this count.
            // 57 -> 597 -> 611. 597 came from a full scrape with
            // --tag-seasonal-festivals over every vendor page, ten times a
            // scoped six-festival run; 611 from the rebuild that partitions
            // the vendor list by name prefix and so reaches pages the
            // unpartitioned query truncated away. Same six festivals both
            // times. The guard this pins is unchanged - a DROP still means
            // the deserialize side lost tags before any merge ran.
            Assert.Equal(611, seasonalBefore.Count);
            Assert.All(
                seasonalBefore,
                o => Assert.Contains(
                    o.SeasonalFestival,
                    new[]
                    {
                        "halloween", "dragonbash", "wintersday",
                        "festivalofthefourwinds", "lunarnewyear", "superadventurefestival",
                    }));
            // 3 -> 9 on the same full refresh: the scoped run had only
            // reached three of this vendor's rows. The tag itself is what
            // matters and is asserted below.
            Assert.Equal(9, seasonalBefore.Count(o => o.MerchantName == "Candy Corn Vendor (Weekly)"));
            Assert.All(
                seasonalBefore.Where(o => o.MerchantName == "Candy Corn Vendor (Weekly)"),
                o => Assert.Equal("halloween", o.SeasonalFestival));

            // Simulate a --merge-into run that re-scrapes a completely
            // unrelated merchant - the exact scenario the finding
            // describes ("even one scraping an unrelated merchant").
            var fresh = new List<VendorOffer>
            {
                new VendorOffer
                {
                    OfferId = "fresh-unrelated-merchant-offer",
                    OutputItemId = 12345,
                    OutputCount = 1,
                    CostLines = new List<CostLine>(),
                    MerchantName = "Some Unrelated Merchant",
                    Locations = new List<string>(),
                },
            };

            var result = Program.MergeIntoBaseline(baseline.Offers, fresh);

            var seasonalAfter = result.Merged
                .Where(o => !string.IsNullOrEmpty(o.SeasonalFestival))
                .ToList();

            Assert.Equal(611, seasonalAfter.Count);
            foreach (var offerId in CandyCornOfferIds(baseline))
            {
                var offer = result.Merged.SingleOrDefault(o => o.OfferId == offerId);
                Assert.True(offer != null, $"Offer {offerId} should survive an unrelated --merge-into run untouched.");
                Assert.Equal("halloween", offer!.SeasonalFestival);
            }
        }
    }
}
