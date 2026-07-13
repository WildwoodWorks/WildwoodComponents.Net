using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WildwoodComponents.Shared.Models;

namespace WildwoodComponents.Razor.Services;

/// <summary>
/// Tenant document service implementation that calls the WildwoodAPI api/documents endpoints using
/// the named "WildwoodAPI" HttpClient (whose base address already ends in <c>/api/</c>, so paths are
/// relative). Razor Pages equivalent of WildwoodComponents.Blazor.Services.DocumentService. The
/// Bearer token is applied from the server-side session on every call via ApplyAuthorizationHeader;
/// app scope is passed per call as ?requestedAppId.
/// </summary>
public class WildwoodDocumentService : IWildwoodDocumentService
{
    private readonly HttpClient _httpClient;
    private readonly IWildwoodSessionManager _sessionManager;
    private readonly ILogger<WildwoodDocumentService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public WildwoodDocumentService(
        HttpClient httpClient,
        IWildwoodSessionManager sessionManager,
        ILogger<WildwoodDocumentService> logger)
    {
        _httpClient = httpClient;
        _sessionManager = sessionManager;
        _logger = logger;
    }

    public async Task<List<AppDocumentModel>> ListAsync(string? appId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var request = GetRequest($"documents{AppQuery(appId)}", acceptJson: true);
            using var response = await _httpClient.SendAsync(request);
            if (!IsAuthorized(response) || !response.IsSuccessStatusCode) return new();
            return await response.Content.ReadFromJsonAsync<List<AppDocumentModel>>(JsonOptions) ?? new();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to list documents");
            return new();
        }
    }

    public async Task<AppDocumentModel?> UploadAsync(
        Stream fileStream, string fileName, string? contentType = null, string? appId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);

            // No manual Content-Type: MultipartFormDataContent sets multipart/form-data with the
            // boundary itself. The file goes in as a form field named "file" with its filename.
            using var form = new MultipartFormDataContent();
            var filePart = new StreamContent(fileStream);
            if (!string.IsNullOrEmpty(contentType))
                filePart.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            form.Add(filePart, "file", string.IsNullOrEmpty(fileName) ? "document" : fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, $"documents{AppQuery(appId)}")
            {
                Content = form
            };
            using var response = await _httpClient.SendAsync(request);

            // NOTE: JS DocumentService.upload throws on failure; the .NET stack returns null + logs
            // for consistency with AIFlow/NotificationInbox (callers here don't expect an exception).
            if (!IsAuthorized(response))
            {
                _logger.LogWarning("Document upload denied: {StatusCode}", response.StatusCode);
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Document upload failed: {StatusCode} - {Detail}",
                    response.StatusCode, await ErrorDetailAsync(response));
                return null;
            }
            return await response.Content.ReadFromJsonAsync<AppDocumentModel>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to upload document {FileName}", fileName);
            return null;
        }
    }

    public async Task<AppDocumentModel?> GetAsync(string documentId, string? appId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var request = GetRequest(
                $"documents/{Uri.EscapeDataString(documentId)}{AppQuery(appId)}", acceptJson: true);
            using var response = await _httpClient.SendAsync(request);
            if (!IsAuthorized(response) || !response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<AppDocumentModel>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load document {DocumentId}", documentId);
            return null;
        }
    }

    public async Task<AppDocumentTextResult?> GetTextAsync(string documentId, string? appId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var request = GetRequest(
                $"documents/{Uri.EscapeDataString(documentId)}/text{AppQuery(appId)}", acceptJson: true);
            using var response = await _httpClient.SendAsync(request);
            if (!IsAuthorized(response)) return null;
            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                // 409 = not parsed yet (or parse failed). Map to a text-less, poll-friendly result.
                TextConflictBody? body = null;
                try { body = await response.Content.ReadFromJsonAsync<TextConflictBody>(JsonOptions); }
                catch (Exception ex) { _logger.LogDebug(ex, "Malformed 409 text body for {DocumentId}", documentId); }
                return new AppDocumentTextResult
                {
                    Id = documentId,
                    Status = body?.Status ?? "parsing",
                    Characters = 0,
                    Text = null,
                    Error = body?.Error
                };
            }
            if (!response.IsSuccessStatusCode) return null;
            return await response.Content.ReadFromJsonAsync<AppDocumentTextResult>(JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load document text {DocumentId}", documentId);
            return null;
        }
    }

    public async Task<byte[]?> DownloadAsync(string documentId, string? appId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            // No Accept header on the download path (binary body).
            using var request = GetRequest(
                $"documents/{Uri.EscapeDataString(documentId)}/download{AppQuery(appId)}", acceptJson: false);
            using var response = await _httpClient.SendAsync(request);
            if (!IsAuthorized(response) || !response.IsSuccessStatusCode) return null;
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to download document {DocumentId}", documentId);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string documentId, string? appId = null)
    {
        try
        {
            _sessionManager.ApplyAuthorizationHeader(_httpClient);
            using var response = await _httpClient.DeleteAsync(
                $"documents/{Uri.EscapeDataString(documentId)}{AppQuery(appId)}");
            if (!IsAuthorized(response)) return false;
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to delete document {DocumentId}", documentId);
            return false;
        }
    }

    // ------------------------------------------------------------------

    private static HttpRequestMessage GetRequest(string url, bool acceptJson)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (acceptJson)
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task<string> ErrorDetailAsync(HttpResponseMessage response)
    {
        var fallback = $"Upload failed ({(int)response.StatusCode})";
        try
        {
            var body = await response.Content.ReadFromJsonAsync<TextConflictBody>(JsonOptions);
            return string.IsNullOrEmpty(body?.Error) ? fallback : body!.Error!;
        }
        catch
        {
            return fallback;
        }
    }

    // 401 = authentication failure; 403 = permission/feature denial (e.g. tier lacks DOCUMENTS).
    // Both are legitimate denies — callers degrade to safe empties/null rather than treating them
    // as transient errors. (Server-side session-expiry signaling is the session manager's concern.)
    private static bool IsAuthorized(HttpResponseMessage response) =>
        response.StatusCode != HttpStatusCode.Unauthorized && response.StatusCode != HttpStatusCode.Forbidden;

    private static string AppQuery(string? appId) =>
        string.IsNullOrEmpty(appId) ? string.Empty : $"?requestedAppId={Uri.EscapeDataString(appId)}";

    /// <summary>Shape of the 409 (not-parsed-yet) and error bodies: <c>{ status?, error? }</c>.</summary>
    private sealed class TextConflictBody
    {
        public string? Status { get; set; }
        public string? Error { get; set; }
    }
}
