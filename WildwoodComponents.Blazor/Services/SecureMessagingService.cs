using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Blazor.Models;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Blazor.Services
{
    /// <summary>
    /// Interface for Secure Messaging Service operations.
    /// </summary>
    public interface ISecureMessagingService
    {
        Task<List<MessageThread>> GetThreadsAsync(string companyAppId);
        Task<MessageThread?> GetThreadAsync(string threadId);
        Task<MessageThread> CreateThreadAsync(string companyAppId, string subject, List<string> participantIds, ThreadType threadType = ThreadType.Direct);
        Task<List<SecureMessage>> GetMessagesAsync(string threadId, int page = 1, int pageSize = 50);
        Task<SecureMessage> SendMessageAsync(string threadId, string content, MessageType messageType = MessageType.Text, string? replyToMessageId = null);
        Task<SecureMessage> EditMessageAsync(string messageId, string newContent);
        Task<bool> DeleteMessageAsync(string messageId);
        Task<bool> ReactToMessageAsync(string messageId, string emoji);
        Task<bool> RemoveReactionAsync(string messageId, string emoji);
        Task<bool> MarkMessageAsReadAsync(string messageId);
        Task<bool> MarkThreadAsReadAsync(string threadId);
        Task<List<CompanyAppUser>> GetCompanyAppUsersAsync(string companyAppId);
        Task<List<CompanyAppUser>> SearchUsersAsync(string companyAppId, string searchTerm);
        Task<bool> StartTypingAsync(string threadId);
        Task<bool> StopTypingAsync(string threadId);
        Task<List<TypingIndicator>> GetTypingIndicatorsAsync(string threadId);
        Task<string> UploadAttachmentAsync(string threadId, PendingAttachment attachment);
        Task<byte[]> DownloadAttachmentAsync(string attachmentId);
        Task<List<MessageSearchResult>> SearchMessagesAsync(string companyAppId, string searchTerm, string? threadId = null);
        Task<bool> UpdateOnlineStatusAsync(string companyAppId, UserStatus status, string? statusMessage = null);
        Task<List<OnlineStatus>> GetOnlineStatusesAsync(string companyAppId);
        void SetAuthToken(string token);
        void SetApiBaseUrl(string apiBaseUrl);

        /// <summary>Raised when a real-time message is received (requires SignalR integration).</summary>
        event EventHandler<SecureMessage>? OnMessageReceived;
        /// <summary>Raised when a user starts or stops typing (requires SignalR integration).</summary>
        event EventHandler<TypingIndicator>? OnTypingChanged;
        /// <summary>Raised when a user's online status changes (requires SignalR integration).</summary>
        event EventHandler<OnlineStatus>? OnUserStatusChanged;
        /// <summary>Raised when a thread is updated (requires SignalR integration).</summary>
        event EventHandler<string>? OnThreadUpdated;
    }

    /// <summary>
    /// Secure Messaging Service implementation for thread and message management.
    /// Real-time events (OnMessageReceived, OnTypingChanged, OnUserStatusChanged, OnThreadUpdated)
    /// are raised when a SignalR connection pushes updates. Without SignalR, use polling via
    /// GetTypingIndicatorsAsync / GetOnlineStatusesAsync.
    /// </summary>
    public class SecureMessagingService : ISecureMessagingService
    {
        private readonly HttpClient _httpClient;
        private readonly ILocalStorageService _localStorage;
        private readonly ILogger<SecureMessagingService> _logger;
        private string _authToken = string.Empty;
        private string _apiBaseUrl = string.Empty; // Must be configured via SetApiBaseUrl - no hardcoded default
        private readonly Dictionary<string, MessageDraft> _drafts = new();

        /// <inheritdoc/>
        public event EventHandler<SecureMessage>? OnMessageReceived;
        /// <inheritdoc/>
        public event EventHandler<TypingIndicator>? OnTypingChanged;
        /// <inheritdoc/>
        public event EventHandler<OnlineStatus>? OnUserStatusChanged;
        /// <inheritdoc/>
        public event EventHandler<string>? OnThreadUpdated;

        /// <summary>Raises OnMessageReceived for external callers (e.g., SignalR hub).</summary>
        public void RaiseMessageReceived(SecureMessage message) => OnMessageReceived?.Invoke(this, message);
        /// <summary>Raises OnTypingChanged for external callers (e.g., SignalR hub).</summary>
        public void RaiseTypingChanged(TypingIndicator indicator) => OnTypingChanged?.Invoke(this, indicator);
        /// <summary>Raises OnUserStatusChanged for external callers (e.g., SignalR hub).</summary>
        public void RaiseUserStatusChanged(OnlineStatus status) => OnUserStatusChanged?.Invoke(this, status);
        /// <summary>Raises OnThreadUpdated for external callers (e.g., SignalR hub).</summary>
        public void RaiseThreadUpdated(string threadId) => OnThreadUpdated?.Invoke(this, threadId);

        public SecureMessagingService(HttpClient httpClient, ILocalStorageService localStorage, ILogger<SecureMessagingService> logger)
        {
            _httpClient = httpClient;
            _localStorage = localStorage;
            _logger = logger;
        }

        public void SetAuthToken(string token)
        {
            _authToken = token;
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        public void SetApiBaseUrl(string apiBaseUrl)
        {
            _apiBaseUrl = apiBaseUrl ?? string.Empty;
            _logger.LogInformation("?? SecureMessagingService: API base URL set to: {ApiBaseUrl}", _apiBaseUrl);
        }

        public async Task<List<MessageThread>> GetThreadsAsync(string companyAppId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/messaging/threads?companyAppId={companyAppId}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<MessageThread>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<MessageThread>();
                }
                return new List<MessageThread>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching message threads for CompanyApp {CompanyAppId}", companyAppId);
                return new List<MessageThread>();
            }
        }

        public async Task<MessageThread?> GetThreadAsync(string threadId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/messaging/threads/{threadId}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<MessageThread>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                }
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching message thread {ThreadId}", threadId);
                return null;
            }
        }

        public async Task<MessageThread> CreateThreadAsync(string companyAppId, string subject, List<string> participantIds, ThreadType threadType = ThreadType.Direct)
        {
            try
            {
                var request = new
                {
                    CompanyAppId = companyAppId,
                    Subject = subject,
                    ParticipantIds = participantIds,
                    ThreadType = threadType
                };

                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/threads", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<MessageThread>(responseJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new MessageThread();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating message thread");
                throw;
            }
        }

        public async Task<List<SecureMessage>> GetMessagesAsync(string threadId, int page = 1, int pageSize = 50)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/messaging/threads/{threadId}/messages?page={page}&pageSize={pageSize}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<SecureMessage>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<SecureMessage>();
                }
                return new List<SecureMessage>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching messages for thread {ThreadId}", threadId);
                return new List<SecureMessage>();
            }
        }

        public async Task<SecureMessage> SendMessageAsync(string threadId, string content, MessageType messageType = MessageType.Text, string? replyToMessageId = null)
        {
            try
            {
                var request = new
                {
                    ThreadId = threadId,
                    Content = content,
                    MessageType = messageType,
                    ReplyToMessageId = replyToMessageId
                };

                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/messages", requestContent);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var message = JsonSerializer.Deserialize<SecureMessage>(responseJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new SecureMessage();

                // Clear draft after sending
                _drafts.Remove(threadId);

                return message;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending message to thread {ThreadId}", threadId);
                throw;
            }
        }

        public async Task<SecureMessage> EditMessageAsync(string messageId, string newContent)
        {
            try
            {
                var request = new { Content = newContent };
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PutAsync($"{_apiBaseUrl}/messaging/messages/{messageId}", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<SecureMessage>(responseJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new SecureMessage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error editing message {MessageId}", messageId);
                throw;
            }
        }

        public async Task<bool> DeleteMessageAsync(string messageId)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_apiBaseUrl}/messaging/messages/{messageId}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting message {MessageId}", messageId);
                return false;
            }
        }

        public async Task<bool> ReactToMessageAsync(string messageId, string emoji)
        {
            try
            {
                var request = new { Emoji = emoji };
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/messages/{messageId}/reactions", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding reaction to message {MessageId}", messageId);
                return false;
            }
        }

        public async Task<bool> RemoveReactionAsync(string messageId, string emoji)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{_apiBaseUrl}/messaging/messages/{messageId}/reactions/{emoji}");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing reaction from message {MessageId}", messageId);
                return false;
            }
        }

        public async Task<bool> MarkMessageAsReadAsync(string messageId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/messages/{messageId}/read", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking message as read {MessageId}", messageId);
                return false;
            }
        }

        public async Task<bool> MarkThreadAsReadAsync(string threadId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/threads/{threadId}/read", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking thread as read {ThreadId}", threadId);
                return false;
            }
        }

        public async Task<List<CompanyAppUser>> GetCompanyAppUsersAsync(string companyAppId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/messaging/users?companyAppId={companyAppId}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<CompanyAppUser>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<CompanyAppUser>();
                }
                return new List<CompanyAppUser>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching users for CompanyApp {CompanyAppId}", companyAppId);
                return new List<CompanyAppUser>();
            }
        }

        public async Task<List<CompanyAppUser>> SearchUsersAsync(string companyAppId, string searchTerm)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/messaging/users/search?companyAppId={companyAppId}&q={Uri.EscapeDataString(searchTerm)}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<CompanyAppUser>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<CompanyAppUser>();
                }
                return new List<CompanyAppUser>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching users in CompanyApp {CompanyAppId}", companyAppId);
                return new List<CompanyAppUser>();
            }
        }

        public async Task<bool> StartTypingAsync(string threadId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/threads/{threadId}/typing/start", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting typing indicator for thread {ThreadId}", threadId);
                return false;
            }
        }

        public async Task<bool> StopTypingAsync(string threadId)
        {
            try
            {
                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/threads/{threadId}/typing/stop", null);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping typing indicator for thread {ThreadId}", threadId);
                return false;
            }
        }

        public async Task<List<TypingIndicator>> GetTypingIndicatorsAsync(string threadId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/messaging/threads/{threadId}/typing");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<TypingIndicator>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<TypingIndicator>();
                }
                return new List<TypingIndicator>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching typing indicators for thread {ThreadId}", threadId);
                return new List<TypingIndicator>();
            }
        }

        public async Task<string> UploadAttachmentAsync(string threadId, PendingAttachment attachment)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(threadId), "threadId");

                if (attachment.FileData != null)
                {
                    var fileContent = new ByteArrayContent(attachment.FileData);
                    fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(attachment.ContentType);
                    content.Add(fileContent, "file", attachment.FileName);
                }

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/attachments", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<Dictionary<string, string>>(responseJson, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                return result?["attachmentId"] ?? string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error uploading attachment for thread {ThreadId}", threadId);
                throw;
            }
        }

        public async Task<byte[]> DownloadAttachmentAsync(string attachmentId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/messaging/attachments/{attachmentId}");
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading attachment {AttachmentId}", attachmentId);
                throw;
            }
        }

        public async Task<List<MessageSearchResult>> SearchMessagesAsync(string companyAppId, string searchTerm, string? threadId = null)
        {
            try
            {
                var url = $"{_apiBaseUrl}/messaging/search?companyAppId={companyAppId}&q={Uri.EscapeDataString(searchTerm)}";
                if (!string.IsNullOrEmpty(threadId))
                    url += $"&threadId={threadId}";

                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<MessageSearchResult>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<MessageSearchResult>();
                }
                return new List<MessageSearchResult>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching messages in CompanyApp {CompanyAppId}", companyAppId);
                return new List<MessageSearchResult>();
            }
        }

        public async Task<bool> UpdateOnlineStatusAsync(string companyAppId, UserStatus status, string? statusMessage = null)
        {
            try
            {
                var request = new { CompanyAppId = companyAppId, Status = status, StatusMessage = statusMessage };
                var json = JsonSerializer.Serialize(request, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_apiBaseUrl}/messaging/status", content);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating online status for CompanyApp {CompanyAppId}", companyAppId);
                return false;
            }
        }

        public async Task<List<OnlineStatus>> GetOnlineStatusesAsync(string companyAppId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{_apiBaseUrl}/messaging/status?companyAppId={companyAppId}");
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    return JsonSerializer.Deserialize<List<OnlineStatus>>(json, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }) ?? new List<OnlineStatus>();
                }
                return new List<OnlineStatus>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching online statuses for CompanyApp {CompanyAppId}", companyAppId);
                return new List<OnlineStatus>();
            }
        }

        // Draft management methods
        public async Task SaveDraftAsync(string threadId, string content, string? replyToMessageId = null)
        {
            try
            {
                var draft = new MessageDraft
                {
                    ThreadId = threadId,
                    Content = content,
                    ReplyToMessageId = replyToMessageId,
                    LastModified = DateTime.UtcNow
                };

                _drafts[threadId] = draft;
                await _localStorage.SetItemAsync($"draft_{threadId}", draft);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving draft for thread {ThreadId}", threadId);
            }
        }

        public async Task<MessageDraft?> GetDraftAsync(string threadId)
        {
            try
            {
                if (_drafts.TryGetValue(threadId, out var draft))
                    return draft;

                return await _localStorage.GetItemAsync<MessageDraft>($"draft_{threadId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting draft for thread {ThreadId}", threadId);
                return null;
            }
        }

        public async Task ClearDraftAsync(string threadId)
        {
            try
            {
                _drafts.Remove(threadId);
                await _localStorage.RemoveItemAsync($"draft_{threadId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing draft for thread {ThreadId}", threadId);
            }
        }
    }
}
