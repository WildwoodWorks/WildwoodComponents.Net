using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

public interface IWildwoodAIChatService
{
    Task<List<AIConfigurationDto>> GetConfigurationsAsync(string? configurationType = null);
    Task<AIConfigurationDto?> GetConfigurationAsync(string configurationId);
    Task<AIChatResponseDto> SendMessageAsync(AIChatRequestDto request);
    Task<AIChatResponseDto> SendMessageWithFileAsync(AIChatRequestDto request, byte[] fileBytes, string fileName);
    Task<AISessionDto?> CreateSessionAsync(string configurationId, string? sessionName = null);
    Task<AISessionDto?> GetSessionAsync(string sessionId);
    Task<List<AISessionSummaryDto>> GetSessionsAsync(string? configurationId = null);
    Task<bool> EndSessionAsync(string sessionId);
    Task<bool> DeleteSessionAsync(string sessionId);
    Task<bool> RenameSessionAsync(string sessionId, string newName);
    Task<List<TTSVoiceDto>> GetTTSVoicesAsync(string? configurationId = null);
    Task<(string AudioBase64, string ContentType)?> SynthesizeSpeechAsync(string text, string voice, double speed = 1.0, string? configurationId = null);
}
