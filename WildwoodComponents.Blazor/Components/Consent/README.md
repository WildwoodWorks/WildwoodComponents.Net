# ConsentBanner (Blazor)

Block-before-consent cookie/consent banner. No gated third-party script loads until the visitor
consents to its category. Honors the Global Privacy Control (GPC) signal and exposes the CCPA opt-out
surfaces. The decision engine runs in JS (loaded via JS isolation from
`wwwroot/js/wildwood-consent.js`); this component renders the banner + preferences UI with `--ww-*`
theme tokens and drives the engine through `IConsentService`.

## Usage

```razor
@using WildwoodComponents.Blazor.Components.Consent

<ConsentBanner AppId="@appId" OnConsentChanged="HandleConsentChanged" />
```

Register services with `builder.Services.AddWildwoodComponents(...)` and set `BaseUrl` to your
WildwoodAPI host. `ConsentBanner` fetches `GET /api/consent/config` on first render, applies the
show/suppress decision table, and injects `StrictlyNecessary` + any previously-consented scripts.

### Parameters

| Parameter | Default | Description |
|-----------|---------|-------------|
| `AppId` | — | App whose consent config + script registry to load (required). |
| `BaseUrl` | options | Override the WildwoodAPI base URL. |
| `OnConsentChanged` | — | `EventCallback<ConsentStateModel>` fired on every consent change. |
| `ShowReopenLink` | `true` | Footer "Privacy choices" link to reopen preferences. |
| `ShowFooterOptOut` | `true` | Standalone one-click "Do Not Sell or Share" / "Limit Use of Sensitive PI" footer links (when the config enables those surfaces). |
| `ReserveSpace` | `true` | While the banner is up, add its measured height to the page's padding at the edge the banner is anchored to, so the fixed banner cannot cover the host's own edge-anchored UI. Set it false to place the room yourself. |

Call `ReopenPreferences()` (capture the component with `@ref`) to open preferences from your own
footer/"Do Not Sell" link.

## Behavior notes

- **It does not cover your page.** The banner is `position: fixed` with a very high z-index, so
  anything anchored to the same edge — a chat composer, a sticky action bar — would otherwise sit
  underneath it and quietly take no clicks. While it is up, the banner measures itself, publishes
  `--ww-consent-height` on `<html>`, and adds that height to `<body>`'s padding at the edge it is
  anchored to. The page's own padding is added to, not replaced, and is restored exactly as found
  when the banner goes or the component is removed (a page that had none inline gets the
  declaration removed, not zeroed over your stylesheet). `ReserveSpace="false"` keeps the
  published variable and leaves the padding to you. Ported from `@wildwood/react`'s
  `ConsentBanner reserveSpace` prop.
- **Only the bars reserve room.** `bottomBar` pads the bottom and `topBar` the top. A `corner`
  card is a ~420px box inset from the bottom-right, so padding the whole page for it would leave
  a full-width blank strip under your content for as long as the banner is up: the corner
  publishes `--ww-consent-height` and pads nothing. Give a corner card room yourself if you need
  it, or use `bottomBar`.
- **Two banners on a page do not fight.** Each holds its own reservation; the page reserves the
  tallest live one on each edge and gets its own padding back only when the last of them goes.
- **Block-before-consent:** gated scripts (from Third-Party Script Management) inject only after the
  matching category is consented to. `StrictlyNecessary` may load immediately.
- **GPC:** when honored and present, `Advertising` + `Sensitive` are forced off (even outside any geo
  target); the banner still shows for the remaining categories, and the decision is recorded when the
  visitor acts.
- **Withdrawal (limitation):** withdrawing records a reject-all and clears the consent cookie, but
  **already-executed scripts cannot be unloaded** (the browser already ran them). Prompt a page
  reload after withdrawal to fully clear in-memory state.
- **Versioning:** bumping the app's config version re-prompts returning visitors.
- **Accessibility:** the preferences modal is keyboard operable, focus-trapped while open (Tab cycles
  within the dialog; focus is restored on close), Escape-closable, and uses theme-token contrast.
