using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.Authentication;

/// <summary>
/// ViewComponent that renders a complete authentication UI (login, register, forgot password, 2FA).
/// Client-side JavaScript handles state transitions and AJAX calls to the consuming app's proxy endpoints.
/// Razor Pages equivalent of WildwoodComponents AuthenticationComponent.
/// </summary>
public class AuthenticationViewComponent : ViewComponent
{
    private readonly IWildwoodAuthService _authService;
    private readonly ILogger<AuthenticationViewComponent> _logger;

    public AuthenticationViewComponent(IWildwoodAuthService authService, ILogger<AuthenticationViewComponent> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the authentication component
    /// </summary>
    /// <param name="returnUrl">URL to redirect to after successful login</param>
    /// <param name="proxyBaseUrl">Base URL for the auth proxy endpoints (default: /api/wildwood-auth)</param>
    /// <param name="allowRegistration">Whether to show the registration form</param>
    /// <param name="title">Card header title</param>
    /// <param name="subtitle">Card header subtitle</param>
    /// <param name="externalLoginPath">Path for external login redirect (default: /Account/ExternalLogin)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? returnUrl = null,
        string proxyBaseUrl = "/api/wildwood-auth",
        bool allowRegistration = true,
        string title = "Welcome",
        string? subtitle = null,
        string externalLoginPath = "/Account/ExternalLogin")
    {
        AuthConfigResponse? config = null;
        try
        {
            config = await _authService.GetAuthConfigAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load auth configuration");
        }

        var model = new AuthenticationViewModel
        {
            ReturnUrl = returnUrl ?? "/",
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            AllowRegistration = allowRegistration && (config?.AllowRegistration ?? true),
            ExternalProviders = config?.ExternalProviders ?? new List<string>(),
            EnableTwoFactor = config?.EnableTwoFactor ?? false,
            Title = title,
            Subtitle = subtitle ?? "Sign in to your account",
            ExternalLoginPath = externalLoginPath
        };

        return View(model);
    }
}
