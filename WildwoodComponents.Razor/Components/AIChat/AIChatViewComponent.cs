using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.AIChat;

/// <summary>
/// ViewComponent that renders a complete AI chat interface with message history,
/// session management, and optional text-to-speech/speech-to-text.
/// Razor Pages equivalent of WildwoodComponents.Blazor AIChatComponent.
/// </summary>
public class AIChatViewComponent : ViewComponent
{
    private readonly IWildwoodAIChatService _aiChatService;
    private readonly ILogger<AIChatViewComponent> _logger;

    public AIChatViewComponent(IWildwoodAIChatService aiChatService, ILogger<AIChatViewComponent> logger)
    {
        _aiChatService = aiChatService;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(
        string? configurationId = null,
        string proxyBaseUrl = "/api/wildwood-ai",
        string? title = null,
        bool showSessionSidebar = true,
        bool showConfigurationSelector = true,
        bool enableTTS = true,
        bool enableSTT = true,
        bool enableFileUpload = false,
        string? placeholderText = null)
    {
        var configurations = new List<AIConfigurationDto>();
        var sessions = new List<AISessionSummaryDto>();

        try
        {
            configurations = await _aiChatService.GetConfigurationsAsync();

            if (!string.IsNullOrEmpty(configurationId) || configurations.Count > 0)
            {
                var activeConfigId = configurationId ?? configurations.FirstOrDefault()?.Id;
                if (!string.IsNullOrEmpty(activeConfigId))
                {
                    sessions = await _aiChatService.GetSessionsAsync(activeConfigId);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI chat configurations or sessions");
        }

        var model = new AIChatViewModel
        {
            ConfigurationId = configurationId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Title = title,
            ShowSessionSidebar = showSessionSidebar,
            ShowConfigurationSelector = showConfigurationSelector,
            EnableTTS = enableTTS,
            EnableSTT = enableSTT,
            EnableFileUpload = enableFileUpload,
            PlaceholderText = placeholderText,
            Configurations = configurations,
            Sessions = sessions
        };

        return View(model);
    }
}
