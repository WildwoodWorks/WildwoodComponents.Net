using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    public class DisclaimerService : IDisclaimerService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DisclaimerService> _logger;

        public DisclaimerService(HttpClient httpClient, ILogger<DisclaimerService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
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

                var httpResponse = await _httpClient.PostAsJsonAsync("api/disclaimeracceptance/accept-bulk", payload);

                if (httpResponse.IsSuccessStatusCode)
                    return new DisclaimerAcceptanceResponse { Success = true };

                var errorMessage = httpResponse.StatusCode switch
                {
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
