using System;
using System.Collections.Generic;

namespace VendorOfferUpdater
{
    /// <summary>One mastery track and its level names, in level order.</summary>
    public sealed class MasteryTrack
    {
        public MasteryTrack(int id, IReadOnlyList<string> levelNames)
        {
            Id = id;
            LevelNames = levelNames ?? Array.Empty<string>();
        }

        public int Id { get; }

        public IReadOnlyList<string> LevelNames { get; }
    }

    /// <summary>Which mastery track, and which of its levels.</summary>
    public sealed class MasteryLevelRef
    {
        public MasteryLevelRef(int masteryId, int level)
        {
            MasteryId = masteryId;
            Level = level;
        }

        public int MasteryId { get; }

        /// <summary>
        /// Index into /v2/masteries.levels. /v2/account/masteries reports
        /// the HIGHEST level index the account has reached on a track, so
        /// the account meets this level when its own index is at least
        /// this one.
        /// </summary>
        public int Level { get; }
    }

    /// <summary>
    /// The GW2 API name lists a wiki requirement string is matched against.
    /// <para>
    /// A name held by two different achievements, or by two different
    /// mastery levels, is dropped rather than resolved to one of them:
    /// measured over the live API, 193 of 8,230 achievement names are
    /// shared by more than one achievement, and picking either would let a
    /// vendor gate report the wrong achievement as satisfied.
    /// </para>
    /// </summary>
    public sealed class VendorRequirementNames
    {
        private VendorRequirementNames(
            Dictionary<string, int> achievementIdsByName,
            Dictionary<int, string> achievementNamesById,
            Dictionary<string, MasteryLevelRef> masteryLevelsByName)
        {
            AchievementIdsByName = achievementIdsByName;
            AchievementNamesById = achievementNamesById;
            MasteryLevelsByName = masteryLevelsByName;
        }

        public IReadOnlyDictionary<string, int> AchievementIdsByName { get; }

        public IReadOnlyDictionary<int, string> AchievementNamesById { get; }

        public IReadOnlyDictionary<string, MasteryLevelRef> MasteryLevelsByName { get; }

        public static VendorRequirementNames Empty { get; } = Build(null, null);

        /// <summary>
        /// Indexes the two API lists. Matching is ordinal and
        /// case-sensitive: a wiki requirement quotes the in-game name, and
        /// a case-insensitive index would collapse names that differ only
        /// in case into the ambiguous bucket for no gain.
        /// </summary>
        public static VendorRequirementNames Build(
            IEnumerable<KeyValuePair<int, string>>? achievements,
            IEnumerable<MasteryTrack>? masteries)
        {
            var achievementIds = new Dictionary<string, int>(StringComparer.Ordinal);
            var achievementNames = new Dictionary<int, string>();
            var ambiguousAchievements = new HashSet<string>(StringComparer.Ordinal);

            foreach (var pair in achievements ?? Array.Empty<KeyValuePair<int, string>>())
            {
                string name = (pair.Value ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    continue;
                }

                achievementNames[pair.Key] = name;

                if (achievementIds.TryGetValue(name, out int existing) && existing != pair.Key)
                {
                    ambiguousAchievements.Add(name);
                    continue;
                }

                achievementIds[name] = pair.Key;
            }

            foreach (string name in ambiguousAchievements)
            {
                achievementIds.Remove(name);
            }

            var masteryLevels = new Dictionary<string, MasteryLevelRef>(StringComparer.Ordinal);
            var ambiguousLevels = new HashSet<string>(StringComparer.Ordinal);

            foreach (var track in masteries ?? Array.Empty<MasteryTrack>())
            {
                for (int level = 0; level < track.LevelNames.Count; level++)
                {
                    string name = (track.LevelNames[level] ?? string.Empty).Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    if (masteryLevels.TryGetValue(name, out var existing) &&
                        (existing.MasteryId != track.Id || existing.Level != level))
                    {
                        ambiguousLevels.Add(name);
                        continue;
                    }

                    masteryLevels[name] = new MasteryLevelRef(track.Id, level);
                }
            }

            foreach (string name in ambiguousLevels)
            {
                masteryLevels.Remove(name);
            }

            return new VendorRequirementNames(achievementIds, achievementNames, masteryLevels);
        }
    }
}
