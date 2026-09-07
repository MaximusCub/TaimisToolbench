using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TaimisToolbench.Services
{
    internal class Gw2PriceApiClient : IPriceApiClient
    {
        private const string BaseUrl = "https://api.guildwars2.com/v2";

        private readonly HttpClient _http;
        private readonly Func<TimeSpan, CancellationToken, Task> _retryDelay;

        /// <summary>
        /// <paramref name="retryDelay"/> is injected by tests so a scripted
        /// refusal costs no wall-clock time; null uses Task.Delay.
        /// </summary>
        public Gw2PriceApiClient(
            HttpClient http, Func<TimeSpan, CancellationToken, Task> retryDelay = null)
        {
            _http = http;
            _retryDelay = retryDelay;
        }

        public async Task<PriceBatchResult> GetPricesAsync(
            IReadOnlyList<int> itemIds, CancellationToken ct)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return new PriceBatchResult(new List<RawPriceEntry>(), absenceProven: true);
            }

            var ids = string.Join(",", itemIds);
            var url = $"{BaseUrl}/commerce/prices?ids={ids}";

            // Gw2ApiRequest rather than HttpClient directly: the throw
            // below would otherwise turn a refusal the API said to retry
            // into an unpriced batch.
            using (var response = await Gw2ApiRequest.GetAsync(_http, url, ct, _retryDelay))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // Empty, but NOT proof of absence - see
                    // PriceBatchResult.AbsenceProven.
                    return new PriceBatchResult(new List<RawPriceEntry>(), absenceProven: false);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"GW2 API error {(int)response.StatusCode} from {url}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var array = JArray.Parse(json);

                var results = new List<RawPriceEntry>();
                foreach (var item in array)
                {
                    results.Add(new RawPriceEntry
                    {
                        Id = item.Value<int>("id"),
                        BuyUnitPrice = item["buys"]?.Value<int>("unit_price") ?? 0,
                        SellUnitPrice = item["sells"]?.Value<int>("unit_price") ?? 0,
                    });
                }

                return new PriceBatchResult(results, absenceProven: true);
            }
        }
    }
}
