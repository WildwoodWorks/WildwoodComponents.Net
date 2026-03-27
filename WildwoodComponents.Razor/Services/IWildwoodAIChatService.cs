using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public interface IWildwoodAIChatService
{
    Task<List<AIConfiguration>> GetConfigurationsAsync(string? configurationType = null);
    Task<AIConfiguration?> GetConfigurationAsync(string configurationId);
    Task<AIChatResponse> SendMessageAsync(AIChatRequest request);
    Task<AIChatResponse> SendMessageWithFileAsync(AIChatRequest request, byte[] fileBytes, string fileName);
    Task<AISession?> CreateSessionAsync(string configurationId, string? sessionName = null);
    Task<AISession?> GetSessionAsync(string sessionId);
    Task<List<AISessionSummary>> GetSessionsAsync(string? configurationId = null);
    Task<bool> EndSessionAsync(string sessionId);
    Task<bool> DeleteSessionAsync(string sessionId);
    Task<bool> RenameSessionAsync(string sessionId, string newName);
    Task<List<TTSVoice>> GetTTSVoicesAsync(string? configurationId = null);
    Task<(string AudioBase64, string ContentType)?> SynthesizeSpeechAsync(string text, string voice, double speed = 1.0, string? configurationId = null);
}
