/*
 * The first block of the default table below - the one headed "Adapted
 * from gw2efficiency" - is adapted from @gw2efficiency/recipe-calculation,
 * src/static/currencyDecisionPrices.ts -
 * https://github.com/gw2efficiency/recipe-calculation
 * License: MIT, Copyright (c) 2016 queicherius (David Reess).
 * The MIT permission notice is reproduced verbatim below, as the license
 * requires for substantial portions of the work. The second block,
 * "Derived here", is this repository's own work under its own stated rule
 * and carries no upstream claim; nothing outside the first block is
 * gw2efficiency's.
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
 * DEALINGS IN THE SOFTWARE.
 */

using System.Collections.Generic;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// Curated default DECISION-ONLY currency valuations (copper per unit).
    /// Every key is the official GW2 wallet currency id, cross-checked
    /// against the live API. Two provenance blocks below, kept apart and
    /// never interleaved: values adapted from gw2efficiency's
    /// CURRENCY_DECISION_PRICES, and values derived here under the rule
    /// stated on that block. A currency in neither stays unvalued rather
    /// than gaining an invented rate.
    /// <para>
    /// DECISION-ONLY: a value here may tip a comparison, and may weight a
    /// ratio the module prints as a percentage, but must never be folded
    /// into any displayed gold total. CurrencyValuation carries the
    /// user-override/cleared/default precedence. Shipping this curated
    /// table is a one-time waiver of the repo's "do not invent data" rule
    /// for this table only: every value is sourced and attributed upstream,
    /// under the licence at the top of this file, or derived from live GW2
    /// API data. Derivations, the currencies deliberately left unvalued,
    /// and where a decision value may appear: docs/ARCHITECTURE.md 8.3.
    /// </para>
    /// </summary>
    internal static class CurrencyDecisionDefaults
    {
        public static readonly IReadOnlyDictionary<int, long> DefaultCopperPerUnit = new Dictionary<int, long>
        {
            // Adapted from gw2efficiency - every entry down to id 69 is
            // theirs, under the licence at the top of this file. Their table
            // stops at id 70 and marks some ids inside this range
            // `undefined`; an `undefined` id is simply absent from this
            // block, and appears below only if this repository derived a
            // value for it independently.
            { 2, 1 },        // Karma
            { 3, 3500 },     // Laurel
            { 4, 3000 },     // Gem
            { 5, 32 },       // Ascalonian Tear
            { 6, 32 },       // Shard of Zhaitan
            { 7, 80 },       // Fractal Relic
            { 9, 32 },       // Seal of Beetletun
            { 10, 32 },      // Manifesto of the Moletariate
            { 11, 32 },      // Deadly Bloom
            { 12, 32 },      // Symbol of Koda
            { 13, 32 },      // Flame Legion Charr Carving
            { 14, 32 },      // Knowledge Crystal
            { 15, 23 },      // Badge of Honor
            { 16, 3600 },    // Guild Commendation
            { 19, 70 },      // Airship Part
            { 20, 70 },      // Ley Line Crystal
            { 22, 70 },      // Lump of Aurillium
            { 23, 3600 },    // Spirit Shard
            { 24, 1200 },    // Pristine Fractal Relic (15 * 80 in gw2e's own source)
            { 25, 100 },     // Geode
            { 26, 800 },     // WvW Skirmish Claim Ticket
            { 27, 45 },      // Bandit Crest
            { 28, 3600 },    // Magnetite Shard
            { 29, 3600 },    // Provisioner Token
            { 31, 50 },      // Proof of Heroics
            { 32, 25 },      // Unbound Magic
            { 33, 1600 },    // Ascended Shards of Glory
            { 34, 9 },       // Trade Contract
            { 35, 720 },     // Elegy Mosaic
            { 36, 135 },     // Testimony of Desert Heroics
            // Upstream values id 39 (Gaeting Crystal); it is dropped here on
            // purpose - retired in-game. docs/ARCHITECTURE.md section 8.3.
            { 45, 50 },      // Volatile Magic
            { 50, 25 },      // Festival Token
            { 53, 3500 },    // Green Prophet Shard
            { 57, 300 },     // Blue Prophet Shard
            { 60, 310 },     // Tyrian Defense Seal
            { 61, 200 },     // Research Note
            { 62, 100 },     // Unusual Coin
            { 64, 35 },      // Jade Sliver
            { 65, 135 },     // Testimony of Jade Heroics
            { 67, 35 },      // Canach Coins
            { 68, 320 },     // Imperial Favor
            { 69, 32 },       // Tales of Dungeon Delving

            // Derived here, NOT gw2efficiency's work: no upstream row exists
            // for any of these. One rule, applied per entry: the most coin a
            // unit converts into through an UNCAPPED vendor offer whose cost
            // is this currency (plus at most a minor coin component), priced
            // at the live trading-post sell listing - or, where the game
            // itself sells the same goods at the same counts for an
            // already-valued sibling currency, that sibling's value.
            // Trading-post figures are snapshots (api.guildwars2.com,
            // 2026-08-29), so they age; the sibling figures do not. Erring
            // high is the safe direction here: an over-valued currency can
            // lose a comparison it should have won, never win one it should
            // have lost. Per-entry working: docs/ARCHITECTURE.md section 8.3.
            { 30, 3770 },    // PvP League Ticket: League Vendor sells 10 Shard of Glory for 1 (TP sell 377 each).
            { 66, 197 },     // Ancient Coin: Chin-Hwa sells Recipe: Harrier's Monastery Shoes for 5 (TP sell 987).
            { 76, 125 },     // Ursus Oblige: Maw of the Volcano sells Potent Standard Sharpening Stone for 7 + 120c (TP sell 995).
            { 77, 3600 },    // Gaeting Crystal, the current expansion's raid currency: its only live exchange sells 1 Magnetite Shard for 1, and its historical tables charged what 28 now charges.
            { 82, 135 },     // Testimony of Castoran Heroics: 1 for 1 with Testimony of Desert and Jade Heroics (36, 65) at the Notary.
        };

        /// <summary>
        /// Looks up the curated default copper-per-unit value of
        /// <paramref name="currencyId"/>. Returns false for the coin
        /// currency id and for any currency neither block above values -
        /// the same "no value" outcome either way, matching
        /// CurrencyValuation.TryGetCopperValue's own no-value contract.
        /// </summary>
        public static bool TryGetDefault(int currencyId, out long copperPerUnit)
        {
            return DefaultCopperPerUnit.TryGetValue(currencyId, out copperPerUnit);
        }
    }
}
