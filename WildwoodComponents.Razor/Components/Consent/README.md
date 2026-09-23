# Consent Banner (Razor)

`<vc:consent-banner>` renders a cookie/consent banner + preferences modal and ships a client-side
engine (`consent.js`) that owns the cookie, GPC, the show/suppress decision table, and
block-before-consent third-party script injection — a vanilla port of the Blazor `ConsentBanner` and
`@wildwood/core`'s `ConsentService`.

## Usage

```cshtml
@* Direct mode: the engine calls the anonymous /api/consent/* endpoints on the WildwoodAPI host. *@
<vc:consent-banner app-id="my-app" />
```

```cshtml
@* Proxy mode: the engine calls a same-origin proxy instead, avoiding cross-origin CORS. *@
<vc:consent-banner app-id="my-app" proxy-base-url="/api/wildwood-consent" />
```

### Direct vs proxy

The consent endpoints are **anonymous**, so the engine can call them directly. That requires the
WildwoodAPI to send CORS headers for your site's origin (the same requirement as the Blazor
component). If your API isn't configured for cross-origin browser calls, use **proxy mode**: set
`proxy-base-url` and add the proxy controller below. The proxy forwards config as **raw JSON** so the
third-party script registry the engine needs is preserved.

## Proxy controller (copy-paste)

`AddWildwoodComponentsRazor(...)` already registers `IWildwoodConsentService`.

```csharp
using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

[ApiController]
[Route("api/wildwood-consent")]
public class WildwoodConsentProxyController : ControllerBase
{
    private readonly IWildwoodConsentService _consent;

    public WildwoodConsentProxyController(IWildwoodConsentService consent) => _consent = consent;

    // GET /api/wildwood-consent/config?appId=...  → raw passthrough (preserves the script registry)
    [HttpGet("config")]
    public async Task<IActionResult> GetConfig([FromQuery] string appId)
    {
        var json = await _consent.GetConfigRawAsync(appId);
        return json is null ? NotFound() : Content(json, "application/json");
    }

    // POST /api/wildwood-consent/record  → records the visitor's decision
    [HttpPost("record")]
    public async Task<IActionResult> Record([FromBody] ConsentRecordModel record)
    {
        await _consent.RecordDecisionAsync(record);
        return Ok();
    }
}
```

## It does not cover your page

The banner is `position: fixed` with a very high z-index, so anything anchored to the same edge —
a chat composer, a sticky action bar — would otherwise sit underneath it and quietly take no
clicks. While it is up, `consent.js` measures the banner, publishes `--ww-consent-height` on
`<html>`, and adds that height to `<body>`'s padding at the edge it is anchored to. The page's own
padding is added to, not replaced, and is restored exactly as found once the visitor decides — a
page that had none inline gets the declaration removed, not zeroed over its own stylesheet.

**Only the bars reserve room.** `bottomBar` pads the bottom and `topBar` the top. A `corner` card
is a ~420px box inset from the bottom-right, so padding the whole page for it would leave a
full-width blank strip under the content for as long as the banner is up: the corner publishes
`--ww-consent-height` and pads nothing. Give a corner card room yourself if you need it, or use
`bottomBar`.

Several `<vc:consent-banner>` on one page do not fight over it: each holds its own reservation
(the banner element is tagged `data-ww-consent-space`), the page reserves the tallest live one on
each edge, and it gets its own padding back only when the last of them goes.

```cshtml
@* Place the room yourself: the height is still published as --ww-consent-height. *@
<vc:consent-banner app-id="my-app" reserve-space="false" />
```

| Tag-helper attribute | Default | Meaning |
|---|---|---|
| `reserve-space` | `true` | Add the banner's measured height to the page's padding while it is up |

Ported from `@wildwood/react`'s `ConsentBanner reserveSpace` prop; the Blazor `ConsentBanner` has
the same parameter.

## Host-app control (`window.wildwoodConsent`)

`consent.js` exposes a small API so app code can drive the banner outside the built-in UI:

```js
window.wildwoodConsent.reopen();              // open the preferences dialog (e.g. a footer link)
window.wildwoodConsent.isGranted('Analytics'); // gate a first-party feature on consent
await window.wildwoodConsent.withdraw();        // clear consent; reload to fully clear injected scripts
window.wildwoodConsent.getState();              // current consent state
```

All methods take an optional trailing `appId` argument to target a specific instance when more than
one banner is on the page (normally there is only one).
