using System;

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

    /// <summary>True when <paramref name="reason"/> is one of the six.</summary>
    public static bool IsKnown(string? reason)
    {
        // Pattern form narrows on netstandard2.0, whose string.IsNullOrEmpty is unannotated.
        if (reason is not { Length: > 0 }) return false;

        return reason == Signup
            || reason == TierChange
            || reason == AddOn
            || reason == Cancel
            || reason == Reactivate
            || reason == Manual;
    }
}

/// <summary>
/// The payload of the JS <c>entitlementsChanged</c> event, as .NET hands it to a listener: which
/// app changed and why. A signal to re-read the subscription and feature map, not proof that the
/// change has propagated.
/// </summary>
public class EntitlementsChangedEventArgs : EventArgs
{
    public EntitlementsChangedEventArgs(string? appId, string reason)
    {
        AppId = appId ?? string.Empty;
        Reason = reason;
    }

    /// <summary>
    /// The app whose entitlements changed. Empty when the caller named none — a cache-wide
    /// invalidation such as a sign-in or sign-out, where every app is affected.
    /// </summary>
    public string AppId { get; }

    /// <summary>One of <see cref="EntitlementsChangedReasons"/>.</summary>
    public string Reason { get; }
}
