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
