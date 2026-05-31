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
