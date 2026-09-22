using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.Rendering;
using WildwoodComponents.Blazor.Components.Subscription.Admin;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// What the Blazor add-ons panel actually renders for a pack row (JS d7eae5a, 27daa30). The panel
/// has no bUnit renderer, so the compiled markup is inspected the way
/// <see cref="AIChatComponentInputBindingContractTests"/> does: <c>BuildRenderTree</c> on a bare
/// instance reads initialised fields only, so no renderer, DI or lifecycle is needed.
/// </summary>
public class AddOnsPanelRenderTests
{
    private static readonly DateTime MarchFirst = new(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    #region Fixtures

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

    private static AppTierAddOnModel Pack(int? trialDays = null, int? pricingTrialDays = null, string? currency = null)
    {
        var pack = new AppTierAddOnModel
        {
            Id = "addon-seats",
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

    /// <summary>
    /// A panel loaded with what the server returned, split by the same call the component makes.
    /// </summary>
    private static AddOnsPanel Panel(
        IEnumerable<UserAddOnSubscriptionModel>? subscriptions = null,
        IEnumerable<AppTierAddOnModel>? addOns = null,
        string? actionError = null,
        string? confirmingSubscriptionId = null,
        bool allowCancel = true,
        bool allowReactivate = true)
    {
        var panel = new AddOnsPanel
        {
            AppId = "app-1",
            AllowCancel = allowCancel,
            AllowReactivate = allowReactivate
        };

        SetField(panel, "_subscriptions", new List<UserAddOnSubscriptionModel>(subscriptions ?? []));
        SetField(panel, "_addOns", new List<AppTierAddOnModel>(addOns ?? []));
        Invoke(panel, "RebuildRows");
        SetField(panel, "_actionError", actionError);
        SetField(panel, "_confirmingSubscriptionId", confirmingSubscriptionId);

        return panel;
    }

    #endregion

    #region Reflection helpers

    private static void SetField(AddOnsPanel panel, string name, object? value)
    {
        typeof(AddOnsPanel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(panel, value);
    }

    private static void Invoke(AddOnsPanel panel, string name)
    {
        typeof(AddOnsPanel)
            .GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(panel, null);
    }

    /// <summary>Everything the markup would put on the page: its text and its CSS classes.</summary>
    private static string Render(AddOnsPanel panel)
    {
        using var builder = new RenderTreeBuilder();
        var buildRenderTree = typeof(AddOnsPanel).GetMethod(
            "BuildRenderTree", BindingFlags.Instance | BindingFlags.NonPublic)!;
        buildRenderTree.Invoke(panel, new object[] { builder });

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

    #endregion

    /// <summary>
    /// The live bug: the server returns every row it has, so a Cancelled pack was listed under
    /// "Active Add-Ons" with a green badge and a Cancel button, and never offered for sale again.
    /// </summary>
    [Fact]
    public void A_cancelled_pack_leaves_the_active_list_and_goes_back_on_offer()
    {
        var markup = Render(Panel(new[] { Row("Cancelled") }, new[] { Pack() }));

        Assert.DoesNotContain("Active Add-Ons", markup);
        Assert.DoesNotContain("Subscribed", markup);
        Assert.Contains("Available Add-Ons", markup);
        Assert.Contains("Subscribe", markup);
    }

    [Fact]
    public void A_pack_scheduled_to_cancel_is_still_owned_and_is_not_offered_twice()
    {
        var markup = Render(Panel(new[] { Row("PendingCancellation", endDate: MarchFirst) }, new[] { Pack() }));

        Assert.Contains("Active Add-Ons", markup);
        Assert.Contains("Cancellation Scheduled", markup);
        Assert.Contains("Cancels on:", markup);
        Assert.DoesNotContain("Renews:", markup);
        // The pack is owned, so nothing sells it again — and a cancelling row offers no second Cancel.
        Assert.DoesNotContain("Available Add-Ons", markup);
    }

    [Fact]
    public void A_scheduled_cancellation_with_no_date_still_says_it_is_scheduled()
    {
        var markup = Render(Panel(new[] { Row("PendingCancellation") }));

        Assert.Contains("Cancels at the end of the billing period", markup);
    }

    /// <summary>
    /// A billed pack's scheduled cancellation can be taken back; a granted one has nothing at a
    /// provider to take back.
    /// </summary>
    [Fact]
    public void Reactivate_is_offered_on_a_billed_scheduled_cancellation_only()
    {
        var billed = Render(Panel(new[] { Row("PendingCancellation", endDate: MarchFirst) }));
        Assert.Contains(AddOnRowRules.DefaultLabels.Reactivate, billed);

        var granted = Render(Panel(new[]
        {
            Row("PendingCancellation", paymentTransactionId: null, endDate: MarchFirst)
        }));
        Assert.DoesNotContain(AddOnRowRules.DefaultLabels.Reactivate, granted);

        var readOnly = Render(Panel(
            new[] { Row("PendingCancellation", endDate: MarchFirst) },
            allowReactivate: false));
        Assert.DoesNotContain(AddOnRowRules.DefaultLabels.Reactivate, readOnly);
    }

    /// <summary>
    /// A pack nothing paid for was granted, not sold: it says so, promises no renewal, and its
    /// cancel confirmation says cancelling removes it rather than talking about a billing period.
    /// </summary>
    [Fact]
    public void A_granted_pack_says_it_is_included_and_promises_no_renewal()
    {
        var markup = Render(Panel(new[] { Row(paymentTransactionId: null, endDate: MarchFirst) }));

        Assert.Contains(AddOnRowRules.DefaultLabels.Included, markup);
        Assert.Contains("ww-addon-included", markup);
        Assert.DoesNotContain("Renews:", markup);
    }

    [Fact]
    public void The_cancel_confirmation_words_match_how_the_pack_is_paid_for()
    {
        var granted = Render(Panel(
            new[] { Row(paymentTransactionId: null) },
            confirmingSubscriptionId: "sub-1"));
        Assert.Contains(AddOnRowRules.DefaultLabels.CancelIncluded, granted);
        Assert.Contains(AddOnRowRules.DefaultLabels.CancelConfirm, granted);
        Assert.Contains(AddOnRowRules.DefaultLabels.CancelKeep, granted);

        var billed = Render(Panel(new[] { Row() }, confirmingSubscriptionId: "sub-1"));
        Assert.Contains(AddOnRowRules.DefaultLabels.CancelBilled, billed);
    }

    /// <summary>
    /// Nothing is cancelled on the first click: the row asks first, so no confirmation copy shows
    /// until the user has asked for one.
    /// </summary>
    [Fact]
    public void The_cancel_confirmation_appears_only_for_the_row_being_confirmed()
    {
        var markup = Render(Panel(new[] { Row() }));

        Assert.DoesNotContain(AddOnRowRules.DefaultLabels.CancelBilled, markup);
        Assert.DoesNotContain("ww-addon-confirm", markup);
        Assert.Contains("Cancel", markup);
    }

    /// <summary>
    /// The failure used to reach the logger and nothing else, so a purchase that never happened
    /// looked as if it had.
    /// </summary>
    [Fact]
    public void A_refused_action_is_shown_in_the_panel()
    {
        var markup = Render(Panel(
            new[] { Row() },
            actionError: "Could not subscribe to Extra Seats. Please try again."));

        Assert.Contains("Could not subscribe to Extra Seats. Please try again.", markup);
        Assert.Contains("ww-addons-error", markup);
        Assert.Contains("ww-alert-danger", markup);
    }

    [Fact]
    public void A_bundled_pack_offers_neither_cancel_nor_reactivate()
    {
        var markup = Render(Panel(new[] { Row(isBundled: true) }));

        Assert.Contains("Bundled", markup);
        Assert.DoesNotContain("ww-addon-confirm", markup);
        Assert.DoesNotContain(AddOnRowRules.DefaultLabels.Reactivate, markup);
    }

    [Fact]
    public void An_offer_shows_the_trial_of_the_pricing_option_that_is_bought()
    {
        var markup = Render(Panel(addOns: new[] { Pack(trialDays: 7, pricingTrialDays: 14) }));

        Assert.Contains("14-day free trial", markup);
        Assert.DoesNotContain("7-day free trial", markup);
    }

    [Fact]
    public void An_offer_with_no_trial_renders_no_trial_line_at_all()
    {
        var markup = Render(Panel(addOns: new[] { Pack(trialDays: 0) }));

        Assert.DoesNotContain("free trial", markup);
    }

    /// <summary>The panel used to hard-code a "$" in front of every price.</summary>
    [Fact]
    public void An_offer_is_priced_in_the_packs_own_currency()
    {
        var panel = Panel(addOns: new[] { Pack(currency: "EUR") });
        panel.Currency = "USD";

        var markup = Render(panel);

        Assert.Contains("€19.00", markup);
        Assert.DoesNotContain("$19.00", markup);
        // The billing cycle is its own render frame, so the "/" before it is not adjacent here.
        Assert.Contains("monthly", markup);
    }
}

/// <summary>
/// The parameters a consuming app binds. The additions are all optional, so an app that renders the
/// panel the way it did before keeps working.
/// </summary>
public class AddOnsPanelParameterContractTests
{
    [Theory]
    [InlineData("Currency", typeof(string))]
    [InlineData("AllowCancel", typeof(bool))]
    [InlineData("AllowReactivate", typeof(bool))]
    [InlineData("Labels", typeof(AddOnsPanelLabels))]
    [InlineData("OnAddPacks", typeof(EventCallback))]
    public void The_new_parameters_are_bindable(string name, Type expected)
    {
        var property = typeof(AddOnsPanel).GetProperty(name);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<ParameterAttribute>());
        Assert.Equal(expected, property.PropertyType);
    }

    [Theory]
    [InlineData("AppId")]
    [InlineData("CompanyId")]
    [InlineData("UserId")]
    [InlineData("IsCompanyMode")]
    [InlineData("IsAdmin")]
    [InlineData("CurrentTierId")]
    [InlineData("OnSubscriptionChanged")]
    public void The_parameters_a_host_already_binds_are_still_there(string name)
    {
        var property = typeof(AddOnsPanel).GetProperty(name);

        Assert.NotNull(property);
        Assert.NotNull(property!.GetCustomAttribute<ParameterAttribute>());
    }

    /// <summary>
    /// A surface that offers both actions is the default: the panel a host already renders keeps
    /// its Cancel button, and gains Reactivate on a row that qualifies.
    /// </summary>
    [Fact]
    public void Cancel_and_reactivate_are_offered_unless_the_host_turns_them_off()
    {
        var panel = new AddOnsPanel();

        Assert.True(panel.AllowCancel);
        Assert.True(panel.AllowReactivate);
        Assert.Equal("USD", panel.Currency);
        Assert.Equal("Included with your registration", panel.Labels.Included);
    }
}
