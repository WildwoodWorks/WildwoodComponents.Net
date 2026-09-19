using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the PaymentViewComponent.
/// Uses shared models from WildwoodComponents.Shared for DTOs.
/// </summary>
/// <remarks>
/// Every piece of money copy is computed here rather than in the view or in
/// <c>wwwroot/js/payment.js</c>: the amounts are formatted once, server-side, in the app's culture,
/// and the script only ever swaps between strings this model produced.
/// </remarks>
public class PaymentViewModel
{
    // Required
    public string AppId { get; set; } = string.Empty;
    public decimal Amount { get; set; }

    // Optional
    public string Currency { get; set; } = "USD";
    public string? Description { get; set; }
    public string? CustomerId { get; set; }
    public string? CustomerEmail { get; set; }
    public string? OrderId { get; set; }
    public string? SubscriptionId { get; set; }
    public string? PricingModelId { get; set; }
    public bool IsSubscription { get; set; }

    /// <summary>
    /// The subscription starts with this many free-trial days. The button offers the trial instead
    /// of a charge, and with Stripe the card is saved (confirmed as a SetupIntent) rather than
    /// charged today, so the processor has something to bill when the trial ends.
    /// </summary>
    public int? TrialDays { get; set; }

    public bool ShowAmount { get; set; } = true;
    public bool RequireBillingAddress { get; set; }
    public string? ReturnUrl { get; set; }
    public string? CancelUrl { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }

    // Pre-loaded providers (skip API discovery)
    public List<PaymentProviderDto>? PreloadedProviders { get; set; }
    public string? PreselectedProviderId { get; set; }

    // Proxy base URL for JS AJAX calls
    public string ProxyBaseUrl { get; set; } = string.Empty;

    // Component instance ID for DOM scoping
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    /// <summary>True while the form is offering a free trial rather than a charge.</summary>
    public bool HasTrial => (TrialDays ?? 0) > 0;

    /// <summary>"14-day free trial", or empty when there is no trial.</summary>
    public string TrialLabel => CatalogHelpers.TrialLabel(TrialDays);

    /// <summary>The amount, formatted once here so no script has to format money.</summary>
    public string AmountDisplay => FormatHelpers.FormatAmount(Amount, Currency);

    /// <summary>
    /// What the submit button says when the form loads: the trial offer when there is one,
    /// otherwise the charge.
    /// </summary>
    public string PayButtonLabel => HasTrial ? $"Start {TrialLabel}" : ChargeButtonLabel;

    /// <summary>
    /// What the submit button says once a charge is what is actually happening. The script swaps to
    /// this when the server answers that the offered trial is not available on the account.
    /// </summary>
    public string ChargeButtonLabel => $"Pay {AmountDisplay}";

    /// <summary>The note under a trial's submit button: nothing is taken today.</summary>
    public string TrialChargeNote =>
        $"You won't be charged today. {AmountDisplay} is due when the trial ends unless you cancel before then.";

    /// <summary>
    /// Shown when a trial was offered but the server started a paid subscription instead — the
    /// account has already used its one trial for the app. Nothing is charged on that click.
    /// </summary>
    public string TrialUnavailableNotice =>
        $"The free trial isn't available on your account, so {AmountDisplay} will be charged today. Select Pay to continue.";
}
