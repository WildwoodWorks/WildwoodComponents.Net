using WildwoodComponents.Blazor.Components.Payment;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Blazor;

/// <summary>
/// The rules PaymentComponent applies before money moves. Ported with the JS commits they come
/// from: f8b095f (save the card for a free trial, retry the same intent, confirm the server's id),
/// 3298f70 (ask before charging when the trial isn't available), 1236503 (offer it again for
/// another plan) and d7eae5a (validate and send the billing address).
/// </summary>
public class PaymentDecisionsTests
{
    private static InitiatePaymentResponse Response(
        string? clientSecret = "cs_test",
        string? clientSecretType = null,
        string? paymentIntentId = null,
        string? subscriptionId = null) => new InitiatePaymentResponse
        {
            Success = true,
            ClientSecret = clientSecret,
            ClientSecretType = clientSecretType,
            PaymentIntentId = paymentIntentId,
            SubscriptionId = subscriptionId
        };

    #region Same-intent retry

    [Fact]
    public void IntentKey_coversEverythingThatWouldMakeTheServerCreateADifferentIntent()
    {
        var baseline = PaymentDecisions.BuildIntentKey("prov", "pm-1", 49.99m, isSubscription: true);

        Assert.NotEqual(baseline, PaymentDecisions.BuildIntentKey("other", "pm-1", 49.99m, true));
        Assert.NotEqual(baseline, PaymentDecisions.BuildIntentKey("prov", "pm-2", 49.99m, true));
        Assert.NotEqual(baseline, PaymentDecisions.BuildIntentKey("prov", "pm-1", 59.99m, true));
        Assert.NotEqual(baseline, PaymentDecisions.BuildIntentKey("prov", "pm-1", 49.99m, false));
        Assert.Equal(baseline, PaymentDecisions.BuildIntentKey("prov", "pm-1", 49.99m, true));
    }

    /// <summary>The amount must not be formatted by the host's culture, or a German host would key
    /// "49,99" and a US one "49.99" for the same payment.</summary>
    [Fact]
    public void IntentKey_formatsTheAmountInvariantly()
    {
        Assert.Contains("49.99", PaymentDecisions.BuildIntentKey("prov", "pm-1", 49.99m, true));
    }

    [Fact]
    public void CanReuseIntent_isTrueForTheSamePlanSoADeclinedCardDoesNotCreateASecondSubscription()
    {
        var key = PaymentDecisions.BuildIntentKey("prov", "pm-1", 49.99m, true);

        Assert.True(PaymentDecisions.CanReuseIntent(key, Response(), key));
    }

    [Fact]
    public void CanReuseIntent_isFalseWithNoStoredIntent_orForAnotherPlan()
    {
        var key = PaymentDecisions.BuildIntentKey("prov", "pm-1", 49.99m, true);
        var otherKey = PaymentDecisions.BuildIntentKey("prov", "pm-2", 49.99m, true);

        Assert.False(PaymentDecisions.CanReuseIntent(key, null, key));
        Assert.False(PaymentDecisions.CanReuseIntent(null, Response(), key));
        Assert.False(PaymentDecisions.CanReuseIntent(otherKey, Response(), key));
    }

    #endregion

    #region SetupIntent branch

    /// <summary>
    /// The live money bug: a trial was reported as a success with no card attached, so there was
    /// nothing to charge when it ended. The flag is what makes the server answer with a SetupIntent.
    /// </summary>
    [Fact]
    public void ShouldRequestSetupIntent_onlyForAStripePaymentThatIsOfferingATrial()
    {
        Assert.True(PaymentDecisions.ShouldRequestSetupIntent(isStripe: true, hasTrial: true));
        Assert.False(PaymentDecisions.ShouldRequestSetupIntent(isStripe: true, hasTrial: false));
        Assert.False(PaymentDecisions.ShouldRequestSetupIntent(isStripe: false, hasTrial: true));
        Assert.False(PaymentDecisions.ShouldRequestSetupIntent(isStripe: false, hasTrial: false));
    }

    [Fact]
    public void IsSetupIntentResponse_selectsTheCardSetupBranchOnlyForASetupIntentSecret()
    {
        Assert.True(PaymentDecisions.IsSetupIntentResponse(
            Response(clientSecretType: PaymentClientSecretTypes.SetupIntent)));

        Assert.False(PaymentDecisions.IsSetupIntentResponse(
            Response(clientSecretType: PaymentClientSecretTypes.PaymentIntent)));
        Assert.False(PaymentDecisions.IsSetupIntentResponse(Response(clientSecretType: null)));
        Assert.False(PaymentDecisions.IsSetupIntentResponse(
            Response(clientSecret: null, clientSecretType: PaymentClientSecretTypes.SetupIntent)));
        Assert.False(PaymentDecisions.IsSetupIntentResponse(null));
    }

    #endregion

    #region Trial unavailable

    /// <summary>
    /// WildwoodAPI gives each account one trial per app. A repeat upgrade comes back as a charge,
    /// and the card handed over for a "free trial" must not be charged on that click.
    /// </summary>
    [Fact]
    public void ShouldAskBeforeCharging_whenATrialWasOfferedAndTheServerWantsAChargeToday()
    {
        Assert.True(PaymentDecisions.ShouldAskBeforeCharging(
            hasTrial: true, isStripe: true, Response(clientSecretType: PaymentClientSecretTypes.PaymentIntent)));
        Assert.True(PaymentDecisions.ShouldAskBeforeCharging(
            hasTrial: true, isStripe: true, Response(clientSecretType: null)));
    }

    [Fact]
    public void ShouldAskBeforeCharging_isFalseWhenTheTrialActuallyApplies()
    {
        Assert.False(PaymentDecisions.ShouldAskBeforeCharging(
            hasTrial: true, isStripe: true, Response(clientSecretType: PaymentClientSecretTypes.SetupIntent)));
    }

    [Fact]
    public void ShouldAskBeforeCharging_isFalseWhenNoTrialWasOffered_soAPlainChargeIsNotInterrupted()
    {
        Assert.False(PaymentDecisions.ShouldAskBeforeCharging(
            hasTrial: false, isStripe: true, Response(clientSecretType: PaymentClientSecretTypes.PaymentIntent)));
        Assert.False(PaymentDecisions.ShouldAskBeforeCharging(
            hasTrial: true, isStripe: false, Response(clientSecretType: PaymentClientSecretTypes.PaymentIntent)));
        Assert.False(PaymentDecisions.ShouldAskBeforeCharging(
            hasTrial: true, isStripe: true, Response(clientSecret: null)));
    }

    [Fact]
    public void PlanChanged_isTrueForANewPricingModel_trialLength_orAmount()
    {
        Assert.True(PaymentDecisions.PlanChanged("pm-1", 14, 49.99m, "pm-2", 14, 49.99m));
        Assert.True(PaymentDecisions.PlanChanged("pm-1", 14, 49.99m, "pm-1", 30, 49.99m));
        Assert.True(PaymentDecisions.PlanChanged("pm-1", 14, 49.99m, "pm-1", 14, 59.99m));
        Assert.True(PaymentDecisions.PlanChanged("pm-1", 14, 49.99m, "pm-1", null, 49.99m));
    }

    [Fact]
    public void PlanChanged_isFalseForTheSamePlan_soAnAnsweredQuestionIsNotAskedAgain()
    {
        Assert.False(PaymentDecisions.PlanChanged("pm-1", 14, 49.99m, "pm-1", 14, 49.99m));
        Assert.False(PaymentDecisions.PlanChanged(null, null, 0m, null, null, 0m));
    }

    #endregion

    #region Server-recorded id

    [Fact]
    public void ServerRecordedId_prefersThePaymentIntentTheServerRecorded()
    {
        Assert.Equal("pi_server", PaymentDecisions.ServerRecordedId(
            Response(paymentIntentId: "pi_server", subscriptionId: "sub_1")));
    }

    [Fact]
    public void ServerRecordedId_fallsBackToTheSubscription()
    {
        Assert.Equal("sub_1", PaymentDecisions.ServerRecordedId(
            Response(paymentIntentId: null, subscriptionId: "sub_1")));
    }

    /// <summary>
    /// Confirming an empty id asks the server to verify a payment it cannot find. The component
    /// fails with a message instead.
    /// </summary>
    [Fact]
    public void ServerRecordedId_isNullWhenTheServerNamedNeither()
    {
        Assert.Null(PaymentDecisions.ServerRecordedId(Response(paymentIntentId: null, subscriptionId: null)));
        Assert.Null(PaymentDecisions.ServerRecordedId(Response(paymentIntentId: "", subscriptionId: "")));
        Assert.Null(PaymentDecisions.ServerRecordedId(null));
    }

    /// <summary>
    /// A subscription's first invoice is what the server recorded; Stripe's own PaymentIntent id is
    /// a different object, and confirming it leaves the subscription unverified.
    /// </summary>
    [Fact]
    public void ConfirmationId_prefersTheServersIdOverTheOneTheBrowserConfirmed()
    {
        Assert.Equal("pi_server", PaymentDecisions.ConfirmationId(
            Response(paymentIntentId: "pi_server"), "pi_browser"));
    }

    [Fact]
    public void ConfirmationId_fallsBackToTheBrowsersIntent_andIsNeverEmpty()
    {
        Assert.Equal("pi_browser", PaymentDecisions.ConfirmationId(Response(), "pi_browser"));
        Assert.Null(PaymentDecisions.ConfirmationId(Response(), null));
        Assert.Null(PaymentDecisions.ConfirmationId(Response(), string.Empty));
    }

    #endregion

    #region Billing address

    [Fact]
    public void TryBuildBillingAddress_sendsNothingWhenTheAppDoesNotAskForAnAddress()
    {
        Assert.True(PaymentDecisions.TryBuildBillingAddress(
            required: false, null, null, null, null, null, null, "US", out var address));
        Assert.Null(address);
    }

    [Fact]
    public void TryBuildBillingAddress_sendsTheTrimmedAddressWithItsCountry()
    {
        Assert.True(PaymentDecisions.TryBuildBillingAddress(
            required: true, " Ada ", " Lovelace ", " 1 Analytical Way ",
            " London ", " NA ", " 12345 ", " US ", out var address));

        Assert.NotNull(address);
        Assert.Equal("Ada", address!.FirstName);
        Assert.Equal("Lovelace", address.LastName);
        Assert.Equal("1 Analytical Way", address.Street);
        Assert.Equal("London", address.City);
        Assert.Equal("NA", address.State);
        Assert.Equal("12345", address.ZipCode);
        Assert.Equal("US", address.Country);
    }

    /// <summary>
    /// An incomplete address fails at the provider, after the intent already exists — so it is
    /// refused before anything is charged.
    /// </summary>
    [Theory]
    [InlineData("", "L", "S", "C", "ST", "Z")]
    [InlineData("F", "", "S", "C", "ST", "Z")]
    [InlineData("F", "L", "   ", "C", "ST", "Z")]
    [InlineData("F", "L", "S", null, "ST", "Z")]
    [InlineData("F", "L", "S", "C", "", "Z")]
    [InlineData("F", "L", "S", "C", "ST", " ")]
    public void TryBuildBillingAddress_refusesAnIncompleteRequiredAddress(
        string? first, string? last, string? street, string? city, string? state, string? zip)
    {
        Assert.False(PaymentDecisions.TryBuildBillingAddress(
            required: true, first, last, street, city, state, zip, "US", out var address));
        Assert.Null(address);
    }

    #endregion

    #region Redirect safety

    [Theory]
    [InlineData("https://checkout.example.com/pay?id=1")]
    [InlineData("http://checkout.example.com/pay")]
    public void IsSafeRedirectUrl_acceptsAnAbsoluteHttpAddress(string url)
    {
        Assert.True(PaymentDecisions.IsSafeRedirectUrl(url));
    }

    /// <summary>
    /// The redirect used to be interpolated into a script string and run through <c>eval</c>, so a
    /// provider answer could write JavaScript into the host page.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/relative/path")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///c:/windows")]
    public void IsSafeRedirectUrl_refusesAnythingElse(string? url)
    {
        Assert.False(PaymentDecisions.IsSafeRedirectUrl(url));
    }

    #endregion
}
