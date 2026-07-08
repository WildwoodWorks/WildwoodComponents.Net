using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Controllers;

/// <summary>
/// Same-origin proxy that fronts the WildwoodAPI notification inbox for the browser. The
/// notification-inbox client JS (bell / list / preferences) calls THIS controller
/// (<c>/api/wildwood-notifications/*</c>) rather than the API directly, so the user's Bearer
/// token stays server-side: <see cref="IWildwoodNotificationInboxService"/> reads it from the
/// server session and applies it when forwarding. This makes real the <c>data-proxy-url</c> hook
/// the notification ViewComponents stamp into the page.
///
/// Host wiring: the consuming app must register MVC controllers
/// (<c>builder.Services.AddControllers()</c> + <c>app.MapControllers()</c>) and have server-side
/// session available for <see cref="WildwoodSessionManager"/> to resolve the token.
///
/// Transient failures from the service (its <c>null</c> / non-success signals) surface as HTTP 502
/// so the client retains its last-good data instead of clobbering it, mirroring the JS core service.
/// </summary>
[ApiController]
[Route("api/wildwood-notifications")]
[Produces("application/json")]
public class WildwoodNotificationsProxyController : ControllerBase
{
    private readonly IWildwoodNotificationInboxService _service;
    private readonly ILogger<WildwoodNotificationsProxyController> _logger;

    public WildwoodNotificationsProxyController(
        IWildwoodNotificationInboxService service,
        ILogger<WildwoodNotificationsProxyController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>GET /api/wildwood-notifications — the authenticated user's inbox list.</summary>
    [HttpGet]
    public async Task<IActionResult> List()
    {
        var list = await _service.GetNotificationsAsync();
        // null = transient: 502 tells the client to retain its last-good list.
        return list is null ? StatusCode(StatusCodes.Status502BadGateway) : Ok(list);
    }

    /// <summary>GET /api/wildwood-notifications/count — the unread count.</summary>
    [HttpGet("count")]
    public async Task<IActionResult> Count()
    {
        var count = await _service.GetUnreadCountAsync();
        return count is null ? StatusCode(StatusCodes.Status502BadGateway) : Ok(count.Value);
    }

    /// <summary>PUT /api/wildwood-notifications/{id}/read — mark one read.</summary>
    [HttpPut("{id}/read")]
    public async Task<IActionResult> MarkRead(string id)
    {
        var ok = await _service.MarkReadAsync(id);
        return ok ? NoContent() : StatusCode(StatusCodes.Status502BadGateway);
    }

    /// <summary>PUT /api/wildwood-notifications/read-all — mark every unread read.</summary>
    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllRead()
    {
        var marked = await _service.MarkAllReadAsync();
        return Ok(new MarkAllReadResponse { MarkedAsRead = marked });
    }

    /// <summary>DELETE /api/wildwood-notifications/{id} — dismiss one.</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Remove(string id)
    {
        var ok = await _service.RemoveAsync(id);
        return ok ? NoContent() : StatusCode(StatusCodes.Status502BadGateway);
    }

    /// <summary>GET /api/wildwood-notifications/preferences?appId= — delivery preferences.</summary>
    [HttpGet("preferences")]
    public async Task<IActionResult> GetPreferences([FromQuery] string appId)
    {
        var pref = await _service.GetPreferencesAsync(appId);
        return pref is null ? StatusCode(StatusCodes.Status502BadGateway) : Ok(pref);
    }

    /// <summary>PUT /api/wildwood-notifications/preferences — persist delivery preferences.</summary>
    [HttpPut("preferences")]
    public async Task<IActionResult> UpdatePreferences([FromBody] UserNotificationPreference pref)
    {
        if (pref is null)
            return BadRequest();

        var saved = await _service.UpdatePreferencesAsync(pref);
        return saved is null ? StatusCode(StatusCodes.Status502BadGateway) : Ok(saved);
    }
}
