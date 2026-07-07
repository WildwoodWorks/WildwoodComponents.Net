using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Client for the app-facing AI Flows with LangChain endpoints (api/ai/flows).
    /// Runs execute the published version and stream over SSE via HttpClient's
    /// response stream (EventSource can't send the Authorization header).
    /// </summary>
    public interface IAIFlowService
    {
        event EventHandler? AuthenticationFailed;

        void SetAuthToken(string token);
        void SetApiBaseUrl(string apiBaseUrl);
        void SetAppId(string? appId);

        Task<List<AIFlow>> GetFlowsAsync();

        /// <summary>
        /// Runs a flow, invoking <paramref name="onEvent"/> for each SSE frame,
        /// and returns the terminal outcome (done/interrupt/error).
        /// </summary>
        Task<AIFlowRunResult> RunFlowAsync(
            string flowId, string inputJson, string? threadId,
            Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken = default);

        /// <summary>Approves or rejects a pending human-review interrupt; approve streams the resumed run.</summary>
        Task<AIFlowRunResult> ResolveInterruptAsync(
            string runId, bool approve, string? valueJson,
            Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken = default);

        Task<List<AIFlowRunSummary>> GetThreadRunsAsync(string threadId);
    }

    public class AIFlowService : IAIFlowService
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly HttpClient _httpClient;
        private readonly ILogger<AIFlowService> _logger;

        private string _authToken = string.Empty;
        private string _apiBaseUrl = string.Empty;
        private string? _appId;
        private bool _authFailureFired;

        public event EventHandler? AuthenticationFailed;

        public AIFlowService(HttpClient httpClient, ILogger<AIFlowService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void SetAuthToken(string token)
        {
            _authToken = token ?? string.Empty;
            _authFailureFired = false; // re-arm the one-shot 401 signal for the new token
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _authToken);
        }

        public void SetApiBaseUrl(string apiBaseUrl) => _apiBaseUrl = apiBaseUrl?.TrimEnd('/') ?? string.Empty;

        public void SetAppId(string? appId) => _appId = appId;

        public async Task<List<AIFlow>> GetFlowsAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/ai/flows{AppQuery()}");
                if (!await EnsureAuthorizedAsync(response)) return new();
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AIFlow>>(Json) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load AI flows");
                return new();
            }
        }

        public Task<AIFlowRunResult> RunFlowAsync(
            string flowId, string inputJson, string? threadId,
            Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken)
        {
            var url = $"{_apiBaseUrl}/ai/flows/{Uri.EscapeDataString(flowId)}/runs/stream{AppQuery()}";
            var body = JsonSerializer.Serialize(new { inputJson, threadId }, Json);
            return StreamAsync(url, body, onEvent, cancellationToken);
        }

        public Task<AIFlowRunResult> ResolveInterruptAsync(
            string runId, bool approve, string? valueJson,
            Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken)
        {
            var url = $"{_apiBaseUrl}/ai/flows/runs/{Uri.EscapeDataString(runId)}/resume{AppQuery()}";
            var body = JsonSerializer.Serialize(new { action = approve ? "approve" : "reject", valueJson }, Json);
            return StreamAsync(url, body, onEvent, cancellationToken);
        }

        public async Task<List<AIFlowRunSummary>> GetThreadRunsAsync(string threadId)
        {
            try
            {
                var response = await _httpClient.GetAsync(
                    $"{_apiBaseUrl}/ai/flows/threads/{Uri.EscapeDataString(threadId)}/runs{AppQuery()}");
                if (!await EnsureAuthorizedAsync(response)) return new();
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<List<AIFlowRunSummary>>(Json) ?? new();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load thread runs");
                return new();
            }
        }

        // ------------------------------------------------------------------

        private async Task<AIFlowRunResult> StreamAsync(
            string url, string body, Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken)
        {
            var result = new AIFlowRunResult();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };

                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

                if (!await EnsureAuthorizedAsync(response))
                {
                    result.Status = "failed";
                    result.ErrorMessage = response.StatusCode == System.Net.HttpStatusCode.Forbidden
                        ? "You don't have access to this flow." : "Not authorized";
                    return result;
                }
                if (!response.IsSuccessStatusCode)
                {
                    result.Status = "failed";
                    result.ErrorMessage = $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}";
                    return result;
                }

                // The reject path (and any non-approve resolution) responds with a
                // plain JSON body, not an SSE stream — map it to a terminal result.
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
                if (!mediaType.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase))
                {
                    result.Status = "cancelled";
                    return result;
                }

                // SSE frame parsing + result mapping is shared with the Razor
                // WildwoodAIFlowService via WildwoodComponents.Shared.
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

        private Task<bool> EnsureAuthorizedAsync(HttpResponseMessage response)
        {
            // Only 401 is an authentication failure. 403 is a permission/feature
            // denial (e.g. tier lacks AI_FLOWS) — the token is valid, so don't
            // fire AuthenticationFailed (which prompts re-login).
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Fire once per token lifetime — a long SSE run can hit many 401s
                // and we don't want to flood a shared re-login handler.
                if (!_authFailureFired)
                {
                    _authFailureFired = true;
                    AuthenticationFailed?.Invoke(this, EventArgs.Empty);
                }
                return Task.FromResult(false);
            }
            if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return Task.FromResult(false);
            return Task.FromResult(true);
        }

        private string AppQuery() =>
            string.IsNullOrEmpty(_appId) ? string.Empty : $"?requestedAppId={Uri.EscapeDataString(_appId)}";
    }
}
