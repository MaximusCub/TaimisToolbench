using System;
using System.Text.Json;

namespace MysticForgeSeeder
{
    /// <summary>
    /// The error object a MediaWiki response carries in its body.
    /// </summary>
    /// <remarks>
    /// A lag refusal is HTTP 200 with an "error" object whose code is
    /// "maxlag", not a status code, so a client that branches on status
    /// alone cannot see one. A rate refusal uses the code "ratelimited".
    /// Both are stated on mediawiki.org, under Manual:Maxlag parameter and
    /// API:Etiquette.
    /// </remarks>
    public class WikiApiRefusal
    {
        private WikiApiRefusal(string code, string info)
        {
            Code = code;
            Info = info;
        }

        public string Code { get; }

        public string Info { get; }

        /// <summary>
        /// True when the wiki asked for the request to be sent again later.
        /// Any other code is this tool's own fault, and repeating the
        /// request only adds load.
        /// </summary>
        public bool IsTransient => Code == "maxlag" || Code == "ratelimited";

        /// <summary>
        /// The refusal a response body carries, or null when it carries
        /// none. A body that is not JSON at all carries no error object
        /// either; the caller parses it and fails there.
        /// </summary>
        public static WikiApiRefusal? Read(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                    !doc.RootElement.TryGetProperty("error", out var error) ||
                    error.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                return new WikiApiRefusal(
                    ReadString(error, "code"), ReadString(error, "info"));
            }
            catch (JsonException)
            {
                return null;
            }
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(Info)
                ? $"wiki API error '{Code}'"
                : $"wiki API error '{Code}': {Info}";
        }

        private static string ReadString(JsonElement parent, string name)
        {
            return parent.TryGetProperty(name, out var value) &&
                   value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }

    /// <summary>
    /// Thrown when the wiki answered with something that is not results:
    /// an error object, or a body carrying neither results nor an error.
    /// Distinct from HttpRequestException because the retry loop and the
    /// item-id resolver both catch that one, and neither should absorb a
    /// refusal into a shorter scrape.
    /// </summary>
    public class WikiApiException : Exception
    {
        public WikiApiException(string message)
            : base(message)
        {
        }
    }
}
