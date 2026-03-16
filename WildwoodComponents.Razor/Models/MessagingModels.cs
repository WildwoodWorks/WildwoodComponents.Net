namespace WildwoodComponents.Razor.Models;

public class SecureMessagingViewModel
{
    public string CompanyAppId { get; set; } = string.Empty;
    public string ProxyBaseUrl { get; set; } = "/api/wildwood-messaging";
    public string? Title { get; set; }
    public bool ShowUserSearch { get; set; } = true;
    public bool ShowThreadList { get; set; } = true;
    public bool EnableReactions { get; set; } = true;
    public bool EnableAttachments { get; set; } = true;
    public bool EnableTypingIndicators { get; set; } = true;
    public List<MessageThreadDto> Threads { get; set; } = new();
    public List<CompanyAppUserDto> Users { get; set; } = new();
    public string ComponentId { get; set; } = Guid.NewGuid().ToString("N")[..8];

    public static string FormatTime(DateTime dateTime)
    {
        var now = DateTime.UtcNow;
        var diff = now - dateTime;

        if (diff.TotalMinutes < 1) return "Now";
        if (diff.TotalHours < 1) return $"{(int)diff.TotalMinutes}m";
        if (diff.TotalDays < 1) return dateTime.ToString("h:mm tt");
        if (diff.TotalDays < 7) return dateTime.ToString("ddd");
        return dateTime.ToString("MMM dd");
    }
}

public class MessageThreadDto
{
    public string Id { get; set; } = string.Empty;
    public string? CompanyAppId { get; set; }
    public string? Subject { get; set; }
    public string ThreadType { get; set; } = "Direct";
    public DateTime CreatedAt { get; set; }
    public DateTime? LastActivity { get; set; }
    public bool IsActive { get; set; }
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
    public List<ThreadParticipantDto> Participants { get; set; } = new();
}

public class ThreadParticipantDto
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? Avatar { get; set; }
    public bool IsOnline { get; set; }
}

public class SecureMessageDto
{
    public string Id { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string? SenderName { get; set; }
    public string? SenderAvatar { get; set; }
    public string Content { get; set; } = string.Empty;
    public string MessageType { get; set; } = "Text";
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public string? ReplyToMessageId { get; set; }
    public List<MessageAttachmentDto> Attachments { get; set; } = new();
    public List<MessageReactionDto> Reactions { get; set; } = new();
}

public class MessageAttachmentDto
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? ContentType { get; set; }
    public long FileSize { get; set; }
    public string? ThumbnailUrl { get; set; }
}

public class MessageReactionDto
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string Emoji { get; set; } = string.Empty;
}

public class CompanyAppUserDto
{
    public string Id { get; set; } = string.Empty;
    public string? CompanyAppId { get; set; }
    public string? UserId { get; set; }
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? Avatar { get; set; }
    public bool IsOnline { get; set; }
    public DateTime? LastSeen { get; set; }
}

public class TypingIndicatorDto
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public DateTime StartedAt { get; set; }
}

public class MessageSearchResultDto
{
    public string MessageId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string? ThreadSubject { get; set; }
    public string? SenderName { get; set; }
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string? Highlight { get; set; }
}

public class OnlineStatusDto
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string Status { get; set; } = "Offline";
    public string? StatusMessage { get; set; }
    public DateTime? LastSeen { get; set; }
}
