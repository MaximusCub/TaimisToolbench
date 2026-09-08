using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Gw2Sharp.WebApi.Http;

namespace TaimisToolbench.Harness
{
    /// <summary>
    /// One HTTP call Gw2Sharp made, as the transport saw it.
    /// </summary>
    internal class ProfiledRequest
    {
        public string Url { get; set; }

        public DateTime StartedUtc { get; set; }

        public double DurationMs { get; set; }

        /// <summary>Decoded response body length in UTF-8 bytes.</summary>
        public long BodyBytes { get; set; }

        public int StatusCode { get; set; }

        public string Failure { get; set; }
    }

    /// <summary>
    /// Wraps Gw2Sharp's own <see cref="IHttpClient"/> and records every call
    /// that reaches it.
    /// </summary>
    /// <remarks>
    /// Gw2Sharp's cache sits above this seam, so a request recorded here is
    /// one that went to the network. A run whose recorded count is lower
    /// than its endpoint count was served from cache, which is how
    /// tools/TaimisToolbench.Harness/README.md's fetch profiler proves it
    /// measured the network.
    /// </remarks>
    internal class CountingGw2HttpClient : IHttpClient
    {
        private readonly IHttpClient _inner;
        private readonly List<ProfiledRequest> _requests = new List<ProfiledRequest>();
        private readonly object _lock = new object();

        public CountingGw2HttpClient(IHttpClient inner)
        {
            _inner = inner;
        }

        public TimeSpan Timeout
        {
            get { return _inner.Timeout; }
            set { _inner.Timeout = value; }
        }

        public IReadOnlyList<ProfiledRequest> Requests
        {
            get
            {
                lock (_lock)
                {
                    return _requests.ToArray();
                }
            }
        }

        public async Task<IWebApiResponse> RequestAsync(
            IWebApiRequest request, CancellationToken cancellationToken)
        {
            var record = new ProfiledRequest
            {
                Url = DescribeUrl(request),
                StartedUtc = DateTime.UtcNow,
            };
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await _inner.RequestAsync(request, cancellationToken);
                stopwatch.Stop();
                record.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                record.StatusCode = (int)response.StatusCode;
                record.BodyBytes = response.Content == null
                    ? 0
                    : Encoding.UTF8.GetByteCount(response.Content);
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                record.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                record.Failure = ex.GetType().Name;
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _requests.Add(record);
                }
            }
        }

        public async Task<IHttpResponseStream> RequestStreamAsync(
            IWebApiRequest request, CancellationToken cancellationToken)
        {
            var record = new ProfiledRequest
            {
                Url = DescribeUrl(request),
                StartedUtc = DateTime.UtcNow,
            };
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var response = await _inner.RequestStreamAsync(request, cancellationToken);
                stopwatch.Stop();
                record.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                record.StatusCode = (int)response.StatusCode;
                return response;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                record.DurationMs = stopwatch.Elapsed.TotalMilliseconds;
                record.Failure = ex.GetType().Name;
                throw;
            }
            finally
            {
                lock (_lock)
                {
                    _requests.Add(record);
                }
            }
        }

        // The access token rides in a header, never in the query string, so
        // a recorded URL cannot carry it. Query is kept because page and
        // ids= parameters are the difference between two approaches.
        private static string DescribeUrl(IWebApiRequest request)
        {
            if (request == null || request.Options == null)
            {
                return "unknown";
            }

            var url = request.Options.Url;
            return url == null ? "unknown" : url.PathAndQuery;
        }
    }
}
