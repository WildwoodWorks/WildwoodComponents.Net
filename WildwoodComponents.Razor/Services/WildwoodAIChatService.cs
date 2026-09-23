using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

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

    public async Task<List<AIConfiguration>> GetConfigurationsAsync(string? configurationType = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = "ai/configurations";
            if (!string.IsNullOrEmpty(configurationType))
                url += $"?configurationType={Uri.EscapeDataString(configurationType)}";

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<AIConfiguration>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI configurations");
        }
        return new();
    }

    public async Task<AIConfiguration?> GetConfigurationAsync(string configurationId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"ai/configurations/{configurationId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AIConfiguration>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI configuration {ConfigurationId}", configurationId);
        }
        return null;
    }

    public async Task<AIChatResponse> SendMessageAsync(AIChatRequest request)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsJsonAsync("ai/chat", request);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<AIChatResponse>(content, JsonOptions)
                    ?? new AIChatResponse { IsError = true, ErrorMessage = "Failed to parse response" };
            }

            return new AIChatResponse { IsError = true, ErrorMessage = $"API error: {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send AI chat message");
            return new AIChatResponse { IsError = true, ErrorMessage = "Failed to send message" };
        }
    }

    public async Task<AIChatResponse> SendMessageWithFileAsync(AIChatRequest request, byte[] fileBytes, string fileName)
    {
        try
        {
            // Convert file to base64 and include in the request
            // Note: AIChatRequest from Shared doesn't have file fields,
            // so we use a local wrapper for the HTTP call
            var payload = new
            {
                request.ConfigurationId,
                request.SessionId,
                request.Message,
                request.SaveToSession,
                request.MacroValues,
                FileBase64 = Convert.ToBase64String(fileBytes),
                FileName = fileName,
                FileMediaType = Path.GetExtension(fileName)?.ToLowerInvariant() switch
                {
                    ".png" => "image/png",
                    ".jpg" or ".jpeg" => "image/jpeg",
                    ".gif" => "image/gif",
                    ".webp" => "image/webp",
                    ".pdf" => "application/pdf",
                    ".txt" => "text/plain",
                    ".csv" => "text/csv",
                    _ => "application/octet-stream"
                }
            };

            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsJsonAsync("ai/chat", payload);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return JsonSerializer.Deserialize<AIChatResponse>(content, JsonOptions)
                    ?? new AIChatResponse { IsError = true, ErrorMessage = "Failed to parse response" };
            }

            return new AIChatResponse { IsError = true, ErrorMessage = $"API error: {response.StatusCode}" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send AI chat message with file");
            return new AIChatResponse { IsError = true, ErrorMessage = "Failed to send message with file" };
        }
    }

    public async Task<AISession?> CreateSessionAsync(string configurationId, string? sessionName = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { ConfigurationId = configurationId, SessionName = sessionName };
            using var response = await _httpClient.PostAsJsonAsync("ai/sessions", payload);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AISession>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create AI session");
        }
        return null;
    }

    public async Task<AISession?> GetSessionAsync(string sessionId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"ai/sessions/{sessionId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<AISession>(content, JsonOptions);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get AI session {SessionId}", sessionId);
        }
        return null;
    }

    public async Task<List<AISessionSummary>> GetSessionsAsync(string? configurationId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = "ai/sessions";
            if (!string.IsNullOrEmpty(configurationId))
                url += $"?configurationId={configurationId}";

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<AISessionSummary>>(content, JsonOptions) ?? new();
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
            using var response = await _httpClient.PostAsync($"ai/sessions/{sessionId}/end", null);
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
            using var response = await _httpClient.DeleteAsync($"ai/sessions/{sessionId}");
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
            using var response = await _httpClient.PutAsJsonAsync($"ai/sessions/{sessionId}/name", new { NewName = newName });
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rename AI session {SessionId}", sessionId);
            return false;
        }
    }

    public async Task<List<TTSVoice>> GetTTSVoicesAsync(string? configurationId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = string.IsNullOrEmpty(configurationId)
                ? "tts/voices"
                : $"tts/voices/configuration/{configurationId}";

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<TTSVoice>>(content, JsonOptions) ?? new();
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
            using var response = await _httpClient.PostAsJsonAsync("tts/synthesize/base64", payload);
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

    /// <inheritdoc />
    /// <remarks>
    /// Wire-identical to Blazor's <c>AIService.TranscribeAudioAsync</c>: one multipart POST, the
    /// audio in a part named <c>file</c> called <c>speech.&lt;ext&gt;</c> and carrying the BARE
    /// media type, with <c>configurationId</c> and <c>language</c> present only when they have a
    /// value. The shared rules live in <see cref="SpeechAudioFormats"/>.
    /// <para>
    /// The bearer goes on the REQUEST, not on <c>DefaultRequestHeaders</c>: the named client is
    /// shared by the scope's services, so a default header is another user's token waiting to
    /// happen. (The older methods above still use <c>ApplyAuthorizationHeader</c>; that is the
    /// deferred hazard the September sync recorded, not a pattern to copy.)
    /// </para>
    /// </remarks>
    public async Task<SpeechTranscriptionResult> TranscribeAudioAsync(byte[] audio, string contentType, string? configurationId = null, string? language = null)
    {
        if (audio is null || audio.Length == 0)
        {
            return SpeechAudioFormats.Failure(SpeechAudioFormats.NoAudioMessage);
        }

        try
        {
            var mediaType = SpeechAudioFormats.BareMediaType(contentType);

            // No manual request Content-Type: MultipartFormDataContent sets multipart/form-data
            // with the boundary itself.
            using var form = new MultipartFormDataContent();
            var filePart = new ByteArrayContent(audio);
            if (mediaType is not null && MediaTypeHeaderValue.TryParse(mediaType, out var partType))
            {
                filePart.Headers.ContentType = partType;
            }
            form.Add(filePart, "file", SpeechAudioFormats.FileNameFor(mediaType));

            if (!string.IsNullOrEmpty(configurationId))
            {
                form.Add(new StringContent(configurationId, Encoding.UTF8), "configurationId");
            }
            if (!string.IsNullOrEmpty(language))
            {
                form.Add(new StringContent(language, Encoding.UTF8), "language");
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "stt/transcribe")
            {
                Content = form
            };
            var token = _sessionManager.GetAccessToken();
            if (!string.IsNullOrEmpty(token))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            }

            using var response = await _httpClient.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();

            SpeechTranscriptionResult? parsed = null;
            try
            {
                parsed = JsonSerializer.Deserialize<SpeechTranscriptionResult>(body, JsonOptions);
            }
            catch (JsonException)
            {
                // Non-JSON body (e.g. a 413 from the web server) — reported by status code below.
            }

            if (response.IsSuccessStatusCode && parsed is not null && parsed.Success)
            {
                return SpeechAudioFormats.Success(parsed.Text);
            }

            var message = !string.IsNullOrEmpty(parsed?.ErrorMessage)
                ? parsed!.ErrorMessage!
                : SpeechAudioFormats.FailureMessageForStatus((int)response.StatusCode);
            _logger.LogWarning("Transcription unsuccessful. Status: {StatusCode}, Error: {Error}",
                response.StatusCode, message);
            return SpeechAudioFormats.Failure(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to transcribe audio");
            return SpeechAudioFormats.Failure(SpeechAudioFormats.GenericFailureMessage);
        }
    }
}
