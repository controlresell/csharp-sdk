using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;

namespace ControlResell.Sdk
{
    /// <summary>An error answered by the API, read from its envelope <c>{"error": {code, message, params, requestId}}</c>.</summary>
    public class ControlResellException : Exception
    {
        public ControlResellException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null)
            : base(message)
        {
            Status = status;
            Code = code;
            Params = parameters ?? new Dictionary<string, string>();
            RequestId = requestId;
        }

        /// <summary>The HTTP status.</summary>
        public int Status { get; }

        /// <summary>The error's code (<c>item_not_found</c>, <c>rate_limited</c>...): an open string, match the ones you handle.</summary>
        public string Code { get; }

        /// <summary>The error's parameters.</summary>
        public IReadOnlyDictionary<string, string> Params { get; }

        /// <summary>The identifier to give the support.</summary>
        public string? RequestId { get; }
    }

    /// <summary>400: the request is malformed.</summary>
    public sealed class BadRequestException : ControlResellException
    {
        public BadRequestException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null) : base(status, code, message, parameters, requestId) { }
    }

    /// <summary>401: no credential, or an unknown, revoked or expired one.</summary>
    public sealed class AuthenticationException : ControlResellException
    {
        public AuthenticationException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null) : base(status, code, message, parameters, requestId) { }
    }

    /// <summary>402: the seller's plan does not include this.</summary>
    public sealed class PaymentRequiredException : ControlResellException
    {
        public PaymentRequiredException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null) : base(status, code, message, parameters, requestId) { }
    }

    /// <summary>403: the API key does not grant the scope the route requires.</summary>
    public sealed class PermissionDeniedException : ControlResellException
    {
        public PermissionDeniedException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null) : base(status, code, message, parameters, requestId) { }
    }

    /// <summary>404: no such resource for this seller.</summary>
    public sealed class NotFoundException : ControlResellException
    {
        public NotFoundException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null) : base(status, code, message, parameters, requestId) { }
    }

    /// <summary>409: an <c>Idempotency-Key</c> used with another request, or still in flight.</summary>
    public sealed class ConflictException : ControlResellException
    {
        public ConflictException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null) : base(status, code, message, parameters, requestId) { }
    }

    /// <summary>422: the request is well formed but refused.</summary>
    public sealed class UnprocessableEntityException : ControlResellException
    {
        public UnprocessableEntityException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null) : base(status, code, message, parameters, requestId) { }
    }

    /// <summary>429: the rate limit of the API key is reached.</summary>
    public sealed class RateLimitException : ControlResellException
    {
        public RateLimitException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null, TimeSpan? retryAfter = null)
            : base(status, code, message, parameters, requestId)
        {
            RetryAfter = retryAfter;
        }

        /// <summary>The <c>Retry-After</c> the API answered, when it did.</summary>
        public TimeSpan? RetryAfter { get; }
    }

    /// <summary>5xx: an error of the API; retry later.</summary>
    public sealed class ServerException : ControlResellException
    {
        public ServerException(int status, string code, string message, IReadOnlyDictionary<string, string>? parameters = null, string? requestId = null) : base(status, code, message, parameters, requestId) { }
    }

    /// <summary>The API could not be reached (network, DNS, TLS), after the retries.</summary>
    public sealed class ConnectionException : Exception
    {
        public ConnectionException(Exception cause) : base("Could not reach the ControlResell API: " + cause.Message, cause) { }
    }

    internal static class ErrorFactory
    {
        /// <summary>The typed error of an answer whose status is not a success, its body being the API's envelope (or anything else).</summary>
        internal static ControlResellException Create(HttpStatusCode statusCode, string body, TimeSpan? retryAfter)
        {
            var status = (int)statusCode;
            var envelope = ReadEnvelope(body);
            var code = envelope.Code ?? "http_" + status;
            var message = envelope.Message ?? "HTTP " + status;
            switch (status)
            {
                case 400: return new BadRequestException(status, code, message, envelope.Params, envelope.RequestId);
                case 401: return new AuthenticationException(status, code, message, envelope.Params, envelope.RequestId);
                case 402: return new PaymentRequiredException(status, code, message, envelope.Params, envelope.RequestId);
                case 403: return new PermissionDeniedException(status, code, message, envelope.Params, envelope.RequestId);
                case 404: return new NotFoundException(status, code, message, envelope.Params, envelope.RequestId);
                case 409: return new ConflictException(status, code, message, envelope.Params, envelope.RequestId);
                case 422: return new UnprocessableEntityException(status, code, message, envelope.Params, envelope.RequestId);
                case 429: return new RateLimitException(status, code, message, envelope.Params, envelope.RequestId, retryAfter);
                default:
                    return status >= 500
                        ? new ServerException(status, code, message, envelope.Params, envelope.RequestId)
                        : new ControlResellException(status, code, message, envelope.Params, envelope.RequestId);
            }
        }

        internal static (string? Code, string? Message, Dictionary<string, string> Params, string? RequestId) ReadEnvelope(string body)
        {
            var parameters = new Dictionary<string, string>();
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("error", out var error)
                    || error.ValueKind != JsonValueKind.Object)
                {
                    return (null, null, parameters, null);
                }
                if (error.TryGetProperty("params", out var map) && map.ValueKind == JsonValueKind.Object)
                {
                    foreach (var property in map.EnumerateObject())
                    {
                        parameters[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString()! : property.Value.GetRawText();
                    }
                }
                return (String(error, "code"), String(error, "message"), parameters, String(error, "requestId"));
            }
            catch (JsonException)
            {
                return (null, null, parameters, null);
            }
        }

        private static string? String(JsonElement element, string name) =>
            element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
