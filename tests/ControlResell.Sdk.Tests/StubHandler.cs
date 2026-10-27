using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ControlResell.Sdk.Tests;

/// <summary>What the stub saw of a request (the request itself is disposed once sent).</summary>
public sealed record Seen(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body)
{
    public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
}

/// <summary>An <see cref="HttpMessageHandler"/> that answers from a function and records every request.</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Func<Seen, HttpResponseMessage> _answer;

    public StubHandler(Func<Seen, HttpResponseMessage> answer) => _answer = answer;

    public List<Seen> Requests { get; } = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync();
        var seen = new Seen(request.Method, request.RequestUri!, headers, body);
        Requests.Add(seen);
        return _answer(seen);
    }

    public static HttpResponseMessage Json(object? body, HttpStatusCode status = HttpStatusCode.OK, params (string, string)[] headers)
    {
        var text = body is string s ? s : System.Text.Json.JsonSerializer.Serialize(body);
        var response = new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }

    public static HttpResponseMessage Error(string code, HttpStatusCode status, params (string, string)[] headers) =>
        Json(new { error = new { code, message = "m", @params = new { field = "name" }, requestId = "req-1" } }, status, headers);
}
