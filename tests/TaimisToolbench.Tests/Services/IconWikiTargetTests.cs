using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The page an icon opens and the sentence that says so, which one
    /// value carries so the affordance and the link cannot disagree.
    /// </summary>
    public class IconWikiTargetTests
    {
        [Fact]
        public void ItemPage_OpensTheSubjectsOwnPage()
        {
            var target = IconWikiTarget.ItemPage("Bolt of Damask");

            Assert.True(target.HasPage);
            Assert.Equal(IconWikiTarget.HintText, target.Hint);
            Assert.Equal("https://wiki.guildwars2.com/wiki/Bolt_of_Damask", target.BuildUrl());
        }

        [Fact]
        public void Acquisition_OpensTheAcquisitionSectionAndSaysSo()
        {
            var target = IconWikiTarget.Acquisition("Bolt of Damask");

            Assert.Equal(IconWikiTarget.AcquisitionHintText, target.Hint);
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Bolt_of_Damask#Acquisition", target.BuildUrl());
        }

        /// <summary>
        /// The sheet page's colon is a literal part of the wiki title, so
        /// it must not be percent-encoded. The crafted item's name is what
        /// the title is built from, never the sheet item's own name, which
        /// already carries the prefix.
        /// </summary>
        [Fact]
        public void RecipeSheet_BuildsTheSheetTitleFromTheCraftedName()
        {
            var target = IconWikiTarget.RecipeSheet("Gift of Light");

            Assert.Equal(IconWikiTarget.HintText, target.Hint);
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Recipe:_Gift_of_Light", target.BuildUrl());
        }

        /// <summary>
        /// The Sold By cell's page: the sheet title, colon intact, with the
        /// merchant list's own anchor on the end.
        /// </summary>
        [Fact]
        public void SheetPageAcquisition_AnchorsTheSheetPageAtItsMerchantList()
        {
            var target = IconWikiTarget.SheetPageAcquisition("Recipe: Gift of Light");

            Assert.Equal(IconWikiTarget.AcquisitionHintText, target.Hint);
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Recipe:_Gift_of_Light#Acquisition",
                target.BuildUrl());
        }

        /// <summary>
        /// A sheet whose name carries no namespace prefix is just an item,
        /// and its acquisition page is the plain one.
        /// </summary>
        [Fact]
        public void SheetPageAcquisition_WithNoPrefix_FallsBackToThePlainItemPage()
        {
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Bolt_of_Damask#Acquisition",
                IconWikiTarget.SheetPageAcquisition("Bolt of Damask").BuildUrl());
        }

        [Theory]
        [InlineData("Unknown Item")]
        [InlineData("Guild upgrade (unresolved)")]
        [InlineData("Unrecognized ingredient type")]
        [InlineData("Currency")]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        public void APlaceholderNameAdvertisesNothingAndOpensNothing(string name)
        {
            foreach (var target in new[]
            {
                IconWikiTarget.ItemPage(name),
                IconWikiTarget.Acquisition(name),
                IconWikiTarget.RecipeSheet(name),
                IconWikiTarget.SheetPageAcquisition(name),
            })
            {
                Assert.False(target.HasPage);
                Assert.Null(target.Hint);
                Assert.Null(target.BuildUrl());
            }
        }

        [Theory]
        [InlineData(nameof(IconWikiSilence.DrawnInsideATooltip))]
        [InlineData(nameof(IconWikiSilence.SitsInASearchDropdown))]
        public void ANamedSilenceAdvertisesNothingAndOpensNothing(string why)
        {
            var target = IconWikiTarget.None(EnumArg.Parse<IconWikiSilence>(why));

            Assert.False(target.HasPage);
            Assert.Null(target.Hint);
            Assert.Null(target.BuildUrl());
        }

        /// <summary>
        /// A caller cannot omit the argument - the compiler stops that -
        /// but it could slide a `default` into the slot. That reads as no
        /// page, which is the safe answer; the call-site audit in
        /// IconStandardCallSiteTests is what stops it being written.
        /// </summary>
        [Fact]
        public void TheDefaultValueOpensNothing()
        {
            Assert.False(default(IconWikiTarget).HasPage);
            Assert.Null(default(IconWikiTarget).Hint);
            Assert.Null(default(IconWikiTarget).BuildUrl());
        }

        [Fact]
        public void ExternalPage_OpensTheUrlItWasGivenAndNamesTheGesture()
        {
            var target = IconWikiTarget.ExternalPage("https://gw2efficiency.com");

            Assert.True(target.HasPage);
            Assert.Equal(IconWikiTarget.ExternalHintText, target.Hint);
            Assert.Equal("https://gw2efficiency.com", target.BuildUrl());
        }

        /// <summary>
        /// The launcher opens https and nothing else, and an external
        /// target answers with the same rule rather than a second copy of
        /// it. A target that fails it draws as plain text.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("http://gw2efficiency.com")]
        [InlineData("file:///C:/Windows/System32/cmd.exe")]
        [InlineData("C:\\Windows\\System32\\cmd.exe")]
        public void ExternalPage_OpensNothingItCannotLaunch(string url)
        {
            var target = IconWikiTarget.ExternalPage(url);

            Assert.False(target.HasPage);
            Assert.Null(target.Hint);
            Assert.Null(target.BuildUrl());
        }

        [Fact]
        public void AnApostropheIsEncodedTheWayTheWikiExpects()
        {
            Assert.Equal(
                "https://wiki.guildwars2.com/wiki/Zojja%27s_Claymore",
                IconWikiTarget.ItemPage("Zojja's Claymore").BuildUrl());
        }
    }
}
