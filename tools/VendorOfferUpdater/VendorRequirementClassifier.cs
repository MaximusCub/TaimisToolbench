using System;
using System.Collections.Generic;
using VendorOfferUpdater.Models;

namespace VendorOfferUpdater
{
    /// <summary>
    /// Decides which kind of gate a wiki "Has requirement" value names, so
    /// the module can check the ones the GW2 API answers and still show the
    /// rest as text.
    /// <para>
    /// Nothing in the wiki data marks the kind, and the shapes are
    /// identical: "Radiance of the Sun God" is an achievement and "Nuhoch
    /// Language" is a mastery level. So a value is only classified when the
    /// WHOLE of it, after one enclosing link is unwrapped, is an exact GW2
    /// API name. A value that matches two kinds is left unclassified rather
    /// than resolved to one. Measured over the 70,644-row scrape's 9,337
    /// requirement rows: 1,284 achievement by name, 115 by wiki anchor,
    /// 965 mastery, 400 expansion, 5 ambiguous, 6,568 text only. Full
    /// method: docs/ARCHITECTURE.md, "Vendor requirements".
    /// </para>
    /// </summary>
    public static class VendorRequirementClassifier
    {
        /// <summary>
        /// Expansion page titles the wiki uses, mapped to the flag
        /// /v2/account reports in its "access" array. Only the expansions
        /// that array is documented to carry are listed; a newer expansion
        /// the wiki cites is left unclassified rather than given a guessed
        /// flag. See docs/ARCHITECTURE.md, "Vendor requirements".
        /// </summary>
        private static readonly Dictionary<string, string> ExpansionAccessByTitle =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Guild Wars 2: Heart of Thorns", "HeartOfThorns" },
                { "Guild Wars 2: Path of Fire", "PathOfFire" },
                { "Guild Wars 2: End of Dragons", "EndOfDragons" },
                { "Guild Wars 2: Secrets of the Obscure", "SecretsOfTheObscure" },
                { "Guild Wars 2: Janthir Wilds", "JanthirWilds" },
            };

        private const string AchievementAnchor = "#achievement";

        /// <summary>
        /// Returns the requirement to store on the offer, or null when the
        /// row records no requirement. Text is always populated; the ids
        /// are populated only for a value this recognizes.
        /// </summary>
        public static VendorRequirement? Classify(
            string? requirement, VendorRequirementNames? names)
        {
            string? text = WikiRequirementText.ToDisplayText(requirement);
            if (text == null)
            {
                return null;
            }

            var result = new VendorRequirement { Text = text };

            string? key = WikiRequirementText.LinkTarget(requirement);
            if (key == null || names == null)
            {
                return result;
            }

            // A wiki achievement-table row carries the achievement's own API
            // id in its HTML anchor, so "Page#achievement4362" names one
            // achievement outright. Verified against the live API: all 48
            // distinct anchors in the scrape resolve, and the id names the
            // achievement the page section is about. It is a page fragment,
            // not prose, so it is read only from a value that is one page
            // reference: any wiki-link bracket still present means
            // LinkTarget refused to unwrap prose, and the digits after the
            // last anchor would then be a fragment of a sentence.
            int anchor = key.LastIndexOf(AchievementAnchor, StringComparison.Ordinal);
            if (anchor > 0 &&
                key.IndexOf('[') < 0 &&
                key.IndexOf(']') < 0 &&
                int.TryParse(
                    key.Substring(anchor + AchievementAnchor.Length),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int anchoredId) &&
                names.AchievementNamesById.TryGetValue(anchoredId, out string? anchoredName))
            {
                result.AchievementId = anchoredId;

                // The raw value is a URL fragment, so it names the
                // achievement without reading as its name.
                result.Text = anchoredName;
                return result;
            }

            bool isAchievement = names.AchievementIdsByName.TryGetValue(key, out int achievementId);
            bool isMastery = names.MasteryLevelsByName.TryGetValue(key, out var masteryLevel);
            bool isExpansion = ExpansionAccessByTitle.TryGetValue(key, out string? access);

            int matched = (isAchievement ? 1 : 0) + (isMastery ? 1 : 0) + (isExpansion ? 1 : 0);
            if (matched != 1)
            {
                return result;
            }

            if (isAchievement)
            {
                result.AchievementId = achievementId;
            }
            else if (isMastery)
            {
                result.MasteryId = masteryLevel!.MasteryId;
                result.MasteryLevel = masteryLevel.Level;
            }
            else
            {
                result.Expansion = access;
            }

            return result;
        }

        /// <summary>
        /// True when a value matches more than one kind of gate, which is
        /// why <see cref="Classify"/> left it as text. Reported by the
        /// updater so a new collision is visible rather than silent.
        /// </summary>
        public static bool IsAmbiguous(string? requirement, VendorRequirementNames? names)
        {
            string? key = WikiRequirementText.LinkTarget(requirement);
            if (key == null || names == null)
            {
                return false;
            }

            int matched = 0;
            matched += names.AchievementIdsByName.ContainsKey(key) ? 1 : 0;
            matched += names.MasteryLevelsByName.ContainsKey(key) ? 1 : 0;
            matched += ExpansionAccessByTitle.ContainsKey(key) ? 1 : 0;
            return matched > 1;
        }
    }
}
