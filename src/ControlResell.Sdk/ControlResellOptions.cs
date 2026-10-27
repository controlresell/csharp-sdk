using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace ControlResell.Sdk
{
    /// <summary>How a <see cref="ControlResellClient"/> (or a <see cref="ControlResellHandler"/>) talks to the API.</summary>
    public sealed class ControlResellOptions
    {
        /// <summary>The default base URL of the API.</summary>
        public const string DefaultBaseUrl = "https://api.controlresell.com";

        /// <summary>The environment variable the key is read from when none is given.</summary>
        public const string ApiKeyEnvironmentVariable = "CONTROLRESELL_API_KEY";

        /// <summary>The API key (<c>cr_live_...</c>); when null, the <c>CONTROLRESELL_API_KEY</c> environment variable.</summary>
        public string? ApiKey { get; set; }

        /// <summary>The base URL of the API: another environment, a test double.</summary>
        public string BaseUrl { get; set; } = DefaultBaseUrl;

        /// <summary>Retries after the first attempt (2 by default); 0 turns them off.</summary>
        public int MaxRetries { get; set; } = 2;

        /// <summary>The first delay, doubled at each retry, with a random part; a <c>Retry-After</c> takes precedence.</summary>
        public TimeSpan BaseDelay { get; set; } = TimeSpan.FromMilliseconds(500);

        /// <summary>The longest delay between two attempts.</summary>
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>The timeout of a whole call, retries included (the <see cref="HttpClient.Timeout"/>); 100 s when null.</summary>
        public TimeSpan? Timeout { get; set; }

        /// <summary>The handler that really sends the requests (a proxy, a test double); the default one of .NET when null.</summary>
        public HttpMessageHandler? InnerHandler { get; set; }

        /// <summary>How the handler waits between two attempts; replaced by the tests.</summary>
        internal Func<TimeSpan, CancellationToken, Task>? Delay { get; set; }

        internal string ResolveApiKey()
        {
            var key = ApiKey;
            if (string.IsNullOrEmpty(key)) key = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("No API key: pass one or set the " + ApiKeyEnvironmentVariable + " environment variable.", nameof(ApiKey));
            }
            return key!;
        }
    }
}
