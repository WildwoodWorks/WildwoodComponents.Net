using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Models;

public class DisclaimerViewModel
{
    public string AppId { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-disclaimer";
    public string Mode { get; set; } = "login";
    public bool ShowCancelButton { get; set; } = true;
    public List<PendingDisclaimerModel> Disclaimers { get; set; } = new();
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}
