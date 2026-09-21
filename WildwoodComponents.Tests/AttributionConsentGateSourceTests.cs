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
    /// The banner is <c>position: fixed</c>, so it covers whatever the host anchors to the same
    /// edge — in the React package it was found sitting on a host's chat composer, where the send
    /// button was visible, enabled, and did nothing. Both engines therefore measure the banner and
    /// reserve that much room, the same way React's <c>reserveSpace</c> effect does.
    /// </summary>
    /// <remarks>
    /// Source-checked for the same reason as everything else in this file: the behaviour is a
    /// browser measurement, and this repository runs no JS test harness. The height is MEASURED
    /// rather than assumed (it depends on the configured copy and on how it wraps), it is ADDED to
    /// what the page already asks for rather than replacing it, and the observer is guarded
    /// because a consent banner must never be the reason a host's page fails to render. The
    /// arithmetic underneath is covered for real by the Node self-test — see
    /// <see cref="Razor.ConsentReserveSpaceSelfTestRunnerTests"/>.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ConsentEngines))]
    public void TheBannerReservesTheRoomItTakes(string engine)
    {
        var source = ReadSource(engine);

        Assert.Contains("'--ww-consent-height'", source, StringComparison.Ordinal);
        Assert.Contains("el.offsetHeight", source, StringComparison.Ordinal);
        Assert.Contains("root.style.setProperty(CONSENT_HEIGHT_VAR, write.height);", source, StringComparison.Ordinal);

        // Added to the page's own padding, read before ours goes on.
        Assert.Contains("getComputedStyle(body)[edge]", source, StringComparison.Ordinal);
        Assert.Contains(
            "if (tallest >= 0) return { edge: edge, value: (record.base + tallest) + 'px' };",
            source,
            StringComparison.Ordinal);

        Assert.Contains("typeof ResizeObserver !== 'undefined'", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// And gives every bit of it back when the banner goes: the observer, the published height,
    /// and the page's own inline padding — RESTORED, not zeroed, so a host that had its own keeps
    /// it, and REMOVED outright when it had none, so an empty or zero inline value cannot override
    /// the host's stylesheet.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConsentEngines))]
    public void TheReservedRoomIsGivenBackWhenTheBannerGoes(string engine)
    {
        var source = ReadSource(engine);

        Assert.Contains("if (held.observer) held.observer.disconnect();", source, StringComparison.Ordinal);
        Assert.Contains("root.style.removeProperty(CONSENT_HEIGHT_VAR);", source, StringComparison.Ordinal);

        // Restored when the page had one of its own; removed as a declaration when it had none.
        Assert.Contains(
            "return { edge: edge, value: record.previous === '' ? null : record.previous };",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (p.value === null) body.style.removeProperty(edgeProperty(p.edge));",
            source,
            StringComparison.Ordinal);
        Assert.Contains("else body.style[p.edge] = p.value;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host that would rather place the room itself opts out and still gets the measurement: the
    /// height is published either way, and only the padding is conditional.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConsentEngines))]
    public void TheHostCanPlaceTheRoomItself(string engine)
    {
        var source = ReadSource(engine);

        // The opt-out is the first thing the edge rule asks, so it beats every position...
        Assert.Contains("if (reserve === false) return null;", source, StringComparison.Ordinal);
        Assert.Contains("reservedEdge(positionOf(el), reserve)", source, StringComparison.Ordinal);

        // ...and only the PADDING is conditional: the height is published for every live banner.
        Assert.Contains("return { height: publishedHeight(ledger), padding: writes };", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The corner position pads NOTHING. It is a ~420px card inset from the bottom-right, not a
    /// full-width bar, so reserving body padding for it would leave a blank strip across the whole
    /// page for as long as the banner is up — a worse bug than the one the reservation fixes, and
    /// one that would hit every app on the default position.
    /// </summary>
    [Theory]
    [MemberData(nameof(ConsentEngines))]
    public void ACornerCardNeverPadsThePage(string engine)
    {
        var source = ReadSource(engine);

        // Only the two BARS name an edge; everything else — corner included, and any position a
        // later config invents — falls through to null.
        Assert.Contains("if (position === 'topBar') return 'paddingTop';", source, StringComparison.Ordinal);
        Assert.Contains("if (position === 'bottomBar') return 'paddingBottom';", source, StringComparison.Ordinal);

        // The one-line rule that treated the corner as a bottom bar is gone and must not come back.
        Assert.DoesNotContain(
            "position === 'topBar' ? 'paddingTop' : 'paddingBottom'", source, StringComparison.Ordinal);

        // A null edge writes no padding at all, while the height is still published.
        Assert.Contains("if (!edge) return null;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every banner holds its OWN reservation, keyed by the token its element is tagged with.
    /// </summary>
    /// <remarks>
    /// The first port kept the release closure in one module-scoped variable, which is wrong as
    /// soon as a page has two banners: in Blazor they share the scoped <c>IConsentService</c>, and
    /// so one cached copy of the module, so the second one's reservation released the first one's
    /// while it was still on screen — and the first component's C#-side flag went on believing it
    /// held room the page had already given back. The rule now: the page reserves the TALLEST live
    /// claim on an edge, the page's own padding is captured once before the first claim and
    /// restored only when the last one goes, and a token holding nothing is a no-op so a double
    /// release cannot strand or steal anyone's padding.
    /// </remarks>
    [Theory]
    [MemberData(nameof(ConsentEngines))]
    public void EveryBannerHoldsItsOwnReservation(string engine)
    {
        var source = ReadSource(engine);

        // No "the last call wins" variable anywhere.
        Assert.DoesNotContain("_releaseSpace", source, StringComparison.Ordinal);

        // A per-element token, and a registry keyed by it.
        Assert.Contains("'data-ww-consent-space'", source, StringComparison.Ordinal);
        Assert.Contains("var token = key || tagged || nextSpaceKey();", source, StringComparison.Ordinal);
        Assert.Contains("_space.held[token] = { el: el, observer: observer };", source, StringComparison.Ordinal);
        Assert.Contains("var write = releaseFromLedger(_space.ledger, token);", source, StringComparison.Ordinal);

        // Tallest, not sum: two banners on one edge overlap.
        Assert.Contains(
            "if (ledger.banners[i].edge === edge && ledger.banners[i].height > tallest) {",
            source,
            StringComparison.Ordinal);

        // Captured once, before the first claim on the edge, and forgotten when it drains.
        Assert.Contains("if (edge && !ledger.edges[edge]) {", source, StringComparison.Ordinal);
        Assert.Contains("delete ledger.edges[edge];", source, StringComparison.Ordinal);

        // A key that holds nothing writes nothing: released twice, or never reserved at all.
        Assert.Contains("if (index < 0) return null;", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The two copies of the bookkeeping are the same text. It is duplicated because the Blazor
    /// engine is an ES module and cannot share this file's CommonJS self-test hook — so the Razor
    /// copy is the one Node covers, and this is what stops the pair drifting apart.
    /// </summary>
    /// <remarks>
    /// Compared line by line with each line TRIMMED: the Razor copy lives inside an IIFE and so
    /// carries four more spaces of indentation on every line. Nothing else about it may differ.
    /// </remarks>
    [Fact]
    public void TheBookkeepingIsTheSameTextInBothEngines()
    {
        Assert.Equal(SharedBookkeeping(BlazorConsent), SharedBookkeeping(RazorConsent));
    }

    /// <summary>
    /// The pure bookkeeping block, trimmed line by line so the Razor copy's IIFE indentation does
    /// not count as a difference.
    /// </summary>
    private static string SharedBookkeeping(string engine)
    {
        const string begin = "// ---- BEGIN shared reserve-space bookkeeping ----";
        const string end = "// ---- END shared reserve-space bookkeeping ----";

        var source = ReadSource(engine);
        var start = source.IndexOf(begin, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Expected {engine} to mark the start of the shared bookkeeping.");
        start += begin.Length;

        var stop = source.IndexOf(end, start, StringComparison.Ordinal);
        Assert.True(stop >= 0, $"Expected {engine} to mark the end of the shared bookkeeping.");

        var block = source.Substring(start, stop - start).Split('\n');
        Assert.True(block.Length > 50, $"Expected {engine}'s shared bookkeeping to be the whole block.");

        return string.Join("\n", block.Select(line => line.Trim()));
    }

    /// <summary>
    /// Opt-OUT, not opt-in, on both stacks: a host that says nothing gets a banner that cannot
    /// cover its page. Razor carries the switch as a data attribute the script reads; Blazor
    /// passes its parameter across the interop call.
    /// </summary>
    [Fact]
    public void TheReservationIsOnUnlessTheHostTurnsItOff()
    {
        Assert.Contains(
            "root.dataset.reserveSpace !== 'false'", ReadSource(RazorConsent), StringComparison.Ordinal);
        Assert.Contains(
            "data-reserve-space=\"@Model.ReserveSpace.ToString().ToLower()\"",
            ReadSource("WildwoodComponents.Razor/Views/Shared/Components/ConsentBanner/Default.cshtml"),
            StringComparison.Ordinal);

        Assert.True(new WildwoodComponents.Razor.Models.ConsentViewModel().ReserveSpace);
        Assert.True(new WildwoodComponents.Blazor.Components.Consent.ConsentBanner().ReserveSpace);
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
