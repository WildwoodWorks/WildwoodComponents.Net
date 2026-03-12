using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// AI Service Interface for chat and configuration operations.
    /// </summary>
    public interface IAIService
    {
        /// <summary>
        /// Fired when an API call receives a 401/403 response, indicating the auth token is invalid.
        /// </summary>
        event EventHandler? AuthenticationFailed;

        Task<AIChatResponse> SendMessageAsync(AIChatRequest request);
        Task<AIChatResponse> SendMessageWithFileAsync(AIChatRequest request, byte[] fileBytes, string fileName);
        Task<List<AIConfiguration>> GetConfigurationsAsync(string? configurationType = null);
        Task<AIConfiguration?> GetConfigurationAsync(string configurationId);
        Task<AISession?> CreateSessionAsync(string configurationId, string? sessionName = null);
        Task<AISession?> GetSessionAsync(string sessionId);
        Task<List<AISessionSummary>> GetSessionsAsync(string? configurationId = null);
        Task<bool> EndSessionAsync(string sessionId);
        Task<bool> DeleteSessionAsync(string sessionId);
        Task<bool> RenameSessionAsync(string sessionId, string newName);
        void SetAuthToken(string token);
        void SetApiBaseUrl(string apiBaseUrl);
        
        // Text-to-Speech methods
        Task<string> GetTTSAudioUrlAsync(string text, string? voice = null, string format = "mp3", double speed = 1.0);
        Task<List<TTSVoice>> GetTTSVoicesAsync();
        
        /// <summary>
        /// Get available TTS voices for a specific AI configuration.
        /// This is the cross-platform method that works on Web, iOS, Android, and desktop.
        /// </summary>
        /// <param name="configurationId">The AI configuration ID</param>
        /// <returns>List of available voices for the configuration</returns>
        Task<List<TTSVoice>> GetTTSVoicesForConfigurationAsync(string configurationId);
        
        /// <summary>
        /// Synthesize text to speech and return audio as base64-encoded data.
        /// This is the cross-platform method that works on Web, iOS, Android, and desktop.
        /// </summary>
        /// <param name="text">Text to synthesize</param>
        /// <param name="voice">Voice ID to use</param>
        /// <param name="speed">Playback speed (0.5 to 2.0)</param>
        /// <param name="configurationId">Optional AI configuration ID</param>
        /// <returns>Base64-encoded audio data and content type</returns>
        Task<(string AudioBase64, string ContentType)?> SynthesizeSpeechAsync(string text, string voice, double speed = 1.0, string? configurationId = null);
    }

    /// <summary>
    /// TTS Voice model for client-side use.
    /// </summary>
    public class TTSVoice
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Gender { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string? Description { get; set; }
        public bool IsDefault { get; set; }
        public string Provider { get; set; } = string.Empty;
    }

    /// <summary>
    /// AI Service implementation for chat, configuration, and session management.
    /// </summary>
    public class AIService : IAIService
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage;
        private readonly ILogger<AIService> _logger;
        private string _authToken = string.Empty;
        private string _apiBaseUrl = string.Empty; // Must be configured via SetApiBaseUrl - no hardcoded default
        private bool _authFailureFired;

        public event EventHandler? AuthenticationFailed;

        public AIService(HttpClient httpClient, ILocalStorageService localStorage, ILogger<AIService> logger)
        {
            _httpClient = httpClient;
            _localStorage = localStorage;
            _logger = logger;
        }

        /// <summary>
        /// Checks an HTTP response for 401/403 status codes and fires AuthenticationFailed if detected.
        /// Throws HttpRequestException for auth failures so callers can detect and handle them.
        /// </summary>
        private void CheckForAuthFailure(System.Net.Http.HttpResponseMessage response, string operationName)
        {
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                _logger.LogWarning("Authentication failed during {Operation}: {StatusCode}", operationName, response.StatusCode);

                // Only fire the event once per service lifetime to avoid flooding
                if (!_authFailureFired)
                {
                    _authFailureFired = true;
                    AuthenticationFailed?.Invoke(this, EventArgs.Empty);
                }

                throw new HttpRequestException($"Unauthorized: {operationName} returned {(int)response.StatusCode}");
            }
        }

        public void SetAuthToken(string token)
        {
            _logger.LogInformation("?? AIService: Setting auth token");
            _logger.LogInformation("?? AIService: Token length: {TokenLength}", token?.Length ?? 0);
            _logger.LogInformation("?? AIService: Token preview: {TokenPreview}...", 
                token?.Substring(0, Math.Min(20, token.Length)) ?? "null");
            
            _authToken = token ?? string.Empty;
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            _authFailureFired = false; // Reset so future auth failures are detected after token refresh

            _logger.LogInformation("? AIService: Authorization header set on AIService HttpClient");
        }

        public void SetApiBaseUrl(string apiBaseUrl)
        {
            _apiBaseUrl = apiBaseUrl ?? string.Empty;
            _logger.LogInformation("?? AIService: API base URL set to: {ApiBaseUrl}", _apiBaseUrl);
        }

        public async Task<AIChatResponse> SendMessageAsync(AIChatRequest request)
        {
            try
            {
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/ai/chat", content);

                CheckForAuthFailure(response, "SendMessage");

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var aiResponse = JsonSerializer.Deserialize<AIChatResponse>(responseJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    return aiResponse ?? new AIChatResponse { IsError = true, ErrorMessage = "Failed to deserialize response" };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("AI API call failed: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    return ParseErrorResponse(response.StatusCode, errorContent);
                }
            }
            catch (HttpRequestException) when (_authFailureFired)
            {
                throw; // Let auth failures propagate to callers
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending AI message");
                return new AIChatResponse
                {
                    IsError = true,
                    ErrorMessage = $"Network error: {ex.Message}"
                };
            }
        }

        public async Task<AIChatResponse> SendMessageWithFileAsync(AIChatRequest request, byte[] fileBytes, string fileName)
        {
            try
            {
                var fileBase64 = Convert.ToBase64String(fileBytes);
                var mediaType = FileMediaTypeHelper.GetMediaTypeFromFileName(fileName);

                var apiRequest = new WildwoodAIRequestDto
                {
                    ConfigurationId = request.ConfigurationId,
                    SessionId = request.SessionId,
                    Message = request.Message,
                    SaveToSession = request.SaveToSession,
                    MacroValues = request.MacroValues,
                    FileBase64 = fileBase64,
                    FileMediaType = mediaType,
                    FileName = fileName
                };

                var json = JsonSerializer.Serialize(apiRequest, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/ai/chat", content);

                CheckForAuthFailure(response, "SendMessageWithFile");

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var aiResponse = JsonSerializer.Deserialize<AIChatResponse>(responseJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    return aiResponse ?? new AIChatResponse { IsError = true, ErrorMessage = "Failed to deserialize response" };
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("AI API file call failed: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    return ParseErrorResponse(response.StatusCode, errorContent);
                }
            }
            catch (HttpRequestException) when (_authFailureFired)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending AI message with file");
                return new AIChatResponse
                {
                    IsError = true,
                    ErrorMessage = $"Network error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Parses an API error response body to extract a user-friendly error message and error code.
        /// Handles the structured error JSON format: { "error": "...", "limitCode": "...", "currentUsage": N, "maxValue": N, ... }
        /// </summary>
        private AIChatResponse ParseErrorResponse(System.Net.HttpStatusCode statusCode, string errorContent)
        {
            var response = new AIChatResponse { IsError = true };

            try
            {
                if (!string.IsNullOrWhiteSpace(errorContent) && errorContent.TrimStart().StartsWith("{"))
                {
                    using var doc = JsonDocument.Parse(errorContent);
                    var root = doc.RootElement;

                    // Extract error code (e.g., "AI_TOKENS", "AI_REQUESTS")
                    if (root.TryGetProperty("limitCode", out var limitCode))
                    {
                        response.ErrorCode = limitCode.GetString();
                    }

                    // Build a user-friendly message from the structured error
                    if (root.TryGetProperty("error", out var errorMsg))
                    {
                        var message = errorMsg.GetString() ?? $"API call failed: {statusCode}";

                        // Append usage details if available
                        if (root.TryGetProperty("currentUsage", out var usage) &&
                            root.TryGetProperty("maxValue", out var maxVal))
                        {
                            var unit = root.TryGetProperty("unit", out var unitProp) ? unitProp.GetString() : "units";
                            message += $" ({usage.GetInt64():N0}/{maxVal.GetInt64():N0} {unit})";
                        }

                        // Append period end if available
                        if (root.TryGetProperty("periodEnd", out var periodEnd))
                        {
                            var periodEndStr = periodEnd.GetString();
                            if (DateTime.TryParse(periodEndStr, out var periodEndDate))
                            {
                                message += $". Resets {periodEndDate:MMM d, yyyy}";
                            }
                        }

                        response.ErrorMessage = message;
                        return response;
                    }

                    // Fallback: try generic "message" or "statusMessage" fields
                    if (root.TryGetProperty("statusMessage", out var statusMsg))
                    {
                        response.ErrorMessage = statusMsg.GetString() ?? $"API call failed: {statusCode}";
                        return response;
                    }
                    if (root.TryGetProperty("message", out var msg))
                    {
                        response.ErrorMessage = msg.GetString() ?? $"API call failed: {statusCode}";
                        return response;
                    }
                }
            }
            catch (JsonException)
            {
                // Error body wasn't valid JSON - fall through to default
            }

            response.ErrorMessage = $"API call failed: {statusCode}";
            return response;
        }

        public async Task<List<AIConfiguration>> GetConfigurationsAsync(string? configurationType = null)
        {
            try
            {
                var url = $"{_apiBaseUrl}/ai/configurations";
                if (!string.IsNullOrEmpty(configurationType))
                {
                    url += $"?configurationType={Uri.EscapeDataString(configurationType)}";
                }

                _logger.LogInformation("?? AIService: Getting AI configurations from {ApiUrl}", url);
                _logger.LogInformation("?? AIService: Auth token set: {HasToken}", !string.IsNullOrEmpty(_authToken));
                _logger.LogInformation("?? AIService: HttpClient auth header: {HasAuthHeader}",
                    _httpClient.DefaultRequestHeaders.Authorization != null);

                if (_httpClient.DefaultRequestHeaders.Authorization != null)
                {
                    _logger.LogInformation("?? AIService: Auth header scheme: {Scheme}, parameter preview: {ParamPreview}...",
                        _httpClient.DefaultRequestHeaders.Authorization.Scheme,
                        _httpClient.DefaultRequestHeaders.Authorization.Parameter?.Substring(0, Math.Min(20, _httpClient.DefaultRequestHeaders.Authorization.Parameter.Length)) ?? "null");
                }

                var response = await _httpClient.GetAsync(url);

                _logger.LogInformation("?? AIService: Response status: {StatusCode}", response.StatusCode);

                CheckForAuthFailure(response, "GetConfigurations");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("? AIService: Successfully received configurations, JSON length: {JsonLength}", json.Length);
                    var configurations = JsonSerializer.Deserialize<List<AIConfiguration>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    return configurations ?? new List<AIConfiguration>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("? AIService: Failed to get configurations. Status: {StatusCode}, Error: {ErrorContent}",
                        response.StatusCode, errorContent);
                }

                return new List<AIConfiguration>();
            }
            catch (HttpRequestException) when (_authFailureFired)
            {
                throw; // Let auth failures propagate to callers
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "?? AIService: Error fetching AI configurations");
                return new List<AIConfiguration>();
            }
        }

        public async Task<AIConfiguration?> GetConfigurationAsync(string configurationId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/ai/configurations/{configurationId}");
                CheckForAuthFailure(response, "GetConfiguration");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<AIConfiguration>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }
                return null;
            }
            catch (HttpRequestException) when (_authFailureFired)
            {
                throw; // Let auth failures propagate to callers
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching AI configuration {ConfigurationId}", configurationId);
                return null;
            }
        }

        public async Task<AISession?> CreateSessionAsync(string configurationId, string? sessionName = null)
        {
            try
            {
                var request = new { ConfigurationId = configurationId, SessionName = sessionName };
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/ai/sessions", content);
                CheckForAuthFailure(response, "CreateSession");
                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<AISession>(responseJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }
                return null;
            }
            catch (HttpRequestException) when (_authFailureFired)
            {
                throw; // Let auth failures propagate to callers
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating AI session");
                return null;
            }
        }

        public async Task<AISession?> GetSessionAsync(string sessionId)
        {
            try
            {
                var url = $"{_apiBaseUrl}/ai/sessions/{sessionId}";
                _logger.LogInformation("?? AIService: Fetching session {SessionId} from {Url}", sessionId, url);
                
                var response = await _httpClient.GetAsync(url);

                _logger.LogInformation("?? AIService: GetSession response status: {StatusCode}", response.StatusCode);

                CheckForAuthFailure(response, "GetSession");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("?? AIService: Session JSON length: {Length}", json.Length);
                    
                    // Log a portion of the JSON to see what we're getting
                    if (json.Length > 200)
                    {
                        _logger.LogInformation("?? AIService: Session JSON start: {JsonStart}", json.Substring(0, 200));
                        _logger.LogInformation("?? AIService: Session JSON end: {JsonEnd}", json.Substring(json.Length - Math.Min(200, json.Length)));
                    }
                    else
                    {
                        _logger.LogInformation("?? AIService: Session JSON: {Json}", json);
                    }
                    
                    // Check if the JSON contains "messages" array
                    if (json.Contains("\"messages\""))
                    {
                        _logger.LogInformation("?? AIService: JSON contains 'messages' field");
                    }
                    else
                    {
                        _logger.LogWarning("?? AIService: JSON does NOT contain 'messages' field!");
                    }
                    
                    var session = JsonSerializer.Deserialize<AISession>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    
                    if (session != null)
                    {
                        _logger.LogInformation("? AIService: Deserialized session - Id: {Id}, Name: '{Name}', MessagesCount: {MessageCount}", 
                            session.Id, session.Name, session.Messages?.Count ?? 0);
                        
                        if (session.Messages != null && session.Messages.Count > 0)
                        {
                            _logger.LogInformation("?? AIService: First message - Role: {Role}, Content preview: {ContentPreview}", 
                                session.Messages[0].Role, 
                                session.Messages[0].Content.Length > 50 ? session.Messages[0].Content.Substring(0, 50) : session.Messages[0].Content);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("?? AIService: Session deserialization returned null");
                    }
                    
                    return session;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("? AIService: Failed to get session {SessionId}. Status: {StatusCode}, Error: {Error}", 
                        sessionId, response.StatusCode, errorContent);
                }
                return null;
            }
            catch (HttpRequestException) when (_authFailureFired)
            {
                throw; // Let auth failures propagate to callers
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "? AIService: Error fetching AI session {SessionId}", sessionId);
                return null;
            }
        }

        public async Task<List<AISessionSummary>> GetSessionsAsync(string? configurationId = null)
        {
            try
            {
                var url = $"{_apiBaseUrl}/ai/sessions";
                if (!string.IsNullOrEmpty(configurationId))
                    url += $"?configurationId={configurationId}";

                _logger.LogInformation("?? AIService: Getting sessions from {Url}", url);

                var response = await _httpClient.GetAsync(url);
                CheckForAuthFailure(response, "GetSessions");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    _logger.LogInformation("?? AIService: Sessions response JSON length: {Length}", json.Length);
                    
                    // The backend returns AISessionSummaryDto directly, so deserialize to AISessionSummary
                    var sessions = JsonSerializer.Deserialize<List<AISessionSummary>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    _logger.LogInformation("? AIService: Loaded {Count} sessions", sessions?.Count ?? 0);
                    return sessions ?? new List<AISessionSummary>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("? AIService: Failed to get sessions. Status: {StatusCode}, Error: {ErrorContent}", 
                        response.StatusCode, errorContent);
                }
                return new List<AISessionSummary>();
            }
            catch (HttpRequestException) when (_authFailureFired)
            {
                throw; // Let auth failures propagate to callers
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching AI sessions");
                return new List<AISessionSummary>();
            }
        }

        public async Task<bool> EndSessionAsync(string sessionId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/ai/sessions/{sessionId}/end", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ending AI session {SessionId}", sessionId);
                return false;
            }
        }

        public async Task<bool> DeleteSessionAsync(string sessionId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_apiBaseUrl}/ai/sessions/{sessionId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting AI session {SessionId}", sessionId);
                return false;
            }
        }

        public async Task<bool> RenameSessionAsync(string sessionId, string newName)
        {
            try
            {
                var requestData = new { NewName = newName };
                var json = JsonSerializer.Serialize(requestData, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                _logger.LogInformation("?? AIService: Renaming session {SessionId} to '{NewName}'", sessionId, newName);
                
                var response = await _httpClient.PutAsync($"{_apiBaseUrl}/ai/sessions/{sessionId}/name", content);
                
                if (response.IsSuccessStatusCode)
                {
                    _logger.LogInformation("? AIService: Successfully renamed session {SessionId}", sessionId);
                    return true;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("? AIService: Failed to rename session. Status: {StatusCode}, Error: {ErrorContent}", 
                        response.StatusCode, errorContent);
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error renaming AI session {SessionId}", sessionId);
                return false;
            }
        }

        public Task<string> GetTTSAudioUrlAsync(string text, string? voice = null, string format = "mp3", double speed = 1.0)
        {
            // Build the TTS endpoint URL with query parameters for streaming audio
            var queryParams = new List<string>();
            
            // The audio will be fetched via POST, but we return the endpoint URL
            // The JavaScript will handle the actual POST request with the text body
            var url = $"{_apiBaseUrl}/tts/synthesize";
            
            _logger.LogInformation("TTS audio URL generated: {Url}", url);
            
            return Task.FromResult(url);
        }

        public async Task<List<TTSVoice>> GetTTSVoicesAsync()
        {
            try
            {
                _logger.LogInformation("Fetching TTS voices from {Url}", $"{_apiBaseUrl}/tts/voices");
                
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/tts/voices");
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var voices = JsonSerializer.Deserialize<List<TTSVoice>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    _logger.LogInformation("Successfully loaded {Count} TTS voices", voices?.Count ?? 0);
                    return voices ?? new List<TTSVoice>();
                }
                else
                {
                    _logger.LogWarning("Failed to fetch TTS voices: {StatusCode}", response.StatusCode);
                    return new List<TTSVoice>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching TTS voices");
                return new List<TTSVoice>();
            }
        }

        /// <inheritdoc/>
        public async Task<List<TTSVoice>> GetTTSVoicesForConfigurationAsync(string configurationId)
        {
            try
            {
                var url = $"{_apiBaseUrl}/tts/voices/configuration/{configurationId}";
                _logger.LogInformation("?? AIService: Fetching TTS voices for configuration {ConfigId} from {Url}", configurationId, url);
                
                var response = await _httpClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var voices = JsonSerializer.Deserialize<List<TTSVoice>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    _logger.LogInformation("?? AIService: Successfully loaded {Count} TTS voices for configuration {ConfigId}", 
                        voices?.Count ?? 0, configurationId);
                    return voices ?? new List<TTSVoice>();
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("?? AIService: Failed to fetch TTS voices for configuration {ConfigId}. Status: {StatusCode}, Error: {Error}", 
                        configurationId, response.StatusCode, errorContent);
                    return new List<TTSVoice>();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "?? AIService: Error fetching TTS voices for configuration {ConfigId}", configurationId);
                return new List<TTSVoice>();
            }
        }

        /// <inheritdoc/>
        public async Task<(string AudioBase64, string ContentType)?> SynthesizeSpeechAsync(string text, string voice, double speed = 1.0, string? configurationId = null)
        {
            try
            {
                var url = $"{_apiBaseUrl}/tts/synthesize/base64";
                _logger.LogInformation("?? AIService: Synthesizing speech, text length: {Length}, voice: {Voice}, speed: {Speed}", 
                    text.Length, voice, speed);

                var request = new
                {
                    Text = text,
                    Voice = voice,
                    Speed = speed,
                    Format = "mp3",
                    ConfigurationId = configurationId
                };

                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(url, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var ttsResponse = JsonSerializer.Deserialize<TTSResponse>(responseJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    
                    if (ttsResponse != null && ttsResponse.Success && !string.IsNullOrEmpty(ttsResponse.AudioData))
                    {
                        _logger.LogInformation("?? AIService: Speech synthesis successful, audio size: {Size} bytes", 
                            ttsResponse.AudioData.Length);
                        return (ttsResponse.AudioData, ttsResponse.ContentType ?? "audio/mpeg");
                    }
                    else
                    {
                        _logger.LogWarning("?? AIService: TTS response was unsuccessful: {Error}", ttsResponse?.ErrorMessage ?? "Unknown error");
                        return null;
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("?? AIService: TTS synthesis failed. Status: {StatusCode}, Error: {Error}", 
                        response.StatusCode, errorContent);
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "?? AIService: Error synthesizing speech");
                return null;
            }
        }
    }

    /// <summary>
    /// Response model for TTS base64 synthesis.
    /// </summary>
    internal class TTSResponse
    {
        public bool Success { get; set; }
        public string? AudioData { get; set; }
        public string? ContentType { get; set; }
        public int CharactersProcessed { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
