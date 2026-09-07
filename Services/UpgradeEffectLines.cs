using System.Collections.Generic;
using TaimisToolbench.Models;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The lines an upgrade component contributes to a tooltip - its effect
    /// text and its rune bonus ladder - emitted the same way whether the
    /// component IS the hovered item or is socketed into it.
    /// <para>
    /// Both texts carry the API's own markup: a sigil's cooldown arrives as
    /// <c>&lt;br&gt;&lt;c=@reminder&gt;(Cooldown: 2 Seconds)&lt;/c&gt;</c>
    /// inside <c>infix_upgrade.buff.description</c> (24560), and a rune's
    /// fourth bonus can carry the same run inside
    /// <c>details.bonuses</c> (24838), so both go through
    /// <see cref="ItemDescriptionSanitizer"/> and neither is emitted raw.
    /// </para>
    /// <para>
    /// Blish-free (repo invariant), like every composer.
    /// </para>
    /// </summary>
    internal static class UpgradeEffectLines
    {
        private static readonly IReadOnlyList<string> NoBonuses = new List<string>();

        /// <summary>
        /// The component's effect text - one line per hard break the API
        /// wrote, unmarked prose promoted to <paramref name="baseRole"/>
        /// and a marked run keeping its own colour.
        /// <para>
        /// Built from <c>infix_upgrade.buff.description</c> alone, never
        /// from <c>infix_upgrade.attributes</c>: the description already
        /// spells out every attribute the component grants, including the
        /// multi-attribute case (37131 reads "+5 Power\n+9 Agony
        /// Resistance"), and an enrichment can carry a description with an
        /// empty attribute list (39332, "+15% Karma"), so the attributes
        /// are the machine-readable copy rather than a second source of
        /// display text.
        /// </para>
        /// </summary>
        public static void AppendBuff(
            TooltipContentBuilder builder, string buffDescription, TooltipSpanRole baseRole)
        {
            if (builder == null || string.IsNullOrEmpty(buffDescription))
            {
                return;
            }

            var spans = ItemDescriptionSanitizer.SanitizeToSpans(buffDescription);
            if (spans.Count == 0)
            {
                return;
            }

            foreach (var span in spans)
            {
                builder.Styled(
                    span.Text, span.Role == TooltipSpanRole.Default ? baseRole : span.Role);
            }

            builder.EndLine();
        }

        /// <summary>
        /// A rune's bonus ladder, one line per tier. The index IS data -
        /// the Nth entry is the bonus at N matching pieces equipped - so it
        /// is printed, and the game prints it in exactly this shape:
        /// "(1): +25 Power" (wiki, Rune).
        /// </summary>
        public static void AppendBonuses(
            TooltipContentBuilder builder, IReadOnlyList<string> bonuses, TooltipSpanRole baseRole)
        {
            AppendBonuses(builder, bonuses, baseRole, baseRole, int.MaxValue);
        }

        /// <summary>
        /// The same ladder with the tiers the wearer has reached in
        /// <paramref name="activeRole"/> and the rest in
        /// <paramref name="inactiveRole"/>. Tiers are cumulative, so
        /// <paramref name="activeTiers"/> lights entries 1 through N: the
        /// wiki's Rune article states the rule the colours follow, "Active
        /// bonuses are shown in blue on the tooltip, or in gray if the
        /// bonus is not active for lack of a sufficient number of this
        /// rune."
        /// </summary>
        public static void AppendBonuses(
            TooltipContentBuilder builder,
            IReadOnlyList<string> bonuses,
            TooltipSpanRole activeRole,
            TooltipSpanRole inactiveRole,
            int activeTiers)
        {
            if (builder == null)
            {
                return;
            }

            var list = bonuses ?? NoBonuses;
            for (int i = 0; i < list.Count; i++)
            {
                var baseRole = i < activeTiers ? activeRole : inactiveRole;
                builder.Styled($"({i + 1}): ", baseRole);
                var spans = ItemDescriptionSanitizer.SanitizeToSpans(list[i]);
                foreach (var span in spans)
                {
                    builder.Styled(
                        span.Text, span.Role == TooltipSpanRole.Default ? baseRole : span.Role);
                }

                builder.EndLine();
            }
        }

        /// <summary>
        /// The rune's name with the counter the game prints after it, as in
        /// "Superior Rune of Fireworks (6/6)".
        /// <para>
        /// The denominator is the ladder height /v2/items reports in
        /// details.bonuses: six on a Superior rune (24836), four on a Major
        /// one (24838). The numerator is NOT capped to it. The wiki's Rune
        /// article says the tooltip shows the total number of identical
        /// runes, and turns that number red past two minor or four major
        /// ones; the module has no measurement of that red, so an
        /// over-count prints in the same blue as the name.
        /// </para>
        /// </summary>
        private static string NameWithSetCounter(
            string name, IReadOnlyList<string> bonuses, int wornCopies)
        {
            int ladderHeight = bonuses == null ? 0 : bonuses.Count;
            return wornCopies > 0 && ladderHeight > 0
                ? $"{name} ({wornCopies}/{ladderHeight})"
                : name;
        }

        /// <summary>
        /// One socketed component's block, the way the game draws it: the
        /// component's icon and name on the first line, then its effect
        /// text or its bonus ladder flush left under it.
        /// <para>
        /// The name is NOT rarity-coloured: measured at three rarities on
        /// one capture - an Ascended infusion (49432), an Exotic rune
        /// (24836) and a Rare sigil (24560) - all three reading exactly
        /// (85,153,255), the same blue as the effect text beside them.
        /// </para>
        /// <para>
        /// <paramref name="wornCopies"/> is how many pieces carrying this
        /// component the host's wearer has equipped, and is 0 unless the
        /// host sits in exactly one place and that place is one character's
        /// worn gear - see <see cref="EquippedRuneSetIndex"/>. At 0 the
        /// name takes no counter and the whole ladder stays inactive, which
        /// is what the module drew before any equipment was read.
        /// </para>
        /// </summary>
        public static void AppendSocketedBlock(
            TooltipContentBuilder builder, ItemStatBlock upgrade, int wornCopies = 0)
        {
            if (builder == null || upgrade == null || string.IsNullOrEmpty(upgrade.Name))
            {
                return;
            }

            string name = NameWithSetCounter(upgrade.Name, upgrade.UpgradeBonuses, wornCopies);
            if (string.IsNullOrEmpty(upgrade.IconUrl))
            {
                builder.Styled(name, TooltipSpanRole.Bonus).EndLine();
            }
            else
            {
                builder.EffectBlock(upgrade.IconUrl, name, TooltipSpanRole.Bonus);
            }

            AppendBuff(builder, upgrade.BuffDescription, TooltipSpanRole.Bonus);
            AppendBonuses(
                builder,
                upgrade.UpgradeBonuses,
                TooltipSpanRole.Bonus,
                TooltipSpanRole.BonusInactive,
                wornCopies);
        }
    }
}
