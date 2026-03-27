using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public interface IWildwoodMessagingService
{
    Task<List<MessageThread>> GetThreadsAsync(string companyAppId);
    Task<MessageThread?> GetThreadAsync(string threadId);
    Task<MessageThread?> CreateThreadAsync(string companyAppId, string subject, List<string> participantIds, string threadType = "Direct");
    Task<List<SecureMessage>> GetMessagesAsync(string threadId, int page = 1, int pageSize = 50);
    Task<SecureMessage?> SendMessageAsync(string threadId, string content, string messageType = "Text", string? replyToMessageId = null);
    Task<SecureMessage?> EditMessageAsync(string messageId, string newContent);
    Task<bool> DeleteMessageAsync(string messageId);
    Task<bool> ReactToMessageAsync(string messageId, string emoji);
    Task<bool> RemoveReactionAsync(string messageId, string emoji);
    Task<bool> MarkThreadAsReadAsync(string threadId);
    Task<List<CompanyAppUser>> GetUsersAsync(string companyAppId);
    Task<List<CompanyAppUser>> SearchUsersAsync(string companyAppId, string searchTerm);

    // Per-message read tracking
    Task<bool> MarkMessageAsReadAsync(string messageId);

    // Typing indicators
    Task<bool> StartTypingAsync(string threadId);
    Task<bool> StopTypingAsync(string threadId);
    Task<List<TypingIndicator>> GetTypingIndicatorsAsync(string threadId);

    // Attachments
    Task<string> UploadAttachmentAsync(string threadId, byte[] fileData, string fileName, string contentType);
    Task<byte[]> DownloadAttachmentAsync(string attachmentId);

    // Message search
    Task<List<MessageSearchResult>> SearchMessagesAsync(string companyAppId, string searchTerm, string? threadId = null);

    // Online status
    Task<bool> UpdateOnlineStatusAsync(string companyAppId, string status, string? statusMessage = null);
    Task<List<OnlineStatus>> GetOnlineStatusesAsync(string companyAppId);
}
