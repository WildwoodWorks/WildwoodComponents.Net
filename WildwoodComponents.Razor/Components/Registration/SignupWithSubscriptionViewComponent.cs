using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Registration;

/// <summary>
/// ViewComponent for combined signup + subscription flow.
/// Multi-step: Plan Selection -> Registration -> Payment (if paid) -> Success.
/// Client-side JavaScript handles step transitions, form validation, and AJAX calls.
/// Razor Pages equivalent of WildwoodComponents.Blazor SignupWithSubscriptionComponent.
/// </summary>
/// <remarks>
/// <b>Superseded by <c>&lt;vc:registration-subscription-signup /&gt;</c></b>
/// (<see cref="WildwoodComponents.Razor.Components.RegistrationSubscription.RegistrationSubscriptionSignupViewComponent"/>),
/// which is PAY-FIRST and works: this component's paid step is a hand-rolled card form whose
/// "Complete Payment" button ships disabled and has no handler, so a paid plan dead-ends unless
/// the host mounts a payment component itself. It keeps working for the flows that do not touch
/// it, and is deprecated rather than removed so no host breaks at compile time.
/// </remarks>
[Obsolete("Use <vc:registration-subscription-signup /> (RegistrationSubscriptionSignupViewComponent): "
    + "pay-first, no raw card fields, and the plan's payment actually completes. "
    + "This component keeps working but its paid step does not.", false)]
public class SignupWithSubscriptionViewComponent : ViewComponent
{
    private readonly IWildwoodAuthService _authService;
    private readonly IWildwoodAppTierService _appTierService;
    private readonly ILogger<SignupWithSubscriptionViewComponent> _logger;

    public SignupWithSubscriptionViewComponent(
        IWildwoodAuthService authService,
        IWildwoodAppTierService appTierService,
        ILogger<SignupWithSubscriptionViewComponent> logger)
    {
        _authService = authService;
        _appTierService = appTierService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the signup with subscription component
    /// </summary>
    /// <param name="appId">Required. The application ID.</param>
    /// <param name="authProxyBaseUrl">Base URL for auth proxy endpoints (default: /api/wildwood-auth)</param>
    /// <param name="subscriptionProxyBaseUrl">Base URL for subscription proxy endpoints (default: /api/wildwood-subscription)</param>
    /// <param name="paymentProxyBaseUrl">Base URL for payment proxy endpoints (default: /api/wildwood-payment)</param>
    /// <param name="returnUrl">URL to redirect after successful signup + subscription</param>
    /// <param name="title">Header title</param>
    /// <param name="subtitle">Header subtitle</param>
    /// <param name="allowRegistration">Whether to allow new user registration (default: true)</param>
    /// <param name="currency">Currency code (default: USD)</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string appId,
        string authProxyBaseUrl = "/api/wildwood-auth",
        string subscriptionProxyBaseUrl = "/api/wildwood-subscription",
        string paymentProxyBaseUrl = "/api/wildwood-payment",
        string? returnUrl = null,
        string? title = null,
        string? subtitle = null,
        bool allowRegistration = true,
        string currency = "USD")
    {
        AuthConfigResponse? config = null;
        try
        {
            config = await _authService.GetAuthConfigAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load auth config for signup");
        }

        var tiers = new List<AppTierModel>();
        try
        {
            tiers = await _appTierService.GetAvailableTiersAsync(appId);
            tiers = tiers.OrderBy(t => t.DisplayOrder).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load available tiers for app {AppId}", appId);
        }

        var model = new SignupWithSubscriptionViewModel
        {
            AppId = appId,
            AuthProxyBaseUrl = authProxyBaseUrl.TrimEnd('/'),
            SubscriptionProxyBaseUrl = subscriptionProxyBaseUrl.TrimEnd('/'),
            PaymentProxyBaseUrl = paymentProxyBaseUrl.TrimEnd('/'),
            ReturnUrl = returnUrl,
            Title = title,
            Subtitle = subtitle,
            AllowRegistration = allowRegistration && (config?.AllowRegistration ?? true),
            Currency = currency,
            Tiers = tiers,
            ExternalProviders = config?.ExternalProviders ?? new()
        };

        return View(model);
    }
}
