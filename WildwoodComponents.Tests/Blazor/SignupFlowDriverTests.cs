using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;
using static WildwoodComponents.Blazor.Components.Registration.TokenRegistrationComponent;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The signup flow, driven: the registration mode and the catalog, what a signup link already
/// chose, the token check, the pay-first order, and the outcome.
/// </summary>
/// <remarks>
/// <para>
/// Ported from packages/wildwood-react/src/__tests__/RegistrationAndSubscription.signup.test.tsx.
/// React drives its flow through a rendered view; this repo has no bUnit renderer and no
/// hook-testing library, so the driver is exercised directly — over the REAL services and a
/// scripted transport, so what is asserted is what the server would be asked.
/// </para>
/// <para>
/// Only the session manager and the payment-provider service are faked, because neither has an
/// HTTP surface this flow drives: one is browser storage, the other is asked to link a
/// transaction.
/// </para>
/// </remarks>
public class SignupFlowDriverTests
{
    private const string AuthConfigRoute = "auth-configuration";
    private const string PublicTiersRoute = "app-tiers/app-1/public";
    private const string PublicPacksRoute = "app-tier-addons/app-1/public";
    private const string TokenDetailsRoute = "registrationtokens/validate-detailed";
    private const string RegisterRoute = "userregistration/register";
    private const string LoginRoute = "auth/login";
    private const string SubscribeRoute = "app-tiers/app-1/my-subscription";

    private const decimal ProMonthly = 79m;

    #region Fixtures

    private const string Tiers =
        """
        [
          {"id":"tier-free","name":"Starter","status":"Active","displayOrder":0,"isFreeTier":true,
           "isDefault":true,"currency":"USD","pricingOptions":[]},
          {"id":"tier-pro","name":"Pro","status":"Active","displayOrder":1,"currency":"USD",
           "pricingOptions":[{"id":"price-pro","price":79,"billingFrequency":"Monthly","isDefault":true,"trialDays":14}]}
        ]
        """;

    private const string Packs =
        """
        [
          {"id":"pack-docs","name":"Docs Pack","status":"Active","displayOrder":1,"currency":"USD",
           "pricingOptions":[{"id":"ao-docs","price":9,"billingFrequency":"Monthly","isDefault":true}]},
          {"id":"pack-radar","name":"Radar Pack","status":"Active","displayOrder":2,"currency":"USD",
           "pricingOptions":[{"id":"ao-radar","price":5,"billingFrequency":"Monthly","isDefault":true}]}
        ]
        """;

    private const string LoginOk =
        """{"jwtToken":"jwt-1","id":"user-1","userId":"user-1","email":"ada@example.test"}""";

    private static string AuthConfig(bool open = true, bool token = true)
        => $$"""{"isEnabled":true,"allowOpenRegistration":{{(open ? "true" : "false")}},"allowTokenRegistration":{{(token ? "true" : "false")}}}""";

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

    #endregion

    #region Harness

    private sealed class Harness
    {
        public ScriptedHttpMessageHandler Handler { get; } = new();

        public FakeBlazorSessionManager Session { get; set; } = new();

        public FakePaymentProviderService Payments { get; } = new();

        public List<(string Code, string Message)> Errors { get; } = new();

        public int SignedInNotifications { get; private set; }

        public int EntitlementNotifications { get; private set; }

        public List<SignupOutcome> Completed { get; } = new();

        public int Renders { get; private set; }

        /// <summary>Every entitlement invalidation the driver made on its own authority.</summary>
        public List<EntitlementsChangedEventArgs> Entitlements { get; } = new();

        /// <summary>Set to drive the account creation by hand instead of over the transport.</summary>
        public ISignupAccountCreator? Creator { get; set; }

        public SignupFlowDriver Driver { get; private set; } = default!;

        public SignupFlowDriver Build(Action<SignupFlowSettings>? configure = null)
        {
            var client = Handler.CreateClient();
            var appTier = new AppTierComponentService(client, NullLogger<AppTierComponentService>.Instance);
            var auth = new AuthenticationService(
                client, new FakeLocalStorageService(), NullLogger<AuthenticationService>.Instance);

            var settings = new SignupFlowSettings
            {
                AppId = "app-1",
                Labels = RegistrationSubscriptionLabels.Defaults
            };
            configure?.Invoke(settings);

            var entitlements = new FeatureEntitlementService(appTier, NullLogger<FeatureEntitlementService>.Instance);
            entitlements.EntitlementsChangedDetailed += args => Entitlements.Add(args);

            Driver = new SignupFlowDriver(
                new PublicCatalogService(appTier, NullLogger<PublicCatalogService>.Instance),
                auth,
                entitlements,
                new DisclaimerService(client, NullLogger<DisclaimerService>.Instance),
                Creator ?? new SignupAccountCreator(
                    // Its own client over the same transport, as IHttpClientFactory hands out:
                    // the creator sets a BaseAddress, which a client that has already sent a
                    // request refuses.
                    new FakeHttpClientFactory(Handler.CreateClient()),
                    Options.Create(new WildwoodComponentsOptions { BaseUrl = "https://api.test" }),
                    auth,
                    appTier,
                    Payments,
                    Session,
                    NullLogger<SignupAccountCreator>.Instance),
                Session,
                settings)
            {
                StateChanged = () => Renders++,
                ErrorReported = (code, message) =>
                {
                    Errors.Add((code, message));
                    return Task.CompletedTask;
                },
                AlreadySignedInDetected = () =>
                {
                    SignedInNotifications++;
                    return Task.CompletedTask;
                },
                EntitlementsChanged = () =>
                {
                    EntitlementNotifications++;
                    return Task.CompletedTask;
                },
                SignupCompleted = outcome =>
                {
                    Completed.Add(outcome);
                    return Task.CompletedTask;
                }
            };

            return Driver;
        }
    }

    /// <summary>The routes a happy signup needs, with a catalog of two plans and two packs.</summary>
    private static Harness Ready(bool open = true, bool token = true, string? tiers = null)
    {
        var harness = new Harness();
        harness.Handler
            .On(AuthConfigRoute, AuthConfig(open, token))
            .On(PublicTiersRoute, tiers ?? Tiers)
            .On(PublicPacksRoute, Packs)
            .On(RegisterRoute, """{"success":true,"userId":"user-1"}""")
            .On(LoginRoute, LoginOk)
            .On(SubscribeRoute, """{"success":true}""");

        return harness;
    }

    private static PaymentSuccessEventArgs Paid(string transactionId = "txn-1", string intentId = "pi_1")
        => new PaymentSuccessEventArgs { TransactionId = transactionId, PaymentIntentId = intentId };

    /// <summary>
    /// An account creator that can be held open part-way through, so a teardown can land inside
    /// the one call that registers, signs in, LINKS THE PLAN'S PAYMENT and subscribes.
    /// </summary>
    private sealed class GatedAccountCreator : ISignupAccountCreator
    {
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        /// <summary>The call ran all the way through rather than being abandoned at the gate.</summary>
        public bool Completed { get; private set; }

        public async Task<SignupAccountResult> CreateAsync(SignupAccountRequest request, SignupAccountAttempt attempt)
        {
            Calls++;

            // The real creator reports each sub-step as it starts; the driver turns that into a
            // re-render, which is one of the things a teardown has to silence.
            request.OnStatus?.Invoke("Creating your account");

            Reached.TrySetResult();
            await Gate.Task;

            Completed = true;
            attempt.Registered = true;
            attempt.LoggedIn = true;
            attempt.AuthResponse = new AuthenticationResponse { Id = "user-1", UserId = "user-1", JwtToken = "jwt-1" };

            return new SignupAccountResult
            {
                Success = true,
                UserId = "user-1",
                AuthResponse = attempt.AuthResponse
            };
        }
    }

    #endregion

    #region The way in

    [Fact]
    public async Task OpensTheFormWithTheOptionalTokenCard_WhenTheAppTakesBoth()
    {
        var harness = Ready();
        var driver = harness.Build();

        await driver.StartAsync();

        Assert.Equal(SignupStep.Register, driver.State.Step);
        Assert.True(driver.Mode.AllowOpenRegistration);
        Assert.True(driver.Mode.ShowOptionalTokenEntry);
        Assert.False(driver.Mode.RequireToken);
    }

    [Fact]
    public async Task RequiresTheToken_WhenTheAppOnlyTakesTokenRegistrations()
    {
        var harness = Ready(open: false);
        var driver = harness.Build();

        await driver.StartAsync();

        Assert.Equal(SignupStep.Register, driver.State.Step);
        Assert.True(driver.Mode.RequireToken);
        Assert.False(driver.Mode.AllowOpenRegistration);
    }

    [Fact]
    public async Task SaysRegistrationIsClosed_WhenTheAppTakesNeither()
    {
        var harness = Ready(open: false, token: false);
        var driver = harness.Build();

        await driver.StartAsync();

        Assert.Equal(SignupStep.Closed, driver.State.Step);
    }

    /// <summary>
    /// Invite redemption overrides the configuration: the link carries a token the server will
    /// validate, and an app that has turned registration off still honours its own invitations.
    /// </summary>
    [Fact]
    public async Task AnInviteOverridesAClosedConfiguration_AndSkipsThePlanAndThePacks()
    {
        var harness = Ready(open: false, token: false);
        var driver = harness.Build(settings =>
        {
            settings.TokenMode = SignupTokenMode.Required;
            settings.PackSelection = SignupPackSelection.Choose;
            settings.PreSelectedTierId = "tier-pro";
            settings.PreSelectedAddOnIds = new[] { "pack-docs" };
        });

        await driver.StartAsync();

        Assert.Equal(SignupStep.Register, driver.State.Step);
        Assert.True(driver.Mode.RequireToken);

        // The token decides everything: no plan from the link, and no packs either.
        Assert.Null(driver.State.Selection.TierId);
        Assert.Empty(driver.State.PacksToBuy);

        harness.Handler.On(TokenDetailsRoute, """{"isValid":true,"appGrants":[]}""");
        await driver.SubmitFormAsync(Form("INVITE-1"));

        // Straight past the plan and the packs to creating the account.
        Assert.Equal(1, harness.Handler.Count(RegisterRoute));
    }

    [Fact]
    public async Task HandsASignedInVisitorBackToTheHost_Once()
    {
        var harness = Ready();
        harness.Session = new FakeBlazorSessionManager(signedIn: true);
        var driver = harness.Build();

        await driver.StartAsync();
        await driver.StartAsync();

        Assert.True(driver.AlreadySignedIn);
        Assert.Equal(1, harness.SignedInNotifications);
    }

    /// <summary>
    /// The latch: the sign-in this very flow performs must not look like "you were already signed
    /// in" — which would hide the success panel behind a notice.
    /// </summary>
    [Fact]
    public async Task TheSignInThisFlowPerforms_DoesNotLookLikeAnExistingSession()
    {
        var harness = Ready();
        var driver = harness.Build(settings => settings.PlanSelection = SignupPlanSelection.Skip);

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());

        Assert.True(harness.Session.IsAuthenticated);
        Assert.False(driver.AlreadySignedIn);
        Assert.Equal(0, harness.SignedInNotifications);
    }

    [Fact]
    public async Task AnUnreadableCatalogFails_AndARetryAsksAgain()
    {
        var harness = new Harness();
        harness.Handler
            .On(AuthConfigRoute, AuthConfig())
            .On(PublicTiersRoute, attempt => attempt == 0
                ? (HttpStatusCode.InternalServerError, "{}")
                : (HttpStatusCode.OK, Tiers))
            .On(PublicPacksRoute, Packs);
        var driver = harness.Build();

        await driver.StartAsync();

        Assert.Equal(SignupStep.Failed, driver.State.Step);
        Assert.Equal(SignupStep.Loading, driver.State.RetryFrom);

        await driver.RetryAsync();

        Assert.Equal(SignupStep.Register, driver.State.Step);
        Assert.Equal(2, harness.Handler.Count(PublicTiersRoute));
    }

    #endregion

    #region The plan a link already chose

    [Fact]
    public async Task APreselectedPlanIsCarried_AndThePlanStepIsSkipped()
    {
        var harness = Ready();

        // Matched case-insensitively: the server's casing for an id is not the link's.
        var driver = harness.Build(settings => settings.PreSelectedTierId = "TIER-PRO");

        await driver.StartAsync();

        Assert.Equal("tier-pro", driver.State.Selection.TierId);
        Assert.Equal("price-pro", driver.State.Selection.PricingId);
        Assert.True(driver.State.PlanPreset);
        Assert.True(driver.State.PlanRequiresPayment);
        Assert.False(driver.PlanStepAhead);
        Assert.Equal(ProMonthly, driver.Plan!.Pricing!.Price);
    }

    [Fact]
    public async Task APlanTheAppDoesNotSellIsIgnored()
    {
        var harness = Ready();
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-from-an-old-email");

        await driver.StartAsync();

        Assert.Null(driver.State.Selection.TierId);
        Assert.False(driver.State.PlanPreset);
        Assert.True(driver.PlanStepAhead);
    }

    [Fact]
    public async Task SkipTakesTheAppsDefaultPlan_AndIgnoresTheLink()
    {
        var harness = Ready();
        var driver = harness.Build(settings =>
        {
            settings.PlanSelection = SignupPlanSelection.Skip;
            settings.PreSelectedTierId = "tier-pro";
        });

        await driver.StartAsync();

        Assert.Equal("tier-free", driver.State.Selection.TierId);
        Assert.False(driver.State.PlanRequiresPayment);
    }

    /// <summary>
    /// A signup link is not a shopping cart: unknown packs are dropped and the list is capped.
    /// </summary>
    [Fact]
    public async Task PreselectedPacksAreVettedAgainstTheCatalog_AndCapped()
    {
        var manyPacks = new StringBuilder("[");
        for (var i = 0; i < 30; i++)
        {
            if (i > 0) manyPacks.Append(',');
            manyPacks.Append($$"""{"id":"pack-{{i}}","name":"Pack {{i}}","status":"Active","displayOrder":{{i}},"currency":"USD","pricingOptions":[]}""");
        }

        manyPacks.Append(']');

        var harness = new Harness();
        harness.Handler
            .On(AuthConfigRoute, AuthConfig())
            .On(PublicTiersRoute, Tiers)
            .On(PublicPacksRoute, manyPacks.ToString());

        var asked = new List<string> { "pack-not-sold" };
        for (var i = 0; i < 30; i++) asked.Add($"pack-{i}");

        var driver = harness.Build(settings => settings.PreSelectedAddOnIds = asked);

        await driver.StartAsync();

        Assert.Equal(CatalogHelpers.MaxAddOnSelection, driver.State.PacksToBuy.Count);
        Assert.DoesNotContain("pack-not-sold", driver.State.PacksToBuy);
        Assert.Equal("pack-0", driver.State.PacksToBuy[0]);
    }

    #endregion

    #region Pay first

    /// <summary>
    /// The web order: nothing is created until the money is in, so a declined card leaves nothing
    /// behind rather than an account on a plan nobody paid for.
    /// </summary>
    [Fact]
    public async Task TakesTheCardBeforeTheAccountExists_ThenRegistersLinksAndSubscribesOnce()
    {
        var harness = Ready();
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-pro");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());

        // The form is behind them and the plan costs money, so the card comes next — and nothing
        // has been created.
        Assert.Equal(SignupStep.Payment, driver.State.Step);
        Assert.Equal(0, harness.Handler.Count(RegisterRoute));

        await driver.PaymentSucceededAsync(Paid());

        Assert.Equal(SignupStep.Done, driver.State.Step);
        Assert.Equal(1, harness.Handler.Count(RegisterRoute));
        Assert.Equal(1, harness.Handler.Count(SubscribeRoute));

        var link = Assert.Single(harness.Payments.Links);
        Assert.Equal("pi_1", link.ExternalTransactionId);
    }

    [Fact]
    public async Task AFreePlanTakesNoCardAtAll()
    {
        var harness = Ready();
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());

        Assert.Equal(SignupStep.Done, driver.State.Step);
        Assert.Empty(harness.Payments.Links);
    }

    /// <summary>
    /// The plan step is a detour, not a short cut: choosing there before the form has been
    /// submitted comes back to the form rather than running ahead to a card.
    /// </summary>
    [Fact]
    public async Task ChangingThePlanBeforeTheFormIsFilledIn_ComesBackToTheForm()
    {
        var harness = Ready();
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.ChangePlanAsync();
        Assert.Equal(SignupStep.Plan, driver.State.Step);

        await driver.ChoosePlanAsync(driver.Catalog!.Tiers[1]);

        Assert.Equal(SignupStep.Register, driver.State.Step);
        Assert.Equal("tier-pro", driver.State.Selection.TierId);
        Assert.Equal(0, harness.Handler.Count(RegisterRoute));

        // ...and once the form IS submitted, the changed (paid) plan asks for the card.
        await driver.SubmitFormAsync(Form());
        Assert.Equal(SignupStep.Payment, driver.State.Step);
    }

    [Fact]
    public async Task WhenTheServerRefusesTheSubscription_TheSignupStillFinishes_AndSaysItIsPending()
    {
        var harness = new Harness();
        harness.Handler
            .On(AuthConfigRoute, AuthConfig())
            .On(PublicTiersRoute, Tiers)
            .On(PublicPacksRoute, Packs)
            .On(RegisterRoute, """{"success":true,"userId":"user-1"}""")
            .On(LoginRoute, LoginOk)
            .On(SubscribeRoute, """{"success":false,"errorMessage":"no seats"}""");
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());

        Assert.Equal(SignupStep.Done, driver.State.Step);
        Assert.True(driver.SubscriptionFailed);
    }

    #endregion

    #region Registration tokens

    private const string GrantDetails =
        """
        {"isValid":true,"appGrants":[
          {"appId":"APP-1","appTierName":"Granted Pro","appTierId":"tier-pro","appTierPricingId":"price-pro",
           "addOnIds":["pack-docs"],"addOnNames":["Docs Pack"],"featureCodes":["DOCUMENTS"],"featureNames":["Documents"]}]}
        """;

    /// <summary>
    /// A grant skips the plan AND the card, never self-subscribes over the subscription the token
    /// just created, and drops the granted pack out of what is bought.
    /// </summary>
    [Fact]
    public async Task ATokenGrantSkipsThePlanAndTheCard_AndNeverSubscribesOverIt()
    {
        var harness = Ready();
        harness.Handler.On(TokenDetailsRoute, GrantDetails);

        var driver = harness.Build(settings =>
        {
            settings.PreSelectedTierId = "tier-pro";
            settings.PreSelectedAddOnIds = new[] { "pack-docs", "pack-radar" };
        });

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form("INVITE-1"));

        // App ids match case-insensitively, so the grant was found.
        Assert.NotNull(driver.TokenGrant);
        Assert.Equal("Granted Pro", driver.TokenGrant!.AppTierName);

        // No card was asked for and no plan was bought.
        Assert.Empty(harness.Payments.Links);
        Assert.Equal(0, harness.Handler.Count(SubscribeRoute));

        // The granted pack is not bought again; the other one still is.
        Assert.Equal(new[] { "pack-radar" }, driver.State.PacksToBuy);
        Assert.Equal(SignupStep.PackCheckout, driver.State.Step);

        // The pack the token granted is not on offer either.
        Assert.DoesNotContain(driver.AvailablePacks, pack => pack.Id == "pack-docs");
    }

    [Fact]
    public async Task AGrantedPackLeadsTheOutcome_WithTheGrantedStatus()
    {
        var harness = Ready();
        harness.Handler.On(TokenDetailsRoute, GrantDetails);
        var driver = harness.Build(settings => settings.PreSelectedAddOnIds = new[] { "pack-radar" });

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form("INVITE-1"));

        await driver.PacksBoughtAsync(new List<SignupPackOutcome>
        {
            new SignupPackOutcome
            {
                AddOnId = "pack-radar", Name = "Radar Pack", Status = SignupPackStatuses.Active
            }
        });

        var outcome = driver.Outcome!;
        Assert.Equal("user-1", outcome.UserId);
        Assert.Equal("tier-pro", outcome.Tier!.TierId);
        Assert.Equal("Pro", outcome.Tier.Name);
        Assert.Equal("price-pro", outcome.Tier.PricingId);
        Assert.NotNull(outcome.TokenGrant);

        // Granted packs are listed first, and nothing was charged for them.
        Assert.Equal(2, outcome.Packs.Count);
        Assert.Equal("pack-docs", outcome.Packs[0].AddOnId);
        Assert.Equal(SignupPackStatuses.Granted, outcome.Packs[0].Status);
        Assert.Equal("Docs Pack", outcome.Packs[0].Name);
        Assert.Equal("pack-radar", outcome.Packs[1].AddOnId);
        Assert.Equal(SignupPackStatuses.Active, outcome.Packs[1].Status);
    }

    /// <summary>
    /// A NULL answer is "the details could not be read", not "invalid": the token still grants app
    /// access, so the signup carries on as an ordinary one.
    /// </summary>
    [Fact]
    public async Task UnreadableTokenDetailsAreNotARejection()
    {
        var harness = Ready();
        harness.Handler.On(TokenDetailsRoute, HttpStatusCode.NotFound, "");
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form("OLD-SERVER"));

        Assert.Null(driver.TokenGrant);
        Assert.Null(driver.TokenMessage);
        Assert.Equal(SignupStep.Done, driver.State.Step);
        Assert.Equal(1, harness.Handler.Count(RegisterRoute));
    }

    [Fact]
    public async Task ARejectedTokenGoesBackToTheForm_WithTheServersReason()
    {
        var harness = Ready();
        harness.Handler.On(TokenDetailsRoute,
            """{"isValid":false,"errorMessage":"That token expired last week.","appGrants":[]}""");
        var driver = harness.Build();

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form("STALE"));

        Assert.Equal(SignupStep.Register, driver.State.Step);
        Assert.Equal("That token expired last week.", driver.TokenMessage);
        Assert.Contains(harness.Errors, error => error.Code == SignupViewDecisions.TokenRejectedCode);
        Assert.Equal(0, harness.Handler.Count(RegisterRoute));
    }

    [Fact]
    public async Task ATokenThatGrantsNothingForThisApp_CarriesOnNormally()
    {
        var harness = Ready();
        harness.Handler.On(TokenDetailsRoute,
            """{"isValid":true,"appGrants":[{"appId":"another-app","appTierId":"tier-x"}]}""");
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form("SHARED"));

        Assert.Null(driver.TokenGrant);
        Assert.Equal(SignupStep.Done, driver.State.Step);
    }

    #endregion

    #region Failing, resuming and finishing

    [Fact]
    public async Task AFailedSignupReportsTheServersWords_AndResumesWithoutRegisteringAgain()
    {
        var harness = new Harness();
        harness.Handler
            .On(AuthConfigRoute, AuthConfig())
            .On(PublicTiersRoute, Tiers)
            .On(PublicPacksRoute, Packs)
            .On(RegisterRoute, """{"success":true,"userId":"user-1"}""")
            .On(LoginRoute, attempt => (HttpStatusCode.OK, attempt == 0 ? "{}" : LoginOk))
            .On(SubscribeRoute, """{"success":true}""");
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());

        Assert.Equal(SignupStep.Failed, driver.State.Step);
        Assert.Contains(harness.Errors, error => error.Code == SignupAccountErrorCodes.LoginFailed);

        await driver.RetryAsync();

        Assert.Equal(SignupStep.Done, driver.State.Step);
        Assert.Equal(1, harness.Handler.Count(RegisterRoute));
        Assert.Equal(2, harness.Handler.Count(LoginRoute));
    }

    [Fact]
    public async Task StartingOverGoesBackToTheForm_WithNothingCarriedOver()
    {
        var harness = Ready();
        harness.Handler.On(TokenDetailsRoute, GrantDetails);
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form("INVITE-1"));
        Assert.NotNull(driver.TokenGrant);

        await driver.StartOverAsync();

        Assert.Equal(SignupStep.Register, driver.State.Step);
        Assert.Null(driver.TokenGrant);
        Assert.False(driver.State.FormSubmitted);

        // The mode and the catalog are the ones already read: starting over is not a reload.
        Assert.Equal(1, harness.Handler.Count(PublicTiersRoute));
    }

    /// <summary>
    /// The account's entitlements are announced once, and the outcome is handed over once however
    /// many times the button is pressed.
    /// </summary>
    [Fact]
    public async Task TheFinishedSignupIsAnnouncedExactlyOnce()
    {
        var harness = Ready();
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());

        Assert.Equal(SignupStep.Done, driver.State.Step);
        Assert.Equal(1, harness.EntitlementNotifications);

        await driver.CompleteAsync();
        await driver.CompleteAsync();

        var outcome = Assert.Single(harness.Completed);
        Assert.Equal("user-1", outcome.UserId);
        Assert.Equal("tier-free", outcome.Tier!.TierId);
        Assert.Equal("Starter", outcome.Tier.Name);
        Assert.Empty(outcome.Packs);
        Assert.Null(outcome.TokenGrant);
        Assert.Null(outcome.PlanActivationPending);
    }

    /// <summary>
    /// An account creator that reports each sub-step the way the real one does, and nothing else.
    /// </summary>
    private sealed class StatusReportingCreator : ISignupAccountCreator
    {
        public Task<SignupAccountResult> CreateAsync(SignupAccountRequest request, SignupAccountAttempt attempt)
        {
            var labels = RegistrationSubscriptionLabels.Defaults;
            request.OnStatus?.Invoke(labels.StatusCreatingAccount);
            request.OnStatus?.Invoke(labels.StatusSigningIn);
            request.OnStatus?.Invoke(labels.StatusActivatingPlan);

            attempt.Registered = true;
            attempt.LoggedIn = true;
            attempt.AuthResponse = new AuthenticationResponse { Id = "user-1", UserId = "user-1", JwtToken = "jwt-1" };

            return Task.FromResult(new SignupAccountResult
            {
                Success = true,
                UserId = "user-1",
                AuthResponse = attempt.AuthResponse
            });
        }
    }

    /// <summary>
    /// The machine stays on <c>creating</c> for the whole of the registration, the sign-in and the
    /// subscription, so the status line is the only thing that moves — and it only moves if each
    /// report marks the driver dirty. Without that the panel reads "Creating your account..." while
    /// the plan is being activated, which is what React's own status sequence does not do.
    /// </summary>
    [Fact]
    public async Task TheCreatingStepsStatusLine_AdvancesThroughEachSubStep()
    {
        var harness = Ready();
        harness.Creator = new StatusReportingCreator();
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();

        var seen = new List<string>();
        driver.StateChanged = () => seen.Add(driver.ProcessingStatus);

        await driver.SubmitFormAsync(Form());

        var labels = RegistrationSubscriptionLabels.Defaults;
        var index = seen.IndexOf(labels.StatusCreatingAccount);
        Assert.True(index >= 0, "the status line never said the account was being created");
        Assert.Contains(labels.StatusSigningIn, seen);
        Assert.Contains(labels.StatusActivatingPlan, seen);
        Assert.True(
            seen.IndexOf(labels.StatusSigningIn) > index
            && seen.IndexOf(labels.StatusActivatingPlan) > seen.IndexOf(labels.StatusSigningIn),
            "the status line did not advance in order");
    }

    #endregion

    #region Teardown: detach, not abandon

    /// <summary>
    /// The pay-first order puts the card BEFORE the account, so account creation is the one stretch
    /// where a charge already exists and is not yet attached to anybody. Leaving the page must not
    /// cut that short: the call runs to its end, linking the transaction and starting the plan,
    /// while every re-render and every host callback stays silent.
    /// </summary>
    [Fact]
    public async Task TornDownWhileTheAccountIsBeingCreated_TheCreationStillFinishes_Silently()
    {
        var harness = Ready();
        var creator = new GatedAccountCreator();
        harness.Creator = creator;
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        var submit = driver.SubmitFormAsync(Form());
        await creator.Reached.Task;

        // The visitor navigates away mid-creation.
        driver.Stop();
        var rendersAtTeardown = harness.Renders;

        creator.Gate.SetResult();
        await submit;

        // The sequence ran through rather than being abandoned between the charge and the link.
        Assert.True(creator.Completed);
        Assert.Equal(1, creator.Calls);
        Assert.Equal(SignupStep.Done, driver.State.Step);

        // And nothing was said to a view that has gone.
        Assert.Equal(rendersAtTeardown, harness.Renders);
        Assert.Equal(0, harness.EntitlementNotifications);
        Assert.Empty(harness.Completed);
        Assert.Empty(harness.Errors);
    }

    /// <summary>
    /// The account that got created is entitled to what it signed up for, and the host callback
    /// that normally says so was cleared by the teardown — so the scoped entitlement service is
    /// told directly, and gates elsewhere in the app stop serving the anonymous answer.
    /// </summary>
    [Fact]
    public async Task ADetachedSignupThatCreatedAnAccount_StillDropsTheEntitlementCache()
    {
        var harness = Ready();
        var creator = new GatedAccountCreator();
        harness.Creator = creator;
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        var submit = driver.SubmitFormAsync(Form());
        await creator.Reached.Task;
        driver.Stop();
        creator.Gate.SetResult();
        await submit;

        var invalidation = Assert.Single(harness.Entitlements);
        Assert.Equal("app-1", invalidation.AppId);
        Assert.Equal(EntitlementsChangedReasons.Signup, invalidation.Reason);
    }

    /// <summary>
    /// A teardown with no account behind it has nothing to settle: no invalidation, and above all
    /// no registration started for a visitor who never finished the form.
    /// </summary>
    [Fact]
    public async Task TearingDownBeforeAnyAccountExists_SettlesNothing()
    {
        var harness = Ready();
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        driver.Stop();
        await driver.SubmitFormAsync(Form());

        Assert.Empty(harness.Entitlements);
        Assert.Equal(0, harness.Handler.Count(RegisterRoute));
    }

    /// <summary>
    /// The ordinary path announces the entitlement change once, through the callback: a Stop that
    /// arrives afterwards must not say it a second time.
    /// </summary>
    [Fact]
    public async Task AFinishedSignupIsNotInvalidatedAgainOnTeardown()
    {
        var harness = Ready();
        var driver = harness.Build(settings => settings.PreSelectedTierId = "tier-free");

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());
        Assert.Equal(1, harness.EntitlementNotifications);

        driver.Stop();
        driver.Stop();

        Assert.Single(harness.Entitlements);
    }

    #endregion

    #region Packs on the way in

    [Fact]
    public async Task ThePackStepIsOffered_AndTheTickedPacksAreCarriedIntoTheCheckout()
    {
        var harness = Ready();
        var driver = harness.Build(settings =>
        {
            settings.PlanSelection = SignupPlanSelection.Skip;
            settings.PackSelection = SignupPackSelection.Choose;
        });

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());

        Assert.Equal(SignupStep.Packs, driver.State.Step);

        await driver.TogglePackAsync(driver.Catalog!.AddOns[1]);
        await driver.ChoosePacksAsync();

        Assert.Equal(SignupStep.PackCheckout, driver.State.Step);
        var item = Assert.Single(driver.CheckoutItems);
        Assert.Equal("pack-radar", item.AddOnId);
    }

    [Fact]
    public async Task SkippingThePackStepFinishesWithNoPacks()
    {
        var harness = Ready();
        var driver = harness.Build(settings =>
        {
            settings.PlanSelection = SignupPlanSelection.Skip;
            settings.PackSelection = SignupPackSelection.Choose;
        });

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());
        await driver.SkipPacksAsync();

        Assert.Equal(SignupStep.Done, driver.State.Step);
        Assert.Empty(driver.Outcome!.Packs);
    }

    /// <summary>
    /// packSelection 'none' removes the STEP, not the packs: what a signup link chose is still
    /// bought.
    /// </summary>
    [Fact]
    public async Task WithNoPackStep_TheLinksPacksAreStillBought()
    {
        var harness = Ready();
        var driver = harness.Build(settings =>
        {
            settings.PlanSelection = SignupPlanSelection.Skip;
            settings.PackSelection = SignupPackSelection.None;
            settings.PreSelectedAddOnIds = new[] { "pack-docs" };
        });

        await driver.StartAsync();
        await driver.SubmitFormAsync(Form());

        Assert.Equal(SignupStep.PackCheckout, driver.State.Step);
        Assert.Equal(new[] { "pack-docs" }, driver.State.PacksToBuy);
    }

    #endregion
}
