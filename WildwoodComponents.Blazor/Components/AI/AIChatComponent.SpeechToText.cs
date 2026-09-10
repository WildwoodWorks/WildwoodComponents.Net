using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing Speech-to-Text functionality for the AI Chat component.
/// </summary>
public partial class AIChatComponent
{
    #region Speech-to-Text Fields

    private bool MicrophonePermissionDenied = false;
    private string? MicrophonePermissionError = null;
    private string? MicrophonePermissionInstructions = null;
    private string? MicrophonePermissionPlatform = null;
    
    /// <summary>
    /// Tracks whether speech recognition was paused due to TTS speaking.
    /// Used to determine if we should auto-resume after TTS ends.
    /// </summary>
    private bool WasPausedForTTS = false;

    #endregion

    #region Speech-to-Text Methods

    private async Task ToggleSpeechToText()
    {
        // Clear any previous permission errors when toggling
        MicrophonePermissionDenied = false;
        MicrophonePermissionError = null;
        
        IsSpeechToTextEnabled = !IsSpeechToTextEnabled;

        // Save the user's preference
        _ = SaveSTTEnabledPreferenceAsync();

        if (IsSpeechToTextEnabled)
        {
            StateHasChanged();
            // Enabling the feature is not a request to record: in recorder mode the mic button starts it
            await StartListeningCoreAsync(explicitStart: false);
        }
        else if (IsListeningForSpeech)
        {
            await StopListening();
        }

        StateHasChanged();
    }

    private async Task<bool> CheckMicrophonePermissionAsync()
    {
        try
        {
            Logger?.LogInformation("?? STT C#: Checking microphone permission...");
            var result = await JSRuntime.InvokeAsync<MicrophonePermissionResult>("aiChatInterop.checkMicrophonePermission");
            
            Logger?.LogInformation("?? STT C#: Permission state: {State}, CanRequest: {CanRequest}", 
                result.State, result.CanRequest);
            
            return result.IsGranted;
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "?? STT C#: Error checking microphone permission");
            return false;
        }
    }

    private async Task<bool> RequestMicrophonePermissionAsync()
    {
        try
        {
            Logger?.LogInformation("?? STT C#: Requesting microphone permission...");
            var result = await JSRuntime.InvokeAsync<MicrophonePermissionResult>("aiChatInterop.requestMicrophonePermission");
            
            if (result.Success)
            {
                Logger?.LogInformation("?? STT C#: Microphone permission granted");
                MicrophonePermissionDenied = false;
                MicrophonePermissionError = null;
                return true;
            }
            
            Logger?.LogWarning("?? STT C#: Microphone permission denied: {Error}", result.Error);
            MicrophonePermissionDenied = result.IsDenied;
            MicrophonePermissionError = result.Error;
            return false;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "?? STT C#: Exception requesting microphone permission");
            MicrophonePermissionError = "Failed to request microphone permission.";
            return false;
        }
    }

    /// <summary>
    /// Starts voice input from an explicit user action (mic button, permission retry).
    /// </summary>
    private Task StartListening() => StartListeningCoreAsync(explicitStart: true);

    /// <summary>
    /// Starts voice input automatically (auto-listen on load, resume after TTS). Never starts a
    /// recording — in recorder mode each clip is a server call, so it waits for the mic button.
    /// </summary>
    private Task StartListeningAutomaticallyAsync() => StartListeningCoreAsync(explicitStart: false);

    private async Task StartListeningCoreAsync(bool explicitStart)
    {
        if (IsListeningForSpeech || IsTranscribing) return;

        await EnsureSpeechInputModeAsync();

        if (_speechInputMode == SpeechInputMode.None)
        {
            Logger?.LogWarning("?? STT C#: No voice input available in this browser");
            IsSpeechToTextEnabled = false;
            await HandleErrorAsync(new Exception("Voice input isn't supported in this browser."), "Starting speech recognition");
            return;
        }

        if (IsRecorderMode)
        {
            if (!explicitStart)
            {
                Logger?.LogInformation("?? STT C#: Recorder mode - waiting for the mic button instead of auto-starting");
                return;
            }

            _listeningStartedExplicitly = true;
            try
            {
                await StartRecordingAsync();
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, "?? STT C#: Exception starting voice recording");
                IsSpeechToTextEnabled = false;
                await HandleErrorAsync(ex, "Starting voice recording");
            }
            return;
        }

        // Don't start listening if TTS is currently speaking
        if (IsSpeakingMessage)
        {
            Logger?.LogInformation("?? STT C#: TTS is speaking, will start listening when TTS finishes");
            WasPausedForTTS = true;
            return;
        }

        Logger?.LogInformation("?? STT C#: StartListening called - browser will prompt for permission if needed");
        _listeningStartedExplicitly = explicitStart;

        try
        {
            speechToTextRef ??= DotNetObjectReference.Create(this);
            var result = await JSRuntime.InvokeAsync<SpeechResult>("aiChatInterop.startSpeechToText", speechToTextRef);
            
            Logger?.LogInformation("?? STT C#: JS returned Success={Success}, RequiresPermission={RequiresPermission}, Error={Error}", 
                result.Success, result.RequiresPermission, result.Error ?? "none");

            if (result.Success)
            {
                IsListeningForSpeech = true;
                InterimTranscript = string.Empty;
                MicrophonePermissionDenied = false;
                MicrophonePermissionError = null;
                WasPausedForTTS = false;
                Logger?.LogInformation("?? STT C#: Now listening for speech");
                
                // Save the auto-listen preference
                _ = SaveAutoListenPreferenceAsync();
                
                StateHasChanged();
            }
            else if (result.RequiresPermission)
            {
                Logger?.LogWarning("?? STT C#: Microphone permission required - State: {State}", result.PermissionState);
                MicrophonePermissionDenied = true;
                MicrophonePermissionError = result.Error ?? "Microphone permission is required for voice input.";
                MicrophonePermissionInstructions = result.Instructions;
                MicrophonePermissionPlatform = result.Platform;
                IsSpeechToTextEnabled = false;
                StateHasChanged();
            }
            else
            {
                Logger?.LogWarning("?? STT C#: Failed to start speech recognition: {Error}", result.Error);
                IsSpeechToTextEnabled = false;
                await HandleErrorAsync(new Exception(result.Error ?? "Speech recognition not available"), "Starting speech recognition");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "?? STT C#: Exception starting speech recognition");
            IsSpeechToTextEnabled = false;
            await HandleErrorAsync(ex, "Starting speech recognition");
        }
    }

    private async Task StopListening()
    {
        if (IsRecorderMode)
        {
            // Stopping a recording means "done talking": transcribe it into the input
            await FinalizeRecordingAsync();
            return;
        }

        if (!IsListeningForSpeech) return;

        Logger?.LogInformation("?? STT C#: StopListening called");

        try
        {
            await JSRuntime.InvokeVoidAsync("aiChatInterop.stopSpeechToText");
            IsListeningForSpeech = false;
            InterimTranscript = string.Empty;
            WasPausedForTTS = false;
            Logger?.LogInformation("?? STT C#: Stopped listening");
            
            // Save the auto-listen preference (now false)
            _ = SaveAutoListenPreferenceAsync();
            
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "?? STT C#: Error stopping speech recognition");
        }
    }

    /// <summary>
    /// Pauses speech recognition temporarily (e.g., while TTS is speaking).
    /// The recognition will be resumed when ResumeSpeechRecognition is called.
    /// </summary>
    private async Task PauseSpeechRecognition()
    {
        // A recording belongs to the user; TTS never interrupts it
        if (IsRecorderMode) return;
        if (!IsListeningForSpeech) return;

        Logger?.LogInformation("?? STT C#: Pausing speech recognition");
        WasPausedForTTS = true;

        try
        {
            await JSRuntime.InvokeVoidAsync("aiChatInterop.stopSpeechToText");
            IsListeningForSpeech = false;
            InterimTranscript = string.Empty;
            StateHasChanged();
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "?? STT C#: Error pausing speech recognition");
        }
    }

    /// <summary>
    /// Resumes speech recognition after it was paused (e.g., after TTS finishes).
    /// </summary>
    private async Task ResumeSpeechRecognition()
    {
        if (IsRecorderMode) return;
        if (!IsSpeechToTextEnabled || IsListeningForSpeech) return;
        if (!WasPausedForTTS) return;

        Logger?.LogInformation("?? STT C#: Resuming speech recognition");
        WasPausedForTTS = false;

        // Add a small delay to ensure TTS audio has fully stopped
        await Task.Delay(300);

        await StartListeningAutomaticallyAsync();
    }

    private async Task RetryMicrophonePermission()
    {
        Logger?.LogInformation("?? STT C#: User retrying microphone permission");
        MicrophonePermissionDenied = false;
        MicrophonePermissionError = null;
        StateHasChanged();
        
        // Try to enable speech-to-text again
        IsSpeechToTextEnabled = true;
        await StartListening();
        StateHasChanged();
    }

    private void DismissMicrophonePermissionError()
    {
        MicrophonePermissionDenied = false;
        MicrophonePermissionError = null;
        MicrophonePermissionInstructions = null;
        StateHasChanged();
    }

    [JSInvokable]
    public void OnSpeechToTextResult(string transcript, bool isFinal)
    {
        Logger?.LogInformation("?? STT C#: Result received - isFinal={IsFinal}, transcript='{Transcript}'", isFinal, transcript);

        if (isFinal)
        {
            AppendFinalTranscript(transcript);
        }
        else
        {
            InterimTranscript = transcript;
        }
        _ = InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Appends finished speech to the input box. Shared by live recognition and recorded input.
    /// </summary>
    private void AppendFinalTranscript(string transcript)
    {
        CurrentMessage = string.IsNullOrEmpty(CurrentMessage)
            ? transcript
            : CurrentMessage + " " + transcript;
        InterimTranscript = string.Empty;
    }

    [JSInvokable]
    public void OnSpeechToTextError(string error)
    {
        // Late events from an abandoned Web Speech session must not reset a recording
        if (IsRecorderMode) return;

        Logger?.LogWarning("?? STT C#: Error callback - {Error}", error);
        IsListeningForSpeech = false;
        InterimTranscript = string.Empty;
        _ = InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public void OnSpeechToTextPermissionDenied()
    {
        Logger?.LogWarning("?? STT C#: Permission denied callback received");
        IsListeningForSpeech = false;
        IsSpeechToTextEnabled = false;
        MicrophonePermissionDenied = true;
        MicrophonePermissionError = "Microphone permission was denied. Please enable microphone access to use voice input.";
        InterimTranscript = string.Empty;
        _ = InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public void OnSpeechToTextEnded()
    {
        // Late events from an abandoned Web Speech session must not reset a recording
        if (IsRecorderMode) return;

        Logger?.LogInformation("?? STT C#: Ended callback - was listening: {WasListening}", IsListeningForSpeech);
        IsListeningForSpeech = false;
        InterimTranscript = string.Empty;
        _ = InvokeAsync(StateHasChanged);
    }

    #endregion

    #region Nested Classes for Speech-to-Text

    private class MicrophonePermissionResult
    {
        public string State { get; set; } = string.Empty;
        public bool CanRequest { get; set; }
        public bool IsGranted { get; set; }
        public bool IsDenied { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
    }

    #endregion
}
