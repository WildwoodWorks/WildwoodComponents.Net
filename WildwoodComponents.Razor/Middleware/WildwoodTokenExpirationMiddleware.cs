using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Razor.Authentication;
using WildwoodComponents.Razor.Services;

namespace WildwoodComponents.Razor.Middleware;

/// <summary>
/// Middleware to check for expired WildwoodAPI tokens and refresh/redirect as needed.
/// Ensures the API token is refreshed on every page navigation so the session stays alive.
/// Ported from WildwoodAdmin — adapted as a reusable library component.
/// </summary>
public class WildwoodTokenExpirationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<WildwoodTokenExpirationMiddleware> _logger;

    // Default public paths that skip token checking.
    // Consuming apps can extend this via the options callback.
    private static readonly string[] DefaultPublicPaths =
    {
        "/account/login",
        "/account/logout",
        "/account/accessdenied",
        "/error",
        "/privacy",
        "/_framework",
        "/css",
        "/js",
        "/lib",
        "/images",
        "/favicon",
        "/_content"
    };

    private static readonly string[] StaticExtensions =
    {
        ".css", ".js", ".png", ".jpg", ".jpeg", ".gif", ".svg", ".ico",
        ".woff", ".woff2", ".ttf", ".eot", ".map"
    };

    private readonly string[] _publicPaths;

    public WildwoodTokenExpirationMiddleware(
        RequestDelegate next,
        ILogger<WildwoodTokenExpirationMiddleware> logger,
        WildwoodTokenMiddlewareOptions? options = null)
    {
        _next = next;
        _logger = logger;

        // Merge default paths with any additional paths from options
        if (options?.AdditionalPublicPaths?.Length > 0)
        {
            _publicPaths = DefaultPublicPaths.Concat(options.AdditionalPublicPaths).ToArray();
        }
        else
        {
            _publicPaths = DefaultPublicPaths;
        }
    }

    public async Task InvokeAsync(HttpContext context, IWildwoodSessionManager sessionManager, IWildwoodAuthService authService)
    {
        var path = context.Request.Path.ToString().ToLower();
        var isPublicPath = IsPublicPath(path);
        var isApiPath = path.StartsWith("/api/");
        var isStaticFile = IsStaticFile(path);
        var isAjaxRequest = RequestHelper.IsAjaxRequest(context.Request);

        // Only check for authenticated, non-public page requests
        if (!isPublicPath && !isApiPath && !isStaticFile && context.User.Identity?.IsAuthenticated == true)
        {
            bool isDefinitelyExpired = false;

            try
            {
                var expiryStr = sessionManager.GetTokenExpiry();
                var hasToken = !string.IsNullOrEmpty(sessionManager.GetAccessToken());

                _logger.LogDebug("WildwoodTokenExpirationMiddleware: Path={Path}, User={User}, HasToken={HasToken}",
                    path, context.User.Identity?.Name ?? "unknown", hasToken);

                if (hasToken && !string.IsNullOrEmpty(expiryStr))
                {
                    if (TokenExpiryParser.IsExpired(expiryStr))
                    {
                        _logger.LogInformation("WildwoodAPI token expired for user {User}, attempting refresh",
                            context.User.Identity?.Name ?? "unknown");

                        try
                        {
                            var refreshResult = await authService.RefreshTokenAsync();

                            if (!refreshResult.Succeeded || !sessionManager.IsAuthenticated)
                            {
                                _logger.LogInformation("Token refresh failed for user {User}, redirecting to login",
                                    context.User.Identity?.Name ?? "unknown");
                                isDefinitelyExpired = true;
                            }
                            else
                            {
                                _logger.LogInformation("Token refreshed successfully for user {User}",
                                    context.User.Identity?.Name ?? "unknown");
                            }
                        }
                        catch (Exception refreshEx)
                        {
                            // Token refresh failed due to error — don't redirect, let the request through.
                            // The user's cookie auth is still valid; subsequent API calls will handle
                            // their own auth failures gracefully.
                            _logger.LogWarning(refreshEx, "Token refresh threw exception for user {User}, allowing request to continue",
                                context.User.Identity?.Name ?? "unknown");
                        }
                    }
                    else
                    {
                        // Token is valid — proactively refresh if getting close to expiry
                        await TryProactiveRefreshAsync(sessionManager, authService, context);
                    }
                }
                // If no token in session, user may have authenticated via local-only path — don't interfere
            }
            catch (Exception ex)
            {
                // Error during token validation — do NOT redirect.
                // Only redirect when we are certain the token is expired and refresh has explicitly failed.
                _logger.LogWarning(ex, "Error checking token expiration for user {User}, allowing request to continue",
                    context.User.Identity?.Name ?? "unknown");
            }

            if (isDefinitelyExpired)
            {
                await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

                try { await authService.LogoutAsync(); } catch { /* best effort */ }

                if (isAjaxRequest)
                {
                    context.Response.StatusCode = 401;
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"error\":\"Session expired\",\"redirect\":\"/Account/Login\"}");
                    return;
                }

                var returnUrl = context.Request.Path + context.Request.QueryString;
                var loginUrl = $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}";
                context.Response.Redirect(loginUrl);
                return;
            }
        }

        await _next(context);
    }

    /// <summary>
    /// Proactively refresh the token if it's within 10 minutes of expiring.
    /// This ensures the session stays active during normal browsing.
    /// </summary>
    private async Task TryProactiveRefreshAsync(IWildwoodSessionManager sessionManager, IWildwoodAuthService authService, HttpContext context)
    {
        try
        {
            var expiryStr = sessionManager.GetTokenExpiry();

            if (TokenExpiryParser.TryParseUtc(expiryStr, out var expiryUtc))
            {
                var timeUntilExpiry = expiryUtc - DateTime.UtcNow;

                if (timeUntilExpiry.TotalMinutes <= 10 && timeUntilExpiry.TotalSeconds > 0)
                {
                    _logger.LogDebug("Token expires in {Minutes} minutes for user {User}, proactively refreshing",
                        timeUntilExpiry.TotalMinutes.ToString("F1"),
                        context.User.Identity?.Name ?? "unknown");

                    await authService.RefreshTokenAsync();
                }
            }
        }
        catch (Exception ex)
        {
            // Don't fail the request if proactive refresh fails
            _logger.LogDebug(ex, "Proactive token refresh failed, will retry on next request");
        }
    }

    private bool IsPublicPath(string path)
    {
        foreach (var publicPath in _publicPaths)
        {
            if (path.StartsWith(publicPath, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsStaticFile(string path)
    {
        return StaticExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Options for configuring the WildwoodTokenExpirationMiddleware.
/// </summary>
public class WildwoodTokenMiddlewareOptions
{
    /// <summary>
    /// Additional paths (beyond the defaults) that should skip token checking.
    /// Paths are matched case-insensitively using StartsWith.
    /// Default public paths include: /account/login, /account/logout, /error, /privacy, etc.
    /// </summary>
    public string[]? AdditionalPublicPaths { get; set; }
}
