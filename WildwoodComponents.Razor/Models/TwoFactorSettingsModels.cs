using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

public class TwoFactorSettingsViewModel
{
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-twofactor";
    public TwoFactorUserStatus? Status { get; set; }
    public List<TwoFactorCredential> Credentials { get; set; } = new();
    public List<TrustedDevice> TrustedDevices { get; set; } = new();
    public RecoveryCodeInfo? RecoveryCodeInfo { get; set; }
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}
