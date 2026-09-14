using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Controllers;

/// <summary>
/// Same-origin proxy for the Campaign Attribution claim. attribution.js holds the captured touches in the browser,
/// while a Razor app keeps the user's JWT in the server session, so the engine posts its payload here once the session
/// is signed in (AttributionViewComponent renders <c>data-claim-url</c> only then) and
/// <see cref="IWildwoodAttributionService"/> forwards it to WildwoodAPI with the session token and the configured app
/// id. This is what attributes a sign-in provider signup, which has no registration request to carry the payload.
///
/// Host wiring: the consuming app must register MVC controllers (<c>builder.Services.AddControllers()</c> +
/// <c>app.MapControllers()</c>) and have server-side session available, as for the notifications proxy.
/// </summary>
[ApiController]
[Route("api/wildwood-attribution")]
[Produces("application/json")]
public class WildwoodAttributionProxyController : ControllerBase
{
    private readonly IWildwoodAttributionService _service;
    private readonly IWildwoodSessionManager _sessionManager;

    public WildwoodAttributionProxyController(IWildwoodAttributionService service, IWildwoodSessionManager sessionManager)
    {
        _service = service;
        _sessionManager = sessionManager;
    }

    /// <summary>POST /api/wildwood-attribution/claim — claims the posted payload for the signed-in user.</summary>
    [HttpPost("claim")]
    public async Task<IActionResult> Claim([FromBody] AttributionPayloadModel? payload)
    {
        if (!_sessionManager.IsAuthenticated)
            return Unauthorized();
        if (payload is null || (payload.FirstTouch is null && payload.LastTouch is null))
            return BadRequest();

        var result = await _service.ClaimAsync(payload, HttpContext.RequestAborted);
        if (_service.LastResponseUnauthorized)
        {
            // The session token is dead; signal it the way the notifications proxy does.
            Response.Headers["X-Session-Expired"] = "true";
            return Unauthorized();
        }

        // null = transient: 502 tells the engine to keep its touches.
        return result is null ? StatusCode(StatusCodes.Status502BadGateway) : Ok(result);
    }
}
