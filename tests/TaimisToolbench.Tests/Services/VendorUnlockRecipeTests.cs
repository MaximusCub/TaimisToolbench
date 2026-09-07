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
    /// A vendor offer can be gated behind a recipe sheet the account must
    /// own before that vendor will trade at all - Lyhr's Obsidian armour
    /// exchange opens only once "Recipe: Legendary Obsidian Armor" has been
    /// consumed. These run the real pipeline end to end and check that the
    /// sheet reaches Required Recipes, that its owned/missing state comes
    /// from the same /v2/account/recipes set a craft recipe's does, and
    /// that nothing about the route or its cost changed.
    /// <para>See docs/KNOWN-ISSUES.md entry 44.</para>
    /// </summary>
    public class VendorUnlockRecipeTests
    {
        private const int GatedItemId = 1;
        private const int SheetItemId = 900;
        private const int SheetRecipeId = 14083;
        private const int TokenCurrencyId = 23;

        private static async Task<CraftingPlanResult> GeneratePlanAsync(
            VendorOffer offer,
            InMemoryAccountRecipeClient accountClient)
        {
            var builder = PipelineBuilder.Create()
                .WithItem(GatedItemId, "Obsidian Light Crown", "crown.png")
                .WithItem(SheetItemId, "Recipe: Legendary Obsidian Armor", "sheet.png");

            if (accountClient != null)
            {
                builder = builder.WithAccountRecipeClient(accountClient);
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

        private static VendorOffer GatedOffer(int? unlockItemId, int? unlockRecipeId)
        {
            return new VendorOffer
            {
                OfferId = "test-unlock-gate",
                OutputItemId = GatedItemId,
                OutputCount = 1,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = TokenCurrencyId, Count = 3 },
                },
                MerchantName = "Lyhr",
                Locations = new List<string> { "Outer Ring" },
                UnlockRecipeItemId = unlockItemId,
                UnlockRecipeId = unlockRecipeId,
            };
        }

        [Fact]
        public async Task GatedOffer_SheetNotLearned_ShowsAsAMissingRequiredRecipe()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetLearnedRecipes();

            var result = await GeneratePlanAsync(
                GatedOffer(SheetItemId, SheetRecipeId), accountClient);

            var required = Assert.Single(result.RequiredRecipes);
            Assert.Equal(SheetRecipeId, required.RecipeId);
            // The row names the SHEET, so the label is what the player has
            // to go and buy, and its metadata resolves (not "Unknown Item")
            // only because the pipeline widened the fetch to unlock items.
            Assert.Equal(SheetItemId, required.OutputItemId);
            Assert.Equal(
                "Recipe: Legendary Obsidian Armor",
                result.ItemMetadata[required.OutputItemId].Name);
            Assert.True(required.IsMissing);
            Assert.False(required.IsAutoLearned);
        }

        [Fact]
        public async Task GatedOffer_SheetNotLearned_RendersAsAMissingRowInTheSection()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetLearnedRecipes();

            var result = await GeneratePlanAsync(
                GatedOffer(SheetItemId, SheetRecipeId), accountClient);
            var vm = new PlanViewModelBuilder().Build(result);

            var section = vm.Sections.Single(
                s => s.SectionType == PlanSectionType.RequiredRecipes);
            var row = Assert.Single(section.Rows);
            Assert.Equal("Recipe: Legendary Obsidian Armor", row.Label);
            Assert.Equal("Missing!", row.StatusTag);
            // The sheet's own wiki page, not a second "Recipe: " prefixed
            // onto a name that already carries one.
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Recipe%3A_Legendary_Obsidian_Armor#Acquisition",
                row.WikiUrl);
            Assert.Equal("Required Recipes (1)", section.Title);
        }

        [Fact]
        public async Task GatedOffer_SheetLearned_ShowsAsOwned()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetLearnedRecipes(SheetRecipeId);

            var result = await GeneratePlanAsync(
                GatedOffer(SheetItemId, SheetRecipeId), accountClient);

            var required = Assert.Single(result.RequiredRecipes);
            Assert.False(required.IsMissing);
        }

        [Fact]
        public async Task GatedOffer_NoRecipePermission_LeavesTheStatusUnknown()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetHasPermission(false);

            var result = await GeneratePlanAsync(
                GatedOffer(SheetItemId, SheetRecipeId), accountClient);

            var required = Assert.Single(result.RequiredRecipes);
            // Never fabricated into "you have it" - the same rule a craft
            // recipe follows when the account endpoint is unavailable.
            Assert.Null(required.IsMissing);
        }

        [Fact]
        public async Task UngatedOffer_AddsNoRequiredRecipe()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetLearnedRecipes();

            var result = await GeneratePlanAsync(GatedOffer(null, null), accountClient);

            Assert.Empty(result.RequiredRecipes);
        }

        [Fact]
        public async Task GatedOffer_StaysSelectableAndCostsExactlyWhatItDidUngated()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetLearnedRecipes();

            var gated = await GeneratePlanAsync(
                GatedOffer(SheetItemId, SheetRecipeId), accountClient);
            var ungated = await GeneratePlanAsync(GatedOffer(null, null), accountClient);

            // The route is named, never hidden or repriced: an unlock the
            // account does not have must change nothing about the decision.
            var step = Assert.Single(gated.Plan.Steps);
            Assert.Equal(AcquisitionSource.BuyFromVendor, step.Source);
            Assert.Equal(ungated.Plan.TotalCoinCost, gated.Plan.TotalCoinCost);
            Assert.Equal(
                ungated.Plan.CurrencyCosts.Single(c => c.CurrencyId == TokenCurrencyId).Amount,
                gated.Plan.CurrencyCosts.Single(c => c.CurrencyId == TokenCurrencyId).Amount);
            Assert.Equal(SheetRecipeId, step.VendorUnlockRecipeId);
            Assert.Equal(SheetItemId, step.VendorUnlockRecipeItemId);
        }

        [Fact]
        public async Task TwoGatedOffersSharingOneSheet_ListTheSheetOnce()
        {
            var accountClient = new InMemoryAccountRecipeClient();
            accountClient.SetLearnedRecipes();

            var builder = PipelineBuilder.Create()
                .WithItem(GatedItemId, "Obsidian Light Crown", "crown.png")
                .WithItem(2, "Obsidian Light Gloves", "gloves.png")
                .WithItem(SheetItemId, "Recipe: Legendary Obsidian Armor", "sheet.png")
                .WithSearchResult(3, 10)
                .WithRecipe(new RawRecipe
                {
                    Id = 10,
                    OutputItemId = 3,
                    OutputItemCount = 1,
                    Disciplines = new List<string> { "Armorsmith" },
                    Ingredients = new List<RawIngredient>
                    {
                        new RawIngredient { Type = "Item", Id = GatedItemId, Count = 1 },
                        new RawIngredient { Type = "Item", Id = 2, Count = 1 },
                    },
                })
                .WithItem(3, "Obsidian Set", "set.png")
                .WithAccountRecipeClient(accountClient);

            CraftingPlanResult result;
            using (var tmp = new TempDirectory())
            {
                var store = new VendorOfferStore(tmp.Path, new VendorOfferLoader());
                store.LoadBaseline(null);
                var second = GatedOffer(SheetItemId, SheetRecipeId);
                second.OfferId = "test-unlock-gate-2";
                second.OutputItemId = 2;
                store.AddOffersToOverlay(new[]
                {
                    GatedOffer(SheetItemId, SheetRecipeId),
                    second,
                });

                var pipeline = builder.WithVendorOfferStore(store).Build();
                result = await pipeline.GenerateStructuredAsync(
                    3, 1, null, CancellationToken.None, priceBasis: PriceBasis.InstantBuy);
            }

            // Two vendor steps, one sheet - and the craft recipe alongside
            // it, so the dedup is not just "the list has one entry".
            Assert.Equal(
                2,
                result.Plan.Steps.Count(s => s.Source == AcquisitionSource.BuyFromVendor));
            Assert.Single(result.RequiredRecipes, r => r.RecipeId == SheetRecipeId);
            Assert.Contains(result.RequiredRecipes, r => r.RecipeId == 10);
        }
    }
}
