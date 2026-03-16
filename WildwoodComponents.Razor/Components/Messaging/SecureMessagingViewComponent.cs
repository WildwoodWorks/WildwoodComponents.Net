using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.Messaging;

/// <summary>
/// ViewComponent that renders a secure messaging interface with thread list and message area.
/// Client-side JavaScript handles real-time messaging, reactions, and AJAX calls.
/// Razor Pages equivalent of WildwoodComponents.Blazor SecureMessagingComponent.
/// </summary>
public class SecureMessagingViewComponent : ViewComponent
{
    private readonly IWildwoodMessagingService _messagingService;
    private readonly ILogger<SecureMessagingViewComponent> _logger;

    public SecureMessagingViewComponent(IWildwoodMessagingService messagingService, ILogger<SecureMessagingViewComponent> logger)
    {
        _messagingService = messagingService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the secure messaging component
    /// </summary>
    /// <param name="companyAppId">Required. The company app ID to load threads for.</param>
    /// <param name="proxyBaseUrl">Base URL for the messaging proxy endpoints (default: /api/wildwood-messaging)</param>
    /// <param name="title">Header title displayed above the messaging interface</param>
    /// <param name="showUserSearch">Whether to show the user search for starting new conversations</param>
    /// <param name="showThreadList">Whether to show the thread list sidebar</param>
    /// <param name="enableReactions">Whether to enable message reactions</param>
    /// <param name="enableAttachments">Whether to enable file attachments</param>
    /// <param name="enableTypingIndicators">Whether to show typing indicators</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string companyAppId,
        string proxyBaseUrl = "/api/wildwood-messaging",
        string? title = null,
        bool showUserSearch = true,
        bool showThreadList = true,
        bool enableReactions = true,
        bool enableAttachments = true,
        bool enableTypingIndicators = true)
    {
        var threads = await _messagingService.GetThreadsAsync(companyAppId);
        List<CompanyAppUserDto> users = new();
        try
        {
            users = await _messagingService.GetUsersAsync(companyAppId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load users for messaging in app {CompanyAppId}", companyAppId);
        }

        var model = new SecureMessagingViewModel
        {
            CompanyAppId = companyAppId,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Title = title,
            ShowUserSearch = showUserSearch,
            ShowThreadList = showThreadList,
            EnableReactions = enableReactions,
            EnableAttachments = enableAttachments,
            EnableTypingIndicators = enableTypingIndicators,
            Threads = threads,
            Users = users
        };
        return View(model);
    }
}
