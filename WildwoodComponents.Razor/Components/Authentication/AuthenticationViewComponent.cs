using Microsoft.AspNetCore.Mvc;
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

    public AuthenticationViewComponent(IWildwoodAuthService authService)
    {
        _authService = authService;
    }

    /// <summary>
    /// Renders the authentication component
    /// </summary>
    /// <param name="returnUrl">URL to redirect to after successful login</param>
    /// <param name="proxyBaseUrl">Base URL for the auth proxy endpoints (default: /api/wildwood-auth)</param>
    /// <param name="allowRegistration">Whether to show the registration form</param>
    /// <param name="title">Card header title</param>
    /// <param name="subtitle">Card header subtitle</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? returnUrl = null,
        string proxyBaseUrl = "/api/wildwood-auth",
        bool allowRegistration = true,
        string title = "Welcome",
        string subtitle = "Sign in to Form Forge")
    {
        var config = await _authService.GetAuthConfigAsync();

        var model = new AuthenticationViewModel
        {
            ReturnUrl = returnUrl ?? "/",
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            AllowRegistration = allowRegistration && (config?.AllowRegistration ?? true),
            ExternalProviders = config?.ExternalProviders ?? new List<string>(),
            EnableTwoFactor = config?.EnableTwoFactor ?? false,
            Title = title,
            Subtitle = subtitle
        };

        return View(model);
    }
}

public class AuthenticationViewModel
{
    public string ReturnUrl { get; set; } = "/";
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-auth";
    public bool AllowRegistration { get; set; } = true;
    public List<string> ExternalProviders { get; set; } = new();
    public bool EnableTwoFactor { get; set; }
    public string Title { get; set; } = "Welcome";
    public string Subtitle { get; set; } = "Sign in to Form Forge";
}
