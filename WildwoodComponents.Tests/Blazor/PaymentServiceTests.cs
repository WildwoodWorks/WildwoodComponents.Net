using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Tests.TestHelpers;

namespace WildwoodComponents.Tests.Blazor;

public class PaymentServiceTests
{
    private static (PaymentService Service, FakeHttpMessageHandler Handler) CreateService()
    {
        var handler = new FakeHttpMessageHandler();
        var service = new PaymentService(
            handler.CreateClient("https://api.test/"),
            NullLogger<PaymentService>.Instance);
        return (service, handler);
    }

    [Fact]
    public async Task ProcessPaymentAsync_PostsToProcessEndpoint_AndDeserializesResult()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("payment/process", """{"isSuccess":true,"transactionId":"txn-1","amount":12.50}""");

        var result = await service.ProcessPaymentAsync(new PaymentRequest { Amount = 12.50m, Currency = "USD" });

        Assert.True(result.IsSuccess);
        Assert.Equal("txn-1", result.TransactionId);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Contains("/api/payment/process", request.Url);
    }

    [Fact]
    public async Task ProcessPaymentAsync_MapsNonSuccessStatusToFailedResult()
    {
        var (service, handler) = CreateService();
        handler.When("payment/process", HttpStatusCode.BadGateway, "{}");

        var result = await service.ProcessPaymentAsync(new PaymentRequest { Amount = 5m });

        Assert.False(result.IsSuccess);
        Assert.Contains("BadGateway", result.ErrorMessage);
    }

    [Fact]
    public async Task RefundPaymentAsync_PostsTransactionIdAndAmount()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("payment/refund", """{"isSuccess":true}""");

        var result = await service.RefundPaymentAsync("txn-9", 4.25m);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Contains("/api/payment/refund", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("txn-9", body.RootElement.GetProperty("TransactionId").GetString());
        Assert.Equal(4.25m, body.RootElement.GetProperty("Amount").GetDecimal());
    }

    [Fact]
    public async Task GetPaymentStatusAsync_GetsStatusForTransaction()
    {
        var (service, handler) = CreateService();
        handler.WhenOk("payment/status/txn-7", """{"isSuccess":true,"transactionId":"txn-7"}""");

        var result = await service.GetPaymentStatusAsync("txn-7");

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Get, request.Method);
        Assert.Contains("/api/payment/status/txn-7", request.Url);
    }

    [Fact]
    public async Task GetAvailablePaymentMethodsAsync_ReturnsTheSupportedMethods()
    {
        var (service, _) = CreateService();

        var methods = await service.GetAvailablePaymentMethodsAsync();

        Assert.Equal(
            new[] { PaymentMethod.CreditCard, PaymentMethod.BankTransfer, PaymentMethod.DigitalWallet },
            methods);
    }
}
