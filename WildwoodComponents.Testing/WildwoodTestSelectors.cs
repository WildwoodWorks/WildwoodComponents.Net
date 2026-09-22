using System.Text.RegularExpressions;

namespace WildwoodComponents.Testing;

// The DOM contract these helpers key on, as plain data.
//
// Ported from packages/wildwood-react/src/testing/selectors.ts, and kept apart from the drivers for
// the two reasons that file gives. It is the shared vocabulary the three stacks that render this
// flow to a DOM are asked to honour - Blazor, Razor, React - so it should be readable in one place
// rather than inlined in a click. And it is the only part of the package a unit test can exercise
// without a browser; the drivers need a live page.
//
// React Native and SwiftUI are asked to honour the same WORDS and cannot honour these selectors:
// neither has a DOM, so `register`, `payment`, `submit-register` and the rest arrive as a `testID`
// and an `accessibilityIdentifier`. One test plan reads against all five stacks; only three of them
// are drivable from here.
//
// Ordered lists vs comma-joined lists. A CSS selector list (`a, b`) resolves in DOM order, not in
// list order, so taking the first match of one does NOT mean "prefer a". Where preference is what
// is wanted - the failure message, whose fallback matches an EARLIER element on a stack that
// renders every step panel at once - the list stays an array and the driver tries each entry in
// turn.

/// <summary>
/// The <c>data-ww-*</c> hooks the Registration and Subscription component publishes, as selectors.
/// </summary>
/// <remarks>
/// Public because a host writing a spec of its own - or another stack implementing the same
/// component - should key on these strings rather than copy them and drift.
/// </remarks>
public static class WildwoodTestSelectors
{
    /// <summary>The attribute every step name is published in.</summary>
    public const string StepAttribute = "data-ww-step";

    /// <summary>
    /// Reads the signup step.
    /// </summary>
    /// <remarks>
    /// Two forms because two stacks place it differently and both are correct: React and Blazor put
    /// <c>data-ww-step</c> on a child of the signup root (exactly one child exists at a time), while
    /// Razor keeps every panel in the DOM and mirrors the active step onto the ROOT instead - the
    /// only place a single readable value can live there. The root comes first in DOM order, so
    /// taking the first match picks the mirror where one exists and the child everywhere else.
    /// </remarks>
    public const string SignupStepSelector =
        "[data-ww-view=\"signup\"][data-ww-step], [data-ww-view=\"signup\"] [data-ww-step]";

    /// <summary>
    /// Reads the manage view's plan-change step. Every stack puts it on the root, so one form
    /// suffices.
    /// </summary>
    public const string ManageViewSelector = "[data-ww-view=\"manage\"]";

    /// <summary>The six fields the registration form collects, by their <c>data-ww-field</c> names.</summary>
    /// <remarks>
    /// An enum rather than JS's string union, which is the one thing this port gets for free:
    /// <c>RegistrationField.ConfirmPassword</c> cannot be misspelled, and a field added to the
    /// contract fails to compile until every stack's helper knows about it.
    /// </remarks>
    public enum RegistrationField
    {
        FirstName = 0,
        LastName = 1,
        Username = 2,
        Email = 3,
        Password = 4,
        ConfirmPassword = 5
    }

    /// <summary>Every field in the contract, in form order.</summary>
    public static readonly IReadOnlyList<RegistrationField> RegistrationFieldOrder = new[]
    {
        RegistrationField.FirstName,
        RegistrationField.LastName,
        RegistrationField.Username,
        RegistrationField.Email,
        RegistrationField.Password,
        RegistrationField.ConfirmPassword
    };

    /// <summary>The field's <c>data-ww-field</c> value - the contract's own spelling.</summary>
    public static string FieldName(RegistrationField field)
    {
        return field switch
        {
            RegistrationField.FirstName => "firstName",
            RegistrationField.LastName => "lastName",
            RegistrationField.Username => "username",
            RegistrationField.Email => "email",
            RegistrationField.Password => "password",
            RegistrationField.ConfirmPassword => "confirmPassword",
            _ => throw new ArgumentOutOfRangeException(
                nameof(field), field, "Not a registration field the DOM contract names.")
        };
    }

    /// <summary>
    /// The React ids, kept as a fallback rather than as the contract.
    /// </summary>
    /// <remarks>
    /// <c>data-ww-field</c> is the contract because an id cannot be one: Razor suffixes its ids with
    /// a per-instance component id (<c>ww-regsub-email-{cid}</c>), so no constant id selector can
    /// exist there, and a bare <c>id="email"</c> would collide with a host page's own. React keeps
    /// these ids - hosts may already depend on them - so the helper accepts either.
    /// </remarks>
    public static string FieldId(RegistrationField field)
    {
        return field switch
        {
            RegistrationField.FirstName => "ww-reg-first",
            RegistrationField.LastName => "ww-reg-last",
            RegistrationField.Username => "ww-reg-username",
            RegistrationField.Email => "ww-reg-email",
            RegistrationField.Password => "ww-reg-password",
            RegistrationField.ConfirmPassword => "ww-reg-confirm",
            _ => throw new ArgumentOutOfRangeException(
                nameof(field), field, "Not a registration field the DOM contract names.")
        };
    }

    /// <summary>
    /// Locates one registration field: the contract hook, or the React id.
    /// </summary>
    /// <remarks>
    /// Comma-joined rather than ordered on purpose - in React both halves resolve to the SAME
    /// element, and no stack renders one field under the contract hook and a different field under
    /// the id.
    /// </remarks>
    public static string RegistrationFieldSelector(RegistrationField field)
    {
        return "[data-ww-field=\"" + FieldName(field) + "\"], #" + FieldId(field);
    }

    /// <summary>
    /// Submits the registration form.
    /// </summary>
    /// <remarks>
    /// <c>button[type="submit"]</c> alone is not enough: a stack whose flow takes the card BEFORE
    /// creating the account has no <c>&lt;form&gt;</c> to submit, so its control is a
    /// <c>type="button"</c> carrying the action hook. Both are scoped to the register PANEL, which
    /// is what keeps the bare <c>button[type="submit"]</c> from picking up a submit belonging to
    /// some other step.
    /// </remarks>
    public static readonly string SubmitRegisterSelector = string.Join(", ",
        StepPanel("register") + " [data-ww-action=\"submit-register\"]",
        StepPanel("register") + " button[type=\"submit\"]");

    /// <summary>The failed step's Try Again.</summary>
    public static readonly string SignupRetrySelector = string.Join(", ",
        StepPanel("failed") + " [data-ww-action=\"signup-retry\"]",
        StepPanel("failed") + " .ww-btn-primary");

    /// <summary>The success panel's final button.</summary>
    public static readonly string SignupGetStartedSelector = string.Join(", ",
        ".ww-signup-success [data-ww-action=\"signup-get-started\"]",
        ".ww-signup-success .ww-btn-primary");

    /// <summary>
    /// The failure text, in PREFERENCE order - the first entry that matches anything wins.
    /// </summary>
    /// <remarks>
    /// Order matters here, unlike the comma-joined selectors above. The class fallback names a
    /// paragraph the processing steps share, so on a stack that keeps every step panel in the DOM
    /// it matches the <c>creating</c> step's "please wait" - which sits EARLIER in the document
    /// than the real error. A comma-joined list would therefore report boilerplate as the cause of
    /// a genuine failure.
    /// </remarks>
    public static readonly IReadOnlyList<string> SignupFailureMessageSelectors = new[]
    {
        StepPanel("failed") + " [data-ww-error-message]",
        ".ww-signup-processing .ww-text-muted"
    };

    /// <summary>
    /// Accept controls, comma-joined: one of them is the gate, and which one depends on the stack
    /// and on how many disclaimers are pending.
    /// </summary>
    public static readonly string AcceptSelector = string.Join(", ",
        "[data-ww-disclaimer-action=\"accept\"]",
        "[data-ww-disclaimer-action=\"accept-all\"]",
        ".ww-disclaimer-footer .ww-btn-primary",
        ".ww-disclaimer-actions .ww-btn-block");

    /// <summary>
    /// The load-failure retry.
    /// </summary>
    /// <remarks>
    /// The class-based fallback exists for builds that predate <c>data-ww-disclaimer-action</c>, and
    /// it must not outlive them: on a stack whose Accept All sits in <c>.ww-disclaimer-actions</c>
    /// as a plain <c>.ww-btn-primary</c>, it matched the Accept button - and because the loop tests
    /// RETRY first, the run clicked Accept as if it were a retry ten times and then reported "the
    /// disclaimers never loaded" about disclaimers that had loaded fine.
    /// <c>:not([data-ww-disclaimer-action])</c> confines the fallback to markup that names no
    /// actions at all, which is exactly the case it was written for.
    /// </remarks>
    public static readonly string RetrySelector = string.Join(", ",
        "[data-ww-disclaimer-action=\"retry\"]",
        ".ww-disclaimer-actions .ww-btn-primary:not(.ww-btn-block):not([data-ww-disclaimer-action])");

    /// <summary>
    /// The checkbox gate in front of Accept, in PREFERENCE order - the first entry that matches
    /// anything is taken to be the gate, and the rest are not consulted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// React has no gate (its Accept is disabled only while a request is in flight), so both miss
    /// and the tick step does nothing. Blazor and Razor disable Accept until every required box is
    /// ticked, and a click waits for actionability, so without a tick the helper hangs on the button
    /// and eventually blames the server.
    /// </para>
    /// <para>
    /// Both entries NAME the box. There is deliberately no bare <c>input[type="checkbox"]</c>
    /// fallback: the tick ticks every box the winning selector matches, so a loose last resort would
    /// opt the run into whatever else the panel happens to host ("email me updates") and - worse -
    /// would quietly cover for a stack that never adopted the contract, which is the failure this
    /// contract exists to expose. An unnamed gate fails loudly instead: Accept stays disabled, the
    /// click times out on it, and the missing hook is named in the error.
    /// </para>
    /// </remarks>
    public static readonly IReadOnlyList<string> DisclaimerCheckSelectors = new[]
    {
        "[data-ww-disclaimer-check]",
        ".ww-disclaimer-accept input[type=\"checkbox\"]"
    };

    /// <summary>
    /// Which responses count as "the acceptance call", for the 429 back-off and the "refused by the
    /// server, HTTP n" diagnostic.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both paths by default: a browser-side stack POSTs <c>api/disclaimeracceptance/accept</c> (and
    /// <c>.../accept-bulk</c>, which this also matches), while Razor proxies acceptance through its
    /// own <c>disclaimer-gate/accept</c>.
    /// </para>
    /// <para>
    /// On a stack that makes the call from the SERVER - Blazor Server, say - no pattern can help:
    /// the browser sees no response at all, so the diagnostic degrades to the generic bounded-
    /// failure message. That is documented rather than fixed, because it is not fixable from the
    /// browser.
    /// </para>
    /// <para>
    /// <see cref="RegexOptions.CultureInvariant"/> alongside <see cref="RegexOptions.IgnoreCase"/>
    /// so the match does not depend on the machine's culture: a Turkish locale lower-cases
    /// <c>I</c> to a dotless <c>i</c>, which would stop this matching the very URL it names.
    /// </para>
    /// </remarks>
    public static readonly Regex AcceptResponsePattern = new Regex(
        "(disclaimeracceptance|disclaimer-gate)/accept",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Does this response URL belong to the acceptance call?</summary>
    /// <remarks>
    /// A method rather than a bare <c>pattern.IsMatch(url)</c> so that the default lives in exactly
    /// one place and a host overriding it overrides one thing. The JS original has a second job
    /// here - stripping <c>/g</c> and <c>/y</c>, whose <c>lastIndex</c> makes <c>RegExp.test</c>
    /// stateful between calls - which .NET's <see cref="Regex"/> does not need: it carries no
    /// position between matches.
    /// </remarks>
    public static bool MatchesAcceptResponse(string url, Regex? pattern = null)
    {
        if (url is null) return false;
        return (pattern ?? AcceptResponsePattern).IsMatch(url);
    }

    /// <summary>
    /// One step's PANEL, never the view root, for scoping a control to the step that owns it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Reading the step and scoping to it are two different jobs, and the same attribute does both -
    /// which is the trap. <see cref="SignupStepSelector"/> deliberately accepts the step on the
    /// ROOT, because Razor keeps every panel in the DOM and mirrors the active step there so that
    /// one readable value exists. But that same mirroring makes
    /// <c>[data-ww-step="failed"] .ww-btn-primary</c> match every primary button under the root the
    /// moment the flow is on <c>failed</c> - including the hidden register panel's submit, which
    /// precedes the real Try Again in document order. The first match is then a hidden button, and
    /// a click on it waits on actionability until it times out: a hang, on the stack the mirroring
    /// was supposed to fix.
    /// </para>
    /// <para>
    /// The root is always the element carrying <c>data-ww-view</c>, and a step panel never carries
    /// one, in every stack that renders this flow. So excluding it costs nothing and keeps the loose
    /// class fallbacks scoped to the panel they were written for.
    /// </para>
    /// </remarks>
    private static string StepPanel(string step)
    {
        return "[data-ww-step=\"" + step + "\"]:not([data-ww-view])";
    }
}
