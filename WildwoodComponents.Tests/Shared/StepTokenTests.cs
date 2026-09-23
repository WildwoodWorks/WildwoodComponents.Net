using WildwoodComponents.Shared.RegistrationSubscription;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The step-token contract the three machines rely on. JS has no test file of its own for
/// stepTokens.ts — its rules are asserted through the machine tests — so these four pin the parts
/// the C# port had to add: an injectable issuer (C# tests run in parallel and would otherwise share
/// one counter) and the "empty is not a token" reading of JS's <c>Boolean(stateToken)</c>.
/// </summary>
public class StepTokenTests
{
    [Fact]
    public void Issue_IsMonotonicAndNeverReusedWithinAnIssuer()
    {
        var issuer = new StepTokenIssuer();

        Assert.Equal("step-1", issuer.Issue());
        Assert.Equal("step-2", issuer.Issue());
        Assert.Equal("step-3", issuer.Issue());
    }

    [Fact]
    public void Default_IssuesDistinctTokens()
    {
        var first = StepToken.Issue();
        var second = StepToken.Issue();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void IsCurrentStep_MatchesOnlyTheTokenTheStateIsWaitingOn()
    {
        Assert.True(StepToken.IsCurrentStep("step-7", "step-7"));
        Assert.False(StepToken.IsCurrentStep("step-7", "step-8"));
    }

    [Fact]
    public void IsCurrentStep_NullOrEmptyNeverMatches()
    {
        Assert.False(StepToken.IsCurrentStep(null, null));
        Assert.False(StepToken.IsCurrentStep(null, "step-1"));
        Assert.False(StepToken.IsCurrentStep("step-1", null));
        Assert.False(StepToken.IsCurrentStep(string.Empty, string.Empty));
    }
}
