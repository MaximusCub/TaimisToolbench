using System.Collections.Generic;
using VendorOfferUpdater;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    /// <summary>
    /// The classifier decides which vendor gates the module can check. Its
    /// two failure modes are opposite and both bad: classifying a value it
    /// does not really recognize lets the module claim a player lacks
    /// something, and dropping a value it does not recognize hides a real
    /// requirement. These pin both.
    /// </summary>
    public class VendorRequirementClassifierTests
    {
        private const int SupplyLineManagementId = 1912;
        private const int FollowsAdviceAchievementId = 4242;
        private const int NuhochLoreMasteryId = 8;

        private static VendorRequirementNames Names()
        {
            return VendorRequirementNames.Build(
                new[]
                {
                    new KeyValuePair<int, string>(SupplyLineManagementId, "Supply Line Management"),
                    new KeyValuePair<int, string>(FollowsAdviceAchievementId, "Follows Advice"),
                    new KeyValuePair<int, string>(4362, "The Convergence of Sorrow II: Requiem"),
                    // Two achievements sharing a name: 193 of the live
                    // list's 8,230 do, and neither may win.
                    new KeyValuePair<int, string>(11, "Risk Taker"),
                    new KeyValuePair<int, string>(12, "Risk Taker"),
                },
                new[]
                {
                    new MasteryTrack(
                        NuhochLoreMasteryId,
                        new[] { "Nuhoch Hunting", "Nuhoch Language", "Follows Advice" }),
                });
        }

        [Fact]
        public void NoRequirement_ProducesNothing()
        {
            Assert.Null(VendorRequirementClassifier.Classify(null, Names()));
            Assert.Null(VendorRequirementClassifier.Classify("   ", Names()));
        }

        [Fact]
        public void ProseItCannotClassify_StillCarriesTheTextForThePlayer()
        {
            var requirement = VendorRequirementClassifier.Classify(
                "the respective item not already unlocked in the wardrobe", Names());

            Assert.NotNull(requirement);
            Assert.Equal(
                "the respective item not already unlocked in the wardrobe", requirement.Text);
            Assert.Null(requirement.AchievementId);
            Assert.Null(requirement.MasteryId);
            Assert.Null(requirement.Expansion);
        }

        [Fact]
        public void AnAchievementName_ResolvesToThatAchievement()
        {
            var requirement = VendorRequirementClassifier.Classify(
                "Supply Line Management", Names());

            Assert.Equal("Supply Line Management", requirement.Text);
            Assert.Equal(SupplyLineManagementId, requirement.AchievementId);
            Assert.Null(requirement.MasteryId);
        }

        [Fact]
        public void AMasteryLevelName_ResolvesToThatTrackAndLevel()
        {
            var requirement = VendorRequirementClassifier.Classify("Nuhoch Language", Names());

            Assert.Equal("Nuhoch Language", requirement.Text);
            Assert.Equal(NuhochLoreMasteryId, requirement.MasteryId);
            Assert.Equal(1, requirement.MasteryLevel);
            Assert.Null(requirement.AchievementId);
        }

        [Fact]
        public void AnExpansionTitle_ResolvesToItsAccountAccessFlag()
        {
            var requirement = VendorRequirementClassifier.Classify(
                "[[Guild Wars 2: Heart of Thorns|Heart of Thorns]]", Names());

            // The link's DISPLAY text is what a player reads; the flag comes
            // from its target.
            Assert.Equal("Heart of Thorns", requirement.Text);
            Assert.Equal("HeartOfThorns", requirement.Expansion);
        }

        [Fact]
        public void AnExpansionWithNoDocumentedAccessFlag_StaysTextOnly()
        {
            var requirement = VendorRequirementClassifier.Classify(
                "[[Guild Wars 2: Visions of Eternity]]", Names());

            Assert.Equal("Guild Wars 2: Visions of Eternity", requirement.Text);
            Assert.Null(requirement.Expansion);
        }

        [Fact]
        public void AnExpansionTitleWithProseAfterIt_StaysTextOnly()
        {
            var requirement = VendorRequirementClassifier.Classify(
                "[[Guild Wars 2: Heart of Thorns|Heart of Thorns]] and "
                + "[[World Experience#Provisions Master|Provisions Master]] 6th tier",
                Names());

            Assert.Null(requirement.Expansion);
            Assert.Null(requirement.AchievementId);
            Assert.Equal(
                "Heart of Thorns and Provisions Master 6th tier", requirement.Text);
        }

        [Fact]
        public void AWikiAchievementAnchor_ResolvesToThatAchievementAndItsName()
        {
            var requirement = VendorRequirementClassifier.Classify(
                "A Star to Guide Us (achievements)#achievement4362", Names());

            Assert.Equal(4362, requirement.AchievementId);
            Assert.Equal("The Convergence of Sorrow II: Requiem", requirement.Text);
        }

        [Fact]
        public void AnAnchorInsideProse_IsNotRead()
        {
            const string prose =
                "completion of [[This Far, No Further]] and "
                + "[[Hero (achievements)#achievement4362|Some Must Fight]]";

            var requirement = VendorRequirementClassifier.Classify(prose, Names());

            Assert.Null(requirement.AchievementId);
            Assert.Equal(
                "completion of This Far, No Further and Some Must Fight", requirement.Text);
        }

        [Fact]
        public void AnAnchorNamingAnUnknownAchievement_StaysTextOnly()
        {
            var requirement = VendorRequirementClassifier.Classify(
                "Somewhere (achievements)#achievement999999", Names());

            Assert.Null(requirement.AchievementId);
            Assert.Equal("Somewhere (achievements)#achievement999999", requirement.Text);
        }

        [Fact]
        public void ANameThatIsBothAnAchievementAndAMastery_StaysTextOnly()
        {
            var requirement = VendorRequirementClassifier.Classify("Follows Advice", Names());

            Assert.Equal("Follows Advice", requirement.Text);
            Assert.Null(requirement.AchievementId);
            Assert.Null(requirement.MasteryId);
            Assert.True(VendorRequirementClassifier.IsAmbiguous("Follows Advice", Names()));
        }

        [Fact]
        public void ANameTwoAchievementsShare_StaysTextOnly()
        {
            var requirement = VendorRequirementClassifier.Classify("Risk Taker", Names());

            Assert.Null(requirement.AchievementId);
            // Not ambiguous in the cross-KIND sense: it never entered the
            // name index at all, so nothing reports it.
            Assert.False(VendorRequirementClassifier.IsAmbiguous("Risk Taker", Names()));
        }

        [Fact]
        public void WithNoApiNames_EveryRequirementIsStillCarriedAsText()
        {
            var requirement = VendorRequirementClassifier.Classify(
                "Supply Line Management", VendorRequirementNames.Empty);

            Assert.Equal("Supply Line Management", requirement.Text);
            Assert.Null(requirement.AchievementId);
        }
    }
}
