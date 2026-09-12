using System.Collections.Generic;

namespace TaimisToolbench.Services
{
    /// <summary>
    /// The About tab's authored text, and which words in it are links
    /// (Blish-free, unit-testable). The view draws these runs; it does not
    /// decide them.
    /// <para>
    /// Every credit paragraph is approved copy. Ship the literal strings
    /// as-is: do not derive them from other constants, reword them, or
    /// re-wrap them.
    /// </para>
    /// </summary>
    internal static class AboutTabText
    {
        public const string CreditsSectionTitle = "Credits";

        public const string Gw2EfficiencySubheading = "gw2efficiency";

        public const string BlishHudSubheading = "Blish HUD";

        public const string OpenSourceSubheading = "Open source libraries";

        public const string Gw2EfficiencyUrl = "https://gw2efficiency.com";

        public const string Gw2EfficiencyPatreonUrl = "https://www.patreon.com/gw2efficiency";

        public const string Gw2EfficiencyPayPalUrl = "https://paypal.me/devoxa";

        public const string BlishHudSourceUrl = "https://github.com/blish-hud/Blish-HUD";

        /// <summary>
        /// The licence texts of every shipped library, in the module's own
        /// repository. Pinned to master rather than to a branch, so the
        /// link keeps working once the file lands there.
        /// </summary>
        public const string ThirdPartyNoticesUrl =
            "https://github.com/MaximusCub/TaimisToolbench/blob/master/ref/THIRD-PARTY-NOTICES.txt";

        /// <summary>
        /// The module's own licence file, in its repository. Pinned to
        /// master for the same reason as the notices link above.
        /// </summary>
        public const string LicenseUrl =
            "https://github.com/MaximusCub/TaimisToolbench/blob/master/LICENSE";

        /// <summary>The whole value of the "License" row, and the word it
        /// hangs the licence file link on.</summary>
        public const string LicenseName = "MIT";

        /// <summary>The word the "Built with" row hangs the Blish HUD
        /// repository link on. The brackets around it are plain text.</summary>
        public const string SourceLinkWord = "source";

        public const string CreditParagraph1 =
            "This module was both inspired by and built upon the great work of the gw2efficiency team. Much of the hardest parts of crafting optimization that this module solves follow the same solutions this team of talented community contributors have created. Taimi's Toolbench runs completely independently of their libraries, custom data or APIs, but reimplements similar approaches.";

        public const string CreditParagraph2 =
            "If you enjoy this module, consider checking out gw2efficiency.com, supporting them via their Patreon or directly via their PayPal.";

        public const string CreditParagraph3 =
            "A big thank you to David Reess (queicherius), Saskia Van Leeuwen, Ecmel Tugcu and their open-source contributors.";

        public const string BlishHudParagraph =
            "Taimi's Toolbench runs inside Blish HUD, the Guild Wars 2 overlay it is built on. Blish HUD is MIT licensed and made by the Blish HUD team.";

        public const string OpenSourceIntro =
            "This module ships these libraries unchanged, with thanks to the people who wrote them.";

        public const string OpenSourceLicenceNote =
            "All are MIT licensed, except MonoGame, which uses the Microsoft Public License. The full licence texts are in our third-party notices file.";

        /// <summary>
        /// One shipped library per line, each already carrying its bullet.
        /// U+2022 is one of the punctuation marks Menomonia actually carries;
        /// a geometric marker would draw nothing and advance nothing - see
        /// CLAUDE.md, "Escaping a codepoint does not make it render".
        /// </summary>
        public static readonly IReadOnlyList<string> OpenSourceLibraryLines = new[]
        {
            "\u2022 Gw2Sharp, by Archomeda",
            "\u2022 Json.NET, by James Newton-King",
            "\u2022 MonoGame, by The MonoGame Team",
            "\u2022 MonoGame.Extended, by Dylan Wilson",
            "\u2022 Seven .NET libraries, by the .NET Foundation",
        };

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

        private static readonly IReadOnlyList<LinkPhrase> BlishHudPhrases = new[]
        {
            new LinkPhrase("Blish HUD team", BlishHudSourceUrl),
        };

        private static readonly IReadOnlyList<LinkPhrase> OpenSourcePhrases = new[]
        {
            new LinkPhrase("third-party notices file", ThirdPartyNoticesUrl),
        };

        /// <summary>The gw2efficiency credit, one segment list per
        /// paragraph.</summary>
        public static IReadOnlyList<IReadOnlyList<PlanNoteSegment>> CreditParagraphs()
        {
            return new[]
            {
                LinkPhraseSpans.Split(CreditParagraph1, CreditPhrases),
                LinkPhraseSpans.Split(CreditParagraph2, CreditPhrases),
                LinkPhraseSpans.Split(CreditParagraph3, CreditPhrases),
            };
        }

        /// <summary>The Blish HUD credit, one paragraph.</summary>
        public static IReadOnlyList<IReadOnlyList<PlanNoteSegment>> BlishHudParagraphs()
        {
            return new[]
            {
                LinkPhraseSpans.Split(BlishHudParagraph, BlishHudPhrases),
            };
        }

        /// <summary>
        /// The shipped-library credit: an intro, one paragraph per library
        /// so each bullet gets a line of its own, then the licence note.
        /// The bullets go through the same phrase table as the prose, which
        /// is what proves a library name is not silently linked.
        /// </summary>
        public static IReadOnlyList<IReadOnlyList<PlanNoteSegment>> OpenSourceParagraphs()
        {
            var paragraphs = new List<IReadOnlyList<PlanNoteSegment>>
            {
                LinkPhraseSpans.Split(OpenSourceIntro, OpenSourcePhrases),
            };

            foreach (string line in OpenSourceLibraryLines)
            {
                paragraphs.Add(LinkPhraseSpans.Split(line, OpenSourcePhrases));
            }

            paragraphs.Add(LinkPhraseSpans.Split(OpenSourceLicenceNote, OpenSourcePhrases));
            return paragraphs;
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
        /// The "License" row's value: the licence name as one link to the
        /// licence file in the module's repository.
        /// </summary>
        public static IReadOnlyList<PlanNoteSegment> LicenseValue()
        {
            return new[]
            {
                PlanNoteSegment.Linked(LicenseName, IconWikiTarget.ExternalPage(LicenseUrl)),
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
