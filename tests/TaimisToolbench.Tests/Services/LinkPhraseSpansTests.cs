using System;
using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Which character ranges of the About tab's credit copy are links.
    /// The view draws one label per run and rules a line under the linked
    /// ones, so a range that started or ended a character out would colour
    /// and underline the wrong words.
    /// </summary>
    public class LinkPhraseSpansTests
    {
        private const string Site = "https://gw2efficiency.com";
        private const string Patreon = "https://www.patreon.com/gw2efficiency";

        private static readonly IReadOnlyList<LinkPhrase> Phrases = new[]
        {
            new LinkPhrase("gw2efficiency.com", Site),
            new LinkPhrase("gw2efficiency", Site),
            new LinkPhrase("their Patreon", Patreon),
        };

        // A fixed-width font: every character is 10px wide, so a budget is
        // a character count and every case below reads as one.
        private static readonly Func<string, int> Fixed10 = s => (s ?? "").Length * 10;

        private static string TextOf(IReadOnlyList<PlanNoteSegment> pieces)
        {
            return string.Concat(pieces.Select(p => p.Text));
        }

        [Fact]
        public void APhraseInTheMiddle_BecomesThreeRuns()
        {
            var runs = LinkPhraseSpans.Split("the gw2efficiency team", Phrases);

            Assert.Equal(new[] { "the ", "gw2efficiency", " team" }, runs.Select(r => r.Text));
            Assert.Equal(new[] { false, true, false }, runs.Select(r => r.IsLink));
        }

        [Fact]
        public void TheLongestPhraseAtAnIndexWins_SoTheDomainSurvivesWhole()
        {
            var spans = LinkPhraseSpans.Find("checking out gw2efficiency.com, then", Phrases);

            var span = Assert.Single(spans);
            Assert.Equal(13, span.Start);
            Assert.Equal("gw2efficiency.com".Length, span.Length);

            var runs = LinkPhraseSpans.Split("checking out gw2efficiency.com, then", Phrases);
            Assert.Equal(
                new[] { "checking out ", "gw2efficiency.com", ", then" },
                runs.Select(r => r.Text));
        }

        [Fact]
        public void TwoPhrasesInOneSentence_EachGetTheirOwnRange()
        {
            var spans = LinkPhraseSpans.Find(
                "visit gw2efficiency.com or their Patreon today", Phrases);

            Assert.Equal(2, spans.Count);
            Assert.Equal(Site, spans[0].Url);
            Assert.Equal(Patreon, spans[1].Url);
            Assert.Equal(6, spans[0].Start);
            Assert.Equal(27, spans[1].Start);
        }

        [Fact]
        public void APhraseAtEitherEnd_EmitsNoEmptyRun()
        {
            var leading = LinkPhraseSpans.Split("gw2efficiency is good", Phrases);
            Assert.Equal(new[] { "gw2efficiency", " is good" }, leading.Select(r => r.Text));

            var trailing = LinkPhraseSpans.Split("go to gw2efficiency", Phrases);
            Assert.Equal(new[] { "go to ", "gw2efficiency" }, trailing.Select(r => r.Text));
        }

        [Fact]
        public void TextWithNoPhrase_StaysOnePlainRun()
        {
            var runs = LinkPhraseSpans.Split("nothing to open here", Phrases);

            var only = Assert.Single(runs);
            Assert.False(only.IsLink);
            Assert.Equal("nothing to open here", only.Text);
        }

        [Fact]
        public void MatchingIsCaseSensitive_SoAMiscasedWordStaysPlain()
        {
            var runs = LinkPhraseSpans.Split("GW2Efficiency is not the phrase", Phrases);

            Assert.All(runs, r => Assert.False(r.IsLink));
        }

        [Fact]
        public void ALinkStraddlingAWrapPoint_KeepsItsTargetOnBothLines()
        {
            // 190px is 19 characters, which is exactly "supported via
            // their". The wrap therefore falls inside "their Patreon".
            var runs = LinkPhraseSpans.Split("supported via their Patreon now", Phrases);

            var wrapped = NoteSegmentWrap.Wrap(runs, 190, 190, Fixed10, 24);

            Assert.Equal(2, wrapped.Lines.Count);
            Assert.False(wrapped.Truncated);
            Assert.Equal("supported via their", TextOf(wrapped.Lines[0]));
            Assert.Equal("Patreon now", TextOf(wrapped.Lines[1]));

            // The phrase is now two runs on two lines. Both still open the
            // same page, and the words around them are still plain.
            var linked = wrapped.Lines
                .SelectMany(line => line)
                .Where(piece => piece.IsLink)
                .ToList();

            Assert.Equal(new[] { "their", "Patreon" }, linked.Select(p => p.Text));
            Assert.All(linked, p => Assert.Equal(Patreon, p.Link.BuildUrl()));

            Assert.Equal(
                new[] { "supported via ", "their" },
                wrapped.Lines[0].Select(p => p.Text));
            Assert.Equal(new[] { false, true }, wrapped.Lines[0].Select(p => p.IsLink));
            Assert.Equal(new[] { "Patreon", " now" }, wrapped.Lines[1].Select(p => p.Text));
            Assert.Equal(new[] { true, false }, wrapped.Lines[1].Select(p => p.IsLink));
        }

        [Fact]
        public void EmptyOrAbsentInput_ProducesNothingToDraw()
        {
            Assert.Empty(LinkPhraseSpans.Split("", Phrases));
            Assert.Empty(LinkPhraseSpans.Split(null, Phrases));
            Assert.Empty(LinkPhraseSpans.Find("gw2efficiency", null));

            var unmatched = LinkPhraseSpans.Split("gw2efficiency", new LinkPhrase[0]);
            Assert.Equal("gw2efficiency", Assert.Single(unmatched).Text);
        }
    }
}
