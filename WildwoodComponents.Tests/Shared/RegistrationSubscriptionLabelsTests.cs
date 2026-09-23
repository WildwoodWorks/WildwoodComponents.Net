using System.Reflection;
using WildwoodComponents.Shared.Utilities;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The registration + subscription component's default copy, pinned word for word against the
/// table in packages/wildwood-react-shared/src/registrationSubscription/labels.ts.
/// </summary>
/// <remarks>
/// These strings are a cross-stack contract, not a style choice: React, React Native, Blazor,
/// Razor and SwiftUI all have to say them, and live sites' end-to-end suites locate elements by
/// them. A test that only checked "a label exists" would pass against copy that had quietly
/// drifted on one stack, which is the failure worth catching — so every default is written out
/// here, and the property count is asserted too, so a key added on one side and not the other
/// fails a build.
/// </remarks>
public class RegistrationSubscriptionLabelsTests
{
    /// <summary>
    /// Every label, keyed by its C# property name, valued with the English string labels.ts
    /// ships. Copied from that file; do not "tidy" a string here without changing it there.
    /// </summary>
    private static readonly Dictionary<string, string> ExpectedDefaults = new()
    {
        // Pricing view
        ["BillingMonthly"] = "Monthly",
        ["BillingAnnual"] = "Annual",
        ["BillingToggleAriaLabel"] = "Toggle annual billing",
        ["AnnualSavings"] = "Save up to {percent}%",
        ["LoadingPlans"] = "Loading plans...",
        ["PricingUnavailable"] = "Pricing is unavailable right now",
        ["Retry"] = "Retry",
        ["ContactUs"] = "Contact us",
        ["MorePacks"] = "More packs",
        ["PackUnavailable"] = "Not yet available",
        ["PackSelect"] = "Select",
        ["PackSelectNamed"] = "Select {name}",
        ["ContinueWithPacks"] = "Continue with {count} packs",
        ["ContinueWithOnePack"] = "Continue with 1 pack",

        // Signup view
        ["LoadingSignup"] = "Getting things ready...",
        ["RegistrationClosed"] = "Registration is closed",
        ["CreateAccount"] = "Create account",
        ["ContinueLabel"] = "Continue",
        ["Back"] = "Back",
        ["Cancel"] = "Cancel",
        ["SkipForNow"] = "Skip for now",
        ["HaveToken"] = "Have a registration token?",
        ["TokenLabel"] = "Registration token",
        ["EmailLabel"] = "Email",
        ["ChoosePlan"] = "Choose a plan",
        ["ChoosePacks"] = "Choose your packs",
        ["ReviewSelection"] = "Review your selection",
        ["SignupComplete"] = "You are all set",
        ["AlreadySignedIn"] = "You are already signed in",
        ["PlanFree"] = "Free",
        ["TokenPlanIncludes"] = "Your registration token includes",
        ["TokenPlanPacks"] = "Packs",
        ["TokenPlanFeatures"] = "Features",
        ["CheckingToken"] = "Checking your registration token...",
        ["OrderSummary"] = "Order Summary",
        ["DueToday"] = "Due today",
        ["SavedCardOnFile"] = "{brand} ending in {last4}",
        ["CardDetails"] = "Card Details",
        ["SaveCard"] = "Save card and continue",
        ["BuyingPacks"] = "Setting up your packs...",
        ["AuthenticatingPack"] = "Confirming {name} with your bank...",
        ["PacksUnavailable"] = "Your packs could not be bought.",
        ["PackStatusTrialing"] = "Trial started",
        ["PackStatusActive"] = "Active",
        ["PackStatusFailed"] = "Could not be added",
        ["PackStatusGranted"] = "Included",
        ["DisclaimersTitle"] = "One more step",
        ["DisclaimersIntro"] = "Please review and accept the following before continuing.",
        ["StatusCreatingAccount"] = "Creating your account...",
        ["StatusSigningIn"] = "Signing you in...",
        ["StatusActivatingPlan"] = "Activating your plan...",
        ["ProcessingWait"] = "Please wait while we set up your account.",
        ["SignupFailed"] = "Something Went Wrong",
        ["TryAgain"] = "Try Again",
        ["StartOver"] = "Start Over",
        ["SignupCompleteTitle"] = "You're All Set!",
        ["SignupCompleteToken"] = "Your account has been created with the {tier} from your registration token.",
        ["SignupCompleteTrial"] = "Your account has been created and your {days}-day free trial has started.",
        ["SignupCompletePlain"] = "Your account has been created successfully.",
        ["SignupCompleteActive"] = "Your account has been created and your plan is active.",
        ["SignupCompletePending"] =
            "Your account is ready! Plan activation is pending - you can select a plan from your dashboard.",
        ["GetStarted"] = "Get Started",

        // Manage view
        ["CurrentPlan"] = "Current plan",
        ["ChangePlan"] = "Change plan",
        ["CancelPlan"] = "Cancel plan",
        ["KeepPlan"] = "Keep plan",
        ["SectionStatus"] = "Subscription",
        ["SectionPlans"] = "Plans",
        ["SectionPacks"] = "Packs",
        ["SectionUsage"] = "Usage",
        ["SectionFeatures"] = "Features",
        ["SectionOverrides"] = "Overrides",
        ["UpgradeToPlan"] = "Upgrade to {tier}",
        ["ClosePayment"] = "Cancel payment",
        ["AuthenticatingChange"] = "Confirming the charge with your bank...",
        ["ApplyingChange"] = "Applying your new plan...",
        ["PlanChangeFailed"] = "The plan change could not be completed",
        ["PlanChangeExpired"] = "The payment window closed - please start the change again",
        ["PlanChangePaymentFailed"] =
            "That payment was not completed, so your plan has not changed. Please try again.",
        ["PlanChangeSuperseded"] = "This plan was changed somewhere else. Refresh and try again.",
        ["PlanChangeNotFound"] = "That plan change is no longer available. Please start it again.",
        ["PlanChangeInProgress"] = "A change to this plan is already under way. Give it a moment and refresh.",
        ["PaymentUnconfirmed"] =
            "Your payment went through but the plan change could not be confirmed automatically. " +
            "Please contact support with your receipt.",
        ["AddPacks"] = "Add packs",
        ["AddPacksTitle"] = "Add packs to your plan",
        ["PackIncluded"] = "Included with your registration",
        ["PackCancelIncluded"] =
            "This pack was included with your registration. Cancelling removes it from your account.",
        ["PackCancelBilled"] = "You keep access until the end of the current billing period.",
        ["PackCancelConfirm"] = "Cancel pack",
        ["PackCancelKeep"] = "Keep pack",
        ["PackReactivate"] = "Reactivate",
        ["FeatureIncluded"] = "Included",
        ["StoreManagesBilling"] = "Your app store manages billing for this change.",

        // Shared
        ["FinishOnWeb"] = "This purchase has to be finished on the web.",
        ["ViewNotAvailable"] = "This view is not available yet"
    };

    private static List<PropertyInfo> LabelProperties()
    {
        var properties = new List<PropertyInfo>();

        foreach (var property in typeof(RegistrationSubscriptionLabels)
                     .GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType == typeof(string) && property.CanRead) properties.Add(property);
        }

        return properties;
    }

    public static TheoryData<string> LabelNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in ExpectedDefaults.Keys) data.Add(name);
        return data;
    }

    [Theory]
    [MemberData(nameof(LabelNames))]
    public void Default_matches_the_string_labels_ts_ships(string propertyName)
    {
        var property = typeof(RegistrationSubscriptionLabels).GetProperty(propertyName);
        Assert.NotNull(property);

        var actual = (string?)property!.GetValue(RegistrationSubscriptionLabels.Defaults);

        Assert.Equal(ExpectedDefaults[propertyName], actual);
    }

    /// <summary>
    /// A key added to labels.ts and not here — or the reverse — is drift, and drift is exactly
    /// what this file exists to catch.
    /// </summary>
    [Fact]
    public void Every_label_the_class_carries_is_pinned_here()
    {
        var unpinned = new List<string>();
        foreach (var property in LabelProperties())
        {
            if (!ExpectedDefaults.ContainsKey(property.Name)) unpinned.Add(property.Name);
        }

        Assert.Empty(unpinned);
        Assert.Equal(ExpectedDefaults.Count, LabelProperties().Count);
    }

    [Fact]
    public void Resolve_keeps_the_shipped_copy_when_the_host_passes_nothing()
    {
        var labels = RegistrationSubscriptionLabels.Resolve(null);

        Assert.Equal("Retry", labels.Retry);
        Assert.Equal("Pricing is unavailable right now", labels.PricingUnavailable);
    }

    /// <summary>
    /// The C# spelling of JS's <c>{ ...DEFAULT_LABELS, ...overrides }</c>: an instance the host
    /// only partially set still answers the shipped word for every string it left alone.
    /// </summary>
    [Fact]
    public void Resolve_takes_the_hosts_words_and_keeps_the_rest()
    {
        var labels = RegistrationSubscriptionLabels.Resolve(
            new RegistrationSubscriptionLabels { Retry = "Try that again" });

        Assert.Equal("Try that again", labels.Retry);
        Assert.Equal("Pricing is unavailable right now", labels.PricingUnavailable);
        Assert.Equal("Continue with {count} packs", labels.ContinueWithPacks);
    }

    [Fact]
    public void Format_fills_a_named_slot()
    {
        Assert.Equal(
            "Continue with 3 packs",
            RegistrationSubscriptionLabels.Format("Continue with {count} packs", "count", 3));
    }

    [Fact]
    public void Format_fills_several_slots()
    {
        var values = new Dictionary<string, object?> { ["brand"] = "Visa", ["last4"] = "4242" };

        Assert.Equal(
            "Visa ending in 4242",
            RegistrationSubscriptionLabels.Format("{brand} ending in {last4}", values));
    }

    /// <summary>
    /// A slot with no matching value is left AS WRITTEN rather than blanked, so a mistyped
    /// override reads as a bug instead of silently losing a word. This is JS's rule exactly.
    /// </summary>
    [Fact]
    public void Format_leaves_an_unmatched_slot_verbatim()
    {
        Assert.Equal(
            "Save up to {percent}%",
            RegistrationSubscriptionLabels.Format("Save up to {percent}%", "pct", 20));
    }

    [Fact]
    public void Format_leaves_text_that_is_not_a_slot_alone()
    {
        var values = new Dictionary<string, object?> { ["name"] = "Docs" };

        Assert.Equal(
            "{ not a slot } Docs",
            RegistrationSubscriptionLabels.Format("{ not a slot } {name}", values));
    }

    /// <summary>
    /// Numbers are stringified invariantly: a saving is "20%" wherever the component renders,
    /// never "20,0%".
    /// </summary>
    [Fact]
    public void Format_writes_numbers_invariantly()
    {
        Assert.Equal(
            "Save up to 20.5%",
            RegistrationSubscriptionLabels.Format("Save up to {percent}%", "percent", 20.5m));
    }
}
