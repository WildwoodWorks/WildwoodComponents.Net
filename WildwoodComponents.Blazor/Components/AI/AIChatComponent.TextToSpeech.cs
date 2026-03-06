using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Services;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing Text-to-Speech functionality for the AI Chat component.
/// </summary>
public partial class AIChatComponent
{
    #region TTS Constants

    /// <summary>
    /// Maximum characters for the first chunk - smaller for faster initial playback.
    /// </summary>
    private const int FirstChunkMaxLength = 200;

    /// <summary>
    /// Maximum characters for subsequent chunks - larger for efficiency.
    /// </summary>
    private const int SubsequentChunkMaxLength = 800;

    #endregion

    #region Voice Helper Methods

    private VoiceApiResponse? FindDefaultVoiceInApiResponse(List<VoiceApiResponse> voices)
    {
        foreach (var voice in voices)
        {
            if (voice.IsDefault)
            {
                return voice;
            }
        }
        return null;
    }

    private TTSVoice? FindDefaultVoiceInServiceResponse(List<TTSVoice> voices)
    {
        foreach (var voice in voices)
        {
            if (voice.IsDefault)
            {
                return voice;
            }
        }
        return null;
    }

    private VoiceOption? FindVoiceById(string voiceId)
    {
        foreach (var voice in AvailableVoices)
        {
            if (voice.Id == voiceId)
            {
                return voice;
            }
        }
        return null;
    }

    private VoiceOption? FindDefaultVoice()
    {
        if (AvailableVoices.Count > 0)
        {
            return AvailableVoices[0];
        }
        return null;
    }

    #endregion

    #region Text Chunking Methods

    /// <summary>
    /// Splits text into chunks for progressive TTS playback.
    /// First chunk is smaller for faster initial speech, subsequent chunks are larger.
    /// </summary>
    private List<string> SplitTextForProgressivePlayback(string text)
    {
        var chunks = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return chunks;

        var remainingText = text.Trim();
        var isFirstChunk = true;

        while (remainingText.Length > 0)
        {
            var maxLength = isFirstChunk ? FirstChunkMaxLength : SubsequentChunkMaxLength;

            if (remainingText.Length <= maxLength)
            {
                chunks.Add(remainingText);
                break;
            }

            var breakPoint = FindBestBreakPoint(remainingText, maxLength);
            chunks.Add(remainingText.Substring(0, breakPoint).Trim());
            remainingText = remainingText.Substring(breakPoint).Trim();
            isFirstChunk = false;
        }

        return chunks;
    }

    /// <summary>
    /// Finds the best break point in text, preferring sentence boundaries.
    /// </summary>
    private int FindBestBreakPoint(string text, int maxLength)
    {
        // Sentence breakers in order of preference
        string[] sentenceBreakers = [". ", "! ", "? ", ".\n", "!\n", "?\n"];

        var bestBreak = -1;
        foreach (var breaker in sentenceBreakers)
        {
            var idx = text.LastIndexOf(breaker, maxLength, StringComparison.Ordinal);
            if (idx > bestBreak && idx > maxLength * 0.3)
            {
                bestBreak = idx + breaker.Length;
            }
        }

        if (bestBreak > 0)
            return bestBreak;

        // Fall back to word boundary
        var spaceBreak = text.LastIndexOf(' ', maxLength);
        if (spaceBreak > maxLength * 0.5)
            return spaceBreak + 1;

        // Last resort: break at max length
        return maxLength;
    }

    #endregion

    #region Text-to-Speech Methods

    private void ToggleTextToSpeech()
    {
        IsTextToSpeechEnabled = !IsTextToSpeechEnabled;
        if (!IsTextToSpeechEnabled && IsSpeakingMessage)
        {
            _ = StopSpeaking();
        }
        
        // Save the preference
        _ = SaveTTSEnabledPreferenceAsync();
        
        StateHasChanged();
    }

    private void ToggleTTSSettings()
    {
        ShowTTSSettings = !ShowTTSSettings;
        StateHasChanged();
    }

    private async Task RetryLoadVoices()
    {
        Logger?.LogInformation("?? TTS: User requested voice reload");
        await LoadAvailableVoicesAsync();
        
        // Re-evaluate if we can use server TTS now
        if (UseApiTTS && !string.IsNullOrEmpty(Settings.ApiBaseUrl))
        {
            IsUsingBrowserTTS = AvailableVoices.Count == 0;
            
            if (!IsUsingBrowserTTS)
            {
                Logger?.LogInformation("?? TTS: Voice reload successful - {VoiceCount} voices loaded", AvailableVoices.Count);
            }
            else
            {
                Logger?.LogWarning("?? TTS: Voice reload failed - still no voices available");
            }
        }
        
        StateHasChanged();
    }

    private async void OnVoiceChanged(ChangeEventArgs e)
    {
        var newVoice = e.Value?.ToString();
        if (!string.IsNullOrEmpty(newVoice) && newVoice != SelectedVoice)
        {
            SelectedVoice = newVoice;
            Logger?.LogInformation("?? TTS: Voice changed to {Voice}", SelectedVoice);
            
            // Save the user's preference
            await SaveVoicePreferenceAsync();
            
            StateHasChanged();
        }
    }

    private async void OnSpeedChanged(ChangeEventArgs e)
    {
        var valueStr = e.Value?.ToString();
        if (!string.IsNullOrEmpty(valueStr) &&
            double.TryParse(valueStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var speed))
        {
            var clampedSpeed = Math.Clamp(speed, 0.5, 2.0);
            if (Math.Abs(clampedSpeed - SelectedSpeed) > 0.01)
            {
                SelectedSpeed = clampedSpeed;
                Logger?.LogInformation("?? TTS: Speed changed to {Speed}x", SelectedSpeed);
                
                // Save the user's preference
                await SaveSpeedPreferenceAsync();
                
                StateHasChanged();
            }
        }
    }

    private async Task TestVoice()
    {
        if (IsSpeakingMessage)
        {
            await StopSpeaking();
            return;
        }

        var testText = "Hello! This is a test of the selected voice and speed settings.";
        await SpeakMessage(testText);
    }

    /// <summary>
    /// Loads available TTS voices using C# HttpClient (cross-platform).
    /// This works on Web, iOS, Android, and desktop without CORS issues.
    /// </summary>
    private async Task LoadAvailableVoicesAsync()
    {
        if (string.IsNullOrEmpty(Settings.ApiBaseUrl))
        {
            Logger?.LogWarning("?? TTS: Cannot load voices - API URL not configured");
            AvailableVoices = new List<VoiceOption>();
            return;
        }

        IsLoadingVoices = true;
        StateHasChanged();

        try
        {
            var configIdToUse = CurrentConfigurationId;
            Logger?.LogInformation("?? TTS: Loading voices for configuration: {ConfigurationId} using C# HttpClient (cross-platform)", configIdToUse ?? "none");

            // Use C# AIService for cross-platform compatibility (works on iOS, Android, Web, Desktop)
            List<TTSVoice>? voices = null;
            
            if (!string.IsNullOrEmpty(configIdToUse))
            {
                voices = await AIService.GetTTSVoicesForConfigurationAsync(configIdToUse);
            }
            else
            {
                voices = await AIService.GetTTSVoicesAsync();
            }

            if (voices != null && voices.Count > 0)
            {
                AvailableVoices.Clear();
                foreach (var voice in voices)
                {
                    AvailableVoices.Add(new VoiceOption
                    {
                        Id = voice.Id,
                        Name = voice.Name,
                        Description = voice.Description ?? voice.Gender ?? "",
                        Gender = voice.Gender,
                        Style = null, // TTSVoice doesn't have Style
                        QualityTier = null // TTSVoice doesn't have QualityTier
                    });
                }
                Logger?.LogInformation("?? TTS: Loaded {Count} voices from API for configuration {ConfigId} (provider: {Provider})",
                    AvailableVoices.Count,
                    configIdToUse ?? "none",
                    voices.Count > 0 ? voices[0].Provider : "unknown");
                
                // Log available voice IDs for debugging
                Logger?.LogInformation("?? TTS: Available voice IDs: {Voices}", 
                    string.Join(", ", AvailableVoices.Select(v => v.Id)));
                Logger?.LogInformation("?? TTS: Current SelectedVoice before applying pending: '{Voice}'", SelectedVoice);
                Logger?.LogInformation("?? TTS: Pending voice preference: '{Pending}'", _pendingVoicePreference ?? "(null)");

                // First, try to apply any pending user voice preference
                ApplyPendingVoicePreference();
                
                Logger?.LogInformation("?? TTS: Current SelectedVoice after applying pending: '{Voice}'", SelectedVoice);

                // If no user preference was applied and no voice selected, use API default
                if (string.IsNullOrEmpty(SelectedVoice))
                {
                    var defaultVoice = FindDefaultVoiceInServiceResponse(voices);
                    if (defaultVoice != null)
                    {
                        SelectedVoice = defaultVoice.Id;
                        Logger?.LogInformation("?? TTS: Using API default voice: {Voice}", SelectedVoice);
                    }
                    else if (AvailableVoices.Count > 0)
                    {
                        SelectedVoice = AvailableVoices[0].Id;
                        Logger?.LogInformation("?? TTS: Using first available voice: {Voice}", SelectedVoice);
                    }
                }
            }
            else
            {
                Logger?.LogWarning("?? TTS: No voices returned from API - TTS provider may not be configured or no voices enabled for this configuration");
                AvailableVoices = new List<VoiceOption>();
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "?? TTS: Failed to load voices from API - TTS may not be available");
            AvailableVoices = new List<VoiceOption>();
        }
        finally
        {
            IsLoadingVoices = false;
            StateHasChanged();
        }
    }

    /// <summary>
    /// Speaks the provided text using TTS with progressive chunked playback.
    /// Uses C# HttpClient for audio synthesis (no CORS) and JavaScript for audio playback.
    /// The first chunk is smaller for faster initial speech, with subsequent chunks pre-fetched.
    /// </summary>
    private async Task SpeakMessage(string text)
    {
        if (IsSpeakingMessage)
        {
            await StopSpeaking();
            return;
        }

        try
        {
            textToSpeechRef ??= DotNetObjectReference.Create(this);

            Logger?.LogInformation("?? TTS: Starting progressive TTS - Voice={Voice}, Speed={Speed}, TextLength={Length}",
                SelectedVoice, SelectedSpeed, text?.Length ?? 0);

            if (string.IsNullOrEmpty(text))
            {
                Logger?.LogWarning("?? TTS: Cannot speak - no text provided");
                return;
            }

            // Split text into chunks for progressive playback
            var chunks = SplitTextForProgressivePlayback(text);
            
            if (chunks.Count == 0)
            {
                Logger?.LogWarning("?? TTS: No chunks to speak");
                return;
            }

            Logger?.LogInformation("?? TTS: Split text into {ChunkCount} chunks for progressive playback", chunks.Count);

            // For short text (single chunk), use simple playback
            if (chunks.Count == 1)
            {
                await SpeakSingleChunk(chunks[0]);
                return;
            }

            // Progressive playback: synthesize first chunk, start playing, pre-fetch next
            await SpeakProgressiveChunks(chunks);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "?? TTS: Error speaking text");
            IsSpeakingMessage = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Speaks a single chunk of text (for short messages).
    /// </summary>
    private async Task SpeakSingleChunk(string text)
    {
        var result = await AIService.SynthesizeSpeechAsync(
            text,
            SelectedVoice ?? "alloy",
            SelectedSpeed,
            CurrentConfigurationId);

        if (result == null)
        {
            Logger?.LogWarning("?? TTS: Speech synthesis failed - no audio returned from API");
            await OnTextToSpeechError("Speech synthesis failed");
            return;
        }

        var (audioBase64, contentType) = result.Value;
        Logger?.LogInformation("?? TTS: Audio synthesized, size={Size} chars", audioBase64.Length);

        var success = await JSRuntime.InvokeAsync<bool>(
            "aiChatInterop.playAudioFromBase64",
            audioBase64,
            contentType,
            textToSpeechRef);

        if (!success)
        {
            Logger?.LogWarning("?? TTS: Audio playback failed");
        }
    }

    /// <summary>
    /// Speaks multiple chunks progressively, pre-fetching the next chunk while playing the current one.
    /// </summary>
    private async Task SpeakProgressiveChunks(List<string> chunks)
    {
        IsSpeakingMessage = true;
        await InvokeAsync(StateHasChanged);

        // Pause speech-to-text while TTS is speaking
        if (IsListeningForSpeech)
        {
            Logger?.LogInformation("?? TTS: Pausing speech recognition while TTS is speaking");
            await PauseSpeechRecognition();
        }

        try
        {
            // Synthesize the first chunk immediately for fast start
            Logger?.LogInformation("?? TTS: Synthesizing first chunk ({Length} chars) for fast start", chunks[0].Length);
            
            var currentAudio = await AIService.SynthesizeSpeechAsync(
                chunks[0],
                SelectedVoice ?? "alloy",
                SelectedSpeed,
                CurrentConfigurationId);

            if (currentAudio == null)
            {
                Logger?.LogWarning("?? TTS: Failed to synthesize first chunk");
                return;
            }

            for (int i = 0; i < chunks.Count; i++)
            {
                if (!IsSpeakingMessage)
                {
                    Logger?.LogInformation("?? TTS: Playback stopped by user");
                    break;
                }

                var isLastChunk = i == chunks.Count - 1;

                // Start pre-fetching next chunk while we play the current one
                Task<(string AudioBase64, string ContentType)?>? prefetchTask = null;
                if (!isLastChunk && IsSpeakingMessage)
                {
                    Logger?.LogInformation("?? TTS: Pre-fetching chunk {Index} ({Length} chars)", i + 2, chunks[i + 1].Length);
                    prefetchTask = AIService.SynthesizeSpeechAsync(
                        chunks[i + 1],
                        SelectedVoice ?? "alloy",
                        SelectedSpeed,
                        CurrentConfigurationId);
                }

                var (audioBase64, contentType) = currentAudio.Value;
                Logger?.LogInformation("?? TTS: Playing chunk {Index}/{Total} ({Size} chars audio)",
                    i + 1, chunks.Count, audioBase64.Length);

                // Play this chunk and wait for it to finish
                var playSuccess = await PlayAudioChunkAsync(audioBase64, contentType, isLastChunk);
                
                if (!playSuccess)
                {
                    Logger?.LogWarning("?? TTS: Chunk {Index} playback failed", i + 1);
                    break;
                }

                // Get the prefetched audio for the next iteration
                if (prefetchTask != null)
                {
                    currentAudio = await prefetchTask;
                    if (currentAudio == null)
                    {
                        Logger?.LogWarning("?? TTS: Failed to synthesize chunk {Index}", i + 2);
                        break;
                    }
                }
            }
        }
        finally
        {
            IsSpeakingMessage = false;
            
            // Resume speech-to-text if it was enabled
            if (IsSpeechToTextEnabled && !IsListeningForSpeech)
            {
                Logger?.LogInformation("?? TTS: Resuming speech recognition after TTS finished");
                await ResumeSpeechRecognition();
            }
            
            await InvokeAsync(StateHasChanged);
        }
    }

    /// <summary>
    /// Plays an audio chunk and waits for it to complete.
    /// </summary>
    private async Task<bool> PlayAudioChunkAsync(string audioBase64, string contentType, bool isLastChunk)
    {
        var tcs = new TaskCompletionSource<bool>();
        
        // Create a temporary callback ref for this chunk
        var chunkCallbackRef = DotNetObjectReference.Create(new ChunkPlaybackCallback(
            onEnded: () => tcs.TrySetResult(true),
            onError: (error) => 
            {
                Logger?.LogWarning("?? TTS: Chunk playback error: {Error}", error);
                tcs.TrySetResult(false);
            }));

        try
        {
            var success = await JSRuntime.InvokeAsync<bool>(
                "aiChatInterop.playAudioChunk",
                audioBase64,
                contentType,
                chunkCallbackRef);

            if (!success)
            {
                return false;
            }

            // Wait for playback to complete (with timeout)
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            try
            {
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                Logger?.LogWarning("?? TTS: Chunk playback timed out");
                return false;
            }
        }
        finally
        {
            chunkCallbackRef.Dispose();
        }
    }

    /// <summary>
    /// Callback class for individual chunk playback notifications.
    /// </summary>
    private class ChunkPlaybackCallback
    {
        private readonly Action _onEnded;
        private readonly Action<string> _onError;

        public ChunkPlaybackCallback(Action onEnded, Action<string> onError)
        {
            _onEnded = onEnded;
            _onError = onError;
        }

        [JSInvokable]
        public void OnChunkEnded() => _onEnded();

        [JSInvokable]
        public void OnChunkError(string error) => _onError(error);
    }

    private async Task StopSpeaking()
    {
        try
        {
            Logger?.LogInformation("?? TTS: User stopping TTS");
            
            // Stop the audio first
            await JSRuntime.InvokeVoidAsync("aiChatInterop.stopSpeaking");
            
            // Reset state immediately
            IsSpeakingMessage = false;
            
            // Force UI update immediately
            await InvokeAsync(StateHasChanged);
            
            // Resume speech-to-text if it was enabled before TTS started
            if (IsSpeechToTextEnabled && !IsListeningForSpeech)
            {
                Logger?.LogInformation("?? TTS: Resuming speech recognition after TTS stopped by user");
                WasPausedForTTS = true; // Ensure resume happens
                await ResumeSpeechRecognition();
            }
            
            Logger?.LogInformation("?? TTS: TTS stopped, UI updated");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Error stopping speech");
            // Ensure state is reset even on error
            IsSpeakingMessage = false;
            await InvokeAsync(StateHasChanged);
        }
    }

    [JSInvokable]
    public async Task OnTextToSpeechStarted()
    {
        IsSpeakingMessage = true;
        
        // Pause speech-to-text while TTS is speaking to prevent recording the AI's voice
        if (IsListeningForSpeech)
        {
            Logger?.LogInformation("?? TTS: Pausing speech recognition while TTS is speaking");
            await PauseSpeechRecognition();
        }
        
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnTextToSpeechEnded()
    {
        IsSpeakingMessage = false;
        
        // Resume speech-to-text if it was enabled before TTS started
        if (IsSpeechToTextEnabled && !IsListeningForSpeech)
        {
            Logger?.LogInformation("?? TTS: Resuming speech recognition after TTS finished");
            await ResumeSpeechRecognition();
        }
        
        await InvokeAsync(StateHasChanged);
    }

    [JSInvokable]
    public async Task OnTextToSpeechError(string error)
    {
        Logger?.LogWarning("Text-to-speech error: {Error}", error);
        IsSpeakingMessage = false;
        
        // Resume speech-to-text if it was enabled
        if (IsSpeechToTextEnabled && !IsListeningForSpeech)
        {
            Logger?.LogInformation("?? TTS: Resuming speech recognition after TTS error");
            await ResumeSpeechRecognition();
        }
        
        await InvokeAsync(StateHasChanged);
    }

    #endregion
}
