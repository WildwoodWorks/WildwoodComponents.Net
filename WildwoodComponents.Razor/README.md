# WildwoodComponents.Razor

Razor Pages-native ViewComponents (authentication, AI, messaging, payments, subscriptions,
feedback, and more). The Razor Pages sibling of the Blazor-based **WildwoodComponents**,
sharing the same `--ww-*` CSS theming system and the same WildwoodAPI service layer.

## Install & register

```csharp
builder.Services.AddWildwoodComponentsRazor(options =>
{
    options.BaseUrl = "https://api.example.com";
    options.ApiKey  = "your-api-key";
    options.AppId   = "your-app-id";
});
```

Use a component in a Razor view with the tag-helper syntax:

```html
<vc:authentication app-id="my-app" />
<vc:feedback-widget app-id="my-app" />
```

ViewComponents self-include their own JS/CSS from the RCL static web assets
(`_content/WildwoodComponents.Razor/...`), so no manual `<script>`/`<link>` is required —
just render the component.

---

## Feedback widget proxy

The Feedback widget (`<vc:feedback-widget app-id="..." />`) renders a floating button and a
slide-out form. Its client JavaScript talks to a **thin server-side proxy** in your app rather
than to the WildwoodAPI directly, so the Bearer token stays server-side and never reaches the
browser. The proxy simply forwards to `IWildwoodFeedbackService`, which is already registered
for you by `AddWildwoodComponentsRazor`.

By default the widget calls a proxy mounted at **`/api/wildwood-feedback`** (override with the
`proxy-base-url` attribute). You must map these four endpoints onto `IWildwoodFeedbackService`:

| Method & path (relative to the proxy base) | Forwards to | Notes |
|--------------------------------------------|-------------|-------|
| `POST /submit`                             | `SubmitFeedbackAsync(request)` | JSON body = `FeedbackSubmissionRequest` |
| `GET  /duplicate-check?title=&appId=`      | `CheckDuplicateAsync(title, appId)` | debounced as the title is typed |
| `POST /{id}/vote`                          | `VoteAsync(id)` | upvote an existing item |
| `GET  /widget?appId=`                      | `GetWidgetConfigAsync(appId)` | optional — config is normally rendered server-side; provided for parity / client refresh |

### Copy-paste proxy controller

Drop this controller into your app (e.g. `Controllers/WildwoodFeedbackProxyController.cs`).
It requires no changes — it forwards every call to the injected `IWildwoodFeedbackService`.

```csharp
using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;

namespace YourApp.Controllers;

/// <summary>
/// Thin server-side proxy for the Wildwood feedback widget. The widget's JavaScript calls these
/// routes; each one forwards to IWildwoodFeedbackService so the WildwoodAPI Bearer token (held in
/// the server-side session) is never exposed to the browser. Route base must match the widget's
/// proxy-base-url (default "/api/wildwood-feedback").
/// </summary>
[ApiController]
[Route("api/wildwood-feedback")]
public class WildwoodFeedbackProxyController : ControllerBase
{
    private readonly IWildwoodFeedbackService _feedback;

    public WildwoodFeedbackProxyController(IWildwoodFeedbackService feedback)
    {
        _feedback = feedback;
    }

    // POST /api/wildwood-feedback/submit
    [HttpPost("submit")]
    public async Task<IActionResult> Submit([FromBody] FeedbackSubmissionRequest request)
    {
        var result = await _feedback.SubmitFeedbackAsync(request);
        if (result.Success)
            return Ok();
        if (result.RateLimited)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { error = result.ErrorMessage });
        return BadRequest(new { error = result.ErrorMessage });
    }

    // GET /api/wildwood-feedback/duplicate-check?title=...&appId=...
    [HttpGet("duplicate-check")]
    public async Task<IActionResult> DuplicateCheck([FromQuery] string title, [FromQuery] string? appId)
        => Ok(await _feedback.CheckDuplicateAsync(title, appId ?? string.Empty));

    // POST /api/wildwood-feedback/{id}/vote
    [HttpPost("{id}/vote")]
    public async Task<IActionResult> Vote(string id)
    {
        var result = await _feedback.VoteAsync(id);
        return result.Success ? Ok(result) : BadRequest(new { error = result.ErrorMessage });
    }

    // GET /api/wildwood-feedback/widget?appId=...  (optional convenience / parity)
    [HttpGet("widget")]
    public async Task<IActionResult> Widget([FromQuery] string appId)
    {
        var config = await _feedback.GetWidgetConfigAsync(appId);
        return config is null ? NotFound() : Ok(config);
    }
}
```

### Minimal-API equivalent

If you prefer minimal APIs, map the same routes in `Program.cs`:

```csharp
var feedback = app.MapGroup("/api/wildwood-feedback");

feedback.MapPost("/submit", async (FeedbackSubmissionRequest request, IWildwoodFeedbackService svc) =>
{
    var result = await svc.SubmitFeedbackAsync(request);
    if (result.Success) return Results.Ok();
    if (result.RateLimited) return Results.StatusCode(StatusCodes.Status429TooManyRequests);
    return Results.BadRequest(new { error = result.ErrorMessage });
});

feedback.MapGet("/duplicate-check", async (string title, string? appId, IWildwoodFeedbackService svc) =>
    Results.Ok(await svc.CheckDuplicateAsync(title, appId ?? string.Empty)));

feedback.MapPost("/{id}/vote", async (string id, IWildwoodFeedbackService svc) =>
{
    var result = await svc.VoteAsync(id);
    return result.Success ? Results.Ok(result) : Results.BadRequest(new { error = result.ErrorMessage });
});

feedback.MapGet("/widget", async (string appId, IWildwoodFeedbackService svc) =>
{
    var config = await svc.GetWidgetConfigAsync(appId);
    return config is null ? Results.NotFound() : Results.Ok(config);
});
```

The error/`429` shapes above match what the widget JavaScript expects (it reads `error`/`title`/
`errorMessage` from a failed response body and treats `429` as rate-limited).

---

## Registration & subscription proxy (ships with the library)

Unlike the feedback and authentication proxies above, this one **is in the package** — you do not
write it. `WildwoodRegistrationSubscriptionProxyController` serves
`/api/wildwood-regsub/*`, the same-origin routes the client JS needs for pack checkout, the pack
lifecycle, the 3-D Secure plan change, trial eligibility, the public catalog and registration-token
plans. A Razor app keeps the user's JWT in the server session, so the browser cannot call
WildwoodAPI itself; shipping these routes is what stops every consumer 404ing on them.

### Host wiring

Exactly what the notifications and attribution proxies need — register MVC controllers, keep
server-side session on, and map them:

```csharp
builder.Services.AddControllers();
// ...
app.UseSession();
app.MapControllers();
```

Controllers in this package are discovered as an application part because your app references it.
If your host narrows controller discovery (its own `ApplicationPartManager` setup), add the
assembly explicitly:

```csharp
builder.Services.AddControllers()
    .AddApplicationPart(typeof(WildwoodComponents.Razor.Controllers.WildwoodRegistrationSubscriptionProxyController).Assembly);
```

### Routes

| Method & path (relative to `/api/wildwood-regsub`) | Auth | Forwards to |
|---|---|---|
| `GET  /trial-eligibility` | session | `GetTrialEligibilityAsync` |
| `POST /checkout/quote` | session | `QuoteAddOnCheckoutAsync` — body `{ "Items": [{ "AddOnId", "PricingId" }] }` |
| `POST /checkout/payment-method` | session | `CreateCheckoutPaymentMethodAsync` — body `{ "ProviderId" }` |
| `POST /checkout` | session | `CheckoutAddOnsAsync` — body `{ "CheckoutId", "ProviderId", "PaymentTransactionId", "UseSavedCard", "Items" }` |
| `POST /checkout/complete` | session | `CompleteAddOnCheckoutAsync` — body `{ "PaymentTransactionId" }` |
| `POST /addons/subscribe` | session | `SubscribeToAddOnDetailedAsync` — body `{ "AddOnId", "PricingId", "PaymentTransactionId" }` |
| `POST /addons/{subscriptionId}/cancel?immediate=false` | session | `CancelAddOnDetailedAsync` |
| `POST /addons/{subscriptionId}/reactivate` | session | `ReactivateAddOnAsync` |
| `POST /tier-change/preview` | session | `PreviewTierChangeAsync` — body `{ "NewAppTierId", "NewAppTierPricingId" }`. The caller's OWN subscription; the admin- and company-scoped previews stay on your app-tier proxy |
| `POST /tier-change` | session | `ChangeTierAsync(options)` — body `{ "NewAppTierId", "NewAppTierPricingId", "Immediate", "PaymentTransactionId", "SupportsPaymentAction" }` |
| `POST /tier-change/{pendingChangeId}/complete` | session | `CompleteTierChangeAsync` |
| `GET  /catalog?currency=` | anonymous | `GetPublicCatalogAsync` — public data the pricing page already renders |
| `GET  /token-details?token=` | anonymous | `GetRegistrationTokenDetailsAsync` |
| `POST /token-details` | anonymous | the same lookup with the token in the body (`{ "Token" }`), so it never lands in an access log |
| `GET  /registration-mode?tokenMode=` | anonymous | the app's live auth configuration, **reduced** to the resolved mode |
| `POST /register` | anonymous | `RegisterWithTokenAsync` when the body has a `Token`, else `RegisterAsync` |
| `POST /login` | anonymous | `IWildwoodRegistrationService.LoginAsync` — puts the tokens in the **server session** |
| `POST /link-transaction` | session | `LinkTransactionToUserAsync` — attaches the plan's payment to the new account |
| `POST /subscribe` | session | `SubscribeToTierAsync` — body `{ "TierId", "PricingId", "PaymentTransactionId" }` |
| `GET  /disclaimers/pending` | session | `GetPendingDisclaimersAsync(appId, null, "registration")` |
| `POST /disclaimers/accept` | session | `AcceptDisclaimersAsync` — body `{ "Acceptances": [{ "CompanyDisclaimerId", "CompanyDisclaimerVersionId" }] }` |
| `POST /payment/initiate` | anonymous | `InitiatePaymentAsync` — the shape `payment.js` posts to `{proxy}/initiate` |
| `POST /payment/confirm` | anonymous | `ConfirmPaymentAsync` — `{ "paymentIntentId", "providerType" }` |

**The signup routes (the last nine) replace routes hosts used to write.** Before this, the Razor
signup reached registration through **your** `/api/wildwood-auth/register`, the plan through **your**
`/api/wildwood-subscription/subscribe`, and the card through **your** `/api/wildwood-payment`; the
token check, the disclaimers and the login went to origin-relative `api/...` paths that only work
when WildwoodAPI happens to sit behind the same origin. `<vc:registration-subscription-signup />`
needs none of that — **you write no routes for it**. Three notes:

- **`/register`, `/login` and `/payment/*` are anonymous, and that is pay-first, not an oversight.**
  The plan's card is taken BEFORE the account exists, so there is no session to require at that
  point. WildwoodAPI applies its own rules; the proxy adds no authority the browser did not have,
  and forwards the bound request rather than the raw body.
- **No token ever reaches the browser.** `/login` puts the JWT and the refresh token in the server
  session through the same service the rest of the package uses, and answers a user id and whether
  disclaimers are pending. Nothing else.
- **`/registration-mode` is reduced.** WildwoodAPI's auth-configuration route also carries the
  password policy, the rate limits and the default pricing model; only `allowOpenRegistration` and
  `allowTokenRegistration` are read, and only the resolved mode is relayed. It is deliberately
  **uncached**, so an operator who closes sign-up is obeyed by the next visitor.

`<vc:payment />` is unchanged for everyone else: its `proxy-base-url` still defaults to
`/api/wildwood-payment`, your own proxy. The signup view simply points it at
`/api/wildwood-regsub/payment` instead, and any other page may do the same.

### The three rules the client can rely on

1. **Refusals are 200s.** Every structured answer is relayed as HTTP 200 with the service's result
   DTO, a refusal included (`success:false` with `errorCode`/`errorMessage`, or a per-pack
   `status:"failed"`). The upstream endpoints answer a refusal with the same DTO as a success, and
   the UI has to word "you already own that pack" differently from "that route does not exist yet"
   (`errorCode: "NotSupported"`). Only the proxy's own preconditions and an unreadable answer break
   that: **401** when there is no signed-in session (nothing is forwarded — an empty bearer is never
   sent), **400** for a missing body or an app id that is not this app's, and **502** for the two
   loads with no structured refusal, the catalog and the token details, so "unavailable" stays
   distinguishable from "sells nothing" and "invalid token".
2. **The app id is the configured one.** `options.AppId` from `AddWildwoodComponentsRazor`, always.
   A client may send `appId`, but only so a mismatch can be rejected with 400: the session token
   lives on the server, so honouring a client-named app would let a page act on any app in the
   company. App-scoped routes answer 400 when no `AppId` is configured.
3. **Bodies are bound, not passed through.** Each body binds to its request model and the proxy
   re-serialises that model, so a property nobody declared never reaches WildwoodAPI.

**Anti-forgery**: none, matching the notifications and attribution proxies. Every route is
JSON-bodied and session-authorised. A host that wants CSRF tokens should apply its own
filter or middleware across all three proxies rather than singling one out.

### The add-ons panel now uses these routes (hosts: three fewer to write)

`<vc:add-ons-panel />` — and the add-ons tab of `<vc:subscription-admin />` — used to post pack
subscribe and cancel to the **host-supplied** app-tier proxy (`proxy-base-url`, default
`/api/wildwood-app-tiers`) at `{proxy}/{appId}/addons/subscribe` and
`{proxy}/{appId}/addons/subscriptions/{id}/cancel`. For the **signed-in user's own packs**,
`wwwroot/js/subscription-admin.js` now posts them to the shipped proxy above instead, and gains
**Reactivate**:

| Action | Now posts to |
|---|---|
| Subscribe | `POST /api/wildwood-regsub/addons/subscribe` — body `{ "AddOnId", "PricingId" }` |
| Cancel | `POST /api/wildwood-regsub/addons/{subscriptionId}/cancel?immediate=false` |
| Reactivate | `POST /api/wildwood-regsub/addons/{subscriptionId}/reactivate` |

You no longer implement those routes on your own proxy — delete them if you did. Three things
follow:

- **The pricing option travels with the subscribe.** It decides both the price and the trial the
  processor starts, so it is sent rather than guessed server-side.
- **A refusal keeps the server's words.** The proxy answers 200 with the structured result, so
  "you already own that pack" is shown as written, inline in the panel (`.ww-addons-error`),
  instead of the purchase silently appearing to have worked.
- **401 says the session is gone.** No signed-in session means nothing is forwarded, and the panel
  says "Your session has expired. Please sign in again."

Override the base with `reg-sub-proxy-url` if your host mounts the controller elsewhere. The
**company- and admin-scoped** add-on routes on your app-tier proxy (`addons/subscribe/company`,
`addons/admin/subscribe-user/{userId}`, `addons/admin/cancel-user-addon/{id}`,
`addons/subscriptions/{id}/cancel?immediate=true`) are unchanged and still yours — the shipped
proxy acts as the signed-in user, so it has no company or admin scope. Reactivate is therefore
offered only on a user's own packs.

---

## Registration & Subscription — pricing

`<vc:registration-subscription-pricing />` is the Razor port of React's
`RegistrationSubscriptionPricing` (and of the Blazor component of the same name): what the app
sells, at the price the server is quoting right now.

```html
@* Minimal: plans only, using the app id from AddWildwoodComponentsRazor *@
<vc:registration-subscription-pricing />

@* Everything on *@
<vc:registration-subscription-pricing
    app-id="my-app"
    show-plans="true"
    show-add-ons="true"
    pack-selection="multi"
    default-billing="annual"
    highlight-tier-id="@currentTierId"
    contact-url="https://example.com/sales"
    include-json-ld="true"
    json-ld-url="https://example.com/pricing"
    select-url="/signup?ref=pricing" />
```

Add the assets once, in your layout:

```html
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/wildwood-razor-themes.css" />
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/regsub.css" />
<script src="~/_content/WildwoodComponents.Razor/js/regsub-pricing.js"></script>
```

### First-paint prices, and no others

The catalog is read **on the server**, before the first byte: the page arrives with real prices,
which is what React reaches for with its `initialCatalog` prop. There is no skeleton, no hydration
flash and no client round trip to quote a plan.

Each plan card carries **both billing cycles**, formatted server-side through
`FormatHelpers.FormatMoney`; `regsub-pricing.js` only swaps which of the two is hidden. The browser
never formats money, so an amount a visitor reads is always one the server quoted.

There is **no fallback price** anywhere. If the catalog cannot be read the whole grid is replaced
by "Pricing is unavailable right now" and a **Retry** — the prices go away with the answer that
produced them. Retry reloads the page: this component is server-rendered, so the server is the one
source of truth for its markup, and a failed catalog load is never cached, so the reload really
does ask again. (The same mechanism `subscription-admin.js` uses after a mutation.)

The catalog sits behind a **60-second shared cache** (`IWildwoodPublicCatalogService`), so a page
with several pricing surfaces makes one pair of requests. The cached catalog is treated as
immutable — the view model is built per request and never edits it.

### Parameters

| Tag-helper attribute | Type | Default | Meaning |
|---|---|---|---|
| `app-id` | string | the configured `AppId` | Which app's catalog to show |
| `currency` | string | the catalog's own | Display override, rarely needed |
| `show-plans` | bool | `true` | The plan grid |
| `show-add-ons` | bool | `false` | The pack grid |
| `offer-free-tier-choice` | bool | `true` | `false` hides every free tier |
| `pack-selection` | `"none"` \| `"multi"` | `"none"` | Per-pack "Select", or tick several and continue once |
| `add-on-groups` | `IReadOnlyList<AddOnGroup>` | — | Headings matched on the pack's catalog `Category` |
| `show-billing-toggle` | bool | `true` | Shown only when some plan is priced annually |
| `default-billing` | `"monthly"` \| `"annual"` | `"monthly"` | Which side the toggle starts on |
| `show-feature-comparison` | bool | `true` | Each plan's feature list |
| `show-limits` | bool | `true` | Each plan's usage limits |
| `highlight-tier-id` | string | — | Marks one plan as already chosen (case-insensitive) |
| `contact-url` | string | — | Where an unpriced plan's "Contact Sales" points |
| `include-json-ld` | bool | `false` | Emit schema.org offers |
| `json-ld-url` | string | — | Canonical URL stamped on every offer |
| `labels` | `RegistrationSubscriptionLabels` | shipped copy | Copy overrides; unset strings keep the shipped word |
| `select-url` | string | — | Where a selection navigates (see below) |
| `unavailable-text` | string | — | Replaces "Pricing is unavailable right now" |
| `component-id` | string | generated | A stable id, for two instances on one page |

`pack-selection` and `default-billing` are **strings**, in the JS union's own spelling, on purpose:
a Razor tag-helper attribute whose property is not a `string` is compiled as a **C# expression**,
so an enum would force every host to write `default-billing="@PricingBilling.Annual"` plus a
`@using`. An unrecognised value falls back to the safe default rather than throwing the page away.
`add-on-groups` and `labels` are genuine C# expressions (`add-on-groups="Model.PackGroups"`).

### `ww-regsub-select` and `select-url`

Razor has no callbacks, so React's `onSelect` is a **bubbling, cancelable `CustomEvent`**:

```js
document.addEventListener('ww-regsub-select', function (e) {
    // e.detail = { tierId, pricingId, billing, addOnIds }
    console.log(e.detail.tierId, e.detail.billing, e.detail.addOnIds);
    e.preventDefault();   // stop the navigation and handle it yourself
});
```

- A **plan** click sends the plan, its pricing option under the current cycle, and whatever packs
  are ticked — one click buys the whole basket.
- A **single pack** click sends `{ tierId: null, pricingId: null, billing, addOnIds: [id] }`.
- The **multi-select Continue** sends every ticked pack, in **catalog order** (never click order),
  capped at 25 — the same cap a selection arriving in a link gets.

When `select-url` is set and no listener called `preventDefault()`, the browser then goes there
with `tier`, `pricing` and `addons` appended, under the same query keys the JS SDK and the signup
view read. A query already on the template is **kept**; those three keys are the visitor's to set,
so a stale one in the template is dropped rather than left to win. Fragments survive
(`/signup?ref=blog#plans` → `/signup?ref=blog&tier=…&pricing=…#plans`).

### JSON-LD

`include-json-ld="true"` writes a `<script type="application/ld+json">` with one schema.org
`Offer` per priced plan and pack. Unpriced items are left out rather than published at zero. The
payload is serialised with `System.Text.Json`'s **default** encoder, which escapes `<`, `>` and
`&` — so an operator who names a plan with a closing script tag cannot break out of the element.

### Test hooks

Root `data-ww-view="pricing"`, classes `ww-regsub ww-regsub-pricing`; `data-ww-group="<id>"` on
each pack heading (`all` when ungrouped, `more` for the catch-all); `data-ww-pack="<addOnId>"` on
each pack card; `data-ww-tier="<tierId>"` on each plan card. The plan grid is the platform's
`.ww-tier-grid` / `.ww-tier-card` markup, unchanged, because live sites' end-to-end suites locate
plans by exactly those.

### What differs from React, and why

| React | Razor | Why |
|---|---|---|
| `onSelect` callback | `ww-regsub-select` event + `select-url` | A server-rendered stack cannot take a delegate |
| `describeAddOn` returning a node | — | Markup callbacks are impossible here; a pack's blurb, features and price come from catalog fields only |
| `loadingFallback` + `PricingSkeleton` | — | There is no loading state: the catalog is read before the first byte |
| `errorFallback` node | `unavailable-text` string | Same reason; the Retry stays either way |
| `initialCatalog` SSR snapshot | — | Razor gets it for free |
| Pack prices follow nothing | Pack prices follow nothing | Unchanged on purpose: a pack quotes its DEFAULT option, so Razor and React never disagree about what the same pack costs. The toggle is a plan control |

---

## Registration & Subscription — signup

`<vc:registration-subscription-signup />` is the Razor port of React's
`RegistrationSubscriptionSignup` (and of the Blazor view of the same name): an account, a plan,
packs and a card, in the order that keeps them consistent.

```html
@* Minimal: the app from AddWildwoodComponentsRazor, plan chosen on the page *@
<vc:registration-subscription-signup />

@* Arriving from the pricing page, which put tier/pricing/addons in the query *@
<vc:registration-subscription-signup complete-url="/dashboard" contact-url="/contact" />

@* Invite redemption: the token is the only way in, and its grant decides everything *@
<vc:registration-subscription-signup token-mode="required" />

@* Everything on *@
<vc:registration-subscription-signup
    app-id="my-app"
    pre-selected-tier-id="@tierId"
    pre-selected-pricing-id="@pricingId"
    pre-selected-add-on-ids="radar,seats"
    prefill-email="@invitedEmail"
    plan-selection="choose"
    pack-selection="choose"
    require-billing-address="true"
    return-url="/welcome"
    complete-url="/dashboard"
    already-signed-in-url="/dashboard"
    contact-url="https://example.com/contact"
    closed-text="We are invitation-only for now." />
```

Add the assets once, in your layout. **`regsub-machines.js` must come before `regsub-signup.js`**,
and `payment.js` is what collects the plan's card:

```html
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/wildwood-razor-themes.css" />
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/regsub.css" />
<script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>
<script src="~/_content/WildwoodComponents.Razor/js/payment.js"></script>
<script src="~/_content/WildwoodComponents.Razor/js/regsub-signup.js"></script>
```

### Pay-first, and why

```
register form -> token check -> plan -> packs -> the plan's card
  -> register, log in, link the payment, subscribe
  -> disclaimers -> pack checkout -> success
```

The card is taken **before the account exists**. A declined card then leaves nothing behind,
instead of an account sitting on a plan nobody paid for — and a card already on file is what lets
the pack checkout ask for **one card for a basket of any size**. Packs are bought **after** the
sign-in, because they are bought as the user.

This is the order React and Blazor ship, and it is a change from the old
`<vc:signup-with-subscription />`, which went plan → register → pay and whose paid step was a
hand-rolled card form with a permanently disabled button. **There are no card-number, expiry or
CVC fields anywhere in the new view** — cards are Stripe Elements inside `<vc:payment />` and the
pack checkout's SetupIntent — and a source guard fails the build if one appears.

The account creation is **one resumable attempt**: "Try Again" resumes at whichever of register /
sign in / link / subscribe is still outstanding, so it never registers the same person twice and
never charges a second card. Linking the payment and starting the plan are **never fatal** — the
account exists and the money is taken, and a refused subscribe turns the success copy into
"Plan activation is pending".

### Everything is decided before the first byte

The registration mode, the catalog and its prices, the plan a link preselected, the packs, the
password policy, the payment provider's publishable key, the registration disclaimers and **every
visible string** are read and rendered on the server. So:

- A **closed** sign-up renders the notice, never a form that flashes and disappears.
- No amount is ever formatted in the browser.
- A disclaimer whose content format is HTML is rendered **as HTML by the view**, which is how this
  package avoids writing a server-supplied string through `innerHTML` (it never does; a guard
  greps the scripts for it).
- The registration mode is read on **every** render and never cached: an operator who closes
  sign-up is obeyed by the next visitor.

### Parameters

| Tag-helper attribute | Type | Default | Meaning |
|---|---|---|---|
| `app-id` | string | the configured `AppId` | Which app to sign up for |
| `pre-selected-tier-id` | string | `?tier=` | Matched case-insensitively; a plan the app does not sell is **ignored** |
| `pre-selected-pricing-id` | string | `?pricing=` | Dropped with the plan when the plan is not sold |
| `pre-selected-add-on-ids` | string (comma-separated) | `?addons=` | Vetted against the catalog, de-duplicated, capped at 25 |
| `registration-token` | string | `?token=`, then `?invite=` | A token to redeem |
| `prefill-email` | string | `?email=` | Fills BOTH the email and the username |
| `plan-selection` | `"choose"` \| `"skip"` | `"choose"` | `skip` takes the app's default plan and removes the step |
| `pack-selection` | `"choose"` \| `"none"` | `"none"` | `none` removes the **step only** — a link's packs are still bought |
| `token-mode` | `"auto"` \| `"required"` | `"auto"` | `required` is invite redemption: token first, no plan, no packs, **overrides a closed config** |
| `require-billing-address` | bool | `false` | Collect a billing address with the plan's card |
| `return-url` | string | — | Carried and handed to the completion listener; never navigated to |
| `complete-url` | string | — | Where "Get Started" goes — the analog of React's `onSignupComplete` navigating |
| `already-signed-in-url` | string | — | Where an already-signed-in visitor goes; omit for the notice |
| `contact-url` | string | — | Where the closed notice's "Contact us" points |
| `currency` | string | the catalog's own | Display override |
| `labels` | `RegistrationSubscriptionLabels` | shipped copy | Copy overrides; unset strings keep the shipped word |
| `closed-text` | string | — | Replaces "Registration is closed" (React's `renderClosed`) |
| `reg-sub-proxy-url` | string | `/api/wildwood-regsub` | Where the shipped proxy is mounted |
| `show-feature-comparison` / `show-limits` | bool | `true` | Passed to the plan grid |
| `component-id` | string | generated | A stable id, for two instances on one page |

**Precedence: an explicit attribute always wins over the query string**, key by key. A host that
wrote `pre-selected-tier-id` meant it, and an address bar must not override the page's own
decision — but a page may hard-code the plan and still read the packs and the email out of the
link. `plan-selection`, `pack-selection` and `token-mode` are **strings** in the JS union's own
spelling, for the same reason the pricing view's are: an enum would force every host to write
`plan-selection="@SignupPlanSelection.Skip"`. `pack-selection="multi"` is accepted as a synonym of
`choose`, because that is what React calls it.

### Events

Razor has no callbacks, so every `on*` prop is a **bubbling `CustomEvent`** on the component root:

| Event | `detail` | Notes |
|---|---|---|
| `ww-regsub-signup-complete` | the `SignupOutcome` | **Cancelable.** `preventDefault()` stops the `complete-url` navigation |
| `ww-regsub-already-signed-in` | `{ appId }` | **Cancelable.** `preventDefault()` stops the `already-signed-in-url` navigation |
| `ww-regsub-cancel` | `{}` | The visitor backed out |
| `ww-regsub-error` | `{ code, message }` | `signup_failed`, `registration_token_rejected`, `catalog_unavailable`, `pack_quote_failed`, `pack_card_failed`, `pack_checkout_failed` |
| `ww-entitlements-changed` | `{ appId, reason: "signup" }` | Once, when the signup finishes |
| `ww-regsub-step` | `{ step }` | Every step change, for analytics |
| `ww-regsub-select` | `{ tierId, pricingId, billing, addOnIds }` | **Cancelable.** Raised when a plan choice is about to navigate (see below) |

```js
document.addEventListener('ww-regsub-signup-complete', function (e) {
    // e.detail = { userId, tier, packs: [{ addOnId, name, status, trialEnd, errorMessage }],
    //              tokenGrant, planActivationPending }
    e.preventDefault();   // stop the complete-url navigation and handle it yourself
});
```

`status` is one of `trialing` / `active` / `failed` / `granted`. **`granted`** is a pack the
registration token set up: there is a subscription row but no payment transaction behind it, so
nothing was charged. Granted packs are listed **first**.

### Token plans and invites

When the registration token grants a plan for this app, a summary panel appears above every later
step listing the plan, the packs and the features it includes — **names only, no prices**: a grant
is not a quote. The grant then **skips the plan and the payment steps entirely**, removes granted
packs from what is bought, and **suppresses the self-subscribe**, because subscribing over the
token's plan would replace the subscription the token just created.

`token-mode="required"` is invite redemption: the token step is the only way in, the plan and the
pack steps are skipped, and it overrides a **closed** configuration — the server validates the
invite itself. The app's settings are not even read in that mode.

A token the server **rejects** is a form-level message above the register form, not a failed flow.
A token whose details could not be **read** (an older server, a transport failure) is not the same
thing: the signup carries on as an ordinary one and the server has the last word at registration.

### Test hooks

Root `data-ww-view="signup"`, classes `ww-regsub ww-regsub-signup`. Every step's markup is
rendered and the script only flips which container is visible; the visible one carries
`data-ww-step`, spelled exactly as React spells it:
`loading | closed | register | token | plan | packs | payment | creating | disclaimers |
packCheckout | success | failed` — note the machine's `done` is **`success`** in the DOM. An
already-signed-in visitor gets a `.ww-regsub-notice` and no step. The legacy locators are
preserved: `.ww-tier-grid` / `.ww-tier-card`, `.ww-signup-processing`, `.ww-signup-disclaimers`,
`.ww-signup-success`, `.ww-plan-summary-card`, the register form's "Continue" / "Create Account",
and `<vc:payment />`'s own button and success panel.

### What differs from React, and why

| React | Razor | Why |
|---|---|---|
| `onSignupComplete`, `onCancel`, `onError`, `onAlreadySignedIn`, `onEntitlementsChanged` | bubbling `CustomEvent`s + `complete-url` / `already-signed-in-url` | A server-rendered stack cannot take a delegate |
| `renderClosed` node | `closed-text` string | Markup callbacks are impossible here |
| `initialCatalog` SSR snapshot | — | Razor gets it for free |
| Choosing a different **paid** plan re-renders the card form in place | it **navigates** to the same page with `tier`/`pricing`/`addons` in the query (raising `ww-regsub-select` first) | Every amount around the card form — the button, the trial note, the "not available" notice — was formatted by the server for one plan. Re-pricing them in the browser is the one thing this package does not do. Choosing the plan the server already priced, or a free one, does **not** navigate |
| The pack order summary shows a money total | it lists the packs and the card on file | Same reason: the quote's due-today amount is only known after the quote returns, and the browser states no price |
| `TokenRegistrationComponent` reused in `deferSubmission` mode | the same fields, rendered by this view | Razor's `<vc:token-registration />` owns its own submit and posts registrations itself; this flow has to hold the details until the card is taken. The password policy is read from the **same** route `token-registration.js` reads, never re-stated |
| Disclaimer list built by the component | rendered by the **view** | A disclaimer whose content format is HTML has to be rendered as HTML, and no script here writes `innerHTML` |

### Where the logic lives, and how it is tested

The two state machines (`signupTransition` / `packCheckoutTransition`, with their step tokens) are
ported **table-identical** from `@wildwood/react-shared` into
`wwwroot/js/regsub-machines.js` — pure functions, no DOM, no fetch. `wwwroot/js/regsub-signup.js`
is the driver: pure decisions at the top, DOM and I/O at the bottom. The server-side decisions are
`RegistrationSubscriptionSignupDecisions`.

This repo has no JavaScript test harness, so the machines carry a dependency-free Node self-test
at `WildwoodComponents.Tests/Razor/js/regsub-machines.selftest.mjs`, which replays a
representative subset of the TypeScript suite. `RegSubMachineSelfTestRunnerTests` shells out to
`node` to run it as part of `dotnet test`, and **skips** (rather than fails) when `node` is not on
PATH.
---

## Registration & Subscription — manage

`<vc:registration-subscription-manage />` is the Razor port of React's `ManageView` (and of the
Blazor view of the same name): what a customer already pays for, and every way of changing it.

```html
@* Minimal: the signed-in user's own subscription, tabbed *@
<vc:registration-subscription-manage />

@* Everything on *@
<vc:registration-subscription-manage
    app-id="my-app"
    layout="stacked"
    sections="subscription,plans,addOns,usage"
    show-status-above-tabs="true"
    allow-pack-self-service="true"
    allow-cancel="true"
    show-add-ons="true"
    currency="USD"
    contact-url="/contact"
    return-url="/account" />

@* An admin managing one user's subscription *@
<vc:registration-subscription-manage is-admin="true" user-id="@userId" />
```

Add the assets once, in your layout. **Order matters** — the three shared scripts come first, and
`subscription-admin.js` is what wires the panels' own actions:

```html
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/wildwood-razor-themes.css" />
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/regsub.css" />
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/subscription-admin.css" />
<script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>
<script src="~/_content/WildwoodComponents.Razor/js/regsub-packcheckout.js"></script>
<script src="~/_content/WildwoodComponents.Razor/js/regsub-planchange.js"></script>
<script src="~/_content/WildwoodComponents.Razor/js/subscription-admin.js"></script>
<script src="~/_content/WildwoodComponents.Razor/js/regsub-manage.js"></script>
```

### It is a composition, not a new surface

The six panels are the platform's own — `<vc:subscription-status-panel />`,
`<vc:tier-plans-panel />`, `<vc:features-panel />`, `<vc:add-ons-panel />`,
`<vc:usage-limits-panel />`, `<vc:overrides-panel />` — so a site swapping a hand-built plan page
for this keeps its markup and its locators. The root carries the `ww-subscription-admin-component`
class as well as `data-ww-view="manage"`, which is why every action `subscription-admin.js`
already wires keeps working unchanged: cancelling, feature overrides, usage limits, and the pack
rows' subscribe / cancel / reactivate, each still raising `ww-subscription-admin-changed` and
`ww-entitlements-changed` with its own reason.

What is new is the middle.

### The plan change, and the parked 3-D Secure change

```
plan clicked -> preview -> confirmation (EVERY layout) -> change
             -> [bank challenge on the card ON FILE -> complete the parked change] -> done
```

The change is posted to the shipped proxy with `SupportsPaymentAction: true` **for the signed-in
user's own subscription only**, which tells the server it may PARK a change on a bank challenge
rather than refusing it. The parked change comes back as `requiresAction` with a `clientSecret`
and a `pendingChangeId` — with `success: false`, which is **not** a failure — and the driver
confirms the charge with `stripe.confirmCardPayment(clientSecret)` against the card already on
file, then finishes the change at `tier-change/{id}/complete`. A `processing` answer means the
money is in and the server is still applying the change, so the completion is asked again (up to
five times, then it says so) rather than being reported as a refusal.

**Once the bank has been asked, the completion runs even if the component is torn down inside the
page.** The alternative is a customer who paid a proration and never got the plan.

Admin- and company-scoped changes are unchanged: they go to your app-tier proxy exactly as they
always did and never send `SupportsPaymentAction`, because only the signed-in user's own session
can answer a challenge and finish the parked change.

### There is no plan-change payment form

**Standing decision, and it is not an oversight.** React solves "this change needs a card" with a
built-in payment modal; Razor does not collect a card for a plan change.

So a confirmed change is **always posted**, whatever it costs. The money is settled server-side —
by the admin bypass, or by the card already on file, which is how a priced upgrade is normally
paid for. A preview saying `paymentRequired` means "this change costs money" (it is what draws the
"Today's charge" line), **not** "this account has no card", and it never stops the change being
attempted.

Only the server can say there is nothing to charge, and only once the change has been put to it.
When it refuses for that reason — recognised by the error **code** `PaymentMethodRequired`
(Shared `AddOnCheckoutErrorCodes`, the JS `AddOnCheckoutErrorCode` union), never by reading the
message — the view:

- shows the server's own message plus "This change needs a payment method."
  (replace it with `payment-required-text`), and does **not** offer Try Again — the same click
  would ask the same question; and
- raises **`ww-regsub-payment-required`**, whose `detail` carries everything a host needs to
  collect one itself: `{ tierId, tierName, pricingId, pricingModelId, price, priceText, trialDays,
  currency }`.

Any other refusal — including today's uncoded `Payment is required to upgrade…` from the
tier-change route — is an ordinary failure: the server's sentence, with Try Again.

```js
document.addEventListener('ww-regsub-payment-required', function (e) {
    // Mount <vc:payment /> yourself with e.detail.pricingModelId / price / trialDays,
    // then send the visitor back here once it succeeds.
});
```

A change that needs the card **already on file** authenticated is a different thing, and that one
the component finishes by itself — no card form is involved in a 3-D Secure confirmation.

### Pack self-service

`allow-pack-self-service="true"` puts **Add packs** on the packs panel and renders the picker: a
server-rendered grid of the packs the account can still buy — everything on sale **minus** the
ones it owns, the ones the plan bundles and the ones a registration token granted, decided by the
same `AddOnRowRules` the rows above are decided by. Prices are formatted on the server. Choosing
packs runs the same card-once checkout the signup uses (one quote, one card for a basket of any
size, each pack the bank wants authenticated walked in order), and the page reloads afterwards.

Cancel, reactivate and the "Included with your registration" rows stay where they are — on the
packs panel, unchanged.

The picker is the **signed-in user's own** action. An admin managing someone else gets no picker
and no publishable key: the shipped proxy acts as the signed-in user and has no admin scope.

### Parameters

| Tag-helper attribute | Type | Default | Meaning |
|---|---|---|---|
| `app-id` | string | the configured `AppId` | Which app is being managed |
| `layout` | `"tabs"` \| `"stacked"` | `"tabs"` | One tab bar, or every section down the page |
| `sections` | string (comma-separated) | all | `subscription,plans,features,addOns,usage,overrides`, **in order**. A section left out is gone, not hidden |
| `show-status-above-tabs` | bool | `false` | Lift the subscription card out of the section list |
| `is-admin` | bool | `false` | Admin controls, and the overrides panel |
| `user-id` | string | — | An admin managing ONE user's subscription |
| `company-id` | string | — | An admin managing a COMPANY's (company tracking only) |
| `allow-pack-self-service` | bool | `false` | "Add packs" and the picker |
| `allow-cancel` | bool | `true` | Offer cancelling the plan and its packs |
| `show-add-ons` | bool | `true` | Show the packs section at all |
| `currency` | string | the catalog's own, else USD | Display override |
| `labels` | `RegistrationSubscriptionLabels` | shipped copy | Copy overrides; unset strings keep the shipped word |
| `contact-url` | string | — | Where a plan with no price sends the visitor |
| `return-url` | string | — | Carried on the root; never navigated to |
| `payment-required-text` | string | — | Replaces "This change needs a payment method." |
| `proxy-base-url` | string | `/api/wildwood-app-tiers` | YOUR app-tier proxy, for the admin-scoped writes |
| `reg-sub-proxy-url` | string | `/api/wildwood-regsub` | Where the shipped proxy is mounted |
| `show-billing-toggle` | bool | `true` | The plans panel's monthly/annual toggle |
| `component-id` | string | generated | A stable id, for two instances on one page |

`layout` and `sections` are **strings** in the JS union's own spelling, for the same reason the
other two views' are: a Razor tag-helper attribute whose property is not a string compiles as a C#
expression, so an enum would force every host to write `layout="@ManageLayout.Stacked"`. An
unknown section name is dropped — a typo costs the section, not the page.

### Test hooks

Root `data-ww-view="manage"`, classes `ww-regsub ww-regsub-manage ww-subscription-admin-component`.
`data-ww-step` on the root is the plan change's step, spelled exactly as React spells it:
`idle | previewing | confirm | collectingPayment | changing | authenticating | completing | done |
failed`. Each section carries `data-ww-section="<section>"` (`addOns`, not `addons`) on its
`<section>` in the stacked layout and on its tab button in the tabbed one; the pack picker is
`data-ww-modal="packs"` and the confirmation `data-ww-modal="tier-change"`; pack cards and outcome
rows carry `data-ww-pack="<addOnId>"`. Every panel's own locators are untouched.

---

## Registration & Subscription — the shell

`<vc:registration-and-subscription view="pricing|signup|manage" />` is one tag helper over the
three views — React's `RegistrationAndSubscriptionComponent`, which is a `view` switch and nothing
else. It holds no state and reads nothing; it renders whichever view the attribute names and
forwards the parameters that apply to it.

```html
@* A page that decides at run time *@
<vc:registration-and-subscription view="@(User.Identity!.IsAuthenticated ? "manage" : "pricing")"
                                  allow-pack-self-service="true"
                                  include-json-ld="true" />
```

Writing `<vc:registration-subscription-pricing />`, `<vc:registration-subscription-signup />` or
`<vc:registration-subscription-manage />` directly is equally correct and one fewer indirection.
The shell earns its keep on the page that switches.

Which parameter belongs to which view:

| Parameters | Views |
|---|---|
| `app-id`, `currency`, `labels`, `contact-url`, `component-id` | all three |
| `return-url` | signup, manage |
| `reg-sub-proxy-url` | signup, manage |
| `show-add-ons` | pricing (pack grid, default off), manage (packs section, default on) |
| `pack-selection` | pricing (`none`/`multi`), signup (`none`/`choose`) |
| `show-billing-toggle` | pricing, manage (its plans panel) |
| `show-feature-comparison`, `show-limits` | pricing, signup |
| `show-plans`, `offer-free-tier-choice`, `add-on-groups`, `default-billing`, `highlight-tier-id`, `include-json-ld`, `json-ld-url`, `select-url`, `unavailable-text` | pricing |
| `pre-selected-tier-id`, `pre-selected-pricing-id`, `pre-selected-add-on-ids`, `registration-token`, `prefill-email`, `plan-selection`, `token-mode`, `require-billing-address`, `complete-url`, `already-signed-in-url`, `closed-text` | signup |
| `layout`, `sections`, `show-status-above-tabs`, `is-admin`, `user-id`, `company-id`, `allow-pack-self-service`, `allow-cancel`, `payment-required-text`, `proxy-base-url` | manage |

**An unknown `view` is a developer mistake and is reported as one**: in Development the page says
which three names it takes, anywhere else it renders nothing at all, and either way a warning is
logged. It does not guess — rendering the signup for an unknown word would offer a second account
to a signed-in customer.

---

## Registration & Subscription — the whole set

Three ViewComponents and a shell:

| Tag helper | ViewComponent | What it is |
|---|---|---|
| `<vc:registration-subscription-pricing />` | `RegistrationSubscriptionPricingViewComponent` | What the app sells, at the price the server is quoting right now |
| `<vc:registration-subscription-signup />` | `RegistrationSubscriptionSignupViewComponent` | An account, a plan, packs and a card — pay-first |
| `<vc:registration-subscription-manage />` | `RegistrationSubscriptionManageViewComponent` | What a customer pays for, and every way of changing it |
| `<vc:registration-and-subscription view="…" />` | `RegistrationAndSubscriptionViewComponent` | The `view` switch over the three |

### Every event

All of them are **bubbling `CustomEvent`s** on the component root — Razor takes no callbacks.

| Event | `detail` | Raised by | Notes |
|---|---|---|---|
| `ww-regsub-select` | `{ tierId, pricingId, billing, addOnIds }` | pricing, signup | **Cancelable.** `preventDefault()` stops the navigation |
| `ww-regsub-signup-complete` | the `SignupOutcome` | signup | **Cancelable.** Stops the `complete-url` navigation |
| `ww-regsub-already-signed-in` | `{ appId }` | signup | **Cancelable.** Stops the `already-signed-in-url` navigation |
| `ww-regsub-cancel` | `{}` | signup | The visitor backed out |
| `ww-regsub-plan-changed` | `{ appId, tierId, pricingId }` | manage | A plan change landed. The page reloads shortly after |
| `ww-regsub-payment-required` | `{ tierId, tierName, pricingId, pricingModelId, price, priceText, trialDays, currency }` | manage, subscription-admin | The change was **attempted** and the server refused it (`PaymentMethodRequired`) for want of a card this package does not collect |
| `ww-regsub-packs-changed` | `{ appId, packs: [{ addOnId, name, status, trialEnd, errorMessage }] }` | manage | The pack picker finished, whatever the outcomes |
| `ww-entitlements-changed` | `{ appId, reason }` | all | `signup` \| `tierChange` \| `addOn` \| `cancel` \| `reactivate` \| `manual` |
| `ww-regsub-error` | `{ code, message }` | all | See the codes below |
| `ww-regsub-step` | `{ step }` | signup, manage | Every step change, for analytics |

Error codes: `catalog_unavailable`, `signup_failed`, `registration_token_rejected`,
`pack_quote_failed`, `pack_card_failed`, `pack_checkout_failed`, `tier_preview_failed`,
`tier_change_failed`, `tier_change_authentication_failed`, `tier_change_completion_failed`, or the
server's own `errorCode` when it sent one (`pending_change_expired`,
`pending_change_payment_failed`, `pending_change_superseded`, `pending_change_not_found`,
`tier_change_already_in_progress`).

### Scripts and styles, in load order

```html
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/wildwood-razor-themes.css" />
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/regsub.css" />
<link rel="stylesheet" href="~/_content/WildwoodComponents.Razor/css/subscription-admin.css" />  <!-- manage only -->

<script src="~/_content/WildwoodComponents.Razor/js/regsub-machines.js"></script>     <!-- always first -->
<script src="~/_content/WildwoodComponents.Razor/js/regsub-packcheckout.js"></script> <!-- signup, manage -->
<script src="~/_content/WildwoodComponents.Razor/js/regsub-planchange.js"></script>   <!-- manage, subscription-admin -->
<script src="~/_content/WildwoodComponents.Razor/js/payment.js"></script>             <!-- signup -->
<script src="~/_content/WildwoodComponents.Razor/js/regsub-pricing.js"></script>      <!-- pricing -->
<script src="~/_content/WildwoodComponents.Razor/js/regsub-signup.js"></script>       <!-- signup -->
<script src="~/_content/WildwoodComponents.Razor/js/subscription-admin.js"></script>  <!-- manage -->
<script src="~/_content/WildwoodComponents.Razor/js/regsub-manage.js"></script>       <!-- manage -->
```

`regsub-machines.js` holds the three state machines (signup, pack checkout, plan change), ported
table-identical from `@wildwood/react-shared`. `regsub-packcheckout.js` and `regsub-planchange.js`
are the two **shared drivers**: the signup and the manage view buy packs through the same one, and
the manage view and `<vc:subscription-admin />` change a plan through the same one. That is not
tidiness — it is the part that moves money, and a second copy is a second place for "may the
server park this change" and "is a parked change ever completed" to drift. A source guard fails
the build if either sequence reappears in a view's own script.

**`<vc:subscription-admin />` now needs `regsub-machines.js` and `regsub-planchange.js` in the
page.** Without them every other action on the panel still works and a plan click says so in the
message bar rather than failing silently — but add them. In return the old admin panel gained the
parked-3-D-Secure completion for free.

### First-paint prices

Every price on all three views was formatted on the server, off the catalog that request read.
There is no fallback price, no remembered price and no "from" price, and a source guard greps the
C#, the markup and the scripts for one. The single exception is the plan-change confirmation: its
figures come from a live preview call, so the browser formats them — through the same
`en-US`-fixed helper the server uses, so a server-rendered amount and a browser-rendered one read
the same.

### Differences from React and Blazor, and why

| React / Blazor | Razor | Why |
|---|---|---|
| Callbacks (`onSelect`, `onSignupComplete`, `onPaymentRequired`, `onEntitlementsChanged`, …) | bubbling `CustomEvent`s plus URL parameters | A server-rendered stack cannot take a delegate |
| Markup callbacks (`renderClosed`, `loadingFallback`, `errorFallback`, `describeAddOn`) | string parameters, or the built-in notice | A tag-helper attribute cannot carry markup |
| A mutation re-renders in place | the page **reloads** | The panels are server-rendered, so the server is the one place that knows what they should say now. That is the established Razor refresh, and it is why every mutation waits ~1.2s before reloading so its message can be read |
| Choosing a different **paid** plan in signup re-renders the card form | it **navigates** with `tier`/`pricing`/`addons` in the query | Every amount around the card form was formatted for one plan; re-pricing in the browser is the one thing this package does not do |
| `PaymentModal` collects a card for a plan change | **no payment-collection modal**: the change is posted and the server charges the card on file; `ww-regsub-payment-required` and the server's message only when it refuses for want of one | Standing decision. The 3-D Secure confirmation of a card ALREADY ON FILE is still done here, because it needs no card form |
| `initialCatalog` SSR snapshot | — | Razor gets it for free |

---

## Migrating from the three deprecated ViewComponents

All three keep working and keep compiling; `[Obsolete]` is a **warning**, never an error.

| Deprecated | Replacement | What changes |
|---|---|---|
| `<vc:pricing-display />` | `<vc:registration-subscription-pricing />` | Same price list, plus pack selection, a JSON-LD offers graph, the cross-stack labels and `ww-regsub-select`. Prices come off the public catalog rather than the tier list |
| `<vc:app-tier />` | `<vc:registration-subscription-manage />` | The plan change is previewed, confirmed and — when the bank asks — authenticated and completed, instead of posting and hoping. Its one-time-charge behaviour is left as it is, exactly as the JS deprecation left React's |
| `<vc:signup-with-subscription />` | `<vc:registration-subscription-signup />` | **The paid path works.** The old step 3 is a hand-rolled card form whose "Complete Payment" button ships disabled with no handler, so a paid plan dead-ends unless the host mounts a payment component itself. The new view is pay-first on `<vc:payment />` and has no card-number fields anywhere |

Two things to do when you move:

1. **Swap the scripts.** `pricing-display.js` / `apptier.js` / `signup-subscription.js` are
   replaced by the load order above.
2. **Swap the routes you wrote.** The new views use the shipped `/api/wildwood-regsub` proxy for
   registration, login, the plan payment, subscribe, the disclaimer gate, the pack checkout and
   the plan change — so the `/api/wildwood-auth/register`, `/api/wildwood-subscription/subscribe`
   and pack routes you wrote for the old components can go. Your app-tier proxy is still needed
   for the **admin- and company-scoped** writes the manage view's panels make.

---

## Money is formatted in one place

Every amount the package renders goes through `FormatHelpers.FormatMoney(amount, currency)` in
`WildwoodComponents.Shared`, which reproduces the JS SDK's
`Intl.NumberFormat('en-US', { style: 'currency', currency })` byte for byte — CLDR's symbol, the
currency's own number of decimals (so JPY shows none), and a fixed `en-US` locale so a price
rendered on the server and re-rendered in the browser read the same.

**Deprecated, not removed:** `ViewHelpers.GetCurrencySymbol` and `ViewHelpers.FormatAmount` are
superseded by **`ViewHelpers.FormatMoney(amount, currency)`**. Both are still there and still do
exactly what they always did — your own views keep compiling and rendering unchanged — but they now
carry `[Obsolete]`, so the compiler points you at the replacement. Both old helpers read a
six-entry symbol table, so every currency outside it (CHF, SEK, PLN, ...) rendered inconsistently
and a zero-decimal currency always showed two. A tier or pack price is better taken from
`CatalogHelpers.FormatPrice(tier, price, fallbackCurrency)` or
`AddOnRowRules.FormatPrice(addOn, pricing, fallbackCurrency)`, which prefer the item's **own**
currency over the page's.

Client-side, the four scripts that must format an amount in the browser (`payment.js`,
`payment-form.js`, `token-registration.js`, `regsub-planchange.js`) each carry the same
`wwFormatMoney(amount, currency)` helper. The copies are deliberate — no shared script is loaded
on every page — and must stay identical; a test asserts they are. `subscription-admin.js` used to
be the fourth; it formats no money at all now that the tier-change confirmation lives in
`regsub-planchange.js`, and the copy went with the modal rather than being left behind unused.

---

## Subscription events: `detail.reason` and `ww-entitlements-changed`

Every mutation that changes what a user's plan includes now says **why**, using the six-word
vocabulary of the JS SDK's `entitlementsChanged` event and of C#
`WildwoodComponents.Shared.Models.EntitlementsChangedReasons`:

`signup` · `tierChange` · `addOn` · `cancel` · `reactivate` · `manual`

Two things carry it.

**1. The existing changed events gained `detail.reason`** (additive — every other field is
unchanged):

| Event | Raised by | Detail |
|---|---|---|
| `ww-subscription-admin-changed` | `<vc:subscription-admin />` | `action`, `appId`, **`reason`** |
| `ww-subscription-changed` | `<vc:app-tier />` | `action`, `tierId` (subscribe only), **`reason`** |
| `ww-signup-complete` | `<vc:signup-with-subscription />` | `tierId`, `tierName`, `fromTokenGrant`, `subscriptionPending`, `user`, **`reason`** (always `signup`) |

`reason` is **`null`** on `ww-subscription-admin-changed` for the two actions that change a number
rather than an entitlement — `limit_updated` and `usage_reset`.

**2. A dedicated bubbling `ww-entitlements-changed`** is raised after every entitlement-changing
mutation, so a host listens once instead of learning each component's own action words:

```js
document.addEventListener('ww-entitlements-changed', function (e) {
    // e.detail = { appId, reason }  -> re-read the subscription and the feature map
    refreshMyFeatureGates(e.detail.appId);
});
```

| Mutation | `reason` | Raised by |
|---|---|---|
| Tier change or first subscribe | `tierChange` | `subscription-admin.js`, `apptier.js` |
| Subscription cancelled | `cancel` | `subscription-admin.js`, `apptier.js` |
| Pack subscribed | `addOn` | `subscription-admin.js` |
| Pack cancelled | `cancel` | `subscription-admin.js` |
| Pack reactivated | `reactivate` | `subscription-admin.js` |
| Feature override set, made permanent or removed | `manual` | `subscription-admin.js` |
| Signup completed | `signup` | `signup-subscription.js` |

It is a signal to **re-read**, not proof the change has propagated: fetch the subscription and the
feature map again rather than assuming what changed.

---

## Payment component: trials, events and the billing address

`<vc:payment />` renders the provider picker, the Stripe card element and the submit button;
`wwwroot/js/payment.js` drives them and posts to the **host-supplied** payment proxy
(`proxy-base-url`, default `/api/wildwood-payment`) at `POST {proxy}/initiate` and
`POST {proxy}/confirm`. Bind those two bodies to the shared `InitiatePaymentRequest` and forward
them to `IWildwoodPaymentService.InitiatePaymentAsync` / `ConfirmPaymentAsync`.

### New parameter

| Parameter | Type | What it does |
|---|---|---|
| `trial-days` | `int?` | The subscription starts with this many free-trial days. The button reads "Start N-day free trial", a note says nothing is taken today, and the initiate request carries `supportsSetupIntent: true` so Stripe answers with a **SetupIntent**. The card is then confirmed with `confirmCardSetup` and saved — a trial that collects nothing leaves the processor with nothing to bill at trial end. |

Two request fields follow from it, and a host proxy that re-serialises a bound
`InitiatePaymentRequest` carries both without changes:

- **`supportsSetupIntent`** — sent only for a Stripe payment that is actually offering a trial.
- **`BillingAddress`** — PascalCase, matching the JS SDK and the `[JsonPropertyName]` on the shared
  model. Sent only when `require-billing-address` is set, and only once all six fields plus the
  country are filled in: an address the app requires is validated **before** anything is charged,
  because an incomplete one fails at the provider after the intent already exists.

Card data never leaves Stripe Elements. The component has no card-number input on the Stripe path
and none should be added.

### Events

| Event | When | Detail |
|---|---|---|
| `ww-payment-success` | **exactly once per payment**, when it completes | `transactionId`, `paymentIntentId`, `subscriptionId`, `amount`, `currency`, `providerType`, `receiptUrl`, `trialEnd` |
| `ww-payment-continue` | the success panel's **Continue** button | the same payload |
| `ww-payment-failure` | a refusal or a transport failure | `errorMessage`, `errorCode`, `providerType`, `isRetryable` |
| `ww-payment-cancel` | the Cancel button | — |

> **Behaviour change.** Continue used to re-dispatch `ww-payment-success`, so a host that subscribed
> a plan or completed an upgrade on that event did it **twice**. Continue now dispatches
> `ww-payment-continue` and nothing else. A host that relied on the second `ww-payment-success` must
> listen for `ww-payment-continue` instead. The library's own listeners
> (`signup-subscription.js`, `token-registration.js`) always treated the second event as a duplicate,
> so they are unaffected.

Three further rules the script now follows, all of them about not taking money twice:

1. **A retry confirms the same intent.** A declined card keeps the intent the server created, keyed
   by provider, pricing model, amount and one-off/subscription. Initiating again would leave a
   second subscription behind.
2. **The confirm uses the id the server recorded** (`paymentIntentId`, else `subscriptionId`) — a
   subscription's first invoice is not the id the browser holds. With neither, nothing is confirmed
   and the form says so rather than asking the server to verify an empty id.
3. **A trial that is not available asks first.** When the view advertised a trial and the server
   answers with a charge secret instead, the form shows "The free trial isn't available on your
   account, so {amount} will be charged today. Select Pay to continue.", switches the button to
   "Pay {amount}" and confirms the SAME intent on the next click. Re-rendering the component for
   another plan and calling `wwPayment.init(componentId)` (or `wwPayment.update(componentId)`)
   re-reads the plan and offers the new plan's trial again.

### Token plans in registration

`<vc:token-registration />` reads `appGrants` from `validate-detailed`. When the token grants a plan
for this app (app ids compared case-insensitively) it renders a **"Your registration token
includes"** summary — tier, pricing name, packs and features by display name, never a price — and
dispatches a bubbling **`ww-token-grant`** (`detail: { appId, grant }`, `grant: null` when the token
is cleared), plus `data-token-grant="true"` on the component root.
`signup-subscription.js` listens for it, skips plan payment and does **not** self-subscribe: the
second subscribe replaces the token's plan, cancelling what the token had just created.

Every dispatch wins, clears included. Clearing the token (or swapping it for one that grants nothing
here) sends `grant: null`, and the wizard drops its copy: the payment step comes back and the
`/subscribe` call is made again. Both decisions — *collect payment?* and *self-subscribe?* — are
re-derived from current state at the moment they are acted on, never latched when the token was
applied, so a token applied and then cleared leaves the plain plan-and-pay flow exactly as it was.
A dispatch carrying a different `appId` than the wizard's is ignored, so it neither sets a grant
nor clears one.

A self-subscribe that is refused is likewise **not** a failed signup. The account exists and is
signed in, so the wizard finishes and says "Your account is ready! Plan activation is pending — you
can select a plan from your dashboard." rather than leaving a spinner or an error on screen.

The subscription admin's tier change collects no payment by standing decision, so it emits no
payment-required event and there is nothing there to carry a pricing model, trial or price.

---

## Authentication proxy

`<vc:authentication />` works the same way: its JavaScript posts to a **thin server-side proxy in
your app** (`data-proxy-url`), never to WildwoodAPI directly, so the Bearer token stays on the
server. The proxy forwards to `IWildwoodAuthService`, registered for you by
`AddWildwoodComponentsRazor`.

Five routes, all `POST`:

| Path (relative to the proxy base) | Forwards to | Notes |
|-----------------------------------|-------------|-------|
| `/login`                          | `LoginAsync` | May answer `requiresTwoFactor` or `requiresPasswordReset` instead of signing in |
| `/register`                       | `RegisterAsync` | |
| `/forgot-password`                | `ForgotPasswordAsync` | Emails a **temporary password**, not a reset link. Always answer the same way, so the response cannot reveal who has an account |
| `/two-factor-verify`              | `VerifyTwoFactorAsync` | Can also answer `requiresPasswordReset` — see below |
| `/reset-password`                 | `ResetPasswordAsync` | The forced reset. Authenticated by the session the temporary-password sign-in established |

### The response shape the script reads

```jsonc
{
  "success": false,               // true => the script navigates to redirectUrl
  "message": "…",                 // shown to the user on failure
  "requiresTwoFactor": false,     // => show the 2FA view
  "twoFactorSessionId": null,     // echoed back on /two-factor-verify
  "requiresPasswordReset": false, // => show the forced-reset view
  "redirectUrl": "/"
}
```

Answer a **rejected** sign-in with HTTP 200 and `success: false`, not a 4xx. The script treats any
non-OK status as a transport failure and replaces your message with a generic one, so a 401 would
hide "wrong password" from the user.

### The forced password reset

An account created by an administrator, or one that used **Forgot password**, signs in with a
temporary password. That sign-in is authenticated but **not finished**: WildwoodAPI answers with a
real JWT *and* `requiresPasswordReset: true`.

Two rules make this work, and both are easy to get wrong:

1. **Store the tokens, withhold the sign-in.** `api/auth/reset-password` is `[Authorize]` and
   identifies the user from the JWT alone — there is no email or user id in the body — so
   `IWildwoodAuthService` must be holding the session before `/reset-password` is called. But do
   **not** issue your app's auth cookie yet: the temporary password is still in force, and the
   rest of your site would treat the user as fully signed in.
2. **Sign the user in after the reset succeeds**, from the session you already hold. Do not
   re-authenticate with the new password — that would re-trigger two-factor for an account that
   just satisfied it on the way to this screen.

`/two-factor-verify` needs the same check as `/login`: with 2FA enabled, `requiresPasswordReset`
only surfaces once the code has been verified.

```csharp
// POST {proxy}/login
[HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    var result = await _auth.LoginAsync(request);

    if (!result.Succeeded)
        return Ok(new { success = false, message = result.ErrorMessage });

    if (result.Response!.RequiresTwoFactor)
        return Ok(new { success = false, requiresTwoFactor = true, twoFactorSessionId = result.Response.TwoFactorSessionId });

    // Authenticated, but on a temporary password: no app sign-in yet.
    if (result.Response.RequiresPasswordReset)
        return Ok(new { success = false, requiresPasswordReset = true, message = "Please choose a new password to finish signing in." });

    await SignInAsync(result.Response);   // your app's cookie sign-in
    return Ok(new { success = true, redirectUrl = LocalReturnUrl(request.ReturnUrl) });
}

// POST {proxy}/reset-password
[HttpPost("reset-password")]
public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordProxyRequest request)
{
    if (request.NewPassword != request.ConfirmPassword)
        return Ok(new { success = false, message = "Passwords must match." });

    var result = await _auth.ResetPasswordAsync(new ResetPasswordRequest
    {
        NewPassword = request.NewPassword,
        ConfirmPassword = request.ConfirmPassword
    });

    if (!result.Success)
        return Ok(new { success = false, message = result.Message });

    // The password is real now, so finish the sign-in the login step withheld.
    await SignInFromCurrentSessionAsync(request.Username);
    return Ok(new { success = true, redirectUrl = LocalReturnUrl(request.ReturnUrl) });
}
```

`Username` is posted by the script from the login form: WildwoodAPI identifies the user from the
JWT, but your cookie needs a name and the session stores tokens only.

> **Always resolve `returnUrl` against a local-URL check** before echoing it back as
> `redirectUrl` — it arrives from the browser, and returning it unvalidated is an open redirect.
> The WebForms package's `WildwoodProxyHandlerBase.ResolveReturnUrl` shows the shape: anything not
> rooted-and-local falls back to `/`, including protocol-relative targets and ones hiding a
> tab/CR/LF.

---

## AI chat: voice input (ships with the library)

`<vc:ai-chat />` has one mic button with **two mechanisms** behind it, picked once per component,
the same pair Blazor's `AIChatComponent` has had since the speech recorder landed:

| Mode | When | What happens |
|---|---|---|
| `native` | the browser has the Web Speech API (Chrome, Edge, Safari with dictation on) | live recognition with interim results. No server call; no audio leaves the machine. |
| `recorder` | it does not (Firefox, Brave/Opera/Vivaldi, WebView2, Safari with dictation off) | the clip is captured with `MediaRecorder` and POSTed to the shipped route below, which forwards it to WildwoodAPI's `stt/transcribe`. One server call per clip, so it records only on an explicit tap. |
| `none` | neither is available, or the recorder has nowhere to upload | **no mic button is rendered at all.** |

A native session can also fail at *runtime* in an engine that exposes the Web Speech API without
a recognition backend. `network`, `service-not-allowed` and `language-not-supported` mean "this
will never work here": they downgrade the component to `recorder` for the rest of its life and
carry straight on into a recording, so the tap that hit the dead button still records what the
user is saying. Every other code (`no-speech`, `aborted`, …) is an ordinary end of a session and
downgrades nothing.

**Caps.** A clip auto-stops at **60 seconds** and is refused past **25 MB** — the server's
transcription limit — with a message, rather than being uploaded for the server to reject. Both
are the constants in `WildwoodComponents.Shared/Utilities/SpeechAudioFormats.cs`, which is also
where the media-type table and the failure wording live, so the two .NET stacks and the proxy
cannot drift apart.

**With `enable-stt="false"` nothing speech-related runs**: no capability detection, no mic
button, no microphone request, no `SpeechRecognition`. The root element carries no `data-stt-*`
attributes at all in that case.

### The shipped route

`WildwoodSpeechProxyController` is part of the package and mounts at
**`POST /api/wildwood-stt/transcribe`**. A Razor app keeps the user's JWT in the server session,
so the browser cannot call WildwoodAPI itself.

Host wiring is the same as for the notifications, attribution and registration/subscription
proxies — MVC controllers plus server-side session:

```csharp
builder.Services.AddControllers();
builder.Services.AddSession();
// ...
app.UseSession();
app.MapControllers();
```

If your host restricts controller discovery, add the assembly explicitly:

```csharp
builder.Services.AddControllers()
    .AddApplicationPart(typeof(WildwoodComponents.Razor.Controllers.WildwoodSpeechProxyController).Assembly);
```

Its rules, all of them deliberate:

| Situation | Answer |
|---|---|
| No signed-in session | **401**, and nothing is forwarded — no empty bearer ever reaches WildwoodAPI |
| Body is not `multipart/form-data` | **415** |
| Not exactly one file part | **400** |
| The part is not recorded audio on the allow-list | **415** — this route must not become a generic file relay |
| Past the 25 MB cap | **413**, as a structured refusal rather than an exception; the request-size and multipart-length limits stop an oversize body before it is buffered, and an explicit length check catches the rest |
| The transcription itself failed | **200** with `success:false` and the provider's message — a refusal is data, and "speech-to-text is not configured" has to read differently from "that route is gone" |
| It worked | **200** with `success:true` and the text |

Every one of those answers is a `SpeechTranscriptionResult`, so the script parses one shape
whatever the status. There is **no anti-forgery token**, matching the other three shipped
proxies; a host that wants CSRF protection should apply its own filter uniformly across all four.

### Parameters

| Parameter | Default | Purpose |
|---|---|---|
| `enable-stt` | `true` | the one gate — see above |
| `speech-proxy-url` | `/api/wildwood-stt/transcribe` | move the route if you mount it elsewhere |
| `speech-language` | *(none)* | BCP-47 tag for recognition and for the provider; the script falls back to `en-US` |

### Calling it from your own code

`IWildwoodAIChatService.TranscribeAudioAsync(audio, contentType, configurationId, language)` is
the client half, and is wire-identical to Blazor's `IAIService.TranscribeAudioAsync`: one
multipart POST to `stt/transcribe`, the audio in a part named `file` called `speech.<ext>`
carrying the bare media type, optional fields omitted when empty. It **never throws** — every
failure comes back as `Success = false` with a presentable `ErrorMessage` — and it puts the
session bearer on the request rather than on the shared `HttpClient`.

### Parity

Recorded voice input was Blazor-only. Razor now matches it: the same mode detection, the same
three downgrade codes, the same mime preference order, the same 60 s / 25 MB caps and the same
`stt/transcribe` call. What stays different is only the shape a server-rendered stack forces —
the browser posts through a same-origin proxy instead of holding a JWT, and the clip is
transcribed by the server rather than streamed over a circuit.
