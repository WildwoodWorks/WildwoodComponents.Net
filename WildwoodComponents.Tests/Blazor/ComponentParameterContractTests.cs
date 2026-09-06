using System.Reflection;
using Microsoft.AspNetCore.Components;
using WildwoodComponents.Blazor.Components.Base;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Guards the one component contract that Blazor enforces at RUNTIME rather than at compile time:
/// a component may not declare two <see cref="ParameterAttribute"/> properties whose names match
/// case-insensitively. Hiding a base-class parameter with <c>new</c> does exactly that — the
/// compiler is happy, and then <c>ComponentProperties.WritersForType</c> throws the first time the
/// component is parameterised. On Blazor Server that throw kills the circuit, which the user sees
/// as a frozen page with no error at all.
///
/// This is not hypothetical: <c>DisclaimerComponent</c>, <c>AIProxyComponent</c> and
/// <c>AppTierComponent</c> all shipped with <c>[Parameter] public new EventCallback&lt;...&gt; OnError</c>,
/// so the "Accept Disclaimers" step of the login flow could never render.
/// </summary>
public class ComponentParameterContractTests
{
    private static readonly Assembly BlazorAssembly = typeof(BaseWildwoodComponent).Assembly;

    /// <summary>
    /// Every component in the library that Blazor could actually construct and render. The datum is
    /// the type's full name (a string) so each component becomes its own named test case.
    /// </summary>
    public static TheoryData<string> RenderableComponentTypeNames()
    {
        var data = new TheoryData<string>();

        var names = BlazorAssembly.GetTypes()
            .Where(IsRenderableComponent)
            .Select(type => type.FullName!)
            .OrderBy(name => name, StringComparer.Ordinal);

        foreach (var name in names)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RenderableComponentTypeNames))]
    public void Component_CanBeParameterisedByBlazor(string typeName)
    {
        var type = BlazorAssembly.GetType(typeName, throwOnError: true)!;
        var instance = Activator.CreateInstance(type)!;

        // ParameterView.SetParameterProperties is the exact call ComponentBase.SetParametersAsync
        // makes, and it builds the type's parameter writers unconditionally — an empty view still
        // triggers the duplicate-name check. Going through SetParametersAsync instead would throw
        // "The render handle is not yet assigned" for an unattached component, which would fail
        // identically whether or not the contract holds.
        //
        // The instance is deliberately NOT disposed: nothing here runs a lifecycle, so it holds no
        // resources, and several components' Dispose overrides dereference fields that only exist
        // after initialisation. Blazor never disposes a component it did not initialise either.
        var exception = Record.Exception(() => ParameterView.Empty.SetParameterProperties(instance));

        if (exception is not null)
        {
            Assert.Fail($"{typeName} cannot be parameterised by Blazor and will kill the circuit " +
                        $"on first render: {exception.Message}");
        }
    }

    /// <summary>
    /// Proves the mechanism the theory above relies on, so that test can never quietly become
    /// vacuous if the framework changes how parameters are discovered. This component lives in the
    /// test assembly, so the theory never sees it.
    /// </summary>
    [Fact]
    public void HidingABaseParameterWithNew_IsRejectedByBlazor()
    {
        var instance = new HidesBaseOnErrorParameter();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ParameterView.Empty.SetParameterProperties(instance));

        Assert.Contains("more than one parameter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRenderableComponent(Type type) =>
        typeof(IComponent).IsAssignableFrom(type)
        && !type.IsAbstract
        && !type.IsInterface
        && !type.IsGenericTypeDefinition
        // Blazor's own ComponentFactory constructs components with a public parameterless
        // constructor; anything else it could not render either.
        && type.GetConstructor(Type.EmptyTypes) is not null;

    private sealed class HidesBaseOnErrorParameter : BaseWildwoodComponent
    {
        [Parameter] public new EventCallback<string> OnError { get; set; }
    }
}
