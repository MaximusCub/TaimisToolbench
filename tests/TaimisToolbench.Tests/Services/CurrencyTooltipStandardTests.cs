using System.Collections.Generic;
using System.Linq;
using TaimisToolbench.Models;
using TaimisToolbench.Services;
using TaimisToolbench.Tests.Helpers;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    /// <summary>
    /// Every currency icon in the module shows the same box for the same
    /// currency. The owner found a Settings tab icon showing the game's
    /// full prose while the same currency in the Recipe Tree showed only
    /// its name, because each caller built the facts out of whatever it
    /// happened to hold.
    /// <para>
    /// These ask the facility for output rather than reading call sites.
    /// A caller now passes an id and a size bucket, so there is one input
    /// and one answer, and a test can compare answers instead of grepping
    /// for argument shapes.
    /// </para>
    /// </summary>
    public class CurrencyTooltipStandardTests
    {
        private const int Karma = 1;

        private static IReadOnlyDictionary<int, CurrencyMetadata> Metadata()
        {
            return new Dictionary<int, CurrencyMetadata>
            {
                {
                    Karma,
                    new CurrencyMetadata
                    {
                        CurrencyId = Karma,
                        Name = "Karma",
                        IconUrl = "karma.png",
                        Description =
                            "Earned through various activities. Spent at vendors throughout the world.",
                    }
                },
            };
        }

        private static TooltipContent Compose(CurrencyTooltipFacts facts)
        {
            return SecondTooltipBox.Attach(
                CurrencyTooltipComposer.BuildContent(facts),
                SecondTooltipBox.Compose(
                    (TooltipContent)null, IconWikiTarget.ItemPage(facts.Name).Hint));
        }

        /// <summary>
        /// The four lines the game shows, in the game's order, measured off
        /// the owner's own capture of the in-game Karma tooltip.
        /// </summary>
        [Fact]
        public void TheFirstBoxIsTheGamesOwnFourLines()
        {
            var content = CurrencyTooltipComposer.BuildContent(
                CurrencyTooltipFacts.ForCurrencyId(
                    Karma,
                    Metadata(),
                    new Dictionary<int, int> { { Karma, 5739844 } }));

            Assert.Equal(
                new[]
                {
                    "Karma",
                    "5,739,844 in Wallet",
                    "Earned through various activities. Spent at vendors throughout the world.",
                    "Currency",
                },
                content.ToPlainLines().ToArray());
        }

        [Fact]
        public void TheSecondBoxEndsWithTheWikiLine()
        {
            var content = Compose(CurrencyTooltipFacts.ForCurrencyId(Karma, Metadata(), (int?)null));

            Assert.True(content.HasExtra);
            Assert.Equal(new[] { IconWikiTarget.HintText }, content.ToExtraLines());
        }

        /// <summary>
        /// The counterexample, as a test. The Settings tab holds a metadata
        /// dictionary and no wallet; the plan's tables hold both; the tree
        /// holds the plan's. Feed each surface's own inputs to the one
        /// resolver and the boxes agree wherever the inputs do.
        /// </summary>
        [Fact]
        public void EverySurfaceShowsTheSameBoxForTheSameCurrency()
        {
            var metadata = Metadata();
            var wallet = new Dictionary<int, int> { { Karma, 5739844 } };

            // The Settings grid and the Ranker read no wallet snapshot.
            var settings = Compose(CurrencyTooltipFacts.ForCurrencyId(Karma, metadata, (int?)null));
            var ranker = Compose(CurrencyTooltipFacts.ForCurrencyId(Karma, metadata, (int?)null));

            // The plan's tables and the Recipe Tree read the plan's.
            var table = Compose(CurrencyTooltipFacts.ForCurrencyId(Karma, metadata, wallet));
            var tree = Compose(CurrencyTooltipFacts.ForCurrencyId(Karma, metadata, wallet));

            // The Snapshot tab answers one entry at a time.
            var snapshot = Compose(
                CurrencyTooltipFacts.ForCurrencyEntry(Karma, metadata[Karma], 5739844));

            Assert.Equal(settings.ToPlainLines(), ranker.ToPlainLines());
            Assert.Equal(table.ToPlainLines(), tree.ToPlainLines());
            Assert.Equal(table.ToPlainLines(), snapshot.ToPlainLines());

            // The only line that differs is the one that depends on a
            // wallet the surface did not read.
            Assert.Equal(
                table.ToPlainLines().Where(l => !l.EndsWith(CurrencyTooltipComposer.WalletSuffix)),
                settings.ToPlainLines());
        }

        /// <summary>
        /// The prose is what the Recipe Tree was missing. It comes from the
        /// id, so a surface cannot show a currency without it once
        /// /v2/currencies has answered.
        /// </summary>
        [Fact]
        public void TheDescriptionComesFromTheIdAndNotFromTheCaller()
        {
            var facts = CurrencyTooltipFacts.ForCurrencyId(Karma, Metadata(), (int?)null);

            Assert.Equal(
                "Earned through various activities. Spent at vendors throughout the world.",
                facts.Description);
            Assert.Equal("Karma", facts.Name);
            Assert.Equal("karma.png", facts.IconUrl);
        }

        /// <summary>
        /// Metadata arrives from the API after the view has drawn. "Not
        /// known yet" drops the line it feeds; it never reads as a fact.
        /// </summary>
        [Fact]
        public void BeforeTheMetadataArrivesTheBoxShowsOnlyWhatIsKnown()
        {
            var facts = CurrencyTooltipFacts.ForCurrencyId(
                Karma, (IReadOnlyDictionary<int, CurrencyMetadata>)null, (int?)null);

            Assert.Null(facts.Description);
            Assert.Null(facts.IconUrl);
            Assert.Null(facts.WalletQuantity);

            var lines = CurrencyTooltipComposer.BuildContent(facts).ToPlainLines();
            Assert.DoesNotContain(lines, l => l.EndsWith(CurrencyTooltipComposer.WalletSuffix));
            Assert.NotEmpty(lines[0]);
        }

        /// <summary>
        /// A holding of zero is a fact and prints. An unread wallet is not
        /// and does not. The two used to be the same null.
        /// </summary>
        [Fact]
        public void AConfirmedZeroPrintsAndAnUnreadWalletDoesNot()
        {
            var zero = CurrencyTooltipFacts.ForCurrencyId(
                Karma, Metadata(), new Dictionary<int, int> { { Karma, 0 } });
            var unread = CurrencyTooltipFacts.ForCurrencyId(
                Karma, Metadata(), new Dictionary<int, int>());

            Assert.Contains("0 in Wallet", CurrencyTooltipComposer.BuildContent(zero).ToPlainText());
            Assert.DoesNotContain("in Wallet", CurrencyTooltipComposer.BuildContent(unread).ToPlainText());
        }
    }
}
