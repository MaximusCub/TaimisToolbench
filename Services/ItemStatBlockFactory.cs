using System;
using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// RawItem (+ its optional details block) -> <see cref="ItemStatBlock"/>.
    /// Every decision about what an absent field MEANS is made here, once, so
    /// the composer downstream only ever renders facts:
    /// <list type="bullet">
    /// <item><description>A null details block is the crafting-material case
    /// (measured on 19700/19685/46683), not an error - the block still
    /// carries name, rarity, type, level, vendor value and flavour.</description></item>
    /// <item><description>A weapon's <c>defense: 0</c> is "no defense", not
    /// "0 defense": every weapon in the API reports it. Same for a 0-value
    /// attribute modifier.</description></item>
    /// <item><description><c>NoSell</c> suppresses the vendor value
    /// entirely, which is what the game itself shows for those
    /// items.</description></item>
    /// </list>
    /// Stat-SELECTABLE items (non-empty stat_choices) record only how many
    /// combinations exist; nothing is guessed here. See
    /// docs/ARCHITECTURE.md section S1.4.
    /// </summary>
    internal static class ItemStatBlockFactory
    {
        private static readonly IReadOnlyList<ItemAttributeLine> NoAttributes = new List<ItemAttributeLine>();
        private static readonly IReadOnlyList<string> NoStrings = new List<string>();
        private static readonly IReadOnlyList<ItemSlotKind> NoSlots = new List<ItemSlotKind>();

        public static ItemStatBlock Build(RawItem raw)
        {
            if (raw == null)
            {
                return null;
            }

            var flags = new HashSet<string>(raw.Flags ?? new List<string>(), StringComparer.OrdinalIgnoreCase);

            var block = new ItemStatBlock
            {
                ItemId = raw.Id,
                Name = raw.Name ?? "",
                Rarity = raw.Rarity,
                IconUrl = raw.Icon,
                ItemType = raw.ItemType,
                RequiredLevel = raw.Level,
                Restrictions = raw.Restrictions ?? NoStrings,
                Bindings = ResolveBindings(flags),
                IsUnique = flags.Contains("Unique"),
                VendorValue = ResolveVendorValue(raw.VendorValue, flags),
                Description = raw.Description ?? "",
                Attributes = NoAttributes,
                UpgradeBonuses = NoStrings,
                UnusedSlots = NoSlots,
            };

            var detail = raw.Detail;
            if (detail == null)
            {
                return block;
            }

            block.SubType = detail.SubType;
            block.WeightClass = detail.WeightClass;
            block.DamageType = detail.DamageType;
            block.UnusedSlots = ResolveUnusedSlots(raw, detail, flags);
            block.BuffDescription = detail.BuffDescription;
            block.StatChoiceCount = detail.StatChoiceIds == null ? 0 : detail.StatChoiceIds.Count;
            block.NourishmentDurationMs = detail.NourishmentDurationMs;
            block.EffectName = detail.EffectName;
            block.EffectIconUrl = detail.EffectIconUrl;

            if (detail.Defense.HasValue && detail.Defense.Value > 0)
            {
                block.Defense = detail.Defense;
            }

            if (detail.MinPower.HasValue && detail.MaxPower.HasValue &&
                detail.MaxPower.Value > 0)
            {
                block.MinPower = detail.MinPower;
                block.MaxPower = detail.MaxPower;
            }

            if (detail.InfixAttributes != null && detail.InfixAttributes.Count > 0)
            {
                var attributes = new List<ItemAttributeLine>(detail.InfixAttributes.Count);
                foreach (var attribute in detail.InfixAttributes)
                {
                    if (attribute == null || attribute.Modifier == 0)
                    {
                        continue;
                    }

                    attributes.Add(new ItemAttributeLine(
                        ItemStatMath.AttributeDisplayName(attribute.Attribute), attribute.Modifier));
                }

                if (attributes.Count > 0)
                {
                    block.Attributes = attributes;
                }
            }

            if (detail.Bonuses != null && detail.Bonuses.Count > 0)
            {
                block.UpgradeBonuses = detail.Bonuses;
            }

            if (!string.IsNullOrEmpty(detail.NourishmentDescription))
            {
                block.NourishmentDescription =
                    ItemDescriptionSanitizer.Sanitize(detail.NourishmentDescription);
            }

            return block;
        }

        /// <summary>
        /// The slots the definition leaves EMPTY, in the game's own order:
        /// upgrade slots first, then infusion and enrichment slots
        /// (measured on a live ascended-staff tooltip, which lists two
        /// upgrade lines above its two infusion lines).
        /// <para>
        /// Both kinds net off what the definition already ships socketed -
        /// its suffix items, and any infusion slot carrying an item_id -
        /// because the game prints the contents of a filled slot instead of
        /// an unused-slot line.
        /// </para>
        /// </summary>
        private static IReadOnlyList<ItemSlotKind> ResolveUnusedSlots(
            RawItem raw, RawItemDetail detail, HashSet<string> flags)
        {
            int upgradeSlots = ItemSlotFacts.UpgradeSlotCount(
                raw.ItemType, detail.SubType, flags.Contains("NotUpgradeable"));
            int unusedUpgrades = upgradeSlots - detail.SocketedUpgradeCount;
            var slots = new List<ItemSlotKind>();
            for (int i = 0; i < unusedUpgrades; i++)
            {
                slots.Add(ItemSlotKind.Upgrade);
            }

            foreach (var slot in detail.InfusionSlots ?? new List<RawInfusionSlot>())
            {
                if (slot != null && !slot.IsFilled)
                {
                    slots.Add(ItemSlotFacts.InfusionSlotKind(slot.Flags));
                }
            }

            return slots.Count == 0 ? NoSlots : slots;
        }

        /// <summary>
        /// The binding lines the game shows, in its order: the account
        /// dimension, then the soul dimension. The two are INDEPENDENT, not a
        /// most-specific ladder - one item can carry AccountBound and
        /// SoulBindOnUse and show both lines stacked. Within a dimension the
        /// stronger flag wins: AccountBound over AccountBindOnUse,
        /// SoulbindOnAcquire over SoulBindOnUse.
        /// <para>
        /// A bind-ON-ACQUIRE flag reads BARE - "Account Bound", "Soulbound" -
        /// because that is what the game prints on the ordinary inventory
        /// hover these tooltips stand in for. A bind-on-USE flag keeps its
        /// "on Use" tail: the binding has not happened yet for any copy.
        /// Which copy the player is looking at is instance state /v2/items
        /// cannot carry, so the wording cannot be resolved per copy.
        /// Measurements: docs/ARCHITECTURE.md section S1.4.
        /// </para>
        /// </summary>
        private static IReadOnlyList<string> ResolveBindings(HashSet<string> flags)
        {
            List<string> lines = null;

            string account =
                flags.Contains("AccountBound") ? "Account Bound" :
                flags.Contains("AccountBindOnUse") ? "Account Bound on Use" : null;
            string soul =
                flags.Contains("SoulbindOnAcquire") ? "Soulbound" :
                flags.Contains("SoulBindOnUse") ? "Soulbound on Use" : null;

            if (account != null)
            {
                (lines = new List<string>(2)).Add(account);
            }

            if (soul != null)
            {
                (lines = lines ?? new List<string>(1)).Add(soul);
            }

            return lines ?? (IReadOnlyList<string>)NoStrings;
        }

        private static long? ResolveVendorValue(int vendorValue, HashSet<string> flags)
        {
            if (vendorValue <= 0 || flags.Contains("NoSell"))
            {
                return null;
            }

            return vendorValue;
        }
    }
}
