using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using VendorOfferUpdater;
using VendorOfferUpdater.Models;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    // VendorOfferHasher.ComputeOfferId does not hash SeasonalFestival, the
    // two unlock-recipe ids, or Requirement. A merge that drops one therefore
    // changes no OfferId, so the loss appears in no diff of
    // ref/vendor_offers.json and nothing downstream reports it.
    public class UnhashedFieldCarryForwardTests
    {
        // The sheet Lyhr's Obsidian armour exchange is gated behind, and the
        // recipe it unlocks. These are the only two such ids in the shipped
        // dataset; all 18 gated offers name this pair.
        private const int ObsidianSheetItemId = 101483;
        private const int ObsidianSheetRecipeId = 14083;

        private static VendorOffer MakeOffer(
            string offerId,
            string merchantName,
            int outputItemId = 1,
            int coinCost = 100,
            string seasonalFestival = null,
            int? unlockRecipeItemId = null,
            int? unlockRecipeId = null,
            VendorRequirement requirement = null)
        {
            return new VendorOffer
            {
                OfferId = offerId,
                OutputItemId = outputItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = 1, Count = coinCost },
                },
                MerchantName = merchantName,
                Locations = new List<string>(),
                SeasonalFestival = seasonalFestival,
                UnlockRecipeItemId = unlockRecipeItemId,
                UnlockRecipeId = unlockRecipeId,
                Requirement = requirement,
            };
        }

        private static VendorRequirement NuhochLanguage()
        {
            return new VendorRequirement
            {
                Text = "Nuhoch Language",
                MasteryId = 8,
                MasteryLevel = 1,
            };
        }

        private static ISet<string> Protecting(string merchantName)
        {
            return new HashSet<string> { merchantName };
        }

        // A protected merchant keeps its baseline rows, and the fresh row wins
        // the OfferId collision. Before the carry-forward covered them, the
        // gate ids went with the losing row.
        [Fact]
        public void ProtectedMerchant_FreshRowWithoutTheGate_KeepsTheBaselineGate()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer(
                    "lyhr-1", "Lyhr",
                    unlockRecipeItemId: ObsidianSheetItemId,
                    unlockRecipeId: ObsidianSheetRecipeId),
            };
            var fresh = new List<VendorOffer> { MakeOffer("lyhr-1", "Lyhr") };

            var result = Program.MergeIntoBaseline(baseline, fresh, Protecting("Lyhr"));

            var merged = Assert.Single(result.Merged);
            Assert.Equal(ObsidianSheetItemId, merged.UnlockRecipeItemId);
            Assert.Equal(ObsidianSheetRecipeId, merged.UnlockRecipeId);
        }

        // The ordinary path: no row of this merchant had a missing game id, so
        // `kept` drops its whole baseline before the OfferId group runs. The
        // pre-drop harvest is the only thing that can save the gate here.
        [Fact]
        public void ReplacedMerchant_FreshRowWithoutTheGate_KeepsTheBaselineGate()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer(
                    "lyhr-1", "Lyhr",
                    unlockRecipeItemId: ObsidianSheetItemId,
                    unlockRecipeId: ObsidianSheetRecipeId),
            };
            var fresh = new List<VendorOffer> { MakeOffer("lyhr-1", "Lyhr") };

            var result = Program.MergeIntoBaseline(baseline, fresh);

            var merged = Assert.Single(result.Merged);
            Assert.Equal(ObsidianSheetItemId, merged.UnlockRecipeItemId);
            Assert.Equal(ObsidianSheetRecipeId, merged.UnlockRecipeId);
        }

        // A baseline row written before a hash-format change carries a stale
        // OfferId, so the group above never pairs the two. ComputeContentKey
        // is what pairs them, and it ignores both unhashed fields.
        [Fact]
        public void ContentKeyPairing_CarriesTheGateOntoTheFreshRow()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer(
                    "stale-hash", "Lyhr",
                    unlockRecipeItemId: ObsidianSheetItemId,
                    unlockRecipeId: ObsidianSheetRecipeId),
            };
            var fresh = new List<VendorOffer> { MakeOffer("current-hash", "Lyhr") };

            var result = Program.MergeIntoBaseline(baseline, fresh, Protecting("Lyhr"));

            var merged = Assert.Single(result.Merged);
            Assert.Equal(ObsidianSheetItemId, merged.UnlockRecipeItemId);
            Assert.Equal(ObsidianSheetRecipeId, merged.UnlockRecipeId);
        }

        // A run that corrects a sale's coin price gives the row a new OfferId
        // and a new content key, so only ComputeSameSaleKey still pairs the
        // two. That pass drops the baseline row at the old price.
        [Fact]
        public void CorrectedCoinPrice_CarriesTheGateOntoTheRepricedRow()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer(
                    "old-price", "Lyhr", coinCost: 200,
                    unlockRecipeItemId: ObsidianSheetItemId,
                    unlockRecipeId: ObsidianSheetRecipeId),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("new-price", "Lyhr", coinCost: 2000000),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh, Protecting("Lyhr"));

            var merged = Assert.Single(result.Merged);
            Assert.Equal("new-price", merged.OfferId);
            Assert.Equal(ObsidianSheetItemId, merged.UnlockRecipeItemId);
            Assert.Equal(ObsidianSheetRecipeId, merged.UnlockRecipeId);
        }

        // One row can hold the festival tag and the other the gate. Both have
        // to reach the survivor, or fixing one field costs the other.
        [Fact]
        public void FestivalTagAndGateOnOppositeRows_BothReachTheSurvivor()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer(
                    "row-1", "Lyhr",
                    unlockRecipeItemId: ObsidianSheetItemId,
                    unlockRecipeId: ObsidianSheetRecipeId),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer("row-1", "Lyhr", seasonalFestival: "wintersday"),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh, Protecting("Lyhr"));

            var merged = Assert.Single(result.Merged);
            Assert.Equal("wintersday", merged.SeasonalFestival);
            Assert.Equal(ObsidianSheetItemId, merged.UnlockRecipeItemId);
            Assert.Equal(ObsidianSheetRecipeId, merged.UnlockRecipeId);
        }

        // Fresh wins any field it derived for itself, the rule the festival
        // tag already followed.
        [Fact]
        public void FreshGate_IsNotOverwrittenByTheBaselineGate()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("row-1", "Lyhr", unlockRecipeItemId: 1, unlockRecipeId: 2),
            };
            var fresh = new List<VendorOffer>
            {
                MakeOffer(
                    "row-1", "Lyhr",
                    unlockRecipeItemId: ObsidianSheetItemId,
                    unlockRecipeId: ObsidianSheetRecipeId),
            };

            var result = Program.MergeIntoBaseline(baseline, fresh, Protecting("Lyhr"));

            var merged = Assert.Single(result.Merged);
            Assert.Equal(ObsidianSheetItemId, merged.UnlockRecipeItemId);
            Assert.Equal(ObsidianSheetRecipeId, merged.UnlockRecipeId);
        }

        // ConvertToOffer sets the two gate ids together or not at all, so a
        // row holding one of them is not topped up from another row's pair.
        [Fact]
        public void AHalfSetGate_IsNotCompletedFromAnotherRow()
        {
            var target = MakeOffer("row-1", "Lyhr", unlockRecipeItemId: 55);
            var source = MakeOffer(
                "row-1", "Lyhr",
                unlockRecipeItemId: ObsidianSheetItemId,
                unlockRecipeId: ObsidianSheetRecipeId);

            Program.CarryForwardUnhashedFields(target, source);

            Assert.Equal(55, target.UnlockRecipeItemId);
            Assert.Null(target.UnlockRecipeId);
        }

        [Fact]
        public void CarriesUnhashedFields_IsFalseOnlyWhenEveryOneIsNull()
        {
            Assert.False(Program.CarriesUnhashedFields(MakeOffer("a", "Lyhr")));
            Assert.True(Program.CarriesUnhashedFields(
                MakeOffer("a", "Lyhr", requirement: NuhochLanguage())));
            Assert.True(Program.CarriesUnhashedFields(
                MakeOffer("a", "Lyhr", seasonalFestival: "wintersday")));
            Assert.True(Program.CarriesUnhashedFields(
                MakeOffer("a", "Lyhr", unlockRecipeItemId: ObsidianSheetItemId)));
            Assert.True(Program.CarriesUnhashedFields(
                MakeOffer("a", "Lyhr", unlockRecipeId: ObsidianSheetRecipeId)));
        }

        // The properties VendorOfferHasher.ComputeOfferId reads, plus OfferId,
        // which is the hash rather than a payload. Losing any of these changes
        // the id, so a merge cannot drop one without the diff showing it.
        // Every OTHER property is unhashed and must survive a merge, which is
        // what the test below asks of the real production method. Adding a
        // property to VendorOffer without handling it fails here.
        private static readonly HashSet<string> HashedOrIdentity = new HashSet<string>
        {
            nameof(VendorOffer.OfferId),
            nameof(VendorOffer.OutputItemId),
            nameof(VendorOffer.OutputCount),
            nameof(VendorOffer.CostLines),
            nameof(VendorOffer.MerchantName),
            nameof(VendorOffer.Locations),
            nameof(VendorOffer.DailyCap),
            nameof(VendorOffer.WeeklyCap),
            nameof(VendorOffer.SeasonalCap),
            nameof(VendorOffer.HomesteadTier),
        };

        // Two wiki rows can describe the identical sale and differ only in
        // their requirement text, which VendorOfferHasher does not hash. The
        // dedupe pass that collapses them therefore has the same duty the
        // merge does.
        [Fact]
        public void DeduplicateByOfferId_FoldsADiscardedSiblingsRequirementIn()
        {
            var withRequirement = MakeOffer("row-1", "Lyhr", requirement: NuhochLanguage());
            var without = MakeOffer("row-1", "Lyhr");

            var unique = Program.DeduplicateByOfferId(
                new List<VendorOffer> { without, withRequirement });

            var survivor = Assert.Single(unique);
            Assert.Equal("Nuhoch Language", survivor.Requirement?.Text);
        }

        [Fact]
        public void DeduplicateByOfferId_KeepsTheFirstRowsOwnRequirement()
        {
            var first = MakeOffer("row-1", "Lyhr", requirement: NuhochLanguage());
            var second = MakeOffer(
                "row-1", "Lyhr",
                requirement: new VendorRequirement { Text = "Itzel Language" });

            var survivor = Assert.Single(
                Program.DeduplicateByOfferId(new List<VendorOffer> { first, second }));

            Assert.Equal("Nuhoch Language", survivor.Requirement?.Text);
        }

        [Fact]
        public void DeduplicateByOfferId_KeepsRowsWithDifferentIds()
        {
            var unique = Program.DeduplicateByOfferId(new List<VendorOffer>
            {
                MakeOffer("row-2", "Lyhr"),
                MakeOffer("row-1", "Lyhr"),
            });

            Assert.Equal(2, unique.Count);
            Assert.Equal("row-1", unique[0].OfferId);
            Assert.Equal("row-2", unique[1].OfferId);
        }

        [Fact]
        public void ProtectedMerchant_FreshRowWithoutTheRequirement_KeepsTheBaselineOne()
        {
            var baseline = new List<VendorOffer>
            {
                MakeOffer("row-1", "Lyhr", requirement: NuhochLanguage()),
            };
            var fresh = new List<VendorOffer> { MakeOffer("row-1", "Lyhr") };

            var merged = Program.MergeIntoBaseline(baseline, fresh, Protecting("Lyhr")).Merged
                .Single();

            Assert.Equal("Nuhoch Language", merged.Requirement?.Text);
            Assert.Equal(8, merged.Requirement?.MasteryId);
            Assert.Equal(1, merged.Requirement?.MasteryLevel);
        }

        [Fact]
        public void EveryUnhashedProperty_SurvivesTheCarryForward()
        {
            var unhashed = typeof(VendorOffer)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite)
                .Where(p => !HashedOrIdentity.Contains(p.Name))
                .ToList();

            Assert.NotEmpty(unhashed);

            var source = MakeOffer(
                "row-1", "Lyhr",
                seasonalFestival: "wintersday",
                unlockRecipeItemId: ObsidianSheetItemId,
                unlockRecipeId: ObsidianSheetRecipeId,
                requirement: NuhochLanguage());

            foreach (var property in unhashed)
            {
                Assert.True(
                    property.GetValue(source) != null,
                    $"VendorOffer.{property.Name} is unhashed, so this test has to set it on " +
                    "the source row before it can prove the carry-forward moves it. Set it in " +
                    "MakeOffer, then make Program.CarryForwardUnhashedFields carry it.");
            }

            var target = MakeOffer("row-1", "Lyhr");

            Program.CarryForwardUnhashedFields(target, source);

            foreach (var property in unhashed)
            {
                Assert.True(
                    property.GetValue(target) != null,
                    $"VendorOffer.{property.Name} is not hashed into OfferId and " +
                    "Program.CarryForwardUnhashedFields does not carry it. A merge that drops " +
                    "it changes no OfferId, so no diff of ref/vendor_offers.json would show " +
                    "the loss.");
            }
        }
    }
}
