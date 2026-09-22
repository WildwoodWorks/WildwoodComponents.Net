using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using WildwoodComponents.Blazor.Extensions;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;
using static WildwoodComponents.Blazor.Components.Registration.TokenRegistrationComponent;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The four server calls that turn a filled-in form into a signed-in account on a plan, lifted out
/// of <c>SignupWithSubscriptionComponent.ProcessSignupAsync</c> so both it and the new signup view
/// create accounts by one route.
/// </summary>
/// <remarks>
/// Ported case for case from the parts of
/// packages/wildwood-react/src/__tests__/RegistrationAndSubscription.signup.test.tsx that exercise
/// <c>runSignup</c>: the order of the calls, the token path, the grant that suppresses the
/// self-subscribe, the refusal that is never fatal, and the retry that resumes rather than
/// registering the same person again.
/// </remarks>
public class SignupAccountCreatorTests
{
    private const string OpenRegisterRoute = "userregistration/register";
    private const string TokenRegisterRoute = "userregistration/register-with-token";
    private const string LoginRoute = "auth/login";
    private const string SubscribeRoute = "app-tiers/app-1/my-subscription";

    private const string LoginOk =
        """{"jwtToken":"jwt-1","id":"user-1","userId":"user-1","email":"ada@example.test"}""";

    #region Harness

    private sealed class Harness
    {
        public ScriptedHttpMessageHandler Handler { get; } = new();

        public FakePaymentProviderService Payments { get; } = new();

        public FakeBlazorSessionManager Session { get; } = new();

        public SignupAccountCreator Creator { get; private set; } = default!;

        public SignupAccountAttempt Attempt { get; } = new();

        public List<string> Statuses { get; } = new();

        public Harness Build()
        {
            var client = Handler.CreateClient();
            var auth = new AuthenticationService(
                client, new FakeLocalStorageService(), NullLogger<AuthenticationService>.Instance);
            var appTier = new AppTierComponentService(client, NullLogger<AppTierComponentService>.Instance);

            Creator = new SignupAccountCreator(
                // Its own client over the same transport, as IHttpClientFactory hands out.
                new FakeHttpClientFactory(Handler.CreateClient()),
                Options.Create(new WildwoodComponentsOptions { BaseUrl = "https://api.test" }),
                auth,
                appTier,
                Payments,
                Session,
                NullLogger<SignupAccountCreator>.Instance);

            return this;
        }

        public SignupAccountRequest Request(
            string? token = null,
            string? tierId = "tier-pro",
            string? pricingId = "price-pro",
            string? paymentTransactionId = null,
            string? paymentExternalId = null,
            RegistrationTokenAppGrant? grant = null)
        {
            return new SignupAccountRequest
            {
                AppId = "app-1",
                FormData = new RegistrationFormData
                {
                    FirstName = "Ada",
                    LastName = "Lovelace",
                    Username = "ada",
                    Email = "ada@example.test",
                    Password = "Analytical1!",
                    Token = token
                },
                TierId = tierId,
                PricingId = pricingId,
                PaymentTransactionId = paymentTransactionId,
                PaymentExternalId = paymentExternalId,
                TokenGrant = grant,
                OnStatus = status => Statuses.Add(status)
            };
        }
    }

    private static Harness Ready(string? loginJson = null)
    {
        var harness = new Harness();
        harness.Handler
            .On(TokenRegisterRoute, """{"success":true,"userId":"user-1"}""")
            .On(OpenRegisterRoute, """{"success":true,"userId":"user-1"}""")
            .On(LoginRoute, loginJson ?? LoginOk)
            .On(SubscribeRoute, """{"success":true}""");

        return harness.Build();
    }

    private static JsonElement Body(ScriptedHttpMessageHandler.RecordedRequest request)
        => JsonDocument.Parse(request.Body ?? "{}").RootElement;

    #endregion

    [Fact]
    public async Task CreateAsync_Registers_SignsIn_LinksThePayment_AndSubscribes_InThatOrder()
    {
        var harness = Ready();

        var result = await harness.Creator.CreateAsync(
            harness.Request(paymentTransactionId: "txn-1", paymentExternalId: "pi_1"),
            harness.Attempt);

        Assert.True(result.Success);
        Assert.Equal("user-1", result.UserId);

        var register = harness.Handler.IndexOf(OpenRegisterRoute);
        var login = harness.Handler.IndexOf(LoginRoute);
        var subscribe = harness.Handler.IndexOf(SubscribeRoute);
        Assert.True(register >= 0 && register < login, "the account is created before it is signed in");
        Assert.True(login < subscribe, "the plan is started as the signed-in user");

        // The server looks a pre-account transaction up by the PROVIDER's id, not ours.
        var link = Assert.Single(harness.Payments.Links);
        Assert.Equal("pi_1", link.ExternalTransactionId);
        Assert.Equal("user-1", link.UserId);

        // ...and subscribes with the Wildwood transaction id.
        Assert.Equal("txn-1", Body(harness.Handler.Single(SubscribeRoute)).GetProperty("PaymentTransactionId").GetString());
        Assert.Equal("tier-pro", Body(harness.Handler.Single(SubscribeRoute)).GetProperty("AppTierId").GetString());

        // The session is stored, so the disclaimers step's accepts are authenticated.
        Assert.Equal(1, harness.Session.LoginCalls);
    }

    [Fact]
    public async Task CreateAsync_SaysWhatItIsDoing_InTheSharedWords()
    {
        var harness = Ready();

        await harness.Creator.CreateAsync(harness.Request(), harness.Attempt);

        var labels = RegistrationSubscriptionLabels.Defaults;
        Assert.Equal(
            new[] { labels.StatusCreatingAccount, labels.StatusSigningIn, labels.StatusActivatingPlan },
            harness.Statuses);
    }

    [Fact]
    public async Task CreateAsync_WithATokenInTheForm_RegistersThroughTheTokenRoute()
    {
        var harness = Ready();

        await harness.Creator.CreateAsync(harness.Request(token: "INVITE-1"), harness.Attempt);

        var request = harness.Handler.Single(TokenRegisterRoute);

        // PostAsJsonAsync serialises with the web defaults, so this body is camelCase — unlike the
        // subscribe call below it, which the app-tier service writes in PascalCase.
        Assert.Equal("INVITE-1", Body(request).GetProperty("token").GetString());

        // One registration call in total: the open route was not also taken.
        Assert.Equal(1, harness.Handler.Count("userregistration/"));
    }

    /// <summary>
    /// A token that carries a plan already subscribed the account. Subscribing over it REPLACES
    /// that subscription, cancelling the plan the token had just created.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WithATokenGrant_NeverSubscribesOverIt()
    {
        var harness = Ready();
        var grant = new RegistrationTokenAppGrant { AppId = "app-1", AppTierId = "tier-granted" };

        var result = await harness.Creator.CreateAsync(
            harness.Request(token: "INVITE-1", tierId: "tier-granted", grant: grant),
            harness.Attempt);

        Assert.True(result.Success);
        Assert.Equal(0, harness.Handler.Count(SubscribeRoute));
        Assert.DoesNotContain(RegistrationSubscriptionLabels.Defaults.StatusActivatingPlan, harness.Statuses);
    }

    [Fact]
    public async Task CreateAsync_WithNoPlanChosen_SubscribesToNothing()
    {
        var harness = Ready();

        var result = await harness.Creator.CreateAsync(
            harness.Request(tierId: null, pricingId: null), harness.Attempt);

        Assert.True(result.Success);
        Assert.Equal(0, harness.Handler.Count(SubscribeRoute));
    }

    /// <summary>
    /// Never fatal: the account exists and the money is taken, so the signup finishes and the
    /// success copy says the plan's activation is pending.
    /// </summary>
    [Fact]
    public async Task CreateAsync_WhenTheSubscriptionIsRefused_StillSucceeds_AndSaysSo()
    {
        var harness = new Harness();
        harness.Handler
            .On(OpenRegisterRoute, """{"success":true,"userId":"user-1"}""")
            .On(LoginRoute, LoginOk)
            .On(SubscribeRoute, """{"success":false,"errorMessage":"no seats left"}""");
        harness.Build();

        var result = await harness.Creator.CreateAsync(harness.Request(), harness.Attempt);

        Assert.True(result.Success);
        Assert.True(result.SubscriptionFailed);
        Assert.True(harness.Attempt.SubscriptionFailed);
    }

    /// <summary>A payment that cannot be linked is not a failed signup either.</summary>
    [Fact]
    public async Task CreateAsync_WhenTheLinkThrows_StillSucceeds()
    {
        var harness = Ready();
        harness.Payments.LinkThrows = new InvalidOperationException("link exploded");

        var result = await harness.Creator.CreateAsync(
            harness.Request(paymentTransactionId: "txn-1"), harness.Attempt);

        Assert.True(result.Success);
    }

    [Fact]
    public async Task CreateAsync_ReportsTheServerRefusal_WithTheRegistrationRefusedCode()
    {
        var harness = new Harness();
        harness.Handler.On(OpenRegisterRoute, """{"success":false,"message":"That email is taken."}""");
        harness.Build();

        var result = await harness.Creator.CreateAsync(harness.Request(), harness.Attempt);

        Assert.False(result.Success);
        Assert.Equal("That email is taken.", result.ErrorMessage);
        Assert.Equal(SignupAccountErrorCodes.RegistrationRefused, result.ErrorCode);
        Assert.False(harness.Attempt.Registered);
        Assert.Equal(0, harness.Handler.Count(LoginRoute));
    }

    [Fact]
    public async Task CreateAsync_ReportsAnHttpRefusal_InTheServersOwnWords()
    {
        var harness = new Harness();
        harness.Handler.On(OpenRegisterRoute, HttpStatusCode.Forbidden, """{"message":"Registration is closed."}""");
        harness.Build();

        var result = await harness.Creator.CreateAsync(harness.Request(), harness.Attempt);

        Assert.False(result.Success);
        Assert.Equal("Registration is closed.", result.ErrorMessage);
    }

    /// <summary>
    /// The rule that makes "Try Again" safe: the account that was already created is not created
    /// again, and the card that was already charged is not charged again.
    /// </summary>
    [Fact]
    public async Task CreateAsync_RetryingAfterAFailedLogin_DoesNotRegisterASecondTime()
    {
        var harness = new Harness();
        harness.Handler
            .On(OpenRegisterRoute, """{"success":true,"userId":"user-1"}""")
            .On(LoginRoute, attempt => (HttpStatusCode.OK, attempt == 0 ? "{}" : LoginOk))
            .On(SubscribeRoute, """{"success":true}""");
        harness.Build();

        var first = await harness.Creator.CreateAsync(
            harness.Request(paymentTransactionId: "txn-1", paymentExternalId: "pi_1"), harness.Attempt);

        Assert.False(first.Success);
        Assert.Equal(SignupAccountErrorCodes.LoginFailed, first.ErrorCode);
        Assert.True(harness.Attempt.Registered);

        var second = await harness.Creator.CreateAsync(
            harness.Request(paymentTransactionId: "txn-1", paymentExternalId: "pi_1"), harness.Attempt);

        Assert.True(second.Success);
        Assert.Equal(1, harness.Handler.Count(OpenRegisterRoute));
        Assert.Equal(2, harness.Handler.Count(LoginRoute));

        // One charge, one link: the retry attached the same transaction rather than taking another.
        Assert.Single(harness.Payments.Links);
        Assert.Equal(1, harness.Handler.Count(SubscribeRoute));
    }

    [Fact]
    public async Task CreateAsync_CarriesThePendingDisclaimersTheSignInReported()
    {
        var harness = new Harness();
        harness.Handler
            .On(OpenRegisterRoute, """{"success":true,"userId":"user-1"}""")
            .On(LoginRoute,
                """
                {"jwtToken":"jwt-1","id":"user-1","requiresDisclaimerAcceptance":true,
                 "pendingDisclaimers":[{"disclaimerId":"d-1","versionId":"v-1","title":"Terms"}]}
                """)
            .On(SubscribeRoute, """{"success":true}""");
        harness.Build();

        var result = await harness.Creator.CreateAsync(harness.Request(), harness.Attempt);

        Assert.True(result.RequiresDisclaimers);
        Assert.Single(result.PendingDisclaimers!);
    }

    [Fact]
    public async Task CreateAsync_ClearsThePassword_OnceTheSessionExists()
    {
        var harness = Ready();
        var request = harness.Request();

        await harness.Creator.CreateAsync(request, harness.Attempt);

        Assert.Null(request.FormData.Password);
    }
}
