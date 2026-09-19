namespace WildwoodComponents.Shared.Models;

/// <summary>
/// Why a user's entitlements changed — the vocabulary of the JS <c>entitlementsChanged</c> event
/// (<c>events/eventEmitter.ts</c>). A signal to re-read the subscription and feature map, not
/// proof that the change has propagated.
/// </summary>
public static class EntitlementsChangedReasons
{
    public const string Signup = "signup";
    public const string TierChange = "tierChange";
    public const string AddOn = "addOn";
    public const string Cancel = "cancel";
    public const string Reactivate = "reactivate";
    public const string Manual = "manual";
}
