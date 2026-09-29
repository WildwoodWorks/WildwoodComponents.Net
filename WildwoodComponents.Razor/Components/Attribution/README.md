# Campaign Attribution (Razor)

`<vc:attribution>` loads the Campaign Attribution engine (`attribution.js`), a vanilla port of the Blazor
`AttributionBootstrap` and `@wildwood/core`'s `AttributionService`. It has no UI: it captures the UTM tags, click id
and referrer from the landing URL, keeps a first and a last touch, and hands them to your signups.

## Usage

```cshtml
@* In the layout, once per page, as early as possible. *@
<vc:attribution app-id="my-app" />
```

`app-id` defaults to the `AppId` configured in `AddWildwoodComponentsRazor(...)`, and the optional `base-url` defaults
to the configured WildwoodAPI base URL (a trailing `/api` is removed).

The engine calls the anonymous `/api/attribution/config` and `/api/attribution/touch` endpoints on the WildwoodAPI host
directly, so the API must allow your site's origin (the same requirement as the consent banner in direct mode).
Attribution must also be enabled for the app (WildwoodAdmin: the app's **Components** page, **Campaign Attribution**).

## What it captures

- UTM tags, a known ad click id and the referrer host, read from the landing URL before the page can navigate away.
- A **first touch** and a **last touch**. They are stored in the browser (`ww_attribution`) only once the visitor
  grants the app's consent category (Analytics by default), and follow the consent banner's `wildwood:consent-change`
  events. Until then they are held in memory for the current page only.
- When the app has the landing beacon on, each tagged landing is recorded anonymously (visits and conversion rate).

## Registration

`authentication.js`, `signup-subscription.js` and `token-registration.js` attach the captured payload to their
registration requests and clear it after a successful signup. `RegisterRequest.Attribution` carries it through your
auth proxy controller to `IWildwoodAuthService.RegisterAsync`, so a proxy that binds `RegisterRequest` needs no change.

A custom signup form should send the payload itself and clear it after success:

```js
const body = { email, password, attribution: window.wildwoodAttribution ? window.wildwoodAttribution.getForRegistration() : null };
// ...after a successful registration:
if (window.wildwoodAttribution) window.wildwoodAttribution.clear();
```

## Sign-in provider signups (the claim)

A sign-in provider signup has no registration request to carry the payload, and a Razor app keeps the user's JWT in
the server session, so the claim goes through a same-origin proxy that ships with this library:

1. `AddWildwoodComponentsRazor(...)` registers `IWildwoodAttributionService`, which uses the configured `AppId`.
2. Make `WildwoodAttributionProxyController` (`POST /api/wildwood-attribution/claim`) reachable and keep server-side
   session on, as for the notifications proxy:

   ```csharp
   builder.Services.AddControllers();
   // ...
   app.UseSession();
   app.MapControllers();
   ```

When a page renders for a signed-in session, `<vc:attribution>` adds `data-claim-url` to the script tag and the engine
posts its captured touches there. The proxy forwards them with the session token; the touches are cleared when it
answers OK, and a failed attempt is not repeated for the rest of the browser session. WildwoodAPI records a claim only
for an account created in the last 15 minutes that has no attribution yet, so claiming on later signed-in pages is
harmless.

```cshtml
<vc:attribution app-id="my-app" enable-claim="false" />              @* no claim *@
<vc:attribution app-id="my-app" claim-url="/my-proxy/claim" />      @* your own proxy route *@
```

A provider sign-in is a full-page redirect: touches captured before the visitor granted the consent category live in
memory only and do not survive it.

## Host-app control (`window.wildwoodAttribution`)

```js
await window.wildwoodAttribution.initialize(baseUrl, appId); // what the tag does for you (idempotent)
window.wildwoodAttribution.captureUrl(url, referrer);       // apply a URL as a touch, e.g. after client-side routing
window.wildwoodAttribution.getForRegistration();            // the payload for a signup request, or null
window.wildwoodAttribution.clear();                         // drop the touches after a recorded signup
window.wildwoodAttribution.getState();                      // visitor key, touches, persistence and config
window.wildwoodAttribution.track('demo_booked', { label: 'pricing', value: 1 }); // a funnel event
window.wildwoodAttribution.trackCta('nav_pricing');         // a cta_click with this label
await window.wildwoodAttribution.flush();                   // send queued funnel events now
window.wildwoodAttribution.signupErrorCategory('USERNAME_EXISTS'); // 'username_taken'
```

## Funnel tracking

When the app turns funnel tracking on (WildwoodAdmin, Components, Campaign Attribution), the engine also records what
a visitor does between landing and signing up. It behaves exactly like `@wildwood/core` and the Blazor engine (the
tracker is the same code, and a Node self-test drives both):

- A **session** that ends after 30 minutes without activity. Each registration payload carries the `sessionKey`, the
  `deviceClass` (`mobile` under 768px, `tablet` under 1024px or under 1280px with a coarse pointer, else `desktop`)
  and the visitor's `sessionCount`.
- **Auto events**, each behind its own config switch: `page_view` for the landing page and every
  `history.pushState` / `replaceState` / `popstate` navigation (once per path), `scroll_depth` at 25/50/75/100
  percent, `engaged` once per session (half the page scrolled, or an interaction plus 10 seconds visible),
  `time_on_page`, and `cta_click` for clicks on elements marked `data-ww-cta`:

  ```html
  <a href="/signup" data-ww-cta="hero_start_trial">Start your free trial</a>
  ```

- **Your own events** through `track()`: a standard name or one of the app's custom names (`^[a-z0-9_]{1,40}$`).
  Calls made before the config loads are buffered; a name the app does not allow is dropped; `signup_complete`,
  `trial_started` and `purchase` are recorded by the server, so a client call with one is dropped too.
- Events go to the anonymous `POST /api/attribution/events` in batches of at most 25: every 5 seconds, and by
  `sendBeacon` (a `text/plain` body, with a keepalive `fetch` as the fallback) when the page is hidden or unloads.
- **Before consent**, with "Session storage before consent" on, the visitor key, the session and the touches are
  mirrored to `sessionStorage` under `ww_attribution_session` while the visitor has not decided, so a reload in the
  same tab keeps them. The mirror moves to `ww_attribution` when consent is granted and is removed when it is declined
  or withdrawn.

Labels carry no personal data: keep your own labels to names like `pricing_page`, never an email or free text.

### Registration funnel events

`token-registration.js` (`<vc:token-registration>`) and `regsub-signup.js` (`<vc:registration-subscription-signup>`)
report the signup steps on their own: `signup_view` when the form renders, `signup_start` on the first focus in the
form, `signup_submit` on submit, `signup_error` with a category (`validation`, `email_taken`, `username_taken`,
`password_policy`, `captcha`, `invalid_token`, `registration_closed`, `rate_limited`, `network`, `server` or
`unknown`, never the visitor's input or the server's message), `plan_selected` with the tier id and `checkout_start`
with the pricing option id when the payment step opens. The one-shot steps are sent once per session, so a form
inside a flow that also reports them is not counted twice.

A custom registration page reports the same steps itself:

```js
var ww = window.wildwoodAttribution;
form.addEventListener('focusin', function once() { ww.track('signup_start'); form.removeEventListener('focusin', once); });
ww.track('signup_view');
// on submit, before the request goes out:
ww.track('signup_submit');
// on a refusal, from the server's errorCode (or the HTTP status):
ww.track('signup_error', { label: ww.signupErrorCategory(result.errorCode, response.status) });
// on the plan and payment steps:
ww.track('plan_selected', { label: tierId });
ww.track('checkout_start', { label: pricingId });
```

The older `signup-subscription.js` (`<vc:signup-with-subscription>`) does not report the steps; use the pattern above
from its `ww-*` events if you need them there.

## Load it once

`attribution.js` ignores a second load of itself, including the `data-app-id` / `data-base-url` on that second script
tag. If a page already includes the script by hand, remove that tag when you add `<vc:attribution>`; otherwise the
component's `app-id` and `base-url` are never used.
