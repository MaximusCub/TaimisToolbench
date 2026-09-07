using System.Collections.Generic;
using VendorOfferUpdater;
using VendorOfferUpdater.Models;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    // Program.MergeIntoBaseline is what
    // "regenerate ONLY those pages' rows" actually means at the data
    // level - a scoped re-scrape of a handful of merchants must replace
    // only those merchants' offers in the full baseline, leaving every
    // other merchant's rows byte-for-byte untouched.
    public class MergeIntoBaselineTests
    {
        private static VendorOffer MakeOffer(
            string offerId, string merchantName, int outputItemId = 1,
            string seasonalFestival = null)
        {
            return new VendorOffer
            {
                OfferId = offerId,
                OutputItemId = outputItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>(),
                MerchantName = merchantName,
                Locations = new List<string>(),
                SeasonalFestival = seasonalFestival,
            };
        }

        [Fact]
        public void FreshMerchant_ReplacesAllBaselineOffersForThatMerchant()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("a", "Homestead Refinement\u2014Farm"),
                MakeOffer("b", "Homestead Refinement\u2014Farm"),
                MakeOffer("c", "Miyani"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("d", "Homestead Refinement\u2014Farm"),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Equal(2, result.RemovedFromBaseline);
            Assert.Equal(2, result.Merged.Count);
            Assert.Contains(result.Merged, o => o.OfferId == "c");
            Assert.Contains(result.Merged, o => o.OfferId == "d");
            Assert.DoesNotContain(result.Merged, o => o.OfferId == "a" || o.OfferId == "b");
        }

        [Fact]
        public void UntouchedMerchants_PassThroughUnchanged()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("a", "Homestead Refinement\u2014Farm"),
                MakeOffer("c", "Miyani"),
                MakeOffer("d", "Battle Master"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("e", "Homestead Refinement\u2014Farm"),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Contains(result.Merged, o => o.OfferId == "c");
            Assert.Contains(result.Merged, o => o.OfferId == "d");
            Assert.Equal(3, result.Merged.Count);
        }

        [Fact]
        public void StaleBaselineRowForReplacedMerchant_RemovedEvenIfFreshQueryDidNotReFindIt()
        {
            // The fresh query found 1 row for the merchant; the baseline
            // had 2 - the extra one must be dropped (it is stale), not kept
            // alongside the fresh row.
            var baseline = new List<VendorOffer>
            {
                MakeOffer("stale1", "Homestead Refinement\u2014Metal Forge"),
                MakeOffer("stale2", "Homestead Refinement\u2014Metal Forge"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("fresh1", "Homestead Refinement\u2014Metal Forge"),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Equal(2, result.RemovedFromBaseline);
            Assert.Single(result.Merged);
            Assert.Equal("fresh1", result.Merged[0].OfferId);
        }

        [Fact]
        public void MultipleFreshMerchants_EachReplacedIndependently()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("a", "Homestead Refinement\u2014Farm"),
                MakeOffer("b", "Homestead Refinement\u2014Lumber Mill"),
                MakeOffer("c", "Miyani"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("d", "Homestead Refinement\u2014Farm"),
                MakeOffer("e", "Homestead Refinement\u2014Lumber Mill"),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Equal(2, result.RemovedFromBaseline);
            Assert.Equal(2, result.MerchantNamesReplaced.Count);
            Assert.Equal(3, result.Merged.Count);
        }

        [Fact]
        public void EmptyFresh_LeavesBaselineCompletelyUnchanged()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("a", "Homestead Refinement\u2014Farm"),
                MakeOffer("b", "Miyani"),
            };

            var result = Program.MergeIntoBaseline(baseline, new List<VendorOffer>());

            Assert.Equal(0, result.RemovedFromBaseline);
            Assert.Equal(2, result.Merged.Count);
        }

        [Fact]
        public void EmptyBaseline_MergedIsJustFresh()
        {
            var fresh = new List<VendorOffer>
            {
                MakeOffer("a", "Homestead Refinement\u2014Farm"),
            };

            var result = Program.MergeIntoBaseline(new List<VendorOffer>(), fresh);

            Assert.Equal(0, result.RemovedFromBaseline);
            Assert.Single(result.Merged);
        }

        [Fact]
        public void MergedResult_IsSortedByOfferId()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("zzz", "Miyani"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("bbb", "Homestead Refinement\u2014Farm"),
                MakeOffer("aaa", "Homestead Refinement\u2014Farm"),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Equal(new[] { "aaa", "bbb", "zzz" },
                result.Merged.ConvertAll(o => o.OfferId));
        }

        [Fact]
        public void NullBaselineOrFresh_TreatedAsEmpty()
        {
            var result1 = Program.MergeIntoBaseline(null, new List<VendorOffer> { MakeOffer("a", "M") });
            Assert.Single(result1.Merged);

            var result2 = Program.MergeIntoBaseline(new List<VendorOffer> { MakeOffer("a", "M") }, null);
            Assert.Single(result2.Merged);
        }

        // DATA LOSS fix: a merchant flagged as having had a
        // GameId<=0 row this pass must NOT have its baseline offers
        // dropped, even though it also appears in the fresh batch -
        // exactly the mechanism that silently deleted 6 shipped offers in
        // a real run (Program.cs's own GameId<=0 filter meant the fresh
        // batch for that merchant was known-incomplete).
        [Fact]
        public void ProtectedMerchant_BaselineOffersSurviveAlongsideFreshOnes()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("stale-a", "Festival Rewards Vendor (Weekly)", 1),
                MakeOffer("stale-b", "Festival Rewards Vendor (Weekly)", 2),
                MakeOffer("c", "Miyani", 3),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("fresh-a", "Festival Rewards Vendor (Weekly)", 4),
            };
            var skipped = new HashSet<string> { "Festival Rewards Vendor (Weekly)" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Equal(0, result.RemovedFromBaseline);
            Assert.Equal(4, result.Merged.Count);
            Assert.Contains(result.Merged, o => o.OfferId == "stale-a");
            Assert.Contains(result.Merged, o => o.OfferId == "stale-b");
            Assert.Contains(result.Merged, o => o.OfferId == "fresh-a");
            Assert.Contains(result.Merged, o => o.OfferId == "c");
            Assert.Equal(
                new[] { "Festival Rewards Vendor (Weekly)" },
                result.MerchantNamesProtected);
            Assert.Empty(result.MerchantNamesReplaced);
        }

        // A run can have BOTH an unaffected merchant (wholesale-replaced
        // as normal) and a protected one (skipped-row merchant) in the
        // same fresh batch - each merchant's own outcome must be
        // independent.
        [Fact]
        public void MixOfProtectedAndUnprotectedMerchants_EachHandledIndependently()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("stale-protected", "Wintersday Trader (Weekly)", 1),
                MakeOffer("stale-replaced", "Homestead Refinement\u2014Farm", 2),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("fresh-protected", "Wintersday Trader (Weekly)", 3),
                MakeOffer("fresh-replaced", "Homestead Refinement\u2014Farm", 4),
            };
            var skipped = new HashSet<string> { "Wintersday Trader (Weekly)" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Equal(1, result.RemovedFromBaseline);
            Assert.Contains(result.Merged, o => o.OfferId == "stale-protected");
            Assert.DoesNotContain(result.Merged, o => o.OfferId == "stale-replaced");
            Assert.Contains(result.Merged, o => o.OfferId == "fresh-protected");
            Assert.Contains(result.Merged, o => o.OfferId == "fresh-replaced");
            Assert.Equal(
                new[] { "Wintersday Trader (Weekly)" }, result.MerchantNamesProtected);
            Assert.Equal(
                new[] { "Homestead Refinement\u2014Farm" }, result.MerchantNamesReplaced);
        }

        // The comment this test used to
        // carry ("Same OfferId means content-identical") is WRONG -
        // VendorOffer.SeasonalFestival is deliberately NOT hashed into
        // OfferId (see VendorOffer.SeasonalFestival's own doc comment), so
        // a baseline row and a freshly-tagged row for the identical
        // offer share an OfferId while differing in exactly the field this
        // whole pass exists to add. The union must not just dedupe to one
        // row - it must keep the FRESH row, so the surviving copy actually
        // carries the new tag rather than silently reverting to the
        // baseline's untagged copy (the old kept.Concat(fresh) ordering
        // let the baseline win every collision).
        [Fact]
        public void MergedResult_DedupesByOfferId_PreferringFreshRow_WhenProtectedBaselineAndFreshShareAnId()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("shared", "Festival Rewards Vendor (Weekly)", 1),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("shared", "Festival Rewards Vendor (Weekly)", 1, seasonalFestival: "dragonbash"),
            };
            var skipped = new HashSet<string> { "Festival Rewards Vendor (Weekly)" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Single(result.Merged);
            Assert.Equal("dragonbash", result.Merged[0].SeasonalFestival);
        }

        // Companion to the fix above: a protected merchant's baseline row
        // can predate a VendorOfferHasher hash-format change (see that
        // file's own doc comment) and so carry a DIFFERENT OfferId than a
        // content-identical fresh row - the OfferId-based GroupBy above
        // does not catch that case, so MergeIntoBaseline also dedupes
        // protected-merchant rows by content (ComputeContentKey, which
        // deliberately excludes SeasonalFestival), keeping the copy that
        // carries the fresh tag.
        [Fact]
        public void ProtectedMerchant_DedupesByContent_WhenOfferIdDiffersButContentMatches()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("old-hash-format", "Festival Rewards Vendor (Weekly)", 1),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("new-hash-format", "Festival Rewards Vendor (Weekly)", 1, seasonalFestival: "dragonbash"),
            };
            var skipped = new HashSet<string> { "Festival Rewards Vendor (Weekly)" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Single(result.Merged);
            Assert.Equal("dragonbash", result.Merged[0].SeasonalFestival);
        }

        // Data-loss fix: mirror image of
        // MergedResult_DedupesByOfferId_PreferringFreshRow_WhenProtectedBaselineAndFreshShareAnId
        // above. That test covers a TAGGED fresh row winning over an
        // untagged baseline row on an OfferId collision (correct: no data
        // lost, the tag is new information this run added). This test
        // covers the opposite and previously-broken direction: an
        // UNTAGGED fresh row (e.g. from a page whose wikitext fetch
        // transiently missed this run) colliding on OfferId with a
        // TAGGED baseline row. Before the fix, g.First() picked the fresh
        // row unconditionally and its lack of a tag silently overwrote
        // the shipped baseline tag - exactly the "never silently deletes
        // shipped data" charter (MergeIntoBaseline's own doc comment)
        // this whole protected-merchant mechanism exists to uphold. The
        // fresh row must still win (it may carry other updated fields),
        // but the baseline's tag must be carried forward onto it.
        [Fact]
        public void MergedResult_DedupesByOfferId_CarriesBaselineTagForward_WhenFreshRowSharesIdButIsUntagged()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("shared", "Festival Rewards Vendor (Weekly)", 1, seasonalFestival: "dragonbash"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("shared", "Festival Rewards Vendor (Weekly)", 1),
            };
            var skipped = new HashSet<string> { "Festival Rewards Vendor (Weekly)" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Single(result.Merged);
            Assert.Equal("dragonbash", result.Merged[0].SeasonalFestival);
        }

        // When the content-key dedupe pass
        // resolves a collision to the baseline row (because it carries
        // the tag and the fresh row does not), the surviving row must not
        // be pinned to the baseline's stale, pre-hash-format-change
        // OfferId when a current-format OfferId (the fresh row's) is
        // available - it should migrate onto the fresh id instead of
        // shipping the old hash forever.
        [Fact]
        public void ProtectedMerchant_ContentKeyWinner_MigratesToFreshOfferId_WhenBaselineRowWinsOnTag()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("old-hash-format", "Festival Rewards Vendor (Weekly)", 1, seasonalFestival: "dragonbash"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("new-hash-format", "Festival Rewards Vendor (Weekly)", 1),
            };
            var skipped = new HashSet<string> { "Festival Rewards Vendor (Weekly)" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Single(result.Merged);
            Assert.Equal("dragonbash", result.Merged[0].SeasonalFestival);
            Assert.Equal("new-hash-format", result.Merged[0].OfferId);
        }

        // Data-loss fix:
        // `kept` used to drop EVERY replaced (non-protected) merchant's
        // baseline rows outright, before the fresh/kept GroupBy tag-carry
        // logic above ever ran - that logic only ever sees a baseline row
        // for PROTECTED merchants. This measured scenario is a
        // --pass2-only run against a pre-tagging wiki_vendor_cache.json:
        // the merchant is NOT protected (no skippedRows), its baseline row
        // is already tagged from a prior --tag-seasonal-festivals run, and
        // the fresh row (recomputed from the pre-tagging cache) carries no
        // tag at all. The tag must survive by being harvested from the
        // baseline row before it is dropped and carried onto the matching
        // fresh row - the SAME OfferId here (a stable hash across the two
        // runs), so this covers the OfferId-keyed side of the harvest
        // lookup.
        [Fact]
        public void UnprotectedMerchant_HarvestsBaselineTag_OntoUntaggedFreshRow_ByOfferId()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("shared-id", "Miyani", 1, seasonalFestival: "halloween"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("shared-id", "Miyani", 1),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Single(result.Merged);
            Assert.Equal("halloween", result.Merged[0].SeasonalFestival);
        }

        // Companion to the above: covers the content-key side of the
        // harvest lookup, for a baseline row that predates a
        // VendorOfferHasher hash-format change and so carries a DIFFERENT
        // OfferId than its content-identical fresh counterpart. The OfferId
        // lookup alone would miss this pair; ComputeContentKey (which
        // deliberately excludes SeasonalFestival) still matches them.
        [Fact]
        public void UnprotectedMerchant_HarvestsBaselineTag_OntoUntaggedFreshRow_ByContentKey_WhenOfferIdDiffers()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("old-hash-format", "Miyani", 1, seasonalFestival: "halloween"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("new-hash-format", "Miyani", 1),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Single(result.Merged);
            Assert.Equal("halloween", result.Merged[0].SeasonalFestival);
            Assert.Equal("new-hash-format", result.Merged[0].OfferId);
        }

        // Non-overwrite companion: a fresh row for an unprotected merchant
        // that already carries its OWN (possibly different) tag must never
        // be overwritten by the harvested baseline tag - fresh always wins
        // when both sides are tagged, same rule as the protected-merchant
        // path (MergedResult_DedupesByOfferId_PreferringFreshRow_...
        // above).
        [Fact]
        public void UnprotectedMerchant_FreshRowWithDifferentTag_FreshTagWins_NotOverwrittenByHarvest()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("shared-id", "Miyani", 1, seasonalFestival: "halloween"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("shared-id", "Miyani", 1, seasonalFestival: "dragonbash"),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Single(result.Merged);
            Assert.Equal("dragonbash", result.Merged[0].SeasonalFestival);
        }

        private static VendorOffer MakeCoinOffer(
            string offerId, string merchantName, int outputItemId, int coinCount,
            int karmaCount = 0, string location = null, string seasonalFestival = null)
        {
            var costs = new List<CostLine>
            {
                new CostLine { Type = "Currency", Id = 1, Count = coinCount },
            };
            if (karmaCount > 0)
            {
                costs.Add(new CostLine { Type = "Currency", Id = 2, Count = karmaCount });
            }

            return new VendorOffer
            {
                OfferId = offerId,
                OutputItemId = outputItemId,
                OutputCount = 1,
                CostLines = costs,
                MerchantName = merchantName,
                Locations = location == null
                    ? new List<string>()
                    : new List<string> { location },
                SeasonalFestival = seasonalFestival,
            };
        }

        // A protected merchant's baseline row and this pass's row can name
        // two different coin prices for one sale, because the wiki writes a
        // coin price in gold, silver or copper. Shipping both lets the
        // solver buy at the lower one.
        [Fact]
        public void ProtectedMerchant_BaselineRowAtADifferentCoinPrice_IsDropped()
        {
            var baseline = new List<VendorOffer>
            {
                MakeCoinOffer("stale", "Palak", 106986, 200, karmaCount: 300000),
            };
            var fresh = new List<VendorOffer>
            {
                MakeCoinOffer("corrected", "Palak", 106986, 2000000, karmaCount: 300000),
            };
            var skipped = new HashSet<string> { "Palak" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Single(result.Merged);
            Assert.Equal("corrected", result.Merged[0].OfferId);
        }

        [Fact]
        public void ProtectedMerchant_BaselineRowAtADifferentCoinPrice_CarriesItsTagAcross()
        {
            var baseline = new List<VendorOffer>
            {
                MakeCoinOffer("stale", "Wintersday Trader (Weekly)", 104132, 5,
                    seasonalFestival: "wintersday"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeCoinOffer("corrected", "Wintersday Trader (Weekly)", 104132, 50000),
            };
            var skipped = new HashSet<string> { "Wintersday Trader (Weekly)" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Single(result.Merged);
            Assert.Equal("corrected", result.Merged[0].OfferId);
            Assert.Equal("wintersday", result.Merged[0].SeasonalFestival);
        }

        // The guard the protected-merchant path exists for: a baseline row
        // this pass produced no row for is still kept.
        [Fact]
        public void ProtectedMerchant_BaselineRowForAnItemThisPassDidNotProduce_IsKept()
        {
            var baseline = new List<VendorOffer>
            {
                MakeCoinOffer("only-in-baseline", "Palak", 100221, 5000),
            };
            var fresh = new List<VendorOffer>
            {
                MakeCoinOffer("other-item", "Palak", 106986, 2000000),
            };
            var skipped = new HashSet<string> { "Palak" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Equal(2, result.Merged.Count);
            Assert.Contains(result.Merged, o => o.OfferId == "only-in-baseline");
        }

        // Two rows for one sale that agree on the coin price are left
        // alone: only a disagreement about the price drops the baseline
        // row, so a row differing in some other field is not swept up.
        [Fact]
        public void ProtectedMerchant_BaselineRowAtTheSameCoinPrice_IsKept()
        {
            var baseline = new List<VendorOffer>
            {
                MakeCoinOffer("stale-location", "Palak", 106986, 2000000,
                    location: "Shipwreck Strand"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeCoinOffer("current", "Palak", 106986, 2000000, location: "Hullgarden"),
            };
            var skipped = new HashSet<string> { "Palak" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Equal(2, result.Merged.Count);
        }

        [Fact]
        public void ProtectedMerchant_RowWithDifferentNonCoinCosts_IsNotTheSameSale()
        {
            var baseline = new List<VendorOffer>
            {
                MakeCoinOffer("karma-priced", "Palak", 106986, 200, karmaCount: 300000),
            };
            var fresh = new List<VendorOffer>
            {
                MakeCoinOffer("coin-only", "Palak", 106986, 2000000),
            };
            var skipped = new HashSet<string> { "Palak" };

            var result = Program.MergeIntoBaseline(baseline, fresh, skipped);

            Assert.Equal(2, result.Merged.Count);
        }

        // A replaced merchant's tag is harvested before its baseline rows
        // are dropped. Both the OfferId and the content key carry the coin
        // count, so a corrected price used to match on neither.
        [Fact]
        public void ReplacedMerchant_FestivalTagSurvivesACoinPriceCorrection()
        {
            var baseline = new List<VendorOffer>
            {
                MakeCoinOffer("stale", "Festival Rewards Vendor (Weekly)", 104132, 5,
                    seasonalFestival: "wintersday"),
            };
            var fresh = new List<VendorOffer>
            {
                MakeCoinOffer("corrected", "Festival Rewards Vendor (Weekly)", 104132, 50000),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            Assert.Single(result.Merged);
            Assert.Equal("corrected", result.Merged[0].OfferId);
            Assert.Equal("wintersday", result.Merged[0].SeasonalFestival);
        }
    }
}
