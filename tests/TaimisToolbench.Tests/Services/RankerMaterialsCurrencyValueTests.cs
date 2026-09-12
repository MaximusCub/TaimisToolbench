using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The Crafting Ranker's materials gate is the plan's whole bill in
    /// copper: coin, plus every currency the plan pays, at the decision
    /// valuation. A currency with no valuation is left out of both halves and
    /// named on the row instead.
    /// <para>
    /// Driven over the SHIPPED corpus - ref/recipes_seed.json,
    /// ref/mystic_forge_recipes.json and ref/vendor_offers.json - through the
    /// production RecipeService, VendorOfferLoader, CraftingPlanPipeline,
    /// PlanSolver, InventoryReducer and RankerReadinessCalculator.
    /// </para>
    /// <para>
    /// Views/RankerTabContent.cs draws the figure and is Blish-bound, so
    /// nothing here covers the drawing. Only the number it is handed.
    /// </para>
    /// </summary>
    public class RankerMaterialsCurrencyValueTests
    {
        /// <summary>The legendary axe. Its four forge ingredients are below.</summary>
        private const int Frostfang = 30684;

        private const int GiftOfFrostfang = 29166;
        private const int GiftOfMastery = 19626;

        /// <summary>The legendary ring, and two of its four forge ingredients.</summary>
        private const int EndlessSummer = 107022;

        private const int GiftOfTheJungle = 107040;
        private const int GiftOfCompassion = 106712;

        private const int SpiritShard = 23;
        private const int GuildCommendation = 16;

        /// <summary>
        /// A Visions of Eternity map currency with no tradable output and no
        /// cross-currency offer in the seed, so no defensible value exists
        /// for it - docs/ARCHITECTURE.md section 8.3.
        /// </summary>
        private const int AntiquatedDucat = 81;

        private const int MithrilIngot = 19684;
        private const int MithrilOre = 19700;

        /// <summary>
        /// A plan that pays only coin must score exactly what it scored
        /// before the materials gate learned about currency. The valuation
        /// argument is the whole change, so handing it one and withholding it
        /// has to make no difference at all here.
        /// </summary>
        [Fact]
        public async Task ACoinOnlyPlanScoresTheSameWithAndWithoutAValuation()
        {
            var baseline = await PlanMithrilIngotsAsync(10);
            var owned = await PlanMithrilIngotsAsync(4);

            Assert.Empty(baseline.Plan.CurrencyCosts);
            Assert.Empty(owned.Plan.CurrencyCosts);
            Assert.Equal(10000, baseline.Plan.TotalCoinCost);
            Assert.Equal(4000, owned.Plan.TotalCoinCost);

            var without = RankerReadinessCalculator.Compute(baseline, owned, null, 0);
            var with = RankerReadinessCalculator.Compute(
                baseline, owned, null, 0, RankerMode.Cascade, Defaults());

            Assert.Equal(0.6, MaterialsOf(without).Completion, 9);
            Assert.Equal(0.6, MaterialsOf(with).Completion, 9);
            Assert.Equal(without.Readiness, with.Readiness, 9);
            Assert.Empty(with.MaterialsUnpricedCurrencyIds);
        }

        /// <summary>
        /// The reported case, on the shipped corpus. Two of Frostfang's four
        /// forge ingredients in the bank barely dent the plan's coin bill and
        /// clear most of its currency bill, so the two halves disagree
        /// sharply about how far along the row is.
        /// <para>
        /// MEASURED on the shipped corpus: coin 12,903,096 to 12,376,000, a
        /// 4 percent cut, against Spirit Shards 1,110,448 to 136,025 and
        /// Guild Commendations 25,268 to 7,398. The gate reads 4 percent on
        /// coin alone and 87 percent counting both, and the blended Ready
        /// figure moves from 21 percent to 66 percent. The bands below are
        /// wide enough to survive a corpus refresh and nowhere near wide
        /// enough to survive a wiring regression.
        /// </para>
        /// </summary>
        [Fact]
        public async Task WhatTheOwnedMaterialsRemovedIsCurrency_NotCoin()
        {
            var pair = await FrostfangPairAsync();

            Assert.True(AmountOf(pair.Baseline, SpiritShard) > AmountOf(pair.Owned, SpiritShard));
            Assert.True(
                AmountOf(pair.Baseline, GuildCommendation) > AmountOf(pair.Owned, GuildCommendation));

            // The coin half hardly moved, so anything the gate now reports as
            // progress came from the currency half.
            Assert.True(pair.Owned.Plan.TotalCoinCost > pair.Baseline.Plan.TotalCoinCost * 0.9);

            var without = RankerReadinessCalculator.Compute(pair.Baseline, pair.Owned, null, 0);
            var with = RankerReadinessCalculator.Compute(
                pair.Baseline, pair.Owned, null, 0, RankerMode.Cascade, Defaults());

            Assert.Empty(with.MaterialsUnpricedCurrencyIds);
            Assert.InRange(MaterialsOf(without).Completion, 0.0, 0.10);
            Assert.InRange(MaterialsOf(with).Completion, 0.80, 0.95);
            Assert.True(with.Readiness > without.Readiness);
        }

        /// <summary>
        /// Endless Summer pays five Janthir Wilds and Visions of Eternity map
        /// currencies that CurrencyDecisionDefaults deliberately leaves
        /// unvalued. The gate reports them rather than pricing them at zero,
        /// and the currencies gate still scores every one.
        /// </summary>
        [Fact]
        public async Task AnUnvaluedCurrencyIsNamedRatherThanPricedAtZero()
        {
            var pair = await EndlessSummerPairAsync();
            var defaults = Defaults();

            var metrics = RankerReadinessCalculator.Compute(
                pair.Baseline, pair.Owned, null, 0, RankerMode.Cascade, defaults);

            Assert.NotEmpty(metrics.MaterialsUnpricedCurrencyIds);
            Assert.Contains(AntiquatedDucat, metrics.MaterialsUnpricedCurrencyIds);

            // Every reported id is genuinely unvalued, and ascending.
            Assert.All(
                metrics.MaterialsUnpricedCurrencyIds,
                id => Assert.False(defaults.TryGetEffectiveCopperValue(id, out _)));
            Assert.Equal(
                metrics.MaterialsUnpricedCurrencyIds.OrderBy(id => id).ToList(),
                metrics.MaterialsUnpricedCurrencyIds.ToList());

            // The owned plan still owes this currency, and the gate that
            // measures it in its own units still lists it. That is what makes
            // leaving it out of a copper ratio a disclosure rather than a
            // discount.
            Assert.True(AmountOf(pair.Owned, AntiquatedDucat) > 0);
            Assert.Contains(metrics.CurrencyShortfalls, s => s.CurrencyId == AntiquatedDucat);
        }

        /// <summary>
        /// The other half of the decision above: the excluded currency is
        /// excluded ONLY because nothing values it. Give it a value in
        /// Settings and it enters the bill like any other.
        /// </summary>
        [Fact]
        public async Task ValuingAnExcludedCurrencyPutsItBackIntoTheBill()
        {
            var pair = await EndlessSummerPairAsync();

            var valued = CurrencyValuation.WithDefaults(new CurrencyValuation(
                new Dictionary<int, long> { { AntiquatedDucat, 500 } }));

            var before = RankerReadinessCalculator.Compute(
                pair.Baseline, pair.Owned, null, 0, RankerMode.Cascade, Defaults());
            var after = RankerReadinessCalculator.Compute(
                pair.Baseline, pair.Owned, null, 0, RankerMode.Cascade, valued);

            Assert.Contains(AntiquatedDucat, before.MaterialsUnpricedCurrencyIds);
            Assert.DoesNotContain(AntiquatedDucat, after.MaterialsUnpricedCurrencyIds);
            Assert.NotEqual(MaterialsOf(before).Completion, MaterialsOf(after).Completion);
        }

        /// <summary>
        /// The blended headline moves because the materials gate moved, and
        /// for no other reason. The four other gates read the same figure
        /// under both valuations, so nothing else was disturbed.
        /// </summary>
        [Fact]
        public async Task NoGateButMaterialsMoves()
        {
            var pair = await EndlessSummerPairAsync();

            var without = RankerReadinessCalculator.Compute(pair.Baseline, pair.Owned, null, 0);
            var with = RankerReadinessCalculator.Compute(
                pair.Baseline, pair.Owned, null, 0, RankerMode.Cascade, Defaults());

            foreach (var gate in with.Gates.Where(g => g.Gate != RankerGate.Materials))
            {
                var twin = without.Gates.Single(g => g.Gate == gate.Gate);
                Assert.Equal(twin.Applies, gate.Applies);
                Assert.Equal(twin.Completion, gate.Completion, 9);
            }

            Assert.NotEqual(
                MaterialsOf(without).Completion, MaterialsOf(with).Completion);
            Assert.NotEqual(without.Readiness, with.Readiness);

            // The coin figures the Remaining column draws are untouched: a
            // decision valuation weights the ratio and never reaches a
            // displayed coin total (docs/ARCHITECTURE.md section 8.3).
            Assert.Equal(without.BaselineCoinCost, with.BaselineCoinCost);
            Assert.Equal(without.RemainingCoinCost, with.RemainingCoinCost);
        }

        private static RankerGateScore MaterialsOf(RankerRowMetrics metrics)
        {
            return metrics.Gates.Single(g => g.Gate == RankerGate.Materials);
        }

        private static CurrencyValuation Defaults()
        {
            return CurrencyValuation.WithDefaults(CurrencyValuation.None);
        }

        private static long AmountOf(CraftingPlanResult result, int currencyId)
        {
            var cost = result.Plan.CurrencyCosts.FirstOrDefault(c => c.CurrencyId == currencyId);
            return cost?.Amount ?? 0;
        }

        private sealed class Pair
        {
            public CraftingPlanResult Baseline;
            public CraftingPlanResult Owned;
        }

        private static Task<Pair> FrostfangPairAsync()
        {
            return PairAsync(Frostfang, GiftOfFrostfang, GiftOfMastery);
        }

        private static Task<Pair> EndlessSummerPairAsync()
        {
            return PairAsync(EndlessSummer, GiftOfTheJungle, GiftOfCompassion);
        }

        /// <summary>
        /// The Ranker's own two solves: one against nothing, one against an
        /// account holding two of the four things the forge recipe takes.
        /// Both under OwnMaterialsMode.Free, which is what the tab uses.
        /// </summary>
        private static async Task<Pair> PairAsync(int root, int ownedA, int ownedB)
        {
            var corpus = RealCorpusFixture.Load();
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                using (var offers = File.OpenRead(RepoFileLocator.FindRepoFile("ref/vendor_offers.json")))
                {
                    store.LoadBaseline(offers);
                }

                var snapshot = new AccountSnapshot
                {
                    CharacterDisciplines = new List<SnapshotCharacterDiscipline>(),
                    Items = new List<SnapshotItemEntry>
                    {
                        new SnapshotItemEntry { ItemId = ownedA, Count = 1, Source = "Bank" },
                        new SnapshotItemEntry { ItemId = ownedB, Count = 1, Source = "Bank" },
                    },
                };

                return new Pair
                {
                    Baseline = await SolveAsync(corpus, store, root, null),
                    Owned = await SolveAsync(corpus, store, root, snapshot),
                };
            }
        }

        private static Task<CraftingPlanResult> SolveAsync(
            RealCorpusFixture corpus, VendorOfferStore store, int root, AccountSnapshot snapshot)
        {
            var pipeline = new CraftingPlanPipeline(
                corpus.NewRecipeService(),
                new TradingPostService(new InMemoryPriceApiClient()),
                new PlanSolver(),
                new ItemMetadataService(new InMemoryItemApiClient()),
                store,
                new InventoryReducer());

            return pipeline.GenerateStructuredAsync(
                root, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.BuyOrder,
                currencyValuation: CurrencyValuation.WithDefaults(CurrencyValuation.None),
                ownMaterialsMode: OwnMaterialsMode.Free);
        }

        /// <summary>
        /// A coin-only plan off the shipped recipe corpus: Mithril Ingots
        /// from Mithril Ore, with no vendor offers in play at all, so the
        /// plan can carry no currency cost to price. The ore tier is priced
        /// flat so the solver takes the ore route rather than the Homestead
        /// refinement one, which no supplied price reaches.
        /// </summary>
        private static async Task<CraftingPlanResult> PlanMithrilIngotsAsync(int quantity)
        {
            var corpus = RealCorpusFixture.Load();
            var prices = new InMemoryPriceApiClient();
            foreach (int oreId in OreTier)
            {
                prices.AddPrice(oreId, 500, 600);
            }

            var pipeline = new CraftingPlanPipeline(
                corpus.NewRecipeService(),
                new TradingPostService(prices),
                new PlanSolver(),
                new ItemMetadataService(new InMemoryItemApiClient()),
                reducer: new InventoryReducer());

            return await pipeline.GenerateStructuredAsync(
                MithrilIngot, quantity, null, CancellationToken.None,
                priceBasis: PriceBasis.BuyOrder,
                ownMaterialsMode: OwnMaterialsMode.Free);
        }

        private static readonly int[] OreTier =
        {
            MithrilOre, 19697, 19698, 19699, 19701, 19702, 19703, 19704,
        };
    }
}
