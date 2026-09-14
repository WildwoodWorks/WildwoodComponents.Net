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

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The view model AttributionViewComponent hands its view. Only the model is exercised: the .cshtml is not rendered
/// (see <see cref="NoViewEngine"/>), so these tests pin the server-side decisions — which app id and API root the
/// attribution engine starts with.
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

    private static (AttributionViewComponent Component, RecordingLogger<AttributionViewComponent> Logger) Create(
        WildwoodComponentsRazorOptions options)
    {
        var logger = new RecordingLogger<AttributionViewComponent>();
        var component = new AttributionViewComponent(options, logger)
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
        var (component, logger) = Create(new WildwoodComponentsRazorOptions { BaseUrl = "https://api.test/api", AppId = "options-app" });

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
}
