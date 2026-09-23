using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.RegistrationSubscription;

/// <summary>
/// Everything that can happen to a pack checkout. The TypeScript discriminated union
/// (<c>PackCheckoutEvent</c> in
/// packages/wildwood-react-shared/src/registrationSubscription/packCheckoutMachine.ts) becomes an
/// abstract base with sealed nested cases, each naming its TS member.
/// </summary>
public abstract class PackCheckoutEvent
{
    private PackCheckoutEvent()
    {
    }

    /// <summary>TS <c>QUOTE_REQUESTED</c>.</summary>
    public sealed class QuoteRequested : PackCheckoutEvent
    {
        public QuoteRequested(string appId, IReadOnlyList<AddOnCheckoutItemInput> items)
        {
            AppId = appId;
            Items = items;
        }

        public string AppId { get; }

        public IReadOnlyList<AddOnCheckoutItemInput> Items { get; }
    }

    /// <summary>TS <c>QUOTE_RECEIVED</c>.</summary>
    public sealed class QuoteReceived : PackCheckoutEvent
    {
        public QuoteReceived(string token, AddOnCheckoutQuoteModel? quote)
        {
            Token = token;
            Quote = quote;
        }

        public string Token { get; }

        public AddOnCheckoutQuoteModel? Quote { get; }
    }

    /// <summary>TS <c>QUOTE_FAILED</c>.</summary>
    public sealed class QuoteFailed : PackCheckoutEvent
    {
        public QuoteFailed(string token, string message, string? errorCode = null)
        {
            Token = token;
            Message = message;
            ErrorCode = errorCode;
        }

        public string Token { get; }

        public string Message { get; }

        public string? ErrorCode { get; }
    }

    /// <summary>TS <c>CARD_REQUESTED</c>. The quote needs a card: start collecting one.</summary>
    public sealed class CardRequested : PackCheckoutEvent
    {
    }

    /// <summary>TS <c>CARD_INTENT_RECEIVED</c>.</summary>
    public sealed class CardIntentReceived : PackCheckoutEvent
    {
        public CardIntentReceived(string token, string clientSecret, string paymentTransactionId)
        {
            Token = token;
            ClientSecret = clientSecret;
            PaymentTransactionId = paymentTransactionId;
        }

        public string Token { get; }

        /// <summary>The SetupIntent secret. Secret — never log it.</summary>
        public string ClientSecret { get; }

        public string PaymentTransactionId { get; }
    }

    /// <summary>TS <c>CARD_INTENT_FAILED</c>.</summary>
    public sealed class CardIntentFailed : PackCheckoutEvent
    {
        public CardIntentFailed(string token, string message, string? errorCode = null)
        {
            Token = token;
            Message = message;
            ErrorCode = errorCode;
        }

        public string Token { get; }

        public string Message { get; }

        public string? ErrorCode { get; }
    }

    /// <summary>TS <c>CARD_CONFIRMED</c>. The customer confirmed the SetupIntent in the card form.</summary>
    public sealed class CardConfirmed : PackCheckoutEvent
    {
        public CardConfirmed(string token, string? paymentTransactionId = null)
        {
            Token = token;
            PaymentTransactionId = paymentTransactionId;
        }

        public string Token { get; }

        public string? PaymentTransactionId { get; }
    }

    /// <summary>TS <c>CARD_FAILED</c>.</summary>
    public sealed class CardFailed : PackCheckoutEvent
    {
        public CardFailed(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>
    /// TS <c>CHECKOUT_REQUESTED</c>. Buy the basket with whatever card the flow has (saved, or the
    /// one just collected).
    /// </summary>
    public sealed class CheckoutRequested : PackCheckoutEvent
    {
    }

    /// <summary>TS <c>CHECKOUT_RECEIVED</c>.</summary>
    public sealed class CheckoutReceived : PackCheckoutEvent
    {
        public CheckoutReceived(string token, AddOnCheckoutResultModel? result)
        {
            Token = token;
            Result = result;
        }

        public string Token { get; }

        public AddOnCheckoutResultModel? Result { get; }
    }

    /// <summary>TS <c>CHECKOUT_FAILED</c>.</summary>
    public sealed class CheckoutFailed : PackCheckoutEvent
    {
        public CheckoutFailed(string token, string message, string? errorCode = null)
        {
            Token = token;
            Message = message;
            ErrorCode = errorCode;
        }

        public string Token { get; }

        public string Message { get; }

        public string? ErrorCode { get; }
    }

    /// <summary>
    /// TS <c>ITEM_AUTHENTICATED</c>. The current pack's 3-D Secure was confirmed in the browser/app.
    /// </summary>
    public sealed class ItemAuthenticated : PackCheckoutEvent
    {
        public ItemAuthenticated(string token)
        {
            Token = token;
        }

        public string Token { get; }
    }

    /// <summary>TS <c>ITEM_AUTH_FAILED</c>.</summary>
    public sealed class ItemAuthFailed : PackCheckoutEvent
    {
        public ItemAuthFailed(string token, string message)
        {
            Token = token;
            Message = message;
        }

        public string Token { get; }

        public string Message { get; }
    }

    /// <summary>TS <c>ITEM_COMPLETED</c>.</summary>
    public sealed class ItemCompleted : PackCheckoutEvent
    {
        public ItemCompleted(string token, AddOnCheckoutItemResultModel result)
        {
            Token = token;
            Result = result;
        }

        public string Token { get; }

        public AddOnCheckoutItemResultModel Result { get; }
    }

    /// <summary>TS <c>RETRY</c>.</summary>
    public sealed class Retry : PackCheckoutEvent
    {
    }

    /// <summary>TS <c>RESET</c>.</summary>
    public sealed class Reset : PackCheckoutEvent
    {
    }
}
