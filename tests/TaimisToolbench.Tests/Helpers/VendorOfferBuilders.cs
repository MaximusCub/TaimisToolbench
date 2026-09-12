using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Tests.Helpers
{
    /// <summary>
    /// Shared VendorOffer builder helpers. CoinVendorOffer and
    /// MixedVendorOffer were private static methods on PlanSolverTests
    /// before that 2705-line file was split into focused test files - both
    /// helpers are called from several of the split files, so they moved
    /// here rather than being duplicated per file.
    /// </summary>
    internal static class VendorOfferBuilders
    {
        public static VendorOffer CoinVendorOffer(
            int outputItemId, int coinCost, int outputCount = 1, int? dailyCap = null, int? weeklyCap = null,
            int? seasonalCap = null)
        {
            return new VendorOffer
            {
                OfferId = $"test-{outputItemId}-{coinCost}",
                OutputItemId = outputItemId,
                OutputCount = outputCount,
                CostLines = new List<CostLine>
                {
                    new CostLine { Type = "Currency", Id = Gw2Constants.CoinCurrencyId, Count = coinCost },
                },
                MerchantName = "TestMerchant",
                DailyCap = dailyCap,
                WeeklyCap = weeklyCap,
                SeasonalCap = seasonalCap,
            };
        }

        public static VendorOffer MixedVendorOffer(
            int outputItemId, int coinCost, int currencyId, int currencyCount, int outputCount = 1,
            int? dailyCap = null, int? weeklyCap = null, int? seasonalCap = null)
        {
            var costLines = new List<CostLine>();
            if (coinCost > 0)
            {
                costLines.Add(new CostLine
                {
                    Type = "Currency",
                    Id = Gw2Constants.CoinCurrencyId,
                    Count = coinCost,
                });
            }

            costLines.Add(new CostLine { Type = "Currency", Id = currencyId, Count = currencyCount });

            return new VendorOffer
            {
                OfferId = "test-mixed-" + outputItemId + "-" + currencyId + "-" + currencyCount,
                OutputItemId = outputItemId,
                OutputCount = outputCount,
                CostLines = costLines,
                MerchantName = "Mixed Vendor",
                DailyCap = dailyCap,
                WeeklyCap = weeklyCap,
                SeasonalCap = seasonalCap,
            };
        }

        /// <summary>
        /// An offer mixing TP-valued
        /// Item cost line(s) with non-coin currency cost line(s) - the real
        /// field case that motivated the feature ("Amalgamated Rift
        /// Essence": currencies + Globs of Ectoplasm). itemCostLines/
        /// currencyCostLines are (id, count) pairs at the OFFER's own
        /// per-batch (unscaled) rate; coinCost is an optional raw coin
        /// line, omitted entirely when 0 (matching MixedVendorOffer's own
        /// convention above) so a caller can build a genuine 2-kind
        /// (item+currency, no coin) offer.
        /// </summary>
        public static VendorOffer ItemAndCurrencyVendorOffer(
            int outputItemId,
            (int ItemId, int Count)[] itemCostLines,
            (int CurrencyId, int Count)[] currencyCostLines,
            int coinCost = 0,
            int outputCount = 1)
        {
            var costLines = new List<CostLine>();
            if (coinCost > 0)
            {
                costLines.Add(new CostLine
                {
                    Type = "Currency",
                    Id = Gw2Constants.CoinCurrencyId,
                    Count = coinCost,
                });
            }

            if (itemCostLines != null)
            {
                foreach (var (itemId, count) in itemCostLines)
                {
                    costLines.Add(new CostLine { Type = "Item", Id = itemId, Count = count });
                }
            }

            if (currencyCostLines != null)
            {
                foreach (var (currencyId, count) in currencyCostLines)
                {
                    costLines.Add(new CostLine { Type = "Currency", Id = currencyId, Count = count });
                }
            }

            return new VendorOffer
            {
                OfferId = "test-item-currency-" + outputItemId,
                OutputItemId = outputItemId,
                OutputCount = outputCount,
                CostLines = costLines,
                MerchantName = "Barter Vendor",
            };
        }
    }
}
