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

    /// <summary>
    /// Transcribes a recorded clip server-side (<c>POST api/stt/transcribe</c>) — the fallback for
    /// browsers with no working Web Speech API, reached from the browser through
    /// <c>WildwoodSpeechProxyController</c>.
    /// </summary>
    /// <param name="audio">The recorded bytes. Empty audio fails without calling the server.</param>
    /// <param name="contentType">
    /// The recorder's mime type. A codec parameter is dropped before it is sent
    /// (<c>audio/webm;codecs=opus</c> goes up as <c>audio/webm</c>), and it picks the upload's
    /// file extension, which is how the provider knows the container.
    /// </param>
    /// <param name="configurationId">The chat's AI configuration; omitted from the form when empty.</param>
    /// <param name="language">A BCP-47 hint for the provider; omitted from the form when empty.</param>
    /// <returns>
    /// Never null and never throws: every failure — no audio, a refusal, a non-2xx, a dead
    /// connection — comes back as <c>Success = false</c> with a presentable
    /// <c>ErrorMessage</c>, exactly as Blazor's <c>IAIService.TranscribeAudioAsync</c> does.
    /// </returns>
    Task<SpeechTranscriptionResult> TranscribeAudioAsync(byte[] audio, string contentType, string? configurationId = null, string? language = null);
}
