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
