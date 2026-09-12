using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The coin prices in the SHIPPED ref/vendor_offers.json, read through
    /// the production VendorOfferLoader.
    /// <para>
    /// A coin cost line counts copper. The GW2 Wiki writes a vendor's coin
    /// price under whichever of "Coin", "Copper", "Silver" or "Gold" the
    /// page's editor chose, and the scraper used to copy every one of those
    /// numbers into the cost line as copper. Every gold price shipped at one
    /// ten-thousandth of its real value and every silver price at one
    /// hundredth, so a vendor route could win a craft-versus-buy comparison
    /// it should lose.
    /// </para>
    /// </summary>
    public class VendorCoinCostShippedDataTests
    {
        private const int CoinCurrencyId = 1;

        /// <summary>Palak sells this for 250 Sun Beads, 300,000 Karma and 200 gold.</summary>
        private const int GiftOfTheHylek = 106986;

        /// <summary>Lyhr sells this recipe sheet for 50 silver and nothing else.</summary>
        private const int RecipeFineRiftMotivation = 100621;

        private static long CoinCost(VendorOffer offer)
        {
            return offer.CostLines
                .Where(l => l.Type == "Currency" && l.Id == CoinCurrencyId)
                .Sum(l => (long)l.Count);
        }

        private static string SaleKey(VendorOffer offer)
        {
            var otherCosts = offer.CostLines
                .Where(l => !(l.Type == "Currency" && l.Id == CoinCurrencyId))
                .Select(l => l.Type + ":" + l.Id + ":" + l.Count)
                .OrderBy(t => t, System.StringComparer.Ordinal);

            return offer.MerchantName + "|" + offer.OutputCount + "|" +
                   string.Join(",", otherCosts);
        }

        [Fact]
        public void GiftOfTheHylek_ShippedCoinCostIsTwoHundredGold()
        {
            var corpus = RealCorpusFixture.Load();

            Assert.True(corpus.OffersByOutputItem.TryGetValue(GiftOfTheHylek, out var offers));
            var offer = Assert.Single(offers, o => o.MerchantName == "Palak");
            Assert.Equal(200L * 10000L, CoinCost(offer));
        }

        [Fact]
        public void RecipeFineRiftMotivation_ShippedCoinCostIsFiftySilver()
        {
            var corpus = RealCorpusFixture.Load();

            Assert.True(corpus.OffersByOutputItem.TryGetValue(
                RecipeFineRiftMotivation, out var offers));
            var offer = Assert.Single(offers, o => o.MerchantName == "Lyhr");
            Assert.Equal(50L * 100L, CoinCost(offer));
        }

        /// <summary>
        /// Two shipped rows for one sale whose coin prices are exactly 100 or
        /// 10,000 apart are the same price read in two different units. The
        /// solver would buy at the lower one.
        /// </summary>
        [Fact]
        public void NoShippedSale_CarriesTheSamePriceInTwoUnits()
        {
            var corpus = RealCorpusFixture.Load();
            var offenders = new List<string>();

            foreach (var byItem in corpus.OffersByOutputItem)
            {
                foreach (var sale in byItem.Value.GroupBy(SaleKey))
                {
                    var prices = sale.Select(CoinCost).Where(c => c > 0).Distinct().ToList();
                    foreach (var low in prices)
                    {
                        if (prices.Contains(low * 100) || prices.Contains(low * 10000))
                        {
                            offenders.Add(sale.First().MerchantName + " item " + byItem.Key);
                            break;
                        }
                    }
                }
            }

            Assert.Empty(offenders);
        }
    }
}
