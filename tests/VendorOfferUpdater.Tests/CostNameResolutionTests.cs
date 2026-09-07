using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using VendorOfferUpdater;
using VendorOfferUpdater.Tests.Helpers;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    /// <summary>
    /// A vendor's price is written on the wiki as display text, and that text
    /// and the page it names disagree far more often than they agree: plural
    /// forms, a doubled space, a reworded chest name. Matching the text
    /// exactly left 1,639 cost lines unpriced. These tests pin what the two
    /// answers now are - the page a name points at, and whether that page is
    /// an item or a wallet currency - and pin that a request that failed is
    /// still not an answer.
    /// </summary>
    public class CostNameResolutionTests
    {
        private static QueryOptions FastOptions(int maxAttempts = 2)
        {
            return new QueryOptions
            {
                DelayBetweenRequestsMs = 0,
                RetryBackoffBaseMs = 0,
                MaxAttempts = maxAttempts,
                MaxTotalRequests = 2000,
                MaxRuntime = TimeSpan.FromMinutes(5),
            };
        }

        private static (WikiSmwClient Client, FakeHttpHandler Handler, HttpClient Http) CreateClient()
        {
            var handler = new FakeHttpHandler();
            var httpClient = new HttpClient(handler);
            return (new WikiSmwClient(httpClient), handler, httpClient);
        }

        /// <summary>
        /// Builds an action=query answer in MediaWiki's shape: "normalized"
        /// and "redirects" are separate arrays, and "pages" is keyed by page
        /// id, or by a negative number with a "missing" key for a title that
        /// has no page.
        /// </summary>
        private static string TitleAnswer(
            (string From, string To)[] normalized = null,
            (string From, string To)[] redirects = null,
            (string Title, bool Missing)[] pages = null)
        {
            var query = new Dictionary<string, object>();

            if (normalized != null)
            {
                query["normalized"] = Hops(normalized);
            }

            if (redirects != null)
            {
                query["redirects"] = Hops(redirects);
            }

            var pageMap = new Dictionary<string, object>();
            int index = 1;
            foreach (var (title, missing) in pages ?? Array.Empty<(string, bool)>())
            {
                var page = new Dictionary<string, object> { ["ns"] = 0, ["title"] = title };
                if (missing)
                {
                    page["missing"] = string.Empty;
                    pageMap["-" + index] = page;
                }
                else
                {
                    page["pageid"] = 1000 + index;
                    pageMap[(1000 + index).ToString()] = page;
                }

                index++;
            }

            query["pages"] = pageMap;

            return JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["batchcomplete"] = string.Empty,
                ["query"] = query,
            });
        }

        private static List<Dictionary<string, string>> Hops((string From, string To)[] hops)
        {
            return hops
                .Select(hop => new Dictionary<string, string>
                {
                    ["from"] = hop.From,
                    ["to"] = hop.To,
                })
                .ToList();
        }

        private static void MapTitles(FakeHttpHandler handler, string body)
        {
            handler.MapUrl(url => url.Contains("action=query"), body);
        }

        private static async Task<Gw2ApiHelper> CurrenciesAsync(
            FakeHttpHandler handler, HttpClient httpClient, params (int Id, string Name)[] currencies)
        {
            string ids = "[" + string.Join(",", currencies.Select(c => c.Id)) + "]";
            string details = "[" + string.Join(
                ",",
                currencies.Select(c => $"{{\"id\":{c.Id},\"name\":\"{c.Name}\"}}")) + "]";

            handler.MapUrl(
                url => url.Contains("/v2/currencies") && !url.Contains("ids="), ids);
            handler.MapUrl(url => url.Contains("/v2/currencies?ids="), details);

            var helper = new Gw2ApiHelper(httpClient);
            await helper.LoadCurrenciesAsync();
            return helper;
        }

        // -- Which page a name points at ----------------------------
        [Fact]
        public async Task ARedirectResolvesToItsTarget()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;
            MapTitles(handler, TitleAnswer(
                redirects: new[] { ("Trade Contracts", "Trade Contract") },
                pages: new[] { ("Trade Contract", false) }));

            var titles = await client.ResolveTitlesAsync(
                new[] { "Trade Contracts" }, default, FastOptions());

            Assert.Equal("Trade Contract", titles.Resolved["Trade Contracts"]);
            Assert.Contains("Trade Contracts", titles.Answered);
        }

        [Fact]
        public async Task ANormalizedTitleResolvesWithoutARedirect()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;

            // The live cache carries this name with two spaces in it. Nothing
            // in the redirects array reports it; title normalization does.
            MapTitles(handler, TitleAnswer(
                normalized: new[] { ("Ancient  Coin", "Ancient Coin") },
                pages: new[] { ("Ancient Coin", false) }));

            var titles = await client.ResolveTitlesAsync(
                new[] { "Ancient  Coin" }, default, FastOptions());

            Assert.Equal("Ancient Coin", titles.Resolved["Ancient  Coin"]);
        }

        [Fact]
        public async Task NormalizationAndARedirectChainInOneAnswer()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;
            MapTitles(handler, TitleAnswer(
                normalized: new[] { ("Globs  of Ectoplasm", "Globs of Ectoplasm") },
                redirects: new[] { ("Globs of Ectoplasm", "Glob of Ectoplasm") },
                pages: new[] { ("Glob of Ectoplasm", false) }));

            var titles = await client.ResolveTitlesAsync(
                new[] { "Globs  of Ectoplasm" }, default, FastOptions());

            Assert.Equal("Glob of Ectoplasm", titles.Resolved["Globs  of Ectoplasm"]);
        }

        [Fact]
        public async Task ATitleThatNamesItsOwnPageIsAnsweredAndLeftAlone()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;
            MapTitles(handler, TitleAnswer(pages: new[] { ("Mystic Coin", false) }));

            var titles = await client.ResolveTitlesAsync(
                new[] { "Mystic Coin" }, default, FastOptions());

            Assert.Empty(titles.Resolved);
            Assert.Contains("Mystic Coin", titles.Answered);
        }

        [Fact]
        public async Task AHopThatPointsBackAtItsSourceTerminates()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;
            MapTitles(handler, TitleAnswer(
                redirects: new[] { ("Loop A", "Loop B"), ("Loop B", "Loop A") },
                pages: new[] { ("Loop A", false) }));

            var titles = await client.ResolveTitlesAsync(
                new[] { "Loop A" }, default, FastOptions());

            Assert.Equal("Loop B", titles.Resolved["Loop A"]);
        }

        [Fact]
        public async Task NamesAreAskedFiftyToARequest()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;
            var names = Enumerable.Range(1, 60).Select(i => "Name " + i).ToList();
            handler.Enqueue(TitleAnswer());
            handler.Enqueue(TitleAnswer());

            var titles = await client.ResolveTitlesAsync(names, default, FastOptions());

            Assert.Equal(2, handler.RequestedUrls.Count);
            Assert.Equal(60, titles.Answered.Count);
        }

        [Fact]
        public async Task ARefusedBatchIsNotAnAnswer()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;
            MapTitles(handler, WikiJsonBuilder.BuildMaxLagError());

            var titles = await client.ResolveTitlesAsync(
                new[] { "Trade Contracts" }, default, FastOptions());

            Assert.Empty(titles.Answered);
            Assert.Empty(titles.Resolved);

            var unresolved = Assert.Single(client.UnresolvedSections);
            Assert.Equal("title-batch", unresolved.Kind);
            Assert.Equal("maxlag", unresolved.ErrorCode);
        }

        // -- Whether that page is an item or a currency -------------
        [Fact]
        public async Task ACurrencyPageIsPricedAsACurrencyNotSkipped()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;

            // The wiki titles the page in the singular, the API names the
            // currency in the plural, and the page carries no item id.
            MapTitles(handler, TitleAnswer(
                pages: new[] { ("Tale of Dungeon Delving", false) }));
            handler.MapUrl(url => url.Contains("action=ask"), WikiJsonBuilder.BuildEmpty());

            var helper = await CurrenciesAsync(
                handler, httpClient, (69, "Tales of Dungeon Delving"));
            var cache = new ItemIdCache();

            await Program.ResolveUnknownNamesAsync(
                new[] { "Tale of Dungeon Delving" },
                client,
                helper,
                cache,
                FastOptions(),
                default);

            Assert.Equal(69, helper.ResolveCurrencyId("Tale of Dungeon Delving"));
            Assert.Equal(
                "Tales of Dungeon Delving", cache.CurrencyNames["Tale of Dungeon Delving"]);
            Assert.Empty(cache.Misses);
        }

        [Fact]
        public async Task AnItemPageIsAskedAboutByItsRealTitleAndCachedUnderTheDisplayString()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;

            MapTitles(handler, TitleAnswer(
                redirects: new[] { ("Ectoplasm", "Glob of Ectoplasm") },
                pages: new[] { ("Glob of Ectoplasm", false) }));
            handler.MapUrl(
                url => url.Contains("action=ask"),
                new WikiJsonBuilder().AddResult("Glob of Ectoplasm", gameId: 19721).Build());

            var helper = await CurrenciesAsync(handler, httpClient, (2, "Karma"));
            var cache = new ItemIdCache();

            await Program.ResolveUnknownNamesAsync(
                new[] { "Ectoplasm" }, client, helper, cache, FastOptions(), default);

            Assert.Equal(19721, cache.Ids["Ectoplasm"]);
            Assert.Empty(cache.CurrencyNames);

            // The display string is what the cost line spells; the redirect
            // target is what the wiki was asked about.
            string askUrl = Assert.Single(handler.RequestedUrls, u => u.Contains("action=ask"));
            Assert.Contains("[[Glob of Ectoplasm]]", Uri.UnescapeDataString(askUrl));
        }

        [Fact]
        public async Task ACurrencyRemovedFromTheGameStaysUnresolved()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;

            // Glory has a wiki page and no wallet entry. A page existing is
            // not proof of a live currency, and an offer priced in one no
            // account can hold is not a route.
            MapTitles(handler, TitleAnswer(pages: new[] { ("Glory", false) }));
            handler.MapUrl(url => url.Contains("action=ask"), WikiJsonBuilder.BuildEmpty());

            var helper = await CurrenciesAsync(handler, httpClient, (2, "Karma"));
            var cache = new ItemIdCache();

            await Program.ResolveUnknownNamesAsync(
                new[] { "Glory" }, client, helper, cache, FastOptions(), default);

            Assert.Null(helper.ResolveCurrencyId("Glory"));
            Assert.Empty(cache.CurrencyNames);
            Assert.True(cache.Misses.ContainsKey("Glory"));
        }

        // -- A failed request is still not an answer ----------------
        [Fact]
        public async Task AFailedTitleRequestCachesNothingAndAsksNothingFurther()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;

            MapTitles(handler, WikiJsonBuilder.BuildMaxLagError());
            var helper = await CurrenciesAsync(handler, httpClient, (2, "Karma"));
            var cache = new ItemIdCache();

            await Program.ResolveUnknownNamesAsync(
                new[] { "Trade Contracts" }, client, helper, cache, FastOptions(), default);

            Assert.Equal(0, cache.Count);
            Assert.False(cache.Contains("Trade Contracts"));
            Assert.DoesNotContain(handler.RequestedUrls, u => u.Contains("action=ask"));
        }

        [Fact]
        public async Task AFailedItemRequestStillCachesNothing()
        {
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;

            MapTitles(handler, TitleAnswer(
                redirects: new[] { ("Ectoplasm", "Glob of Ectoplasm") },
                pages: new[] { ("Glob of Ectoplasm", false) }));
            handler.MapUrl(
                url => url.Contains("action=ask"), WikiJsonBuilder.BuildMaxLagError());

            var helper = await CurrenciesAsync(handler, httpClient, (2, "Karma"));
            var cache = new ItemIdCache();

            await Program.ResolveUnknownNamesAsync(
                new[] { "Ectoplasm" }, client, helper, cache, FastOptions(), default);

            Assert.Equal(0, cache.Count);
            Assert.False(cache.Contains("Ectoplasm"));
        }

        // -- What a later run inherits ------------------------------
        [Fact]
        public async Task ACachedCurrencyNameIsRegisteredWithoutAskingTheWikiAgain()
        {
            var (_, handler, httpClient) = CreateClient();
            using var __ = httpClient;
            var helper = await CurrenciesAsync(
                handler, httpClient, (69, "Tales of Dungeon Delving"));

            var cache = new ItemIdCache();
            cache.RecordCurrencyName("Tale of Dungeon Delving", "Tales of Dungeon Delving");

            Program.RegisterCachedCurrencyNames(cache, helper);

            Assert.Equal(69, helper.ResolveCurrencyId("Tale of Dungeon Delving"));
            Assert.True(cache.Contains("Tale of Dungeon Delving"));
        }

        [Fact]
        public async Task ACachedNameForACurrencyTheApiNoLongerHasIsDropped()
        {
            var (_, handler, httpClient) = CreateClient();
            using var __ = httpClient;
            var helper = await CurrenciesAsync(handler, httpClient, (2, "Karma"));

            var cache = new ItemIdCache();
            cache.RecordCurrencyName("Gaeting Crystal", "Gaeting Crystal");

            Program.RegisterCachedCurrencyNames(cache, helper);

            // Left in place it would price offers in a retired currency for
            // ever, because a settled name is never asked about again.
            Assert.Empty(cache.CurrencyNames);
            Assert.False(cache.Contains("Gaeting Crystal"));
        }

        [Fact]
        public async Task AnUnlockRequirementNameIsSettledAsAnItem()
        {
            // A vendor row's "Has requirement" spells a recipe sheet the way
            // a cost line spells an item, so both names take this route. The
            // sheet has to come out of it with an item id: Program.Main feeds
            // that id to ResolveUnlockRecipeIdsAsync, and a sheet settled as
            // a currency or as a miss would leave the offer's gate untagged.
            var (client, handler, httpClient) = CreateClient();
            using var _ = httpClient;

            MapTitles(handler, TitleAnswer(
                pages: new[] { ("Recipe: Legendary Obsidian Armor", false) }));
            handler.MapUrl(
                url => url.Contains("action=ask"),
                new WikiJsonBuilder()
                    .AddResult("Recipe: Legendary Obsidian Armor", gameId: 101483)
                    .Build());

            var helper = await CurrenciesAsync(handler, httpClient, (2, "Karma"));
            var cache = new ItemIdCache();

            await Program.ResolveUnknownNamesAsync(
                new[] { "Recipe: Legendary Obsidian Armor" },
                client,
                helper,
                cache,
                FastOptions(),
                default);

            Assert.Equal(101483, cache.Ids["Recipe: Legendary Obsidian Armor"]);
            Assert.Empty(cache.CurrencyNames);
            Assert.Empty(cache.Misses);
        }
    }
}
