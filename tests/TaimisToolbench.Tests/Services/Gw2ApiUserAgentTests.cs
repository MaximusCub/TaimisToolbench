using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using TaimisToolbench.Services;
using Xunit;

namespace TaimisToolbench.Tests.Services
{
    public class Gw2ApiUserAgentTests
    {
        private class CapturingHandler : HttpMessageHandler
        {
            public string LastUserAgent { get; private set; }

            public int RequestCount { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(
                HttpRequestMessage request, CancellationToken cancellationToken)
            {
                RequestCount++;

                // Headers.ToString() renders the header the way it goes on
                // the wire, separators and all; the parsed UserAgent
                // collection would hide a wrong one.
                LastUserAgent = ReadUserAgentLine(request.Headers.ToString());
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]"),
                });
            }

            private static string ReadUserAgentLine(string headerBlock)
            {
                foreach (var line in headerBlock.Split('\n'))
                {
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("User-Agent:", StringComparison.Ordinal))
                    {
                        return trimmed.Substring("User-Agent:".Length).Trim();
                    }
                }

                return null;
            }
        }

        private class VersionedManifest
        {
            public VersionedManifest(object version)
            {
                Version = version;
            }

            public object Version { get; }
        }

        private class ThrowingManifest
        {
            public object Version
            {
                get { throw new InvalidOperationException("no version here"); }
            }
        }

        [Fact]
        public void Build_CarriesProductVersionAndContactAddress()
        {
            string agent = Gw2ApiUserAgent.Build("TaimisToolbench", "0.3.0");

            Assert.Equal(
                "TaimisToolbench/0.3.0 (+https://github.com/MaximusCub/TaimisToolbench)",
                agent);
        }

        [Fact]
        public async Task Apply_PutsTheAgentOnEveryRequestTheClientSends()
        {
            using (var handler = new CapturingHandler())
            using (var http = new HttpClient(handler))
            {
                Gw2ApiUserAgent.Apply(http, "TaimisToolbench", "0.3.0");
                await http.GetStringAsync("https://api.guildwars2.com/v2/build");

                Assert.Equal(1, handler.RequestCount);
                Assert.Equal(
                    "TaimisToolbench/0.3.0 (+https://github.com/MaximusCub/TaimisToolbench)",
                    handler.LastUserAgent);
            }
        }

        [Fact]
        public async Task Apply_TwiceLeavesOneAgent()
        {
            using (var handler = new CapturingHandler())
            using (var http = new HttpClient(handler))
            {
                Gw2ApiUserAgent.Apply(http, "TaimisToolbench", "0.3.0");
                Gw2ApiUserAgent.Apply(http, "TaimisToolbench", "0.4.0");
                await http.GetStringAsync("https://api.guildwars2.com/v2/build");

                Assert.Equal(
                    "TaimisToolbench/0.4.0 (+https://github.com/MaximusCub/TaimisToolbench)",
                    handler.LastUserAgent);
            }
        }

        // A manifest version is authored outside this project. ParseAdd throws
        // on a space or a bracket, and Module.Initialize has no catch around
        // the call, so an unsanitised version would fail the whole module load.
        [Theory]
        [InlineData("0.3.0 (dev)", "TaimisToolbench/0.3.0dev")]
        [InlineData("0.3.0-rc.1+42", "TaimisToolbench/0.3.0-rc.1+42")]
        [InlineData("\"0.3.0\"", "TaimisToolbench/0.3.0")]
        public async Task Apply_AcceptsAVersionHttpWouldReject(
            string version, string expectedProduct)
        {
            using (var handler = new CapturingHandler())
            using (var http = new HttpClient(handler))
            {
                Gw2ApiUserAgent.Apply(http, "TaimisToolbench", version);
                await http.GetStringAsync("https://api.guildwars2.com/v2/build");

                Assert.StartsWith(expectedProduct + " (+", handler.LastUserAgent);
            }
        }

        [Fact]
        public async Task Apply_StillIdentifiesWhenTheVersionIsUnreadable()
        {
            using (var handler = new CapturingHandler())
            using (var http = new HttpClient(handler))
            {
                Gw2ApiUserAgent.Apply(http, "TaimisToolbench", null);
                await http.GetStringAsync("https://api.guildwars2.com/v2/build");

                Assert.Equal(
                    "TaimisToolbench/0.0.0 (+https://github.com/MaximusCub/TaimisToolbench)",
                    handler.LastUserAgent);
            }
        }

        [Fact]
        public void Apply_OnANullClientDoesNothing()
        {
            Gw2ApiUserAgent.Apply(null, "TaimisToolbench", "0.3.0");
        }

        [Fact]
        public void ReadManifestVersion_ReadsTheVersionPropertyAsText()
        {
            Assert.Equal(
                "0.3.0",
                Gw2ApiUserAgent.ReadManifestVersion(new VersionedManifest("0.3.0")));
        }

        [Fact]
        public void ReadManifestVersion_ReturnsNullWhenThereIsNothingToRead()
        {
            Assert.Null(Gw2ApiUserAgent.ReadManifestVersion(null));
            Assert.Null(Gw2ApiUserAgent.ReadManifestVersion(new object()));
            Assert.Null(Gw2ApiUserAgent.ReadManifestVersion(new VersionedManifest(null)));
            Assert.Null(Gw2ApiUserAgent.ReadManifestVersion(new VersionedManifest("  ")));
            Assert.Null(Gw2ApiUserAgent.ReadManifestVersion(new ThrowingManifest()));
        }
    }
}
