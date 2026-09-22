using System.Text.Json;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Tests.Shared;

/// <summary>
/// The initiate-payment request is serialised camelCase by the payment services, except the
/// billing address, which the JS SDK sends as PascalCase <c>BillingAddress</c> — so the model has
/// to pin that key itself. The response carries the SetupIntent/trial fields a free-trial signup
/// needs to save a card.
/// </summary>
public class PaymentInitiateModelsTests
{
    // The same options WildwoodComponents.Blazor's PaymentProviderService serialises with.
    private static readonly JsonSerializerOptions CamelCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void Request_SendsBillingAddressUnderThePascalCaseKey()
    {
        var request = new InitiatePaymentRequest
        {
            ProviderId = "prov-1",
            AppId = "app-1",
            Amount = 79m,
            SupportsSetupIntent = true,
            BillingAddress = new BillingAddress
            {
                FirstName = "Ada",
                LastName = "Lovelace",
                Street = "1 Analytical Way",
                City = "London",
                State = "LDN",
                ZipCode = "SW1A",
                Country = "GB"
            }
        };

        var json = JsonSerializer.Serialize(request, CamelCase);

        Assert.Contains("\"providerId\":\"prov-1\"", json);
        Assert.Contains("\"supportsSetupIntent\":true", json);
        Assert.Contains("\"BillingAddress\":{", json);
        Assert.Contains("\"firstName\":\"Ada\"", json);
        Assert.Contains("\"zipCode\":\"SW1A\"", json);
        Assert.Contains("\"country\":\"GB\"", json);
        Assert.DoesNotContain("\"billingAddress\":", json);
    }

    [Fact]
    public void Request_OmitsBillingAddressAndSetupIntentWhenNotSet()
    {
        var json = JsonSerializer.Serialize(
            new InitiatePaymentRequest { ProviderId = "prov-1", AppId = "app-1", Amount = 10m },
            CamelCase);

        Assert.DoesNotContain("BillingAddress", json);
        Assert.DoesNotContain("supportsSetupIntent", json);
    }

    [Fact]
    public void Request_ReadsBackTheBillingAddressItWrote()
    {
        var request = new InitiatePaymentRequest
        {
            ProviderId = "prov-1",
            AppId = "app-1",
            Amount = 79m,
            BillingAddress = new BillingAddress { FirstName = "Ada", Country = "GB" }
        };

        var round = JsonSerializer.Deserialize<InitiatePaymentRequest>(
            JsonSerializer.Serialize(request, CamelCase), CamelCase);

        Assert.NotNull(round);
        Assert.Equal("Ada", round!.BillingAddress?.FirstName);
        Assert.Equal("GB", round.BillingAddress?.Country);
    }

    [Fact]
    public void Response_ReadsTheSetupIntentBranchOfAFreeTrial()
    {
        const string json = """
            {"success":true,"paymentIntentId":"seti_1","clientSecret":"seti_1_secret_2",
             "clientSecretType":"setup_intent","trialDays":14,"trialEnd":"2026-10-02T00:00:00Z",
             "requiresClientConfirmation":true,"providerType":"Stripe"}
            """;

        var response = JsonSerializer.Deserialize<InitiatePaymentResponse>(json, CamelCase);

        Assert.NotNull(response);
        Assert.True(response!.Success);
        Assert.Equal(PaymentClientSecretTypes.SetupIntent, response.ClientSecretType);
        Assert.Equal(14, response.TrialDays);
        Assert.Equal(new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc), response.TrialEnd!.Value.ToUniversalTime());
        Assert.Equal(PaymentProviderType.Stripe, response.ProviderType);
    }

    [Fact]
    public void Response_ReadsThePaymentIntentBranchOfAPaidPlan()
    {
        const string json = """
            {"success":true,"paymentIntentId":"pi_1","clientSecret":"pi_1_secret_2",
             "clientSecretType":"payment_intent","requiresClientConfirmation":true,"providerType":"Stripe"}
            """;

        var response = JsonSerializer.Deserialize<InitiatePaymentResponse>(json, CamelCase);

        Assert.NotNull(response);
        Assert.Equal(PaymentClientSecretTypes.PaymentIntent, response.ClientSecretType);
        Assert.Null(response.TrialDays);
        Assert.Null(response.TrialEnd);
    }

    [Fact]
    public void Response_NewFieldsAreNullOnAnOlderServer()
    {
        const string json = """{"success":true,"clientSecret":"pi_1_secret_2","providerType":"Stripe"}""";

        var response = JsonSerializer.Deserialize<InitiatePaymentResponse>(json, CamelCase);

        Assert.NotNull(response);
        Assert.Null(response!.ClientSecretType);
        Assert.Null(response.TrialDays);
        Assert.Null(response.TrialEnd);
    }

    [Fact]
    public void ClientSecretTypes_CarryTheTwoWireValues()
    {
        Assert.Equal("payment_intent", PaymentClientSecretTypes.PaymentIntent);
        Assert.Equal("setup_intent", PaymentClientSecretTypes.SetupIntent);
    }
}
