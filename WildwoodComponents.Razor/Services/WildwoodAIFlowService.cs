using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// AI Flows (LangGraph) service implementation that calls the WildwoodAPI api/ai/flows
/// endpoints. Razor Pages equivalent of WildwoodComponents.Blazor.Services.AIFlowService.
/// Uses server-side session for JWT token management via IWildwoodSessionManager.
/// </summary>
public class WildwoodAIFlowService : IWildwoodAIFlowService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodAIFlowService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions JsonWeb = new(JsonSerializerDefaults.Web);

    public WildwoodAIFlowService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodAIFlowService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<List<AIFlow>> GetFlowsAsync(string? appId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"ai/flows{AppQuery(appId)}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AIFlow>>(JsonOptions);
                return result ?? new();
            }

            _logger.LogWarning("Failed to get AI flows: {StatusCode}", response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI flows");
        }
        return new();
    }

    public async Task<List<AIFlowRunSummary>> GetThreadRunsAsync(string threadId, string? appId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync(
                $"ai/flows/threads/{Uri.EscapeDataString(threadId)}/runs{AppQuery(appId)}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<List<AIFlowRunSummary>>(JsonOptions);
                return result ?? new();
            }

            _logger.LogWarning("Failed to get thread runs for {ThreadId}: {StatusCode}", threadId, response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load thread runs");
        }
        return new();
    }

    public Task<AIFlowRunResult> RunFlowAsync(
        string flowId, string inputJson, string? threadId, string? appId,
        Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken = default)
    {
        var url = $"ai/flows/{Uri.EscapeDataString(flowId)}/runs/stream{AppQuery(appId)}";
        var body = JsonSerializer.Serialize(new { inputJson, threadId }, JsonWeb);
        return StreamAsync(url, body, onEvent, cancellationToken);
    }

    public Task<AIFlowRunResult> ResolveInterruptAsync(
        string runId, bool approve, string? valueJson, string? appId,
        Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken = default)
    {
        var url = $"ai/flows/runs/{Uri.EscapeDataString(runId)}/resume{AppQuery(appId)}";
        var body = JsonSerializer.Serialize(new { action = approve ? "approve" : "reject", valueJson }, JsonWeb);
        return StreamAsync(url, body, onEvent, cancellationToken);
    }

    // ------------------------------------------------------------------

    private async Task<AIFlowRunResult> StreamAsync(
        string url, string body, Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken)
    {
        var result = new AIFlowRunResult();
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };

            using var response = await _httpClient.SendAsync(
                request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                result.Status = "failed";
                // Only 401 is an authentication failure; 403 is a permission/feature denial
                // (e.g. tier lacks AI_FLOWS) — mirror the Blazor AIFlowService messages.
                result.ErrorMessage = response.StatusCode switch
                {
                    System.Net.HttpStatusCode.Unauthorized => "Not authorized",
                    System.Net.HttpStatusCode.Forbidden => "You don't have access to this flow.",
                    _ => $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}"
                };
                return result;
            }

            // The reject path (and any non-approve resolution) responds with a plain JSON
            // body, not an SSE stream — map it to a terminal result.
            var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!mediaType.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
            {
                result.Status = "cancelled";
                return result;
            }

            // SSE frame parsing + result mapping is shared with the Blazor
            // AIFlowService via WildwoodComponents.Shared.
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await AIFlowStreamParser.ParseAsync(stream, result, onEvent, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            result.Status = result.Status == "unknown" ? "cancelled" : result.Status;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI flow run stream failed");
            result.Status = "failed";
            result.ErrorMessage = ex.Message;
        }
        return result;
    }

    private static string AppQuery(string? appId) =>
        string.IsNullOrEmpty(appId) ? string.Empty : $"?requestedAppId={Uri.EscapeDataString(appId)}";
}
