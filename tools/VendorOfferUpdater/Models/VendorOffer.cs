using System.Collections.Generic;

namespace VendorOfferUpdater.Models
{
    public class VendorOffer
    {
        // Nullable rather than defaulted to "": always populated by the
        // one production construction site (ConvertToOffer), but a
        // deserialized --merge-into baseline entry from a malformed/legacy
        // file could theoretically omit either key, and MergeIntoBaseline's
        // own "?? string.Empty" coalescing below already treats
        // MerchantName as possibly null - a non-null default here would
        // make such a row round-trip differently (an empty "" written out
        // instead of the key being omitted, since output serialization
        // uses DefaultIgnoreCondition.WhenWritingNull).
        public string? OfferId { get; set; }

        public int OutputItemId { get; set; }

        public int OutputCount { get; set; }

        public List<CostLine> CostLines { get; set; } = new List<CostLine>();

        public string? MerchantName { get; set; }

        // Nullable, not just an empty list: --merge-into deliberately
        // restores this to null (rather than []) for a baseline offer with
        // no location data, so an untouched offer round-trips byte-for-byte
        // against a baseline that never had the "locations" key at all -
        // see the --merge-into round-trip fix in Program.cs.
        public List<string>? Locations { get; set; } = new List<string>();

        public int? DailyCap { get; set; }

        public int? WeeklyCap { get; set; }

        // Astral Acclaim package: Wizard's Vault seasonal purchase cap
        // (resets each Vault season), or null for every non-Vault offer.
        // See Models/VendorOffer.cs (the runtime copy) for the full
        // deferred-consumption note - this field is additive/seed-only in
        // this tool; nothing here interprets it.
        public int? SeasonalCap { get; set; }

        // Homestead Refinement efficiency tier
        // (0/1/2) this offer row corresponds to, or null for a non-
        // Homestead-Refinement offer. See ConvertToOffer/HomesteadTierResolver.
        public int? HomesteadTier { get; set; }

        // Additive
        // pass-through mirror of the runtime Models/VendorOffer.cs field
        // (see that file's own doc comment) - same round-trip-safety
        // reason SeasonalCap/HomesteadTier are both mirrored here. Without
        // this, --merge-into deserializes the whole baseline through THIS
        // model (MergeIntoBaseline, Program.cs), and any row this pass
        // does not touch is still re-serialized through it - a property
        // this class does not declare is silently dropped on every future
        // run, even one scraping an unrelated merchant, with no OfferId
        // change to make the loss noticeable (SeasonalFestival is
        // deliberately not hashed by VendorOfferHasher).
        public string? SeasonalFestival { get; set; }

        // The recipe sheet the account must own before this vendor will
        // trade, and the recipe id that sheet unlocks. See
        // Models/VendorOffer.cs (the runtime copy) for the full note, and
        // ConvertToOffer/VendorUnlockRequirementParser for how the wiki's
        // "Has requirement" text becomes these two ids. Not hashed into
        // OfferId, so the same pass-through-mirror reason SeasonalFestival
        // is declared here applies: a property this class does not declare
        // is dropped from every untouched --merge-into row with no OfferId
        // change to make the loss noticeable.
        public int? UnlockRecipeItemId { get; set; }

        public int? UnlockRecipeId { get; set; }
    }
}
