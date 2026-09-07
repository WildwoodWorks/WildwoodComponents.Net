using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Components.Authentication;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The view model AuthenticationViewComponent hands its view. Only the model is exercised: the
/// .cshtml is not rendered (see <see cref="NoViewEngine"/>), so these tests pin the server-side
/// decisions — the host-owned sign-up URL, and the deliberately AND-only registration gate.
/// </summary>
public class AuthenticationViewComponentTests
{
    /// <summary>
    /// Stands in for the composite view engine. <c>ViewComponent.View(...)</c> otherwise resolves one
    /// from <c>HttpContext.RequestServices</c>, which no request-free test has; assigning this makes
    /// the call pure model construction. It is never asked to render anything.
    /// </summary>
    private sealed class NoViewEngine : ICompositeViewEngine
    {
        public IReadOnlyList<IViewEngine> ViewEngines => Array.Empty<IViewEngine>();

        public ViewEngineResult FindView(ActionContext context, string viewName, bool isMainPage) =>
            ViewEngineResult.NotFound(viewName, Array.Empty<string>());

        public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage) =>
            ViewEngineResult.NotFound(viewPath, Array.Empty<string>());
    }

    private static AuthenticationViewComponent CreateComponent(AuthConfigResponse config)
    {
        return new AuthenticationViewComponent(
            new FakeRazorAuthService(config),
            NullLogger<AuthenticationViewComponent>.Instance)
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
    }

    private static async Task<AuthenticationViewModel> InvokeAsync(
        AuthConfigResponse config,
        bool allowRegistration = true,
        string? registerUrl = null)
    {
        var result = await CreateComponent(config).InvokeAsync(
            allowRegistration: allowRegistration,
            registerUrl: registerUrl);

        var view = Assert.IsType<ViewViewComponentResult>(result);
        return Assert.IsType<AuthenticationViewModel>(view.ViewData!.Model);
    }

    [Fact]
    public async Task A_register_url_reaches_the_view()
    {
        var model = await InvokeAsync(new AuthConfigResponse { AllowRegistration = true }, registerUrl: "/signup");

        Assert.Equal("/signup", model.RegisterUrl);
    }

    [Fact]
    public async Task No_register_url_leaves_the_component_owning_sign_up()
    {
        var model = await InvokeAsync(new AuthConfigResponse { AllowRegistration = true });

        Assert.Null(model.RegisterUrl);
    }

    [Fact]
    public async Task A_whitespace_register_url_is_treated_as_unset()
    {
        // "   " would render as a data-register-url the script reads as truthy, sending the browser
        // to a blank destination on the very first click.
        var model = await InvokeAsync(new AuthConfigResponse { AllowRegistration = true }, registerUrl: "   ");

        Assert.Null(model.RegisterUrl);
    }

    [Fact]
    public async Task The_host_can_hide_registration_the_configuration_allows()
    {
        var model = await InvokeAsync(new AuthConfigResponse { AllowRegistration = true }, allowRegistration: false);

        Assert.False(model.AllowRegistration);
    }

    [Fact]
    public async Task The_host_cannot_force_registration_the_configuration_denies()
    {
        // Deliberate asymmetry, unlike the Blazor component's two-directional override: a
        // server-rendered view has no way to make an app accept registrations it has switched off,
        // so the gate stays AND-only here.
        var model = await InvokeAsync(new AuthConfigResponse { AllowRegistration = false }, allowRegistration: true);

        Assert.False(model.AllowRegistration);
    }
}
