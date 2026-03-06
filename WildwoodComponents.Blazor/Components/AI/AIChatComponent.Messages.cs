using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Components.Base;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing Message handling functionality for the AI Chat component.
/// </summary>
public partial class AIChatComponent
{
    #region Message Methods

    private async Task SendMessage()
    {
        if (!CanSendMessage) return;

        var messageText = CurrentMessage.Trim();
        CurrentMessage = string.Empty;

        // Immediately update UI to clear the input field
        StateHasChanged();

        // Also clear via JS to ensure the DOM textarea value is cleared
        try
        {
            await JSRuntime.InvokeVoidAsync("clearTextareaValue", messageInput);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to clear textarea via JS");
        }

        // Stop listening if speech-to-text is active
        if (IsListeningForSpeech)
        {
            await StopListening();
        }

        // Track if this is the first message in the session (for auto-naming)
        // Sessions are created with default names like "Chat 2024-01-15 10:30", so we check for that pattern
        var isFirstMessageInSession = CurrentSession == null ||
            (CurrentSession != null && IsDefaultSessionName(CurrentSession.Name) && Messages.Count == 0);

        await SetLoadingAsync(true);
        StateHasChanged();

        try
        {
            Logger?.LogInformation("?? AIChatComponent: Starting SendMessage for text: {MessageText}",
                messageText.Substring(0, Math.Min(50, messageText.Length)));

            var userMessage = new AIMessage
            {
                Id = Guid.NewGuid().ToString(),
                Role = "user",
                Content = messageText,
                Timestamp = DateTime.UtcNow
            };

            Messages.Add(userMessage);
            StateHasChanged();
            Logger?.LogInformation("? AIChatComponent: User message added to UI - Messages count: {Count}", Messages.Count);

            await OnMessageSent.InvokeAsync(userMessage);
            await ScrollToBottom();

            TypingIndicator.IsVisible = true;
            StateHasChanged();
            Logger?.LogInformation("? AIChatComponent: Typing indicator shown");

            var request = new AIChatRequest
            {
                ConfigurationId = CurrentConfigurationId,
                SessionId = CurrentSession?.Id,
                Message = messageText,
                SaveToSession = Settings.EnableSessions,
                MacroValues = new Dictionary<string, string>()
            };

            Logger?.LogInformation("?? AIChatComponent: Sending request - ConfigId: {ConfigId}, SessionId: {SessionId}, SaveToSession: {SaveToSession}",
                CurrentConfigurationId, CurrentSession?.Id ?? "null", Settings.EnableSessions);

            var response = await AIService.SendMessageAsync(request);

            Logger?.LogInformation("?? AIChatComponent: Received response - IsError: {IsError}, Response length: {ResponseLength}, ResponseId: {ResponseId}",
                response.IsError, response.Response?.Length ?? 0, response.Id);

            if (response.IsError)
            {
                Logger?.LogError("? AIChatComponent: AI response contains error: {ErrorMessage}", response.ErrorMessage);

                Messages.Remove(userMessage);
                var errorArgs = new ComponentErrorEventArgs
                {
                    Exception = new Exception(response.ErrorMessage ?? "An error occurred"),
                    Context = "Sending AI message"
                };
                await OnError.InvokeAsync(errorArgs);
                throw errorArgs.Exception;
            }
            else
            {
                Logger?.LogInformation("? AIChatComponent: Processing successful AI response");

                var aiMessage = new AIMessage
                {
                    Id = response.Id,
                    Role = "assistant",
                    Content = response.Response ?? string.Empty,
                    Timestamp = response.CreatedAt,
                    TokenCount = response.TokensUsed
                };

                Logger?.LogInformation("?? AIChatComponent: Created AI message object - Content preview: {ContentPreview}",
                    aiMessage.Content?.Substring(0, Math.Min(100, aiMessage.Content.Length)) ?? "null");

                Messages.Add(aiMessage);
                Logger?.LogInformation("?? AIChatComponent: AI message added to collection - Total messages: {Count}", Messages.Count);

                await OnMessageReceived.InvokeAsync(aiMessage);
                Logger?.LogInformation("?? AIChatComponent: OnMessageReceived event fired");

                if (IsTextToSpeechEnabled && !string.IsNullOrEmpty(aiMessage.Content))
                {
                    await SpeakMessage(aiMessage.Content);
                }

                // Handle session creation and auto-naming
                if (!string.IsNullOrEmpty(response.SessionId))
                {
                    // Case 1: New session was created by the API
                    if (CurrentSession == null || CurrentSession.Id != response.SessionId)
                    {
                        Logger?.LogInformation("?? AIChatComponent: New session created by API: {SessionId}", response.SessionId);
                        CurrentSessionId = response.SessionId;
                        CurrentSession = await AIService.GetSessionAsync(response.SessionId);
                        
                        // Refresh the sessions list to show the new session in the sidebar
                        await LoadSessions();
                    }
                    
                    // Auto-name the session if this was the first message and session has no name
                    if (CurrentSession != null && isFirstMessageInSession)
                    {
                        await AutoNameSessionAsync(messageText);
                    }
                }
                // Case 2: We have an existing session that needs naming (created via New Chat button)
                else if (CurrentSession != null && isFirstMessageInSession)
                {
                    await AutoNameSessionAsync(messageText);
                }
            }

            TypingIndicator.IsVisible = false;
            StateHasChanged();
            Logger?.LogInformation("?? AIChatComponent: Final state update - Messages count: {MessageCount}, Typing indicator hidden", Messages.Count);
            
            // Small delay to ensure DOM is updated before scrolling
            await Task.Delay(50);
            await ScrollToLastMessage(); // Scroll to top of the AI response, not bottom
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "?? AIChatComponent: Exception in SendMessage: {Message}", ex.Message);

            TypingIndicator.IsVisible = false;

            // Check if this is an authentication failure - notify parent to redirect
            if (IsAuthenticationError(ex))
            {
                Logger?.LogWarning("Authentication failure during SendMessage - notifying parent");
                await OnAuthenticationFailed.InvokeAsync();
                return;
            }

            await HandleErrorAsync(ex, "Sending AI message");
            StateHasChanged();
        }
        finally
        {
            await SetLoadingAsync(false);
            Logger?.LogInformation("?? AIChatComponent: SendMessage completed, loading state cleared");
        }
    }

    /// <summary>
    /// Auto-names the current session based on the user's first message.
    /// </summary>
    private async Task AutoNameSessionAsync(string messageText)
    {
        if (CurrentSession == null) return;
        
        try
        {
            var autoSessionName = GenerateSessionNameFromMessage(messageText);
            var renamed = await AIService.RenameSessionAsync(CurrentSession.Id, autoSessionName);
            if (renamed)
            {
                CurrentSession.Name = autoSessionName;
                Logger?.LogInformation("?? AIChatComponent: Auto-named session to '{SessionName}'", autoSessionName);
                
                await OnSessionCreated.InvokeAsync(CurrentSession);
                await LoadSessions();
                StateHasChanged();
            }
            else
            {
                Logger?.LogWarning("?? AIChatComponent: Failed to auto-name session");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "?? AIChatComponent: Error auto-naming session");
        }
    }

    #endregion

    #region Input Event Handlers

    private async Task OnInputChanged(ChangeEventArgs e)
    {
        CurrentMessage = e.Value?.ToString() ?? string.Empty;
        StateHasChanged();

        try
        {
            await JSRuntime.InvokeVoidAsync("autoResizeTextarea", messageInput);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to auto-resize textarea");
        }
    }

    private async Task ScrollToBottom()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("scrollToBottom", messagesContainer);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to scroll to bottom");
        }
    }

    /// <summary>
    /// Scrolls to show the top of the last message in the chat container.
    /// Used after receiving an AI response so the user sees the beginning of the response.
    /// </summary>
    private async Task ScrollToLastMessage()
    {
        try
        {
            await JSRuntime.InvokeVoidAsync("scrollToLastMessage", messagesContainer);
        }
        catch (Exception ex)
        {
            Logger?.LogWarning(ex, "Failed to scroll to last message");
        }
    }

    #endregion
}
