using System;
using System.Collections.Generic;
using Gw2Sharp.WebApi.V2.Models;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// Turns one Gw2Sharp character record into the rows a snapshot stores.
    /// </summary>
    /// <remarks>
    /// Held apart from Gw2AccountSnapshotService so the fetch profiler in
    /// tools/TaimisToolbench.Harness can run the shipped projection rather
    /// than a copy of it, which is what makes its old-shape against
    /// new-shape comparison evidence about this code. Free of Blish HUD for
    /// the same reason.
    /// </remarks>
    internal static class CharacterRecordProjection
    {
        /// <summary>
        /// One character's contribution to a snapshot, projected out of its
        /// record. Null for a null record, which the fold reads as holdings
        /// the fetch could not get, and which is what stops an
        /// under-counted snapshot being committed.
        /// </summary>
        public static CharacterSnapshotPart Build(
            string characterName, Character record)
        {
            if (record == null)
            {
                return null;
            }

            var part = new CharacterSnapshotPart();
            AddBagItems(part, record.Bags, characterName);
            AddEquipmentItems(part, record.Equipment, characterName);
            AddDisciplines(part, record.Crafting, characterName);
            return part;
        }

        // A record with no bags is read as no bags, which is how the narrow
        // inventory endpoint's empty answer was read before it.
        private static void AddBagItems(
            CharacterSnapshotPart part,
            IEnumerable<CharacterInventoryBag> bags,
            string characterName)
        {
            if (bags == null)
            {
                return;
            }

            foreach (var bag in bags)
            {
                if (bag?.Inventory == null)
                {
                    continue;
                }

                foreach (var item in bag.Inventory)
                {
                    if (item == null)
                    {
                        continue;
                    }

                    part.Items.Add(new SnapshotItemEntry
                    {
                        ItemId = item.Id,
                        Count = item.Count,
                        Source = AccountItemIndex.CharacterSourcePrefix + characterName,
                        Upgrades = SocketedIds(item.Upgrades),
                        Infusions = SocketedIds(item.Infusions),
                        SkinId = SkinIdOf(item.Skin),
                    });
                }
            }
        }

        /// <summary>
        /// What this character is wearing, plus what its saved equipment
        /// tabs hold, as one entry per physical item under the
        /// "Equipped:&lt;name&gt;" source, which is not the source its bags
        /// use. Ids drawn from the account-wide Legendary Armory are named
        /// on the part instead, and never counted
        /// (Models.SnapshotArmoryEquip). What is socketed into a slot
        /// becomes a row of its own either way
        /// (Services.SocketedItemRows).
        /// </summary>
        /// <remarks>
        /// The record's equipment block reports each physical item once and
        /// names every tab it sits in, so an item shared by three loadouts
        /// is one entry, not three. It is the same
        /// <see cref="CharacterEquipmentItem"/> the narrow endpoint
        /// returned, field for field. Which store a slot draws from is
        /// Services.EquipmentLocationPolicy's decision.
        /// </remarks>
        private static void AddEquipmentItems(
            CharacterSnapshotPart part,
            IEnumerable<CharacterEquipmentItem> equipment,
            string characterName)
        {
            if (equipment == null)
            {
                return;
            }

            foreach (var item in equipment)
            {
                if (item == null)
                {
                    continue;
                }

                string location = RawLocation(item);
                if (!EquipmentLocationPolicy.IsHeldByCharacter(location))
                {
                    if (item.Id > 0
                        && EquipmentLocationPolicy.IsEquippedFromLegendaryArmory(location))
                    {
                        part.ArmoryItemIds.Add(item.Id);

                        // The wrapper is the armory's to count. What is
                        // fitted into it is not: those are this account's
                        // own items, one per socket.
                        SocketedItemRows.AddFor(
                            part.Items, item.Id, characterName, 1,
                            item.Upgrades, item.Infusions);
                    }

                    continue;
                }

                part.Items.Add(new SnapshotItemEntry
                {
                    ItemId = item.Id,
                    Count = 1,

                    // Worn gear gets its own source encoding so the snapshot
                    // can tell it apart from this same character's bags.
                    Source = AccountItemIndex.CharacterEquipmentSourcePrefix + characterName,
                    Upgrades = SocketedIds(item.Upgrades),
                    Infusions = SocketedIds(item.Infusions),
                    SkinId = SkinIdOf(item.Skin),
                });

                // The lists above stay on the gear row for the tooltip and
                // the rune-set count; these are the same items as rows of
                // their own, which is the only shape search and the plan's
                // owned-stock reader can see.
                SocketedItemRows.AddFor(
                    part.Items, item.Id, characterName, 1,
                    item.Upgrades, item.Infusions);
            }
        }

        /// <summary>
        /// This character's crafting disciplines. A record carrying no
        /// crafting block is degraded rather than empty, which discards the
        /// whole account's discipline list: a partial list would read as an
        /// affirmative "not trained" claim for every character missing from
        /// it.
        /// </summary>
        private static void AddDisciplines(
            CharacterSnapshotPart part,
            IEnumerable<CharacterCraftingDiscipline> crafting,
            string characterName)
        {
            if (crafting == null)
            {
                part.DisciplinesDegraded = true;
                return;
            }

            foreach (var cd in crafting)
            {
                if (cd == null)
                {
                    continue;
                }

                part.Disciplines.Add(new SnapshotCharacterDiscipline
                {
                    CharacterName = characterName,

                    // RawValue preserves the literal API string even for a
                    // discipline Gw2Sharp's enum does not recognize,
                    // matching the plain-string shape
                    // RequiredDiscipline.Discipline uses.
                    Discipline = cd.Discipline?.RawValue ?? "",
                    Rating = cd.Rating,
                    Active = cd.Active,
                });
            }
        }

        private static string RawLocation(CharacterEquipmentItem item)
        {
            var location = item.Location;
            if (location == null)
            {
                return "";
            }

            string raw = location.RawValue;
            return string.IsNullOrEmpty(raw) ? location.Value.ToString() : raw;
        }

        /// <summary>
        /// One stack's socketed item ids in the shape
        /// <see cref="SnapshotItemEntry.Upgrades"/> documents. Gw2Sharp
        /// surfaces the API's omitted field as null; an empty list is
        /// folded to null as well, so only one of the two ever reaches
        /// disk.
        /// </summary>
        public static List<int> SocketedIds(IEnumerable<int> ids)
        {
            if (ids == null)
            {
                return null;
            }

            var copied = new List<int>(ids);
            return copied.Count > 0 ? copied : null;
        }

        /// <summary>
        /// One stack's applied skin as a plain id, or 0 for none. Gw2Sharp
        /// surfaces the API's omitted field as null. Material storage and
        /// the Legendary Armory carry no skin field at all, so their rows
        /// never reach here.
        /// </summary>
        public static int SkinIdOf(int? skin)
        {
            return skin.HasValue && skin.Value > 0 ? skin.Value : 0;
        }
    }
}
