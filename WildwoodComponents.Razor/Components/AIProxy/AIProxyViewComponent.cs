using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.AIProxy;

/// <summary>
/// ViewComponent that renders a general-purpose AI proxy UI (prompt input, file upload, response display).
/// NOT a "chat" component — single request/response interaction.
/// Client-side JavaScript handles AJAX calls to the consuming app's proxy endpoints.
/// </summary>
public class AIProxyViewComponent : ViewComponent
{
    private readonly IWildwoodAIProxyService _aiService;

    public AIProxyViewComponent(IWildwoodAIProxyService aiService)
    {
        _aiService = aiService;
    }

    /// <summary>
    /// Renders the AI proxy component
    /// </summary>
    /// <param name="configurationId">Specific AI configuration ID to use</param>
    /// <param name="configurationName">Named AI configuration (resolved to ID at runtime)</param>
    /// <param name="placeholder">Placeholder text for the prompt input</param>
    /// <param name="allowFileUpload">Whether to show the file upload area</param>
    /// <param name="proxyBaseUrl">Base URL for the AI proxy endpoints</param>
    /// <param name="onCompleteCallback">Optional JS function name to call with the response</param>
    /// <param name="title">Component title</param>
    /// <param name="submitLabel">Text for the submit button</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? configurationId = null,
        string? configurationName = null,
        string placeholder = "Describe what you need...",
        bool allowFileUpload = false,
        string proxyBaseUrl = "/api/ai-proxy",
        string? onCompleteCallback = null,
        string title = "AI Assistant",
        string submitLabel = "Generate")
    {
        // If only name provided, try to resolve to ID
        if (string.IsNullOrEmpty(configurationId) && !string.IsNullOrEmpty(configurationName))
        {
            var configs = await _aiService.GetConfigurationsAsync();
            var match = configs.FirstOrDefault(c => c.IsActive &&
                c.Name.Equals(configurationName, StringComparison.OrdinalIgnoreCase));
            configurationId = match?.Id;
        }

        var model = new AIProxyViewModel
        {
            ConfigurationId = configurationId,
            ConfigurationName = configurationName,
            Placeholder = placeholder,
            AllowFileUpload = allowFileUpload,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            OnCompleteCallback = onCompleteCallback,
            Title = title,
            SubmitLabel = submitLabel
        };

        return View(model);
    }
}

public class AIProxyViewModel
{
    public string? ConfigurationId { get; set; }
    public string? ConfigurationName { get; set; }
    public string Placeholder { get; set; } = "Describe what you need...";
    public bool AllowFileUpload { get; set; }
    public string ProxyBaseUrl { get; set; } = "/api/ai-proxy";
    public string? OnCompleteCallback { get; set; }
    public string Title { get; set; } = "AI Assistant";
    public string SubmitLabel { get; set; } = "Generate";
}
