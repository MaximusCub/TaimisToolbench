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
    /// A vendor can refuse to trade until the account has an achievement, a
    /// mastery or an expansion. These run the real pipeline end to end and
    /// check what the player is told, and that the plan still routes
    /// through the gated vendor either way.
    /// </summary>
    public class VendorRequirementNoticeTests
    {
        private const int GatedItemId = 1;
        private const int TokenCurrencyId = 23;
        private const int AchievementId = 1912;

        private static VendorOffer GatedOffer(VendorRequirement requirement)
        {
            return new VendorOffer
            {
                OfferId = "test-vendor-requirement",
                OutputItemId = GatedItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = TokenCurrencyId, Count = 3 },
                },
                MerchantName = "Quartermaster",
                Locations = new List<string> { "Outer Ring" },
                Requirement = requirement,
            };
        }

        private static VendorRequirement AchievementRequirement()
        {
            return new VendorRequirement
            {
                Text = "Supply Line Management",
                AchievementId = AchievementId,
            };
        }

        private static async Task<CraftingPlanResult> GeneratePlanAsync(
            VendorOffer offer, IAccountProgressionClient progressionClient)
        {
            var builder = PipelineBuilder.Create()
                .WithItem(GatedItemId, "Exalted Helm", "helm.png");

            if (progressionClient != null)
            {
                builder = builder.WithAccountProgressionClient(progressionClient);
            }

            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                store.LoadBaseline(null);
                store.AddOffersToOverlay(new[] { offer });

                var pipeline = builder.WithVendorOfferStore(store).Build();
                return await pipeline.GenerateStructuredAsync(
                    GatedItemId, 1, null, CancellationToken.None,
                    priceBasis: PriceBasis.InstantBuy);
            }
        }

        private static string NoticeLabel(CraftingPlanResult result)
        {
            var vm = new PlanViewModelBuilder().Build(result);
            var section = vm.Sections.Single(
                s => s.SectionType == PlanSectionType.CraftingSteps);
            var row = Assert.Single(
                section.Rows.Where(
                    r => r.RowType == PlanRowType.VendorRequirementNotice));
            return row.Label;
        }

        [Fact]
        public async Task RequirementTheAccountLacks_IsShownAsSomethingTheyDoNotHave()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithAchievements()));

            var notice = Assert.Single(result.VendorRequirementNotices);
            Assert.Equal(GatedItemId, notice.ItemId);
            Assert.Equal(VendorRequirementStatus.NotMet, notice.Status);
            Assert.Equal(
                "Exalted Helm - this vendor requires Supply Line Management, "
                + "which your account does not have",
                NoticeLabel(result));
        }

        [Fact]
        public async Task RequirementTheAccountHas_IsNotMentionedAtAll()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithAchievements(AchievementId)));

            Assert.Empty(result.VendorRequirementNotices);

            var vm = new PlanViewModelBuilder().Build(result);
            Assert.DoesNotContain(
                vm.Sections.SelectMany(s => s.Rows),
                r => r.RowType == PlanRowType.VendorRequirementNotice);
        }

        [Fact]
        public async Task RequirementWithNoAccountDataAtAll_ReadsAsNotCheckedNotAsUnmet()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()), progressionClient: null);

            var notice = Assert.Single(result.VendorRequirementNotices);
            Assert.Equal(VendorRequirementStatus.Unknown, notice.Status);
            Assert.Equal(
                "Exalted Helm - this vendor requires Supply Line Management, "
                + "which was not checked",
                NoticeLabel(result));
        }

        [Fact]
        public async Task RequirementWhoseAccountFetchFailed_ReadsAsNotChecked()
        {
            var progressionClient = new InMemoryAccountProgressionClient(
                InMemoryAccountProgressionClient.WithAchievements(AchievementId))
            {
                ThrowOnGet = true,
            };

            var result = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()), progressionClient);

            var notice = Assert.Single(result.VendorRequirementNotices);
            Assert.Equal(VendorRequirementStatus.Unknown, notice.Status);
        }

        [Fact]
        public async Task RequirementNothingCanClassify_IsStillShownAsText()
        {
            var requirement = new VendorRequirement
            {
                Text = "the respective item not already unlocked in the wardrobe",
            };

            var result = await GeneratePlanAsync(
                GatedOffer(requirement),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithAchievements(AchievementId)));

            var notice = Assert.Single(result.VendorRequirementNotices);
            Assert.Equal(VendorRequirementStatus.Unknown, notice.Status);
            Assert.Contains(
                "the respective item not already unlocked in the wardrobe",
                NoticeLabel(result));
        }

        [Fact]
        public async Task AGatedVendorIsStillTheRouteThePlanPicks()
        {
            // The notice never re-routes the plan. Both accounts buy from
            // the same vendor at the same price.
            var lacking = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithAchievements()));
            var having = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithAchievements(AchievementId)));

            var lackingStep = Assert.Single(lacking.Plan.Steps);
            var havingStep = Assert.Single(having.Plan.Steps);

            Assert.Equal(AcquisitionSource.BuyFromVendor, lackingStep.Source);
            Assert.Equal(AcquisitionSource.BuyFromVendor, havingStep.Source);
            Assert.Equal(havingStep.TotalCost, lackingStep.TotalCost);
            Assert.Equal(
                havingStep.VendorCurrencyCosts?.Count,
                lackingStep.VendorCurrencyCosts?.Count);
        }

        [Fact]
        public async Task AnUngatedVendor_ProducesNoNoticeAndCostsNoAccountCall()
        {
            // The account endpoints are only worth reading when the plan
            // actually runs into a gated vendor, which almost none do.
            var progressionClient = new InMemoryAccountProgressionClient(
                InMemoryAccountProgressionClient.WithAchievements());

            var result = await GeneratePlanAsync(
                GatedOffer(requirement: null), progressionClient);

            Assert.Empty(result.VendorRequirementNotices);
            Assert.Equal(0, progressionClient.GetCallCount);
        }

        [Fact]
        public async Task AGatedVendor_CostsExactlyOneAccountCall()
        {
            var progressionClient = new InMemoryAccountProgressionClient(
                InMemoryAccountProgressionClient.WithAchievements(AchievementId));

            await GeneratePlanAsync(GatedOffer(AchievementRequirement()), progressionClient);

            Assert.Equal(1, progressionClient.GetCallCount);
        }
    }
}
