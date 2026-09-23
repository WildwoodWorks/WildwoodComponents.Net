using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// When the Razor status panel prints a trial end date (JS 1eefaa1). The server keeps a finished
/// trial's end date on the row — that is how it records that the account has already had its trial
/// for the app — so printing it unconditionally put "Trial Ends" with a date on an Active, paid
/// plan after a repeat upgrade had been charged.
/// </summary>
public class SubscriptionStatusPanelViewModelTests
{
    private static readonly DateTime Now = new(2026, 9, 18, 12, 0, 0, DateTimeKind.Unspecified);

    private static SubscriptionStatusPanelViewModel Panel(string status, DateTime? trialEnd)
    {
        return new SubscriptionStatusPanelViewModel
        {
            AsOf = Now,
            Subscription = new UserTierSubscriptionModel
            {
                Status = status,
                TrialEndDate = trialEnd
            }
        };
    }

    [Fact]
    public void A_running_trial_shows_its_end_date()
    {
        Assert.True(Panel("Trialing", Now.AddDays(5)).ShowTrialEnd);
    }

    [Fact]
    public void An_active_plan_never_shows_a_trial_end()
    {
        // The paid plan that a charged upgrade produced still carries the old trial's date.
        Assert.False(Panel("Active", Now.AddDays(5)).ShowTrialEnd);
    }

    [Fact]
    public void A_finished_trial_does_not_show_its_end_date()
    {
        Assert.False(Panel("Trialing", Now.AddDays(-1)).ShowTrialEnd);
    }

    [Fact]
    public void No_trial_end_date_shows_nothing()
    {
        Assert.False(Panel("Trialing", null).ShowTrialEnd);
    }

    [Fact]
    public void No_subscription_shows_nothing()
    {
        Assert.False(new SubscriptionStatusPanelViewModel { AsOf = Now }.ShowTrialEnd);
    }

    /// <summary>
    /// The panel's own clock, when nothing pins it, is "now" — the rule is never evaluated against
    /// a default <see cref="DateTime"/>.
    /// </summary>
    [Fact]
    public void The_default_clock_is_now()
    {
        var panel = new SubscriptionStatusPanelViewModel();

        Assert.True(panel.AsOf > DateTime.Now.AddMinutes(-5));
    }
}
