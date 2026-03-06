using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services.Payment;

/// <summary>
/// Script provider for Stripe payment integration.
/// Provides embedded JavaScript for Stripe Elements.
/// </summary>
public class StripeScriptProvider : PaymentScriptProviderBase
{
    /// <inheritdoc />
    public override PaymentProviderType ProviderType => PaymentProviderType.Stripe;

    /// <inheritdoc />
    protected override string EmbeddedResourceName => "WildwoodComponents.Blazor.Scripts.StripePaymentScript.js";

    /// <inheritdoc />
    public override IReadOnlyList<string> GetExternalScriptUrls()
    {
        // Stripe.js must be loaded from Stripe's CDN
        return new[] { "https://js.stripe.com/v3/" };
    }
}
