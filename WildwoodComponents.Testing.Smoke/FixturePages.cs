namespace WildwoodComponents.Testing.Smoke;

// The pages the drivers are put through, and the one rule that decides what they look like.
//
// These are NOT a stand-in for the components. Every hook the Blazor and Razor surfaces render is
// already pinned where it is rendered - WildwoodComponents.Tests reads Blazor's render tree back and
// Razor's shipped .cshtml and .js sources - and nothing here could pin it better. What no test in
// that project can do is put a DRIVER through the markup, because a driver needs a live page: a
// selector list that resolves in document order, a click that waits on a disabled control rather
// than failing, a MutationObserver that was installed before the mount, a response the browser
// actually reported back. That is what these pages exist for, and it is all they claim.
//
// The step names below are written out as literal strings rather than taken from StepNames. Reading
// them from the same table the assertions use would make the walk agree with itself and prove
// nothing; written here, they are the DOM contract as an independent author would spell it, and the
// helpers have to meet it.
//
// The shape is RAZOR's - every step panel left in the document and the active step mirrored onto
// the view root - because that is the harder of the two .NET surfaces to drive and the one whose
// traps are documented in WildwoodTestSelectors. Two of them are staged here deliberately:
//
//   - The register panel's `.ww-btn-primary` sits EARLIER in the document than the failed panel's
//     Try Again, so a retry selector that is not scoped off the root reaches a hidden button and
//     hangs on it.
//   - The creating panel's `.ww-signup-processing .ww-text-muted` sits EARLIER than the failed
//     panel's real error, so a comma-joined failure-message list reports boilerplate as the cause.

/// <summary>The staged pages the smoke run serves.</summary>
internal static class FixturePages
{
    /// <summary>
    /// Every <c>data-ww-step</c> name the signup contract publishes, in the order the views render
    /// their panels.
    /// </summary>
    /// <remarks>
    /// Spelled here rather than read from <c>StepNames</c> on purpose - see the file header. The two
    /// renames are the interesting entries: the machine's <c>Done</c> is <c>success</c> in the DOM,
    /// and <c>PackCheckout</c> keeps its camel case.
    /// </remarks>
    internal static readonly string[] SignupStepNames =
    {
        "loading", "closed", "register", "token", "plan", "packs",
        "payment", "creating", "disclaimers", "packCheckout", "success", "failed"
    };

    /// <summary>Every <c>data-ww-step</c> name a plan change publishes.</summary>
    internal static readonly string[] PlanChangeStepNames =
    {
        "idle", "previewing", "confirm", "collectingPayment", "authenticating",
        "completing", "changing", "done", "failed"
    };

    /// <summary>The path the staged accept button POSTs to, and the watcher's default pattern matches.</summary>
    internal const string AcceptPath = "/api/disclaimeracceptance/accept";

    /// <summary>What the staged failure says, so the assertion can name it exactly.</summary>
    internal const string FailureText = "The staged registration refused this account on purpose.";

    /// <summary>What the staged success panel says.</summary>
    internal const string SuccessText = "You are all set.";

    /// <summary>The boilerplate the processing panels share - what must NOT be reported as a cause.</summary>
    internal const string ProcessingBoilerplate = "This will only take a moment.";

    /// <summary>
    /// The signup page: the Razor shape, with a script that walks it the way the flow would.
    /// </summary>
    internal static string Signup()
    {
        return Document("Signup contract fixture", SignupBody(), SignupScript());
    }

    /// <summary>
    /// The manage page: a plan-change root whose step is set from the console, nothing else.
    /// </summary>
    internal static string Manage()
    {
        return Document(
            "Manage contract fixture",
            "<div class=\"ww-regsub ww-regsub-manage\" data-ww-view=\"manage\" data-ww-step=\"idle\">"
            + "<p>Plan change</p></div>",
            "window.__wwShowManage = (step) => "
            + "document.querySelector('[data-ww-view=\"manage\"]').setAttribute('data-ww-step', step);");
    }

    /// <summary>
    /// A page carrying only the consent banner, so answering it is proved on its own.
    /// </summary>
    internal static string Consent()
    {
        return Document(
            "Consent contract fixture",
            "<div class=\"ww-consent-banner\" role=\"dialog\">"
            + "<p>We use cookies.</p>"
            + "<button type=\"button\" class=\"ww-consent-btn ww-consent-btn-primary\">Accept all</button>"
            + "</div>",
            @"window.__wwConsentClicks = 0;
document.querySelector('.ww-consent-btn-primary').addEventListener('click', () => {
  window.__wwConsentClicks += 1;
  document.querySelector('.ww-consent-banner').hidden = true;
});");
    }

    private static string Document(string title, string body, string script)
    {
        return "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\" />\n<title>"
            + title + "</title>\n</head>\n<body>\n" + body + "\n<script>\n" + script + "\n</script>\n</body>\n</html>\n";
    }

    private static string SignupBody()
    {
        var html = new System.Text.StringBuilder();

        // The step on the ROOT, ahead of every panel's own, is the whole reason a reader can answer
        // on this shape at all: `[data-ww-view="signup"][data-ww-step]` comes first in the selector
        // list, and the list resolves in document order.
        html.Append("<div class=\"ww-regsub ww-regsub-signup\" data-ww-view=\"signup\" data-ww-step=\"loading\">\n");

        foreach (var step in SignupStepNames)
        {
            html.Append(Panel(step));
            html.Append('\n');
        }

        html.Append("</div>\n");
        return html.ToString();
    }

    private static string Panel(string step)
    {
        // Every panel is in the document at all times; `hidden` is what the script toggles. Only
        // `loading` starts visible, matching the root's initial step.
        var hidden = step == "loading" ? string.Empty : " hidden";

        return step switch
        {
            "register" =>
                "<div class=\"ww-signup-step\" data-ww-step=\"register\"" + hidden + ">"
                + Field("firstName") + Field("lastName") + Field("username")
                + Field("email") + Field("password", "password") + Field("confirmPassword", "password")
                // type="button", not submit, and no <form> behind it: the pay-first surfaces have no
                // form to submit, so the action hook is the only way in. It is also a .ww-btn-primary
                // sitting ahead of the failed panel's Try Again, which is the scoping trap.
                + "<button type=\"button\" class=\"ww-btn ww-btn-primary\" data-ww-action=\"submit-register\">"
                + "Continue</button></div>",

            "creating" =>
                "<div class=\"ww-signup-step ww-signup-processing\" data-ww-step=\"creating\"" + hidden + ">"
                + "<h3>Creating your account</h3>"
                + "<p class=\"ww-text-muted\">" + ProcessingBoilerplate + "</p></div>",

            "failed" =>
                "<div class=\"ww-signup-step ww-signup-processing\" data-ww-step=\"failed\"" + hidden + ">"
                + "<h3>Something went wrong</h3>"
                + "<p class=\"ww-text-muted\" data-ww-error-message>" + FailureText + "</p>"
                + "<button type=\"button\" class=\"ww-btn ww-btn-primary\" data-ww-action=\"signup-retry\">"
                + "Try again</button>"
                + "<button type=\"button\" class=\"ww-btn ww-btn-link\" data-ww-action=\"signup-start-over\">"
                + "Start over</button></div>",

            "disclaimers" =>
                "<div class=\"ww-signup-step ww-signup-disclaimers\" data-ww-step=\"disclaimers\"" + hidden + ">"
                + "<div class=\"ww-disclaimer-card\"><h3>Terms of service</h3>"
                + "<label><input type=\"checkbox\" data-ww-disclaimer-check /> I have read the terms</label>"
                + "</div>"
                // The exact shape the retry selector was tightened for: an Accept All sitting in
                // .ww-disclaimer-actions as a plain .ww-btn-primary, and disabled until the gate is
                // ticked.
                + "<div class=\"ww-disclaimer-actions\">"
                + "<button type=\"button\" class=\"ww-btn ww-btn-primary\" "
                + "data-ww-disclaimer-action=\"accept-all\" disabled>Accept all</button>"
                + "</div></div>",

            "success" =>
                "<div class=\"ww-signup-step ww-signup-success\" data-ww-step=\"success\"" + hidden + ">"
                + "<h3>All set</h3><p>" + SuccessText + "</p>"
                + "<button type=\"button\" class=\"ww-btn ww-btn-primary\" data-ww-action=\"signup-get-started\">"
                + "Get started</button></div>",

            "loading" or "packCheckout" =>
                "<div class=\"ww-signup-step ww-signup-processing\" data-ww-step=\"" + step + "\"" + hidden + ">"
                + "<h3>" + step + "</h3>"
                + "<p class=\"ww-text-muted\">" + ProcessingBoilerplate + "</p></div>",

            _ =>
                "<div class=\"ww-signup-step\" data-ww-step=\"" + step + "\"" + hidden + ">"
                + "<h3>" + step + "</h3></div>"
        };
    }

    private static string Field(string name, string type = "text")
    {
        // data-ww-field and no id at all. The React ids are a fallback the helpers accept, and a
        // server-rendered surface suffixes its ids per instance - so leaving them off is the state
        // those surfaces are permanently in, and the contract hook is the only way in.
        return "<label>" + name + "<input type=\"" + type + "\" data-ww-field=\"" + name + "\" /></label>";
    }

    /// <summary>
    /// The walk: what the flow would do, reduced to the transitions the drivers are asked to follow.
    /// </summary>
    /// <remarks>
    /// Every control counts its own clicks on <c>window</c>, so an assertion can say the click landed
    /// on the control it named rather than on some other primary button that happened to match.
    /// </remarks>
    private static string SignupScript()
    {
        return @"(() => {
  const root = document.querySelector('[data-ww-view=""signup""]');
  const panels = root.querySelectorAll('.ww-signup-step');

  window.__wwClicks = { submitRegister: 0, retry: 0, accept: 0, getStarted: 0 };

  const show = (step) => {
    root.setAttribute('data-ww-step', step);
    panels.forEach((panel) => { panel.hidden = panel.getAttribute('data-ww-step') !== step; });
  };
  window.__wwShow = show;

  const gate = root.querySelector('[data-ww-disclaimer-check]');
  const accept = root.querySelector('[data-ww-disclaimer-action=""accept-all""]');
  gate.addEventListener('change', () => { accept.disabled = !gate.checked; });

  root.addEventListener('click', async (event) => {
    const action = event.target.closest('[data-ww-action]')?.getAttribute('data-ww-action');
    if (action === 'submit-register') {
      window.__wwClicks.submitRegister += 1;
      show('creating');
      setTimeout(() => show('failed'), 200);
      return;
    }
    if (action === 'signup-retry') {
      window.__wwClicks.retry += 1;
      show('creating');
      setTimeout(() => show('disclaimers'), 200);
      return;
    }
    if (action === 'signup-get-started') {
      window.__wwClicks.getStarted += 1;
    }
  });

  accept.addEventListener('click', async () => {
    window.__wwClicks.accept += 1;
    // A real same-origin POST: the response watcher reads the browser's own report of it, which is
    // the half of the accept loop no unit test can reach.
    const response = await fetch('" + AcceptPath + @"', { method: 'POST' });
    if (response.ok) show('success');
  });

  // Starts on loading and moves itself on, so the first wait ends because the step CHANGED rather
  // than because it was already there.
  setTimeout(() => show('register'), 150);
})();";
    }
}
