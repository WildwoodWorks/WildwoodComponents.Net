using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests;

/// <summary>
/// The funnel tracker in the two browser attribution engines. Behavior is covered for real by the
/// Node self-test (<see cref="Razor.AttributionFunnelSelfTestRunnerTests"/>); these checks pin what
/// must not drift even on a machine without Node: the shared block is the same text in both engines,
/// its constants match <see cref="AttributionRules"/> and <c>@wildwood/core</c>, and both engines
/// expose the same public functions.
/// </summary>
public class AttributionFunnelSourceTests
{
    private const string BlazorAttribution = "WildwoodComponents.Blazor/wwwroot/js/wildwood-attribution.js";
    private const string RazorAttribution = "WildwoodComponents.Razor/wwwroot/js/attribution.js";

    public static TheoryData<string> Engines() => new() { BlazorAttribution, RazorAttribution };

    /// <summary>
    /// The Blazor copy is an ES module and the Razor copy a classic script inside an IIFE, so the block
    /// is compared line by line with each line TRIMMED. Nothing else about it may differ.
    /// </summary>
    [Fact]
    public void TheFunnelTrackerIsTheSameTextInBothEngines()
    {
        Assert.Equal(SharedBlock(BlazorAttribution), SharedBlock(RazorAttribution));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void TheStorageKeysAndLimitsMatchTheJsSdk(string engine)
    {
        var block = SharedBlock(engine);

        Assert.Contains("var SESSION_STORAGE_KEY = 'ww_attribution_session';", block, StringComparison.Ordinal);
        Assert.Contains("var SESSION_TIMEOUT_MS = 30 * 60 * 1000;", block, StringComparison.Ordinal);
        Assert.Contains("var FLUSH_INTERVAL_MS = 5000;", block, StringComparison.Ordinal);
        Assert.Contains($"var MAX_EVENTS_PER_REQUEST = {AttributionRules.MaxFunnelEventsPerRequest};", block, StringComparison.Ordinal);
        Assert.Contains($"var FUNNEL_LABEL_MAX = {AttributionRules.FunnelLabelMaxLength};", block, StringComparison.Ordinal);
        Assert.Contains($"var MAX_CUSTOM_EVENT_NAMES = {AttributionRules.MaxCustomEventNames};", block, StringComparison.Ordinal);
        Assert.Contains("var FUNNEL_EVENT_NAME = /^[a-z0-9_]{1,40}$/;", block, StringComparison.Ordinal);
        Assert.Contains("'text/plain;charset=UTF-8'", block, StringComparison.Ordinal);
        Assert.Contains("'[data-ww-cta]'", block, StringComparison.Ordinal);
        Assert.Contains("if (width < 768) return 'mobile';", block, StringComparison.Ordinal);
        Assert.Contains("if (width < 1024 || (coarse && width < 1280)) return 'tablet';", block, StringComparison.Ordinal);
    }

    /// <summary>The JS event lists and the C# ones are the same sets, in the same order.</summary>
    [Theory]
    [MemberData(nameof(Engines))]
    public void TheEventAllowlistMatchesTheSharedRules(string engine)
    {
        var block = SharedBlock(engine);

        Assert.Equal(AttributionRules.FunnelClientEvents, JsStringArray(block, "FUNNEL_CLIENT_EVENTS"));
        Assert.Equal(AttributionRules.FunnelServerOnlyEvents, JsStringArray(block, "FUNNEL_SERVER_ONLY_EVENTS"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void BothEnginesExposeTrackTrackCtaAndFlush(string engine)
    {
        var source = ReadSource(engine);

        if (engine == BlazorAttribution)
        {
            Assert.Contains("export function track(name, options)", source, StringComparison.Ordinal);
            Assert.Contains("export function trackCta(label)", source, StringComparison.Ordinal);
            Assert.Contains("export function flush()", source, StringComparison.Ordinal);
        }
        else
        {
            Assert.Contains("track: function (name, options)", source, StringComparison.Ordinal);
            Assert.Contains("trackCta: function (label)", source, StringComparison.Ordinal);
            Assert.Contains("flush: function ()", source, StringComparison.Ordinal);
            Assert.Contains("signupErrorCategory: function (code, status)", source, StringComparison.Ordinal);
        }

        // The registration payload carries the funnel session, in both engines.
        Assert.Contains("sessionKey: session.sessionKey,", source, StringComparison.Ordinal);
        Assert.Contains("deviceClass: this.tracker.getDeviceClass(),", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The mirror runs through the shared rule in both engines, before the undecided early return, so an
    /// undecided visitor still gets it and the stored blob is still left alone.
    /// </summary>
    [Theory]
    [MemberData(nameof(Engines))]
    public void TheMirrorIsSyncedBeforeTheUndecidedReturn(string engine)
    {
        var source = ReadSource(engine);

        var sync = source.IndexOf("syncSessionMirror(this.mirrorGate, decision, hasData,", StringComparison.Ordinal);
        var undecided = source.IndexOf("if (!decision) return;", StringComparison.Ordinal);
        Assert.True(sync >= 0, $"{engine} must sync the sessionStorage mirror from _persistIfAllowed.");
        Assert.True(sync < undecided, $"{engine} must sync the mirror before the undecided return.");
    }

    /// <summary>
    /// The Blazor registration form reports its steps through <c>SignupFunnel</c> (unit-tested in
    /// <c>SignupFunnelTests</c>); there is no bUnit renderer here, so the wiring is pinned in the source.
    /// </summary>
    [Fact]
    public void TheBlazorRegistrationFormReportsItsSteps()
    {
        var source = ReadSource("WildwoodComponents.Blazor/Components/Registration/TokenRegistrationComponent.razor");

        Assert.Contains("@onfocusin=\"HandleFormFocusAsync\"", source, StringComparison.Ordinal);
        Assert.Contains("if (firstRender) await Funnel.ViewAsync();", source, StringComparison.Ordinal);
        Assert.Contains("await Funnel.SubmitAsync();", source, StringComparison.Ordinal);
        Assert.Contains("await Funnel.ErrorAsync(\"invalid_token\");", source, StringComparison.Ordinal);
        Assert.Contains("await Funnel.ErrorAsync(ValidationErrorCategory(validation));", source, StringComparison.Ordinal);
        Assert.Contains("await Funnel.CheckoutStartAsync(", source, StringComparison.Ordinal);
        // A signup_error never carries the server's message.
        Assert.DoesNotContain("Funnel.ErrorAsync(_registrationError", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Funnel.ErrorAsync(ex.Message", source, StringComparison.Ordinal);
    }

    [Fact]
    public void TheBlazorSignupWizardsReportPlanAndCheckout()
    {
        var wizard = ReadSource("WildwoodComponents.Blazor/Components/Registration/SignupWithSubscriptionComponent.razor.cs");
        Assert.Contains("await Funnel.PlanSelectedAsync(args.Tier.Id, args.Tier.Name);", wizard, StringComparison.Ordinal);
        Assert.Contains("await Funnel.CheckoutStartAsync(", wizard, StringComparison.Ordinal);

        var view = ReadSource("WildwoodComponents.Blazor/Components/RegistrationSubscription/RegistrationSubscriptionSignup.razor.cs");
        Assert.Contains("Funnel = new SignupFunnel(", view, StringComparison.Ordinal);
    }

    /// <summary>The Razor registration scripts report the same steps through window.wildwoodAttribution.track.</summary>
    [Theory]
    [InlineData("WildwoodComponents.Razor/wwwroot/js/token-registration.js")]
    [InlineData("WildwoodComponents.Razor/wwwroot/js/regsub-signup.js")]
    public void TheRazorRegistrationScriptsReportTheirSteps(string script)
    {
        var source = ReadSource(script);

        Assert.Contains("window.wildwoodAttribution", source, StringComparison.Ordinal);
        Assert.Contains("'signup_view'", source, StringComparison.Ordinal);
        Assert.Contains("'signup_start'", source, StringComparison.Ordinal);
        Assert.Contains("'signup_submit'", source, StringComparison.Ordinal);
        Assert.Contains("'signup_error', 'invalid_token'", source, StringComparison.Ordinal);
        Assert.Contains("'checkout_start'", source, StringComparison.Ordinal);
        Assert.Contains("addEventListener('focusin'", source, StringComparison.Ordinal);
    }

    private static List<string> JsStringArray(string block, string name)
    {
        var start = block.IndexOf("var " + name + " = [", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected a {name} array.");
        var end = block.IndexOf("];", start, StringComparison.Ordinal);
        var body = block.Substring(start, end - start);
        body = body.Substring(body.IndexOf('[') + 1);
        return body
            .Split(',')
            .Select(part => part.Trim().Trim('\'').Trim())
            .Where(part => part.Length > 0)
            .ToList();
    }

    private static string SharedBlock(string engine)
    {
        const string begin = "// ---- BEGIN shared funnel tracker ----";
        const string end = "// ---- END shared funnel tracker ----";

        var source = ReadSource(engine);
        var start = source.IndexOf(begin, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected {engine} to mark the start of the shared funnel tracker.");
        var stop = source.IndexOf(end, start, StringComparison.Ordinal);
        Assert.True(stop >= 0, $"Expected {engine} to mark the end of the shared funnel tracker.");

        var block = source.Substring(start, stop - start).Split('\n');
        Assert.True(block.Length > 300, $"Expected {engine}'s shared funnel tracker to be the whole block.");
        return string.Join("\n", block.Select(line => line.Trim()));
    }

    private static string ReadSource(string relativePath)
    {
        var full = Path.Combine(NodeSelfTest.RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"Expected {relativePath} to exist.");
        return File.ReadAllText(full).Replace("\r\n", "\n");
    }
}
