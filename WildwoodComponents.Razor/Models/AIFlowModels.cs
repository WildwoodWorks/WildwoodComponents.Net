using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the AIFlowViewComponent.
/// Razor Pages equivalent of the Blazor AIFlowSettings (WildwoodComponents.Shared).
/// The flow/run/event models themselves live in WildwoodComponents.Shared.Models (AIFlow,
/// AIFlowInputField, AIFlowRunSummary, ...).
/// </summary>
public class AIFlowViewModel
{
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>Optional app scope forwarded to the API as ?requestedAppId=.</summary>
    public string? AppId { get; set; }

    /// <summary>Fixed flow to run; when null the component shows a flow picker.</summary>
    public string? FlowId { get; set; }

    /// <summary>Base URL for the host app's AI flow proxy endpoints.</summary>
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-ai-flows";

    public string Title { get; set; } = "AI Flows";
    public string RunLabel { get; set; } = "Run";

    public bool ShowFlowPicker { get; set; } = true;
    public bool ShowLiveProgress { get; set; } = true;
    public bool ShowDebugInfo { get; set; }

    /// <summary>Shows the current thread's prior runs beneath the result.</summary>
    public bool ShowRunHistory { get; set; } = true;

    /// <summary>Published flows preloaded server-side for the initial picker render.</summary>
    public List<AIFlow> Flows { get; set; } = new();
}
