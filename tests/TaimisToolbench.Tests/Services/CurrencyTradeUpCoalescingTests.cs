using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The reported shape, driven end to end through the real pipeline, the
    /// real solver and the real PlanViewModelBuilder: two things need the
    /// same item by two different routes, and the plan used to state it
    /// twice. One of them took 13 of the item as a vendor barter line and
    /// produced an item row. The other took 5 of it as a recipe ingredient,
    /// the solver bought those from a vendor for 250 map currency each, and
    /// that produced a separate 1,250-currency row. One item, two numbers,
    /// neither of them 18.
    /// <para>
    /// Item 3 stands for Clot of Congealed Screams, Pouch of Stardust and
    /// Case of Captured Lightning, which the shipped corpus prices at a flat
    /// 250 of one map currency each. Currency 75 is Calcified Gasp, one of
    /// the three the module deliberately puts no coin value on
    /// (docs/ARCHITECTURE.md section 8.3).
    /// </para>
    /// </summary>
    public class CurrencyTradeUpCoalescingTests
    {
        private const int BarterParent = 1;
        private const int CraftParent = 2;
        private const int TradedUpItem = 3;

        /// <summary>Calcified Gasp: a map currency with no coin value.</summary>
        private const int SubCurrency = 75;

        /// <summary>Spirit Shard: a currency the curated table values.</summary>
        private const int ValuedCurrency = 23;

        private const int CurrencyPerUnit = 250;

        [Fact]
        public async Task TwoRoutesToOneItem_ProduceOneItemLineAndNoCurrencyLine()
        {
            var result = await GenerateAsync();

            var cost = Assert.Single(result.Plan.BarterItemCosts);
            Assert.Equal(TradedUpItem, cost.ItemId);
            Assert.Equal(18, cost.Amount);
            Assert.Equal(SubCurrency, cost.TradeUpCurrencyId);
            Assert.Equal(CurrencyPerUnit, cost.TradeUpCurrencyPerUnit);

            Assert.Empty(result.Plan.CurrencyCosts);
        }

        [Fact]
        public async Task TheTotalCostTableShowsOneRow_WithOneNumber()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync());

            var row = Assert.Single(NonCoinRows(vm));
            Assert.True(row.IsBarterItemCost);
            Assert.Equal("Clot of Congealed Screams", row.Label);
            Assert.Equal(18, row.Quantity);
        }

        /// <summary>
        /// The holding buys four, so four of the eighteen are covered and
        /// the note says where that came from. Have stays the literal count
        /// of the item itself, which is none.
        /// </summary>
        [Fact]
        public async Task AHoldingThatBuysSeveral_ReadsOnTheNoteAndComesOffNeeded()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync(WalletHolding(1013)));
            var row = Assert.Single(NonCoinRows(vm));

            Assert.Equal(0, row.CurrencyOwnedQuantity);
            Assert.Equal(14, row.CurrencyNeededQuantity);
            Assert.False(row.CurrencyFullyCovered);

            Assert.Equal("Calcified Gasp", row.TradeUpCurrencyName);
            Assert.Equal(1013, row.TradeUpCurrencyHeld);
            Assert.Equal(4, row.TradeUpBuysQuantity);
            Assert.Equal("1013", SummarySectionLayoutMath.TradeUpNoteHeldText(row));
            Assert.Equal("Buys 4", SummarySectionLayoutMath.TradeUpNoteBuysText(row));
        }

        [Fact]
        public async Task AHoldingThatBuysNone_StillReadsOnTheNote()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync(WalletHolding(163)));
            var row = Assert.Single(NonCoinRows(vm));

            Assert.Equal(18, row.CurrencyNeededQuantity);
            Assert.Equal(163, row.TradeUpCurrencyHeld);
            Assert.Equal(0, row.TradeUpBuysQuantity);
            Assert.Equal("163", SummarySectionLayoutMath.TradeUpNoteHeldText(row));
            Assert.Equal("Buys 0", SummarySectionLayoutMath.TradeUpNoteBuysText(row));
        }

        /// <summary>
        /// A holding big enough for every outstanding unit covers the row,
        /// and the note never claims more than the row asks for.
        /// </summary>
        [Fact]
        public async Task AHoldingThatBuysAllOfThem_CoversTheRow()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync(WalletHolding(100000)));
            var row = Assert.Single(NonCoinRows(vm));

            Assert.Equal(18, row.TradeUpBuysQuantity);
            Assert.Equal(0, row.CurrencyNeededQuantity);
            Assert.True(row.CurrencyFullyCovered);
        }

        [Fact]
        public async Task WithNoWalletSnapshot_ThereIsNoNoteAndNoCoverageClaim()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync());
            var row = Assert.Single(NonCoinRows(vm));

            Assert.Null(row.TradeUpCurrencyHeld);
            Assert.Null(SummarySectionLayoutMath.TradeUpNoteHeldText(row));
            Assert.False(SummarySectionLayoutMath.AnyTradeUpNote(new[] { row }));
            Assert.Null(row.CurrencyNeededQuantity);
        }

        /// <summary>
        /// Needed is arithmetic on the player's own wallet, so whether
        /// /v2/currencies has answered yet cannot move it. The same plan
        /// used to read 18 before the fetch landed and 14 after.
        /// </summary>
        [Fact]
        public async Task WithNoCurrencyMetadata_NeededIsStillTheSameNumber()
        {
            var withMetadata = new PlanViewModelBuilder().Build(
                await GenerateAsync(WalletHolding(1013)));
            var without = new PlanViewModelBuilder().Build(
                await GenerateAsync(WalletHolding(1013), currencyMetadata: false));

            var before = Assert.Single(NonCoinRows(without));
            var after = Assert.Single(NonCoinRows(withMetadata));

            Assert.Equal(18, before.Quantity);
            Assert.Equal(14, before.CurrencyNeededQuantity);
            Assert.Equal(after.CurrencyNeededQuantity, before.CurrencyNeededQuantity);

            // The note is still seated. Its icon frame carries the currency
            // name on hover, so the number keeps a derivation on screen even
            // with no art to draw.
            Assert.Equal(1013, before.TradeUpCurrencyHeld);
            Assert.Equal(4, before.TradeUpBuysQuantity);
            Assert.False(string.IsNullOrEmpty(before.TradeUpCurrencyName));
        }

        /// <summary>
        /// A currency the curated table prices has a coin equivalent, so the
        /// plan can compare a route to it and the wallet line is the honest
        /// statement. Only an unvalued one coalesces.
        /// </summary>
        [Fact]
        public async Task AValuedCurrencyStaysAWalletLine()
        {
            var result = await GenerateAsync(currencyId: ValuedCurrency);

            var barter = Assert.Single(result.Plan.BarterItemCosts);
            Assert.Equal(13, barter.Amount);
            Assert.Null(barter.TradeUpCurrencyId);

            var currency = Assert.Single(result.Plan.CurrencyCosts);
            Assert.Equal(ValuedCurrency, currency.CurrencyId);
            Assert.Equal(5L * CurrencyPerUnit, currency.Amount);
        }

        /// <summary>
        /// The plan produces what the user asked for, so its cost is the
        /// currency. Coalescing a requested item would state it as a
        /// requirement for itself.
        /// </summary>
        [Fact]
        public async Task ARequestedItemKeepsItsCurrencyCost()
        {
            var result = await SolveAsync(
                SubCurrency,
                pipeline => pipeline.GenerateStructuredAsync(
                    TradedUpItem, 5, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy));

            Assert.Empty(result.Plan.BarterItemCosts);
            var currency = Assert.Single(result.Plan.CurrencyCosts);
            Assert.Equal(SubCurrency, currency.CurrencyId);
            Assert.Equal(5L * CurrencyPerUnit, currency.Amount);
        }

        /// <summary>
        /// The reported disagreement. The Shopping List used to state the
        /// plan step's own quantity, 5, which is only the part of the
        /// requirement the solver routed through the vendor. The Note on
        /// the same plan said the holding buys 6. Both are 6 now, and it is
        /// one number, not two that happen to match.
        /// </summary>
        [Fact]
        public async Task TheShoppingListStatesWhatTheHoldingBuys_NotTheStepQuantity()
        {
            var result = await GenerateAsync(WalletHolding(1592));
            var step = Assert.Single(result.Plan.Steps, s => s.ItemId == TradedUpItem);
            Assert.Equal(5, step.Quantity);

            var vm = new PlanViewModelBuilder().Build(result);

            Assert.Equal(6, Assert.Single(NonCoinRows(vm)).TradeUpBuysQuantity);
            Assert.Equal(6, TradedUpShoppingRow(vm).Quantity);
        }

        /// <summary>
        /// The listed purchase costs 6 x 250, not the plan step's 5 x 250.
        /// </summary>
        [Fact]
        public async Task TheShoppingListStatesWhatThatPurchaseCosts()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync(WalletHolding(1592)));

            var amount = Assert.Single(TradedUpShoppingRow(vm).CurrencyCosts);
            Assert.Equal("Calcified Gasp", amount.Name);
            Assert.Equal(6 * CurrencyPerUnit, amount.Amount);
        }

        /// <summary>
        /// A holding that buys none is not a directive to buy any, so the
        /// item is not on the list. The Total Cost table still states the
        /// whole requirement.
        /// </summary>
        [Fact]
        public async Task AHoldingThatBuysNone_PutsNoRowOnTheShoppingList()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync(WalletHolding(163)));

            Assert.DoesNotContain(ShoppingRows(vm), r => r.Label == "Clot of Congealed Screams");
            Assert.Equal("Shopping List (1)", ShoppingSection(vm).Title);
            Assert.Equal(18, Assert.Single(NonCoinRows(vm)).Quantity);
        }

        /// <summary>
        /// Without a wallet snapshot the module cannot say what the holding
        /// buys, so it directs no purchase. The Note states nothing on the
        /// same plan.
        /// </summary>
        [Fact]
        public async Task WithNoWalletSnapshot_ThereIsNoShoppingRowEither()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync());

            Assert.Null(Assert.Single(NonCoinRows(vm)).TradeUpCurrencyHeld);
            Assert.DoesNotContain(ShoppingRows(vm), r => r.Label == "Clot of Congealed Screams");
        }

        /// <summary>
        /// Owning some of the item itself shrinks what is left to acquire,
        /// so the holding is capped against the smaller outstanding count.
        /// Have on the Total Cost row stays the literal item count.
        /// </summary>
        [Fact]
        public async Task OwningSomeOfTheItem_CapsTheDirectiveAndLeavesHaveLiteral()
        {
            var vm = new PlanViewModelBuilder().Build(
                await GenerateAsync(WalletHolding(100000, ownedItems: 16)));

            var row = Assert.Single(NonCoinRows(vm));
            Assert.Equal(16, row.CurrencyOwnedQuantity);
            Assert.Equal(2, row.TradeUpBuysQuantity);
            Assert.Equal(2, TradedUpShoppingRow(vm).Quantity);
        }

        /// <summary>
        /// The trade-up step costs no coin, so what the plan claims the
        /// player spends does not move with the listed quantity.
        /// </summary>
        [Fact]
        public async Task ChangingTheListedQuantity_MovesNoCoinOrNonCoinTotal()
        {
            var poorResult = await GenerateAsync(WalletHolding(1250));
            var richResult = await GenerateAsync(WalletHolding(1592));
            var poor = new PlanViewModelBuilder().Build(poorResult);
            var rich = new PlanViewModelBuilder().Build(richResult);

            Assert.Equal(5, TradedUpShoppingRow(poor).Quantity);
            Assert.Equal(6, TradedUpShoppingRow(rich).Quantity);

            Assert.Equal(poorResult.Plan.TotalCoinCost, richResult.Plan.TotalCoinCost);
            Assert.Equal(0, TradedUpShoppingRow(rich).CoinValue);
            Assert.Equal(
                Assert.Single(poor.NonCoinCostTotals).Amount,
                Assert.Single(rich.NonCoinCostTotals).Amount);
        }

        /// <summary>
        /// Only the coalesced item is re-stated. The vendor row for an item
        /// bought outright keeps the plan step's own quantity.
        /// </summary>
        [Fact]
        public async Task AnOrdinaryVendorRowIsUntouched()
        {
            var vm = new PlanViewModelBuilder().Build(await GenerateAsync(WalletHolding(1592)));

            var row = Assert.Single(ShoppingRows(vm), r => r.Label == "Sealed Reliquary");
            Assert.Equal(1, row.Quantity);
        }

        /// <summary>
        /// A plan restored from disk carries the wallet it was generated
        /// with, so both numbers are equally old. They still come from one
        /// derivation, so they still agree; only regenerating the plan
        /// picks up a wallet that has since changed.
        /// </summary>
        [Fact]
        public async Task ARestoredPlan_StillAgreesWithItsOwnNote()
        {
            var result = await GenerateAsync(WalletHolding(1592));

            using (var tmp = new TempDirectory())
            {
                var store = new PlanStore(tmp.Path);
                store.Save(new PersistedPlan
                {
                    SchemaVersion = PersistedPlan.CurrentSchemaVersion,
                    GeneratedAt = new DateTime(2026, 9, 6, 12, 0, 0, DateTimeKind.Local),
                    RequestItems = new List<PlanRequestItem>
                    {
                        new PlanRequestItem { ItemId = BarterParent, Quantity = 1 },
                        new PlanRequestItem { ItemId = CraftParent, Quantity = 1 },
                    },
                    PriceBasis = PriceBasis.InstantBuy,
                    ValueOwnMaterials = true,
                    Result = result,
                });

                var restored = store.LoadLatest()?.Plan?.Result;
                Assert.NotNull(restored);

                var vm = new PlanViewModelBuilder().Build(restored);
                Assert.Equal(6, Assert.Single(NonCoinRows(vm)).TradeUpBuysQuantity);
                Assert.Equal(6, TradedUpShoppingRow(vm).Quantity);
            }
        }

        private static PlanSectionViewModel ShoppingSection(PlanViewModel vm)
        {
            return vm.Sections.Single(s => s.SectionType == PlanSectionType.ShoppingList);
        }

        private static List<PlanRowViewModel> ShoppingRows(PlanViewModel vm)
        {
            return ShoppingSection(vm).Rows;
        }

        private static PlanRowViewModel TradedUpShoppingRow(PlanViewModel vm)
        {
            return Assert.Single(ShoppingRows(vm), r => r.Label == "Clot of Congealed Screams");
        }

        private static List<PlanRowViewModel> NonCoinRows(PlanViewModel vm)
        {
            return vm.Sections
                .Single(s => s.SectionType == PlanSectionType.Summary)
                .Rows
                .Where(r => r.RowType == PlanRowType.CurrencyCost)
                .ToList();
        }

        private static AccountSnapshot WalletHolding(int gasps, int ownedItems = 0)
        {
            var items = new List<SnapshotItemEntry>();
            if (ownedItems > 0)
            {
                items.Add(new SnapshotItemEntry
                {
                    ItemId = TradedUpItem,
                    Name = "Clot of Congealed Screams",
                    Count = ownedItems,
                    Source = "bank",
                });
            }

            return new AccountSnapshot
            {
                Items = items,
                Wallet = new List<SnapshotWalletEntry>
                {
                    new SnapshotWalletEntry { CurrencyId = SubCurrency, CurrencyName = "Calcified Gasp", Value = gasps },
                },
            };
        }

        private static Task<CraftingPlanResult> GenerateAsync(
            AccountSnapshot snapshot = null, int currencyId = SubCurrency,
            bool currencyMetadata = true)
        {
            var items = new List<PlanRequestItem>
            {
                new PlanRequestItem { ItemId = BarterParent, Quantity = 1 },
                new PlanRequestItem { ItemId = CraftParent, Quantity = 1 },
            };

            return SolveAsync(
                currencyId,
                pipeline => pipeline.GenerateStructuredAsync(
                    items, snapshot, CancellationToken.None, priceBasis: PriceBasis.InstantBuy),
                currencyMetadata);
        }

        /// <summary>
        /// Item 1 is vendor-only and its offer takes 13 of item 3 in barter.
        /// Item 2 is crafted from 5 of item 3. Item 3 itself has one vendor
        /// offer, a flat 250 of one currency. Nothing carries a Trading Post
        /// price, so every route above is the only one the solver can take.
        /// </summary>
        private static async Task<CraftingPlanResult> SolveAsync(
            int currencyId, Func<CraftingPlanPipeline, Task<CraftingPlanResult>> generate,
            bool currencyMetadata = true)
        {
            var builder = PipelineBuilder.Create()
                .WithItem(BarterParent, "Sealed Reliquary", "reliquary.png")
                .WithItem(CraftParent, "Warded Sigil", "sigil.png")
                .WithItem(TradedUpItem, "Clot of Congealed Screams", "clot.png")
                .WithSearchResult(CraftParent, 20)
                .WithRecipe(new RawRecipe
                {
                    Id = 20,
                    OutputItemId = CraftParent,
                    OutputItemCount = 1,
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = TradedUpItem, Count = 5 },
                    },
                });

            using (var handler = new StubCurrencyHandler())
            using (var http = new HttpClient(handler))
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[]
                {
                    new VendorOffer
                    {
                        OfferId = "test-barter-parent",
                        OutputItemId = BarterParent,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Item", Id = TradedUpItem, Count = 13 },
                        },
                        MerchantName = "Test NPC",
                        Locations = new List<string>(),
                    },
                    new VendorOffer
                    {
                        OfferId = "test-trade-up",
                        OutputItemId = TradedUpItem,
                        OutputCount = 1,
                        CostLines = new List<CostLine>
                        {
                            new CostLine { Type = "Currency", Id = currencyId, Count = CurrencyPerUnit },
                        },
                        MerchantName = "Test Provisioner",
                        Locations = new List<string>(),
                    },
                });

                builder = builder.WithVendorOfferStore(store);
                if (currencyMetadata)
                {
                    builder = builder.WithCurrencyMetadataService(new CurrencyMetadataService(http));
                }

                return await generate(builder.Build());
            }
        }

        /// <summary>Answers /v2/currencies with the two ids used here.</summary>
        private sealed class StubCurrencyHandler : HttpMessageHandler
        {
            private const string Body = @"[
                { ""id"": 75, ""name"": ""Calcified Gasp"", ""icon"": ""https://render.guildwars2.com/file/gasp.png"" },
                { ""id"": 23, ""name"": ""Spirit Shard"", ""icon"": ""https://render.guildwars2.com/file/shard.png"" }
            ]";

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(Body),
                });
            }
        }
    }
}
