using WildwoodComponents.Shared.Models;

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
    public List<MessageThread> Threads { get; set; } = new();
    public List<CompanyAppUser> Users { get; set; } = new();
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

// DTOs (MessageThreadDto, SecureMessageDto, ThreadParticipantDto, MessageAttachmentDto,
// MessageReactionDto, CompanyAppUserDto, TypingIndicatorDto, MessageSearchResultDto,
// OnlineStatusDto) have been consolidated into WildwoodComponents.Shared.Models as
// MessageThread, SecureMessage, ThreadParticipant, MessageAttachment, MessageReaction,
// CompanyAppUser, TypingIndicator, MessageSearchResult, OnlineStatus.
