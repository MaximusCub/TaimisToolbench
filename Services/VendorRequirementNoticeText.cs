using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What a vendor requirement notice says to the player. Two sentences:
    /// what the vendor wants, then what that means for them.
    /// <para>
    /// The first sentence names the KIND of thing. A player who reads
    /// "Radiance of the Sun God" cannot tell an achievement from an item.
    /// The second sentence names an action wherever there is one, so a
    /// requirement the module did not check says why and what to change.
    /// </para>
    /// </summary>
    internal static class VendorRequirementNoticeText
    {
        /// <summary>
        /// The Plan Notes line, which names the item because nothing
        /// around it does.
        /// </summary>
        public static string For(
            string itemName, VendorRequirementNotice notice, AccountProgressionAccess access)
        {
            if (notice == null)
            {
                return null;
            }

            return $"{itemName}: this vendor requires {Subject(notice)}. {Outcome(notice, access)}";
        }

        /// <summary>
        /// The same fact on the Shopping List row for that purchase, where
        /// the row already names the item and repeating it reads as a
        /// stutter. Written to follow a clause, because the source badge's
        /// own hover puts "Buy from a vendor - " in front of it.
        /// </summary>
        public static string ForRow(
            VendorRequirementNotice notice, AccountProgressionAccess access)
        {
            if (notice == null)
            {
                return null;
            }

            return $"Requires {Subject(notice)}. {Outcome(notice, access)}";
        }

        private static string Subject(VendorRequirementNotice notice)
        {
            string text = notice.RequirementText;
            switch (notice.Kind)
            {
                case VendorRequirementKind.Achievement:
                    return $"the {text} achievement";
                case VendorRequirementKind.Mastery:
                    return $"the {text} mastery";
                case VendorRequirementKind.Expansion:
                    return $"the {text} expansion";
                default:
                    return text;
            }
        }

        private static string Outcome(
            VendorRequirementNotice notice, AccountProgressionAccess access)
        {
            if (notice.Status == VendorRequirementStatus.NotMet)
            {
                return "Your account does not have it.";
            }

            if (notice.UnknownReason == VendorRequirementUnknownReason.RequirementNotUnderstood)
            {
                return "This module cannot check that one.";
            }

            switch (access)
            {
                case AccountProgressionAccess.NotConsented:
                    return "To check it, disable this module in Blish HUD, tick its progression "
                        + "permission, then enable it again.";
                case AccountProgressionAccess.KeyMissingScope:
                    return "Your Guild Wars 2 API key does not grant progression. "
                        + "Make a new key with that permission.";
                case AccountProgressionAccess.SubtokenNotReady:
                    return "Your API key had not reached the module yet. Generate the plan again.";
                case AccountProgressionAccess.NotNeeded:
                    // A plan restored from a build that predates this
                    // answer carries the default. It never checked, and it
                    // cannot say why, so it says the one thing that is
                    // true and useful.
                    return "Generate the plan again to check it.";
                default:
                    return "The check failed. Generate the plan again.";
            }
        }
    }
}
