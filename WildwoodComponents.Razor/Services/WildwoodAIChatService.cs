using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

public class WildwoodAIChatService : IWildwoodAIChatService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodAIChatService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodAIChatService(HttpClient httpClient, IWildwoodSessionManager sessionManager, ILogger<WildwoodAIChatService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<List<AIConfigurationDto>> GetConfigurationsAsync(string? configurationType = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = "api/ai/configurations";
            if (!string.IsNullOrEmpty(configurationType))
                url += $"?configurationType={Uri.EscapeDataString(configurationType)}";

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<AIConfigurationDto>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI configurations");
        }
        return new();
    }

    public async Task<AIConfigurationDto?> GetConfigurationAsync(string configurationId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/ai/configurations/{configurationId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AIConfigurationDto>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI configuration {ConfigurationId}", configurationId);
        }
        return null;
    }

    public async Task<AIChatResponseDto> SendMessageAsync(AIChatRequestDto request)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsJsonAsync("api/ai/chat", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<AIChatResponseDto>(content, JsonOptions)
                    ?? new AIChatResponseDto { IsError = true, ErrorMessage = "Failed to parse response" };
            }

            return new AIChatResponseDto { IsError = true, ErrorMessage = $"API error: {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send AI chat message");
            return new AIChatResponseDto { IsError = true, ErrorMessage = "Failed to send message" };
        }
    }

    public async Task<AIChatResponseDto> SendMessageWithFileAsync(AIChatRequestDto request, byte[] fileBytes, string fileName)
    {
        try
        {
            // Convert file to base64 and include in the request
            request.FileBase64 = Convert.ToBase64String(fileBytes);
            request.FileName = fileName;

            // Detect media type from extension
            var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
            request.FileMediaType = ext switch
            {
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".webp" => "image/webp",
                ".pdf" => "application/pdf",
                ".txt" => "text/plain",
                ".csv" => "text/csv",
                _ => "application/octet-stream"
            };

            return await SendMessageAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send AI chat message with file");
            return new AIChatResponseDto { IsError = true, ErrorMessage = "Failed to send message with file" };
        }
    }

    public async Task<AISessionDto?> CreateSessionAsync(string configurationId, string? sessionName = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { ConfigurationId = configurationId, SessionName = sessionName };
            using var response = await _httpClient.PostAsJsonAsync("api/ai/sessions", payload);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AISessionDto>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create AI session");
        }
        return null;
    }

    public async Task<AISessionDto?> GetSessionAsync(string sessionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/ai/sessions/{sessionId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AISessionDto>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI session {SessionId}", sessionId);
        }
        return null;
    }

    public async Task<List<AISessionSummaryDto>> GetSessionsAsync(string? configurationId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = "api/ai/sessions";
            if (!string.IsNullOrEmpty(configurationId))
                url += $"?configurationId={configurationId}";

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<AISessionSummaryDto>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI sessions");
        }
        return new();
    }

    public async Task<bool> EndSessionAsync(string sessionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/ai/sessions/{sessionId}/end", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to end AI session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<bool> DeleteSessionAsync(string sessionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync($"api/ai/sessions/{sessionId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete AI session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<bool> RenameSessionAsync(string sessionId, string newName)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PutAsJsonAsync($"api/ai/sessions/{sessionId}/name", new { NewName = newName });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename AI session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<List<TTSVoiceDto>> GetTTSVoicesAsync(string? configurationId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = string.IsNullOrEmpty(configurationId)
                ? "api/tts/voices"
                : $"api/tts/voices/configuration/{configurationId}";

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<TTSVoiceDto>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get TTS voices");
        }
        return new();
    }

    public async Task<(string AudioBase64, string ContentType)?> SynthesizeSpeechAsync(string text, string voice, double speed = 1.0, string? configurationId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { Text = text, Voice = voice, Speed = speed, Format = "mp3", ConfigurationId = configurationId };
            using var response = await _httpClient.PostAsJsonAsync("api/tts/synthesize/base64", payload);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(content, JsonOptions);
                if (result != null && result.TryGetValue("audioBase64", out var audio))
                {
                    result.TryGetValue("contentType", out var ct);
                    return (audio, ct ?? "audio/mpeg");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to synthesize speech");
        }
        return null;
    }
}
