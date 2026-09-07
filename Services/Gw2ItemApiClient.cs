using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TaimisToolbench.Services
{
    internal class Gw2ItemApiClient : IItemApiClient
    {
        private const string BaseUrl = "https://api.guildwars2.com/v2";

        private readonly HttpClient _http;

        public Gw2ItemApiClient(HttpClient http)
        {
            _http = http;
        }

        public async Task<IReadOnlyList<RawItem>> GetItemsAsync(
            IReadOnlyList<int> itemIds, CancellationToken ct)
        {
            if (itemIds == null || itemIds.Count == 0)
            {
                return new List<RawItem>();
            }

            var ids = string.Join(",", itemIds);
            var url = $"{BaseUrl}/items?ids={ids}";

            using (var request = new HttpRequestMessage(HttpMethod.Get, url))
            using (var response = await _http.SendAsync(request, ct))
            {
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    return new List<RawItem>();
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"GW2 API error {(int)response.StatusCode} from {url}");
                }

                var json = await response.Content.ReadAsStringAsync();
                var array = JArray.Parse(json);

                var results = new List<RawItem>();
                foreach (var item in array)
                {
                    // Missing/non-array
                    // "flags" yields an empty list, never null, mirroring
                    // the Name/Icon/Rarity "" fallback convention above.
                    var flags = new List<string>();
                    var flagsToken = item["flags"] as JArray;
                    if (flagsToken != null)
                    {
                        foreach (var flag in flagsToken)
                        {
                            // a malformed array
                            // element (null, or a non-string token) must
                            // not inject a null into Flags - RawItem.Flags'
                            // own doc comment promises a never-null LIST,
                            // but a null ENTRY inside it would still be a
                            // silent contract violation for any future
                            // consumer beyond the current Contains(...) check.
                            var flagValue = flag.Value<string>();
                            if (flagValue != null)
                            {
                                flags.Add(flagValue);
                            }
                        }
                    }

                    results.Add(new RawItem
                    {
                        Id = item.Value<int>("id"),
                        Name = item.Value<string>("name") ?? "",
                        Icon = item.Value<string>("icon") ?? "",
                        Rarity = item.Value<string>("rarity") ?? "",
                        Flags = flags,
                        ItemType = item.Value<string>("type") ?? "",
                        Level = item.Value<int>("level"),
                        VendorValue = item.Value<int>("vendor_value"),
                        Description = item.Value<string>("description"),
                        Restrictions = ReadStringArray(item["restrictions"] as JArray),
                        Detail = ParseDetail(item["details"] as JObject),
                    });
                }

                return results;
            }
        }

        /// <summary>
        /// The "details" block, or null when the item has none. A null
        /// return is the normal case for crafting materials (measured on
        /// 19700/19685/46683), not an error - see RawItem.Detail.
        /// </summary>
        private static RawItemDetail ParseDetail(JObject details)
        {
            if (details == null)
            {
                return null;
            }

            var detail = new RawItemDetail
            {
                SubType = details.Value<string>("type"),
                WeightClass = details.Value<string>("weight_class"),
                Defense = details.Value<int?>("defense"),
                MinPower = details.Value<int?>("min_power"),
                MaxPower = details.Value<int?>("max_power"),
                DamageType = details.Value<string>("damage_type"),
                AttributeAdjustment = details.Value<double?>("attribute_adjustment") ?? 0d,
                Bonuses = ReadStringArray(details["bonuses"] as JArray),
                StatChoiceIds = ReadIntArray(details["stat_choices"] as JArray),
                NourishmentDurationMs = details.Value<int?>("duration_ms"),
                NourishmentDescription = details.Value<string>("description"),
                EffectName = details.Value<string>("name"),
                EffectIconUrl = details.Value<string>("icon"),
                InfixAttributes = new List<RawItemAttribute>(),
            };

            detail.InfusionSlots = ReadInfusionSlots(details["infusion_slots"] as JArray);
            detail.SocketedUpgradeCount = CountSocketedUpgrades(details);

            var infix = details["infix_upgrade"] as JObject;
            if (infix != null)
            {
                detail.InfixStatId = infix.Value<int?>("id");
                detail.BuffDescription = (infix["buff"] as JObject)?.Value<string>("description");

                var attributes = infix["attributes"] as JArray;
                if (attributes != null)
                {
                    foreach (var attribute in attributes)
                    {
                        var obj = attribute as JObject;
                        var name = obj?.Value<string>("attribute");
                        if (string.IsNullOrEmpty(name))
                        {
                            continue;
                        }

                        detail.InfixAttributes.Add(new RawItemAttribute
                        {
                            Attribute = name,
                            Modifier = obj.Value<int?>("modifier") ?? 0,
                        });
                    }
                }
            }

            return detail;
        }

        // A malformed element is dropped rather than counted: a slot the
        // parser cannot read is not evidence that a slot exists.
        private static List<RawInfusionSlot> ReadInfusionSlots(JArray array)
        {
            var slots = new List<RawInfusionSlot>();
            if (array == null)
            {
                return slots;
            }

            foreach (var token in array)
            {
                var obj = token as JObject;
                if (obj == null)
                {
                    continue;
                }

                slots.Add(new RawInfusionSlot
                {
                    Flags = ReadStringArray(obj["flags"] as JArray),
                    IsFilled = obj["item_id"] != null && obj["item_id"].Type != JTokenType.Null,
                });
            }

            return slots;
        }

        // suffix_item_id is a number and secondary_suffix_item_id a string
        // that is "" when absent (measured on 30699 Bolt and 30703 Sunrise),
        // so both are read as text and an id of 0 counts as no upgrade.
        private static int CountSocketedUpgrades(JObject details)
        {
            int count = 0;
            if (HasUpgradeId(details, "suffix_item_id"))
            {
                count++;
            }

            if (HasUpgradeId(details, "secondary_suffix_item_id"))
            {
                count++;
            }

            return count;
        }

        private static bool HasUpgradeId(JObject details, string field)
        {
            var value = details[field];
            if (value == null ||
                (value.Type != JTokenType.Integer && value.Type != JTokenType.String))
            {
                return false;
            }

            var text = value.Value<string>();
            return !string.IsNullOrEmpty(text) && text != "0";
        }

        // Malformed elements are dropped rather than injected as nulls -
        // same rule the "flags" walk above applies for the same reason.
        private static List<string> ReadStringArray(JArray array)
        {
            var values = new List<string>();
            if (array == null)
            {
                return values;
            }

            foreach (var token in array)
            {
                var value = token.Value<string>();
                if (value != null)
                {
                    values.Add(value);
                }
            }

            return values;
        }

        private static List<int> ReadIntArray(JArray array)
        {
            var values = new List<int>();
            if (array == null)
            {
                return values;
            }

            foreach (var token in array)
            {
                var value = token.Value<int?>();
                if (value.HasValue)
                {
                    values.Add(value.Value);
                }
            }

            return values;
        }
    }
}
