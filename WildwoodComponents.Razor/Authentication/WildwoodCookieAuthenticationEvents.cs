using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Razor.Authentication;

/// <summary>
/// Custom cookie authentication events that handle AJAX requests gracefully
/// and restore session tokens from cookie backup when session data is lost.
/// Ported from WildwoodAdmin — adapted as a reusable library component.
/// </summary>
public class WildwoodCookieAuthenticationEvents : CookieAuthenticationEvents
{
    private readonly ILogger<WildwoodCookieAuthenticationEvents> _logger;

    public WildwoodCookieAuthenticationEvents(
        ILogger<WildwoodCookieAuthenticationEvents> logger)
    {
        _logger = logger;
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (RequestHelper.IsAjaxRequest(context.Request))
        {
            _logger.LogInformation("AJAX request to {Url} requires auth, returning 401",
                context.Request.GetDisplayUrl());
            context.Response.StatusCode = 401;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("{\"error\":\"Authentication required\",\"redirect\":\"/Account/Login\"}");
        }

        return base.RedirectToLogin(context);
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        try
        {
            if (context.Principal?.Identity?.IsAuthenticated == true)
            {
                // Ensure session is loaded before accessing session data
                try
                {
                    await context.HttpContext.Session.LoadAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load session in ValidatePrincipal, skipping token check");
                    await base.ValidatePrincipal(context);
                    return;
                }

                var session = context.HttpContext.Session;
                var accessToken = session.GetString(SessionConstants.AccessToken);
                var expiryStr = session.GetString(SessionConstants.TokenExpiry);

                // If session tokens are missing, restore them from auth cookie properties.
                // Session data stored in DistributedMemoryCache can be lost after app restart
                // or cache eviction. The auth cookie carries backup tokens.
                if (string.IsNullOrEmpty(accessToken) || string.IsNullOrEmpty(expiryStr))
                {
                    if (AuthCookieTokenHelper.TryRestoreSessionFromCookie(
                        context.Properties, session, _logger))
                    {
                        accessToken = session.GetString(SessionConstants.AccessToken);
                        expiryStr = session.GetString(SessionConstants.TokenExpiry);
                    }
                }

                _logger.LogDebug("ValidatePrincipal: User={User}, HasAccessToken={HasToken}, HasExpiry={HasExpiry}",
                    context.Principal?.Identity?.Name ?? "unknown",
                    !string.IsNullOrEmpty(accessToken),
                    !string.IsNullOrEmpty(expiryStr));

                // Do NOT reject the cookie principal when the API token is expired.
                // The cookie session outlives the API token.
                // TokenExpirationMiddleware will refresh the API token on the next
                // page navigation — but only if the cookie principal is still valid.
                if (!string.IsNullOrEmpty(accessToken) && !string.IsNullOrEmpty(expiryStr))
                {
                    if (TokenExpiryParser.IsExpired(expiryStr))
                    {
                        _logger.LogInformation(
                            "WildwoodAPI token expired for user {User} (expiry={Expiry}), keeping cookie principal so middleware can refresh",
                            context.Principal?.Identity?.Name ?? "unknown",
                            expiryStr);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating principal for user {User}",
                context.Principal?.Identity?.Name ?? "unknown");
        }

        await base.ValidatePrincipal(context);
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        if (RequestHelper.IsAjaxRequest(context.Request))
        {
            _logger.LogInformation("AJAX request to {Url} access denied, returning 403",
                context.Request.GetDisplayUrl());
            context.Response.StatusCode = 403;
            context.Response.ContentType = "application/json";
            return context.Response.WriteAsync("{\"error\":\"Access denied\"}");
        }

        return base.RedirectToAccessDenied(context);
    }
}
