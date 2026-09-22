using System.Text.Json;
using System.Text.Json.Serialization;
using WildwoodComponents.Razor.Components.RegistrationSubscription;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Models;

// The Razor manage surface's types. Named after their React and Blazor counterparts so the same
// thing is called the same thing on every stack; declared here rather than shared with Blazor
// because the Razor package does not reference the Blazor package.

/// <summary>How the manage view arranges its sections.</summary>
public enum ManageLayout
{
    /// <summary>One tab bar, one section on screen. React's default.</summary>
    Tabs = 0,

    /// <summary>Every section down the page, each under its own heading.</summary>
    Stacked = 1
}

/// <summary>One panel of the manage view. The names are React's <c>ManageSection</c> union.</summary>
public enum ManageSection
{
    Subscription = 0,
    Plans = 1,
    Features = 2,
    AddOns = 3,
    Usage = 4,
    Overrides = 5
}

/// <summary>
/// One section of the manage view, for the <c>_RegSubManageSection</c> partial.
/// </summary>
/// <remarks>
/// The two layouts render the SAME panels with the same parameters and differ only in what wraps
/// them, so the panel invocations live in one partial rather than being written out twice. A
/// two-property class rather than a tuple, because a partial's model has to be a nameable type.
/// </remarks>
public class RegSubManageSectionViewModel
{
    public RegSubManageSectionViewModel(RegistrationSubscriptionManageViewModel manage, ManageSection section)
    {
        Manage = manage;
        Section = section;
    }

    public RegistrationSubscriptionManageViewModel Manage { get; }

    public ManageSection Section { get; }
}

/// <summary>
/// The copy <c>regsub-manage.js</c> and the shared drivers paint with, as one JSON attribute.
/// </summary>
/// <remarks>
/// Every visible string the browser writes comes from here, so there is no English in the scripts
/// and a host's label overrides reach the browser. The keys are the ones the drivers read.
/// </remarks>
public class ManageScriptLabels
{
    // ----- The plan change ------------------------------------------------------------------

    [JsonPropertyName("authenticatingChange")] public string AuthenticatingChange { get; set; } = string.Empty;
    [JsonPropertyName("applyingChange")] public string ApplyingChange { get; set; } = string.Empty;
    [JsonPropertyName("planChangeFailed")] public string PlanChangeFailed { get; set; } = string.Empty;
    [JsonPropertyName("planChangeExpired")] public string PlanChangeExpired { get; set; } = string.Empty;
    [JsonPropertyName("planChangePaymentFailed")] public string PlanChangePaymentFailed { get; set; } = string.Empty;
    [JsonPropertyName("planChangeSuperseded")] public string PlanChangeSuperseded { get; set; } = string.Empty;
    [JsonPropertyName("planChangeNotFound")] public string PlanChangeNotFound { get; set; } = string.Empty;
    [JsonPropertyName("planChangeInProgress")] public string PlanChangeInProgress { get; set; } = string.Empty;
    [JsonPropertyName("paymentUnconfirmed")] public string PaymentUnconfirmed { get; set; } = string.Empty;

    /// <summary>"Upgrade to {tier}" — the confirmation modal's heading.</summary>
    [JsonPropertyName("upgradeToPlan")] public string UpgradeToPlan { get; set; } = string.Empty;

    /// <summary>The confirmation modal's way out.</summary>
    [JsonPropertyName("keepPlan")] public string KeepPlan { get; set; } = string.Empty;

    /// <summary>Said when a change needs a card this package does not collect.</summary>
    [JsonPropertyName("paymentMethodRequired")] public string PaymentMethodRequired { get; set; } = string.Empty;

    /// <summary>"Subscribe to {tier}?" — asked before a FIRST subscription, which has no preview.</summary>
    [JsonPropertyName("subscribeConfirm")] public string SubscribeConfirm { get; set; } = string.Empty;

    /// <summary>Said when the proxy answers 401.</summary>
    [JsonPropertyName("sessionExpired")] public string SessionExpired { get; set; } = string.Empty;

    // ----- The pack picker ------------------------------------------------------------------

    [JsonPropertyName("buyingPacks")] public string BuyingPacks { get; set; } = string.Empty;

    /// <summary>"Confirming {name} with your bank..." — one slot.</summary>
    [JsonPropertyName("authenticatingPack")] public string AuthenticatingPack { get; set; } = string.Empty;

    [JsonPropertyName("packsUnavailable")] public string PacksUnavailable { get; set; } = string.Empty;
    [JsonPropertyName("packStatusTrialing")] public string PackStatusTrialing { get; set; } = string.Empty;
    [JsonPropertyName("packStatusActive")] public string PackStatusActive { get; set; } = string.Empty;
    [JsonPropertyName("packStatusFailed")] public string PackStatusFailed { get; set; } = string.Empty;
    [JsonPropertyName("packStatusGranted")] public string PackStatusGranted { get; set; } = string.Empty;

    /// <summary>"{brand} ending in {last4}".</summary>
    [JsonPropertyName("savedCardOnFile")] public string SavedCardOnFile { get; set; } = string.Empty;

    /// <summary>"Continue with {count} packs".</summary>
    [JsonPropertyName("continueWithPacks")] public string ContinueWithPacks { get; set; } = string.Empty;

    /// <summary>The singular, used at exactly one pack.</summary>
    [JsonPropertyName("continueWithOnePack")] public string ContinueWithOnePack { get; set; } = string.Empty;
}

/// <summary>
/// What <c>&lt;vc:registration-subscription-manage /&gt;</c> renders: what a customer already pays
/// for, and every way of changing it.
/// </summary>
/// <remarks>
/// <para>
/// The panels are the platform's own six — status, plans, features, packs, usage, overrides — so a
/// site swapping a hand-built plan page for this keeps its markup and its locators. What is new is
/// the middle: the plan change runs through the shared driver, so a preview is confirmed in EVERY
/// layout and a prorated charge the bank wants to see is authenticated and the parked change
/// completed instead of being refused.
/// </para>
/// <para>
/// There is NO plan-change payment form, by standing decision. A change that needs a new card says
/// so and raises <c>ww-regsub-payment-required</c> with everything a host needs to mount
/// <c>&lt;vc:payment /&gt;</c> itself.
/// </para>
/// </remarks>
public class RegistrationSubscriptionManageViewModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private List<ManageSection>? _visibleSections;
    private List<ManageSection>? _bodySections;
    private List<AppTierAddOnModel>? _pickerPacks;
    private string? _labelsJson;
    private string? _packNamesJson;

    /// <summary>A stable id for the root element; generated unless the host named one.</summary>
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string AppId { get; set; } = string.Empty;

    /// <summary>
    /// The HOST's app-tier proxy. Still needed: the admin- and company-scoped writes (feature
    /// overrides, usage limits, another user's plan) have no shipped route, because the shipped
    /// proxy acts as the signed-in user and has no admin scope.
    /// </summary>
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-app-tiers";

    /// <summary>Where the shipped same-origin proxy is mounted.</summary>
    public string RegSubProxyUrl { get; set; } = "/api/wildwood-regsub";

    /// <summary>Copy the host may replace; the shipped words otherwise.</summary>
    public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

    public string? CompanyId { get; set; }

    public string? UserId { get; set; }

    /// <summary>Whether the app tracks subscriptions per company, as the server reports it.</summary>
    public bool IsCompanyMode { get; set; }

    public bool IsAdmin { get; set; }

    /// <summary>The currency the surface quotes in.</summary>
    public string Currency { get; set; } = "USD";

    public ManageLayout Layout { get; set; } = ManageLayout.Tabs;

    /// <summary>The sections the host asked for, or null for all of them in the shipped order.</summary>
    public IReadOnlyList<ManageSection>? RequestedSections { get; set; }

    public bool ShowStatusAboveTabs { get; set; }

    /// <summary>Whether the packs section is offered at all.</summary>
    public bool ShowAddOns { get; set; } = true;

    /// <summary>Whether the subscription and its packs may be cancelled from here.</summary>
    public bool AllowCancel { get; set; } = true;

    /// <summary>Whether the packs panel offers "Add packs", and the picker is rendered.</summary>
    public bool AllowPackSelfService { get; set; }

    public bool ShowBillingToggle { get; set; } = true;

    /// <summary>Where a plan that names no price sends the visitor.</summary>
    public string? ContactUrl { get; set; }

    /// <summary>Carried on the root for a host that reads it; never navigated to by this view.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>The Stripe key the 3-D Secure confirmation and the pack card need.</summary>
    public string? PublishableKey { get; set; }

    /// <summary>The provider the pack checkout's SetupIntent is created on.</summary>
    public string? PaymentProviderId { get; set; }

    /// <summary>The subscription the panels render, read once and handed to each of them.</summary>
    public UserTierSubscriptionModel? Subscription { get; set; }

    /// <summary>How many feature overrides are active, for the overrides tab's badge.</summary>
    public int OverrideCount { get; set; }

    /// <summary>Every pack the account holds, cancelled and expired ones included.</summary>
    public List<UserAddOnSubscriptionModel> ActiveAddOns { get; set; } = new();

    /// <summary>Every pack the app sells to this account.</summary>
    public List<AppTierAddOnModel> AvailableAddOns { get; set; } = new();

    /// <summary>Replaces "This change needs a payment method."</summary>
    public string? PaymentRequiredText { get; set; }

    /// <summary>Whether this view has an app to manage at all.</summary>
    public bool HasApp
    {
        get { return AppId is { Length: > 0 }; }
    }

    /// <summary>The sentence shown in place of everything when it does not.</summary>
    public string AppIdRequiredMessage
    {
        get { return RegistrationSubscriptionManageDecisions.AppIdRequired; }
    }

    /// <summary>The plan the account is on, for the packs panel's "bundled" rule.</summary>
    public string? CurrentTierId
    {
        get { return Subscription?.AppTierId; }
    }

    /// <summary>The sections this viewer gets, in the host's order.</summary>
    public List<ManageSection> VisibleSections
    {
        get
        {
            return _visibleSections ??= RegistrationSubscriptionManageDecisions.VisibleSections(
                RequestedSections, IsAdmin, ShowAddOns);
        }
    }

    /// <summary>Whether the subscription card is lifted out of the section list.</summary>
    public bool StatusAbove
    {
        get { return RegistrationSubscriptionManageDecisions.StatusAbove(VisibleSections, ShowStatusAboveTabs); }
    }

    /// <summary>The sections rendered in the body, the lifted status card excluded.</summary>
    public List<ManageSection> BodySections
    {
        get
        {
            return _bodySections ??= RegistrationSubscriptionManageDecisions.BodySections(
                VisibleSections, StatusAbove);
        }
    }

    /// <summary>The tab or heading text for one section.</summary>
    public string SectionTitle(ManageSection section)
    {
        return RegistrationSubscriptionManageDecisions.SectionTitle(section, Labels);
    }

    /// <summary>The <c>data-ww-section</c> value for one section.</summary>
    public string SectionName(ManageSection section)
    {
        return RegistrationSubscriptionManageDecisions.SectionName(section);
    }

    /// <summary>The wire spelling of the layout, for the root's data attribute.</summary>
    public string LayoutCode
    {
        get { return RegistrationSubscriptionManageDecisions.LayoutCode(Layout); }
    }

    /// <summary>Whether the panels read the company's subscription.</summary>
    public bool UseCompanyScope
    {
        get { return RegistrationSubscriptionManageDecisions.UseCompanyScope(IsCompanyMode, CompanyId); }
    }

    /// <summary>Whether the panels read one user's subscription, as an admin acting for them.</summary>
    public bool UseUserScope
    {
        get { return RegistrationSubscriptionManageDecisions.UseUserScope(IsCompanyMode, UserId); }
    }

    /// <summary>
    /// Whether the change this view posts is the SIGNED-IN USER's own — the only scope that sends
    /// <c>SupportsPaymentAction</c> and can finish a parked 3-D Secure change.
    /// </summary>
    public bool IsSelfScope
    {
        get { return !UseCompanyScope && !UseUserScope; }
    }

    /// <summary>
    /// The packs the picker offers: everything on sale the account does not hold and the plan does
    /// not bundle.
    /// </summary>
    public List<AppTierAddOnModel> PickerPacks
    {
        get
        {
            return _pickerPacks ??= RegistrationSubscriptionManageDecisions.PickerPacks(
                AvailableAddOns, ActiveAddOns, CurrentTierId);
        }
    }

    /// <summary>Whether "Add packs" is offered: self-service, packs shown, and something to sell.</summary>
    public bool ShowPackPicker
    {
        get { return AllowPackSelfService && ShowAddOns && IsSelfScope && PickerPacks.Count > 0; }
    }

    /// <summary>One pack card's rendering decisions, built on demand by the view.</summary>
    public PricingPackCardViewModel PackCard(AppTierAddOnModel addOn)
    {
        return new PricingPackCardViewModel(addOn, Currency, Labels);
    }

    /// <summary>Every offered pack id in catalog order, so a basket reads in catalog order.</summary>
    public string PackOrderAttribute
    {
        get
        {
            var ids = new List<string>(PickerPacks.Count);
            foreach (var pack in PickerPacks)
            {
                if (pack?.Id is { Length: > 0 }) ids.Add(pack.Id);
            }
            return string.Join(",", ids);
        }
    }

    /// <summary>The cap a selection may not exceed. A basket is not a shopping cart.</summary>
    public int MaxPackSelection
    {
        get { return CatalogHelpers.MaxAddOnSelection; }
    }

    /// <summary>The picker's Continue wording before anything is ticked.</summary>
    public string ContinuePacksInitialLabel
    {
        get { return RegistrationSubscriptionLabels.Format(Labels.ContinueWithPacks, "count", 0); }
    }

    /// <summary>Pack display names, so an outcome can be named without re-reading the catalog.</summary>
    public string PackNamesJson
    {
        get { return _packNamesJson ??= BuildPackNamesJson(); }
    }

    /// <summary>The copy the scripts paint with, as one JSON attribute.</summary>
    public string LabelsJson
    {
        get { return _labelsJson ??= JsonSerializer.Serialize(BuildScriptLabels(), SerializerOptions); }
    }

    private string BuildPackNamesJson()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var addOn in AvailableAddOns)
        {
            if (addOn?.Id is { Length: > 0 }) names[addOn.Id] = addOn.Name ?? addOn.Id;
        }

        foreach (var owned in ActiveAddOns)
        {
            if (owned?.AppTierAddOnId is { Length: > 0 } && !names.ContainsKey(owned.AppTierAddOnId))
            {
                names[owned.AppTierAddOnId] = owned.AddOnName ?? owned.AppTierAddOnId;
            }
        }

        return JsonSerializer.Serialize(names, SerializerOptions);
    }

    private ManageScriptLabels BuildScriptLabels()
    {
        return new ManageScriptLabels
        {
            AuthenticatingChange = Labels.AuthenticatingChange,
            ApplyingChange = Labels.ApplyingChange,
            PlanChangeFailed = Labels.PlanChangeFailed,
            PlanChangeExpired = Labels.PlanChangeExpired,
            PlanChangePaymentFailed = Labels.PlanChangePaymentFailed,
            PlanChangeSuperseded = Labels.PlanChangeSuperseded,
            PlanChangeNotFound = Labels.PlanChangeNotFound,
            PlanChangeInProgress = Labels.PlanChangeInProgress,
            PaymentUnconfirmed = Labels.PaymentUnconfirmed,
            UpgradeToPlan = Labels.UpgradeToPlan,
            KeepPlan = Labels.KeepPlan,

            PaymentMethodRequired = PaymentRequiredText is { Length: > 0 }
                ? PaymentRequiredText
                : RegistrationSubscriptionManageDecisions.PaymentMethodRequired,

            SubscribeConfirm = RegistrationSubscriptionManageDecisions.SubscribeConfirm,
            SessionExpired = RegistrationSubscriptionManageDecisions.SessionExpired,

            BuyingPacks = Labels.BuyingPacks,
            AuthenticatingPack = Labels.AuthenticatingPack,
            PacksUnavailable = Labels.PacksUnavailable,
            PackStatusTrialing = Labels.PackStatusTrialing,
            PackStatusActive = Labels.PackStatusActive,
            PackStatusFailed = Labels.PackStatusFailed,
            PackStatusGranted = Labels.PackStatusGranted,
            SavedCardOnFile = Labels.SavedCardOnFile,
            ContinueWithPacks = Labels.ContinueWithPacks,
            ContinueWithOnePack = Labels.ContinueWithOnePack
        };
    }
}
