# WildwoodComponents.Testing.Smoke

A browser smoke run for **WildwoodComponents.Testing** — the Playwright helpers a host drives the
Registration & Subscription component with. It launches a Chromium, serves a handful of staged pages
from a loopback server, and puts the *shipped* helpers through them.

It is **not** part of `dotnet test`, for the reason given under "Why a console runner" below. Run it
by hand:

```
dotnet run --project WildwoodComponents.Testing.Smoke
```

Options: `--headed`, `--browser <chromium|firefox|webkit>`, `--filter <text>`. Exit code 0 when every
check passed, 1 when one failed, 2 when the run could not start.

Playwright needs its browsers once:

```
pwsh WildwoodComponents.Testing.Smoke/bin/Debug/net10.0/playwright.ps1 install chromium
```

Nothing here reaches a network, needs a credential, or contacts a Wildwood server. The fixture
server binds `127.0.0.1` on an ephemeral port.

## What it proves

`WildwoodComponents.Tests` already exercises every RULE in these drivers — the preference order, the
bounded budgets, the 429 back-off, the gate tick before the click, the response handler coming back
off the page — behind the `IFlowSurface` seam, with no browser. What it cannot exercise is the seam
itself (one locator call per operation) and the browser behaviours those calls lean on. That is what
this run is for, and each check names which behaviour it stands on:

| Check | The browser behaviour it needs |
|---|---|
| the step reader answers every contract name on the Razor shape | a selector list resolves in DOCUMENT order, so the root's mirrored step wins over twelve panels that each carry one |
| a wait refuses to return while the flow is still on the step | the reader really resolves against a live DOM — a reader that answered `null` would make every "wait for it to leave" return at once |
| a wait ends because the step changed, not because it had already | the wait is armed before the transition and ends on the transition |
| the form is filled through `data-ww-field`, with no ids to fall back on | six real `fill()` calls land in six real inputs |
| submit is found by its action hook where there is no form behind it | `button[type="submit"]` cannot reach a `type="button"`, and there is no `<form>` on the page to mask it |
| the failure text is read from its own hook, not the shared boilerplate | the ORDERED failure-message list: the `creating` panel's "this will only take a moment" is still in the document, and earlier |
| Accept is never mistaken for a retry, and its gate is ticked first | `click()` waits for actionability rather than failing, so an unticked gate is a hang, not an error |
| the accept loop waits out an HTTP 429 and carries on | `page.Response` really fires for a same-origin POST, and the watcher really receives it |
| Try Again is scoped off the view root, and the signup finishes | `:not([data-ww-view])` — without it the retry reaches the hidden register submit that precedes it in document order |
| the recorder sees the steps entered, and none that were not | an init script runs before the page paints, and a `MutationObserver` catches the transitions |
| the consent banner is answered, and its absence is not a failure | the banner is waited for, clicked and waited out; a page with none costs the budget and returns |
| the manage reader answers every plan-change name | the manage root reads back all nine names, and "any of these" says which it reached |

Two of these were checked by mutation while they were written: dropping `:not([data-ww-view])` from
`WildwoodTestSelectors.StepPanel` turns "Try Again is scoped off the view root" into a 30-second hang
on an invisible button, and reversing `SignupFailureMessageSelectors` makes "the failure text is read
from its own hook" report `This will only take a moment.` as the cause of the failure. Neither check
passes vacuously.

## What it does NOT prove

**It does not prove that the Blazor or Razor components render this markup.** The pages under
`FixturePages` are staged. That half of the contract is pinned where the markup is produced, without a
browser, and those tests are the ones to change if a hook moves:

- `WildwoodComponents.Tests/Blazor/TestingContractRenderTests.cs` and
  `RegistrationSubscriptionSignupRenderTests.cs` — Blazor's render tree, read back.
- `WildwoodComponents.Tests/Razor/RegistrationSubscriptionSignupDomContractTests.cs` — Razor's shipped
  `.cshtml` and `regsub-signup.js`, read as sources.
- `WildwoodComponents.Tests/Shared/StepNamesTests.cs` — the `data-ww-step` vocabulary itself.

So the loop is closed in two halves that do not meet: those tests prove the components publish the
contract, this run proves the helpers can drive it. **Nothing in this repository proves the two meet
on a live page** — see below for why, and for what would close it.

The staged pages use RAZOR's shape (every step panel in the document, the active step mirrored onto
the view root) because it is the harder of the two .NET surfaces to drive and the one whose traps are
documented. Blazor's shape — one panel at a time, the step on a child of the root — is the easier arm
of the same selector and is not staged separately.

## Why there is no run against WildwoodComponentsTestSuiteBlazor

The obvious spec would drive the real component on `/test/registration-subscription` in
`WildwoodComponentsTestSuiteBlazor`. Three things block it, and the first two are nothing to do with
having a backend:

1. **Every test page is gated, and the app never issues the credential that would open the gate.**
   The gate is `MainLayout.razor`'s `<AuthorizeView>` with a `<NotAuthorized><RedirectToLogin /></NotAuthorized>`,
   not endpoint authorization — `Program.cs` calls neither `UseAuthentication` nor `UseAuthorization`,
   and only two of the test pages carry an attribute at all. It bites just as hard: the layout runs
   during static-SSR prerendering, so an unauthenticated `GET /test/registration-subscription`
   redirects to `/login` before any component renders. (`RedirectToLogin` navigates to a bare
   `/login` — no `ReturnUrl` is appended anywhere in the app, so there is nothing to come back to.)
   Nothing calls `SignInAsync`: the JWT `TestSuiteAuthService` fetches goes into per-circuit memory
   (`CircuitStateStorageService`), which a fresh HTTP request cannot see. And `HandleLogin` finishes
   with `Navigation.NavigateTo("/", forceLoad: true)` — a full page load, into the same gate.
2. **The login form cannot submit.** `Login.razor` declares no `@rendermode`, and `App.razor` renders
   `<Routes />` without one, so the page is static SSR: its `EditForm` posts back carrying
   `_handler=loginForm` and `name="loginModel.Email"`, but `loginModel` is a plain field with no
   `[SupplyParameterFromForm]`, so the POST rehydrates an empty model and `OnValidSubmit` never runs.
   Filling the form in a browser and submitting it returns "Email is required" and "Password is
   required". (Verified against the running app.)
3. **Blazor Server makes the component's HTTP calls from the SERVER.** Even past the two above,
   `page.RouteAsync` — the trick the JS suite uses to stub the whole backend from the browser — sees
   nothing, because the browser makes none of those requests. A stub would have to be a real server the
   app is pointed at through `WildwoodAPI:BaseUrl`.

(1) and (2) are defects in the harness app rather than in anything this parity run touched, so they are
reported here rather than fixed: fixing authentication in a test-suite app is its own change, with its
own tests. Until they are fixed, no browser can reach the component at all.

**What a future spec would need**, in order:

- A way into the app for a browser — either a cookie sign-in on successful login, or an
  `AuthorizeRouteView` in `Routes.razor` with the pages' `[Authorize]` enforced in the circuit rather
  than at the endpoint, plus an interactive login page.
- A local stub WildwoodAPI the app is pointed at with `WildwoodAPI__BaseUrl`, serving at least
  `POST api/auth/login`, `GET api/AppComponentConfigurations/{appId}/auth-configuration`,
  `GET api/app-tiers/{appId}/public`, `GET api/app-tier-addons/{appId}/public` and
  `POST api/userregistration/register`. Keying the scenario on the `appId` in the harness page's
  `?appId=` would let one stub serve the open / token-only / closed registration modes without state.
- The app launched as a child process on `127.0.0.1`, since a `WebApplicationFactory` test server has
  no socket for a browser to connect to.

One thing such a spec would still not reach: the acceptance response watcher. Blazor Server posts
disclaimer acceptance over its circuit, so the browser never sees that response and no pattern can
match one — which `WildwoodComponents.Testing/README.md` already records.

## Why a console runner

It needs a browser, and a build machine does not have one. The alternative — an xUnit test that skips
itself when no browser is installed, as `NodeFactAttribute` does for Node — would report coverage that
never actually runs anywhere, because unlike Node the browsers are a ~150 MB download nobody has
installed in CI. A console runner keeps `dotnet test` from ever trying to run it.

What stands between it and bit rot is the COMPILER, and that needed wiring up rather than assuming.
CI does not build the solution — `dotnet-tests.yml` scopes `dotnet test` to the two test projects —
and the test project must not reference this one, because the package boundary forbids anything
shipped from reaching Playwright and a test asserts it. So this project sat in no build graph CI
touched. `dotnet-tests.yml` now compiles it explicitly (no browser needed for that), which is what
makes a rename in `WildwoodComponents.Testing` break a required check instead of breaking `main`
quietly.
