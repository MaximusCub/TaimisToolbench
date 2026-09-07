using VendorOfferUpdater;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    public class VendorUnlockRequirementParserTests
    {
        [Fact]
        public void PlainRecipeTitle_IsTheSheetName()
        {
            Assert.Equal(
                "Recipe: Legendary Obsidian Armor",
                VendorUnlockRequirementParser.ExtractRecipeSheetName(
                    "Recipe: Legendary Obsidian Armor"));
        }

        [Fact]
        public void SurroundingWhitespace_IsTrimmed()
        {
            Assert.Equal(
                "Recipe: Legendary Obsidian Armor",
                VendorUnlockRequirementParser.ExtractRecipeSheetName(
                    "  Recipe: Legendary Obsidian Armor \n"));
        }

        [Fact]
        public void SingleWikiLink_IsUnwrapped()
        {
            Assert.Equal(
                "Recipe: Bowl of Fish Stew",
                VendorUnlockRequirementParser.ExtractRecipeSheetName(
                    "[[Recipe: Bowl of Fish Stew]]"));
        }

        [Fact]
        public void PipedWikiLink_KeepsTheTitleNotTheDisplayText()
        {
            Assert.Equal(
                "Recipe: Bowl of Fish Stew",
                VendorUnlockRequirementParser.ExtractRecipeSheetName(
                    "[[Recipe: Bowl of Fish Stew|the fish stew recipe]]"));
        }

        // Every one of these is a real "Has requirement" value from the
        // wiki scrape, covering the gate kinds this parser must not claim:
        // masteries, achievements, expansions, festivals, wardrobe skins,
        // renown hearts, and the prose that merely mentions recipes.
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Nuhoch Language")]
        [InlineData("Obsidian Armor Crafting")]
        [InlineData("Supply Line Management")]
        [InlineData("the recipe not already unlocked")]
        [InlineData("the respective item not already unlocked in the wardrobe")]
        [InlineData("the festival [[Wintersday]]")]
        [InlineData("[[Guild Wars 2: Janthir Wilds]]")]
        [InlineData("Obsidian Staff (skin)")]
        [InlineData("one [[Homestead Upgrade: Fiber Trade Efficiency]]")]
        [InlineData("exchange item in player's inventory")]
        public void NonSheetRequirement_IsNotClaimed(string requirement)
        {
            Assert.Null(VendorUnlockRequirementParser.ExtractRecipeSheetName(requirement));
        }

        // Prose that happens to contain a Recipe: link is not a bare
        // requirement to own that sheet, so no fragment of it is accepted.
        [Theory]
        [InlineData("[[Recipe: Bowl of Fish Stew]] and [[Guild Wars 2: Janthir Wilds]]")]
        [InlineData("owning [[Recipe: Bowl of Fish Stew]]")]
        [InlineData("Recipe:")]
        [InlineData("[[Recipe:]]")]
        public void RequirementWithSurroundingProse_IsNotClaimed(string requirement)
        {
            Assert.Null(VendorUnlockRequirementParser.ExtractRecipeSheetName(requirement));
        }
    }
}
