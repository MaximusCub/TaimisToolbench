using System;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Answers whether the account satisfies one vendor requirement.
    /// <para>
    /// Never guesses. A requirement the updater could not classify, and a
    /// kind whose account data was not read, both come back Unknown. Only a
    /// requirement that names something the account data covers can come
    /// back NotMet.
    /// </para>
    /// </summary>
    internal static class VendorRequirementEvaluator
    {
        /// <summary>
        /// /v2/account flags Path of Fire but NOT Heart of Thorns on an
        /// account that got Heart of Thorns by buying Path of Fire, so
        /// testing for the Heart of Thorns flag alone reports a player who
        /// owns the content as not owning it.
        /// </summary>
        private const string HeartOfThornsAccess = "HeartOfThorns";

        private const string PathOfFireAccess = "PathOfFire";

        public static VendorRequirementStatus Evaluate(
            VendorRequirement requirement, AccountProgression progression,
            out VendorRequirementUnknownReason reason)
        {
            var kind = KindOf(requirement);
            if (kind == VendorRequirementKind.Unclassified)
            {
                reason = VendorRequirementUnknownReason.RequirementNotUnderstood;
                return VendorRequirementStatus.Unknown;
            }

            if (progression == null)
            {
                reason = VendorRequirementUnknownReason.AccountDataUnavailable;
                return VendorRequirementStatus.Unknown;
            }

            if (kind == VendorRequirementKind.Achievement)
            {
                return Answer(
                    progression.CompletedAchievementIds != null,
                    progression.CompletedAchievementIds != null &&
                        progression.CompletedAchievementIds.Contains(
                            requirement.AchievementId.Value),
                    out reason);
            }

            if (kind == VendorRequirementKind.Mastery)
            {
                var levels = progression.MasteryLevelsByMasteryId;
                return Answer(
                    levels != null,
                    levels != null &&
                        levels.TryGetValue(requirement.MasteryId.Value, out int level) &&
                        level >= requirement.MasteryLevel.Value,
                    out reason);
            }

            var access = progression.ExpansionAccess;
            return Answer(
                access != null, access != null && HasExpansion(access, requirement.Expansion),
                out reason);
        }

        /// <summary>
        /// What the updater recognized this requirement as. Unclassified
        /// covers both a null requirement and prose no GW2 API name
        /// matched, which are the same thing to every caller: nothing to
        /// check and nothing to name.
        /// </summary>
        public static VendorRequirementKind KindOf(VendorRequirement requirement)
        {
            if (requirement == null)
            {
                return VendorRequirementKind.Unclassified;
            }

            if (requirement.AchievementId.HasValue)
            {
                return VendorRequirementKind.Achievement;
            }

            if (requirement.MasteryId.HasValue && requirement.MasteryLevel.HasValue)
            {
                return VendorRequirementKind.Mastery;
            }

            return string.IsNullOrEmpty(requirement.Expansion)
                ? VendorRequirementKind.Unclassified
                : VendorRequirementKind.Expansion;
        }

        private static bool HasExpansion(
            System.Collections.Generic.ICollection<string> access, string expansion)
        {
            if (access.Contains(expansion))
            {
                return true;
            }

            return string.Equals(expansion, HeartOfThornsAccess, StringComparison.Ordinal)
                && access.Contains(PathOfFireAccess);
        }

        private static VendorRequirementStatus Answer(
            bool checkable, bool met, out VendorRequirementUnknownReason reason)
        {
            if (!checkable)
            {
                reason = VendorRequirementUnknownReason.AccountDataUnavailable;
                return VendorRequirementStatus.Unknown;
            }

            reason = VendorRequirementUnknownReason.None;
            return met ? VendorRequirementStatus.Met : VendorRequirementStatus.NotMet;
        }
    }
}
