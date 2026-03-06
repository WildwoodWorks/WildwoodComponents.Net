using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// AI Proxy service implementation that calls WildwoodAPI AI endpoints.
/// Supports named configurations for different AI tasks.
/// </summary>
public class WildwoodAIProxyService : IWildwoodAIProxyService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodAIProxyService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodAIProxyService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodAIProxyService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<AIProxyResponse> SendRequestAsync(AIProxyRequest request)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);

            // Resolve configuration ID from name if needed
            var configId = request.ConfigurationId;
            if (string.IsNullOrEmpty(configId) && !string.IsNullOrEmpty(request.ConfigurationName))
            {
                configId = await ResolveConfigurationIdAsync(request.ConfigurationName);
                if (string.IsNullOrEmpty(configId))
                    return AIProxyResponse.Error($"AI configuration '{request.ConfigurationName}' not found.");
            }

            var apiRequest = new WildwoodAIRequestDto
            {
                ConfigurationId = configId,
                SessionId = request.SessionId,
                Message = BuildMessage(request),
                SaveToSession = request.SaveToSession
            };

            var response = await _httpClient.PostAsJsonAsync("ai/chat", apiRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var wwResponse = JsonSerializer.Deserialize<WildwoodAIResponseDto>(content, JsonOptions);
                if (wwResponse != null)
                    return AIProxyResponse.FromWildwoodResponse(wwResponse);
            }

            _logger.LogWarning("AI proxy request failed with status {StatusCode}: {ResponseBody}", response.StatusCode, content);
            return AIProxyResponse.Error(ExtractErrorMessage(content, response.StatusCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI proxy request failed");
            return AIProxyResponse.Error("An error occurred processing the AI request.");
        }
    }

    public async Task<AIProxyResponse> SendRequestWithFileAsync(AIProxyRequest request, Stream fileStream, string fileName)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);

            // Resolve configuration ID from name if needed
            var configId = request.ConfigurationId;
            if (string.IsNullOrEmpty(configId) && !string.IsNullOrEmpty(request.ConfigurationName))
            {
                configId = await ResolveConfigurationIdAsync(request.ConfigurationName);
                if (string.IsNullOrEmpty(configId))
                    return AIProxyResponse.Error($"AI configuration '{request.ConfigurationName}' not found.");
            }

            // Convert file stream to base64
            using var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream);
            var fileBytes = memoryStream.ToArray();
            var fileBase64 = Convert.ToBase64String(fileBytes);

            // Infer MIME type from file extension
            var mediaType = FileMediaTypeHelper.GetMediaTypeFromFileName(fileName);

            _logger.LogInformation("Sending AI file request: config={ConfigId}, file={FileName}, mediaType={MediaType}, base64Length={Length}",
                configId, fileName, mediaType, fileBase64.Length);

            var apiRequest = new WildwoodAIRequestDto
            {
                ConfigurationId = configId,
                SessionId = request.SessionId,
                Message = BuildMessage(request),
                SaveToSession = request.SaveToSession,
                FileBase64 = fileBase64,
                FileMediaType = mediaType,
                FileName = fileName
            };

            // Send as JSON (not multipart) — this is the key fix for the 415 error
            var response = await _httpClient.PostAsJsonAsync("ai/chat", apiRequest);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var wwResponse = JsonSerializer.Deserialize<WildwoodAIResponseDto>(content, JsonOptions);
                if (wwResponse != null)
                    return AIProxyResponse.FromWildwoodResponse(wwResponse);
            }

            _logger.LogWarning("AI proxy file request failed with status {StatusCode}: {ResponseBody}", response.StatusCode, content);
            return AIProxyResponse.Error(ExtractErrorMessage(content, response.StatusCode));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI proxy file request failed");
            return AIProxyResponse.Error("An error occurred processing the AI request.");
        }
    }

    public async Task<List<AIProxyConfigInfo>> GetConfigurationsAsync()
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var response = await _httpClient.GetAsync("ai/configurations");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<AIProxyConfigInfo>>(content, JsonOptions)
                       ?? new List<AIProxyConfigInfo>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI configurations");
        }

        return new List<AIProxyConfigInfo>();
    }

    private async Task<string?> ResolveConfigurationIdAsync(string configurationName)
    {
        var configs = await GetConfigurationsAsync();
        return configs
            .FirstOrDefault(c => c.IsActive &&
                c.Name.Equals(configurationName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    private static string BuildMessage(AIProxyRequest request)
    {
        if (string.IsNullOrEmpty(request.Context))
            return request.Prompt ?? string.Empty;

        return $"{request.Prompt}\n\n---\nContext:\n{request.Context}";
    }

    /// <summary>
    /// Extracts a human-readable error message from the API response body.
    /// The API may return a plain string, a JSON string, or a JSON error object.
    /// </summary>
    private static string ExtractErrorMessage(string responseBody, System.Net.HttpStatusCode statusCode)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
            return $"AI request failed: {statusCode}";

        // Try to parse as JSON string (API returns BadRequest("message") which serializes as a JSON string)
        try
        {
            var parsed = JsonSerializer.Deserialize<string>(responseBody);
            if (!string.IsNullOrWhiteSpace(parsed))
                return parsed;
        }
        catch { }

        // If it's plain text (not JSON-wrapped), use it directly
        var trimmed = responseBody.Trim();
        if (!trimmed.StartsWith('{') && !trimmed.StartsWith('[') && trimmed.Length < 500)
            return trimmed;

        return $"AI request failed: {statusCode}";
    }
}
