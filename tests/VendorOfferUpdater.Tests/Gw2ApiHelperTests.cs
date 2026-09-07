using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using VendorOfferUpdater;
using VendorOfferUpdater.Tests.Helpers;
using Xunit;

namespace VendorOfferUpdater.Tests
{
    public class Gw2ApiHelperTests
    {
        private static (Gw2ApiHelper helper, FakeHttpHandler handler, HttpClient httpClient) CreateHelper()
        {
            var handler = new FakeHttpHandler();
            var httpClient = new HttpClient(handler);
            var helper = new Gw2ApiHelper(httpClient);
            return (helper, handler, httpClient);
        }

        private static void SetupCurrencyResponses(FakeHttpHandler handler, string idsJson, string detailsJson)
        {
            handler.MapUrl(
                url => url.Contains("/v2/currencies") && !url.Contains("ids="),
                idsJson);
            handler.MapUrl(
                url => url.Contains("/v2/currencies?ids="),
                detailsJson);
        }

        [Fact]
        public async Task LoadCurrenciesAsync_ParsesApiResponse()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;
            SetupCurrencyResponses(handler,
                "[2,4,15]",
                "[{\"id\":2,\"name\":\"Karma\"},{\"id\":4,\"name\":\"Gem\"},{\"id\":15,\"name\":\"Badge of Honor\"}]");

            await helper.LoadCurrenciesAsync();

            Assert.Equal(2, helper.ResolveCurrencyId("Karma"));
            Assert.Equal(4, helper.ResolveCurrencyId("Gem"));
            Assert.Equal(15, helper.ResolveCurrencyId("Badge of Honor"));
        }

        [Fact]
        public async Task LoadCurrenciesAsync_BatchesOver200()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;

            // Generate 250 IDs
            var ids = Enumerable.Range(1, 250).ToList();
            string idsJson = "[" + string.Join(",", ids) + "]";

            // First batch: IDs 1-200
            var batch1 = ids.Take(200).Select(i =>
                $"{{\"id\":{i},\"name\":\"Currency{i}\"}}");
            string batch1Json = "[" + string.Join(",", batch1) + "]";

            // Second batch: IDs 201-250
            var batch2 = ids.Skip(200).Select(i =>
                $"{{\"id\":{i},\"name\":\"Currency{i}\"}}");
            string batch2Json = "[" + string.Join(",", batch2) + "]";

            handler.MapUrl(
                url => url.Contains("/v2/currencies") && !url.Contains("ids="),
                idsJson);

            // Queue the two batch responses (order matters)
            handler.Enqueue(batch1Json);
            handler.Enqueue(batch2Json);

            await helper.LoadCurrenciesAsync();

            // 1 IDs request + 2 batch requests = 3 total
            Assert.Equal(3, handler.RequestedUrls.Count);
            Assert.Equal(1, helper.ResolveCurrencyId("Currency1"));
            Assert.Equal(250, helper.ResolveCurrencyId("Currency250"));
        }

        [Theory]
        [InlineData("Coin")]
        [InlineData("Coins")]
        [InlineData("Gold")]
        [InlineData("Copper")]
        [InlineData("Silver")]
        public void CoinAliases_ReturnCurrencyId1(string alias)
        {
            var (helper, _, httpClient) = CreateHelper();
            using var __ = httpClient;
            // No LoadCurrenciesAsync needed - aliases are hardcoded
            Assert.Equal(1, helper.ResolveCurrencyId(alias));
        }

        [Fact]
        public void CoinAliases_AreCaseInsensitive()
        {
            var (helper, _, httpClient) = CreateHelper();
            using var __ = httpClient;
            Assert.Equal(1, helper.ResolveCurrencyId("coin"));
            Assert.Equal(1, helper.ResolveCurrencyId("COIN"));
            Assert.Equal(1, helper.ResolveCurrencyId("gOLD"));
        }

        [Fact]
        public async Task ResolveCurrencyId_CaseInsensitiveAfterLoad()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;
            SetupCurrencyResponses(handler,
                "[2]",
                "[{\"id\":2,\"name\":\"Karma\"}]");

            await helper.LoadCurrenciesAsync();

            Assert.Equal(2, helper.ResolveCurrencyId("karma"));
            Assert.Equal(2, helper.ResolveCurrencyId("KARMA"));
        }

        [Fact]
        public void ResolveCurrencyId_Unknown_ReturnsNull()
        {
            var (helper, _, httpClient) = CreateHelper();
            using var __ = httpClient;
            Assert.Null(helper.ResolveCurrencyId("Nonexistent Currency"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void ResolveCurrencyId_NullOrEmpty_ReturnsNull(string input)
        {
            var (helper, _, httpClient) = CreateHelper();
            using var __ = httpClient;
            Assert.Null(helper.ResolveCurrencyId(input));
        }

        // -- Wiki page titles against API currency names ------------
        [Fact]
        public async Task MatchCurrencyName_SingularWikiTitle_FindsThePluralApiName()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;
            SetupCurrencyResponses(handler,
                "[33,69]",
                "[{\"id\":33,\"name\":\"Ascended Shards of Glory\"}," +
                "{\"id\":69,\"name\":\"Tales of Dungeon Delving\"}]");

            await helper.LoadCurrenciesAsync();

            Assert.Equal(
                "Tales of Dungeon Delving", helper.MatchCurrencyName("Tale of Dungeon Delving"));
            Assert.Equal(
                "Ascended Shards of Glory", helper.MatchCurrencyName("Ascended Shard of Glory"));
        }

        [Fact]
        public async Task MatchCurrencyName_ExactName_IsReturnedUnchanged()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;
            SetupCurrencyResponses(handler, "[66]", "[{\"id\":66,\"name\":\"Ancient Coin\"}]");

            await helper.LoadCurrenciesAsync();

            Assert.Equal("Ancient Coin", helper.MatchCurrencyName("Ancient Coin"));
        }

        [Fact]
        public async Task MatchCurrencyName_TwoCurrenciesSharingAStem_MatchesNeither()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;
            SetupCurrencyResponses(handler,
                "[90,91]",
                "[{\"id\":90,\"name\":\"Prophet Shard\"},{\"id\":91,\"name\":\"Prophet Shards\"}]");

            await helper.LoadCurrenciesAsync();

            // Both are still reachable by their exact names; only the guess
            // between them is refused.
            Assert.Equal(90, helper.ResolveCurrencyId("Prophet Shard"));
            Assert.Null(helper.MatchCurrencyName("Prophets Shard"));
        }

        [Fact]
        public async Task MatchCurrencyName_ACurrencyTheApiDoesNotHave_ReturnsNull()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;
            SetupCurrencyResponses(handler, "[2]", "[{\"id\":2,\"name\":\"Karma\"}]");

            await helper.LoadCurrenciesAsync();

            Assert.Null(helper.MatchCurrencyName("Glory"));
            Assert.Null(helper.MatchCurrencyName("Influence"));
        }

        [Fact]
        public async Task AddCurrencyAlias_MakesTheDisplayStringResolve()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;
            SetupCurrencyResponses(handler, "[3]", "[{\"id\":3,\"name\":\"Laurel\"}]");

            await helper.LoadCurrenciesAsync();

            Assert.True(helper.AddCurrencyAlias("Laurels", "Laurel"));
            Assert.Equal(3, helper.ResolveCurrencyId("Laurels"));
        }

        [Fact]
        public async Task AddCurrencyAlias_RefusesANameTheApiDoesNotHave()
        {
            var (helper, handler, httpClient) = CreateHelper();
            using var _ = httpClient;
            SetupCurrencyResponses(handler, "[2]", "[{\"id\":2,\"name\":\"Karma\"}]");

            await helper.LoadCurrenciesAsync();

            Assert.False(helper.AddCurrencyAlias("Glory", "Glory"));
            Assert.Null(helper.ResolveCurrencyId("Glory"));
        }
    }
}
