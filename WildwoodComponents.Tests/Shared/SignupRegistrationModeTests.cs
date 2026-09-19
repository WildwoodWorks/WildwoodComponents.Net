using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The registration-mode resolver that replaced the copy each host site carried. The four
/// configuration combinations, the unknown-config fallback, and the invite override.
/// Ported case for case from packages/wildwood-react/src/__tests__/registrationMode.test.ts.
/// </summary>
public class SignupRegistrationModeTests
{
    private static SignupRegistrationSettings Config(bool allowOpenRegistration, bool allowTokenRegistration)
    {
        return new SignupRegistrationSettings
        {
            AllowOpenRegistration = allowOpenRegistration,
            AllowTokenRegistration = allowTokenRegistration
        };
    }

    [Fact]
    public void Resolve_OpenPlusToken_OpenSignUpWithTheOptionalTokenEntry()
    {
        var expected = new SignupRegistrationMode.Result(
            closed: false,
            requireToken: false,
            allowOpenRegistration: true,
            showOptionalTokenEntry: true,
            source: SignupRegistrationModeSource.Config);

        Assert.Equal(expected, SignupRegistrationMode.Resolve(Config(true, true)));
    }

    [Fact]
    public void Resolve_OpenOnly_OpenSignUpNoTokenCard()
    {
        var expected = new SignupRegistrationMode.Result(
            closed: false,
            requireToken: false,
            allowOpenRegistration: true,
            showOptionalTokenEntry: false,
            source: SignupRegistrationModeSource.Config);

        Assert.Equal(expected, SignupRegistrationMode.Resolve(Config(true, false)));
    }

    [Fact]
    public void Resolve_TokenOnly_TheTokenIsRequired()
    {
        var expected = new SignupRegistrationMode.Result(
            closed: false,
            requireToken: true,
            allowOpenRegistration: false,
            showOptionalTokenEntry: false,
            source: SignupRegistrationModeSource.Config);

        Assert.Equal(expected, SignupRegistrationMode.Resolve(Config(false, true)));
    }

    [Fact]
    public void Resolve_Neither_SignUpIsClosed()
    {
        var expected = new SignupRegistrationMode.Result(
            closed: true,
            requireToken: true,
            allowOpenRegistration: false,
            showOptionalTokenEntry: false,
            source: SignupRegistrationModeSource.Config);

        Assert.Equal(expected, SignupRegistrationMode.Resolve(Config(false, false)));
    }

    [Fact]
    public void Resolve_FallsBackToOpenSignUpPlusTheOptionalToken_WhenTheSettingsAreUnknown()
    {
        var mode = SignupRegistrationMode.Resolve(null);

        Assert.Equal(
            new SignupRegistrationMode.Result(
                closed: false,
                requireToken: false,
                allowOpenRegistration: true,
                showOptionalTokenEntry: true,
                source: SignupRegistrationModeSource.Fallback),
            mode);

        // JS distinguishes null from undefined; C# has one absent value, so the default argument
        // stands in for the second case.
        Assert.Equal(mode, SignupRegistrationMode.Resolve(config: null, tokenMode: SignupTokenMode.Auto));
    }

    [Fact]
    public void Resolve_TokenModeRequired_ForcesTheTokenPathEvenForAClosedApp()
    {
        var expected = new SignupRegistrationMode.Result(
            closed: false,
            requireToken: true,
            allowOpenRegistration: false,
            showOptionalTokenEntry: false,
            source: SignupRegistrationModeSource.TokenMode);

        // The server validates the invite token itself, so a closed configuration must not block it.
        Assert.Equal(expected, SignupRegistrationMode.Resolve(Config(false, false), SignupTokenMode.Required));
        Assert.Equal(expected, SignupRegistrationMode.Resolve(Config(true, true), SignupTokenMode.Required));
        Assert.Equal(expected, SignupRegistrationMode.Resolve(null, SignupTokenMode.Required));
    }

    [Fact]
    public void Resolve_TokenModeAuto_IsTheConfigurationsOwnAnswer()
    {
        Assert.Equal(
            SignupRegistrationMode.Resolve(Config(true, false)),
            SignupRegistrationMode.Resolve(Config(true, false), SignupTokenMode.Auto));
    }
}
