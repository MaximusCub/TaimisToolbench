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
            Assert.Equal("Recipe: Gift of Light", row.NoteSubject);
            Assert.Equal("Missing Recipe. Buy from Aveline and 1 other merchant", row.Label);
        }

        /// <summary>
        /// The name and the note are two runs on one row, and the row
        /// reads as one sentence. Without the separator the subject runs
        /// straight into the note's own first word.
        /// </summary>
        [Fact]
        public void TheNameAndTheNote_ReadAsOneSentence()
        {
            var section = NotesFor(CoinOffer("a", "Aveline", 100));

            var row = Assert.Single(section.Rows);

            Assert.Equal(
                "Recipe: Gift of Light: Missing Recipe. Buy from Aveline",
                NotesSectionLayoutMath.SubjectRun(row.NoteSubject)
                    + PlanNoteSegment.Join(row.NoteSegments));
        }

        /// <summary>
        /// The note says where to buy and nothing about the price. The
        /// price is the Required Recipes row's own Cost cell, where the
        /// coin half rides CoinValue and the barter half SheetBarterItems,
        /// so every part of it draws as a number with its own icon.
        /// </summary>
        [Fact]
        public void MixedCoinAndBarterOffer_PricesTheRowAndNotTheNote()
        {
            var vm = BuildFor(new VendorOffer
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

            var row = Assert.Single(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.Notes).Rows);
            Assert.Equal("Missing Recipe. Buy from Miyani", row.Label);
            Assert.Equal(0, row.CoinValue);

            var recipeRow = Assert.Single(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.RequiredRecipes).Rows);
            Assert.Equal(400, recipeRow.CoinValue);
            Assert.Equal("Miyani", recipeRow.SoldByText);
            var charm = Assert.Single(recipeRow.SheetBarterItems);
            Assert.Equal(CharmItemId, charm.ItemId);
            Assert.Equal(5, charm.Amount);
        }

        [Fact]
        public void NonCoinCurrencyOffer_PricesTheRowInThatCurrency()
        {
            var vm = BuildFor(new VendorOffer
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

            var row = Assert.Single(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.Notes).Rows);
            Assert.Equal("Missing Recipe. Buy from Miyani", row.Label);

            var recipeRow = Assert.Single(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.RequiredRecipes).Rows);
            Assert.Equal(0, recipeRow.CoinValue);
            var karma = Assert.Single(recipeRow.CurrencyCosts);
            Assert.Equal(KarmaCurrencyId, karma.CurrencyId);
            Assert.Equal(350, karma.Amount);
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
        /// An item the module has no name and no picture for cannot be
        /// drawn as part of a price, so the row carries none - half a price
        /// is a wrong price. The note still says where to buy, which does
        /// not depend on that item at all.
        /// </summary>
        [Fact]
        public void BarterItemWithNoMetadata_DropsThePriceAndKeepsTheNote()
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

            var vm = new PlanViewModelBuilder().Build(result);
            var note = Assert.Single(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.Notes).Rows);
            Assert.Equal("Missing Recipe. Buy from Miyani", note.Label);

            var recipeRow = Assert.Single(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.RequiredRecipes).Rows);
            Assert.Equal(0, recipeRow.CoinValue);
            Assert.Null(recipeRow.SheetBarterItems);
            Assert.Null(recipeRow.SoldByText);
        }

        /// <summary>
        /// A plan saved by a build whose calculator wrote the row
        /// differently could restore with neither half of a price. The
        /// note is about where to buy, so it still draws; the row's Cost
        /// cell is what goes empty.
        /// </summary>
        [Fact]
        public void RestoredSourceCarryingNoPriceAtAll_StillNamesTheMerchant()
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

            var vm = new PlanViewModelBuilder().Build(result);
            var note = Assert.Single(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.Notes).Rows);
            Assert.Equal("Missing Recipe. Buy from Miyani", note.Label);

            var recipeRow = Assert.Single(
                vm.Sections.Single(s => s.SectionType == PlanSectionType.RequiredRecipes).Rows);
            Assert.Equal(0, recipeRow.CoinValue);
            Assert.Null(recipeRow.SoldByText);
        }

        private static PlanViewModel BuildFor(params VendorOffer[] offers)
        {
            var result = MakeResult(
                metadata: new Dictionary<int, ItemMetadata>
                {
                    { SheetItemId, new ItemMetadata { Name = "Recipe: Gift of Light" } },
                    { CharmItemId, new ItemMetadata { Name = "Charm of Skill" } },
                },
                requiredRecipes: RequiredRecipes(missing: true));

            MissingRecipeSheetSourceCalculator.Apply(result, OffersReturning(offers));

            return new PlanViewModelBuilder().Build(result);
        }

        private static PlanSectionViewModel NotesFor(params VendorOffer[] offers)
        {
            return BuildFor(offers).Sections.Single(s => s.SectionType == PlanSectionType.Notes);
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
