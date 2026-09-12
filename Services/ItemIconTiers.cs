namespace TaimisToolbench.Services
{
    /// <summary>
    /// The closed vocabulary of framed-icon sizes: what a call site names
    /// instead of passing a number. Nothing is measured here - every tier
    /// resolves to a constant on <see cref="ItemIconTiers"/> or
    /// <see cref="CurrencyIconTiers"/>, which own the measurements. This is
    /// the vocabulary; those two are the ruler.
    ///
    /// <para>
    /// Four of the six are fixed by an explicit rule (two item tiers, two
    /// currency tiers). The last two are the surfaces no rule covers, named
    /// here rather than left as literals so every icon in the module traces
    /// to a name and a new surface has to pick from this list instead of
    /// inventing a number.
    /// </para>
    /// </summary>
    internal enum ItemIconTier
    {
        /// <summary>
        /// ITEM TIER 1 - in-game bag-slot art: the Snapshot tab's item grid,
        /// the Crafting Plan heading item, the Crafting Ranker's rows, the
        /// Plan History tab's collapsed rows.
        /// </summary>
        BagSlot,

        /// <summary>
        /// ITEM TIER 2 - in-game bag-SIDEBAR art: every row-level icon in
        /// the Crafting Plan tab, and the Plan History tab's expanded
        /// per-item detail lines.
        /// </summary>
        BagSidebar,

        /// <summary>
        /// CURRENCY TIER 1 - in-game wallet LIST art, where the icon is a
        /// table row's subject rather than a unit marker on a number: the
        /// Snapshot tab's wallet rows and the plan Summary's currency table.
        /// </summary>
        CurrencyListRow,

        /// <summary>
        /// CURRENCY TIER 2 - in-game wallet SUMMARY BAR art, for a currency
        /// icon inline beside a number inside a cell. There the icon is a
        /// currency symbol in the coin denominations' role, so it takes no
        /// frame at all (<see cref="IconFrameGeometry.CurrencyIsFramed"/>) -
        /// like the coin runs beside it, which are unframed and go through
        /// CoinCurrencyRenderer.
        /// <para>
        /// A bartered ITEM standing as the unit of a price takes this tier
        /// too - the Required Recipes Cost cell, where five Charms of Skill
        /// is the price the same way 350 Karma is. It keeps the rarity
        /// frame every item icon has, and the frame lands inside the same
        /// measured window, so the run's advance is unchanged.
        /// </para>
        /// </summary>
        CurrencyBarRun,

        /// <summary>
        /// EXEMPT - the item-search suggestion list, whose row height is
        /// fixed by the dropdown it drops out of.
        /// </summary>
        SearchSuggestion,

        /// <summary>
        /// EXEMPT - the rich tooltip's header icon, sized to the game's own
        /// tooltip rather than to the game's bags.
        /// </summary>
        TooltipHeader,
    }

    /// <summary>
    /// The module's two item-icon sizes, matched to the game's own two
    /// inventory tiers, plus the pixel table behind
    /// <see cref="ItemIconTier"/>. Blish-free so the layout math that
    /// reserves room for an icon and the view that draws it read the same
    /// number.
    /// <para>
    /// Art sizes below EXCLUDE the module's own rarity frame, so the frame
    /// lands inside the measured window: 52+2 = 54 against the game's 54-56,
    /// 40+2 = 42 against 39-40 plus its border. That derivation fixes
    /// <see cref="FrameBorder"/> at 1 per side and so fixes
    /// <see cref="FrameSize"/>; what the frame is PAINTED at is
    /// <see cref="PaintedFrameThickness"/>, which is a different number and
    /// says why.
    /// </para>
    /// <para>Measurements: docs/ARCHITECTURE.md section S1.3.</para>
    /// </summary>
    internal static class ItemIconTiers
    {
        /// <summary>
        /// TIER 1 - in-game bag-slot size: the Snapshot tab's item grid,
        /// the Crafting Plan heading item, the Crafting Ranker's rows.
        /// </summary>
        public const int BagSlotIconSize = 52;

        /// <summary>
        /// TIER 2 - in-game bag-SIDEBAR size: the Crafting Plan tab's
        /// row-level icons (recipe tree, Used Materials, Required Recipes,
        /// Shopping List, Crafting Steps). The row heights that carry these
        /// icons are derived from this constant in
        /// PlanContentHeightMath (RowIconFrameSize and the row-height sums
        /// built on it), and the divider-vanishing immunity proof was
        /// re-run at those heights - see LabelHelpers.CreateRowDivider and
        /// the executable re-derivation in
        /// tests/.../RowDividerScissorSimulationTests.cs.
        /// </summary>
        public const int BagSidebarIconSize = 40;

        /// <summary>
        /// The room a frame RESERVES on each side, at every tier. See the
        /// derivation above: the measured art windows already account for
        /// one pixel of module frame on each side. This is the term
        /// <see cref="FrameSize"/> is built from, so every layout that
        /// reserves an icon box reserves the same box it always did.
        /// </summary>
        public const int FrameBorder = 1;

        /// <summary>
        /// The thickness a frame is PAINTED at. 2, not
        /// <see cref="FrameBorder"/>: Blish applies the GW2 UI scale as a
        /// GPU matrix, so a 1px band covers 0.897 physical pixels at UI Size
        /// Normal and rasterizes to no scanline at all on about 10% of the
        /// vertical positions a window can take (40% at UI Size Small). A
        /// reader sees a frame with one edge missing and reads the icon as
        /// cut off. 2px covers at least one scanline at every shipped scale.
        /// <para>
        /// The extra pixel comes out of the ART, not out of the reserved
        /// box, so <see cref="FrameSize"/> and every layout built on it are
        /// unchanged. The sweep that proves it is
        /// tests/TaimisToolbench.Tests/Services/IconFrameScissorSimulationTests.cs.
        /// </para>
        /// </summary>
        public const int PaintedFrameThickness = 2;

        /// <summary>Art size, in logical pixels, of one tier's icon.</summary>
        public static int ArtSize(ItemIconTier tier)
        {
            switch (tier)
            {
                case ItemIconTier.BagSlot: return BagSlotIconSize;
                case ItemIconTier.BagSidebar: return BagSidebarIconSize;

                // The two currency tiers read their measurement off
                // CurrencyIconTiers rather than restating it, so the module
                // has ONE source of truth per measured window. They differ
                // from the item tiers in where the frame sits: an item tier's
                // measured window is the ART (the game draws no frame of its
                // own around a bag slot, so the module's 1px sits outside
                // it), while a currency tier's measured window is the whole
                // BOX - the wallet list's 32px is the icon's footprint in the
                // row. So the art is inset by the frame and the framed box
                // lands exactly on the measurement instead of overflowing it
                // by 2. Both currency call sites already did this arithmetic
                // inline; here it happens once.
                case ItemIconTier.CurrencyListRow:
                    return CurrencyIconTiers.WalletListIconSize - (2 * FrameBorder);
                case ItemIconTier.CurrencyBarRun:
                    return CurrencyIconTiers.WalletBarIconSize - (2 * FrameBorder);

                // The two the rulings do not cover. Their numbers ARE the
                // measurement, because the surface each belongs to is the
                // only thing that sizes them: the suggestion dropdown's 24px
                // row box, and the game tooltip's own header icon.
                //
                // That header icon measures 34x34 physical - 1px of frame
                // around 32px of art - on a capture taken at the "Normal"
                // GW2 UI size, whose 0.897 scale Blish applies to logical
                // coordinates as a GPU matrix. So the logical box to reserve
                // is 34 / 0.897 = 37.9, which 36 of art carries.
                case ItemIconTier.SearchSuggestion: return 22;
                case ItemIconTier.TooltipHeader: return 36;

                // Not a fallback that guesses a size: an unnamed tier is a
                // programming error, and a silent default is how the module
                // grew its icon-size variations in the first place.
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(tier), tier, "No art size is defined for this icon tier.");
            }
        }

        /// <summary>Room one tier's rarity frame reserves on each side. What
        /// it is drawn at is <see cref="PaintedFrameThickness"/>.</summary>
        public static int BorderThickness(ItemIconTier tier)
        {
            // Uniform across tiers today, and asked for by tier rather than
            // read as a constant so a future tier-specific frame becomes a
            // switch here instead of a number at every call site. Routed
            // through ArtSize for its validity check, so an unnamed tier
            // throws rather than quietly answering 1.
            ArtSize(tier);
            return FrameBorder;
        }

        /// <summary>
        /// Overall edge of one tier's framed icon - art plus the frame on
        /// both sides. This is the number layout math reserves, never the
        /// art size alone.
        /// </summary>
        public static int FrameSize(ItemIconTier tier)
        {
            return ArtSize(tier) + (2 * BorderThickness(tier));
        }
    }
}
