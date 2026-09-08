using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Three answers, and the one that must never be confused with the
    /// others: NotMet is a claim about the account, and may only be made
    /// where the account data actually covers the question.
    /// </summary>
    public class VendorRequirementEvaluatorTests
    {
        private static VendorRequirement Achievement(int id)
        {
            return new VendorRequirement { Text = "Supply Line Management", AchievementId = id };
        }

        private static VendorRequirement MasteryLevel(int masteryId, int level)
        {
            return new VendorRequirement
            {
                Text = "Nuhoch Language",
                MasteryId = masteryId,
                MasteryLevel = level,
            };
        }

        private static VendorRequirement Expansion(string access)
        {
            return new VendorRequirement { Text = "Heart of Thorns", Expansion = access };
        }

        private static AccountProgression Progression(
            ISet<int> achievements = null,
            IReadOnlyDictionary<int, int> masteries = null,
            ISet<string> access = null)
        {
            return new AccountProgression
            {
                CompletedAchievementIds = achievements,
                MasteryLevelsByMasteryId = masteries,
                ExpansionAccess = access,
            };
        }

        [Fact]
        public void NoAccountData_IsUnknown()
        {
            Assert.Equal(
                VendorRequirementStatus.Unknown,
                VendorRequirementEvaluator.Evaluate(Achievement(1912), null));
        }

        [Fact]
        public void AnUnclassifiedRequirement_IsUnknownEvenWithFullAccountData()
        {
            var textOnly = new VendorRequirement
            {
                Text = "the respective item not already unlocked in the wardrobe",
            };

            Assert.Equal(
                VendorRequirementStatus.Unknown,
                VendorRequirementEvaluator.Evaluate(
                    textOnly,
                    Progression(
                        achievements: new HashSet<int> { 1912 },
                        masteries: new Dictionary<int, int> { [8] = 4 },
                        access: new HashSet<string> { "HeartOfThorns" })));
        }

        [Fact]
        public void AnAchievementTheAccountHasDone_IsMet()
        {
            Assert.Equal(
                VendorRequirementStatus.Met,
                VendorRequirementEvaluator.Evaluate(
                    Achievement(1912), Progression(achievements: new HashSet<int> { 1912 })));
        }

        [Fact]
        public void AnAchievementTheAccountHasNotDone_IsNotMet()
        {
            Assert.Equal(
                VendorRequirementStatus.NotMet,
                VendorRequirementEvaluator.Evaluate(
                    Achievement(1912), Progression(achievements: new HashSet<int>())));
        }

        [Fact]
        public void AnAchievementWithNoAchievementDataRead_IsUnknownNotNotMet()
        {
            // The account fetch failed or the key lacks "progression". A
            // player who HAS the achievement must not be told they do not.
            Assert.Equal(
                VendorRequirementStatus.Unknown,
                VendorRequirementEvaluator.Evaluate(
                    Achievement(1912),
                    Progression(access: new HashSet<string> { "HeartOfThorns" })));
        }

        [Fact]
        public void AMasteryLevelAtOrBelowTheAccountsLevel_IsMet()
        {
            var progression = Progression(masteries: new Dictionary<int, int> { [8] = 2 });

            Assert.Equal(
                VendorRequirementStatus.Met,
                VendorRequirementEvaluator.Evaluate(MasteryLevel(8, 2), progression));
            Assert.Equal(
                VendorRequirementStatus.Met,
                VendorRequirementEvaluator.Evaluate(MasteryLevel(8, 0), progression));
        }

        [Fact]
        public void AMasteryLevelAboveTheAccountsLevel_IsNotMet()
        {
            Assert.Equal(
                VendorRequirementStatus.NotMet,
                VendorRequirementEvaluator.Evaluate(
                    MasteryLevel(8, 3),
                    Progression(masteries: new Dictionary<int, int> { [8] = 2 })));
        }

        [Fact]
        public void AMasteryTrackTheAccountNeverStarted_IsNotMet()
        {
            Assert.Equal(
                VendorRequirementStatus.NotMet,
                VendorRequirementEvaluator.Evaluate(
                    MasteryLevel(8, 0),
                    Progression(masteries: new Dictionary<int, int> { [9] = 4 })));
        }

        [Fact]
        public void AMasteryLevelWithNoMasteryDataRead_IsUnknown()
        {
            Assert.Equal(
                VendorRequirementStatus.Unknown,
                VendorRequirementEvaluator.Evaluate(
                    MasteryLevel(8, 1),
                    Progression(achievements: new HashSet<int> { 1912 })));
        }

        [Fact]
        public void AnExpansionTheAccountOwns_IsMet()
        {
            Assert.Equal(
                VendorRequirementStatus.Met,
                VendorRequirementEvaluator.Evaluate(
                    Expansion("JanthirWilds"),
                    Progression(access: new HashSet<string> { "GuildWars2", "JanthirWilds" })));
        }

        [Fact]
        public void AnExpansionTheAccountDoesNotOwn_IsNotMet()
        {
            Assert.Equal(
                VendorRequirementStatus.NotMet,
                VendorRequirementEvaluator.Evaluate(
                    Expansion("JanthirWilds"),
                    Progression(access: new HashSet<string> { "GuildWars2" })));
        }

        [Fact]
        public void PathOfFireAlone_SatisfiesHeartOfThorns()
        {
            // /v2/account does not set the Heart of Thorns flag on an
            // account that received it by buying Path of Fire.
            Assert.Equal(
                VendorRequirementStatus.Met,
                VendorRequirementEvaluator.Evaluate(
                    Expansion("HeartOfThorns"),
                    Progression(access: new HashSet<string> { "GuildWars2", "PathOfFire" })));
        }

        [Fact]
        public void HeartOfThornsAlone_DoesNotSatisfyPathOfFire()
        {
            Assert.Equal(
                VendorRequirementStatus.NotMet,
                VendorRequirementEvaluator.Evaluate(
                    Expansion("PathOfFire"),
                    Progression(access: new HashSet<string> { "GuildWars2", "HeartOfThorns" })));
        }

        [Fact]
        public void AnExpansionWithNoAccessDataRead_IsUnknown()
        {
            Assert.Equal(
                VendorRequirementStatus.Unknown,
                VendorRequirementEvaluator.Evaluate(
                    Expansion("JanthirWilds"),
                    Progression(achievements: new HashSet<int>())));
        }
    }
}
