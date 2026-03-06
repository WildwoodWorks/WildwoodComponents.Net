using WildwoodComponents.Blazor.Models;

namespace WildwoodComponents.Blazor.Components.AI;

/// <summary>
/// Partial class containing UI and Collection helper methods for the AI Chat component.
/// </summary>
public partial class AIChatComponent
{
    #region UI Helper Methods

    private void ToggleSpeechMenu()
    {
        ShowSpeechMenu = !ShowSpeechMenu;
        StateHasChanged();
    }

    private void CloseSpeechMenu()
    {
        ShowSpeechMenu = false;
        StateHasChanged();
    }

    /// <summary>
    /// Returns true if any speech feature is currently active (STT or TTS enabled)
    /// </summary>
    private bool HasActiveSpeechFeature => IsSpeechToTextEnabled || IsTextToSpeechEnabled;

    private string GetThemeClass()
    {
        return $"theme-{Settings.Theme.PrimaryColor.Replace("#", "").ToLower()}";
    }

    private string GetMessageClass(AIMessage message)
    {
        var classes = new List<string> { "ai-chat-message" };

        if (message.Role == "user")
            classes.Add("ai-chat-message-user");
        else if (message.Role == "assistant")
            classes.Add("ai-chat-message-assistant");
        else
            classes.Add("ai-chat-message-system");

        if (message.IsError)
            classes.Add("ai-chat-message-error");

        return string.Join(" ", classes);
    }

    private string GetDisplayRole(string role) => role switch
    {
        "user" => "You",
        "assistant" => "Assistant",
        "system" => "System",
        _ => role
    };

    private string FormatMessageContent(string content)
    {
        content = System.Web.HttpUtility.HtmlEncode(content);
        content = content.Replace("\n", "<br>");
        content = System.Text.RegularExpressions.Regex.Replace(content, @"\*\*(.*?)\*\*", "<strong>$1</strong>");
        content = System.Text.RegularExpressions.Regex.Replace(content, @"\*(.*?)\*", "<em>$1</em>");
        content = System.Text.RegularExpressions.Regex.Replace(content, @"`(.*?)`", "<code>$1</code>");
        return content;
    }

    /// <summary>
    /// Formats the tooltip text for a session item showing name and date/time.
    /// </summary>
    private string FormatSessionTooltip(AISessionSummary session)
    {
        var tooltip = session.Name;

        // Add creation date/time
        var createdLocal = session.CreatedAt.ToLocalTime();
        tooltip += $"\nCreated: {createdLocal:g}";

        // Add last accessed time if different from creation
        if (session.LastAccessedAt != session.CreatedAt)
        {
            var lastAccessedLocal = session.LastAccessedAt.ToLocalTime();
            tooltip += $"\nLast accessed: {lastAccessedLocal:g}";
        }

        // Add message count
        if (session.MessageCount > 0)
        {
            tooltip += $"\nMessages: {session.MessageCount}";
        }

        return tooltip;
    }

    private List<AIMessage> GetVisibleMessages()
    {
        var visibleMessages = new List<AIMessage>();
        foreach (var message in Messages)
        {
            if (message.Role != "system")
            {
                visibleMessages.Add(message);
            }
        }
        return visibleMessages;
    }

    private int GetVisibleMessagesCount()
    {
        var count = 0;
        foreach (var message in Messages)
        {
            if (message.Role != "system")
            {
                count++;
            }
        }
        return count;
    }

    #endregion

    #region Collection Helper Methods

    private AIConfiguration? FindConfigurationById(string configId)
    {
        foreach (var config in Configurations)
        {
            if (config.Id == configId)
            {
                return config;
            }
        }
        return null;
    }

    private AIConfiguration? GetFirstConfiguration()
    {
        if (Configurations.Count == 0) return null;

        foreach (var config in Configurations)
        {
            if (config.IsActive) return config;
        }

        return Configurations[0];
    }

    private bool HasAnyConfigurations() => Configurations.Count > 0;

    private bool HasAnySessions() => Sessions.Count > 0;

    private AISessionSummary? GetMostRecentSession()
    {
        if (Sessions.Count == 0) return null;

        AISessionSummary? mostRecent = null;
        foreach (var session in Sessions)
        {
            if (mostRecent == null || session.LastAccessedAt > mostRecent.LastAccessedAt)
            {
                mostRecent = session;
            }
        }
        return mostRecent;
    }

    private List<AIMessage> TakeLastMessages(List<AIMessage> messages, int count)
    {
        if (messages.Count <= count) return new List<AIMessage>(messages);

        var result = new List<AIMessage>();
        var startIndex = messages.Count - count;
        for (int i = startIndex; i < messages.Count; i++)
        {
            result.Add(messages[i]);
        }
        return result;
    }

    private List<AIMessage> ConvertToMessageList(IEnumerable<AIMessage> messages)
    {
        var result = new List<AIMessage>();
        foreach (var message in messages)
        {
            result.Add(message);
        }
        return result;
    }

    /// <summary>
    /// Checks if a session name is a default auto-generated name (e.g., "Chat 2024-01-15 10:30").
    /// Default names should trigger auto-renaming on first user message.
    /// </summary>
    private bool IsDefaultSessionName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        // Check for the default "Chat YYYY-MM-DD HH:MM" pattern
        // The server creates sessions with this pattern when no custom name is provided
        if (name.StartsWith("Chat ") && name.Length >= 21)
        {
            // Try to match the pattern: "Chat 2024-01-15 10:30"
            var datePart = name.Substring(5); // Remove "Chat "
            if (datePart.Length >= 16)
            {
                // Check for date-time pattern: YYYY-MM-DD HH:MM
                if (datePart[4] == '-' && datePart[7] == '-' && datePart[10] == ' ' && datePart[13] == ':')
                {
                    return true;
                }
            }
        }

        // Also check for "Untitled Chat" which might be used as a fallback
        if (name == "Untitled Chat")
        {
            return true;
        }

        return false;
    }

    private string GenerateSessionNameFromMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return $"Chat {DateTime.UtcNow:yyyy-MM-dd HH:mm}";
        }

        var cleanMessage = message.Trim().Replace("\n", " ").Replace("\r", "");
        while (cleanMessage.Contains("  "))
        {
            cleanMessage = cleanMessage.Replace("  ", " ");
        }

        const int maxLength = 50;
        if (cleanMessage.Length <= maxLength) return cleanMessage;

        var truncated = cleanMessage.Substring(0, maxLength);
        var lastSpace = truncated.LastIndexOf(' ');

        if (lastSpace > 20)
        {
            truncated = truncated.Substring(0, lastSpace);
        }

        return truncated + "...";
    }

    #endregion
}
