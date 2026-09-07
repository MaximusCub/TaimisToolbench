using System.Collections.Generic;
using System.ComponentModel;
using Newtonsoft.Json;

namespace TaimisToolbench.Models
{
    internal class SnapshotItemEntry
    {
        public int ItemId { get; set; }

        public string Name { get; set; } = "";

        public string IconUrl { get; set; } = "";

        // Captured from the SAME /v2/items response Name and IconUrl come
        // from, so it costs no additional request. Schema-additive: a
        // snapshot.json written before this field existed deserializes to
        // the "" initializer, which the rarity policy reads as unknown.
        public string Rarity { get; set; } = "";

        public int Count { get; set; }

        public string Source { get; set; } = "";

        /// <summary>
        /// Item ids of the upgrade components socketed into this stack, or
        /// null when nothing is socketed.
        /// <para>
        /// Null rather than an empty list, so the pair costs no bytes on
        /// the large majority of rows and a snapshot.json written before
        /// these fields existed loads to the value it would have been
        /// written with. /v2/account/materials carries neither field, so
        /// material storage rows are always null.
        /// </para>
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<int> Upgrades { get; set; }

        /// <summary>
        /// Item ids of the infusions socketed into this stack, or null when
        /// none are. Same shape as <see cref="Upgrades"/>.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<int> Infusions { get; set; }

        /// <summary>
        /// The skin applied to this stack, or 0 when it wears its own
        /// look. Held per stack because the game applies a skin per copy:
        /// two copies of one sword can wear different skins.
        /// <para>
        /// /v2/account/materials and /v2/account/legendaryarmory carry no
        /// skin field, so their rows are always 0. Written only when set,
        /// so a snapshot.json from before this field loads to 0.
        /// </para>
        /// </summary>
        [DefaultValue(0)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int SkinId { get; set; }

        /// <summary>
        /// The skin's own name, resolved from /v2/skins at capture time
        /// the way <see cref="Name"/> is resolved from /v2/items. Empty
        /// when no skin is applied, and empty as well when the lookup did
        /// not answer, so a reader never has an id it cannot name.
        /// </summary>
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string SkinName { get; set; } = "";

        /// <summary>
        /// The skin's own icon, from the same /v2/skins response
        /// <see cref="SkinName"/> comes from, so it costs no additional
        /// request. Empty on the same terms. Models.TransmutedSkin says why
        /// a reader must not take one of the two without the other.
        /// </summary>
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string SkinIconUrl { get; set; } = "";
    }
}
