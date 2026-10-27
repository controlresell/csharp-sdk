using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ControlResell.Sdk
{
    /// <summary>
    /// The <see cref="DelegatingHandler"/> that makes a call to the ControlResell API: it sets the key, the <c>User-Agent</c>
    /// and the <c>Idempotency-Key</c> of a <c>POST</c>, retries a failed call and turns an error answer into a typed
    /// <see cref="ControlResellException"/>. <see cref="ControlResellClient"/> uses it; with <c>IHttpClientFactory</c>, add it with
    /// <c>AddHttpMessageHandler(() =&gt; new ControlResellHandler(options))</c>.
    /// </summary>
    public sealed class ControlResellHandler : DelegatingHandler
    {
        /// <summary>The header that makes a <c>POST</c> safe to retry: the API replays the first answer to the same key.</summary>
        public const string IdempotencyKeyHeader = "Idempotency-Key";

        private const string IdempotencyInProgress = "idempotency_in_progress";

        /// <summary><c>POST /webhook-endpoints/{id}/test</c> sends a real event and ignores the key: a replay sends a second one.</summary>
        private static readonly Regex WebhookTestPath = new Regex("/webhook-endpoints/[^/]+/test/?$", RegexOptions.Compiled);

        private readonly string _apiKey;
        private readonly int _maxRetries;
        private readonly TimeSpan _baseDelay;
        private readonly TimeSpan _maxDelay;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;
        private readonly Random _random = new Random();

        /// <summary>A handler whose <see cref="DelegatingHandler.InnerHandler"/> is set by the caller (<c>IHttpClientFactory</c> does).</summary>
        public ControlResellHandler(ControlResellOptions options)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _apiKey = options.ResolveApiKey();
            _maxRetries = Math.Max(0, options.MaxRetries);
            _baseDelay = options.BaseDelay;
            _maxDelay = options.MaxDelay;
            _delay = options.Delay ?? ((delay, token) => Task.Delay(delay, token));
        }

        /// <summary>A handler that sends through <paramref name="innerHandler"/>.</summary>
        public ControlResellHandler(ControlResellOptions options, HttpMessageHandler innerHandler) : this(options)
        {
            InnerHandler = innerHandler ?? throw new ArgumentNullException(nameof(innerHandler));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content == null ? null : await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            var isPost = request.Method == HttpMethod.Post;
            var webhookTest = isPost && request.RequestUri != null && WebhookTestPath.IsMatch(request.RequestUri.AbsolutePath);
            // One key per call, kept on every retry.
            var idempotencyKey = isPost ? ExistingKey(request) ?? IdempotencyScope.Key ?? Guid.NewGuid().ToString() : null;

            for (var attempt = 0; ; attempt++)
            {
                using var attemptRequest = Copy(request, body, idempotencyKey);
                HttpResponseMessage response;
                try
                {
                    response = await base.SendAsync(attemptRequest, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (!cancellationToken.IsCancellationRequested && IsNetworkError(e))
                {
                    // A request that may have reached the server is not replayed for the test event.
                    if (webhookTest || attempt >= _maxRetries) throw new ConnectionException(e);
                    await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                if (response.IsSuccessStatusCode) return response;

                var text = await ReadAsync(response).ConfigureAwait(false);
                var retryAfter = RetryAfterOf(response);
                if (attempt < _maxRetries && ShouldRetry(response.StatusCode, text, webhookTest))
                {
                    response.Dispose();
                    var wait = retryAfter.HasValue ? Min(retryAfter.Value, _maxDelay) : Backoff(attempt);
                    await _delay(wait, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var status = response.StatusCode;
                response.Dispose();
                throw ErrorFactory.Create(status, text, retryAfter);
            }
        }

        /// <summary>A rate limit, a gateway or availability error; never a 500; a 409 only for a key still in flight.</summary>
        private static bool ShouldRetry(HttpStatusCode status, string body, bool webhookTest)
        {
            switch ((int)status)
            {
                case 429: return true;
                case 502:
                case 503:
                case 504: return !webhookTest;
                case 409: return !webhookTest && ErrorFactory.ReadEnvelope(body).Code == IdempotencyInProgress;
                default: return false;
            }
        }

        private static bool IsNetworkError(Exception e) =>
            e is HttpRequestException || e is IOException || e is TaskCanceledException /* HttpClient.Timeout */;

        private TimeSpan Backoff(int attempt)
        {
            var exponential = _baseDelay.TotalMilliseconds * Math.Pow(2, attempt);
            double jitter;
            lock (_random) jitter = _random.NextDouble() * _baseDelay.TotalMilliseconds / 2;
            return Min(TimeSpan.FromMilliseconds(exponential + jitter), _maxDelay);
        }

        private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;

        private static TimeSpan? RetryAfterOf(HttpResponseMessage response)
        {
            var header = response.Headers.RetryAfter;
            if (header == null) return null;
            if (header.Delta.HasValue) return header.Delta.Value < TimeSpan.Zero ? TimeSpan.Zero : header.Delta.Value;
            if (header.Date.HasValue)
            {
                var wait = header.Date.Value - DateTimeOffset.UtcNow;
                return wait < TimeSpan.Zero ? TimeSpan.Zero : wait;
            }
            return null;
        }

        private static async Task<string> ReadAsync(HttpResponseMessage response)
        {
            try
            {
                return response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            }
            catch (Exception e) when (e is HttpRequestException || e is IOException)
            {
                return string.Empty;
            }
        }

        private static string? ExistingKey(HttpRequestMessage request) =>
            request.Headers.TryGetValues(IdempotencyKeyHeader, out var values) ? values.FirstOrDefault() : null;

        /// <summary>A request cannot be sent twice: each attempt sends a copy, with the key, the User-Agent and the credential.</summary>
        private HttpRequestMessage Copy(HttpRequestMessage request, byte[]? body, string? idempotencyKey)
        {
            var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Version = request.Version };
            foreach (var header in request.Headers)
            {
                if (string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, "User-Agent", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(header.Key, IdempotencyKeyHeader, StringComparison.OrdinalIgnoreCase)) continue;
                copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            copy.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _apiKey);
            copy.Headers.TryAddWithoutValidation("User-Agent", ControlResellClient.UserAgent);
            if (idempotencyKey != null) copy.Headers.TryAddWithoutValidation(IdempotencyKeyHeader, idempotencyKey);
            if (body != null)
            {
                copy.Content = new ByteArrayContent(body);
                foreach (var header in request.Content!.Headers) copy.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return copy;
        }
    }
}
