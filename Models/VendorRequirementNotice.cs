namespace TaimisToolbench.Models
{
    /// <summary>
    /// Informational-only record of a plan step whose winning vendor offer
    /// is behind a requirement the account does not meet, or that could not
    /// be checked.
    /// <para>
    /// Treated exactly as TimegatedItem is: it never gates offer
    /// eligibility and never re-routes the solver. A gated vendor is still
    /// chosen and still priced; the player is told what the vendor wants.
    /// A requirement the account MEETS produces no notice.
    /// </para>
    /// </summary>
    internal class VendorRequirementNotice
    {
        public int ItemId { get; set; }

        public string RequirementText { get; set; }

        public VendorRequirementStatus Status { get; set; }

        /// <summary>What kind of thing the vendor wants - see
        /// Models/VendorRequirement.cs.</summary>
        public VendorRequirementKind Kind { get; set; }

        /// <summary>
        /// Why <see cref="Status"/> is Unknown; None on a NotMet notice.
        /// Read together with
        /// CraftingPlanResult.AccountProgressionAccess, which says what
        /// the player can do about an AccountDataUnavailable answer.
        /// </summary>
        public VendorRequirementUnknownReason UnknownReason { get; set; }
    }
}
