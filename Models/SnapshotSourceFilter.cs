using System;
using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// Which account-inventory sources the Snapshot tab's search/filter row
    /// should include (dev/proposals/d1-snapshot-about-settings.md Feature
    /// 1). The four storage locations default to true (show everything),
    /// matching the pre-search-box tab's implicit no-filter behavior.
    /// <para>
    /// Characters are carried as an EXCLUSION set of bare character names (no
    /// "Character:" encoding prefix). Exclusion rather than inclusion is what
    /// makes a character absent from the set VISIBLE, so a character that
    /// appears in a fresh snapshot defaults to shown without this type ever
    /// needing to know the account's roster. Ordinal comparison: the names
    /// are the same strings AccountItemIndex encodes its source keys from.
    /// </para>
    /// <para>
    /// A plain data carrier with no behavior of its own - matching it against
    /// a raw AccountItemIndex source string lives in
    /// Services.SnapshotSearchResultBuilder, keeping this Models type free of
    /// a Services-namespace dependency.
    /// </para>
    /// </summary>
    internal class SnapshotSourceFilter
    {
        public bool Bank { get; set; } = true;

        public bool MaterialStorage { get; set; } = true;

        public bool SharedInventory { get; set; } = true;

        public bool LegendaryArmory { get; set; } = true;

        public HashSet<string> UncheckedCharacters { get; set; } = new HashSet<string>(StringComparer.Ordinal);
    }
}
