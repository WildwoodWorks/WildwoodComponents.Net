using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
// The namespace segment "AIFlow" shadows the Shared.Models.AIFlow type — alias it.
using AIFlowModel = WildwoodComponents.Shared.Models.AIFlow;

namespace WildwoodComponents.Razor.Components.AIFlow;

/// <summary>
/// ViewComponent that runs published "AI Flows with LangChain" for an app user: flow picker
/// (or fixed flowId), an auto-generated input form from the flow's state channels, live
/// streamed progress over SSE, human-in-the-loop approval, and output rendering.
/// Client-side JavaScript (ai-flow.js) handles the run/resume streaming and state.
/// Razor Pages equivalent of WildwoodComponents.Blazor AIFlowComponent.
/// </summary>
public class AIFlowViewComponent : ViewComponent
{
    private readonly IWildwoodAIFlowService _flowService;
    private readonly ILogger<AIFlowViewComponent> _logger;

    public AIFlowViewComponent(IWildwoodAIFlowService flowService, ILogger<AIFlowViewComponent> logger)
    {
        _flowService = flowService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the AI flow component
    /// </summary>
    /// <param name="appId">Optional app scope forwarded to the API as ?requestedAppId=</param>
    /// <param name="flowId">Fixed flow to run; when null the component shows a flow picker</param>
    /// <param name="proxyBaseUrl">Base URL for AI flow proxy endpoints (default: /api/wildwood-ai-flows)</param>
    /// <param name="title">Header title (default: "AI Flows")</param>
    /// <param name="runLabel">Run button label (default: "Run")</param>
    /// <param name="showFlowPicker">Whether to show the flow picker (default true)</param>
    /// <param name="showLiveProgress">Whether to show the active-node progress line (default true)</param>
    /// <param name="showDebugInfo">Whether to show the raw event debug log (default false)</param>
    /// <param name="showRunHistory">Whether to show the current thread's prior runs (default true)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? appId = null,
        string? flowId = null,
        string proxyBaseUrl = "/api/wildwood-ai-flows",
        string title = "AI Flows",
        string runLabel = "Run",
        bool showFlowPicker = true,
        bool showLiveProgress = true,
        bool showDebugInfo = false,
        bool showRunHistory = true)
    {
        var flows = new List<AIFlowModel>();
        try
        {
            flows = await _flowService.GetFlowsAsync(appId);
        }
        catch (Exception ex)
        {
            // User may not be authenticated yet — ai-flow.js reloads the list client-side.
            _logger.LogWarning(ex, "Failed to preload AI flows for app {AppId}", appId);
        }

        var model = new AIFlowViewModel
        {
            AppId = appId,
            FlowId = flowId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Title = title,
            RunLabel = runLabel,
            ShowFlowPicker = showFlowPicker,
            ShowLiveProgress = showLiveProgress,
            ShowDebugInfo = showDebugInfo,
            ShowRunHistory = showRunHistory,
            Flows = flows
        };

        return View(model);
    }
}
