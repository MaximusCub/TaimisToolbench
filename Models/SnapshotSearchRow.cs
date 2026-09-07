using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// One grouped result row for the Snapshot tab's search list
    /// (dev/proposals/d1-snapshot-about-settings.md Feature 1): one row
    /// per matching itemId, with its account-wide total and a per-source
    /// breakdown (ordered via the existing, unmodified
    /// AccountItemIndex.GetPrioritizedSources - see
    /// Services.SnapshotSearchResultBuilder). Replaces the retired
    /// "Aggregate" checkbox's one-row-per-item-per-source rendering with
    /// this as the only, always-on behavior.
    /// </summary>
    internal class SnapshotSearchRow
    {
        public int ItemId { get; set; }

        /// <summary>
        /// What the row calls the item, and the picture it draws. Both are
        /// the skin's when every copy wears the same skin, because that is
        /// what the game shows; see Services.TransmutedNameIndex.
        /// </summary>
        public string Name { get; set; } = "";

        /// <summary>See <see cref="Name"/>. Never the skin's name over the
        /// item's own icon: <see cref="Skin"/> settles both at once.</summary>
        public string IconUrl { get; set; } = "";

        /// <summary>
        /// The skin behind <see cref="Name"/> and <see cref="IconUrl"/>, or
        /// <see cref="TransmutedSkin.None"/> when the row shows the item's
        /// own. The tooltip needs them apart: it draws the skin as the
        /// heading and prints the item's own name under a "Transmuted"
        /// line, the way the game does.
        /// </summary>
        public TransmutedSkin Skin { get; set; } = TransmutedSkin.None;

        /// <summary>
        /// The rarity captured with this item's name and icon, or "" for a
        /// row read out of a snapshot.json written before snapshots carried
        /// rarity. The view resolves it against the session's stat cache
        /// (ItemRarityResolution) rather than reading it raw.
        /// </summary>
        public string Rarity { get; set; } = "";

        public int TotalCount { get; set; }

        /// <summary>
        /// Every place holding some of <see cref="TotalCount"/>, in the
        /// order AccountItemIndex.GetPrioritizedSources produced. Not
        /// display text: Services.SnapshotHoldLine turns the whole list into
        /// one line, because whether a place prints its count depends on the
        /// other places in the same list.
        /// </summary>
        public List<SnapshotHoldLocation> Breakdown { get; set; } = new List<SnapshotHoldLocation>();
    }
}
