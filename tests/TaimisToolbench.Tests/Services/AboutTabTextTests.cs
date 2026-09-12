using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// The About tab's shipped text, and the four phrases in it that open a
    /// page. The copy is approved wording, so these tests pin the literal
    /// strings as well as the ranges: a reworded sentence is a change the
    /// author has to make deliberately.
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
            Assert.Equal("Credits - gw2efficiency", AboutTabText.CreditsSectionTitle);
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
