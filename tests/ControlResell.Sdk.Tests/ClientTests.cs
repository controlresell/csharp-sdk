using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;
using static ControlResell.Sdk.Tests.StubHandler;

namespace ControlResell.Sdk.Tests;

public class ClientTests
{
    private const string Key = "cr_test_secret";

    private static string ItemJson(long id, string sex = "WOMEN") =>
        "{\"id\":" + id + ",\"name\":\"Jacket\",\"sku\":null,\"description\":null,\"photos\":[],\"categoryId\":null,\"brandId\":null," +
        "\"sizeId\":null,\"colorIds\":[],\"state\":null,\"sex\":\"" + sex + "\",\"labels\":[],\"sold\":false,\"materialIds\":[]," +
        "\"enumValues\":[],\"attributes\":[],\"customFields\":[],\"estimatedPrice\":{\"amount\":\"12.50\",\"currency\":\"EUR\"}," +
        "\"purchasePrice\":null,\"purchaseDate\":null,\"sellingPrice\":null,\"sellingDate\":null," +
        "\"createdAt\":\"2026-10-20T10:00:00Z\",\"updatedAt\":\"2026-10-20T10:00:00Z\",\"unknownField\":1}";

    private static string ItemPageJson(string? next, params long[] ids) =>
        "{\"data\":[" + string.Join(",", ids.Select(id => ItemJson(id))) + "],\"nextCursor\":" + (next == null ? "null" : "\"" + next + "\"") + "}";

    private static (ControlResellClient Client, StubHandler Stub, List<TimeSpan> Delays) NewClient(Func<Seen, HttpResponseMessage> answer, int maxRetries = 2)
    {
        var stub = new StubHandler(answer);
        var delays = new List<TimeSpan>();
        var options = new ControlResellOptions
        {
            BaseUrl = "https://api.test",
            InnerHandler = stub,
            MaxRetries = maxRetries,
            BaseDelay = TimeSpan.FromMilliseconds(10),
            MaxDelay = TimeSpan.FromSeconds(5),
            Delay = (delay, _) => { delays.Add(delay); return Task.CompletedTask; },
        };
        return (new ControlResellClient(Key, options), stub, delays);
    }

    [Fact]
    public async Task A_call_sends_the_key_and_the_user_agent_to_the_base_url()
    {
        var (client, stub, _) = NewClient(_ => Json(new { id = 1, username = "seller", currency = "EUR", unknownField = true }));
        var me = await client.Me.GetMeAsync();
        Assert.Equal("seller", me.Username);
        var request = Assert.Single(stub.Requests);
        Assert.Equal("https://api.test/api/public/v1/me", request.Uri.ToString());
        Assert.Equal("Bearer " + Key, request.Header("Authorization"));
        Assert.Equal("controlresell-csharp/1.0.0", request.Header("User-Agent"));
        Assert.Null(request.Header("Idempotency-Key"));
    }

    [Fact]
    public void The_key_comes_from_the_environment_when_none_is_given_and_is_required()
    {
        var previous = Environment.GetEnvironmentVariable("CONTROLRESELL_API_KEY");
        try
        {
            Environment.SetEnvironmentVariable("CONTROLRESELL_API_KEY", null);
            Assert.Throws<ArgumentException>(() => new ControlResellClient());
            Environment.SetEnvironmentVariable("CONTROLRESELL_API_KEY", "cr_env");
            using var client = new ControlResellClient();
            Assert.NotNull(client.Items);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CONTROLRESELL_API_KEY", previous);
        }
    }

    [Fact]
    public async Task An_error_is_typed_by_its_status_with_its_code_params_and_request_id()
    {
        var (client, stub, _) = NewClient(_ => Error("item_not_found", HttpStatusCode.NotFound));
        var error = await Assert.ThrowsAsync<NotFoundException>(() => client.Items.GetItemAsync(1));
        Assert.Equal(404, error.Status);
        Assert.Equal("item_not_found", error.Code);
        Assert.Equal("req-1", error.RequestId);
        Assert.Equal("name", error.Params["field"]);
        Assert.DoesNotContain(Key, error.ToString());
        Assert.Single(stub.Requests);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestException))]
    [InlineData(401, typeof(AuthenticationException))]
    [InlineData(402, typeof(PaymentRequiredException))]
    [InlineData(403, typeof(PermissionDeniedException))]
    [InlineData(409, typeof(ConflictException))]
    [InlineData(422, typeof(UnprocessableEntityException))]
    [InlineData(500, typeof(ServerException))]
    [InlineData(418, typeof(ControlResellException))]
    public async Task Each_status_has_its_error_and_an_unknown_code_is_kept(int status, Type type)
    {
        var (client, _, _) = NewClient(_ => Error("a_code_from_the_future", (HttpStatusCode)status));
        var error = await Assert.ThrowsAnyAsync<ControlResellException>(() => client.Items.GetItemAsync(1));
        Assert.Equal(type, error.GetType());
        Assert.Equal("a_code_from_the_future", error.Code);
    }

    [Fact]
    public async Task An_error_without_the_envelope_is_still_typed()
    {
        var (client, _, _) = NewClient(_ => Json("<html>forbidden</html>", HttpStatusCode.Forbidden));
        var error = await Assert.ThrowsAsync<PermissionDeniedException>(() => client.Items.GetItemAsync(1));
        Assert.Equal("http_403", error.Code);
        Assert.Null(error.RequestId);
    }

    [Fact]
    public async Task A_rate_limit_is_retried_after_Retry_After_then_succeeds()
    {
        var calls = 0;
        var (client, _, delays) = NewClient(_ => ++calls == 1
            ? Error("rate_limited", (HttpStatusCode)429, ("Retry-After", "2"))
            : Json(new { data = Array.Empty<object>(), nextCursor = (string?)null }));
        await client.Accounts.ListAccountsAsync();
        Assert.Equal(2, calls);
        Assert.Equal(new[] { TimeSpan.FromSeconds(2) }, delays);
    }

    [Fact]
    public async Task A_rate_limit_after_the_retries_is_a_RateLimitException_with_its_retryAfter()
    {
        var calls = 0;
        var (client, _, _) = NewClient(_ => { calls++; return Error("rate_limited", (HttpStatusCode)429, ("Retry-After", "3")); });
        var error = await Assert.ThrowsAsync<RateLimitException>(() => client.Accounts.ListAccountsAsync());
        Assert.Equal(TimeSpan.FromSeconds(3), error.RetryAfter);
        Assert.Equal(3, calls); // the first attempt and two retries
    }

    [Fact]
    public async Task Without_Retry_After_the_delay_grows_exponentially_with_some_jitter()
    {
        var (client, _, delays) = NewClient(_ => Error("unavailable", HttpStatusCode.ServiceUnavailable));
        await Assert.ThrowsAsync<ServerException>(() => client.Me.GetMeAsync());
        Assert.Equal(2, delays.Count);
        Assert.InRange(delays[0].TotalMilliseconds, 10, 15);
        Assert.InRange(delays[1].TotalMilliseconds, 20, 25);
    }

    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task A_gateway_or_availability_error_is_retried(int status)
    {
        var calls = 0;
        var (client, _, _) = NewClient(_ => ++calls == 1 ? Error("x", (HttpStatusCode)status) : Json(new { id = 1, username = "u", currency = "EUR" }));
        await client.Me.GetMeAsync();
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task A_POST_keeps_the_same_generated_idempotency_key_and_body_across_retries()
    {
        var calls = 0;
        var (client, stub, _) = NewClient(_ => ++calls < 3 ? Error("unavailable", HttpStatusCode.ServiceUnavailable) : Json(ItemJson(7), HttpStatusCode.Created));
        var item = await client.Items.CreateItemAsync(new ItemCreateRequest { Name = "Jacket" });
        Assert.Equal(7, item.Id);
        Assert.Equal(3, stub.Requests.Count);
        var keys = stub.Requests.Select(r => r.Header("Idempotency-Key")).ToList();
        Assert.NotNull(keys[0]);
        Assert.All(keys, key => Assert.Equal(keys[0], key));
        var guid = Guid.Parse(keys[0]!);
        Assert.Equal('4', guid.ToString()[14]); // UUID v4
        Assert.All(stub.Requests, r => Assert.Equal(stub.Requests[0].Body, r.Body));
        Assert.Contains("\"name\":\"Jacket\"", stub.Requests[0].Body);
    }

    [Fact]
    public async Task Two_calls_get_two_different_keys()
    {
        var (client, stub, _) = NewClient(_ => Json(ItemJson(7), HttpStatusCode.Created));
        await client.Items.CreateItemAsync(new ItemCreateRequest { Name = "a" });
        await client.Items.CreateItemAsync(new ItemCreateRequest { Name = "b" });
        Assert.NotEqual(stub.Requests[0].Header("Idempotency-Key"), stub.Requests[1].Header("Idempotency-Key"));
    }

    [Fact]
    public async Task The_key_chosen_by_the_caller_is_used_on_every_attempt()
    {
        var calls = 0;
        var (client, stub, _) = NewClient(_ => ++calls < 2 ? Error("unavailable", HttpStatusCode.BadGateway) : Json(ItemJson(7), HttpStatusCode.Created));
        using (IdempotencyScope.Use("import-42"))
        {
            await client.Items.CreateItemAsync(new ItemCreateRequest { Name = "a" });
        }
        Assert.All(stub.Requests, r => Assert.Equal("import-42", r.Header("Idempotency-Key")));
        await client.Items.CreateItemAsync(new ItemCreateRequest { Name = "b" });
        Assert.NotEqual("import-42", stub.Requests.Last().Header("Idempotency-Key"));
    }

    [Fact]
    public async Task A_500_is_never_retried()
    {
        var (client, stub, _) = NewClient(_ => Error("internal_error", HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<ServerException>(() => client.Items.CreateItemAsync(new ItemCreateRequest { Name = "a" }));
        await Assert.ThrowsAsync<ServerException>(() => client.Items.GetItemAsync(1));
        Assert.Equal(2, stub.Requests.Count);
    }

    [Fact]
    public async Task A_409_idempotency_in_progress_is_retried_but_another_409_is_not()
    {
        var calls = 0;
        var (client, _, delays) = NewClient(_ => ++calls == 1 ? Error("idempotency_in_progress", HttpStatusCode.Conflict) : Json(ItemJson(7), HttpStatusCode.Created));
        await client.Items.CreateItemAsync(new ItemCreateRequest { Name = "a" });
        Assert.Equal(2, calls);
        Assert.Single(delays);

        calls = 0;
        var other = NewClient(_ => { calls++; return Error("idempotency_key_reused", HttpStatusCode.Conflict); });
        var error = await Assert.ThrowsAsync<ConflictException>(() => other.Client.Items.CreateItemAsync(new ItemCreateRequest { Name = "a" }));
        Assert.Equal("idempotency_key_reused", error.Code);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task The_webhook_test_is_only_retried_on_a_429()
    {
        var id = Guid.NewGuid();
        var calls = 0;
        var (client, _, _) = NewClient(_ => { calls++; return Error("unavailable", HttpStatusCode.ServiceUnavailable); });
        await Assert.ThrowsAsync<ServerException>(() => client.Webhooks.TestWebhookEndpointAsync(id));
        Assert.Equal(1, calls);

        calls = 0;
        var limited = NewClient(_ => ++calls == 1 ? Error("rate_limited", (HttpStatusCode)429, ("Retry-After", "0")) : Json(new { delivered = true }));
        try { await limited.Client.Webhooks.TestWebhookEndpointAsync(id); } catch (ControlResellException) { /* the answer's shape is not the point */ }
        Assert.Equal(2, calls);

        calls = 0;
        var network = NewClient(_ => { calls++; throw new HttpRequestException("reset"); });
        await Assert.ThrowsAsync<ConnectionException>(() => network.Client.Webhooks.TestWebhookEndpointAsync(id));
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task A_network_error_is_retried_then_becomes_a_ConnectionException_without_the_key()
    {
        var calls = 0;
        var (client, _, _) = NewClient(_ => { calls++; throw new HttpRequestException("connection refused"); });
        var error = await Assert.ThrowsAsync<ConnectionException>(() => client.Me.GetMeAsync());
        Assert.Equal(3, calls);
        Assert.Contains("connection refused", error.Message);
        Assert.DoesNotContain(Key, error.ToString());

        calls = 0;
        var recovered = NewClient(_ => ++calls == 1 ? throw new HttpRequestException("reset") : Json(new { id = 1, username = "u", currency = "EUR" }));
        Assert.Equal("u", (await recovered.Client.Me.GetMeAsync()).Username);
    }

    [Fact]
    public async Task Retries_can_be_turned_off()
    {
        var (client, stub, _) = NewClient(_ => Error("unavailable", HttpStatusCode.ServiceUnavailable), maxRetries: 0);
        await Assert.ThrowsAsync<ServerException>(() => client.Me.GetMeAsync());
        Assert.Single(stub.Requests);
    }

    [Fact]
    public async Task An_item_is_read_with_its_amount_as_a_decimal_string_and_unknown_values_are_tolerated()
    {
        var (client, _, _) = NewClient(_ => Json(ItemJson(42, sex: "A_SEX_ADDED_LATER")));
        var item = await client.Items.GetItemAsync(42);
        Assert.Equal(42, item.Id);
        Assert.Equal("Jacket", item.Name);
        Assert.Null(item.Sku);
        Assert.Equal("12.50", item.EstimatedPrice!.Amount); // never a float
        Assert.Equal("EUR", item.EstimatedPrice.Currency);
        Assert.Null(item.PurchasePrice);
        Assert.Equal("A_SEX_ADDED_LATER", item.Sex);
        Assert.Equal(new DateTimeOffset(2026, 10, 20, 10, 0, 0, TimeSpan.Zero), item.CreatedAt);
    }

    [Fact]
    public async Task Pagination_follows_the_cursor_over_two_pages_with_the_same_filters()
    {
        var (client, stub, _) = NewClient(request => request.Uri.Query.Contains("cursor=c1") ? Json(ItemPageJson(null, 3)) : Json(ItemPageJson("c1", 1, 2)));
        var ids = new List<long>();
        await foreach (var item in client.Items.ListAllItemsAsync(sold: false, limit: 2)) ids.Add(item.Id);
        Assert.Equal(new long[] { 1, 2, 3 }, ids);
        Assert.Equal(2, stub.Requests.Count);
        Assert.All(stub.Requests, r => Assert.Contains("sold=false", r.Uri.Query));
        Assert.All(stub.Requests, r => Assert.Contains("limit=2", r.Uri.Query));
        Assert.DoesNotContain("cursor", stub.Requests[0].Uri.Query);
        Assert.Contains("cursor=c1", stub.Requests[1].Uri.Query);
    }

    [Fact]
    public async Task The_generic_Paginate_helper_stops_when_there_is_no_next_cursor()
    {
        var pages = new Dictionary<string, ItemPage>
        {
            [""] = new ItemPage { Data = new[] { new Item { Id = 1 } }, NextCursor = "n" },
            ["n"] = new ItemPage { Data = new[] { new Item { Id = 2 } }, NextCursor = "" },
        };
        var asked = new List<string?>();
        var ids = new List<long>();
        await foreach (var item in Pagination.Paginate<Item>((cursor, _) => { asked.Add(cursor); return Task.FromResult<ICursorPage<Item>>(pages[cursor ?? ""]); })) ids.Add(item.Id);
        Assert.Equal(new long[] { 1, 2 }, ids);
        Assert.Equal(new string?[] { null, "n" }, asked);
    }

    [Fact]
    public void Every_generated_page_with_a_cursor_is_a_cursor_page()
    {
        var pages = typeof(ItemPage).Assembly.GetTypes()
            .Where(t => t.Namespace == "ControlResell.Sdk" && t.Name.EndsWith("Page") && t.GetProperty("NextCursor") != null)
            .ToList();
        Assert.True(pages.Count >= 14, "pages found: " + pages.Count);
        Assert.All(pages, page => Assert.Contains(page.GetInterfaces(), i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICursorPage<>)));
    }

    [Fact]
    public async Task A_client_over_an_injected_HttpClient_works_with_the_handler_in_its_pipeline()
    {
        var stub = new StubHandler(_ => Json(new { id = 1, username = "seller", currency = "EUR" }));
        var options = new ControlResellOptions { ApiKey = Key, Delay = (_, _) => Task.CompletedTask };
        using var http = new HttpClient(new ControlResellHandler(options, stub)) { BaseAddress = new Uri("https://api.test/") };
        using var client = new ControlResellClient(http);
        Assert.Equal("seller", (await client.Me.GetMeAsync()).Username);
        Assert.Equal("Bearer " + Key, stub.Requests.Single().Header("Authorization"));
    }
}
