using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    // The four-gate readiness model's edge cases and its stated properties -
    // not its happy path. Every fixture is a real CraftingPlanResult shape.
    public class RankerReadinessCalculatorTests
    {
        private const int MithrilliumId = 46742;
        private const int ResidueId = 46744;

        private static CraftingPlanResult Result(
            long coin = 0,
            List<CurrencyCost> currencies = null,
            List<PlanStep> steps = null,
            List<RequiredDiscipline> disciplines = null,
            List<SnapshotCharacterDiscipline> characters = null,
            Dictionary<int, DailyCooldownItem> cooldowns = null,
            List<TimegatedItem> vendorCaps = null)
        {
            return CraftingPlanResultBuilders.MakeResult(
                totalCoinCost: coin,
                currencyCosts: currencies,
                steps: steps,
                requiredDisciplines: disciplines,
                characterDisciplines: characters,
                dailyCooldownItems: cooldowns,
                timegatedItems: vendorCaps);
        }

        private static List<PlanStep> Craft(int itemId, int quantity)
        {
            return new List<PlanStep>
            {
                new PlanStep { ItemId = itemId, Quantity = quantity, Source = AcquisitionSource.Craft },
            };
        }

        private static Dictionary<int, DailyCooldownItem> Cooldowns(params int[] itemIds)
        {
            return itemIds.ToDictionary(
                id => id,
                id => new DailyCooldownItem { ItemId = id, PerDayCap = 1 });
        }

        private static RankerSlotAvailability Availability(
            int? coin = null,
            Dictionary<int, int> currency = null,
            Dictionary<int, int> claimedGated = null,
            HashSet<int> claimedItems = null,
            HashSet<int> claimedCurrencies = null)
        {
            return new RankerSlotAvailability
            {
                CoinCopper = coin,
                Currency = currency ?? new Dictionary<int, int>(),
                ClaimedGatedUnits = claimedGated ?? new Dictionary<int, int>(),
                ClaimedItemIds = claimedItems ?? new HashSet<int>(),
                ClaimedCurrencyIds = claimedCurrencies ?? new HashSet<int>(),
            };
        }

        private static double GateCompletion(RankerRowMetrics metrics, RankerGate gate)
        {
            return metrics.Gates.First(g => g.Gate == gate).Completion;
        }

        private static bool GateApplies(RankerRowMetrics metrics, RankerGate gate)
        {
            return metrics.Gates.First(g => g.Gate == gate).Applies;
        }

        // ---------------------------------------------------------------
        // Null and degenerate inputs
        // ---------------------------------------------------------------
        [Fact]
        public void NullResults_AreNotMeasurableAndNeverThrow()
        {
            var real = Result(coin: 100);

            foreach (var metrics in new[]
            {
                RankerReadinessCalculator.Compute(null, real, Availability(), 0),
                RankerReadinessCalculator.Compute(real, null, Availability(), 0),
                RankerReadinessCalculator.Compute(null, null, Availability(), 0),
            })
            {
                Assert.Equal(RankerReadinessKind.NotMeasurable, metrics.Kind);
                Assert.Equal(0, metrics.Readiness);
                Assert.Equal(5, metrics.Gates.Count);
                Assert.Empty(metrics.CurrencyShortfalls);
                Assert.Empty(metrics.VendorCappedItems);
                Assert.Empty(metrics.DisciplineGaps);
            }
        }

        [Fact]
        public void NullAvailability_IsToleratedAndSuppressesAffordability()
        {
            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), Result(coin: 40), null, 0);

            Assert.Equal(RankerReadinessKind.Measured, metrics.Kind);
            Assert.False(metrics.HasSnapshot);
            Assert.False(metrics.AffordableNow);
            Assert.Equal(0, metrics.ShortfallCoin);
        }

        [Fact]
        public void NoGateApplies_AndNothingIsOutstanding_ReadsNothingLeft()
        {
            var metrics = RankerReadinessCalculator.Compute(Result(), Result(), Availability(), 0);

            Assert.Equal(RankerReadinessKind.NothingLeft, metrics.Kind);
            Assert.Equal(RankerReadinessCalculator.NothingLeftText,
                RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void NoGateApplies_ButSomethingIsOutstanding_ReadsNotMeasurableRatherThanDone()
        {
            // A currency cost the baseline never saw: no gate can score it,
            // and calling that "done" would be the lie the tab must not tell.
            var owned = Result(currencies: new List<CurrencyCost>());
            owned.Plan.CurrencyCosts = null;
            owned.Plan.TotalCoinCost = 900;

            var metrics = RankerReadinessCalculator.Compute(Result(), owned, Availability(), 0);

            Assert.Equal(RankerReadinessKind.NotMeasurable, metrics.Kind);
            Assert.Equal(RankerReadinessCalculator.NotMeasurableText,
                RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void NullCurrencyAndTimegateCollections_ComeBackEmptyNeverNull()
        {
            var baseline = Result(coin: 100);
            var owned = Result(coin: 50);
            owned.Plan.CurrencyCosts = null;
            owned.Plan.TimegatedItems = null;
            baseline.Plan.CurrencyCosts = null;

            var metrics = RankerReadinessCalculator.Compute(baseline, owned, Availability(), 0);

            Assert.NotNull(metrics.CurrencyShortfalls);
            Assert.NotNull(metrics.VendorCappedItems);
            Assert.NotNull(metrics.DisciplineGaps);
        }

        // ---------------------------------------------------------------
        // The property that keeps the change honest
        // ---------------------------------------------------------------
        [Fact]
        public void AnItemWhoseOnlyBarrierIsMaterials_ScoresExactlyTheCoinOnlyFigure()
        {
            // Renormalisation over applicable gates is what buys this: the new
            // model is a strict superset of the old coin-only metric, and a
            // simple item's number is unchanged.
            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 1000), Result(coin: 270), Availability(), 0);

            Assert.True(GateApplies(metrics, RankerGate.Materials));
            Assert.False(GateApplies(metrics, RankerGate.Currencies));
            Assert.False(GateApplies(metrics, RankerGate.TimeGates));
            Assert.False(GateApplies(metrics, RankerGate.Disciplines));
            Assert.Equal(0.73, metrics.Readiness, 6);
            Assert.Equal("73%", RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void AGateTheItemDoesNotHave_Reads100PercentWithoutJoiningTheBlend()
        {
            // The rule: nothing is outstanding behind a barrier the
            // item does not have, so its cell reads 100% rather than a dash.
            // The cell is NOT a term of the mean, and this row is what proves
            // it - four of the five gates read 100% while the headline stays
            // at the materials figure. Entering four gates at 1.0 instead of
            // dropping them would put this row at 90%.
            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 1000), Result(coin: 270), Availability(), 0);

            foreach (var gate in metrics.Gates)
            {
                Assert.Equal(
                    gate.Gate == RankerGate.Materials ? "73%" : "100%",
                    RankerReadinessCalculator.FormatGate(gate));
            }

            Assert.Equal(0.73, metrics.Readiness, 6);
            Assert.Equal("73%", RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void AMissingGateObject_StillReadsAsUnmeasuredRatherThanComplete()
        {
            // The dash's remaining job: a row that has never been measured is
            // not a row whose barriers are all satisfied.
            Assert.Equal(
                RankerReadinessCalculator.DashText, RankerReadinessCalculator.FormatGate(null));
        }

        [Fact]
        public void NinetyFivePercentByCoinWithThirtyDaysOfDailiesLeft_IsNotNinetyFivePercent()
        {
            // The worked case. 0.5*(0.95) + 0.5*(1 - 30/32) = 0.506.
            var cooldowns = Cooldowns(MithrilliumId);
            var baseline = Result(coin: 20000, steps: Craft(MithrilliumId, 32), cooldowns: cooldowns);
            var owned = Result(coin: 1000, steps: Craft(MithrilliumId, 30), cooldowns: cooldowns);

            var metrics = RankerReadinessCalculator.Compute(baseline, owned, Availability(), 0);

            Assert.Equal(0.95, GateCompletion(metrics, RankerGate.Materials), 6);
            Assert.Equal(1.0 - 30.0 / 32.0, GateCompletion(metrics, RankerGate.TimeGates), 6);
            Assert.Equal("50%", RankerReadinessCalculator.FormatReadiness(metrics));
            Assert.Equal(30, metrics.DaysRemaining);
            Assert.Equal(32, metrics.DaysFromScratch);
        }

        // ---------------------------------------------------------------
        // Never claim done when it is not
        // ---------------------------------------------------------------
        [Fact]
        public void NinetyNinePointSixPercent_FloorsToNinetyNine()
        {
            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 100000), Result(coin: 400), Availability(), 0);

            Assert.Equal("99%", RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void AnIncompleteGateCapsTheHeadlineAtNinetyNineEvenWhenTheMeanRoundsTo100()
        {
            // Materials complete, disciplines 999/1000: the weighted mean is
            // 0.9999, and printing 100% for an item you cannot craft yet is
            // the single most trust-destroying number this tab can show.
            var baseline = Result(coin: 1000);
            var owned = Result(
                coin: 0,
                disciplines: new List<RequiredDiscipline> { new RequiredDiscipline { Discipline = "Huntsman", MinRating = 1000 } },
                characters: new List<SnapshotCharacterDiscipline> { new SnapshotCharacterDiscipline { CharacterName = "Kara", Discipline = "Huntsman", Rating = 999 } });

            var metrics = RankerReadinessCalculator.Compute(baseline, owned, Availability(), 0);

            Assert.Equal("99%", RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void EveryGateComplete_ReadsExactlyOneHundredPercent()
        {
            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 1000), Result(coin: 0), Availability(), 0);

            Assert.Equal("100%", RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void OwnedAboveBaseline_ClampsToZeroRatherThanGoingNegative()
        {
            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 100), Result(coin: 400), Availability(), 0);

            Assert.Equal(0, metrics.Readiness);
            Assert.Equal("0%", RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void FormattingIsInvariantCultureUnderACommaDecimalCulture()
        {
            var previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
                var metrics = RankerReadinessCalculator.Compute(
                    Result(coin: 1000), Result(coin: 270), Availability(), 0);

                Assert.Equal("73%", RankerReadinessCalculator.FormatReadiness(metrics));
                Assert.Equal("42%", RankerReadinessCalculator.FormatPercent(0.42));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
        }

        // ---------------------------------------------------------------
        // Currencies: never priced, only ever compared with themselves
        // ---------------------------------------------------------------
        [Fact]
        public void AnUnvaluedCurrencyDragsTheScoreDownRatherThanReadingAsCheap()
        {
            // 500 Provisioner Tokens with none held. The score never asks what
            // a token is worth, so it cannot read the item as nearly free.
            var currency = new List<CurrencyCost> { new CurrencyCost { CurrencyId = 29, Amount = 500 } };
            var baseline = Result(coin: 1000, currencies: currency);
            var owned = Result(coin: 50, currencies: currency);

            var metrics = RankerReadinessCalculator.Compute(baseline, owned, Availability(), 0);

            Assert.Equal(0.95, GateCompletion(metrics, RankerGate.Materials), 6);
            Assert.Equal(0.0, GateCompletion(metrics, RankerGate.Currencies), 6);

            // 0.35*0.95 + 0.20*0 over 0.55 of weight.
            Assert.Equal(0.35 * 0.95 / 0.55, metrics.Readiness, 6);
            Assert.Equal(500, metrics.CurrencyShortfalls.Single().Short);
        }

        [Fact]
        public void HeldCurrencyReducesTheShortfallWithinThatCurrencyOnly()
        {
            var currency = new List<CurrencyCost> { new CurrencyCost { CurrencyId = 29, Amount = 500 } };
            var availability = Availability(currency: new Dictionary<int, int> { { 29, 400 } });

            var metrics = RankerReadinessCalculator.Compute(
                Result(currencies: currency), Result(currencies: currency), availability, 0);

            Assert.Equal(0.8, GateCompletion(metrics, RankerGate.Currencies), 6);
            Assert.Equal(100, metrics.CurrencyShortfalls.Single().Short);
            Assert.Equal(400, metrics.CurrencyShortfalls.Single().Held);
        }

        [Fact]
        public void EachCurrencyCountsOnceRegardlessOfMagnitude()
        {
            // Weighting by need would compare 5,000 karma against 10 laurels as
            // though a karma equalled a laurel, which is the invalid comparison
            // the repo forbids. The mean is deliberately unweighted.
            var costs = new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 2, Amount = 5000 },
                new CurrencyCost { CurrencyId = 3, Amount = 10 },
            };
            var availability = Availability(currency: new Dictionary<int, int> { { 2, 5000 } });

            var metrics = RankerReadinessCalculator.Compute(
                Result(currencies: costs), Result(currencies: costs), availability, 0);

            Assert.Equal(0.5, GateCompletion(metrics, RankerGate.Currencies), 6);
        }

        [Fact]
        public void ACurrencyIntroducedOnlyByReduction_IsStillListedAndScored()
        {
            var baseline = Result(coin: 1000);
            var owned = Result(coin: 900, currencies: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 29, Amount = 40 },
            });

            var metrics = RankerReadinessCalculator.Compute(baseline, owned, Availability(), 0);

            Assert.True(GateApplies(metrics, RankerGate.Currencies));
            var shortfall = Assert.Single(metrics.CurrencyShortfalls);
            Assert.Equal(29, shortfall.CurrencyId);
            Assert.Equal(40, shortfall.Short);
            Assert.Equal(40, shortfall.BaselineNeeded);
            Assert.Equal(0.0, GateCompletion(metrics, RankerGate.Currencies), 6);
        }

        // ---------------------------------------------------------------
        // Time gates
        // ---------------------------------------------------------------
        [Fact]
        public void DaysAcrossDifferentGatedItems_IsTheMaxNotTheSum()
        {
            // Per-account daily caps run independently: a Lump of Mithrillium
            // and a Glob of Elder Spirit Residue can be crafted the same day.
            var cooldowns = Cooldowns(MithrilliumId, ResidueId);
            var steps = new List<PlanStep>
            {
                new PlanStep { ItemId = MithrilliumId, Quantity = 20, Source = AcquisitionSource.Craft },
                new PlanStep { ItemId = ResidueId, Quantity = 12, Source = AcquisitionSource.Craft },
            };
            var result = Result(steps: steps, cooldowns: cooldowns);

            var metrics = RankerReadinessCalculator.Compute(result, result, Availability(), 0);

            Assert.Equal(20, metrics.DaysRemaining);
        }

        [Fact]
        public void DaysForTheSameGatedItem_QueueBehindHigherPrioritySlots()
        {
            // This is the cascade. Slot 1 takes days 1-30 of the Mithrillium
            // queue; a slot needing 20 more finishes on day 50, not day 20.
            var cooldowns = Cooldowns(MithrilliumId);
            var result = Result(steps: Craft(MithrilliumId, 20), cooldowns: cooldowns);
            var availability = Availability(claimedGated: new Dictionary<int, int> { { MithrilliumId, 30 } });

            var metrics = RankerReadinessCalculator.Compute(result, result, availability, 1);

            Assert.Equal(50, metrics.DaysRemaining);
            Assert.Equal(20, metrics.DaysAlone);

            // Queued past its own from-scratch total, so the gate gets no
            // credit - and the row still shows the real 50-day figure.
            Assert.Equal(0.0, GateCompletion(metrics, RankerGate.TimeGates), 6);
        }

        [Fact]
        public void AVendorPurchaseCapIsSurfacedButNeverScored()
        {
            // A cap that is not binding is not a barrier - the shipped seed can
            // cap an item as TP-liquid as Glob of Ectoplasm through one
            // incidental festival-vendor offer.
            var vendorCaps = new List<TimegatedItem>
            {
                new TimegatedItem { ItemId = 19721, CapType = TimegatedCapType.Weekly, CapValue = 1, NeededCount = 86 },
            };
            var baseline = Result(coin: 1000, vendorCaps: vendorCaps);
            var owned = Result(coin: 500, vendorCaps: vendorCaps);

            var metrics = RankerReadinessCalculator.Compute(baseline, owned, Availability(), 0);

            Assert.False(GateApplies(metrics, RankerGate.TimeGates));
            Assert.Equal(0, metrics.DaysRemaining);
            Assert.Single(metrics.VendorCappedItems);
            Assert.Equal("50%", RankerReadinessCalculator.FormatReadiness(metrics));
        }

        [Fact]
        public void AnItemWithNoDailyCooldownAnywhere_HasNoTimeGateAtAll()
        {
            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 1000, steps: Craft(999, 40)),
                Result(coin: 500, steps: Craft(999, 40)),
                Availability(), 0);

            Assert.False(GateApplies(metrics, RankerGate.TimeGates));

            // A MEASURED absence of any daily gate reads as zero, not as the
            // dash a never-solved row gets - the two used to be the same
            // mark in the same column.
            Assert.Equal(RankerReadinessCalculator.ZeroDaysText, RankerReadinessCalculator.FormatDays(metrics));
            Assert.Equal(RankerReadinessCalculator.DashText, RankerReadinessCalculator.FormatDays(null));
            Assert.NotEqual(RankerReadinessCalculator.DashText, RankerReadinessCalculator.ZeroDaysText);
        }

        [Fact]
        public void ABoughtGatedItemIsNotADailyCraft()
        {
            // The cap is on the crafting ACTION; buying the item off the
            // Trading Post is not gated at all.
            var cooldowns = Cooldowns(MithrilliumId);
            var steps = new List<PlanStep>
            {
                new PlanStep { ItemId = MithrilliumId, Quantity = 40, Source = AcquisitionSource.BuyFromTp },
            };
            var result = Result(steps: steps, cooldowns: cooldowns);

            var metrics = RankerReadinessCalculator.Compute(result, result, Availability(), 0);

            Assert.False(GateApplies(metrics, RankerGate.TimeGates));
        }

        // ---------------------------------------------------------------
        // Disciplines
        // ---------------------------------------------------------------
        [Fact]
        public void WithNoDisciplineDataCaptured_TheGateDoesNotApply()
        {
            // AccountSnapshot.CharacterDisciplines' null-vs-empty distinction
            // exists so nothing fabricates a "not trained" claim for a snapshot
            // that never looked.
            var owned = Result(
                coin: 1000,
                disciplines: new List<RequiredDiscipline> { new RequiredDiscipline { Discipline = "Huntsman", MinRating = 500 } },
                characters: null);

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 1000), owned, Availability(), 0);

            Assert.False(GateApplies(metrics, RankerGate.Disciplines));
            Assert.Empty(metrics.DisciplineGaps);
        }

        [Fact]
        public void AnUnlearnedDiscipline_ScoresZeroAndIsNamedWithItsRatingGap()
        {
            var owned = Result(
                coin: 1000,
                disciplines: new List<RequiredDiscipline> { new RequiredDiscipline { Discipline = "Huntsman", MinRating = 500 } },
                characters: new List<SnapshotCharacterDiscipline>());

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 1000), owned, Availability(), 0);

            Assert.True(GateApplies(metrics, RankerGate.Disciplines));
            Assert.Equal(0.0, GateCompletion(metrics, RankerGate.Disciplines), 6);
            var gap = Assert.Single(metrics.DisciplineGaps);
            Assert.Equal("Huntsman", gap.Discipline);
            Assert.Equal(500, gap.RequiredRating);
            Assert.Equal(0, gap.BestRating);
            Assert.Null(gap.BestCharacterName);
        }

        [Fact]
        public void APartlyLevelledDisciplineScoresItsRatingFractionAndNamesTheBestCharacter()
        {
            var owned = Result(
                coin: 1000,
                disciplines: new List<RequiredDiscipline> { new RequiredDiscipline { Discipline = "Huntsman", MinRating = 500 } },
                characters: new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Kara", Discipline = "Huntsman", Rating = 400, Active = false },
                    new SnapshotCharacterDiscipline { CharacterName = "Tems", Discipline = "Huntsman", Rating = 275, Active = true },
                });

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 1000), owned, Availability(), 0);

            Assert.Equal(0.8, GateCompletion(metrics, RankerGate.Disciplines), 6);
            var gap = Assert.Single(metrics.DisciplineGaps);
            Assert.Equal(400, gap.BestRating);
            Assert.Equal("Kara", gap.BestCharacterName);
        }

        [Fact]
        public void ASwappedOutDisciplineStillCounts()
        {
            // Rating persists when a discipline is swapped out - a Master NPC
            // re-activates it for a small fee - so Active is not consulted.
            var owned = Result(
                coin: 1000,
                disciplines: new List<RequiredDiscipline> { new RequiredDiscipline { Discipline = "Armorsmith", MinRating = 400 } },
                characters: new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Kara", Discipline = "Armorsmith", Rating = 400, Active = false },
                });

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 1000), owned, Availability(), 0);

            Assert.Equal(1.0, GateCompletion(metrics, RankerGate.Disciplines), 6);
        }

        [Fact]
        public void SeveralRequiredDisciplinesAverageUnweighted()
        {
            var owned = Result(
                coin: 1000,
                disciplines: new List<RequiredDiscipline>
                {
                    new RequiredDiscipline { Discipline = "Huntsman", MinRating = 500 },
                    new RequiredDiscipline { Discipline = "Leatherworker", MinRating = 400 },
                },
                characters: new List<SnapshotCharacterDiscipline>
                {
                    new SnapshotCharacterDiscipline { CharacterName = "Kara", Discipline = "Huntsman", Rating = 500 },
                });

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 1000), owned, Availability(), 0);

            Assert.Equal(0.5, GateCompletion(metrics, RankerGate.Disciplines), 6);
            Assert.Equal(2, metrics.DisciplineGaps.Count);
            // Biggest gap first, so the row's one note line names the worst.
            Assert.Equal("Leatherworker", metrics.DisciplineGaps[0].Discipline);
        }

        // ---------------------------------------------------------------
        // Affordability and contention, both cascade-aware
        // ---------------------------------------------------------------
        [Fact]
        public void AffordabilityIsMeasuredAgainstCoinLeftAfterHigherPrioritySlots()
        {
            var baseline = Result(coin: 1000);
            var owned = Result(coin: 600);

            var flush = RankerReadinessCalculator.Compute(baseline, owned, Availability(coin: 600), 0);
            var drained = RankerReadinessCalculator.Compute(baseline, owned, Availability(coin: 100), 2);

            Assert.True(flush.AffordableNow);
            Assert.Equal(0, flush.ShortfallCoin);
            Assert.False(drained.AffordableNow);
            Assert.Equal(500, drained.ShortfallCoin);
            Assert.True(drained.HasSnapshot);
        }

        [Fact]
        public void ContestedCountsOnlyCoverThingsThisSlotMustStillAcquire()
        {
            var owned = Result(coin: 500, currencies: new List<CurrencyCost>
            {
                new CurrencyCost { CurrencyId = 29, Amount = 100 },
            });
            owned.Plan.Steps = new List<PlanStep>
            {
                new PlanStep { ItemId = 111, Quantity = 4, Source = AcquisitionSource.BuyFromTp },
                new PlanStep { ItemId = 222, Quantity = 2, Source = AcquisitionSource.Craft },
            };

            var availability = Availability(
                claimedItems: new HashSet<int> { 111, 999 },
                claimedCurrencies: new HashSet<int> { 29 });

            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 1000, currencies: owned.Plan.CurrencyCosts), owned, availability, 1);

            // 999 was claimed but this slot does not need it, so it is not
            // contested - only a cost the user's own ordering caused counts.
            Assert.Equal(1, metrics.ContestedItemCount);
            Assert.Equal(1, metrics.ContestedCurrencyCount);
        }

        [Fact]
        public void PriorityIndexIsCarriedSoAMovedRowCanBeDetectedAsStale()
        {
            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 100), Result(coin: 50), Availability(), 3);

            Assert.Equal(3, metrics.PriorityIndex);
        }

        [Fact]
        public void EveryGateCarriesItsWeightForInspection()
        {
            var metrics = RankerReadinessCalculator.Compute(
                Result(coin: 100), Result(coin: 50), Availability(), 0);

            Assert.Equal(RankerReadinessWeights.Materials, metrics.Gates.First(g => g.Gate == RankerGate.Materials).Weight);
            Assert.Equal(RankerReadinessWeights.Currencies, metrics.Gates.First(g => g.Gate == RankerGate.Currencies).Weight);
            Assert.Equal(RankerReadinessWeights.TimeGates, metrics.Gates.First(g => g.Gate == RankerGate.TimeGates).Weight);
            Assert.Equal(RankerReadinessWeights.Disciplines, metrics.Gates.First(g => g.Gate == RankerGate.Disciplines).Weight);
            Assert.Equal(RankerReadinessWeights.Recipes, metrics.Gates.First(g => g.Gate == RankerGate.Recipes).Weight);
        }

        // ---------------------------------------------------------------
        // The recipes gate
        // ---------------------------------------------------------------
        private static RequiredRecipe Recipe(
            bool? isMissing, bool autoLearned = false, params string[] disciplines)
        {
            return new RequiredRecipe
            {
                RecipeId = 1,
                OutputItemId = 2,
                IsMissing = isMissing,
                IsAutoLearned = autoLearned,
                Disciplines = new List<string>(disciplines),
            };
        }

        [Fact]
        public void RecipesGate_ScoresKnownOverCheckable()
        {
            var owned = Result(coin: 50);
            owned.RequiredRecipes = new List<RequiredRecipe>
            {
                Recipe(isMissing: false),
                Recipe(isMissing: false),
                Recipe(isMissing: true),
                Recipe(isMissing: true),
            };

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.True(GateApplies(metrics, RankerGate.Recipes));
            Assert.Equal(0.5, GateCompletion(metrics, RankerGate.Recipes), 9);
        }

        [Fact]
        public void RecipesGate_NeverFabricatesFromAnUncheckedRecipe()
        {
            // IsMissing null means the learned-recipes check never ran -
            // the same never-fabricate rule as the disciplines gate.
            var owned = Result(coin: 50);
            owned.RequiredRecipes = new List<RequiredRecipe>
            {
                Recipe(isMissing: null),
                Recipe(isMissing: null),
            };

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.False(GateApplies(metrics, RankerGate.Recipes));
        }

        [Fact]
        public void RecipesGate_IgnoresAutoLearnedRecipes()
        {
            // An auto-learned recipe carries no unlock barrier, so a plan
            // made only of them has no recipes gate at all.
            var owned = Result(coin: 50);
            owned.RequiredRecipes = new List<RequiredRecipe>
            {
                Recipe(isMissing: false, autoLearned: true),
                Recipe(isMissing: true, autoLearned: true),
            };

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.False(GateApplies(metrics, RankerGate.Recipes));
        }

        [Fact]
        public void RecipesGate_IgnoresMysticForgeOnlyRecipes()
        {
            // The Mystic Forge has no unlock, so PlanResultBuilder marks a
            // forge recipe as not missing. Counting it padded both halves of
            // this fraction and lifted the cell above the plan's own count.
            var owned = Result(coin: 50);
            owned.RequiredRecipes = new List<RequiredRecipe>
            {
                Recipe(isMissing: false, autoLearned: false, "MysticForge"),
                Recipe(isMissing: false, autoLearned: false, "MysticForge"),
                Recipe(isMissing: true, autoLearned: false, "Weaponsmith"),
            };

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.True(GateApplies(metrics, RankerGate.Recipes));
            Assert.Equal(0.0, GateCompletion(metrics, RankerGate.Recipes), 9);
        }

        [Fact]
        public void RecipesGate_KeepsARecipeThatPairsTheForgeWithALeveledDiscipline()
        {
            // A recipe combining the forge with a real discipline still has
            // something to learn, so it stays in the score.
            var owned = Result(coin: 50);
            owned.RequiredRecipes = new List<RequiredRecipe>
            {
                Recipe(isMissing: true, autoLearned: false, "MysticForge", "Artificer"),
            };

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.True(GateApplies(metrics, RankerGate.Recipes));
            Assert.Equal(0.0, GateCompletion(metrics, RankerGate.Recipes), 9);
        }

        [Fact]
        public void RecipesGate_StillScoresARecipeWithNoDisciplineData()
        {
            // An empty discipline list is absent data, not a forge recipe.
            var owned = Result(coin: 50);
            owned.RequiredRecipes = new List<RequiredRecipe> { Recipe(isMissing: false) };

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.True(GateApplies(metrics, RankerGate.Recipes));
            Assert.Equal(1.0, GateCompletion(metrics, RankerGate.Recipes), 9);
        }

        [Fact]
        public void AMissingRecipeCapsTheHeadlineBelowOneHundredPercent()
        {
            var owned = Result(coin: 0);
            owned.RequiredRecipes = new List<RequiredRecipe> { Recipe(isMissing: true) };

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.Equal(RankerReadinessKind.Measured, metrics.Kind);
            Assert.True(metrics.Readiness < 1.0);
        }

        // ---------------------------------------------------------------
        // Vendor purchase caps vs TP liquidity (field issue: Mystic Coin's
        // "10 per week cap" presented a coin problem as a time gate)
        // ---------------------------------------------------------------
        private static CraftingPlanResult WithPrices(CraftingPlanResult result, Dictionary<int, ItemPrice> prices)
        {
            result.SolveContext = new PlanSolveContext { Prices = prices };
            return result;
        }

        private static List<TimegatedItem> WeeklyCap(int itemId)
        {
            return new List<TimegatedItem>
            {
                new TimegatedItem { ItemId = itemId, CapType = TimegatedCapType.Weekly, CapValue = 10, NeededCount = 16 },
            };
        }

        [Fact]
        public void ATpLiquidItemsVendorCapIsDroppedNotPresentedAsATimeGate()
        {
            const int mysticCoinLike = 19976;
            var owned = WithPrices(
                Result(coin: 50, vendorCaps: WeeklyCap(mysticCoinLike)),
                new Dictionary<int, ItemPrice>
                {
                    [mysticCoinLike] = new ItemPrice { ItemId = mysticCoinLike, BuyInstant = 100, SellInstant = 120 },
                });

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            // The remainder above the cap is coin, not time.
            Assert.Empty(metrics.VendorCappedItems);
        }

        [Fact]
        public void AnUnpricedItemsVendorCapSurvivesTheFilter()
        {
            const int boundItem = 12345;
            var owned = WithPrices(
                Result(coin: 50, vendorCaps: WeeklyCap(boundItem)),
                new Dictionary<int, ItemPrice>
                {
                    // Present but with no live orders on either side.
                    [boundItem] = new ItemPrice { ItemId = boundItem, BuyInstant = 0, SellInstant = 0 },
                });

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.Single(metrics.VendorCappedItems);
        }

        [Fact]
        public void MissingPriceDataKeepsTheCapRatherThanInventingLiquidity()
        {
            var owned = Result(coin: 50, vendorCaps: WeeklyCap(777));

            var metrics = RankerReadinessCalculator.Compute(Result(coin: 100), owned, Availability(), 0);

            Assert.Single(metrics.VendorCappedItems);
        }

        // ---------------------------------------------------------------
        // The comparison-mode tag
        // ---------------------------------------------------------------
        [Fact]
        public void MetricsCarryTheModeTheyWereComputedUnder()
        {
            var cascade = RankerReadinessCalculator.Compute(
                Result(coin: 100), Result(coin: 50), Availability(), 0);
            var independent = RankerReadinessCalculator.Compute(
                Result(coin: 100), Result(coin: 50), Availability(), 0, RankerMode.Independent);

            Assert.Equal(RankerMode.Cascade, cascade.Mode);
            Assert.Equal(RankerMode.Independent, independent.Mode);
        }
    }
}
