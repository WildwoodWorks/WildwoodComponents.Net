using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using WildwoodComponents.Blazor.Components.Disclaimer;
using WildwoodComponents.Blazor.Components.Registration;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The <c>data-ww-*</c> DOM contract the shared end-to-end suite locates by, as Blazor renders it.
/// </summary>
/// <remarks>
/// One spec drives React, Blazor and Razor, and it finds nothing by copy: every label the
/// components render is host-configurable, so a reworded button reads as a hang. The selectors are
/// written down once, in the JS package's <c>src/testing/selectors.ts</c>, and this file pins the half
/// of them Blazor's registration form and disclaimer panel are responsible for. The signup view's
/// own hooks are pinned in <see cref="RegistrationSubscriptionSignupRenderTests"/>, which already
/// owns the driver harness those steps need.
///
/// Same technique as the other render tests here: there is no bUnit renderer in this repo, so
/// <c>BuildRenderTree</c> is called on a bare instance and the frames are read back.
/// </remarks>
public class TestingContractRenderTests
{
    #region Render helpers

    private static void SetField(object component, string name, object? value)
    {
        component.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);
    }

    // BL0006: reading RenderTree frames is exactly what these tests are for - the components have
    // no bUnit renderer, so the compiled markup is the only thing there is to assert against.
#pragma warning disable BL0006
    private static ArrayRange<RenderTreeFrame> Frames(object component)
    {
        var builder = new RenderTreeBuilder();
        component.GetType()
            .GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, new object[] { builder });

        return builder.GetFrames();
    }

    /// <summary>Text, markup and every attribute as <c>name="value"</c>, child content included.</summary>
    /// <remarks>
    /// The child-content step is what the other render tests here do without: a component's
    /// <c>ChildContent</c> is a delegate rather than frames, and the whole registration form lives
    /// inside an <c>EditForm</c>, so its fields are invisible until that delegate is invoked.
    /// </remarks>
    private static string RenderAll(object component)
    {
        var text = new StringBuilder();
        Append(text, Frames(component));
        return text.ToString();
    }

    private static void Append(StringBuilder text, ArrayRange<RenderTreeFrame> frames)
    {
        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Text:
                    text.Append(frame.TextContent).Append(' ');
                    break;
                case RenderTreeFrameType.Markup:
                    text.Append(frame.MarkupContent).Append(' ');
                    break;
                case RenderTreeFrameType.Attribute:
                    // A minimized Razor attribute - `data-ww-error-message` with no value - compiles
                    // to a boxed bool rather than a string, and Blazor renders `true` as a bare
                    // present attribute and `false` as no attribute at all. Reading it as a string
                    // would collapse both to the empty string, so an attribute that regressed to
                    // `false` would still read as present here while `[data-ww-error-message]` found
                    // nothing in a real browser. Every such attribute in these components is a
                    // compile-time `true` today; this is so that stops being something to remember.
                    if (frame.AttributeValue is bool present)
                    {
                        if (present) text.Append(frame.AttributeName).Append(' ');
                        break;
                    }

                    text.Append(frame.AttributeName)
                        .Append("=\"")
                        .Append(frame.AttributeValue as string ?? string.Empty)
                        .Append("\" ");
                    Append(text, ChildContent(frame.AttributeValue));
                    break;
            }
        }
    }

    /// <summary>
    /// The frames behind a <c>RenderFragment</c> parameter, or nothing for any other value.
    /// </summary>
    /// <remarks>
    /// Narrowed to the two fragment types on purpose: an event handler or a <c>Func</c> parameter
    /// is a delegate too, and invoking one of those would run component logic rather than render
    /// markup. A typed fragment's generated lambda ignores its context argument here, so the
    /// default is enough to reach the markup.
    /// </remarks>
    private static ArrayRange<RenderTreeFrame> ChildContent(object? value)
    {
        var builder = new RenderTreeBuilder();

        if (value is RenderFragment fragment)
        {
            fragment(builder);
        }
        else if (value is Delegate typed
                 && typed.GetType() is { IsGenericType: true } type
                 && type.GetGenericTypeDefinition() == typeof(RenderFragment<>))
        {
            var argument = type.GetGenericArguments()[0];
            var context = argument.IsValueType ? Activator.CreateInstance(argument) : null;
            if (typed.DynamicInvoke(context) is RenderFragment inner) inner(builder);
        }

        return builder.GetFrames();
    }

    /// <summary>
    /// Is there an <c>input type="checkbox"</c> inside an element carrying <paramref name="cssClass"/>?
    /// </summary>
    /// <remarks>
    /// A flat attribute dump cannot answer that, and containment is the whole of the claim: the
    /// helper's gate selector is <c>.ww-disclaimer-accept input[type="checkbox"]</c>, so a box
    /// moved out from under that wrapper stops being found even though both strings still appear.
    /// </remarks>
    private static bool HasCheckboxUnder(object component, string cssClass)
    {
        var frames = Frames(component);

        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType != RenderTreeFrameType.Element) continue;
            if (!HasClass(frames, i, cssClass)) continue;

            var end = i + frames.Array[i].ElementSubtreeLength;
            for (var j = i + 1; j < end; j++)
            {
                if (frames.Array[j].FrameType != RenderTreeFrameType.Element) continue;
                if (frames.Array[j].ElementName != "input") continue;
                if (HasAttribute(frames, j, "type", "checkbox")) return true;
            }
        }

        return false;
    }

    /// <summary>An element's own attributes, which are the frames between it and its first child.</summary>
    private static bool HasClass(ArrayRange<RenderTreeFrame> frames, int element, string cssClass)
    {
        for (var i = element + 1; i < element + frames.Array[element].ElementSubtreeLength; i++)
        {
            if (frames.Array[i].FrameType != RenderTreeFrameType.Attribute) break;
            if (frames.Array[i].AttributeName != "class") continue;
            if ((frames.Array[i].AttributeValue as string ?? string.Empty)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains(cssClass))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAttribute(ArrayRange<RenderTreeFrame> frames, int element, string name, string? value = null)
    {
        for (var i = element + 1; i < element + frames.Array[element].ElementSubtreeLength; i++)
        {
            if (frames.Array[i].FrameType != RenderTreeFrameType.Attribute) break;
            if (frames.Array[i].AttributeName != name) continue;
            if (value is null || (frames.Array[i].AttributeValue as string) == value) return true;
        }

        return false;
    }

    /// <summary>
    /// How many <c>.ww-btn-primary</c> buttons sit inside <c>.ww-disclaimer-actions</c>, and
    /// whether every one of them names an action.
    /// </summary>
    /// <remarks>
    /// Same-element, not same-document: the exclusion the helper relies on is
    /// <c>:not([data-ww-disclaimer-action])</c> on the button itself, so a run that found the two
    /// strings anywhere in the markup would prove nothing.
    /// </remarks>
    private static (int Buttons, bool AllNamed) ActionBarPrimaries(object component)
    {
        var frames = Frames(component);
        var buttons = 0;
        var allNamed = true;

        for (var i = 0; i < frames.Count; i++)
        {
            if (frames.Array[i].FrameType != RenderTreeFrameType.Element) continue;
            if (!HasClass(frames, i, "ww-disclaimer-actions")) continue;

            var end = i + frames.Array[i].ElementSubtreeLength;
            for (var j = i + 1; j < end; j++)
            {
                if (frames.Array[j].FrameType != RenderTreeFrameType.Element) continue;
                if (!HasClass(frames, j, "ww-btn-primary")) continue;

                buttons++;
                if (!HasAttribute(frames, j, "data-ww-disclaimer-action")) allNamed = false;
            }
        }

        return (buttons, allNamed);
    }
#pragma warning restore BL0006

    #endregion

    #region The registration form

    /// <summary>The form on screen once a token is behind it (or none was ever needed).</summary>
    private static TokenRegistrationComponent RegistrationForm()
    {
        var component = new TokenRegistrationComponent();
        SetField(component, "_tokenValidated", true);
        SetField(component, "_currentStep", 2);
        return component;
    }

    /// <summary>
    /// <c>data-ww-field</c> is the contract and the id is not: the Razor stack suffixes its ids
    /// with a per-instance component id, so no constant id selector can exist there.
    /// </summary>
    /// <remarks>
    /// These six land on <c>InputText</c> rather than on a bare <c>input</c>, so what is asserted
    /// is that each one reaches the component; putting it on the rendered element from there is
    /// <c>InputBase</c>'s unmatched-attribute splat, which is the framework's job and not this
    /// repo's to re-test.
    /// </remarks>
    [Theory]
    [InlineData("firstName")]
    [InlineData("lastName")]
    [InlineData("username")]
    [InlineData("email")]
    [InlineData("password")]
    [InlineData("confirmPassword")]
    public void The_registration_form_names_every_field_the_shared_suite_fills(string field)
    {
        Assert.Contains($"data-ww-field=\"{field}\"", RenderAll(RegistrationForm()));
    }

    /// <summary>
    /// The register step's submit names its action. The <c>type="submit"</c> the helper falls back
    /// to on a build that predates the hook is still rendered too.
    /// </summary>
    [Fact]
    public void The_registration_form_names_its_submit()
    {
        var markup = RenderAll(RegistrationForm());

        Assert.Contains("data-ww-action=\"submit-register\"", markup);
        Assert.Contains("type=\"submit\"", markup);
    }

    /// <summary>
    /// The ids hosts may already depend on are kept, not replaced. That Blazor's ids spell the same
    /// names as the contract is a coincidence of this markup rather than part of it - the Razor
    /// stack's ids spell something else entirely.
    /// </summary>
    [Fact]
    public void The_registration_form_keeps_the_ids_it_always_had()
    {
        var markup = RenderAll(RegistrationForm());

        Assert.All(
            new[] { "firstName", "lastName", "username", "email", "password", "confirmPassword" },
            id => Assert.Contains($"id=\"{id}\"", markup));
    }

    #endregion

    #region The disclaimer panel

    private static DisclaimerComponent DisclaimerPanel()
    {
        var component = new DisclaimerComponent();
        SetField(component, "_isLoading", false);
        SetField(component, "_disclaimers", new List<PendingDisclaimerModel>
        {
            new PendingDisclaimerModel
            {
                DisclaimerId = "d-1",
                VersionId = "v-1",
                Title = "Terms",
                DisclaimerType = "Terms",
                VersionNumber = 1,
                Content = "Be excellent to each other.",
                IsRequired = true
            }
        });

        return component;
    }

    /// <summary>
    /// One bulk accept is all this component has - there is no per-disclaimer accept and no retry
    /// to hang the other two values on - so <c>accept-all</c> is the one value it can carry.
    /// </summary>
    [Fact]
    public void The_disclaimer_panel_names_its_accept_control()
    {
        var markup = RenderAll(DisclaimerPanel());

        Assert.Contains("data-ww-disclaimer-action=\"accept-all\"", markup);
        Assert.DoesNotContain("data-ww-disclaimer-action=\"accept\"", markup);
    }

    /// <summary>
    /// Accept stays disabled until every required box is ticked, and a Playwright click waits for
    /// actionability - so the helper ticks the gate first, and it finds it under
    /// <c>.ww-disclaimer-accept</c>. There is deliberately no bare <c>input[type="checkbox"]</c>
    /// fallback in the helper, so this wrapper is load-bearing rather than cosmetic.
    /// </summary>
    [Fact]
    public void The_disclaimer_gate_checkbox_is_where_the_helper_looks_for_it()
    {
        Assert.True(HasCheckboxUnder(DisclaimerPanel(), "ww-disclaimer-accept"));
    }

    /// <summary>
    /// The helper's legacy retry fallback is
    /// <c>.ww-disclaimer-actions .ww-btn-primary:not(.ww-btn-block):not([data-ww-disclaimer-action])</c>
    /// - it is confined to markup that names no actions at all, which is what keeps it off this
    /// component's Accept. Naming the action is what buys that exclusion, so it is pinned here.
    /// </summary>
    [Fact]
    public void The_accept_control_is_a_named_action_so_the_legacy_retry_fallback_skips_it()
    {
        var (buttons, allNamed) = ActionBarPrimaries(DisclaimerPanel());

        Assert.Equal(1, buttons);
        Assert.True(allNamed, "an unnamed primary in the action bar would be clicked as a retry");
    }

    #endregion
}
