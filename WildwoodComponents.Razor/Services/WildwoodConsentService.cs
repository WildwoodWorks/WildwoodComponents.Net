using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Talks to the anonymous Wildwood consent endpoints. The consent banner's client engine can call
/// these directly; this server-side path backs the optional same-origin proxy (a copy-paste proxy
/// controller is shown in the component README) so host apps can avoid cross-origin CORS.
/// </summary>
public class WildwoodConsentService : IWildwoodConsentService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<WildwoodConsentService> _logger;

    public WildwoodConsentService(HttpClient httpClient, ILogger<WildwoodConsentService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string?> GetConfigRawAsync(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return null;
        try
        {
            // The consent config endpoint is anonymous — no Authorization header applied.
            using var response = await _httpClient.GetAsync($"consent/config?appId={Uri.EscapeDataString(appId)}");
            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadAsStringAsync();
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to load consent config for app {AppId}", appId); }
        return null;
    }

    public async Task RecordDecisionAsync(ConsentRecordModel record)
    {
        if (string.IsNullOrEmpty(record?.AppId)) return;
        try
        {
            // Recording is best-effort; the appId is also sent as a query param so the server's
            // per-app rate-limit partition applies to the record path.
            using var response = await _httpClient.PostAsJsonAsync(
                $"consent/record?appId={Uri.EscapeDataString(record.AppId)}", record);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Consent record returned {Status} for app {AppId}", response.StatusCode, record.AppId);
            }
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Failed to record consent decision for app {AppId}", record.AppId); }
    }
}
