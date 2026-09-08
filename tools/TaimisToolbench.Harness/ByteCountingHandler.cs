using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace TaimisToolbench.Harness
{
    /// <summary>
    /// Counts the bytes api.guildwars2.com actually sends, then decodes them
    /// so the caller sees an ordinary response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured: the API answers with chunked transfer encoding and no
    /// Content-Length header, so a response's size cannot be read off a
    /// header. It also gzips when asked, so counting the decoded body would
    /// overstate what crossed the network several times over.
    /// </para>
    /// <para>
    /// HttpClientHandler.AutomaticDecompression decodes below this handler,
    /// where the compressed length is already gone. So decompression is done
    /// here instead, after the raw bytes have been counted.
    /// </para>
    /// </remarks>
    internal class ByteCountingHandler : DelegatingHandler
    {
        private long _compressedBytes;
        private long _decodedBytes;

        public ByteCountingHandler(HttpMessageHandler inner)
            : base(inner)
        {
        }

        public long CompressedBytes
        {
            get { return Interlocked.Read(ref _compressedBytes); }
        }

        public long DecodedBytes
        {
            get { return Interlocked.Read(ref _decodedBytes); }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!request.Headers.AcceptEncoding.Any())
            {
                request.Headers.AcceptEncoding.ParseAdd("gzip, deflate");
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Content == null)
            {
                return response;
            }

            byte[] raw = await response.Content.ReadAsByteArrayAsync();
            Interlocked.Add(ref _compressedBytes, raw.Length);

            var encodings = response.Content.Headers.ContentEncoding.ToList();
            byte[] decoded = Decode(raw, encodings);
            Interlocked.Add(ref _decodedBytes, decoded.Length);

            var replacement = new ByteArrayContent(decoded);
            foreach (var header in response.Content.Headers)
            {
                if (string.Equals(header.Key, "Content-Encoding", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "Content-Length", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                replacement.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            response.Content.Dispose();
            response.Content = replacement;
            return response;
        }

        private static byte[] Decode(byte[] raw, List<string> encodings)
        {
            bool gzip = encodings.Any(e => string.Equals(e, "gzip", StringComparison.OrdinalIgnoreCase));
            bool deflate = encodings.Any(e => string.Equals(e, "deflate", StringComparison.OrdinalIgnoreCase));
            if (!gzip && !deflate)
            {
                return raw;
            }

            using (var source = new MemoryStream(raw))
            using (Stream decompressor = gzip
                ? (Stream)new GZipStream(source, CompressionMode.Decompress)
                : new DeflateStream(source, CompressionMode.Decompress))
            using (var target = new MemoryStream())
            {
                decompressor.CopyTo(target);
                return target.ToArray();
            }
        }
    }
}
