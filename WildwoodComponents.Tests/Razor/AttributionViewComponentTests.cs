using System.Net.Http.Headers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Components.Attribution;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The view model AttributionViewComponent hands its view. Only the model is exercised: the .cshtml is not rendered
/// (see <see cref="NoViewEngine"/>), so these tests pin the server-side decisions — which app id and API root the
/// attribution engine starts with, and when it is handed the claim proxy route.
/// </summary>
public class AttributionViewComponentTests
{
    /// <summary>
    /// Stands in for the composite view engine, so <c>ViewComponent.View(...)</c> is pure model construction in a
    /// request-free test. It is never asked to render anything.
    /// </summary>
    private sealed class NoViewEngine : ICompositeViewEngine
    {
        public IReadOnlyList<IViewEngine> ViewEngines => Array.Empty<IViewEngine>();

        public ViewEngineResult FindView(ActionContext context, string viewName, bool isMainPage) =>
            ViewEngineResult.NotFound(viewName, Array.Empty<string>());

        public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage) =>
            ViewEngineResult.NotFound(viewPath, Array.Empty<string>());
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogLevel> Levels { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Levels.Add(logLevel);
    }

    /// <summary>A host without server-side session configured: reading the session throws.</summary>
    private sealed class NoSessionManager : IWildwoodSessionManager
    {
        private static InvalidOperationException NoSession() => new("Session has not been configured for this application or request.");

        public string? GetAccessToken() => throw NoSession();
        public string? GetRefreshToken() => throw NoSession();
        public void SetTokens(string accessToken, string refreshToken) => throw NoSession();
        public void SetTokens(string accessToken, string refreshToken, DateTime expiryUtc) => throw NoSession();
        public string? GetTokenExpiry() => throw NoSession();
        public void ClearTokens() => throw NoSession();
        public bool RequiresPasswordReset => throw NoSession();
        public void SetRequiresPasswordReset(bool value) => throw NoSession();
        public bool IsAuthenticated => throw NoSession();
        public void ApplyAuthorizationHeader(HttpClient httpClient) => throw NoSession();
    }

    private static (AttributionViewComponent Component, RecordingLogger<AttributionViewComponent> Logger) Create(
        WildwoodComponentsRazorOptions options,
        IWildwoodSessionManager? session = null)
    {
        var logger = new RecordingLogger<AttributionViewComponent>();
        var component = new AttributionViewComponent(options, session ?? new FakeSessionManager(null), logger)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext
                {
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                }
            },
            ViewEngine = new NoViewEngine()
        };
        return (component, logger);
    }

    private static async Task<AttributionViewModel> ModelOf(Task<IViewComponentResult> invocation)
    {
        var view = Assert.IsType<ViewViewComponentResult>(await invocation);
        return Assert.IsType<AttributionViewModel>(view.ViewData!.Model);
    }

    private static WildwoodComponentsRazorOptions Options() => new() { BaseUrl = "https://api.test/api", AppId = "options-app" };

    [Fact]
    public async Task The_tag_attributes_win_and_the_api_suffix_is_stripped()
    {
        var (component, _) = Create(new WildwoodComponentsRazorOptions { BaseUrl = "https://options.test", AppId = "options-app" });

        var model = await ModelOf(component.InvokeAsync(appId: "app-1", baseUrl: "https://api.test/api/"));

        Assert.Equal("app-1", model.AppId);
        Assert.Equal("https://api.test", model.BaseUrl);
    }

    [Fact]
    public async Task Without_attributes_the_configured_options_apply()
    {
        var (component, logger) = Create(Options());

        var model = await ModelOf(component.InvokeAsync());

        Assert.Equal("options-app", model.AppId);
        Assert.Equal("https://api.test", model.BaseUrl);
        Assert.DoesNotContain(LogLevel.Warning, logger.Levels);
    }

    [Fact]
    public async Task A_missing_app_id_warns_and_still_renders()
    {
        // The engine still loads (window.wildwoodAttribution stays defined for the registration scripts); it just
        // does not start capturing.
        var (component, logger) = Create(new WildwoodComponentsRazorOptions { BaseUrl = "https://api.test" });

        var model = await ModelOf(component.InvokeAsync(appId: "   "));

        Assert.Equal(string.Empty, model.AppId);
        Assert.Contains(LogLevel.Warning, logger.Levels);
    }

    // ── The claim proxy route is handed only to a signed-in session ──

    [Fact]
    public async Task A_signed_in_session_gets_the_default_claim_url()
    {
        var (component, _) = Create(Options(), new FakeSessionManager("jwt-1"));

        var model = await ModelOf(component.InvokeAsync());

        Assert.Equal("/api/wildwood-attribution/claim", model.ClaimUrl);
    }

    [Fact]
    public async Task An_anonymous_visitor_gets_no_claim_url()
    {
        var (component, _) = Create(Options(), new FakeSessionManager(null));

        var model = await ModelOf(component.InvokeAsync());

        Assert.Null(model.ClaimUrl);
    }

    [Fact]
    public async Task The_host_can_turn_the_claim_off()
    {
        var (component, _) = Create(Options(), new FakeSessionManager("jwt-1"));

        var model = await ModelOf(component.InvokeAsync(enableClaim: false));

        Assert.Null(model.ClaimUrl);
    }

    [Fact]
    public async Task The_host_can_point_the_claim_at_its_own_route()
    {
        var (component, _) = Create(Options(), new FakeSessionManager("jwt-1"));

        var model = await ModelOf(component.InvokeAsync(claimUrl: " /my-proxy/claim "));

        Assert.Equal("/my-proxy/claim", model.ClaimUrl);
    }

    [Fact]
    public async Task A_host_without_session_still_renders_capture_without_the_claim()
    {
        var (component, _) = Create(Options(), new NoSessionManager());

        var model = await ModelOf(component.InvokeAsync());

        Assert.Equal("options-app", model.AppId);
        Assert.Null(model.ClaimUrl);
    }
}
