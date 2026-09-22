using System.Reflection;
using WildwoodComponents.Testing;

namespace WildwoodComponents.Tests.Testing;

/// <summary>
/// The budgets the signup drivers spend, and the promise that passing none is the React behaviour.
/// </summary>
/// <remarks>
/// Every default below is a literal in <c>packages/wildwood-react/src/testing/flows.ts</c>, so a
/// driver whose timing is tuned in one stack and not the other fails here rather than diverging
/// quietly - and a spec that names no options gets the numbers the React helpers have been run
/// with. That each option reaches the call it names is pinned separately, in
/// <see cref="WildwoodSignupFlowsTests"/>, where the drivers can be watched spending them.
/// </remarks>
public class WildwoodTestingOptionsTests
{
    [Fact]
    public void The_defaults_are_the_JS_helpers_literals()
    {
        var options = new WildwoodTestingOptions();

        Assert.Equal(15_000, options.DisclaimerSettleMs);
        Assert.Equal(30_000, options.DisclaimerRenderTimeoutMs);
        Assert.Equal(5_000, options.DisclaimerGateTimeoutMs);
        Assert.Equal(10, options.AcceptAttempts);
        Assert.Equal(400, options.AcceptPauseMs);
        Assert.Equal(1_000, options.RetryPauseMs);
        Assert.Equal(3, options.RateLimitWaits);
        Assert.Equal(20_000, options.RateLimitPauseMs);
        Assert.Equal(120_000, options.ProcessingTimeoutMs);
        Assert.Equal(2, options.SignupRetries);
        Assert.Equal(30_000, options.LeaveFailedTimeoutMs);
        Assert.Equal(60_000, options.SuccessTimeoutMs);
        Assert.Equal(30_000, options.SuccessTextTimeoutMs);
        Assert.Equal(5_000, options.ConsentBannerWaitMs);
        Assert.Equal(10_000, options.ConsentBannerHiddenMs);
    }

    /// <summary>
    /// The acceptance pattern defaults to the DOM contract's, so both paths are read by default.
    /// </summary>
    /// <remarks>
    /// Naming the contract's own pattern rather than restating it here is the point: a host that
    /// proxies acceptance somewhere else overrides one property, and a stack that adds a new path
    /// adds it in one place.
    /// </remarks>
    [Fact]
    public void The_acceptance_pattern_defaults_to_the_contracts_own()
    {
        Assert.Same(
            WildwoodTestSelectors.AcceptResponsePattern,
            new WildwoodTestingOptions().AcceptResponsePattern);
    }

    /// <summary>
    /// There is no shared instance to edit by accident.
    /// </summary>
    /// <remarks>
    /// A static <c>Default</c> would be the obvious convenience and the wrong one: the type is
    /// mutable, so one spec raising its rate-limit budget would raise everyone's - and in a suite
    /// running its classes in parallel, unpredictably. Every driver that is passed no options
    /// constructs its own, which is only safe while no shared one exists.
    /// </remarks>
    [Fact]
    public void There_is_no_shared_mutable_default()
    {
        Assert.Empty(typeof(WildwoodTestingOptions)
            .GetProperties(BindingFlags.Public | BindingFlags.Static));
        Assert.Empty(typeof(WildwoodTestingOptions)
            .GetFields(BindingFlags.Public | BindingFlags.Static));

        var mine = new WildwoodTestingOptions { AcceptAttempts = 1 };

        Assert.Equal(10, new WildwoodTestingOptions().AcceptAttempts);
        Assert.Equal(1, mine.AcceptAttempts);
    }
}
