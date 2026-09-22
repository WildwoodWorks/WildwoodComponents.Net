using System.Text.RegularExpressions;

namespace WildwoodComponents.Testing;

// Every budget the drivers spend, and the pattern they read a response by.
//
// Ported from the literals and the two per-call option objects in
// packages/wildwood-react/src/testing/flows.ts. JS writes most of these numbers at their use sites
// and lets exactly two of them be overridden - `settleMs` and `acceptResponsePattern`, through
// AcceptDisclaimersOptions / FinishSignupOptions. This port gathers them into one type instead: a
// compiled spec has to declare an options object somewhere anyway, and one type covering every
// driver is less to learn than three covering a third of it each.
//
// The DEFAULTS are flows.ts's literals, value for value, so a spec that passes no options behaves
// as the React helpers do. Every property below names the number it came from.
//
// They are also what lets a deliberate failure be proved quickly. The accept loop's shipped budget
// is ten clicks and three rate-limit waits, and the processing window is two minutes; a test that
// proved either bound at those numbers would spend minutes failing on purpose.

/// <summary>
/// The timeouts, budgets and acceptance-response pattern the signup drivers use.
/// </summary>
/// <remarks>
/// Every property carries flows.ts's value, so <c>new WildwoodTestingOptions()</c> is the React
/// behaviour. A driver that is passed none constructs one, rather than sharing a single instance,
/// so a spec that edits its own options cannot change anyone else's defaults.
/// </remarks>
public sealed class WildwoodTestingOptions
{
    /// <summary>
    /// Which response URLs count as the acceptance call, for the 429 back-off and the "refused by
    /// the server, HTTP n" diagnostic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to <see cref="WildwoodTestSelectors.AcceptResponsePattern"/>, which matches both the
    /// direct API path and the proxied one a server-rendered host uses. Override it when the host
    /// proxies acceptance under a path of its own.
    /// </para>
    /// <para>
    /// There is nothing to override on a stack that makes the call from the SERVER: Blazor Server
    /// accepts over its circuit, so the browser sees no response at all and no pattern can match
    /// one. The diagnostic degrades to the generic bounded-failure message there, which is a
    /// property of where the call is made rather than of this setting.
    /// </para>
    /// </remarks>
    public Regex AcceptResponsePattern { get; set; } = WildwoodTestSelectors.AcceptResponsePattern;

    /// <summary>
    /// How long to wait for the pending list to render before concluding there is nothing to
    /// accept. flows.ts: 15s.
    /// </summary>
    /// <remarks>
    /// Only matters on entry - once a control is on screen the accept loop drives itself.
    /// </remarks>
    public int DisclaimerSettleMs { get; set; } = 15_000;

    /// <summary>
    /// How long the signup's disclaimers step is given to render an Accept, its retry, or the
    /// success panel. flows.ts: 30s.
    /// </summary>
    public int DisclaimerRenderTimeoutMs { get; set; } = 30_000;

    /// <summary>How long one gate checkbox is given to accept a tick. flows.ts: 5s.</summary>
    public int DisclaimerGateTimeoutMs { get; set; } = 5_000;

    /// <summary>
    /// How many Accept clicks (or load retries) the loop spends before failing loudly. flows.ts: 10.
    /// </summary>
    /// <remarks>
    /// Bounded on purpose: an Accept that never goes away is a real defect and has to be reported as
    /// one rather than spin until the spec's own timeout, where it would read as a hang with no
    /// cause.
    /// </remarks>
    public int AcceptAttempts { get; set; } = 10;

    /// <summary>How long to leave after an Accept click before reading the page. flows.ts: 400ms.</summary>
    public int AcceptPauseMs { get; set; } = 400;

    /// <summary>How long to leave after clicking the component's own retry. flows.ts: 1s.</summary>
    public int RetryPauseMs { get; set; } = 1_000;

    /// <summary>How many HTTP 429s to wait out before giving up. flows.ts: 3.</summary>
    public int RateLimitWaits { get; set; } = 3;

    /// <summary>
    /// How long to wait for the rate-limit window to replenish after a 429. flows.ts: 20s.
    /// </summary>
    public int RateLimitPauseMs { get; set; } = 20_000;

    /// <summary>
    /// How long the flow is given to reach success, disclaimers or a failure. flows.ts: 120s.
    /// </summary>
    /// <remarks>
    /// Generous because the window covers registering the account, signing it in, linking the
    /// payment and activating the plan - several server round trips.
    /// </remarks>
    public int ProcessingTimeoutMs { get; set; } = 120_000;

    /// <summary>How many times to retry a failed processing step. flows.ts: 2.</summary>
    public int SignupRetries { get; set; } = 2;

    /// <summary>
    /// How long the flow is given to leave the failed step after Try Again. flows.ts: 30s.
    /// </summary>
    /// <remarks>
    /// The wait exists so the next poll cannot re-read the same failure and burn a retry on it.
    /// </remarks>
    public int LeaveFailedTimeoutMs { get; set; } = 30_000;

    /// <summary>How long to wait for the success step once disclaimers are done. flows.ts: 60s.</summary>
    public int SuccessTimeoutMs { get; set; } = 60_000;

    /// <summary>How long to wait for the text the success panel must show. flows.ts: 30s.</summary>
    public int SuccessTextTimeoutMs { get; set; } = 30_000;

    /// <summary>How long to wait for the consent banner to appear. flows.ts: 5s.</summary>
    /// <remarks>
    /// A banner that never appears is not a failure - most environments configure none - so this
    /// budget is spent and the driver returns having done nothing.
    /// </remarks>
    public int ConsentBannerWaitMs { get; set; } = 5_000;

    /// <summary>How long to wait for the answered consent banner to go away. flows.ts: 10s.</summary>
    public int ConsentBannerHiddenMs { get; set; } = 10_000;
}
