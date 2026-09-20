using WildwoodComponents.Razor.Components.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// What <c>&lt;vc:registration-and-subscription /&gt;</c> forwards: the view to render and every
/// parameter any of the three takes.
/// </summary>
/// <remarks>
/// A flat bag on purpose. The shell holds no state and reads nothing; it exists so a page that
/// decides at run time which surface to show writes one attribute instead of a branch, and the
/// view below forwards only the parameters the chosen ViewComponent actually accepts.
/// </remarks>
public class RegistrationAndSubscriptionViewModel
{
    /// <summary>The view to render. <see cref="RegistrationSubscriptionView.Pricing"/> when unknown.</summary>
    public RegistrationSubscriptionView View { get; set; } = RegistrationSubscriptionView.Pricing;

    /// <summary>Whether <see cref="RequestedView"/> named one of the three.</summary>
    public bool IsKnownView { get; set; } = true;

    /// <summary>What the host actually wrote, for the developer message.</summary>
    public string? RequestedView { get; set; }

    /// <summary>Whether this render says so on the page. Development only.</summary>
    public bool ShowDeveloperMessage { get; set; }

    /// <summary>The sentence it says.</summary>
    public string DeveloperMessage { get; set; } = string.Empty;

    // ----- Every view -----------------------------------------------------------------------

    public string? AppId { get; set; }
    public string? Currency { get; set; }
    public RegistrationSubscriptionLabels? Labels { get; set; }
    public string? ContactUrl { get; set; }
    public string? ReturnUrl { get; set; }
    public string? ComponentId { get; set; }

    // ----- Pricing --------------------------------------------------------------------------

    public bool ShowPlans { get; set; } = true;
    public bool ShowAddOns { get; set; }
    public bool OfferFreeTierChoice { get; set; } = true;
    public string PackSelection { get; set; } = "none";
    public IReadOnlyList<AddOnGroup>? AddOnGroups { get; set; }
    public bool ShowBillingToggle { get; set; } = true;
    public string DefaultBilling { get; set; } = "monthly";
    public bool ShowFeatureComparison { get; set; } = true;
    public bool ShowLimits { get; set; } = true;
    public string? HighlightTierId { get; set; }
    public bool IncludeJsonLd { get; set; }
    public string? JsonLdUrl { get; set; }
    public string? SelectUrl { get; set; }
    public string? UnavailableText { get; set; }

    // ----- Signup ---------------------------------------------------------------------------

    public string? PreSelectedTierId { get; set; }
    public string? PreSelectedPricingId { get; set; }
    public string? PreSelectedAddOnIds { get; set; }
    public string? RegistrationToken { get; set; }
    public string? PrefillEmail { get; set; }
    public string PlanSelection { get; set; } = "choose";
    public string TokenMode { get; set; } = "auto";
    public bool RequireBillingAddress { get; set; }
    public string? CompleteUrl { get; set; }
    public string? AlreadySignedInUrl { get; set; }
    public string? ClosedText { get; set; }

    // ----- Manage ---------------------------------------------------------------------------

    public string Layout { get; set; } = "tabs";
    public string? Sections { get; set; }
    public bool ShowStatusAboveTabs { get; set; }
    public bool IsAdmin { get; set; }
    public string? UserId { get; set; }
    public string? CompanyId { get; set; }
    public bool AllowPackSelfService { get; set; }
    public bool AllowCancel { get; set; } = true;
    public string? PaymentRequiredText { get; set; }
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-app-tiers";
    public string RegSubProxyUrl { get; set; } = "/api/wildwood-regsub";

    /// <summary>The ViewComponent this render invokes.</summary>
    public string ComponentName
    {
        get { return RegistrationAndSubscriptionShell.ComponentName(View); }
    }
}
