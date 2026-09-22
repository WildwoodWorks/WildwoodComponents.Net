using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.RegistrationSubscription;

/// <summary>
/// Everything that can happen to a plan change. The TypeScript discriminated union
/// (<c>PlanChangeEvent</c> in
/// packages/wildwood-react-shared/src/registrationSubscription/planChangeMachine.ts) becomes an
/// abstract base with sealed nested cases, each naming its TS member.
/// </summary>
public abstract class PlanChangeEvent
{
    private PlanChangeEvent()
    {
    }

    /// <summary>TS <c>PREVIEW_REQUESTED</c>.</summary>
    public sealed class PreviewRequested : PlanChangeEvent
    {
        public PreviewRequested(string appId, string tierId, string? pricingId = null, bool? immediate = null)
        {
            AppId = appId;
            TierId = tierId;
            PricingId = pricingId;
            Immediate = immediate;
        }

        public string AppId { get; }

        public string TierId { get; }

        public string? PricingId { get; }

        /// <summary>Null keeps the timing the state already has.</summary>
        public bool? Immediate { get; }
    }

    /// <summary>TS <c>PREVIEW_RECEIVED</c>.</summary>
    public sealed class PreviewReceived : PlanChangeEvent
    {
        public PreviewReceived(string token, TierChangePreviewModel? preview)
        {
            Token = token;
            Preview = preview;
        }

        public string Token { get; }

        public TierChangePreviewModel? Preview { get; }
    }

    /// <summary>TS <c>PREVIEW_FAILED</c>.</summary>
    public sealed class PreviewFailed : PlanChangeEvent
    {
        public PreviewFailed(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>
    /// TS <c>CONFIRMED</c>. The customer confirmed. <see cref="CollectPayment"/> overrides what the
    /// preview implies, and <see cref="Immediate"/> carries the timing they chose (a downgrade may
    /// be scheduled for the end of the period).
    /// </summary>
    public sealed class Confirmed : PlanChangeEvent
    {
        public Confirmed(bool? collectPayment = null, bool? immediate = null)
        {
            CollectPayment = collectPayment;
            Immediate = immediate;
        }

        public bool? CollectPayment { get; }

        public bool? Immediate { get; }
    }

    /// <summary>TS <c>PAYMENT_COMPLETED</c>.</summary>
    public sealed class PaymentCompleted : PlanChangeEvent
    {
        public PaymentCompleted(string paymentTransactionId)
        {
            PaymentTransactionId = paymentTransactionId;
        }

        public string PaymentTransactionId { get; }
    }

    /// <summary>TS <c>PAYMENT_FAILED</c>.</summary>
    public sealed class PaymentFailed : PlanChangeEvent
    {
        public PaymentFailed(string message)
        {
            Message = message;
        }

        public string Message { get; }
    }

    /// <summary>TS <c>PAYMENT_CANCELLED</c>.</summary>
    public sealed class PaymentCancelled : PlanChangeEvent
    {
    }

    /// <summary>TS <c>CHANGE_RESULT</c>.</summary>
    public sealed class ChangeResult : PlanChangeEvent
    {
        public ChangeResult(string token, AppTierChangeResultModel result)
        {
            Token = token;
            Result = result;
        }

        public string Token { get; }

        public AppTierChangeResultModel Result { get; }
    }

    /// <summary>TS <c>CHANGE_FAILED</c>.</summary>
    public sealed class ChangeFailed : PlanChangeEvent
    {
        public ChangeFailed(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>
    /// TS <c>AUTHENTICATED</c>. The prorated charge was authenticated in the browser/app.
    /// </summary>
    public sealed class Authenticated : PlanChangeEvent
    {
        public Authenticated(string token)
        {
            Token = token;
        }

        public string Token { get; }
    }

    /// <summary>TS <c>AUTH_FAILED</c>.</summary>
    public sealed class AuthFailed : PlanChangeEvent
    {
        public AuthFailed(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>TS <c>COMPLETE_RESULT</c>.</summary>
    public sealed class CompleteResult : PlanChangeEvent
    {
        public CompleteResult(string token, AppTierChangeResultModel result)
        {
            Token = token;
            Result = result;
        }

        public string Token { get; }

        public AppTierChangeResultModel Result { get; }
    }

    /// <summary>TS <c>COMPLETE_FAILED</c>.</summary>
    public sealed class CompleteFailed : PlanChangeEvent
    {
        public CompleteFailed(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>TS <c>RETRY</c>.</summary>
    public sealed class Retry : PlanChangeEvent
    {
    }

    /// <summary>TS <c>RESET</c>.</summary>
    public sealed class Reset : PlanChangeEvent
    {
    }
}
