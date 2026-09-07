using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What one character contributed to a snapshot. The fetch that
    /// produces this reports a crafting-discipline failure through
    /// <see cref="DisciplinesDegraded"/> rather than through an empty
    /// <see cref="Disciplines"/> list, because the two mean different
    /// things to <see cref="CharacterSnapshotCollector"/>.
    /// </summary>
    internal class CharacterSnapshotPart
    {
        public List<SnapshotItemEntry> Items { get; } = new List<SnapshotItemEntry>();

        public List<int> ArmoryItemIds { get; } = new List<int>();

        public bool DisciplinesDegraded { get; set; }

        public List<SnapshotCharacterDiscipline> Disciplines { get; } = new List<SnapshotCharacterDiscipline>();
    }

    /// <summary>
    /// Every character's contribution, folded together in character-list
    /// order. <see cref="Disciplines"/> is null when the account's
    /// discipline data is incomplete, which
    /// <see cref="Models.AccountSnapshot.CharacterDisciplines"/> reads as
    /// "never captured".
    /// </summary>
    internal class CharacterSnapshotHarvest
    {
        public List<SnapshotItemEntry> Items { get; } = new List<SnapshotItemEntry>();

        public List<SnapshotArmoryEquip> ArmoryEquipped { get; } = new List<SnapshotArmoryEquip>();

        public List<SnapshotCharacterDiscipline> Disciplines { get; set; }
    }

    /// <summary>
    /// Runs the per-character part of a snapshot fetch several characters
    /// at a time, then folds the results back into one result ordered by
    /// the character list.
    /// <para>
    /// Items and armory wearers are collected best-effort: a character
    /// that fails contributes nothing, which under-counts what the account
    /// owns and never invents a holding. Crafting disciplines are
    /// all-or-nothing instead: one failed character discards the whole
    /// list, because a partial list reads as an affirmative "not trained"
    /// claim for every character the fetch never reached.
    /// </para>
    /// </summary>
    internal static class CharacterSnapshotCollector
    {
        // Each character costs three requests, so this is 18 in flight.
        // The GW2 API allows roughly 300 requests per minute plus a burst
        // bucket, and a 14-character account issues 42 requests in total,
        // so a whole fetch stays inside one bucket. Raising the bound past
        // this buys less and less: 14 characters already fall into 3
        // rounds.
        public const int DefaultMaxCharactersInFlight = 6;

        public static async Task<CharacterSnapshotHarvest> CollectAsync(
            IEnumerable<string> characterNames,
            int maxCharactersInFlight,
            Func<string, Task<CharacterSnapshotPart>> fetchCharacter,
            CancellationToken ct)
        {
            if (fetchCharacter == null)
            {
                throw new ArgumentNullException(nameof(fetchCharacter));
            }

            var names = characterNames == null
                ? new List<string>()
                : new List<string>(characterNames);

            // Written by several tasks at once, one distinct index each, so
            // the fold below can run in list order on a single thread.
            var parts = new CharacterSnapshotPart[names.Count];

            await BoundedConcurrency.ForEachAsync(
                Enumerable.Range(0, names.Count),
                maxCharactersInFlight,
                async index =>
                {
                    try
                    {
                        parts[index] = await fetchCharacter(names[index]);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                        // A null slot is this character reporting nothing,
                        // which the fold reads as a discipline failure.
                        parts[index] = null;
                    }
                },
                ct);

            return Fold(names, parts);
        }

        private static CharacterSnapshotHarvest Fold(List<string> names, CharacterSnapshotPart[] parts)
        {
            var harvest = new CharacterSnapshotHarvest();
            var disciplines = new List<SnapshotCharacterDiscipline>();
            bool degraded = false;

            for (int i = 0; i < names.Count; i++)
            {
                var part = parts[i];
                if (part == null)
                {
                    degraded = true;
                    continue;
                }

                harvest.Items.AddRange(part.Items);
                foreach (int itemId in part.ArmoryItemIds)
                {
                    harvest.ArmoryEquipped.Add(new SnapshotArmoryEquip
                    {
                        ItemId = itemId,
                        CharacterName = names[i],
                    });
                }

                if (part.DisciplinesDegraded)
                {
                    degraded = true;
                }
                else
                {
                    disciplines.AddRange(part.Disciplines);
                }
            }

            harvest.Disciplines = degraded ? null : disciplines;
            return harvest;
        }
    }
}
