using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public class WildwoodAIFlowService : IWildwoodAIFlowService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodAIFlowService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodAIFlowService(HttpClient httpClient, IWildwoodSessionManager sessionManager, ILogger<WildwoodAIFlowService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<List<FlowDefinitionDto>> GetFlowDefinitionsAsync(string appId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/AppComponentConfigurations/{appId}/flows");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<FlowDefinitionDto>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get flow definitions for app {AppId}", appId);
        }
        return new();
    }

    public async Task<FlowDefinitionDto?> GetFlowDefinitionAsync(string appId, string flowId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/AppComponentConfigurations/{appId}/flows/{flowId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FlowDefinitionDto>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get flow definition {FlowId}", flowId);
        }
        return null;
    }

    public async Task<FlowExecution> ExecuteFlowAsync(string appId, string flowId, string? inputDataJson)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { InputDataJson = inputDataJson };
            using var response = await _httpClient.PostAsJsonAsync($"api/FlowExecution/{appId}/execute/{flowId}", payload);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<FlowExecution>(content, JsonOptions)
                    ?? new FlowExecution { Status = "Failed", ErrorMessage = "Failed to parse response" };
            }

            return new FlowExecution { Status = "Failed", ErrorMessage = $"API error: {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute flow {FlowId}", flowId);
            return new FlowExecution { Status = "Failed", ErrorMessage = "Failed to execute flow" };
        }
    }

    public async Task<FlowExecution?> GetExecutionStatusAsync(string appId, string executionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/FlowExecution/{appId}/status/{executionId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<FlowExecution>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution status {ExecutionId}", executionId);
        }
        return null;
    }

    public async Task<bool> CancelExecutionAsync(string appId, string executionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/FlowExecution/{appId}/cancel/{executionId}", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel execution {ExecutionId}", executionId);
            return false;
        }
    }
}
