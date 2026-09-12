using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using Xunit;
using static TaimisToolbench.Tests.Helpers.RecipeNodeBuilders;
using static TaimisToolbench.Tests.Helpers.RepoFileLocator;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// ref/acquisition_hints_seed.json is hand-maintained prose while
    /// ref/vendor_offers.json is re-scraped by tools/VendorOfferUpdater.
    /// Where both describe the same item they are two answers to one
    /// question, and nothing but these tests keeps them from diverging.
    /// The vendor record is the authority: a hint may add facts the record
    /// has no field for (an achievement, "not craftable"), but may not
    /// contradict the merchant or location it carries.
    /// </summary>
    public class AcquisitionHintSeedVendorAgreementTests
    {
        private static IReadOnlyDictionary<int, AcquisitionHint> LoadShippedHints()
        {
            string path = FindRepoFile(Path.Combine("ref", "acquisition_hints_seed.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/acquisition_hints_seed.json by walking up from the test assembly's directory.");
            using (var stream = File.OpenRead(path))
            {
                return AcquisitionHintService.Load(stream);
            }
        }

        private static List<VendorOffer> LoadShippedOffers()
        {
            string path = FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            Assert.False(
                string.IsNullOrEmpty(path),
                "Could not locate ref/vendor_offers.json by walking up from the test assembly's directory.");
            using (var stream = File.OpenRead(path))
            {
                return new VendorOfferLoader().Load(stream).Offers;
            }
        }

        private static IReadOnlyDictionary<string, IReadOnlyList<string>> LoadShippedLocations(
            IEnumerable<string> offerIds)
        {
            string path = FindRepoFile(Path.Combine("ref", "vendor_offers.json"));
            using (var stream = File.OpenRead(path))
            {
                return VendorOfferLocations.Read(stream, offerIds);
            }
        }

        [Fact]
        public void ShippedHints_NoBadgeRendersAsAnAcquisitionSourcePill()
        {
            foreach (var hint in LoadShippedHints().Values)
            {
                Assert.False(
                    DecisionPillPlanner.IsReservedSourceBadgeText(hint.Badge),
                    $"Hint badge '{hint.Badge}' for item {hint.ItemId} renders identically to an " +
                    "acquisition-source pill, which means the opposite thing (a priced source whose " +
                    "cost is in Plan.TotalCoinCost, versus an Unknown node contributing 0). " +
                    "DecisionPillPlanner drops such a badge back to UNKNOWN, so shipping one only " +
                    "throws the hint's badge away.");
            }
        }

        [Fact]
        public void ShippedHints_ForItemsTheModuleAlsoShipsAVendorOfferFor_AgreeWithThatOffer()
        {
            var hints = LoadShippedHints();
            var offersByItem = LoadShippedOffers()
                .GroupBy(o => o.OutputItemId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var hintedItemsWithOffers = hints.Keys.Where(offersByItem.ContainsKey).ToList();

            // Models/VendorOffer.cs holds no locations, so they come back
            // off disk through the production reader that exists for this.
            var locationsByOfferId = LoadShippedLocations(
                hintedItemsWithOffers.SelectMany(id => offersByItem[id]).Select(o => o.OfferId));

            // Trip-wire on the population itself: for the first seven
            // hints the module held no vendor data at all, and the
            // mechanism's implicit contract was "no source anywhere in our
            // data". These three break that precedent deliberately (see
            // KNOWN-ISSUES #8), so a fourth arriving unnoticed is
            // worth a manual look.
            Assert.Equal(
                new[] { 105804, 106712, 106986 },
                hintedItemsWithOffers.OrderBy(id => id).ToArray());

            foreach (int itemId in hintedItemsWithOffers)
            {
                string text = hints[itemId].Hint;
                var offers = offersByItem[itemId];

                Assert.True(
                    offers.Any(o => !string.IsNullOrEmpty(o.MerchantName) &&
                                    text.Contains(o.MerchantName)),
                    $"Hint for item {itemId} names no merchant the shipped vendor offer carries " +
                    $"(offer merchants: {string.Join(", ", offers.Select(o => o.MerchantName))}).");

                var locations = offers
                    .SelectMany(o => locationsByOfferId.TryGetValue(o.OfferId, out var found)
                        ? found
                        : Array.Empty<string>())
                    .ToList();

                Assert.True(
                    locations.Any(loc => !string.IsNullOrEmpty(loc) && text.Contains(loc)),
                    $"Hint for item {itemId} names no location the shipped vendor offer carries " +
                    $"(offer locations: {string.Join(", ", locations)}).");
            }
        }

        [Fact]
        public void ShippedBarterOffer_WithNoTradingPostPriceForItsCostItems_IsAFallbackVendorRoute()
        {
            // A shipped barter offer, end to end: Gift of the Survivors is
            // bought from Castaway Agnes for three account-bound Endless
            // Summer tokens plus 500 of a wallet currency, none of which
            // has a Trading Post price or a curated valuation. The whole
            // offer used to be dropped and the item read UNKNOWN; it is now
            // the fallback-tier vendor route it always was in game.
            const int GiftOfTheSurvivors = 106712;

            var offers = LoadShippedOffers()
                .Where(o => o.OutputItemId == GiftOfTheSurvivors)
                .ToList();
            Assert.Single(offers);
            Assert.Contains(offers[0].CostLines, c => c.Type == "Item");

            var vendorOffers = new Dictionary<int, IReadOnlyList<VendorOffer>>
            {
                { GiftOfTheSurvivors, offers },
            };

            var result = new PlanSolver().Solve(
                Leaf(GiftOfTheSurvivors, 1),
                new Dictionary<int, ItemPrice>(),
                vendorOffers);

            var decision = result.Decisions[0];
            Assert.Equal(AcquisitionSource.BuyFromVendor, decision.Source);
            Assert.True(decision.CanBuyVendor);

            // Fallback tier: nothing on this offer is coin, so nothing is
            // committed as coin. The three tokens ride on VendorItemCosts
            // with no gold value, and the wallet currency on
            // VendorCurrencyCosts, exactly as each is really paid.
            Assert.Equal(0, decision.TotalCost);
            Assert.Equal(3, decision.VendorItemCosts.Count);
            Assert.All(decision.VendorItemCosts, line => Assert.Null(line.GoldValue));
            Assert.Single(decision.VendorCurrencyCosts);
        }
    }
}
