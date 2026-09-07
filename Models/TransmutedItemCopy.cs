namespace TaimisToolbench.Models
{
    /// <summary>
    /// One stack of an item, as the place holding it and the skin it wears.
    /// <para>
    /// A Snapshot row sums many of these, and the source filter can hide
    /// some of them. The skin a row shows, and the skin names that find it,
    /// therefore depend on which stacks the row counted, so neither can be
    /// worked out once per snapshot. Built by
    /// Services.TransmutedNameIndex, which owns the rule.
    /// </para>
    /// </summary>
    internal sealed class TransmutedItemCopy
    {
        internal TransmutedItemCopy(string source, TransmutedSkin skin)
        {
            Source = source ?? "";
            Skin = skin ?? TransmutedSkin.None;
        }

        /// <summary>
        /// The raw AccountItemIndex source key holding this stack. The
        /// caller matches it against the same source filter the row's own
        /// breakdown was built through.
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// What this stack wears, or <see cref="TransmutedSkin.None"/> when
        /// it wears the item's own look.
        /// </summary>
        public TransmutedSkin Skin { get; }
    }
}
