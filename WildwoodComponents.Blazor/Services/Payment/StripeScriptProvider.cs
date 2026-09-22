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

    /// <summary>
    /// The script element's id, which is also this loader's only cache key: the injector skips a
    /// script whose id is already in the document, so a page still holding the previous
    /// single-instance script would never be given the instance-keyed one. The suffix is bumped
    /// whenever the script's contract changes — v2 is the instance-keyed rewrite.
    /// </summary>
    public override string ScriptElementId => "ww-payment-stripe-v2";

    /// <inheritdoc />
    protected override string EmbeddedResourceName => "WildwoodComponents.Blazor.Scripts.StripePaymentScript.js";

    /// <inheritdoc />
    public override IReadOnlyList<string> GetExternalScriptUrls()
    {
        // Stripe.js must be loaded from Stripe's CDN
        return new[] { "https://js.stripe.com/v3/" };
    }
}
