using System.Text.Json.Serialization;

namespace WildwoodComponents.Razor.Models;

public class AIChatViewModel
{
    public string? ConfigurationId { get; set; }
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-ai";
    public string? Title { get; set; }
    public bool ShowSessionSidebar { get; set; } = true;
    public bool ShowConfigurationSelector { get; set; } = true;
    public bool EnableTTS { get; set; } = true;
    public bool EnableSTT { get; set; } = true;
    public bool EnableFileUpload { get; set; }
    public string? PlaceholderText { get; set; }
    public List<AIConfigurationDto> Configurations { get; set; } = new();
    public List<AISessionSummaryDto> Sessions { get; set; } = new();
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

public class AIConfigurationDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Model { get; set; }
    public string? ProviderType { get; set; }
    public bool IsActive { get; set; }
    public bool PersistentSessionEnabled { get; set; }
    public string? ConfigurationType { get; set; }
    public bool EnableTTS { get; set; }
    public string? TTSModel { get; set; }
    public string? TTSDefaultVoice { get; set; }
    public double TTSDefaultSpeed { get; set; } = 1.0;
    public string? TTSDefaultFormat { get; set; }
}

public class AISessionSummaryDto
{
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sessionName")]
    public string? Name { get; set; }
    public int MessageCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastAccessedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool IsActive { get; set; }
    public string? LastMessagePreview { get; set; }
}

public class AIMessageDto
{
    public string Id { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("createdAt")]
    public DateTime Timestamp { get; set; }
    public int? TokenCount { get; set; }
    public bool IsError { get; set; }
}

public class AIChatRequestDto
{
    public string? ConfigurationId { get; set; }
    public string? SessionId { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool SaveToSession { get; set; } = true;
    public Dictionary<string, string>? MacroValues { get; set; }
    public string? FileBase64 { get; set; }
    public string? FileMediaType { get; set; }
    public string? FileName { get; set; }
}

public class AIChatResponseDto
{
    public string? Id { get; set; }
    public string? SessionId { get; set; }
    public string Response { get; set; } = string.Empty;
    public int? TokensUsed { get; set; }
    public string? Model { get; set; }
    public string? ProviderType { get; set; }
    public DateTime? CreatedAt { get; set; }
    public bool IsError { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AISessionDto
{
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("sessionName")]
    public string? Name { get; set; }
    public List<AIMessageDto> Messages { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public bool IsActive { get; set; }
    public int MessageCount { get; set; }
}

public class TTSVoiceDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Gender { get; set; }
    public string? Language { get; set; }
    public string? Description { get; set; }
    public bool IsDefault { get; set; }
}
