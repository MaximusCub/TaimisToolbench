using System;
using System.Collections.Generic;
using System.Text;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// An <see cref="ItemStatBlock"/> rendered as tooltip content, in the
    /// line order the in-game item tooltip uses (KNOWN-ISSUES #42,
    /// fidelity-audit live3 addendum): the icon+name header, what the item
    /// DOES (strength/defense, attributes, granted bonuses, or a
    /// consumable's use prompt and effect block), then - on materials,
    /// trophies and consumables - the description as its own block, the
    /// slot lines, the identity block (rarity word where the game
    /// shows one, type, level, equipment's description and flavour, the
    /// binding lines), and last of all, unlabelled and contiguous, the
    /// vendor value.
    ///
    /// <para>
    /// Blish-free, so the whole line-by-line contract is directly testable
    /// (repo invariant, same precedent as TreeRowTooltipComposer and
    /// ValueDetailTooltipBuilder). Nothing here decides what an absent
    /// field means - <see cref="ItemStatBlockFactory"/> already did, and
    /// this class only ever asks "is it present".
    /// </para>
    /// </summary>
    internal static class ItemStatTooltipComposer
    {
        private static readonly IReadOnlyList<ItemAttributeLine> EmptyAttributes = new List<ItemAttributeLine>();
        private static readonly IReadOnlyList<string> EmptyStrings = new List<string>();
        private static readonly IReadOnlyList<ItemSlotKind> EmptySlots = new List<ItemSlotKind>();

        /// <summary>The game's own description string on a piece of gear
        /// whose stats have not been chosen yet (KNOWN-ISSUES #42, gap
        /// G12). Emitted verbatim rather than approximated.</summary>
        private const string SelectStatsPrompt = "Double-click to select stats.";

        /// <summary>The game's use line on a consumable that carries
        /// effect data - measured verbatim on live3 soul-pastries (89002,
        /// Food), candy-corn (36041, Generic) and omnomberry (12452, Food),
        /// 2026-08-26. Not in the API; the string is the game's own, same
        /// precedent as <see cref="SelectStatsPrompt"/>.</summary>
        private const string ConsumePrompt = "Double-click to consume.";

        /// <summary>The game's own word for an item wearing another item's
        /// skin, read off the capture BuildTransmutedBlock cites.</summary>
        private const string TransmutedLabel = "Transmuted";

        public static TooltipContent BuildContent(ItemStatBlock stats)
        {
            return BuildContent(stats, SocketedUpgradeView.None);
        }

        /// <summary>
        /// The same tooltip for a stack the account actually holds, whose
        /// socketed components are known. They render where the game
        /// renders them - between what the item does and its identity
        /// block, in place of the anonymous slot lines
        /// <see cref="BuildSlots"/> emits when the contents are
        /// unknown.
        /// </summary>
        public static TooltipContent BuildContent(ItemStatBlock stats, SocketedUpgradeView sockets)
        {
            return BuildContent(stats, sockets, TransmutedSkin.None);
        }

        /// <summary>
        /// The same tooltip for a stack wearing a skin.
        /// <paramref name="skin"/> takes the heading, name and icon
        /// together, because that is what the game shows there. The item's
        /// own name moves down to the "Transmuted" block. Pass
        /// <see cref="TransmutedSkin.None"/> for a stack wearing its own
        /// look.
        /// </summary>
        public static TooltipContent BuildContent(
            ItemStatBlock stats, SocketedUpgradeView sockets, TransmutedSkin skin)
        {
            if (stats == null)
            {
                return TooltipContent.Empty;
            }

            string ownName = string.IsNullOrEmpty(stats.Name) ? "Unknown Item" : stats.Name;
            var worn = skin ?? TransmutedSkin.None;
            bool transmuted = worn.IsPresent
                && !string.Equals(worn.Name, ownName, StringComparison.Ordinal);

            // The icon+name header row every in-game item tooltip opens
            // with (gap G11). The standing comment claiming the game shows
            // no icon was simply wrong - all three wiki captures show one.
            // Name and icon are read off the same value, so the heading
            // cannot name one item and draw another.
            var builder = new TooltipContentBuilder();
            builder.Header(
                transmuted ? worn.IconUrl : stats.IconUrl,
                transmuted ? worn.Name : ownName,
                TooltipHeaderSubject.ItemOfRarity(stats.Rarity));

            var transmutedBlock = BuildTransmutedBlock(transmuted ? ownName : null);

            var facts = BuildFacts(stats, out bool bodyOpensUnderHeader);

            if (DescriptionLeadsBody(stats))
            {
                // Materials, trophies and consumables run EVERYTHING
                // contiguous under the header - the description (fury,
                // eyes, almonds, heart, ticket), the consume prompt and
                // effect block (soul-pastries, omnomberry, candy-corn),
                // and even a bare identity block (red-festival-lantern's
                // "Trophy" starts one pitch under its icon) - with exactly
                // ONE blank in the whole shape: after the description
                // block, before the type line (candy-corn: effect block ->
                // description contiguous, then a blank before
                // "Consumable"; fury/heart/eyes: description then a blank
                // before what follows). All measured on live3, 2026-08-26.
                var description = new TooltipContentBuilder();
                AppendDescription(description, stats);
                var descriptionBlock = description.Build();
                var identity = BuildIdentityBlock(stats, includeDescription: false);

                builder.Append(facts);
                builder.Append(descriptionBlock);
                if (!transmutedBlock.IsEmpty)
                {
                    builder.Separator();
                    builder.Append(transmutedBlock);
                }

                if (!identity.IsEmpty)
                {
                    if (!descriptionBlock.IsEmpty || !transmutedBlock.IsEmpty)
                    {
                        builder.Separator();
                    }

                    builder.Append(identity);
                }

                builder.Append(BuildVendorValue(stats));
                return builder.Build();
            }

            // Blocks are collected first and joined with exactly one blank
            // line each, so an empty block never leaves a separator behind
            // and a name-only block never ends on a stray blank row.
            var blocks = new List<TooltipContent>(4);
            AddBlock(blocks, facts);
            AddBlock(blocks, BuildSlots(stats, sockets));
            AddBlock(blocks, transmutedBlock);
            AddBlock(blocks, BuildIdentityBlock(stats, includeDescription: true));

            for (int i = 0; i < blocks.Count; i++)
            {
                // Measured, and it splits on what the body OPENS with, not
                // on combat facts alone. A body that opens with the item's
                // combat facts or its effect block runs straight on under
                // the header (steak.png 37 -> 39). One that opens with the
                // identity block gets a blank first (xyaren.png: icon
                // bottom y=34, first text band y=53), and so does an
                // upgrade component's bonus run (FWDekker; no
                // unequipped-rune capture exists). Gap G15.
                if (i > 0 || !bodyOpensUnderHeader)
                {
                    builder.Separator();
                }

                builder.Append(blocks[i]);
            }

            var value = BuildVendorValue(stats);
            if (!value.IsEmpty)
            {
                // The value ALWAYS runs contiguous under the line above it.
                // Measured on every capture that shows one: steak.png
                // (2012), and live3 vials (discipline line -> coin row at
                // one 16px pitch), fury-scorched, red-festival-lantern,
                // counterfeit-ticket, sigil-rage and relic-livingcity
                // (2026-08-26). The old Generic-shape blank came from
                // FWDekker's <br /> before getValue(), which the vials
                // capture - a crafting material, exactly Generic's case -
                // refutes; no capture anywhere shows a blank above a value.
                // The header rule still owns the case where the value is
                // the whole body.
                if (blocks.Count == 0 && !bodyOpensUnderHeader)
                {
                    builder.Separator();
                }

                builder.Append(value);
            }

            return builder.Build();
        }

        /// <summary>
        /// The types whose description leads the body instead of sitting
        /// inside the identity block. Measured on live3 (2026-08-26):
        /// CraftingMaterial (eyes-of-kormir, almonds - "Ingredient" IS
        /// 12337's description field), Trophy (fury-scorched,
        /// heart-of-destroyer, counterfeit-ticket) and Consumable
        /// (candy-corn, description after the effect block). FWDekker's
        /// Generic/Trophy/Consumable builders agree. Other non-equipment
        /// types (Container, Gizmo, MiniPet, Gathering) look the same in
        /// the replica but have no capture, so they keep the module's
        /// one-shape identity placement until one exists.
        /// </summary>
        private static bool DescriptionLeadsBody(ItemStatBlock stats)
        {
            switch (stats.ItemType)
            {
                case "CraftingMaterial":
                case "Trophy":
                case "Consumable":
                    return true;
                default:
                    return false;
            }
        }

        private static void AddBlock(List<TooltipContent> blocks, TooltipContent block)
        {
            if (!block.IsEmpty)
            {
                blocks.Add(block);
            }
        }

        /// <summary>
        /// What the item does: weapon strength / defense / attributes, then
        /// the granted bonuses, or a consumable's use prompt and effect
        /// block, sitting contiguously under the header.
        /// <para>
        /// <paramref name="bodyOpensUnderHeader"/> reports whether this
        /// block leads with content the game runs straight on under the
        /// header - the combat facts, or the consume prompt / effect block
        /// - as opposed to a bonus run, which the game breaks before. It
        /// decides the header's blank line; see <see cref="BuildContent"/>.
        /// </para>
        /// </summary>
        private static TooltipContent BuildFacts(ItemStatBlock stats, out bool bodyOpensUnderHeader)
        {
            var facts = new TooltipContentBuilder();
            bool buffAlreadyShown = false;
            bool hasCombatFacts = false;

            if (stats.MinPower.HasValue && stats.MaxPower.HasValue)
            {
                facts.Text($"Weapon Strength: {FormatCount(stats.MinPower.Value)} - " +
                    FormatCount(stats.MaxPower.Value)).EndLine();
                hasCombatFacts = true;
            }

            if (stats.Defense.HasValue)
            {
                facts.Text($"Defense: {FormatCount(stats.Defense.Value)}").EndLine();
                hasCombatFacts = true;
            }

            // Null-tolerant even though ItemStatBlockFactory never leaves
            // these null: ItemStatBlock has public setters, so a future or
            // test-built block might.
            string buffFlatText = ItemDescriptionSanitizer.Sanitize(stats.BuffDescription);
            foreach (var attribute in stats.Attributes ?? EmptyAttributes)
            {
                // The API's modifier is already signed for the one
                // attribute type that can be negative; a positive one needs
                // its "+" added, as the game shows it.
                string sign = attribute.Value < 0 ? "" : "+";
                string line = $"{sign}{attribute.Value} {attribute.DisplayName}";
                facts.Text(line).EndLine();
                hasCombatFacts = true;
                buffAlreadyShown = buffAlreadyShown || line == buffFlatText;
            }

            AppendUpgradeEffects(facts, stats, buffAlreadyShown);

            // Only when the effect block is what OPENS the body: an item
            // carrying a bonus run above it opens with the bonus, and the
            // game breaks before one.
            bool effectOpensTheBody = facts.IsEmpty;
            AppendConsumableEffect(facts, stats);
            effectOpensTheBody = effectOpensTheBody && !facts.IsEmpty;

            bodyOpensUnderHeader = hasCombatFacts || effectOpensTheBody;
            return facts.Build();
        }

        private static void AppendUpgradeEffects(
            TooltipContentBuilder builder, ItemStatBlock stats, bool buffAlreadyShown)
        {
            // An agony infusion reports the same fact twice - once as
            // infix_upgrade.buff.description ("+1 Agony Resistance") and
            // once as an infix_upgrade.attributes entry that renders to the
            // identical string. The game prints it once, so the buff line
            // yields to the attribute line that already said it. Only an
            // exact match is suppressed: a buff description that summarises
            // several attributes ("+5 Power, +5 Precision") is its own
            // distinct wording and still belongs.
            //
            // Both texts carry API markup and both go through the one
            // emitter that handles it - see UpgradeEffectLines, which the
            // socketed-component path shares.
            if (!buffAlreadyShown)
            {
                UpgradeEffectLines.AppendBuff(builder, stats.BuffDescription, TooltipSpanRole.Bonus);
            }

            // The loose component itself, not one socketed into armour: no
            // counter and no greyed tier, because no set is being worn to
            // count against. The socketed case does differ - see
            // UpgradeEffectLines.AppendSocketedBlock.
            UpgradeEffectLines.AppendBonuses(builder, stats.UpgradeBonuses, TooltipSpanRole.Bonus);
        }

        /// <summary>
        /// The consume prompt and effect block, the way the game draws them:
        /// <code>
        /// Double-click to consume.                       white
        /// [icon] Nourishment (45 m): +100 Concentration  grey (#AAA)
        ///        +70 Power                               grey
        ///        +15% Experience from Kills              grey
        /// </code>
        /// The effect NAME (details.name), its duration and its first effect
        /// line share one line; EVERY line of the block is the annotation
        /// grey. The effect lines' own +/% prefixes come from the API text
        /// and are not normalised.
        /// <para>
        /// Ascended food returns details:{type:Food} and nothing else
        /// (measured on 91805). Silence, not a "no effect data" marker.
        /// Measurements: docs/ARCHITECTURE.md section S1.4.
        /// </para>
        /// </summary>
        private static void AppendConsumableEffect(TooltipContentBuilder builder, ItemStatBlock stats)
        {
            string description = string.IsNullOrEmpty(stats.NourishmentDescription)
                ? null : stats.NourishmentDescription.Replace("\r\n", "\n").Replace('\r', '\n');
            bool hasDuration = stats.NourishmentDurationMs.HasValue && stats.NourishmentDurationMs.Value > 0;

            if (string.IsNullOrEmpty(stats.EffectName))
            {
                // No effect name in the details block: the pre-live3 shape,
                // minus its separate white line - the whole block is grey
                // now that the game's effect text is measured at #AAA.
                if (description != null)
                {
                    builder.Styled(description, TooltipSpanRole.Muted).EndLine();
                }

                if (hasDuration)
                {
                    builder.Styled(
                        "Duration: " + FormatDuration(stats.NourishmentDurationMs.Value),
                        TooltipSpanRole.Muted).EndLine();
                }

                return;
            }

            // "Double-click to consume." precedes the block, white -
            // emitted only for a Consumable that actually carries effect
            // data, so a detail-less consumable (ascended food) and any
            // future non-consumable type with a named effect invent
            // nothing.
            if (stats.ItemType == "Consumable")
            {
                builder.Text(ConsumePrompt).EndLine();
            }

            var text = new StringBuilder(stats.EffectName.Length + 16);
            text.Append(stats.EffectName);
            if (hasDuration)
            {
                text.Append(FormatEffectDuration(stats.NourishmentDurationMs.Value));
            }

            if (description != null)
            {
                text.Append(": ").Append(description);
            }

            string block = text.ToString();
            if (!string.IsNullOrEmpty(stats.EffectIconUrl))
            {
                builder.EffectBlock(stats.EffectIconUrl, block, TooltipSpanRole.Muted);
            }
            else
            {
                // No icon URL in the data: plain unindented grey lines
                // rather than an invented placeholder (no capture of an
                // icon-less effect block exists).
                builder.Styled(block, TooltipSpanRole.Muted).EndLine();
            }
        }

        /// <summary>
        /// The duration parenthetical joined to the effect name. MEASURED
        /// (2026-08-26): minutes take a space on both sides of the unit
        /// AND before the paren - "Nourishment (45 m):" (live3
        /// soul-pastries) and "Nourishment (30 m):" (omnomberry, its
        /// name-to-paren gap pixel-identical to soul-pastries' 6px space) -
        /// while seconds are tight on both - "Sugar Rush(10s):"
        /// (candy-corn) and "Soul of the Titan(5s):" (relic-livingcity),
        /// both with a 2px letter-kern gap where the minutes captures show
        /// 6px. The hour arm extends the minutes style; no capture of an
        /// hour-long effect exists.
        /// </summary>
        private static string FormatEffectDuration(int durationMs)
        {
            int totalSeconds = durationMs / 1000;
            if (totalSeconds < 60 || totalSeconds % 60 != 0)
            {
                return $"({totalSeconds}s)";
            }

            return " (" + FormatDuration(durationMs) + ")";
        }

        /// <summary>
        /// The slot lines, each its own blank-separated block: on
        /// live/eq-weapon-full.png (2026-08-25) two sigils and two
        /// infusions each render as blank / block / blank, corroborated on
        /// spire, ascended_comparison LEFT and naptown RIGHT - not one
        /// contiguous run (gap G16, fidelity-audit F8).
        /// <para>
        /// Each carries the game's own glyph for its slot, and the game's
        /// own "Unused" wording, which is a fact about a definition rather
        /// than a guess about a copy - see KNOWN-ISSUES #42. A slot the
        /// definition itself fills is already absent from
        /// <see cref="ItemStatBlock.UnusedSlots"/>.
        /// </para>
        /// </summary>
        private static TooltipContent BuildSlots(ItemStatBlock stats, SocketedUpgradeView sockets)
        {
            var slots = new TooltipContentBuilder();
            var socketed = sockets ?? SocketedUpgradeView.None;

            // Infusions above upgrades, and both above the identity block:
            // measured on one ascended-gloves capture whose agony infusion
            // block sits between the attribute lines and the rune block.
            // Separator() is a no-op on an empty builder, so the first
            // block never opens on a blank row.
            foreach (var infusion in socketed.Infusions)
            {
                slots.Separator();
                UpgradeEffectLines.AppendSocketedBlock(slots, infusion);
            }

            // A slot a reading already filled is not also reported unused.
            // The reading is per stack and the slot list per definition, so
            // each socketed block spends one slot of its own kind and only
            // the remainder prints a line. Infusion readings spend
            // enrichment slots too, because /v2/account/* reports an
            // enrichment in the same infusions array.
            int upgradesLeft = socketed.Upgrades.Count;
            int infusionsLeft = socketed.Infusions.Count;
            var unused = stats.UnusedSlots ?? EmptySlots;
            for (int i = 0; i < unused.Count; i++)
            {
                if (unused[i] == ItemSlotKind.Upgrade)
                {
                    if (upgradesLeft > 0)
                    {
                        upgradesLeft--;
                        continue;
                    }
                }
                else if (infusionsLeft > 0)
                {
                    infusionsLeft--;
                    continue;
                }

                slots.Separator();
                slots.SlotLine(ItemSlotFacts.SlotArtAssetId(unused[i]), SlotLineText(unused[i]));
            }

            foreach (var upgrade in socketed.Upgrades)
            {
                slots.Separator();
                UpgradeEffectLines.AppendSocketedBlock(
                    slots, upgrade, socketed.WornCopies(upgrade.ItemId));
            }

            return slots.Build();
        }

        /// <summary>
        /// The two lines the game prints for a transmuted item, between the
        /// socket blocks and the identity block: the word "Transmuted",
        /// then the item's own name. Measured on an ascended-sword capture
        /// (2026-09-06) - both lines are plain body white, and one blank
        /// row sits above the pair and one below it.
        /// </summary>
        private static TooltipContent BuildTransmutedBlock(string ownName)
        {
            if (string.IsNullOrEmpty(ownName))
            {
                return TooltipContent.Empty;
            }

            var block = new TooltipContentBuilder();
            block.Text(TransmutedLabel).EndLine();
            block.Text(ownName).EndLine();
            return block.Build();
        }

        private static string SlotLineText(ItemSlotKind kind)
        {
            switch (kind)
            {
                case ItemSlotKind.Upgrade: return "Unused Upgrade Slot";
                case ItemSlotKind.Enrichment: return "Unused Enrichment Slot";
                default: return "Unused Infusion Slot";
            }
        }

        private static TooltipContent BuildIdentityBlock(ItemStatBlock stats, bool includeDescription)
        {
            var identity = new TooltipContentBuilder();

            // The rarity word carries the rarity colour, same as the name
            // line: measured on the 2026-08-25 live captures - s07's "Fine"
            // reads (82,146,240) and eq-weapon-full's "Legendary"
            // (153,51,255), both on non-comparison hovers. The 2012-2016
            // captures behind the old white reading (G5) are superseded;
            // the game changed. Basic is still suppressed outright, as the
            // game suppresses it (G20) - and so is the WHOLE line on the
            // types the game shows no rarity word for; see ShowsRarityWord.
            if (!string.IsNullOrEmpty(stats.Rarity) && stats.Rarity != "Basic"
                && ShowsRarityWord(stats.ItemType))
            {
                identity.RarityText(stats.Rarity, stats.Rarity).EndLine();
            }

            string type = TypeLine(stats);
            bool isArmor = !string.IsNullOrEmpty(stats.WeightClass);

            // Armour is the one shape where the game splits these two: the
            // weight class alone, then the SLOT plus the word "Armor"
            // ("Heavy" / "Head Armor", measured on warhelm.jpg). The slot
            // word is the API's own (details.type), never a mapping table.
            if (isArmor)
            {
                identity.Text(stats.WeightClass).EndLine();
            }

            if (type != null)
            {
                identity.Text(type + (isArmor ? " Armor" : "")).EndLine();
            }

            // Grey, not white: "(Two-Handed)" measures (160,161,162) on
            // live/eq-weapon-full.png (2026-08-25, lossless), same grey as
            // the game's other parentheticals.
            string hand = WeaponHand(stats);
            if (hand != null)
            {
                identity.Styled(hand, TooltipSpanRole.Muted).EndLine();
            }

            if (!string.IsNullOrEmpty(stats.DamageType))
            {
                identity.Text("Damage Type: " + stats.DamageType).EndLine();
            }

            // An upgrade component's description precedes its level line:
            // the live3 sigil-rage capture runs "Element: Enhancement" and
            // "Double-click to apply to a weapon." ABOVE "Required Level:
            // 60" (2026-08-26), and FWDekker's UpgradeComponent builder is
            // getBuffs + getDescription + getLevel. Equipment keeps the
            // level first (xyaren G13; live3 wings: "Required Level: 80"
            // above the flavour).
            bool descriptionBeforeLevel = stats.ItemType == "UpgradeComponent";
            if (includeDescription && descriptionBeforeLevel)
            {
                AppendDescription(identity, stats);
            }

            if (stats.RequiredLevel > 0)
            {
                identity.Text($"Required Level: {stats.RequiredLevel}").EndLine();
            }

            if (includeDescription && !descriptionBeforeLevel)
            {
                AppendDescription(identity, stats);
            }

            // Above the binding line, which is where the game puts it (G18).
            if (stats.IsUnique)
            {
                identity.Text("Unique").EndLine();
            }

            // Up to two lines, account dimension then soul dimension, the
            // order the game stacks them in (live3 relic-livingcity:
            // "Account Bound" over "Soulbound on Use", 2026-08-26). See
            // ItemStatBlockFactory.ResolveBindings.
            foreach (var binding in stats.Bindings ?? EmptyStrings)
            {
                identity.Text(binding).EndLine();
            }

            if (stats.Restrictions != null && stats.Restrictions.Count > 0)
            {
                identity.Text("Restricted to: " + string.Join(", ", stats.Restrictions)).EndLine();
            }

            return identity.Build();
        }

        /// <summary>
        /// Whether the game gives this type a rarity-word line. MEASURED
        /// absent (2026-08-26, live3) on CraftingMaterial (vials Rare,
        /// eyes-of-kormir Masterwork), Trophy (fury-scorched Exotic,
        /// heart-of-destroyer Exotic, red-festival-lantern Junk) and
        /// Consumable (soul-pastries Masterwork, omnomberry Fine), and on
        /// UpgradeComponent (sigil-rage Exotic shows no "Exotic" line);
        /// MEASURED present on Armor (s07), Weapon (eq-weapon-full), Back
        /// (wings Ascended) and Relic (relic-livingcity, gold "Exotic").
        /// The remaining suppressions mirror FWDekker's builders, which
        /// call getRarity() only in Armor/Back/Tool/Trinket/Weapon -
        /// INFERRED, no capture. An unknown type shows the word, matching
        /// the measured Relic default.
        /// </summary>
        private static bool ShowsRarityWord(string itemType)
        {
            switch (itemType)
            {
                case "CraftingMaterial":
                case "Trophy":
                case "Consumable":
                case "UpgradeComponent":
                case "Bag":
                case "Container":
                case "Gathering":
                case "Gizmo":
                case "MiniPet":
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// The type line, or null for the types the game shows none for.
        /// MEASURED (2026-08-26, live3): a crafting material has NO type
        /// line (vials/eyes/almonds - almonds' "Ingredient" is 12337's own
        /// description field, not a type word); an upgrade component has
        /// none either (sigil-rage runs buff/cooldown/description straight
        /// into Required Level); a consumable says "Consumable" - the
        /// top-level type - never its details.type ("Food" on soul-pastries
        /// and omnomberry, "Generic" on candy-corn, all three rendered
        /// "Consumable"); a Back item says "Back Item" (wings; FWDekker
        /// agrees). Everything else keeps the API's own noun, split at
        /// camel-case boundaries.
        /// </summary>
        private static string TypeLine(ItemStatBlock stats)
        {
            switch (stats.ItemType)
            {
                case "CraftingMaterial":
                case "UpgradeComponent":
                    return null;
                case "Consumable":
                    return "Consumable";
                case "Back":
                    return "Back Item";
            }

            string type = !string.IsNullOrEmpty(stats.SubType) ? stats.SubType : stats.ItemType;
            return string.IsNullOrEmpty(type) ? null : SpaceCamelCase(type);
        }

        /// <summary>
        /// The item's description. On equipment it sits INSIDE the
        /// identity block, after the type/level lines and before the
        /// binding flags (gap G13, xyaren; live3 wings agrees); on the
        /// description-leading types it is this same content emitted as
        /// the body-opening block instead - see
        /// <see cref="DescriptionLeadsBody"/>.
        /// <para>
        /// The description's own <c>&lt;c=@...&gt;</c> runs decide the
        /// colours: unmarked prose stays white, a flavour run goes teal, an
        /// abilitytype lead-in pale yellow (gap G7). Flattening the whole
        /// string to one role is what made "A gift bag!" indistinguishable
        /// from the quoted flavour after it.
        /// </para>
        /// </summary>
        private static void AppendDescription(TooltipContentBuilder builder, ItemStatBlock stats)
        {
            // A stat-selectable item's description in the game IS this
            // string. It is emitted ahead of whatever description the API
            // carries rather than instead of it, so nothing the item
            // actually says is lost.
            if (stats.StatChoiceCount > 0)
            {
                builder.Text(SelectStatsPrompt).EndLine();
            }

            var spans = ItemDescriptionSanitizer.SanitizeToSpans(stats.Description);
            if (spans.Count == 0)
            {
                return;
            }

            foreach (var span in spans)
            {
                builder.Styled(span.Text, span.Role);
            }

            builder.EndLine();
        }

        /// <summary>
        /// The vendor value the way the game shows it: unlabelled, alone on
        /// the last line, and absent entirely when the item cannot be sold
        /// (<see cref="ItemStatBlock.VendorValue"/> is null exactly then).
        /// Gap G14. It always runs contiguous under the line above it -
        /// see the measurement note in <see cref="BuildContent"/>.
        /// </summary>
        private static TooltipContent BuildVendorValue(ItemStatBlock stats)
        {
            if (!stats.VendorValue.HasValue)
            {
                return TooltipContent.Empty;
            }

            return new TooltipContentBuilder()
                .Coin(stats.VendorValue.Value, CoinSegmentMath.GameStyleText(stats.VendorValue.Value))
                .Build();
        }

        /// <summary>
        /// The game's hand line under a weapon's type - "(Two-Handed)",
        /// "(Main Hand)", "(Off Hand)", "(Aquatic)" (gap G17). A weapon
        /// type this table does not know renders no line at all rather than
        /// a guessed one.
        /// </summary>
        private static string WeaponHand(ItemStatBlock stats)
        {
            if (stats.ItemType != "Weapon" || string.IsNullOrEmpty(stats.SubType))
            {
                return null;
            }

            switch (stats.SubType)
            {
                case "Greatsword":
                case "Hammer":
                case "LongBow":
                case "Rifle":
                case "ShortBow":
                case "Staff":
                    return "(Two-Handed)";
                case "Axe":
                case "Dagger":
                case "Mace":
                case "Pistol":
                case "Scepter":
                case "Sword":
                    return "(Main Hand)";
                case "Focus":
                case "Shield":
                case "Torch":
                case "Warhorn":
                    return "(Off Hand)";
                case "Harpoon":
                case "Speargun":
                case "Trident":
                    return "(Aquatic)";
                default:
                    return null;
            }
        }

        /// <summary>
        /// "CraftingMaterial" -> "Crafting Material". A mechanical split at
        /// camel-case boundaries, never a lookup table: the API's type
        /// vocabulary grows with every expansion, and a table would render
        /// a new type as nothing rather than as its own raw name.
        /// </summary>
        private static string SpaceCamelCase(string value)
        {
            var sb = new StringBuilder(value.Length + 4);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (i > 0 && char.IsUpper(c) && !char.IsUpper(value[i - 1]) && value[i - 1] != ' ')
                {
                    sb.Append(' ');
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        private static string FormatDuration(int durationMs)
        {
            int totalMinutes = durationMs / 60000;
            if (totalMinutes < 60)
            {
                return $"{totalMinutes} m";
            }

            int hours = totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return minutes == 0 ? $"{hours} h" : $"{hours} h {minutes} m";
        }

        // Thousands-separated, as the game shows a four-figure weapon
        // strength ("1,045 - 1,155" - gap G19). Invariant culture, the
        // module's standing policy for its English-only strings.
        private static string FormatCount(int value)
        {
            return value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
