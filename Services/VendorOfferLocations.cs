using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Reads the place names attached to shipped vendor offers, on demand,
    /// from the same ref/vendor_offers.json the baseline is loaded from.
    /// <para>
    /// Models/VendorOffer.cs does not carry them. All 65,315 shipped offers
    /// together hold 2.19MB of location strings, nothing in the module
    /// reads one, and a plan does not need them to route or price a
    /// purchase - so they are on disk rather than in the heap the module
    /// keeps for its whole session. This is how a surface that wants to
    /// tell a player where a merchant stands gets them back.
    /// </para>
    /// <para>
    /// The offer ids they are keyed by still hash the locations, so the
    /// generator side is untouched; see tools/VendorOfferUpdater/VendorOfferHasher.cs.
    /// </para>
    /// </summary>
    internal static class VendorOfferLocations
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        /// <summary>
        /// The locations of each requested offer that the stream carries,
        /// keyed by offer id. An id the file does not carry is absent from
        /// the result; an offer the file carries with no locations at all
        /// maps to an empty list, which is a different answer.
        /// <para>
        /// The whole file is parsed to answer this, into a shape holding
        /// only the two fields below, so nothing is retained but the
        /// requested entries. That is affordable for a lookup a user action
        /// triggers and would not be on a per-frame path; ask for every id
        /// at once rather than calling this per offer.
        /// </para>
        /// <para>
        /// A file this cannot parse throws, unlike VendorOfferStore's own
        /// load: there is no error channel here, and a caller that has
        /// already loaded the same file successfully is the only caller
        /// this has.
        /// </para>
        /// </summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<string>> Read(
            Stream offersStream, IEnumerable<string> offerIds)
        {
            if (offersStream == null)
            {
                throw new ArgumentNullException(nameof(offersStream));
            }

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            if (offerIds != null)
            {
                foreach (string id in offerIds)
                {
                    if (!string.IsNullOrEmpty(id))
                    {
                        wanted.Add(id);
                    }
                }
            }

            var found = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            if (wanted.Count == 0)
            {
                return found;
            }

            // Blocking on the async overload for the reason
            // VendorOfferLoader.Load does: System.Text.Json 5.0.0 on net461
            // ships no synchronous Deserialize(Stream).
            var dataset = JsonSerializer
                .DeserializeAsync<LocationDataset>(offersStream, Options)
                .GetAwaiter().GetResult();

            if (dataset?.Offers == null)
            {
                return found;
            }

            foreach (var offer in dataset.Offers)
            {
                if (offer?.OfferId == null || !wanted.Contains(offer.OfferId))
                {
                    continue;
                }

                found[offer.OfferId] = offer.Locations ?? (IReadOnlyList<string>)Array.Empty<string>();
            }

            return found;
        }

        /// <summary>
        /// The two fields this reader wants. Every other property in the
        /// file is unknown to this type, and System.Text.Json skips an
        /// unknown property without materialising it.
        /// </summary>
        private sealed class LocationDataset
        {
            [JsonPropertyName("offers")]
            public List<LocationRecord> Offers { get; set; }
        }

        private sealed class LocationRecord
        {
            [JsonPropertyName("offerId")]
            public string OfferId { get; set; }

            [JsonPropertyName("locations")]
            public List<string> Locations { get; set; }
        }
    }
}
