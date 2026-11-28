using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Razor.Components.Registration;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Tests.Razor;

/// <summary>
/// Razor token registration, brought to Blazor #33's payment behaviour. Razor's browser script has
/// no JS harness (the repo's stated convention), so its rules are pinned against the source here,
/// the way MoneyAndEntitlementSourceGuardTests pins the money helper.
/// </summary>
public class TokenRegistrationPaymentParityTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "WildwoodComponents.sln"))
               && !Directory.GetFiles(dir.FullName, "*.sln*").Any())
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Could not find the solution directory.");
    }

    private static string Script() =>
        File.ReadAllText(Path.Combine(RepoRoot(), "WildwoodComponents.Razor", "wwwroot", "js", "token-registration.js"));

    /// <summary>
    /// The follow-up link call needed a session the new user did not have yet, so it always 401'd.
    /// The payment reference now rides on the registration request instead.
    /// </summary>
    [Fact]
    public void The_registration_request_carries_the_payment_and_no_link_call_follows()
    {
        var source = Script();

        Assert.Contains("paymentTransactionId: this.pendingPaymentTransactionId || null", source);
        Assert.Contains("paymentIntentId: this.pendingPaymentIntentId || null", source);
        Assert.DoesNotContain("_apiPost('api/payment/link-transaction'", source);
        Assert.DoesNotContain("_linkPaymentTransaction", source);
    }

    /// <summary>
    /// The models a host's registration proxy binds (and the registration service forwards) carry
    /// the pair; otherwise System.Text.Json would drop what the script sent and the payment would
    /// be linked to nobody.
    /// </summary>
    [Fact]
    public async Task The_payment_reference_survives_the_proxy_models_and_reaches_the_api()
    {
        const string browserBody =
            "{\"token\":\"tok\",\"email\":\"a@b.test\",\"password\":\"pw\",\"pricingModelId\":\"pm\"," +
            "\"paymentTransactionId\":\"txn-1\",\"paymentIntentId\":\"pi_1\"}";
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);

        var withToken = JsonSerializer.Deserialize<TokenRegistrationRequest>(browserBody, web)!;
        var open = JsonSerializer.Deserialize<OpenRegistrationRequest>(browserBody, web)!;
        Assert.Equal("txn-1", withToken.PaymentTransactionId);
        Assert.Equal("pi_1", withToken.PaymentIntentId);
        Assert.Equal("txn-1", open.PaymentTransactionId);
        Assert.Equal("pi_1", open.PaymentIntentId);

        var handler = new WildwoodComponents.Tests.TestHelpers.FakeHttpMessageHandler()
            .WhenOk("userregistration/register-with-token", "{\"success\":true,\"userId\":\"u1\"}");
        var service = new WildwoodComponents.Razor.Services.WildwoodRegistrationService(
            handler.CreateClient("https://api.test/api/"),
            new WildwoodComponents.Tests.TestHelpers.FakeSessionManager(null),
            NullLogger<WildwoodComponents.Razor.Services.WildwoodRegistrationService>.Instance,
            "app-1");

        await service.RegisterWithTokenAsync(withToken);

        var sent = Assert.Single(handler.Requests).Body!;
        Assert.Contains("\"paymentTransactionId\":\"txn-1\"", sent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"paymentIntentId\":\"pi_1\"", sent, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Cancel registers to pay later only when the host allows it.</summary>
    [Fact]
    public void Cancelling_payment_honours_allowSkipPayment()
    {
        var source = Script();

        Assert.Contains("this.allowSkipPayment = root.dataset.allowSkipPayment !== 'false';", source);
        Assert.Contains("RegistrationInstance.prototype._cancelPayment = function ()", source);
        Assert.Contains("addEventListener('ww-payment-cancel', function () { self._cancelPayment(); })", source);
    }

    [Fact]
    public void The_trial_length_reaches_the_payment_step()
    {
        var source = Script();

        Assert.Contains("trialDays: data.trialDays || 0", source);
        Assert.Contains("'data-trial-days=\"'", source);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_view_component_passes_allowSkipPayment_to_the_view(bool allow)
    {
        // Open registration is off, so the service is never called; a real one over a fake
        // transport keeps the test honest about the constructor the host uses.
        var service = new WildwoodComponents.Razor.Services.WildwoodRegistrationService(
            new WildwoodComponents.Tests.TestHelpers.FakeHttpMessageHandler().CreateClient("https://api.test/api/"),
            new WildwoodComponents.Tests.TestHelpers.FakeSessionManager(null),
            NullLogger<WildwoodComponents.Razor.Services.WildwoodRegistrationService>.Instance,
            "app-1");
        var component = new TokenRegistrationViewComponent(service, NullLogger<TokenRegistrationViewComponent>.Instance);

        var result = await component.InvokeAsync(allowSkipPayment: allow);

        var view = Assert.IsType<ViewViewComponentResult>(result);
        var model = Assert.IsType<TokenRegistrationViewModel>(view.ViewData!.Model);
        Assert.Equal(allow, model.AllowSkipPayment);
    }

    /// <summary>
    /// The default pricing is serialized into a data attribute the script reads in camelCase
    /// (priceAmount, trialDays); the view now serializes with web defaults to match.
    /// </summary>
    [Fact]
    public void Default_pricing_serializes_in_the_casing_the_script_reads()
    {
        var json = JsonSerializer.Serialize(
            new PricingDetails { PriceAmount = 9.5m, TrialDays = 14 },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.Contains("\"priceAmount\":9.5", json);
        Assert.Contains("\"trialDays\":14", json);

        var view = File.ReadAllText(Path.Combine(RepoRoot(), "WildwoodComponents.Razor", "Views", "Shared",
            "Components", "TokenRegistration", "Default.cshtml"));
        Assert.Contains("JsonSerializerDefaults.Web", view);
    }
}
