using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.RegistrationSubscription;

// The pack-checkout flow's state, ported from
// packages/wildwood-react-shared/src/registrationSubscription/packCheckoutMachine.ts.
//
// Same immutability idiom as SignupState: a reference type with getter-only public properties, a
// hand-written Copy() the reducer writes onto, and the SAME instance back for an ignored event.

/// <summary>Where a pack checkout is.</summary>
public enum PackCheckoutStep
{
    Idle = 0,
    Quoting = 1,
    Quoted = 2,
    CollectingCard = 3,
    CheckingOut = 4,
    Authenticating = 5,
    Completing = 6,
    Done = 7,
    Failed = 8
}

/// <summary>What a pack checkout starts with.</summary>
public sealed class PackCheckoutMachineOptions
{
    public string? AppId { get; set; }

    public IReadOnlyList<AddOnCheckoutItemInput>? Items { get; set; }
}

/// <summary>
/// Buying any number of packs against one card. Immutable to callers: only
/// <see cref="PackCheckoutMachine"/> writes it, and only onto a fresh <see cref="Copy"/>.
/// </summary>
public sealed class PackCheckoutState
{
    internal PackCheckoutState()
    {
    }

    private PackCheckoutState(PackCheckoutState source)
    {
        Step = source.Step;
        Token = source.Token;
        AppId = source.AppId;
        Items = source.Items;
        Quote = source.Quote;
        CardClientSecret = source.CardClientSecret;
        PaymentTransactionId = source.PaymentTransactionId;
        UseSavedCard = source.UseSavedCard;
        Results = source.Results;
        PendingIndexes = source.PendingIndexes;
        PendingPosition = source.PendingPosition;
        Error = source.Error;
        ErrorCode = source.ErrorCode;
        RetryFrom = source.RetryFrom;
    }

    public PackCheckoutStep Step { get; internal set; } = PackCheckoutStep.Idle;

    /// <summary>The async step in flight, or null. Results carrying another token are ignored.</summary>
    public string? Token { get; internal set; }

    public string AppId { get; internal set; } = string.Empty;

    public IReadOnlyList<AddOnCheckoutItemInput> Items { get; internal set; } =
        Array.Empty<AddOnCheckoutItemInput>();

    public AddOnCheckoutQuoteModel? Quote { get; internal set; }

    /// <summary>The SetupIntent secret the card form confirms, while a card is being collected.</summary>
    public string? CardClientSecret { get; internal set; }

    /// <summary>The card, once collected — handed to the purchase so it reads the saved card off it.</summary>
    public string? PaymentTransactionId { get; internal set; }

    /// <summary>Whether the purchase should charge the card already on file.</summary>
    public bool UseSavedCard { get; internal set; }

    /// <summary>
    /// One entry per requested pack, updated in place as each one is authenticated and completed.
    /// </summary>
    public IReadOnlyList<AddOnCheckoutItemResultModel> Results { get; internal set; } =
        Array.Empty<AddOnCheckoutItemResultModel>();

    /// <summary>The indexes into <see cref="Results"/> still needing 3-D Secure, in walk order.</summary>
    public IReadOnlyList<int> PendingIndexes { get; internal set; } = Array.Empty<int>();

    /// <summary>How far along <see cref="PendingIndexes"/> the walk is.</summary>
    public int PendingPosition { get; internal set; }

    public string? Error { get; internal set; }

    public string? ErrorCode { get; internal set; }

    /// <summary>Which step a RETRY goes back to.</summary>
    public PackCheckoutStep? RetryFrom { get; internal set; }

    /// <summary>A field-for-field copy the reducer writes its changes onto.</summary>
    internal PackCheckoutState Copy()
    {
        return new PackCheckoutState(this);
    }
}
