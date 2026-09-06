using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Calls the Wildwood disclaimer-acceptance API. Every path this service builds already starts
    /// with <c>api/</c>, so the client's <see cref="HttpClient.BaseAddress"/> must be the API ROOT
    /// (e.g. <c>https://api.example.com/</c>), never the <c>/api/</c> sub-path.
    /// </summary>
    public class DisclaimerService : IDisclaimerService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DisclaimerService> _logger;
        private string? _authToken;

        public DisclaimerService(HttpClient httpClient, ILogger<DisclaimerService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;

            // A base address that already ends in /api produces /api/api/... — every call 404s, and
            // because the service maps a 404 to a friendly message rather than throwing, the only
            // symptom is "the disclaimer version was not found". Say so once, loudly, at construction.
            var basePath = httpClient.BaseAddress?.AbsolutePath.TrimEnd('/');
            if (basePath != null && basePath.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "DisclaimerService was given the base address {BaseAddress}, which already ends in /api. " +
                    "This service prefixes 'api/' itself, so requests will go to /api/api/... and fail. " +
                    "Register it against the API root instead.",
                    httpClient.BaseAddress);
            }
        }

        public void SetAuthToken(string? token)
        {
            _authToken = string.IsNullOrEmpty(token) ? null : token;
        }

        /// <summary>
        /// Builds a request, attaching the bearer token per-request rather than on the client — the
        /// HttpClient may be a shared named client, so mutating its DefaultRequestHeaders would leak
        /// one user's token into every other consumer of the same instance.
        /// </summary>
        private HttpRequestMessage CreateRequest(HttpMethod method, string url, bool includeAuth)
        {
            var request = new HttpRequestMessage(method, url);
            if (includeAuth && !string.IsNullOrEmpty(_authToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
            }
            return request;
        }

        public async Task<PendingDisclaimersResponse> GetPendingDisclaimersAsync(string appId, string? userId = null, string? showOn = null)
        {
            try
            {
                var url = $"api/disclaimeracceptance/pending/{appId}";
                var queryParams = new List<string>();
                if (!string.IsNullOrEmpty(userId))
                    queryParams.Add($"userId={Uri.EscapeDataString(userId)}");
                if (!string.IsNullOrEmpty(showOn))
                    queryParams.Add($"showOn={Uri.EscapeDataString(showOn)}");
                if (queryParams.Any())
                    url += "?" + string.Join("&", queryParams);

                var httpResponse = await _httpClient.GetAsync(url);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    var errorMessage = httpResponse.StatusCode switch
                    {
                        HttpStatusCode.NotFound => "Disclaimer configuration not found for this application.",
                        HttpStatusCode.TooManyRequests => "Too many requests. Please wait a moment and try again.",
                        HttpStatusCode.InternalServerError => "The server encountered an error. Please try again later.",
                        _ => $"Failed to load disclaimers (HTTP {(int)httpResponse.StatusCode})."
                    };
                    _logger.LogWarning("GetPendingDisclaimers returned {StatusCode} for app {AppId}", httpResponse.StatusCode, appId);
                    return new PendingDisclaimersResponse { ErrorMessage = errorMessage };
                }

                var response = await httpResponse.Content.ReadFromJsonAsync<PendingDisclaimersResponse>();
                return response ?? new PendingDisclaimersResponse();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error getting pending disclaimers for app {AppId}", appId);
                return new PendingDisclaimersResponse { ErrorMessage = "Unable to connect to the server. Please check your connection." };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request timeout getting pending disclaimers for app {AppId}", appId);
                return new PendingDisclaimersResponse { ErrorMessage = "The request timed out. Please try again." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error getting pending disclaimers for app {AppId}", appId);
                return new PendingDisclaimersResponse { ErrorMessage = "An unexpected error occurred while loading disclaimers." };
            }
        }

        public async Task<DisclaimerAcceptanceResponse> AcceptDisclaimerAsync(string appId, string companyDisclaimerId, string companyDisclaimerVersionId)
        {
            try
            {
                var payload = new
                {
                    AppId = appId,
                    CompanyDisclaimerId = companyDisclaimerId,
                    CompanyDisclaimerVersionId = companyDisclaimerVersionId
                };

                var request = CreateRequest(HttpMethod.Post, "api/disclaimeracceptance/accept", includeAuth: true);
                request.Content = JsonContent.Create(payload);
                var httpResponse = await _httpClient.SendAsync(request);

                if (httpResponse.IsSuccessStatusCode)
                    return new DisclaimerAcceptanceResponse { Success = true };

                var errorMessage = httpResponse.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
                    HttpStatusCode.NotFound => "The disclaimer version was not found. Please refresh and try again.",
                    HttpStatusCode.BadRequest => "The disclaimer acceptance is invalid. Please refresh and try again.",
                    HttpStatusCode.TooManyRequests => "Too many requests. Please wait a moment and try again.",
                    HttpStatusCode.InternalServerError => "The server encountered an error. Please try again later.",
                    _ => $"Failed to submit acceptance (HTTP {(int)httpResponse.StatusCode})."
                };
                _logger.LogWarning("AcceptDisclaimer returned {StatusCode} for app {AppId}", httpResponse.StatusCode, appId);
                return new DisclaimerAcceptanceResponse { Success = false, ErrorMessage = errorMessage };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error accepting disclaimer for app {AppId}", appId);
                return new DisclaimerAcceptanceResponse { Success = false, ErrorMessage = "Unable to connect to the server. Please check your connection." };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request timeout accepting disclaimer for app {AppId}", appId);
                return new DisclaimerAcceptanceResponse { Success = false, ErrorMessage = "The request timed out. Please try again." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error accepting disclaimer for app {AppId}", appId);
                return new DisclaimerAcceptanceResponse { Success = false, ErrorMessage = "An unexpected error occurred. Please try again." };
            }
        }

        public async Task<DisclaimerAcceptanceResponse> AcceptDisclaimersAsync(string appId, List<DisclaimerAcceptanceResult> acceptances)
        {
            try
            {
                var payload = new
                {
                    AppId = appId,
                    Acceptances = acceptances.Select(a => new
                    {
                        a.CompanyDisclaimerId,
                        a.CompanyDisclaimerVersionId
                    }).ToList()
                };

                var request = CreateRequest(HttpMethod.Post, "api/disclaimeracceptance/accept-bulk", includeAuth: true);
                request.Content = JsonContent.Create(payload);
                var httpResponse = await _httpClient.SendAsync(request);

                if (httpResponse.IsSuccessStatusCode)
                    return new DisclaimerAcceptanceResponse { Success = true };

                var errorMessage = httpResponse.StatusCode switch
                {
                    HttpStatusCode.Unauthorized => "Your session has expired. Please sign in again.",
                    HttpStatusCode.BadRequest => "One or more disclaimer versions are invalid. Please refresh and try again.",
                    HttpStatusCode.TooManyRequests => "Too many requests. Please wait a moment and try again.",
                    HttpStatusCode.InternalServerError => "The server encountered an error. Please try again later.",
                    _ => $"Failed to submit acceptances (HTTP {(int)httpResponse.StatusCode})."
                };
                _logger.LogWarning("AcceptDisclaimers returned {StatusCode} for app {AppId}", httpResponse.StatusCode, appId);
                return new DisclaimerAcceptanceResponse { Success = false, ErrorMessage = errorMessage };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error accepting disclaimers for app {AppId}", appId);
                return new DisclaimerAcceptanceResponse { Success = false, ErrorMessage = "Unable to connect to the server. Please check your connection." };
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex, "Request timeout accepting disclaimers for app {AppId}", appId);
                return new DisclaimerAcceptanceResponse { Success = false, ErrorMessage = "The request timed out. Please try again." };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error accepting disclaimers for app {AppId}", appId);
                return new DisclaimerAcceptanceResponse { Success = false, ErrorMessage = "An unexpected error occurred. Please try again." };
            }
        }
    }
}
