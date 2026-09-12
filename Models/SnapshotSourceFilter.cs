using System;
using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// Which account-inventory sources the Snapshot tab's search should
    /// include. Two independent sets. The seven location booleans say which
    /// places to show. The character set says whose bags, worn gear and
    /// equipment templates to show.
    /// <para>
    /// The two AND together, and each only narrows what it is about. Bank,
    /// Material Storage, Shared Inventory and Legendary Armory carry no
    /// character name, so the character set cannot hide them. Every
    /// boolean defaults to true, which shows everything.
    /// </para>
    /// <para>
    /// Characters are an EXCLUSION set of bare names, so a character absent
    /// from it is VISIBLE and a new one needs no roster lookup here.
    /// Ordinal: the names are the strings AccountItemIndex builds its source
    /// keys from. Matching a key against this carrier lives in
    /// Services.SnapshotSearchResultBuilder, which keeps this type free of a
    /// Services dependency.
    /// </para>
    /// </summary>
    internal class SnapshotSourceFilter
    {
        public bool Bank { get; set; } = true;

        public bool MaterialStorage { get; set; } = true;

        public bool SharedInventory { get; set; } = true;

        public bool LegendaryArmory { get; set; } = true;

        /// <summary>Items in a character's bags.</summary>
        public bool Bags { get; set; } = true;

        /// <summary>Gear a character is wearing right now.</summary>
        public bool Equipped { get; set; } = true;

        /// <summary>Gear parked in a character's saved equipment
        /// templates, other than the one it is wearing.</summary>
        public bool EquipmentTemplates { get; set; } = true;

        public HashSet<string> UncheckedCharacters { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    }
}
