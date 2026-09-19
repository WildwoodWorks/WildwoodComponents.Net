namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// One rule for what "subscribed" means, ported from
/// packages/wildwood-react-shared/src/subscription/statusDisplay.ts so every panel agrees.
/// </summary>
public static class SubscriptionAccess
{
    /// <summary>
    /// Statuses in which a subscription still grants what it pays for. A row scheduled to cancel
    /// keeps access to the end of the period, so it counts; a Cancelled or Expired row grants
    /// nothing and the plan or pack behind it is on offer again.
    /// </summary>
    public static readonly IReadOnlyList<string> AccessGrantingStatuses =
        new[] { "Active", "Trialing", "PendingCancellation" };

    /// <summary>
    /// True when the status is one of <see cref="AccessGrantingStatuses"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT <c>UserTierSubscriptionModel.IsActive</c>, which counts PastDue and not
    /// PendingCancellation: that one answers "is this subscription healthy", this one answers "does
    /// it still grant access". The comparison is ordinal, as the JS <c>includes</c> check is — the
    /// server sends these statuses in exactly this casing.
    /// </remarks>
    public static bool GrantsAccess(string? status)
    {
        // Pattern form narrows on netstandard2.0, whose string.IsNullOrEmpty is unannotated.
        if (status is not { Length: > 0 }) return false;

        foreach (var granting in AccessGrantingStatuses)
        {
            if (string.Equals(status, granting, StringComparison.Ordinal)) return true;
        }

        return false;
    }
}
