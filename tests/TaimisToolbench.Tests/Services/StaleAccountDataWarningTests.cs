using System;
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
    /// Every plan here comes out of the real pipeline, so what the decision
    /// reads - the solved tree, the currency costs, the required
    /// disciplines - is what a generation actually produces rather than a
    /// hand-built stand-in.
    /// </summary>
    public class StaleAccountDataWarningTests
    {
        private static readonly DateTime CapturedAt = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

        private static readonly DateTime Now = CapturedAt.AddMinutes(14);

        /// <summary>
        /// The suite's craft tree solved for real, with prices that make the
        /// craft win so the plan carries both the ingredient and a
        /// discipline requirement.
        /// </summary>
        private static async Task<CraftingPlanResult> CraftPlanAsync(AccountSnapshot snapshot)
        {
            var builder = PipelineBuilder.SingleRecipeTree(5).WithInventoryReducer();
            builder.WithPrice(1, buyUnitPrice: 400, sellUnitPrice: 1000);
            builder.WithPrice(2, buyUnitPrice: 10, sellUnitPrice: 100);

            return await builder.Build().GenerateStructuredAsync(
                1, 1, snapshot, CancellationToken.None,
                priceBasis: PriceBasis.InstantBuy);
        }

        private static AccountSnapshot Snapshot(params SnapshotItemEntry[] items)
        {
            return new AccountSnapshot
            {
                CapturedAt = CapturedAt,
                Items = items.ToList(),
            };
        }

        private static SnapshotItemEntry Held(int itemId, string name, string source, int count = 3)
        {
            return new SnapshotItemEntry
            {
                ItemId = itemId,
                Name = name,
                Count = count,
                Source = source,
            };
        }

        [Fact]
        public async Task MaterialStorageFails_AndHoldsNothingThePlanNeeds_SaysNothing()
        {
            // The plan needs items 1 and 2. Material storage holds neither;
            // the bank holds the ingredient. Refreshing material storage
            // could not have changed what this plan subtracted.
            var snapshot = Snapshot(
                Held(2, "Ingredient", AccountItemIndex.SourceBank),
                Held(999, "Something Else", AccountItemIndex.SourceMaterialStorage));

            var result = await CraftPlanAsync(snapshot);

            var notice = StaleAccountDataWarning.Evaluate(
                new[] { AccountDataSource.MaterialStorage },
                null,
                snapshot,
                result,
                planUsedHoldings: true,
                Now);

            Assert.Null(notice);
        }

        [Fact]
        public async Task MaterialStorageFails_AndHoldsAnIngredient_NamesItAndTheSource()
        {
            // Round-tripped through the real store: the snapshot a failed
            // refresh leaves behind is the one on disk.
            using (var tmp = new TempDirectory())
            {
                var store = new SnapshotStore(tmp.Path);
                store.Save(Snapshot(Held(2, "Ingredient", AccountItemIndex.SourceMaterialStorage)));
                var snapshot = store.LoadLatest();

                var result = await CraftPlanAsync(snapshot);

                var notice = StaleAccountDataWarning.Evaluate(
                    new[] { AccountDataSource.MaterialStorage },
                    null,
                    snapshot,
                    result,
                    planUsedHoldings: true,
                    Now);

                Assert.NotNull(notice);
                Assert.Equal(new[] { AccountDataSource.MaterialStorage }, notice.Sources);
                Assert.Equal(new[] { "Ingredient" }, notice.ItemNames);

                string message = StaleAccountDataWarning.Compose(notice);
                Assert.Equal(
                    "Could not refresh your account, so this plan used data from 14 minutes ago. "
                    + "Your material storage could not be read, and what you own may have changed since.",
                    message);

                // The detail the dialog no longer carries is not lost. It
                // moved to the line the Log tab shows.
                string detail = StaleAccountDataWarning.ComposeLogDetail(notice);
                Assert.Contains("material storage", detail);
                Assert.Contains("Ingredient", detail);
                Assert.Contains("14 minutes ago", detail);

                // The item name is exactly what the dialog must not spell
                // out - it is what grew the old message to six sentences.
                Assert.DoesNotContain("Ingredient", message);
            }
        }

        [Fact]
        public async Task AnIngredientTheAccountCoversInFull_IsStillNamed()
        {
            // The reducer clears the sub-recipes of an ingredient the
            // account covers, so the solved tree can stop carrying that
            // branch. The reduction's own record of what it consumed is
            // what keeps the item in the plan's set.
            var snapshot = Snapshot(
                Held(2, "Ingredient", AccountItemIndex.SourceMaterialStorage, count: 50));

            var result = await CraftPlanAsync(snapshot);
            Assert.Equal(0, result.Plan.TotalCoinCost);
            Assert.Single(result.UsedMaterials);

            var notice = StaleAccountDataWarning.Evaluate(
                new[] { AccountDataSource.MaterialStorage },
                null,
                snapshot,
                result,
                planUsedHoldings: true,
                Now);

            Assert.NotNull(notice);
            Assert.Equal(new[] { "Ingredient" }, notice.ItemNames);
        }

        [Fact]
        public async Task UseOwnMaterialsOff_SaysNothingAboutHoldings()
        {
            // The plan read no holding at all, so no item source could have
            // mattered to it.
            var snapshot = Snapshot(Held(2, "Ingredient", AccountItemIndex.SourceMaterialStorage));
            var result = await CraftPlanAsync(null);

            var notice = StaleAccountDataWarning.Evaluate(
                new[] { AccountDataSource.MaterialStorage },
                null,
                snapshot,
                result,
                planUsedHoldings: false,
                Now);

            Assert.Null(notice);
        }

        [Fact]
        public async Task WalletFails_AndThePlanSpendsACurrency_Warns()
        {
            using (var tmp = new TempDirectory())
            {
                var result = await KarmaPlanAsync(tmp.Path);
                Assert.NotEmpty(result.Plan.CurrencyCosts);

                var snapshot = Snapshot();
                var notice = StaleAccountDataWarning.Evaluate(
                    new[] { AccountDataSource.Wallet },
                    null,
                    snapshot,
                    result,
                    planUsedHoldings: true,
                    Now);

                Assert.NotNull(notice);
                Assert.True(notice.AffectsCurrencyAmounts);
                Assert.Contains("wallet", StaleAccountDataWarning.Compose(notice));
            }
        }

        [Fact]
        public async Task WalletFails_AndThePlanSpendsNoCurrency_SaysNothing()
        {
            // A craft paid for in coin only. The solver never asks the
            // wallet what the account's coin balance is, so a wallet that
            // went unread changes nothing this plan shows.
            var snapshot = Snapshot(Held(2, "Ingredient", AccountItemIndex.SourceBank));
            var result = await CraftPlanAsync(snapshot);
            Assert.Empty(result.Plan.CurrencyCosts);

            var notice = StaleAccountDataWarning.Evaluate(
                new[] { AccountDataSource.Wallet },
                null,
                snapshot,
                result,
                planUsedHoldings: true,
                Now);

            Assert.Null(notice);
        }

        [Fact]
        public async Task CharactersIncomplete_AndThePlanCrafts_Warns()
        {
            var snapshot = Snapshot();
            var result = await CraftPlanAsync(snapshot);
            Assert.NotEmpty(result.RequiredDisciplines);

            var notice = StaleAccountDataWarning.Evaluate(
                Array.Empty<AccountDataSource>(),
                new[] { "Taimi" },
                snapshot,
                result,
                planUsedHoldings: true,
                Now);

            Assert.NotNull(notice);
            Assert.Equal(new[] { AccountDataSource.Characters }, notice.Sources);
            Assert.True(notice.AffectsCraftingDisciplines);
        }

        [Fact]
        public async Task CharactersIncomplete_AndThePlanOnlyBuys_SaysNothing()
        {
            using (var tmp = new TempDirectory())
            {
                var result = await KarmaPlanAsync(tmp.Path);
                Assert.Empty(result.RequiredDisciplines);

                var notice = StaleAccountDataWarning.Evaluate(
                    Array.Empty<AccountDataSource>(),
                    new[] { "Taimi" },
                    Snapshot(),
                    result,
                    planUsedHoldings: true,
                    Now);

                Assert.Null(notice);
            }
        }

        [Fact]
        public async Task NothingFailed_SaysNothing()
        {
            var snapshot = Snapshot(Held(2, "Ingredient", AccountItemIndex.SourceMaterialStorage));
            var result = await CraftPlanAsync(snapshot);

            Assert.Null(StaleAccountDataWarning.Evaluate(
                Array.Empty<AccountDataSource>(), null, snapshot, result, true, Now));
        }

        [Fact]
        public async Task NoSnapshotAtAll_SaysNothing()
        {
            // Nothing went stale, because nothing was ever captured. The
            // Use Own Materials gate already tells the user this.
            var result = await CraftPlanAsync(null);

            Assert.Null(StaleAccountDataWarning.Evaluate(
                AccountDataSources.All, null, null, result, false, Now));
        }

        [Fact]
        public async Task TheMessageIsOneParagraphWithNoLineBreaks()
        {
            var snapshot = Snapshot(Held(2, "Ingredient", AccountItemIndex.SourceMaterialStorage));
            var result = await CraftPlanAsync(snapshot);

            var notice = StaleAccountDataWarning.Evaluate(
                AccountDataSources.All, null, snapshot, result, true, Now);

            string message = StaleAccountDataWarning.Compose(notice);
            Assert.DoesNotContain("\n", message);
            Assert.DoesNotContain("\r", message);
        }

        /// <summary>
        /// The message this replaced ran to six sentences over four
        /// branches, and every branch had been added one at a time. This is
        /// the test that has to fail before a seventh can arrive.
        /// </summary>
        [Fact]
        public async Task TheMessageStaysTwoSentencesAtEveryShapeItCanTake()
        {
            // The ingredient sits in both, so both count as unread data the
            // plan depends on and the two-source shape is reachable.
            var snapshot = Snapshot(
                Held(2, "Ingredient", AccountItemIndex.SourceMaterialStorage),
                Held(2, "Ingredient", AccountItemIndex.SourceBank));
            var result = await CraftPlanAsync(snapshot);

            var shapes = new IReadOnlyList<AccountDataSource>[]
            {
                new[] { AccountDataSource.MaterialStorage },
                new[] { AccountDataSource.Bank, AccountDataSource.MaterialStorage },
                AccountDataSources.All,
            };

            foreach (var failed in shapes)
            {
                var notice = StaleAccountDataWarning.Evaluate(
                    failed, null, snapshot, result, true, Now);
                Assert.NotNull(notice);

                string message = StaleAccountDataWarning.Compose(notice);
                Assert.True(
                    message.Length <= StaleAccountDataWarning.MaxMessageLength,
                    $"{message.Length} chars, over the {StaleAccountDataWarning.MaxMessageLength} cap: {message}");
                Assert.Equal(2, message.Count(c => c == '.'));

                // The four things that were cut and must not come back.
                Assert.DoesNotContain("We cannot tell", message);
                Assert.DoesNotContain("disciplines", message);
                Assert.DoesNotContain("currencies", message);
                Assert.DoesNotContain("Ingredient", message);
            }
        }

        /// <summary>
        /// Two unread sources are joined as prose, not as a comma list, and
        /// three or more stop being named at all - see
        /// StaleAccountDataWarning's own note on why.
        /// </summary>
        [Fact]
        public async Task MoreThanTwoUnreadSources_AreSummarisedRatherThanListed()
        {
            var snapshot = Snapshot(
                Held(2, "Ingredient", AccountItemIndex.SourceMaterialStorage),
                Held(2, "Ingredient", AccountItemIndex.SourceBank));
            var result = await CraftPlanAsync(snapshot);

            var two = StaleAccountDataWarning.Evaluate(
                new[] { AccountDataSource.Bank, AccountDataSource.MaterialStorage },
                null, snapshot, result, true, Now);
            Assert.Contains(
                "Your bank and material storage could not be read",
                StaleAccountDataWarning.Compose(two));

            var many = StaleAccountDataWarning.Evaluate(
                AccountDataSources.All, null, snapshot, result, true, Now);
            string manyMessage = StaleAccountDataWarning.Compose(many);
            Assert.Contains("Several parts of your account could not be read", manyMessage);
            Assert.DoesNotContain("material storage", manyMessage);
        }

        /// <summary>
        /// Evaluate never produces a notice with no source, so this branch
        /// is reachable only through Compose's own public seam. It is here
        /// because the closer has to carry a capital when it becomes the
        /// whole sentence, and nothing else exercises that.
        /// </summary>
        [Fact]
        public void AMessageWithNoSourceToName_StillReadsAsASentence()
        {
            var notice = new StaleAccountDataNotice(
                new AccountDataSource[0],
                new string[0],
                affectsCurrencyAmounts: false,
                affectsCraftingDisciplines: false,
                age: TimeSpan.FromMinutes(14));

            Assert.Equal(
                "Could not refresh your account, so this plan used data from 14 minutes ago. "
                + "What you own may have changed since.",
                StaleAccountDataWarning.Compose(notice));
        }

        /// <summary>
        /// The honest closer survives the rewrite. It is the one sentence
        /// that must never become either "this plan is wrong" or "this plan
        /// is fine", and it now carries that meaning on its own.
        /// </summary>
        [Fact]
        public async Task TheMessageNeitherCondemnsNorClearsThePlan()
        {
            var snapshot = Snapshot(Held(2, "Ingredient", AccountItemIndex.SourceMaterialStorage));
            var result = await CraftPlanAsync(snapshot);

            var notice = StaleAccountDataWarning.Evaluate(
                new[] { AccountDataSource.MaterialStorage }, null, snapshot, result, true, Now);

            string message = StaleAccountDataWarning.Compose(notice);
            Assert.Contains("may have changed since", message);
            Assert.DoesNotContain("wrong", message);
            Assert.DoesNotContain("incorrect", message);
            Assert.DoesNotContain("still correct", message);
            Assert.DoesNotContain("unaffected", message);
        }

        /// <summary>
        /// A vendor-only purchase paid for in karma: no recipe, no coin
        /// price, so the plan carries a currency cost and no discipline.
        /// </summary>
        private static async Task<CraftingPlanResult> KarmaPlanAsync(string tempDir)
        {
            var loader = new VendorOfferLoader();
            var store = new VendorOfferStore(tempDir, loader);
            store.LoadBaseline(null);
            store.AddOffersToOverlay(new[]
            {
                new VendorOffer
                {
                    OfferId = "stale-warning-karma-offer",
                    OutputItemId = 1,
                    OutputCount = 1,
                    CostLines = new List<CostLine>
                    {
                        new CostLine { Type = "Currency", Id = 2, Count = 500 },
                    },
                    MerchantName = "Karma Vendor",
                },
            });

            var pipeline = PipelineBuilder.Create()
                .WithItem(1, "Karma Item", "karma.png")
                .WithVendorOfferStore(store)
                .WithInventoryReducer()
                .Build();

            return await pipeline.GenerateStructuredAsync(
                1, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
        }
    }
}
