using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services.Payment;

/// <summary>
/// Script provider for Google Pay payment integration.
/// Provides embedded JavaScript for Google Pay API.
/// </summary>
public class GooglePayScriptProvider : PaymentScriptProviderBase
{
    /// <inheritdoc />
    public override PaymentProviderType ProviderType => PaymentProviderType.GooglePay;

    /// <inheritdoc />
    protected override string EmbeddedResourceName => "WildwoodComponents.Blazor.Scripts.GooglePayPaymentScript.js";

    /// <inheritdoc />
    public override IReadOnlyList<string> GetExternalScriptUrls()
    {
        // Google Pay API is loaded dynamically in the script
        // No static external URL needed here
        return Array.Empty<string>();
    }
}
