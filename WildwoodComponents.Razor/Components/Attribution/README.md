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

## Host-app control (`window.wildwoodAttribution`)

```js
await window.wildwoodAttribution.initialize(baseUrl, appId); // what the tag does for you (idempotent)
window.wildwoodAttribution.captureUrl(url, referrer);       // apply a URL as a touch, e.g. after client-side routing
window.wildwoodAttribution.getForRegistration();            // the payload for a signup request, or null
window.wildwoodAttribution.clear();                         // drop the touches after a recorded signup
window.wildwoodAttribution.getState();                      // visitor key, touches, persistence and config
```

## Load it once

`attribution.js` ignores a second load of itself, including the `data-app-id` / `data-base-url` on that second script
tag. If a page already includes the script by hand, remove that tag when you add `<vc:attribution>`; otherwise the
component's `app-id` and `base-url` are never used.
