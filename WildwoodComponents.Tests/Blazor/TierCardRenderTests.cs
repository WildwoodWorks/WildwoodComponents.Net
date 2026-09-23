using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using WildwoodComponents.Blazor.Components.Pricing;
using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

// CS0618: PricingDisplayComponent is deprecated in favour of RegistrationSubscriptionPricing, and
// it still ships - so its card markup still has to be pinned. Scoped to this file, so a NEW call
// site anywhere else still warns.
#pragma warning disable CS0618

/// <summary>
/// What a tier card renders: the price, and the free-trial line React's <c>TierCardHeader</c>
/// shows (JS f8b095f + 541e446). The two grids had no trial line at all and each formatted money
/// with a private three-to-five-entry symbol table, so a CHF plan showed a dollar sign.
/// </summary>
/// <remarks>
/// Neither panel has a bUnit renderer, so the compiled markup is inspected the way
/// <see cref="AddOnsPanelRenderTests"/> does: <c>BuildRenderTree</c> on a bare instance reads
/// initialised fields only, so no renderer, DI or lifecycle is needed.
/// </remarks>
public class TierCardRenderTests
{
    #region Fixtures

    private static AppTierPricingModel Pricing(
        decimal price = 79m,
        string billingFrequency = "Monthly",
        int? trialDays = null,
        bool isDefault = true)
    {
        return new AppTierPricingModel
        {
            Id = "atp-monthly",
            AppTierId = "tier-pro",
            PricingModelId = "pm-1",
            IsDefault = isDefault,
            PricingModelName = "Monthly",
            Price = price,
            BillingFrequency = billingFrequency,
            TrialDays = trialDays
        };
    }

    private static AppTierModel Tier(
        bool isFreeTier = false,
        string? currency = null,
        bool showPrice = true,
        params AppTierPricingModel[] pricing)
    {
        return new AppTierModel
        {
            Id = "tier-pro",
            AppId = "app-1",
            Name = "Pro",
            Status = "Active",
            IsFreeTier = isFreeTier,
            ShowPrice = showPrice,
            Currency = currency,
            PricingOptions = new List<AppTierPricingModel>(pricing)
        };
    }

    #endregion

    #region Reflection helpers

    private static T Panel<T>(string currency, params AppTierModel[] tiers) where T : new()
    {
        var panel = new T();
        typeof(T).GetProperty("Currency")!.SetValue(panel, currency);
        typeof(T)
            .GetField("_tiers", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel, new List<AppTierModel>(tiers));
        return panel;
    }

    /// <summary>Everything the markup would put on the page: its text and its CSS classes.</summary>
    // BL0006: reading RenderTree frames is exactly what this test is for - the two panels have no
    // bUnit renderer, so the compiled markup is the only thing there is to assert against. Scoped
    // to this method so the advisory still applies everywhere else.
#pragma warning disable BL0006
    private static string Render<T>(T panel)
    {
        using var builder = new RenderTreeBuilder();
        typeof(T)
            .GetMethod("BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(panel, new object[] { builder });

        var frames = builder.GetFrames();
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
                    if (frame.AttributeName == "class")
                    {
                        text.Append(frame.AttributeValue as string ?? string.Empty).Append(' ');
                    }
                    break;
            }
        }

        return text.ToString();
    }
#pragma warning restore BL0006

    #endregion

    #region Money

    [Fact]
    public void TierPlansPanel_prices_a_plan_in_the_tiers_own_currency()
    {
        // The tier says CHF; the panel's Currency parameter is only the fallback. The private
        // formatter this replaced had three entries and returned "$" for everything else.
        var markup = Render(Panel<TierPlansPanel>("USD", Tier(currency: "CHF", pricing: Pricing(price: 1234.5m))));

        Assert.Contains("CHF\u00a01,234.50", markup);
        Assert.DoesNotContain("$1,234.50", markup);
    }

    [Fact]
    public void PricingDisplayComponent_falls_back_to_the_components_currency()
    {
        var markup = Render(Panel<PricingDisplayComponent>("EUR", Tier(currency: null, pricing: Pricing(price: 79m))));

        Assert.Contains("€79.00", markup);
    }

    #endregion

    #region Trial line

    [Fact]
    public void TierPlansPanel_shows_the_trial_line_on_a_paid_priced_plan()
    {
        var markup = Render(Panel<TierPlansPanel>("USD", Tier(pricing: Pricing(price: 79m, trialDays: 14))));

        Assert.Contains("ww-plan-trial", markup);
        Assert.Contains("14-day free trial", markup);
    }

    [Fact]
    public void PricingDisplayComponent_shows_the_trial_line_on_a_paid_priced_plan()
    {
        var markup = Render(Panel<PricingDisplayComponent>("USD", Tier(pricing: Pricing(price: 79m, trialDays: 30))));

        Assert.Contains("ww-plan-trial", markup);
        Assert.Contains("30-day free trial", markup);
    }

    /// <summary>
    /// A plan with no trial, a free plan and a card that is not showing a price all advertise
    /// nothing — there is no trial to start before paying when nothing is being paid.
    /// </summary>
    [Theory]
    [InlineData(79, null, false, true)]   // paid, no trial days
    [InlineData(0, 14, false, true)]      // zero-priced option
    [InlineData(79, 14, true, true)]      // free tier
    [InlineData(79, 14, false, false)]    // ShowPrice off
    public void No_trial_line_when_there_is_nothing_to_try(
        int price, int? trialDays, bool isFreeTier, bool showPrice)
    {
        var tier = Tier(
            isFreeTier: isFreeTier,
            showPrice: showPrice,
            pricing: Pricing(price: price, trialDays: trialDays));

        Assert.DoesNotContain("free trial", Render(Panel<TierPlansPanel>("USD", tier)));
        Assert.DoesNotContain("free trial", Render(Panel<PricingDisplayComponent>("USD", tier)));
    }

    #endregion
}

#pragma warning restore CS0618
