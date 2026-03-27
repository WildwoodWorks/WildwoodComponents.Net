using System.Text.Json.Serialization;

namespace WildwoodComponents.Shared.Models;

// ──────────────────────────────────────────────
// AI Chat shared DTOs
// Used by both WildwoodComponents.Blazor and WildwoodComponents.Razor
// ──────────────────────────────────────────────

public class AIMessage
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty; // user, assistant, system
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    public int TokenCount { get; set; }
    public bool IsError { get; set; }

    // Additional properties from server DTO
    public string? SessionId { get; set; }
    public int MessageOrder { get; set; }
    public string? ParentMessageId { get; set; }
    public bool IsEdited { get; set; }
    public DateTime? EditedAt { get; set; }
}

public class AISession
{
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sessionName")]
    public string Name { get; set; } = string.Empty;

    public List<AIMessage> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? EndedAt { get; set; }
    public bool IsActive { get; set; } = true;

    // Additional properties from server DTO
    public string? UserId { get; set; }

    [JsonPropertyName("aIConfigurationId")]
    public string? AIConfigurationId { get; set; }

    public DateTime LastAccessedAt { get; set; }
    public int MessageCount { get; set; }
    public string? LastMessagePreview { get; set; }
}

public class AIConfiguration
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool PersistentSessionEnabled { get; set; }
    public string ConfigurationType { get; set; } = "chat";

    // TTS Configuration
    public bool EnableTTS { get; set; } = false;
    public string? TTSModel { get; set; }
    public string? TTSDefaultVoice { get; set; }
    public double TTSDefaultSpeed { get; set; } = 1.0;
    public string TTSDefaultFormat { get; set; } = "mp3";
    public string? TTSEnabledVoicesJson { get; set; }
}

public class AISessionSummary
{
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sessionName")]
    public string Name { get; set; } = string.Empty;

    public int MessageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool IsActive { get; set; } = true;
    public string? LastMessagePreview { get; set; }
}

public class ComponentTheme
{
    public string PrimaryColor { get; set; } = "#007bff";
    public string SecondaryColor { get; set; } = "#6c757d";
    public string SuccessColor { get; set; } = "#28a745";
    public string WarningColor { get; set; } = "#ffc107";
    public string DangerColor { get; set; } = "#dc3545";
    public string InfoColor { get; set; } = "#17a2b8";
    public string LightColor { get; set; } = "#f8f9fa";
    public string DarkColor { get; set; } = "#343a40";
    public string FontFamily { get; set; } = "system-ui, -apple-system, sans-serif";
    public string BorderRadius { get; set; } = "0.375rem";
    public string BoxShadow { get; set; } = "0 0.125rem 0.25rem rgba(0, 0, 0, 0.075)";
}

// Extension Methods Helper Class
public static class ComponentExtensions
{
    public static string GetThemeClass(this ComponentTheme theme)
    {
        return $"theme-{theme.PrimaryColor.Replace("#", "").ToLower()}";
    }
}

public class AIChatSettings
{
    public string ApiBaseUrl { get; set; } = string.Empty;
    public bool EnableSessions { get; set; } = true;
    public bool AutoLoadRecentSession { get; set; } = true;
    public bool ShowTokenUsage { get; set; } = true;
    public bool AutoScroll { get; set; } = true;
    public bool EnableFileUpload { get; set; } = false;
    public bool EnableVoiceInput { get; set; } = false;
    public bool EnableSpeechToText { get; set; } = false;
    public bool EnableTextToSpeech { get; set; } = false;
    public bool UseServerTTS { get; set; } = true;
    public string? TTSVoice { get; set; } = "alloy";
    public double TTSSpeed { get; set; } = 1.0;
    public bool ShowDebugInfo { get; set; } = false;
    public bool ShowConfigurationName { get; set; } = true;
    public bool ShowConfigurationSelector { get; set; } = true;
    public string PlaceholderText { get; set; } = "Ask anything";
    public string WelcomeMessage { get; set; } = "What's on the agenda today?";
    public int MaxHistorySize { get; set; } = 100;
    public int MaxMessageLength { get; set; } = 4000;
    public ComponentTheme Theme { get; set; } = new();
}

public class ChatTypingIndicator
{
    public bool IsVisible { get; set; }
    public string Text { get; set; } = "is typing...";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}

public class AIChatRequest
{
    public string ConfigurationId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool SaveToSession { get; set; } = true;
    public Dictionary<string, string> MacroValues { get; set; } = new();
}

public class AIChatResponse
{
    public string Id { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public string Response { get; set; } = string.Empty;
    public int TokensUsed { get; set; }
    public string Model { get; set; } = string.Empty;
    public string ProviderType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
    /// <summary>
    /// Structured error code from the API (e.g., "AI_TOKENS", "RATE_LIMIT").
    /// Allows callers to distinguish error types without parsing the message string.
    /// </summary>
    public string? ErrorCode { get; set; }
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
