using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The About tab's authored text, and which words in it are links
    /// (Blish-free, unit-testable). The view draws these runs; it does not
    /// decide them.
    /// <para>
    /// The three credit paragraphs are approved copy. Ship the literal
    /// strings as-is: do not derive them from other constants, reword them,
    /// or re-wrap them.
    /// </para>
    /// </summary>
    internal static class AboutTabText
    {
        public const string CreditsSectionTitle = "Credits - gw2efficiency";

        public const string Gw2EfficiencyUrl = "https://gw2efficiency.com";

        public const string Gw2EfficiencyPatreonUrl = "https://www.patreon.com/gw2efficiency";

        public const string Gw2EfficiencyPayPalUrl = "https://paypal.me/devoxa";

        public const string BlishHudSourceUrl = "https://github.com/blish-hud/Blish-HUD";

        /// <summary>The word the "Built with" row hangs the Blish HUD
        /// repository link on. The brackets around it are plain text.</summary>
        public const string SourceLinkWord = "source";

        public const string CreditParagraph1 =
            "This module was both inspired by and built upon the great work of the gw2efficiency team. Much of the hardest parts of crafting optimization that this module solves follow the same solutions this team of talented community contributors have created. Taimi's Toolbench runs completely independently of their libraries or APIs, but reimplements logic following their approach.";

        public const string CreditParagraph2 =
            "If you enjoy this module, consider checking out gw2efficiency.com, supporting them via their Patreon or directly via their PayPal.";

        public const string CreditParagraph3 =
            "A big thank you to David Reess (queicherius), Saskia Van Leeuwen, Ecmel Tugcu and their open-source contributors.";

        /// <summary>
        /// The four linked phrases of the credit copy. "gw2efficiency.com"
        /// is listed alongside "gw2efficiency" on purpose: the longest
        /// match at an index wins, so the bare name never swallows the
        /// domain's first thirteen characters.
        /// </summary>
        private static readonly IReadOnlyList<LinkPhrase> CreditPhrases = new[]
        {
            new LinkPhrase("gw2efficiency.com", Gw2EfficiencyUrl),
            new LinkPhrase("gw2efficiency", Gw2EfficiencyUrl),
            new LinkPhrase("their Patreon", Gw2EfficiencyPatreonUrl),
            new LinkPhrase("their PayPal", Gw2EfficiencyPayPalUrl),
        };

        /// <summary>The credit block, one segment list per paragraph.</summary>
        public static IReadOnlyList<IReadOnlyList<PlanNoteSegment>> CreditParagraphs()
        {
            return new[]
            {
                LinkPhraseSpans.Split(CreditParagraph1, CreditPhrases),
                LinkPhraseSpans.Split(CreditParagraph2, CreditPhrases),
                LinkPhraseSpans.Split(CreditParagraph3, CreditPhrases),
            };
        }

        /// <summary>
        /// The "Source" row's value: the module's own repository as one
        /// link. A url the launcher will not open falls back to plain text,
        /// so the row still says what it knows.
        /// </summary>
        public static IReadOnlyList<PlanNoteSegment> SourceValue(string url, string fallbackText)
        {
            var target = IconWikiTarget.ExternalPage(url);
            if (target.HasPage)
            {
                return new[] { PlanNoteSegment.Linked(url, target) };
            }

            return new[]
            {
                PlanNoteSegment.Plain(string.IsNullOrWhiteSpace(url) ? fallbackText ?? "" : url),
            };
        }

        /// <summary>
        /// The "Built with" row's value: the Blish HUD version this module
        /// targets, then the word "source" linking Blish HUD's own
        /// repository. The brackets stay plain, so only the word is
        /// coloured and ruled.
        /// </summary>
        public static IReadOnlyList<PlanNoteSegment> BuiltWithValue(string blishVersionText)
        {
            return new[]
            {
                PlanNoteSegment.Plain("Blish HUD " + (blishVersionText ?? "") + " ("),
                PlanNoteSegment.Linked(
                    SourceLinkWord, IconWikiTarget.ExternalPage(BlishHudSourceUrl)),
                PlanNoteSegment.Plain(")"),
            };
        }
    }
}
