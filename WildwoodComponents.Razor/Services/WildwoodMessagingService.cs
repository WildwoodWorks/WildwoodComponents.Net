using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

public class WildwoodMessagingService : IWildwoodMessagingService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodMessagingService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public WildwoodMessagingService(HttpClient httpClient, IWildwoodSessionManager sessionManager, ILogger<WildwoodMessagingService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<List<MessageThread>> GetThreadsAsync(string companyAppId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/messaging/threads?companyAppId={companyAppId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<MessageThread>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get threads"); }
        return new();
    }

    public async Task<MessageThread?> GetThreadAsync(string threadId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/messaging/threads/{threadId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MessageThread>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get thread {ThreadId}", threadId); }
        return null;
    }

    public async Task<MessageThread?> CreateThreadAsync(string companyAppId, string subject, List<string> participantIds, string threadType = "Direct")
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { CompanyAppId = companyAppId, Subject = subject, ParticipantIds = participantIds, ThreadType = threadType };
            using var response = await _httpClient.PostAsJsonAsync("api/messaging/threads", payload);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MessageThread>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to create thread"); }
        return null;
    }

    public async Task<List<SecureMessage>> GetMessagesAsync(string threadId, int page = 1, int pageSize = 50)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/messaging/threads/{threadId}/messages?page={page}&pageSize={pageSize}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<SecureMessage>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get messages for thread {ThreadId}", threadId); }
        return new();
    }

    public async Task<SecureMessage?> SendMessageAsync(string threadId, string content, string messageType = "Text", string? replyToMessageId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { ThreadId = threadId, Content = content, MessageType = messageType, ReplyToMessageId = replyToMessageId };
            using var response = await _httpClient.PostAsJsonAsync("api/messaging/messages", payload);
            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SecureMessage>(responseContent, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to send message"); }
        return null;
    }

    public async Task<SecureMessage?> EditMessageAsync(string messageId, string newContent)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { Content = newContent };
            using var response = await _httpClient.PutAsJsonAsync($"api/messaging/messages/{messageId}", payload);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SecureMessage>(content, JsonOptions);
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to edit message {MessageId}", messageId); }
        return null;
    }

    public async Task<bool> DeleteMessageAsync(string messageId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync($"api/messaging/messages/{messageId}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to delete message {MessageId}", messageId); return false; }
    }

    public async Task<bool> ReactToMessageAsync(string messageId, string emoji)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { Emoji = emoji };
            using var response = await _httpClient.PostAsJsonAsync($"api/messaging/messages/{messageId}/reactions", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to react to message"); return false; }
    }

    public async Task<bool> RemoveReactionAsync(string messageId, string emoji)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync($"api/messaging/messages/{messageId}/reactions/{Uri.EscapeDataString(emoji)}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to remove reaction"); return false; }
    }

    public async Task<bool> MarkThreadAsReadAsync(string threadId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/messaging/threads/{threadId}/read", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to mark thread as read"); return false; }
    }

    public async Task<List<CompanyAppUser>> GetUsersAsync(string companyAppId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/messaging/users?companyAppId={companyAppId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<CompanyAppUser>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get users"); }
        return new();
    }

    public async Task<List<CompanyAppUser>> SearchUsersAsync(string companyAppId, string searchTerm)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/messaging/users/search?companyAppId={companyAppId}&q={Uri.EscapeDataString(searchTerm)}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<CompanyAppUser>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to search users"); }
        return new();
    }

    public async Task<bool> MarkMessageAsReadAsync(string messageId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/messaging/messages/{messageId}/read", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to mark message {MessageId} as read", messageId); return false; }
    }

    public async Task<bool> StartTypingAsync(string threadId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/messaging/threads/{threadId}/typing/start", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to start typing for thread {ThreadId}", threadId); return false; }
    }

    public async Task<bool> StopTypingAsync(string threadId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.PostAsync($"api/messaging/threads/{threadId}/typing/stop", null);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to stop typing for thread {ThreadId}", threadId); return false; }
    }

    public async Task<List<TypingIndicator>> GetTypingIndicatorsAsync(string threadId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/messaging/threads/{threadId}/typing");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<TypingIndicator>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get typing indicators for thread {ThreadId}", threadId); }
        return new();
    }

    public async Task<string> UploadAttachmentAsync(string threadId, byte[] fileData, string fileName, string contentType)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var content = new MultipartFormDataContent();
            content.Add(new StringContent(threadId), "threadId");

            var fileContent = new ByteArrayContent(fileData);
            fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
            content.Add(fileContent, "file", fileName);

            using var response = await _httpClient.PostAsync("api/messaging/attachments", content);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseJson, JsonOptions);
            return result?["attachmentId"] ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload attachment for thread {ThreadId}", threadId);
            throw;
        }
    }

    public async Task<byte[]> DownloadAttachmentAsync(string attachmentId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/messaging/attachments/{attachmentId}");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download attachment {AttachmentId}", attachmentId);
            throw;
        }
    }

    public async Task<List<MessageSearchResult>> SearchMessagesAsync(string companyAppId, string searchTerm, string? threadId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var url = $"api/messaging/search?companyAppId={companyAppId}&q={Uri.EscapeDataString(searchTerm)}";
            if (!string.IsNullOrEmpty(threadId))
                url += $"&threadId={threadId}";

            using var response = await _httpClient.GetAsync(url);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<MessageSearchResult>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to search messages in app {CompanyAppId}", companyAppId); }
        return new();
    }

    public async Task<bool> UpdateOnlineStatusAsync(string companyAppId, string status, string? statusMessage = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            var payload = new { CompanyAppId = companyAppId, Status = status, StatusMessage = statusMessage };
            using var response = await _httpClient.PostAsJsonAsync("api/messaging/status", payload);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to update online status for app {CompanyAppId}", companyAppId); return false; }
    }

    public async Task<List<OnlineStatus>> GetOnlineStatusesAsync(string companyAppId)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.GetAsync($"api/messaging/status?companyAppId={companyAppId}");
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<List<OnlineStatus>>(content, JsonOptions) ?? new();
            }
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to get online statuses for app {CompanyAppId}", companyAppId); }
        return new();
    }
}
