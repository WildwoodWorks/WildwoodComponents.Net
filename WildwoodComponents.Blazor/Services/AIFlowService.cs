using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Services
{
    public interface IAIFlowService
    {
        event EventHandler? AuthenticationFailed;

        Task<List<FlowDefinition>> GetFlowDefinitionsAsync(string appId);
        Task<FlowDefinition?> GetFlowDefinitionAsync(string appId, string flowId);
        Task<FlowExecution> ExecuteFlowAsync(string appId, string flowId, string? inputDataJson);
        Task<FlowExecution?> GetExecutionStatusAsync(string appId, string executionId);
        Task<bool> CancelExecutionAsync(string appId, string executionId);
        void SetAuthToken(string token);
        void SetApiBaseUrl(string apiBaseUrl);
    }

    public class AIFlowService : IAIFlowService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<AIFlowService> _logger;
        private string _authToken = string.Empty;
        private string _apiBaseUrl = string.Empty;

        public event EventHandler? AuthenticationFailed;

        public AIFlowService(HttpClient httpClient, ILogger<AIFlowService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public void SetAuthToken(string token) => _authToken = token;
        public void SetApiBaseUrl(string apiBaseUrl) => _apiBaseUrl = apiBaseUrl.TrimEnd('/');

        private HttpRequestMessage CreateRequest(HttpMethod method, string path)
        {
            var request = new HttpRequestMessage(method, $"{_apiBaseUrl}/{path}");
            if (!string.IsNullOrEmpty(_authToken))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _authToken);
            }
            return request;
        }

        private async Task<T?> SendAsync<T>(HttpRequestMessage request) where T : class
        {
            try
            {
                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    _logger.LogWarning("Flow API authentication failed: {StatusCode} {Path}", response.StatusCode, request.RequestUri);
                    AuthenticationFailed?.Invoke(this, EventArgs.Empty);
                    return null;
                }

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadFromJsonAsync<T>();
                }

                _logger.LogWarning("Flow API request failed: {StatusCode} {Path}", response.StatusCode, request.RequestUri);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Flow API: {Path}", request.RequestUri);
                return null;
            }
        }

        public async Task<List<FlowDefinition>> GetFlowDefinitionsAsync(string appId)
        {
            var request = CreateRequest(HttpMethod.Get, $"AppComponentConfigurations/{appId}/flows");
            return await SendAsync<List<FlowDefinition>>(request) ?? new List<FlowDefinition>();
        }

        public async Task<FlowDefinition?> GetFlowDefinitionAsync(string appId, string flowId)
        {
            var request = CreateRequest(HttpMethod.Get, $"AppComponentConfigurations/{appId}/flows/{flowId}");
            return await SendAsync<FlowDefinition>(request);
        }

        public async Task<FlowExecution> ExecuteFlowAsync(string appId, string flowId, string? inputDataJson)
        {
            var request = CreateRequest(HttpMethod.Post, $"FlowExecution/{appId}/execute/{flowId}");
            var body = new FlowExecuteRequest { InputDataJson = inputDataJson };
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var result = await SendAsync<FlowExecution>(request);
            return result ?? new FlowExecution { Status = "failed", ErrorMessage = "Failed to start flow execution" };
        }

        public async Task<FlowExecution?> GetExecutionStatusAsync(string appId, string executionId)
        {
            var request = CreateRequest(HttpMethod.Get, $"FlowExecution/{appId}/status/{executionId}");
            return await SendAsync<FlowExecution>(request);
        }

        public async Task<bool> CancelExecutionAsync(string appId, string executionId)
        {
            var request = CreateRequest(HttpMethod.Post, $"FlowExecution/{appId}/cancel/{executionId}");
            try
            {
                var response = await _httpClient.SendAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                    response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    AuthenticationFailed?.Invoke(this, EventArgs.Empty);
                    return false;
                }

                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling flow execution {ExecutionId}", executionId);
                return false;
            }
        }
    }
}
