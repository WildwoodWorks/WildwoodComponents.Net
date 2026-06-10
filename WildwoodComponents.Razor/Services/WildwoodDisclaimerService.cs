using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public class WildwoodDisclaimerService : IWildwoodDisclaimerService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodDisclaimerService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodDisclaimerService(HttpClient httpClient, IWildwoodSessionManager sessionManager, ILogger<WildwoodDisclaimerService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<PendingDisclaimersResponse?> GetPendingDisclaimersAsync(string appId, string? userId = null, string? showOn = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = $"disclaimeracceptance/pending/{appId}";
            var queryParams = new List<string>();
            if (!string.IsNullOrEmpty(userId)) queryParams.Add($"userId={Uri.EscapeDataString(userId)}");
            if (!string.IsNullOrEmpty(showOn)) queryParams.Add($"showOn={Uri.EscapeDataString(showOn)}");
            if (queryParams.Count > 0) url += "?" + string.Join("&", queryParams);

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<PendingDisclaimersResponse>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get pending disclaimers"); }
        return null;
    }

    public async Task<ApiResult> AcceptDisclaimerAsync(string appId, string companyDisclaimerId, string companyDisclaimerVersionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new
            {
                AppId = appId,
                CompanyDisclaimerId = companyDisclaimerId,
                CompanyDisclaimerVersionId = companyDisclaimerVersionId
            };
            using var response = await _httpClient.PostAsJsonAsync("disclaimeracceptance/accept", payload);
            if (response.IsSuccessStatusCode)
                return ApiResult.Ok("Disclaimer accepted");

            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return ApiResult.Fail(error?.Message ?? "Failed to accept disclaimer");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept disclaimer");
            return ApiResult.Fail("An error occurred while accepting the disclaimer");
        }
    }

    public async Task<ApiResult> AcceptDisclaimersAsync(string appId, List<DisclaimerAcceptanceResult> acceptances)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { AppId = appId, Acceptances = acceptances };
            using var response = await _httpClient.PostAsJsonAsync("disclaimeracceptance/accept-bulk", payload);
            if (response.IsSuccessStatusCode)
                return ApiResult.Ok("Disclaimers accepted");

            var content = await response.Content.ReadAsStringAsync();
            var error = JsonSerializer.Deserialize<ApiErrorResponse>(content, JsonOptions);
            return ApiResult.Fail(error?.Message ?? "Failed to accept disclaimers");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to accept disclaimers");
            return ApiResult.Fail("An error occurred while accepting disclaimers");
        }
    }
}
