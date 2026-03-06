using Microsoft.AspNetCore.Builder;
using WildwoodComponents.Razor.Middleware;

namespace WildwoodComponents.Razor.Extensions;

/// <summary>
/// Extension methods for IApplicationBuilder to add WildwoodComponents.Razor middleware.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Adds the WildwoodAPI token expiration middleware to the pipeline.
    /// This middleware checks/refreshes JWT tokens on each page navigation.
    /// Must be placed AFTER UseAuthentication() and UseAuthorization().
    ///
    /// Usage in Program.cs:
    /// <code>
    /// app.UseAuthentication();
    /// app.UseAuthorization();
    /// app.UseWildwoodTokenManagement();
    /// </code>
    /// </summary>
    public static IApplicationBuilder UseWildwoodTokenManagement(
        this IApplicationBuilder app,
        Action<WildwoodTokenMiddlewareOptions>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            var options = new WildwoodTokenMiddlewareOptions();
            configureOptions(options);
            // Register the options as a singleton so the middleware constructor can receive it
            app.ApplicationServices.GetType(); // no-op, just ensure services exist
            return app.UseMiddleware<WildwoodTokenExpirationMiddleware>(options);
        }

        return app.UseMiddleware<WildwoodTokenExpirationMiddleware>();
    }
}
