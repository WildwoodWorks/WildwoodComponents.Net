using System.Text.RegularExpressions;
using Microsoft.Playwright;
using WildwoodComponents.Shared.RegistrationSubscription;

namespace WildwoodComponents.Testing;

// Driving the signup flow end to end, and the two things that reliably get in its way on a real
// deployment: a pending disclaimer, and the consent banner.
//
// Ported from packages/wildwood-react/src/testing/flows.ts. Everything here keys on the structural
// hooks (data-ww-step, data-ww-field, data-ww-action, data-ww-disclaimer-action) rather than on
// button copy, because every label the component renders is host-configurable - in .NET through
// RegistrationSubscriptionLabels, the 95-string contract. The class-name and type="submit"
// selectors these helpers shipped with are kept as fallbacks so a host testing an older build is
// not told its component is broken. Every selector lives in WildwoodTestSelectors.
//
// Two ports of the same idea, JS and here:
//
//   - `import type { Page }` keeps Playwright out of the JS module at runtime. C# has no type-only
//     import, so the equivalent is the package boundary: this assembly is referenced by test
//     projects only, and TestingPackageBoundaryTests holds that line.
//   - Every public entry point takes an IPage or an ILocator and immediately wraps it in an
//     IFlowSurface - the dozen page operations these drivers perform, and nothing else. The rules
//     worth proving (the preference order, the bounded budgets, the 429 back-off, the gate tick
//     before the click, the subscribe/unsubscribe pair) then sit behind that seam and are exercised
//     in this repository, which has no browser in CI. PlaywrightFlowSurface is the one real
//     implementation; what it does per call is a single locator method, which a browser suite
//     proves and a unit test cannot.

/// <summary>
/// Drives the Registration and Subscription signup: the form, the disclaimers, the consent banner
/// and the finish.
/// </summary>
public static class WildwoodSignupFlows
{
    /// <summary>The consent banner, which takes the clicks aimed at whatever it covers.</summary>
    private const string ConsentBannerSelector = ".ww-consent-banner";

    /// <summary>The banner's accept button.</summary>
    private const string ConsentAcceptSelector = ".ww-consent-banner .ww-consent-btn-primary";

    /// <summary>The signup's disclaimers panel, which scopes the accept loop.</summary>
    private const string SignupDisclaimersSelector = ".ww-signup-disclaimers";

    /// <summary>The success panel, which is what "done" looks like while disclaimers are accepted.</summary>
    private const string SignupSuccessSelector = ".ww-signup-success";

    /// <summary>
    /// Either control the disclaimers panel can render - an Accept, or the load-failure retry.
    /// </summary>
    /// <remarks>
    /// Comma-joined rather than ordered: the question it answers is "has the panel rendered
    /// anything at all yet", and either answer ends the wait.
    /// </remarks>
    internal static readonly string AcceptOrRetrySelector =
        WildwoodTestSelectors.AcceptSelector + ", " + WildwoodTestSelectors.RetrySelector;

    private static readonly string SuccessStepName = StepNames.ForSignup(SignupStep.Done);
    private static readonly string DisclaimersStepName = StepNames.ForSignup(SignupStep.Disclaimers);
    private static readonly string FailedStepName = StepNames.ForSignup(SignupStep.Failed);

    #region The registration form

    /// <summary>
    /// Fills the registration form.
    /// </summary>
    /// <remarks>
    /// Each field is located by <c>data-ww-field</c> first and by its React id second - see
    /// <see cref="WildwoodTestSelectors.FieldId"/> for why the id cannot be the contract. The order
    /// is <see cref="WildwoodTestSelectors.RegistrationFieldOrder"/>, so a field added to the
    /// contract has to be given a value here before this compiles.
    /// </remarks>
    public static Task FillRegistrationFormAsync(IPage page, RegistrationFields user)
    {
        if (user is null)
        {
            throw new ArgumentNullException(nameof(user));
        }

        return FillRegistrationFormAsync(PlaywrightFlowSurface.ForPage(page), user);
    }

    internal static async Task FillRegistrationFormAsync(IFlowSurface surface, RegistrationFields user)
    {
        foreach (var field in WildwoodTestSelectors.RegistrationFieldOrder)
        {
            await surface.FillAsync(
                WildwoodTestSelectors.RegistrationFieldSelector(field), ValueFor(field, user))
                .ConfigureAwait(false);
        }
    }

    /// <summary>What this driver types into one field.</summary>
    /// <remarks>
    /// The confirmation takes the password itself. A spec that wants the two to differ is testing
    /// the form's validation rather than driving a signup, and fills that field itself.
    /// </remarks>
    private static string ValueFor(WildwoodTestSelectors.RegistrationField field, RegistrationFields user)
    {
        return field switch
        {
            WildwoodTestSelectors.RegistrationField.FirstName => user.FirstName,
            WildwoodTestSelectors.RegistrationField.LastName => user.LastName,
            WildwoodTestSelectors.RegistrationField.Username => user.Username,
            WildwoodTestSelectors.RegistrationField.Email => user.Email,
            WildwoodTestSelectors.RegistrationField.Password => user.Password,
            WildwoodTestSelectors.RegistrationField.ConfirmPassword => user.Password,
            _ => throw new ArgumentOutOfRangeException(
                nameof(field), field, "Not a registration field the DOM contract names.")
        };
    }

    /// <summary>
    /// Submits the registration form.
    /// </summary>
    /// <remarks>
    /// Structurally, not by name: the component labels this button differently depending on whether
    /// a plan step follows or the card comes next, and both strings are configurable.
    /// <c>data-ww-action="submit-register"</c> is the contract and <c>button[type="submit"]</c> the
    /// fallback.
    /// </remarks>
    public static Task SubmitRegistrationFormAsync(IPage page)
    {
        return SubmitRegistrationFormAsync(PlaywrightFlowSurface.ForPage(page));
    }

    internal static Task SubmitRegistrationFormAsync(IFlowSurface surface)
    {
        return surface.ClickAsync(WildwoodTestSelectors.SubmitRegisterSelector);
    }

    #endregion

    #region The consent banner

    /// <summary>
    /// Answers the consent banner if it is up.
    /// </summary>
    /// <remarks>
    /// The banner reserves its own height by default, so it no longer covers bottom-anchored UI -
    /// but it is still on screen and still takes the clicks aimed at it. A spec that interacts with
    /// the page underneath answers it first, exactly as a visitor would.
    /// </remarks>
    public static Task DismissConsentBannerAsync(IPage page, WildwoodTestingOptions? options = null)
    {
        return DismissConsentBannerAsync(PlaywrightFlowSurface.ForPage(page), options ?? new WildwoodTestingOptions());
    }

    internal static async Task DismissConsentBannerAsync(IFlowSurface surface, WildwoodTestingOptions options)
    {
        // A wait rather than a bare visibility read: a read never waits, so a banner that paints a
        // beat after the navigation slips straight past and the spec then fights it all test.
        var shown = await surface
            .WaitForVisibleAsync(ConsentBannerSelector, options.ConsentBannerWaitMs)
            .ConfigureAwait(false);

        if (!shown) return;

        await surface.ClickAsync(ConsentAcceptSelector).ConfigureAwait(false);

        var gone = await surface
            .WaitForHiddenAsync(ConsentBannerSelector, options.ConsentBannerHiddenMs)
            .ConfigureAwait(false);

        if (!gone)
        {
            // JS lets Playwright's own timeout surface here. This port answers with the package's
            // own failure instead, for the reason WildwoodContractException exists: the driver did
            // what it was told and the component did not do its half, which a timeout on an
            // anonymous locator does not say.
            throw new WildwoodContractException(
                "the consent banner was answered but never went away");
        }
    }

    #endregion

    #region Disclaimers

    /// <summary>
    /// Accepts whatever pending disclaimers are on screen.
    /// </summary>
    /// <param name="scope">The panel that holds them - the disclaimers step, or the component.</param>
    /// <param name="isDone">
    /// Answers true once the caller considers the flow finished (the success panel is up, say), so
    /// acceptance can stop early rather than wait for the panel to empty.
    /// </param>
    /// <param name="options">Budgets and the acceptance-response pattern; flows.ts's by default.</param>
    /// <remarks>
    /// <para>
    /// Bounded on purpose: an Accept that never goes away is a real defect and has to fail loudly
    /// rather than spin until the spec's own timeout, where it would be reported as a hang with no
    /// cause.
    /// </para>
    /// <para>
    /// Two failure modes this handles, both found against a live deployment and neither visible on a
    /// local stack. The accept POST can be REFUSED - acceptance sits under the API's per-IP auth
    /// limiter together with login and register, so a suite enrolling several users a minute from
    /// one address gets 429s, and the component leaves the button where it is, which without
    /// watching the response reads as "the button does nothing". Or the pending list can FAIL TO
    /// LOAD, in which case the component renders its own retry instead of an Accept, and waiting
    /// only for an Accept times out on a button that is never coming.
    /// </para>
    /// <para>
    /// And one that is a stack difference rather than a failure: where Accept is gated on required
    /// checkboxes - both .NET stacks - they are ticked before the click. See
    /// <see cref="TickDisclaimerGateAsync"/>.
    /// </para>
    /// </remarks>
    public static Task AcceptDisclaimersAsync(
        ILocator scope,
        Func<Task<bool>>? isDone = null,
        WildwoodTestingOptions? options = null)
    {
        return AcceptDisclaimersAsync(
            PlaywrightFlowSurface.ForScope(scope), isDone, options ?? new WildwoodTestingOptions());
    }

    internal static async Task AcceptDisclaimersAsync(
        IFlowSurface scope,
        Func<Task<bool>>? isDone,
        WildwoodTestingOptions options)
    {
        var watcher = new AcceptResponseWatcher(options.AcceptResponsePattern);

        // `using`, so the handler comes off the page however this ends - and every one of its
        // bounded-failure paths ends by throwing. A handler left attached to a long-lived page runs
        // for every response that page makes for the rest of the spec, and keeps this closure and
        // the watcher it writes to alive with it.
        using (scope.WatchAcceptResponses(watcher))
        {
            var clicks = 0;
            var rateLimitWaits = 0;

            // The component fetches its pending list when the step mounts, so on entry the panel may
            // hold neither an Accept nor the retry yet. Without this wait, "nothing on screen" is
            // indistinguishable from "nothing left to accept" and this returns having silently done
            // nothing - a success the caller cannot tell from a real one. A panel that genuinely has
            // no disclaimers falls through after the settle budget and returns, as it should.
            if (isDone is null || !await isDone().ConfigureAwait(false))
            {
                await scope.WaitForVisibleAsync(AcceptOrRetrySelector, options.DisclaimerSettleMs)
                    .ConfigureAwait(false);
            }

            for (; ; )
            {
                if (await scope.IsVisibleAsync(WildwoodTestSelectors.RetrySelector).ConfigureAwait(false))
                {
                    if (rateLimitWaits + clicks >= options.AcceptAttempts)
                    {
                        throw new WildwoodContractException(
                            "The disclaimers never loaded - the component's own retry stayed on screen.");
                    }

                    await scope.ClickAsync(WildwoodTestSelectors.RetrySelector).ConfigureAwait(false);
                    await scope.DelayAsync(options.RetryPauseMs).ConfigureAwait(false);
                    clicks++;
                    continue;
                }

                if (await scope.CountAsync(WildwoodTestSelectors.AcceptSelector).ConfigureAwait(false) == 0)
                {
                    return;
                }

                if (clicks >= options.AcceptAttempts)
                {
                    throw new WildwoodContractException(watcher.LastStatus >= 400
                        ? "Disclaimer accept was refused by the server - last response HTTP "
                            + watcher.LastStatus + ", after " + options.AcceptAttempts + " attempts."
                        : "Disclaimer accept did not converge after " + options.AcceptAttempts
                            + " clicks - is an Accept button stuck disabled?");
                }

                watcher.Reset();

                // Before the click, not after a hang: where Accept is gated on checkboxes, an
                // unticked gate leaves it disabled and a click waits for actionability until the
                // spec's own timeout.
                await TickDisclaimerGateAsync(scope, options).ConfigureAwait(false);
                await scope.ClickAsync(WildwoodTestSelectors.AcceptSelector).ConfigureAwait(false);
                await scope.DelayAsync(options.AcceptPauseMs).ConfigureAwait(false);

                if (isDone is not null && await isDone().ConfigureAwait(false)) return;

                // A 429 is the server pacing us, not a defect: wait for the window to replenish
                // instead of spending the click budget on refusals.
                if (watcher.LastStatus == 429)
                {
                    if (rateLimitWaits >= options.RateLimitWaits)
                    {
                        throw new WildwoodContractException(
                            "Disclaimer accept kept returning HTTP 429. Acceptance shares the API's "
                            + "per-IP auth rate limit with login and register - enroll fewer users per "
                            + "minute from one address, or raise the limit for the environment under test.");
                    }

                    rateLimitWaits++;
                    await scope.DelayAsync(options.RateLimitPauseMs).ConfigureAwait(false);
                    continue; // a refused attempt proved nothing, so it must not spend the click budget
                }

                clicks++;
            }
        }
    }

    /// <summary>
    /// Ticks whatever gates the Accept button, where one exists.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not every stack has one: React disables Accept only while a request is in flight, so nothing
    /// here matches and this returns having done nothing. Blazor and Razor disable it until every
    /// required box is ticked.
    /// </para>
    /// <para>
    /// A box that cannot be ticked is not evidence of anything on its own, so a refusal is ignored -
    /// the Accept click that follows produces the real diagnostic.
    /// </para>
    /// </remarks>
    internal static async Task TickDisclaimerGateAsync(IFlowSurface scope, WildwoodTestingOptions options)
    {
        foreach (var selector in WildwoodTestSelectors.DisclaimerCheckSelectors)
        {
            var count = await scope.CountAsync(selector).ConfigureAwait(false);
            if (count == 0) continue;

            for (var i = 0; i < count; i++)
            {
                if (await scope.IsCheckedAsync(selector, i).ConfigureAwait(false)) continue;
                await scope.CheckAsync(selector, i, options.DisclaimerGateTimeoutMs).ConfigureAwait(false);
            }

            // The first list entry that exists IS the gate - the later entries are broader fallbacks
            // for markup that does not name its checkboxes, and running them too would tick
            // unrelated boxes.
            return;
        }
    }

    #endregion

    #region Finishing

    /// <summary>
    /// Drives the signup from wherever it is to the finish: waits out processing, retries a
    /// transient failure, accepts any pending disclaimers, then clicks the success button.
    /// </summary>
    /// <remarks>
    /// Nothing is located by name: the retry and the final button are label-contract strings hosts
    /// routinely reword. Both are found by their <c>data-ww-action</c> hook, falling back to the
    /// class selectors these helpers shipped with.
    /// </remarks>
    public static Task FinishSignupAsync(IPage page, WildwoodTestingOptions? options = null)
    {
        return FinishSignupAsync(
            PlaywrightFlowSurface.ForPage(page),
            () => WildwoodSignupSteps.SignupStepAsync(page),
            expectText: null,
            expectPattern: null,
            options ?? new WildwoodTestingOptions());
    }

    /// <summary>
    /// Drives the signup to the finish and asserts what the success panel says.
    /// </summary>
    /// <param name="page">The page the signup is on.</param>
    /// <param name="expectSuccessText">
    /// Text the success panel must show. Matched as Playwright matches text: a substring, case
    /// insensitively, with whitespace normalised.
    /// </param>
    /// <param name="options">Budgets and the acceptance-response pattern; flows.ts's by default.</param>
    /// <remarks>
    /// The assertion belongs here rather than to the caller: the panel is the only place the
    /// completion message appears, the click navigates away from it, and the disclaimers step can
    /// sit between payment and success - so a caller asserting straight after the card sees the
    /// disclaimer instead. Any environment with real terms configured breaks that assumption.
    /// </remarks>
    public static Task FinishSignupAsync(
        IPage page,
        string expectSuccessText,
        WildwoodTestingOptions? options = null)
    {
        if (expectSuccessText is null)
        {
            throw new ArgumentNullException(nameof(expectSuccessText));
        }

        return FinishSignupAsync(
            PlaywrightFlowSurface.ForPage(page),
            () => WildwoodSignupSteps.SignupStepAsync(page),
            expectSuccessText,
            expectPattern: null,
            options ?? new WildwoodTestingOptions());
    }

    /// <summary>
    /// Drives the signup to the finish and asserts the success panel against a pattern.
    /// </summary>
    /// <remarks>
    /// The pattern form of the overload above, for a message carrying something that varies - an
    /// order number, a plan name.
    /// </remarks>
    public static Task FinishSignupAsync(
        IPage page,
        Regex expectSuccessText,
        WildwoodTestingOptions? options = null)
    {
        if (expectSuccessText is null)
        {
            throw new ArgumentNullException(nameof(expectSuccessText));
        }

        return FinishSignupAsync(
            PlaywrightFlowSurface.ForPage(page),
            () => WildwoodSignupSteps.SignupStepAsync(page),
            expectText: null,
            expectSuccessText,
            options ?? new WildwoodTestingOptions());
    }

    internal static async Task FinishSignupAsync(
        IFlowSurface surface,
        Func<Task<string?>> readStep,
        string? expectText,
        Regex? expectPattern,
        WildwoodTestingOptions options)
    {
        string? settled;

        for (var attempt = 0; ; attempt++)
        {
            // Generous: this window covers registering the account, signing it in, linking the
            // payment and activating the plan - several server round trips.
            settled = await Poll.UntilValueAsync<string?>(
                readStep,
                step => string.Equals(step, SuccessStepName, StringComparison.Ordinal)
                    || string.Equals(step, DisclaimersStepName, StringComparison.Ordinal)
                    || string.Equals(step, FailedStepName, StringComparison.Ordinal),
                options.ProcessingTimeoutMs,
                last => "the signup never reached " + SuccessStepName + ", " + DisclaimersStepName
                    + " or a failure - it is on \"" + (last ?? WildwoodSignupSteps.NotRendered) + "\"")
                .ConfigureAwait(false);

            // The value the wait ended on, rather than a second read of the page: one read, and the
            // answer this loop was given.
            if (!string.Equals(settled, FailedStepName, StringComparison.Ordinal)) break;

            var message = await FirstPresentTextAsync(
                surface, WildwoodTestSelectors.SignupFailureMessageSelectors).ConfigureAwait(false);

            if (attempt >= options.SignupRetries)
            {
                throw new WildwoodContractException("Signup processing failed: " + message);
            }

            // The flow tracks completed sub-steps, so its retry resumes where it failed rather than
            // registering the user a second time.
            await surface.ClickAsync(WildwoodTestSelectors.SignupRetrySelector).ConfigureAwait(false);

            // Leave the failed step before polling again, or the next poll re-reads this same
            // failure and burns a retry on it.
            await WildwoodSignupSteps.WaitForSignupStepToLeaveAsync(
                readStep, SignupStep.Failed, options.LeaveFailedTimeoutMs).ConfigureAwait(false);
        }

        if (string.Equals(settled, DisclaimersStepName, StringComparison.Ordinal))
        {
            var disclaimers = surface.Scope(SignupDisclaimersSelector);
            Task<bool> SuccessShownAsync() => surface.IsVisibleAsync(SignupSuccessSelector);

            // The step's container renders before the disclaimer list has been fetched, so an
            // immediate Accept count is 0 and the accept loop would return having clicked nothing.
            await Poll.UntilAsync(
                async () => await disclaimers.IsVisibleAsync(AcceptOrRetrySelector).ConfigureAwait(false)
                    || await SuccessShownAsync().ConfigureAwait(false),
                options.DisclaimerRenderTimeoutMs,
                () => "the disclaimers step rendered neither an Accept, its retry, nor the success panel")
                .ConfigureAwait(false);

            await AcceptDisclaimersAsync(disclaimers, SuccessShownAsync, options).ConfigureAwait(false);
        }

        await WildwoodSignupSteps.WaitForSignupStepAsync(readStep, SignupStep.Done, options.SuccessTimeoutMs)
            .ConfigureAwait(false);

        if (expectText is not null || expectPattern is not null)
        {
            var shown = await surface
                .WaitForTextAsync(expectText, expectPattern, options.SuccessTextTimeoutMs)
                .ConfigureAwait(false);

            if (!shown)
            {
                throw new WildwoodContractException(
                    "the success panel never showed "
                    + (expectPattern is not null ? expectPattern.ToString() : "\"" + expectText + "\""));
            }
        }

        await surface.ClickAsync(WildwoodTestSelectors.SignupGetStartedSelector).ConfigureAwait(false);
    }

    /// <summary>
    /// The text of the first selector in <paramref name="candidates"/> that matches anything, or an
    /// empty string.
    /// </summary>
    /// <remarks>
    /// For lists where PREFERENCE is the point. A CSS selector list resolves in document order, so
    /// <c>a, b</c> plus "the first one" can hand back <c>b</c> when <c>a</c> also matched - and for
    /// <see cref="WildwoodTestSelectors.SignupFailureMessageSelectors"/> it would, on a stack that
    /// keeps every step panel in the DOM: the class fallback names a paragraph the processing steps
    /// share, which sits earlier in the document than the real error, so the joined form reports
    /// boilerplate as the cause of a genuine failure.
    /// </remarks>
    internal static async Task<string> FirstPresentTextAsync(
        IFlowSurface surface,
        IReadOnlyList<string> candidates)
    {
        foreach (var selector in candidates)
        {
            if (await surface.CountAsync(selector).ConfigureAwait(false) == 0) continue;
            return await surface.TextAsync(selector).ConfigureAwait(false) ?? string.Empty;
        }

        return string.Empty;
    }

    #endregion
}

/// <summary>The fields the registration form collects, by their contract names.</summary>
public sealed class RegistrationFields
{
    /// <summary>The given name.</summary>
    public required string FirstName { get; init; }

    /// <summary>The family name.</summary>
    public required string LastName { get; init; }

    /// <summary>The username the account is created with.</summary>
    public required string Username { get; init; }

    /// <summary>The email address the account is created with.</summary>
    public required string Email { get; init; }

    /// <summary>The password. The confirmation field is filled with it too.</summary>
    public required string Password { get; init; }
}

/// <summary>
/// The last status the acceptance call answered with, as seen from the browser.
/// </summary>
/// <remarks>
/// <para>
/// What the 429 back-off and the "refused by the server, HTTP n" diagnostic are built on. Kept
/// apart from the page so the rules can be proved without one: it is handed a URL and a status and
/// decides whether that response was the acceptance call.
/// </para>
/// <para>
/// It answers nothing on a stack that accepts from the SERVER. Blazor Server posts acceptance over
/// its circuit, so the browser never sees the response and the status stays 0 - the bounded failure
/// then reports the generic "did not converge" message rather than an HTTP code. That is a property
/// of where the call is made, not something a pattern can fix, and it is why the watcher is worth
/// having anyway: Razor proxies acceptance through <c>disclaimer-gate/accept</c>, which the default
/// pattern matches, and a React or React Native host calls the API from the browser.
/// </para>
/// </remarks>
internal sealed class AcceptResponseWatcher
{
    private readonly Regex _pattern;

    // Volatile because the response event is raised by Playwright's own plumbing rather than by the
    // loop that reads this between awaits.
    private volatile int _lastStatus;

    internal AcceptResponseWatcher(Regex? pattern)
    {
        _pattern = pattern ?? WildwoodTestSelectors.AcceptResponsePattern;
    }

    /// <summary>The status of the last acceptance response seen, or 0 if none has been.</summary>
    internal int LastStatus => _lastStatus;

    /// <summary>Records one response, if it is the acceptance call.</summary>
    internal void Record(string url, int status)
    {
        if (WildwoodTestSelectors.MatchesAcceptResponse(url, _pattern)) _lastStatus = status;
    }

    /// <summary>Forgets what was seen, so one click's answer cannot be read as the next one's.</summary>
    internal void Reset()
    {
        _lastStatus = 0;
    }
}

/// <summary>
/// The page operations the signup drivers perform, and nothing else.
/// </summary>
/// <remarks>
/// <para>
/// The seam that lets the rules above be tested where there is no browser, in the same spirit as the
/// step READER the waits in <see cref="WildwoodSignupSteps"/> sit behind.
/// </para>
/// <para>
/// Reads answer rather than throw - a selector that matches nothing counts 0, is not visible, has no
/// text - exactly as JS's <c>.catch(() =&gt; ...)</c> at each of these call sites does. A box that
/// cannot be read counts as checked, so the tick leaves it alone. Actions (fill, click) are left to
/// throw: those are how the drivers surface a control that never became clickable.
/// </para>
/// </remarks>
internal interface IFlowSurface
{
    /// <summary>The same surface, narrowed to one element's subtree.</summary>
    IFlowSurface Scope(string selector);

    /// <summary>Fills the first match.</summary>
    Task FillAsync(string selector, string value);

    /// <summary>Clicks the first match.</summary>
    Task ClickAsync(string selector);

    /// <summary>How many elements match, over the whole scope rather than the first one.</summary>
    Task<int> CountAsync(string selector);

    /// <summary>Whether the first match is visible right now, without waiting for it.</summary>
    Task<bool> IsVisibleAsync(string selector);

    /// <summary>Waits for the first match to be visible. False if it never was.</summary>
    Task<bool> WaitForVisibleAsync(string selector, int timeoutMs);

    /// <summary>Waits for the first match to be gone. False if it stayed.</summary>
    Task<bool> WaitForHiddenAsync(string selector, int timeoutMs);

    /// <summary>The first match's text, or null when nothing matched.</summary>
    Task<string?> TextAsync(string selector);

    /// <summary>Whether one of the matches is ticked. True when it cannot be read.</summary>
    Task<bool> IsCheckedAsync(string selector, int index);

    /// <summary>Ticks one of the matches. False if it refused.</summary>
    Task<bool> CheckAsync(string selector, int index, int timeoutMs);

    /// <summary>Waits for text to be visible anywhere in the scope. False if it never was.</summary>
    Task<bool> WaitForTextAsync(string? text, Regex? pattern, int timeoutMs);

    /// <summary>Waits, for the pauses the accept loop leaves between clicks.</summary>
    Task DelayAsync(int ms);

    /// <summary>
    /// Starts reporting responses to <paramref name="watcher"/>; disposing stops it.
    /// </summary>
    IDisposable WatchAcceptResponses(AcceptResponseWatcher watcher);
}
