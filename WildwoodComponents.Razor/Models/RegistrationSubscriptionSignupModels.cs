using System.Text.Json;
using System.Text.Json.Serialization;
using WildwoodComponents.Razor.Components.RegistrationSubscription;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Models;

/// <summary>
/// The plan grid, as a partial's model, so the pricing surface and the signup's plan step render
/// the SAME markup from the same decisions rather than two grids that drift.
/// </summary>
/// <remarks>
/// A live site's end-to-end suite locates plans by <c>.ww-tier-grid</c> / <c>.ww-tier-card</c>, so
/// there is exactly one place those are written.
/// </remarks>
public class RegSubPlanGridViewModel
{
    public List<PricingPlanCardViewModel> Cards { get; set; } = new();

    public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

    public PricingBilling Billing { get; set; } = PricingBilling.Monthly;

    /// <summary>Whether the monthly/annual toggle is rendered above the grid.</summary>
    public bool ShowToggle { get; set; }

    /// <summary>"Save up to 17%", or empty when no plan is cheaper by the year.</summary>
    public string AnnualSavingsText { get; set; } = string.Empty;

    public bool ShowFeatureComparison { get; set; } = true;

    public bool ShowLimits { get; set; } = true;

    /// <summary>True when the current cycle is annual, which decides which price block is hidden.</summary>
    public bool IsAnnual
    {
        get { return Billing == PricingBilling.Annual; }
    }
}

/// <summary>
/// The copy <c>wwwroot/js/regsub-signup.js</c> needs at runtime, serialised onto the root element
/// as one <c>data-ww-labels</c> attribute.
/// </summary>
/// <remarks>
/// <para>
/// Everything a visitor can read is decided by the SERVER. A sentence whose wording is only known
/// once the account exists — which of the five success messages applies, what became of a pack —
/// travels as a template or a word, never as English inside the script. A host that overrode
/// <c>labels</c> therefore sees its own wording in the parts of the flow the browser paints, not
/// just the parts the server painted.
/// </para>
/// <para>
/// The property names are the keys the script reads, so they are camelCase on the wire and short.
/// </para>
/// </remarks>
public class SignupScriptLabels
{
    [JsonPropertyName("statusCreatingAccount")] public string StatusCreatingAccount { get; set; } = string.Empty;
    [JsonPropertyName("statusSigningIn")] public string StatusSigningIn { get; set; } = string.Empty;
    [JsonPropertyName("statusActivatingPlan")] public string StatusActivatingPlan { get; set; } = string.Empty;

    [JsonPropertyName("buyingPacks")] public string BuyingPacks { get; set; } = string.Empty;

    /// <summary>"Confirming {name} with your bank..." — one slot.</summary>
    [JsonPropertyName("authenticatingPack")] public string AuthenticatingPack { get; set; } = string.Empty;

    [JsonPropertyName("packsUnavailable")] public string PacksUnavailable { get; set; } = string.Empty;

    [JsonPropertyName("packStatusTrialing")] public string PackStatusTrialing { get; set; } = string.Empty;
    [JsonPropertyName("packStatusActive")] public string PackStatusActive { get; set; } = string.Empty;
    [JsonPropertyName("packStatusFailed")] public string PackStatusFailed { get; set; } = string.Empty;
    [JsonPropertyName("packStatusGranted")] public string PackStatusGranted { get; set; } = string.Empty;

    /// <summary>"Your account has been created with the {tier} from your registration token."</summary>
    [JsonPropertyName("successToken")] public string SuccessToken { get; set; } = string.Empty;

    /// <summary>Already has its <c>{days}</c> filled in for the plan being bought.</summary>
    [JsonPropertyName("successTrial")] public string SuccessTrial { get; set; } = string.Empty;

    [JsonPropertyName("successPlain")] public string SuccessPlain { get; set; } = string.Empty;
    [JsonPropertyName("successActive")] public string SuccessActive { get; set; } = string.Empty;
    [JsonPropertyName("successPending")] public string SuccessPending { get; set; } = string.Empty;

    /// <summary>The word standing in for a tier the token named but the server did not name back.</summary>
    [JsonPropertyName("planWord")] public string PlanWord { get; set; } = "plan";

    /// <summary>"{brand} ending in {last4}".</summary>
    [JsonPropertyName("savedCardOnFile")] public string SavedCardOnFile { get; set; } = string.Empty;

    [JsonPropertyName("tokenPlanPacks")] public string TokenPlanPacks { get; set; } = string.Empty;
    [JsonPropertyName("tokenPlanFeatures")] public string TokenPlanFeatures { get; set; } = string.Empty;

    /// <summary>"Continue with {count} packs".</summary>
    [JsonPropertyName("continueWithPacks")] public string ContinueWithPacks { get; set; } = string.Empty;

    /// <summary>The singular, used at exactly one pack.</summary>
    [JsonPropertyName("continueWithOnePack")] public string ContinueWithOnePack { get; set; } = string.Empty;

    [JsonPropertyName("tokenRejected")] public string TokenRejected { get; set; } = string.Empty;
    [JsonPropertyName("signupFailed")] public string SignupFailed { get; set; } = string.Empty;

    /// <summary>The submit wording for each half of the register form's two states.</summary>
    [JsonPropertyName("submitContinue")] public string SubmitContinue { get; set; } = string.Empty;
    [JsonPropertyName("submitCreate")] public string SubmitCreate { get; set; } = string.Empty;
}

/// <summary>
/// What <c>&lt;vc:registration-subscription-signup /&gt;</c> renders: an account, a plan, packs and
/// a card, in the order that keeps them consistent.
/// </summary>
/// <remarks>
/// <para>
/// The card is taken BEFORE the account exists (PAY-FIRST, as React and Blazor ship it), because a
/// declined card then leaves nothing behind rather than an account sitting on a plan nobody paid
/// for; and the packs are bought AFTER the sign-in, because they are bought as the user.
/// </para>
/// <para>
/// Everything that can be decided before the first byte is decided here: the registration mode,
/// the catalog, the plan the link preselected, the packs, the prices, and every string. The
/// browser walks the flow and paints what the server already wrote.
/// </para>
/// </remarks>
public class RegistrationSubscriptionSignupViewModel
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>A stable id for the root element; generated unless the host named one.</summary>
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public string AppId { get; set; } = string.Empty;

    /// <summary>Where the shipped same-origin proxy is mounted.</summary>
    public string ProxyUrl { get; set; } = "/api/wildwood-regsub";

    public RegistrationSubscriptionLabels Labels { get; set; } = RegistrationSubscriptionLabels.Defaults;

    /// <summary>The live catalog, or null when it could not be read.</summary>
    public PublicCatalog? Catalog { get; set; }

    /// <summary>Why the catalog could not be read, when it could not.</summary>
    public string? CatalogError { get; set; }

    /// <summary>The registration mode, resolved from the app's LIVE settings on this request.</summary>
    public SignupRegistrationMode.Result Mode { get; set; } = SignupRegistrationMode.Resolve(null);

    public SignupTokenMode TokenMode { get; set; } = SignupTokenMode.Auto;

    public SignupPlanSelection PlanSelection { get; set; } = SignupPlanSelection.Choose;

    public SignupPackSelection PackSelection { get; set; } = SignupPackSelection.None;

    /// <summary>What the link and the parameters asked for, vetted against the catalog.</summary>
    public SignupResolvedParams Params { get; set; } = new();

    /// <summary>The plan the flow starts on, or null when the visitor still picks one.</summary>
    public SignupPlanView? PresetPlan { get; set; }

    public bool RequireBillingAddress { get; set; }

    /// <summary>Carried through the flow and handed to whoever listens for the completion.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>Where "Get Started" goes once the signup is done. The Razor analog of React's
    /// <c>onSignupComplete</c> navigating; the event is raised first either way.</summary>
    public string? CompleteUrl { get; set; }

    /// <summary>Where a visitor who is already signed in is sent, when the host names somewhere.</summary>
    public string? AlreadySignedInUrl { get; set; }

    public string? ContactUrl { get; set; }

    /// <summary>The currency the surface quotes in.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Replaces "Registration is closed" — the Razor analog of React's `renderClosed`.</summary>
    public string? ClosedText { get; set; }

    /// <summary>Whether this request already has a signed-in session.</summary>
    public bool AlreadySignedIn { get; set; }

    /// <summary>The app's password policy, in the server's own words, shown under the field.</summary>
    public string? PasswordRequirements { get; set; }

    /// <summary>
    /// The app's registration disclaimers, read anonymously at render time and rendered as real
    /// markup.
    /// </summary>
    /// <remarks>
    /// They are rendered SERVER-SIDE on purpose. A disclaimer whose <c>ContentFormat</c> is HTML
    /// has to be rendered as HTML, and a browser script that did that would be writing a
    /// server-supplied string through <c>innerHTML</c> — which nothing in this package does. The
    /// gate then shows only the ones the signed-in account is actually still asked for.
    /// </remarks>
    public List<PendingDisclaimerModel> Disclaimers { get; set; } = new();

    /// <summary>The Stripe publishable key the pack checkout's card form and 3-D Secure need.</summary>
    /// <remarks>
    /// Resolved server-side off the app's enabled providers by the same rule React follows (the
    /// default provider, else the one marked default, else the first with a key), so the pack
    /// checkout needs no extra round trip and no second provider list.
    /// </remarks>
    public string? PublishableKey { get; set; }

    /// <summary>The provider that key belongs to, echoed on the checkout so they cannot disagree.</summary>
    public string? PaymentProviderId { get; set; }

    public bool ShowFeatureComparison { get; set; } = true;

    public bool ShowLimits { get; set; } = true;

    // ── Derived ────────────────────────────────────────────────────────────────

    /// <summary>Invite redemption: the token decides the plan, the packs and the way in.</summary>
    public bool IsInvite
    {
        get { return TokenMode == SignupTokenMode.Required; }
    }

    /// <summary>True when the catalog could not be read: the flow starts failed, never priced.</summary>
    public bool IsCatalogUnavailable
    {
        get { return Catalog is null || CatalogError is { Length: > 0 }; }
    }

    /// <summary>The sentence the unavailable panel says.</summary>
    public string CatalogUnavailableMessage
    {
        get { return CatalogError is { Length: > 0 } ? CatalogError! : Labels.PricingUnavailable; }
    }

    /// <summary>The sentence the closed panel says.</summary>
    public string ClosedMessage
    {
        get { return ClosedText is { Length: > 0 } ? ClosedText! : Labels.RegistrationClosed; }
    }

    /// <summary>
    /// The step the server paints first. Closed and unavailable are decided here rather than
    /// after a round trip, so a closed sign-up never flashes a form.
    /// </summary>
    public string InitialStep
    {
        get
        {
            if (AlreadySignedIn) return "signedIn";
            if (IsCatalogUnavailable) return "failed";
            if (Mode.Closed) return "closed";
            return "register";
        }
    }

    /// <summary>Whether a plan step is still ahead when the form is first shown.</summary>
    public bool PlanStepAhead
    {
        get
        {
            return RegistrationSubscriptionSignupDecisions.PlanStepAhead(
                PlanSelection, IsInvite, PresetPlan is not null);
        }
    }

    /// <summary>The register form's submit text for the state the server is rendering.</summary>
    public string SubmitButtonText
    {
        get { return RegistrationSubscriptionSignupDecisions.SubmitButtonText(PlanStepAhead); }
    }

    /// <summary>The plan grid the plan step shows, or null when there is nothing to choose from.</summary>
    public RegSubPlanGridViewModel? PlanGrid
    {
        get { return _planGrid ??= BuildPlanGrid(); }
    }

    /// <summary>The packs on offer, grouped the way the pricing surface groups them.</summary>
    public List<PricingPackGroup> PackGroups
    {
        get
        {
            return _packGroups ??= RegistrationSubscriptionPricingDecisions.GroupAddOns(
                Catalog?.AddOns, null, Labels.MorePacks);
        }
    }

    /// <summary>One pack card's rendering decisions, built on demand by the view.</summary>
    public PricingPackCardViewModel PackCard(AppTierAddOnModel addOn)
    {
        return new PricingPackCardViewModel(addOn, Currency, Labels);
    }

    /// <summary>Whether the pack step has anything to show.</summary>
    public bool HasPackStep
    {
        get { return PackSelection == SignupPackSelection.Choose && !IsInvite && PackGroups.Count > 0; }
    }

    /// <summary>Whether the plan step has anything to show.</summary>
    public bool HasPlanStep
    {
        get { return PlanGrid is not null && PlanGrid.Cards.Count > 0; }
    }

    /// <summary>The plan's amount, for the payment step. Zero when nothing is being charged.</summary>
    public decimal PlanAmount
    {
        get { return PresetPlan?.Pricing?.Price ?? 0m; }
    }

    /// <summary>Free-trial days on the plan being paid for.</summary>
    public int PlanTrialDays
    {
        get { return RegistrationSubscriptionSignupDecisions.TrialDays(PresetPlan); }
    }

    /// <summary>Whether the plan the flow starts with has to be paid for.</summary>
    public bool PlanRequiresPayment
    {
        get { return RegistrationSubscriptionSignupDecisions.RequiresPayment(PresetPlan?.Tier, PresetPlan?.Pricing); }
    }

    /// <summary>The cap a pack selection may not exceed.</summary>
    public int MaxPackSelection
    {
        get { return CatalogHelpers.MaxAddOnSelection; }
    }

    /// <summary>The comma-joined ids the link asked for, for the root's data attribute.</summary>
    public string SelectedAddOnsAttribute
    {
        get { return string.Join(",", Params.AddOnIds); }
    }

    /// <summary>Every pack id in catalog order, so a basket reads in catalog order in the browser.</summary>
    public string PackOrderAttribute
    {
        get { return string.Join(",", RegistrationSubscriptionPricingDecisions.PackOrder(Catalog)); }
    }

    /// <summary>
    /// The display names an outcome needs, so a finished signup can name a plan or a pack without
    /// the browser re-reading the catalog.
    /// </summary>
    public string NamesJson
    {
        get { return _namesJson ??= BuildNamesJson(); }
    }

    /// <summary>The copy the script paints with, as one JSON attribute.</summary>
    public string LabelsJson
    {
        get { return _labelsJson ??= JsonSerializer.Serialize(BuildScriptLabels(), SerializerOptions); }
    }

    /// <summary>The wire spelling of the token mode, for the machine's options.</summary>
    public string TokenModeCode
    {
        get { return RegistrationSubscriptionSignupDecisions.TokenModeCode(TokenMode); }
    }

    /// <summary>The wire spelling of the plan-selection mode.</summary>
    public string PlanSelectionCode
    {
        get { return RegistrationSubscriptionSignupDecisions.PlanSelectionCode(PlanSelection); }
    }

    /// <summary>The wire spelling of the pack-selection mode.</summary>
    public string PackSelectionCode
    {
        get { return RegistrationSubscriptionSignupDecisions.PackSelectionCode(PackSelection); }
    }

    /// <summary>The order summary's price line for the plan being bought.</summary>
    public string PlanPriceText
    {
        get { return FormatHelpers.FormatMoney(PlanAmount, Currency); }
    }

    /// <summary>The frequency the plan is billed at, as the summary writes it.</summary>
    public string PlanFrequencyText
    {
        get { return RegistrationSubscriptionPricingDecisions.IntervalText(PresetPlan?.Pricing); }
    }

    /// <summary>
    /// The trial line above the card: the trial label, then the due-today amount formatted by
    /// <see cref="FormatHelpers.FormatMoney"/> off the catalog. Empty when there is no trial.
    /// </summary>
    public string PlanTrialText
    {
        get
        {
            var days = PlanTrialDays;
            if (days <= 0) return string.Empty;

            return CatalogHelpers.TrialLabel(days)
                + ". " + Labels.DueToday + ": " + FormatHelpers.FormatMoney(0m, Currency);
        }
    }

    private RegSubPlanGridViewModel? _planGrid;
    private List<PricingPackGroup>? _packGroups;
    private string? _namesJson;
    private string? _labelsJson;

    private RegSubPlanGridViewModel? BuildPlanGrid()
    {
        if (Catalog is null) return null;

        var tiers = RegistrationSubscriptionPricingDecisions.VisibleTiers(Catalog, true);
        var cards = new List<PricingPlanCardViewModel>(tiers.Count);
        foreach (var tier in tiers)
        {
            cards.Add(new PricingPlanCardViewModel(tier, Currency, ContactUrl, Params.TierId));
        }

        var best = RegistrationSubscriptionPricingDecisions.BestAnnualDiscount(tiers);

        return new RegSubPlanGridViewModel
        {
            Cards = cards,
            Labels = Labels,
            Billing = PricingBilling.Monthly,
            ShowToggle = RegistrationSubscriptionPricingDecisions.HasAnnualPricing(tiers),
            AnnualSavingsText = best > 0
                ? RegistrationSubscriptionLabels.Format(Labels.AnnualSavings, "percent", best)
                : string.Empty,
            ShowFeatureComparison = ShowFeatureComparison,
            ShowLimits = ShowLimits
        };
    }

    private string BuildNamesJson()
    {
        var tiers = new Dictionary<string, string>(StringComparer.Ordinal);
        var addOns = new Dictionary<string, string>(StringComparer.Ordinal);

        if (Catalog is not null)
        {
            foreach (var tier in Catalog.Tiers)
            {
                if (tier is not null) tiers[tier.Id] = tier.Name;
            }

            foreach (var addOn in Catalog.AddOns)
            {
                if (addOn is not null) addOns[addOn.Id] = addOn.Name;
            }
        }

        return JsonSerializer.Serialize(new { tiers, addOns }, SerializerOptions);
    }

    private SignupScriptLabels BuildScriptLabels()
    {
        return new SignupScriptLabels
        {
            StatusCreatingAccount = Labels.StatusCreatingAccount,
            StatusSigningIn = Labels.StatusSigningIn,
            StatusActivatingPlan = Labels.StatusActivatingPlan,
            BuyingPacks = Labels.BuyingPacks,
            AuthenticatingPack = Labels.AuthenticatingPack,
            PacksUnavailable = Labels.PacksUnavailable,
            PackStatusTrialing = Labels.PackStatusTrialing,
            PackStatusActive = Labels.PackStatusActive,
            PackStatusFailed = Labels.PackStatusFailed,
            PackStatusGranted = Labels.PackStatusGranted,
            SuccessToken = Labels.SignupCompleteToken,

            // The trial sentence's day count is the plan's, and the plan is known here.
            SuccessTrial = RegistrationSubscriptionLabels.Format(
                Labels.SignupCompleteTrial, "days", PlanTrialDays),

            SuccessPlain = Labels.SignupCompletePlain,
            SuccessActive = Labels.SignupCompleteActive,
            SuccessPending = Labels.SignupCompletePending,
            SavedCardOnFile = Labels.SavedCardOnFile,
            TokenPlanPacks = Labels.TokenPlanPacks,
            TokenPlanFeatures = Labels.TokenPlanFeatures,
            ContinueWithPacks = Labels.ContinueWithPacks,
            ContinueWithOnePack = Labels.ContinueWithOnePack,
            TokenRejected = RegistrationSubscriptionSignupDecisions.TokenRejectedFallback,
            SignupFailed = RegistrationSubscriptionSignupDecisions.SignupFailedFallback,
            SubmitContinue = RegistrationSubscriptionSignupDecisions.ContinueButtonText,
            SubmitCreate = RegistrationSubscriptionSignupDecisions.CreateAccountButtonText
        };
    }
}
