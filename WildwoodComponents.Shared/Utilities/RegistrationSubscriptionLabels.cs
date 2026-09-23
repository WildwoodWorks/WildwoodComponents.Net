using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WildwoodComponents.Shared.Utilities;

/// <summary>
/// Every user-facing string the registration + subscription component says, with the copy the live
/// sites already ship as the default for each one.
/// </summary>
/// <remarks>
/// <para>
/// Ported key for key from packages/wildwood-react-shared/src/registrationSubscription/labels.ts.
/// The default strings are a CROSS-STACK CONTRACT, not a style choice: the same words have to come
/// out of React, React Native, Blazor, Razor and SwiftUI, because end-to-end suites on live sites
/// locate elements by them. <c>RegistrationSubscriptionLabelDefaultsTests</c> pins every one of
/// them against a table copied out of labels.ts, so drift fails a build rather than a customer's
/// smoke test.
/// </para>
/// <para>
/// The plan-card calls to action ("Get Started", "Subscribe", "Switch to &lt;Tier&gt;") are
/// deliberately NOT here: they belong to the shared tier-card footer every plan grid on the
/// platform renders, and moving them would change what the admin surfaces say too.
/// </para>
/// <para>
/// Overriding is by property initialiser: a host constructs the class and sets only the strings it
/// wants to change, and every property it leaves alone still holds the shipped word — the C#
/// spelling of JS's <c>{ ...DEFAULT_LABELS, ...overrides }</c>. <see cref="Resolve"/> turns a
/// possibly-null parameter into a usable set.
/// </para>
/// <para>
/// This lives in Shared, beside <see cref="AddOnsPanelLabels"/>, because Blazor and Razor render
/// the same component from it and the defaults must not be able to diverge between them.
/// </para>
/// </remarks>
public class RegistrationSubscriptionLabels
{
    #region Pricing view

    /// <summary>Billing toggle, monthly side.</summary>
    public string BillingMonthly { get; set; } = "Monthly";

    /// <summary>Billing toggle, annual side.</summary>
    public string BillingAnnual { get; set; } = "Annual";

    /// <summary>Accessible name of the billing toggle control itself.</summary>
    public string BillingToggleAriaLabel { get; set; } = "Toggle annual billing";

    /// <summary>Badge next to "Annual". <c>{percent}</c> is the largest annual saving in the grid.</summary>
    public string AnnualSavings { get; set; } = "Save up to {percent}%";

    /// <summary>Said by the loading placeholder. Carries no numbers, because none are known yet.</summary>
    public string LoadingPlans { get; set; } = "Loading plans...";

    /// <summary>Shown instead of prices when the catalog could not be read. Never beside a price.</summary>
    public string PricingUnavailable { get; set; } = "Pricing is unavailable right now";

    /// <summary>Retries the catalog request.</summary>
    public string Retry { get; set; } = "Retry";

    /// <summary>Generic "talk to a human" link, pointed at the component's contact URL.</summary>
    public string ContactUs { get; set; } = "Contact us";

    /// <summary>Heading of the trailing group holding packs that match none of the host's groups.</summary>
    public string MorePacks { get; set; } = "More packs";

    /// <summary>Said in place of a price by a pack the operator has defined but not yet priced.</summary>
    public string PackUnavailable { get; set; } = "Not yet available";

    /// <summary>Single-pack call to action, used when pack selection is off.</summary>
    public string PackSelect { get; set; } = "Select";

    /// <summary>Accessible name for that call to action. <c>{name}</c> is the pack's name.</summary>
    public string PackSelectNamed { get; set; } = "Select {name}";

    /// <summary>Multi-select summary bar. <c>{count}</c> is how many packs are selected.</summary>
    public string ContinueWithPacks { get; set; } = "Continue with {count} packs";

    /// <summary>The singular of <see cref="ContinueWithPacks"/>.</summary>
    public string ContinueWithOnePack { get; set; } = "Continue with 1 pack";

    #endregion

    #region Signup view

    /// <summary>Said while the app's registration settings and catalog are still being read.</summary>
    public string LoadingSignup { get; set; } = "Getting things ready...";

    /// <summary>Said when the app is not accepting registrations at all.</summary>
    public string RegistrationClosed { get; set; } = "Registration is closed";

    /// <summary>Submits the registration form.</summary>
    public string CreateAccount { get; set; } = "Create account";

    /// <summary>Advances a step.</summary>
    public string ContinueLabel { get; set; } = "Continue";

    /// <summary>Returns to the previous step.</summary>
    public string Back { get; set; } = "Back";

    /// <summary>Abandons the flow.</summary>
    public string Cancel { get; set; } = "Cancel";

    /// <summary>Skips an optional step (the pack step, typically).</summary>
    public string SkipForNow { get; set; } = "Skip for now";

    /// <summary>Opens the optional registration-token entry.</summary>
    public string HaveToken { get; set; } = "Have a registration token?";

    /// <summary>Field label for the registration token.</summary>
    public string TokenLabel { get; set; } = "Registration token";

    /// <summary>Field label for the email address.</summary>
    public string EmailLabel { get; set; } = "Email";

    /// <summary>Step heading: pick a plan.</summary>
    public string ChoosePlan { get; set; } = "Choose a plan";

    /// <summary>Step heading: pick packs.</summary>
    public string ChoosePacks { get; set; } = "Choose your packs";

    /// <summary>Step heading: confirm what is about to be bought.</summary>
    public string ReviewSelection { get; set; } = "Review your selection";

    /// <summary>Said once the account exists and everything asked for has been granted.</summary>
    public string SignupComplete { get; set; } = "You are all set";

    /// <summary>Said when a signed-in visitor lands on the signup view.</summary>
    public string AlreadySignedIn { get; set; } = "You are already signed in";

    /// <summary>Shown on a free plan in the plan summary card, where a price would otherwise be.</summary>
    public string PlanFree { get; set; } = "Free";

    /// <summary>Heading over the plan and packs a registration token sets up.</summary>
    public string TokenPlanIncludes { get; set; } = "Your registration token includes";

    /// <summary>Sub-heading over the token's packs.</summary>
    public string TokenPlanPacks { get; set; } = "Packs";

    /// <summary>Sub-heading over the token's extra features.</summary>
    public string TokenPlanFeatures { get; set; } = "Features";

    /// <summary>Said while the registration token is being checked.</summary>
    public string CheckingToken { get; set; } = "Checking your registration token...";

    /// <summary>Heading over what is about to be charged.</summary>
    public string OrderSummary { get; set; } = "Order Summary";

    /// <summary>Labels the amount charged today (zero while a trial runs).</summary>
    public string DueToday { get; set; } = "Due today";

    /// <summary>The card already on file. <c>{brand}</c> and <c>{last4}</c> come from the quote.</summary>
    public string SavedCardOnFile { get; set; } = "{brand} ending in {last4}";

    /// <summary>Field label over the card entry.</summary>
    public string CardDetails { get; set; } = "Card Details";

    /// <summary>Saves the card and continues the purchase.</summary>
    public string SaveCard { get; set; } = "Save card and continue";

    /// <summary>Said while the packs are being bought.</summary>
    public string BuyingPacks { get; set; } = "Setting up your packs...";

    /// <summary>Said while one pack's card is being authenticated with the bank.</summary>
    public string AuthenticatingPack { get; set; } = "Confirming {name} with your bank...";

    /// <summary>Said when a pack purchase was refused and no message came back.</summary>
    public string PacksUnavailable { get; set; } = "Your packs could not be bought.";

    /// <summary>Pack outcome: the pack's trial has started.</summary>
    public string PackStatusTrialing { get; set; } = "Trial started";

    /// <summary>Pack outcome: the pack is running and paid for.</summary>
    public string PackStatusActive { get; set; } = "Active";

    /// <summary>Pack outcome: the pack could not be bought.</summary>
    public string PackStatusFailed { get; set; } = "Could not be added";

    /// <summary>Pack outcome: the registration token included the pack.</summary>
    public string PackStatusGranted { get; set; } = "Included";

    /// <summary>Heading over the disclaimers step.</summary>
    public string DisclaimersTitle { get; set; } = "One more step";

    /// <summary>Sentence under that heading.</summary>
    public string DisclaimersIntro { get; set; } = "Please review and accept the following before continuing.";

    /// <summary>Status while the account is being registered.</summary>
    public string StatusCreatingAccount { get; set; } = "Creating your account...";

    /// <summary>Status while the new account is being signed in.</summary>
    public string StatusSigningIn { get; set; } = "Signing you in...";

    /// <summary>Status while the plan is being activated.</summary>
    public string StatusActivatingPlan { get; set; } = "Activating your plan...";

    /// <summary>Reassurance under the processing status.</summary>
    public string ProcessingWait { get; set; } = "Please wait while we set up your account.";

    /// <summary>Heading of the failed-signup panel.</summary>
    public string SignupFailed { get; set; } = "Something Went Wrong";

    /// <summary>Resumes a failed signup where it stopped.</summary>
    public string TryAgain { get; set; } = "Try Again";

    /// <summary>Throws the attempt away and returns to the form.</summary>
    public string StartOver { get; set; } = "Start Over";

    /// <summary>Heading of the success panel.</summary>
    public string SignupCompleteTitle { get; set; } = "You're All Set!";

    /// <summary>Success copy when a registration token set the account up. <c>{tier}</c> is the plan.</summary>
    public string SignupCompleteToken { get; set; } =
        "Your account has been created with the {tier} from your registration token.";

    /// <summary>Success copy when the plan started a trial. <c>{days}</c> is the trial length.</summary>
    public string SignupCompleteTrial { get; set; } =
        "Your account has been created and your {days}-day free trial has started.";

    /// <summary>Success copy when no plan was chosen.</summary>
    public string SignupCompletePlain { get; set; } = "Your account has been created successfully.";

    /// <summary>Success copy when the plan is running.</summary>
    public string SignupCompleteActive { get; set; } = "Your account has been created and your plan is active.";

    /// <summary>Success copy when the account exists but the plan could not be activated.</summary>
    public string SignupCompletePending { get; set; } =
        "Your account is ready! Plan activation is pending - you can select a plan from your dashboard.";

    /// <summary>Leaves the finished signup.</summary>
    public string GetStarted { get; set; } = "Get Started";

    #endregion

    #region Manage view

    /// <summary>Names the plan the user is on.</summary>
    public string CurrentPlan { get; set; } = "Current plan";

    /// <summary>Starts a plan change.</summary>
    public string ChangePlan { get; set; } = "Change plan";

    /// <summary>Starts a cancellation.</summary>
    public string CancelPlan { get; set; } = "Cancel plan";

    /// <summary>Backs out of a cancellation.</summary>
    public string KeepPlan { get; set; } = "Keep plan";

    /// <summary>Section/tab: the subscription itself.</summary>
    public string SectionStatus { get; set; } = "Subscription";

    /// <summary>Section/tab: the plans on offer.</summary>
    public string SectionPlans { get; set; } = "Plans";

    /// <summary>Section/tab: the packs on offer.</summary>
    public string SectionPacks { get; set; } = "Packs";

    /// <summary>Section/tab: usage against the plan's limits.</summary>
    public string SectionUsage { get; set; } = "Usage";

    /// <summary>Section/tab: the features the plan grants.</summary>
    public string SectionFeatures { get; set; } = "Features";

    /// <summary>Section/tab: per-user feature overrides (admins only).</summary>
    public string SectionOverrides { get; set; } = "Overrides";

    /// <summary>Title of the built-in card modal. <c>{tier}</c> is the plan being moved to.</summary>
    public string UpgradeToPlan { get; set; } = "Upgrade to {tier}";

    /// <summary>Accessible name of that modal's close button.</summary>
    public string ClosePayment { get; set; } = "Cancel payment";

    /// <summary>Said while the prorated charge is being confirmed with the bank.</summary>
    public string AuthenticatingChange { get; set; } = "Confirming the charge with your bank...";

    /// <summary>Said while the server finishes applying a paid-for change.</summary>
    public string ApplyingChange { get; set; } = "Applying your new plan...";

    /// <summary>Heading over a failed plan change.</summary>
    public string PlanChangeFailed { get; set; } = "The plan change could not be completed";

    /// <summary>Said when the parked change's payment window closed before it was finished.</summary>
    public string PlanChangeExpired { get; set; } = "The payment window closed - please start the change again";

    /// <summary>Said when the prorated charge was refused.</summary>
    public string PlanChangePaymentFailed { get; set; } =
        "That payment was not completed, so your plan has not changed. Please try again.";

    /// <summary>Said when another change replaced the one being finished.</summary>
    public string PlanChangeSuperseded { get; set; } = "This plan was changed somewhere else. Refresh and try again.";

    /// <summary>Said when the parked change is no longer on the server.</summary>
    public string PlanChangeNotFound { get; set; } = "That plan change is no longer available. Please start it again.";

    /// <summary>Said when a change is already under way on this subscription.</summary>
    public string PlanChangeInProgress { get; set; } =
        "A change to this plan is already under way. Give it a moment and refresh.";

    /// <summary>Said when a payment went through but carried no id to complete the change with.</summary>
    public string PaymentUnconfirmed { get; set; } =
        "Your payment went through but the plan change could not be confirmed automatically. " +
        "Please contact support with your receipt.";

    /// <summary>Opens the pack picker from the packs panel.</summary>
    public string AddPacks { get; set; } = "Add packs";

    /// <summary>Heading of the pack picker.</summary>
    public string AddPacksTitle { get; set; } = "Add packs to your plan";

    /// <summary>Badge on a pack nothing bills: a registration token's, or an admin grant.</summary>
    public string PackIncluded { get; set; } = "Included with your registration";

    /// <summary>Confirmation copy before cancelling a pack nothing bills.</summary>
    public string PackCancelIncluded { get; set; } =
        "This pack was included with your registration. Cancelling removes it from your account.";

    /// <summary>Confirmation copy before cancelling a pack that is billed.</summary>
    public string PackCancelBilled { get; set; } = "You keep access until the end of the current billing period.";

    /// <summary>Confirms a pack cancellation.</summary>
    public string PackCancelConfirm { get; set; } = "Cancel pack";

    /// <summary>Backs out of a pack cancellation.</summary>
    public string PackCancelKeep { get; set; } = "Keep pack";

    /// <summary>Takes back a scheduled pack cancellation.</summary>
    public string PackReactivate { get; set; } = "Reactivate";

    /// <summary>Marks a feature an override grants outside the plan.</summary>
    public string FeatureIncluded { get; set; } = "Included";

    /// <summary>
    /// Said in place of the preview's proration figures when the app is billed through a device
    /// store. The store decides what is charged and when, so quoting a net charge here would state
    /// a number nobody on this side can honour. Used by the native stacks, which are the only ones
    /// a store can bill.
    /// </summary>
    public string StoreManagesBilling { get; set; } = "Your app store manages billing for this change.";

    #endregion

    #region Shared

    /// <summary>
    /// Said when a purchase needs a card challenge the host cannot answer — a stack with no payment
    /// action adapter carries no payment SDK, so a 3-D Secure prompt has nowhere to run. Never a
    /// silent failure: the customer is told where to finish it instead.
    /// </summary>
    public string FinishOnWeb { get; set; } = "This purchase has to be finished on the web.";

    /// <summary>Said by a view that has not shipped yet.</summary>
    public string ViewNotAvailable { get; set; } = "This view is not available yet";

    #endregion

    #region Resolution and formatting

    /// <summary>The shipped copy, for a caller that supplies no labels of its own.</summary>
    public static RegistrationSubscriptionLabels Defaults
    {
        get { return new RegistrationSubscriptionLabels(); }
    }

    /// <summary>
    /// The set a component renders from: the host's instance when it passed one, the shipped copy
    /// otherwise.
    /// </summary>
    /// <remarks>
    /// This is JS's <c>resolveLabels</c>. The merge does not need to run here because it has
    /// already happened: every property is initialised with its default, so an instance the host
    /// only partially set still answers the shipped word for everything it left alone.
    /// </remarks>
    public static RegistrationSubscriptionLabels Resolve(RegistrationSubscriptionLabels? overrides)
    {
        return overrides ?? new RegistrationSubscriptionLabels();
    }

    /// <summary>
    /// Fills one <c>{placeholder}</c> slot — the common case, since most labels carry a single one.
    /// </summary>
    public static string Format(string? template, string key, object? value)
    {
        var values = new Dictionary<string, object?>(1);
        values[key] = value;
        return Format(template, values);
    }

    /// <summary>
    /// Fills <c>{placeholder}</c> slots in a label, the same way JS's <c>formatLabel</c> does: a
    /// slot with no matching value is left AS WRITTEN rather than blanked, so a mistyped override
    /// reads as a bug instead of silently losing a word.
    /// </summary>
    /// <remarks>
    /// A slot is <c>{</c>, one or more word characters, <c>}</c> — exactly JS's <c>/\{(\w+)\}/g</c>.
    /// Values are stringified with the invariant culture, so a percentage or a day count reads the
    /// same wherever the component renders.
    /// </remarks>
    public static string Format(string? template, IReadOnlyDictionary<string, object?>? values)
    {
        if (template is null || template.Length == 0) return string.Empty;
        if (values is null || values.Count == 0) return template;

        var builder = new StringBuilder(template.Length);
        var index = 0;

        while (index < template.Length)
        {
            var open = template.IndexOf('{', index);
            if (open < 0)
            {
                builder.Append(template, index, template.Length - index);
                break;
            }

            builder.Append(template, index, open - index);

            var end = open + 1;
            while (end < template.Length && IsWordCharacter(template[end])) end++;

            if (end > open + 1 && end < template.Length && template[end] == '}')
            {
                var key = template.Substring(open + 1, end - open - 1);
                object? value;
                if (values.TryGetValue(key, out value))
                {
                    builder.Append(Stringify(value));
                }
                else
                {
                    // Unmatched: the slot stays visible rather than leaving a hole in the sentence.
                    builder.Append(template, open, end - open + 1);
                }

                index = end + 1;
                continue;
            }

            builder.Append('{');
            index = open + 1;
        }

        return builder.ToString();
    }

    private static bool IsWordCharacter(char c)
    {
        return c == '_' || char.IsLetterOrDigit(c);
    }

    private static string Stringify(object? value)
    {
        if (value is null) return string.Empty;
        if (value is string text) return text;
        if (value is IFormattable formattable) return formattable.ToString(null, CultureInfo.InvariantCulture);
        return value.ToString() ?? string.Empty;
    }

    #endregion
}
