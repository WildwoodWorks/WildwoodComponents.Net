using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WildwoodComponents.Testing.Smoke;

// A real origin on the loopback interface, and nothing else.
//
// page.SetContentAsync would serve the staged pages with less code, but it would also leave the
// accept POST with nowhere to go: the response watcher reads the browser's own report of that
// request, so the request has to be real. An in-process Kestrel on 127.0.0.1 with an ephemeral port
// gives one, costs a few lines, and reaches no network - which is the rule this whole runner is
// written under.

/// <summary>The loopback server the staged pages and the staged acceptance endpoint are served from.</summary>
internal sealed class FixtureServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FixtureServer(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
    }

    /// <summary>Where the server ended up, port included.</summary>
    internal string BaseUrl { get; }

    /// <summary>
    /// How many more acceptance POSTs are answered with HTTP 429 before one succeeds.
    /// </summary>
    /// <remarks>
    /// Acceptance really does sit under the API's per-IP auth limiter, together with login and
    /// register, so a suite enrolling several users a minute from one address really does see this.
    /// A check sets the number it wants and reads it back to confirm the refusals were spent.
    /// </remarks>
    internal int RateLimitedAccepts { get; set; }

    internal static async Task<FixtureServer> StartAsync()
    {
        var builder = WebApplication.CreateSlimBuilder();

        // Loopback with an ephemeral port: two runs at once do not collide, and nothing is reachable
        // from off the machine.
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();

        var app = builder.Build();
        FixtureServer? server = null;

        app.MapGet("/signup", () => Html(FixturePages.Signup()));
        app.MapGet("/manage", () => Html(FixturePages.Manage()));
        app.MapGet("/consent", () => Html(FixturePages.Consent()));

        app.MapPost(FixturePages.AcceptPath, () =>
        {
            if (server!.RateLimitedAccepts > 0)
            {
                server.RateLimitedAccepts--;
                return Results.StatusCode(StatusCodes.Status429TooManyRequests);
            }

            return Results.Json(new { success = true });
        });

        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var url = addresses?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("The fixture server started without an address.");

        server = new FixtureServer(app, url.TrimEnd('/'));
        return server;
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private static IResult Html(string body)
    {
        return Results.Content(body, "text/html; charset=utf-8");
    }
}
