using System.Linq;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing Configuration management functionality for the AI Chat component.
/// </summary>
public partial class AIChatComponent
{
    #region User Preferences Storage

    /// <summary>
    /// Model for storing TTS user preferences
    /// </summary>
    private class TTSUserPreferences
    {
        public string? VoiceId { get; set; }
        public double Speed { get; set; } = 1.0;
        public bool IsEnabled { get; set; } = true;
    }

    /// <summary>
    /// Loads user's saved TTS and STT preferences from storage
    /// </summary>
    private async Task LoadUserTTSPreferencesAsync()
    {
        try
        {
            Logger?.LogInformation("?? TTS: LoadUserTTSPreferencesAsync starting...");
            
            // Load voice preference - store it for later application after voices load
            var savedVoice = await LocalStorage.GetItemAsync<string>(VoicePreferenceKey);
            Logger?.LogInformation("?? TTS: Retrieved voice from storage: '{Voice}'", savedVoice ?? "(null)");
            
            if (!string.IsNullOrEmpty(savedVoice))
            {
                _pendingVoicePreference = savedVoice;
                Logger?.LogInformation("?? TTS: Set pending voice preference: {Voice}", savedVoice);
                
                // Try to apply immediately if voices are already loaded
                Logger?.LogInformation("?? TTS: AvailableVoices.Count = {Count}", AvailableVoices.Count);
                if (AvailableVoices.Count > 0)
                {
                    ApplyPendingVoicePreference();
                }
            }

            // Load speed preference
            var savedSpeedStr = await LocalStorage.GetItemAsync<string>(SpeedPreferenceKey);
            Logger?.LogInformation("?? TTS: Retrieved speed from storage: '{Speed}'", savedSpeedStr ?? "(null)");
            
            if (!string.IsNullOrEmpty(savedSpeedStr) && 
                double.TryParse(savedSpeedStr, System.Globalization.NumberStyles.Any, 
                    System.Globalization.CultureInfo.InvariantCulture, out var savedSpeed))
            {
                SelectedSpeed = Math.Clamp(savedSpeed, 0.5, 2.0);
                Logger?.LogInformation("?? TTS: Applied saved speed preference: {Speed}x", SelectedSpeed);
            }

            // Load TTS enabled preference
            var savedTTSEnabledStr = await LocalStorage.GetItemAsync<string>(TTSEnabledPreferenceKey);
            Logger?.LogInformation("?? TTS: Retrieved TTS enabled from storage: '{Enabled}'", savedTTSEnabledStr ?? "(null)");
            
            if (!string.IsNullOrEmpty(savedTTSEnabledStr) && bool.TryParse(savedTTSEnabledStr, out var savedTTSEnabled))
            {
                IsTextToSpeechEnabled = savedTTSEnabled;
                Logger?.LogInformation("?? TTS: Applied saved TTS enabled preference: {Enabled}", IsTextToSpeechEnabled);
            }

            // Load STT (microphone) enabled preference
            var savedSTTEnabledStr = await LocalStorage.GetItemAsync<string>(STTEnabledPreferenceKey);
            Logger?.LogInformation("?? STT: Retrieved STT enabled from storage: '{Enabled}'", savedSTTEnabledStr ?? "(null)");
            
            if (!string.IsNullOrEmpty(savedSTTEnabledStr) && bool.TryParse(savedSTTEnabledStr, out var savedSTTEnabled))
            {
                IsSpeechToTextEnabled = savedSTTEnabled;
                Logger?.LogInformation("?? STT: Applied saved microphone enabled preference: {Enabled}", IsSpeechToTextEnabled);
            }

            // Load auto-listen (active listening) preference - separate from STT enabled
            var savedAutoListenStr = await LocalStorage.GetItemAsync<string>(AutoListenPreferenceKey);
            Logger?.LogInformation("?? STT: Retrieved auto-listen from storage: '{AutoListen}'", savedAutoListenStr ?? "(null)");
            
            if (!string.IsNullOrEmpty(savedAutoListenStr) && bool.TryParse(savedAutoListenStr, out var savedAutoListen))
            {
                _autoListenOnLoad = savedAutoListen;
                Logger?.LogInformation("?? STT: Auto-listen preference: {AutoListen}", _autoListenOnLoad);
            }
            
            Logger?.LogInformation("?? TTS: LoadUserTTSPreferencesAsync completed");
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "?? TTS: Error loading user preferences");
        }
    }

    /// <summary>
    /// Applies the pending voice preference if the voice is now available
    /// </summary>
    private void ApplyPendingVoicePreference()
    {
        Logger?.LogInformation("?? TTS: ApplyPendingVoicePreference called - pending='{Pending}', voiceCount={Count}", 
            _pendingVoicePreference ?? "(null)", AvailableVoices.Count);
        
        if (string.IsNullOrEmpty(_pendingVoicePreference))
        {
            Logger?.LogInformation("?? TTS: No pending voice preference to apply");
            return;
        }
        
        if (AvailableVoices.Count == 0)
        {
            Logger?.LogInformation("?? TTS: No voices available yet, keeping preference pending");
            return;
        }

        var voiceExists = FindVoiceById(_pendingVoicePreference) != null;
        Logger?.LogInformation("?? TTS: Voice '{Voice}' exists in available voices: {Exists}", 
            _pendingVoicePreference, voiceExists);
        
        if (voiceExists)
        {
            SelectedVoice = _pendingVoicePreference;
            Logger?.LogInformation("?? TTS: ? Applied saved voice preference: {Voice}", SelectedVoice);
            _pendingVoicePreference = null; // Clear after applying
        }
        else
        {
            Logger?.LogWarning("?? TTS: Saved voice {Voice} not available. Available voices: {Voices}", 
                _pendingVoicePreference,
                string.Join(", ", AvailableVoices.Select(v => v.Id)));
        }
    }

    /// <summary>
    /// Saves the user's current voice selection to storage
    /// </summary>
    private async Task SaveVoicePreferenceAsync()
    {
        try
        {
            if (!string.IsNullOrEmpty(SelectedVoice))
            {
                await LocalStorage.SetItemAsync(VoicePreferenceKey, SelectedVoice);
                Logger?.LogDebug("?? TTS: Saved voice preference: {Voice}", SelectedVoice);
            }
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "?? TTS: Could not save voice preference");
        }
    }

    /// <summary>
    /// Saves the user's current speed selection to storage
    /// </summary>
    private async Task SaveSpeedPreferenceAsync()
    {
        try
        {
            await LocalStorage.SetItemAsync(SpeedPreferenceKey, 
                SelectedSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Logger?.LogDebug("?? TTS: Saved speed preference: {Speed}x", SelectedSpeed);
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "?? TTS: Could not save speed preference");
        }
    }

    /// <summary>
    /// Saves the user's TTS enabled state to storage
    /// </summary>
    private async Task SaveTTSEnabledPreferenceAsync()
    {
        try
        {
            await LocalStorage.SetItemAsync(TTSEnabledPreferenceKey, IsTextToSpeechEnabled.ToString());
            Logger?.LogDebug("?? TTS: Saved enabled preference: {Enabled}", IsTextToSpeechEnabled);
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "?? TTS: Could not save enabled preference");
        }
    }

    /// <summary>
    /// Saves the user's STT (microphone) enabled state to storage
    /// </summary>
    private async Task SaveSTTEnabledPreferenceAsync()
    {
        try
        {
            await LocalStorage.SetItemAsync(STTEnabledPreferenceKey, IsSpeechToTextEnabled.ToString());
            Logger?.LogDebug("?? STT: Saved microphone enabled preference: {Enabled}", IsSpeechToTextEnabled);
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "?? STT: Could not save microphone enabled preference");
        }
    }

    /// <summary>
    /// Saves the user's auto-listen (active listening) state to storage
    /// </summary>
    private async Task SaveAutoListenPreferenceAsync()
    {
        try
        {
            await LocalStorage.SetItemAsync(AutoListenPreferenceKey, IsListeningForSpeech.ToString());
            Logger?.LogDebug("?? STT: Saved auto-listen preference: {Listening}", IsListeningForSpeech);
        }
        catch (Exception ex)
        {
            Logger?.LogDebug(ex, "?? STT: Could not save auto-listen preference");
        }
    }

    #endregion

    #region Initialization Helpers

    private async Task InitializeVoiceSettingsAsync()
    {
        // First, try to load user's saved preferences
        await LoadUserTTSPreferencesAsync();

        // If no saved preference, fall back to configuration defaults
        if (string.IsNullOrEmpty(SelectedVoice))
        {
            if (CurrentConfiguration != null && CurrentConfiguration.EnableTTS && !string.IsNullOrEmpty(CurrentConfiguration.TTSDefaultVoice))
            {
                SelectedVoice = CurrentConfiguration.TTSDefaultVoice;
                Logger?.LogInformation("?? TTS: Using configuration default voice: {Voice}", SelectedVoice);
            }
            else if (!string.IsNullOrEmpty(Settings.TTSVoice) && AvailableVoices.Count > 0)
            {
                var configuredVoice = FindVoiceById(Settings.TTSVoice);
                if (configuredVoice != null)
                {
                    SelectedVoice = Settings.TTSVoice;
                }
                else if (AvailableVoices.Count > 0)
                {
                    SelectedVoice = AvailableVoices[0].Id;
                }
            }
            else if (AvailableVoices.Count > 0)
            {
                var defaultVoice = FindDefaultVoice();
                SelectedVoice = defaultVoice?.Id ?? AvailableVoices[0].Id;
            }
        }
    }

    // Keep sync version for backward compatibility
    private void InitializeVoiceSettings()
    {
        // Sync initialization - preferences will be loaded async after render
        if (CurrentConfiguration != null && CurrentConfiguration.EnableTTS && !string.IsNullOrEmpty(CurrentConfiguration.TTSDefaultVoice))
        {
            SelectedVoice = CurrentConfiguration.TTSDefaultVoice;
            Logger?.LogInformation("?? TTS: Using configuration default voice: {Voice}", SelectedVoice);
        }
        else if (!string.IsNullOrEmpty(Settings.TTSVoice) && AvailableVoices.Count > 0)
        {
            var configuredVoice = FindVoiceById(Settings.TTSVoice);
            if (configuredVoice != null)
            {
                SelectedVoice = Settings.TTSVoice;
            }
            else if (AvailableVoices.Count > 0)
            {
                SelectedVoice = AvailableVoices[0].Id;
            }
        }
        else if (AvailableVoices.Count > 0)
        {
            var defaultVoice = FindDefaultVoice();
            SelectedVoice = defaultVoice?.Id ?? AvailableVoices[0].Id;
        }
    }

    private void InitializeSpeedSettings()
    {
        // Only set from config if no user preference was loaded
        if (SelectedSpeed == 1.0) // Default value indicates no preference loaded
        {
            if (CurrentConfiguration != null && CurrentConfiguration.EnableTTS && CurrentConfiguration.TTSDefaultSpeed > 0)
            {
                SelectedSpeed = CurrentConfiguration.TTSDefaultSpeed;
                Logger?.LogInformation("?? TTS: Using configuration default speed: {Speed}x", SelectedSpeed);
            }
            else
            {
                SelectedSpeed = Settings.TTSSpeed > 0 ? Settings.TTSSpeed : 1.0;
            }
        }
    }

    #endregion

    #region Configuration Methods

    private async Task LoadConfigurations()
    {
        try
        {
            var chatConfigs = await AIService.GetConfigurationsAsync("chat");
            var ttsChatConfigs = await AIService.GetConfigurationsAsync("ttschat");
            Configurations = chatConfigs.Concat(ttsChatConfigs).ToList();

            foreach (var config in Configurations)
            {
                Logger?.LogInformation("?? Configuration loaded: {Name} (ID: {Id}) - Type={Type}, EnableTTS={EnableTTS}, TTSDefaultVoice={Voice}, TTSDefaultSpeed={Speed}",
                    config.Name, config.Id, config.ConfigurationType, config.EnableTTS, config.TTSDefaultVoice ?? "null", config.TTSDefaultSpeed);
            }
        }
        catch (Exception ex) when (IsAuthenticationError(ex))
        {
            // Let auth errors bubble up so the parent can redirect to login
            Logger?.LogWarning(ex, "Authentication failure while loading configurations");
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error loading AI configurations");
            await HandleErrorAsync(ex, "Failed to load AI configurations");
        }
    }

    private async Task LoadConfiguration(string configurationId)
    {
        await ExecuteAsync(async () =>
        {
            try
            {
                CurrentConfiguration = await AIService.GetConfigurationAsync(configurationId);
                if (CurrentConfiguration == null)
                {
                    Logger?.LogWarning("Configuration {ConfigurationId} not found, will use first available configuration", configurationId);

                    if (HasAnyConfigurations())
                    {
                        var firstConfig = GetFirstConfiguration();
                        if (firstConfig != null)
                        {
                            CurrentConfiguration = firstConfig;
                            CurrentConfigurationId = CurrentConfiguration.Id;
                            Logger?.LogInformation("Using fallback configuration: {ConfigurationName} ({ConfigurationId})",
                                CurrentConfiguration.Name, CurrentConfiguration.Id);
                        }
                    }
                    else
                    {
                        Logger?.LogError("No AI configurations available");
                        return;
                    }
                }

                ApplyConfigurationTTSSettings();
            }
            catch (Exception ex)
            {
                Logger?.LogWarning(ex, "Failed to load configuration {ConfigurationId}, trying fallback", configurationId);

                if (HasAnyConfigurations())
                {
                    var firstConfig = GetFirstConfiguration();
                    if (firstConfig != null)
                    {
                        CurrentConfiguration = firstConfig;
                        CurrentConfigurationId = CurrentConfiguration.Id;
                        Logger?.LogInformation("Using fallback configuration: {ConfigurationName} ({ConfigurationId})",
                            CurrentConfiguration.Name, CurrentConfiguration.Id);

                        ApplyConfigurationTTSSettings();
                    }
                }
                else
                {
                    Logger?.LogError("No AI configurations available for fallback");
                    throw new Exception("No AI configurations available");
                }
            }
        }, "Loading configuration");
    }

    private void ApplyConfigurationTTSSettings()
    {
        if (CurrentConfiguration == null)
        {
            Logger?.LogWarning("?? TTS: Cannot apply settings - CurrentConfiguration is null");
            return;
        }

        Logger?.LogInformation("?? TTS: ApplyConfigurationTTSSettings called for config: {Name} (ID: {Id})",
            CurrentConfiguration.Name, CurrentConfiguration.Id);
        Logger?.LogInformation("?? TTS: Raw configuration values - EnableTTS={EnableTTS}, Voice={Voice}, Speed={Speed}, TTSModel={TTSModel}",
            CurrentConfiguration.EnableTTS,
            CurrentConfiguration.TTSDefaultVoice ?? "null",
            CurrentConfiguration.TTSDefaultSpeed,
            CurrentConfiguration.TTSModel ?? "null");

        if (CurrentConfiguration.EnableTTS)
        {
            Logger?.LogInformation("?? TTS: EnableTTS is TRUE - Applying TTS settings from configuration");

            Settings.EnableTextToSpeech = true;
            Settings.UseServerTTS = true;

            UseApiTTS = true;
            IsUsingBrowserTTS = false;

            // Only set voice from config if there's no pending user preference
            if (string.IsNullOrEmpty(_pendingVoicePreference) && !string.IsNullOrEmpty(CurrentConfiguration.TTSDefaultVoice))
            {
                Settings.TTSVoice = CurrentConfiguration.TTSDefaultVoice;
                SelectedVoice = CurrentConfiguration.TTSDefaultVoice;
                Logger?.LogInformation("?? TTS: Voice set to config default: {Voice}", SelectedVoice);
            }
            else if (!string.IsNullOrEmpty(_pendingVoicePreference))
            {
                Logger?.LogInformation("?? TTS: Skipping config voice - user preference pending: {Voice}", _pendingVoicePreference);
            }

            if (CurrentConfiguration.TTSDefaultSpeed > 0)
            {
                Settings.TTSSpeed = CurrentConfiguration.TTSDefaultSpeed;
                // Only set speed if it hasn't been loaded from user preferences (still at default)
                if (Math.Abs(SelectedSpeed - 1.0) < 0.01)
                {
                    SelectedSpeed = CurrentConfiguration.TTSDefaultSpeed;
                    Logger?.LogInformation("?? TTS: Speed set to config default: {Speed}x", SelectedSpeed);
                }
            }

            Logger?.LogInformation("?? TTS: Settings applied - EnableTextToSpeech={EnableTTS}, UseServerTTS={UseServerTTS}, UseApiTTS={UseApiTTS}, IsUsingBrowserTTS={IsUsingBrowserTTS}",
                Settings.EnableTextToSpeech, Settings.UseServerTTS, UseApiTTS, IsUsingBrowserTTS);
        }
        else
        {
            Logger?.LogWarning("?? TTS: EnableTTS is FALSE in configuration - TTS will use component Settings defaults (may use browser fallback)");
        }
    }

    private async Task OnConfigurationChanged(ChangeEventArgs e)
    {
        var newConfigId = e.Value?.ToString();
        if (!string.IsNullOrEmpty(newConfigId) && newConfigId != CurrentConfigurationId)
        {
            var selectedConfig = FindConfigurationById(newConfigId);
            if (selectedConfig != null)
            {
                CurrentConfigurationId = newConfigId;
                CurrentConfiguration = selectedConfig;

                ApplyConfigurationTTSSettings();

                CurrentSession = null;
                CurrentSessionId = string.Empty;
                Messages.Clear();

                if (Settings.EnableSessions)
                {
                    await LoadSessions();
                }

                if (Settings.EnableTextToSpeech)
                {
                    await LoadAvailableVoicesAsync();
                }

                StateHasChanged();
            }
            else
            {
                Logger?.LogWarning("Selected configuration {ConfigurationId} not found in available configurations", newConfigId);
            }
        }
    }

    #endregion
}
