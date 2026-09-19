using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Shared.RegistrationSubscription;

// The signup flow's state, ported from
// packages/wildwood-react-shared/src/registrationSubscription/signupMachine.ts.
//
// The state is a reference type with getter-only public properties: the reducer builds every new
// state from Copy(), and an event it ignores returns the SAME instance, so a host can compare with
// ReferenceEquals and skip a re-render. Records/init are unavailable on the netstandard2.0 leg
// (no IsExternalInit), so the copy is written out by hand.

/// <summary>Where the signup flow is.</summary>
public enum SignupStep
{
    Loading = 0,
    Closed = 1,
    Register = 2,
    Token = 3,
    Plan = 4,
    Packs = 5,
    Payment = 6,
    Creating = 7,
    Disclaimers = 8,
    PackCheckout = 9,
    Done = 10,
    Failed = 11
}

/// <summary>Whether the visitor picks a plan, or the app decides it elsewhere.</summary>
public enum SignupPlanSelection
{
    /// <summary>Walk the plan step.</summary>
    Choose = 0,

    /// <summary>Leave the plan to the app (a single-plan product, or a plan chosen elsewhere).</summary>
    Skip = 1
}

/// <summary>Whether the pack step is offered at all.</summary>
public enum SignupPackSelection
{
    Choose = 0,

    /// <summary>Hide the pack step.</summary>
    None = 1
}

/// <summary>
/// When the plan's card is taken. <see cref="BeforeAccount"/> is the web order — nothing is created
/// until the money is in. <see cref="AfterAccount"/> is the store-billed order — nothing is charged
/// until there is an account to attach it to.
/// </summary>
public enum SignupPaymentOrder
{
    BeforeAccount = 0,
    AfterAccount = 1
}

/// <summary>
/// Display names from the catalog, so an outcome can name a tier/pack without re-reading it.
/// </summary>
public sealed class SignupCatalogNames
{
    /// <summary>No names known yet.</summary>
    public static readonly SignupCatalogNames Empty = new SignupCatalogNames(null, null);

    public SignupCatalogNames(
        IReadOnlyDictionary<string, string>? tiers = null,
        IReadOnlyDictionary<string, string>? addOns = null)
    {
        Tiers = tiers;
        AddOns = addOns;
    }

    public IReadOnlyDictionary<string, string>? Tiers { get; }

    public IReadOnlyDictionary<string, string>? AddOns { get; }

    /// <summary>The tier's display name, or the id when the catalog did not name it.</summary>
    public string TierName(string tierId)
    {
        return Lookup(Tiers, tierId);
    }

    /// <summary>The pack's display name, or the id when the catalog did not name it.</summary>
    public string AddOnName(string addOnId)
    {
        return Lookup(AddOns, addOnId);
    }

    private static string Lookup(IReadOnlyDictionary<string, string>? map, string id)
    {
        if (map is not null && map.TryGetValue(id, out var name) && name is not null)
        {
            return name;
        }

        return id;
    }
}

/// <summary>What the visitor is buying.</summary>
public sealed class SignupSelection
{
    /// <summary>Nothing chosen.</summary>
    public static readonly SignupSelection Empty = new SignupSelection(null, null, null);

    public SignupSelection(string? tierId, string? pricingId, IReadOnlyList<string>? addOnIds)
    {
        TierId = tierId;
        PricingId = pricingId;
        AddOnIds = addOnIds ?? Array.Empty<string>();
    }

    public string? TierId { get; }

    public string? PricingId { get; }

    public IReadOnlyList<string> AddOnIds { get; }
}

/// <summary>How the signup flow is configured. Every member has the JS default.</summary>
public sealed class SignupMachineOptions
{
    /// <summary>
    /// <see cref="SignupTokenMode.Required"/> is invite redemption: the token step is the only way
    /// in, and packs are skipped.
    /// </summary>
    public SignupTokenMode TokenMode { get; set; } = SignupTokenMode.Auto;

    public SignupPlanSelection PlanSelection { get; set; } = SignupPlanSelection.Choose;

    public SignupPackSelection PackSelection { get; set; } = SignupPackSelection.Choose;

    /// <summary>When the plan's card is taken. Default <see cref="SignupPaymentOrder.BeforeAccount"/>.</summary>
    public SignupPaymentOrder PaymentOrder { get; set; } = SignupPaymentOrder.BeforeAccount;

    /// <summary>A selection the signup link already made.</summary>
    public SignupSelection? Selection { get; set; }
}

/// <summary>The options with every default filled in, so the reducer never re-applies them.</summary>
public sealed class ResolvedSignupOptions
{
    public ResolvedSignupOptions(
        SignupTokenMode tokenMode,
        SignupPlanSelection planSelection,
        SignupPackSelection packSelection,
        SignupPaymentOrder paymentOrder)
    {
        TokenMode = tokenMode;
        PlanSelection = planSelection;
        PackSelection = packSelection;
        PaymentOrder = paymentOrder;
    }

    public SignupTokenMode TokenMode { get; }

    public SignupPlanSelection PlanSelection { get; }

    public SignupPackSelection PackSelection { get; }

    public SignupPaymentOrder PaymentOrder { get; }
}

/// <summary>
/// The signup flow's state. Immutable to callers: only <see cref="SignupMachine"/> writes it, and
/// only onto a fresh <see cref="Copy"/>.
/// </summary>
public sealed class SignupState
{
    internal SignupState()
    {
    }

    private SignupState(SignupState source)
    {
        Step = source.Step;
        Token = source.Token;
        Options = source.Options;
        Mode = source.Mode;
        ModeReady = source.ModeReady;
        CatalogReady = source.CatalogReady;
        Names = source.Names;
        Selection = source.Selection;
        PlanRequiresPayment = source.PlanRequiresPayment;
        PlanPreset = source.PlanPreset;
        FormSubmitted = source.FormSubmitted;
        Email = source.Email;
        UserId = source.UserId;
        PaymentTransactionId = source.PaymentTransactionId;
        PaymentAfterAccount = source.PaymentAfterAccount;
        PendingDisclaimers = source.PendingDisclaimers;
        PlanActivationPending = source.PlanActivationPending;
        TokenValue = source.TokenValue;
        TokenGrant = source.TokenGrant;
        TokenChecking = source.TokenChecking;
        TokenError = source.TokenError;
        PacksToBuy = source.PacksToBuy;
        AlreadySignedInLatched = source.AlreadySignedInLatched;
        Initialized = source.Initialized;
        Outcome = source.Outcome;
        Error = source.Error;
        RetryFrom = source.RetryFrom;
    }

    public SignupStep Step { get; internal set; } = SignupStep.Loading;

    /// <summary>The async step in flight, or null. Results carrying another token are ignored.</summary>
    public string? Token { get; internal set; }

    public ResolvedSignupOptions Options { get; internal set; } = new ResolvedSignupOptions(
        SignupTokenMode.Auto,
        SignupPlanSelection.Choose,
        SignupPackSelection.Choose,
        SignupPaymentOrder.BeforeAccount);

    /// <summary>The registration mode, once the app's settings have answered.</summary>
    public SignupRegistrationMode.Result? Mode { get; internal set; }

    public bool ModeReady { get; internal set; }

    public bool CatalogReady { get; internal set; }

    public SignupCatalogNames Names { get; internal set; } = SignupCatalogNames.Empty;

    public SignupSelection Selection { get; internal set; } = SignupSelection.Empty;

    /// <summary>Whether the chosen plan has to be paid for.</summary>
    public bool PlanRequiresPayment { get; internal set; }

    /// <summary>
    /// The plan was decided before the form opened — a signup link's plan, or the app's default in
    /// a <see cref="SignupPlanSelection.Skip"/> flow — so the plan step is not walked. The visitor
    /// is shown what they are getting and a way back to the grid (a GO_TO to the plan step) rather
    /// than a grid they have already chosen from.
    /// </summary>
    public bool PlanPreset { get; internal set; }

    /// <summary>
    /// Whether the registration form has actually been submitted.
    /// </summary>
    /// <remarks>
    /// Everything after the form spends the visitor's money or creates their account, and both need
    /// the details the form collects. "Change plan" can send them to the grid before they have
    /// typed anything, so a plan or a pack chosen while this is false takes them back to the form
    /// rather than onward — nothing may run ahead of it.
    /// </remarks>
    public bool FormSubmitted { get; internal set; }

    public string Email { get; internal set; } = string.Empty;

    public string UserId { get; internal set; } = string.Empty;

    public string? PaymentTransactionId { get; internal set; }

    /// <summary>
    /// The payment step is the ACCOUNT-FIRST one: the account already exists, so completing it
    /// carries on to the disclaimers rather than to <see cref="SignupStep.Creating"/>, and
    /// abandoning it is allowed. Always false in the pay-first order.
    /// </summary>
    public bool PaymentAfterAccount { get; internal set; }

    /// <summary>
    /// What ACCOUNT_CREATED said about disclaimers, remembered across an account-first payment step
    /// so the machine still knows where to go once the card is done with.
    /// </summary>
    public bool PendingDisclaimers { get; internal set; }

    /// <summary>
    /// The account was created but its plan was not paid for: the customer walked away from the
    /// card. Cleared again if they go back to the card and it goes through.
    /// </summary>
    public bool PlanActivationPending { get; internal set; }

    /// <summary>The registration token as typed, once validated.</summary>
    public string? TokenValue { get; internal set; }

    public SignupTokenGrant? TokenGrant { get; internal set; }

    /// <summary>A token validation is in flight.</summary>
    public bool TokenChecking { get; internal set; }

    /// <summary>A rejected token: a form-level message, not a failed flow.</summary>
    public string? TokenError { get; internal set; }

    /// <summary>
    /// Packs still to buy after login — what was chosen, minus anything the grant already covers.
    /// </summary>
    public IReadOnlyList<string> PacksToBuy { get; internal set; } = Array.Empty<string>();

    /// <summary>
    /// Whether the visitor was ALREADY signed in when the flow started. Latched at the first INIT.
    /// </summary>
    public bool AlreadySignedInLatched { get; internal set; }

    public bool Initialized { get; internal set; }

    public SignupOutcome? Outcome { get; internal set; }

    public string? Error { get; internal set; }

    /// <summary>Which step a RETRY goes back to.</summary>
    public SignupStep? RetryFrom { get; internal set; }

    /// <summary>A field-for-field copy the reducer writes its changes onto.</summary>
    internal SignupState Copy()
    {
        return new SignupState(this);
    }
}
