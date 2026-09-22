using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// What a pack row says has to be true: a pack the account no longer has is on offer again, a pack
/// nothing paid for promises no renewal and cannot be reactivated at a provider, the trial shown is
/// the one the processor will start, and a refused subscribe or cancel is reported rather than
/// swallowed. Ported case for case from
/// packages/wildwood-react-native/src/__tests__/addOnRows.test.ts and
/// packages/wildwood-react/src/__tests__/AddOnsPanel.test.tsx (JS d7eae5a, 27daa30).
/// </summary>
public class AddOnRowRulesTests
{
    private static UserAddOnSubscriptionModel Row(
        string status = "Active",
        string? paymentTransactionId = "txn-seats",
        bool isBundled = false,
        DateTime? endDate = null,
        DateTime? currentPeriodEnd = null,
        string appTierAddOnId = "addon-seats")
    {
        return new UserAddOnSubscriptionModel
        {
            Id = "sub-1",
            UserId = "user-1",
            AppId = "app-1",
            AppTierAddOnId = appTierAddOnId,
            Status = status,
            PaymentTransactionId = paymentTransactionId,
            AddOnName = "Extra Seats",
            IsBundled = isBundled,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = endDate,
            CurrentPeriodEnd = currentPeriodEnd
        };
    }

    private static AppTierAddOnModel Pack(
        string id = "addon-seats",
        int? trialDays = null,
        string? currency = null,
        decimal price = 19m,
        int? pricingTrialDays = null,
        bool withPricing = true)
    {
        var pack = new AppTierAddOnModel
        {
            Id = id,
            AppId = "app-1",
            Name = "Extra Seats",
            Status = "Active",
            TrialDays = trialDays,
            Currency = currency
        };

        if (withPricing)
        {
            pack.PricingOptions.Add(new AppTierAddOnPricingModel
            {
                Id = "aap-monthly",
                PricingModelId = "pm-monthly",
                PricingModelName = "Monthly",
                Price = price,
                BillingFrequency = "Monthly",
                TrialDays = pricingTrialDays,
                IsDefault = true
            });
        }

        return pack;
    }

    private static readonly DateTime MarchFirst = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    #region OwnsAddOn

    /// <summary>Every status the server can put on a pack subscription.</summary>
    [Theory]
    [InlineData("Active", true)]
    [InlineData("Trialing", true)]
    [InlineData("PendingCancellation", true)]
    [InlineData("Cancelled", false)]
    [InlineData("Expired", false)]
    [InlineData("Suspended", false)]
    public void OwnsAddOn_matches_GrantsAccess_for_every_status(string status, bool owned)
    {
        Assert.Equal(owned, AddOnRowRules.OwnsAddOn(new[] { Row(status) }, "addon-seats"));
    }

    /// <summary>
    /// The live bug: ANY row — Cancelled included — badged the pack "Subscribed" and hid the offer.
    /// </summary>
    [Fact]
    public void OwnsAddOn_offers_a_pack_again_once_its_subscription_is_cancelled_or_expired()
    {
        Assert.False(AddOnRowRules.OwnsAddOn(new[] { Row("Cancelled") }, "addon-seats"));
        Assert.False(AddOnRowRules.OwnsAddOn(new[] { Row("Expired") }, "addon-seats"));
    }

    [Fact]
    public void OwnsAddOn_still_counts_a_pack_scheduled_to_cancel_as_owned_so_it_is_not_sold_twice()
    {
        Assert.True(AddOnRowRules.OwnsAddOn(new[] { Row("PendingCancellation") }, "addon-seats"));
    }

    [Fact]
    public void OwnsAddOn_compares_ids_case_insensitively_and_ignores_other_packs()
    {
        Assert.True(AddOnRowRules.OwnsAddOn(new[] { Row(appTierAddOnId: "ADDON-SEATS") }, "addon-seats"));
        Assert.False(AddOnRowRules.OwnsAddOn(new[] { Row(appTierAddOnId: "addon-storage") }, "addon-seats"));
        Assert.False(AddOnRowRules.OwnsAddOn(new UserAddOnSubscriptionModel[0], "addon-seats"));
        Assert.False(AddOnRowRules.OwnsAddOn(null, "addon-seats"));
        Assert.False(AddOnRowRules.OwnsAddOn(new[] { Row() }, null));
    }

    [Fact]
    public void OwnedRows_keeps_only_the_rows_that_still_grant_access()
    {
        var rows = AddOnRowRules.OwnedRows(new[] { Row("Active"), Row("Cancelled"), Row("PendingCancellation") });

        Assert.Equal(2, rows.Count);
        Assert.Equal("Active", rows[0].Status);
        Assert.Equal("PendingCancellation", rows[1].Status);
    }

    [Fact]
    public void AvailableRows_puts_a_cancelled_pack_back_on_offer_and_leaves_an_owned_one_off()
    {
        var packs = new[] { Pack("addon-seats"), Pack("addon-storage") };

        var afterCancel = AddOnRowRules.AvailableRows(packs, new[] { Row("Cancelled") });
        Assert.Equal(2, afterCancel.Count);

        var whileOwned = AddOnRowRules.AvailableRows(packs, new[] { Row("PendingCancellation") });
        Assert.Single(whileOwned);
        Assert.Equal("addon-storage", whileOwned[0].Id);
    }

    #endregion

    #region IsComplimentary

    [Fact]
    public void IsComplimentary_is_true_only_for_a_row_nothing_paid_for_and_no_plan_bundles()
    {
        Assert.True(AddOnRowRules.IsComplimentary(Row(paymentTransactionId: null)));
        Assert.True(AddOnRowRules.IsComplimentary(Row(paymentTransactionId: "")));
        Assert.False(AddOnRowRules.IsComplimentary(Row(paymentTransactionId: "txn-seats")));
        Assert.False(AddOnRowRules.IsComplimentary(Row(paymentTransactionId: null, isBundled: true)));
        Assert.False(AddOnRowRules.IsComplimentary(null));
    }

    #endregion

    #region Describe

    [Fact]
    public void Describe_shows_a_granted_pack_as_included_with_no_renewal_date_and_no_reactivate()
    {
        var decision = AddOnRowRules.Describe(Row(paymentTransactionId: null, endDate: MarchFirst));

        Assert.True(decision.Complimentary);
        Assert.True(decision.ShowIncludedBadge);
        // Nothing bills it, so there is no renewal to promise even though the server sent a date.
        Assert.Equal(AddOnDateLine.None, decision.DateLine);
        Assert.Null(decision.EndDate);
        Assert.False(decision.OffersReactivate);
        Assert.True(decision.OffersCancel);
        Assert.Equal(AddOnRowRules.DefaultLabels.CancelIncluded, decision.CancelMessage);
        Assert.Equal("cancel", decision.ActionsAttribute);
    }

    [Fact]
    public void Describe_asks_about_the_billing_period_before_cancelling_a_billed_pack()
    {
        var decision = AddOnRowRules.Describe(Row(endDate: MarchFirst));

        Assert.Equal(AddOnDateLine.Renews, decision.DateLine);
        Assert.Equal(MarchFirst, decision.EndDate);
        Assert.Equal(AddOnRowRules.DefaultLabels.CancelBilled, decision.CancelMessage);
        Assert.Equal("Active", decision.StatusLabel);
        Assert.False(decision.ShowIncludedBadge);
    }

    [Fact]
    public void Describe_badges_a_scheduled_cancellation_and_says_when_it_ends()
    {
        var decision = AddOnRowRules.Describe(Row("PendingCancellation", endDate: MarchFirst));

        Assert.Equal("Cancellation Scheduled", decision.StatusLabel);
        Assert.Equal(AddOnDateLine.Cancels, decision.DateLine);
        Assert.True(decision.OffersReactivate);
        // Already cancelling: no second Cancel button.
        Assert.False(decision.OffersCancel);
        Assert.Equal("reactivate", decision.ActionsAttribute);
    }

    [Fact]
    public void Describe_still_says_a_pack_is_scheduled_to_cancel_when_the_server_sent_no_end_date()
    {
        var decision = AddOnRowRules.Describe(Row("PendingCancellation"));

        Assert.Equal(AddOnDateLine.CancelsAtPeriodEnd, decision.DateLine);
        Assert.Null(decision.EndDate);
    }

    /// <summary>
    /// JS carries one <c>endDate</c>; the .NET model splits the same idea across EndDate and
    /// CurrentPeriodEnd, so a live billed row still prints its renewal.
    /// </summary>
    [Fact]
    public void Describe_renews_from_the_current_period_end_when_there_is_no_end_date()
    {
        var decision = AddOnRowRules.Describe(Row(currentPeriodEnd: MarchFirst));

        Assert.Equal(AddOnDateLine.Renews, decision.DateLine);
        Assert.Equal(MarchFirst, decision.EndDate);
    }

    [Fact]
    public void Describe_never_offers_reactivate_on_a_granted_or_bundled_row_or_where_the_surface_forbids_it()
    {
        Assert.False(AddOnRowRules
            .Describe(Row("PendingCancellation", paymentTransactionId: null, endDate: MarchFirst)).OffersReactivate);
        Assert.False(AddOnRowRules
            .Describe(Row("PendingCancellation", isBundled: true, endDate: MarchFirst)).OffersReactivate);
        Assert.False(AddOnRowRules
            .Describe(Row("PendingCancellation", endDate: MarchFirst), canReactivate: false).OffersReactivate);
    }

    [Fact]
    public void Describe_offers_nothing_on_a_bundled_row_or_where_the_surface_forbids_cancelling()
    {
        var bundled = AddOnRowRules.Describe(Row(isBundled: true));
        Assert.False(bundled.OffersCancel);
        Assert.False(bundled.OffersReactivate);
        Assert.Equal("Bundled", bundled.StatusLabel);
        Assert.Equal(string.Empty, bundled.ActionsAttribute);

        Assert.False(AddOnRowRules.Describe(Row(), canCancel: false).OffersCancel);
    }

    [Fact]
    public void Describe_takes_the_hosts_cancel_copy_when_it_supplies_one()
    {
        var decision = AddOnRowRules.Describe(
            Row(paymentTransactionId: null),
            labels: new AddOnsPanelLabels { CancelIncluded = "Cancelling gives it back." });

        Assert.Equal("Cancelling gives it back.", decision.CancelMessage);
    }

    #endregion

    #region FailureMessage

    [Fact]
    public void FailureMessage_names_the_pack_and_the_action_when_the_refusal_carried_no_words()
    {
        Assert.Equal(
            "Could not subscribe to Extra Seats. Please try again.",
            AddOnRowRules.FailureMessage(AddOnAction.Subscribe, "Extra Seats", null));
        Assert.Equal(
            "Could not cancel Extra Seats. Please try again.",
            AddOnRowRules.FailureMessage(AddOnAction.Cancel, "Extra Seats", null));
        Assert.Equal(
            "Could not reactivate Extra Seats. Please try again.",
            AddOnRowRules.FailureMessage(AddOnAction.Reactivate, "Extra Seats", "   "));
    }

    [Fact]
    public void FailureMessage_prefers_the_servers_own_refusal_when_it_sent_one()
    {
        Assert.Equal(
            "Card declined.",
            AddOnRowRules.FailureMessage(AddOnAction.Subscribe, "Extra Seats", "Card declined."));
    }

    [Fact]
    public void FailureMessage_is_never_empty_even_for_a_row_with_no_name()
    {
        Assert.Equal(
            "Could not cancel this pack. Please try again.",
            AddOnRowRules.FailureMessage(AddOnAction.Cancel, null, null));
        Assert.Equal(
            "Could not cancel this pack. Please try again.",
            AddOnRowRules.FailureMessage(AddOnAction.Cancel, "  ", string.Empty));
    }

    #endregion

    #region Trial, currency and pricing

    [Fact]
    public void TrialLabel_takes_the_trial_from_the_pricing_option_that_is_bought_not_the_pack()
    {
        var pack = Pack(trialDays: 7, pricingTrialDays: 14);

        Assert.Equal("14-day free trial", AddOnRowRules.TrialLabel(pack, AddOnRowRules.DefaultPricing(pack)));
    }

    [Fact]
    public void TrialLabel_falls_back_to_the_pack_when_the_pricing_option_starts_no_trial()
    {
        var pack = Pack(trialDays: 7);

        Assert.Equal("7-day free trial", AddOnRowRules.TrialLabel(pack, AddOnRowRules.DefaultPricing(pack)));
        Assert.Equal("7-day free trial", AddOnRowRules.TrialLabel(pack, null));
    }

    [Fact]
    public void TrialLabel_says_nothing_and_never_renders_a_bare_zero_when_there_is_no_trial()
    {
        var pack = Pack(trialDays: 0, pricingTrialDays: 0);

        Assert.Equal(string.Empty, AddOnRowRules.TrialLabel(pack, AddOnRowRules.DefaultPricing(pack)));
        Assert.Equal(string.Empty, AddOnRowRules.TrialLabel(Pack(), null));
    }

    [Fact]
    public void FormatPrice_prices_the_pack_in_its_own_currency_then_the_catalogs()
    {
        Assert.Equal("€19.00", AddOnRowRules.FormatPrice(Pack(currency: "EUR"), AddOnRowRules.DefaultPricing(Pack(currency: "EUR")), "USD"));
        Assert.Equal("$19.00", AddOnRowRules.FormatPrice(Pack(), AddOnRowRules.DefaultPricing(Pack()), "USD"));
        // The hard-coded "$" priced a Swiss pack in dollars.
        Assert.StartsWith("CHF", AddOnRowRules.FormatPrice(Pack(currency: "CHF"), AddOnRowRules.DefaultPricing(Pack()), null));
    }

    [Fact]
    public void ResolveCurrency_ignores_a_blank_pack_currency()
    {
        Assert.Equal("USD", AddOnRowRules.ResolveCurrency(Pack(currency: "  "), "USD"));
        Assert.Equal("SEK", AddOnRowRules.ResolveCurrency(Pack(currency: "SEK"), "USD"));
        Assert.Null(AddOnRowRules.ResolveCurrency(null, null));
    }

    [Fact]
    public void DefaultPricing_is_the_packs_default_then_the_first_it_sells()
    {
        var pack = Pack();
        pack.PricingOptions.Insert(0, new AppTierAddOnPricingModel { Id = "aap-annual", Price = 190m, IsDefault = false });

        Assert.Equal("aap-monthly", AddOnRowRules.DefaultPricing(pack)!.Id);

        pack.PricingOptions[1].IsDefault = false;
        Assert.Equal("aap-annual", AddOnRowRules.DefaultPricing(pack)!.Id);

        Assert.Null(AddOnRowRules.DefaultPricing(Pack(withPricing: false)));
    }

    [Fact]
    public void BillingSuffix_lower_cases_the_cycle_and_says_month_when_the_server_sent_nothing()
    {
        Assert.Equal("monthly", AddOnRowRules.BillingSuffix(AddOnRowRules.DefaultPricing(Pack())));
        Assert.Equal("month", AddOnRowRules.BillingSuffix(new AppTierAddOnPricingModel()));
        Assert.Equal("month", AddOnRowRules.BillingSuffix(null));
    }

    #endregion

    #region IsBundledInTier

    [Fact]
    public void IsBundledInTier_matches_the_current_plan_case_insensitively()
    {
        var pack = Pack();
        pack.BundledInTierIds.Add("TIER-PRO");

        Assert.True(AddOnRowRules.IsBundledInTier(pack, "tier-pro"));
        Assert.False(AddOnRowRules.IsBundledInTier(pack, "tier-free"));
        Assert.False(AddOnRowRules.IsBundledInTier(pack, null));
        Assert.False(AddOnRowRules.IsBundledInTier(Pack(), "tier-pro"));
    }

    #endregion
}
