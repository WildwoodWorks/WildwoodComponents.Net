using System.Reflection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Components.RegistrationSubscription;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The Razor manage surface. It is a COMPOSITION of the six admin panels, so what these tests pin
/// is the composition: which sections a viewer gets and in what order, which scope the panels and
/// the plan change act in, what reaches the browser as data attributes, and the one thing the
/// picker must never get wrong — offering a pack the account already holds.
/// </summary>
public class RegistrationSubscriptionManageViewComponentTests
{
    /// <summary>Stands in for the composite view engine, which a request-free test has none of.</summary>
    private sealed class NoViewEngine : ICompositeViewEngine
    {
        public IReadOnlyList<IViewEngine> ViewEngines => Array.Empty<IViewEngine>();

        public ViewEngineResult FindView(Microsoft.AspNetCore.Mvc.ActionContext context, string viewName, bool isMainPage) =>
            ViewEngineResult.NotFound(viewName, Array.Empty<string>());

        public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage) =>
            ViewEngineResult.NotFound(viewPath, Array.Empty<string>());
    }

    private sealed class StubPaymentService : IWildwoodPaymentService
    {
        public PlatformFilteredProvidersDto? Providers { get; set; }

        public int ProviderReads { get; private set; }

        public Task<PlatformFilteredProvidersDto?> GetAvailableProvidersAsync(string appId)
        {
            ProviderReads++;
            return Task.FromResult(Providers);
        }

        public Task<AppPaymentConfigurationDto?> GetAppPaymentConfigurationAsync(string appId) => throw new NotSupportedException();
        public Task<InitiatePaymentResponse> InitiatePaymentAsync(InitiatePaymentRequest request) => throw new NotSupportedException();
        public Task<PaymentCompletionResult> ConfirmPaymentAsync(string paymentIntentId, PaymentProviderType providerType) => throw new NotSupportedException();
        public Task<List<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(string customerId) => throw new NotSupportedException();
        public Task<bool> DeleteSavedPaymentMethodAsync(string paymentMethodId) => throw new NotSupportedException();
        public Task<bool> SetDefaultPaymentMethodAsync(string paymentMethodId) => throw new NotSupportedException();
        public Task<PaymentCompletionResult?> GetPaymentStatusAsync(string transactionId) => throw new NotSupportedException();
        public Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null) => throw new NotSupportedException();
    }

    private sealed class StubCatalogService : IWildwoodPublicCatalogService
    {
        public PublicCatalog? Catalog { get; set; }

        public int Reads { get; private set; }

        public Task<PublicCatalog> GetAsync(string appId, string? currencyOverride = null, bool forceRefresh = false)
        {
            Reads++;
            if (Catalog is null) return Task.FromException<PublicCatalog>(new InvalidOperationException("no catalog"));
            return Task.FromResult(Catalog);
        }

        public void Invalidate(string appId) { }
    }

    /// <summary>The shell asks the environment whether to say a misconfiguration out loud.</summary>
    private sealed class StubEnvironment : IWebHostEnvironment
    {
        public StubEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    // ── Fixtures ────────────────────────────────────────────────────────────────

    private static AppTierAddOnModel Pack(string id, string name, string? tierId = null)
    {
        var pack = new AppTierAddOnModel
        {
            Id = id,
            Name = name,
            Status = CatalogHelpers.ActiveTierStatus,
            Currency = "USD"
        };

        pack.PricingOptions.Add(new AppTierAddOnPricingModel
        {
            Id = id + "-monthly",
            Price = 9m,
            BillingFrequency = "Monthly",
            IsDefault = true
        });

        if (tierId is { Length: > 0 }) pack.BundledInTierIds.Add(tierId);

        return pack;
    }

    private static UserAddOnSubscriptionModel Owned(string addOnId, string name, string status = "Active")
    {
        return new UserAddOnSubscriptionModel
        {
            Id = addOnId + "-sub",
            AppTierAddOnId = addOnId,
            AddOnName = name,
            Status = status,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            PaymentTransactionId = "tx-" + addOnId
        };
    }

    private static string Json(object value) => System.Text.Json.JsonSerializer.Serialize(value);

    private static PlatformFilteredProvidersDto Providers(string key = "pk_test_123")
    {
        var providers = new PlatformFilteredProvidersDto();
        providers.AvailableProviders.Add(new PaymentProviderDto
        {
            Id = "provider-1",
            IsEnabled = true,
            IsDefault = true,
            PublishableKey = key
        });
        return providers;
    }

    // ── Invocation ──────────────────────────────────────────────────────────────

    private sealed class Harness
    {
        public required RegistrationSubscriptionManageViewModel Model { get; init; }
        public required FakeHttpMessageHandler Handler { get; init; }
        public required StubPaymentService Payments { get; init; }
        public required StubCatalogService Catalog { get; init; }
    }

    private static async Task<Harness> InvokeAsync(
        string? appId = null,
        string? configuredAppId = "app-1",
        string layout = "tabs",
        string? sections = null,
        bool showStatusAboveTabs = false,
        bool isAdmin = false,
        string? userId = null,
        string? companyId = null,
        bool allowPackSelfService = false,
        bool allowCancel = true,
        bool showAddOns = true,
        string? currency = null,
        RegistrationSubscriptionLabels? labels = null,
        string? contactUrl = null,
        string? returnUrl = null,
        string? paymentRequiredText = null,
        string trackingMode = "User",
        IEnumerable<AppTierAddOnModel>? available = null,
        IEnumerable<UserAddOnSubscriptionModel>? owned = null,
        string? currentTierId = "tier-pro",
        PlatformFilteredProvidersDto? providers = null,
        PublicCatalog? catalog = null)
    {
        // Routes are matched by substring in registration order, and "app-tier-addons" contains
        // "addons" - so the two pack routes are registered by their own last segment, and before
        // anything shorter could swallow them.
        var handler = new FakeHttpMessageHandler();
        handler.WhenOk("settings/tracking-mode", Json(new { mode = trackingMode }));
        handler.WhenOk("my-addons", Json(owned ?? Array.Empty<UserAddOnSubscriptionModel>()));
        handler.WhenOk("admin/feature-overrides", Json(new[] { new { featureCode = "X", isEnabled = true } }));
        handler.WhenOk("my-subscription", Json(new
        {
            id = "sub-1",
            appTierId = currentTierId,
            appTierName = "Pro",
            status = "Active"
        }));
        handler.WhenOk("subscription/user", Json(new { id = "sub-1", appTierId = currentTierId, status = "Active" }));
        handler.WhenOk("company", Json(new { id = "sub-1", appTierId = currentTierId, status = "Active" }));
        handler.WhenOk("available", Json(available ?? Array.Empty<AppTierAddOnModel>()));
        handler.WhenOk("app-tier-addons", Json(available ?? Array.Empty<AppTierAddOnModel>()));

        var appTiers = new WildwoodAppTierService(
            handler.CreateClient("https://api.test/api/"),
            new FakeSessionManager("test-jwt"),
            NullLogger<WildwoodAppTierService>.Instance);

        var payments = new StubPaymentService { Providers = providers ?? Providers() };
        var catalogService = new StubCatalogService { Catalog = catalog };

        var component = new RegistrationSubscriptionManageViewComponent(
            appTiers,
            payments,
            catalogService,
            new WildwoodComponentsRazorOptions { BaseUrl = "https://api.test", AppId = configuredAppId },
            NullLogger<RegistrationSubscriptionManageViewComponent>.Instance)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext
                {
                    HttpContext = new DefaultHttpContext(),
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                }
            },
            ViewEngine = new NoViewEngine()
        };

        var result = await component.InvokeAsync(
            appId: appId,
            layout: layout,
            sections: sections,
            showStatusAboveTabs: showStatusAboveTabs,
            isAdmin: isAdmin,
            userId: userId,
            companyId: companyId,
            allowPackSelfService: allowPackSelfService,
            allowCancel: allowCancel,
            showAddOns: showAddOns,
            currency: currency,
            labels: labels,
            contactUrl: contactUrl,
            returnUrl: returnUrl,
            paymentRequiredText: paymentRequiredText);

        var view = Assert.IsType<ViewViewComponentResult>(result);

        return new Harness
        {
            Model = Assert.IsType<RegistrationSubscriptionManageViewModel>(view.ViewData!.Model),
            Handler = handler,
            Payments = payments,
            Catalog = catalogService
        };
    }

    // ── No app ──────────────────────────────────────────────────────────────────

    /// <summary>Nothing to manage is said once, not rendered as six empty panels.</summary>
    [Fact]
    public async Task With_no_app_it_says_so_and_reads_nothing()
    {
        var harness = await InvokeAsync(configuredAppId: null);

        Assert.False(harness.Model.HasApp);
        Assert.Equal("An appId is required.", harness.Model.AppIdRequiredMessage);
        Assert.Empty(harness.Handler.Requests);
        Assert.Equal(0, harness.Payments.ProviderReads);
    }

    // ── Sections and layout ─────────────────────────────────────────────────────

    [Fact]
    public async Task By_default_every_section_but_overrides_renders_in_the_shipped_order()
    {
        var harness = await InvokeAsync();

        Assert.Equal(
            new[]
            {
                ManageSection.Subscription,
                ManageSection.Plans,
                ManageSection.Features,
                ManageSection.AddOns,
                ManageSection.Usage
            },
            harness.Model.VisibleSections);
    }

    [Fact]
    public async Task Overrides_is_an_admin_panel()
    {
        Assert.DoesNotContain(ManageSection.Overrides, (await InvokeAsync(isAdmin: false)).Model.VisibleSections);
        Assert.Contains(ManageSection.Overrides, (await InvokeAsync(isAdmin: true)).Model.VisibleSections);
    }

    [Fact]
    public async Task Packs_go_with_show_add_ons()
    {
        Assert.DoesNotContain(ManageSection.AddOns, (await InvokeAsync(showAddOns: false)).Model.VisibleSections);
    }

    [Fact]
    public async Task The_sections_attribute_names_them_in_the_hosts_order()
    {
        var harness = await InvokeAsync(sections: "usage, plans ,addOns", isAdmin: true);

        Assert.Equal(
            new[] { ManageSection.Usage, ManageSection.Plans, ManageSection.AddOns },
            harness.Model.VisibleSections);
    }

    /// <summary>A typo costs the section, not the page.</summary>
    [Fact]
    public async Task An_unknown_section_name_is_dropped()
    {
        var harness = await InvokeAsync(sections: "plans,nonsense,usage");

        Assert.Equal(new[] { ManageSection.Plans, ManageSection.Usage }, harness.Model.VisibleSections);
    }

    [Fact]
    public async Task The_status_card_is_lifted_out_of_the_body_when_asked()
    {
        var harness = await InvokeAsync(showStatusAboveTabs: true);

        Assert.True(harness.Model.StatusAbove);
        Assert.Contains(ManageSection.Subscription, harness.Model.VisibleSections);
        Assert.DoesNotContain(ManageSection.Subscription, harness.Model.BodySections);
    }

    /// <summary>Lifting a card that was never on offer would leave an empty box above the tabs.</summary>
    [Fact]
    public async Task The_status_card_is_not_lifted_when_the_host_left_that_section_out()
    {
        var harness = await InvokeAsync(sections: "plans,usage", showStatusAboveTabs: true);

        Assert.False(harness.Model.StatusAbove);
    }

    [Theory]
    [InlineData(null, "tabs")]
    [InlineData("tabs", "tabs")]
    [InlineData("nonsense", "tabs")]
    [InlineData(" Stacked ", "stacked")]
    public async Task The_layout_parses_leniently_and_defaults_to_tabs(string? layout, string expected)
    {
        var harness = await InvokeAsync(layout: layout ?? "tabs");

        Assert.Equal(expected, harness.Model.LayoutCode);
    }

    // ── Scope ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// The scope the panels are rendered for and the scope the plan change posts in are the same
    /// answer: only the signed-in user's own change may send SupportsPaymentAction and finish a
    /// parked 3-D Secure change.
    /// </summary>
    [Fact]
    public async Task The_signed_in_users_own_subscription_is_the_self_scope()
    {
        var harness = await InvokeAsync();

        Assert.True(harness.Model.IsSelfScope);
        Assert.False(harness.Model.UseUserScope);
        Assert.False(harness.Model.UseCompanyScope);
    }

    [Fact]
    public async Task An_admin_managing_one_user_is_not_the_self_scope()
    {
        var harness = await InvokeAsync(userId: "user-9", isAdmin: true);

        Assert.True(harness.Model.UseUserScope);
        Assert.False(harness.Model.IsSelfScope);
    }

    [Fact]
    public async Task A_company_scope_needs_company_tracking_as_well_as_an_id()
    {
        Assert.False((await InvokeAsync(companyId: "co-7")).Model.UseCompanyScope);
        Assert.True((await InvokeAsync(companyId: "co-7", trackingMode: "Company")).Model.UseCompanyScope);
    }

    /// <summary>
    /// An admin acting for someone else can neither buy a pack nor answer a bank challenge —
    /// the shipped proxy acts as the signed-in user — so neither the picker nor the key is offered.
    /// </summary>
    [Fact]
    public async Task An_admin_scope_gets_no_pack_picker_and_no_publishable_key()
    {
        var harness = await InvokeAsync(
            userId: "user-9",
            isAdmin: true,
            allowPackSelfService: true,
            available: new[] { Pack("radar", "Radar") });

        Assert.False(harness.Model.ShowPackPicker);
        Assert.Null(harness.Model.PublishableKey);
        Assert.Equal(0, harness.Payments.ProviderReads);
    }

    // ── The publishable key ─────────────────────────────────────────────────────

    [Fact]
    public async Task The_self_scope_carries_the_apps_publishable_key_for_the_bank_challenge()
    {
        var harness = await InvokeAsync();

        Assert.Equal("pk_test_123", harness.Model.PublishableKey);
        Assert.Equal("provider-1", harness.Model.PaymentProviderId);
    }

    /// <summary>A provider with no key is no key: the flow says so rather than half-working.</summary>
    [Fact]
    public async Task A_provider_with_no_key_leaves_the_key_unset()
    {
        var providers = new PlatformFilteredProvidersDto();
        providers.AvailableProviders.Add(new PaymentProviderDto { Id = "p", IsEnabled = true });

        var harness = await InvokeAsync(providers: providers);

        Assert.Null(harness.Model.PublishableKey);
    }

    // ── The pack picker ─────────────────────────────────────────────────────────

    /// <summary>
    /// The one rule the picker must not get wrong: a pack the account already holds, one the plan
    /// bundles and one a registration token granted are all NOT on offer. A cancelled one is.
    /// </summary>
    [Fact]
    public async Task The_picker_excludes_owned_bundled_and_granted_packs()
    {
        var granted = Owned("seats", "Seats");
        granted.PaymentTransactionId = null; // granted: a subscription row nothing bills

        var harness = await InvokeAsync(
            allowPackSelfService: true,
            available: new[]
            {
                Pack("radar", "Radar"),
                Pack("seats", "Seats"),
                Pack("history", "History"),
                Pack("bundled", "Bundled", tierId: "tier-pro")
            },
            owned: new[]
            {
                Owned("history", "History"),
                granted,
                Owned("gone", "Gone", status: "Cancelled")
            });

        var offered = new List<string>();
        foreach (var pack in harness.Model.PickerPacks) offered.Add(pack.Id);

        Assert.Equal(new[] { "radar" }, offered);
        Assert.True(harness.Model.ShowPackPicker);
    }

    /// <summary>A cancelled pack grants nothing, so it goes back on offer.</summary>
    [Fact]
    public async Task A_cancelled_pack_is_offered_again()
    {
        var harness = await InvokeAsync(
            allowPackSelfService: true,
            available: new[] { Pack("radar", "Radar") },
            owned: new[] { Owned("radar", "Radar", status: "Cancelled") });

        Assert.Single(harness.Model.PickerPacks);
    }

    [Fact]
    public async Task With_nothing_left_to_sell_there_is_no_picker()
    {
        var harness = await InvokeAsync(
            allowPackSelfService: true,
            available: new[] { Pack("radar", "Radar") },
            owned: new[] { Owned("radar", "Radar") });

        Assert.Empty(harness.Model.PickerPacks);
        Assert.False(harness.Model.ShowPackPicker);
    }

    [Fact]
    public async Task The_picker_is_off_unless_the_host_asks_for_it()
    {
        var harness = await InvokeAsync(available: new[] { Pack("radar", "Radar") });

        Assert.False(harness.Model.ShowPackPicker);
    }

    [Fact]
    public async Task The_pack_order_and_names_reach_the_browser()
    {
        var harness = await InvokeAsync(
            allowPackSelfService: true,
            available: new[] { Pack("radar", "Radar"), Pack("seats", "Seats") });

        Assert.Equal("radar,seats", harness.Model.PackOrderAttribute);
        Assert.Contains("\"radar\":\"Radar\"", harness.Model.PackNamesJson);
        Assert.Equal(25, harness.Model.MaxPackSelection);
    }

    // ── Labels ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_labels_the_scripts_paint_with_are_rendered_into_the_attribute()
    {
        var harness = await InvokeAsync();
        var json = harness.Model.LabelsJson;

        Assert.Contains("\"authenticatingChange\":\"Confirming the charge with your bank...\"", json);
        Assert.Contains("\"applyingChange\":\"Applying your new plan...\"", json);
        Assert.Contains("\"planChangeExpired\":", json);
        Assert.Contains("\"packStatusTrialing\":\"Trial started\"", json);
        Assert.Contains("\"continueWithPacks\":\"Continue with {count} packs\"", json);
        Assert.Contains("\"sessionExpired\":\"Your session has expired. Please sign in again.\"", json);
    }

    [Fact]
    public async Task A_host_label_override_reaches_the_browser_and_the_rest_keep_the_shipped_word()
    {
        var harness = await InvokeAsync(labels: new RegistrationSubscriptionLabels
        {
            ApplyingChange = "Nearly there..."
        });

        Assert.Contains("\"applyingChange\":\"Nearly there...\"", harness.Model.LabelsJson);
        Assert.Contains("\"authenticatingChange\":\"Confirming the charge with your bank...\"", harness.Model.LabelsJson);
    }

    [Fact]
    public async Task The_payment_required_sentence_can_be_replaced()
    {
        var shipped = await InvokeAsync();
        Assert.Contains("\"paymentMethodRequired\":\"This change needs a payment method.\"", shipped.Model.LabelsJson);

        var replaced = await InvokeAsync(paymentRequiredText: "Add a card in Billing first.");
        Assert.Contains("\"paymentMethodRequired\":\"Add a card in Billing first.\"", replaced.Model.LabelsJson);
    }

    [Fact]
    public async Task The_section_titles_come_from_the_labels()
    {
        var harness = await InvokeAsync(labels: new RegistrationSubscriptionLabels { SectionPacks = "Extras" });

        Assert.Equal("Extras", harness.Model.SectionTitle(ManageSection.AddOns));
        Assert.Equal("addOns", harness.Model.SectionName(ManageSection.AddOns));
        Assert.Equal("Subscription", harness.Model.SectionTitle(ManageSection.Subscription));
    }

    // ── Currency ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_currency_parameter_wins_and_the_catalog_is_not_even_read()
    {
        var harness = await InvokeAsync(currency: "CHF");

        Assert.Equal("CHF", harness.Model.Currency);
        Assert.Equal(0, harness.Catalog.Reads);
    }

    [Fact]
    public async Task Without_one_the_apps_own_catalog_currency_is_used()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1", new List<AppTierModel>(), new List<AppTierAddOnModel>(), "CHF");

        var harness = await InvokeAsync(catalog: catalog);

        Assert.Equal("CHF", harness.Model.Currency);
    }

    [Fact]
    public async Task An_unreadable_catalog_leaves_the_currency_at_USD_rather_than_blank()
    {
        var harness = await InvokeAsync();

        Assert.Equal("USD", harness.Model.Currency);
    }

    // ── The view compiles into the package ──────────────────────────────────────

    /// <summary>
    /// The views are compiled into the package by the Razor source generator, which writes no
    /// .g.cs to obj — so this is what proves the markup parses at build time rather than blowing
    /// up in a consuming app's first render.
    /// </summary>
    [Theory]
    [InlineData("/Views/Shared/Components/RegistrationSubscriptionManage/Default.cshtml")]
    [InlineData("/Views/Shared/Components/RegistrationAndSubscription/Default.cshtml")]
    [InlineData("/Views/Shared/_RegSubManageSection.cshtml")]
    public void The_views_are_compiled_into_the_package(string identifier)
    {
        var items = typeof(RegistrationSubscriptionManageViewModel).Assembly
            .GetCustomAttributes<RazorCompiledItemAttribute>();

        Assert.Contains(items, item => item.Identifier == identifier);
    }

    // ── The shell ───────────────────────────────────────────────────────────────

    private static RegistrationAndSubscriptionViewModel Shell(string view, string environment = "Production")
    {
        var component = new RegistrationAndSubscriptionViewComponent(
            new StubEnvironment(environment),
            NullLogger<RegistrationAndSubscriptionViewComponent>.Instance)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext
                {
                    HttpContext = new DefaultHttpContext(),
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                }
            },
            ViewEngine = new NoViewEngine()
        };

        var result = Assert.IsType<ViewViewComponentResult>(component.Invoke(view));
        return Assert.IsType<RegistrationAndSubscriptionViewModel>(result.ViewData!.Model);
    }

    [Theory]
    [InlineData("pricing", "RegistrationSubscriptionPricing")]
    [InlineData("signup", "RegistrationSubscriptionSignup")]
    [InlineData("manage", "RegistrationSubscriptionManage")]
    [InlineData(" Manage ", "RegistrationSubscriptionManage")]
    public void The_shell_dispatches_to_the_view_it_was_named(string view, string component)
    {
        var model = Shell(view);

        Assert.True(model.IsKnownView);
        Assert.Equal(component, model.ComponentName);
    }

    /// <summary>
    /// An unknown view renders nothing in production and says why in Development. Guessing would
    /// be worse than either: rendering the signup for an unknown word would offer a second account
    /// to a signed-in customer.
    /// </summary>
    [Fact]
    public void An_unknown_view_is_a_developer_message_in_development_and_silence_elsewhere()
    {
        var production = Shell("dashboard");
        Assert.False(production.IsKnownView);
        Assert.False(production.ShowDeveloperMessage);

        var development = Shell("dashboard", environment: "Development");
        Assert.True(development.ShowDeveloperMessage);
        Assert.Contains("dashboard", development.DeveloperMessage);
        Assert.Contains("\"pricing\", \"signup\" or \"manage\"", development.DeveloperMessage);
    }

    [Fact]
    public void The_shell_forwards_every_parameter_the_three_views_take()
    {
        var component = new RegistrationAndSubscriptionViewComponent(
            new StubEnvironment("Development"),
            NullLogger<RegistrationAndSubscriptionViewComponent>.Instance);

        var shellParameters = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in component.GetType().GetMethod("Invoke")!.GetParameters())
        {
            shellParameters.Add(parameter.Name!);
        }

        // Every parameter of the three views has to be reachable through the shell, or a host that
        // switches to it silently loses a setting.
        foreach (var view in new[]
                 {
                     typeof(RegistrationSubscriptionPricingViewComponent),
                     typeof(RegistrationSubscriptionSignupViewComponent),
                     typeof(RegistrationSubscriptionManageViewComponent)
                 })
        {
            var invoke = view.GetMethod("InvokeAsync")!;
            foreach (var parameter in invoke.GetParameters())
            {
                Assert.True(
                    shellParameters.Contains(parameter.Name!),
                    $"<vc:registration-and-subscription> cannot pass '{parameter.Name}' to {view.Name}.");
            }
        }
    }

    // ── The deprecations ────────────────────────────────────────────────────────

    /// <summary>
    /// The three superseded ViewComponents are deprecated, not removed: a host that uses one keeps
    /// compiling and keeps working, with the compiler pointing at the replacement. A warning, never
    /// an error — <c>[Obsolete(..., false)]</c>.
    /// </summary>
    // Naming a deprecated type is the ONE thing this test is for, so the warning it raises is
    // suppressed here and nowhere else. Narrow on purpose: a real call site in the library or a
    // consuming app must still be warned about.
#pragma warning disable CS0618
    [Theory]
    [InlineData(typeof(WildwoodComponents.Razor.Components.Registration.SignupWithSubscriptionViewComponent),
        "registration-subscription-signup")]
    [InlineData(typeof(WildwoodComponents.Razor.Components.AppTier.AppTierViewComponent),
        "registration-subscription-manage")]
    [InlineData(typeof(WildwoodComponents.Razor.Components.AppTier.PricingDisplayViewComponent),
        "registration-subscription-pricing")]
    public void The_superseded_view_components_are_deprecated_with_a_pointer_to_the_replacement(
        Type component, string replacement)
    {
        var obsolete = component.GetCustomAttribute<ObsoleteAttribute>();

        Assert.NotNull(obsolete);
        Assert.False(obsolete!.IsError, $"{component.Name} must warn, not break a host's build.");
        Assert.Contains(replacement, obsolete.Message ?? string.Empty, StringComparison.Ordinal);
    }
#pragma warning restore CS0618
}

/// <summary>
/// The pure decisions behind the manage surface, tested on their own because a rule that lives
/// only in markup is a rule this repo cannot test.
/// </summary>
public class RegistrationSubscriptionManageDecisionsTests
{
    [Fact]
    public void Section_names_are_the_JS_test_hook_vocabulary()
    {
        Assert.Equal("subscription", RegistrationSubscriptionManageDecisions.SectionName(ManageSection.Subscription));
        Assert.Equal("plans", RegistrationSubscriptionManageDecisions.SectionName(ManageSection.Plans));
        Assert.Equal("features", RegistrationSubscriptionManageDecisions.SectionName(ManageSection.Features));
        Assert.Equal("addOns", RegistrationSubscriptionManageDecisions.SectionName(ManageSection.AddOns));
        Assert.Equal("usage", RegistrationSubscriptionManageDecisions.SectionName(ManageSection.Usage));
        Assert.Equal("overrides", RegistrationSubscriptionManageDecisions.SectionName(ManageSection.Overrides));
    }

    [Theory]
    [InlineData("addons", ManageSection.AddOns)]
    [InlineData("addOns", ManageSection.AddOns)]
    [InlineData("ADDONS", ManageSection.AddOns)]
    [InlineData(" plans ", ManageSection.Plans)]
    public void Section_names_parse_the_way_a_host_writes_them(string name, ManageSection expected)
    {
        Assert.Equal(expected, RegistrationSubscriptionManageDecisions.ParseSection(name));
    }

    [Fact]
    public void A_repeated_section_is_rendered_once()
    {
        Assert.Equal(
            new[] { ManageSection.Plans, ManageSection.Usage },
            RegistrationSubscriptionManageDecisions.ParseSections("plans,usage,plans"));
    }

    [Fact]
    public void Naming_no_sections_is_not_the_same_as_naming_none()
    {
        // Null means "the host said nothing", which is every section. An empty list would be a
        // host asking for an empty page, and nothing in the tag helper can express that.
        Assert.Null(RegistrationSubscriptionManageDecisions.ParseSections(null));
        Assert.Null(RegistrationSubscriptionManageDecisions.ParseSections("   "));

        Assert.Equal(
            RegistrationSubscriptionManageDecisions.AllSections,
            RegistrationSubscriptionManageDecisions.VisibleSections(null, isAdmin: true, showAddOns: true));
    }

    [Fact]
    public void The_view_names_are_the_three_React_spells()
    {
        Assert.Equal(new[] { "pricing", "signup", "manage" }, RegistrationAndSubscriptionShell.ViewNames);
        Assert.Null(RegistrationAndSubscriptionShell.ParseView(null));
        Assert.Null(RegistrationAndSubscriptionShell.ParseView(""));
        Assert.Equal(RegistrationSubscriptionView.Manage, RegistrationAndSubscriptionShell.ParseView("MANAGE"));
    }
}
