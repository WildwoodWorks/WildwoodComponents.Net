namespace WildwoodComponents.Blazor.Components.Authentication;

/// <summary>
/// Registration gate for AuthenticationComponent. Mirrors @wildwood/react-shared's
/// resolveRegistrationAccess: the AllowRegistration parameter, when supplied, wins over the
/// server configuration in both directions, and an active register view collapses back to
/// login when registration is hidden.
/// </summary>
public static class RegistrationAccess
{
    /// <summary>
    /// The resolved gate: whether the sign-up affordance renders, and whether the component
    /// should be showing the login view.
    /// </summary>
    /// <param name="ShowRegistration">Whether the sign-up affordance should be rendered.</param>
    /// <param name="IsLoginView">
    /// The view to render: the register view collapses to login when registration is hidden.
    /// </param>
    public readonly record struct Result(bool ShowRegistration, bool IsLoginView);

    /// <summary>
    /// Resolves the effective registration gate.
    /// </summary>
    /// <param name="allowRegistrationOverride">
    /// The component's AllowRegistration parameter. When supplied it wins over the server
    /// configuration in both directions; <c>null</c> leaves the configuration in charge.
    /// </param>
    /// <param name="configAllowsRegistration">
    /// Whether the authentication configuration permits open or token registration.
    /// </param>
    /// <param name="isLoginView">Whether the component is currently showing the login view.</param>
    public static Result Resolve(bool? allowRegistrationOverride, bool configAllowsRegistration, bool isLoginView)
    {
        var show = allowRegistrationOverride ?? configAllowsRegistration;
        return new Result(show, isLoginView || !show);
    }
}
