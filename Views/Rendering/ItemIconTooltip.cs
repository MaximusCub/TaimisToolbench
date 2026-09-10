using Blish_HUD.Controls;
using System;
using System.Collections.Generic;
using TaimisToolbench.Models;
using TaimisToolbench.Services;

namespace TaimisToolbench.Views.Rendering
{
    /// <summary>
    /// What an item icon says on hover and which wiki page it opens - the
    /// parameter <see cref="IconControls.DrawItemIcon"/> takes instead of
    /// an optional trailing tooltip string. The call site has to say which
    /// it means, and a factory name is what a diff shows.
    /// <para>
    /// SCOPE is the other half of the contract, and this type is where it is
    /// enforced: the hover belongs to the item's ICON and to nothing else.
    /// There is no seam that takes a Label or a row Panel, so the only
    /// control that can carry an item tooltip is the one
    /// <see cref="IconControls.DrawItemIcon"/> builds - see StampOnIconTree.
    /// </para>
    /// <para>
    /// The rich half is always DEFERRED: a stat block can land after the row
    /// was built (a plan restored from disk tops its stats up in the
    /// background), and content snapshotted at render time could never show
    /// it. See <c>TooltipFacility.ApplyRichDeferred</c>.
    /// </para>
    /// docs/ARCHITECTURE.md, "Views: relocated design narrative".
    /// </summary>
    internal readonly struct ItemIconTooltip
    {
        private readonly Func<TooltipContent> _build;
        private readonly string _plainText;
        private readonly IconWikiTarget _wiki;

        private ItemIconTooltip(Func<TooltipContent> build, string plainText, IconWikiTarget wiki)
        {
            _build = build;
            _plainText = plainText;
            _wiki = wiki;
        }

        /// <summary>
        /// What the control says before the rich builder runs, and what it
        /// falls back to if that builder composes nothing or throws - the
        /// item's own name. Null for a silent intent.
        /// </summary>
        internal string PlainText
        {
            get { return _plainText; }
        }

        /// <summary>
        /// THE standard item hover: the item's stat block if this session
        /// happens to hold one, headed either way by the icon+name row the
        /// game's own tooltip opens with, and a second box carrying the
        /// right-click affordance.
        /// The facts come from the id, so the header renders whether or
        /// not this session has fetched the item's stat block.
        /// </summary>
        internal static ItemIconTooltip ForItem(
            string name, Func<ItemTooltipFacts> getFacts, IconWikiTarget wiki)
        {
            return ForItem(name, getFacts, (Func<TooltipContent>)null, wiki);
        }

        /// <summary>
        /// The standard item hover plus this surface's own prose tips - a
        /// HAVE/NEED split, an acquisition hint, a source breakdown -
        /// gathered at hover time so a line that depends on the row's
        /// current width is read when it is shown, not when it was built.
        /// The tips lead the second box; the wiki line still ends it.
        /// </summary>
        internal static ItemIconTooltip ForItem(
            string name,
            Func<ItemTooltipFacts> getFacts,
            Func<IReadOnlyList<string>> tips,
            IconWikiTarget wiki)
        {
            return ForItem(
                name,
                getFacts,
                tips == null
                    ? (Func<TooltipContent>)null
                    : () => SecondTooltipBox.Compose(tips(), null),
                wiki);
        }

        /// <summary>
        /// The same item hover for a surface whose tips are CONTENT rather
        /// than prose - a Recipe Tree row, whose unit price has to keep its
        /// coin span instead of being spelled out as "1s 0c".
        /// </summary>
        internal static ItemIconTooltip ForItem(
            string name,
            Func<ItemTooltipFacts> getFacts,
            Func<TooltipContent> tips,
            IconWikiTarget wiki)
        {
            if (getFacts == null)
            {
                throw new ArgumentNullException(nameof(getFacts));
            }

            return new ItemIconTooltip(
                () =>
                {
                    var facts = getFacts();
                    return ItemRowTooltipComposer.BuildRowContent(
                        ItemStatTooltipComposer.BuildContent(
                            facts.Stats, facts.Sockets, facts.Skin),
                        facts.Identity,
                        SecondTooltipBox.Compose(tips == null ? null : tips(), wiki.Hint));
                },
                name,
                wiki);
        }

        /// <summary>
        /// For the surfaces that compose the FIRST box themselves - a stat
        /// block with its socketed components folded in, a tree row whose
        /// id-space gate decides whether the row has a stat block at all -
        /// and whose tips are CONTENT rather than prose, because a unit
        /// price has to keep its coin span. Both are read at hover time.
        /// The second box is still assembled here, so this is not a way
        /// past the ordering rule.
        /// </summary>
        internal static ItemIconTooltip Composed(
            ItemTooltipIdentity identity,
            Func<TooltipContent> gameContent,
            Func<TooltipContent> tips,
            IconWikiTarget wiki)
        {
            return new ItemIconTooltip(
                () => ItemRowTooltipComposer.BuildRowContent(
                    gameContent == null ? null : gameContent(),
                    identity,
                    SecondTooltipBox.Compose(tips == null ? null : tips(), wiki.Hint)),
                identity.Name,
                wiki);
        }

        /// <summary>
        /// THE standard wallet-currency hover: the game's own currency
        /// tooltip - icon+name, the wallet balance, the currency's prose,
        /// the type line (<see cref="CurrencyTooltipComposer"/>) - under a
        /// second box carrying the right-click affordance.
        /// <paramref name="getFacts"/> is read at hover time, so a
        /// /v2/currencies reply that lands after the row was built still
        /// reaches the box.
        /// <para>
        /// The KIND is the caller's to choose and it must come from the id
        /// space the caller drew the icon from, never from the name:
        /// "Gaeting Crystal" is wallet currency 77 AND item 104026, so a
        /// name lookup cannot tell which tooltip it is owed. A good the
        /// module lists among its currencies but which is really an ITEM -
        /// Crystalline Ore 46682 - takes ForItem instead.
        /// Views/SettingsTabContent's <c>IsBarterItem</c> is the
        /// discriminator that already carries this distinction.
        /// </para>
        /// </summary>
        internal static ItemIconTooltip ForCurrency(
            string name, Func<CurrencyTooltipFacts> getFacts, IconWikiTarget wiki)
        {
            return ForCurrency(name, getFacts, null, wiki);
        }

        /// <summary>
        /// The same currency hover plus this surface's own tips - a unit
        /// price on a Recipe Tree row. They lead the second box; the wiki
        /// line still ends it.
        /// </summary>
        internal static ItemIconTooltip ForCurrency(
            string name,
            Func<CurrencyTooltipFacts> getFacts,
            Func<TooltipContent> tips,
            IconWikiTarget wiki)
        {
            if (getFacts == null)
            {
                throw new ArgumentNullException(nameof(getFacts));
            }

            return new ItemIconTooltip(
                () => SecondTooltipBox.Attach(
                    CurrencyTooltipComposer.BuildContent(getFacts()),
                    SecondTooltipBox.Compose(tips == null ? null : tips(), wiki.Hint)),
                name,
                wiki);
        }

        /// <summary>
        /// Deliberately silent, with the reason named at the call site.
        /// Adding a reason to <see cref="ItemIconSilence"/> is the act of
        /// the commit that needs one. Silent on the right-click too: both
        /// reasons below are icons a reader cannot usefully click, so the
        /// wiki silence follows from the hover silence rather than being a
        /// second decision the call site could get wrong.
        /// </summary>
        internal static ItemIconTooltip None(ItemIconSilence why)
        {
            return new ItemIconTooltip(null, null, WikiSilenceFor(why));
        }

        /// <summary>
        /// A Panel laid OVER an icon's art - the tree's dimming scrim for a
        /// reference branch. It is part of the icon as far as the cursor is
        /// concerned, and Blish resolves on the deepest control, so leaving
        /// it unstamped puts a hole in the middle of the icon.
        /// </summary>
        internal void StampOnIconOverlay(Panel overlay)
        {
            StampOnIconTree(overlay);
        }

        /// <summary>
        /// The icon and everything nested inside it, for
        /// <see cref="IconControls"/> to call as it builds one. Blish
        /// resolves a tooltip on the deepest control under the cursor and
        /// never bubbles, so the frame, its art square and the missing-icon
        /// placeholder mark each need their own hover.
        /// <para>
        /// The right-click goes on the outermost control ALONE, because
        /// click handlers accumulate up the tree where hovers do not. See
        /// <see cref="IconWikiClick"/>.
        /// </para>
        /// <para>
        /// SCOPE: the icon, and no further. The module used to stamp the
        /// row panel and every label on it, precisely BECAUSE Blish does
        /// not bubble and the gaps otherwise answered nothing - which meant
        /// a wide row popped the item's tooltip over its counts, its
        /// prices, its timestamps and its empty middle. Those gaps SHOULD
        /// answer nothing.
        /// </para>
        /// </summary>
        internal void StampOnIconTree(Control iconTree)
        {
            if (iconTree == null)
            {
                return;
            }

            // Before the hover half and unconditionally: an icon whose
            // hover is deliberately silent still keeps whatever wiki page
            // its intent named, and a re-stamp has to be able to retarget
            // an icon whose subject was swapped underneath it.
            IconWikiClick.ApplyToIcon(iconTree, _wiki);

            // The icon's own note ("no icon available for this entry") is
            // already on the tree and is worth more than silence, so a
            // silent intent leaves the plain layer alone rather than
            // clearing it.
            if (_build == null)
            {
                return;
            }

            IconControls.ApplyRichDeferredToIconTree(iconTree, _build);
        }

        private static IconWikiTarget WikiSilenceFor(ItemIconSilence why)
        {
            return why == ItemIconSilence.WouldCoverTheListItSitsIn
                ? IconWikiTarget.None(IconWikiSilence.SitsInASearchDropdown)
                : IconWikiTarget.None(IconWikiSilence.DrawnInsideATooltip);
        }
    }

    /// <summary>
    /// Why an item icon shows nothing on hover. One member per real reason;
    /// there is deliberately no "other".
    /// </summary>
    internal enum ItemIconSilence
    {
        /// <summary>The icon is being drawn INSIDE a tooltip box. A tooltip
        /// that spawns a tooltip has nowhere to put it.</summary>
        DrawnInsideATooltip,

        /// <summary>The icon sits in an open dropdown list whose remaining
        /// rows are what the reader is scanning; a hover box would cover
        /// the choices it is meant to help them make.</summary>
        WouldCoverTheListItSitsIn,
    }
}
