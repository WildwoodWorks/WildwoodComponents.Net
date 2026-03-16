using WildwoodComponents.Razor.Models;

namespace WildwoodComponents.Razor.Services;

public interface IWildwoodMessagingService
{
    Task<List<MessageThreadDto>> GetThreadsAsync(string companyAppId);
    Task<MessageThreadDto?> GetThreadAsync(string threadId);
    Task<MessageThreadDto?> CreateThreadAsync(string companyAppId, string subject, List<string> participantIds, string threadType = "Direct");
    Task<List<SecureMessageDto>> GetMessagesAsync(string threadId, int page = 1, int pageSize = 50);
    Task<SecureMessageDto?> SendMessageAsync(string threadId, string content, string messageType = "Text", string? replyToMessageId = null);
    Task<SecureMessageDto?> EditMessageAsync(string messageId, string newContent);
    Task<bool> DeleteMessageAsync(string messageId);
    Task<bool> ReactToMessageAsync(string messageId, string emoji);
    Task<bool> RemoveReactionAsync(string messageId, string emoji);
    Task<bool> MarkThreadAsReadAsync(string threadId);
    Task<List<CompanyAppUserDto>> GetUsersAsync(string companyAppId);
    Task<List<CompanyAppUserDto>> SearchUsersAsync(string companyAppId, string searchTerm);

    // Per-message read tracking
    Task<bool> MarkMessageAsReadAsync(string messageId);

    // Typing indicators
    Task<bool> StartTypingAsync(string threadId);
    Task<bool> StopTypingAsync(string threadId);
    Task<List<TypingIndicatorDto>> GetTypingIndicatorsAsync(string threadId);

    // Attachments
    Task<string> UploadAttachmentAsync(string threadId, byte[] fileData, string fileName, string contentType);
    Task<byte[]> DownloadAttachmentAsync(string attachmentId);

    // Message search
    Task<List<MessageSearchResultDto>> SearchMessagesAsync(string companyAppId, string searchTerm, string? threadId = null);

    // Online status
    Task<bool> UpdateOnlineStatusAsync(string companyAppId, string status, string? statusMessage = null);
    Task<List<OnlineStatusDto>> GetOnlineStatusesAsync(string companyAppId);
}
