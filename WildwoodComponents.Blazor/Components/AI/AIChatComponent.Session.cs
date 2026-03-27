using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing Session management functionality for the AI Chat component.
/// </summary>
public partial class AIChatComponent
{
    #region Sidebar Methods

    private void ToggleSidebar()
    {
        IsSidebarOpen = !IsSidebarOpen;
        StateHasChanged();
    }

    private async Task LoadSessionFromMenu(string sessionId)
    {
        Logger?.LogInformation("?? LoadSessionFromMenu called for session: {SessionId}", sessionId);
        
        if (sessionId != CurrentSessionId)
        {
            await LoadSession(sessionId);
            
            // Auto-close sidebar on mobile after selecting a session
            if (IsSidebarOpen)
            {
                IsSidebarOpen = false;
            }
            
            StateHasChanged();
            Logger?.LogInformation("? LoadSessionFromMenu completed for session: {SessionId}", sessionId);
        }
        else
        {
            Logger?.LogInformation("?? Session {SessionId} is already the current session", sessionId);
        }
    }

    #endregion

    #region Session Details Popup Methods (Touch Device Support)

    /// <summary>
    /// Shows the session details popup for touch devices (called on long press).
    /// </summary>
    private void ShowSessionDetails(AISessionSummary session)
    {
        SelectedSessionForDetails = session;
        ShowSessionDetailsPopup = true;
        StateHasChanged();
    }

    /// <summary>
    /// Hides the session details popup.
    /// </summary>
    private void HideSessionDetails()
    {
        ShowSessionDetailsPopup = false;
        SelectedSessionForDetails = null;
        StateHasChanged();
    }

    /// <summary>
    /// Loads a session from the details popup and closes the popup.
    /// </summary>
    private async Task LoadSessionFromDetails()
    {
        Logger?.LogInformation("?? LoadSessionFromDetails called");
        
        if (SelectedSessionForDetails == null)
        {
            Logger?.LogWarning("?? LoadSessionFromDetails: No session selected");
            return;
        }
        
        try
        {
            Logger?.LogInformation("?? Loading session from details: {SessionId}", SelectedSessionForDetails.Id);
            var sessionId = SelectedSessionForDetails.Id;
            
            // Hide the popup first
            HideSessionDetails();
            
            // Then load the session
            await LoadSessionFromMenu(sessionId);
            
            Logger?.LogInformation("? Session loaded successfully from details popup");
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "? Error loading session from details popup");
            HideSessionDetails();
        }
    }

    #endregion

    #region Session Methods

    /// <summary>
    /// Creates an initial session silently during component initialization.
    /// Unlike CreateNewSession, this doesn't show loading states or trigger UI feedback.
    /// </summary>
    private async Task CreateInitialSessionAsync()
    {
        if (string.IsNullOrEmpty(CurrentConfigurationId))
        {
            Logger?.LogWarning("Cannot create initial session: No current configuration ID");
            return;
        }

        try
        {
            var session = await AIService.CreateSessionAsync(CurrentConfigurationId, null);
            if (session != null)
            {
                CurrentSession = session;
                CurrentSessionId = session.Id;
                Messages.Clear();
                await OnSessionCreated.InvokeAsync(session);
                await LoadSessions(); // Refresh the sessions list
                Logger?.LogInformation("Auto-created initial session: {SessionName} ({SessionId})", session.Name, session.Id);
            }
            else
            {
                Logger?.LogWarning("Failed to auto-create initial session");
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error auto-creating initial session for configuration {ConfigurationId}", CurrentConfigurationId);
            // Don't throw - allow component to continue without a session
        }
    }

    private async Task LoadSessions()
    {
        if (string.IsNullOrEmpty(CurrentConfigurationId))
        {
            Logger?.LogWarning("Cannot load sessions: No current configuration ID");
            return;
        }

        try
        {
            Sessions = await AIService.GetSessionsAsync(CurrentConfigurationId);
            Logger?.LogInformation("Loaded {Count} sessions for configuration {ConfigurationId}", Sessions.Count, CurrentConfigurationId);
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Error loading AI sessions for configuration {ConfigurationId}", CurrentConfigurationId);
            Sessions = new List<AISessionSummary>();
        }
    }

    private async Task OnSessionChanged(ChangeEventArgs e)
    {
        var sessionId = e.Value?.ToString();
        if (sessionId != CurrentSessionId)
        {
            CurrentSessionId = sessionId ?? string.Empty;

            if (!string.IsNullOrEmpty(sessionId))
            {
                await LoadSession(sessionId);
            }
            else
            {
                CurrentSession = null;
                Messages.Clear();
            }

            StateHasChanged();
        }
    }

    private async Task LoadSession(string sessionId)
    {
        Logger?.LogInformation("?? LoadSession called for session: {SessionId}", sessionId);
        
        try
        {
            CurrentSession = await AIService.GetSessionAsync(sessionId);
            
            if (CurrentSession != null)
            {
                CurrentSessionId = sessionId;
                Logger?.LogInformation("?? Session fetched: {SessionName}, Messages in response: {MessageCount}", 
                    CurrentSession.Name, CurrentSession.Messages?.Count ?? 0);
                
                // Log first and last message if available for debugging
                if (CurrentSession.Messages != null && CurrentSession.Messages.Count > 0)
                {
                    var firstMsg = CurrentSession.Messages[0];
                    Logger?.LogInformation("?? First message: Role={Role}, Content={ContentPreview}", 
                        firstMsg.Role, 
                        firstMsg.Content.Length > 100 ? firstMsg.Content.Substring(0, 100) + "..." : firstMsg.Content);
                }
                
                Messages = ConvertToMessageList(CurrentSession.Messages);
                Logger?.LogInformation("?? Messages list now has {MessageCount} items", Messages.Count);

                if (Messages.Count > Settings.MaxHistorySize)
                {
                    Messages = TakeLastMessages(Messages, Settings.MaxHistorySize);
                    Logger?.LogInformation("Truncated session messages to {MaxHistorySize} most recent messages", Settings.MaxHistorySize);
                }

                Logger?.LogInformation("? Loaded session {SessionId} with {MessageCount} visible messages", sessionId, Messages.Count);
                
                // Force UI update
                StateHasChanged();
                
                // Scroll to bottom after loading messages
                await ScrollToBottom();
            }
            else
            {
                Logger?.LogWarning("?? Session {SessionId} not found or inaccessible - CurrentSession is null", sessionId);
                CurrentSessionId = string.Empty;
                Messages.Clear();
                StateHasChanged();
            }
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "? Error loading session {SessionId}", sessionId);
            await HandleErrorAsync(ex, "Failed to load session");
            CurrentSessionId = string.Empty;
            Messages.Clear();
            StateHasChanged();
        }
    }

    private async Task CreateNewSession()
    {
        if (string.IsNullOrEmpty(CurrentConfigurationId))
        {
            Logger?.LogWarning("Cannot create session: No current configuration ID");
            return;
        }

        await ExecuteAsync(async () =>
        {
            var session = await AIService.CreateSessionAsync(CurrentConfigurationId, null);
            if (session != null)
            {
                CurrentSession = session;
                CurrentSessionId = session.Id;
                Messages.Clear();
                await OnSessionCreated.InvokeAsync(session);
                await LoadSessions();
                StateHasChanged();
                Logger?.LogInformation("Successfully created new session: {SessionName} ({SessionId})", session.Name, session.Id);
            }
            else
            {
                throw new Exception("Failed to create new session");
            }
        }, "Creating new session");
    }

    private async Task DeleteCurrentSession()
    {
        if (CurrentSession == null) return;

        var confirmed = await JSRuntime.InvokeAsync<bool>("confirm", $"Are you sure you want to delete the session '{CurrentSession.Name}'? This action cannot be undone.");
        if (!confirmed) return;

        await ExecuteAsync(async () =>
        {
            var success = await AIService.DeleteSessionAsync(CurrentSession.Id);
            if (success)
            {
                await OnSessionEnded.InvokeAsync(CurrentSession);
                CurrentSession = null;
                CurrentSessionId = string.Empty;
                Messages.Clear();
                await LoadSessions();
                StateHasChanged();
                Logger?.LogInformation("Successfully deleted session");
            }
            else
            {
                throw new Exception("Failed to delete session");
            }
        }, "Deleting session");
    }

    private async Task RenameCurrentSession()
    {
        if (CurrentSession == null) return;

        var newName = await JSRuntime.InvokeAsync<string>("prompt", "Enter new session name:", CurrentSession.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == CurrentSession.Name) return;

        await ExecuteAsync(async () =>
        {
            var success = await AIService.RenameSessionAsync(CurrentSession.Id, newName.Trim());
            if (success)
            {
                var oldName = CurrentSession.Name;
                CurrentSession.Name = newName.Trim();
                await LoadSessions();
                StateHasChanged();
                Logger?.LogInformation("Successfully renamed session from '{OldName}' to '{NewName}'", oldName, newName);
            }
            else
            {
                throw new Exception("Failed to rename session");
            }
        }, "Renaming session");
    }

    private async Task EndCurrentSession()
    {
        if (CurrentSession == null) return;

        await ExecuteAsync(async () =>
        {
            var success = await AIService.EndSessionAsync(CurrentSession.Id);
            if (success)
            {
                await OnSessionEnded.InvokeAsync(CurrentSession);
                CurrentSession = null;
                CurrentSessionId = string.Empty;
                Messages.Clear();
                await LoadSessions();
                StateHasChanged();
            }
            else
            {
                throw new Exception("Failed to end session");
            }
        }, "Ending session");
    }

    #endregion
}
