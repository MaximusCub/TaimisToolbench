namespace TaimisToolbench.Services
{
    /// <summary>
    /// The wiki page an icon's right-click opens, and the sentence that
    /// tells the reader it can. One value carries both, so the affordance
    /// and the link can never disagree about whether there is a page.
    /// <para>
    /// Every hovering icon factory on <c>Views/Rendering/ItemIconTooltip</c>
    /// requires one. The line itself is written by
    /// <see cref="SecondTooltipBox"/>, never by a call site, which is what
    /// keeps it last in the box everywhere.
    /// </para>
    /// <para>
    /// Blish-free (repo invariant), so the page choice and the wording are
    /// unit-testable without a live control.
    /// </para>
    /// </summary>
    internal readonly struct IconWikiTarget
    {
        /// <summary>
        /// The right-click affordance line, in the module's one wording.
        /// </summary>
        public const string HintText = "Right-click to open the wiki page.";

        /// <summary>The same affordance on a subject whose acquisition is
        /// left to the player, where the link lands on the page's
        /// Acquisition section rather than its top.</summary>
        public const string AcquisitionHintText =
            "Right-click to see how to get this on the wiki.";

        private readonly string _title;
        private readonly IconWikiPage _page;

        private IconWikiTarget(string title, IconWikiPage page)
        {
            _title = title;
            _page = page;
        }

        /// <summary>
        /// Whether this icon has a page to open. False for a named silence
        /// and for the placeholder names that never resolve to a real page
        /// (see <see cref="WikiLinkBuilder.HasWikiPage"/>), so an icon
        /// carrying "Unknown Item" advertises nothing and opens nothing.
        /// </summary>
        public bool HasPage
        {
            get { return _page != IconWikiPage.None && WikiLinkBuilder.HasWikiPage(_title); }
        }

        /// <summary>What the second box says about the right-click, or null
        /// when there is no page. The LAST line of that box, always.</summary>
        public string Hint
        {
            get
            {
                if (!HasPage)
                {
                    return null;
                }

                bool acquisition = _page == IconWikiPage.Acquisition
                    || _page == IconWikiPage.SheetPageAcquisition;
                return acquisition ? AcquisitionHintText : HintText;
            }
        }

        /// <summary>
        /// The url the right-click opens, built on demand. Callers build it
        /// inside the click handler rather than per row: a plan tree can
        /// carry hundreds of icons and almost none of them are ever
        /// right-clicked.
        /// </summary>
        public string BuildUrl()
        {
            if (!HasPage)
            {
                return null;
            }

            switch (_page)
            {
                case IconWikiPage.Acquisition:
                    return WikiLinkBuilder.BuildItemAcquisitionUrl(_title);
                case IconWikiPage.RecipeSheet:
                    return WikiLinkBuilder.BuildRecipeSheetUrl(_title);
                case IconWikiPage.SheetPageAcquisition:
                    return WikiLinkBuilder.BuildSheetPageAcquisitionUrl(_title);
                default:
                    return WikiLinkBuilder.BuildItemPageUrl(_title);
            }
        }

        /// <summary>The subject's own page, for an item or a wallet
        /// currency alike. <paramref name="name"/> is the name already on
        /// the row, so the page and the label name the same thing.</summary>
        public static IconWikiTarget ItemPage(string name)
        {
            return new IconWikiTarget(name, IconWikiPage.ItemPage);
        }

        /// <summary>The subject's page opened at its Acquisition section,
        /// for a subject the module deliberately plans no route to and
        /// whose options are listed there.</summary>
        public static IconWikiTarget Acquisition(string name)
        {
            return new IconWikiTarget(name, IconWikiPage.Acquisition);
        }

        /// <summary>
        /// The "Recipe: &lt;crafted item&gt;" sheet page.
        /// <paramref name="craftedItemName"/> is the item the recipe makes,
        /// not the sheet's own name: the sheet page title is built from the
        /// crafted name, and its colon must survive unencoded
        /// (<see cref="WikiLinkBuilder.BuildRecipeSheetUrl"/>).
        /// </summary>
        public static IconWikiTarget RecipeSheet(string craftedItemName)
        {
            return new IconWikiTarget(craftedItemName, IconWikiPage.RecipeSheet);
        }

        /// <summary>
        /// A recipe SHEET's own page opened at its Acquisition section,
        /// which is where the wiki lists every merchant selling it.
        /// <paramref name="sheetItemName"/> is the SHEET's own name, not
        /// the crafted item's: that name already carries the "Recipe: "
        /// prefix, so nothing has to synthesize one, and it is known on
        /// exactly the rows this page is offered from. Contrast
        /// <see cref="RecipeSheet"/>, which builds the prefix because the
        /// sheet's own name is not known there.
        /// </summary>
        public static IconWikiTarget SheetPageAcquisition(string sheetItemName)
        {
            return new IconWikiTarget(sheetItemName, IconWikiPage.SheetPageAcquisition);
        }

        /// <summary>
        /// Deliberately no page, with the reason named at the call site.
        /// Adding a reason to <see cref="IconWikiSilence"/> is the act of
        /// the commit that needs one.
        /// </summary>
        public static IconWikiTarget None(IconWikiSilence why)
        {
            // Nothing is read from the reason, and that is the point: it
            // exists so the silence is a statement in the diff rather than
            // an absent argument.
            _ = why;
            return new IconWikiTarget(null, IconWikiPage.None);
        }

        private enum IconWikiPage
        {
            None,
            ItemPage,
            Acquisition,
            RecipeSheet,
            SheetPageAcquisition,
        }
    }

    /// <summary>
    /// Why an icon opens no wiki page. One member per real reason; there is
    /// deliberately no "other".
    /// </summary>
    internal enum IconWikiSilence
    {
        /// <summary>The icon is drawn INSIDE a tooltip box. A tooltip is
        /// not a click target, so nothing there can be clicked.</summary>
        DrawnInsideATooltip,

        /// <summary>The icon sits in an open search dropdown, where a click
        /// picks the row it is on. A right-click there would send the
        /// reader to a browser in the middle of typing.</summary>
        SitsInASearchDropdown,
    }
}
