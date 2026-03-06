using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services.Payment;

/// <summary>
/// Interface for payment provider script providers.
/// Each implementation provides embedded JavaScript for a specific payment provider.
/// </summary>
public interface IPaymentScriptProvider
{
    /// <summary>
    /// The payment provider type this script provider handles.
    /// </summary>
    PaymentProviderType ProviderType { get; }

    /// <summary>
    /// Gets the unique identifier for the script element in the DOM.
    /// </summary>
    string ScriptElementId { get; }

    /// <summary>
    /// Gets the embedded JavaScript content for this provider.
    /// </summary>
    /// <returns>The JavaScript code as a string.</returns>
    string GetScriptContent();

    /// <summary>
    /// Gets any external script URLs that need to be loaded before the provider script.
    /// For example, Stripe.js CDN URL.
    /// </summary>
    /// <returns>List of external script URLs, or empty if none required.</returns>
    IReadOnlyList<string> GetExternalScriptUrls();
}
