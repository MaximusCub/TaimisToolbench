using System.Collections.Generic;
using Newtonsoft.Json;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// Which question the Crafting Ranker's table answers. Internal (with
    /// the persisted property below carrying an explicit JsonProperty) so
    /// the mode does not widen the module's pinned public surface.
    /// </summary>
    internal enum RankerMode
    {
        /// <summary>
        /// "Given my order, how close is each item?" - every row is measured
        /// after the rows above it claim materials, currencies, coin and
        /// daily crafts (RankerPriorityCascade).
        /// </summary>
        Cascade = 0,

        /// <summary>
        /// "Which is closest to done right now?" - every row is measured
        /// against the full account, ignoring the other rows, and the table
        /// displays by readiness. The stored priority order is untouched.
        /// </summary>
        Independent = 1,
    }

    /// <summary>
    /// One item on the Crafting Ranker's priority list. Name/IconUrl/Rarity
    /// are denormalized for the same reason SnapshotItemEntry duplicates
    /// them: the list renders before any solve has run, with no metadata
    /// round trip available.
    /// </summary>
    public class RankerWatchlistEntry
    {
        public int ItemId { get; set; }

        public int Quantity { get; set; }

        public string Name { get; set; }

        public string IconUrl { get; set; }

        /// <summary>GW2 API rarity string, for the icon frame colour; null = unknown.</summary>
        public string Rarity { get; set; }
    }

    /// <summary>The whole ranker.json payload.</summary>
    public class RankerWatchlist
    {
        /// <summary>The version <see cref="Services.RankerStore"/>.Save stamps.</summary>
        public const int CurrentSchemaVersion = 1;

        /// <summary>
        /// The OLDEST version this build still reads. A file anywhere in
        /// [this, <see cref="CurrentSchemaVersion"/>] keeps its rows and is
        /// restamped by the next Save; anything outside it is discarded.
        /// <para>
        /// Raising it is the single act that silently empties a user's
        /// priority list. Only version 1 has ever shipped, so the range
        /// accepts exactly what the old equality did; the constant exists
        /// so the NEXT bump has to decide, rather than emptying the list as
        /// a side effect. Move it only for a bump that renames or retypes a
        /// member of this graph - an addition leaves an absent member at
        /// its default and every existing file still loads. Unlike
        /// PlanHistoryIndex there is no shape hash holding the graph to
        /// that, so the decision is the bumping author's to make.
        /// </para>
        /// </summary>
        public const int MinimumReadableSchemaVersion = 1;

        // No property initializer: a file that omits the field must
        // deserialize to 0 and be rejected, exactly as PersistedPlan does.
        public int SchemaVersion { get; set; }

        /// <summary>
        /// LIST ORDER IS THE PRIORITY ORDER. Index 0 is highest priority and
        /// has first claim on the account's materials, currencies, coin and
        /// daily crafts (see RankerPriorityCascade). There is deliberately no
        /// stored rank field: a rank int can drift out of sync with the
        /// list's actual order, and the list is what renders and what the
        /// cascade walks.
        /// </summary>
        public List<RankerWatchlistEntry> Entries { get; set; } = new List<RankerWatchlistEntry>();

        /// <summary>
        /// The selected comparison mode. Additive: a file written before the
        /// field existed deserializes to Cascade, the original behaviour, so
        /// no schema bump and no list loss. JsonProperty is load-bearing -
        /// the property is internal (see RankerMode) and Json.NET skips
        /// non-public members without it.
        /// </summary>
        [JsonProperty]
        internal RankerMode Mode { get; set; }

        /// <summary>
        /// Whether every row draws the category strip - materials,
        /// currencies, time gates, disciplines, recipes - and the notes that
        /// explain it. Off by default, as <see cref="ShowCurrencies"/> is:
        /// the headline row is the comparison and everything under it is the
        /// explanation, so the table opens on twenty rows of comparison
        /// rather than five rows of essay.
        /// </summary>
        public bool ShowCategories { get; set; }

        /// <summary>
        /// Whether every row lists the currencies it is still short of.
        /// Independent of <see cref="ShowCategories"/>: the currency list
        /// answers "which currency, and how much", which the Currencies
        /// category percentage alone cannot.
        /// </summary>
        public bool ShowCurrencies { get; set; }
    }
}
