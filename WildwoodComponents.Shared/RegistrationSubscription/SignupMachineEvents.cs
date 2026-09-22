using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Shared.RegistrationSubscription;

/// <summary>
/// Everything that can happen to a signup flow. The TypeScript discriminated union
/// (<c>SignupEvent</c> in
/// packages/wildwood-react-shared/src/registrationSubscription/signupMachine.ts) becomes an
/// abstract base with sealed nested cases; the base constructor is private, so the set is closed
/// exactly as the union is. Each case names its TS member so the two can be diffed.
/// </summary>
public abstract class SignupEvent
{
    private SignupEvent()
    {
    }

    /// <summary>TS <c>INIT</c>. The flow started. Only the FIRST one latches "already signed in".</summary>
    public sealed class Init : SignupEvent
    {
        public Init(bool signedIn)
        {
            SignedIn = signedIn;
        }

        public bool SignedIn { get; }
    }

    /// <summary>TS <c>MODE_LOADED</c>.</summary>
    public sealed class ModeLoaded : SignupEvent
    {
        public ModeLoaded(SignupRegistrationMode.Result mode)
        {
            Mode = mode;
        }

        public SignupRegistrationMode.Result Mode { get; }
    }

    /// <summary>TS <c>CATALOG_LOADED</c>.</summary>
    public sealed class CatalogLoaded : SignupEvent
    {
        public CatalogLoaded(SignupCatalogNames? names = null)
        {
            Names = names;
        }

        /// <summary>Null keeps whatever names the state already holds.</summary>
        public SignupCatalogNames? Names { get; }
    }

    /// <summary>
    /// TS <c>SELECTION_RESOLVED</c>. The live catalog decided what the signup starts with: the plan
    /// a signup link preselected, or the app's default plan in a skip flow, plus the packs the link
    /// asked for — all checked against the catalog by whoever dispatches this. A resolved plan takes
    /// the plan step out of the flow. Accepted only before the form is submitted.
    /// </summary>
    public sealed class SelectionResolved : SignupEvent
    {
        public SelectionResolved(
            string? tierId = null,
            string? pricingId = null,
            IReadOnlyList<string>? addOnIds = null,
            bool? requiresPayment = null)
        {
            TierId = tierId;
            PricingId = pricingId;
            AddOnIds = addOnIds;
            RequiresPayment = requiresPayment;
        }

        public string? TierId { get; }

        public string? PricingId { get; }

        public IReadOnlyList<string>? AddOnIds { get; }

        public bool? RequiresPayment { get; }
    }

    /// <summary>TS <c>LOAD_FAILED</c>.</summary>
    public sealed class LoadFailed : SignupEvent
    {
        public LoadFailed(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    /// <summary>TS <c>REGISTER_SUBMITTED</c>.</summary>
    public sealed class RegisterSubmitted : SignupEvent
    {
        public RegisterSubmitted(string email)
        {
            Email = email;
        }

        public string Email { get; }
    }

    /// <summary>TS <c>TOKEN_CHECK_STARTED</c>.</summary>
    public sealed class TokenCheckStarted : SignupEvent
    {
    }

    /// <summary>TS <c>TOKEN_ACCEPTED</c>.</summary>
    public sealed class TokenAccepted : SignupEvent
    {
        public TokenAccepted(string token, string value, SignupTokenGrant? grant = null)
        {
            Token = token;
            Value = value;
            Grant = grant;
        }

        /// <summary>The step token the validation was started with.</summary>
        public string Token { get; }

        /// <summary>The registration token as typed.</summary>
        public string Value { get; }

        public SignupTokenGrant? Grant { get; }
    }

    /// <summary>TS <c>TOKEN_REJECTED</c>.</summary>
    public sealed class TokenRejected : SignupEvent
    {
        public TokenRejected(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>TS <c>TOKEN_SKIPPED</c>.</summary>
    public sealed class TokenSkipped : SignupEvent
    {
    }

    /// <summary>TS <c>PLAN_CHOSEN</c>.</summary>
    public sealed class PlanChosen : SignupEvent
    {
        public PlanChosen(string? tierId = null, string? pricingId = null, bool? requiresPayment = null)
        {
            TierId = tierId;
            PricingId = pricingId;
            RequiresPayment = requiresPayment;
        }

        public string? TierId { get; }

        public string? PricingId { get; }

        public bool? RequiresPayment { get; }
    }

    /// <summary>TS <c>PACKS_CHOSEN</c>.</summary>
    public sealed class PacksChosen : SignupEvent
    {
        public PacksChosen(IReadOnlyList<string> addOnIds)
        {
            AddOnIds = addOnIds;
        }

        public IReadOnlyList<string> AddOnIds { get; }
    }

    /// <summary>TS <c>PAYMENT_COMPLETED</c>.</summary>
    public sealed class PaymentCompleted : SignupEvent
    {
        public PaymentCompleted(string paymentTransactionId)
        {
            PaymentTransactionId = paymentTransactionId;
        }

        public string PaymentTransactionId { get; }
    }

    /// <summary>
    /// TS <c>PAYMENT_ABANDONED</c>. The customer walked away from the plan's card. Only ever valid
    /// on the ACCOUNT-FIRST payment step: there the account already exists, so the signup finishes
    /// with the plan unactivated rather than throwing away a registration that succeeded. Ignored
    /// in the pay-first order, where abandoning the card is simply a step back.
    /// </summary>
    public sealed class PaymentAbandoned : SignupEvent
    {
    }

    /// <summary>TS <c>ACCOUNT_CREATED</c>.</summary>
    public sealed class AccountCreated : SignupEvent
    {
        public AccountCreated(string token, string userId, bool? requiresDisclaimers = null)
        {
            Token = token;
            UserId = userId;
            RequiresDisclaimers = requiresDisclaimers;
        }

        public string Token { get; }

        public string UserId { get; }

        /// <summary>
        /// Null means "yes": the plan's order puts disclaimers after account creation, and a host
        /// that knows there are none passes false rather than rendering an empty step.
        /// </summary>
        public bool? RequiresDisclaimers { get; }
    }

    /// <summary>TS <c>ACCOUNT_FAILED</c>.</summary>
    public sealed class AccountFailed : SignupEvent
    {
        public AccountFailed(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>TS <c>DISCLAIMERS_ACCEPTED</c>.</summary>
    public sealed class DisclaimersAccepted : SignupEvent
    {
    }

    /// <summary>TS <c>PACK_CHECKOUT_FINISHED</c>.</summary>
    public sealed class PackCheckoutFinished : SignupEvent
    {
        public PackCheckoutFinished(string token, IReadOnlyList<SignupPackOutcome> packs)
        {
            Token = token;
            Packs = packs;
        }

        public string Token { get; }

        public IReadOnlyList<SignupPackOutcome> Packs { get; }
    }

    /// <summary>TS <c>PACK_CHECKOUT_FAILED</c>.</summary>
    public sealed class PackCheckoutFailed : SignupEvent
    {
        public PackCheckoutFailed(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>
    /// TS <c>GO_TO</c>. Back-navigation from a view. Only the form steps are reachable this way —
    /// TypeScript says so in the type (<c>Extract&lt;SignupStep, 'register' | 'token' | 'plan' |
    /// 'packs' | 'payment'&gt;</c>), which C# cannot express on an enum, so the reducer ignores any
    /// other step instead.
    /// </summary>
    public sealed class GoTo : SignupEvent
    {
        public GoTo(SignupStep step)
        {
            Step = step;
        }

        public SignupStep Step { get; }
    }

    /// <summary>TS <c>RETRY</c>.</summary>
    public sealed class Retry : SignupEvent
    {
    }

    /// <summary>TS <c>RESET</c>.</summary>
    public sealed class Reset : SignupEvent
    {
    }
}
