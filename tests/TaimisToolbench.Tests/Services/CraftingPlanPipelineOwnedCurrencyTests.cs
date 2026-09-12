using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class CraftingPlanPipelineOwnedCurrencyTests
    {
        // --- Owned currency (cosmetic only, never affects decisions) ---
        private static CraftingPlanPipeline BuildVendorCurrencyPipeline(
            out VendorOfferStore store, string tempDir)
        {
            var loader = new VendorOfferLoader();
            store = new VendorOfferStore(tempDir, loader);
            store.LoadBaseline(null);
            store.AddOffersToOverlay(new[]
            {
                new VendorOffer
                {
                    OfferId = "test-karma-offer",
                    OutputItemId = 1,
                    OutputCount = 1,
                    CostLines = new List<CostLine>
                    {
                        new CostLine { Type = "Currency", Id = 2, Count = 500 },
                    },
                    MerchantName = "Karma Vendor",
                },
            });

            // No recipe for item 1, and (deliberately) no TP price either -
            // a vendor-only purchase. The offer's karma cost line is never
            // valued (no CurrencyValuation passed below), so it can only
            // win via the "fallback" tier (PlanSolver's last-resort branch
            // when nothing coin-priceable/craftable exists at all) - giving
            // it a TP price here would make TP win outright instead.
            return PipelineBuilder.Create()
                .WithItem(1, "Karma Item", "karma.png")
                .WithVendorOfferStore(store)
                .WithInventoryReducer()
                .Build();
        }

        [Fact]
        public async Task OwnedCurrency_DoesNotAffectDecisionsOrTotals()
        {
            // Regression guard: wallet currency data is
            // cosmetic-only annotation. A plan generated WITH wallet karma
            // must produce the IDENTICAL decisions/costs as one generated
            // with none - only OwnedCurrencyAmounts may differ.
            using (var tmp = new TempDirectory())
            {
                var tempDir = tmp.Path;
                var pipeline = BuildVendorCurrencyPipeline(out _, tempDir);

                var withoutWallet = await pipeline.GenerateStructuredAsync(
                    1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

                var snapshotWithWallet = new AccountSnapshot
                {
                    Wallet = new List<SnapshotWalletEntry>
                    {
                        new SnapshotWalletEntry { CurrencyId = 2, Value = 100000 },
                    },
                };
                var withWallet = await pipeline.GenerateStructuredAsync(
                    1, 1, snapshotWithWallet, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

                // Decisions/costs identical regardless of wallet content.
                Assert.Equal(withoutWallet.Plan.Steps.Count, withWallet.Plan.Steps.Count);
                Assert.Equal(withoutWallet.Plan.Steps[0].Source, withWallet.Plan.Steps[0].Source);
                Assert.Equal(withoutWallet.Plan.TotalCoinCost, withWallet.Plan.TotalCoinCost);
                Assert.Equal(withoutWallet.Plan.CurrencyCosts.Count, withWallet.Plan.CurrencyCosts.Count);
                Assert.Equal(withoutWallet.Plan.CurrencyCosts[0].Amount, withWallet.Plan.CurrencyCosts[0].Amount);
                Assert.Equal(withoutWallet.CraftingTree.Decision, withWallet.CraftingTree.Decision);

                // Only the annotation differs. CraftingPlanResult.
                // OwnedCurrencyAmounts stores the RAW wallet amount
                // (capping-at-needed is a view-model presentation concern -
                // see PlanViewModelBuilder / the CurrencyCostRow test below).
                Assert.Null(withoutWallet.OwnedCurrencyAmounts);
                Assert.NotNull(withWallet.OwnedCurrencyAmounts);
                Assert.Equal(100000, withWallet.OwnedCurrencyAmounts[2]);
            }
        }

        [Fact]
        public async Task OwnedCurrency_PartialWalletAmount_CappedAtNeeded()
        {
            using (var tmp = new TempDirectory())
            {
                var tempDir = tmp.Path;
                var pipeline = BuildVendorCurrencyPipeline(out _, tempDir);

                var snapshot = new AccountSnapshot
                {
                    Wallet = new List<SnapshotWalletEntry>
                    {
                        new SnapshotWalletEntry { CurrencyId = 2, Value = 200 },
                    },
                };
                var result = await pipeline.GenerateStructuredAsync(
                    1, 1, snapshot, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

                // Needs 500, owns only 200 - reported as-is (not capped to
                // itself, since 200 < 500).
                Assert.Equal(200, result.OwnedCurrencyAmounts[2]);
                // The plan itself still needs the full 500 (owned currency
                // never nets against the plan's own currency total).
                Assert.Equal(500, result.Plan.CurrencyCosts[0].Amount);
            }
        }

        [Fact]
        public async Task OwnedCurrency_NoWalletAtAll_AmountsNull()
        {
            using (var tmp = new TempDirectory())
            {
                var tempDir = tmp.Path;
                var pipeline = BuildVendorCurrencyPipeline(out _, tempDir);

                var result = await pipeline.GenerateStructuredAsync(
                    1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

                Assert.Null(result.OwnedCurrencyAmounts);
            }
        }

        [Fact]
        public async Task OwnedCurrency_ViewModel_CurrencyCostRowGetsOwnedQuantity()
        {
            using (var tmp = new TempDirectory())
            {
                var tempDir = tmp.Path;
                var pipeline = BuildVendorCurrencyPipeline(out _, tempDir);

                var snapshot = new AccountSnapshot
                {
                    Wallet = new List<SnapshotWalletEntry>
                    {
                        new SnapshotWalletEntry { CurrencyId = 2, Value = 200 },
                    },
                };
                var result = await pipeline.GenerateStructuredAsync(
                    1, 1, snapshot, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

                var vm = new PlanViewModelBuilder().Build(result);
                var summarySection = vm.Sections.First(s => s.SectionType == PlanSectionType.Summary);
                var currencyRow = summarySection.Rows.First(r => r.RowType == PlanRowType.CurrencyCost);

                Assert.Equal(200, currencyRow.CurrencyOwnedQuantity);
                Assert.Equal(500, currencyRow.Quantity);
            }
        }

        [Fact]
        public async Task OwnedCurrency_ViewModel_NoWallet_OwnedQuantityNull()
        {
            using (var tmp = new TempDirectory())
            {
                var tempDir = tmp.Path;
                var pipeline = BuildVendorCurrencyPipeline(out _, tempDir);

                var result = await pipeline.GenerateStructuredAsync(
                    1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

                var vm = new PlanViewModelBuilder().Build(result);
                var summarySection = vm.Sections.First(s => s.SectionType == PlanSectionType.Summary);
                var currencyRow = summarySection.Rows.First(r => r.RowType == PlanRowType.CurrencyCost);

                Assert.Null(currencyRow.CurrencyOwnedQuantity);
            }
        }
    }
}
