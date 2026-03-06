using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services.Payment;

/// <summary>
/// Script provider for Apple Pay payment integration.
/// Provides embedded JavaScript for Apple Pay sessions.
/// </summary>
public class ApplePayScriptProvider : PaymentScriptProviderBase
{
    /// <inheritdoc />
    public override PaymentProviderType ProviderType => PaymentProviderType.ApplePay;

    /// <inheritdoc />
    protected override string EmbeddedResourceName => "WildwoodComponents.Blazor.Scripts.ApplePayPaymentScript.js";

    /// <inheritdoc />
    public override IReadOnlyList<string> GetExternalScriptUrls()
    {
        // Apple Pay uses the native ApplePaySession API - no external script needed
        return Array.Empty<string>();
    }
}
