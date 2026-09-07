namespace TaimisToolbench.Services
{
    /// <summary>
    /// The module's two currency-icon sizes, matched to the game's own two
    /// wallet tiers - the currency counterpart of <see cref="ItemIconTiers"/>,
    /// Blish-free so layout math and renderer read the same number.
    /// <para>
    /// The same coin triple appears at BOTH tiers in the game, which is why
    /// two constants exist: the wallet LIST's Coins row draws gold/silver/
    /// copper at 32, and the summary bar redraws that identical run at 18.
    /// </para>
    /// <para>
    /// WHERE THE ICON SITS, measured at both tiers: always to the RIGHT of
    /// the number (the repo's coin invariant, confirmed by the game itself).
    /// A wallet currency's box is centred on the number's INK rather than on
    /// the baseline; a gold, silver or copper coin instead seats its ART on
    /// the number's ink bottom - see <see cref="VerticalAlignmentRule"/>. The
    /// measured gap between the last glyph pixel and the icon box is 5px at
    /// list tier and 3px at bar tier, which CoinSegmentMath.CoinLabelIconGap
    /// approximates. Derivation: docs/ARCHITECTURE.md section S1.3.
    /// </para>
    /// </summary>
    internal static class CurrencyIconTiers
    {
        /// <summary>
        /// TIER 1 - the in-game wallet LIST size (Inventory &gt; All
        /// Currencies, one row per currency): a currency TABLE ROW, where the
        /// icon is the row's subject rather than a unit marker on a number.
        /// The Summary section's currency table and the Snapshot tab's wallet
        /// rows are that table.
        ///
        /// <para>
        /// MEASURED 32: the gold coin's 32x32 texture matched at box
        /// (x329, y178) with error 2.0, sharply minimal. Row geometry from
        /// the same capture: the Coins row band is y173..214 (42px, the list's
        /// uniform row pitch) and the icon box is y178..209, so the icon is
        /// centred with 5px of clearance above and below - the derivation
        /// behind PlanContentHeightMath.CurrencyRowIconPad.
        /// </para>
        /// </summary>
        public const int WalletListIconSize = 32;

        /// <summary>
        /// TIER 2 - the in-game wallet SUMMARY BAR size (the compact inline
        /// runs pinned to the bottom edge of the Inventory window): an inline
        /// gold/silver/copper (or single-currency) run sitting beside a
        /// number inside a cell or a sentence. Every coin run the plan tables,
        /// Ranker, Snapshot header and tooltips draw is this tier - see
        /// CoinSegmentMath.CoinIconSize, which is defined as this constant.
        /// <para>
        /// MEASURED 18, from our bar beside the game's in ONE screenshot, so
        /// the ratio needs no interface scale. The gold disc is 10 pixels
        /// wide in ours and 11 in the game's. The game's read wider at every
        /// threshold tried, by 1.10x to 1.29x. 1.1 of 16 is 17.6, so 18.
        /// </para>
        /// <para>
        /// This supersedes a 16 that template-matched the 32x32 texture at
        /// error 23.9. That fitted the texture to the screen. It never
        /// compared our drawn size against the game's. Do not match it back.
        /// </para>
        /// </summary>
        public const int WalletBarIconSize = 18;

        /// <summary>
        /// Where the icon sits relative to the number it marks. MEASURED at
        /// both tiers: the icon is always to the RIGHT of the number (the
        /// repo's coin invariant, confirmed by the game itself), and a wallet
        /// currency's box is centred on the number's ink rather than sitting
        /// on its baseline - list tier, icon box y178..209 (centre 193.5)
        /// against the "841" glyph ink y188..198 (centre 193.0); bar tier,
        /// box y114..129 against ink y115..126, centred within a pixel.
        /// CoinSegmentMath.InlineIconY computes that seat.
        /// <para>
        /// The coins are the exception:
        /// gold, silver and copper seat their ART on the number's ink bottom
        /// (CoinSegmentMath.CoinIconY), because centring the BOX leaves the
        /// padded art reading high against the digits. Non-coin currencies
        /// were measured centred to within half a pixel in the same capture
        /// and were left alone deliberately.
        /// </para>
        /// </summary>
        public const string VerticalAlignmentRule =
            "icon to the right of the number; a wallet currency's box centred on the " +
            "number's ink, a coin's art seated on the number's ink bottom";
    }
}
