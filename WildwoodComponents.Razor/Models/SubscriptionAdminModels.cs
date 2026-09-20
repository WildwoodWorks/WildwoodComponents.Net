using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// View model for the SubscriptionAdminViewComponent (tabbed container)
/// </summary>
public class SubscriptionAdminViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-app-tiers";
    public string? CompanyId { get; set; }
    public string? UserId { get; set; }
    public SubscriptionDisplayMode DisplayMode { get; set; } = SubscriptionDisplayMode.All;
    public bool IsAdmin { get; set; }
    public string Currency { get; set; } = "USD";
    public bool ShowBillingToggle { get; set; } = true;
    public bool ShowStatusAboveTabs { get; set; }
    public bool IsCompanyMode { get; set; }
    public UserTierSubscriptionModel? Subscription { get; set; }
    public int OverrideCount { get; set; }
}

/// <summary>
/// View model for the SubscriptionStatusPanel
/// </summary>
public class SubscriptionStatusPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public UserTierSubscriptionModel? Subscription { get; set; }

    public string StatusBadgeClass
    {
        get
        {
            if (Subscription == null) return "bg-secondary";
            return Subscription.Status?.ToLower() switch
            {
                "active" => "bg-success",
                "trialing" => "bg-info",
                "pastdue" => "bg-warning",
                "cancelled" => "bg-danger",
                "expired" => "bg-secondary",
                "pendingupgrade" => "bg-info",
                "pendingdowngrade" => "bg-info",
                "pendingcancellation" => "bg-warning",
                _ => "bg-secondary"
            };
        }
    }

    /// <summary>Friendly display label for raw subscription statuses.</summary>
    public string StatusLabel
    {
        get
        {
            if (Subscription == null) return string.Empty;
            return Subscription.Status?.ToLower() switch
            {
                "pastdue" => "Past Due",
                "pendingupgrade" => "Upgrade Scheduled",
                "pendingdowngrade" => "Downgrade Scheduled",
                "pendingcancellation" => "Cancellation Scheduled",
                _ => Subscription.Status ?? string.Empty
            };
        }
    }

    public bool IsPendingCancellation =>
        string.Equals(Subscription?.Status, "PendingCancellation", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// End of access for a scheduled cancellation: the pending change date when the server
    /// set one, otherwise the subscription's end date.
    /// </summary>
    public DateTime? CancellationAccessEndDate => Subscription?.PendingChangeDate ?? Subscription?.EndDate;

    /// <summary>
    /// "Now" for the trial-end rule. Settable so a test can pin the clock; the view never sets it.
    /// </summary>
    public DateTime AsOf { get; set; } = DateTime.Now;

    /// <summary>
    /// Whether to show the trial end date. The server keeps a finished trial's end date on the row,
    /// so it shows only while that trial is still running — never on a plan already being paid for.
    /// The rule lives in <see cref="SubscriptionAccess.IsTrialRunning"/> so Blazor's panel applies
    /// the same one.
    /// </summary>
    public bool ShowTrialEnd =>
        SubscriptionAccess.IsTrialRunning(Subscription?.Status, Subscription?.TrialEndDate, AsOf);
}

/// <summary>
/// View model for the TierPlansPanel
/// </summary>
public class TierPlansPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public string Currency { get; set; } = "USD";
    public bool ShowBillingToggle { get; set; } = true;
    public bool IsAdmin { get; set; }
    public List<AppTierModel> Tiers { get; set; } = new();
    public UserTierSubscriptionModel? Subscription { get; set; }
    public bool HasMonthlyAndAnnual { get; set; }
}

/// <summary>
/// View model for the FeaturesPanel
/// </summary>
public class FeaturesPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public List<FeatureGroupViewModel> FeatureGroups { get; set; } = new();
    public List<AppFeatureOverrideModel> Overrides { get; set; } = new();
}

public class FeatureGroupViewModel
{
    public string Category { get; set; } = string.Empty;
    public List<FeatureItemViewModel> Features { get; set; } = new();
}

public class FeatureItemViewModel
{
    public string FeatureCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string IconClass { get; set; } = string.Empty;
    public bool IsEnabled { get; set; }
    public bool HasOverride { get; set; }
    public AppFeatureOverrideModel? Override { get; set; }
}

/// <summary>
/// View model for the OverridesPanel
/// </summary>
public class OverridesPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public List<AppFeatureOverrideModel> Overrides { get; set; } = new();
}

/// <summary>
/// View model for the AddOnsPanel.
/// </summary>
/// <remarks>
/// The rows are decided server-side through <see cref="AddOnRowRules"/> — the same rules the Blazor
/// panel applies — and travel to the browser as data attributes, so the JS acts on a row instead of
/// re-deriving who owns what.
/// </remarks>
public class AddOnsPanelViewModel
{
    private List<AddOnOwnedRowViewModel>? _ownedRows;
    private List<AddOnOfferViewModel>? _offers;

    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string Currency { get; set; } = "USD";
    public string? CurrentTierId { get; set; }

    /// <summary>
    /// Every pack subscription the server returned, cancelled and expired ones included. Filtered
    /// into <see cref="OwnedRows"/> by the access-granting rule.
    /// </summary>
    public List<UserAddOnSubscriptionModel> ActiveAddOns { get; set; } = new();

    public List<AppTierAddOnModel> AvailableAddOns { get; set; } = new();

    /// <summary>
    /// Where the shipped same-origin proxy lives. The pack lifecycle no longer needs a host-written
    /// route: <c>WildwoodRegistrationSubscriptionProxyController</c> serves subscribe, cancel and
    /// reactivate, all answering HTTP 200 with the structured result so a refusal keeps its words.
    /// </summary>
    public string RegSubProxyUrl { get; set; } = "/api/wildwood-regsub";

    /// <summary>Copy the host may replace; the shipped words otherwise.</summary>
    public AddOnsPanelLabels Labels { get; set; } = new();

    /// <summary>Whether this surface offers cancelling at all.</summary>
    public bool AllowCancel { get; set; } = true;

    /// <summary>Whether this surface offers taking a scheduled cancellation back.</summary>
    public bool AllowReactivate { get; set; } = true;

    /// <summary>
    /// Whether the panel offers "Add packs". The BUTTON is here; the picker it opens is the
    /// caller's — <c>&lt;vc:registration-subscription-manage /&gt;</c> renders one and
    /// <c>regsub-manage.js</c> answers the click, which is why this is off by default.
    /// </summary>
    public bool ShowAddPacks { get; set; }

    /// <summary>
    /// The packs the account still holds — the "Active Add-Ons" list. A Cancelled or Expired row
    /// grants nothing, so it is not listed and its pack goes back on offer.
    /// </summary>
    public List<AddOnOwnedRowViewModel> OwnedRows
    {
        get { return _ownedRows ??= BuildOwnedRows(); }
    }

    /// <summary>The packs still on offer: everything the account does not currently own.</summary>
    public List<AddOnOfferViewModel> Offers
    {
        get { return _offers ??= BuildOffers(); }
    }

    private List<AddOnOwnedRowViewModel> BuildOwnedRows()
    {
        var rows = new List<AddOnOwnedRowViewModel>();

        foreach (var subscription in AddOnRowRules.OwnedRows(ActiveAddOns))
        {
            rows.Add(new AddOnOwnedRowViewModel(
                subscription,
                AddOnRowRules.Describe(subscription, AllowCancel, AllowReactivate, Labels)));
        }

        return rows;
    }

    private List<AddOnOfferViewModel> BuildOffers()
    {
        var offers = new List<AddOnOfferViewModel>();

        foreach (var addOn in AddOnRowRules.AvailableRows(AvailableAddOns, ActiveAddOns))
        {
            var pricing = AddOnRowRules.DefaultPricing(addOn);
            offers.Add(new AddOnOfferViewModel(
                addOn,
                pricing,
                AddOnRowRules.IsBundledInTier(addOn, CurrentTierId),
                AddOnRowRules.FormatPrice(addOn, pricing, Currency),
                AddOnRowRules.BillingSuffix(pricing),
                AddOnRowRules.TrialLabel(addOn, pricing)));
        }

        return offers;
    }
}

/// <summary>
/// One pack the account holds, with everything its row decided about itself.
/// </summary>
public class AddOnOwnedRowViewModel
{
    public AddOnOwnedRowViewModel(UserAddOnSubscriptionModel subscription, AddOnRowDecision decision)
    {
        Subscription = subscription;
        Decision = decision;
    }

    public UserAddOnSubscriptionModel Subscription { get; }
    public AddOnRowDecision Decision { get; }

    /// <summary>The row's allowed actions, for the <c>data-ww-addon-actions</c> attribute.</summary>
    public string ActionsAttribute
    {
        get { return Decision.ActionsAttribute; }
    }
}

/// <summary>
/// One pack still on offer, priced in its own currency with the trial the processor will start.
/// </summary>
public class AddOnOfferViewModel
{
    public AddOnOfferViewModel(
        AppTierAddOnModel addOn,
        AppTierAddOnPricingModel? pricing,
        bool isBundled,
        string priceText,
        string billingSuffix,
        string trialText)
    {
        AddOn = addOn;
        Pricing = pricing;
        IsBundled = isBundled;
        PriceText = priceText;
        BillingSuffix = billingSuffix;
        TrialText = trialText;
    }

    public AppTierAddOnModel AddOn { get; }

    /// <summary>The option a one-click subscribe buys; null when the pack sells none.</summary>
    public AppTierAddOnPricingModel? Pricing { get; }

    public bool IsBundled { get; }

    /// <summary>The price in the pack's own currency — never a hard-coded symbol.</summary>
    public string PriceText { get; }

    public string BillingSuffix { get; }

    /// <summary>"14-day free trial", or empty when nothing starts one.</summary>
    public string TrialText { get; }

    /// <summary>A bundled pack is already included, so it is shown rather than sold.</summary>
    public bool CanSubscribe
    {
        get { return !IsBundled; }
    }
}

/// <summary>
/// View model for the UsageLimitsPanel
/// </summary>
public class UsageLimitsPanelViewModel
{
    public string ComponentId { get; set; } = string.Empty;
    public string AppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public List<AppTierLimitStatusModel> LimitStatuses { get; set; } = new();
}
