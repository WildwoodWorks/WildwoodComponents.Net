using WildwoodComponents.Blazor.Components.Authentication;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The Blazor half of the AuthenticationComponent's registration gate, mirroring
/// @wildwood/react-shared's <c>resolveRegistrationAccess</c> and its React Native test
/// (<c>registrationAccess.test.ts</c>). The gate is exercised as the pure function the component
/// calls: there is no bUnit in this project, so the rendered behaviour is not covered here.
/// </summary>
public class RegistrationAccessTests
{
    [Fact]
    public void Follows_the_configuration_when_the_override_is_null()
    {
        Assert.True(RegistrationAccess.Resolve(null, true, true).ShowRegistration);
        Assert.False(RegistrationAccess.Resolve(null, false, true).ShowRegistration);
    }

    [Fact]
    public void A_false_override_denies_registration_the_configuration_allows()
    {
        Assert.False(RegistrationAccess.Resolve(false, true, true).ShowRegistration);
    }

    [Fact]
    public void A_true_override_allows_registration_the_configuration_denies()
    {
        Assert.True(RegistrationAccess.Resolve(true, false, true).ShowRegistration);
    }

    [Fact]
    public void Collapses_the_register_view_to_login_when_registration_is_hidden()
    {
        // isLoginView: false is the register view. Both a false override and a denying
        // configuration have to pull it back to login.
        Assert.True(RegistrationAccess.Resolve(false, true, false).IsLoginView);
        Assert.True(RegistrationAccess.Resolve(null, false, false).IsLoginView);
    }

    [Fact]
    public void Leaves_the_register_view_alone_when_registration_is_shown()
    {
        Assert.False(RegistrationAccess.Resolve(true, false, false).IsLoginView);
        Assert.False(RegistrationAccess.Resolve(null, true, false).IsLoginView);
    }

    [Fact]
    public void The_login_view_passes_through_whatever_the_gate_says()
    {
        Assert.True(RegistrationAccess.Resolve(false, false, true).IsLoginView);
        Assert.True(RegistrationAccess.Resolve(true, true, true).IsLoginView);
    }
}
