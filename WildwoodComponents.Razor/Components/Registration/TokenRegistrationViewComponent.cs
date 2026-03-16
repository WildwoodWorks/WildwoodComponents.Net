using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Components.Registration;

/// <summary>
/// ViewComponent that renders a multi-step registration form with optional token validation,
/// registration form, disclaimer acceptance, and payment integration.
/// Client-side JavaScript handles state transitions, AJAX calls, and payment embedding.
/// Razor Pages equivalent of WildwoodComponents TokenRegistrationComponent.
/// </summary>
public class TokenRegistrationViewComponent : ViewComponent
{
    private readonly IWildwoodRegistrationService _registrationService;
    private readonly ILogger<TokenRegistrationViewComponent> _logger;

    public TokenRegistrationViewComponent(IWildwoodRegistrationService registrationService, ILogger<TokenRegistrationViewComponent> logger)
    {
        _registrationService = registrationService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the token registration component
    /// </summary>
    /// <param name="appId">Application ID for registration</param>
    /// <param name="token">Pre-supplied registration token (auto-validated on load)</param>
    /// <param name="allowTokenRegistration">Whether token-based registration is allowed (default true)</param>
    /// <param name="allowOpenRegistration">Whether open registration without token is allowed (default false)</param>
    /// <param name="defaultPricingModelId">Default pricing model ID for open registration (null = free)</param>
    /// <param name="autoLogin">Whether to auto-login after successful registration (default true)</param>
    /// <param name="redirectUrl">URL to redirect to after successful auto-login</param>
    /// <param name="proxyBaseUrl">Base URL for registration proxy endpoints</param>
    /// <param name="paymentProxyBaseUrl">Base URL for payment proxy endpoints</param>
    public async Task<IViewComponentResult> InvokeAsync(
        string? appId = null,
        string? token = null,
        bool allowTokenRegistration = true,
        bool allowOpenRegistration = false,
        string? defaultPricingModelId = null,
        bool autoLogin = true,
        string? redirectUrl = null,
        string proxyBaseUrl = "/api/wildwood-registration",
        string? paymentProxyBaseUrl = null)
    {
        var model = new TokenRegistrationViewModel
        {
            AppId = appId,
            Token = token,
            AllowTokenRegistration = allowTokenRegistration,
            AllowOpenRegistration = allowOpenRegistration,
            DefaultPricingModelId = defaultPricingModelId,
            AutoLogin = autoLogin,
            RedirectUrl = redirectUrl,
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            PaymentProxyBaseUrl = paymentProxyBaseUrl
        };

        // If a default pricing model is set for open registration, pre-load pricing info
        if (allowOpenRegistration && !string.IsNullOrEmpty(defaultPricingModelId))
        {
            try
            {
                var pricing = await _registrationService.GetPricingModelAsync(defaultPricingModelId);
                if (pricing != null)
                {
                    ViewData["DefaultPricing"] = new PricingDetails
                    {
                        PlanName = pricing.Name,
                        PlanDescription = pricing.Description,
                        PriceAmount = pricing.Price,
                        Currency = pricing.Currency ?? "USD",
                        IsSubscription = !string.Equals(pricing.BillingFrequency, "OneTime", StringComparison.OrdinalIgnoreCase),
                        BillingFrequency = pricing.BillingFrequency
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load pricing model {PricingModelId}", defaultPricingModelId);
            }
        }

        return View(model);
    }
}
