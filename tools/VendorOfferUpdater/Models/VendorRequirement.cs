namespace VendorOfferUpdater.Models
{
    /// <summary>
    /// What a vendor demands of the account before it will trade, as the
    /// wiki's free-form "Has requirement" text plus whatever of it the GW2
    /// API could be made to name.
    /// <para>
    /// Mirrors the runtime Models/VendorRequirement.cs - see that file for
    /// what each id means to the module. Additive pass-through here for the
    /// same round-trip reason as VendorOffer.SeasonalFestival: a property
    /// this model does not declare is dropped from every untouched
    /// --merge-into row.
    /// </para>
    /// </summary>
    public class VendorRequirement
    {
        public string? Text { get; set; }

        public int? AchievementId { get; set; }

        public int? MasteryId { get; set; }

        public int? MasteryLevel { get; set; }

        public string? Expansion { get; set; }
    }
}
