using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <inheritdoc cref="IWildwoodAttributionService"/>
public class WildwoodAttributionService : IWildwoodAttributionService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodAttributionService> _logger;
    private readonly string _appId;

    /// <inheritdoc />
    public bool LastResponseUnauthorized { get; private set; }

    public WildwoodAttributionService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodAttributionService> logger,
        string appId)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
        _appId = appId ?? string.Empty;
    }

    public async Task<AttributionClaimResultModel?> ClaimAsync(AttributionPayloadModel? payload, CancellationToken cancellationToken = default)
    {
        LastResponseUnauthorized = false;

        if (payload is null || (payload.FirstTouch is null && payload.LastTouch is null))
            return null;
        if (string.IsNullOrWhiteSpace(_appId) || !_sessionManager.IsAuthenticated)
            return null;

        var token = _sessionManager.GetAccessToken();
        if (string.IsNullOrEmpty(token))
            return null;

        try
        {
            // appId rides in the query string too: the server's rate-limit partition reads it and it must match the body.
            using var request = new HttpRequestMessage(HttpMethod.Post, "attribution/claim?appId=" + Uri.EscapeDataString(_appId))
            {
                Content = JsonContent.Create(AttributionClaimRequestModel.From(_appId, payload))
            };
            // On the request, not DefaultRequestHeaders: the named client is shared by the scope's services.
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            LastResponseUnauthorized = response.StatusCode == HttpStatusCode.Unauthorized;
            if (!response.IsSuccessStatusCode)
                return null;

            try
            {
                return await response.Content.ReadFromJsonAsync<AttributionClaimResultModel>(cancellationToken: cancellationToken)
                    ?? new AttributionClaimResultModel();
            }
            catch (Exception ex)
            {
                // A 2xx without a readable body is still the server's answer for these touches.
                _logger.LogDebug(ex, "Campaign Attribution claim returned an unreadable body");
                return new AttributionClaimResultModel();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Campaign Attribution claim failed");
            return null;
        }
    }
}
