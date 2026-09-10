using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.CraftingPlanResultBuilders;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// MissingRecipeSheetSourceCalculator's offer choice and
    /// PlanViewModelBuilder's wording for it, over offer shapes the
    /// Endless Summer corpus test does not reach. Both production types
    /// run: the calculator picks the offer, the builder writes the row.
    /// </summary>
    public class MissingRecipeSheetSourceCalculatorTests
    {
        private const int RecipeId = 843;
        private const int SheetItemId = 9626;
        private const int CharmItemId = 89216;
        private const int KarmaCurrencyId = 2;

        [Fact]
        public void CheapestCoinOfferWins_AndTheDearerMerchantIsNotCounted()
        {
            var section = NotesFor(
                CoinOffer("a", "Zola", 300),
                CoinOffer("b", "Aveline", 100),
                CoinOffer("c", "Brill", 100));

            var row = Assert.Single(section.Rows);
            Assert.Equal(
                "Missing recipe - buy Recipe: Gift of Light from Aveline and 1 other merchant for",
                row.Label);
            Assert.Equal(100, row.CoinValue);
        }

        /// <summary>
        /// The barter half is named in the label and the coin half rides
        /// CoinValue, so the view still draws coin icons to the right of
        /// the number.
        /// </summary>
        [Fact]
        public void MixedCoinAndBarterOffer_SplitsAcrossLabelAndCoinValue()
        {
            var section = NotesFor(new VendorOffer
            {
                OfferId = "mixed",
                OutputItemId = SheetItemId,
                OutputCount = 1,
                MerchantName = "Miyani",
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Item", Id = CharmItemId, Count = 5 },
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = 400 },
                },
            });

            var row = Assert.Single(section.Rows);
            Assert.Equal(
                "Missing recipe - buy Recipe: Gift of Light from Miyani for 5x Charm of Skill plus",
                row.Label);
            Assert.Equal(400, row.CoinValue);
        }

        [Fact]
        public void NonCoinCurrencyOffer_NamesTheCurrency()
        {
            var section = NotesFor(new VendorOffer
            {
                OfferId = "karma",
                OutputItemId = SheetItemId,
                OutputCount = 1,
                MerchantName = "Miyani",
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = KarmaCurrencyId, Count = 350 },
                },
            });

            var row = Assert.Single(section.Rows);
            Assert.Equal(
                "Missing recipe - buy Recipe: Gift of Light from Miyani for 350 Karma",
                row.Label);
            Assert.Equal(0, row.CoinValue);
        }

        /// <summary>
        /// A festival offer is not the regular market, so it cannot be the
        /// note's answer - the same rule RecipeSheetSavingsCalculator
        /// applies to a seasonal sheet offer.
        /// </summary>
        [Fact]
        public void SeasonalOnlyOffer_EmitsNothing()
        {
            var seasonal = CoinOffer("s", "Miyani", 100);
            seasonal.SeasonalFestival = "halloween";

            Assert.Empty(SourcesFor(RequiredRecipes(missing: true), seasonal));
        }

        [Fact]
        public void RecipeWhoseMissingStateIsUnknown_EmitsNothing()
        {
            // IsMissing is null when the account API gave no recipe
            // permission. The plan cannot claim the player lacks it.
            var unknown = RequiredRecipes(missing: null);

            Assert.Empty(SourcesFor(unknown, CoinOffer("a", "Miyani", 100)));
        }

        [Fact]
        public void RecipeAlreadyNamedByTheSavingsNote_EmitsNothing()
        {
            var result = MakeResult(requiredRecipes: RequiredRecipes(missing: true));
            result.RecipeSheetSavingsOpportunities = new List<RecipeSheetSavingsOpportunity>
            {
                new RecipeSheetSavingsOpportunity
                {
                    ItemId = 19632, RecipeId = RecipeId, SheetItemId = SheetItemId,
                    SheetCost = 100000, SavingsPerUnit = 1,
                },
            };

            MissingRecipeSheetSourceCalculator.Apply(result, OffersReturning(CoinOffer("a", "Miyani", 100)));

            Assert.Empty(result.MissingRecipeSheetSources);
        }

        [Fact]
        public void SheetNoVendorSells_EmitsNothing()
        {
            Assert.Empty(SourcesFor(RequiredRecipes(missing: true)));
        }

        /// <summary>
        /// The barter item's name is the whole cost statement. Printing
        /// PlanViewModelBuilder's "Unknown Item" placeholder instead would
        /// tell the player nothing about what to bring.
        /// </summary>
        [Fact]
        public void BarterItemWithNoMetadata_DropsTheNote()
        {
            var result = MakeResult(
                metadata: new Dictionary<int, ItemMetadata>
                {
                    { SheetItemId, new ItemMetadata { Name = "Recipe: Gift of Light" } },
                },
                requiredRecipes: RequiredRecipes(missing: true));

            MissingRecipeSheetSourceCalculator.Apply(result, OffersReturning(new VendorOffer
            {
                OfferId = "barter",
                OutputItemId = SheetItemId,
                OutputCount = 1,
                MerchantName = "Miyani",
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Item", Id = CharmItemId, Count = 5 },
                },
            }));

            Assert.Single(result.MissingRecipeSheetSources);
            Assert.DoesNotContain(
                new PlanViewModelBuilder().Build(result).Sections,
                s => s.SectionType == PlanSectionType.Notes);
        }

        /// <summary>
        /// A plan saved by a build whose calculator wrote the row
        /// differently could restore with neither half of a price. The
        /// price is the whole point of the note, so the builder drops it
        /// rather than render "sold by Miyani for" and nothing.
        /// </summary>
        [Fact]
        public void RestoredSourceCarryingNoPriceAtAll_DrawsNoRow()
        {
            var result = MakeResult(
                metadata: new Dictionary<int, ItemMetadata>
                {
                    { SheetItemId, new ItemMetadata { Name = "Recipe: Gift of Light" } },
                },
                requiredRecipes: RequiredRecipes(missing: true));
            result.MissingRecipeSheetSources = new List<MissingRecipeSheetSource>
            {
                new MissingRecipeSheetSource
                {
                    RecipeId = RecipeId, SheetItemId = SheetItemId, MerchantName = "Miyani",
                },
            };

            Assert.DoesNotContain(
                new PlanViewModelBuilder().Build(result).Sections,
                s => s.SectionType == PlanSectionType.Notes);
        }

        private static PlanSectionViewModel NotesFor(params VendorOffer[] offers)
        {
            var result = MakeResult(
                metadata: new Dictionary<int, ItemMetadata>
                {
                    { SheetItemId, new ItemMetadata { Name = "Recipe: Gift of Light" } },
                    { CharmItemId, new ItemMetadata { Name = "Charm of Skill" } },
                },
                requiredRecipes: RequiredRecipes(missing: true));

            MissingRecipeSheetSourceCalculator.Apply(result, OffersReturning(offers));

            return new PlanViewModelBuilder().Build(result).Sections
                .Single(s => s.SectionType == PlanSectionType.Notes);
        }

        private static IReadOnlyList<MissingRecipeSheetSource> SourcesFor(
            List<RequiredRecipe> requiredRecipes, params VendorOffer[] offers)
        {
            var result = MakeResult(requiredRecipes: requiredRecipes);
            MissingRecipeSheetSourceCalculator.Apply(result, OffersReturning(offers));
            return result.MissingRecipeSheetSources;
        }

        private static System.Func<int, IReadOnlyList<VendorOffer>> OffersReturning(
            params VendorOffer[] offers)
        {
            return itemId => itemId == SheetItemId
                ? (IReadOnlyList<VendorOffer>)offers
                : new VendorOffer[0];
        }

        private static List<RequiredRecipe> RequiredRecipes(bool? missing)
        {
            return new List<RequiredRecipe>
            {
                new RequiredRecipe
                {
                    RecipeId = RecipeId,
                    OutputItemId = 19632,
                    IsLearnedFromItem = true,
                    IsMissing = missing,
                    SheetItemId = SheetItemId,
                    Disciplines = new List<string> { "Armorsmith" },
                    MinRating = 400,
                },
            };
        }

        private static VendorOffer CoinOffer(string offerId, string merchant, int coin)
        {
            return new VendorOffer
            {
                OfferId = offerId,
                OutputItemId = SheetItemId,
                OutputCount = 1,
                MerchantName = merchant,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = coin },
                },
            };
        }
    }
}
