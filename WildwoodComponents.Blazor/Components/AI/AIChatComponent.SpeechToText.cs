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
            await StartListening();
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

    private async Task StartListening()
    {
        if (IsListeningForSpeech) return;
        
        // Don't start listening if TTS is currently speaking
        if (IsSpeakingMessage)
        {
            Logger?.LogInformation("?? STT C#: TTS is speaking, will start listening when TTS finishes");
            WasPausedForTTS = true;
            return;
        }

        Logger?.LogInformation("?? STT C#: StartListening called - browser will prompt for permission if needed");

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
        if (!IsSpeechToTextEnabled || IsListeningForSpeech) return;
        if (!WasPausedForTTS) return;

        Logger?.LogInformation("?? STT C#: Resuming speech recognition");
        WasPausedForTTS = false;

        // Add a small delay to ensure TTS audio has fully stopped
        await Task.Delay(300);

        await StartListening();
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
            CurrentMessage = string.IsNullOrEmpty(CurrentMessage)
                ? transcript
                : CurrentMessage + " " + transcript;
            InterimTranscript = string.Empty;
        }
        else
        {
            InterimTranscript = transcript;
        }
        _ = InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public void OnSpeechToTextError(string error)
    {
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
