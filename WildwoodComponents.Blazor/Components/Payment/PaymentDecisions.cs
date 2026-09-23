using System.Globalization;

namespace WildwoodComponents.Blazor.Components.Payment;

/// <summary>
/// The decisions <see cref="PaymentComponent"/> makes before money moves, lifted out of the
/// component so they can be tested without a renderer.
/// </summary>
/// <remarks>
/// Ported from packages/wildwood-react/src/components/payment/PaymentComponent.tsx
/// (JS f8b095f, 3298f70, 1236503, d7eae5a). The component holds the state and the I/O; every rule
/// that decides whether a card is charged, saved or left alone lives here.
/// </remarks>
internal static class PaymentDecisions
{
    /// <summary>
    /// Identifies the Stripe intent a plan's payment belongs to. A retry after a declined card must
    /// confirm the SAME intent — initiating again creates a second subscription that bills
    /// separately or is abandoned — so the key covers everything that would make the server create
    /// a different one.
    /// </summary>
    public static string BuildIntentKey(string? providerId, string? pricingModelId, decimal amount, bool isSubscription)
    {
        return string.Join("|", new[]
        {
            providerId ?? string.Empty,
            pricingModelId ?? string.Empty,
            amount.ToString(CultureInfo.InvariantCulture),
            isSubscription ? "sub" : "once"
        });
    }

    /// <summary>
    /// True when a stored intent belongs to the payment about to be made and can be reused instead
    /// of calling InitiatePayment again.
    /// </summary>
    public static bool CanReuseIntent(string? storedKey, InitiatePaymentResponse? storedResponse, string currentKey)
    {
        if (storedResponse is null) return false;
        if (storedKey is not { Length: > 0 }) return false;
        return string.Equals(storedKey, currentKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Whether the initiate request tells the server the client can confirm a SetupIntent. Only a
    /// Stripe payment that is actually offering a trial asks for one: that is the payment where a
    /// card must be saved rather than charged.
    /// </summary>
    /// <remarks>
    /// React sends the flag for every Stripe payment; .NET narrows it to the trial case so a plain
    /// charge can never come back as a card-setup flow. Both reach the same behaviour, because the
    /// server only answers with a SetupIntent when the subscription starts on a trial.
    /// </remarks>
    public static bool ShouldRequestSetupIntent(bool isStripe, bool hasTrial) => isStripe && hasTrial;

    /// <summary>True when the server answered with a SetupIntent secret — a card to save, not charge.</summary>
    public static bool IsSetupIntentResponse(InitiatePaymentResponse? response)
    {
        return response is { } r
               && r.ClientSecret is { Length: > 0 }
               && string.Equals(r.ClientSecretType, PaymentClientSecretTypes.SetupIntent, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when a trial was offered but the server started a paid subscription instead (the
    /// account has already used its one trial for the app). Nothing is charged on that click: the
    /// component says so and waits for the user to agree, then confirms the SAME intent.
    /// </summary>
    public static bool ShouldAskBeforeCharging(bool hasTrial, bool isStripe, InitiatePaymentResponse? response)
    {
        if (!hasTrial || !isStripe) return false;
        if (response is not { } r) return false;
        if (r.ClientSecret is not { Length: > 0 }) return false;
        return !string.Equals(r.ClientSecretType, PaymentClientSecretTypes.SetupIntent, StringComparison.Ordinal);
    }

    /// <summary>
    /// True when the plan the form is collecting for has changed, so a "the trial isn't available"
    /// answer given about the previous plan no longer applies (JS 1236503).
    /// </summary>
    public static bool PlanChanged(
        string? previousPricingModelId, int? previousTrialDays, decimal previousAmount,
        string? pricingModelId, int? trialDays, decimal amount)
    {
        return !string.Equals(previousPricingModelId, pricingModelId, StringComparison.Ordinal)
               || previousTrialDays != trialDays
               || previousAmount != amount;
    }

    /// <summary>
    /// The id the SERVER recorded for this payment — a subscription's first invoice, or the
    /// PaymentIntent itself for a one-time payment — which is what the confirm endpoint can verify
    /// with the processor. Null when the server named neither, in which case nothing is confirmed:
    /// confirming an empty id asks the server to verify a payment it cannot find.
    /// </summary>
    public static string? ServerRecordedId(InitiatePaymentResponse? response)
    {
        if (response is not { } r) return null;
        if (r.PaymentIntentId is { Length: > 0 } intentId) return intentId;
        if (r.SubscriptionId is { Length: > 0 } subscriptionId) return subscriptionId;
        return null;
    }

    /// <summary>
    /// The id to confirm a Stripe card payment with: the server's own id when it recorded one,
    /// otherwise the PaymentIntent the browser just confirmed.
    /// </summary>
    public static string? ConfirmationId(InitiatePaymentResponse? response, string? clientPaymentIntentId)
    {
        if (ServerRecordedId(response) is { Length: > 0 } serverId) return serverId;
        return clientPaymentIntentId is { Length: > 0 } ? clientPaymentIntentId : null;
    }

    /// <summary>The message shown when a payment cannot be confirmed because no id names it.</summary>
    public const string MissingPaymentIdMessage =
        "The payment could not be confirmed because the server did not return a payment id. Please try again.";

    /// <summary>The message shown when a required billing address is incomplete.</summary>
    public const string IncompleteBillingAddressMessage = "Please complete your billing address.";

    /// <summary>
    /// Validates and builds the billing address to send with the payment. An address the app
    /// requires has to be complete BEFORE anything is charged — an incomplete one fails at the
    /// provider, after the intent already exists.
    /// </summary>
    /// <param name="required">Whether the app's payment configuration asks for an address at all.</param>
    /// <param name="firstName">The cardholder's first name, as typed.</param>
    /// <param name="lastName">The cardholder's last name, as typed.</param>
    /// <param name="street">The street address, as typed.</param>
    /// <param name="city">The city, as typed.</param>
    /// <param name="state">The state or region, as typed.</param>
    /// <param name="zipCode">The postal code, as typed.</param>
    /// <param name="country">The country code the form carries (no picker yet — "US").</param>
    /// <param name="address">
    /// When the method returns true and <paramref name="required"/> was set, the trimmed address to
    /// send; null when no address is required, so the request omits the property entirely.
    /// </param>
    /// <returns>False when an address is required and incomplete.</returns>
    public static bool TryBuildBillingAddress(
        bool required,
        string? firstName, string? lastName, string? street,
        string? city, string? state, string? zipCode, string? country,
        out BillingAddress? address)
    {
        address = null;
        if (!required) return true;

        var fields = new[] { firstName, lastName, street, city, state, zipCode };
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field)) return false;
        }

        address = new BillingAddress
        {
            FirstName = firstName!.Trim(),
            LastName = lastName!.Trim(),
            Street = street!.Trim(),
            City = city!.Trim(),
            State = state!.Trim(),
            ZipCode = zipCode!.Trim(),
            // No country picker yet — the form collects a US address, and the value still travels
            // with it so the server and the provider get a complete address.
            Country = (country ?? string.Empty).Trim()
        };
        return true;
    }

    /// <summary>
    /// True when a URL is safe to navigate the browser to: an absolute http(s) address and nothing
    /// else. A provider-supplied redirect used to be run through <c>eval</c> with the URL
    /// interpolated into a script string, which let the server's answer write JavaScript.
    /// </summary>
    public static bool IsSafeRedirectUrl(string? url)
    {
        if (url is not { Length: > 0 }) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        return string.Equals(parsed.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
               || string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
    }
}
