using System.Collections.Generic;
using System.Linq;
using VendorOfferUpdater;
using VendorOfferUpdater.Models;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    /// <summary>
    /// The diff report is the only reviewable artifact a `data(vendor):` change
    /// produces - `git diff` on ref/vendor_offers.json is one 14.8MB line. If it
    /// miscounts, or classifies a repricing as an unrelated add plus remove, a
    /// reviewer reads a summary that is worse than none.
    /// </summary>
    public class VendorOfferDiffTests
    {
        private static VendorOffer Offer(
            int itemId,
            string merchant,
            int coinCost,
            int outputCount = 1,
            string festival = null,
            int? weeklyCap = null,
            VendorRequirement requirement = null)
        {
            var offer = new VendorOffer
            {
                Requirement = requirement,
                OutputItemId = itemId,
                OutputCount = outputCount,
                MerchantName = merchant,
                Locations = null,
                WeeklyCap = weeklyCap,
                SeasonalFestival = festival,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = 1, Count = coinCost },
                },
            };

            // The real dataset's offerId is a content hash over every field but
            // SeasonalFestival. Computing it the same way here is what makes
            // these tests exercise the classification the tool actually faces:
            // a price change genuinely produces a different id, not a mutation.
            offer.OfferId = VendorOfferHasher.ComputeOfferId(
                offer.OutputItemId,
                offer.OutputCount,
                offer.CostLines,
                offer.MerchantName,
                offer.Locations,
                offer.DailyCap,
                offer.WeeklyCap,
                offer.HomesteadTier,
                offer.SeasonalCap);

            return offer;
        }

        [Fact]
        public void IdenticalDatasets_ReportNoChange()
        {
            var a = new List<VendorOffer> { Offer(1, "Miyani", 100), Offer(2, "Miyani", 200) };
            var b = new List<VendorOffer> { Offer(1, "Miyani", 100), Offer(2, "Miyani", 200) };

            var result = VendorOfferDiff.Compute(a, b);

            Assert.True(result.IsEmpty);
            Assert.Contains("No offer changed", VendorOfferDiff.Format(result, "old", "new"));
        }

        /// <summary>
        /// The whole reason this class exists. offerId hashes the cost, so a
        /// price move deletes one id and creates another; reported raw, that is
        /// two unrelated hex strings instead of "this got more expensive".
        /// </summary>
        [Fact]
        public void PriceChange_IsOneRepricing_NotAnAddPlusARemove()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100) };
            var after = new List<VendorOffer> { Offer(1, "Miyani", 250) };

            Assert.NotEqual(before[0].OfferId, after[0].OfferId);

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Single(result.Repriced);
            Assert.Equal(100, result.Repriced[0].Before.CostLines[0].Count);
            Assert.Equal(250, result.Repriced[0].After.CostLines[0].Count);
            Assert.Contains("100x currency 1 -> 250x currency 1",
                VendorOfferDiff.Format(result, "old", "new"));
        }

        [Fact]
        public void CapChangeWithSamePrice_IsAlsoARepricing()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100) };
            var after = new List<VendorOffer> { Offer(1, "Miyani", 100, weeklyCap: 5) };

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Single(result.Repriced);
            Assert.Contains("weekly cap 5", VendorOfferDiff.Format(result, "old", "new"));
        }

        [Fact]
        public void NewAndDroppedMerchantItemPairs_AreAddsAndRemoves()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100), Offer(2, "Miyani", 100) };
            var after = new List<VendorOffer> { Offer(1, "Miyani", 100), Offer(3, "Arriske", 50) };

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Single(result.Added);
            Assert.Equal(3, result.Added[0].OutputItemId);
            Assert.Single(result.Removed);
            Assert.Equal(2, result.Removed[0].OutputItemId);
            Assert.Empty(result.Repriced);
        }

        /// <summary>
        /// SeasonalFestival is the one field the offerId hash excludes, so a
        /// retag keeps the id. Reporting it as "nothing changed" would hide a
        /// tag regression, which is a real failure mode this dataset has had.
        /// </summary>
        [Fact]
        public void SeasonalFestivalRetag_KeepsTheIdAndIsReportedSeparately()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100) };
            var after = new List<VendorOffer> { Offer(1, "Miyani", 100, festival: "Wintersday") };

            Assert.Equal(before[0].OfferId, after[0].OfferId);

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Single(result.Retagged);
            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Empty(result.Repriced);
            Assert.Contains("festival (none), requirement (none)",
                VendorOfferDiff.Format(result, "old", "new"));
            Assert.Contains("festival Wintersday, requirement (none)",
                VendorOfferDiff.Format(result, "old", "new"));
        }

        /// <summary>
        /// The offerId is a hash the tool computes, not a guarantee the data
        /// carries. A hand-edited row keeps its old id while its price moves;
        /// trusting the id alone would report that row as unchanged, in the one
        /// situation a reviewer most needs the report not to lie.
        /// </summary>
        [Fact]
        public void SameIdWithDifferentContent_IsStillReportedAsARepricing()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100) };
            var after = new List<VendorOffer> { Offer(1, "Miyani", 100) };
            after[0].CostLines[0].Count = 4000; // hand-edit: id left stale

            Assert.Equal(before[0].OfferId, after[0].OfferId);

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Single(result.Repriced);
            Assert.Empty(result.Retagged);
            Assert.Contains("100x currency 1 -> 4000x currency 1",
                VendorOfferDiff.Format(result, "old", "new"));
        }

        /// <summary>
        /// VendorOfferHasher's own comment records that recomputing the hash
        /// changes every id in the dataset at once. Reported as repricings that
        /// is tens of thousands of lines whose before and after are identical,
        /// burying the handful of rows a reviewer actually has to check.
        /// </summary>
        [Fact]
        public void HashFormatChange_IsRehashed_NotRepriced()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100), Offer(2, "Miyani", 200) };
            var after = new List<VendorOffer> { Offer(1, "Miyani", 100), Offer(2, "Miyani", 200) };
            foreach (var offer in after)
            {
                offer.OfferId = "stale-format-" + offer.OutputItemId;
            }

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Empty(result.Repriced);
            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Empty(result.Retagged);
            Assert.Equal(2, result.Rehashed);
            Assert.True(result.IsEmpty);

            string report = VendorOfferDiff.Format(result, "old", "new");
            Assert.Contains("rehashed: 2", report);
            Assert.Contains("the file moved without a data change", report);
        }

        /// <summary>
        /// One merchant can sell the same item at several output counts (the
        /// live dataset's "Cannibal" | item 67389 rows are x1/x3/x8). Pairing
        /// those on (merchant, item) alone pairs the x1 row's old cost against
        /// the x3 row's new cost, inventing a price move in a row nobody
        /// touched.
        /// </summary>
        [Fact]
        public void RowsDifferingOnlyInOutputCount_AreNotCrossPairedIntoRepricings()
        {
            var counts = new[] { 1, 3, 8 };
            var before = counts.Select(c => Offer(67389, "Cannibal", c * 10, outputCount: c)).ToList();
            var after = counts.Select(c => Offer(67389, "Cannibal", c * 10, outputCount: c)).ToList();
            foreach (var offer in after)
            {
                offer.OfferId = "stale-format-x" + offer.OutputCount;
            }

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Empty(result.Repriced);
            Assert.Empty(result.Added);
            Assert.Empty(result.Removed);
            Assert.Equal(3, result.Rehashed);
        }

        /// <summary>
        /// A rehash must not swallow a real change arriving in the same run:
        /// the reviewer still has to see the one row that actually moved.
        /// </summary>
        [Fact]
        public void RealRepricingIsStillReportedAlongsideARehash()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100), Offer(2, "Miyani", 200) };
            var after = new List<VendorOffer> { Offer(1, "Miyani", 100), Offer(2, "Miyani", 999) };
            foreach (var offer in after)
            {
                offer.OfferId = "stale-format-" + offer.OutputItemId;
            }

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Equal(1, result.Rehashed);
            Assert.Single(result.Repriced);
            Assert.Equal(2, result.Repriced[0].After.OutputItemId);
            Assert.Contains("200x currency 1 -> 999x currency 1",
                VendorOfferDiff.Format(result, "old", "new"));
        }

        /// <summary>
        /// SeasonalFestival sits outside both the hash and the content key, so a
        /// row that is rehashed AND retagged in one run must still report the
        /// retag - a dropped festival tag is a regression this dataset has had.
        /// </summary>
        [Fact]
        public void RehashedRowThatAlsoChangedFestival_IsStillReportedAsARetag()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100, festival: "Wintersday") };
            var after = new List<VendorOffer> { Offer(1, "Miyani", 100) };
            after[0].OfferId = "stale-format-1";

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Equal(1, result.Rehashed);
            Assert.Single(result.Retagged);
            Assert.Empty(result.Repriced);
            Assert.False(result.IsEmpty);
            Assert.Contains("festival Wintersday, requirement (none)",
                VendorOfferDiff.Format(result, "old", "new"));
            Assert.Contains("-> festival (none), requirement (none)",
                VendorOfferDiff.Format(result, "old", "new"));
        }

        /// <summary>
        /// A requirement is outside the hash exactly as the festival tag is,
        /// so back-filling one onto a shipped row changes no offerId. Without
        /// TagKey covering it, the whole back-fill would appear in the report
        /// as nothing at all.
        /// </summary>
        [Fact]
        public void RequirementBackFill_KeepsTheIdAndIsReportedAsARetag()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100) };
            var after = new List<VendorOffer>
            {
                Offer(
                    1, "Miyani", 100,
                    requirement: new VendorRequirement
                    {
                        Text = "Nuhoch Language",
                        MasteryId = 3,
                        MasteryLevel = 2,
                    }),
            };

            Assert.Equal(before[0].OfferId, after[0].OfferId);

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Single(result.Retagged);
            Assert.Empty(result.Repriced);
            Assert.Contains("requirement Nuhoch Language",
                VendorOfferDiff.Format(result, "old", "new"));
            Assert.Contains("mastery 3/2",
                VendorOfferDiff.Format(result, "old", "new"));
        }

        [Fact]
        public void SurplusRowsForOneMerchantItemPair_FallThroughAsAddsAndRemoves()
        {
            var before = new List<VendorOffer> { Offer(1, "Miyani", 100) };
            var after = new List<VendorOffer>
            {
                Offer(1, "Miyani", 150),
                Offer(1, "Miyani", 150, outputCount: 5),
            };

            var result = VendorOfferDiff.Compute(before, after);

            Assert.Single(result.Repriced);
            Assert.Single(result.Added);
            Assert.Empty(result.Removed);
        }

        [Fact]
        public void EmptyAndNullInputs_AreHandledWithoutThrowing()
        {
            Assert.True(VendorOfferDiff.Compute(null, null).IsEmpty);

            var added = VendorOfferDiff.Compute(
                new List<VendorOffer>(), new List<VendorOffer> { Offer(1, "Miyani", 100) });
            Assert.Single(added.Added);
            Assert.Equal(0, added.OldCount);
            Assert.Equal(1, added.NewCount);

            var removed = VendorOfferDiff.Compute(
                new List<VendorOffer> { Offer(1, "Miyani", 100) }, new List<VendorOffer>());
            Assert.Single(removed.Removed);
        }

        [Fact]
        public void CountsInTheHeaderAreExactEvenWhenTheListingIsTruncated()
        {
            int rows = VendorOfferDiff.MaxListedPerSection + 7;
            var after = Enumerable.Range(1, rows)
                .Select(i => Offer(i, "Miyani", 100))
                .ToList();

            var result = VendorOfferDiff.Compute(new List<VendorOffer>(), after);
            string report = VendorOfferDiff.Format(result, "old", "new");

            Assert.Equal(rows, result.Added.Count);
            Assert.Contains($"added:    {rows}", report);
            Assert.Contains("... and 7 more", report);
        }

        /// <summary>
        /// Two runs over the same data must produce the same report text, or
        /// the summary cannot be pasted into a PR body and trusted.
        /// </summary>
        [Fact]
        public void ReportTextIsStableAcrossRuns()
        {
            var before = new List<VendorOffer>
            {
                Offer(2, "Zho", 100), Offer(1, "Miyani", 100), Offer(3, "Arriske", 100),
            };
            var after = new List<VendorOffer>
            {
                Offer(3, "Arriske", 200), Offer(1, "Miyani", 300), Offer(9, "Zho", 100),
            };

            string first = VendorOfferDiff.Format(VendorOfferDiff.Compute(before, after), "a", "b");
            string second = VendorOfferDiff.Format(VendorOfferDiff.Compute(before, after), "a", "b");

            Assert.Equal(first, second);
        }
    }
}
