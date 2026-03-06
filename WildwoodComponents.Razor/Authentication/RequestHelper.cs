using Microsoft.AspNetCore.Http;

namespace WildwoodComponents.Razor.Authentication;

/// <summary>
/// Shared HTTP request utilities used by authentication middleware and event handlers.
/// Ported from WildwoodAdmin.
/// </summary>
public static class RequestHelper
{
    /// <summary>
    /// Determines if the request is an AJAX/API request that should receive
    /// JSON error responses instead of HTML redirects.
    /// </summary>
    public static bool IsAjaxRequest(HttpRequest request)
    {
        // Check for X-Requested-With header (jQuery-style AJAX)
        if (request.Headers.ContainsKey("X-Requested-With") &&
            request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            return true;
        }

        // Check for Accept header requesting JSON (fetch API calls)
        if (request.Headers.ContainsKey("Accept"))
        {
            var accept = request.Headers["Accept"].ToString();
            if (accept.Contains("application/json", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        // Check for query string handlers (Razor Page AJAX handlers)
        if (request.Query.ContainsKey("handler"))
        {
            return true;
        }

        return false;
    }
}
