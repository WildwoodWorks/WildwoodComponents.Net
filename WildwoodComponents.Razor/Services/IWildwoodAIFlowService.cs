using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Client for the app-facing AI Flows with LangChain endpoints (api/ai/flows).
/// Razor Pages equivalent of WildwoodComponents.Blazor.Services.IAIFlowService.
/// Runs execute the published version and stream over SSE via HttpClient's response
/// stream. In Razor the user-facing run/resume behavior lives in wwwroot/js/ai-flow.js
/// (fetch through the host app's proxy); the run/resume methods here exist for service
/// surface parity and for host proxies that relay the SSE stream server-side.
/// </summary>
public interface IWildwoodAIFlowService
{
    /// <summary>Published flows the current user can run. Optional app scope via requestedAppId.</summary>
    Task<List<AIFlow>> GetFlowsAsync(string? appId = null);

    /// <summary>Prior runs on a conversation thread (newest first, per the API).</summary>
    Task<List<AIFlowRunSummary>> GetThreadRunsAsync(string threadId, string? appId = null);

    /// <summary>
    /// Runs a flow, invoking <paramref name="onEvent"/> for each SSE frame, and returns the
    /// terminal outcome (done/interrupt/error). Parity placeholder for the Blazor service —
    /// the Razor component's live path is ai-flow.js.
    /// </summary>
    Task<AIFlowRunResult> RunFlowAsync(
        string flowId, string inputJson, string? threadId, string? appId,
        Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Approves or rejects a pending human-review interrupt; approve streams the resumed run,
    /// reject responds with plain JSON (mapped to a terminal "cancelled" result). Parity
    /// placeholder for the Blazor service — the Razor component's live path is ai-flow.js.
    /// </summary>
    Task<AIFlowRunResult> ResolveInterruptAsync(
        string runId, bool approve, string? valueJson, string? appId,
        Func<AIFlowRunEvent, Task>? onEvent, CancellationToken cancellationToken = default);
}
