# ControlResell .NET SDK

.NET client for the [ControlResell](https://controlresell.com) public API (`netstandard2.0` and `net8.0`: .NET Framework 4.6.2+, .NET 6+).

## Install

From the 1.0.0 release, on [NuGet](https://www.nuget.org/packages/ControlResell.Sdk):

```sh
dotnet add package ControlResell.Sdk
```

## Use

```csharp
using ControlResell.Sdk;

using var client = new ControlResellClient("cr_live_..."); // or set CONTROLRESELL_API_KEY and pass nothing

var me = await client.Me.GetMeAsync();
await foreach (var item in client.Items.ListAllItemsAsync(sold: false)) Console.WriteLine(item.Name);
await foreach (var order in client.Orders.ListAllOrdersAsync(bucket: "to_send")) Console.WriteLine(order.Id); // needs the orders:read scope

// Writes (API key with `items:write` / `conversations:write`).
var item = await client.Items.CreateItemAsync(new ItemCreateRequest { Name = "Jacket" });
await client.Items.PublishItemAsync(item.Id, new PublishRequest { AccountIds = new[] { accountId } });
// PublishRequest, PriceDropRequest and RepublishRequest take an optional ScheduledAt (a DateTimeOffset, at most 30 days ahead):
// the action starts then instead of now, and the 202 receipt carries the date.
await client.Items.RepublishItemAsync(item.Id, new RepublishRequest { ScheduledAt = DateTimeOffset.UtcNow.AddDays(1) });
await client.Conversations.SendMessageAsync(conversationId, new SendMessageRequest { Text = "Hello!" });

// Webhooks (`webhooks:read` / `webhooks:write`); ListAllWebhookEventsAsync catches up the last 7 days.
await foreach (var evt in client.Webhooks.ListAllWebhookEventsAsync(type: "item.sold")) Console.WriteLine(evt.Type);
```

Every tag of the API is a property of the client (`Items`, `Accounts`, `Conversations`, `Orders`, `Customers`, `Favorites`,
`Catalogs`, `Webhooks`, `Me`), each operation is `<OperationId>Async`, and the optional filters are optional arguments.
The client is thread-safe: create one per API key and keep it. `baseUrl` is settable for another environment:

```csharp
var client = new ControlResellClient(options: new ControlResellOptions { ApiKey = "cr_test_...", BaseUrl = "https://staging.example.com" });
```

### With `IHttpClientFactory`

`ControlResellHandler` is a `DelegatingHandler` that carries the key, the retries, the idempotency keys and the errors; put it
in the pipeline of an `HttpClient` you own and wrap that client:

```csharp
services.AddHttpClient("controlresell", c => c.BaseAddress = new Uri("https://api.controlresell.com/"))
        .AddHttpMessageHandler(() => new ControlResellHandler(new ControlResellOptions { ApiKey = "cr_live_..." }));
// ... new ControlResellClient(factory.CreateClient("controlresell"))
```

## Retries

Two retries by default (`ControlResellOptions.MaxRetries`), with an exponential delay and some jitter (`BaseDelay`, `MaxDelay`).
A `429` waits for `Retry-After`. A `502`, `503`, `504` and a network error are retried; a `500` never is. A `409` with the code
`idempotency_in_progress` is retried after a delay; any other `409` is returned. `POST /webhook-endpoints/{id}/test` sends a
real event: only a `429` is retried.

## Idempotency

Every `POST` carries an `Idempotency-Key` (a UUID v4 the SDK generates), **the same on every retry of the call**, so a retried
write is never applied twice. To choose the key:

```csharp
using (IdempotencyScope.Use("import-2026-10-26-item-42"))
{
    await client.Items.PublishItemAsync(42, request);
}
```

## Pagination

`ListAll<Things>Async(...)` iterates a list page after page as an `IAsyncEnumerable<T>`, with the same filters on each page
(items, accounts, conversations and their messages, orders and labels, customers, favorites, webhook deliveries and events, and the
catalogs). The cursor is opaque. For a page at a time call `List<Things>Async(limit:, cursor:)` and read `Data` and `NextCursor`;
for any other list, `Pagination.Paginate<T>(fetchPage)`.

## Errors

An error answered by the API is a `ControlResellException` read from its envelope: `Status`, `Code` (an open string: match
the codes you handle), `Message`, `Params`, `RequestId` (give it to the support). One subclass per status: `BadRequestException`
(400), `AuthenticationException` (401), `PaymentRequiredException` (402), `PermissionDeniedException` (403),
`NotFoundException` (404), `ConflictException` (409), `UnprocessableEntityException` (422), `RateLimitException` (429, with
`RetryAfter`), `ServerException` (5xx). `ConnectionException` means the API could not be reached after the retries. The key never
appears in a message.

```csharp
try { await client.Items.GetItemAsync(42); }
catch (NotFoundException e) when (e.Code == "item_not_found") { Console.WriteLine(e.RequestId); }
```

An amount is `Money { Amount, Currency }`, `Amount` being the decimal **string** of the API (`"12.50"`): parse it with
`decimal.Parse(..., CultureInfo.InvariantCulture)`. A field or a value the API adds later is tolerated.

## Generated code

The operations and models in `src/ControlResell.Sdk/Generated` are generated from the OpenAPI document of the API with
[NSwag](https://github.com/RicoSuter/NSwag) (`scripts/generate.sh`, version pinned in `.config/dotnet-tools.json`; needs `dotnet`
and `python3`); `.spec-sha256` is the hash of the document they come from. The rest of `src` is written by hand.

```sh
scripts/generate.sh   # from https://api.controlresell.com/api/public/openapi.json, or: scripts/generate.sh path/to/openapi.json
dotnet test
```

Licensed under the Apache License 2.0.
