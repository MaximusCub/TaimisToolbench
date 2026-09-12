using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The About tab's shipped text, and the phrases in it that open a page.
    /// The copy is approved wording, so these tests pin the literal strings
    /// as well as the ranges: a reworded sentence is a change the author has
    /// to make deliberately.
    /// </summary>
    public class AboutTabTextTests
    {
        private static IReadOnlyList<PlanNoteSegment> Linked(
            IReadOnlyList<PlanNoteSegment> runs)
        {
            return runs.Where(r => r.IsLink).ToList();
        }

        [Fact]
        public void TheSectionHeading_IsTheApprovedWording()
        {
            Assert.Equal("Credits", AboutTabText.CreditsSectionTitle);
        }

        // The section title carries the word Credits, so the subheadings do
        // not repeat it.
        [Fact]
        public void TheThreeSubheadings_AreTheApprovedWording()
        {
            Assert.Equal("gw2efficiency", AboutTabText.Gw2EfficiencySubheading);
            Assert.Equal("Blish HUD", AboutTabText.BlishHudSubheading);
            Assert.Equal("Open source libraries", AboutTabText.OpenSourceSubheading);

            Assert.DoesNotContain("Credits", AboutTabText.Gw2EfficiencySubheading);
        }

        [Fact]
        public void TheCreditCopy_IsThreeParagraphsNamingTheThreeAuthors()
        {
            var paragraphs = AboutTabText.CreditParagraphs();

            Assert.Equal(3, paragraphs.Count);
            Assert.StartsWith(
                "This module was both inspired by and built upon",
                PlanNoteSegment.Join(paragraphs[0]));
            Assert.Equal(
                AboutTabText.CreditParagraph2, PlanNoteSegment.Join(paragraphs[1]));

            string thanks = PlanNoteSegment.Join(paragraphs[2]);
            Assert.Contains("David Reess (queicherius)", thanks);
            Assert.Contains("Saskia Van Leeuwen", thanks);
            Assert.Contains("Ecmel Tugcu", thanks);
        }

        [Fact]
        public void EveryParagraph_RejoinsToTheTextItWasSplitFrom()
        {
            var paragraphs = AboutTabText.CreditParagraphs();

            Assert.Equal(
                AboutTabText.CreditParagraph1, PlanNoteSegment.Join(paragraphs[0]));
            Assert.Equal(
                AboutTabText.CreditParagraph2, PlanNoteSegment.Join(paragraphs[1]));
            Assert.Equal(
                AboutTabText.CreditParagraph3, PlanNoteSegment.Join(paragraphs[2]));
        }

        [Fact]
        public void TheFirstParagraph_LinksTheBareNameOnceAndNothingElse()
        {
            var links = Linked(AboutTabText.CreditParagraphs()[0]);

            var only = Assert.Single(links);
            Assert.Equal("gw2efficiency", only.Text);
            Assert.Equal(AboutTabText.Gw2EfficiencyUrl, only.Link.BuildUrl());
        }

        [Fact]
        public void TheSecondParagraph_LinksTheSiteThePatreonAndThePayPal()
        {
            var links = Linked(AboutTabText.CreditParagraphs()[1]);

            Assert.Equal(
                new[] { "gw2efficiency.com", "their Patreon", "their PayPal" },
                links.Select(r => r.Text));
            Assert.Equal(
                new[]
                {
                    AboutTabText.Gw2EfficiencyUrl,
                    AboutTabText.Gw2EfficiencyPatreonUrl,
                    AboutTabText.Gw2EfficiencyPayPalUrl,
                },
                links.Select(r => r.Link.BuildUrl()));
        }

        [Fact]
        public void TheThanksParagraph_CarriesNoLink()
        {
            Assert.Empty(Linked(AboutTabText.CreditParagraphs()[2]));
        }

        [Fact]
        public void TheDomainIsNeverSplit_SoTheBareNameCannotSwallowIt()
        {
            var second = AboutTabText.CreditParagraphs()[1];

            // A ".com" left stranded outside the link is what a
            // shortest-match scan would produce.
            Assert.DoesNotContain(second, r => r.Text.StartsWith(".com"));
        }

        [Fact]
        public void TheBlishHudCredit_IsOneParagraphNamingTheOverlayAndItsLicence()
        {
            var paragraph = Assert.Single(AboutTabText.BlishHudParagraphs());
            string text = PlanNoteSegment.Join(paragraph);

            Assert.Equal(AboutTabText.BlishHudParagraph, text);
            Assert.Contains("the Guild Wars 2 overlay it is built on", text);
            Assert.Contains("MIT licensed", text);
        }

        [Fact]
        public void TheBlishHudCredit_LinksTheTeamAndNothingElse()
        {
            var links = Linked(AboutTabText.BlishHudParagraphs()[0]);

            var only = Assert.Single(links);
            Assert.Equal("Blish HUD team", only.Text);
            Assert.Equal(AboutTabText.BlishHudSourceUrl, only.Link.BuildUrl());
        }

        // "Blish HUD" appears three times in that sentence. Only the one
        // inside "Blish HUD team" is a link, which is what a phrase table
        // holding the bare product name would have got wrong.
        [Fact]
        public void TheBlishHudCredit_LeavesTheBareProductNamePlain()
        {
            var runs = AboutTabText.BlishHudParagraphs()[0];

            Assert.DoesNotContain(runs, r => r.IsLink && r.Text == "Blish HUD");
            Assert.Equal(3, CountOccurrences(AboutTabText.BlishHudParagraph, "Blish HUD"));
        }

        [Fact]
        public void TheOpenSourceCredit_IsAnIntro_FiveLibraryLines_ThenTheLicenceNote()
        {
            var paragraphs = AboutTabText.OpenSourceParagraphs();

            Assert.Equal(7, paragraphs.Count);
            Assert.Equal(AboutTabText.OpenSourceIntro, PlanNoteSegment.Join(paragraphs[0]));
            Assert.Equal(
                AboutTabText.OpenSourceLibraryLines.ToList(),
                paragraphs.Skip(1).Take(5).Select(PlanNoteSegment.Join).ToList());
            Assert.Equal(
                AboutTabText.OpenSourceLicenceNote, PlanNoteSegment.Join(paragraphs[6]));
        }

        // A library gets its own paragraph so it gets its own line: the
        // wrapper breaks on spaces only, so a newline inside one paragraph
        // would draw as a missing glyph rather than as a break. The library
        // names are read off the shipped list rather than restated here,
        // which is also what keeps a vendor name out of the test tree.
        [Fact]
        public void EveryLibraryLine_IsOneBulletedLibraryAndItsAuthor()
        {
            Assert.Equal(5, AboutTabText.OpenSourceLibraryLines.Count);

            var names = new List<string>();
            foreach (string line in AboutTabText.OpenSourceLibraryLines)
            {
                Assert.StartsWith("\u2022 ", line);
                Assert.DoesNotContain("\n", line);

                int by = line.IndexOf(", by ", StringComparison.Ordinal);
                Assert.True(by > 2, line);
                names.Add(line.Substring(2, by - 2));
                Assert.NotEmpty(line.Substring(by + 5));
            }

            Assert.Equal(names.Count, names.Distinct().Count());
        }

        [Fact]
        public void TheOpenSourceCredit_LinksOnlyTheNoticesFile_AndOnlyInTheLicenceNote()
        {
            var paragraphs = AboutTabText.OpenSourceParagraphs();

            var only = Assert.Single(Linked(paragraphs[6]));
            Assert.Equal("third-party notices file", only.Text);
            Assert.Equal(AboutTabText.ThirdPartyNoticesUrl, only.Link.BuildUrl());
            Assert.Equal(
                "https://github.com/MaximusCub/TaimisToolbench/blob/master/ref/THIRD-PARTY-NOTICES.txt",
                only.Link.BuildUrl());

            for (int i = 0; i < 6; i++)
            {
                Assert.Empty(Linked(paragraphs[i]));
            }
        }

        // The gw2efficiency phrase table must not reach the other two
        // subsections, and theirs must not reach it.
        [Fact]
        public void NoSubsectionBorrowsAnotherSubsectionsLinks()
        {
            var urls = AboutTabText.CreditParagraphs()
                .Concat(AboutTabText.BlishHudParagraphs())
                .Concat(AboutTabText.OpenSourceParagraphs())
                .SelectMany(Linked)
                .Select(r => r.Link.BuildUrl())
                .Distinct()
                .ToList();

            Assert.Equal(
                new[]
                {
                    AboutTabText.Gw2EfficiencyUrl,
                    AboutTabText.Gw2EfficiencyPatreonUrl,
                    AboutTabText.Gw2EfficiencyPayPalUrl,
                    AboutTabText.BlishHudSourceUrl,
                    AboutTabText.ThirdPartyNoticesUrl,
                },
                urls);
        }

        private static int CountOccurrences(string text, string needle)
        {
            int count = 0;
            int at = text.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }

            return count;
        }

        [Fact]
        public void BuiltWithValue_LinksOnlyTheWordInsideTheBrackets()
        {
            var runs = AboutTabText.BuiltWithValue(">=1.3.0");

            Assert.Equal(
                new[] { "Blish HUD >=1.3.0 (", "source", ")" }, runs.Select(r => r.Text));
            Assert.Equal(new[] { false, true, false }, runs.Select(r => r.IsLink));
            Assert.Equal(
                AboutTabText.BlishHudSourceUrl, runs[1].Link.BuildUrl());
            Assert.Equal("https://github.com/blish-hud/Blish-HUD", runs[1].Link.BuildUrl());
        }

        [Fact]
        public void SourceValue_IsOneLinkWhenTheManifestCarriesAUrl()
        {
            var runs = AboutTabText.SourceValue(
                "https://github.com/MaximusCub/TaimisToolbench", "Not available");

            var only = Assert.Single(runs);
            Assert.True(only.IsLink);
            Assert.Equal("https://github.com/MaximusCub/TaimisToolbench", only.Text);
            Assert.Equal(only.Text, only.Link.BuildUrl());
        }

        [Fact]
        public void SourceValue_FallsBackToPlainTextWhenThereIsNoUrlToOpen()
        {
            var missing = Assert.Single(AboutTabText.SourceValue(null, "Not available"));
            Assert.False(missing.IsLink);
            Assert.Equal("Not available", missing.Text);

            // http, a file path or a custom scheme is not something the
            // launcher will open, so it must not draw as a link.
            var insecure = Assert.Single(
                AboutTabText.SourceValue("http://example.com", "Not available"));
            Assert.False(insecure.IsLink);
            Assert.Equal("http://example.com", insecure.Text);
        }
    }
}
