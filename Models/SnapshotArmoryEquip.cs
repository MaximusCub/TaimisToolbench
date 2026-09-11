using System.ComponentModel;
using Newtonsoft.Json;

namespace TaimisToolbench.Models
{
    /// <summary>
    /// One place drawing on one Legendary Armory item: a character wearing
    /// it, or a piece of gear it is socketed into.
    /// <para>
    /// Carries no count on purpose. The armory holds one account-wide copy
    /// and /v2/characters/:id/equipment reports it once per slot per
    /// character, so a count read off these pairings would multiply that
    /// one copy by the number of characters using it. The account's own
    /// /v2/account/legendaryarmory read is what carries the count; this
    /// pairing only names where a copy is drawn.
    /// </para>
    /// </summary>
    internal class SnapshotArmoryEquip
    {
        public int ItemId { get; set; }

        public string CharacterName { get; set; } = "";

        /// <summary>
        /// The socket source key of a piece of gear drawing this armory
        /// item, for a piece nobody is wearing. Empty for a wearer, who is
        /// named by <see cref="CharacterName"/> instead, and empty in a
        /// snapshot.json written before this field existed.
        /// </summary>
        [DefaultValue("")]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Source { get; set; } = "";
    }
}
