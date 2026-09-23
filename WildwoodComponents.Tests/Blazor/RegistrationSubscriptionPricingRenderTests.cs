using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Blazor;

// BL0005: setting a component parameter from outside the component is exactly how a component is
// put into a given state here - there is no renderer to pass a ParameterView through. Scoped to
// this file, so the advisory still applies to every component that ships.
#pragma warning disable BL0005

/// <summary>
/// What the pricing view and its parts actually put on the page.
/// </summary>
/// <remarks>
/// <para>
/// There is no bUnit renderer in this repo, so the compiled markup is inspected the way
/// <see cref="AddOnsPanelRenderTests"/> and <see cref="TierCardRenderTests"/> do:
/// <c>BuildRenderTree</c> on a bare instance reads initialised fields only, so no renderer, DI or
/// lifecycle is needed. A child component appears as a component FRAME rather than its markup,
/// which is why each part is also rendered on its own below.
/// </para>
/// <para>
/// Every price assertion goes through <see cref="FormatHelpers.FormatMoney"/> against a figure the
/// fixture carries, never a string typed into the assertion.
/// </para>
/// </remarks>
public class RegistrationSubscriptionPricingRenderTests
{
    private const decimal ProMonthly = 79m;
    private const decimal ProAnnual = 790m;
    private const decimal DocsPackPrice = 9m;

    #region Fixtures

    private static AppTierModel ProTier()
    {
        return new AppTierModel
        {
            Id = "tier-pro",
            Name = "Pro",
            Status = "Active",
            DisplayOrder = 1,
            Currency = "USD",
            PricingOptions =
            {
                new AppTierPricingModel
                {
                    Id = "price-pro-monthly", Price = ProMonthly, BillingFrequency = "Monthly",
                    IsDefault = true, DisplayOrder = 1, TrialDays = 14
                },
                new AppTierPricingModel
                {
                    Id = "price-pro-annual", Price = ProAnnual, BillingFrequency = "Yearly", DisplayOrder = 2
                }
            }
        };
    }

    private static AppTierModel EnterpriseTier()
    {
        return new AppTierModel { Id = "tier-ent", Name = "Enterprise", Status = "Active", DisplayOrder = 2 };
    }

    /// <summary>
    /// A plan the operator pointed at their own contact page. It sells no pricing option either, so
    /// it is enterprise-shaped too — which is exactly what makes it pin the footer's priority order.
    /// </summary>
    private static AppTierModel ContactTier()
    {
        return new AppTierModel
        {
            Id = "tier-contact",
            Name = "Concierge",
            Status = "Active",
            DisplayOrder = 3,
            ShowContactButton = true,
            ContactButtonUrl = "https://example.test/sales"
        };
    }

    private static AppTierModel FreeTier()
    {
        return new AppTierModel
        {
            Id = "tier-free", Name = "Starter", Status = "Active", DisplayOrder = 0, IsFreeTier = true
        };
    }

    private static AppTierAddOnModel DocsPack()
    {
        return new AppTierAddOnModel
        {
            Id = "pack-docs",
            Name = "Docs Pack",
            Category = "Documents",
            Description = "Upload and search documents.",
            Status = "Active",
            DisplayOrder = 1,
            Currency = "USD",
            PricingOptions =
            {
                new AppTierAddOnPricingModel
                {
                    Id = "ao-docs", Price = DocsPackPrice, BillingFrequency = "Monthly",
                    IsDefault = true, TrialDays = 14
                }
            }
        };
    }

    private static AppTierAddOnModel LoosePack()
    {
        return new AppTierAddOnModel
        {
            Id = "pack-loose", Name = "Loose Pack", Category = "Misc", Status = "Active", DisplayOrder = 2
        };
    }

    private static PublicCatalog Catalog()
    {
        return CatalogHelpers.BuildPublicCatalog(
            "app-1",
            new List<AppTierModel> { ProTier(), EnterpriseTier() },
            new List<AppTierAddOnModel> { DocsPack(), LoosePack() });
    }

    #endregion

    #region Reflection + render helpers

    private static void SetField(object component, string name, object? value)
    {
        component.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(component, value);
    }

    // BL0006: reading RenderTree frames is exactly what these tests are for - the components have
    // no bUnit renderer, so the compiled markup is the only thing there is to assert against.
#pragma warning disable BL0006
    private static ArrayRange<RenderTreeFrame> Frames(object component)
    {
        var builder = new RenderTreeBuilder();
        component.GetType()
            .GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(component, new object[] { builder });

        return builder.GetFrames();
    }

    /// <summary>Only what a reader would see: text and raw markup, no attribute values.</summary>
    private static string RenderText(object component)
    {
        var frames = Frames(component);
        var text = new StringBuilder();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Text) text.Append(frame.TextContent).Append(' ');
            else if (frame.FrameType == RenderTreeFrameType.Markup) text.Append(frame.MarkupContent).Append(' ');
        }

        return text.ToString();
    }

    /// <summary>Text, markup and every attribute as <c>name="value"</c>, for the test hooks.</summary>
    private static string RenderAll(object component)
    {
        var frames = Frames(component);
        var text = new StringBuilder();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            switch (frame.FrameType)
            {
                case RenderTreeFrameType.Text:
                    text.Append(frame.TextContent).Append(' ');
                    break;
                case RenderTreeFrameType.Markup:
                    text.Append(frame.MarkupContent).Append(' ');
                    break;
                case RenderTreeFrameType.Attribute:
                    text.Append(frame.AttributeName)
                        .Append("=\"")
                        .Append(frame.AttributeValue as string ?? string.Empty)
                        .Append("\" ");
                    break;
            }
        }

        return text.ToString();
    }

    /// <summary>The child components the markup mounted, by type.</summary>
    private static List<Type> ChildComponents(object component)
    {
        var frames = Frames(component);
        var types = new List<Type>();

        for (var i = 0; i < frames.Count; i++)
        {
            var frame = frames.Array[i];
            if (frame.FrameType == RenderTreeFrameType.Component) types.Add(frame.ComponentType);
        }

        return types;
    }
#pragma warning restore BL0006

    private static RegistrationSubscriptionPricing View(
        PublicCatalog? catalog = null,
        bool loading = false,
        string? error = null,
        bool showPlans = true,
        bool showAddOns = false,
        PublicCatalog? preloaded = null,
        bool includeJsonLd = false)
    {
        var view = new RegistrationSubscriptionPricing
        {
            AppId = "app-1",
            ShowPlans = showPlans,
            ShowAddOns = showAddOns,
            PreloadedCatalog = preloaded,
            IncludeJsonLd = includeJsonLd
        };

        SetField(view, "_catalog", catalog);
        SetField(view, "_catalogLoading", loading);
        SetField(view, "_catalogError", error);

        return view;
    }

    #endregion

    #region The view's own hooks

    [Fact]
    public void The_root_carries_the_view_hook_and_the_cross_stack_classes()
    {
        var markup = RenderAll(View(Catalog()));

        Assert.Contains("data-ww-view=\"pricing\"", markup);
        Assert.Contains("ww-regsub ww-regsub-pricing", markup);
    }

    /// <summary>
    /// A placeholder that guesses a price shows a price the server never quoted, so there is not
    /// one digit in the loading body.
    /// </summary>
    [Fact]
    public void While_the_catalog_loads_nothing_on_the_page_is_a_number()
    {
        var view = View(catalog: null, loading: true);

        var text = RenderText(view);

        Assert.DoesNotContain(ChildComponents(view), type => type == typeof(PlanGrid));
        Assert.Contains(typeof(PricingSkeleton), ChildComponents(view));
        foreach (var character in text)
        {
            Assert.False(char.IsDigit(character), $"the loading body showed a digit: {text}");
        }
    }

    [Fact]
    public void The_skeleton_is_a_status_with_no_numbers_in_it()
    {
        var skeleton = new PricingSkeleton { Label = RegistrationSubscriptionLabels.Defaults.LoadingPlans };

        var markup = RenderAll(skeleton);

        Assert.Contains("ww-pricing-skeleton", markup);
        Assert.Contains("role=\"status\"", markup);
        Assert.Contains("aria-busy=\"true\"", markup);
        Assert.Contains("Loading plans...", markup);
        foreach (var character in RenderText(skeleton))
        {
            Assert.False(char.IsDigit(character), "the skeleton showed a digit");
        }
    }

    [Fact]
    public void A_host_loading_fallback_replaces_the_skeleton()
    {
        var view = View(catalog: null, loading: true);
        view.LoadingFallback = builder => builder.AddContent(0, "Hold on");

        Assert.Contains("Hold on", RenderText(view));
        Assert.DoesNotContain(typeof(PricingSkeleton), ChildComponents(view));
    }

    /// <summary>
    /// When the catalog cannot be read the prices go away with it: the panel says so, offers a
    /// Retry, and shows no figure at all.
    /// </summary>
    [Fact]
    public void An_unreadable_catalog_says_so_and_offers_a_retry_with_no_price_in_sight()
    {
        var markup = RenderAll(View(Catalog(), error: "catalog exploded"));

        Assert.Contains("Pricing is unavailable right now", markup);
        Assert.Contains("Retry", markup);
        Assert.Contains("ww-regsub-unavailable", markup);
        Assert.Contains("role=\"alert\"", markup);
        Assert.DoesNotContain(FormatHelpers.FormatMoney(ProMonthly, "USD"), markup);
        Assert.Empty(ChildComponents(View(Catalog(), error: "catalog exploded")));
    }

    [Fact]
    public void A_host_error_fallback_replaces_the_unavailable_panel()
    {
        var view = View(Catalog(), error: "catalog exploded");
        view.ErrorFallback = builder => builder.AddContent(0, "Ask us for a quote");

        var text = RenderText(view);

        Assert.Contains("Ask us for a quote", text);
        Assert.DoesNotContain("Pricing is unavailable right now", text);
    }

    [Fact]
    public void A_preloaded_catalog_renders_the_plans_with_no_load_at_all()
    {
        // No _catalog field is set: everything on screen comes from the host's snapshot, and the
        // view asks the service for nothing (PricingViewDecisions.ShouldLoadCatalog says so).
        var view = View(catalog: null, loading: true, preloaded: Catalog());

        Assert.Contains(typeof(PlanGrid), ChildComponents(view));
        Assert.DoesNotContain(typeof(PricingSkeleton), ChildComponents(view));
        Assert.False(PricingViewDecisions.ShouldLoadCatalog(Catalog(), "app-1"));
    }

    [Fact]
    public void Packs_render_alone_when_the_host_turns_the_plans_off()
    {
        var children = ChildComponents(View(Catalog(), showPlans: false, showAddOns: true));

        Assert.Contains(typeof(PackGrid), children);
        Assert.DoesNotContain(typeof(PlanGrid), children);
    }

    [Fact]
    public void No_packs_are_shown_unless_the_host_asks_for_them()
    {
        var children = ChildComponents(View(Catalog()));

        Assert.Contains(typeof(PlanGrid), children);
        Assert.DoesNotContain(typeof(PackGrid), children);
    }

    #endregion

    #region The plan grid

    [Fact]
    public void The_plan_grid_quotes_the_monthly_price_the_server_just_sent()
    {
        var grid = new PlanGrid { Tiers = new List<AppTierModel> { ProTier() }, Currency = "USD" };

        var markup = RenderAll(grid);

        Assert.Contains(FormatHelpers.FormatMoney(ProMonthly, "USD"), markup);
        Assert.DoesNotContain(FormatHelpers.FormatMoney(ProAnnual, "USD"), markup);
        Assert.Contains("ww-tier-grid", markup);
        Assert.Contains("14-day free trial", markup);
        Assert.Contains("ww-plan-trial", markup);
    }

    [Fact]
    public void The_plan_grid_quotes_the_annual_price_when_the_toggle_is_on_annual()
    {
        var grid = new PlanGrid
        {
            Tiers = new List<AppTierModel> { ProTier() },
            Currency = "USD",
            Billing = PricingBilling.Annual
        };

        var markup = RenderAll(grid);

        Assert.Contains(FormatHelpers.FormatMoney(ProAnnual, "USD"), markup);
        Assert.DoesNotContain(FormatHelpers.FormatMoney(ProMonthly, "USD"), markup);

        var saving = PricingViewDecisions.AnnualDiscount(ProTier());
        Assert.Contains($"Save up to {saving}%", markup);
        Assert.Contains("Toggle annual billing", markup);
    }

    [Fact]
    public void The_billing_toggle_is_hidden_when_the_host_turns_it_off()
    {
        var grid = new PlanGrid
        {
            Tiers = new List<AppTierModel> { ProTier() },
            Currency = "USD",
            ShowBillingToggle = false
        };

        Assert.DoesNotContain("Toggle annual billing", RenderAll(grid));
    }

    [Fact]
    public void The_highlighted_plan_is_marked_and_says_so()
    {
        var grid = new PlanGrid
        {
            Tiers = new List<AppTierModel> { ProTier() },
            Currency = "USD",
            HighlightTierId = "TIER-PRO"
        };

        var markup = RenderAll(grid);

        Assert.Contains("ww-tier-preselected", markup);
        Assert.Contains("Your Selection", markup);
        Assert.Contains("Continue with This Plan", markup);
    }

    [Fact]
    public void A_contact_us_plan_links_to_the_hosts_contact_url_instead_of_a_price()
    {
        var grid = new PlanGrid
        {
            Tiers = new List<AppTierModel> { EnterpriseTier() },
            Currency = "USD",
            ContactUrl = "/contact?plan=enterprise"
        };

        var markup = RenderAll(grid);

        Assert.Contains("href=\"/contact?plan=enterprise\"", markup);
        Assert.Contains("Contact Sales", markup);
        Assert.Contains("Custom", markup);
    }

    /// <summary>
    /// The footer's wording belongs to the tier card, not to the label set. React's
    /// <c>TierCardFooter</c> hard-codes "Contact Us" for a plan's own contact button, and
    /// <c>Labels.ContactUs</c> ("Contact us") is a DIFFERENT string used elsewhere — so a host that
    /// renames the label must not rename the button, which live end-to-end suites locate by text.
    /// This also pins the priority order: the plan's own button wins over the enterprise fallback,
    /// which is live here too.
    /// </summary>
    [Fact]
    public void A_plans_own_contact_button_says_Contact_Us_whatever_the_host_renames_the_label_to()
    {
        var grid = new PlanGrid
        {
            Tiers = new List<AppTierModel> { ContactTier() },
            Currency = "USD",
            ContactUrl = "/contact?plan=enterprise",
            Labels = new RegistrationSubscriptionLabels { ContactUs = "Talk to a robot" }
        };

        var markup = RenderAll(grid);

        Assert.Contains("Contact Us", markup);
        Assert.DoesNotContain("Talk to a robot", markup);
        Assert.DoesNotContain("Contact Sales", markup);
        Assert.Contains("href=\"https://example.test/sales\"", markup);
        Assert.Contains("target=\"_blank\"", markup);
        Assert.Contains("rel=\"noopener noreferrer\"", markup);
    }

    /// <summary>
    /// The enterprise fallback's own wording, and the rule for the link it renders: a URL that
    /// leaves the app opens in a new tab with <c>noopener noreferrer</c>, and one that does not gets
    /// neither attribute.
    /// </summary>
    [Fact]
    public void The_enterprise_fallback_says_Contact_Sales_and_only_a_foreign_link_leaves_the_tab()
    {
        var foreign = RenderAll(new PlanGrid
        {
            Tiers = new List<AppTierModel> { EnterpriseTier() },
            Currency = "USD",
            ContactUrl = "https://example.test/enterprise"
        });

        Assert.Contains("Contact Sales", foreign);
        Assert.DoesNotContain("Contact Us", foreign);
        Assert.Contains("href=\"https://example.test/enterprise\"", foreign);
        Assert.Contains("target=\"_blank\"", foreign);
        Assert.Contains("rel=\"noopener noreferrer\"", foreign);

        var local = RenderAll(new PlanGrid
        {
            Tiers = new List<AppTierModel> { EnterpriseTier() },
            Currency = "USD",
            ContactUrl = "/contact?plan=enterprise"
        });

        Assert.Contains("Contact Sales", local);
        Assert.DoesNotContain("target=\"_blank\"", local);
        Assert.DoesNotContain("noopener", local);
    }

    /// <summary>An enterprise plan with nowhere to point still asks for the conversation.</summary>
    [Fact]
    public void An_enterprise_plan_with_no_contact_url_offers_Contact_Sales_as_a_button()
    {
        var markup = RenderAll(new PlanGrid
        {
            Tiers = new List<AppTierModel> { EnterpriseTier() }, Currency = "USD"
        });

        Assert.Contains("Contact Sales", markup);
        Assert.DoesNotContain("href=", markup);
    }

    /// <summary>
    /// "Free" and "Get Started" are the tier card's words too — <c>Labels.PlanFree</c> is what the
    /// plan SUMMARY card says where a price would go, and renaming it must not reprice the grid.
    /// </summary>
    [Fact]
    public void A_free_plan_prices_itself_Free_and_starts_rather_than_subscribing()
    {
        var grid = new PlanGrid
        {
            Tiers = new List<AppTierModel> { FreeTier() },
            Currency = "USD",
            Labels = new RegistrationSubscriptionLabels { PlanFree = "Gratis" }
        };

        var markup = RenderAll(grid);

        Assert.Contains("Free", markup);
        Assert.DoesNotContain("Gratis", markup);
        Assert.Contains("Get Started", markup);
        Assert.DoesNotContain("Subscribe", markup);
    }

    /// <summary>
    /// Features and limits read the way the shared tier card renders them: a tick or a cross beside
    /// every feature, and an unlimited allowance spelled out rather than shown as -1.
    /// </summary>
    [Fact]
    public void A_plans_features_and_limits_render_the_way_the_shared_tier_card_does()
    {
        var tier = ProTier();
        tier.Features.Add(new AppTierFeatureModel
        {
            Id = "f-docs", FeatureCode = "DOCUMENTS", DisplayName = "Documents", IsEnabled = true
        });
        tier.Features.Add(new AppTierFeatureModel
        {
            Id = "f-sso", FeatureCode = "SSO", DisplayName = "Single sign-on", IsEnabled = false
        });
        tier.Limits.Add(new AppTierLimitModel
        {
            Id = "l-seats", LimitCode = "SEATS", DisplayName = "Seats", MaxValue = -1
        });

        var markup = RenderAll(new PlanGrid { Tiers = new List<AppTierModel> { tier }, Currency = "USD" });

        Assert.Contains("ww-tier-feature-check", markup);
        Assert.Contains("ww-tier-feature-x", markup);
        Assert.Contains("ww-tier-feature-disabled", markup);
        Assert.Contains("Single sign-on", markup);
        Assert.Contains("Unlimited", markup);
        Assert.Contains("Seats", markup);
    }

    #endregion

    #region The pack grid

    [Fact]
    public void A_pack_card_prices_itself_off_the_catalog()
    {
        var card = new PackCardBody { AddOn = DocsPack(), Currency = "USD" };

        var markup = RenderAll(card);

        Assert.Contains(FormatMoneyWithPeriod(DocsPackPrice, "USD", "/mo"), markup);
        Assert.Contains("14-day free trial", markup);
        Assert.Contains("Upload and search documents.", markup);
    }

    /// <summary>
    /// No icon comes off the catalog: React's pack card renders only what its <c>describeAddOn</c>
    /// callback supplies, so honouring the add-on's <c>IconClass</c> here would put a glyph on the
    /// Blazor card that the React card does not have.
    /// </summary>
    [Fact]
    public void A_pack_card_renders_no_icon_from_the_catalog()
    {
        var pack = DocsPack();
        pack.IconClass = "bi bi-file-earmark";

        var markup = RenderAll(new PackCardBody { AddOn = pack, Currency = "USD" });

        Assert.DoesNotContain("ww-pack-card-icon", markup);
        Assert.DoesNotContain("bi-file-earmark", markup);
        Assert.Contains("Docs Pack", markup);
    }

    [Fact]
    public void An_unpriced_pack_card_says_it_is_not_available_yet()
    {
        var card = new PackCardBody { AddOn = LoosePack(), Currency = "USD" };

        var markup = RenderAll(card);

        Assert.Contains("Not yet available", markup);
        Assert.Contains("ww-pack-card-unavailable", markup);
    }

    [Fact]
    public void Each_pack_carries_its_own_test_hook_and_a_named_call_to_action()
    {
        var grid = new PackGrid { AddOns = Catalog().AddOns, Currency = "USD" };

        var markup = RenderAll(grid);

        Assert.Contains("data-ww-pack=\"pack-docs\"", markup);
        Assert.Contains("data-ww-group=\"all\"", markup);
        Assert.Contains("aria-label=\"Select Docs Pack\"", markup);
        Assert.Contains("Select", markup);
    }

    [Fact]
    public void Grouped_packs_render_under_their_headings_with_the_strays_kept()
    {
        var grid = new PackGrid
        {
            AddOns = Catalog().AddOns,
            Currency = "USD",
            AddOnGroups = new List<AddOnGroup>
            {
                new AddOnGroup { Id = "core", Title = "Core packs", Categories = { "Documents" } },
                new AddOnGroup { Id = "empty", Title = "Nothing here", Categories = { "Nope" } }
            }
        };

        var markup = RenderAll(grid);

        Assert.Contains("data-ww-group=\"core\"", markup);
        Assert.Contains("Core packs", markup);
        Assert.Contains("data-ww-group=\"more\"", markup);
        Assert.Contains("More packs", markup);
        Assert.DoesNotContain("Nothing here", markup);
    }

    [Fact]
    public void A_multi_select_card_is_a_toggle_and_the_summary_counts_the_basket()
    {
        var grid = new PackGrid
        {
            AddOns = Catalog().AddOns,
            Currency = "USD",
            Selection = PricingPackSelection.Multi,
            SelectedIds = new List<string> { "pack-docs" }
        };

        var markup = RenderAll(grid);

        Assert.Contains("aria-pressed=\"true\"", markup);
        Assert.Contains("aria-pressed=\"false\"", markup);
        Assert.Contains("ww-pack-card--selected", markup);
        Assert.Contains("Continue with 1 pack", markup);
        Assert.Contains("ww-regsub-summary", markup);
    }

    [Fact]
    public void An_empty_basket_offers_nothing_to_continue_with()
    {
        var grid = new PackGrid
        {
            AddOns = Catalog().AddOns,
            Currency = "USD",
            Selection = PricingPackSelection.Multi
        };

        Assert.Contains("Continue with 0 packs", RenderAll(grid));
    }

    [Fact]
    public void An_app_with_no_packs_renders_no_grid_at_all()
    {
        var grid = new PackGrid { AddOns = new List<AppTierAddOnModel>(), Currency = "USD" };

        Assert.DoesNotContain("ww-pack-grid", RenderAll(grid));
    }

    #endregion

    #region JSON-LD

    [Fact]
    public void The_offers_carry_the_live_prices_and_the_canonical_url()
    {
        var jsonLd = new CatalogJsonLd { Catalog = Catalog(), Url = "https://example.test/pricing" };

        var payload = RenderText(jsonLd).Trim();
        using var document = JsonDocument.Parse(payload);

        Assert.Equal("https://schema.org", document.RootElement.GetProperty("@context").GetString());
        var graph = document.RootElement.GetProperty("@graph");

        var names = new List<string>();
        string? proPrice = null;
        string? proUrl = null;
        string? proCurrency = null;
        foreach (var offer in graph.EnumerateArray())
        {
            var name = offer.GetProperty("name").GetString()!;
            names.Add(name);
            if (name != "Pro") continue;

            proPrice = offer.GetProperty("price").GetString();
            proUrl = offer.GetProperty("url").GetString();
            proCurrency = offer.GetProperty("priceCurrency").GetString();
        }

        Assert.Equal(ProMonthly.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), proPrice);
        Assert.Equal("https://example.test/pricing", proUrl);
        Assert.Equal("USD", proCurrency);
        Assert.Contains("Docs Pack", names);
        // The enterprise plan carries no price, so it is left out rather than published at zero.
        Assert.DoesNotContain("Enterprise", names);
        // ... and neither is the unpriced pack.
        Assert.DoesNotContain("Loose Pack", names);
    }

    /// <summary>
    /// A pack the operator named with a closing script tag must not be able to close the element
    /// and run as markup: the serializer escapes the angle brackets, and the payload still parses
    /// back to the name the operator typed.
    /// </summary>
    [Fact]
    public void A_pack_name_cannot_break_out_of_the_script_element()
    {
        var hostile = DocsPack();
        hostile.Name = "</script><img src=x onerror=alert(1)>";
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1", new List<AppTierModel>(), new List<AppTierAddOnModel> { hostile });

        var payload = RenderText(new CatalogJsonLd { Catalog = catalog }).Trim();

        Assert.DoesNotContain("<", payload);
        Assert.DoesNotContain(">", payload);

        using var document = JsonDocument.Parse(payload);
        var offer = document.RootElement.GetProperty("@graph")[0];
        Assert.Equal(hostile.Name, offer.GetProperty("name").GetString());
    }

    [Fact]
    public void Nothing_is_emitted_when_the_catalog_prices_nothing()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog(
            "app-1", new List<AppTierModel> { EnterpriseTier() }, new List<AppTierAddOnModel>());

        Assert.Equal(string.Empty, RenderText(new CatalogJsonLd { Catalog = catalog }).Trim());
        Assert.Equal(string.Empty, RenderText(new CatalogJsonLd()).Trim());
    }

    [Fact]
    public void The_view_emits_no_structured_data_unless_the_host_asks_for_it()
    {
        Assert.DoesNotContain(typeof(CatalogJsonLd), ChildComponents(View(Catalog())));
        Assert.Contains(typeof(CatalogJsonLd), ChildComponents(View(Catalog(), includeJsonLd: true)));
    }

    #endregion

    private static string FormatMoneyWithPeriod(decimal amount, string currency, string period)
    {
        return FormatHelpers.FormatMoney(amount, currency) + period;
    }
}

#pragma warning restore BL0005
