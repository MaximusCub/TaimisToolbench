using System;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// What /v2/characters/:id/equipment's "location" field says about one
    /// slot, decided on the literal wire string.
    /// <para>
    /// The endpoint uses four values. "Equipped" is worn gear the character
    /// owns; "Armory" is gear it owns sitting in a saved equipment
    /// template. The other two are an account-wide Legendary Armory copy
    /// the slot draws from: "EquippedFromLegendaryArmory" is worn right
    /// now, "LegendaryArmory" sits in a saved template the character is not
    /// wearing.
    /// </para>
    /// <para>
    /// A value none of the four match is neither held nor worn, so an
    /// endpoint that grows a fifth reports nothing rather than something
    /// wrong. Blish-free so the decision is testable on its own.
    /// </para>
    /// </summary>
    internal static class EquipmentLocationPolicy
    {
        /// <summary>
        /// Whether the slot holds a copy the character owns, and so a copy
        /// the snapshot may count. An armory copy is reported once per slot
        /// per character, so counting one would multiply a single legendary
        /// by the number of slots using it.
        /// </summary>
        public static bool IsHeldByCharacter(string rawLocation)
        {
            return Is(rawLocation, "Equipped") || Is(rawLocation, "Armory");
        }

        /// <summary>
        /// Whether the character is wearing an account-wide Legendary
        /// Armory copy in this slot right now. A saved template it is not
        /// wearing answers false: the reader asked who has the item
        /// equipped.
        /// </summary>
        public static bool IsEquippedFromLegendaryArmory(string rawLocation)
        {
            return Is(rawLocation, "EquippedFromLegendaryArmory");
        }

        private static bool Is(string rawLocation, string value)
        {
            return string.Equals(rawLocation, value, StringComparison.OrdinalIgnoreCase);
        }
    }
}
