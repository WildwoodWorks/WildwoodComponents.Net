using WildwoodComponents.Shared.Models;

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
    public List<AIConfiguration> Configurations { get; set; } = new();
    public List<AISessionSummary> Sessions { get; set; } = new();
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];
}

// DTOs (AIConfigurationDto, AISessionSummaryDto, AIMessageDto, AIChatRequestDto,
// AIChatResponseDto, AISessionDto, TTSVoiceDto) have been consolidated into
// WildwoodComponents.Shared.Models as AIConfiguration, AISessionSummary, AIMessage,
// AIChatRequest, AIChatResponse, AISession, TTSVoice.
