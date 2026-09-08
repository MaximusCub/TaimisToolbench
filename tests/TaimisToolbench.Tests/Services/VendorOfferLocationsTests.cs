using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RepoFileLocator;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Models/VendorOffer.cs holds no place names, so this is the only way
    /// back to them. The shipped file still carries every one.
    /// </summary>
    public class VendorOfferLocationsTests
    {
        private const string Json = @"{
            ""schemaVersion"": 1,
            ""offers"": [
                { ""offerId"": ""a"", ""outputItemId"": 1, ""outputCount"": 1,
                  ""merchantName"": ""Somebody"",
                  ""locations"": [""Lion's Arch"", ""Divinity's Reach""] },
                { ""offerId"": ""b"", ""outputItemId"": 2, ""outputCount"": 1,
                  ""merchantName"": ""Nobody"" }
            ]
        }";

        [Fact]
        public void AnOfferWithLocations_ReadsThemBackInOrder()
        {
            var found = Read(Json, "a");

            Assert.Equal(new[] { "Lion's Arch", "Divinity's Reach" }, Assert.Single(found).Value);
        }

        /// <summary>
        /// An offer the file carries with no locations answers with an
        /// empty list; an offer it does not carry answers with nothing at
        /// all. A caller has to be able to tell those apart.
        /// </summary>
        [Fact]
        public void NoLocationsAndNoSuchOffer_AreDifferentAnswers()
        {
            var found = Read(Json, "b", "no-such-offer");

            Assert.Empty(found["b"]);
            Assert.False(found.ContainsKey("no-such-offer"));
        }

        [Fact]
        public void NoIdsRequested_ReadsNothing()
        {
            Assert.Empty(Read(Json));
            Assert.Empty(VendorOfferLocations.Read(Stream(Json), null));
        }

        [Fact]
        public void ANullStream_Throws()
        {
            Assert.Throws<ArgumentNullException>(
                () => VendorOfferLocations.Read(null, new[] { "a" }));
        }

        /// <summary>
        /// The shipped corpus, through the same loader the module uses and
        /// the reader beside it: an offer whose place names are gone from
        /// the loaded model still has them on disk.
        /// </summary>
        [Fact]
        public void TheShippedCorpus_StillCarriesLocationsTheModelDoesNot()
        {
            string path = FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            Assert.False(string.IsNullOrEmpty(path), "Could not locate ref/vendor_offers.json.");

            List<VendorOffer> offers;
            using (var stream = File.OpenRead(path))
            {
                offers = new VendorOfferLoader().Load(stream).Offers;
            }

            // Gift of the Hylek: Sayida the Sly's offer, the one
            // AcquisitionHintSeedVendorAgreementTests checks the shipped
            // hint prose against.
            var ids = offers.Where(o => o.OutputItemId == 106986).Select(o => o.OfferId).ToList();
            Assert.NotEmpty(ids);

            IReadOnlyDictionary<string, IReadOnlyList<string>> located;
            using (var stream = File.OpenRead(path))
            {
                located = VendorOfferLocations.Read(stream, ids);
            }

            Assert.Equal(ids.Count, located.Count);
            Assert.Contains(located.Values.SelectMany(v => v), name => !string.IsNullOrEmpty(name));
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> Read(
            string json, params string[] offerIds)
        {
            using (var stream = Stream(json))
            {
                return VendorOfferLocations.Read(stream, offerIds);
            }
        }

        private static MemoryStream Stream(string json)
        {
            return new MemoryStream(Encoding.UTF8.GetBytes(json));
        }
    }
}
