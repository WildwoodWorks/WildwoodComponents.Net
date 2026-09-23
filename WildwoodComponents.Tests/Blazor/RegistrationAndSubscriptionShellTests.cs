using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using WildwoodComponents.Blazor.Components.AppTier;
using WildwoodComponents.Blazor.Components.Pricing;
using WildwoodComponents.Blazor.Components.Registration;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Blazor;

// BL0005: setting a component parameter from outside the component is exactly how the shell is put
// into a given state here - there is no renderer to pass a ParameterView through. Scoped to this
// file, so the advisory still applies to every component that ships.
#pragma warning disable BL0005

/// <summary>
/// <see cref="RegistrationAndSubscriptionComponent"/> is a switch and nothing else, so what there
/// is to pin is the switching and the forwarding.
/// </summary>
/// <remarks>
/// <para>
/// The reachability test is the load-bearing one. Blazor has no discriminated-union parameters, so
/// the shell restates the union of the three views' parameters by hand; left unchecked, a
/// parameter added to a view later would be silently unreachable through the shell — set it and
/// nothing happens, with no compile error and no runtime error either, because the shell's
/// <c>AdditionalAttributes</c> would swallow it as an HTML attribute. So the shell's surface is
/// compared against the views' by reflection on every build, in both directions: every view
/// parameter must exist on the shell, and every view parameter must actually be written into the
/// render tree when that view is the active one.
/// </para>
/// <para>
/// There is no bUnit renderer in this repo, so the compiled markup is read the way
/// <see cref="RegistrationSubscriptionPricingRenderTests"/> does: <c>BuildRenderTree</c> on a bare
/// instance needs no renderer, DI or lifecycle.
/// </para>
/// </remarks>
public class RegistrationAndSubscriptionShellTests
{
    #region Reflection helpers

    // BL0006: reading RenderTree frames is exactly what these tests are for - the shell has no
    // bUnit renderer, so the compiled markup is the only thing there is to assert against.
#pragma warning disable BL0006
    private static ArrayRange<RenderTreeFrame> Frames(object component)
    {
        var builder = new RenderTreeBuilder();
        component.GetType()
            .GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, new object[] { builder });

        return builder.GetFrames();
    }

    /// <summary>The component types the shell mounted, in order.</summary>
    private static List<Type> ChildComponents(object component)
    {
        var frames = Frames(component);
        var types = new List<Type>();

        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType == RenderTreeFrameType.Component)
            {
                types.Add(frames.Array[i].ComponentType);
            }
        }

        return types;
    }

    /// <summary>Every HTML element the shell opened itself, by tag name.</summary>
    private static List<string> OwnElements(object component)
    {
        var frames = Frames(component);
        var names = new List<string>();

        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType == RenderTreeFrameType.Element)
            {
                names.Add(frames.Array[i].ElementName);
            }
        }

        return names;
    }

    /// <summary>The parameter names the shell wrote onto the view it mounted.</summary>
    private static HashSet<string> ForwardedParameterNames(object component)
    {
        var frames = Frames(component);
        var names = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType == RenderTreeFrameType.Attribute)
            {
                names.Add(frames.Array[i].AttributeName);
            }
        }

        return names;
    }
#pragma warning restore BL0006

    /// <summary>Every <c>[Parameter]</c> on a component, inherited ones included.</summary>
    private static List<PropertyInfo> Parameters(Type type)
    {
        var properties = new List<PropertyInfo>();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<ParameterAttribute>() is not null)
            {
                properties.Add(property);
            }
        }

        return properties;
    }

    private static RegistrationAndSubscriptionComponent Shell(RegistrationSubscriptionView view)
    {
        return new RegistrationAndSubscriptionComponent { View = view, AppId = "app-1" };
    }

    public static TheoryData<RegistrationSubscriptionView, Type> ViewTypes()
    {
        return new TheoryData<RegistrationSubscriptionView, Type>
        {
            { RegistrationSubscriptionView.Pricing, typeof(RegistrationSubscriptionPricing) },
            { RegistrationSubscriptionView.Signup, typeof(RegistrationSubscriptionSignup) },
            { RegistrationSubscriptionView.Manage, typeof(RegistrationSubscriptionManage) }
        };
    }

    #endregion

    #region The switch

    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void Each_View_value_mounts_exactly_that_view(RegistrationSubscriptionView view, Type expected)
    {
        var children = ChildComponents(Shell(view));

        Assert.Equal(new[] { expected }, children);
    }

    /// <summary>
    /// JS defaults <c>view</c> to <c>'pricing'</c>, and so does this. A host that forgets the
    /// parameter gets the price list, not an empty page.
    /// </summary>
    [Fact]
    public void The_default_view_is_pricing()
    {
        var shell = new RegistrationAndSubscriptionComponent { AppId = "app-1" };

        Assert.Equal(RegistrationSubscriptionView.Pricing, shell.View);
        Assert.Equal(new[] { typeof(RegistrationSubscriptionPricing) }, ChildComponents(shell));
    }

    /// <summary>
    /// The shell renders no markup of its own, so the active view owns the root element, its
    /// <c>data-ww-view</c> hook and its CSS class exactly as it does when mounted directly.
    /// </summary>
    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void The_shell_adds_no_element_of_its_own(RegistrationSubscriptionView view, Type expected)
    {
        Assert.NotNull(expected);
        Assert.Empty(OwnElements(Shell(view)));
    }

    #endregion

    #region Foreign parameters

    /// <summary>
    /// React makes a parameter belonging to another view a compile error through its props union;
    /// Blazor cannot, so the rule is that one is IGNORED. Never an exception, and never leaked
    /// onto the view that is showing.
    /// </summary>
    [Fact]
    public void A_manage_parameter_set_on_the_pricing_view_is_ignored()
    {
        var shell = Shell(RegistrationSubscriptionView.Pricing);
        shell.Layout = ManageLayout.Stacked;
        shell.UserId = "user-1";
        shell.IsAdmin = true;
        shell.AllowPackSelfService = true;

        var forwarded = ForwardedParameterNames(shell);

        Assert.Equal(new[] { typeof(RegistrationSubscriptionPricing) }, ChildComponents(shell));
        Assert.DoesNotContain("Layout", forwarded);
        Assert.DoesNotContain("UserId", forwarded);
        Assert.DoesNotContain("IsAdmin", forwarded);
        Assert.DoesNotContain("AllowPackSelfService", forwarded);
    }

    [Fact]
    public void A_pricing_parameter_set_on_the_signup_view_is_ignored()
    {
        var shell = Shell(RegistrationSubscriptionView.Signup);
        shell.IncludeJsonLd = true;
        shell.JsonLdUrl = "https://example.test/pricing";
        shell.HighlightTierId = "tier-pro";

        var forwarded = ForwardedParameterNames(shell);

        Assert.Equal(new[] { typeof(RegistrationSubscriptionSignup) }, ChildComponents(shell));
        Assert.DoesNotContain("IncludeJsonLd", forwarded);
        Assert.DoesNotContain("JsonLdUrl", forwarded);
        Assert.DoesNotContain("HighlightTierId", forwarded);
    }

    [Fact]
    public void A_signup_parameter_set_on_the_manage_view_is_ignored()
    {
        var shell = Shell(RegistrationSubscriptionView.Manage);
        shell.RegistrationToken = "tok-1";
        shell.PrefillEmail = "someone@example.test";
        shell.TokenMode = SignupTokenMode.Required;
        shell.PlanSelection = SignupPlanSelection.Skip;

        var forwarded = ForwardedParameterNames(shell);

        Assert.Equal(new[] { typeof(RegistrationSubscriptionManage) }, ChildComponents(shell));
        Assert.DoesNotContain("RegistrationToken", forwarded);
        Assert.DoesNotContain("PrefillEmail", forwarded);
        Assert.DoesNotContain("TokenMode", forwarded);
        Assert.DoesNotContain("PlanSelection", forwarded);
    }

    #endregion

    #region Reachability

    /// <summary>
    /// Every <c>[Parameter]</c> on every view is declared on the shell, under the same name and
    /// the same type — or that type wrapped in <see cref="Nullable{T}"/>, which is how the shell
    /// says "leave the active view's own default alone" for a parameter whose default differs
    /// between views.
    /// </summary>
    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void Every_view_parameter_is_declared_on_the_shell(RegistrationSubscriptionView view, Type viewType)
    {
        Assert.True(Enum.IsDefined(view));

        var shellType = typeof(RegistrationAndSubscriptionComponent);
        var missing = new List<string>();
        var mistyped = new List<string>();

        foreach (var parameter in Parameters(viewType))
        {
            var onShell = shellType.GetProperty(parameter.Name, BindingFlags.Public | BindingFlags.Instance);

            if (onShell is null || onShell.GetCustomAttribute<ParameterAttribute>() is null)
            {
                missing.Add(parameter.Name);
                continue;
            }

            var widened = Nullable.GetUnderlyingType(onShell.PropertyType) ?? onShell.PropertyType;

            if (onShell.PropertyType != parameter.PropertyType && widened != parameter.PropertyType)
            {
                mistyped.Add($"{parameter.Name} ({onShell.PropertyType} on the shell, " +
                             $"{parameter.PropertyType} on {viewType.Name})");
            }
        }

        Assert.True(
            missing.Count == 0,
            $"{viewType.Name} parameters unreachable through the shell: {string.Join(", ", missing)}");
        Assert.True(
            mistyped.Count == 0,
            $"{viewType.Name} parameters declared with a different type on the shell: " +
            string.Join("; ", mistyped));
    }

    /// <summary>
    /// Declaring the parameter is not enough — it has to be written onto the view. This compares
    /// what the markup actually forwards against the view's whole parameter list, so a parameter
    /// added to both the view and the shell but forgotten in the markup fails too.
    /// </summary>
    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void Every_view_parameter_is_forwarded_when_that_view_is_active(
        RegistrationSubscriptionView view, Type viewType)
    {
        var forwarded = ForwardedParameterNames(Shell(view));
        var notForwarded = new List<string>();

        foreach (var parameter in Parameters(viewType))
        {
            if (!forwarded.Contains(parameter.Name))
            {
                notForwarded.Add(parameter.Name);
            }
        }

        Assert.True(
            notForwarded.Count == 0,
            $"{viewType.Name} parameters the shell declares but never writes: " +
            string.Join(", ", notForwarded));
    }

    /// <summary>
    /// And nothing extra: the shell must not write a name the view does not declare, which would
    /// throw "does not have a property matching the name" the first time Blazor rendered it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void The_shell_forwards_nothing_the_view_does_not_declare(
        RegistrationSubscriptionView view, Type viewType)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in Parameters(viewType))
        {
            declared.Add(parameter.Name);
        }

        var unknown = new List<string>();
        foreach (var name in ForwardedParameterNames(Shell(view)))
        {
            if (!declared.Contains(name))
            {
                unknown.Add(name);
            }
        }

        Assert.True(
            unknown.Count == 0,
            $"the shell writes names {viewType.Name} does not declare: {string.Join(", ", unknown)}");
    }

    /// <summary>
    /// A shell parameter of the same type as the view's carries the same default, so going through
    /// the shell never changes what a host gets. The one parameter whose default differs between
    /// two views is nullable on the shell and is checked by
    /// <see cref="ShowAddOns_defaults_to_each_views_own_answer"/> instead.
    /// </summary>
    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void Shared_parameters_carry_the_views_own_default(
        RegistrationSubscriptionView view, Type viewType)
    {
        Assert.True(Enum.IsDefined(view));

        var shell = new RegistrationAndSubscriptionComponent();
        var viewInstance = Activator.CreateInstance(viewType)!;
        var shellType = typeof(RegistrationAndSubscriptionComponent);
        var drifted = new List<string>();

        foreach (var parameter in Parameters(viewType))
        {
            var onShell = shellType.GetProperty(parameter.Name, BindingFlags.Public | BindingFlags.Instance);
            if (onShell is null || onShell.PropertyType != parameter.PropertyType)
            {
                continue;
            }

            var shellDefault = onShell.GetValue(shell);
            var viewDefault = parameter.GetValue(viewInstance);

            if (!Equals(shellDefault, viewDefault))
            {
                drifted.Add($"{parameter.Name} (shell {shellDefault ?? "null"}, " +
                            $"{viewType.Name} {viewDefault ?? "null"})");
            }
        }

        Assert.True(
            drifted.Count == 0,
            $"shell defaults that no longer match {viewType.Name}: {string.Join("; ", drifted)}");
    }

    /// <summary>
    /// The pricing page sells plans and hides packs; the manage view is where packs are bought and
    /// cancelled, so it shows them. Left alone, the shell must honour both.
    /// </summary>
    [Fact]
    public void ShowAddOns_defaults_to_each_views_own_answer()
    {
        Assert.False(new RegistrationSubscriptionPricing().ShowAddOns);
        Assert.True(new RegistrationSubscriptionManage().ShowAddOns);
        Assert.Null(new RegistrationAndSubscriptionComponent().ShowAddOns);

        Assert.False(ForwardedShowAddOns(RegistrationSubscriptionView.Pricing, null));
        Assert.True(ForwardedShowAddOns(RegistrationSubscriptionView.Manage, null));

        // And a host that says so wins on either surface.
        Assert.True(ForwardedShowAddOns(RegistrationSubscriptionView.Pricing, true));
        Assert.False(ForwardedShowAddOns(RegistrationSubscriptionView.Manage, false));
    }

    // BL0006: see the note on Frames above.
#pragma warning disable BL0006
    private static bool ForwardedShowAddOns(RegistrationSubscriptionView view, bool? set)
    {
        var shell = Shell(view);
        shell.ShowAddOns = set;

        var frames = Frames(shell);

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Attribute && frame.AttributeName == "ShowAddOns")
            {
                return (bool)frame.AttributeValue!;
            }
        }

        throw new InvalidOperationException($"the {view} view was given no ShowAddOns at all");
    }
#pragma warning restore BL0006

    #endregion

    #region Deprecations

    // CS0618: naming the three deprecated components is the whole point of this region. Scoped to
    // it, so a NEW call site anywhere else still warns.
#pragma warning disable CS0618

    /// <summary>
    /// The three legacy surfaces carry an <c>[Obsolete]</c> WARNING naming their replacement, in
    /// step with the JS package (541e446). A warning, never an error: they still compile, still
    /// render, and nothing has been removed.
    /// </summary>
    [Theory]
    [InlineData(typeof(PricingDisplayComponent), "RegistrationSubscriptionPricing")]
    [InlineData(typeof(SignupWithSubscriptionComponent), "RegistrationSubscriptionSignup")]
    [InlineData(typeof(AppTierComponent), "RegistrationSubscriptionManage")]
    public void The_legacy_components_are_deprecated_and_still_usable(Type legacy, string replacement)
    {
        var obsolete = legacy.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError, $"{legacy.Name} must stay usable — [Obsolete] error, not warning");
        Assert.Contains("RegistrationAndSubscriptionComponent", obsolete.Message);
        Assert.Contains(replacement, obsolete.Message);
    }

    /// <summary>
    /// The replacement views are not themselves deprecated, and neither is the shell — a guard
    /// against an over-broad find-and-replace.
    /// </summary>
    [Theory]
    [MemberData(nameof(ViewTypes))]
    public void The_replacement_views_are_not_deprecated(RegistrationSubscriptionView view, Type viewType)
    {
        Assert.True(Enum.IsDefined(view));
        Assert.Null(viewType.GetCustomAttribute<ObsoleteAttribute>());
        Assert.Null(typeof(RegistrationAndSubscriptionComponent).GetCustomAttribute<ObsoleteAttribute>());
    }

#pragma warning restore CS0618

    #endregion
}
