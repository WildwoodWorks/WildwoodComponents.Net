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

    /// <summary>
    /// True when a subscription's trial is still running, which is the only time a trial end date
    /// is worth showing. Ported from
    /// packages/wildwood-react/src/components/subscription/admin/SubscriptionStatusPanel.tsx
    /// (JS 1eefaa1).
    /// </summary>
    /// <remarks>
    /// The server keeps a finished trial's end date on the row — that is how it records that the
    /// account has already had its trial for the app. Showing it unconditionally put "Trial Ends"
    /// with a future-looking date on an Active, paid plan after a repeat upgrade was charged. Two
    /// conditions rule that out: the plan must not be Active (a trial reads Trialing), and the date
    /// must still be ahead of <paramref name="asOf"/>.
    /// </remarks>
    /// <param name="status">The subscription's raw status, as the server sent it.</param>
    /// <param name="trialEndDate">The row's trial end date, if any.</param>
    /// <param name="asOf">
    /// "Now", usually <c>DateTime.Now</c>. It is converted to UTC when the trial end carries a UTC
    /// kind, so a server date serialised with a Z suffix is not compared against a local clock.
    /// </param>
    public static bool IsTrialRunning(string? status, DateTime? trialEndDate, DateTime asOf)
    {
        if (trialEndDate is not { } trialEnd) return false;

        // Ordinal, like the JS `status !== 'Active'` comparison: the server sends this casing.
        if (string.Equals(status, "Active", StringComparison.Ordinal)) return false;

        var now = trialEnd.Kind == DateTimeKind.Utc ? asOf.ToUniversalTime() : asOf;
        return trialEnd > now;
    }
}
