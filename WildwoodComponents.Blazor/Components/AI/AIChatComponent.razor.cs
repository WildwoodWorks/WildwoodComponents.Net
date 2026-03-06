using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Blazor.Services;
using WildwoodComponents.Blazor.Components.Base;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// AI Chat component that provides a conversational interface with AI configurations.
/// Supports sessions, speech-to-text, text-to-speech, and multiple AI configurations.
/// </summary>
/// <remarks>
/// This component is split into multiple partial class files for maintainability:
/// - AIChatComponent.razor.cs - Core: Parameters, fields, lifecycle, disposal
/// - AIChatComponent.SpeechToText.cs - Speech-to-text functionality
/// - AIChatComponent.TextToSpeech.cs - Text-to-speech functionality
/// - AIChatComponent.Configuration.cs - Configuration management
/// - AIChatComponent.Session.cs - Session management
/// - AIChatComponent.Messages.cs - Message handling and input
/// - AIChatComponent.Helpers.cs - UI and collection helpers
/// </remarks>
public partial class AIChatComponent : BaseWildwoodComponent
{
    [Inject] private IAIService AIService { get; set; } = default!;
    [Inject] private ILocalStorageService LocalStorage { get; set; } = default!;

    // Storage keys for user preferences
    private const string VoicePreferenceKey = "aicoach_tts_voice";
    private const string SpeedPreferenceKey = "aicoach_tts_speed";
    private const string TTSEnabledPreferenceKey = "aicoach_tts_enabled";
    private const string STTEnabledPreferenceKey = "aicoach_stt_enabled";
    private const string AutoListenPreferenceKey = "aicoach_auto_listen";
    
    // Cached voice preference (loaded before voices are available)
    private string? _pendingVoicePreference;
    
    // Flag to track if auto-listen should be started on load
    private bool _autoListenOnLoad = false;

    #region Parameters

    [Parameter] public string? ConfigurationId { get; set; }
    [Parameter] public string? AuthToken { get; set; }
    [Parameter] public AIChatSettings Settings { get; set; } = new();
    [Parameter] public EventCallback<AIMessage> OnMessageSent { get; set; }
    [Parameter] public EventCallback<AIMessage> OnMessageReceived { get; set; }
    [Parameter] public EventCallback<AISession> OnSessionCreated { get; set; }
    [Parameter] public EventCallback<AISession> OnSessionEnded { get; set; }
    [Parameter] public EventCallback OnAuthenticationFailed { get; set; }

    #endregion

    #region Private Fields

    private List<AIMessage> Messages = new();
    private List<AIConfiguration> Configurations = new();
    private List<AISessionSummary> Sessions = new();
    private AIConfiguration? CurrentConfiguration;
    private AISession? CurrentSession;
    private string CurrentConfigurationId = string.Empty;
    private string CurrentSessionId = string.Empty;
    private ChatTypingIndicator TypingIndicator = new();

    private ElementReference messagesContainer;
    private ElementReference messageInput;

    // Sidebar state
    private bool IsSidebarOpen = false;
    
    // Session details popup state (for touch devices)
    private bool ShowSessionDetailsPopup = false;
    private AISessionSummary? SelectedSessionForDetails;
    
    // Speech controls menu state (for narrow screens)
    private bool ShowSpeechMenu = false;

    // Speech-to-Text state
    private bool IsSpeechToTextEnabled = false;
    private bool IsListeningForSpeech = false;
    private string InterimTranscript = string.Empty;
    private DotNetObjectReference<AIChatComponent>? speechToTextRef;

    // Input handler reference for Enter key
    private DotNetObjectReference<AIChatComponent>? inputHandlerRef;

    // Text-to-Speech state
    private bool IsTextToSpeechEnabled = false;
    private bool IsSpeakingMessage = false;
    private DotNetObjectReference<AIChatComponent>? textToSpeechRef;
    private bool UseApiTTS = false;
    private bool IsUsingBrowserTTS = false;
    private bool ShowTTSSettings = false;
    private string SelectedVoice = string.Empty;
    private double SelectedSpeed = 1.0;

    // Available TTS voices - loaded dynamically from API
    private List<VoiceOption> AvailableVoices = new();
    private bool IsLoadingVoices = false;

    private string CurrentMessage = string.Empty;

    #endregion

    #region Nested Classes

    private class VoiceOption
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Style { get; set; }
        public string? Gender { get; set; }
        public string? QualityTier { get; set; }
    }

    private class VoiceApiResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Gender { get; set; }
        public string? Language { get; set; }
        public string? Description { get; set; }
        public bool IsDefault { get; set; }
        public string Provider { get; set; } = string.Empty;
        public string? Style { get; set; }
        public string? Accent { get; set; }
        public string? QualityTier { get; set; }
        public string? PreviewAudioUrl { get; set; }
    }

    private class SpeechResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public bool RequiresPermission { get; set; }
        public string? PermissionState { get; set; }
        public string? Instructions { get; set; }
        public string? Platform { get; set; }
    }

    #endregion

    #region Computed Properties

    private bool CanSendMessage => !IsLoading && !string.IsNullOrWhiteSpace(CurrentMessage) &&
        (CurrentConfiguration != null || Settings.ShowDebugInfo || Configurations.Count == 0);

    #endregion

    #region Authentication Helpers

    /// <summary>
    /// Checks if an exception indicates an authentication/authorization failure.
    /// </summary>
    private bool IsAuthenticationError(Exception ex)
    {
        if (ex == null) return false;

        var message = ex.Message ?? string.Empty;
        if (message.Contains("Unauthorized") || message.Contains("401") || message.Contains("403"))
        {
            return true;
        }

        // Check inner exception
        if (ex.InnerException != null)
        {
            var innerMessage = ex.InnerException.Message ?? string.Empty;
            if (innerMessage.Contains("Unauthorized") || innerMessage.Contains("401") || innerMessage.Contains("403"))
            {
                return true;
            }
        }

        return false;
    }

    #endregion

    #region Lifecycle Methods

    protected override async Task OnComponentInitializedAsync()
    {
        try
        {
            await LoadConfigurations();
        }
        catch (Exception ex) when (IsAuthenticationError(ex))
        {
            Logger?.LogWarning(ex, "Authentication failure during component initialization - notifying parent");
            await OnAuthenticationFailed.InvokeAsync();
            return;
        }

        if (!string.IsNullOrEmpty(ConfigurationId))
        {
            var requestedConfig = FindConfigurationById(ConfigurationId);
            if (requestedConfig != null)
            {
                CurrentConfigurationId = ConfigurationId;
                await LoadConfiguration(ConfigurationId);
            }
            else
            {
                Logger?.LogWarning("Requested configuration {ConfigurationId} not found in available configurations", ConfigurationId);
                if (HasAnyConfigurations())
                {
                    var firstConfig = GetFirstConfiguration();
                    if (firstConfig != null)
                    {
                        CurrentConfigurationId = firstConfig.Id;
                        CurrentConfiguration = firstConfig;
                        ApplyConfigurationTTSSettings();
                        Logger?.LogInformation("Using fallback configuration: {ConfigurationName} ({ConfigurationId})",
                            CurrentConfiguration.Name, CurrentConfiguration.Id);
                    }
                }
            }
        }
        else if (HasAnyConfigurations())
        {
            var firstConfig = GetFirstConfiguration();
            if (firstConfig != null)
            {
                CurrentConfigurationId = firstConfig.Id;
                CurrentConfiguration = firstConfig;
                ApplyConfigurationTTSSettings();
            }
        }

        if (Settings.EnableSessions && CurrentConfiguration != null)
        {
            await LoadSessions();

            if (Settings.AutoLoadRecentSession && HasAnySessions())
            {
                var mostRecentSession = GetMostRecentSession();
                if (mostRecentSession != null)
                {
                    await LoadSession(mostRecentSession.Id);
                }
            }
            else if (!HasAnySessions())
            {
                // Auto-create an initial session when none exist
                await CreateInitialSessionAsync();
            }
        }

        if (!string.IsNullOrEmpty(Settings.ApiBaseUrl))
        {
            AIService.SetApiBaseUrl(Settings.ApiBaseUrl);
        }

        UseApiTTS = Settings.UseServerTTS;
        IsUsingBrowserTTS = !UseApiTTS || string.IsNullOrEmpty(Settings.ApiBaseUrl);

        if (Settings.EnableTextToSpeech)
        {
            await LoadAvailableVoicesAsync();

            if (UseApiTTS && !string.IsNullOrEmpty(Settings.ApiBaseUrl))
            {
                IsUsingBrowserTTS = AvailableVoices.Count == 0;

                if (IsUsingBrowserTTS)
                {
                    Logger?.LogWarning("?? TTS: No voices loaded from API - falling back to browser TTS");
                }
                else
                {
                    Logger?.LogInformation("?? TTS: {VoiceCount} voices loaded - using server TTS", AvailableVoices.Count);
                }
            }
        }

        // Note: Voice and speed settings are now loaded from user preferences in OnAfterRenderAsync
        // Only set defaults here if no configuration or pending preference will be applied
        // The sync InitializeVoiceSettings/InitializeSpeedSettings are no longer called here
        // to avoid overwriting user preferences loaded from storage
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            Logger?.LogInformation("?? TTS: OnAfterRenderAsync(firstRender=true) - Loading preferences...");
            Logger?.LogInformation("?? TTS: Before loading - SelectedVoice='{Voice}', AvailableVoices.Count={Count}", 
                SelectedVoice, AvailableVoices.Count);
            
            // Load user's saved preferences (requires JS interop, so must be after first render)
            await LoadUserTTSPreferencesAsync();
            
            Logger?.LogInformation("?? TTS: After loading - _pendingVoicePreference='{Pending}', SelectedVoice='{Voice}'", 
                _pendingVoicePreference ?? "(null)", SelectedVoice);
            
            // Now that preferences are loaded, try to apply voice preference if voices were already loaded
            if (!string.IsNullOrEmpty(_pendingVoicePreference) && AvailableVoices.Count > 0)
            {
                Logger?.LogInformation("?? TTS: Voices already loaded, applying pending preference now");
                ApplyPendingVoicePreference();
            }
            else if (!string.IsNullOrEmpty(_pendingVoicePreference))
            {
                Logger?.LogInformation("?? TTS: Voices not loaded yet, preference will be applied when voices load");
            }
            
            // Only auto-start listening if BOTH STT is enabled AND auto-listen was saved as true
            if (IsSpeechToTextEnabled && Settings.EnableSpeechToText && _autoListenOnLoad && !IsListeningForSpeech)
            {
                Logger?.LogInformation("?? STT: Auto-starting speech recognition from saved auto-listen preference");
                await StartListening();
            }
            else if (IsSpeechToTextEnabled && !_autoListenOnLoad)
            {
                Logger?.LogInformation("?? STT: STT enabled but auto-listen is off - not starting listening");
            }
            
            Logger?.LogInformation("?? TTS: OnAfterRenderAsync complete - SelectedVoice='{Voice}'", SelectedVoice);

            // Set up Enter key handler for chat input
            inputHandlerRef = DotNetObjectReference.Create(this);
            try
            {
                await JSRuntime.InvokeVoidAsync("setupChatInputKeyHandler", messageInput, inputHandlerRef);
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to set up chat input key handler");
            }

            StateHasChanged();
        }
        
        if (Settings.AutoScroll && GetVisibleMessagesCount() > 0)
        {
            await ScrollToBottom();
        }
    }

    #endregion

    #region Input Handling

    /// <summary>
    /// Called from JavaScript when Enter key is pressed in the chat input.
    /// </summary>
    [JSInvokable]
    public async Task HandleEnterKeyPress()
    {
        await SendMessage();
    }

    #endregion

    #region IDisposable

    public new void Dispose()
    {
        // Clean up JS event handler
        try
        {
            _ = JSRuntime.InvokeVoidAsync("removeChatInputKeyHandler", messageInput);
        }
        catch
        {
            // Ignore errors during disposal
        }

        inputHandlerRef?.Dispose();
        speechToTextRef?.Dispose();
        textToSpeechRef?.Dispose();
        base.Dispose();
    }

    #endregion
}
