namespace WildwoodComponents.Tests;

/// <summary>
/// The consent gate in the two browser attribution engines and the two consent engines. These are
/// checked against the source because the engines are JavaScript — the behaviour lives in the browser,
/// and this repository runs no JS test harness — and because what they must get right is a SHAPE: a
/// three-state gate where a two-state one reads almost identically and is wrong.
/// </summary>
/// <remarks>
/// <para>
/// The rule, from <c>@wildwood/core</c>'s <c>AttributionService.persistIfAllowed</c>: granted writes the
/// blob; decided-and-not-granted removes it; UNDECIDED keeps everything in memory and leaves a stored
/// blob alone. An engine that reads the <c>ww_consent</c> cookie directly and treats any cookie as a
/// decision gets the third state wrong in the worst direction — a cookie written against an older
/// consent config counts as consent nobody gave.
/// </para>
/// <para>
/// The companion rule lives in the consent engines: they must announce the restored or defaulted state
/// from <c>initialize()</c>. Without it a returning visitor never produces a change event and a
/// consumer gated on consent waits forever.
/// </para>
/// </remarks>
public class AttributionConsentGateSourceTests
{
    private const string BlazorAttribution = "WildwoodComponents.Blazor/wwwroot/js/wildwood-attribution.js";
    private const string RazorAttribution = "WildwoodComponents.Razor/wwwroot/js/attribution.js";
    private const string BlazorConsent = "WildwoodComponents.Blazor/wwwroot/js/wildwood-consent.js";
    private const string RazorConsent = "WildwoodComponents.Razor/wwwroot/js/consent.js";

    public static TheoryData<string> AttributionEngines() => new() { BlazorAttribution, RazorAttribution };

    public static TheoryData<string> ConsentEngines() => new() { BlazorConsent, RazorConsent };

    [Theory]
    [MemberData(nameof(AttributionEngines))]
    public void AnUndecidedVisitorLeavesTheStoredBlobAlone(string engine)
    {
        var source = ReadSource(engine);

        // A null decision is the undecided state, and it returns BEFORE either branch that touches
        // storage: no write, and — the part a two-state gate gets wrong — no removal either.
        Assert.True(
            source.Contains("var decision = this._consentDecision(", StringComparison.Ordinal)
                || source.Contains("const decision = this._consentDecision(", StringComparison.Ordinal),
            $"{engine} must resolve the gate through _consentDecision.");
        Assert.Contains("if (!decision) return;", source, StringComparison.Ordinal);
        Assert.Contains("if (decision.granted) {", source, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AttributionEngines))]
    public void ADecisionPrefersTheConsentEngineOverTheCookie(string engine)
    {
        var source = ReadSource(engine);

        // The consent engine's state has already applied the config version, config.enabled and the
        // GPC forced-off categories, so it is read first and the cookie is only the fallback.
        var enginePosition = source.IndexOf("readConsentEngineState(", StringComparison.Ordinal);
        var cookiePosition = source.IndexOf("readConsentCookie(", StringComparison.Ordinal);
        Assert.True(enginePosition >= 0, $"{engine} must read the consent engine's state.");
        Assert.True(cookiePosition >= 0, $"{engine} must keep the cookie fallback.");
        Assert.True(
            source.IndexOf("var state = consentState", StringComparison.Ordinal) >= 0
                || source.IndexOf("const state = consentState", StringComparison.Ordinal) >= 0,
            $"{engine} must accept the state carried by a consent-change event.");
    }

    [Theory]
    [MemberData(nameof(AttributionEngines))]
    public void AStaleOrUnverifiableCookieIsNotAGrant(string engine)
    {
        var source = ReadSource(engine);

        // Three things the old cookie-only gate ignored, each of which turned a non-decision into a grant.
        Assert.Contains("cookie.configVersion !== consentConfig.version", source, StringComparison.Ordinal);
        Assert.Contains("if (!consentConfig.enabled) return { granted: false }", source, StringComparison.Ordinal);
        Assert.Contains("GPC_FORCED_OFF", source, StringComparison.Ordinal);

        // And with no consent config to judge the cookie against, the gate stays undecided rather than
        // guessing: it fails closed for persistence.
        Assert.Contains("this._loadConsentConfig();", source, StringComparison.Ordinal);
        Assert.Contains("/consent/config?appId=", source, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AttributionEngines))]
    public void TheOldCookieFirstHelperIsGone(string engine)
    {
        // readConsentCategories() answered "granted" from any parseable cookie. Nothing may call it again.
        Assert.DoesNotContain("readConsentCategories", ReadSource(engine), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(AttributionEngines))]
    public void TheStorageKeyIsUnchanged(string engine)
    {
        // Shared with @wildwood/core and WildwoodStorageKeys.Attribution; the parity check hard-fails on drift.
        Assert.Contains("'ww_attribution'", ReadSource(engine), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(ConsentEngines))]
    public void InitializeAnnouncesTheRestoredOrDefaultedState(string engine)
    {
        var source = ReadSource(engine);

        // The decision table runs in _initializeState; initialize() wraps it and emits exactly once, for
        // every path through it — including the one where a valid cookie means nothing else changes.
        Assert.Contains("_initializeState", source, StringComparison.Ordinal);
        Assert.Contains("_emitChange", source, StringComparison.Ordinal);
        Assert.Contains("wildwood:consent-change", source, StringComparison.Ordinal);

        // The non-target default runs inside initialize, so an emit there would be the second one.
        var nonTarget = Section(source, "_applyNonTargetDefault");
        Assert.DoesNotContain("_emitChange()", nonTarget, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body of one method: from its definition — the LAST mention of the name, the call sites
    /// coming first in both engines — to the line that closes it. Crude on purpose: it only has to
    /// scope an assertion to a single function in a file this test already pins by name.
    /// </summary>
    private static string Section(string source, string methodName)
    {
        var start = source.LastIndexOf(methodName, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected a {methodName} method.");

        var end = source.Length;
        foreach (var close in new[] { "\n  }\n", "\n    };\n" })
        {
            var candidate = source.IndexOf(close, start, StringComparison.Ordinal);
            if (candidate >= 0 && candidate < end)
            {
                end = candidate;
            }
        }

        return source.Substring(start, end - start);
    }

    private static string ReadSource(string relativePath)
    {
        var full = Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"Expected {relativePath} to exist.");
        // Normalised so the section markers below do not depend on how the file was checked out.
        return File.ReadAllText(full).Replace("\r\n", "\n");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WildwoodComponents.Net.slnx")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find WildwoodComponents.Net.slnx above {AppContext.BaseDirectory}.");
    }
}
