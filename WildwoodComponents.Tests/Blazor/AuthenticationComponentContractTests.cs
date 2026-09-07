using System.Reflection;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Authentication;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Pins the two AuthenticationComponent parameters that carry the React component's
/// <c>allowRegistration</c> / <c>onRegisterClick</c> contract. Both are reachable only through
/// reflection here (the markup lives in a .razor file with no bUnit renderer), so the test checks
/// what a consuming app depends on: the names, the exact types, and that Blazor will bind them.
/// A <c>bool</c> instead of <c>bool?</c> would silently lose the "leave the configuration in
/// charge" third state.
/// </summary>
public class AuthenticationComponentContractTests
{
    [Fact]
    public void AllowRegistration_is_a_nullable_bool_parameter()
    {
        var property = typeof(AuthenticationComponent).GetProperty("AllowRegistration");

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<ParameterAttribute>());
        Assert.Equal(typeof(bool?), property.PropertyType);
    }

    [Fact]
    public void OnRegisterClick_is_an_EventCallback_parameter()
    {
        var property = typeof(AuthenticationComponent).GetProperty("OnRegisterClick");

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<ParameterAttribute>());
        Assert.Equal(typeof(EventCallback), property.PropertyType);
    }
}
