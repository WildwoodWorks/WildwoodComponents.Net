using System;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Which plan switches a subscriber's current tier allows. Port of <c>isTierChangeAllowed</c> in
/// packages/wildwood-core/src/features/tierUtils.ts, so Blazor, Razor and WebForms share one copy.
/// </summary>
public static class TierChangeRules
{
    /// <summary>
    /// Whether a subscriber on <paramref name="current"/> may move to <paramref name="target"/>, by
    /// the same rule the server applies in <c>ChangeTierAsync</c>: a move to a higher
    /// <see cref="AppTierModel.DisplayOrder"/> is an upgrade and needs the CURRENT tier's
    /// <see cref="AppTierModel.AllowUpgrades"/>; any other move is a downgrade and needs its
    /// <see cref="AppTierModel.AllowDowngrades"/>. The server applies it to admins too, so a grid
    /// that offers a switch this refuses offers a button that can only fail.
    /// </summary>
    /// <remarks>No current tier (a first subscription) is always allowed, and so is staying put.</remarks>
    public static bool IsTierChangeAllowed(AppTierModel? current, AppTierModel target)
    {
        if (target is null) throw new ArgumentNullException(nameof(target));
        if (current is null) return true;
        if (string.Equals(current.Id, target.Id, StringComparison.OrdinalIgnoreCase)) return true;

        var isUpgrade = target.DisplayOrder > current.DisplayOrder;
        return isUpgrade ? current.AllowUpgrades : current.AllowDowngrades;
    }

    /// <summary>The marker shown in place of a switch the current plan does not allow.</summary>
    public const string UnavailableText = "Not available on your plan";
}
