using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.Hosting;
using Microsoft.Extensions.Caching.Memory;
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
/// The Razor pricing surface. It is SERVER-rendered, so almost everything React decides in the
/// browser is decided here and shipped as markup: both billing cycles' prices, the pack grouping,
/// the footer wording, the JSON-LD and the select-URL merge. These tests pin those decisions
/// against a stubbed catalog, and pin the two rules the surface exists to keep — no price is
/// invented, and a catalog that could not be read leaves NO price behind.
/// </summary>
public class RegistrationSubscriptionPricingViewComponentTests
{
    /// <summary>
    /// Stands in for the composite view engine. <c>ViewComponent.View(...)</c> otherwise resolves
    /// one from <c>HttpContext.RequestServices</c>, which no request-free test has.
    /// </summary>
    private sealed class NoViewEngine : ICompositeViewEngine
    {
        public IReadOnlyList<IViewEngine> ViewEngines => Array.Empty<IViewEngine>();

        public ViewEngineResult FindView(Microsoft.AspNetCore.Mvc.ActionContext context, string viewName, bool isMainPage) =>
            ViewEngineResult.NotFound(viewName, Array.Empty<string>());

        public ViewEngineResult GetView(string? executingFilePath, string viewPath, bool isMainPage) =>
            ViewEngineResult.NotFound(viewPath, Array.Empty<string>());
    }

    /// <summary>A catalog the component is handed, or a failure it has to survive.</summary>
    private sealed class StubCatalogService : IWildwoodPublicCatalogService
    {
        private readonly PublicCatalog? _catalog;
        private readonly Exception? _failure;

        public StubCatalogService(PublicCatalog? catalog, Exception? failure = null)
        {
            _catalog = catalog;
            _failure = failure;
        }

        public int Calls { get; private set; }

        public string? LastCurrency { get; private set; }

        public Task<PublicCatalog> GetAsync(string appId, string? currencyOverride = null, bool forceRefresh = false)
        {
            Calls++;
            LastCurrency = currencyOverride;
            if (_failure is not null) return Task.FromException<PublicCatalog>(_failure);
            return Task.FromResult(_catalog!);
        }

        public void Invalidate(string appId) { }
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
            DisplayOrder = order
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

    private static AppTierAddOnModel Pack(
        string id = "addon-seats",
        string name = "Extra Seats",
        string category = "",
        decimal? price = 19m,
        int? trialDays = null,
        int order = 0)
    {
        var pack = new AppTierAddOnModel
        {
            Id = id,
            Name = name,
            Category = category,
            Status = CatalogHelpers.ActiveTierStatus,
            Currency = "USD",
            DisplayOrder = order
        };

        if (price is not null)
        {
            pack.PricingOptions.Add(new AppTierAddOnPricingModel
            {
                Id = id + "-monthly",
                Price = price.Value,
                BillingFrequency = "Monthly",
                TrialDays = trialDays,
                IsDefault = true
            });
        }

        return pack;
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

    /// <summary>A plan priced both ways, so the billing toggle has something to switch to.</summary>
    private static AppTierModel PaidTier()
    {
        return Tier(
            options:
            [
                Option("price-monthly", 10m, "Monthly", trialDays: 14, isDefault: true),
                Option("price-annual", 100m, "Annually", trialDays: 14, order: 1)
            ]);
    }

    // ── Invocation ──────────────────────────────────────────────────────────────

    private static RegistrationSubscriptionPricingViewComponent Component(
        IWildwoodPublicCatalogService catalog, string? configuredAppId = "app-1")
    {
        return new RegistrationSubscriptionPricingViewComponent(
            catalog,
            new WildwoodComponentsRazorOptions { BaseUrl = "https://api.test", AppId = configuredAppId },
            NullLogger<RegistrationSubscriptionPricingViewComponent>.Instance)
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

    private static async Task<RegistrationSubscriptionPricingViewModel> InvokeAsync(
        PublicCatalog? catalog = null,
        Exception? failure = null,
        string? appId = null,
        string? currency = null,
        bool showPlans = true,
        bool showAddOns = false,
        bool offerFreeTierChoice = true,
        string packSelection = "none",
        IReadOnlyList<AddOnGroup>? addOnGroups = null,
        bool showBillingToggle = true,
        string defaultBilling = "monthly",
        string? highlightTierId = null,
        string? contactUrl = null,
        bool includeJsonLd = false,
        string? jsonLdUrl = null,
        RegistrationSubscriptionLabels? labels = null,
        string? selectUrl = null,
        string? unavailableText = null,
        string? configuredAppId = "app-1")
    {
        var service = new StubCatalogService(catalog ?? Catalog(), failure);
        var result = await Component(service, configuredAppId).InvokeAsync(
            appId: appId,
            currency: currency,
            showPlans: showPlans,
            showAddOns: showAddOns,
            offerFreeTierChoice: offerFreeTierChoice,
            packSelection: packSelection,
            addOnGroups: addOnGroups,
            showBillingToggle: showBillingToggle,
            defaultBilling: defaultBilling,
            showFeatureComparison: true,
            showLimits: true,
            highlightTierId: highlightTierId,
            contactUrl: contactUrl,
            includeJsonLd: includeJsonLd,
            jsonLdUrl: jsonLdUrl,
            labels: labels,
            selectUrl: selectUrl,
            unavailableText: unavailableText);

        var view = Assert.IsType<ViewViewComponentResult>(result);
        return Assert.IsType<RegistrationSubscriptionPricingViewModel>(view.ViewData!.Model);
    }

    // ── First paint: live prices, both cycles ───────────────────────────────────

    /// <summary>
    /// The whole point of the Razor port: the page ships with real prices for BOTH cycles, so the
    /// toggle is a visibility switch and the browser never formats money.
    /// </summary>
    [Fact]
    public async Task Both_billing_periods_are_priced_on_the_server()
    {
        var model = await InvokeAsync();

        var card = Assert.Single(model.PlanCards);
        Assert.Equal(FormatHelpers.FormatMoney(10m, "USD"), card.MonthlyPriceText);
        Assert.Equal(FormatHelpers.FormatMoney(100m, "USD"), card.AnnualPriceText);
        // The interval is the server's own frequency, lower-cased, exactly as the Blazor grid
        // writes it; "month" is only the fallback for an option that names no frequency at all.
        Assert.Equal("monthly", card.MonthlyIntervalText);
        Assert.Equal("annually", card.AnnualIntervalText);
        Assert.Equal("price-monthly", card.PricingIdFor(PricingBilling.Monthly));
        Assert.Equal("price-annual", card.PricingIdFor(PricingBilling.Annual));
    }

    [Fact]
    public async Task The_trial_line_is_rendered_for_each_cycle()
    {
        var model = await InvokeAsync();

        var card = Assert.Single(model.PlanCards);
        Assert.Equal("14-day free trial", card.MonthlyTrialText);
        Assert.Equal("14-day free trial", card.AnnualTrialText);
    }

    /// <summary>Twelve months at 10 against 100 a year: 17% off, rounded away from zero.</summary>
    [Fact]
    public async Task The_annual_saving_is_computed_from_the_live_options()
    {
        var model = await InvokeAsync();

        Assert.Equal(17, model.PlanCards[0].AnnualDiscount);
        Assert.Equal("Save 17%", model.PlanCards[0].AnnualDiscountText);
        Assert.Equal("Save up to 17%", model.AnnualSavingsText);
        Assert.True(model.ShowToggle);
    }

    /// <summary>A toggle with nothing to switch to is a control that lies.</summary>
    [Fact]
    public async Task The_toggle_is_hidden_when_nothing_is_priced_annually()
    {
        var model = await InvokeAsync(Catalog([Tier(options: [Option("m", 10m, "Monthly", isDefault: true)])]));

        Assert.False(model.ShowToggle);
        Assert.Equal(string.Empty, model.AnnualSavingsText);
    }

    [Fact]
    public async Task The_default_billing_decides_which_price_shows_first()
    {
        var model = await InvokeAsync(defaultBilling: "annual");

        Assert.Equal("annual", model.BillingCode);
        Assert.Equal(PricingBilling.Annual, model.Billing);
    }

    /// <summary>
    /// The two mode attributes are STRINGS, in the JS union's own spelling, because a Razor
    /// tag-helper attribute whose property is not a string compiles as a C# expression — an enum
    /// would make every host write <c>default-billing="@PricingBilling.Annual"</c>. A typo picks
    /// the safe default rather than throwing the page away.
    /// </summary>
    [Theory]
    [InlineData("annual", PricingBilling.Annual)]
    [InlineData("ANNUAL", PricingBilling.Annual)]
    [InlineData("monthly", PricingBilling.Monthly)]
    [InlineData("", PricingBilling.Monthly)]
    [InlineData("yearly", PricingBilling.Monthly)]
    public void The_billing_attribute_is_the_js_spelling(string value, PricingBilling expected)
    {
        Assert.Equal(expected, RegistrationSubscriptionPricingDecisions.ParseBilling(value));
    }

    [Theory]
    [InlineData("multi", PricingPackSelection.Multi)]
    [InlineData("Multi", PricingPackSelection.Multi)]
    [InlineData("none", PricingPackSelection.None)]
    [InlineData("several", PricingPackSelection.None)]
    [InlineData(null, PricingPackSelection.None)]
    public void The_pack_selection_attribute_is_the_js_spelling(string? value, PricingPackSelection expected)
    {
        Assert.Equal(expected, RegistrationSubscriptionPricingDecisions.ParsePackSelection(value));
    }

    [Fact]
    public async Task A_free_plan_with_no_option_says_Free_and_starts_no_trial()
    {
        var model = await InvokeAsync(Catalog([Tier("tier-free", "Starter", isFreeTier: true)]));

        var card = Assert.Single(model.PlanCards);
        Assert.Equal("Free", card.MonthlyPriceText);
        Assert.Equal(string.Empty, card.MonthlyTrialText);
        Assert.Equal("Get Started", card.CallToActionText);
    }

    [Fact]
    public async Task A_paid_plan_that_sells_nothing_says_Custom()
    {
        var model = await InvokeAsync(Catalog([Tier("tier-ent", "Enterprise")]));

        var card = Assert.Single(model.PlanCards);
        Assert.True(card.IsEnterprise);
        Assert.Equal("Custom", card.MonthlyPriceText);
        Assert.Equal("Custom", card.AnnualPriceText);
    }

    [Fact]
    public async Task Free_plans_disappear_when_the_host_does_not_offer_them()
    {
        var catalog = Catalog([Tier("tier-free", "Starter", isFreeTier: true), PaidTier()]);

        var offered = await InvokeAsync(catalog);
        var withheld = await InvokeAsync(catalog, offerFreeTierChoice: false);

        Assert.Equal(2, offered.PlanCards.Count);
        Assert.Equal("tier-pro", Assert.Single(withheld.PlanCards).Tier.Id);
    }

    [Fact]
    public async Task The_highlighted_plan_matches_case_insensitively_and_changes_its_call_to_action()
    {
        var model = await InvokeAsync(highlightTierId: "TIER-PRO");

        var card = Assert.Single(model.PlanCards);
        Assert.True(card.IsHighlighted);
        Assert.Equal("Continue with This Plan", card.CallToActionText);
        Assert.Equal(PricingFooterKind.Select, card.Footer);
    }

    // ── Footer priority and link attributes ─────────────────────────────────────

    /// <summary>
    /// The order is the Blazor grid's, and it is load-bearing: a plan's OWN contact link wins over
    /// the component's, and a "contact us" plan with nowhere to go still offers a button rather
    /// than a dead card.
    /// </summary>
    [Fact]
    public async Task A_plans_own_contact_link_wins_over_the_components()
    {
        var tier = Tier("tier-ent", "Enterprise");
        tier.ShowContactButton = true;
        tier.ContactButtonUrl = "https://example.test/sales";

        var model = await InvokeAsync(Catalog([tier]), contactUrl: "https://example.test/other");

        var card = Assert.Single(model.PlanCards);
        Assert.Equal(PricingFooterKind.ContactLink, card.Footer);
        Assert.Equal("Contact Us", card.FooterText);
        Assert.Equal("https://example.test/sales", card.FooterUrl);
        Assert.Equal("_blank", card.FooterTarget);
        Assert.Equal("noopener noreferrer", card.FooterRel);
    }

    [Fact]
    public async Task An_unpriced_plan_points_at_the_components_contact_url()
    {
        var model = await InvokeAsync(Catalog([Tier("tier-ent", "Enterprise")]), contactUrl: "/contact");

        var card = Assert.Single(model.PlanCards);
        Assert.Equal(PricingFooterKind.ContactSalesLink, card.Footer);
        Assert.Equal("Contact Sales", card.FooterText);

        // A relative URL stays in the app, so it gets neither attribute.
        Assert.Null(card.FooterTarget);
        Assert.Null(card.FooterRel);
    }

    [Fact]
    public async Task An_unpriced_plan_with_nowhere_to_go_still_offers_a_button()
    {
        var model = await InvokeAsync(Catalog([Tier("tier-ent", "Enterprise")]));

        var card = Assert.Single(model.PlanCards);
        Assert.Equal(PricingFooterKind.ContactSalesButton, card.Footer);
        Assert.Equal("Contact Sales", card.FooterText);
    }

    [Fact]
    public async Task A_plan_the_operator_hid_the_button_on_renders_no_footer()
    {
        var tier = PaidTier();
        tier.ShowSubscribeButton = false;

        var model = await InvokeAsync(Catalog([tier]));

        Assert.Equal(PricingFooterKind.None, Assert.Single(model.PlanCards).Footer);
    }

    // ── Packs ───────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_packs_only_surface_renders_packs_and_no_plans()
    {
        var model = await InvokeAsync(
            Catalog(addOns: [Pack(trialDays: 7)]),
            showPlans: false,
            showAddOns: true);

        Assert.False(model.HasPlans);
        Assert.True(model.HasPacks);

        var group = Assert.Single(model.PackGroups);
        Assert.Equal("all", group.Id);

        var card = model.PackCard(Assert.Single(group.AddOns));
        Assert.True(card.HasPrice);
        Assert.Equal(FormatHelpers.FormatMoney(19m, "USD") + "/mo", card.PriceText);
        Assert.Equal("7-day free trial", card.TrialText);
        Assert.Equal("Select Extra Seats", card.SelectAriaLabel);
    }

    /// <summary>A pack the operator defined but never priced says so; a blank would read as free.</summary>
    [Fact]
    public async Task An_unpriced_pack_says_it_is_not_yet_available()
    {
        var model = await InvokeAsync(
            Catalog(addOns: [Pack(price: null)]), showPlans: false, showAddOns: true);

        var card = model.PackCard(model.PackGroups[0].AddOns[0]);
        Assert.False(card.HasPrice);
        Assert.Equal("Not yet available", card.PriceText);
    }

    /// <summary>
    /// The stray rule: a pack the company sells and has priced going silently missing from the page
    /// that sells it is the one failure this must not have.
    /// </summary>
    [Fact]
    public async Task Grouped_packs_keep_their_headings_and_strays_land_in_a_catch_all()
    {
        var catalog = Catalog(addOns:
        [
            Pack("addon-docs", "Documents", category: "Docs", order: 0),
            Pack("addon-seats", "Extra Seats", category: "Nowhere", order: 1)
        ]);

        var groups = new List<AddOnGroup>
        {
            new() { Id = "docs", Title = "Documents", Blurb = "Store more", Categories = { "Docs" } },
            new() { Id = "empty", Title = "Nothing here", Categories = { "Unused" } }
        };

        var model = await InvokeAsync(catalog, showPlans: false, showAddOns: true, addOnGroups: groups);

        Assert.Equal(2, model.PackGroups.Count);

        Assert.Equal("docs", model.PackGroups[0].Id);
        Assert.Equal("Store more", model.PackGroups[0].Blurb);
        Assert.Equal("addon-docs", Assert.Single(model.PackGroups[0].AddOns).Id);

        // The empty group was dropped; the stray came last, under "More packs".
        Assert.Equal("more", model.PackGroups[1].Id);
        Assert.Equal("More packs", model.PackGroups[1].Title);
        Assert.Equal("addon-seats", Assert.Single(model.PackGroups[1].AddOns).Id);
    }

    [Fact]
    public async Task The_basket_hooks_carry_the_catalog_order_and_the_cap()
    {
        var model = await InvokeAsync(
            Catalog(addOns: [Pack("addon-a", order: 0), Pack("addon-b", order: 1)]),
            showPlans: false,
            showAddOns: true,
            packSelection: "multi");

        Assert.True(model.IsMultiSelect);
        Assert.Equal("addon-a,addon-b", model.PackOrderAttribute);
        Assert.Equal(CatalogHelpers.MaxAddOnSelection, model.MaxPackSelection);
        Assert.Equal(25, model.MaxPackSelection);
        Assert.Equal("Continue with 0 packs", model.ContinuePacksInitialLabel);
    }

    [Fact]
    public void The_continue_label_is_singular_at_exactly_one_pack()
    {
        var labels = RegistrationSubscriptionLabels.Defaults;

        Assert.Equal(
            "Continue with 1 pack",
            RegistrationSubscriptionPricingDecisions.ContinueWithPacksLabel(1, labels));
        Assert.Equal(
            "Continue with 3 packs",
            RegistrationSubscriptionPricingDecisions.ContinueWithPacksLabel(3, labels));
    }

    // ── The unavailable panel ───────────────────────────────────────────────────

    [Fact]
    public async Task An_unreadable_catalog_leaves_no_price_behind()
    {
        var model = await InvokeAsync(failure: new HttpRequestException("upstream exploded"));

        Assert.True(model.IsUnavailable);
        Assert.Null(model.Catalog);
        Assert.Empty(model.PlanCards);
        Assert.Empty(model.PackGroups);
        Assert.False(model.HasPlans);
        Assert.Equal("Pricing is unavailable right now", model.UnavailableMessage);
        Assert.Equal("upstream exploded", model.Error);

        // The cross-stack code React hands its onError, so a host's analytics can tell a pricing
        // outage apart from every other failure without parsing the sentence.
        Assert.Equal("catalog_unavailable", model.ErrorCode);
    }

    [Fact]
    public async Task A_readable_catalog_carries_no_error_code()
    {
        Assert.Equal(string.Empty, (await InvokeAsync()).ErrorCode);
    }

    /// <summary>
    /// The payload a host building its own anchors needs: the plan, its option under the cycle on
    /// screen, and whatever packs were ticked — so one link buys the whole basket.
    /// </summary>
    [Fact]
    public void A_plans_payload_carries_the_cycles_option_and_the_whole_basket()
    {
        var selection = RegistrationSubscriptionPricingDecisions.PlanSelectionPayload(
            PaidTier(), PricingBilling.Annual, new[] { "addon-a", "addon-b" });

        Assert.Equal("tier-pro", selection.TierId);
        Assert.Equal("price-annual", selection.PricingId);
        Assert.Equal(PricingBilling.Annual, selection.Billing);
        Assert.Equal(new[] { "addon-a", "addon-b" }, selection.AddOnIds);
    }

    [Fact]
    public void A_single_packs_payload_implies_no_plan()
    {
        var selection = RegistrationSubscriptionPricingDecisions.PackSelectionPayload(
            "addon-a", PricingBilling.Monthly);

        Assert.Null(selection.TierId);
        Assert.Null(selection.PricingId);
        Assert.Equal("addon-a", Assert.Single(selection.AddOnIds));
    }

    [Fact]
    public async Task The_host_may_replace_the_unavailable_sentence()
    {
        var model = await InvokeAsync(
            failure: new HttpRequestException("down"), unavailableText: "Prices are back shortly.");

        Assert.Equal("Prices are back shortly.", model.UnavailableMessage);
    }

    /// <summary>
    /// With no app configured and none on the tag helper there is nothing to ask about: the
    /// unavailable panel, never a price.
    /// </summary>
    [Fact]
    public async Task No_app_id_anywhere_is_the_unavailable_panel()
    {
        var service = new StubCatalogService(Catalog());
        var result = await Component(service, configuredAppId: null).InvokeAsync();

        var view = Assert.IsType<ViewViewComponentResult>(result);
        var model = Assert.IsType<RegistrationSubscriptionPricingViewModel>(view.ViewData!.Model);

        Assert.True(model.IsUnavailable);
        Assert.Equal(0, service.Calls);
    }

    [Fact]
    public async Task The_configured_app_is_used_when_the_tag_helper_names_none()
    {
        var model = await InvokeAsync(configuredAppId: "configured-app");

        Assert.Equal("configured-app", model.AppId);
    }

    // ── Currency ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task The_catalogs_currency_is_what_the_surface_quotes()
    {
        var model = await InvokeAsync();

        Assert.Equal("USD", model.Currency);
    }

    [Fact]
    public async Task A_host_override_wins_over_the_catalogs_currency()
    {
        var model = await InvokeAsync(currency: "GBP");

        Assert.Equal("GBP", model.Currency);
    }

    // ── select-url ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task No_select_url_means_the_component_never_navigates()
    {
        var model = await InvokeAsync();

        Assert.False(model.SelectUrl.HasValue);
    }

    /// <summary>
    /// The host's own query survives; the three catalog keys are the visitor's to set, so a stale
    /// one in the template is dropped rather than left to win or duplicate.
    /// </summary>
    [Fact]
    public async Task A_select_url_keeps_the_hosts_query_and_drops_the_catalog_keys()
    {
        var model = await InvokeAsync(selectUrl: "/signup?ref=blog&tier=stale&addons=stale&utm=x#plans");

        Assert.True(model.SelectUrl.HasValue);
        Assert.Equal("/signup", model.SelectUrl.Path);
        Assert.Equal("ref=blog&utm=x", model.SelectUrl.Query);
        Assert.Equal("#plans", model.SelectUrl.Fragment);
    }

    [Fact]
    public void A_selection_is_stamped_onto_the_template_under_the_shared_query_keys()
    {
        var template = RegistrationSubscriptionPricingDecisions.ParseSelectUrl("/signup?ref=blog#plans");
        var selection = new PricingSelection
        {
            TierId = "tier-pro",
            PricingId = "price-annual",
            Billing = PricingBilling.Annual,
            AddOnIds = { "addon-a", "addon-b" }
        };

        Assert.Equal(
            "/signup?ref=blog&tier=tier-pro&pricing=price-annual&addons=addon-a%2Caddon-b#plans",
            RegistrationSubscriptionPricingDecisions.BuildSelectUrl(template, selection));
    }

    [Fact]
    public void A_template_with_no_query_gets_one()
    {
        var template = RegistrationSubscriptionPricingDecisions.ParseSelectUrl("/signup");
        var selection = new PricingSelection { TierId = "tier-pro" };

        Assert.Equal("/signup?tier=tier-pro", RegistrationSubscriptionPricingDecisions.BuildSelectUrl(template, selection));
    }

    [Fact]
    public void An_unset_template_builds_nothing()
    {
        Assert.False(RegistrationSubscriptionPricingDecisions.ParseSelectUrl(null).HasValue);
        Assert.Equal(
            string.Empty,
            RegistrationSubscriptionPricingDecisions.BuildSelectUrl(SelectUrlTemplate.None, new PricingSelection()));
    }

    // ── JSON-LD ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Json_ld_is_emitted_only_when_the_host_asks_for_it()
    {
        Assert.Equal(string.Empty, (await InvokeAsync()).JsonLd);
        Assert.NotEqual(string.Empty, (await InvokeAsync(includeJsonLd: true)).JsonLd);
    }

    [Fact]
    public async Task Json_ld_publishes_the_live_price_and_the_canonical_url()
    {
        var model = await InvokeAsync(includeJsonLd: true, jsonLdUrl: "https://example.test/pricing");

        Assert.Contains("\"@context\":\"https://schema.org\"", model.JsonLd);
        Assert.Contains("\"price\":\"10.00\"", model.JsonLd);
        Assert.Contains("\"priceCurrency\":\"USD\"", model.JsonLd);
        Assert.Contains("https://example.test/pricing", model.JsonLd);
    }

    /// <summary>
    /// The payload is written into a &lt;script&gt; with Html.Raw, so the default encoder is what
    /// stands between an operator-supplied name and a script injection. It escapes the opening
    /// angle bracket, so a plan named with a closing script tag cannot end the element.
    /// </summary>
    [Fact]
    public async Task A_hostile_plan_name_cannot_close_the_json_ld_script()
    {
        var hostile = Tier("tier-x", "</script><script>alert(1)</script>",
            options: [Option("p", 5m, "Monthly", isDefault: true)]);

        var model = await InvokeAsync(Catalog([hostile]), includeJsonLd: true);

        Assert.DoesNotContain("</script>", model.JsonLd, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<", model.JsonLd, StringComparison.Ordinal);
        Assert.Contains("\\u003C", model.JsonLd, StringComparison.Ordinal);
    }

    /// <summary>An unpriced plan is left out rather than published at zero.</summary>
    [Fact]
    public async Task Json_ld_never_invents_a_price()
    {
        var model = await InvokeAsync(Catalog([Tier("tier-ent", "Enterprise")]), includeJsonLd: true);

        Assert.Equal(string.Empty, model.JsonLd);
    }

    // ── The cached catalog is not the view model's to edit ──────────────────────

    /// <summary>
    /// The catalog is shared between every render inside the cache window, so a view model that
    /// sorted or filtered it in place would poison them all.
    /// </summary>
    [Fact]
    public async Task Building_the_view_model_never_touches_the_cached_catalog()
    {
        var catalog = Catalog(
            [Tier("tier-free", "Starter", isFreeTier: true), PaidTier()],
            [Pack("addon-a"), Pack("addon-b", order: 1)]);

        var tiersBefore = new List<AppTierModel>(catalog.Tiers);
        var addOnsBefore = new List<AppTierAddOnModel>(catalog.AddOns);

        var first = await InvokeAsync(catalog, showAddOns: true, offerFreeTierChoice: false);
        var second = await InvokeAsync(catalog, showAddOns: true);

        // Read twice, both answers correct, and the source untouched.
        Assert.Single(first.PlanCards);
        Assert.Equal(2, second.PlanCards.Count);
        Assert.Equal(tiersBefore, catalog.Tiers);
        Assert.Equal(addOnsBefore, catalog.AddOns);
        Assert.NotSame(catalog.Tiers, first.VisibleTiers);
        Assert.NotSame(catalog.AddOns, second.PackGroups[0].AddOns);
    }

    // ── The view compiles into the package ──────────────────────────────────────

    /// <summary>
    /// The views are compiled into the package by the Razor source generator, which writes no
    /// .g.cs to obj — so this is what proves the markup parses at build time rather than blowing up
    /// in a consuming app's first render.
    /// </summary>
    [Theory]
    [InlineData("/Views/Shared/Components/RegistrationSubscriptionPricing/Default.cshtml")]
    [InlineData("/Views/Shared/_RegSubPackCardBody.cshtml")]
    public void The_views_are_compiled_into_the_package(string identifier)
    {
        var items = typeof(RegistrationSubscriptionPricingViewModel).Assembly
            .GetCustomAttributes<RazorCompiledItemAttribute>();

        Assert.Contains(items, item => item.Identifier == identifier);
    }
}

/// <summary>
/// The 60-second cache in front of the public catalog. Two rules matter and both are the kind that
/// only shows up in production: a page of pricing surfaces must make ONE pair of requests, and a
/// FAILED load must never be remembered — otherwise one blip pins "pricing is unavailable right
/// now" on every visitor for a minute.
/// </summary>
public class WildwoodPublicCatalogServiceTests
{
    private const string TiersJson = """
        [{"id":"tier-pro","name":"Pro","status":"Active","currency":"USD",
          "pricingOptions":[{"id":"price-monthly","price":10,"billingFrequency":"Monthly","isDefault":true}]}]
        """;

    /// <summary>A second app's tiers, so a mixed-up cache entry is visible rather than plausible.</summary>
    private const string OtherTiersJson = """
        [{"id":"tier-other","name":"Other","status":"Active","currency":"USD",
          "pricingOptions":[{"id":"price-other","price":20,"billingFrequency":"Monthly","isDefault":true}]}]
        """;

    private const string AddOnsJson = """[]""";

    /// <summary>
    /// A handler the test steers between calls: the catalog's two GETs answer from whatever the
    /// closure says at that moment, so "fail, then succeed" is one object rather than two.
    /// </summary>
    private sealed class SteerableHandler : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode Status, string Json)> _answer;

        public SteerableHandler(Func<string, (HttpStatusCode, string)> answer) => _answer = answer;

        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            var (status, json) = _answer(request.RequestUri?.ToString() ?? string.Empty);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }

    private static WildwoodPublicCatalogService Create(SteerableHandler handler, IMemoryCache cache)
    {
        var appTiers = new WildwoodAppTierService(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.test/api/") },
            new FakeSessionManager(),
            NullLogger<WildwoodAppTierService>.Instance);

        return new WildwoodPublicCatalogService(appTiers, cache, NullLogger<WildwoodPublicCatalogService>.Instance);
    }

    private static (HttpStatusCode, string) Ok(string url)
    {
        return url.Contains("app-tier-addons", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, AddOnsJson)
            : (HttpStatusCode.OK, TiersJson);
    }

    [Fact]
    public void The_window_is_the_sixty_seconds_every_other_stack_keeps_a_catalog_for()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), WildwoodPublicCatalogService.CacheTtl);
    }

    [Fact]
    public async Task A_second_read_inside_the_window_costs_nothing()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new SteerableHandler(Ok);
        var service = Create(handler, cache);

        var first = await service.GetAsync("app-1");
        var second = await service.GetAsync("app-1");

        Assert.Same(first, second);
        Assert.Equal(2, handler.Calls); // the tiers GET and the add-ons GET, once between them
    }

    [Fact]
    public async Task A_forced_refresh_bypasses_the_window()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new SteerableHandler(Ok);
        var service = Create(handler, cache);

        await service.GetAsync("app-1");
        await service.GetAsync("app-1", forceRefresh: true);

        Assert.Equal(4, handler.Calls);
    }

    [Fact]
    public async Task Two_currencies_do_not_read_each_others_answer()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new SteerableHandler(Ok);
        var service = Create(handler, cache);

        await service.GetAsync("app-1", "USD");
        await service.GetAsync("app-1", "GBP");

        Assert.Equal(4, handler.Calls);
    }

    /// <summary>
    /// The key joins the app id and the currency override with a separator no id and no ISO code
    /// can contain. Joined with nothing — which is what the file looks like it says, because the
    /// separator is a character no editor renders — ("tenant1", "GBP") and ("tenant1GBP", null)
    /// collapse onto ONE key and one tenant is served the other's prices for the whole window.
    /// This is that ambiguity written as a test, so a reformat that eats the separator fails here
    /// rather than in production.
    /// </summary>
    [Fact]
    public async Task An_app_id_ending_in_a_currency_code_cannot_collide_with_that_currency()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new SteerableHandler(url =>
        {
            if (url.Contains("app-tier-addons", StringComparison.Ordinal)) return (HttpStatusCode.OK, AddOnsJson);
            return url.Contains("app-tiers/tenant1GBP/public", StringComparison.Ordinal)
                ? (HttpStatusCode.OK, OtherTiersJson)
                : (HttpStatusCode.OK, TiersJson);
        });
        var service = Create(handler, cache);

        var withOverride = await service.GetAsync("tenant1", "GBP");
        var otherApp = await service.GetAsync("tenant1GBP");

        // Two loads, not one answer served twice.
        Assert.Equal(4, handler.Calls);
        Assert.NotSame(withOverride, otherApp);
        Assert.Equal("tenant1", withOverride.AppId);
        Assert.Equal("tenant1GBP", otherApp.AppId);
        Assert.Equal("tier-pro", withOverride.Tiers[0].Id);
        Assert.Equal("tier-other", otherApp.Tiers[0].Id);

        // And the second read did not overwrite the first's entry: re-asking still gets tenant1's.
        var again = await service.GetAsync("tenant1", "GBP");
        Assert.Same(withOverride, again);
        Assert.Equal(4, handler.Calls);
    }

    /// <summary>
    /// The rule the Retry depends on. Retry reloads the page, which re-invokes the component; if
    /// the failure had been cached the reload would show the same panel for a minute.
    /// </summary>
    [Fact]
    public async Task A_failure_is_never_cached_so_the_next_render_really_does_retry()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var broken = true;
        var handler = new SteerableHandler(url =>
            broken ? (HttpStatusCode.ServiceUnavailable, "{}") : Ok(url));
        var service = Create(handler, cache);

        await Assert.ThrowsAnyAsync<Exception>(() => service.GetAsync("app-1"));

        broken = false;
        var catalog = await service.GetAsync("app-1");

        Assert.Single(catalog.Tiers);
        Assert.Equal("tier-pro", catalog.Tiers[0].Id);
    }

    [Fact]
    public async Task Invalidating_an_app_drops_every_currency_it_was_read_under()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new SteerableHandler(Ok);
        var service = Create(handler, cache);

        await service.GetAsync("app-1", "USD");
        await service.GetAsync("app-1", "GBP");
        service.Invalidate("app-1");
        await service.GetAsync("app-1", "USD");
        await service.GetAsync("app-1", "GBP");

        Assert.Equal(8, handler.Calls);
    }

    [Fact]
    public async Task An_empty_app_id_is_refused_rather_than_looked_up()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new SteerableHandler(Ok);
        var service = Create(handler, cache);

        await Assert.ThrowsAsync<ArgumentException>(() => service.GetAsync(string.Empty));
        Assert.Equal(0, handler.Calls);
    }
}
