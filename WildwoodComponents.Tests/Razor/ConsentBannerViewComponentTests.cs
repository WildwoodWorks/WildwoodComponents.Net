using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Components.Consent;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The view model <see cref="ConsentBannerViewComponent"/> hands its view. The .cshtml is not
/// rendered here (see <see cref="NoViewEngine"/>), so what these pin is the server-side wiring —
/// including <c>reserve-space</c>, the switch that decides whether the fixed banner is allowed to
/// sit on top of the host's own edge-anchored UI.
/// </summary>
public class ConsentBannerViewComponentTests
{
    /// <summary>Stands in for the composite view engine, which a request-free test has none of.</summary>
    private sealed class NoViewEngine : ICompositeViewEngine
    {
        public IReadOnlyList<IViewEngine> ViewEngines => Array.Empty<IViewEngine>();

        public ViewEngineResult FindView(ActionContext context, string viewName, bool isMainPage) =>
            ViewEngineResult.NotFound(viewName, Array.Empty<string>());

        public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage) =>
            ViewEngineResult.NotFound(viewPath, Array.Empty<string>());
    }

    private static async Task<ConsentViewModel> InvokeAsync(bool? reserveSpace = null)
    {
        var component = new ConsentBannerViewComponent(
            new WildwoodComponentsRazorOptions { BaseUrl = "https://api.test/api", AppId = "app-1" },
            NullLogger<ConsentBannerViewComponent>.Instance)
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

        var result = reserveSpace is null
            ? await component.InvokeAsync("app-1")
            : await component.InvokeAsync("app-1", reserveSpace: reserveSpace.Value);

        var view = Assert.IsType<ViewViewComponentResult>(result);
        return Assert.IsType<ConsentViewModel>(view.ViewData!.Model);
    }

    /// <summary>
    /// Opt-out, not opt-in: a host that says nothing gets a banner that cannot cover its page.
    /// </summary>
    [Fact]
    public async Task The_banner_reserves_its_own_room_unless_the_host_says_otherwise()
    {
        Assert.True((await InvokeAsync()).ReserveSpace);
        Assert.True((await InvokeAsync(reserveSpace: true)).ReserveSpace);
        Assert.False((await InvokeAsync(reserveSpace: false)).ReserveSpace);
    }
}
