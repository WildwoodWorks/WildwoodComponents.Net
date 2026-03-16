using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Components.Security;

/// <summary>
/// ViewComponent that renders a complete two-factor authentication settings UI.
/// Includes authenticator setup, email 2FA, credential management, trusted devices,
/// and recovery codes. Client-side JavaScript handles AJAX calls to the proxy endpoints.
/// Razor Pages equivalent of WildwoodComponents.Blazor TwoFactorSettingsComponent.
/// </summary>
public class TwoFactorSettingsViewComponent : ViewComponent
{
    private readonly IWildwoodTwoFactorSettingsService _twoFactorService;
    private readonly ILogger<TwoFactorSettingsViewComponent> _logger;

    public TwoFactorSettingsViewComponent(IWildwoodTwoFactorSettingsService twoFactorService, ILogger<TwoFactorSettingsViewComponent> logger)
    {
        _twoFactorService = twoFactorService;
        _logger = logger;
    }

    /// <summary>
    /// Renders the two-factor settings component
    /// </summary>
    /// <param name="proxyBaseUrl">Base URL for the 2FA proxy endpoints (default: /api/wildwood-twofactor)</param>
    public async Task<IViewComponentResult> InvokeAsync(string proxyBaseUrl = "/api/wildwood-twofactor")
    {
        var status = await _twoFactorService.GetStatusAsync();
        var credentials = await _twoFactorService.GetCredentialsAsync();
        var trustedDevices = await _twoFactorService.GetTrustedDevicesAsync();
        RecoveryCodeInfo? recoveryInfo = null;
        try
        {
            recoveryInfo = await _twoFactorService.GetRecoveryCodeInfoAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load recovery code info");
        }

        var model = new TwoFactorSettingsViewModel
        {
            ProxyBaseUrl = proxyBaseUrl.TrimEnd('/'),
            Status = status,
            Credentials = credentials,
            TrustedDevices = trustedDevices,
            RecoveryCodeInfo = recoveryInfo
        };
        return View(model);
    }
}
