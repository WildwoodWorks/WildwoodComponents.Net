using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services.Payment;

/// <summary>
/// Script provider for PayPal payment integration.
/// Provides embedded JavaScript for PayPal Buttons.
/// </summary>
public class PayPalScriptProvider : PaymentScriptProviderBase
{
    /// <inheritdoc />
    public override PaymentProviderType ProviderType => PaymentProviderType.PayPal;

    /// <inheritdoc />
    protected override string EmbeddedResourceName => "WildwoodComponents.Blazor.Scripts.PayPalPaymentScript.js";

    /// <inheritdoc />
    public override IReadOnlyList<string> GetExternalScriptUrls()
    {
        // PayPal SDK is loaded dynamically in the script with client ID and currency
        // No static external URL needed here
        return Array.Empty<string>();
    }
}
