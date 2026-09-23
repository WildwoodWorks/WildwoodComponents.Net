using WildwoodComponents.Blazor.Components.Registration;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// What a registration token's plan means for the signup wizard (JS f8b095f). Registering with a
/// token that carries a plan already subscribes the user; the wizard used to self-subscribe
/// afterwards anyway, which REPLACED that subscription and cancelled the plan the token had just
/// created.
/// </summary>
public class SignupPlanDecisionsTests
{
    private static RegistrationTokenDetails DetailsFor(string appId) => new RegistrationTokenDetails
    {
        IsValid = true,
        AppGrants = new List<RegistrationTokenAppGrant>
        {
            new RegistrationTokenAppGrant
            {
                AppId = appId,
                AppTierName = "Professional",
                AppTierId = "tier-1",
                PricingName = "Annual"
            }
        }
    };

    [Fact]
    public void FindGrantForApp_matchesTheAppIdCaseInsensitively()
    {
        var grant = SignupPlanDecisions.FindGrantForApp(
            DetailsFor("A1B2C3D4-0000-0000-0000-000000000001"),
            "a1b2c3d4-0000-0000-0000-000000000001");

        Assert.NotNull(grant);
        Assert.Equal("Professional", grant!.AppTierName);
    }

    [Fact]
    public void FindGrantForApp_ignoresAGrantForAnotherApp()
    {
        Assert.Null(SignupPlanDecisions.FindGrantForApp(DetailsFor("app-other"), "app-1"));
    }

    /// <summary>
    /// "Details unreadable" is not "no plan and not a valid token": a server that predates the
    /// detailed route, or a transport failure, must fall through to the normal flow, which the
    /// token still authorises.
    /// </summary>
    [Fact]
    public void FindGrantForApp_returnsNothingWhenTheDetailsCouldNotBeRead()
    {
        Assert.Null(SignupPlanDecisions.FindGrantForApp(null, "app-1"));
    }

    [Fact]
    public void FindGrantForApp_returnsNothingForATokenThatOnlyGrantsAppAccess()
    {
        var details = new RegistrationTokenDetails { IsValid = true };

        Assert.Null(SignupPlanDecisions.FindGrantForApp(details, "app-1"));
    }

    [Fact]
    public void FindGrantForApp_returnsNothingWithoutAnAppId()
    {
        Assert.Null(SignupPlanDecisions.FindGrantForApp(DetailsFor("app-1"), null));
        Assert.Null(SignupPlanDecisions.FindGrantForApp(DetailsFor("app-1"), string.Empty));
    }

    /// <summary>The live bug: this call cancelled the subscription the token had just created.</summary>
    [Fact]
    public void ShouldSelfSubscribe_isFalseWhenTheTokenAlreadyPutTheAccountOnAPlan()
    {
        var grant = SignupPlanDecisions.FindGrantForApp(DetailsFor("app-1"), "app-1");

        Assert.False(SignupPlanDecisions.ShouldSelfSubscribe(grant, "tier-chosen"));
        Assert.False(SignupPlanDecisions.ShouldSelfSubscribe(grant, null));
    }

    [Fact]
    public void ShouldSelfSubscribe_isTrueForAPlanTheVisitorChose()
    {
        Assert.True(SignupPlanDecisions.ShouldSelfSubscribe(null, "tier-chosen"));
    }

    [Fact]
    public void ShouldSelfSubscribe_isFalseWhenNoPlanWasChosen()
    {
        Assert.False(SignupPlanDecisions.ShouldSelfSubscribe(null, null));
        Assert.False(SignupPlanDecisions.ShouldSelfSubscribe(null, string.Empty));
    }

    [Fact]
    public void GrantPlanName_usesTheServersName_thenTheId_thenANeutralWord()
    {
        Assert.Equal("Professional", SignupPlanDecisions.GrantPlanName(
            new RegistrationTokenAppGrant { AppTierName = "Professional", AppTierId = "tier-1" }));
        Assert.Equal("tier-1", SignupPlanDecisions.GrantPlanName(
            new RegistrationTokenAppGrant { AppTierId = "tier-1" }));
        Assert.Equal("plan", SignupPlanDecisions.GrantPlanName(new RegistrationTokenAppGrant()));
        Assert.Equal("plan", SignupPlanDecisions.GrantPlanName(null));
    }

    [Fact]
    public void DisplayNames_namesEachGrantedIdTheWayTheServerNamedIt()
    {
        var names = SignupPlanDecisions.DisplayNames(
            new List<string> { "pack-1", "pack-2" },
            new List<string> { "Extra Seats", "Priority Support" });

        Assert.Equal(new[] { "Extra Seats", "Priority Support" }, names);
    }

    [Fact]
    public void DisplayNames_fallsBackToTheIdWhenTheServerSentNoName()
    {
        Assert.Equal(
            new[] { "pack-1", "pack-2" },
            SignupPlanDecisions.DisplayNames(new List<string> { "pack-1", "pack-2" }, null));

        Assert.Equal(
            new[] { "Extra Seats", "pack-2" },
            SignupPlanDecisions.DisplayNames(
                new List<string> { "pack-1", "pack-2" },
                new List<string> { "Extra Seats", string.Empty }));
    }

    [Fact]
    public void DisplayNames_isEmptyWhenNothingWasGranted()
    {
        Assert.Empty(SignupPlanDecisions.DisplayNames(null, null));
        Assert.Empty(SignupPlanDecisions.DisplayNames(new List<string>(), new List<string> { "unused" }));
    }
}
