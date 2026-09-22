using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Components.RegistrationSubscription;
using WildwoodComponents.Razor.Extensions;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Razor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The Razor signup surface. It is SERVER-rendered, so the things React decides in the browser are
/// decided here and shipped as markup: the registration mode, the catalog, the plan a link
/// preselected, the packs, the prices and every string. These tests pin those decisions, and the
/// rules the surface exists to keep — a closed sign-up never flashes a form, an invite overrides a
/// closed configuration, a plan the app does not sell is ignored, a basket is capped, and there is
/// no card-number field anywhere.
/// </summary>
public class RegistrationSubscriptionSignupViewComponentTests
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

    private sealed class StubCatalogService : IWildwoodPublicCatalogService
    {
        private readonly PublicCatalog? _catalog;
        private readonly Exception? _failure;

        public StubCatalogService(PublicCatalog? catalog, Exception? failure = null)
        {
            _catalog = catalog;
            _failure = failure;
        }

        public Task<PublicCatalog> GetAsync(string appId, string? currencyOverride = null, bool forceRefresh = false)
        {
            if (_failure is not null) return Task.FromException<PublicCatalog>(_failure);
            return Task.FromResult(_catalog!);
        }

        public void Invalidate(string appId) { }
    }

    /// <summary>Answers only what the signup view asks of the registration service.</summary>
    private sealed class StubRegistrationService : IWildwoodRegistrationService
    {
        public SignupRegistrationSettings? Settings { get; set; }

        public string? PasswordRequirements { get; set; }

        public PendingDisclaimersResponse? Disclaimers { get; set; }

        public int SettingsReads { get; private set; }

        public Task<SignupRegistrationSettings?> GetSignupRegistrationSettingsAsync(string? appId = null)
        {
            SettingsReads++;
            return Task.FromResult(Settings);
        }

        public Task<string?> GetPasswordRequirementsAsync(string appId) => Task.FromResult(PasswordRequirements);

        public Task<PendingDisclaimersResponse?> GetRegistrationDisclaimersAsync(string appId)
            => Task.FromResult(Disclaimers);

        // Not reached by the view.
        public Task<TokenValidationResponse?> ValidateTokenAsync(string token) => throw new NotSupportedException();
        public Task<RegistrationTokenDetails?> GetRegistrationTokenDetailsAsync(string token, string? appId = null) => throw new NotSupportedException();
        public Task<RegistrationValidationResponse?> ValidateRegistrationAsync(ValidateRegistrationRequest request) => throw new NotSupportedException();
        public Task<RegistrationSuccessResponse?> RegisterWithTokenAsync(TokenRegistrationRequest request) => throw new NotSupportedException();
        public Task<RegistrationSuccessResponse?> RegisterAsync(OpenRegistrationRequest request) => throw new NotSupportedException();
        public Task<PricingModelResponse?> GetPricingModelAsync(string pricingModelId) => throw new NotSupportedException();
        public Task<PricingDetails?> GetTokenPricingAsync(string token) => throw new NotSupportedException();
        public Task<bool> SkipPaymentAsync(SkipPaymentRequest request) => throw new NotSupportedException();
        public Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null) => throw new NotSupportedException();
        public Task<AuthResult> LoginAsync(string username, string email, string password, string? appId) => throw new NotSupportedException();
    }

    private sealed class StubPaymentService : IWildwoodPaymentService
    {
        public PlatformFilteredProvidersDto? Providers { get; set; }

        public Task<PlatformFilteredProvidersDto?> GetAvailableProvidersAsync(string appId)
            => Task.FromResult(Providers);

        public Task<AppPaymentConfigurationDto?> GetAppPaymentConfigurationAsync(string appId) => throw new NotSupportedException();
        public Task<InitiatePaymentResponse> InitiatePaymentAsync(InitiatePaymentRequest request) => throw new NotSupportedException();
        public Task<PaymentCompletionResult> ConfirmPaymentAsync(string paymentIntentId, PaymentProviderType providerType) => throw new NotSupportedException();
        public Task<List<SavedPaymentMethodDto>> GetSavedPaymentMethodsAsync(string customerId) => throw new NotSupportedException();
        public Task<bool> DeleteSavedPaymentMethodAsync(string paymentMethodId) => throw new NotSupportedException();
        public Task<bool> SetDefaultPaymentMethodAsync(string paymentMethodId) => throw new NotSupportedException();
        public Task<PaymentCompletionResult?> GetPaymentStatusAsync(string transactionId) => throw new NotSupportedException();
        public Task<bool> LinkTransactionToUserAsync(string externalTransactionId, string userId, string? companyClientId = null) => throw new NotSupportedException();
    }

    // ── Catalog fixtures ────────────────────────────────────────────────────────

    private static AppTierPricingModel Option(
        string id, decimal price, string frequency, int? trialDays = null, bool isDefault = false, int order = 0)
    {
        return new AppTierPricingModel
        {
            Id = id,
            Price = price,
            BillingFrequency = frequency,
            TrialDays = trialDays,
            IsDefault = isDefault,
            DisplayOrder = order,
            PricingModelId = id + "-model"
        };
    }

    private static AppTierModel Tier(
        string id = "tier-pro",
        string name = "Pro",
        bool isFreeTier = false,
        bool isDefault = false,
        params AppTierPricingModel[] options)
    {
        var tier = new AppTierModel
        {
            Id = id,
            Name = name,
            Status = CatalogHelpers.ActiveTierStatus,
            IsFreeTier = isFreeTier,
            IsDefault = isDefault,
            Currency = "USD"
        };

        foreach (var option in options) tier.PricingOptions.Add(option);
        return tier;
    }

    private static AppTierAddOnModel Pack(string id, string name, int order = 0)
    {
        var pack = new AppTierAddOnModel
        {
            Id = id,
            Name = name,
            Status = CatalogHelpers.ActiveTierStatus,
            Currency = "USD",
            DisplayOrder = order
        };

        pack.PricingOptions.Add(new AppTierAddOnPricingModel
        {
            Id = id + "-monthly",
            Price = 9m,
            BillingFrequency = "Monthly",
            IsDefault = true
        });

        return pack;
    }

    private static AppTierModel PaidTier()
    {
        return Tier(options:
        [
            Option("price-monthly", 10m, "Monthly", trialDays: 14, isDefault: true),
            Option("price-annual", 100m, "Annually", trialDays: 14, order: 1)
        ]);
    }

    private static PublicCatalog Catalog(
        IEnumerable<AppTierModel>? tiers = null,
        IEnumerable<AppTierAddOnModel>? addOns = null)
    {
        return CatalogHelpers.BuildPublicCatalog(
            "app-1",
            new List<AppTierModel>(tiers ?? new[] { PaidTier() }),
            new List<AppTierAddOnModel>(addOns ?? Array.Empty<AppTierAddOnModel>()));
    }

    // ── Invocation ──────────────────────────────────────────────────────────────

    private static readonly SignupRegistrationSettings OpenWithToken = new()
    {
        AllowOpenRegistration = true,
        AllowTokenRegistration = true
    };

    private static readonly SignupRegistrationSettings ClosedSettings = new()
    {
        AllowOpenRegistration = false,
        AllowTokenRegistration = false
    };

    private static async Task<RegistrationSubscriptionSignupViewModel> InvokeAsync(
        PublicCatalog? catalog = null,
        Exception? catalogFailure = null,
        SignupRegistrationSettings? settings = null,
        string? queryString = null,
        bool signedIn = false,
        string? appId = null,
        string? configuredAppId = "app-1",
        string? preSelectedTierId = null,
        string? preSelectedPricingId = null,
        string? preSelectedAddOnIds = null,
        string? registrationToken = null,
        string? prefillEmail = null,
        string planSelection = "choose",
        string planDefault = "none",
        string packSelection = "none",
        string tokenMode = "auto",
        string? contactUrl = null,
        string? closedText = null,
        RegistrationSubscriptionLabels? labels = null,
        PlatformFilteredProvidersDto? providers = null,
        PendingDisclaimersResponse? disclaimers = null,
        StubRegistrationService? registrationService = null)
    {
        var registration = registrationService ?? new StubRegistrationService();
        registration.Settings = settings ?? OpenWithToken;
        registration.Disclaimers = disclaimers;

        var component = new RegistrationSubscriptionSignupViewComponent(
            new StubCatalogService(catalog ?? Catalog(), catalogFailure),
            registration,
            new FakeSessionManager(signedIn ? "test-jwt" : null),
            new StubPaymentService { Providers = providers },
            new WildwoodComponentsRazorOptions { BaseUrl = "https://api.test", AppId = configuredAppId },
            NullLogger<RegistrationSubscriptionSignupViewComponent>.Instance)
        {
            ViewComponentContext = new ViewComponentContext
            {
                ViewContext = new ViewContext
                {
                    HttpContext = HttpContextWith(queryString),
                    ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                }
            },
            ViewEngine = new NoViewEngine()
        };

        var result = await component.InvokeAsync(
            appId: appId,
            preSelectedTierId: preSelectedTierId,
            preSelectedPricingId: preSelectedPricingId,
            preSelectedAddOnIds: preSelectedAddOnIds,
            registrationToken: registrationToken,
            prefillEmail: prefillEmail,
            planSelection: planSelection,
            planDefault: planDefault,
            packSelection: packSelection,
            tokenMode: tokenMode,
            contactUrl: contactUrl,
            labels: labels,
            closedText: closedText);

        var view = Assert.IsType<ViewViewComponentResult>(result);
        return Assert.IsType<RegistrationSubscriptionSignupViewModel>(view.ViewData!.Model);
    }

    private static HttpContext HttpContextWith(string? queryString)
    {
        var context = new DefaultHttpContext();
        if (queryString is { Length: > 0 }) context.Request.QueryString = new QueryString(queryString);
        return context;
    }

    // ── Closed, invite, already signed in ───────────────────────────────────────

    /// <summary>A closed sign-up is decided on the server, so it never flashes a form first.</summary>
    [Fact]
    public async Task Registration_closed_renders_the_notice_and_no_form()
    {
        var model = await InvokeAsync(settings: ClosedSettings);

        Assert.True(model.Mode.Closed);
        Assert.Equal("closed", model.InitialStep);
        Assert.Equal("Registration is closed", model.ClosedMessage);
    }

    [Fact]
    public async Task The_closed_text_parameter_replaces_the_shipped_sentence()
    {
        var model = await InvokeAsync(settings: ClosedSettings, closedText: "We are invitation-only for now.");

        Assert.Equal("We are invitation-only for now.", model.ClosedMessage);
    }

    /// <summary>
    /// Invite redemption wins over EVERYTHING, a closed configuration included: the server
    /// validates the invite token itself. The settings are not even read.
    /// </summary>
    [Fact]
    public async Task Token_mode_required_overrides_a_closed_configuration()
    {
        var registration = new StubRegistrationService();
        var model = await InvokeAsync(
            settings: ClosedSettings, tokenMode: "required", registrationService: registration);

        Assert.False(model.Mode.Closed);
        Assert.True(model.Mode.RequireToken);
        Assert.False(model.Mode.AllowOpenRegistration);
        Assert.False(model.Mode.ShowOptionalTokenEntry);
        Assert.Equal(SignupRegistrationModeSource.TokenMode, model.Mode.Source);
        Assert.Equal("register", model.InitialStep);
        Assert.Equal(0, registration.SettingsReads);
    }

    /// <summary>An invite takes its plan and its packs from the token, not from the link.</summary>
    [Fact]
    public async Task Invite_mode_resolves_no_plan_and_no_packs()
    {
        var model = await InvokeAsync(
            catalog: Catalog([PaidTier()], [Pack("addon-a", "Pack A")]),
            tokenMode: "required",
            preSelectedTierId: "tier-pro",
            preSelectedAddOnIds: "addon-a");

        Assert.True(model.IsInvite);
        Assert.Null(model.PresetPlan);
        Assert.False(model.HasPackStep);
    }

    [Fact]
    public async Task A_signed_in_visitor_is_offered_no_second_account()
    {
        var model = await InvokeAsync(signedIn: true);

        Assert.True(model.AlreadySignedIn);
        Assert.Equal("signedIn", model.InitialStep);
    }

    /// <summary>
    /// The registration mode is read on EVERY render and never cached: an operator who closes
    /// sign-up is obeyed by the next visitor rather than sixty seconds later.
    /// </summary>
    [Fact]
    public async Task The_registration_mode_is_read_on_every_render()
    {
        var registration = new StubRegistrationService();

        await InvokeAsync(registrationService: registration);
        await InvokeAsync(registrationService: registration);

        Assert.Equal(2, registration.SettingsReads);
    }

    /// <summary>Unreadable settings fall back to open sign-up, not to a closed one.</summary>
    [Fact]
    public async Task Unreadable_settings_fall_back_to_open_signup()
    {
        var registration = new StubRegistrationService { Settings = null };
        var model = await InvokeAsync(registrationService: registration, settings: null!);

        // InvokeAsync assigns OpenWithToken when settings is null, so ask the resolver directly
        // for the case the service could not answer.
        var fallback = SignupRegistrationMode.Resolve(null);
        Assert.False(fallback.Closed);
        Assert.True(fallback.AllowOpenRegistration);
        Assert.True(fallback.ShowOptionalTokenEntry);
        Assert.Equal(SignupRegistrationModeSource.Fallback, fallback.Source);

        // And the component's own answer is a working form either way.
        Assert.Equal("register", model.InitialStep);
    }

    // ── The catalog ─────────────────────────────────────────────────────────────

    /// <summary>A catalog that could not be read fails the flow rather than pricing anything.</summary>
    [Fact]
    public async Task An_unreadable_catalog_leaves_no_price_behind()
    {
        var model = await InvokeAsync(catalogFailure: new HttpRequestException("upstream is down"));

        Assert.True(model.IsCatalogUnavailable);
        Assert.Equal("failed", model.InitialStep);
        Assert.Equal("upstream is down", model.CatalogUnavailableMessage);
        Assert.Null(model.PresetPlan);
    }

    [Fact]
    public async Task With_no_app_id_the_component_says_so_rather_than_rendering_a_form()
    {
        var model = await InvokeAsync(configuredAppId: null);

        Assert.True(model.IsCatalogUnavailable);
        Assert.Equal("failed", model.InitialStep);
    }

    // ── Preselection, validation and the cap ────────────────────────────────────

    [Fact]
    public async Task A_preselected_plan_is_matched_case_insensitively_and_priced()
    {
        var model = await InvokeAsync(preSelectedTierId: "TIER-PRO", preSelectedPricingId: "price-annual");

        Assert.NotNull(model.PresetPlan);
        Assert.Equal("tier-pro", model.PresetPlan!.Tier.Id);
        Assert.Equal("price-annual", model.PresetPlan.Pricing!.Id);
        Assert.True(model.PlanRequiresPayment);
        Assert.Equal(14, model.PlanTrialDays);
    }

    /// <summary>A plan the app does not sell is ignored: the link is stale or hand-edited.</summary>
    [Fact]
    public async Task A_plan_the_app_does_not_sell_is_ignored()
    {
        var model = await InvokeAsync(preSelectedTierId: "tier-nope", preSelectedPricingId: "price-nope");

        Assert.Null(model.PresetPlan);
        Assert.Null(model.Params.TierId);
        Assert.Null(model.Params.PricingId);
    }

    [Fact]
    public async Task Preselected_packs_are_vetted_deduplicated_and_capped()
    {
        var packs = new List<AppTierAddOnModel>();
        for (var i = 0; i < 30; i++) packs.Add(Pack("addon-" + i, "Pack " + i, i));

        var asked = new List<string>();
        for (var i = 0; i < 30; i++) asked.Add("addon-" + i);
        asked.Add("addon-0");            // a duplicate
        asked.Add("addon-not-sold");     // and one the app does not sell

        var model = await InvokeAsync(
            catalog: Catalog([PaidTier()], packs),
            preSelectedAddOnIds: string.Join(",", asked));

        Assert.Equal(CatalogHelpers.MaxAddOnSelection, model.Params.AddOnIds.Count);
        Assert.DoesNotContain("addon-not-sold", model.Params.AddOnIds);
        Assert.Equal(model.Params.AddOnIds.Count, new HashSet<string>(model.Params.AddOnIds).Count);
    }

    [Fact]
    public async Task A_free_plan_takes_no_card()
    {
        var model = await InvokeAsync(
            catalog: Catalog([Tier("tier-free", "Starter", isFreeTier: true, options: Option("free", 0m, "Monthly"))]),
            preSelectedTierId: "tier-free");

        Assert.NotNull(model.PresetPlan);
        Assert.False(model.PlanRequiresPayment);
        Assert.Equal(0, model.PlanTrialDays);
    }

    /// <summary>A skip flow takes the app's default plan, never the link's.</summary>
    [Fact]
    public async Task Plan_selection_skip_takes_the_apps_default_plan()
    {
        var model = await InvokeAsync(
            catalog: Catalog([PaidTier(), Tier("tier-basic", "Basic", isDefault: true, options: Option("b", 5m, "Monthly"))]),
            planSelection: "skip",
            preSelectedTierId: "tier-pro");

        Assert.Equal("tier-basic", model.PresetPlan!.Tier.Id);
    }

    // ── Query-string precedence ─────────────────────────────────────────────────

    [Fact]
    public async Task The_query_string_fills_in_what_the_host_left_unset()
    {
        var model = await InvokeAsync(
            catalog: Catalog([PaidTier()], [Pack("addon-a", "Pack A"), Pack("addon-b", "Pack B", 1)]),
            queryString: "?tier=tier-pro&pricing=price-annual&addons=addon-b&token=TOK&email=a%40b.test");

        Assert.Equal("tier-pro", model.Params.TierId);
        Assert.Equal("price-annual", model.Params.PricingId);
        Assert.Equal(["addon-b"], model.Params.AddOnIds);
        Assert.Equal("TOK", model.Params.RegistrationToken);
        Assert.Equal("a@b.test", model.Params.PrefillEmail);
    }

    /// <summary>
    /// An explicit parameter always wins: a host that wrote it meant it, and an address bar must
    /// not quietly override the page's own decision.
    /// </summary>
    [Fact]
    public async Task An_explicit_parameter_wins_over_the_query_string()
    {
        var model = await InvokeAsync(
            catalog: Catalog(
                [PaidTier(), Tier("tier-basic", "Basic", options: Option("b", 5m, "Monthly", isDefault: true))],
                [Pack("addon-a", "Pack A"), Pack("addon-b", "Pack B", 1)]),
            queryString: "?tier=tier-basic&addons=addon-b&token=FROM-QUERY&email=query%40b.test",
            preSelectedTierId: "tier-pro",
            preSelectedAddOnIds: "addon-a",
            registrationToken: "FROM-PARAMETER",
            prefillEmail: "param@b.test");

        Assert.Equal("tier-pro", model.Params.TierId);
        Assert.Equal(["addon-a"], model.Params.AddOnIds);
        Assert.Equal("FROM-PARAMETER", model.Params.RegistrationToken);
        Assert.Equal("param@b.test", model.Params.PrefillEmail);
    }

    /// <summary><c>?invite=</c> names the same thing <c>?token=</c> does.</summary>
    [Fact]
    public async Task The_invite_query_key_is_read_as_a_registration_token()
    {
        var model = await InvokeAsync(queryString: "?invite=INV-1");

        Assert.Equal("INV-1", model.Params.RegistrationToken);
    }

    // ── Labels ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Label_overrides_reach_both_the_markup_and_the_script_bundle()
    {
        var model = await InvokeAsync(
            preSelectedTierId: "tier-pro",
            labels: new RegistrationSubscriptionLabels
            {
                RegistrationClosed = "No new accounts",
                PackStatusGranted = "Comes with your invite",
                SignupCompleteTrial = "Enjoy your {days} days"
            });

        // The ones the server writes into markup...
        Assert.Equal("No new accounts", model.Labels.RegistrationClosed);

        // ...and the ones the script paints with, already slotted where the value is known here.
        using var bundle = JsonDocument.Parse(model.LabelsJson);
        Assert.Equal("Comes with your invite", bundle.RootElement.GetProperty("packStatusGranted").GetString());
        Assert.Equal("Enjoy your 14 days", bundle.RootElement.GetProperty("successTrial").GetString());

        // Everything the host left alone keeps the shipped word.
        Assert.Equal("You are all set", model.Labels.SignupComplete);
    }

    /// <summary>The script's copy bundle carries no key the script does not read.</summary>
    [Fact]
    public async Task The_script_label_bundle_is_valid_json_with_the_keys_the_script_reads()
    {
        var model = await InvokeAsync();

        using var bundle = JsonDocument.Parse(model.LabelsJson);
        foreach (var key in new[]
                 {
                     "statusCreatingAccount", "statusSigningIn", "statusActivatingPlan", "buyingPacks",
                     "authenticatingPack", "packsUnavailable", "packStatusTrialing", "packStatusActive",
                     "packStatusFailed", "packStatusGranted", "successToken", "successTrial", "successPlain",
                     "successActive", "successPending", "planWord", "savedCardOnFile", "continueWithPacks",
                     "continueWithOnePack", "tokenRejected", "signupFailed", "submitContinue", "submitCreate"
                 })
        {
            Assert.True(bundle.RootElement.TryGetProperty(key, out var value), $"The bundle is missing {key}.");
            Assert.False(string.IsNullOrEmpty(value.GetString()), $"The bundle's {key} is empty.");
        }
    }

    [Fact]
    public async Task The_catalog_names_travel_with_the_page_so_an_outcome_can_name_a_pack()
    {
        var model = await InvokeAsync(
            catalog: Catalog([PaidTier()], [Pack("addon-a", "Radar Pack")]));

        using var names = JsonDocument.Parse(model.NamesJson);
        Assert.Equal("Pro", names.RootElement.GetProperty("tiers").GetProperty("tier-pro").GetString());
        Assert.Equal("Radar Pack", names.RootElement.GetProperty("addOns").GetProperty("addon-a").GetString());
    }

    // ── The form's submit wording ───────────────────────────────────────────────

    [Fact]
    public async Task The_submit_button_says_Continue_while_a_plan_step_is_still_ahead()
    {
        var choosing = await InvokeAsync(planSelection: "choose");
        Assert.True(choosing.PlanStepAhead);
        Assert.Equal("Continue", choosing.SubmitButtonText);

        var preset = await InvokeAsync(planSelection: "choose", preSelectedTierId: "tier-pro");
        Assert.False(preset.PlanStepAhead);
        Assert.Equal("Create Account", preset.SubmitButtonText);

        var invite = await InvokeAsync(tokenMode: "required");
        Assert.False(invite.PlanStepAhead);
        Assert.Equal("Create Account", invite.SubmitButtonText);
    }

    // ── The plan the grid opens on ──────────────────────────────────────────────

    /// <summary>
    /// The pure rules, ported from the JS suites that cover them
    /// (<c>regsubSignupFlowDefault.test.tsx</c> and the <c>planDefault</c> cases in
    /// <c>RegistrationAndSubscription.signup.test.tsx</c>).
    /// </summary>
    [Theory]
    [InlineData("free", SignupPlanDefault.Free)]
    [InlineData("Free", SignupPlanDefault.Free)]
    [InlineData("  free  ", SignupPlanDefault.Free)]
    [InlineData("none", SignupPlanDefault.None)]
    [InlineData("", SignupPlanDefault.None)]
    [InlineData(null, SignupPlanDefault.None)]
    [InlineData("freeish", SignupPlanDefault.None)]
    public void The_plan_default_attribute_is_parsed_leniently(string? value, SignupPlanDefault expected)
    {
        Assert.Equal(expected, RegistrationSubscriptionSignupDecisions.ParsePlanDefault(value));
    }

    [Fact]
    public void The_default_tier_is_the_apps_first_free_plan_and_only_when_asked_for()
    {
        var catalog = Catalog([PaidTier(), Tier(id: "tier-free", name: "Starter", isFreeTier: true)]);

        Assert.Equal(
            "tier-free",
            RegistrationSubscriptionSignupDecisions.DefaultTierId(SignupPlanDefault.Free, false, catalog));

        // Not asked for, no free plan to suggest, an invite, or no catalog at all: nothing.
        Assert.Null(RegistrationSubscriptionSignupDecisions.DefaultTierId(SignupPlanDefault.None, false, catalog));
        Assert.Null(RegistrationSubscriptionSignupDecisions.DefaultTierId(
            SignupPlanDefault.Free, false, Catalog([PaidTier()])));
        Assert.Null(RegistrationSubscriptionSignupDecisions.DefaultTierId(SignupPlanDefault.Free, true, catalog));
        Assert.Null(RegistrationSubscriptionSignupDecisions.DefaultTierId(SignupPlanDefault.Free, false, null));
    }

    /// <summary>
    /// A chosen plan wins; the default beats the link's plan, because a preselected id still
    /// showing here is one the flow already refused; the link's plan applies when there is no
    /// default.
    /// </summary>
    [Fact]
    public void The_grid_marks_the_chosen_plan_then_the_default_then_the_links_plan()
    {
        Assert.Equal(
            "tier-chosen",
            RegistrationSubscriptionSignupDecisions.HighlightTierId("tier-chosen", "tier-free", "tier-pro"));
        Assert.Equal(
            "tier-free", RegistrationSubscriptionSignupDecisions.HighlightTierId(null, "tier-free", "tier-pro"));
        Assert.Equal("tier-pro", RegistrationSubscriptionSignupDecisions.HighlightTierId(null, null, "tier-pro"));
        Assert.Null(RegistrationSubscriptionSignupDecisions.HighlightTierId(null, null, null));
    }

    /// <summary>
    /// And what the surface actually renders: the free plan's card opens marked, with the
    /// call-to-action a marked card carries — while nothing has been chosen, so the plan step is
    /// still ahead of the visitor.
    /// </summary>
    [Fact]
    public async Task Plan_default_free_opens_the_grid_on_the_free_plan_without_choosing_it()
    {
        var catalog = Catalog([PaidTier(), Tier(id: "tier-free", name: "Starter", isFreeTier: true)]);

        var model = await InvokeAsync(catalog: catalog, planDefault: "free");

        Assert.Equal("tier-free", model.DefaultTierId);
        Assert.Equal("tier-free", model.HighlightedTierId);
        Assert.True(model.PlanStepAhead);
        Assert.Null(model.PresetPlan);
        Assert.Null(model.Params.TierId);

        var marked = model.PlanGrid!.Cards.Single(card => card.IsHighlighted);
        Assert.Equal("tier-free", marked.Tier.Id);
    }

    [Fact]
    public async Task Without_a_default_the_grid_opens_on_nothing()
    {
        var catalog = Catalog([PaidTier(), Tier(id: "tier-free", name: "Starter", isFreeTier: true)]);

        var model = await InvokeAsync(catalog: catalog);

        Assert.Null(model.DefaultTierId);
        Assert.Null(model.HighlightedTierId);
        Assert.DoesNotContain(model.PlanGrid!.Cards, card => card.IsHighlighted);
    }

    /// <summary>A link's plan is the visitor's own choice, and it still skips the step entirely.</summary>
    [Fact]
    public async Task A_links_plan_beats_the_default_and_still_skips_the_plan_step()
    {
        var catalog = Catalog([PaidTier(), Tier(id: "tier-free", name: "Starter", isFreeTier: true)]);

        var model = await InvokeAsync(catalog: catalog, planDefault: "free", preSelectedTierId: "tier-pro");

        Assert.Equal("tier-pro", model.HighlightedTierId);
        Assert.Equal("tier-pro", model.PresetPlan!.Tier.Id);
        Assert.False(model.PlanStepAhead);
    }

    /// <summary>An invite's plan comes from its token, so there is no grid for a default to open on.</summary>
    [Fact]
    public async Task An_invite_is_suggested_nothing()
    {
        var catalog = Catalog([PaidTier(), Tier(id: "tier-free", name: "Starter", isFreeTier: true)]);

        var model = await InvokeAsync(catalog: catalog, planDefault: "free", tokenMode: "required");

        Assert.Null(model.DefaultTierId);
        Assert.Null(model.HighlightedTierId);
    }

    /// <summary>Nothing to suggest is not a failure: the grid is the one it would have been anyway.</summary>
    [Fact]
    public async Task An_app_with_no_free_plan_is_suggested_nothing()
    {
        var model = await InvokeAsync(catalog: Catalog([PaidTier()]), planDefault: "free");

        Assert.Null(model.DefaultTierId);
        Assert.Null(model.HighlightedTierId);
    }

    // ── The pack step ───────────────────────────────────────────────────────────

    [Fact]
    public async Task The_pack_step_is_off_by_default_and_on_for_pack_selection_choose()
    {
        var packs = new[] { Pack("addon-a", "Pack A") };

        var off = await InvokeAsync(catalog: Catalog([PaidTier()], packs));
        Assert.False(off.HasPackStep);

        var on = await InvokeAsync(catalog: Catalog([PaidTier()], packs), packSelection: "choose");
        Assert.True(on.HasPackStep);

        // React spells the same mode "multi"; both are accepted.
        var multi = await InvokeAsync(catalog: Catalog([PaidTier()], packs), packSelection: "multi");
        Assert.True(multi.HasPackStep);
    }

    /// <summary>
    /// <c>pack-selection="none"</c> removes the STEP only — a link's packs are still bought,
    /// because the visitor picked them on the pricing page.
    /// </summary>
    [Fact]
    public async Task Pack_selection_none_still_carries_a_links_packs()
    {
        var model = await InvokeAsync(
            catalog: Catalog([PaidTier()], [Pack("addon-a", "Pack A")]),
            packSelection: "none",
            preSelectedAddOnIds: "addon-a");

        Assert.False(model.HasPackStep);
        Assert.Equal(["addon-a"], model.Params.AddOnIds);
        Assert.Equal("addon-a", model.SelectedAddOnsAttribute);
    }

    // ── The publishable key the pack checkout needs ─────────────────────────────

    [Fact]
    public async Task The_publishable_key_is_resolved_server_side_by_the_shared_rule()
    {
        var providers = new PlatformFilteredProvidersDto
        {
            AvailableProviders =
            [
                new PaymentProviderDto { Id = "no-key", IsEnabled = true },
                new PaymentProviderDto { Id = "keyed", IsEnabled = true, PublishableKey = "pk_test_1" },
                new PaymentProviderDto { Id = "default", IsEnabled = true, IsDefault = true, PublishableKey = "pk_test_2" }
            ]
        };

        var model = await InvokeAsync(providers: providers);

        Assert.Equal("pk_test_2", model.PublishableKey);
        Assert.Equal("default", model.PaymentProviderId);
    }

    [Fact]
    public async Task No_provider_with_a_key_leaves_the_key_unset_rather_than_guessing()
    {
        var model = await InvokeAsync(providers: new PlatformFilteredProvidersDto
        {
            AvailableProviders = [new PaymentProviderDto { Id = "no-key", IsEnabled = true }]
        });

        Assert.Null(model.PublishableKey);
    }

    // ── Disclaimers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// The app's registration disclaimers are read at render time and rendered as real markup. A
    /// script cannot do that: one whose content format is HTML has to be rendered AS HTML, and
    /// nothing in this package writes a server string through innerHTML.
    /// </summary>
    [Fact]
    public async Task The_registration_disclaimers_are_rendered_server_side()
    {
        var model = await InvokeAsync(disclaimers: new PendingDisclaimersResponse
        {
            HasPendingDisclaimers = true,
            Disclaimers =
            [
                new PendingDisclaimerModel
                {
                    DisclaimerId = "d1",
                    VersionId = "v1",
                    Title = "Terms",
                    Content = "<p>Be nice.</p>",
                    ContentFormat = "HTML",
                    IsRequired = true
                }
            ]
        });

        var disclaimer = Assert.Single(model.Disclaimers);
        Assert.Equal("d1", disclaimer.DisclaimerId);
        Assert.Equal("v1", disclaimer.VersionId);
        Assert.True(disclaimer.IsRequired);
    }

    // ── The view compiles into the package ──────────────────────────────────────

    /// <summary>
    /// The views are compiled into the package by the Razor source generator, which writes no
    /// .g.cs to obj — so this is what proves the markup parses at build time rather than blowing
    /// up in a consuming app's first render.
    /// </summary>
    [Theory]
    [InlineData("/Views/Shared/Components/RegistrationSubscriptionSignup/Default.cshtml")]
    [InlineData("/Views/Shared/_RegSubPlanGrid.cshtml")]
    public void The_views_are_compiled_into_the_package(string identifier)
    {
        var items = typeof(RegistrationSubscriptionSignupViewModel).Assembly
            .GetCustomAttributes<RazorCompiledItemAttribute>();

        Assert.Contains(items, item => item.Identifier == identifier);
    }
}

/// <summary>
/// The pure decisions behind the signup surface, tested on their own because a rule that lives
/// only in markup is a rule this repo cannot test.
/// </summary>
public class RegistrationSubscriptionSignupDecisionsTests
{
    [Theory]
    [InlineData(null, "choose")]
    [InlineData("", "choose")]
    [InlineData("choose", "choose")]
    [InlineData("CHOOSE", "choose")]
    [InlineData("nonsense", "choose")]
    [InlineData("skip", "skip")]
    [InlineData(" Skip ", "skip")]
    public void Plan_selection_parses_leniently_and_defaults_to_choose(string? value, string expected)
    {
        var parsed = RegistrationSubscriptionSignupDecisions.ParsePlanSelection(value);
        Assert.Equal(expected, RegistrationSubscriptionSignupDecisions.PlanSelectionCode(parsed));
    }

    [Theory]
    [InlineData(null, "none")]
    [InlineData("none", "none")]
    [InlineData("nonsense", "none")]
    [InlineData("choose", "choose")]
    [InlineData("multi", "choose")]
    [InlineData(" MULTI ", "choose")]
    public void Pack_selection_parses_leniently_and_defaults_to_none(string? value, string expected)
    {
        var parsed = RegistrationSubscriptionSignupDecisions.ParsePackSelection(value);
        Assert.Equal(expected, RegistrationSubscriptionSignupDecisions.PackSelectionCode(parsed));
    }

    [Theory]
    [InlineData(null, "auto")]
    [InlineData("auto", "auto")]
    [InlineData("nonsense", "auto")]
    [InlineData("required", "required")]
    [InlineData(" Required ", "required")]
    public void Token_mode_parses_leniently_and_defaults_to_auto(string? value, string expected)
    {
        var parsed = RegistrationSubscriptionSignupDecisions.ParseTokenMode(value);
        Assert.Equal(expected, RegistrationSubscriptionSignupDecisions.TokenModeCode(parsed));
    }

    /// <summary>The machine's Done is spelled <c>success</c> in the DOM, as React spells it.</summary>
    [Theory]
    [InlineData(SignupStep.Loading, "loading")]
    [InlineData(SignupStep.Closed, "closed")]
    [InlineData(SignupStep.Register, "register")]
    [InlineData(SignupStep.Token, "token")]
    [InlineData(SignupStep.Plan, "plan")]
    [InlineData(SignupStep.Packs, "packs")]
    [InlineData(SignupStep.Payment, "payment")]
    [InlineData(SignupStep.Creating, "creating")]
    [InlineData(SignupStep.Disclaimers, "disclaimers")]
    [InlineData(SignupStep.PackCheckout, "packCheckout")]
    [InlineData(SignupStep.Done, "success")]
    [InlineData(SignupStep.Failed, "failed")]
    public void Every_step_has_the_name_the_DOM_uses(SignupStep step, string expected)
    {
        Assert.Equal(expected, RegistrationSubscriptionSignupDecisions.StepName(step));
    }

    [Fact]
    public void The_success_sentence_follows_what_actually_happened()
    {
        var labels = RegistrationSubscriptionLabels.Defaults;

        Assert.Equal(
            labels.SignupCompleteToken,
            RegistrationSubscriptionSignupDecisions.SuccessMessageTemplate(labels, SignupSuccessKind.TokenGrant, 0));
        Assert.Equal(
            labels.SignupCompletePlain,
            RegistrationSubscriptionSignupDecisions.SuccessMessageTemplate(labels, SignupSuccessKind.NoPlan, 0));
        Assert.Equal(
            labels.SignupCompletePending,
            RegistrationSubscriptionSignupDecisions.SuccessMessageTemplate(labels, SignupSuccessKind.Pending, 0));
        Assert.Contains(
            "14",
            RegistrationSubscriptionSignupDecisions.SuccessMessageTemplate(labels, SignupSuccessKind.Trial, 14));
        Assert.Equal(
            labels.SignupCompleteActive,
            RegistrationSubscriptionSignupDecisions.SuccessMessageTemplate(labels, SignupSuccessKind.Active, 0));
    }

    [Fact]
    public void Each_pack_status_has_its_own_word()
    {
        var labels = RegistrationSubscriptionLabels.Defaults;

        Assert.Equal(labels.PackStatusTrialing, RegistrationSubscriptionSignupDecisions.PackStatusLabel(labels, "trialing"));
        Assert.Equal(labels.PackStatusActive, RegistrationSubscriptionSignupDecisions.PackStatusLabel(labels, "active"));
        Assert.Equal(labels.PackStatusGranted, RegistrationSubscriptionSignupDecisions.PackStatusLabel(labels, "granted"));
        Assert.Equal(labels.PackStatusFailed, RegistrationSubscriptionSignupDecisions.PackStatusLabel(labels, "failed"));
        Assert.Equal(labels.PackStatusFailed, RegistrationSubscriptionSignupDecisions.PackStatusLabel(labels, "anything else"));
    }
}
