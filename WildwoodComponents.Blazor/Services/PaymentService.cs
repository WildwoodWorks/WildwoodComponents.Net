using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Interface for Payment Service operations.
    /// </summary>
    public interface IPaymentService
    {
        Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request);
        Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal? amount = null);
        Task<PaymentResult> GetPaymentStatusAsync(string transactionId);
        Task<List<PaymentMethod>> GetAvailablePaymentMethodsAsync();
    }

    /// <summary>
    /// Payment Service implementation for processing payments.
    /// </summary>
    public class PaymentService : IPaymentService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<PaymentService> _logger;

        public PaymentService(HttpClient httpClient, ILogger<PaymentService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<PaymentResult> ProcessPaymentAsync(PaymentRequest request)
        {
            try
            {
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/api/payment/process", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<PaymentResult>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    
                    return result ?? new PaymentResult { IsSuccess = false, ErrorMessage = "Invalid response" };
                }
                else
                {
                    return new PaymentResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = $"Payment failed with status: {response.StatusCode}" 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing payment");
                return new PaymentResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<PaymentResult> RefundPaymentAsync(string transactionId, decimal? amount = null)
        {
            try
            {
                var refundRequest = new { TransactionId = transactionId, Amount = amount };
                var json = JsonSerializer.Serialize(refundRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync("/api/payment/refund", content);
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<PaymentResult>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new PaymentResult { IsSuccess = false, ErrorMessage = "Invalid response" };
                }
                else
                {
                    return new PaymentResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = $"Refund failed with status: {response.StatusCode}" 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing refund");
                return new PaymentResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public async Task<PaymentResult> GetPaymentStatusAsync(string transactionId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/payment/status/{transactionId}");
                
                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<PaymentResult>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    }) ?? new PaymentResult { IsSuccess = false, ErrorMessage = "Invalid response" };
                }
                else
                {
                    return new PaymentResult 
                    { 
                        IsSuccess = false, 
                        ErrorMessage = $"Status check failed with status: {response.StatusCode}" 
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking payment status");
                return new PaymentResult { IsSuccess = false, ErrorMessage = ex.Message };
            }
        }

        public Task<List<PaymentMethod>> GetAvailablePaymentMethodsAsync()
        {
            // Return available payment methods - this could be configured or retrieved from API
            var methods = new List<PaymentMethod>
            {
                PaymentMethod.CreditCard,
                PaymentMethod.BankTransfer,
                PaymentMethod.DigitalWallet
            };
            
            return Task.FromResult(methods);
        }
    }
}
