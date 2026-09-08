using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VendorOfferUpdater
{
    /// <summary>
    /// Fetches the achievement and mastery name lists
    /// <see cref="VendorRequirementClassifier"/> matches against, from
    /// api.guildwars2.com and never the wiki.
    /// <para>
    /// Costs one request for the achievement id list plus one per 200 ids
    /// (43 in total, measured at 8,230 achievements), and one for the whole
    /// mastery list. A failure is thrown, not swallowed: a run that
    /// silently classified nothing would write a dataset whose gates the
    /// module can no longer check, with no sign in the output that anything
    /// went wrong.
    /// </para>
    /// </summary>
    public static class VendorRequirementNameLoader
    {
        private const string AchievementsUrl = "https://api.guildwars2.com/v2/achievements";
        private const string MasteriesUrl = "https://api.guildwars2.com/v2/masteries?ids=all";

        /// <summary>Ids per request. 200 is the GW2 API's own page cap.</summary>
        private const int BatchSize = 200;

        public static async Task<VendorRequirementNames> LoadAsync(
            HttpClient httpClient, CancellationToken ct)
        {
            Console.WriteLine("Loading GW2 API achievement and mastery names...");

            var achievements = await LoadAchievementsAsync(httpClient, ct);
            var masteries = await LoadMasteriesAsync(httpClient, ct);

            var names = VendorRequirementNames.Build(achievements, masteries);
            Console.WriteLine(
                $"  Loaded {achievements.Count} achievements " +
                $"({names.AchievementIdsByName.Count} uniquely named) and " +
                $"{masteries.Count} mastery tracks " +
                $"({names.MasteryLevelsByName.Count} uniquely named levels).");

            return names;
        }

        private static async Task<List<KeyValuePair<int, string>>> LoadAchievementsAsync(
            HttpClient httpClient, CancellationToken ct)
        {
            string idsResponse = await httpClient.GetStringAsync(AchievementsUrl);
            var ids = JsonSerializer.Deserialize<List<int>>(idsResponse)
                ?? throw new InvalidOperationException(
                    "GW2 API achievements response deserialized to null.");

            var achievements = new List<KeyValuePair<int, string>>(ids.Count);

            for (int i = 0; i < ids.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batch = ids.GetRange(i, Math.Min(BatchSize, ids.Count - i));
                string url = $"{AchievementsUrl}?ids={string.Join(",", batch)}";
                string response = await httpClient.GetStringAsync(url);
                using var parsed = JsonDocument.Parse(response);

                foreach (var achievement in parsed.RootElement.EnumerateArray())
                {
                    if (!achievement.TryGetProperty("id", out var id) ||
                        !achievement.TryGetProperty("name", out var name))
                    {
                        continue;
                    }

                    string? text = name.GetString();
                    if (text != null)
                    {
                        achievements.Add(new KeyValuePair<int, string>(id.GetInt32(), text));
                    }
                }
            }

            return achievements;
        }

        private static async Task<List<MasteryTrack>> LoadMasteriesAsync(
            HttpClient httpClient, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string response = await httpClient.GetStringAsync(MasteriesUrl);
            using var parsed = JsonDocument.Parse(response);

            var tracks = new List<MasteryTrack>();
            foreach (var mastery in parsed.RootElement.EnumerateArray())
            {
                if (!mastery.TryGetProperty("id", out var id))
                {
                    continue;
                }

                var levelNames = new List<string>();
                if (mastery.TryGetProperty("levels", out var levels))
                {
                    foreach (var level in levels.EnumerateArray())
                    {
                        if (level.TryGetProperty("name", out var name))
                        {
                            levelNames.Add(name.GetString() ?? string.Empty);
                        }
                        else
                        {
                            // A level with no name still occupies its index,
                            // which is what /v2/account/masteries reports.
                            levelNames.Add(string.Empty);
                        }
                    }
                }

                tracks.Add(new MasteryTrack(id.GetInt32(), levelNames));
            }

            return tracks;
        }
    }
}
