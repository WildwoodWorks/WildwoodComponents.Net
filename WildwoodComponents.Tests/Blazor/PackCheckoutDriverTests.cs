using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Components.RegistrationSubscription;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.RegistrationSubscription;
using WildwoodComponents.Shared.Utilities;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// Buying a basket of packs: one quote, one card at most, one purchase, then any pack the bank
/// wants authenticated, one at a time.
/// </summary>
/// <remarks>
/// Ported from packages/wildwood-react/src/__tests__/RegistrationAndSubscription.packCheckout.test.tsx.
/// The driver is exercised directly — the repo has no bUnit renderer — with the real app-tier
/// service over a scripted transport, so the requests asserted here are the requests the server
/// would receive.
/// </remarks>
public class PackCheckoutDriverTests
{
    private const string QuoteRoute = "checkout/quote";
    private const string PaymentMethodRoute = "checkout/payment-method";
    private const string CompleteRoute = "checkout/complete";

    /// <summary>The purchase itself: /checkout with no suffix.</summary>
    private static bool IsPurchase(ScriptedHttpMessageHandler.RecordedRequest request)
    {
        return request.Url.EndsWith("/checkout", StringComparison.OrdinalIgnoreCase);
    }

    #region Harness

    /// <summary>
    /// A payment-action adapter that records what it was asked to confirm, can be held open
    /// mid-challenge, and owns a disposable "instance" the way the Stripe one does.
    /// </summary>
    private sealed class RecordingPaymentActions : IPaymentActionAdapter, IAsyncDisposable
    {
        private readonly Func<string, PaymentActionOutcome> _answer;

        public RecordingPaymentActions(Func<string, PaymentActionOutcome>? answer = null)
        {
            _answer = answer ?? (_ => PaymentActionOutcome.Success);
        }

        public List<string> Confirmed { get; } = new();

        public List<string?> Keys { get; } = new();

        /// <summary>Closed, a confirmation waits here: a customer part-way through a bank challenge.</summary>
        public TaskCompletionSource? Gate { get; set; }

        /// <summary>Signalled once a confirmation is actually waiting on <see cref="Gate"/>.</summary>
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>How many times the instance was dropped. Once, however the teardown went.</summary>
        public int Disposals { get; private set; }

        /// <summary>Set to make the teardown itself blow up, which must fault nothing.</summary>
        public Exception? DisposeThrows { get; set; }

        /// <summary>Read at disposal, so "dropped only after the drain" is a fact rather than an order of calls.</summary>
        public Func<int>? CompletionsSoFar { get; set; }

        /// <summary>What <see cref="CompletionsSoFar"/> said when the instance was dropped.</summary>
        public int CompletionsAtDisposal { get; private set; } = -1;

        public async Task<PaymentActionOutcome> ConfirmPaymentAsync(string clientSecret, string? publishableKey)
        {
            Confirmed.Add(clientSecret);
            Keys.Add(publishableKey);

            var gate = Gate;
            if (gate is not null)
            {
                Reached.TrySetResult();
                await gate.Task;
            }

            return _answer(clientSecret);
        }

        public ValueTask DisposeAsync()
        {
            Disposals++;
            if (CompletionsSoFar is not null) CompletionsAtDisposal = CompletionsSoFar();

            if (DisposeThrows is not null) throw DisposeThrows;
            return default;
        }
    }

    /// <summary>
    /// The real app-tier service with a gate on each of the two calls a teardown can land inside.
    /// </summary>
    /// <remarks>
    /// Same trick as <see cref="ThrowingCompleteAppTier"/>: re-declaring the interface on a
    /// subclass re-maps it, so the driver's interface call reaches the gate and everything else
    /// stays the real service over the scripted transport. The gate is taken BEFORE the request is
    /// made, so "the call was in flight" and "the request had not been sent" are both true — which
    /// is exactly the window a navigation lands in.
    /// </remarks>
    private sealed class GatedAppTier : AppTierComponentService, IAppTierComponentService
    {
        public GatedAppTier(HttpClient client)
            : base(client, NullLogger<AppTierComponentService>.Instance)
        {
        }

        public TaskCompletionSource? QuoteGate { get; set; }

        public TaskCompletionSource QuoteReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource? CheckoutGate { get; set; }

        public TaskCompletionSource CheckoutReached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<AddOnCheckoutQuoteModel> IAppTierComponentService.QuoteAddOnCheckoutAsync(
            string appId, IReadOnlyList<AddOnCheckoutItemInput>? items)
        {
            var gate = QuoteGate;
            if (gate is not null)
            {
                QuoteReached.TrySetResult();
                await gate.Task;
            }

            return await base.QuoteAddOnCheckoutAsync(appId, items);
        }

        async Task<AddOnCheckoutResultModel> IAppTierComponentService.CheckoutAddOnsAsync(
            string appId, AddOnCheckoutRequestModel request)
        {
            var gate = CheckoutGate;
            if (gate is not null)
            {
                CheckoutReached.TrySetResult();
                await gate.Task;
            }

            return await base.CheckoutAddOnsAsync(appId, request);
        }
    }

    /// <summary>
    /// The real app-tier service with the pack-completion call made to throw.
    /// </summary>
    /// <remarks>
    /// The service swallows its own failures, so a refusal cannot be scripted into a throw over
    /// the transport — and a hand-written stub would be 61 members of NotSupportedException.
    /// Re-declaring the interface on a subclass re-maps it, so the explicit implementation below
    /// is what the driver's interface call reaches while every other method stays the real one.
    /// </remarks>
    private sealed class ThrowingCompleteAppTier : AppTierComponentService, IAppTierComponentService
    {
        public ThrowingCompleteAppTier(HttpClient client)
            : base(client, NullLogger<AppTierComponentService>.Instance)
        {
        }

        Task<AddOnCheckoutItemResultModel> IAppTierComponentService.CompleteAddOnCheckoutAsync(
            string appId, string paymentTransactionId)
        {
            throw new InvalidOperationException("the completion blew up");
        }
    }

    private sealed class Harness
    {
        public ScriptedHttpMessageHandler Handler { get; } = new();

        public FakePaymentProviderService Payments { get; } = new();

        public RecordingPaymentActions Actions { get; set; } = new();

        /// <summary>Builds the app-tier service the driver is given. Overridden to make it throw.</summary>
        public Func<HttpClient, IAppTierComponentService> AppTier { get; set; } =
            client => new AppTierComponentService(client, NullLogger<AppTierComponentService>.Instance);

        public List<IReadOnlyList<SignupPackOutcome>> Finished { get; } = new();

        public List<(string Code, string Message)> Errors { get; } = new();

        /// <summary>Every re-render the driver asked the view for.</summary>
        public int Renders { get; private set; }

        /// <summary>Every entitlement invalidation the driver made on its own authority.</summary>
        public List<EntitlementsChangedEventArgs> Entitlements { get; } = new();

        /// <summary>The gated app-tier service, once <see cref="WithGates"/> has been called.</summary>
        public GatedAppTier Gated { get; private set; } = default!;

        public PackCheckoutDriver Driver { get; private set; } = default!;

        /// <summary>Puts a gate on the quote and the purchase, so a teardown can land inside either.</summary>
        public Harness WithGates()
        {
            AppTier = client => Gated = new GatedAppTier(client);
            return this;
        }

        public PackCheckoutDriver Build(params string[] addOnIds)
        {
            var items = new List<AddOnCheckoutItemInput>();
            foreach (var id in addOnIds) items.Add(new AddOnCheckoutItemInput { AddOnId = id });

            // The real service: Invalidate is a cache drop and an event, with no HTTP surface of
            // its own, so the reason asserted here is the reason a host would receive.
            var entitlements = new FeatureEntitlementService(
                new AppTierComponentService(Handler.CreateClient(), NullLogger<AppTierComponentService>.Instance),
                NullLogger<FeatureEntitlementService>.Instance);
            entitlements.EntitlementsChangedDetailed += args => Entitlements.Add(args);

            Driver = new PackCheckoutDriver(
                AppTier(Handler.CreateClient()),
                Payments,
                Actions,
                new PackCheckoutSettings
                {
                    AppId = "app-1",
                    Items = items,
                    Names = new Dictionary<string, string>(StringComparer.Ordinal) { ["radar"] = "Radar" },
                    Labels = RegistrationSubscriptionLabels.Defaults
                },
                entitlements,
                NullLogger.Instance)
            {
                StateChanged = () => Renders++,
                Finished = outcomes =>
                {
                    Finished.Add(outcomes);
                    return Task.CompletedTask;
                },
                ErrorReported = (code, message) =>
                {
                    Errors.Add((code, message));
                    return Task.CompletedTask;
                }
            };

            return Driver;
        }
    }

    private static Harness WithStripeProvider(Harness harness)
    {
        harness.Payments.Configuration = new AppPaymentConfigurationDto
        {
            Providers =
            {
                new PaymentProviderDto { Id = "prov-1", IsEnabled = true, PublishableKey = "pk_test_1" }
            }
        };

        return harness;
    }

    private static JsonElement Body(ScriptedHttpMessageHandler.RecordedRequest request)
        => JsonDocument.Parse(request.Body ?? "{}").RootElement;

    private const string SavedCardQuote =
        """
        {"success":true,"checkoutId":"co-1","providerId":"prov-1","currency":"USD",
         "lines":[{"addOnId":"radar","pricingId":"ap-1","name":"Radar","price":19,
                   "billingFrequency":"Monthly","trialDays":14,"trialEligible":true,"dueToday":0}],
         "totalDueToday":0,"requiresPaymentMethod":false,
         "savedCard":{"brand":"visa","last4":"4242"}}
        """;

    private const string NewCardQuote =
        """
        {"success":true,"checkoutId":"co-1","providerId":"prov-1","currency":"USD",
         "lines":[{"addOnId":"radar","pricingId":"ap-1","name":"Radar","price":19,
                   "billingFrequency":"Monthly","trialDays":0,"trialEligible":false,"dueToday":19}],
         "totalDueToday":19,"requiresPaymentMethod":true}
        """;

    #endregion

    #region A card on file

    [Fact]
    public async Task ChargesTheSavedCard_InOneQuoteAndOneCheckout_AndReportsEachPack()
    {
        var harness = new Harness();
        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On("app-1/checkout",
                """{"success":true,"checkoutId":"co-1","results":[{"addOnId":"radar","pricingId":"ap-1","status":"trialing","subscriptionId":"sub-1"}]}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();

        Assert.Equal(PackCheckoutStep.Done, driver.Step);
        Assert.Equal(1, harness.Handler.Count(QuoteRoute));
        Assert.Equal(0, harness.Handler.Count(PaymentMethodRoute));

        var purchase = Assert.Single(harness.Handler.Requests.FindAll(IsPurchase));
        Assert.True(Body(purchase).GetProperty("UseSavedCard").GetBoolean());
        Assert.Equal("co-1", Body(purchase).GetProperty("CheckoutId").GetString());

        var outcomes = Assert.Single(harness.Finished);
        var pack = Assert.Single(outcomes);
        Assert.Equal("radar", pack.AddOnId);
        Assert.Equal("Radar", pack.Name);
        Assert.Equal(SignupPackStatuses.Trialing, pack.Status);
    }

    /// <summary>
    /// The step tokens' whole job: a second start (a re-render, a double click, a doubled effect)
    /// lands on a token the machine has moved past.
    /// </summary>
    [Fact]
    public async Task QuotesOnceAndBuysOnce_HoweverManyTimesItIsStarted()
    {
        var harness = new Harness();
        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On("app-1/checkout",
                """{"success":true,"checkoutId":"co-1","results":[{"addOnId":"radar","status":"active"}]}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();
        await driver.StartAsync();

        Assert.Equal(1, harness.Handler.Count(QuoteRoute));
        Assert.Single(harness.Handler.Requests.FindAll(IsPurchase));
        Assert.Single(harness.Finished);
    }

    [Fact]
    public async Task AQuoteTheServerRefuses_FailsWithItsReason()
    {
        var harness = new Harness();
        harness.Handler.On(QuoteRoute, HttpStatusCode.BadRequest,
            """
            {"success":false,"checkoutId":"co-2","currency":"USD","lines":[],"totalDueToday":0,
             "requiresPaymentMethod":false,"errorCode":"AlreadySubscribed",
             "errorMessage":"You already have that pack"}
            """);
        var driver = harness.Build("radar");

        await driver.StartAsync();

        Assert.Equal(PackCheckoutStep.Failed, driver.Step);
        Assert.Equal("You already have that pack", driver.State.Error);
        Assert.Contains(harness.Errors, error => error.Code == PackCheckoutDriver.QuoteFailedCode);
        Assert.Empty(harness.Finished);
    }

    /// <summary>
    /// Skipping is not forgetting: the signup still finishes, and every pack that was asked for is
    /// reported as failed with the reason.
    /// </summary>
    [Fact]
    public async Task SkippingABasketThatCannotBePriced_StillSaysWhatHappened()
    {
        var harness = new Harness();
        harness.Handler.On(QuoteRoute, HttpStatusCode.BadRequest,
            """
            {"success":false,"currency":"USD","lines":[],"totalDueToday":0,"requiresPaymentMethod":false,
             "errorMessage":"The packs could not be priced."}
            """);
        var driver = harness.Build("radar", "vault");

        await driver.StartAsync();
        await driver.SkipAsync();
        await driver.SkipAsync();

        var outcomes = Assert.Single(harness.Finished);
        Assert.Equal(2, outcomes.Count);
        foreach (var pack in outcomes)
        {
            Assert.Equal(SignupPackStatuses.Failed, pack.Status);
            Assert.Equal("The packs could not be priced.", pack.ErrorMessage);
        }
    }

    #endregion

    #region Collecting a card

    [Fact]
    public async Task AsksForTheCardOnce_AndBuysTheBasketWithIt()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(QuoteRoute, NewCardQuote)
            .On(PaymentMethodRoute,
                """{"success":true,"clientSecret":"seti_1_secret","setupIntentId":"seti_1","paymentTransactionId":"txn-1"}""")
            .On("app-1/checkout",
                """{"success":true,"checkoutId":"co-1","results":[{"addOnId":"radar","status":"active"}]}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();

        // The flow stops at the card and waits to be told, exactly as the web hook's
        // hostCollectsCard does.
        Assert.Equal(PackCheckoutStep.CollectingCard, driver.Step);
        Assert.Equal("seti_1_secret", driver.CardClientSecret);
        Assert.Equal("pk_test_1", driver.PublishableKey);
        Assert.Empty(harness.Handler.Requests.FindAll(IsPurchase));

        await driver.CardConfirmedAsync();

        Assert.Equal(PackCheckoutStep.Done, driver.Step);
        var purchase = Assert.Single(harness.Handler.Requests.FindAll(IsPurchase));
        Assert.False(Body(purchase).GetProperty("UseSavedCard").GetBoolean());
        Assert.Equal("txn-1", Body(purchase).GetProperty("PaymentTransactionId").GetString());
        Assert.Equal(1, harness.Handler.Count(PaymentMethodRoute));
    }

    /// <summary>
    /// The secret the failed attempt was holding belongs to that attempt; confirming it again
    /// confirms the wrong intent, so a retry asks the server for a fresh one.
    /// </summary>
    [Fact]
    public async Task RetryingTheCard_AsksTheServerForAFreshSetupIntent()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(QuoteRoute, NewCardQuote)
            .On(PaymentMethodRoute, attempt => (HttpStatusCode.OK,
                $$"""{"success":true,"clientSecret":"seti_{{attempt}}_secret","paymentTransactionId":"txn-{{attempt}}"}"""))
            .On("app-1/checkout",
                """{"success":true,"checkoutId":"co-1","results":[{"addOnId":"radar","status":"active"}]}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();
        Assert.Equal("seti_0_secret", driver.CardClientSecret);

        await driver.CardFailedAsync("Your card was declined.");
        Assert.Equal(PackCheckoutStep.Failed, driver.Step);
        Assert.Contains(harness.Errors, error => error.Code == PackCheckoutDriver.CardFailedCode);

        await driver.RetryAsync();

        Assert.Equal(PackCheckoutStep.CollectingCard, driver.Step);
        Assert.Equal("seti_1_secret", driver.CardClientSecret);
        Assert.Equal(2, harness.Handler.Count(PaymentMethodRoute));
    }

    /// <summary>
    /// With no Stripe account to mount a card field on, the SetupIntent is never asked for: one
    /// created first would sit on the customer's account with nobody able to confirm it, and the
    /// checkout stops either way.
    /// </summary>
    [Fact]
    public async Task WithNoPublishableKey_NoSetupIntentIsCreatedAtAll()
    {
        // No provider configuration at all: nothing to mount a card field with.
        var harness = new Harness();
        harness.Handler
            .On(QuoteRoute, NewCardQuote)
            .On(PaymentMethodRoute,
                """{"success":true,"clientSecret":"seti_1_secret","paymentTransactionId":"txn-1"}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();

        Assert.Equal(0, harness.Handler.Count(PaymentMethodRoute));
        Assert.Equal(PackCheckoutStep.Failed, driver.Step);
        Assert.Equal(PackCheckoutDriver.CardUnavailable, driver.State.Error);
        Assert.Contains(harness.Errors, error => error.Code == PackCheckoutDriver.CardFailedCode);
        Assert.Empty(harness.Handler.Requests.FindAll(IsPurchase));
    }

    [Fact]
    public async Task ACardTheServerWillNotIssueAnIntentFor_FailsWithTheCardCode()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(QuoteRoute, NewCardQuote)
            .On(PaymentMethodRoute, HttpStatusCode.BadRequest,
                """{"success":false,"errorCode":"ProviderNotAvailable","errorMessage":"No card processor"}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();

        Assert.Equal(PackCheckoutStep.Failed, driver.Step);
        Assert.Equal("No card processor", driver.State.Error);
        Assert.Contains(harness.Errors, error => error.Code == PackCheckoutDriver.CardFailedCode);
        Assert.Empty(harness.Handler.Requests.FindAll(IsPurchase));
    }

    #endregion

    #region Authentication

    [Fact]
    public async Task AuthenticatesEachPackTheBankAskedAbout_InOrder_AndCompletesIt()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On(CompleteRoute, attempt => (HttpStatusCode.OK,
                attempt == 0
                    ? """{"addOnId":"radar","status":"active"}"""
                    : """{"addOnId":"vault","status":"trialing"}"""))
            .On("app-1/checkout",
                """
                {"success":true,"checkoutId":"co-1","results":[
                  {"addOnId":"radar","status":"requires_action","clientSecret":"pi_radar","paymentTransactionId":"txn-radar"},
                  {"addOnId":"vault","status":"requires_action","clientSecret":"pi_vault","paymentTransactionId":"txn-vault"}]}
                """);
        var driver = harness.Build("radar", "vault");

        await driver.StartAsync();

        Assert.Equal(PackCheckoutStep.Done, driver.Step);
        Assert.Equal(new[] { "pi_radar", "pi_vault" }, harness.Actions.Confirmed);
        Assert.Equal(2, harness.Handler.Count(CompleteRoute));

        var completes = harness.Handler.All(CompleteRoute);
        Assert.Equal("txn-radar", Body(completes[0]).GetProperty("PaymentTransactionId").GetString());
        Assert.Equal("txn-vault", Body(completes[1]).GetProperty("PaymentTransactionId").GetString());

        var outcomes = Assert.Single(harness.Finished);
        Assert.Equal(SignupPackStatuses.Active, outcomes[0].Status);
        Assert.Equal(SignupPackStatuses.Trialing, outcomes[1].Status);

        // The publishable key is looked up once and reused for every challenge.
        Assert.Equal(1, harness.Payments.ConfigurationReads);
    }

    /// <summary>A basket is not a transaction: one refusal must not take the others with it.</summary>
    [Fact]
    public async Task OnePackCanFail_WithoutTakingTheOthersWithIt()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Actions = new RecordingPaymentActions(secret =>
            secret == "pi_radar" ? PaymentActionOutcome.Failed("Your card was declined.") : PaymentActionOutcome.Success);

        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On(CompleteRoute, """{"addOnId":"vault","status":"active"}""")
            .On("app-1/checkout",
                """
                {"success":true,"checkoutId":"co-1","results":[
                  {"addOnId":"radar","status":"requires_action","clientSecret":"pi_radar","paymentTransactionId":"txn-radar"},
                  {"addOnId":"vault","status":"requires_action","clientSecret":"pi_vault","paymentTransactionId":"txn-vault"}]}
                """);
        var driver = harness.Build("radar", "vault");

        await driver.StartAsync();

        Assert.Equal(PackCheckoutStep.Done, driver.Step);

        // The refused pack is never completed; the other one is.
        Assert.Equal(1, harness.Handler.Count(CompleteRoute));

        var outcomes = Assert.Single(harness.Finished);
        Assert.Equal(SignupPackStatuses.Failed, outcomes[0].Status);
        Assert.Equal("Your card was declined.", outcomes[0].ErrorMessage);
        Assert.Equal(SignupPackStatuses.Active, outcomes[1].Status);
    }

    /// <summary>
    /// Completing is the step AFTER the bank said yes, and the service it calls swallows its own
    /// failures — so anything that throws there is a surprise. A surprise must still come out as
    /// that pack failing: escaping the pump would strand the basket with no outcome for any pack
    /// in it, and take the signup's own await down with it.
    /// </summary>
    [Fact]
    public async Task AnUnexpectedThrowWhileCompleting_FailsThatPack_RatherThanEscapingThePump()
    {
        var harness = WithStripeProvider(new Harness());
        harness.AppTier = client => new ThrowingCompleteAppTier(client);
        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On("app-1/checkout",
                """
                {"success":true,"checkoutId":"co-1","results":[
                  {"addOnId":"radar","status":"requires_action","clientSecret":"pi_radar","paymentTransactionId":"txn-radar"},
                  {"addOnId":"vault","status":"requires_action","clientSecret":"pi_vault","paymentTransactionId":"txn-vault"}]}
                """);
        var driver = harness.Build("radar", "vault");

        var exception = await Record.ExceptionAsync(() => driver.StartAsync());

        Assert.Null(exception);
        Assert.Equal(PackCheckoutStep.Done, driver.Step);

        // Both packs were authenticated, both completions blew up, and both are reported.
        var outcomes = Assert.Single(harness.Finished);
        Assert.Equal(2, outcomes.Count);
        foreach (var pack in outcomes)
        {
            Assert.Equal(SignupPackStatuses.Failed, pack.Status);
            Assert.Equal("the completion blew up", pack.ErrorMessage);
        }
    }

    [Fact]
    public async Task WithNoPublishableKey_ThePackIsReportedUnconfirmed_AndTheBasketFinishes()
    {
        var harness = new Harness();
        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On("app-1/checkout",
                """{"success":true,"checkoutId":"co-1","results":[{"addOnId":"radar","status":"requires_action","clientSecret":"pi_radar"}]}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();

        Assert.Equal(PackCheckoutStep.Done, driver.Step);
        Assert.Empty(harness.Actions.Confirmed);

        var outcomes = Assert.Single(harness.Finished);
        Assert.Equal(SignupPackStatuses.Failed, outcomes[0].Status);
        Assert.Equal(PackCheckoutDriver.PackUnconfirmed, outcomes[0].ErrorMessage);
    }

    #endregion

    #region Teardown: detach, then drain

    /// <summary>Two packs bought, both of which the bank wants authenticated.</summary>
    private const string BothNeedAuthenticating =
        """
        {"success":true,"checkoutId":"co-1","results":[
          {"addOnId":"radar","status":"requires_action","clientSecret":"pi_radar","paymentTransactionId":"txn-radar"},
          {"addOnId":"vault","status":"requires_action","clientSecret":"pi_vault","paymentTransactionId":"txn-vault"}]}
        """;

    /// <summary>A basket where one pack went straight through and the other needs the bank.</summary>
    private const string OneThroughOneNeedsAuthenticating =
        """
        {"success":true,"checkoutId":"co-1","results":[
          {"addOnId":"radar","status":"active","subscriptionId":"sub-radar"},
          {"addOnId":"vault","status":"requires_action","clientSecret":"pi_vault","paymentTransactionId":"txn-vault"}]}
        """;

    private static Harness AuthenticatingBothPacks(Func<string, PaymentActionOutcome>? answer = null)
    {
        var harness = WithStripeProvider(new Harness());
        harness.Actions = new RecordingPaymentActions(answer)
        {
            Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        };

        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On(CompleteRoute, """{"addOnId":"radar","status":"active","subscriptionId":"sub-radar"}""")
            .On("app-1/checkout", BothNeedAuthenticating);

        return harness;
    }

    /// <summary>
    /// The one that cost money. A bank challenge is a live interop call that teardown neither
    /// cancels nor waits for, so the customer can still say yes minutes after the page has gone —
    /// and at that point the card IS charged. Abandoning the answer would leave a pack paid for
    /// and never granted, with nothing said either way, so the completion is DRAINED.
    /// </summary>
    [Fact]
    public async Task TornDownMidChallenge_TheBankSaysYes_TheCompletionIsStillMade()
    {
        var harness = AuthenticatingBothPacks();
        harness.Actions.CompletionsSoFar = () => harness.Handler.Count(CompleteRoute);
        var driver = harness.Build("radar", "vault");

        var run = driver.StartAsync();
        await harness.Actions.Reached.Task;

        // The host page navigates away while the customer is with their bank.
        await driver.DisposeAsync();
        var rendersAtTeardown = harness.Renders;

        harness.Actions.Gate!.SetResult();
        await run;

        // The money moved, so the pack is finished on the server: exactly once.
        Assert.Equal(1, harness.Handler.Count(CompleteRoute));
        Assert.Equal(
            "txn-radar",
            Body(harness.Handler.Single(CompleteRoute)).GetProperty("PaymentTransactionId").GetString());

        // The pack that still needed a person is left where it is — no challenge, no completion.
        Assert.Equal(new[] { "pi_radar" }, harness.Actions.Confirmed);
        Assert.Equal(PackCheckoutStep.Authenticating, driver.Step);

        // Nothing was said to a view that has gone.
        Assert.Equal(rendersAtTeardown, harness.Renders);
        Assert.Empty(harness.Finished);
        Assert.Empty(harness.Errors);

        // The script instance outlives the challenge it is running and goes only once the drain is over.
        Assert.Equal(1, harness.Actions.Disposals);
        Assert.Equal(1, harness.Actions.CompletionsAtDisposal);
    }

    /// <summary>
    /// The drained purchase granted an add-on, and the callback that normally tells the rest of the
    /// app was cleared by the teardown — so the driver tells the entitlement cache itself.
    /// </summary>
    [Fact]
    public async Task ADrainedCompletion_StillDropsTheEntitlementCache()
    {
        var harness = AuthenticatingBothPacks();
        var driver = harness.Build("radar", "vault");

        var run = driver.StartAsync();
        await harness.Actions.Reached.Task;
        await driver.DisposeAsync();
        harness.Actions.Gate!.SetResult();
        await run;

        var invalidation = Assert.Single(harness.Entitlements);
        Assert.Equal("app-1", invalidation.AppId);
        Assert.Equal(EntitlementsChangedReasons.AddOn, invalidation.Reason);
    }

    /// <summary>
    /// The ordinary path hands the outcome to the host, whose own flow invalidates: saying it twice
    /// for one purchase would be noise.
    /// </summary>
    [Fact]
    public async Task AnOutcomeTheHostReceived_IsNotInvalidatedTwice()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On("app-1/checkout",
                """{"success":true,"checkoutId":"co-1","results":[{"addOnId":"radar","status":"active"}]}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();
        await driver.DisposeAsync();

        Assert.Single(harness.Finished);
        Assert.Empty(harness.Entitlements);
    }

    /// <summary>A refused challenge moved no money, so there is nothing to finish and nothing to announce.</summary>
    [Fact]
    public async Task TornDownMidChallenge_TheBankSaysNo_NothingIsCompleted()
    {
        var harness = AuthenticatingBothPacks(_ => PaymentActionOutcome.Failed("Your card was declined."));
        var driver = harness.Build("radar", "vault");

        var run = driver.StartAsync();
        await harness.Actions.Reached.Task;
        await driver.DisposeAsync();
        harness.Actions.Gate!.SetResult();
        await run;

        Assert.Equal(0, harness.Handler.Count(CompleteRoute));
        Assert.Equal(new[] { "pi_radar" }, harness.Actions.Confirmed);
        Assert.Empty(harness.Finished);
        Assert.Empty(harness.Entitlements);

        // The instance still goes: nothing is running on it any more.
        Assert.Equal(1, harness.Actions.Disposals);
    }

    /// <summary>
    /// Teardown while the basket is being PRICED. The quote is applied when it lands — the state
    /// stays true — but the purchase it would have led to is new work for a visitor who has gone,
    /// so it is never issued.
    /// </summary>
    [Fact]
    public async Task TornDownBeforeThePurchaseWasIssued_NothingIsBought()
    {
        var harness = WithStripeProvider(new Harness()).WithGates();
        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On("app-1/checkout",
                """{"success":true,"checkoutId":"co-1","results":[{"addOnId":"radar","status":"active"}]}""");
        var driver = harness.Build("radar");
        harness.Gated.QuoteGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = driver.StartAsync();
        await harness.Gated.QuoteReached.Task;
        await driver.DisposeAsync();
        harness.Gated.QuoteGate.SetResult();
        await run;

        Assert.Empty(harness.Handler.Requests.FindAll(IsPurchase));
        Assert.Equal(PackCheckoutStep.Quoted, driver.Step);
        Assert.Empty(harness.Finished);
        Assert.Empty(harness.Entitlements);
        Assert.Equal(1, harness.Actions.Disposals);
    }

    /// <summary>
    /// Teardown while the purchase is in flight. That one HAS been issued, so its answer is applied:
    /// the pack the server granted outright is granted, and the pack that still wants a person is
    /// left unchallenged.
    /// </summary>
    [Fact]
    public async Task TornDownWhileBuying_TheResultIsApplied_AndNoChallengeIsPutToAnyone()
    {
        var harness = WithStripeProvider(new Harness()).WithGates();
        harness.Handler
            .On(QuoteRoute, SavedCardQuote)
            .On(CompleteRoute, """{"addOnId":"vault","status":"active"}""")
            .On("app-1/checkout", OneThroughOneNeedsAuthenticating);
        var driver = harness.Build("radar", "vault");
        harness.Gated.CheckoutGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var run = driver.StartAsync();
        await harness.Gated.CheckoutReached.Task;
        await driver.DisposeAsync();
        harness.Gated.CheckoutGate.SetResult();
        await run;

        // The purchase answered and the answer was kept.
        Assert.Single(harness.Handler.Requests.FindAll(IsPurchase));
        Assert.Equal(2, driver.State.Results.Count);
        Assert.Equal(AddOnCheckoutItemStatuses.Active, driver.State.Results[0].Status);

        // Nothing was put to the customer, and nothing was completed on their behalf.
        Assert.Empty(harness.Actions.Confirmed);
        Assert.Equal(0, harness.Handler.Count(CompleteRoute));
        Assert.Equal(PackCheckoutStep.Authenticating, driver.Step);
        Assert.Empty(harness.Finished);

        // The pack the server did grant changed what the account is entitled to.
        var invalidation = Assert.Single(harness.Entitlements);
        Assert.Equal(EntitlementsChangedReasons.AddOn, invalidation.Reason);
    }

    /// <summary>
    /// A drain runs after the component has gone, so the task carrying it may have nobody left to
    /// await it: a throw on the way out would become an unobserved task exception rather than an
    /// error anyone sees. Everything the drain's tail does is therefore swallowed and logged.
    /// </summary>
    [Fact]
    public async Task ADrainWhoseTeardownBlowsUp_FaultsNothing_AndStillCompletesThePack()
    {
        var harness = AuthenticatingBothPacks();
        harness.Actions.DisposeThrows = new InvalidOperationException("the instance blew up on the way out");
        var driver = harness.Build("radar", "vault");

        var run = driver.StartAsync();
        await harness.Actions.Reached.Task;
        await driver.DisposeAsync();
        harness.Actions.Gate!.SetResult();

        var exception = await Record.ExceptionAsync(() => run);

        Assert.Null(exception);
        Assert.Equal(1, harness.Handler.Count(CompleteRoute));
        Assert.Equal(1, harness.Actions.Disposals);
    }

    /// <summary>
    /// Teardown with nothing in flight does not wait for a drain that cannot happen: the script
    /// instance goes there and then, which is what <c>PackCheckout</c>'s own disposal relies on.
    /// </summary>
    [Fact]
    public async Task TornDownWithNothingInFlight_DropsTheInstanceAtOnce()
    {
        var harness = WithStripeProvider(new Harness());
        harness.Handler
            .On(QuoteRoute, NewCardQuote)
            .On(PaymentMethodRoute,
                """{"success":true,"clientSecret":"seti_1_secret","paymentTransactionId":"txn-1"}""");
        var driver = harness.Build("radar");

        await driver.StartAsync();
        Assert.Equal(PackCheckoutStep.CollectingCard, driver.Step);

        await driver.DisposeAsync();
        Assert.Equal(1, harness.Actions.Disposals);

        // And the card the view can no longer collect is not bought with.
        await driver.CardConfirmedAsync();
        Assert.Empty(harness.Handler.Requests.FindAll(IsPurchase));
    }

    #endregion

    #region Outcomes

    /// <summary>
    /// One outcome per REQUESTED pack, whatever the server answered about: a pack the purchase
    /// never mentioned is reported as failed rather than quietly dropped.
    /// </summary>
    [Fact]
    public void ToOutcomes_NamesEveryRequestedPack_EvenTheOnesTheServerNeverMentioned()
    {
        var items = new List<AddOnCheckoutItemInput>
        {
            new AddOnCheckoutItemInput { AddOnId = "radar" },
            new AddOnCheckoutItemInput { AddOnId = "vault" }
        };

        var results = new List<AddOnCheckoutItemResultModel>
        {
            new AddOnCheckoutItemResultModel { AddOnId = "radar", Status = AddOnCheckoutItemStatuses.Active }
        };

        var quote = new AddOnCheckoutQuoteModel
        {
            Success = true,
            Lines = { new AddOnCheckoutQuoteLineModel { AddOnId = "vault", Name = "Vault" } }
        };

        var outcomes = PackCheckoutDriver.ToOutcomes(items, results, quote, null, "nothing was bought");

        Assert.Equal(2, outcomes.Count);
        Assert.Equal(SignupPackStatuses.Active, outcomes[0].Status);

        // Unnamed by the catalog, so the quote's own name carries it.
        Assert.Equal("radar", outcomes[0].Name);
        Assert.Equal("Vault", outcomes[1].Name);
        Assert.Equal(SignupPackStatuses.Failed, outcomes[1].Status);
        Assert.Equal("nothing was bought", outcomes[1].ErrorMessage);
    }

    #endregion
}
