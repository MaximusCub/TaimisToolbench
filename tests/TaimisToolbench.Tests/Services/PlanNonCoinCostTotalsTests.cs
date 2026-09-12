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
    /// <summary>
    /// The plan's whole NON-COIN price, driven end to end through the real
    /// pipeline, the real solver and the real PlanViewModelBuilder.
    /// <para>
    /// A vendor paid in an untradeable token folds nothing into
    /// CraftingPlan.TotalCoinCost - the token's units ARE the price - so a
    /// plan that reported only its coin figure reported less than it costs.
    /// These tests pin the aggregate that closes that hole and the Total
    /// Cost section that shows it.
    /// </para>
    /// </summary>
    public class PlanNonCoinCostTotalsTests
    {
        private const int Target = 1;
        private const int BarterToken = 99;
        private const int SecondBarterToken = 98;
        private const int SpiritShardCurrency = 23;
        private const int AscalonianTearsCurrency = 5;

        /// <summary>
        /// Item 1 has no recipe and no Trading Post price, so the vendor
        /// offer below is its only route; the token it costs has neither
        /// either, which is exactly what makes that cost line a BARTER
        /// line rather than money.
        /// </summary>
        private static async Task<CraftingPlanResult> GenerateAsync(
            IEnumerable<CostLine> costLines,
            int outputCount = 1,
            int requestQuantity = 1,
            AccountSnapshot snapshot = null)
        {
            var builder = PipelineBuilder.Create()
                .WithItem(Target, "Vendor Only Widget", "widget.png")
                .WithItem(BarterToken, "Blue Prophet Shard", "shard.png")
                .WithItem(SecondBarterToken, "Ancient Coin", "coin.png")
                .WithInventoryReducer();

            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-barter-w5",
                        OutputItemId = Target,
                        OutputCount = outputCount,
                        CostLines = new List<CostLine>(costLines),
                        MerchantName = "Test NPC",
                    },
                });

                return await builder.WithVendorOfferStore(store).Build()
                    .GenerateStructuredAsync(
                        Target, requestQuantity, snapshot, CancellationToken.None,
                        priceBasis: PriceBasis.InstantBuy);
            }
        }

        private static IEnumerable<CostLine> BarterOnly(int count)
        {
            yield return new CostLine { Type = "Item", Id = BarterToken, Count = count };
        }

        private static IEnumerable<CostLine> CurrencyOnly(int count)
        {
            yield return new CostLine { Type = "Currency", Id = SpiritShardCurrency, Count = count };
        }

        /// <summary>
        /// The table's rows as the renderer and the height math see them:
        /// through the one production grouping both of them draw from.
        /// </summary>
        private static IReadOnlyList<SummarySectionLayoutMath.NonCoinRowGroup> Groups(PlanViewModel vm)
        {
            return SummarySectionLayoutMath.GroupNonCoinRows(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.Summary).Rows);
        }

        private static List<PlanRowViewModel> NonCoinRows(PlanViewModel vm)
        {
            return vm.Sections
                .Single(s => s.SectionType == PlanSectionType.Summary)
                .Rows
                .Where(r => r.RowType == PlanRowType.CurrencyCost)
                .ToList();
        }

        private static List<PlanRowViewModel> Footnotes(PlanViewModel vm)
        {
            return vm.Sections
                .Single(s => s.SectionType == PlanSectionType.Summary)
                .Rows
                .Where(r => r.RowType == PlanRowType.SummaryFootnote)
                .ToList();
        }

        [Fact]
        public async Task BarterOnlyVendorOffer_ReachesThePlanLevelTotal()
        {
            var result = await GenerateAsync(BarterOnly(3), requestQuantity: 2);

            Assert.Equal(AcquisitionSource.BuyFromVendor, Assert.Single(result.Plan.Steps).Source);

            // The whole cost of this plan, and none of it is coin.
            Assert.Equal(0, result.Plan.TotalCoinCost);
            var barter = Assert.Single(result.Plan.BarterItemCosts);
            Assert.Equal(BarterToken, barter.ItemId);
            Assert.Equal(6, barter.Amount);
        }

        [Fact]
        public async Task BarterOnlyVendorOffer_ShowsInTheTotalCostSection()
        {
            var result = await GenerateAsync(BarterOnly(3), requestQuantity: 2);
            var vm = new PlanViewModelBuilder().Build(result);

            var row = Assert.Single(NonCoinRows(vm));
            Assert.True(row.IsBarterItemCost);
            Assert.Equal("Blue Prophet Shard", row.Label);
            Assert.Equal(6, row.Quantity);

            // No account snapshot at all, so the holding is unknown -
            // never a fabricated zero, and never a coverage claim.
            Assert.Null(row.CurrencyOwnedQuantity);
            Assert.Null(row.CurrencyNeededQuantity);
            Assert.False(row.CurrencyFullyCovered);

            var total = Assert.Single(vm.NonCoinCostTotals);
            Assert.Equal("Blue Prophet Shard", total.Name);
            Assert.Equal(6, total.Amount);
            Assert.Null(total.OwnedQuantity);
        }

        /// <summary>
        /// An account snapshot holding <paramref name="ownedTokens"/> of
        /// the barter token and nothing else.
        /// </summary>
        private static AccountSnapshot SnapshotHolding(int ownedTokens)
        {
            return new AccountSnapshot
            {
                Items = new List<SnapshotItemEntry>
                {
                    new SnapshotItemEntry
                    {
                        ItemId = BarterToken,
                        Count = ownedTokens,
                        Source = AccountItemIndex.SourceMaterialStorage,
                    },
                },
            };
        }

        private static PlanRowViewModel BarterRow(CraftingPlanResult result)
        {
            var vm = new PlanViewModelBuilder().Build(result);
            return Assert.Single(NonCoinRows(vm).Where(r => r.IsBarterItemCost));
        }

        /// <summary>
        /// An inventory row's Have and Needed are real numbers, from the
        /// account's own count of the token. The plan needs 6 and the
        /// account holds 4, so 2 are still to find.
        /// </summary>
        [Fact]
        public async Task BarterRow_PartialHolding_FillsHaveAndNeeded()
        {
            var result = await GenerateAsync(
                BarterOnly(3), requestQuantity: 2, snapshot: SnapshotHolding(4));

            var row = BarterRow(result);
            Assert.Equal(6, row.Quantity);
            Assert.Equal(4, row.CurrencyOwnedQuantity);
            Assert.Equal(2, row.CurrencyNeededQuantity);
            Assert.False(row.CurrencyFullyCovered);
        }

        /// <summary>
        /// Holding the lot closes the gap and lights the coverage marker,
        /// on the same terms a wallet currency gets it: the holding is
        /// known and it meets the requirement. The holding is reported RAW,
        /// so an account with more than the plan needs says so.
        /// </summary>
        [Fact]
        public async Task BarterRow_FullHolding_ClosesTheGapAndMarksItCovered()
        {
            var result = await GenerateAsync(
                BarterOnly(3), requestQuantity: 2, snapshot: SnapshotHolding(10));

            var row = BarterRow(result);
            Assert.Equal(10, row.CurrencyOwnedQuantity);
            Assert.Equal(0, row.CurrencyNeededQuantity);
            Assert.True(row.CurrencyFullyCovered);

            // Owning the token is cosmetic: the plan still costs 6 of it.
            Assert.Equal(6, Assert.Single(result.Plan.BarterItemCosts).Amount);
            Assert.Equal(6, row.Quantity);
        }

        /// <summary>
        /// The distinction the whole null contract exists for: a snapshot
        /// that shows none of the token is a known ZERO, not the unknown a
        /// missing snapshot gives.
        /// </summary>
        [Fact]
        public async Task BarterRow_SnapshotHoldingNoneOfIt_ReadsZeroRatherThanUnknown()
        {
            var result = await GenerateAsync(
                BarterOnly(3), requestQuantity: 2, snapshot: new AccountSnapshot());

            var row = BarterRow(result);
            Assert.Equal(0, row.CurrencyOwnedQuantity);
            Assert.Equal(6, row.CurrencyNeededQuantity);
            Assert.False(row.CurrencyFullyCovered);
        }

        /// <summary>
        /// One source, two consumers: the Recipe Tree's cost-component leaf
        /// and the Total Cost table both state the account's holding of the
        /// same token, so they have to state the same number. A second
        /// count derived anywhere else is what this pins against.
        /// </summary>
        [Fact]
        public async Task BarterRow_AndItsTreeLeaf_StateTheSameHolding()
        {
            var result = await GenerateAsync(
                BarterOnly(3), requestQuantity: 2, snapshot: SnapshotHolding(4));

            var leaf = Assert.Single(
                result.CraftingTree.Children.Where(c => c.IsCostComponent && c.ItemId == BarterToken));
            Assert.Equal(leaf.ComponentOwnedQuantity, BarterRow(result).CurrencyOwnedQuantity);
        }

        /// <summary>
        /// A barter cost is a real cost the coin total counts as zero, so
        /// the floor disclosure has to fire on it.
        /// </summary>
        [Fact]
        public async Task BarterCost_FiresTheFloorDisclosure()
        {
            var result = await GenerateAsync(BarterOnly(3), requestQuantity: 2);
            var vm = new PlanViewModelBuilder().Build(result);

            Assert.Contains(
                Footnotes(vm),
                f => f.Label == PlanViewModelBuilder.UnpricedFootnoteText);
        }

        /// <summary>
        /// The merged-ceil contract, on the barter side: a "1 token buys 2"
        /// offer bought for a demand of 3 is TWO purchases, so two tokens -
        /// not the three a per-unit multiplication would report. Same
        /// derivation as the coin and currency totals beside it
        /// (docs/ARCHITECTURE.md, "Merged-ceil vendor batching").
        /// </summary>
        [Fact]
        public async Task BarterTotal_ComesFromTheOffersBatchShape()
        {
            var result = await GenerateAsync(BarterOnly(1), outputCount: 2, requestQuantity: 3);

            var barter = Assert.Single(result.Plan.BarterItemCosts);
            Assert.Equal(2, barter.Amount);

            var step = Assert.Single(result.Plan.Steps);
            Assert.Equal(3, step.Quantity);
            var stepLine = Assert.Single(step.VendorBarterItemCosts);
            Assert.Equal(BarterToken, stepLine.Id);
            Assert.Equal(2, stepLine.Count);
        }

        [Fact]
        public async Task CurrencyAndBarterInOneOffer_BothReachTheTable()
        {
            var result = await GenerateAsync(new[]
            {
                new CostLine { Type = "Item", Id = BarterToken, Count = 2 },
                new CostLine { Type = "Currency", Id = SpiritShardCurrency, Count = 5 },
            });

            Assert.Equal(2, Assert.Single(result.Plan.BarterItemCosts).Amount);
            Assert.Equal(5, Assert.Single(result.Plan.CurrencyCosts).Amount);

            var vm = new PlanViewModelBuilder().Build(result);
            var rows = NonCoinRows(vm);
            Assert.Equal(2, rows.Count);

            // Grouped, not interleaved: the wallet currency leads even
            // though the barter item's name sorts ahead of it, because the
            // two are checked in two different places.
            Assert.False(rows[0].IsBarterItemCost);
            Assert.Equal("Spirit Shards", rows[0].Label);
            Assert.True(rows[1].IsBarterItemCost);
            Assert.Equal("Blue Prophet Shard", rows[1].Label);
            Assert.True(
                string.CompareOrdinal(rows[1].Label, rows[0].Label) < 0,
                "the case is only interesting while the barter name sorts first");

            // The plan-level list is that same order - it is projected from
            // these very rows.
            Assert.Equal(
                rows.Select(r => r.Label).ToList(),
                vm.NonCoinCostTotals.Select(t => t.Name).ToList());
        }

        [Fact]
        public async Task CurrencyAndBarter_SplitIntoTheWalletAndInventoryGroups()
        {
            var result = await GenerateAsync(new[]
            {
                new CostLine { Type = "Item", Id = BarterToken, Count = 2 },
                new CostLine { Type = "Currency", Id = SpiritShardCurrency, Count = 5 },
            });

            var vm = new PlanViewModelBuilder().Build(result);
            var groups = Groups(vm);

            Assert.Equal(2, groups.Count);
            Assert.Equal(SummarySectionLayoutMath.WalletGroupHeading, groups[0].Heading);
            Assert.Equal("Spirit Shards", Assert.Single(groups[0].Rows).Label);
            Assert.Equal(SummarySectionLayoutMath.InventoryGroupHeading, groups[1].Heading);
            Assert.Equal("Blue Prophet Shard", Assert.Single(groups[1].Rows).Label);
        }

        /// <summary>
        /// A plan with nothing to check in inventory must not grow an
        /// inventory heading with no rows under it, and vice versa.
        /// </summary>
        [Fact]
        public async Task OneKindOnly_DrawsThatGroupsHeadingAndNoOther()
        {
            var barterVm = new PlanViewModelBuilder()
                .Build(await GenerateAsync(BarterOnly(3), requestQuantity: 2));
            var barterGroup = Assert.Single(Groups(barterVm));
            Assert.Equal(SummarySectionLayoutMath.InventoryGroupHeading, barterGroup.Heading);
            Assert.Equal("Blue Prophet Shard", Assert.Single(barterGroup.Rows).Label);

            var currencyVm = new PlanViewModelBuilder()
                .Build(await GenerateAsync(CurrencyOnly(5)));
            var currencyGroup = Assert.Single(Groups(currencyVm));
            Assert.Equal(SummarySectionLayoutMath.WalletGroupHeading, currencyGroup.Heading);
            Assert.Equal("Spirit Shards", Assert.Single(currencyGroup.Rows).Label);
        }

        /// <summary>
        /// Grouping did not cost the table its alphabetical order - it
        /// moved it inside each group.
        /// </summary>
        [Fact]
        public async Task EachGroupIsAlphabeticalWithinItself()
        {
            var result = await GenerateAsync(new[]
            {
                new CostLine { Type = "Currency", Id = SpiritShardCurrency, Count = 5 },
                new CostLine { Type = "Item", Id = BarterToken, Count = 2 },
                new CostLine { Type = "Currency", Id = AscalonianTearsCurrency, Count = 7 },
                new CostLine { Type = "Item", Id = SecondBarterToken, Count = 1 },
            });

            var vm = new PlanViewModelBuilder().Build(result);
            var groups = Groups(vm);
            Assert.Equal(2, groups.Count);
            Assert.Equal(
                new[] { "Ascalonian Tears", "Spirit Shards" },
                groups[0].Rows.Select(r => r.Label).ToArray());
            Assert.Equal(
                new[] { "Ancient Coin", "Blue Prophet Shard" },
                groups[1].Rows.Select(r => r.Label).ToArray());
        }

        /// <summary>
        /// The one aggregation, still one: NonCoinCostTotals is exactly the
        /// table's cost rows, in the table's own grouped order, with the
        /// same amounts. Nothing a group heading contributed can appear
        /// here, because a heading is not a row.
        /// </summary>
        [Fact]
        public async Task NonCoinCostTotals_AreExactlyTheGroupedCostRows()
        {
            var result = await GenerateAsync(new[]
            {
                new CostLine { Type = "Currency", Id = SpiritShardCurrency, Count = 5 },
                new CostLine { Type = "Item", Id = BarterToken, Count = 2 },
                new CostLine { Type = "Currency", Id = AscalonianTearsCurrency, Count = 7 },
                new CostLine { Type = "Item", Id = SecondBarterToken, Count = 1 },
            });

            var vm = new PlanViewModelBuilder().Build(result);
            var rows = NonCoinRows(vm);
            var grouped = Groups(vm).SelectMany(g => g.Rows).ToList();

            // Every cost row is in exactly one group, and the section's own
            // row order already IS the grouped order.
            Assert.Equal(rows, grouped);
            Assert.Equal(4, rows.Count);

            Assert.Equal(
                rows.Select(r => r.Label).ToList(),
                vm.NonCoinCostTotals.Select(t => t.Name).ToList());
            Assert.Equal(
                rows.Select(r => (long)r.Quantity).ToList(),
                vm.NonCoinCostTotals.Select(t => t.Amount).ToList());
            Assert.DoesNotContain(
                vm.NonCoinCostTotals,
                t => t.Name == SummarySectionLayoutMath.WalletGroupHeading
                    || t.Name == SummarySectionLayoutMath.InventoryGroupHeading);
        }

        /// <summary>
        /// A batch solves ONE synthetic wrapper tree, so its barter total
        /// is the sum across every requested item with nothing to merge -
        /// pinned here because a future batch path that stitched separate
        /// plans together would have to sum this list too.
        /// </summary>
        [Fact]
        public async Task MultiItemBatch_SumsTheBarterCostAcrossRequestedItems()
        {
            const int SecondTarget = 2;
            var builder = PipelineBuilder.Create()
                .WithItem(Target, "Vendor Only Widget", "widget.png")
                .WithItem(SecondTarget, "Other Vendor Widget", "other.png")
                .WithItem(BarterToken, "Blue Prophet Shard", "shard.png")
                .WithInventoryReducer();

            CraftingPlanResult result;
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    BarterOffer("test-barter-w5-a", Target, 3),
                    BarterOffer("test-barter-w5-b", SecondTarget, 4),
                });

                result = await builder.WithVendorOfferStore(store).Build()
                    .GenerateStructuredAsync(
                        new List<PlanRequestItem>
                        {
                            new PlanRequestItem { ItemId = Target, Quantity = 2 },
                            new PlanRequestItem { ItemId = SecondTarget, Quantity = 1 },
                        },
                        null,
                        CancellationToken.None,
                        priceBasis: PriceBasis.InstantBuy);
            }

            // 2 x 3 + 1 x 4
            var barter = Assert.Single(result.Plan.BarterItemCosts);
            Assert.Equal(BarterToken, barter.ItemId);
            Assert.Equal(10, barter.Amount);

            var vm = new PlanViewModelBuilder().Build(result);
            Assert.Equal(10, Assert.Single(vm.NonCoinCostTotals).Amount);
        }

        private static VendorOffer BarterOffer(string offerId, int outputItemId, int tokenCount)
        {
            return new VendorOffer
            {
                OfferId = offerId,
                OutputItemId = outputItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Item", Id = BarterToken, Count = tokenCount },
                },
                MerchantName = "Test NPC",
            };
        }

        /// <summary>
        /// The common case: a plan that costs nothing but coin grows no
        /// table, no disclosure line and no plan-level list.
        /// </summary>
        [Fact]
        public async Task CoinOnlyPlan_AddsNoNonCoinChrome()
        {
            var pipeline = PipelineBuilder.Create()
                .WithPrice(Target, buyUnitPrice: 500, sellUnitPrice: 900)
                .WithItem(Target, "Plain Widget", "widget.png")
                .WithInventoryReducer()
                .Build();

            var result = await pipeline.GenerateStructuredAsync(
                Target, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);

            Assert.Empty(result.Plan.BarterItemCosts);
            Assert.Empty(result.Plan.CurrencyCosts);

            var vm = new PlanViewModelBuilder().Build(result);
            Assert.Empty(NonCoinRows(vm));
            Assert.Empty(Groups(vm));
            Assert.Null(vm.NonCoinCostTotals);

            var footnote = Assert.Single(Footnotes(vm));
            Assert.Equal(PlanViewModelBuilder.FootnoteText, footnote.Label);
            Assert.All(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.Summary).Rows
                    .Where(r => r.RowType == PlanRowType.CostFormulaTile),
                t => Assert.DoesNotContain(PlanViewModelBuilder.UnpricedTileMarker, t.Label));
        }
    }
}
