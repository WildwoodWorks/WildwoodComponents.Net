using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

public class TwoFactorSettingsViewModel
{
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-twofactor";
    public TwoFactorUserStatus? Status { get; set; }
    public List<TwoFactorCredential> Credentials { get; set; } = new();
    public List<TrustedDevice> TrustedDevices { get; set; } = new();
    public RecoveryCodeInfo? RecoveryCodeInfo { get; set; }

    /// <summary>
    /// The app has no two-factor provider enabled, so its sign-in never asks for a code: the view
    /// says so and offers no new enrollment (existing methods can still be removed). Port of the
    /// Blazor TwoFactorSettingsComponent's app-not-enforcing notice. False when the app's
    /// configuration could not be read.
    /// </summary>
    public bool AppDoesNotEnforce { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}
