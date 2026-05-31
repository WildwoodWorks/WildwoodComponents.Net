using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Default <see cref="IWildwoodFeedbackService"/> implementation. Calls the Wildwood feedback API
/// endpoints using the named "WildwoodAPI" HttpClient. Endpoint URLs are built from a root computed
/// by stripping a trailing <c>/api</c> segment from the HttpClient's configured base address, then
/// re-adding <c>/api/...</c> per endpoint — matching the canonical vanilla widget's
/// <c>apiBase.replace(/\/api\/?$/, '')</c> and the Blazor FeedbackService.
///
/// The Bearer token is applied from the server-side session for the authenticated calls
/// (submit and vote); config and duplicate-check are anonymous.
/// </summary>
public class WildwoodFeedbackService : IWildwoodFeedbackService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodFeedbackService> _logger;
    private readonly IMemoryCache? _cache;
    private readonly string _apiRoot;

    /// <summary>How long a successfully-resolved widget config is cached (it changes rarely).</summary>
    private static readonly TimeSpan WidgetConfigTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Short negative-cache TTL when the config endpoint returns nothing (app down / not configured).
    /// Avoids hammering the API on every page render while still recovering quickly once it's healthy.
    /// </summary>
    private static readonly TimeSpan WidgetConfigNegativeTtl = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Per-call timeout for the widget-config fetch. It runs on every page render, so it must never
    /// block the page for the HttpClient's full (default 30s) timeout when the API is slow.
    /// </summary>
    private static readonly TimeSpan WidgetConfigTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodFeedbackService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodFeedbackService> logger,
        IMemoryCache? cache = null)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
        _cache = cache;
        _apiRoot = StripApiSuffix(_httpClient.BaseAddress?.ToString());
    }

    private static string WidgetCacheKey(string appId) => $"ww-feedback-widget-cfg:{appId}";

    /// <summary>
    /// Removes a trailing <c>/api</c> or <c>/api/</c> from the configured base address so the result
    /// can have per-endpoint <c>/api/...</c> paths appended (no <c>/api/api</c>).
    /// </summary>
    private static string StripApiSuffix(string? baseUrl)
    {
        if (string.IsNullOrEmpty(baseUrl))
            return string.Empty;

        var trimmed = baseUrl.TrimEnd('/');
        if (trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = trimmed.Substring(0, trimmed.Length - 4);
        }
        return trimmed.TrimEnd('/');
    }

    private string BuildUrl(string path)
    {
        var suffix = path.TrimStart('/');
        if (!string.IsNullOrEmpty(_apiRoot))
            return $"{_apiRoot}/api/{suffix}";
        return $"/api/{suffix}";
    }

    public async Task<FeedbackWidgetConfig?> GetWidgetConfigAsync(string appId)
    {
        if (string.IsNullOrEmpty(appId))
            return null;

        // The widget config is fetched server-side on every page render and changes rarely, so a
        // shared (singleton) IMemoryCache turns the per-render API round-trip into an occasional one.
        var cacheKey = WidgetCacheKey(appId);
        if (_cache != null && _cache.TryGetValue(cacheKey, out FeedbackWidgetConfig? cached))
            return cached;

        var config = await FetchWidgetConfigAsync(appId);

        // Cache successes for the full TTL; negative-cache misses briefly so a transient outage or a
        // not-yet-configured app doesn't hammer the API on every render but recovers within seconds.
        _cache?.Set(cacheKey, config, config != null ? WidgetConfigTtl : WidgetConfigNegativeTtl);

        return config;
    }

    private async Task<FeedbackWidgetConfig?> FetchWidgetConfigAsync(string appId)
    {
        // Bound this call independently of the HttpClient's (default 30s) timeout — it must not stall
        // page rendering when the API is slow or unreachable.
        using var cts = new CancellationTokenSource(WidgetConfigTimeout);
        try
        {
            var url = BuildUrl($"AppComponentConfigurations/{Uri.EscapeDataString(appId)}/feedback/widget");
            using var response = await _httpClient.GetAsync(url, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("GetWidgetConfig returned {StatusCode} for app {AppId}", response.StatusCode, appId);
                return null;
            }

            return await response.Content.ReadFromJsonAsync<FeedbackWidgetConfig>(JsonOptions, cts.Token);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error loading feedback widget config for app {AppId}", appId);
            return null;
        }
        catch (OperationCanceledException ex)
        {
            _logger.LogWarning(ex, "Timed out loading feedback widget config for app {AppId} (>{Timeout}s)",
                appId, WidgetConfigTimeout.TotalSeconds);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error loading feedback widget config for app {AppId}", appId);
            return null;
        }
    }

    public async Task<FeedbackSubmissionResult> SubmitFeedbackAsync(FeedbackSubmissionRequest request)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = BuildUrl("SystemFeedback");
            using var response = await _httpClient.PostAsJsonAsync(url, request);

            if (response.IsSuccessStatusCode)
            {
                return new FeedbackSubmissionResult { Success = true };
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new FeedbackSubmissionResult
                {
                    Success = false,
                    RateLimited = true,
                    ErrorMessage = "Too many submissions. Please try again later."
                };
            }

            var errorMessage = await ExtractErrorMessageAsync(response);
            _logger.LogWarning("SubmitFeedback returned {StatusCode} for app {AppId}", response.StatusCode, request.AppId);
            return new FeedbackSubmissionResult { Success = false, ErrorMessage = errorMessage };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Network error submitting feedback for app {AppId}", request.AppId);
            return new FeedbackSubmissionResult
            {
                Success = false,
                ErrorMessage = "Could not reach the server. Please check your connection and try again."
            };
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogError(ex, "Request timeout submitting feedback for app {AppId}", request.AppId);
            return new FeedbackSubmissionResult
            {
                Success = false,
                ErrorMessage = "The request timed out. Please try again."
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error submitting feedback for app {AppId}", request.AppId);
            return new FeedbackSubmissionResult
            {
                Success = false,
                ErrorMessage = "An unexpected error occurred. Please try again."
            };
        }
    }

    public async Task<FeedbackDuplicateCheckResult> CheckDuplicateAsync(string title, string appId)
    {
        var empty = new FeedbackDuplicateCheckResult { HasPotentialDuplicate = false };

        if (string.IsNullOrWhiteSpace(title))
            return empty;

        try
        {
            var url = BuildUrl($"SystemFeedback/duplicate-check?title={Uri.EscapeDataString(title.Trim())}");
            if (!string.IsNullOrEmpty(appId))
            {
                url += $"&appId={Uri.EscapeDataString(appId)}";
            }

            using var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("CheckDuplicate returned {StatusCode} for app {AppId}", response.StatusCode, appId);
                return empty;
            }

            var result = await response.Content.ReadFromJsonAsync<FeedbackDuplicateCheckResult>(JsonOptions);
            return result ?? empty;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error checking for duplicate feedback for app {AppId}", appId);
            return empty;
        }
    }

    public async Task<FeedbackVoteResult> VoteAsync(string feedbackId)
    {
        if (string.IsNullOrEmpty(feedbackId))
            return new FeedbackVoteResult { Success = false, ErrorMessage = "Invalid feedback id." };

        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = BuildUrl($"SystemFeedback/{Uri.EscapeDataString(feedbackId)}/vote");
            using var response = await _httpClient.PostAsync(url, content: null);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<FeedbackVoteResult>(JsonOptions);
                if (result != null)
                {
                    result.Success = true;
                    return result;
                }
                return new FeedbackVoteResult { Success = true };
            }

            _logger.LogWarning("Vote returned {StatusCode} for feedback {FeedbackId}", response.StatusCode, feedbackId);
            return new FeedbackVoteResult { Success = false, ErrorMessage = "Could not record your vote." };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error voting on feedback {FeedbackId}", feedbackId);
            return new FeedbackVoteResult { Success = false, ErrorMessage = "Network error while voting." };
        }
    }

    /// <summary>
    /// Extracts a user-facing error message from a failed response body, preferring an
    /// <c>error</c> or <c>title</c> JSON property, then falling back to a status-code message.
    /// </summary>
    private static async Task<string> ExtractErrorMessageAsync(HttpResponseMessage response)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync();
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    using var doc = JsonDocument.Parse(text);
                    if (doc.RootElement.ValueKind == JsonValueKind.Object)
                    {
                        if (doc.RootElement.TryGetProperty("error", out var errorProp) &&
                            errorProp.ValueKind == JsonValueKind.String)
                        {
                            var msg = errorProp.GetString();
                            if (!string.IsNullOrEmpty(msg))
                                return msg;
                        }
                        if (doc.RootElement.TryGetProperty("title", out var titleProp) &&
                            titleProp.ValueKind == JsonValueKind.String)
                        {
                            var msg = titleProp.GetString();
                            if (!string.IsNullOrEmpty(msg))
                                return msg;
                        }
                    }
                }
                catch (JsonException)
                {
                    // Body was not JSON — fall through to the status-code message.
                }
            }
        }
        catch
        {
            // Ignore body read failures and use the status-code message.
        }

        return $"Failed to submit feedback (status {(int)response.StatusCode}).";
    }
}
