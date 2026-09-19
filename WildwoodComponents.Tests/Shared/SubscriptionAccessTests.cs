using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The access-granting status rule and the running-trial rule, both shared so every panel in every
/// .NET stack answers the same way. Ported from JS 1eefaa1 and
/// packages/wildwood-react-shared/src/subscription/statusDisplay.ts.
/// </summary>
public class SubscriptionAccessTests
{
    [Theory]
    [InlineData("Active", true)]
    [InlineData("Trialing", true)]
    [InlineData("PendingCancellation", true)]
    [InlineData("Cancelled", false)]
    [InlineData("Expired", false)]
    [InlineData("PastDue", false)]
    [InlineData("active", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void GrantsAccess_countsOnlyTheStatusesThatStillGiveWhatWasPaidFor(string? status, bool expected)
    {
        Assert.Equal(expected, SubscriptionAccess.GrantsAccess(status));
    }

    private static readonly DateTime Now = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Unspecified);

    [Fact]
    public void IsTrialRunning_isFalseWithoutATrialEndDate()
    {
        Assert.False(SubscriptionAccess.IsTrialRunning("Trialing", null, Now));
    }

    [Fact]
    public void IsTrialRunning_isTrueWhileTheTrialIsStillRunning()
    {
        Assert.True(SubscriptionAccess.IsTrialRunning("Trialing", Now.AddDays(7), Now));
    }

    /// <summary>
    /// The live bug: the server keeps a finished trial's end date on the row, so a repeat upgrade
    /// that was CHARGED showed "Trial Ends" with a future-looking date on an Active, paid plan.
    /// </summary>
    [Fact]
    public void IsTrialRunning_isFalseOnAnActivePaidPlan_evenWithAFutureTrialEnd()
    {
        Assert.False(SubscriptionAccess.IsTrialRunning("Active", Now.AddDays(7), Now));
    }

    [Fact]
    public void IsTrialRunning_isFalseOnceTheDateHasPassed()
    {
        Assert.False(SubscriptionAccess.IsTrialRunning("Trialing", Now.AddDays(-1), Now));
    }

    [Fact]
    public void IsTrialRunning_isFalseAtTheExactMomentTheTrialEnds()
    {
        Assert.False(SubscriptionAccess.IsTrialRunning("Trialing", Now, Now));
    }

    /// <summary>
    /// A server date serialised with a Z suffix deserialises as UTC. Comparing it against a local
    /// clock would shift the answer by the host's offset, so "now" is converted to match.
    /// </summary>
    [Fact]
    public void IsTrialRunning_comparesAUtcTrialEndAgainstUtcNow()
    {
        var utcTrialEnd = DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(30), DateTimeKind.Utc);

        Assert.True(SubscriptionAccess.IsTrialRunning("Trialing", utcTrialEnd, DateTime.Now));
        Assert.False(SubscriptionAccess.IsTrialRunning(
            "Trialing", DateTime.SpecifyKind(DateTime.UtcNow.AddMinutes(-30), DateTimeKind.Utc), DateTime.Now));
    }
}
