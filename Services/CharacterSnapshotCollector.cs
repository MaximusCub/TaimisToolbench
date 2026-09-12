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

        /// <summary>
        /// This character's bags or equipment failed to fetch, so
        /// <see cref="Items"/> is missing holdings rather than empty. The
        /// items still listed are real, so they stay in the harvest, but
        /// the harvest as a whole is then not fit to commit.
        /// </summary>
        public bool ItemsDegraded { get; set; }

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

        /// <summary>How many characters the fetch was asked for.</summary>
        public int CharacterCount { get; set; }

        /// <summary>
        /// How many of those could not be read in full: bags, equipment or
        /// disciplines failed. Their holdings are missing from
        /// <see cref="Items"/>, so a caller that reports this snapshot as
        /// complete is claiming the account owns less than it does.
        /// </summary>
        public int IncompleteCharacterCount { get; set; }

        /// <summary>
        /// Which characters those were, in character-list order. The names
        /// are what a failure has to say out loud: the user sees them in
        /// game, unlike the ids the module keeps to itself.
        /// </summary>
        public List<string> IncompleteCharacterNames { get; } = new List<string>();

        /// <summary>
        /// Every character was read in full. Gw2AccountSnapshotService
        /// refuses to commit a harvest that is not, so the previous
        /// snapshot stays: older and complete beats fresh with holes.
        /// </summary>
        public bool IsComplete
        {
            get { return IncompleteCharacterCount == 0; }
        }
    }

    /// <summary>
    /// Runs the per-character part of a snapshot fetch several characters
    /// at a time, then folds the results back into one result ordered by
    /// the character list.
    /// <para>
    /// A character that fails contributes nothing, which under-counts what
    /// the account owns and never invents a holding. Crafting disciplines
    /// go further and are all-or-nothing: one failed character discards the
    /// whole list, because a partial list reads as an affirmative "not
    /// trained" claim for every character the fetch never reached.
    /// </para>
    /// <para>
    /// Either failure is named in
    /// <see cref="CharacterSnapshotHarvest.IncompleteCharacterNames"/>. An
    /// under-count is conservative for cost and wrong for advice: it makes
    /// the plan tell the user to buy an item their own bags hold. So the
    /// caller refuses the whole harvest rather than commit one.
    /// </para>
    /// </summary>
    internal static class CharacterSnapshotCollector
    {
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
            int incomplete = 0;
            harvest.CharacterCount = names.Count;

            for (int i = 0; i < names.Count; i++)
            {
                var part = parts[i];
                if (part == null)
                {
                    degraded = true;
                    incomplete++;
                    harvest.IncompleteCharacterNames.Add(names[i]);
                    continue;
                }

                if (part.ItemsDegraded || part.DisciplinesDegraded)
                {
                    incomplete++;
                    harvest.IncompleteCharacterNames.Add(names[i]);
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
            harvest.IncompleteCharacterCount = incomplete;
            return harvest;
        }
    }
}
