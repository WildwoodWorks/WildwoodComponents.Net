namespace WildwoodComponents.Shared.Models;

// ──────────────────────────────────────────────
// Secure Messaging shared DTOs
// Used by both WildwoodComponents.Blazor and WildwoodComponents.Razor
// ──────────────────────────────────────────────

public class SecureMessage
{
    public string Id { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public string? SenderAvatar { get; set; }
    public string Content { get; set; } = string.Empty;
    public MessageType MessageType { get; set; } = MessageType.Text;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsEdited { get; set; }
    public bool IsDeleted { get; set; }
    public string? ReplyToMessageId { get; set; }
    public List<MessageAttachment> Attachments { get; set; } = new();
    public List<MessageReaction> Reactions { get; set; } = new();
    public List<MessageReadReceipt> ReadReceipts { get; set; } = new();
}

public class MessageThread
{
    public string Id { get; set; } = string.Empty;
    public string CompanyAppId { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public ThreadType ThreadType { get; set; } = ThreadType.Direct;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
    public string? LastMessagePreview { get; set; }
    public int UnreadCount { get; set; }
    public List<ThreadParticipant> Participants { get; set; } = new();
    public ThreadSettings Settings { get; set; } = new();
}

public class ThreadParticipant
{
    public string Id { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public ParticipantRole Role { get; set; } = ParticipantRole.Member;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ThreadSettings
{
    public string Id { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public bool AllowFileSharing { get; set; } = true;
    public bool AllowReactions { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool ReadReceiptsEnabled { get; set; } = true;
    public int MaxParticipants { get; set; } = 100;
    public List<string> AllowedFileTypes { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

public class MessageAttachment
{
    public string Id { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string StoragePath { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MessageReaction
{
    public string Id { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class MessageReadReceipt
{
    public string Id { get; set; } = string.Empty;
    public string MessageId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}

public class CompanyAppUser
{
    public string Id { get; set; } = string.Empty;
    public string CompanyAppId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Avatar { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Available;
    public bool IsActive { get; set; } = true;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
}

public class OnlineStatus
{
    public string UserId { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public UserStatus Status { get; set; } = UserStatus.Available;
    public DateTime LastSeen { get; set; } = DateTime.UtcNow;
    public string? StatusMessage { get; set; }
}

public class TypingIndicator
{
    public string UserId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public bool IsVisible { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
}

public class MessageSearchResult
{
    public string MessageId { get; set; } = string.Empty;
    public string ThreadId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string SenderName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string ThreadSubject { get; set; } = string.Empty;
}

public class MessageDraft
{
    public string ThreadId { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string? ReplyToMessageId { get; set; }
    public DateTime LastModified { get; set; } = DateTime.UtcNow;
}

public class PendingAttachment
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public byte[]? FileData { get; set; }
    public bool IsUploading { get; set; }
    public double UploadProgress { get; set; }
}

// Configuration and Settings Classes
public class SecureMessagingSettings
{
    public string CompanyAppId { get; set; } = string.Empty;
    public string ApiBaseUrl { get; set; } = string.Empty;
    public bool EnableEncryption { get; set; } = true;
    public bool EnableFileUploads { get; set; } = true;
    public bool EnableReactions { get; set; } = true;
    public bool EnableTypingIndicators { get; set; } = true;
    public bool EnableReadReceipts { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool AutoScrollToBottom { get; set; } = true;
    public long MaxFileSize { get; set; } = 10485760; // 10MB
    public int MaxMessageLength { get; set; } = 2000;
    public int MessagesPerPage { get; set; } = 50;
    public ComponentTheme Theme { get; set; } = new();
    public NotificationSettings Notifications { get; set; } = new();
}

public class NotificationSettings
{
    public bool DesktopNotifications { get; set; } = true;
    public bool SoundNotifications { get; set; } = true;
    public bool VibrateOnMobile { get; set; } = true;
    public string NotificationSound { get; set; } = "default";
}

// Enums for messaging
public enum MessageType
{
    Text = 1,
    File = 2,
    Image = 3,
    System = 4
}

public enum ThreadType
{
    Direct = 1,
    Group = 2,
    Channel = 3
}

public enum ParticipantRole
{
    Member = 1,
    Admin = 2,
    Owner = 3
}

public enum UserStatus
{
    Available = 1,
    Busy = 2,
    Away = 3,
    DoNotDisturb = 4,
    Offline = 5
}
