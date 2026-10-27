using System;
using System.Net.Http;

namespace ControlResell.Sdk
{
    /// <summary>
    /// The client of the ControlResell public API: one per API key, safe to share between threads. Each tag of the API is a
    /// property (<c>client.Items.ListItemsAsync()</c>); <see cref="Dispose"/> it when it is not used any more.
    /// </summary>
    public sealed class ControlResellClient : IDisposable
    {
        /// <summary>The version of the SDK.</summary>
        public const string Version = "1.0.0";

        internal static readonly string UserAgent = "controlresell-csharp/" + Version;

        private readonly HttpClient _httpClient;
        private readonly bool _ownsHttpClient;

        /// <param name="apiKey">The key (<c>cr_live_...</c>); when null, the <c>CONTROLRESELL_API_KEY</c> environment variable.</param>
        /// <param name="options">The base URL, the retries, the timeout; <see cref="ControlResellOptions.ApiKey"/> is ignored when <paramref name="apiKey"/> is given.</param>
        public ControlResellClient(string? apiKey = null, ControlResellOptions? options = null)
        {
            options ??= new ControlResellOptions();
            if (apiKey != null) options.ApiKey = apiKey;
            var handler = new ControlResellHandler(options, options.InnerHandler ?? new HttpClientHandler());
            _httpClient = new HttpClient(handler) { BaseAddress = BaseAddressOf(options.BaseUrl) };
            if (options.Timeout.HasValue) _httpClient.Timeout = options.Timeout.Value;
            _ownsHttpClient = true;
            (Me, Accounts, Items, Favorites, Conversations, Customers, Orders, Catalogs, Webhooks) = Wire(_httpClient);
        }

        /// <summary>
        /// A client over an <see cref="HttpClient"/> you own (<c>IHttpClientFactory</c>): its <see cref="HttpClient.BaseAddress"/> is the
        /// API and its pipeline holds a <see cref="ControlResellHandler"/>, which carries the key, the retries and the errors.
        /// </summary>
        public ControlResellClient(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (httpClient.BaseAddress == null) httpClient.BaseAddress = BaseAddressOf(ControlResellOptions.DefaultBaseUrl);
            (Me, Accounts, Items, Favorites, Conversations, Customers, Orders, Catalogs, Webhooks) = Wire(_httpClient);
        }

        public IMeApi Me { get; }
        public IAccountsApi Accounts { get; }
        public IItemsApi Items { get; }
        public IFavoritesApi Favorites { get; }
        public IConversationsApi Conversations { get; }
        public ICustomersApi Customers { get; }
        public IOrdersApi Orders { get; }
        public ICatalogsApi Catalogs { get; }
        public IWebhooksApi Webhooks { get; }

        public void Dispose()
        {
            if (_ownsHttpClient) _httpClient.Dispose();
        }

        private static (IMeApi, IAccountsApi, IItemsApi, IFavoritesApi, IConversationsApi, ICustomersApi, IOrdersApi, ICatalogsApi, IWebhooksApi) Wire(HttpClient http) =>
            (new MeApi(http), new AccountsApi(http), new ItemsApi(http), new FavoritesApi(http), new ConversationsApi(http),
                new CustomersApi(http), new OrdersApi(http), new CatalogsApi(http), new WebhooksApi(http));

        /// <summary>The generated paths are relative (<c>api/public/v1/items</c>): the base address ends with a slash.</summary>
        private static Uri BaseAddressOf(string baseUrl) => new Uri(baseUrl.TrimEnd('/') + "/");
    }
}
