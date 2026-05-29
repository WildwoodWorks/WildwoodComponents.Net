using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Interface for the standalone <c>PaymentFormComponent</c> (raw card-entry form).
    /// </summary>
    /// <remarks>
    /// IMPORTANT: This service POSTs to <c>/api/payment/process</c>, which is NOT a built-in
    /// WildwoodAPI endpoint. It must be supplied by the host application (e.g. a server-side
    /// proxy that tokenizes the card and forwards to a provider). Collecting a raw card number
    /// in your own request is a PCI-DSS anti-pattern.
    /// <para>
    /// For end-to-end payments against WildwoodAPI, prefer <c>PaymentComponent</c> +
    /// <c>PaymentProviderService.InitiatePaymentAsync</c>, which uses provider tokenization
    /// (Stripe Elements, PayPal, etc.), persists a <c>PaymentTransaction</c>, and carries
    /// the <c>AppId</c>. Until a <c>/process</c> endpoint exists, this form is a UI demo only.
    /// </para>
    /// </remarks>
    public interface IPaymentService
    {
        /// <summary>
        /// POSTs the card details to <c>/api/payment/process</c>. Requires a host-provided
        /// endpoint (see the interface remarks); returns a failed <see cref="PaymentResult"/>
        /// (e.g. 404) if none is configured. The request's <c>AppId</c> is forwarded when set.
        /// </summary>
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
