using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing the recorded voice input for the AI Chat component.
/// Browsers with a working Web Speech API (Chrome, Edge, Safari, iOS) keep live recognition;
/// everywhere else (Firefox, Brave/Opera/Vivaldi, WebView2, Safari with dictation off) the clip is
/// captured with MediaRecorder and transcribed server-side via <c>IAIService.TranscribeAudioAsync</c>.
/// </summary>
public partial class AIChatComponent
{
    #region Speech Recorder Fields

    private enum SpeechInputMode
    {
        Unknown,
        Native,
        Recorder,
        None
    }

    /// <summary>
    /// Longest clip the recorder captures before it stops itself and transcribes.
    /// </summary>
    private const int MaxRecordingSeconds = 60;

    /// <summary>
    /// Upper bound on the audio pulled from JS (the server's transcription limit).
    /// </summary>
    private const long MaxRecordingBytes = 25 * 1024 * 1024;

    private SpeechInputMode _speechInputMode = SpeechInputMode.Unknown;
    private bool IsTranscribing = false;
    private Task? _finalizeRecordingTask;

    /// <summary>
    /// Whether the current (or last) listening session was started by an explicit user action,
    /// as opposed to auto-listen on load or resume after TTS. Only explicit sessions may turn
    /// into a recording on their own — recorded input costs a server call per clip.
    /// </summary>
    private bool _listeningStartedExplicitly = false;

    private bool IsRecorderMode => _speechInputMode == SpeechInputMode.Recorder;

    /// <summary>
    /// The mic button is disabled while loading (as before) and while a clip is transcribing —
    /// but a running recording can always be stopped, so its audio is never stranded.
    /// </summary>
    private bool IsMicButtonDisabled =>
        IsTranscribing || (IsLoading && !(IsRecorderMode && IsListeningForSpeech));

    #endregion

    #region Speech Recorder Methods

    /// <summary>
    /// Detects once which voice input the browser supports. Falls back to Native (the legacy
    /// behavior) if detection itself fails, e.g. an older cached ai-chat.js.
    /// </summary>
    private async Task EnsureSpeechInputModeAsync()
    {
        if (_speechInputMode != SpeechInputMode.Unknown) return;

        try
        {
            var mode = await JSRuntime.InvokeAsync<string>("aiChatInterop.getSpeechInputMode");
            _speechInputMode = mode switch
            {
                "native" => SpeechInputMode.Native,
                "recorder" => SpeechInputMode.Recorder,
                _ => SpeechInputMode.None
            };
            Logger?.LogInformation("?? STT C#: Speech input mode detected: {Mode}", _speechInputMode);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "?? STT C#: Could not detect speech input mode; assuming Web Speech API");
            _speechInputMode = SpeechInputMode.Native;
        }
    }

    private async Task StartRecordingAsync()
    {
        // Recording while the assistant is talking would capture it — the user tapping the mic
        // means they want to talk now, so stop playback first.
        if (IsSpeakingMessage)
        {
            await StopSpeaking();
        }

        speechToTextRef ??= DotNetObjectReference.Create(this);
        var result = await JSRuntime.InvokeAsync<SpeechResult>("aiChatInterop.startRecording", speechToTextRef, MaxRecordingSeconds);

        Logger?.LogInformation("?? STT C#: startRecording returned Success={Success}, RequiresPermission={RequiresPermission}, Error={Error}",
            result.Success, result.RequiresPermission, result.Error ?? "none");

        if (result.Success)
        {
            IsListeningForSpeech = true;
            InterimTranscript = string.Empty;
            MicrophonePermissionDenied = false;
            MicrophonePermissionError = null;
            WasPausedForTTS = false;
            StateHasChanged();
        }
        else if (result.RequiresPermission)
        {
            MicrophonePermissionDenied = true;
            MicrophonePermissionError = result.Error ?? "Microphone permission is required for voice input.";
            MicrophonePermissionInstructions = result.Instructions;
            MicrophonePermissionPlatform = result.Platform;
            IsSpeechToTextEnabled = false;
            StateHasChanged();
        }
        else
        {
            IsSpeechToTextEnabled = false;
            await HandleErrorAsync(new Exception(result.Error ?? "Voice recording is not available."), "Starting voice recording");
        }
    }

    /// <summary>
    /// Stops the active recording, transcribes it, and appends the text to the input.
    /// Safe to call from several places at once (mic tap, auto-stop, send): concurrent callers
    /// share the single in-flight finalize.
    /// </summary>
    private async Task FinalizeRecordingAsync()
    {
        if (_finalizeRecordingTask != null)
        {
            await _finalizeRecordingTask;
            return;
        }

        if (!IsListeningForSpeech) return;

        _finalizeRecordingTask = FinalizeRecordingCoreAsync();
        try
        {
            await _finalizeRecordingTask;
        }
        finally
        {
            _finalizeRecordingTask = null;
        }
    }

    private async Task FinalizeRecordingCoreAsync()
    {
        try
        {
            var stopped = await JSRuntime.InvokeAsync<RecordingStopResult>("aiChatInterop.stopRecording");
            IsListeningForSpeech = false;

            if (!stopped.Success || stopped.Size <= 0)
            {
                Logger?.LogInformation("?? STT C#: Recording produced no audio: {Error}", stopped.Error ?? "empty");
                return;
            }

            IsTranscribing = true;
            StateHasChanged();

            byte[] audio;
            var streamRef = await JSRuntime.InvokeAsync<IJSStreamReference>("aiChatInterop.takeRecordingBlob");
            await using (streamRef)
            {
                // Streams in chunks, so it works over a Blazor Server circuit as well as in a WebView
                await using var stream = await streamRef.OpenReadStreamAsync(MaxRecordingBytes);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer);
                audio = buffer.ToArray();
            }

            var mimeType = string.IsNullOrEmpty(stopped.MimeType) ? "audio/webm" : stopped.MimeType;
            var configurationId = string.IsNullOrEmpty(CurrentConfigurationId) ? null : CurrentConfigurationId;
            Logger?.LogInformation("?? STT C#: Transcribing {Size} bytes of {MimeType}", audio.Length, mimeType);

            var result = await AIService.TranscribeAudioAsync(audio, mimeType, configurationId);
            if (result.Success)
            {
                var text = result.Text?.Trim();
                if (!string.IsNullOrEmpty(text))
                {
                    AppendFinalTranscript(text);
                }
            }
            else
            {
                await HandleErrorAsync(new Exception(result.ErrorMessage ?? "Transcription failed. Please try again."), "Transcribing voice input");
            }
        }
        catch (JSDisconnectedException)
        {
            // Circuit gone — nothing to render into
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "?? STT C#: Failed to finalize recording");
            await HandleErrorAsync(new Exception("Voice input could not be processed. Please try again.", ex), "Transcribing voice input");
        }
        finally
        {
            IsTranscribing = false;
            IsListeningForSpeech = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Called from JavaScript when a recording hits <see cref="MaxRecordingSeconds"/> and stopped itself.
    /// </summary>
    [JSInvokable]
    public Task OnRecordingAutoStopped()
    {
        Logger?.LogInformation("?? STT C#: Recording reached the {Seconds}s limit - transcribing", MaxRecordingSeconds);
        return InvokeAsync(FinalizeRecordingAsync);
    }

    /// <summary>
    /// Called from JavaScript when the Web Speech API exists but cannot run (network,
    /// service-not-allowed, language-not-supported). Switches this component to recorded input
    /// for the rest of its lifetime, and starts recording if the user had just tapped the mic.
    /// </summary>
    [JSInvokable]
    public Task OnSpeechRecognitionUnavailable(string code)
    {
        Logger?.LogWarning("?? STT C#: Web Speech API unavailable ({Code}) - switching to recorded voice input", code);

        return InvokeAsync(async () =>
        {
            _speechInputMode = SpeechInputMode.Recorder;
            IsListeningForSpeech = false;
            InterimTranscript = string.Empty;
            WasPausedForTTS = false;
            StateHasChanged();

            if (_listeningStartedExplicitly && IsSpeechToTextEnabled)
            {
                await StartRecordingAsync();
            }
        });
    }

    #endregion

    #region Nested Classes for Speech Recorder

    private class RecordingStopResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? MimeType { get; set; }
        public long Size { get; set; }
    }

    #endregion
}
