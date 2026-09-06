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
    /// The broader rule, of which the fatal case above is a subset: a <c>[Parameter]</c> must not
    /// hide a base-class property at all.
    /// </summary>
    /// <remarks>
    /// Blazor only rejects the hiding when the BASE member is also a <c>[Parameter]</c>. Hiding a
    /// plain base property compiles, renders, and quietly gives the component two properties of the
    /// same name: the base class's own methods keep reading theirs while the derived markup reads
    /// the parameter. <c>TierChangeConfirmationModal</c> did this with <c>IsLoading</c> — the base's
    /// <c>GetRootCssClasses</c> and <c>SetLoadingAsync</c> operated on a flag the component never
    /// showed, and the parameter the parent set was invisible to them. Two concepts sharing one
    /// name is worth catching even when the framework tolerates it, so the fix is a distinct name
    /// rather than a <c>new</c> declaration.
    /// </remarks>
    [Theory]
    [MemberData(nameof(RenderableComponentTypeNames))]
    public void Component_DoesNotHideABasePropertyWithAParameter(string typeName)
    {
        var type = BlazorAssembly.GetType(typeName, throwOnError: true)!;

        var offenders = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(property => property.IsDefined(typeof(ParameterAttribute), inherit: false))
            .Where(property => HidesABaseProperty(type, property))
            .Select(property => property.Name)
            .ToArray();

        if (offenders.Length > 0)
        {
            Assert.Fail($"{typeName} declares [Parameter] {string.Join(", ", offenders)} hiding a " +
                        "base-class property of the same name. Give the parameter its own name " +
                        "instead — the base class keeps reading its own property, so the two names " +
                        "silently diverge.");
        }
    }

    /// <summary>
    /// Proves the mechanism the first theory relies on, so that test can never quietly become
    /// vacuous if the framework changes how parameters are discovered. This component lives in the
    /// test assembly, so the theories never see it.
    /// </summary>
    [Fact]
    public void HidingABaseParameterWithNew_IsRejectedByBlazor()
    {
        var instance = new HidesBaseOnErrorParameter();

        var exception = Assert.Throws<InvalidOperationException>(
            () => ParameterView.Empty.SetParameterProperties(instance));

        Assert.Contains("more than one parameter", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// True when a base class declares a property of the same name — i.e. the derived one hides it
    /// (with <c>new</c>, or with the CS0108 warning suppressed some other way). A genuine override
    /// is not hiding, so it is excluded via <c>GetBaseDefinition</c>, the same test Blazor's own
    /// parameter discovery uses to collapse overrides.
    /// </summary>
    private static bool HidesABaseProperty(Type declaringType, PropertyInfo property)
    {
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        var derivedDefinition = property.GetMethod?.GetBaseDefinition();

        for (var baseType = declaringType.BaseType; baseType != null; baseType = baseType.BaseType)
        {
            foreach (var candidate in baseType.GetProperties(Flags))
            {
                if (!string.Equals(candidate.Name, property.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                // An override shares the base definition of its accessor; hiding does not.
                if (derivedDefinition != null && candidate.GetMethod?.GetBaseDefinition() == derivedDefinition)
                {
                    continue;
                }

                return true;
            }
        }

        return false;
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
