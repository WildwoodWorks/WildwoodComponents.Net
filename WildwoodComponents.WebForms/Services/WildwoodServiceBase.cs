using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using WildwoodComponents.WebForms.Logging;
using WildwoodComponents.WebForms.Session;

namespace WildwoodComponents.WebForms.Services
{
    /// <summary>
    /// Shared plumbing for the WebForms services: it issues requests against the
    /// WildwoodAPI, attaches the caller's bearer token to each one, and turns responses
    /// into models.
    /// </summary>
    /// <remarks>
    /// Every request carries its <c>Authorization</c> header on the
    /// <see cref="HttpRequestMessage"/> itself. The package shares one
    /// <see cref="System.Net.Http.HttpClient"/> for the life of the application, so setting
    /// the header on <c>DefaultRequestHeaders</c> — as the Razor package's
    /// <c>ApplyAuthorizationHeader</c> does — would publish one user's token to every
    /// request in flight, including other users'.
    /// <para>
    /// Failures are logged and turned into an empty or default result rather than thrown,
    /// matching the Razor services: a component that cannot reach the API renders its empty
    /// state instead of yellow-screening the host's page.
    /// </para>
    /// </remarks>
    public abstract class WildwoodServiceBase
    {
        /// <summary>
        /// Web defaults: camelCase on the way out and case-insensitive on the way in. This
        /// reproduces the Razor services exactly, where <c>PostAsJsonAsync</c> serializes
        /// with <see cref="JsonSerializerDefaults.Web"/> and their own case-insensitive
        /// options handle the response.
        /// </summary>
        protected static readonly JsonSerializerOptions JsonOptions =
            new JsonSerializerOptions(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;

        /// <summary>Creates a service over the given client and session.</summary>
        /// <param name="httpClient">The shared client; never mutated.</param>
        /// <param name="sessionManager">Supplies the current user's bearer token.</param>
        /// <param name="logger">Diagnostics sink; null selects a silent logger.</param>
        /// <param name="appId">The Wildwood app id, for endpoints that take one.</param>
        protected WildwoodServiceBase(
            HttpClient httpClient,
            IWildwoodSessionManager sessionManager,
            IWildwoodLogger? logger,
            string? appId)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            SessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            Logger = logger ?? NullWildwoodLogger.Instance;
            AppId = appId ?? string.Empty;
        }

        /// <summary>Tokens for the current request.</summary>
        protected IWildwoodSessionManager SessionManager { get; }

        /// <summary>Diagnostics sink.</summary>
        protected IWildwoodLogger Logger { get; }

        /// <summary>The configured Wildwood app id, or an empty string.</summary>
        protected string AppId { get; }

        /// <summary>
        /// Sends one request with the current user's bearer token attached. The caller owns
        /// the returned response and must dispose it.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path relative to the configured base URL, e.g. <c>auth/login</c>.</param>
        /// <param name="body">Optional body, serialized as JSON.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        protected async Task<HttpResponseMessage> SendAsync(
            HttpMethod method,
            string path,
            object? body = null,
            CancellationToken cancellationToken = default)
        {
            using (var request = new HttpRequestMessage(method, path))
            {
                var authorization = SessionManager.GetAuthorizationHeader();
                if (authorization != null)
                {
                    request.Headers.Authorization = authorization;
                }

                if (body != null)
                {
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(body, JsonOptions),
                        Encoding.UTF8,
                        "application/json");
                }

                return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sends a request and deserializes a successful JSON response, returning the
        /// default value when the call fails for any reason.
        /// </summary>
        /// <typeparam name="TResult">Type to deserialize into.</typeparam>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path relative to the base URL.</param>
        /// <param name="body">Optional JSON body.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        protected async Task<TResult?> SendForResultAsync<TResult>(
            HttpMethod method,
            string path,
            object? body = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using (var response = await SendAsync(method, path, body, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        await LogFailureAsync(method, path, response).ConfigureAwait(false);
                        return default;
                    }

                    return await ReadJsonAsync<TResult>(response).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                throw; // The caller asked to stop, or the client timed out; not a service failure.
            }
            catch (Exception ex)
            {
                Logger.Error("Wildwood " + method.Method + " " + path + " failed.", ex);
                return default;
            }
        }

        /// <summary>
        /// Sends a request and reports only whether it succeeded. For endpoints whose
        /// response body carries nothing the caller needs.
        /// </summary>
        /// <param name="method">HTTP method.</param>
        /// <param name="path">Path relative to the base URL.</param>
        /// <param name="body">Optional JSON body.</param>
        /// <param name="cancellationToken">Cancels the request.</param>
        protected async Task<bool> SendForSuccessAsync(
            HttpMethod method,
            string path,
            object? body = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                using (var response = await SendAsync(method, path, body, cancellationToken).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        await LogFailureAsync(method, path, response).ConfigureAwait(false);
                        return false;
                    }

                    return true;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error("Wildwood " + method.Method + " " + path + " failed.", ex);
                return false;
            }
        }

        /// <summary>
        /// Deserializes a response body, returning the default value for an empty body or
        /// malformed JSON.
        /// </summary>
        /// <typeparam name="TResult">Type to deserialize into.</typeparam>
        /// <param name="response">The response to read.</param>
        protected async Task<TResult?> ReadJsonAsync<TResult>(HttpResponseMessage response)
        {
            if (response == null) throw new ArgumentNullException(nameof(response));

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return default;
            }

            try
            {
                return JsonSerializer.Deserialize<TResult>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                Logger.Error("Wildwood could not read a " + typeof(TResult).Name + " from the API response.", ex);
                return default;
            }
        }

        /// <summary>
        /// Records a non-success response. The body is included because the API's error
        /// payload usually names the actual problem, and it is truncated because an error
        /// page can be arbitrarily long.
        /// </summary>
        private async Task LogFailureAsync(HttpMethod method, string path, HttpResponseMessage response)
        {
            string detail;
            try
            {
                var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                detail = body.Length > 512 ? body.Substring(0, 512) + "..." : body;
            }
            catch (Exception)
            {
                detail = "<response body unavailable>";
            }

            Logger.Warn(
                "Wildwood " + method.Method + " " + path + " returned " +
                (int)response.StatusCode + " " + response.ReasonPhrase + ". " + detail);
        }
    }
}
