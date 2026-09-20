using WildwoodComponents.Shared.Models;
using WildwoodComponents.Shared.Utilities;

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

    /// <summary>
    /// Where a recorded clip is uploaded when the browser has no working Web Speech API —
    /// <c>WildwoodSpeechProxyController</c>'s route. Rendered only while
    /// <see cref="EnableSTT"/> is true, so the script has nothing to post to otherwise.
    /// </summary>
    public string SpeechProxyUrl { get; set; } = "/api/wildwood-stt/transcribe";

    /// <summary>
    /// BCP-47 tag for live recognition and for the transcription provider. Empty leaves the
    /// script on its own default (<c>en-US</c>), which is what Blazor uses.
    /// </summary>
    public string? SpeechLanguage { get; set; }

    /// <summary>Longest clip the recorder captures before it stops itself and transcribes.</summary>
    public int MaxRecordingSeconds { get; set; } = SpeechAudioFormats.MaxRecordingSeconds;

    /// <summary>Largest clip that is uploaded at all; a bigger one is refused in the browser.</summary>
    public long MaxRecordingBytes { get; set; } = SpeechAudioFormats.MaxAudioBytes;
}

// DTOs (AIConfigurationDto, AISessionSummaryDto, AIMessageDto, AIChatRequestDto,
// AIChatResponseDto, AISessionDto, TTSVoiceDto) have been consolidated into
// WildwoodComponents.Shared.Models as AIConfiguration, AISessionSummary, AIMessage,
// AIChatRequest, AIChatResponse, AISession, TTSVoice.
