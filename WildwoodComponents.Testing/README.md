# WildwoodComponents.Testing

Playwright helpers for driving the **Registration & Subscription** component from a browser
test: the step readers, the step recorder, the signup flows, and the `data-ww-*` DOM contract
that the Blazor, Razor and React implementations all honour.

The contract's STRINGS reach further than its attributes do. `register`, `payment`, `failed`,
`submit-register`, `disclaimer-accept-all` and the rest are the same words on React Native and
SwiftUI, which have no DOM and carry them as `testID` and `accessibilityIdentifier` instead. So
a test plan reads the same against all five stacks; only the three web ones are drivable from
here, and only they are what these selectors are about.

Every host integrating this component ends up writing the same scaffolding — read the step
hooks, wait out the processing step, accept whatever disclaimers the app has configured,
answer the consent banner. Shipping it here means each app does not rediscover the same
handful of traps, in particular the ones that only appear against a real deployment, where
terms are configured and the API's per-IP rate limit is real.

This is the .NET port of `@wildwood/react/testing`: the same DOM contract, the same budgets,
and the same traps handled the same way. What C# does differently, it does because C# has no
type-only import — see below.

## Reference it from a test project only

```
dotnet add package WildwoodComponents.Testing
```

**Never from a project you ship.** This package's whole reason to exist is that
`Microsoft.Playwright` is not a dependency of anything a host deploys: its driver copies
around 100 MB of Node and JavaScript into the build output of every project that references
it, before a single browser is installed. `WildwoodComponents.Blazor`,
`WildwoodComponents.Razor` and `WildwoodComponents.Shared` therefore reference neither this
package nor Playwright, and a test in this repository parses those project files and fails if
that ever changes.

Playwright still needs its browsers once, after the test project builds:

```
pwsh bin/Debug/net10.0/playwright.ps1 install
```

## What C# trades for JS's type-only import

The JS helpers import Playwright **for types only**, so the built module imports nothing at
runtime. That is not a style choice there: a consumer that resolves a different copy of
Playwright than its own test runner — any monorepo, any workspace with a nested install —
crashes on "Playwright was loaded twice" before a single assertion runs. Types erase at
compile time, so the JS helpers work with whatever Playwright the consumer already has.

C# has no type-only import: an assembly that names `IPage` in a signature references
`Microsoft.Playwright` at runtime. Two things stand in for it.

**The package boundary is the mechanism.** Where JS keeps Playwright out of the built
*module*, this keeps it out of every *shipped package*. Nothing a host deploys references this
assembly, so nothing a host deploys carries the driver.

**A host picks its own Playwright version.** The `PackageReference` here is a floor, not a
pin — NuGet has no peer-dependency concept, so the only way to say "whatever version you
already have" is to name a minimum and let the consumer's own reference win. A test project
that references a newer `Microsoft.Playwright` gets that version, and these helpers bind to
it; NuGet flags a downgrade below the floor (NU1605) rather than honouring it silently. There
is no "loaded twice" hazard to trade against: a test project resolves exactly one
`Microsoft.Playwright`, and both the runner and these helpers use it.

## What is in it

| Type | What it does |
|---|---|
| `WildwoodTestSelectors` | The `data-ww-*` DOM contract as plain strings — the whole vocabulary, for a host writing specs of its own. |
| `WildwoodSignupSteps` | Reads and waits on the published steps: `SignupStepAsync`, `WaitForSignupStepAsync`, `WaitForSignupStepToLeaveAsync`, `ManageStepAsync`, `WaitForManageStepAsync`, `WaitForAnyManageStepAsync`, `RecordSignupStepsAsync`. |
| `WildwoodSignupFlows` | Drives the signup: `FillRegistrationFormAsync`, `SubmitRegistrationFormAsync`, `DismissConsentBannerAsync`, `AcceptDisclaimersAsync`, `FinishSignupAsync`. |
| `WildwoodTestingOptions` | Every budget the drivers spend, and the acceptance-response pattern. Defaults are the JS helpers' literals. |
| `WildwoodContractException` | The component never honoured the contract — as opposed to Playwright's own exception, which means the driver could not do what it was told. |

```csharp
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Testing;

await WildwoodSignupFlows.DismissConsentBannerAsync(page);

await WildwoodSignupSteps.WaitForSignupStepAsync(page, SignupStep.Register);
await WildwoodSignupFlows.FillRegistrationFormAsync(page, new RegistrationFields
{
    FirstName = "Ada",
    LastName  = "Lovelace",
    Username  = "ada-" + Guid.NewGuid().ToString("N")[..8],
    Email     = $"ada-{Guid.NewGuid():N}@example.test",
    Password  = "Correct-Horse-9!"
});
await WildwoodSignupFlows.SubmitRegistrationFormAsync(page);

await WildwoodSignupFlows.FinishSignupAsync(page, "You're all set");
```

Waits take the machines' own step enums from `WildwoodComponents.Shared`, spelled with the
same `StepNames` table the Blazor and Razor views render from — so the names a spec waits for
and the names the views publish cannot drift apart, and `SignupStep.Done` cannot be
misspelled. (Its DOM name is `success`; the rename lives in `StepNames`, in one place.)

## Why these helpers key on hooks rather than copy

Every label the component renders is host-configurable through the 95-string label contract,
and headings move between releases. So nothing here is located by its text: steps are read
from `data-ww-step`, fields from `data-ww-field`, buttons from `data-ww-action` and
`data-ww-disclaimer-action`. The class-name and `button[type="submit"]` selectors these
helpers shipped with are kept as fallbacks, so a host testing a deployment built before it
upgraded is not told its component is broken.

One rule is worth stating because it costs a debugging session to rediscover: **reading the
step and scoping a control to it are different jobs**, and `data-ww-step` does both. Razor
keeps every step panel in the DOM and mirrors the active step onto the view root, so an
unscoped `[data-ww-step="failed"] .ww-btn-primary` reaches every primary button in the view,
including the hidden register submit that precedes the real Try Again. Every step-scoped
selector in `WildwoodTestSelectors` therefore carries `:not([data-ww-view])`. Use those
selectors rather than writing your own.

## What `AcceptDisclaimersAsync` is really for

Accepting a disclaimer is one click. The helper exists for the three things that happen
instead.

- **The accept POST is refused.** Acceptance sits under the API's per-IP auth limiter together
  with login and register, so a suite enrolling several users a minute from one address gets
  HTTP 429s. The component leaves the button where it is, so without watching the response
  this reads as "the button does nothing". The loop waits the window out instead of spending
  its click budget on refusals, and says so if the 429s keep coming.
- **The pending list fails to load.** The component then renders its own retry instead of an
  Accept, and a spec waiting only for an Accept times out on a button that is never coming.
- **Accept is gated on required checkboxes.** Both .NET stacks keep Accept `disabled` until
  every required box is ticked, and Playwright's click waits for actionability — so without
  ticking the gate first, the helper hangs on the button and eventually blames the server. The
  gate is ticked before the click, by `data-ww-disclaimer-check`. There is deliberately **no**
  bare `input[type="checkbox"]` fallback: it would tick unrelated boxes, and it would quietly
  cover for a stack that never adopted the hook.

The loop is bounded (ten clicks by default). An Accept that never goes away is a real defect
and is reported as one, rather than spinning until the spec's own timeout where it would read
as a hang with no cause.

### The response watcher, and where it is silent

The 429 back-off and the "refused by the server, HTTP n" diagnostic come from watching the
browser's responses for the acceptance call, matched by
`WildwoodTestSelectors.AcceptResponsePattern` — which covers the direct API path
(`disclaimeracceptance/accept`) and the proxied one Razor ships
(`disclaimer-gate/accept`). Override it with `WildwoodTestingOptions.AcceptResponsePattern`
if your host proxies acceptance under a path of its own.

**On Blazor Server there is nothing to watch.** Acceptance is posted from the server over the
circuit, so the browser never sees that response and no pattern can match one: the bounded
failure there reports the generic "did not converge" message rather than an HTTP code. That
is a property of where the call is made, not something this package can fix. The watcher still
earns its place on the stacks that call the API from the browser, and on Razor, whose proxy
route the default pattern already matches.

## Budgets

`WildwoodTestingOptions` carries every timeout and budget the drivers spend; the defaults are
the JS helpers' literals, value for value, so passing no options is the React behaviour.

```csharp
var options = new WildwoodTestingOptions
{
    // A shared environment with a tighter per-IP limit.
    RateLimitPauseMs = 45_000,
    RateLimitWaits   = 5
};

await WildwoodSignupFlows.FinishSignupAsync(page, options);
```

The ones most often worth changing: `ProcessingTimeoutMs` (120s — registering, signing in,
linking the payment and activating the plan), `SignupRetries` (2), `AcceptAttempts` (10),
`RateLimitPauseMs` (20s), `DisclaimerSettleMs` (15s) and `ConsentBannerWaitMs` (5s — spent in
full on every environment that configures no banner).

## Testing the helpers themselves

Each public entry point takes an `IPage` or an `ILocator` and immediately wraps it in an
internal seam — the dozen page operations these drivers perform, and nothing else. The rules
worth proving sit behind that seam and are exercised by `WildwoodComponents.Tests` with no
browser at all: the preference order the failure message is read in, the bounded budgets, the
429 back-off, the gate tick before the click, and the response handler coming back off the
page however the loop ends.
