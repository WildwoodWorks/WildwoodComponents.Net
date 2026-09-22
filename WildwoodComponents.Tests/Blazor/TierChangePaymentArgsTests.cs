using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// What an upgrade hands the host to collect payment with (JS 05dd7cb). Hosts were given the
/// tier-pricing LINK id and the prorated charge, and wired the link id into
/// <c>PaymentComponent.PricingModelId</c>: the server found no pricing model for it, charged the
/// prorated amount once, and left no recurring subscription, renewal or trial behind.
/// </summary>
public class TierChangePaymentArgsTests
{
    private static AppTierModel Tier() => new AppTierModel
    {
        Id = "tier-pro",
        Name = "Professional",
        PricingOptions = new List<AppTierPricingModel>
        {
            new AppTierPricingModel
            {
                Id = "tp-monthly",
                PricingModelId = "pm-monthly",
                Price = 49.99m,
                BillingFrequency = "Monthly",
                TrialDays = 14,
                IsDefault = true
            },
            new AppTierPricingModel
            {
                Id = "tp-annual",
                PricingModelId = "pm-annual",
                Price = 499.00m,
                BillingFrequency = "Annually",
                TrialDays = 30
            }
        }
    };

    [Fact]
    public void TierSelected_carriesThePricingModel_priceAndTrial_ofTheOptionThatWasClicked()
    {
        var args = TierPlansPanel.BuildTierSelectedArgs(Tier(), "tp-annual", "monthly", isChange: true);

        Assert.Equal("tp-annual", args.PricingId);
        Assert.Equal("pm-annual", args.PricingModelId);
        Assert.Equal(499.00m, args.Price);
        Assert.Equal(30, args.TrialDays);
        Assert.True(args.IsChange);
    }

    [Fact]
    public void TierSelected_fallsBackToTheBillingCyclesOption_whenNoPricingWasNamed()
    {
        var args = TierPlansPanel.BuildTierSelectedArgs(Tier(), null, "annually", isChange: false);

        Assert.Equal("tp-annual", args.PricingId);
        Assert.Equal("pm-annual", args.PricingModelId);
        Assert.Equal(499.00m, args.Price);
        Assert.Equal(30, args.TrialDays);
    }

    [Fact]
    public void TierSelected_hasNoPricingForATierThatIsPricedOnRequest()
    {
        var enterprise = new AppTierModel { Id = "tier-ent", Name = "Enterprise" };

        var args = TierPlansPanel.BuildTierSelectedArgs(enterprise, null, "monthly", isChange: false);

        Assert.Null(args.PricingId);
        Assert.Null(args.PricingModelId);
        Assert.Equal(0m, args.Price);
        Assert.Null(args.TrialDays);
    }

    [Fact]
    public void PaymentRequired_carriesThePricingModel_notTheTierPricingLinkId()
    {
        var selected = TierPlansPanel.BuildTierSelectedArgs(Tier(), "tp-monthly", "monthly", isChange: true);
        var preview = new TierChangePreviewModel { NewPrice = 49.99m, ProratedChargeToday = 12.34m };

        var args = SubscriptionAdminComponent.BuildPaymentRequiredArgs(selected, preview);

        Assert.Equal("tp-monthly", args.PricingId);
        Assert.Equal("pm-monthly", args.PricingModelId);
    }

    /// <summary>
    /// The price is what the plan's subscription bills, not the one-time prorated charge: paying
    /// the prorated amount buys a charge, not the plan.
    /// </summary>
    [Fact]
    public void PaymentRequired_chargesThePlansPrice_notTheProratedAmount()
    {
        var selected = TierPlansPanel.BuildTierSelectedArgs(Tier(), "tp-monthly", "monthly", isChange: true);
        var preview = new TierChangePreviewModel { NewPrice = 49.99m, ProratedChargeToday = 12.34m };

        var args = SubscriptionAdminComponent.BuildPaymentRequiredArgs(selected, preview);

        Assert.Equal(49.99m, args.Price);
    }

    [Fact]
    public void PaymentRequired_carriesThePlansTrial()
    {
        var selected = TierPlansPanel.BuildTierSelectedArgs(Tier(), "tp-annual", "monthly", isChange: true);
        var preview = new TierChangePreviewModel { NewPrice = 499.00m };

        var args = SubscriptionAdminComponent.BuildPaymentRequiredArgs(selected, preview);

        Assert.Equal(30, args.TrialDays);
    }

    [Fact]
    public void PaymentRequired_fallsBackToThePreviewWhenThePlansOwnPriceDidNotReachUs()
    {
        var selected = new TierSelectedEventArgs { TierId = "tier-pro", TierName = "Professional" };

        Assert.Equal(49.99m, SubscriptionAdminComponent.BuildPaymentRequiredArgs(
            selected, new TierChangePreviewModel { NewPrice = 49.99m, ProratedChargeToday = 12.34m }).Price);

        Assert.Equal(12.34m, SubscriptionAdminComponent.BuildPaymentRequiredArgs(
            selected, new TierChangePreviewModel { ProratedChargeToday = 12.34m }).Price);

        Assert.Equal(0m, SubscriptionAdminComponent.BuildPaymentRequiredArgs(
            selected, new TierChangePreviewModel()).Price);
    }
}
