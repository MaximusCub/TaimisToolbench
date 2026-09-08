namespace TaimisToolbench.Models
{
    /// <summary>
    /// Whether the account satisfies a vendor's requirement.
    /// <para>
    /// Unknown is a first-class answer, not a default: it covers a
    /// requirement the wiki states in prose nothing can match, and an
    /// account whose data was never read. Reporting either as NotMet would
    /// tell a player they cannot buy something they can.
    /// </para>
    /// </summary>
    internal enum VendorRequirementStatus
    {
        Unknown,
        Met,
        NotMet,
    }

    /// <summary>
    /// What a vendor demands of the account before it will trade, from the
    /// wiki's "Has requirement" text.
    /// <para>
    /// Text is always present. The ids are present only where
    /// tools/VendorOfferUpdater/VendorRequirementClassifier.cs recognized
    /// the whole value as one GW2 API name, which it does for about a third
    /// of the rows that carry a requirement; the rest are shown as text and
    /// never checked. At most one kind is set on any one requirement.
    /// </para>
    /// </summary>
    internal class VendorRequirement
    {
        public string Text { get; set; }

        public int? AchievementId { get; set; }

        public int? MasteryId { get; set; }

        /// <summary>
        /// Index into that mastery track's levels. /v2/account/masteries
        /// reports the highest index the account has reached, so the
        /// account meets this level when its own index is at least this.
        /// </summary>
        public int? MasteryLevel { get; set; }

        /// <summary>
        /// The flag /v2/account lists in its "access" array, e.g.
        /// "HeartOfThorns". A string rather than an enum because Gw2Sharp
        /// 1.7.4 predates three of the expansions the wiki cites, and its
        /// enum turns an unknown value into a member this code would then
        /// read as some other expansion.
        /// </summary>
        public string Expansion { get; set; }
    }
}
