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
            VendorOffer offer, IAccountProgressionClient progressionClient,
            ModuleLog moduleLog = null)
        {
            var builder = PipelineBuilder.Create()
                .WithItem(GatedItemId, "Exalted Helm", "helm.png");

            if (progressionClient != null)
            {
                builder = builder.WithAccountProgressionClient(progressionClient);
            }

            if (moduleLog != null)
            {
                builder = builder.WithModuleLog(moduleLog);
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

        /// <summary>
        /// The notice as the view is handed it. Read out of Plan Notes,
        /// the section it belongs to: it is a caveat about a purchase in
        /// the Shopping List, and the Crafting Steps list it used to trail
        /// has no row for a vendor purchase at all.
        /// </summary>
        private static string NoticeLabel(CraftingPlanResult result)
        {
            var vm = new PlanViewModelBuilder().Build(result);
            var section = vm.Sections.Single(s => s.SectionType == PlanSectionType.Notes);
            return Assert.Single(section.Rows).Label;
        }

        /// <summary>
        /// The same sentence on the Shopping List row for the gated
        /// purchase, which is what a reader is looking at when they wonder
        /// why they cannot buy it.
        /// </summary>
        private static string ShoppingRowHint(CraftingPlanResult result)
        {
            var vm = new PlanViewModelBuilder().Build(result);
            var section = vm.Sections.Single(s => s.SectionType == PlanSectionType.ShoppingList);
            return section.Rows.Single(r => r.ItemId == GatedItemId).HintText;
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
            Assert.Equal(VendorRequirementKind.Achievement, notice.Kind);

            Assert.Equal(
                "Exalted Helm: this vendor requires the Supply Line Management achievement. "
                + "Your account does not have it.",
                NoticeLabel(result));

            // The row already names the item, and the source badge's own
            // hover reads "Buy from a vendor - " in front of this.
            Assert.Equal(
                "Requires the Supply Line Management achievement. "
                + "Your account does not have it.",
                ShoppingRowHint(result));
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
                r => r.Label != null && r.Label.Contains("this vendor requires"));
            Assert.Null(ShoppingRowHint(result));
        }

        [Fact]
        public async Task RequirementWithNoAccountDataAtAll_ReadsAsNotCheckedNotAsUnmet()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()), progressionClient: null);

            var notice = Assert.Single(result.VendorRequirementNotices);
            Assert.Equal(VendorRequirementStatus.Unknown, notice.Status);
            Assert.Equal(
                VendorRequirementUnknownReason.AccountDataUnavailable, notice.UnknownReason);
            Assert.Equal(AccountProgressionAccess.FetchFailed, result.AccountProgressionAccess);
            Assert.Equal(
                "Exalted Helm: this vendor requires the Supply Line Management achievement. "
                + "The check failed. Generate the plan again.",
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
            Assert.Equal(AccountProgressionAccess.FetchFailed, result.AccountProgressionAccess);
        }

        /// <summary>
        /// The reported case. Blish HUD builds a module's subtoken from
        /// its saved consent list, not from manifest.json, so an optional
        /// permission the manifest declares can be missing from a module
        /// the player enabled before it was declared. The player can fix
        /// that, so the words say how.
        /// </summary>
        [Fact]
        public async Task RequirementTheModuleWasNeverGrantedPermissionToCheck_SaysHowToGrantIt()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithoutProgressionScope(),
                    AccountProgressionAccess.NotConsented));

            var notice = Assert.Single(result.VendorRequirementNotices);
            Assert.Equal(VendorRequirementStatus.Unknown, notice.Status);
            Assert.Equal(
                VendorRequirementUnknownReason.AccountDataUnavailable, notice.UnknownReason);
            Assert.Equal(AccountProgressionAccess.NotConsented, result.AccountProgressionAccess);
            Assert.Equal(
                "Exalted Helm: this vendor requires the Supply Line Management achievement. "
                + "To check it, disable this module in Blish HUD, tick its progression "
                + "permission, then enable it again.",
                NoticeLabel(result));
        }

        [Fact]
        public async Task RequirementTheKeyItselfCannotCheck_SaysToMakeANewKey()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithoutProgressionScope(),
                    AccountProgressionAccess.KeyMissingScope));

            Assert.Equal(AccountProgressionAccess.KeyMissingScope, result.AccountProgressionAccess);
            Assert.Equal(
                "Exalted Helm: this vendor requires the Supply Line Management achievement. "
                + "Your Guild Wars 2 API key does not grant progression. "
                + "Make a new key with that permission.",
                NoticeLabel(result));
        }

        [Fact]
        public async Task RequirementCheckedBeforeTheKeyArrived_SaysToGenerateAgain()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithoutProgressionScope(),
                    AccountProgressionAccess.SubtokenNotReady));

            Assert.Equal(
                "Exalted Helm: this vendor requires the Supply Line Management achievement. "
                + "Your API key had not reached the module yet. Generate the plan again.",
                NoticeLabel(result));
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
            Assert.Equal(VendorRequirementKind.Unclassified, notice.Kind);
            Assert.Equal(
                VendorRequirementUnknownReason.RequirementNotUnderstood, notice.UnknownReason);

            // No kind word in front of it, because the module has not
            // established one, and no advice, because there is nothing the
            // player can do about it.
            Assert.Equal(
                "Exalted Helm: this vendor requires the respective item not already "
                + "unlocked in the wardrobe. This module cannot check that one.",
                NoticeLabel(result));
        }

        /// <summary>
        /// A player who reads a bare name cannot tell an achievement from
        /// an item, so every kind the updater recognizes names itself.
        /// </summary>
        [Fact]
        public async Task AMasteryRequirement_NamesItselfAsAMastery()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(new VendorRequirement
                {
                    Text = "Return to Gyala Delve",
                    MasteryId = 8,
                    MasteryLevel = 2,
                }),
                new InMemoryAccountProgressionClient(new AccountProgression
                {
                    MasteryLevelsByMasteryId = new Dictionary<int, int>(),
                }));

            Assert.Equal(
                "Exalted Helm: this vendor requires the Return to Gyala Delve mastery. "
                + "Your account does not have it.",
                NoticeLabel(result));
        }

        [Fact]
        public async Task AnExpansionRequirement_NamesItselfAsAnExpansion()
        {
            var result = await GeneratePlanAsync(
                GatedOffer(new VendorRequirement
                {
                    Text = "Heart of Thorns",
                    Expansion = "HeartOfThorns",
                }),
                new InMemoryAccountProgressionClient(new AccountProgression
                {
                    ExpansionAccess = new HashSet<string> { "GuildWars2" },
                }));

            Assert.Equal(
                "Exalted Helm: this vendor requires the Heart of Thorns expansion. "
                + "Your account does not have it.",
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

        /// <summary>
        /// The reported plan's module log said nothing about progression
        /// at all, so the player was told a check did not happen with no
        /// way to find out why. Every outcome now writes a line.
        /// </summary>
        [Fact]
        public async Task AProgressionReadThatCouldNotHappen_SaysWhyInTheModuleLog()
        {
            var log = new ModuleLog();

            await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithoutProgressionScope(),
                    AccountProgressionAccess.NotConsented),
                log);

            var entry = Assert.Single(
                log.Snapshot().Where(e => e.Message.Contains("Account progression")));
            Assert.Equal(ModuleLogLevel.Warn, entry.Level);
            Assert.Contains(
                "the progression permission is not enabled for this module in Blish HUD",
                entry.Message);
        }

        [Fact]
        public async Task AProgressionReadThatHappened_IsAlsoLogged()
        {
            var log = new ModuleLog();

            await GeneratePlanAsync(
                GatedOffer(AchievementRequirement()),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithAchievements(AchievementId)),
                log);

            var entry = Assert.Single(
                log.Snapshot().Where(e => e.Message.Contains("Account progression")));
            Assert.Equal(ModuleLogLevel.Info, entry.Level);
        }

        [Fact]
        public async Task AnUngatedPlan_WritesNoProgressionLineAtAll()
        {
            var log = new ModuleLog();

            await GeneratePlanAsync(
                GatedOffer(requirement: null),
                new InMemoryAccountProgressionClient(
                    InMemoryAccountProgressionClient.WithAchievements()),
                log);

            Assert.DoesNotContain(
                log.Snapshot(), e => e.Message.Contains("Account progression"));
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
