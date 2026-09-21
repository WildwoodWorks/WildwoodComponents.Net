using System.Net;
using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WildwoodComponents.Blazor.Components.Disclaimer;
using WildwoodComponents.Blazor.Components.Payment;
using WildwoodComponents.Blazor.Components.Registration;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Blazor.Components.RegistrationSubscription.Parts;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;
using static WildwoodComponents.Blazor.Components.Registration.TokenRegistrationComponent;

namespace WildwoodComponents.Tests.Blazor;

// BL0005: setting a component parameter from outside the component is exactly how a component is
// put into a given state here - there is no renderer to pass a ParameterView through. Scoped to
// this file, so the advisory still applies to every component that ships.
#pragma warning disable BL0005

/// <summary>
/// What the signup view and its parts actually put on the page: the step hooks the end-to-end
/// suites locate by, and the exact words every stack has to say.
/// </summary>
/// <remarks>
/// Same technique as <see cref="RegistrationSubscriptionPricingRenderTests"/>: there is no bUnit
/// renderer in this repo, so <c>BuildRenderTree</c> is called on a bare instance, which reads
/// initialised fields only. The view renders from its flow driver, so each step is reached by
/// DRIVING a real driver over a scripted transport and then handing it to the component — the
/// state under test is one the flow can actually be in.
/// </remarks>
public class RegistrationSubscriptionSignupRenderTests
{
    private const decimal ProMonthly = 79m;
    private const decimal PackPrice = 9m;

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

    #endregion

    #region Driving a flow to a step

    private const string Tiers =
        """
        [
          {"id":"tier-free","name":"Starter","status":"Active","displayOrder":0,"isFreeTier":true,
           "isDefault":true,"currency":"USD","pricingOptions":[]},
          {"id":"tier-pro","name":"Pro","status":"Active","displayOrder":1,"currency":"USD",
           "description":"Everything, for teams.",
           "pricingOptions":[{"id":"price-pro","price":79,"billingFrequency":"Monthly","isDefault":true,"trialDays":14}]}
        ]
        """;

    private const string Packs =
        """
        [{"id":"pack-docs","name":"Docs Pack","status":"Active","displayOrder":1,"currency":"USD",
          "pricingOptions":[{"id":"ao-docs","price":9,"billingFrequency":"Monthly","isDefault":true}]}]
        """;

    private const string LoginOk =
        """{"jwtToken":"jwt-1","id":"user-1","userId":"user-1","email":"ada@example.test"}""";

    private static RegistrationFormData Form(string? token = null)
    {
        return new RegistrationFormData
        {
            FirstName = "Ada",
            LastName = "Lovelace",
            Username = "ada",
            Email = "ada@example.test",
            Password = "Analytical1!",
            Token = token
        };
    }

    /// <summary>A driver over a scripted transport, configured for whichever step is wanted.</summary>
    private static SignupFlowDriver Driver(
        ScriptedHttpMessageHandler handler,
        Action<SignupFlowSettings>? configure = null,
        bool signedIn = false)
    {
        var client = handler.CreateClient();
        var appTier = new AppTierComponentService(client, NullLogger<AppTierComponentService>.Instance);
        var auth = new AuthenticationService(
            client, new FakeLocalStorageService(), NullLogger<AuthenticationService>.Instance);
        var session = new FakeBlazorSessionManager(signedIn);

        var settings = new SignupFlowSettings { AppId = "app-1" };
        configure?.Invoke(settings);

        return new SignupFlowDriver(
            new PublicCatalogService(appTier, NullLogger<PublicCatalogService>.Instance),
            auth,
            new FeatureEntitlementService(appTier, NullLogger<FeatureEntitlementService>.Instance),
            new DisclaimerService(client, NullLogger<DisclaimerService>.Instance),
            new SignupAccountCreator(
                new FakeHttpClientFactory(handler.CreateClient()),
                Options.Create(new WildwoodComponentsOptions { BaseUrl = "https://api.test" }),
                auth,
                appTier,
                new FakePaymentProviderService(),
                session,
                NullLogger<SignupAccountCreator>.Instance),
            session,
            settings);
    }

    private static ScriptedHttpMessageHandler Routes(
        bool open = true,
        bool token = true,
        string? login = null)
    {
        var handler = new ScriptedHttpMessageHandler();
        handler
            .On("auth-configuration",
                $$"""{"isEnabled":true,"allowOpenRegistration":{{(open ? "true" : "false")}},"allowTokenRegistration":{{(token ? "true" : "false")}}}""")
            .On("app-tiers/app-1/public", Tiers)
            .On("app-tier-addons/app-1/public", Packs)
            .On("userregistration/register", """{"success":true,"userId":"user-1"}""")
            .On("auth/login", login ?? LoginOk)
            .On("app-tiers/app-1/my-subscription", """{"success":true}""");

        return handler;
    }

    /// <summary>The view, with a flow already driven to wherever the caller left it.</summary>
    private static RegistrationSubscriptionSignup View(SignupFlowDriver? flow, Action<RegistrationSubscriptionSignup>? configure = null)
    {
        var view = new RegistrationSubscriptionSignup { AppId = "app-1" };
        configure?.Invoke(view);
        SetField(view, "_flow", flow);
        return view;
    }

    #endregion

    #region The view's own hooks

    [Fact]
    public void The_root_carries_the_view_hook_and_the_cross_stack_classes()
    {
        var markup = RenderAll(View(null));

        Assert.Contains("data-ww-view=\"signup\"", markup);
        Assert.Contains("ww-regsub ww-regsub-signup", markup);
    }

    /// <summary>
    /// The DOM names every machine step, with <c>done</c> spelled <c>success</c> — the vocabulary
    /// JS's test-hook table pins, because live suites locate steps by it.
    /// </summary>
    [Theory]
    [InlineData(SignupStep.Loading, "loading")]
    [InlineData(SignupStep.Closed, "closed")]
    [InlineData(SignupStep.Register, "register")]
    [InlineData(SignupStep.Token, "token")]
    [InlineData(SignupStep.Plan, "plan")]
    [InlineData(SignupStep.Packs, "packs")]
    [InlineData(SignupStep.Payment, "payment")]
    [InlineData(SignupStep.Creating, "creating")]
    [InlineData(SignupStep.Disclaimers, "disclaimers")]
    [InlineData(SignupStep.PackCheckout, "packCheckout")]
    [InlineData(SignupStep.Done, "success")]
    [InlineData(SignupStep.Failed, "failed")]
    public void Every_step_has_the_name_the_DOM_hook_uses(SignupStep step, string expected)
    {
        Assert.Equal(expected, SignupViewDecisions.StepName(step));
    }

    [Fact]
    public async Task A_signed_in_visitor_is_offered_no_second_account()
    {
        var flow = Driver(Routes(), signedIn: true);
        await flow.StartAsync();

        var markup = RenderAll(View(flow));

        Assert.Contains("ww-regsub-notice", markup);
        Assert.Contains("You are already signed in", markup);
        Assert.DoesNotContain("data-ww-step", markup);
        Assert.Empty(ChildComponents(View(flow)));
    }

    [Fact]
    public async Task A_closed_app_says_so_and_offers_the_contact_link()
    {
        var flow = Driver(Routes(open: false, token: false));
        await flow.StartAsync();

        var view = View(flow, v => v.ContactUrl = "https://example.test/sales");
        var markup = RenderAll(view);

        Assert.Contains("data-ww-step=\"closed\"", markup);
        Assert.Contains(typeof(ClosedNotice), ChildComponents(view));
    }

    [Fact]
    public async Task A_host_can_say_registration_is_closed_its_own_way()
    {
        var flow = Driver(Routes(open: false, token: false));
        await flow.StartAsync();

        var view = View(flow, v => v.RenderClosed = context => builder =>
            builder.AddContent(0, "Ask your administrator: " + context.Message));

        var text = RenderText(view);

        Assert.Contains("Ask your administrator: Registration is closed", text);
        Assert.DoesNotContain(typeof(ClosedNotice), ChildComponents(view));
    }

    #endregion

    #region The steps

    [Fact]
    public async Task The_form_step_carries_the_plan_summary_and_the_deferred_registration_form()
    {
        var flow = Driver(Routes(), settings => settings.PreSelectedTierId = "tier-pro");
        await flow.StartAsync();

        var view = View(flow);
        var markup = RenderAll(view);
        var children = ChildComponents(view);

        Assert.Contains("data-ww-step=\"register\"", markup);
        Assert.Contains(typeof(PlanSummaryCard), children);
        Assert.Contains(typeof(TokenRegistrationComponent), children);

        // The plan is already chosen, so the form's submit is the last thing before an account.
        Assert.Contains("Create Account", markup);
    }

    /// <summary>
    /// Not a label on any stack: React hard-codes both words, and a live site's suite clicks them
    /// by name.
    /// </summary>
    [Fact]
    public async Task With_a_plan_still_to_choose_the_form_says_Continue()
    {
        var flow = Driver(Routes());
        await flow.StartAsync();

        Assert.Contains("Continue", RenderAll(View(flow)));
    }

    [Fact]
    public async Task The_plan_step_shows_the_grid_under_its_heading()
    {
        var flow = Driver(Routes(), settings => settings.PreSelectedTierId = "tier-free");
        await flow.StartAsync();
        await flow.ChangePlanAsync();

        var view = View(flow);
        var markup = RenderAll(view);

        Assert.Contains("data-ww-step=\"plan\"", markup);
        Assert.Contains("Choose a plan", markup);
        Assert.Contains("ww-regsub-step-title", markup);
        Assert.Contains(typeof(PlanGrid), ChildComponents(view));
        Assert.Contains("Back", markup);
    }

    /// <summary>
    /// <c>PlanDefault.Free</c> opens the grid ON the app's free plan — and on nothing else: the
    /// machine's selection is still empty and the plan step is still ahead, because a suggestion
    /// is not a choice. The JS twin is "planDefault 'free' opens the grid on the free plan without
    /// choosing it for them".
    /// </summary>
    [Fact]
    public async Task PlanDefault_Free_opens_the_plan_step_on_the_free_plan_without_choosing_it()
    {
        var flow = Driver(Routes(), settings => settings.PlanDefault = SignupPlanDefault.Free);
        await flow.StartAsync();
        await flow.SubmitFormAsync(Form());

        var view = View(flow, v => v.PlanDefault = SignupPlanDefault.Free);
        var markup = RenderAll(view);

        Assert.Contains("data-ww-step=\"plan\"", markup);
        Assert.Contains("HighlightTierId=\"tier-free\"", markup);

        // A suggestion, not a choice: nothing is selected, and the plan step is where the flow is.
        Assert.Null(flow.State.Selection.TierId);
        Assert.Null(flow.Plan);
        Assert.Equal(SignupStep.Plan, flow.State.Step);
    }

    [Fact]
    public async Task Without_a_default_the_plan_step_opens_on_nothing()
    {
        var flow = Driver(Routes());
        await flow.StartAsync();
        await flow.SubmitFormAsync(Form());

        var markup = RenderAll(View(flow));

        Assert.Contains("data-ww-step=\"plan\"", markup);
        Assert.DoesNotContain("HighlightTierId=\"tier-", markup);
    }

    [Fact]
    public async Task The_pack_step_shows_the_grid_its_heading_and_a_way_past_it()
    {
        var flow = Driver(Routes(), settings =>
        {
            settings.PlanSelection = SignupPlanSelection.Skip;
            settings.PackSelection = SignupPackSelection.Choose;
        });
        await flow.StartAsync();
        await flow.SubmitFormAsync(Form());

        var view = View(flow);
        var markup = RenderAll(view);

        Assert.Contains("data-ww-step=\"packs\"", markup);
        Assert.Contains("Choose your packs", markup);
        Assert.Contains("Skip for now", markup);
        Assert.Contains(typeof(PackGrid), ChildComponents(view));
    }

    /// <summary>
    /// Pay-first: the card is asked for with the order summary above it, priced off the live
    /// catalog, and the customer's email goes to the payment component so the transaction can be
    /// linked to the account that does not exist yet.
    /// </summary>
    [Fact]
    public async Task The_payment_step_summarises_the_order_at_the_live_price()
    {
        var flow = Driver(Routes(), settings => settings.PreSelectedTierId = "tier-pro");
        await flow.StartAsync();
        await flow.SubmitFormAsync(Form());

        var view = View(flow);
        var markup = RenderAll(view);

        Assert.Contains("data-ww-step=\"payment\"", markup);
        Assert.Contains("Order Summary", markup);
        Assert.Contains("Pro", markup);
        Assert.Contains(FormatHelpers.FormatMoney(ProMonthly, "USD"), markup);

        // The frequency is its own text frame beside the slash, so it is asserted on its own.
        Assert.Contains("ww-payment-summary-period", markup);
        Assert.Contains("monthly", markup);

        // The trial line says what is due today, which is nothing.
        Assert.Contains(CatalogHelpers.TrialLabel(14), markup);
        Assert.Contains("Due today", markup);
        Assert.Contains(typeof(PaymentComponent), ChildComponents(view));

        // Pay-first, so there is no account yet: the card is taken against the email the form
        // collected, and the transaction is attached to the account afterwards.
        Assert.Contains("CustomerEmail=\"ada@example.test\"", markup);
    }

    [Fact]
    public async Task The_creating_step_says_what_it_is_doing_and_asks_them_to_wait()
    {
        var flow = Driver(Routes(), settings => settings.PreSelectedTierId = "tier-free");
        await flow.StartAsync();

        // Freeze the flow ON the creating step: the form is submitted, but the account creation is
        // still in flight as far as the markup is concerned.
        var state = flow.State;
        typeof(SignupState).GetProperty("Step")!.SetValue(state, SignupStep.Creating);

        var markup = RenderAll(View(flow));

        Assert.Contains("data-ww-step=\"creating\"", markup);
        Assert.Contains("ww-signup-processing", markup);
        Assert.Contains("Please wait while we set up your account.", markup);
    }

    [Fact]
    public async Task The_disclaimers_step_gates_the_signup_behind_the_pending_terms()
    {
        var handler = Routes(login:
            """
            {"jwtToken":"jwt-1","id":"user-1","requiresDisclaimerAcceptance":true,
             "pendingDisclaimers":[{"disclaimerId":"d-1","versionId":"v-1","title":"Terms"}]}
            """);
        var flow = Driver(handler, settings => settings.PreSelectedTierId = "tier-free");
        await flow.StartAsync();
        await flow.SubmitFormAsync(Form());

        var view = View(flow);
        var markup = RenderAll(view);

        Assert.Contains("data-ww-step=\"disclaimers\"", markup);
        Assert.Contains("ww-signup-disclaimers", markup);
        Assert.Contains("One more step", markup);
        Assert.Contains("Please review and accept the following before continuing.", markup);
        Assert.Contains(typeof(DisclaimerComponent), ChildComponents(view));
    }

    [Fact]
    public async Task The_pack_checkout_step_hands_the_basket_to_the_checkout()
    {
        var flow = Driver(Routes(), settings =>
        {
            settings.PreSelectedTierId = "tier-free";
            settings.PreSelectedAddOnIds = new[] { "pack-docs" };
        });
        await flow.StartAsync();
        await flow.SubmitFormAsync(Form());

        var view = View(flow);
        var markup = RenderAll(view);

        Assert.Contains("data-ww-step=\"packCheckout\"", markup);
        Assert.Contains(typeof(PackCheckout), ChildComponents(view));
    }

    [Fact]
    public async Task The_failed_step_says_what_went_wrong_and_offers_both_ways_out()
    {
        var flow = Driver(Routes(login: "{}"), settings => settings.PreSelectedTierId = "tier-free");
        await flow.StartAsync();
        await flow.SubmitFormAsync(Form());

        var markup = RenderAll(View(flow));

        Assert.Contains("data-ww-step=\"failed\"", markup);
        Assert.Contains("Something Went Wrong", markup);
        Assert.Contains("Try Again", markup);
        Assert.Contains("Start Over", markup);
        Assert.Contains("Login failed after registration. Please try logging in manually.", markup);
    }

    [Fact]
    public async Task The_success_panel_names_the_plan_and_offers_the_way_on()
    {
        var flow = Driver(Routes(), settings => settings.PreSelectedTierId = "tier-free");
        await flow.StartAsync();
        await flow.SubmitFormAsync(Form());

        var view = View(flow);
        var markup = RenderAll(view);

        Assert.Contains("data-ww-step=\"success\"", markup);
        Assert.Contains("ww-signup-success", markup);
        Assert.Contains("You're All Set!", markup);

        // A plan WAS chosen - the app's free one - so the copy is the active-plan copy, not the
        // no-plan one.
        Assert.Contains("Your account has been created and your plan is active.", markup);
        Assert.Contains("Get Started", markup);
        Assert.Contains(typeof(PackOutcomeList), ChildComponents(view));
    }

    /// <summary>
    /// A paid plan with a trial says how long the trial is; a token grant says the token's plan
    /// instead. Both come out of one decision, so they cannot drift apart.
    /// </summary>
    [Theory]
    [InlineData(false, false, 0, "Your account has been created and your plan is active.")]
    [InlineData(false, false, 14, "Your account has been created and your 14-day free trial has started.")]
    [InlineData(false, true, 0, "Your account is ready! Plan activation is pending - you can select a plan from your dashboard.")]
    [InlineData(true, false, 0, "Your account has been created with the Granted Pro from your registration token.")]
    public void The_success_copy_says_what_actually_happened(
        bool granted, bool subscriptionFailed, int trialDays, string expected)
    {
        var grant = granted
            ? new RegistrationTokenAppGrant { AppId = "app-1", AppTierId = "tier-pro", AppTierName = "Granted Pro" }
            : null;

        var message = SignupViewDecisions.SuccessMessage(
            RegistrationSubscriptionLabels.Defaults, grant, hasPlan: true, subscriptionFailed, trialDays);

        Assert.Equal(expected, message);
    }

    [Fact]
    public void The_success_copy_without_a_plan_says_only_that_the_account_exists()
    {
        var message = SignupViewDecisions.SuccessMessage(
            RegistrationSubscriptionLabels.Defaults, null, hasPlan: false, subscriptionFailed: false, trialDays: 0);

        Assert.Equal("Your account has been created successfully.", message);
    }

    /// <summary>
    /// The plan the flow is carrying is matched by the SAME case-insensitive rule the preset plan
    /// uses. A selection can hold a link's casing for a GUID rather than the server's, and an
    /// exact match would then lose the plan entirely: no summary card, no price, and a payment
    /// step with nothing to charge for.
    /// </summary>
    [Theory]
    [InlineData("tier-pro")]
    [InlineData("TIER-PRO")]
    [InlineData("Tier-Pro")]
    public void The_carried_plan_is_matched_however_its_id_is_cased(string tierId)
    {
        var catalog = new PublicCatalog
        {
            Tiers = new List<AppTierModel>
            {
                new AppTierModel
                {
                    Id = "tier-pro",
                    Name = "Pro",
                    PricingOptions = new List<AppTierPricingModel>
                    {
                        new AppTierPricingModel { Id = "price-pro", Price = ProMonthly, IsDefault = true }
                    }
                }
            }
        };

        var plan = SignupViewDecisions.ResolvePlan(catalog, tierId, "price-pro");

        Assert.NotNull(plan);
        Assert.Equal("tier-pro", plan!.Tier.Id);
        Assert.Equal(ProMonthly, plan.Pricing!.Price);
    }

    #endregion

    #region The parts

    [Fact]
    public void The_closed_notice_says_so_and_links_out_in_a_new_tab()
    {
        var notice = new ClosedNotice
        {
            Message = RegistrationSubscriptionLabels.Defaults.RegistrationClosed,
            ContactUrl = "https://example.test/sales",
            ContactLabel = RegistrationSubscriptionLabels.Defaults.ContactUs
        };

        var markup = RenderAll(notice);

        Assert.Contains("ww-regsub-closed", markup);
        Assert.Contains("role=\"status\"", markup);
        Assert.Contains("Registration is closed", markup);
        Assert.Contains("Contact us", markup);
        Assert.Contains("target=\"_blank\"", markup);
        Assert.Contains("rel=\"noopener noreferrer\"", markup);
    }

    [Fact]
    public void The_closed_notice_renders_no_link_without_a_contact_url()
    {
        var notice = new ClosedNotice { Message = "Registration is closed" };

        Assert.DoesNotContain("href", RenderAll(notice));
    }

    /// <summary>
    /// A registration token's plan is never priced: the visitor is not paying for it, and a price
    /// beside it would say they were.
    /// </summary>
    [Fact]
    public void The_token_plan_summary_lists_what_the_token_grants_without_a_price()
    {
        var summary = new TokenPlanSummary
        {
            Grant = new RegistrationTokenAppGrant
            {
                AppId = "app-1",
                AppTierId = "tier-pro",
                AppTierName = "Granted Pro",
                PricingName = "Annual",
                AddOnIds = new List<string> { "pack-docs" },
                AddOnNames = new List<string> { "Docs Pack" },
                FeatureCodes = new List<string> { "DOCUMENTS" },
                FeatureNames = new List<string> { "Documents" }
            }
        };

        var markup = RenderAll(summary);

        Assert.Contains("ww-token-plan-summary", markup);
        Assert.Contains("Your registration token includes", markup);
        Assert.Contains("Granted Pro", markup);
        Assert.Contains("Annual", markup);
        Assert.Contains("Packs", markup);
        Assert.Contains("Docs Pack", markup);
        Assert.Contains("Features", markup);
        Assert.Contains("Documents", markup);

        foreach (var character in RenderText(summary))
        {
            Assert.False(char.IsDigit(character), "a granted plan was priced");
        }
    }

    [Fact]
    public void The_plan_summary_card_quotes_the_live_price_and_the_trial()
    {
        var tier = new AppTierModel
        {
            Id = "tier-pro",
            Name = "Pro",
            Description = "Everything, for teams.",
            Currency = "USD"
        };
        var pricing = new AppTierPricingModel
        {
            Id = "price-pro", Price = ProMonthly, BillingFrequency = "Monthly", TrialDays = 14
        };

        var card = new PlanSummaryCard { Tier = tier, Pricing = pricing, Currency = "USD" };
        var markup = RenderAll(card);

        Assert.Contains("ww-plan-summary-card", markup);
        Assert.Contains("Pro", markup);
        Assert.Contains("Everything, for teams.", markup);
        Assert.Contains(FormatHelpers.FormatMoney(ProMonthly, "USD"), markup);
        Assert.Contains("ww-price-period", markup);
        Assert.Contains("monthly", markup);
        Assert.Contains(CatalogHelpers.TrialLabel(14), markup);

        // Read-only until the host offers a way back to the grid.
        Assert.DoesNotContain("Change plan", markup);
    }

    [Fact]
    public void The_plan_summary_card_offers_a_way_back_to_the_grid_when_the_host_wires_one()
    {
        var card = new PlanSummaryCard
        {
            Tier = new AppTierModel { Id = "tier-pro", Name = "Pro" },
            OnChangePlan = EventCallback.Factory.Create(new object(), () => { })
        };

        var markup = RenderAll(card);

        Assert.Contains("ww-plan-change-link", markup);
        Assert.Contains("Change plan", markup);
    }

    /// <summary>A free plan says so where a price would go, rather than inventing a zero.</summary>
    [Fact]
    public void The_plan_summary_card_says_Free_for_a_free_plan_with_no_pricing()
    {
        var card = new PlanSummaryCard
        {
            Tier = new AppTierModel { Id = "tier-free", Name = "Starter", IsFreeTier = true }
        };

        var text = RenderText(card);

        Assert.Contains("Free", text);
        foreach (var character in text)
        {
            Assert.False(char.IsDigit(character), "a plan with no pricing showed a figure");
        }
    }

    [Fact]
    public void The_order_summary_quotes_the_server_and_names_the_saved_card()
    {
        var quote = new AddOnCheckoutQuoteModel
        {
            Success = true,
            CheckoutId = "co-1",
            Currency = "USD",
            TotalDueToday = 0m,
            Lines =
            {
                new AddOnCheckoutQuoteLineModel
                {
                    AddOnId = "pack-docs", PricingId = "ao-docs", Name = "Docs Pack",
                    Price = PackPrice, TrialDays = 14, TrialEligible = true
                }
            },
            SavedCard = new AddOnCheckoutSavedCardModel { Brand = "visa", Last4 = "4242" }
        };

        var summary = new OrderSummary { Quote = quote };
        var markup = RenderAll(summary);

        Assert.Contains("ww-regsub-order-summary", markup);
        Assert.Contains("Order Summary", markup);
        Assert.Contains("data-ww-pack=\"pack-docs\"", markup);
        Assert.Contains("Docs Pack", markup);
        Assert.Contains(FormatHelpers.FormatMoney(PackPrice, "USD"), markup);
        Assert.Contains(CatalogHelpers.TrialLabel(14), markup);
        Assert.Contains("Due today", markup);
        Assert.Contains(FormatHelpers.FormatMoney(0m, "USD"), markup);
        Assert.Contains("visa ending in 4242", markup);
    }

    [Fact]
    public void The_pack_outcome_list_says_what_became_of_every_pack()
    {
        var list = new PackOutcomeList
        {
            Packs = new List<SignupPackOutcome>
            {
                new SignupPackOutcome
                {
                    AddOnId = "pack-docs", Name = "Docs Pack", Status = SignupPackStatuses.Granted
                },
                new SignupPackOutcome
                {
                    AddOnId = "pack-radar", Name = "Radar Pack", Status = SignupPackStatuses.Failed,
                    ErrorMessage = "Your card was declined."
                }
            }
        };

        var markup = RenderAll(list);

        Assert.Contains("ww-pack-outcome-list", markup);
        Assert.Contains("ww-pack-outcome ww-pack-outcome--granted", markup);
        Assert.Contains("ww-pack-outcome ww-pack-outcome--failed", markup);
        Assert.Contains("data-ww-pack=\"pack-docs\"", markup);
        Assert.Contains("Included", markup);
        Assert.Contains("Could not be added", markup);
        Assert.Contains("Your card was declined.", markup);
    }

    [Fact]
    public void The_pack_outcome_list_renders_nothing_when_there_are_no_packs()
    {
        var list = new PackOutcomeList { Packs = new List<SignupPackOutcome>() };

        Assert.Equal(string.Empty, RenderAll(list).Trim());
    }

    #endregion
}
