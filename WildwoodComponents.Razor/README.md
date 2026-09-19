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
| `POST /tier-change` | session | `ChangeTierAsync(options)` — body `{ "NewAppTierId", "NewAppTierPricingId", "Immediate", "PaymentTransactionId", "SupportsPaymentAction" }` |
| `POST /tier-change/{pendingChangeId}/complete` | session | `CompleteTierChangeAsync` |
| `GET  /catalog?currency=` | anonymous | `GetPublicCatalogAsync` — public data the pricing page already renders |
| `GET  /token-details?token=` | anonymous | `GetRegistrationTokenDetailsAsync` |
| `POST /token-details` | anonymous | the same lookup with the token in the body (`{ "Token" }`), so it never lands in an access log |

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
`payment-form.js`, `token-registration.js`, `subscription-admin.js`) each carry the same
`wwFormatMoney(amount, currency)` helper. The copies are deliberate — no shared script is loaded
on every page — and must stay identical; a test asserts they are.

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
