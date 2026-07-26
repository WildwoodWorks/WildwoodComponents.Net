using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace WildwoodComponents.Shared.Seeder
{
    /// <summary>Non-2xx response surfaced with status code and body.</summary>
    public sealed class SeederApiException : Exception
    {
        public HttpStatusCode StatusCode { get; }
        public string ResponseBody { get; }

        public SeederApiException(string method, string path, HttpStatusCode statusCode, string body)
            : base($"{method} {path} failed with {(int)statusCode} {statusCode}: {Truncate(body)}")
        {
            StatusCode = statusCode;
            ResponseBody = body;
        }

        private static string Truncate(string s) =>
            string.IsNullOrWhiteSpace(s) ? "(empty body)" : (s.Length <= 800 ? s : s[..800] + "…");
    }

    /// <summary>
    /// Default <see cref="ISeederApiClient"/>. Generalizes TrailForecast.Installer's
    /// WildwoodAdminClient: camelCase JSON, Bearer after login, optional X-API-Key,
    /// loopback dev-cert acceptance, and a dry-run write guard.
    /// </summary>
    public sealed class SeederApiClient : ISeederApiClient, IDisposable
    {
        public static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly HttpClient _http;
        private readonly SeederOptions _options;
        private readonly ILogger<SeederApiClient> _logger;
        private readonly SemaphoreSlim _loginLock = new(1, 1);
        private bool _authenticated;

        public string? BearerToken { get; private set; }
        public string? ApiKey { get; set; }

        public SeederApiClient(SeederOptions options, ILogger<SeederApiClient> logger)
        {
            _options = options;
            _logger = logger;
            ApiKey = options.ApiKey;

            HttpMessageHandler handler;
            if (options.PrimaryHandlerFactory is not null)
            {
                // App-supplied handler (e.g. IPv4-preferred connects) — used as-is, no dev-cert bypass.
                handler = options.PrimaryHandlerFactory();
            }
            else
            {
                var defaultHandler = new HttpClientHandler();
                if (Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var uri) && uri.IsLoopback)
                {
                    defaultHandler.ServerCertificateCustomValidationCallback =
                        HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
                }
                handler = defaultHandler;
            }

            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/"),
                Timeout = TimeSpan.FromMinutes(5), // MCP tool generation fetches remote specs
            };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("WildwoodComponents.Seeder/1.0");
        }

        public async Task EnsureAuthenticatedAsync(CancellationToken ct = default)
        {
            if (_authenticated) return;
            await _loginLock.WaitAsync(ct);
            try
            {
                if (_authenticated) return;

                // A pre-issued token wins over the login flow (parity with consuming apps' own
                // token providers; the token expires and cannot be refreshed here).
                if (!string.IsNullOrWhiteSpace(_options.BearerToken))
                {
                    BearerToken = _options.BearerToken;
                    _authenticated = true;
                    _logger.LogInformation("Seeder using the pre-issued bearer token (no login).");
                    return;
                }

                // API-key mode (primary): the app's X-API-Key is already attached to every request,
                // so no user login is needed. Mint the key with the tiers:manage scope so the
                // tier-catalog tasks can define tiers/add-ons/feature definitions headlessly; scope
                // stays confined to the key's own app/company.
                if (!string.IsNullOrWhiteSpace(ApiKey))
                {
                    if (!string.IsNullOrWhiteSpace(_options.AdminEmail) || !string.IsNullOrWhiteSpace(_options.AdminPassword))
                        _logger.LogWarning(
                            "Seeder AdminEmail/AdminPassword are deprecated and ignored because ApiKey is set. " +
                            "Remove them; mint the X-API-Key with the tiers:manage scope instead.");
                    _authenticated = true;
                    _logger.LogInformation("Seeder authenticating via X-API-Key (app-scoped; no user login).");
                    return;
                }

                if (!_options.HasCredentials)
                    throw new InvalidOperationException(
                        "Seeder has no credentials. Set Wildwood:Seeder:ApiKey to the app's X-API-Key — mint it " +
                        "with the tiers:manage scope so the tier-catalog tasks can manage the catalog. " +
                        "(Deprecated fallback: AdminEmail + AdminPassword for a CompanyAdmin service account " +
                        "without 2FA, or a pre-issued BearerToken.)");

                _logger.LogWarning(
                    "Seeder email/password login is deprecated: mint the app X-API-Key with the tiers:manage " +
                    "scope and set ApiKey instead. Credential login will be removed in a future major.");

                var body = new SeederLoginRequest
                {
                    Username = _options.AdminEmail!,
                    Email = _options.AdminEmail!,
                    Password = _options.AdminPassword!,
                    AppId = _options.EffectiveLoginAppId,
                };
                var response = await SendAsync(HttpMethod.Post, "api/auth/login", body, isLogin: true, ct: ct);
                var login = Deserialize<SeederLoginResponse>(response, "api/auth/login");

                if (login.RequiresTwoFactor)
                    throw new InvalidOperationException(
                        "The seeder admin account requires two-factor authentication. Use a CompanyAdmin service account without 2FA.");

                BearerToken = login.JwtToken;
                _authenticated = true;
                _logger.LogInformation("Seeder authenticated to WildwoodAPI as {Email}", login.Email);
            }
            finally
            {
                _loginLock.Release();
            }
        }

        public async Task<T> GetAsync<T>(string path, CancellationToken ct = default)
            => Deserialize<T>(await SendAsync(HttpMethod.Get, path, null, ct: ct), path);

        public async Task<T?> GetOrDefaultAsync<T>(string path, CancellationToken ct = default) where T : class
        {
            var body = await SendAsync(HttpMethod.Get, path, null, allowNotFound: true, ct: ct);
            return body is null ? null : Deserialize<T>(body, path);
        }

        public async Task<T> PostAsync<T>(string path, object? body, CancellationToken ct = default)
            => Deserialize<T>(await SendAsync(HttpMethod.Post, path, body, ct: ct), path);

        public Task PostAsync(string path, object? body, CancellationToken ct = default)
            => SendAsync(HttpMethod.Post, path, body, ct: ct);

        public async Task<T> PutAsync<T>(string path, object body, CancellationToken ct = default)
            => Deserialize<T>(await SendAsync(HttpMethod.Put, path, body, ct: ct), path);

        public Task PutAsync(string path, object body, CancellationToken ct = default)
            => SendAsync(HttpMethod.Put, path, body, ct: ct);

        // ---- seeder ledger / history / config ----

        public Task<SeederConfigurationDto> GetSeederConfigurationAsync(string appId, CancellationToken ct = default)
            => GetAsync<SeederConfigurationDto>($"api/AppComponentConfigurations/{appId}/seeder-configuration", ct);

        public async Task<List<SeedTaskLedgerDto>> GetLedgerAsync(string appId, string? environment = null, CancellationToken ct = default)
        {
            var path = $"api/AppComponentConfigurations/{appId}/seeder/ledger";
            if (!string.IsNullOrWhiteSpace(environment))
                path += $"?environment={Uri.EscapeDataString(environment)}";
            return await GetAsync<List<SeedTaskLedgerDto>>(path, ct);
        }

        public Task UpsertLedgerAsync(string appId, UpsertSeedLedgerRequest request, CancellationToken ct = default)
            => PostAsync($"api/AppComponentConfigurations/{appId}/seeder/ledger", request, ct);

        public Task<SeedRunHistoryDto> RecordRunAsync(string appId, RecordSeedRunRequest request, CancellationToken ct = default)
            => PostAsync<SeedRunHistoryDto>($"api/AppComponentConfigurations/{appId}/seeder/history", request, ct);

        private async Task<string?> SendAsync(
            HttpMethod method, string path, object? body, bool allowNotFound = false, bool isLogin = false, CancellationToken ct = default)
        {
            if (_options.DryRun && method != HttpMethod.Get && !isLogin)
                throw new InvalidOperationException(
                    $"BUG: attempted write ({method} {path}) during dry-run. Tasks must guard writes with SeederContext.ShouldWrite.");

            using var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrEmpty(BearerToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", BearerToken);
            if (!string.IsNullOrEmpty(ApiKey))
                request.Headers.Add("X-API-Key", ApiKey);
            if (body is not null || method == HttpMethod.Post || method == HttpMethod.Put)
            {
                var json = body is null ? "{}" : JsonSerializer.Serialize(body, body.GetType(), JsonOptions);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }

            using var response = await _http.SendAsync(request, ct);
            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (allowNotFound && response.StatusCode == HttpStatusCode.NotFound)
                return null;
            if (!response.IsSuccessStatusCode)
                throw new SeederApiException(method.Method, path, response.StatusCode, responseBody);

            return responseBody;
        }

        private static T Deserialize<T>(string? body, string path)
        {
            if (string.IsNullOrWhiteSpace(body))
                throw new InvalidOperationException($"Empty response body from {path} (expected {typeof(T).Name}).");
            try
            {
                return JsonSerializer.Deserialize<T>(body, JsonOptions)
                    ?? throw new InvalidOperationException($"Response from {path} deserialized to null.");
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse response from {path} as {typeof(T).Name}: {ex.Message}");
            }
        }

        public void Dispose() => _http.Dispose();
    }
}
