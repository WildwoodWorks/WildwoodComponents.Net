using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The rules the pricing view renders from, ported from the React view and its react-native
/// view-model (<c>views/pricingViewModel.ts</c>).
/// </summary>
/// <remarks>
/// Every price assertion here goes through <see cref="FormatHelpers.FormatMoney"/> against a
/// figure the fixture catalog carries, never against a string typed into an assertion — a test
/// that hard-coded "$79.00" would pass just as happily against a component that hard-coded it
/// too, which is the one failure the whole dynamic-pricing rule exists to prevent.
/// </remarks>
public class PricingViewDecisionsTests
{
    #region Fixtures

    private const decimal ProMonthly = 79m;
    private const decimal ProAnnual = 790m;
    private const decimal DocsPackPrice = 9m;
    private const decimal AiPackPrice = 19m;

    private static readonly RegistrationSubscriptionLabels Labels = RegistrationSubscriptionLabels.Defaults;

    private static AppTierModel FreeTier()
    {
        return new AppTierModel
        {
            Id = "tier-free",
            Name = "Starter",
            Status = "Active",
            DisplayOrder = 1,
            IsFreeTier = true,
            Currency = "USD",
            PricingOptions =
            {
                new AppTierPricingModel { Id = "price-free", Price = 0m, BillingFrequency = "Monthly", IsDefault = true }
            }
        };
    }

    private static AppTierModel ProTier()
    {
        return new AppTierModel
        {
            Id = "tier-pro",
            Name = "Pro",
            Status = "Active",
            DisplayOrder = 2,
            Currency = "USD",
            PricingOptions =
            {
                new AppTierPricingModel
                {
                    Id = "price-pro-monthly", Price = ProMonthly, BillingFrequency = "Monthly",
                    IsDefault = true, DisplayOrder = 1
                },
                new AppTierPricingModel
                {
                    Id = "price-pro-annual", Price = ProAnnual, BillingFrequency = "Yearly", DisplayOrder = 2
                }
            }
        };
    }

    /// <summary>No pricing options at all: the platform's definition of a "contact us" plan.</summary>
    private static AppTierModel EnterpriseTier()
    {
        return new AppTierModel { Id = "tier-ent", Name = "Enterprise", Status = "Active", DisplayOrder = 3 };
    }

    private static AppTierAddOnModel DocsPack()
    {
        return new AppTierAddOnModel
        {
            Id = "pack-docs",
            Name = "Docs Pack",
            Category = "Documents",
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

    private static AppTierAddOnModel AiPack()
    {
        return new AppTierAddOnModel
        {
            Id = "pack-ai",
            Name = "AI Pack",
            Category = "AI",
            Status = "Active",
            DisplayOrder = 2,
            Currency = "USD",
            PricingOptions =
            {
                new AppTierAddOnPricingModel
                {
                    Id = "ao-ai", Price = AiPackPrice, BillingFrequency = "Monthly", IsDefault = true
                }
            }
        };
    }

    /// <summary>Defined in WildwoodAdmin, never priced, in a category no host group claims.</summary>
    private static AppTierAddOnModel LoosePack()
    {
        return new AppTierAddOnModel
        {
            Id = "pack-loose", Name = "Loose Pack", Category = "Misc", Status = "Active", DisplayOrder = 3
        };
    }

    private static PublicCatalog Catalog()
    {
        return CatalogHelpers.BuildPublicCatalog(
            "app-1",
            new List<AppTierModel> { FreeTier(), ProTier(), EnterpriseTier() },
            new List<AppTierAddOnModel> { DocsPack(), AiPack(), LoosePack() });
    }

    private static List<AddOnGroup> Groups()
    {
        return new List<AddOnGroup>
        {
            new AddOnGroup
            {
                Id = "core",
                Title = "Core packs",
                Blurb = "The ones most teams start with.",
                Categories = { "Documents", "AI" }
            },
            new AddOnGroup { Id = "empty", Title = "Nothing here", Categories = { "Nope" } }
        };
    }

    #endregion

    #region Body kind

    [Fact]
    public void While_the_catalog_loads_the_body_is_the_placeholder()
    {
        Assert.Equal(
            PricingBodyKind.Loading,
            PricingViewDecisions.BodyKind(null, true, null, true, false, true));
    }

    [Fact]
    public void An_unreadable_catalog_is_an_error_even_when_one_is_already_on_screen()
    {
        // A failure wins over a catalog in hand: the prices go away with the answer that produced
        // them, rather than standing there as a quote nobody can vouch for any more.
        Assert.Equal(
            PricingBodyKind.Error,
            PricingViewDecisions.BodyKind(Catalog(), false, "catalog exploded", true, false, true));
    }

    [Fact]
    public void A_finished_load_with_no_catalog_is_an_error_not_a_placeholder()
    {
        Assert.Equal(
            PricingBodyKind.Error,
            PricingViewDecisions.BodyKind(null, false, null, true, false, true));
    }

    [Fact]
    public void A_live_catalog_renders_content()
    {
        Assert.Equal(
            PricingBodyKind.Content,
            PricingViewDecisions.BodyKind(Catalog(), false, null, true, false, true));
    }

    [Fact]
    public void Packs_alone_are_content_when_the_host_turns_the_plans_off()
    {
        Assert.Equal(
            PricingBodyKind.Content,
            PricingViewDecisions.BodyKind(Catalog(), false, null, false, true, true));
    }

    [Fact]
    public void A_catalog_with_nothing_the_host_asked_for_renders_nothing()
    {
        Assert.Equal(
            PricingBodyKind.Empty,
            PricingViewDecisions.BodyKind(Catalog(), false, null, false, false, true));
    }

    [Fact]
    public void An_app_that_sells_no_plans_is_empty_rather_than_unavailable()
    {
        var catalog = CatalogHelpers.BuildPublicCatalog("app-1");

        Assert.Equal(
            PricingBodyKind.Empty,
            PricingViewDecisions.BodyKind(catalog, false, null, true, true, true));
    }

    #endregion

    #region Loading and error reporting

    [Fact]
    public void A_preloaded_catalog_is_rendered_without_asking_the_server()
    {
        Assert.False(PricingViewDecisions.ShouldLoadCatalog(Catalog(), "app-1"));
        Assert.True(PricingViewDecisions.ShouldLoadCatalog(null, "app-1"));
        Assert.False(PricingViewDecisions.ShouldLoadCatalog(null, string.Empty));
    }

    /// <summary>
    /// One report per distinct failure: a Retry that fails again is news, a re-render is not.
    /// </summary>
    [Fact]
    public void A_failure_is_reported_once_and_a_new_one_is_reported_again()
    {
        Assert.True(PricingViewDecisions.ShouldReportError(null, "catalog exploded"));
        Assert.False(PricingViewDecisions.ShouldReportError("catalog exploded", "catalog exploded"));
        Assert.True(PricingViewDecisions.ShouldReportError("catalog exploded", "still exploded"));
        Assert.False(PricingViewDecisions.ShouldReportError("catalog exploded", null));
    }

    [Fact]
    public void The_error_code_is_the_one_every_stack_reports()
    {
        Assert.Equal("catalog_unavailable", PricingViewDecisions.CatalogErrorCode);
    }

    #endregion

    #region Plans

    [Fact]
    public void The_server_names_the_currency_and_the_parameter_overrides_it()
    {
        var catalog = Catalog();

        Assert.Equal("USD", PricingViewDecisions.ResolveDisplayCurrency(null, catalog));
        Assert.Equal("GBP", PricingViewDecisions.ResolveDisplayCurrency("GBP", catalog));
        Assert.Equal(string.Empty, PricingViewDecisions.ResolveDisplayCurrency(null, null));
    }

    [Fact]
    public void Free_plans_are_hidden_when_the_host_does_not_offer_that_choice()
    {
        var catalog = Catalog();

        Assert.Equal(3, PricingViewDecisions.VisibleTiers(catalog, true).Count);

        var paidOnly = PricingViewDecisions.VisibleTiers(catalog, false);
        Assert.Equal(2, paidOnly.Count);
        foreach (var tier in paidOnly) Assert.NotEqual("tier-free", tier.Id);
    }

    [Fact]
    public void The_highlighted_plan_is_matched_case_insensitively()
    {
        Assert.True(PricingViewDecisions.IsHighlightedTier("tier-pro", "TIER-PRO"));
        Assert.False(PricingViewDecisions.IsHighlightedTier("tier-free", "tier-pro"));
        Assert.False(PricingViewDecisions.IsHighlightedTier("tier-pro", null));
    }

    /// <summary>
    /// The live price at the quoted amount per billing period: the toggle picks the option, and
    /// the option carries the figure the server just sent.
    /// </summary>
    [Fact]
    public void The_billing_toggle_picks_the_option_the_server_priced()
    {
        var pro = ProTier();

        var monthly = PricingViewDecisions.PlanPriceOption(pro, PricingBilling.Monthly);
        Assert.NotNull(monthly);
        Assert.Equal("price-pro-monthly", monthly!.Id);
        Assert.Equal(FormatHelpers.FormatMoney(ProMonthly, "USD"), CatalogHelpers.FormatPrice(pro, monthly.Price, "USD"));

        var annual = PricingViewDecisions.PlanPriceOption(pro, PricingBilling.Annual);
        Assert.NotNull(annual);
        Assert.Equal("price-pro-annual", annual!.Id);
        Assert.Equal(FormatHelpers.FormatMoney(ProAnnual, "USD"), CatalogHelpers.FormatPrice(pro, annual.Price, "USD"));
    }

    [Fact]
    public void A_plan_priced_in_its_own_currency_is_quoted_in_it()
    {
        var pro = ProTier();
        pro.Currency = "CHF";
        var pricing = PricingViewDecisions.PlanPriceOption(pro, PricingBilling.Monthly)!;

        var text = CatalogHelpers.FormatPrice(pro, pricing.Price, "USD");

        Assert.Equal(FormatHelpers.FormatMoney(ProMonthly, "CHF"), text);
        Assert.DoesNotContain("$", text);
    }

    [Fact]
    public void The_billing_cycle_travels_as_the_word_every_stack_uses()
    {
        Assert.Equal("monthly", PricingViewDecisions.BillingCode(PricingBilling.Monthly));
        Assert.Equal("annual", PricingViewDecisions.BillingCode(PricingBilling.Annual));
    }

    [Fact]
    public void A_plan_with_no_pricing_option_at_all_is_the_contact_us_plan()
    {
        Assert.True(PricingViewDecisions.IsEnterpriseTier(EnterpriseTier()));
        Assert.False(PricingViewDecisions.IsEnterpriseTier(ProTier()));
        Assert.False(PricingViewDecisions.IsEnterpriseTier(FreeTier()));
    }

    [Fact]
    public void The_toggle_is_offered_only_when_some_plan_is_priced_by_the_year()
    {
        Assert.True(PricingViewDecisions.HasAnnualPricing(new List<AppTierModel> { ProTier() }));
        Assert.False(PricingViewDecisions.HasAnnualPricing(new List<AppTierModel> { FreeTier() }));
    }

    [Fact]
    public void The_annual_saving_is_computed_from_the_live_prices()
    {
        var expected = (int)Math.Round((ProMonthly * 12m - ProAnnual) / (ProMonthly * 12m) * 100m);

        Assert.Equal(expected, PricingViewDecisions.AnnualDiscount(ProTier()));
        Assert.Equal(
            expected,
            PricingViewDecisions.BestAnnualDiscount(new List<AppTierModel> { FreeTier(), ProTier() }));
    }

    [Fact]
    public void A_plan_that_is_no_cheaper_by_the_year_advertises_no_saving()
    {
        var tier = ProTier();
        tier.PricingOptions[1].Price = ProMonthly * 12m;

        Assert.Equal(0, PricingViewDecisions.AnnualDiscount(tier));
    }

    #endregion

    #region Packs

    [Theory]
    [InlineData("Monthly", "/mo")]
    [InlineData("Yearly", "/yr")]
    [InlineData("Annually", "/yr")]
    [InlineData("Weekly", "/wk")]
    [InlineData("Daily", "/day")]
    [InlineData("OneTime", "")]
    [InlineData("", "")]
    public void A_pack_price_carries_its_period(string frequency, string suffix)
    {
        Assert.Equal(suffix, PricingViewDecisions.BillingSuffix(frequency));
    }

    [Fact]
    public void A_pack_is_priced_off_the_catalog()
    {
        Assert.Equal(
            FormatHelpers.FormatMoney(DocsPackPrice, "USD") + "/mo",
            PricingViewDecisions.PackPriceText(DocsPack(), "USD", Labels));
    }

    /// <summary>
    /// A pack the operator defined but never priced says so, rather than rendering a blank or a
    /// zero — either of which a visitor would read as "free".
    /// </summary>
    [Fact]
    public void An_unpriced_pack_says_so_instead_of_implying_it_is_free()
    {
        Assert.Equal("Not yet available", PricingViewDecisions.PackPriceText(LoosePack(), "USD", Labels));
        Assert.False(PricingViewDecisions.HasPackPrice(LoosePack()));
        Assert.True(PricingViewDecisions.HasPackPrice(DocsPack()));
    }

    [Fact]
    public void A_packs_trial_is_worded_the_way_every_stack_words_it()
    {
        Assert.Equal("14-day free trial", PricingViewDecisions.PackTrialLabel(DocsPack()));
        Assert.Equal(string.Empty, PricingViewDecisions.PackTrialLabel(AiPack()));
    }

    [Fact]
    public void Packs_with_no_groups_render_as_one_untitled_grid()
    {
        var groups = PricingViewDecisions.GroupAddOns(Catalog().AddOns, null, Labels.MorePacks);

        var only = Assert.Single(groups);
        Assert.Equal("all", only.Id);
        Assert.Null(only.Title);
        Assert.Equal(3, only.AddOns.Count);
    }

    /// <summary>
    /// Empty groups are dropped rather than rendered as a heading with nothing under it, and a
    /// pack matching no group is NOT dropped — it lands in the trailing catch-all, because a pack
    /// the company sells going silently missing from the page that sells it is the one failure
    /// this must not have.
    /// </summary>
    [Fact]
    public void Packs_are_filed_under_the_host_groups_with_the_strays_kept()
    {
        var groups = PricingViewDecisions.GroupAddOns(Catalog().AddOns, Groups(), Labels.MorePacks);

        Assert.Equal(2, groups.Count);

        Assert.Equal("core", groups[0].Id);
        Assert.Equal("Core packs", groups[0].Title);
        Assert.Equal(2, groups[0].AddOns.Count);

        Assert.Equal("more", groups[1].Id);
        Assert.Equal("More packs", groups[1].Title);
        Assert.Equal("pack-loose", Assert.Single(groups[1].AddOns).Id);
    }

    /// <summary>
    /// Category matching is case-SENSITIVE, because JS's is: the grouping there is
    /// <c>group.categories.includes(addOn.category)</c> over a <c>Set</c>, so "documents" and
    /// "Documents" are two different categories. A pack that matches no group by that rule is still
    /// never dropped — it lands in the trailing catch-all, which is the guarantee that matters.
    /// </summary>
    [Fact]
    public void A_category_that_differs_only_in_case_is_a_stray_the_way_it_is_in_JS()
    {
        var shouty = DocsPack();
        shouty.Category = "DOCUMENTS";

        var groups = PricingViewDecisions.GroupAddOns(
            new List<AppTierAddOnModel> { shouty, AiPack() }, Groups(), Labels.MorePacks);

        Assert.Equal(2, groups.Count);

        // Only the exactly-matching pack is filed under the heading.
        Assert.Equal("core", groups[0].Id);
        Assert.Equal("pack-ai", Assert.Single(groups[0].AddOns).Id);

        // The mis-cased one is visible under the catch-all rather than missing from the page.
        Assert.Equal("more", groups[1].Id);
        Assert.Equal("pack-docs", Assert.Single(groups[1].AddOns).Id);
    }

    [Fact]
    public void No_packs_means_no_groups_at_all()
    {
        Assert.Empty(PricingViewDecisions.GroupAddOns(new List<AppTierAddOnModel>(), null, Labels.MorePacks));
    }

    [Fact]
    public void Ticking_a_pack_reads_the_selection_back_in_catalog_order()
    {
        var catalog = Catalog();

        // Picked out of catalog order on purpose.
        var selection = PricingViewDecisions.TogglePackSelection(catalog, new List<string>(), "pack-ai");
        selection = PricingViewDecisions.TogglePackSelection(catalog, selection, "pack-docs");

        Assert.Equal(new[] { "pack-docs", "pack-ai" }, selection);
    }

    [Fact]
    public void Ticking_a_selected_pack_unticks_it()
    {
        var catalog = Catalog();
        var selection = PricingViewDecisions.TogglePackSelection(catalog, new List<string>(), "pack-ai");

        selection = PricingViewDecisions.TogglePackSelection(catalog, selection, "pack-ai");

        Assert.Empty(selection);
    }

    /// <summary>
    /// The platform-wide cap, the same one a selection arriving in a link is held to, so a basket
    /// built by clicking and one built from a URL cannot differ.
    /// </summary>
    [Fact]
    public void A_selection_never_grows_past_the_pack_cap()
    {
        var packs = new List<AppTierAddOnModel>();
        for (var index = 0; index < CatalogHelpers.MaxAddOnSelection + 5; index++)
        {
            packs.Add(new AppTierAddOnModel
            {
                Id = "pack-" + index.ToString("00"),
                Name = "Pack " + index,
                Status = "Active",
                DisplayOrder = index
            });
        }

        var catalog = CatalogHelpers.BuildPublicCatalog("app-1", null, packs);
        var selection = new List<string>();
        foreach (var pack in packs)
        {
            selection = PricingViewDecisions.TogglePackSelection(catalog, selection, pack.Id);
        }

        Assert.Equal(CatalogHelpers.MaxAddOnSelection, selection.Count);

        // Unticking is always allowed, cap or no cap.
        selection = PricingViewDecisions.TogglePackSelection(catalog, selection, selection[0]);
        Assert.Equal(CatalogHelpers.MaxAddOnSelection - 1, selection.Count);
    }

    [Fact]
    public void The_continue_button_counts_the_packs_it_would_buy()
    {
        Assert.Equal("Continue with 0 packs", PricingViewDecisions.ContinueWithPacksLabel(0, Labels));
        Assert.Equal("Continue with 1 pack", PricingViewDecisions.ContinueWithPacksLabel(1, Labels));
        Assert.Equal("Continue with 3 packs", PricingViewDecisions.ContinueWithPacksLabel(3, Labels));
    }

    #endregion

    #region Selection payloads

    [Fact]
    public void A_plan_carries_its_option_the_cycle_and_the_whole_basket()
    {
        var selection = PricingViewDecisions.PlanSelectionPayload(
            ProTier(), PricingBilling.Annual, new List<string> { "pack-docs" });

        Assert.Equal("tier-pro", selection.TierId);
        Assert.Equal("price-pro-annual", selection.PricingId);
        Assert.Equal(PricingBilling.Annual, selection.Billing);
        Assert.Equal(new[] { "pack-docs" }, selection.AddOnIds);
    }

    [Fact]
    public void A_free_plan_still_carries_the_option_the_server_gave_it()
    {
        var selection = PricingViewDecisions.PlanSelectionPayload(FreeTier(), PricingBilling.Monthly, null);

        Assert.Equal("tier-free", selection.TierId);
        Assert.Equal("price-free", selection.PricingId);
        Assert.Empty(selection.AddOnIds);
    }

    [Fact]
    public void A_contact_us_plan_carries_no_pricing_option()
    {
        var selection = PricingViewDecisions.PlanSelectionPayload(
            EnterpriseTier(), PricingBilling.Monthly, null);

        Assert.Equal("tier-ent", selection.TierId);
        Assert.Null(selection.PricingId);
    }

    [Fact]
    public void One_pack_taken_straight_through_implies_no_plan()
    {
        var selection = PricingViewDecisions.PackSelectionPayload("pack-docs", PricingBilling.Monthly);

        Assert.Null(selection.TierId);
        Assert.Null(selection.PricingId);
        Assert.Equal(new[] { "pack-docs" }, selection.AddOnIds);
    }

    [Fact]
    public void Continuing_with_packs_hands_back_everything_ticked()
    {
        var selection = PricingViewDecisions.PacksContinuePayload(
            PricingBilling.Annual, new List<string> { "pack-docs", "pack-ai" });

        Assert.Null(selection.TierId);
        Assert.Equal(PricingBilling.Annual, selection.Billing);
        Assert.Equal(new[] { "pack-docs", "pack-ai" }, selection.AddOnIds);
    }

    #endregion
}
