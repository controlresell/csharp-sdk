using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace ControlResell.Sdk
{
    /// <summary>A page of a list: its items and the cursor of the next page (null on the last one).</summary>
    public interface ICursorPage<out T>
    {
        IEnumerable<T> Items { get; }

        string? NextCursor { get; }
    }

    public static class Pagination
    {
        /// <summary>
        /// Iterates every item of a list, page after page: <paramref name="fetchPage"/> reads one page from a cursor (null for
        /// the first one) and must pass the same filters each time. The cursor is opaque: it is only given back.
        /// </summary>
        public static async IAsyncEnumerable<T> Paginate<T>(
            Func<string?, CancellationToken, Task<ICursorPage<T>>> fetchPage,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (fetchPage == null) throw new ArgumentNullException(nameof(fetchPage));
            string? cursor = null;
            do
            {
                var page = await fetchPage(cursor, cancellationToken).ConfigureAwait(false);
                foreach (var item in page.Items) yield return item;
                cursor = string.IsNullOrEmpty(page.NextCursor) ? null : page.NextCursor;
            }
            while (cursor != null);
        }
    }

    // The generated pages are partial classes: they read as ICursorPage<T> without the generator knowing.
    public partial class AccountPage : ICursorPage<Account> { IEnumerable<Account> ICursorPage<Account>.Items => Data; }
    public partial class ItemPage : ICursorPage<Item> { IEnumerable<Item> ICursorPage<Item>.Items => Data; }
    public partial class FavoritePage : ICursorPage<Favorite> { IEnumerable<Favorite> ICursorPage<Favorite>.Items => Data; }
    public partial class ConversationPage : ICursorPage<Conversation> { IEnumerable<Conversation> ICursorPage<Conversation>.Items => Data; }
    public partial class MessagePage : ICursorPage<Message> { IEnumerable<Message> ICursorPage<Message>.Items => Data; }
    public partial class CustomerPage : ICursorPage<Customer> { IEnumerable<Customer> ICursorPage<Customer>.Items => Data; }
    public partial class OrderPage : ICursorPage<Order> { IEnumerable<Order> ICursorPage<Order>.Items => Data; }
    public partial class OrderLabelPage : ICursorPage<OrderLabel> { IEnumerable<OrderLabel> ICursorPage<OrderLabel>.Items => Data; }
    public partial class CatalogCategoryPage : ICursorPage<CatalogCategory> { IEnumerable<CatalogCategory> ICursorPage<CatalogCategory>.Items => Data; }
    public partial class CatalogBrandPage : ICursorPage<CatalogBrand> { IEnumerable<CatalogBrand> ICursorPage<CatalogBrand>.Items => Data; }
    public partial class CatalogSizePage : ICursorPage<CatalogSize> { IEnumerable<CatalogSize> ICursorPage<CatalogSize>.Items => Data; }
    public partial class CatalogValuePage : ICursorPage<CatalogValue> { IEnumerable<CatalogValue> ICursorPage<CatalogValue>.Items => Data; }
    public partial class WebhookDeliveryPage : ICursorPage<WebhookDelivery> { IEnumerable<WebhookDelivery> ICursorPage<WebhookDelivery>.Items => Data; }
    public partial class ListingPage : ICursorPage<Listing> { IEnumerable<Listing> ICursorPage<Listing>.Items => Data; }
    public partial class WebhookEndpointPage : ICursorPage<WebhookEndpoint> { IEnumerable<WebhookEndpoint> ICursorPage<WebhookEndpoint>.Items => Data; }
    public partial class WebhookEventPage : ICursorPage<WebhookEvent> { IEnumerable<WebhookEvent> ICursorPage<WebhookEvent>.Items => Data; }

    /// <summary>
    /// <c>ListAll...Async</c>: every item of a list as an <see cref="IAsyncEnumerable{T}"/>, with the filters of the matching
    /// <c>List...Async</c> operation (<c>limit</c> is the size of each page).
    /// </summary>
    public static class PaginationExtensions
    {
        public static IAsyncEnumerable<Account> ListAllAccountsAsync(this IAccountsApi api, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<Account>(async (cursor, token) => await api.ListAccountsAsync(limit, cursor, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<Item> ListAllItemsAsync(this IItemsApi api, bool? sold = null, Guid? accountId = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<Item>(async (cursor, token) => await api.ListItemsAsync(limit, cursor, sold, accountId, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<Favorite> ListAllItemFavoritesAsync(this IFavoritesApi api, long itemId, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<Favorite>(async (cursor, token) => await api.ListItemFavoritesAsync(itemId, limit, cursor, from, to, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<Favorite> ListAllFavoritesAsync(this IFavoritesApi api, DateTimeOffset? from = null, DateTimeOffset? to = null, Guid? accountId = null, long? itemId = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<Favorite>(async (cursor, token) => await api.ListFavoritesAsync(limit, cursor, from, to, accountId, itemId, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<Conversation> ListAllConversationsAsync(this IConversationsApi api, bool? unread = null, Guid? accountId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, string? buyerId = null, long? itemId = null, string? offerStatus = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<Conversation>(async (cursor, token) => await api.ListConversationsAsync(limit, cursor, unread, accountId, from, to, buyerId, itemId, offerStatus, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<Message> ListAllConversationMessagesAsync(this IConversationsApi api, Guid conversationId, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<Message>(async (cursor, token) => await api.ListConversationMessagesAsync(conversationId, limit, cursor, from, to, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<Customer> ListAllCustomersAsync(this ICustomersApi api, DateTimeOffset? from = null, DateTimeOffset? to = null, Guid? accountId = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<Customer>(async (cursor, token) => await api.ListCustomersAsync(limit, cursor, from, to, accountId, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<Order> ListAllOrdersAsync(this IOrdersApi api, string? bucket = null, string? platform = null, Guid? accountId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<Order>(async (cursor, token) => await api.ListOrdersAsync(limit, cursor, bucket, platform, accountId, from, to, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<OrderLabel> ListAllLabelsToShipAsync(this IOrdersApi api, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<OrderLabel>(async (cursor, token) => await api.ListLabelsToShipAsync(limit, cursor, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<CatalogCategory> ListAllCatalogCategoriesAsync(this ICatalogsApi api, long? parentId = null, string? platform = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<CatalogCategory>(async (cursor, token) => await api.ListCatalogCategoriesAsync(limit, cursor, parentId, platform, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<CatalogBrand> ListAllCatalogBrandsAsync(this ICatalogsApi api, string? q = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<CatalogBrand>(async (cursor, token) => await api.ListCatalogBrandsAsync(limit, cursor, q, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<CatalogSize> ListAllCatalogSizesAsync(this ICatalogsApi api, long? categoryId = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<CatalogSize>(async (cursor, token) => await api.ListCatalogSizesAsync(limit, cursor, categoryId, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<CatalogValue> ListAllCatalogColorsAsync(this ICatalogsApi api, long? categoryId = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<CatalogValue>(async (cursor, token) => await api.ListCatalogColorsAsync(limit, cursor, categoryId, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<CatalogValue> ListAllCatalogConditionsAsync(this ICatalogsApi api, long? categoryId = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<CatalogValue>(async (cursor, token) => await api.ListCatalogConditionsAsync(limit, cursor, categoryId, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<CatalogValue> ListAllCatalogPackageSizesAsync(this ICatalogsApi api, long? categoryId = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<CatalogValue>(async (cursor, token) => await api.ListCatalogPackageSizesAsync(limit, cursor, categoryId, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<CatalogValue> ListAllCatalogMaterialsAsync(this ICatalogsApi api, long? categoryId = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<CatalogValue>(async (cursor, token) => await api.ListCatalogMaterialsAsync(limit, cursor, categoryId, token).ConfigureAwait(false), cancellationToken);

        public static IAsyncEnumerable<WebhookDelivery> ListAllWebhookDeliveriesAsync(this IWebhooksApi api, Guid endpointId, string? status = null, string? type = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<WebhookDelivery>(async (cursor, token) => await api.ListWebhookDeliveriesAsync(endpointId, status, type, limit, cursor, token).ConfigureAwait(false), cancellationToken);

        /// <summary>The events of the last 7 days (or since <paramref name="since"/>), to catch up after an outage.</summary>
        public static IAsyncEnumerable<WebhookEvent> ListAllWebhookEventsAsync(this IWebhooksApi api, DateTimeOffset? since = null, string? type = null, int? limit = null, CancellationToken cancellationToken = default) =>
            Pagination.Paginate<WebhookEvent>(async (cursor, token) => await api.ListWebhookEventsAsync(since, type, limit, cursor, token).ConfigureAwait(false), cancellationToken);
    }
}
