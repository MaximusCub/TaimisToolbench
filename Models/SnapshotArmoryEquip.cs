namespace TaimisToolbench.Models
{
    /// <summary>
    /// One character wearing one Legendary Armory item.
    /// <para>
    /// Carries no count on purpose. The armory holds one account-wide copy
    /// and /v2/characters/:id/equipment reports it once per slot per
    /// character, so a count read off these pairings would multiply that
    /// one copy by the number of characters using it. The account's own
    /// /v2/account/legendaryarmory read is what carries the count; this
    /// pairing only names who is wearing it.
    /// </para>
    /// </summary>
    internal class SnapshotArmoryEquip
    {
        public int ItemId { get; set; }

        public string CharacterName { get; set; } = "";
    }
}
