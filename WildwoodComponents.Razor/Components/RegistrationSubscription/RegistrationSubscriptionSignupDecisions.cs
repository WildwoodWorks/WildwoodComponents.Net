using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Components.RegistrationSubscription;

/// <summary>
/// Every decision the Razor signup view makes on the server, as pure functions.
/// </summary>
/// <remarks>
/// <para>
/// Ported from packages/wildwood-react-shared/src/registrationSubscription/useSignupFlow.ts and
/// packages/wildwood-react/src/components/registrationSubscription/views/SignupView.tsx, by way of
/// the Blazor <c>SignupViewDecisions</c>, and named after them so the same rule is called the same
/// thing on every stack.
/// </para>
/// <para>
/// A rule that lives inside markup or inside a browser script is a rule this repo cannot test:
/// there is no bUnit renderer and no JS harness. So the resolution rules live here, the view is a
/// thin arrangement of what this produces, and <c>wwwroot/js/regsub-signup.js</c> reads the
/// answers off data attributes rather than deriving them again.
/// </para>
/// <para>
/// The mirror image of that rule is the state machine, which the browser genuinely does own (the
/// flow is client-driven after the first paint): it lives in <c>wwwroot/js/regsub-machines.js</c>
/// and is covered by a Node self-test instead.
/// </para>
/// </remarks>
public static class RegistrationSubscriptionSignupDecisions
{
    #region Error codes and fallbacks

    /// <summary>The code a failed signup is reported under when nothing more precise is known.</summary>
    public const string SignupFailedCode = "signup_failed";

    /// <summary>The code a token the server refused is reported under.</summary>
    public const string TokenRejectedCode = "registration_token_rejected";

    /// <summary>Said when the server refused a token but sent no reason.</summary>
    public const string TokenRejectedFallback = "Invalid or expired registration token";

    /// <summary>Said when a signup failed and nothing carried a message.</summary>
    public const string SignupFailedFallback = "Signup failed. Please try again.";

    /// <summary>The code an unreadable catalog is reported under, shared with the pricing view.</summary>
    public const string CatalogErrorCode = RegistrationSubscriptionPricingDecisions.CatalogErrorCode;

    /// <summary>
    /// What the register form's submit button says. NOT a label on any stack: React hard-codes
    /// both words, and a live site's suite clicks them by name.
    /// </summary>
    public const string ContinueButtonText = "Continue";

    /// <summary>The other half of that pair, said when no plan step is still ahead.</summary>
    public const string CreateAccountButtonText = "Create Account";

    #endregion

    #region Parameter parsing

    /// <summary>
    /// <c>plan-selection</c>: <c>skip</c> leaves the plan to the app, anything else lets the
    /// visitor choose. A typo picks the safe default rather than throwing the page away.
    /// </summary>
    public static SignupPlanSelection ParsePlanSelection(string? value)
    {
        return string.Equals(value?.Trim(), "skip", StringComparison.OrdinalIgnoreCase)
            ? SignupPlanSelection.Skip
            : SignupPlanSelection.Choose;
    }

    /// <summary>
    /// <c>plan-default</c>: <c>free</c> opens the plan grid highlighted on the app's free plan;
    /// anything else (the default <c>none</c>) opens it on nothing. A typo picks the safe default
    /// rather than throwing the page away, exactly as the other mode attributes do.
    /// </summary>
    public static SignupPlanDefault ParsePlanDefault(string? value)
    {
        return string.Equals(value?.Trim(), "free", StringComparison.OrdinalIgnoreCase)
            ? SignupPlanDefault.Free
            : SignupPlanDefault.None;
    }

    /// <summary>
    /// <c>pack-selection</c>: <c>choose</c> (React spells the same thing <c>multi</c>) offers the
    /// pack step; anything else removes it. Default <c>none</c>, as React's signup defaults.
    /// </summary>
    /// <remarks>
    /// <c>none</c> removes the STEP ONLY. Packs a signup link chose are still bought — the visitor
    /// picked them on the pricing page, and dropping them silently would be a basket that
    /// evaporates between two screens.
    /// </remarks>
    public static SignupPackSelection ParsePackSelection(string? value)
    {
        var trimmed = value?.Trim();
        var chooses = string.Equals(trimmed, "choose", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "multi", StringComparison.OrdinalIgnoreCase);

        return chooses ? SignupPackSelection.Choose : SignupPackSelection.None;
    }

    /// <summary>
    /// <c>token-mode</c>: <c>required</c> is invite redemption — the token step is the only way
    /// in, the plan and the packs come from the token, and it overrides a CLOSED configuration
    /// because the server validates the invite itself.
    /// </summary>
    public static SignupTokenMode ParseTokenMode(string? value)
    {
        return string.Equals(value?.Trim(), "required", StringComparison.OrdinalIgnoreCase)
            ? SignupTokenMode.Required
            : SignupTokenMode.Auto;
    }

    /// <summary>The machine's wire spelling of a plan-selection mode.</summary>
    public static string PlanSelectionCode(SignupPlanSelection value)
    {
        return value == SignupPlanSelection.Skip ? "skip" : "choose";
    }

    /// <summary>The machine's wire spelling of a pack-selection mode.</summary>
    public static string PackSelectionCode(SignupPackSelection value)
    {
        return value == SignupPackSelection.Choose ? "choose" : "none";
    }

    /// <summary>The machine's wire spelling of a token mode.</summary>
    public static string TokenModeCode(SignupTokenMode value)
    {
        return value == SignupTokenMode.Required ? "required" : "auto";
    }

    #endregion

    #region Steps

    /// <summary>
    /// The <c>data-ww-step</c> name for a machine step.
    /// </summary>
    /// <remarks>
    /// The table itself is <see cref="StepNames.ForSignup"/>, in Shared: Blazor's signup view
    /// publishes the same names and the Playwright helpers in WildwoodComponents.Testing wait on
    /// them, so a copy here would be a name that could drift in one stack only.
    /// </remarks>
    public static string StepName(SignupStep step)
    {
        return StepNames.ForSignup(step);
    }

    #endregion

    #region Selection

    /// <summary>
    /// What the signup starts with, taking the explicit parameter over the query string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Precedence: an explicit tag-helper attribute always wins.</b> A host that wrote
    /// <c>pre-selected-tier-id="@tier"</c> meant it, and a <c>?tier=</c> in the address bar must
    /// not quietly override the page's own decision. The query string fills in only what the host
    /// left unset, key for key — so a page may hard-code the plan and still read the packs and the
    /// email out of the link.
    /// </para>
    /// <para>
    /// Everything is vetted against the live catalog: an unknown tier is IGNORED rather than
    /// honoured (the link is stale or hand-edited), and the pack list is de-duplicated and capped
    /// at <see cref="CatalogHelpers.MaxAddOnSelection"/> — a signup link is not a shopping cart.
    /// </para>
    /// </remarks>
    public static SignupResolvedParams ResolveParams(
        PublicCatalog? catalog,
        QueryParameters? query,
        string? preSelectedTierId,
        string? preSelectedPricingId,
        string? preSelectedAddOnIds,
        string? registrationToken,
        string? prefillEmail)
    {
        var fromQuery = SignupParams.Parse(query, catalog);

        var tierId = Trimmed(preSelectedTierId) ?? fromQuery.TierId;
        var pricingId = Trimmed(preSelectedPricingId) ?? fromQuery.PricingId;

        // A tier the app does not sell is dropped, and its pricing option goes with it: a pricing
        // id without a tier cannot be priced and would travel into the payment step as a phantom.
        var tier = FindTier(catalog, tierId);
        if (tier is null)
        {
            tierId = null;
            pricingId = null;
        }

        // A comma-separated string, not a List<string>: a tag-helper attribute whose property is
        // not a string is compiled as a C# expression, and `?addons=` spells a basket this way
        // already. ParseAddOnIdList trims, de-duplicates, drops what the app does not sell and
        // caps at 25 — the same rules the link gets.
        var addOnIds = preSelectedAddOnIds is { Length: > 0 }
            ? CatalogHelpers.ParseAddOnIdList(preSelectedAddOnIds, catalog)
            : fromQuery.AddOnIds;

        // `?invite=` is the same thing as `?token=` on a signup link: both name the invitation the
        // visitor is redeeming, and the JS SDK reads either.
        var token = Trimmed(registrationToken) ?? fromQuery.Token ?? fromQuery.Invite;

        return new SignupResolvedParams
        {
            TierId = tierId,
            PricingId = pricingId,
            AddOnIds = addOnIds,
            RegistrationToken = token,
            PrefillEmail = Trimmed(prefillEmail) ?? fromQuery.Email
        };
    }

    /// <summary>A plan matched case-insensitively: the server's casing for a GUID is not the link's.</summary>
    public static AppTierModel? FindTier(PublicCatalog? catalog, string? tierId)
    {
        if (catalog is null || !(tierId is { Length: > 0 })) return null;

        foreach (var tier in catalog.Tiers)
        {
            if (tier is not null && string.Equals(tier.Id, tierId, StringComparison.OrdinalIgnoreCase)) return tier;
        }

        return null;
    }

    /// <summary>
    /// The plan the flow starts with: the one a signup link preselected, or the app's default when
    /// the host skips the plan step. TS <c>presetPlan</c>.
    /// </summary>
    /// <remarks>
    /// An invite's plan comes from its token, not from the link or the app's default, so invite
    /// redemption resolves nothing here.
    /// </remarks>
    public static SignupPlanView? ResolvePresetPlan(
        PublicCatalog? catalog,
        bool invite,
        SignupPlanSelection planSelection,
        string? preSelectedTierId,
        string? preSelectedPricingId)
    {
        if (catalog is null || invite) return null;

        if (planSelection == SignupPlanSelection.Skip)
        {
            // The URL never picks a plan in a skip flow: the app has one, and this is it.
            AppTierModel? chosen = null;
            foreach (var tier in catalog.Tiers)
            {
                if (tier is not null && tier.IsDefault) { chosen = tier; break; }
            }

            if (chosen is null)
            {
                foreach (var tier in catalog.Tiers)
                {
                    if (tier is not null && tier.IsFreeTier) { chosen = tier; break; }
                }
            }

            return chosen is null ? null : new SignupPlanView(chosen, CatalogHelpers.ResolvePriceOption(chosen));
        }

        var wanted = FindTier(catalog, preSelectedTierId);
        if (wanted is null) return null;

        return new SignupPlanView(wanted, CatalogHelpers.ResolvePriceOption(wanted, preSelectedPricingId));
    }

    /// <summary>
    /// The plan the grid opens on when nothing has chosen one. TS <c>defaultTierId</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A HIGHLIGHT and nothing more: the machine in <c>wwwroot/js/regsub-machines.js</c> never
    /// sees it, no plan is selected, and the visitor still confirms with a click.
    /// <see cref="SignupPlanDefault.Free"/> names the app's first free plan; an app that sells
    /// none has nothing to suggest, which is not a failure.
    /// </para>
    /// <para>
    /// Invite redemption suggests nothing either: an invite's plan comes from its token, so there
    /// is no grid for a default to open on.
    /// </para>
    /// </remarks>
    public static string? DefaultTierId(SignupPlanDefault planDefault, bool invite, PublicCatalog? catalog)
    {
        if (planDefault != SignupPlanDefault.Free || invite || catalog is null) return null;

        foreach (var tier in catalog.Tiers)
        {
            if (tier is not null && tier.IsFreeTier) return tier.Id;
        }

        return null;
    }

    /// <summary>
    /// Which plan the grid marks. TS
    /// <c>state.selection.tierId ?? flow.defaultTierId ?? props.preSelectedTierId</c>.
    /// </summary>
    /// <remarks>
    /// The default sits AHEAD of the link's plan on purpose: a <c>pre-selected-tier-id</c> still
    /// showing at this point is an id the flow already refused (stale, or hand-edited), so the
    /// grid opens on the host's default rather than on nothing at all. A plan already chosen —
    /// which on this server-rendered surface is the vetted <c>?tier=</c> the machine is seeded
    /// with — beats both.
    /// </remarks>
    public static string? HighlightTierId(string? selectionTierId, string? defaultTierId, string? preSelectedTierId)
    {
        if (selectionTierId is { Length: > 0 }) return selectionTierId;
        if (defaultTierId is { Length: > 0 }) return defaultTierId;
        return preSelectedTierId;
    }

    /// <summary>
    /// Whether a plan has to be paid for before the account is created. TS <c>requiresPayment</c>.
    /// </summary>
    public static bool RequiresPayment(AppTierModel? tier, AppTierPricingModel? pricing)
    {
        if (tier is null) return false;
        return !tier.IsFreeTier && pricing is not null && pricing.Price > 0m;
    }

    /// <summary>Free-trial days on the plan being paid for, 0 when there is no trial or no payment.</summary>
    public static int TrialDays(SignupPlanView? plan)
    {
        if (plan is null || !RequiresPayment(plan.Tier, plan.Pricing)) return 0;
        return plan.Pricing?.TrialDays ?? 0;
    }

    /// <summary>
    /// Whether a plan is still to be chosen, which is what the form's submit button says. TS
    /// <c>planStepAhead</c>. A token's grant is not known while the form is on screen, so this is
    /// what the flow knows then.
    /// </summary>
    public static bool PlanStepAhead(SignupPlanSelection planSelection, bool invite, bool planPreset)
    {
        return planSelection == SignupPlanSelection.Choose && !invite && !planPreset;
    }

    /// <summary>The register form's submit text, by whether a plan step is still ahead.</summary>
    public static string SubmitButtonText(bool planStepAhead)
    {
        return planStepAhead ? ContinueButtonText : CreateAccountButtonText;
    }

    #endregion

    #region Copy

    /// <summary>
    /// The four success sentences, rendered as templates the browser picks between: which one
    /// applies is only known after the account exists, and none of them may be written in English
    /// inside the script.
    /// </summary>
    /// <remarks>
    /// The token sentence's <c>{tier}</c> is filled in the browser from the grant's own tier name,
    /// with the plain word "plan" when the server named none — the same fallback React uses.
    /// </remarks>
    public static string SuccessMessageTemplate(
        RegistrationSubscriptionLabels labels, SignupSuccessKind kind, int trialDays)
    {
        switch (kind)
        {
            case SignupSuccessKind.TokenGrant:
                return labels.SignupCompleteToken;
            case SignupSuccessKind.NoPlan:
                return labels.SignupCompletePlain;
            case SignupSuccessKind.Pending:
                return labels.SignupCompletePending;
            case SignupSuccessKind.Trial:
                return RegistrationSubscriptionLabels.Format(labels.SignupCompleteTrial, "days", trialDays);
            default:
                return labels.SignupCompleteActive;
        }
    }

    /// <summary>The pack-status word for one outcome, so the browser writes no English.</summary>
    public static string PackStatusLabel(RegistrationSubscriptionLabels labels, string? status)
    {
        if (string.Equals(status, SignupPackStatuses.Trialing, StringComparison.Ordinal)) return labels.PackStatusTrialing;
        if (string.Equals(status, SignupPackStatuses.Active, StringComparison.Ordinal)) return labels.PackStatusActive;
        if (string.Equals(status, SignupPackStatuses.Granted, StringComparison.Ordinal)) return labels.PackStatusGranted;
        return labels.PackStatusFailed;
    }

    #endregion

    private static string? Trimmed(string? value)
    {
        if (value is null) return null;
        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }
}

/// <summary>The plan a signup is carrying, resolved against the live catalog.</summary>
public sealed class SignupPlanView
{
    public SignupPlanView(AppTierModel tier, AppTierPricingModel? pricing)
    {
        Tier = tier;
        Pricing = pricing;
    }

    public AppTierModel Tier { get; }

    /// <summary>The option being bought, or null for a plan that sells none ("contact us").</summary>
    public AppTierPricingModel? Pricing { get; }
}

/// <summary>
/// What a signup arrived carrying, after the explicit parameters and the query string have been
/// merged and vetted against the catalog.
/// </summary>
public sealed class SignupResolvedParams
{
    public string? TierId { get; set; }

    public string? PricingId { get; set; }

    public List<string> AddOnIds { get; set; } = new();

    public string? RegistrationToken { get; set; }

    public string? PrefillEmail { get; set; }
}

/// <summary>Which success sentence a finished signup gets.</summary>
public enum SignupSuccessKind
{
    /// <summary>A registration token set the account up; nobody was charged.</summary>
    TokenGrant = 0,

    /// <summary>No plan was chosen.</summary>
    NoPlan = 1,

    /// <summary>The plan could not be started, so its activation is pending.</summary>
    Pending = 2,

    /// <summary>A free trial has started.</summary>
    Trial = 3,

    /// <summary>The plan is active.</summary>
    Active = 4
}
