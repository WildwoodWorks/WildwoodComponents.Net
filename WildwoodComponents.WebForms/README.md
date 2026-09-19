# WildwoodComponents.WebForms

Wildwood platform components for classic **ASP.NET WebForms** sites, targeting
**.NET Framework 4.8**.

This package exists for sites that cannot be migrated but can still adopt Wildwood
features. Components are built the way a WebForms site expects: user controls you
register and drop onto a page, an `IHttpModule` in `web.config`, and `.ashx` handlers.

**This release ships Authentication and Two-Factor Settings**, together with the
infrastructure the rest of the library will sit on. The Blazor and Razor packages
currently implement 27 components; the remainder — legal and feedback, tiers and
payments, AI, messaging, notifications and the rest — follow in later phases and are
not present yet. See [Components](#components) for exactly what is here today.

## Why .NET Framework 4.8

WebForms was never carried forward to .NET Core or later, so the only question is
*which* 4.x release to target. 4.8 is the last universal one: it installs back to
Windows 7 SP1 / Server 2008 R2 SP1, ships in-box from Windows 10 / Server 2019 on, and
is an in-place, highly compatible upgrade from 4.5–4.7.2 — a legacy site moves its
runtime without touching application code. Support for 4.6.2–4.7.2 ends 2027-01-12, and
4.8.1 only installs on Windows 11 / Server 2022+, which is too narrow a floor.

## Install

```
Install-Package WildwoodComponents.WebForms
```

The package copies its content into the consuming web project and adds the Wildwood
settings and HTTP module to `web.config`. Fill in the two blank values:

```xml
<add key="WildwoodAPI:ApiKey" value="YOUR-APP-API-KEY" />
<add key="WildwoodAPI:AppId"  value="YOUR-APP-ID" />
```

Both come from the app's Details page in [WildwoodAdmin](https://admin.wildwoodworks.io).
`Package/readme.txt` is shown at install time and covers the rest.

## Components

| Component | Control | Proxy handler |
|---|---|---|
| Authentication | `AuthenticationControl.ascx` | `WildwoodAuthProxy.ashx` |
| Two-Factor Settings | `TwoFactorSettingsControl.ascx` | `WildwoodTwoFactorProxy.ashx` |

```aspx
<%@ Register TagPrefix="ww" TagName="Authentication"
             Src="~/Controls/Wildwood/AuthenticationControl.ascx" %>

<ww:Authentication runat="server" ReturnUrl="~/Default.aspx" Title="Welcome back" />
```

## Campaign Attribution

A registration carries the campaign the visitor arrived on — UTM tags, an ad-platform click
id, the external referrer — so a signup can be credited to the campaign that produced it.
There are two ways to capture that, and they meet at the same registration field.

**In the browser** (the same engine the other stacks run). Load `attribution.js` once, as
early as the master page allows, so it reads the landing URL before anything navigates:

```aspx
<script src='<%= ResolveUrl("~/Scripts/wildwood/attribution.js") %>'
        data-app-id="YOUR-APP-ID"
        data-base-url="https://api.wildwoodworks.io"></script>
```

`authentication.js` then posts `window.wildwoodAttribution.getForRegistration()` with the
signup and clears the touches once it succeeds. Nothing else is needed.

**On the server**, for a site that renders its own sign-up page, or one that would rather not
add a script. Capture every request in `Global.asax` — campaigns land on a marketing page, not
on the sign-up page, so capturing only where the form lives misses them:

```csharp
void Application_AcquireRequestState(object sender, EventArgs e)
{
    WildwoodAttribution.Capture(new HttpContextWrapper(HttpContext.Current));
}
```

`WildwoodWebForms.Auth.RegisterAsync` then attaches what was captured to any registration that
does not already carry a payload, and drops the touches once the signup is recorded. A failed
registration keeps them. The capture lives in ASP.NET session state under the SDK's
`ww_attribution` key, and the normalization rules are the shared
`WildwoodComponents.Shared.Utilities.AttributionRules` — the C# port of the same rules the
browser engines and `@wildwood/core` apply, because WildwoodAPI re-applies them and drops
whatever does not fit.

**Consent.** This package ships no consent component, so the host reports the decision:

```csharp
WildwoodAttribution.ConsentDecision = () => MyConsentCookie.AnalyticsGranted
    ? WildwoodAttributionConsent.Granted
    : WildwoodAttributionConsent.Denied;
```

Unset, the decision is `Undecided`, which is the JS SDK's default: the touches are held for the
visit and nothing new is written to the visitor's device — session state rides the site's
existing session cookie. `Denied` drops anything held and captures nothing further, matching the
SDK rule that stored touches are removed once consent no longer grants them. The browser engine
applies its own gate from the app's consent configuration and needs no wiring here.

## How it is put together

**The browser never talks to the WildwoodAPI.** A control renders a shell carrying
`data-*` attributes; the shipped JavaScript reads those and calls a proxy handler in the
host site, which forwards the call server-side. The bearer token stays on the server.
This is the same design the Razor package uses — with one difference: Razor documents
the proxies as copy-paste controllers for the host to write, whereas here they ship
compiled and the `.ashx` files simply point at them, so a site gets working endpoints on
install.

**The `.ascx` files need no compilation.** Each one carries `Inherits=` naming a compiled
class in this assembly and deliberately no `CodeFile`, so the ASP.NET runtime compiles
the markup against that base class. Consumers build nothing.

**Scripts and stylesheets are the Razor package's, byte for byte.** They are packed
directly out of `WildwoodComponents.Razor/wwwroot`, not copied into this project, so
there is one source of truth per file and the two stacks cannot drift.

**Forms are not forms.** An `.aspx` page already sits inside one
`<form runat="server">`, and HTML forbids nesting another — a browser discards the inner
tag, and a `type="submit"` button would post the host page back. The components render
`<div data-ww-form>` with `type="button"` controls instead, and `wildwood-forms.js`
supplies the submit, validation and reset semantics a form would have. That shim is
optional: when it is not loaded, the shared scripts fall through to their native paths,
which is what the other stacks do.

**Configuration has no container.** Classic WebForms has no dependency injection, so a
static `WildwoodWebForms` type holds the options, owns the single `HttpClient`, and hands
out services. It configures itself from `web.config` on first use; calling
`WildwoodWebForms.Configure()` in `Application_Start` is optional and makes a bad
configuration fail at startup instead of at first render.

**Tokens are per request.** The bearer token goes on each `HttpRequestMessage`, never on
the shared client's `DefaultRequestHeaders` — one client is shared process-wide, so a
token left there would travel with other users' concurrent requests.

**Sessions survive a recycle.** Tokens live in ASP.NET session state under the same key
names the Razor package uses, with a copy in the Forms Authentication ticket's
`UserData`. When session state is lost but the auth cookie is still valid, the HTTP
module rehydrates from the ticket. Because classic Forms Authentication does not chunk an
oversized cookie, that payload is size-budgeted and degrades — dropping the refresh token
first — rather than emitting a cookie the browser will silently refuse.

## Requirements in the host page

- Bootstrap 5 CSS (class names only, so it coexists with older Bootstrap JavaScript).
  The Two-Factor control also uses Bootstrap's JS for its confirmation modal.
- Bootstrap Icons.
- Session state enabled. Tokens are stored as strings, so StateServer and SQLServer
  session modes work as well as InProc.
- `Async="true"` on the `@Page` directive is recommended but not required: without it the
  controls use a thread-pool fallback that cannot deadlock.

## Limitations

- **UpdatePanel is not supported.** A partial postback re-renders the markup without
  re-running the scripts bound to it. The controls log a warning when they detect one.
- **One Authentication control per page** — its element ids are fixed, the same
  constraint the Razor component has. The Two-Factor control may be used repeatedly; its
  ids carry a per-instance suffix.

## Tests

```bash
dotnet test WildwoodComponents.WebForms.Tests   # Windows only: net48
```

CI runs this on a `windows-latest` job. The project compiles on Linux through
`Microsoft.NETFramework.ReferenceAssemblies`, but net48 tests can only run on Windows.
