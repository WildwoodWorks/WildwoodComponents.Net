using System.Reflection;
using Microsoft.AspNetCore.Razor.Hosting;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// The rows the Razor add-ons panel renders (JS d7eae5a, 27daa30). The view is server-rendered, so
/// the decisions are made here and travel to the browser as data attributes — the JS acts on a row
/// rather than re-deriving who owns what.
/// </summary>
public class AddOnsPanelViewModelTests
{
    private static readonly DateTime MarchFirst = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private static UserAddOnSubscriptionModel Row(
        string status = "Active",
        string? paymentTransactionId = "txn-seats",
        bool isBundled = false,
        DateTime? endDate = null)
    {
        return new UserAddOnSubscriptionModel
        {
            Id = "sub-1",
            AppTierAddOnId = "addon-seats",
            Status = status,
            PaymentTransactionId = paymentTransactionId,
            AddOnName = "Extra Seats",
            IsBundled = isBundled,
            StartDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = endDate
        };
    }

    private static AppTierAddOnModel Pack(
        string id = "addon-seats",
        int? trialDays = null,
        int? pricingTrialDays = null,
        string? currency = null)
    {
        var pack = new AppTierAddOnModel
        {
            Id = id,
            Name = "Extra Seats",
            Status = "Active",
            TrialDays = trialDays,
            Currency = currency
        };

        pack.PricingOptions.Add(new AppTierAddOnPricingModel
        {
            Id = "aap-monthly",
            Price = 19m,
            BillingFrequency = "Monthly",
            TrialDays = pricingTrialDays,
            IsDefault = true
        });

        return pack;
    }

    private static AddOnsPanelViewModel Model(
        IEnumerable<UserAddOnSubscriptionModel>? subscriptions = null,
        IEnumerable<AppTierAddOnModel>? addOns = null,
        string currency = "USD",
        string? currentTierId = null,
        bool allowCancel = true,
        bool allowReactivate = true)
    {
        return new AddOnsPanelViewModel
        {
            ComponentId = "cid",
            AppId = "app-1",
            Currency = currency,
            CurrentTierId = currentTierId,
            AllowCancel = allowCancel,
            AllowReactivate = allowReactivate,
            ActiveAddOns = new List<UserAddOnSubscriptionModel>(subscriptions ?? []),
            AvailableAddOns = new List<AppTierAddOnModel>(addOns ?? [])
        };
    }

    /// <summary>
    /// The live bug: every row the server returned was badged with the hard-coded text "Active", and
    /// a row of ANY status hid the offer, so a cancelled pack could never be bought again.
    /// </summary>
    [Fact]
    public void A_cancelled_pack_leaves_the_owned_list_and_goes_back_on_offer()
    {
        var model = Model(new[] { Row("Cancelled") }, new[] { Pack() });

        Assert.Empty(model.OwnedRows);
        Assert.Single(model.Offers);
        Assert.True(model.Offers[0].CanSubscribe);
    }

    [Fact]
    public void A_pack_scheduled_to_cancel_is_still_owned_and_is_not_offered_twice()
    {
        var model = Model(new[] { Row("PendingCancellation", endDate: MarchFirst) }, new[] { Pack() });

        var row = Assert.Single(model.OwnedRows);
        Assert.Equal("Cancellation Scheduled", row.Decision.StatusLabel);
        Assert.Equal(AddOnDateLine.Cancels, row.Decision.DateLine);
        Assert.Equal(MarchFirst, row.Decision.EndDate);
        Assert.Empty(model.Offers);
    }

    [Fact]
    public void A_scheduled_cancellation_with_no_date_still_says_it_is_scheduled()
    {
        var model = Model(new[] { Row("PendingCancellation") });

        Assert.Equal(AddOnDateLine.CancelsAtPeriodEnd, model.OwnedRows[0].Decision.DateLine);
    }

    /// <summary>
    /// The row's allowed actions travel as a data attribute, so the JS refuses a click the server
    /// never offered.
    /// </summary>
    [Theory]
    [InlineData("Active", "txn-seats", false, "cancel")]
    [InlineData("PendingCancellation", "txn-seats", false, "reactivate")]
    [InlineData("Active", null, false, "cancel")]
    [InlineData("PendingCancellation", null, false, "")]
    [InlineData("Active", "txn-seats", true, "")]
    public void The_rows_allowed_actions_travel_as_a_data_attribute(
        string status, string? paymentTransactionId, bool isBundled, string expected)
    {
        var model = Model(new[] { Row(status, paymentTransactionId, isBundled, MarchFirst) });

        Assert.Equal(expected, model.OwnedRows[0].ActionsAttribute);
    }

    [Fact]
    public void A_read_only_surface_offers_neither_action()
    {
        var model = Model(
            new[] { Row("PendingCancellation", endDate: MarchFirst) },
            allowCancel: false,
            allowReactivate: false);

        Assert.Equal(string.Empty, model.OwnedRows[0].ActionsAttribute);
    }

    /// <summary>
    /// A pack nothing paid for was granted, not sold: it says so, promises no renewal, and its
    /// cancel confirmation says cancelling removes it.
    /// </summary>
    [Fact]
    public void A_granted_pack_says_it_is_included_and_promises_no_renewal()
    {
        var model = Model(new[] { Row(paymentTransactionId: null, endDate: MarchFirst) });
        var decision = model.OwnedRows[0].Decision;

        Assert.True(decision.ShowIncludedBadge);
        Assert.Equal(AddOnDateLine.None, decision.DateLine);
        Assert.False(decision.OffersReactivate);
        Assert.Equal(AddOnRowRules.DefaultLabels.CancelIncluded, decision.CancelMessage);
    }

    [Fact]
    public void A_billed_pack_is_asked_about_the_billing_period_before_it_is_cancelled()
    {
        var decision = Model(new[] { Row(endDate: MarchFirst) }).OwnedRows[0].Decision;

        Assert.False(decision.ShowIncludedBadge);
        Assert.Equal(AddOnDateLine.Renews, decision.DateLine);
        Assert.Equal(AddOnRowRules.DefaultLabels.CancelBilled, decision.CancelMessage);
    }

    [Fact]
    public void An_offer_carries_the_pricing_id_that_a_subscribe_sends()
    {
        var offer = Assert.Single(Model(addOns: new[] { Pack() }).Offers);

        Assert.NotNull(offer.Pricing);
        Assert.Equal("aap-monthly", offer.Pricing!.Id);
    }

    [Fact]
    public void An_offer_shows_the_trial_of_the_pricing_option_that_is_bought()
    {
        Assert.Equal(
            "14-day free trial",
            Model(addOns: new[] { Pack(trialDays: 7, pricingTrialDays: 14) }).Offers[0].TrialText);
        Assert.Equal(
            "7-day free trial",
            Model(addOns: new[] { Pack(trialDays: 7) }).Offers[0].TrialText);
        Assert.Equal(
            string.Empty,
            Model(addOns: new[] { Pack(trialDays: 0) }).Offers[0].TrialText);
    }

    /// <summary>The view used to price everything through a two-culture switch: EUR or en-US.</summary>
    [Fact]
    public void An_offer_is_priced_in_the_packs_own_currency()
    {
        Assert.Equal("€19.00", Model(addOns: new[] { Pack(currency: "EUR") }).Offers[0].PriceText);
        Assert.Equal("$19.00", Model(addOns: new[] { Pack() }).Offers[0].PriceText);
        Assert.StartsWith("CHF", Model(addOns: new[] { Pack() }, currency: "CHF").Offers[0].PriceText);
        Assert.Equal("monthly", Model(addOns: new[] { Pack() }).Offers[0].BillingSuffix);
    }

    [Fact]
    public void A_pack_the_current_plan_bundles_is_shown_rather_than_sold()
    {
        var pack = Pack();
        pack.BundledInTierIds.Add("TIER-PRO");

        var offer = Assert.Single(Model(addOns: new[] { pack }, currentTierId: "tier-pro").Offers);

        Assert.True(offer.IsBundled);
        Assert.False(offer.CanSubscribe);
    }

    /// <summary>The shipped same-origin proxy, unless the host mounts the controller elsewhere.</summary>
    [Fact]
    public void The_panel_points_at_the_shipped_proxy_by_default()
    {
        Assert.Equal("/api/wildwood-regsub", new AddOnsPanelViewModel().RegSubProxyUrl);
    }

    /// <summary>
    /// The view is compiled into the package by the Razor source generator, which writes no .g.cs
    /// to obj — so this is what proves the .cshtml parses at build time rather than blowing up in a
    /// consuming app's first render.
    /// </summary>
    [Fact]
    public void The_view_is_compiled_into_the_package()
    {
        var items = typeof(AddOnsPanelViewModel).Assembly
            .GetCustomAttributes<RazorCompiledItemAttribute>();

        Assert.Contains(items, item =>
            item.Identifier == "/Views/Shared/Components/AddOnsPanel/Default.cshtml");
    }
}
